using MillBurn.Core;
using MillBurn.Optimize;

namespace MillBurn.Tests;

/// <summary>
/// The optimizer, judged against a baseline that was not tuned to flatter it.
///
/// The claim being tested is not "it produces a route" but "it produces a better one, always, and
/// the same one twice". An optimizer that is usually better is not usable in a regression gate,
/// and one whose output moves between runs cannot be diffed by whoever is about to cut a board
/// with it.
/// </summary>
public sealed class RouteOptimizerTests
{
    private static readonly MachineProfile Machine = new();

    private static Point2 Mm(double x, double y) =>
        new(Nm.FromMillimetres(x), Nm.FromMillimetres(y));

    private static RouteNode Open(int reference, (double X, double Y) a, (double X, double Y) b) =>
        RouteNode.ForOpen(reference, Mm(a.X, a.Y), Mm(b.X, b.Y));

    /// <summary>Total rapid distance for a route, measured independently of the optimizer.</summary>
    private static double TravelOf(IReadOnlyList<RouteStep> steps, Point2 from)
    {
        var total = 0.0;
        var at = from;

        foreach (var step in steps)
        {
            total += at.DistanceTo(step.Entry) / Nm.PerMillimetre;
            at = step.Exit;
        }

        return total;
    }

    // ------------------------------------------------------------------ it is a valid route

    [Fact]
    public void EveryNodeIsVisitedExactlyOnce()
    {
        var nodes = Enumerable.Range(0, 40)
            .Select(i => Open(i, (i % 7 * 9.0, i / 7 * 11.0), ((i % 7 * 9.0) + 4, i / 7 * 11.0)))
            .ToList();

        var plan = RouteOptimizer.Solve(nodes, Point2.Origin, Machine);

        Assert.Equal(40, plan.Steps.Count);
        Assert.Equal(
            Enumerable.Range(0, 40),
            plan.Steps.Select(s => s.Reference).OrderBy(r => r));
    }

    [Fact]
    public void NothingToDoIsNotAnError()
    {
        var plan = RouteOptimizer.Solve([], Point2.Origin, Machine);

        Assert.Empty(plan.Steps);
        Assert.Equal(0, plan.TravelMm);
        Assert.Equal(0, plan.TravelSavedFraction);
    }

    [Fact]
    public void ASingleNodeIsReturnedUntouched()
    {
        var plan = RouteOptimizer.Solve([Open(7, (10, 10), (20, 10))], Point2.Origin, Machine);

        Assert.Single(plan.Steps);
        Assert.Equal(7, plan.Steps[0].Reference);
    }

    /// <summary>
    /// Same input, same answer. Required for the regression gate and for anyone comparing two
    /// exports of the same board.
    /// </summary>
    [Fact]
    public void TheSameInputGivesTheSameRouteEveryTime()
    {
        var nodes = Scatter(120);

        var first = RouteOptimizer.Solve(nodes, Point2.Origin, Machine, RouteEffort.Thorough);
        var second = RouteOptimizer.Solve(nodes, Point2.Origin, Machine, RouteEffort.Thorough);

        Assert.Equal(
            first.Steps.Select(s => (s.Reference, s.Option)),
            second.Steps.Select(s => (s.Reference, s.Option)));
    }

    /// <summary>
    /// The local search is bounded by work done, not by a clock.
    ///
    /// This is the hole the test above could not see. Two runs in one process on one machine agree
    /// under a time budget too — what does not agree is the same build on a different machine, or
    /// the same machine under different load, and that is exactly what
    /// [06 §2](../../Documentation/06-Roadmap-and-Risks.md#2-cross-cutting-acceptance-criteria)
    /// promises will not happen. A panel with fifty routed channels found it: the search there does
    /// not settle, so the 500 ms cut-off landed in a different place every run and the emitted
    /// travel came out 1709, 1831 or 1926 mm from identical input.
    /// </summary>
    [Fact]
    public void TheBudgetIsCountedInWorkRatherThanTime()
    {
        Assert.Equal(0, RouteOptimizer.BudgetFor(RouteEffort.Fast, 500));
        Assert.Equal(500_000, RouteOptimizer.BudgetFor(RouteEffort.Balanced, 500));

        Assert.True(
            RouteOptimizer.BudgetFor(RouteEffort.Thorough, 500)
            > RouteOptimizer.BudgetFor(RouteEffort.Balanced, 500),
            "thorough must be allowed to look harder than balanced");
    }

    // ------------------------------------------------------------------ it is actually better

