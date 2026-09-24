using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Geometry;
using MillBurn.Optimize;
using MillBurn.Pipeline;
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
    /// Two pads, each isolated once, far enough apart that the tool is never asked.
    ///
    /// **This tests the reach, not the material** — and it used to claim otherwise. Its comment said
    /// "the case a distance rule gets wrong, which is why there is not one", while the pair it built
    /// is thrown out *by* the distance cutoff: the rings' start and end vertices are 2.2 mm apart,
    /// against a reach of ten cut widths, so the question of what lies between them is never
    /// reached. Asserting that nothing was linked was therefore true for a reason the name did not
    /// mention, and would have stayed true if the material rule were deleted outright.
    ///
    /// The reach is worth a test of its own, so this is now that test and says so. What actually
    /// holds the line on material is <see cref="ALinkIsNeverAllowedAcrossTheIslandARingSurrounds"/>,
    /// where a crossing really is proposed and refused, and
    /// <see cref="NoLinkOnARealBoardTouchesCopper"/>, which measures every link on a real board
    /// against the copper from the Gerber.
    /// </summary>
    [Fact]
    public void TwoIslandsTooFarApartAreNeverEvenConsidered()
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

        // Ordered first, and measured on that: the linker sees the ordered toolpath, and a gap
        // measured on the unordered one is a fact about a pair it may never have been offered.
        var ordered = Ordered(isolation);

        var gap = ordered.Passes[0].End.DistanceTo(ordered.Passes[1].Start) / Nm.PerMillimetre;
        var (_, result) = PassLinker.Apply(ordered);

        output.WriteLine($"islands {gap:F3} mm apart, {result.Linked} linked of {result.Considered} considered");

        // Never even asked: beyond the reach, the geometry is not run at all. Pinned rather than
        // assumed, because "nothing was linked" reads the same whichever rule did the refusing, and
        // the two are worth telling apart.
        Assert.Equal(0, result.Considered);
        Assert.Equal(0, result.Linked);
        Assert.True(gap > 2.0, $"the pads are {gap:F3} mm apart, which is no longer out of reach");
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

        // Two halves of the same ring, the second taken backwards so it *starts* at the far side:
        // the straight line from where the first ends to where the second begins runs through the
        // middle of the pad.
        //
        // The second half used to be handed over the right way round, which shares its first vertex
        // with the first half's last one — so the link under test was zero length, sitting on the
        // contour the tool had just cut, and no crossing of the island was ever proposed. The test
        // refused something, and it was not this.
        var half = ring.Count / 2;

        var toolpath = new Toolpath
        {
            Kind = ToolpathKind.Isolation,
            Label = "Across a pad",
            Tool = VBit,
            Passes =
            [
                Pass(ring.Take(half + 1), closed: false),
                Pass(Enumerable.Reverse(ring.Skip(half).ToList()), closed: false),
            ],
        };

        var (_, result) = PassLinker.Apply(toolpath);

        var across = toolpath.Passes[0].End.DistanceTo(toolpath.Passes[1].Start) / Nm.PerMillimetre;

        output.WriteLine($"across {across:F3} mm of pad: {result.Linked} linked of {result.Considered}");

        // The link really does span the pad, so the refusal below is about the copper in the way
        // rather than about there being nothing to cross.
        // Against the diameter, which is what the message names. It compared against the radius,
        // so a link half way across would have satisfied a guard whose text claims it crossed.
        Assert.True(
            across > Nm.ToMillimetres(radius) * 2,
            $"the proposed link is {across:F3} mm, which does not cross a {Nm.ToMillimetres(radius) * 2:F3} mm pad");

        Assert.True(result.Considered > 0, "the pair was never offered to the rule being tested");
        Assert.Equal(0, result.Linked);
    }

    /// <summary>
    /// An outline cuts through the stock; there is nothing beside it that is already gone.
    ///
    /// With a control arm, and ordered — without either, this asserted that some passes did not
    /// link without ever showing they otherwise would, so deleting the rule it guards need not have
    /// failed it. The two arms differ in one field.
    /// </summary>
    [Fact]
    public void AnOutlineNeverLinks()
    {
        var isolation = Ordered(IsolationOperation.Build(
            [Tessellate.Circle(new Point2(0, 0), Nm.FromMillimetres(0.85))],
            new IsolationOptions { DepthNm = Depth, WidthNm = Nm.FromMillimetres(0.4) }));

        var asOutline = isolation with { Kind = ToolpathKind.Outline };

        Assert.Equal(isolation.Passes.Count - 1, PassLinker.Apply(isolation).Result.Linked);
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
    /// A lap that starts deeper than the last one ended carries on, and is reached by dropping
    /// where the tool stands.
    ///
    /// This used to assert the opposite, and gave its reason: "a linked pass is written without a
    /// plunge". That was true of the emitter and is no longer — 6.24 is precisely the change, and
    /// the reason had become the whole of the justification. Lifting to the safe height, rapiding to
    /// the point the tool is already standing on and dropping back past where it started is three
    /// moves that achieve nothing, on the axis that runs at a twentieth of the others.
    ///
    /// What has not changed is the condition: the two passes must meet at a point. Anything else is
    /// a journey, and <see cref="AnotherFeatureStartingWhereOneEndsDoesNotCarryOn"/> and the
    /// distance cases below still hold the line there.
    /// </summary>
    [Fact]
    public void ALapStartingDeeperThanTheLastEndedDropsStraightToIt()
    {
        var toolpath = HoleOnAThinBoard();
        var plunged = toolpath with { Passes = [.. toolpath.Passes.Select(p => p with { RampFromNm = null })] };

        var (linked, result) = PassLinker.Apply(plunged);

        Assert.Equal(plunged.Passes.Count - 1, result.Continued);
        Assert.All(linked.Passes.Skip(1), p => Assert.True(p.LinkedFromPrevious));

        // And the program says so: the deeper lap is reached by a feed straight down, with no
        // retract to the safe height in between. Read from the emitted text rather than from the
        // flag, because the flag is a request and the emitter is what the machine is given.
        var job = new Job { Name = "drop", Toolpaths = [linked] };
        var (text, _) = GcodeEmitter.Emit(job, new GcodeOptions());

        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var firstCut = lines.FindIndex(l => l.StartsWith("G1 Z-", StringComparison.Ordinal));

        Assert.True(firstCut >= 0, "the program never reaches depth");

        var after = lines.Skip(firstCut + 1).ToList();

        Assert.Contains(after, l => l.StartsWith("G1 Z-", StringComparison.Ordinal));

        // Nothing at all between the two descents: no lift, and no reposition either. "Straight to
        // it" is a claim about X and Y as much as about Z — a rapid across at the safe height and a
        // rapid across on the spot are different programs, and only one of them is this story.
        var between = after.TakeWhile(l => !l.StartsWith("G1 Z-", StringComparison.Ordinal)).ToList();

        Assert.DoesNotContain(between, l => l.StartsWith("G0 ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two features that happen to meet are still two features: the next one's first lap is reached
    /// the ordinary way, not treated as the other's next lap.
    /// </summary>
    [Fact]
    public void AnotherFeatureStartingWhereOneEndsDoesNotCarryOn()
    {
        // With a control arm, because without one this proved nothing about *why* it refuses.
        //
        // The fixture below differs from a hole's laps — which do carry on — in four ways at once:
        // the stack, whether the passes are closed, the geometry, and the tool. Any of them could
        // have been the reason, and a linker with the continuation rule deleted outright would have
        // produced the same answer. So the two arms here differ in one field: the stack.
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

        // The same two passes, told they are one feature: now it carries on. This is what makes the
        // line above a statement about the stack rather than about anything else in the fixture.
        var sameStack = toolpath with
        {
            Passes = [.. toolpath.Passes.Select(p => p with { Stack = 0 })],
        };

        Assert.Equal(1, PassLinker.Apply(sameStack).Result.Continued);
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
