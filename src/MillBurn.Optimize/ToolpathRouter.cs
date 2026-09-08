using MillBurn.Core;

namespace MillBurn.Optimize;

/// <summary>
/// Turns a toolpath into an ordering problem and the answer back into a toolpath.
///
/// Kept apart from <see cref="RouteOptimizer"/> so the solver never sees a segment or an arc: the
/// combinatorics can then be tested on coordinates, and the geometry — rotating a closed contour to
/// start somewhere else, reversing an arc's sweep — is tested on geometry.
/// </summary>
public static class ToolpathRouter
{
    /// <summary>
    /// Orders one toolpath's passes and drills, starting from <paramref name="from"/>.
    ///
    /// Drills come before passes in the same toolpath, but that ordering is the caller's business:
    /// within a single operation everything is one group, because a drilling operation contains
    /// only holes and an isolation operation only contours.
    /// </summary>
    public static (Toolpath Ordered, RoutePlan Plan) Order(
        Toolpath toolpath,
        Point2 from,
        MachineProfile? machine = null,
        RouteEffort effort = RouteEffort.Balanced,
        Point2? returnTo = null)
    {
        ArgumentNullException.ThrowIfNull(toolpath);

        // Passes that share a stack are one thing to route: a contour's depth passes stay together
        // and in order, because the tool is already standing over the place it is about to cut
        // deeper. Splitting them apart is what makes a panel take five traversals instead of one.
        var stacks = Stacks(toolpath.Passes);
        var nodes = new List<RouteNode>(stacks.Count + toolpath.Drills.Count);

        // References index a single combined list, so one solve covers passes and holes together
        // and the answer says which is which by where the index falls.
        for (var i = 0; i < stacks.Count; i++)
        {
            nodes.Add(NodeFor(i, toolpath.Passes, stacks[i]));
        }

        for (var i = 0; i < toolpath.Drills.Count; i++)
        {
            nodes.Add(RouteNode.ForPoint(stacks.Count + i, toolpath.Drills[i].At));
        }

        var plan = RouteOptimizer.Solve(nodes, from, machine, effort, returnTo);

        var passes = new List<ToolpathPass>(toolpath.Passes.Count);
        var drills = new List<DrillTarget>(toolpath.Drills.Count);

        foreach (var step in plan.Steps)
        {
            if (step.Reference < stacks.Count)
            {
                foreach (var index in stacks[step.Reference])
                {
                    passes.Add(Materialise(toolpath.Passes[index], step));
                }
            }
            else
            {
                drills.Add(toolpath.Drills[step.Reference - stacks.Count]);
            }
        }

        return (toolpath with { Passes = passes, Drills = drills }, plan);
    }

    /// <summary>
    /// Groups passes into the units that must be cut together, keeping their given order.
    ///
    /// A negative stack id means the pass stands alone, which is every isolation contour and every
    /// untabbed single-depth profile.
    /// </summary>
    private static List<List<int>> Stacks(IReadOnlyList<ToolpathPass> passes)
    {
        var stacks = new List<List<int>>();
        var byId = new Dictionary<int, int>();

        for (var i = 0; i < passes.Count; i++)
        {
            var id = passes[i].Stack;

            if (id < 0)
            {
                stacks.Add([i]);
                continue;
            }

            if (!byId.TryGetValue(id, out var at))
            {
                at = stacks.Count;
                byId[id] = at;
                stacks.Add([]);
            }

            stacks[at].Add(i);
        }

        return stacks;
    }

    /// <summary>
    /// The node for one stack.
    ///
    /// A stack of one closed contour keeps every entry vertex as a choice. A stack of several — a
    /// tabbed profile, cut in runs and at several depths — is fixed: its passes have to run in the
    /// order and direction they were built in, so the only question left is where it goes.
    /// </summary>
    private static RouteNode NodeFor(int reference, IReadOnlyList<ToolpathPass> passes, List<int> stack)
    {
        var first = passes[stack[0]];
        var last = passes[stack[^1]];
        var group = first.Group;

        if (stack.Count == 1)
        {
            return NodeFor(reference, first, group);
        }

        // Every pass in a closed stack is the same contour at a different depth, so rotating them
        // all to the same vertex is still valid and keeps the nearest-entry saving.
        if (passes.All(p => p.Closed) && stack.All(i => SameContour(first, passes[i])))
        {
            return RouteNode.ForClosed(reference, [.. first.Path.Select(s => s.From)], group);
        }

        return RouteNode.ForFixed(reference, first.Start, last.End, group);
    }

    private static bool SameContour(ToolpathPass a, ToolpathPass b) =>
        a.Path.Count == b.Path.Count && a.Start == b.Start && a.End == b.End;

    private static RouteNode NodeFor(int reference, ToolpathPass pass, int group)
    {
        if (pass.Path.Count == 0)
        {
            return RouteNode.ForPoint(reference, Point2.Origin, group);
        }

        return pass.Closed
            ? RouteNode.ForClosed(reference, [.. pass.Path.Select(s => s.From)], group)
            : RouteNode.ForOpen(reference, pass.Start, pass.End, group);
    }

    /// <summary>Applies the chosen configuration to the geometry.</summary>
    private static ToolpathPass Materialise(ToolpathPass pass, RouteStep step)
    {
        if (pass.Path.Count == 0)
        {
            return pass;
        }

        if (!pass.Closed)
        {
            // A fixed stack has one option and must not be reversed.
            return step.Option == 0 ? pass : pass with { Path = Reverse(pass.Path) };
        }

        // The solver worked from a sample of the contour's vertices; the exact nearest one is
        // recovered here in a single pass, so the sample costs nothing in the final answer.
        var start = NearestVertex(pass.Path, step.Entry);
        return start == 0 ? pass : pass with { Path = RotateTo(pass.Path, start) };
    }

    private static int NearestVertex(IReadOnlyList<ArtSegment> path, Point2 to)
    {
        var best = 0;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < path.Count; i++)
        {
            var distance = path[i].From.DistanceTo(to);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Re-cuts a closed contour so it begins at a different vertex.
    ///
    /// The segments are a cycle, so this is a rotation and nothing else: no geometry is added,
    /// removed or moved, and the tool covers exactly the same material in the same direction.
    /// </summary>
    private static IReadOnlyList<ArtSegment> RotateTo(IReadOnlyList<ArtSegment> path, int start) =>
        [.. path.Skip(start), .. path.Take(start)];

    /// <summary>
    /// Reverses an open run.
    ///
    /// An arc has to have its sweep flipped as well as its endpoints swapped. Reversing the points
    /// and leaving the direction alone turns a short arc into the long way round the same circle,
    /// which is a real cut straight through whatever the arc was going around.
    /// </summary>
    public static IReadOnlyList<ArtSegment> Reverse(IReadOnlyList<ArtSegment> path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var reversed = new List<ArtSegment>(path.Count);

        for (var i = path.Count - 1; i >= 0; i--)
        {
            var s = path[i];
            reversed.Add(new ArtSegment(
                s.Sweep switch
                {
                    ArtSweep.Clockwise => ArtSweep.CounterClockwise,
                    ArtSweep.CounterClockwise => ArtSweep.Clockwise,
                    _ => ArtSweep.Linear,
                },
                s.To,
                s.From,
                s.Centre));
        }

        return reversed;
    }
}
