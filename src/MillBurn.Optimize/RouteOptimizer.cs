using System.Diagnostics;
using MillBurn.Core;

namespace MillBurn.Optimize;

/// <summary>
/// Orders toolpaths to minimise the time spent not cutting.
///
/// This is the answer to *"edge cuts all over the place causing tons of excessive travel"*. Four
/// things are done differently from pcb2gcode's solver, and each is a defect rather than a
/// simplification on its part (Documentation/03, section 1):
///
/// <list type="number">
/// <item>Both ends of every candidate are considered from the start, so a path whose far end is
/// next to the tool is not scored by how distant its near end is.</item>
/// <item>A closed contour is entered at whichever of its vertices is nearest, rather than at
/// whichever happened to be first in the file.</item>
/// <item>The cost is <em>time</em> on the machine's own motion profile, not Chebyshev distance.
/// Short moves never reach full speed, so they cost far more than their length suggests, and an
/// optimizer scored on distance makes choices a time-scored one would not.</item>
/// <item>Local search is Or-opt as well as 2-opt. Or-opt is the move that relocates one stray
/// contour into the middle of the route, which is exactly the reported symptom, and segment
/// reversal alone cannot do it.</item>
/// </list>
///
/// Determinism is a hard requirement: same input, same budget, same answer. An optimizer whose
/// output moves between runs cannot be diffed, cannot be regression-tested, and cannot be trusted
/// by someone comparing two exports.
/// </summary>
public static class RouteOptimizer
{
    /// <summary>How many neighbours each node considers. Ten is the usual sweet spot.</summary>
    public const int CandidateCount = 10;

    /// <summary>
    /// How much local search one group of nodes is allowed, counted in moves examined.
    ///
    /// Counted rather than timed, because
    /// [06 §2](../../Documentation/06-Roadmap-and-Risks.md#2-cross-cutting-acceptance-criteria)
    /// requires the same input to produce byte-identical output on every machine, and a wall clock
    /// does not. It used to be 500 ms, and that held only while every search converged well inside
    /// it: the first job that did not — a panel's fifty routed channels — came out with a different
    /// route, and a different travel distance, on every run of the same build.
    ///
    /// A search that is working converges in a couple of passes over its nodes; the panel's copper,
    /// 594 nodes, settles in 690 moves. The budget is three orders of magnitude above that because
    /// it is a backstop for a search that is not working, not a target.
    /// </summary>
    public static long BudgetFor(RouteEffort effort, int nodes) => effort switch
    {
        RouteEffort.Fast => 0,
        RouteEffort.Thorough => 20_000L * nodes,
        _ => 1_000L * nodes,
    };

    /// <summary>
    /// Orders the nodes, starting from <paramref name="from"/>.
    ///
    /// <paramref name="returnTo"/> is where the program parks when it finishes. Give it whenever
    /// the emitter really does go back somewhere, because that last hop is part of the job: on a
    /// small board it can be nearly half the total rapid, and an optimizer blind to it will happily
    /// finish in the far corner.
    /// </summary>
    public static RoutePlan Solve(
        IReadOnlyList<RouteNode> nodes,
        Point2 from,
        MachineProfile? machine = null,
        RouteEffort effort = RouteEffort.Balanced,
        Point2? returnTo = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        machine ??= new MachineProfile();

        if (nodes.Count == 0)
        {
            return new RoutePlan
            {
                Steps = [],
                InitialTravelMm = 0,
                TravelMm = 0,
                InitialSeconds = 0,
                Seconds = 0,
            };
        }

        var watch = Stopwatch.StartNew();

        // Groups are a hard partition, so each is solved on its own and the results concatenated.
        // A move that could cross a boundary would be able to trade away a precedence constraint
        // for a shorter route, and precedence here is physics.
        var groups = nodes
            .Select((n, i) => (Node: n, Index: i))
            .GroupBy(x => x.Node.Group)
            .OrderBy(g => g.Key)
            .ToList();

        var steps = new List<RouteStep>(nodes.Count);
        var at = from;
        var initialTravel = 0.0;
        var initialSeconds = 0.0;
        var improvements = 0;

        foreach (var group in groups)
        {
            var members = group.Select(x => x.Node).ToList();
            // Only the final group pays for the trip home; the others hand over to the next one.
            var isLast = ReferenceEquals(group, groups[^1]);
            var solver = new Solver(members, at, machine, isLast ? returnTo : null);

            solver.Construct();
            initialTravel += solver.TravelMm();
            initialSeconds += solver.Seconds();

            if (effort != RouteEffort.Fast)
            {
                improvements += solver.Improve(BudgetFor(effort, members.Count));
            }

            steps.AddRange(solver.Steps());
            at = solver.Finish;
        }

        var travel = 0.0;
        var seconds = 0.0;
        var cursor = from;

        foreach (var step in steps)
        {
            travel += cursor.DistanceTo(step.Entry) / Nm.PerMillimetre;
            seconds += MotionPlanner.RapidSeconds(
                cursor.DistanceTo(step.Entry) / Nm.PerMillimetre, machine);
            cursor = step.Exit;
        }

        if (returnTo is { } home)
        {
            travel += cursor.DistanceTo(home) / Nm.PerMillimetre;
            seconds += MotionPlanner.RapidSeconds(cursor.DistanceTo(home) / Nm.PerMillimetre, machine);
        }

        return new RoutePlan
        {
            Steps = steps,
            InitialTravelMm = initialTravel,
            TravelMm = travel,
            InitialSeconds = initialSeconds,
            Seconds = seconds,
            Improvements = improvements,
            Elapsed = watch.Elapsed,
        };
    }

