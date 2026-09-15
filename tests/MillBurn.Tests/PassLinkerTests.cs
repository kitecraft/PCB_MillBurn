using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Geometry;
using MillBurn.Optimize;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Staying down between passes that touch.
///
/// Requested from the workshop: "When cutting paths next to each other, the job does a z-lift then
/// back down. Cutting around a circle with 3 loops: the first loop is cut, then a z-lift and
/// z-lower, then the second ring. Perhaps we can optimize some of them out?"
///
/// The prize is larger than it sounds, because Z traverse on an ordinary GRBL router is 100 mm/min
/// against 2000 in XY. One lift-and-return is about 2.7 seconds, and a three-lap isolation of a
/// board spends a quarter of its wall clock doing them.
///
/// The danger is equally large and points the other way: a link across copper that was meant to
/// stay is a cut trace, and it would be invisible in the file. So the tests that matter here are
/// the refusals.
/// </summary>
public sealed class PassLinkerTests(ITestOutputHelper output)
{
    private static readonly Tool VBit = Tool.DefaultVBit;

    private static readonly long Depth = Nm.FromMillimetres(0.05);

    private static long Width => VBit.WidthAtDepth(Depth);

    // ------------------------------------------------------------------ the shape it was asked about

    /// <summary>
    /// Three laps round one pad: cut, hop a stepover, cut, hop, cut. Two lifts removed from three.
    /// </summary>
    [Fact]
    public void ThreeLapsRoundOnePadBecomeOnePlunge()
    {
        var isolation = IsolationOperation.Build(
            [Tessellate.Circle(new Point2(0, 0), Nm.FromMillimetres(0.85))],
            new IsolationOptions { DepthNm = Depth, WidthNm = Nm.FromMillimetres(0.4) });

        var laps = isolation.Passes.Count;
        Assert.True(laps > 2, $"a 0.4 mm moat should take several laps, got {laps}");

        var (linked, result) = PassLinker.Apply(Ordered(isolation));

        output.WriteLine(
            $"{laps} laps, {result.Linked} of {result.Considered} links kept down, {result.LinkedLengthMm:F3} mm");

        // Every lap after the first is reached without lifting: one plunge for the whole pad.
        Assert.Equal(laps - 1, result.Linked);
        Assert.Equal(laps - 1, linked.Passes.Count(p => p.LinkedFromPrevious));
        Assert.False(linked.Passes[0].LinkedFromPrevious);
    }

    // ------------------------------------------------------------------ the refusals

    /// <summary>
    /// Two pads a hair apart, each isolated once. The gap between the two rings can be smaller than
    /// the stepover and it is still not linkable: there is a trace's worth of copper between them,
    /// and the previous lap cleared nothing on the way across.
    ///
    /// This is the case a distance rule gets wrong, which is why there is not one.
    /// </summary>
    [Fact]
    public void TwoSeparateIslandsAreNeverLinked()
    {
        // Far enough apart that the offsets do not merge, close enough that the rings nearly touch.
        var apart = Nm.FromMillimetres(2.2);

        var isolation = IsolationOperation.Build(
            [
                Tessellate.Circle(new Point2(0, 0), Nm.FromMillimetres(1.0)),
                Tessellate.Circle(new Point2(apart, 0), Nm.FromMillimetres(1.0)),
            ],
            new IsolationOptions { DepthNm = Depth, WidthNm = 0 });

        Assert.Equal(2, isolation.Passes.Count);

        var gap = isolation.Passes[0].End.DistanceTo(isolation.Passes[1].Start) / Nm.PerMillimetre;
        var (_, result) = PassLinker.Apply(Ordered(isolation));

        output.WriteLine($"islands {gap:F3} mm apart, {result.Linked} linked of {result.Considered} considered");

        Assert.Equal(0, result.Linked);
    }

    /// <summary>
    /// The inside of an isolation ring is copper, not cleared ground.
    ///
    /// A ring inflated as a <em>polygon</em> rather than as a ribbon claims the island it surrounds
    /// as material already removed, and a link straight across the middle of a pad would then look
    /// safe. It is the one mistake in here that cuts a trace in half, so it gets its own test:
    /// a pass starting on the far side of a pad is not reachable across it.
    /// </summary>
    [Fact]
    public void ALinkIsNeverAllowedAcrossTheIslandARingSurrounds()
    {
        var radius = Nm.FromMillimetres(0.5);
        var ring = Tessellate.Circle(new Point2(0, 0), radius + (Width / 2));

        // Two halves of the same ring, entered at opposite ends: the straight line between them
        // runs through the middle of the pad.
        var half = ring.Count / 2;

        var toolpath = new Toolpath
        {
            Kind = ToolpathKind.Isolation,
            Label = "Across a pad",
            Tool = VBit,
            Passes =
            [
                Pass(ring.Take(half + 1), closed: false),
                Pass(ring.Skip(half), closed: false),
            ],
        };

        var (_, result) = PassLinker.Apply(toolpath);

        output.WriteLine(
            $"across {toolpath.Passes[0].End.DistanceTo(toolpath.Passes[1].Start) / Nm.PerMillimetre:F3} mm of pad: {result.Linked} linked");

        Assert.Equal(0, result.Linked);
    }

