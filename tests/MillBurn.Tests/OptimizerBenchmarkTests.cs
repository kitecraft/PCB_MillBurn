using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Optimize;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The acceptance gate for Phase 3 (Documentation/03, section 8), on real boards.
///
/// The comparison is against the nearest-neighbour orderer that actually shipped, not against a
/// straw man: that baseline already considers both ends of every candidate, which is the defect
/// pcb2gcode's solver has. Beating a baseline that was deliberately built well is the only result
/// worth reporting, and it is the reason the baseline is kept in the tree.
///
/// These assert direction and magnitude rather than exact millimetres. A gate that pins the number
/// fails on every legitimate improvement, which teaches whoever sees it to update the number
/// without looking — and then it is guarding nothing.
/// </summary>
public sealed class OptimizerBenchmarkTests(ITestOutputHelper output)
{
    private static readonly MachineProfile Machine = new();

    private static Board Load(string name) =>
        BoardLoader.LoadFolder(RealBoards.Directory(name));

    /// <summary>Rapid distance for a toolpath as ordered, including the park move home.</summary>
    private static double TravelMm(Toolpath toolpath, Point2 from)
    {
        var at = from;
        var total = 0.0;

        foreach (var pass in toolpath.Passes)
        {
            total += at.DistanceTo(pass.Start) / Nm.PerMillimetre;
            at = pass.End;
        }

        foreach (var drill in toolpath.Drills)
        {
            total += at.DistanceTo(drill.At) / Nm.PerMillimetre;
            at = drill.At;
        }

        return total + (at.DistanceTo(from) / Nm.PerMillimetre);
    }

    private static Toolpath Baseline(Toolpath toolpath, Point2 from) => toolpath with
    {
        Passes = NearestNeighbour.Order(toolpath.Passes, from),
        Drills = NearestNeighbour.Order(toolpath.Drills, from),
    };

    private static (Toolpath Path, Point2 Start) IsolationFor(string board)
    {
        var loaded = Load(board);
        var copper = loaded.Layers.First(l => l.Role == LayerRole.TopCopper);

        var toolpath = IsolationOperation.Build(
            copper.Area,
            new IsolationOptions { Tool = Tool.DefaultVBit, DepthNm = Nm.FromMillimetres(0.05) });

        return (toolpath, new Point2(loaded.Bounds.MinX, loaded.Bounds.MinY));
    }

    /// <summary>
    /// The board's busiest drill file. Picking the first one instead lands on whichever of PTH and
    /// NPTH happens to sort first, and an eight-hole NPTH file is already optimally ordered by any
    /// method — which reads as "the optimizer achieved nothing" rather than "there was nothing to
    /// achieve".
    /// </summary>
    private static (Toolpath Path, Point2 Start) DrillingFor(string board)
    {
        var loaded = Load(board);

        var drill = loaded.Layers
            .Where(l => l.Drill is not null)
            .OrderByDescending(l => l.Drill!.Hits.Count)
            .First();

        // One toolpath per drill size; merged here because the ordering question is over every
        // hole in the file, exactly as the exporter treats it.
        var byTool = DrillOperation.Build(
            drill.Drill!,
            new DrillOptions { BoardThicknessNm = Nm.FromMillimetres(1.6) },
            Tool.DefaultDrill);

        var toolpath = byTool.Count == 0
            ? new Toolpath { Kind = ToolpathKind.Drill, Label = drill.Label, Tool = Tool.DefaultDrill }
            : byTool.Aggregate((a, b) => a with { Drills = [.. a.Drills, .. b.Drills] });

        return (toolpath, new Point2(loaded.Bounds.MinX, loaded.Bounds.MinY));
    }

    private void Report(string what, double before, double after)
    {
        var saved = before <= 0 ? 0 : 1.0 - (after / before);
        output.WriteLine($"{what,-34} {before,7:F1} mm -> {after,7:F1} mm   {saved,6:P1}");
    }