    /// <summary>
    /// One group's worth of ordering. Holds the mutable route so the moves can be delta-evaluated.
    /// </summary>
    private sealed class Solver
    {
        private readonly List<RouteNode> _nodes;
        private readonly MachineProfile _machine;
        private readonly Point2 _from;
        private readonly Point2? _returnTo;
        private readonly int[] _order;
        private readonly int[] _choice;
        private readonly bool[] _lookAgain;
        private readonly List<int>[] _candidates;

        public Solver(List<RouteNode> nodes, Point2 from, MachineProfile machine, Point2? returnTo)
        {
            _nodes = nodes;
            _from = from;
            _returnTo = returnTo;
            _machine = machine;
            _order = new int[nodes.Count];
            _choice = new int[nodes.Count];
            _lookAgain = new bool[nodes.Count];
            _candidates = BuildCandidates();
        }

        public Point2 Finish { get; private set; }

        /// <summary>
        /// Cost of moving between two points: the time a rapid takes, starting and ending at rest.
        ///
        /// The lift and the plunge either side are deliberately not included. They are the same for
        /// every transition and there is one per node however the nodes are ordered, so including
        /// them would add a constant to every candidate route and change no decision — while making
        /// the reported saving look smaller than it is.
        /// </summary>
        private double Cost(Point2 a, Point2 b) =>
            MotionPlanner.RapidSeconds(a.DistanceTo(b) / Nm.PerMillimetre, _machine);

        private Point2 EntryAt(int position) => _nodes[_order[position]].EntryFor(_choice[position]);

        private Point2 ExitAt(int position) => _nodes[_order[position]].ExitFor(_choice[position]);

        private Point2 Before(int position) => position == 0 ? _from : ExitAt(position - 1);

        /// <summary>
        /// Where the tool goes after the node at <paramref name="position"/>.
        ///
        /// One accessor rather than a <c>position + 1 &lt; length</c> test repeated in every move,
        /// because the park move is exactly the edge that is easy to forget in one of them — and a
        /// move that mis-evaluates one edge silently makes the route worse while reporting a gain.
        /// </summary>
        private bool After(int position, out Point2 point)
        {
            if (position + 1 < _order.Length)
            {
                point = EntryAt(position + 1);
                return true;
            }

            if (_returnTo is { } home)
            {
                point = home;
                return true;
            }

            point = default;
            return false;
        }

        /// <summary>k nearest neighbours by representative point, for pruning the search.</summary>
        private List<int>[] BuildCandidates()
        {
            var anchors = _nodes.Select(n => n.Start).ToList();
            var grid = new SpatialGrid(anchors);
            var lists = new List<int>[_nodes.Count];

            for (var i = 0; i < _nodes.Count; i++)
            {
                lists[i] = [.. grid.Nearest(anchors[i], CandidateCount, i)];
            }

            return lists;
        }

        /// <summary>
        /// Greedy construction over configurations rather than over paths.
        ///
        /// Considering every way into every remaining node — both ends of an open run, every
        /// sampled vertex of a loop — is what fixes the defect that produces the zig-zag: a path is
        /// never scored by an endpoint the tool was not going to use.
        /// </summary>
        public void Construct()
        {
            var remaining = new bool[_nodes.Count];
            Array.Fill(remaining, true);

            var at = _from;

            for (var position = 0; position < _nodes.Count; position++)
            {
                var bestNode = -1;
                var bestOption = 0;
                var bestCost = double.MaxValue;

                for (var i = 0; i < _nodes.Count; i++)
                {
                    if (!remaining[i])
                    {
                        continue;
                    }

                    var node = _nodes[i];
                    for (var option = 0; option < node.OptionCount; option++)
                    {
                        var cost = Cost(at, node.EntryFor(option));

                        // Index order breaks ties, so an equal-cost choice is always the same one.
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestNode = i;
                            bestOption = option;
                        }
                    }
                }

                remaining[bestNode] = false;
                _order[position] = bestNode;
                _choice[position] = bestOption;
                at = _nodes[bestNode].ExitFor(bestOption);
            }