    /// <summary>An outline cuts through the stock; there is nothing beside it that is already gone.</summary>
    [Fact]
    public void AnOutlineNeverLinks()
    {
        var isolation = IsolationOperation.Build(
            [Tessellate.Circle(new Point2(0, 0), Nm.FromMillimetres(0.85))],
            new IsolationOptions { DepthNm = Depth, WidthNm = Nm.FromMillimetres(0.4) });

        var asOutline = isolation with { Kind = ToolpathKind.Outline };

        Assert.Equal(0, PassLinker.Apply(asOutline).Result.Linked);
    }

    /// <summary>
    /// A deeper pass has to plunge, however close it starts to where the last one finished. The
    /// tool is above the material it is about to cut, not in it.
    /// </summary>
    [Fact]
    public void ADifferentDepthAlwaysPlunges()
    {
        var isolation = IsolationOperation.Build(
            [Tessellate.Circle(new Point2(0, 0), Nm.FromMillimetres(0.85))],
            new IsolationOptions { DepthNm = Depth, WidthNm = Nm.FromMillimetres(0.4) });

        var ordered = Ordered(isolation);

        // Every lap a step deeper than the one before, so no two consecutive passes share a depth.
        // The geometry is otherwise identical to the case that links every one of them.
        var stepped = ordered with
        {
            Passes = [.. ordered.Passes.Select((p, i) =>
                p with { DepthNm = p.DepthNm + (i * Nm.FromMillimetres(0.02)) })],
        };

        Assert.Equal(ordered.Passes.Count - 1, PassLinker.Apply(ordered).Result.Linked);
        Assert.Equal(0, PassLinker.Apply(stepped).Result.Linked);
    }

    // ------------------------------------------------------------------ routing: laps that carry on

    private static Toolpath HoleOnAThinBoard()
    {
        var mill = new Tool
        {
            Name = "0.8 mm end mill",
            Kind = ToolKind.EndMill,
            DiameterNm = Nm.FromMillimetres(0.8),
            StepdownNm = Nm.FromMillimetres(0.5),
        };

        var plan = SlotOperation.Holes(
            [new DrillSlotTarget(0, Point2.Origin, Point2.Origin, Nm.FromMillimetres(2.2))],
            new ToolLibrary { Tools = [mill] },
            new SlotOptions { BoardThicknessNm = Nm.FromMillimetres(0.8), BreakThroughNm = Nm.FromMillimetres(0.3) });

        return Assert.Single(plan.Toolpaths);
    }

    /// <summary>
    /// A milled hole's laps are one helix, so every lap after the first carries straight on.
    ///
    /// Found at the machine: a 2.2 mm hole on a 0.8 mm board was four laps with a lift between each,
    /// because every lap starts deeper than the last and exactly where it ended — the two things the
    /// area rule never links.
    /// </summary>
    [Fact]
    public void AHolesLapsCarryStraightOnWithoutLifting()
    {
        var toolpath = HoleOnAThinBoard();
        var (linked, result) = PassLinker.Apply(toolpath);

        output.WriteLine($"{toolpath.Passes.Count} laps, {result.Continued} carried on, {result.Linked} linked across");

        Assert.True(toolpath.Passes.Count > 1, "a 1.1 mm hole in 0.5 mm steps is more than one lap");
        Assert.Equal(toolpath.Passes.Count - 1, result.Continued);
        Assert.Equal(0, result.Linked);
        Assert.False(linked.Passes[0].LinkedFromPrevious);
        Assert.All(linked.Passes.Skip(1), p => Assert.True(p.LinkedFromPrevious));
    }

    /// <summary>
    /// A lap that would start deeper than the last one ended needs a plunge to reach, and a linked
    /// pass is written without one — so it never carries on, however exactly it lines up.
    /// </summary>
    [Fact]
    public void ALapStartingDeeperThanTheLastEndedDoesNotCarryOn()
    {
        var toolpath = HoleOnAThinBoard();
        var plunged = toolpath with { Passes = [.. toolpath.Passes.Select(p => p with { RampFromNm = null })] };

        Assert.Equal(0, PassLinker.Apply(plunged).Result.Continued);
    }