    /// <summary>
    /// Local search only ever applies improving moves, so the result cannot be worse than the
    /// construction it started from. If this ever fails, a delta is being computed wrongly.
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(120)]
    [InlineData(400)]
    public void OptimisingNeverMakesTheRouteWorse(int count)
    {
        var nodes = Scatter(count);
        var plan = RouteOptimizer.Solve(nodes, Point2.Origin, Machine, RouteEffort.Thorough);

        Assert.True(
            plan.Seconds <= plan.InitialSeconds + 1e-9,
            $"local search made it worse: {plan.InitialSeconds:F3}s -> {plan.Seconds:F3}s");

        // And the reported travel matches an independent measurement of the route it returned.
        Assert.Equal(TravelOf(plan.Steps, Point2.Origin), plan.TravelMm, 6);
    }

    /// <summary>
    /// The defect that produces the reported zig-zag: pcb2gcode measures only the FRONT endpoint of
    /// each candidate, so a path whose far end is next to the tool is scored by how distant its
    /// near end is.
    ///
    /// These four runs all point the same way. Scored front-first the tool crosses the board and
    /// comes back for every one, about 300 mm of rapid. Free to enter either end it snakes: down
    /// one run, straight up to the next, back along that one — 30 mm, and the optimum.
    /// </summary>
    [Fact]
    public void APathIsEnteredFromWhicheverEndIsNearer()
    {
        List<RouteNode> nodes =
        [
            Open(0, (100, 0), (0, 0)),
            Open(1, (100, 10), (0, 10)),
            Open(2, (100, 20), (0, 20)),
            Open(3, (100, 30), (0, 30)),
        ];

        var plan = RouteOptimizer.Solve(nodes, Point2.Origin, Machine);

        // Three 10 mm hops between rows and nothing else: the serpentine, which is optimal here.
        Assert.Equal(30.0, plan.TravelMm, 6);

        // Which is only reachable by entering alternate runs from opposite ends.
        var entryX = plan.Steps.Select(s => s.Entry.X / (double)Nm.PerMillimetre).ToList();
        Assert.Contains(0.0, entryX);
        Assert.Contains(100.0, entryX);
    }

    /// <summary>
    /// A closed contour can be entered at any vertex at no cost. Entering at whichever vertex
    /// happened to be first in the file is the second defect, and on a board of cutouts it throws
    /// away most of the available saving before the solver starts.
    /// </summary>
    [Fact]
    public void AClosedContourIsEnteredAtItsNearestVertex()
    {
        // A square whose first vertex is the far corner.
        List<Point2> square = [Mm(50, 50), Mm(50, 40), Mm(40, 40), Mm(40, 50)];
        var nodes = new List<RouteNode> { RouteNode.ForClosed(0, square) };

        var plan = RouteOptimizer.Solve(nodes, Point2.Origin, Machine);

        Assert.Equal(Mm(40, 40), plan.Steps[0].Entry);

        // Entry and exit coincide: a loop comes back to where it started.
        Assert.Equal(plan.Steps[0].Entry, plan.Steps[0].Exit);
    }

    /// <summary>
    /// The move pcb2gcode does not have, on the case that needs it.
    ///
    /// Nearest-neighbour's characteristic failure is stranding a node it kept almost-choosing: the
    /// one off to the side is never the closest, so the sweep runs to the far end of the board and
    /// then has to come all the way back for it. Reversing a run cannot fix that. Picking the node
    /// up and dropping it into the middle of the route — Or-opt — can.
    /// </summary>
    [Fact]
    public void AStrandedContourIsRelocatedRatherThanLeftInPlace()
    {
        var nodes = new List<RouteNode>();
        for (var i = 0; i <= 10; i++)
        {
            nodes.Add(RouteNode.ForPoint(i, Mm(i * 10.0, 0)));
        }

        // Close enough to the start of the sweep to belong there, never close enough to be next.
        nodes.Add(RouteNode.ForPoint(99, Mm(5, 40)));

        var fast = RouteOptimizer.Solve(nodes, Point2.Origin, Machine, RouteEffort.Fast);
        var full = RouteOptimizer.Solve(nodes, Point2.Origin, Machine, RouteEffort.Thorough);

        // Greedy sweeps to x=100 and then returns 103 mm for the one it skipped.
        Assert.Equal(99, fast.Steps[^1].Reference);
        Assert.True(fast.TravelMm > 195, $"expected greedy to strand it, got {fast.TravelMm:F1} mm");

        // Local search moves it into the sweep instead of returning for it.
        Assert.NotEqual(99, full.Steps[^1].Reference);
        Assert.True(
            full.TravelMm < fast.TravelMm * 0.85,
            $"local search should have relocated it: {fast.TravelMm:F1} -> {full.TravelMm:F1} mm");
    }

    /// <summary>
    /// A case with an obvious right answer: points on a line should be visited along it. Greedy
    /// alone gets this one too, which is the point — the optimizer must not be worse on easy input.
    /// </summary>
    [Fact]
    public void PointsInALineAreVisitedInOrder()
    {
        var nodes = Enumerable.Range(0, 12)
            .Select(i => RouteNode.ForPoint(i, Mm(i * 5.0, 0)))
            .ToList();

        var plan = RouteOptimizer.Solve(nodes, Point2.Origin, Machine, RouteEffort.Thorough);

        Assert.Equal(Enumerable.Range(0, 12), plan.Steps.Select(s => s.Reference));
        Assert.Equal(55.0, plan.TravelMm, 6);
    }

    // ------------------------------------------------------------------ precedence

    /// <summary>
    /// Groups are a hard partition, not a penalty. The board has to still be held down when it is
    /// drilled, so no saving anywhere is worth cutting the outline first.
    /// </summary>
    [Fact]
    public void GroupsAreNeverInterleavedHoweverMuchThatWouldSave()
    {
        // The group-1 node sits right next to the start, so any cost-only solver would take it
        // first.
        List<RouteNode> nodes =
        [
            RouteNode.ForPoint(0, Mm(100, 100)),
            RouteNode.ForPoint(1, Mm(101, 100)),
            RouteNode.ForPoint(2, Mm(1, 0), group: 1),
        ];

        var plan = RouteOptimizer.Solve(nodes, Point2.Origin, Machine, RouteEffort.Thorough);

        Assert.Equal([0, 1, 2], plan.Steps.Select(s => s.Reference).ToArray());
    }

    [Fact]
    public void GroupsRunInAscendingOrder()
    {
        var nodes = new List<RouteNode>();
        for (var g = 3; g >= 0; g--)
        {
            nodes.Add(RouteNode.ForPoint(g, Mm(g * 10.0, 0), group: g));
        }

        var plan = RouteOptimizer.Solve(nodes, Point2.Origin, Machine, RouteEffort.Thorough);

        Assert.Equal([0, 1, 2, 3], plan.Steps.Select(s => s.Reference).ToArray());
    }

    // ------------------------------------------------------------------ budget

    /// <summary>
    /// Fast is construction only, so it must return promptly and must still be a valid route — it
    /// is what a live preview runs.
    /// </summary>
    [Fact]
    public void FastSkipsLocalSearchEntirely()
    {
        var plan = RouteOptimizer.Solve(Scatter(500), Point2.Origin, Machine, RouteEffort.Fast);

        Assert.Equal(0, plan.Improvements);
        Assert.Equal(500, plan.Steps.Count);
        Assert.Equal(plan.InitialSeconds, plan.Seconds, 9);
    }

    /// <summary>
    /// The acceptance criterion from Documentation/03, section 8: 2,000 nodes finish in a time a
    /// person would sit through, on the Balanced budget.
    ///
    /// The bound is deliberately loose. On an idle machine this run takes about 0.35 s; the useful
    /// thing to catch is a regression that makes it thirty times slower, not a build agent that was
    /// busy. Two seconds was the original bound and it failed three times on a developer's machine
    /// while the other thousand tests ran alongside it — a gate that cries wolf gets muted, and then
    /// it is guarding nothing. What the optimizer's determinism rests on is the *move* budget
    /// (§6), which no clock can disturb.
    /// </summary>
    [Fact]
    public void BalancedFinishesWithinItsBudget()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var plan = RouteOptimizer.Solve(Scatter(2000), Point2.Origin, Machine);
        watch.Stop();

        Assert.True(
            watch.Elapsed < TimeSpan.FromSeconds(10),
            $"balanced took {watch.ElapsedMilliseconds} ms");

        Assert.Equal(2000, plan.Steps.Count);
    }

    /// <summary>
    /// A deterministic scatter that looks like a board: clustered runs, not uniform noise, because
    /// uniform noise is the case every ordering heuristic does well on.
    /// </summary>
    private static List<RouteNode> Scatter(int count)
    {
        var nodes = new List<RouteNode>(count);
        var random = new Random(20260908);

        for (var i = 0; i < count; i++)
        {
            var cx = random.Next(0, 12) * 8.0;
            var cy = random.Next(0, 12) * 8.0;
            var x = cx + (random.NextDouble() * 3);
            var y = cy + (random.NextDouble() * 3);

            nodes.Add(i % 4 == 0
                ? RouteNode.ForPoint(i, Mm(x, y))
                : Open(i, (x, y), (x + 1.5, y + 0.5)));
        }

        return nodes;
    }
}