            Finish = at;
        }

        public double Seconds()
        {
            var total = 0.0;
            var at = _from;

            for (var p = 0; p < _order.Length; p++)
            {
                total += Cost(at, EntryAt(p));
                at = ExitAt(p);
            }

            return _returnTo is { } home ? total + Cost(at, home) : total;
        }

        public double TravelMm()
        {
            var total = 0.0;
            var at = _from;

            for (var p = 0; p < _order.Length; p++)
            {
                total += at.DistanceTo(EntryAt(p)) / Nm.PerMillimetre;
                at = ExitAt(p);
            }

            return _returnTo is { } home ? total + (at.DistanceTo(home) / Nm.PerMillimetre) : total;
        }

        /// <summary>
        /// Local search until the budget runs out or nothing improves.
        ///
        /// Don't-look bits are what keep this affordable: a node is only re-examined once something
        /// next to it has actually moved, so a settled region of the route costs nothing to skip.
        /// </summary>
        public int Improve(long budget)
        {
            if (_order.Length < 3)
            {
                RefreshFinish();
                return 0;
            }

            Array.Fill(_lookAgain, true);

            var improvements = 0;
            var queue = new Queue<int>(Enumerable.Range(0, _order.Length));
            var steps = 0L;

            while (queue.Count > 0)
            {
                if (++steps > budget)
                {
                    break;
                }

                var position = queue.Dequeue();
                if (position >= _order.Length || !_lookAgain[position])
                {
                    continue;
                }

                _lookAgain[position] = false;

                if (TryFlip(position) || TryTwoOpt(position, queue) || TryOrOpt(position, queue))
                {
                    improvements++;
                    Wake(position, queue);
                }
            }

            RefreshFinish();
            return improvements;
        }

        private void Wake(int position, Queue<int> queue)
        {
            for (var d = -2; d <= 2; d++)
            {
                var p = position + d;
                if (p >= 0 && p < _order.Length && !_lookAgain[p])
                {
                    _lookAgain[p] = true;
                    queue.Enqueue(p);
                }
            }
        }

        private void RefreshFinish() =>
            Finish = _order.Length == 0 ? _from : ExitAt(_order.Length - 1);

        /// <summary>
        /// Re-chooses how one node is entered, leaving the order alone.
        ///
        /// For a closed contour this is the move that matters most and it is nearly free: with the
        /// neighbours fixed there is a best entry vertex and it can simply be looked up.
        /// </summary>
        private bool TryFlip(int position)
        {
            var node = _nodes[_order[position]];
            if (node.OptionCount < 2)
            {
                return false;
            }

            var before = Before(position);
            var hasNext = After(position, out var after);

            var current = _choice[position];
            var bestCost = Cost(before, node.EntryFor(current))
                + (hasNext ? Cost(node.ExitFor(current), after) : 0);
            var best = current;

            for (var option = 0; option < node.OptionCount; option++)
            {
                if (option == current)
                {
                    continue;
                }

                var cost = Cost(before, node.EntryFor(option))
                    + (hasNext ? Cost(node.ExitFor(option), after) : 0);

                if (cost < bestCost - Epsilon)
                {
                    bestCost = cost;
                    best = option;
                }
            }

            if (best == current)
            {
                return false;
            }

            _choice[position] = best;
            return true;
        }

        /// <summary>
        /// Reverses a run of the route.
        ///
        /// The interior edges survive reversal at exactly the same cost, because the transition
        /// cost is a function of distance and distance is symmetric — so only the two edges at the
        /// ends have to be evaluated, which is what makes this O(1) rather than O(n).
        /// </summary>
        private bool TryTwoOpt(int i, Queue<int> queue)
        {
            var before = Before(i);
            var entry = EntryAt(i);

            foreach (var candidate in _candidates[_order[i]])
            {
                var j = IndexOf(candidate);
                if (j <= i)
                {
                    continue;
                }

                var hasAfterRun = After(j, out var afterRun);
                var oldCost = Cost(before, entry) + (hasAfterRun ? Cost(ExitAt(j), afterRun) : 0);

                // After reversal the run is entered at what was its far end, in flipped orientation.
                var newHead = _nodes[_order[j]].EntryFor(_nodes[_order[j]].Flip(_choice[j]));
                var newTail = _nodes[_order[i]].ExitFor(_nodes[_order[i]].Flip(_choice[i]));

                var newCost = Cost(before, newHead) + (hasAfterRun ? Cost(newTail, afterRun) : 0);

                if (newCost < oldCost - Epsilon)
                {
                    Reverse(i, j);
                    Wake(j, queue);
                    return true;
                }
            }

            return false;
        }