    // ------------------------------------------------------------------ against the baseline

    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.GridStripConnector)]
    public void IsolationRoutingBeatsTheNearestNeighbourBaseline(string board)
    {
        var (toolpath, start) = IsolationFor(board);

        var baseline = TravelMm(Baseline(toolpath, start), start);
        var optimised = TravelMm(
            ToolpathRouter.Order(toolpath, start, Machine, RouteEffort.Thorough, start).Ordered, start);

        Report($"{board} isolation", baseline, optimised);

        Assert.True(
            optimised <= baseline,
            $"the optimizer lost to the baseline: {baseline:F1} -> {optimised:F1} mm");
    }

    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.GridStripConnector)]
    public void DrillRoutingBeatsTheNearestNeighbourBaseline(string board)
    {
        var (toolpath, start) = DrillingFor(board);

        var baseline = TravelMm(Baseline(toolpath, start), start);
        var optimised = TravelMm(
            ToolpathRouter.Order(toolpath, start, Machine, RouteEffort.Thorough, start).Ordered, start);

        Report($"{board} drilling ({toolpath.Drills.Count} holes)", baseline, optimised);

        // GridStripConnector has drill files with no hits in them, which is worth keeping as a case:
        // an ordering pass over nothing must be a no-op rather than a crash.
        Assert.True(
            optimised <= baseline,
            $"the optimizer lost to the baseline: {baseline:F1} -> {optimised:F1} mm");
    }

    /// <summary>
    /// The magnitude, on the boards in the corpus. Isolation is where most of the rapid lives,
    /// because a board has far more copper islands than it has holes.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1, 0.15)]
    [InlineData(RealBoards.GridStripConnector, 0.40)]
    public void IsolationTravelFallsBySomethingWorthHaving(string board, double atLeast)
    {
        var (toolpath, start) = IsolationFor(board);

        var baseline = TravelMm(Baseline(toolpath, start), start);
        var optimised = TravelMm(
            ToolpathRouter.Order(toolpath, start, Machine, RouteEffort.Thorough, start).Ordered, start);

        var saved = 1.0 - (optimised / baseline);

        Assert.True(
            saved >= atLeast,
            $"expected at least {atLeast:P0} off, got {saved:P1} ({baseline:F1} -> {optimised:F1} mm)");
    }

    /// <summary>
    /// Drilling is where the ordering has the most room, because every hole is a bare point with no
    /// orientation to get right — so it is pure ordering, and the baseline's greedy sweep strands
    /// holes exactly the way nearest-neighbour always does.
    /// </summary>
    [Fact]
    public void DrillingImprovesByAtLeastATenth()
    {
        var (toolpath, start) = DrillingFor(RealBoards.PogoTest1);

        var baseline = TravelMm(Baseline(toolpath, start), start);
        var optimised = TravelMm(
            ToolpathRouter.Order(toolpath, start, Machine, RouteEffort.Thorough, start).Ordered, start);

        Assert.True(
            optimised < baseline * 0.9,
            $"expected at least 10% off drilling travel, got {baseline:F1} -> {optimised:F1} mm");
    }

    // ------------------------------------------------------------------ it stays correct

    /// <summary>
    /// Reordering must not change what is cut, only when. Same contours, same total length, same
    /// number of holes — anything else means geometry was lost or duplicated on the way through.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.GridStripConnector)]
    public void OrderingChangesTheOrderAndNothingElse(string board)
    {
        var (toolpath, start) = IsolationFor(board);
        var ordered = ToolpathRouter.Order(toolpath, start, Machine, RouteEffort.Thorough, start).Ordered;

        Assert.Equal(toolpath.Passes.Count, ordered.Passes.Count);
        Assert.Equal(toolpath.Drills.Count, ordered.Drills.Count);

        Assert.Equal(
            toolpath.Passes.Sum(p => p.LengthNm),
            ordered.Passes.Sum(p => p.LengthNm),
            2);

        // Every contour still has the same number of segments, and each is still a closed cycle.
        Assert.Equal(
            toolpath.Passes.Select(p => p.Path.Count).Order(),
            ordered.Passes.Select(p => p.Path.Count).Order());

        foreach (var pass in ordered.Passes.Where(p => p.Closed))
        {
            Assert.Equal(pass.Path[0].From, pass.Path[^1].To);
        }
    }

    /// <summary>
    /// A rotated contour has to stay connected end to end. A rotation that broke the cycle would
    /// leave a gap the tool never cuts, and the picture would look perfect.
    /// </summary>
    [Fact]
    public void ARotatedContourIsStillContinuous()
    {
        var (toolpath, start) = IsolationFor(RealBoards.PogoTest1);
        var ordered = ToolpathRouter.Order(toolpath, start, Machine, RouteEffort.Thorough, start).Ordered;

        foreach (var pass in ordered.Passes)
        {
            for (var i = 0; i < pass.Path.Count - 1; i++)
            {
                Assert.Equal(pass.Path[i].To, pass.Path[i + 1].From);
            }
        }
    }

    /// <summary>
    /// A panel is the case this phase exists for: dozens of separate cutouts, which is exactly the
    /// "edge cuts all over the place causing tons of excessive travel" the project started from.
    ///
    /// Optional, because a panel is a large file and somebody's real design. Point
    /// <c>MILLBURN_BOARDS</c> at a corpus containing a <c>Panel</c> folder and this runs; otherwise
    /// it passes quietly rather than failing for a board nobody supplied.
    /// </summary>
    [Fact]
    public void APanelIsWhereTheOrderingMattersMost()
    {
        if (!RealBoards.Has(RealBoards.Panel))
        {
            output.WriteLine("no Panel board in the corpus; nothing measured");
            return;
        }

        var (isolation, start) = IsolationFor(RealBoards.Panel);
        var outline = OutlineFor(RealBoards.Panel);

        foreach (var (what, toolpath) in new[] { ("isolation", isolation), ("outline", outline) })
        {
            var baseline = TravelMm(Baseline(toolpath, start), start);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var optimised = TravelMm(
                ToolpathRouter.Order(toolpath, start, Machine, RouteEffort.Balanced, start).Ordered, start);
            watch.Stop();

            Report($"panel {what} ({toolpath.Passes.Count} contours, {watch.ElapsedMilliseconds} ms)",
                baseline, optimised);

            Assert.True(
                optimised <= baseline,
                $"the optimizer lost to the baseline on the panel {what}: "
                + $"{baseline:F1} -> {optimised:F1} mm");
        }
    }

    private static Toolpath OutlineFor(string board)
    {
        var loaded = Load(board);
        var outline = loaded.Layers.First(l => l.Role == LayerRole.Outline);

        return OutlineOperation.Build(
            outline.Area,
            new OutlineOptions
            {
                Tool = Tool.DefaultOutlineMill,
                BoardThicknessNm = Nm.FromMillimetres(1.6),
            });
    }

    /// <summary>The whole point of a budget: it has to be affordable on every settled edit.</summary>
    [Fact]
    public void ARealBoardOrdersWellInsideTheBalancedBudget()
    {
        var (toolpath, start) = IsolationFor(RealBoards.PogoTest1);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var plan = ToolpathRouter.Order(toolpath, start, Machine, RouteEffort.Balanced, start).Plan;
        watch.Stop();

        output.WriteLine(
            $"{toolpath.Passes.Count} contours ordered in {watch.ElapsedMilliseconds} ms, "
            + $"{plan.Improvements} improving moves");

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.ElapsedMilliseconds} ms");
    }
}