    /// <summary>
    /// Two features that happen to meet are still two features: the next one's first lap is reached
    /// the ordinary way, not treated as the other's next lap.
    /// </summary>
    [Fact]
    public void AnotherFeatureStartingWhereOneEndsDoesNotCarryOn()
    {
        var middle = new Point2(Nm.FromMillimetres(5), 0);

        var toolpath = new Toolpath
        {
            Kind = ToolpathKind.Outline,
            Label = "Two slots end to end",
            Tool = Tool.DefaultOutlineMill,
            Passes =
            [
                new ToolpathPass
                {
                    Path = [new ArtSegment(ArtSweep.Linear, Point2.Origin, middle, Point2.Origin)],
                    DepthNm = Nm.FromMillimetres(0.5),
                    RampFromNm = 0,
                    Stack = 0,
                },
                new ToolpathPass
                {
                    Path = [new ArtSegment(ArtSweep.Linear, middle, new Point2(Nm.FromMillimetres(10), 0), Point2.Origin)],
                    DepthNm = Nm.FromMillimetres(1.0),
                    RampFromNm = Nm.FromMillimetres(0.5),
                    Stack = 1,
                },
            ],
        };

        Assert.Equal(0, PassLinker.Apply(toolpath).Result.Continued);
    }

    // ------------------------------------------------------------------ on a real board

    /// <summary>
    /// The check that would catch a gouge: sweep every link on a real board's isolation by the
    /// tool and intersect it with the copper the board is made of.
    ///
    /// Deliberately not asking <c>PassLinker</c> whether it was right — that would only prove it
    /// agrees with itself. This takes the copper straight from the Gerber and the links from the
    /// passes it marked, and the two have no code in common.
    /// </summary>
    [Fact]
    public void NoLinkOnARealBoardTouchesCopper()
    {
        var copper = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1))
            .Layers.First(l => l.Role == LayerRole.TopCopper).Area;

        var isolation = IsolationOperation.Build(
            copper,
            new IsolationOptions { DepthNm = Depth, WidthNm = Nm.FromMillimetres(0.4) });

        var (linked, result) = PassLinker.Apply(Ordered(isolation));

        output.WriteLine(
            $"{isolation.Passes.Count} passes, {result.Linked} of {result.Considered} links kept down");

        Assert.True(result.Linked > 0, "the board should have linkable laps at all");

        var half = Width / 2;
        var worst = 0.0;

        for (var i = 1; i < linked.Passes.Count; i++)
        {
            if (!linked.Passes[i].LinkedFromPrevious)
            {
                continue;
            }

            var from = linked.Passes[i - 1].End;
            var to = linked.Passes[i].Start;

            var swept = Clipper.InflatePaths(
                new Paths64 { new Path64 { new Point64(from.X, from.Y), new Point64(to.X, to.Y) } },
                half,
                JoinType.Round,
                EndType.Round);

            var bitten = Math.Abs(Clipper.Area(Clipper.Intersect(swept, copper, FillRule.NonZero)));
            worst = Math.Max(worst, bitten);
        }

        // In square nanometres, so a whole square micron is 1e6. Nothing should register at all.
        output.WriteLine($"worst copper touched by any link: {worst:N0} nm²");
        Assert.True(worst < 1e6, $"a link bit {worst:N0} nm² out of the copper");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Puts the laps in the order the router would: nearest next. The linker reads the order it is
    /// given and nothing else, so a test that fed it an arbitrary one would be testing nothing.
    /// </summary>
    private static Toolpath Ordered(Toolpath toolpath)
    {
        var remaining = toolpath.Passes.ToList();
        var ordered = new List<ToolpathPass>(remaining.Count);
        var at = Point2.Origin;

        while (remaining.Count > 0)
        {
            var best = 0;
            for (var i = 1; i < remaining.Count; i++)
            {
                if (at.DistanceTo(remaining[i].Start) < at.DistanceTo(remaining[best].Start))
                {
                    best = i;
                }
            }

            at = remaining[best].End;
            ordered.Add(remaining[best]);
            remaining.RemoveAt(best);
        }

        return toolpath with { Passes = ordered };
    }

    private static ToolpathPass Pass(IEnumerable<Point64> points, bool closed)
    {
        var list = points.Select(p => new Point2(p.X, p.Y)).ToList();
        var segments = new List<ArtSegment>(list.Count);

        for (var i = 1; i < list.Count; i++)
        {
            segments.Add(new ArtSegment(ArtSweep.Linear, list[i - 1], list[i], Point2.Origin));
        }

        return new ToolpathPass { Path = segments, DepthNm = Depth, Closed = closed };
    }
}