        private void Reverse(int i, int j)
        {
            while (i < j)
            {
                (_order[i], _order[j]) = (_order[j], _order[i]);
                (_choice[i], _choice[j]) = (_choice[j], _choice[i]);

                _choice[i] = _nodes[_order[i]].Flip(_choice[i]);
                _choice[j] = _nodes[_order[j]].Flip(_choice[j]);

                i++;
                j--;
            }

            if (i == j)
            {
                _choice[i] = _nodes[_order[i]].Flip(_choice[i]);
            }
        }

        /// <summary>
        /// Relocates a short run somewhere else in the route, forwards or reversed.
        ///
        /// This is the move pcb2gcode does not have, and it is the one that fixes the reported
        /// symptom: a single contour stranded in the wrong part of the order cannot be rescued by
        /// reversing a run, only by picking it up and putting it somewhere else.
        /// </summary>
        private bool TryOrOpt(int start, Queue<int> queue)
        {
            for (var length = 1; length <= 3; length++)
            {
                var end = start + length - 1;
                if (end >= _order.Length)
                {
                    break;
                }

                var hasAfterRun = After(end, out var afterRun);

                var removed = Cost(Before(start), EntryAt(start))
                    + (hasAfterRun ? Cost(ExitAt(end), afterRun) : 0);

                var closed = hasAfterRun ? Cost(Before(start), afterRun) : 0;

                var gain = removed - closed;
                if (gain <= Epsilon)
                {
                    continue;
                }

                foreach (var candidate in _candidates[_order[start]])
                {
                    var target = IndexOf(candidate);

                    // Inserting inside the run being moved is meaningless.
                    if (target >= start - 1 && target <= end)
                    {
                        continue;
                    }

                    // The successor of the insertion point, which for the last position is the
                    // park move rather than nothing.
                    var hasAfter = After(target, out var afterTarget)
                        && (target + 1 >= _order.Length || target + 1 < start || target + 1 > end);

                    var oldLink = hasAfter ? Cost(ExitAt(target), afterTarget) : 0;

                    for (var reversed = 0; reversed < 2; reversed++)
                    {
                        var head = reversed == 0
                            ? EntryAt(start)
                            : _nodes[_order[end]].EntryFor(_nodes[_order[end]].Flip(_choice[end]));
                        var tail = reversed == 0
                            ? ExitAt(end)
                            : _nodes[_order[start]].ExitFor(_nodes[_order[start]].Flip(_choice[start]));

                        var added = Cost(ExitAt(target), head)
                            + (hasAfter ? Cost(tail, afterTarget) : 0)
                            - oldLink;

                        if (added < gain - Epsilon)
                        {
                            Relocate(start, end, target, reversed == 1);
                            Wake(Math.Min(start, target), queue);
                            Wake(Math.Max(start, target), queue);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void Relocate(int start, int end, int target, bool reversed)
        {
            var length = end - start + 1;
            var movedOrder = new int[length];
            var movedChoice = new int[length];

            Array.Copy(_order, start, movedOrder, 0, length);
            Array.Copy(_choice, start, movedChoice, 0, length);

            if (reversed)
            {
                Array.Reverse(movedOrder);
                Array.Reverse(movedChoice);
                for (var i = 0; i < length; i++)
                {
                    movedChoice[i] = _nodes[movedOrder[i]].Flip(movedChoice[i]);
                }
            }

            var rest = new List<(int Order, int Choice)>(_order.Length - length);
            for (var i = 0; i < _order.Length; i++)
            {
                if (i < start || i > end)
                {
                    rest.Add((_order[i], _choice[i]));
                }
            }

            // The insertion point is expressed in the original indexing, so it has to be rebased
            // once the run has been lifted out.
            var insertAfter = target < start ? target : target - length;

            var rebuilt = new List<(int Order, int Choice)>(_order.Length);
            rebuilt.AddRange(rest.Take(insertAfter + 1));
            for (var i = 0; i < length; i++)
            {
                rebuilt.Add((movedOrder[i], movedChoice[i]));
            }

            rebuilt.AddRange(rest.Skip(insertAfter + 1));

            for (var i = 0; i < rebuilt.Count; i++)
            {
                _order[i] = rebuilt[i].Order;
                _choice[i] = rebuilt[i].Choice;
            }
        }

        private int IndexOf(int node)
        {
            for (var i = 0; i < _order.Length; i++)
            {
                if (_order[i] == node)
                {
                    return i;
                }
            }

            return -1;
        }

        public IEnumerable<RouteStep> Steps()
        {
            for (var p = 0; p < _order.Length; p++)
            {
                var node = _nodes[_order[p]];
                yield return new RouteStep(
                    node.Reference, _choice[p], node.EntryFor(_choice[p]), node.ExitFor(_choice[p]));
            }
        }

        /// <summary>Below a microsecond, a "gain" is floating-point noise rather than an improvement.</summary>
        private const double Epsilon = 1e-9;
    }
}
