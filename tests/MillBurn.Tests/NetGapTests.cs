using System.Diagnostics;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Where a gap actually is, rather than which copper it is in.
///
/// **The distinction this file exists for is written into the type it tests.**
/// <see cref="NetJoin.Near"/> is one of a net's own points inside the shared copper — a pad centre
/// or a trace midpoint — and its own doc comment says it can be tens of millimetres from the narrow
/// place: *"enough to select the right piece of copper and not enough to point at the fault"*. A
/// view built on it says "this copper"; <see cref="ElectricalCheck.Gaps"/> says "here".
///
/// So the assertions below are about that difference being real. It is not enough that a coordinate
/// comes back — a coordinate always comes back. What matters is that it lands where a cut of this
/// width genuinely cannot fit, which is checkable directly: grow the copper by half the cut and the
/// point must be inside the result, and the two pieces it joins must be separate pieces of artwork.
/// </summary>
public sealed class NetGapTests(ITestOutputHelper output)
{
    /// <summary>
    /// A board's top copper and the cut to check it against.
    ///
    /// **The depth sets the cut width, not the moat, and this file got that wrong first.** It set
    /// `WidthNm` and named the tests after it — "at 0.4 mm" — while
    /// <see cref="IsolationOptions.EffectiveWidthNm"/> is `Tool.WidthAtDepth(DepthNm)` and never
    /// reads `WidthNm` at all. `WidthNm` decides how many passes clear a moat; the width of one
    /// pass is a property of the bit and how deep it goes. So every test ran at the default
    /// 0.05 mm depth whatever number was passed, and two tests that read as a wide cut against a
    /// narrow one were the same cut twice.
    ///
    /// `CheckSourceTests` records this exact trap — *"The depth is what does it, not the moat,
    /// which is worth stating because the first version of this file set only the moat and got
    /// nothing"* — which is the second time it has been walked into, and the reason the width is
    /// now printed by every test rather than assumed from the argument.
    /// </summary>
    private static (BoardLayer Layer, IsolationOptions Options) Copper(string board, double depthMm = 0.05)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));
        var layer = loaded.Layers.First(l => l.Role == LayerRole.TopCopper);

        return (layer, new IsolationOptions { DepthNm = Nm.FromMillimetres(depthMm) });
    }

    private static string Width(IsolationOptions options) =>
        Nm.ToMillimetreString(options.EffectiveWidthNm, 3) + " mm";

    /// <summary>
    /// Every located gap is somewhere a cut of this width cannot fit.
    ///
    /// **The check is independent of how the answer was found.** `Gaps` grows each piece of artwork
    /// separately and intersects the results; this verifies the outcome by growing the *whole*
    /// layer at once and asking whether the point is inside — a different computation reaching the
    /// same place. A point in the grown copper is a point the isolation centreline cannot pass
    /// through, which is exactly what "the cut cannot get between them here" means.
    ///
    /// Asserting it only on the whole-layer offset would be weaker than it looks, since most of the
    /// board's copper satisfies it. The guard that gives it teeth is the second one: the point must
    /// be in copper that was *grown into existence*, not in the artwork itself — a bridge is the
    /// space between two pieces, so a coordinate sitting on solid copper is a coordinate that has
    /// not found a gap at all.
    /// </summary>
    [Fact]
    public void EveryGapIsSomewhereTheCutCannotFit()
    {
        var (layer, options) = Copper(RealBoards.ArduinoMega);

        var gaps = ElectricalCheck.Gaps(layer.Area, layer.Nets, options);

        output.WriteLine($"{gaps.Count} gap(s) on the Mega's top copper at a {Width(options)} cut");

        Assert.NotEmpty(gaps);

        var grown = Geometry.Polygons.Inflate(
            layer.Area, options.EffectiveWidthNm / 2.0,
            Clipper2Lib.JoinType.Round, Clipper2Lib.EndType.Polygon);

        foreach (var gap in gaps)
        {
            var at = new Clipper2Lib.Point64(gap.At.X, gap.At.Y);

            Assert.True(
                Inside(grown, at),
                $"a gap at {Nm.ToMillimetreString(gap.At.X, 3)}, {Nm.ToMillimetreString(gap.At.Y, 3)} "
                + "is not in the copper this cut grows to, so the cut could pass through it");

            // The half that stops this passing on any point of the board: a bridge is the space
            // *between* two pieces, so it cannot be on copper that was already there.
            Assert.False(
                Inside(layer.Area, at),
                $"a gap at {Nm.ToMillimetreString(gap.At.X, 3)}, {Nm.ToMillimetreString(gap.At.Y, 3)} "
                + "is on solid copper, so it is not a gap between two pieces");
        }
    }

    /// <summary>
    /// The located gaps agree with the groups the cheap check named.
    ///
    /// Two computations of the same fact from different directions — `Isolation` merges the whole
    /// layer at once and reads the nets off each merged region, `Gaps` grows the pieces one at a
    /// time and intersects pairs — so every pair of nets that `Gaps` reports shorted must appear
    /// together inside some group `Isolation` found. A bridge between two nets that no group holds
    /// would mean one of the two is wrong about the board.
    ///
    /// **Stated in that direction and not the other on purpose.** One group can hold many bridges
    /// and a group of twenty-three nets is one merged region reached through many of them, so the
    /// counts are not equal and asserting they were would be asserting something false.
    /// </summary>
    [Fact]
    public void ALocatedShortIsOneTheNamedCheckAlsoFound()
    {
        var (layer, options) = Copper(RealBoards.ArduinoMega);

        var named = ElectricalCheck.Isolation(layer.Area, layer.Nets, options);
        var gaps = ElectricalCheck.Gaps(layer.Area, layer.Nets, options);

        var shorts = gaps.Where(g => g.IsShort).ToList();

        output.WriteLine($"{named.Joins.Count} group(s) named, {shorts.Count} short(s) located");

        Assert.NotEmpty(shorts);

        foreach (var gap in shorts)
        {
            Assert.Contains(
                named.Joins,
                join => gap.Between.All(n => join.Nets.Contains(n)));
        }
    }

    /// <summary>
    /// The same board answers the same way twice.
    ///
    /// Determinism is a hard requirement here, and this path has two places to lose it: a dictionary
    /// walked in hash order, and an unstable sort over gaps that share their first net name — the
    /// same trap <see cref="NetJoin"/>'s ordering comment records being caught by, where a ground
    /// pour shorted in two places gives two entries both beginning "GND".
    /// </summary>
    [Fact]
    public void TwoRunsLocateTheSameGapsInTheSameOrder()
    {
        var (layer, options) = Copper(RealBoards.ArduinoMega);

        var first = ElectricalCheck.Gaps(layer.Area, layer.Nets, options);
        var again = ElectricalCheck.Gaps(layer.Area, layer.Nets, options);

        // Two empty lists are equal and prove nothing. Named by a `/describe-test` pass, which said
        // plainly that this comparison had no guard under it.
        Assert.NotEmpty(first);

        Assert.Equal(
            first.Select(g => $"{string.Join("+", g.Between)}@{g.At.X},{g.At.Y}"),
            again.Select(g => $"{string.Join("+", g.Between)}@{g.At.X},{g.At.Y}"));
    }

    /// <summary>
    /// A wider cut finds more places the copper will not come apart.
    ///
    /// **This test exists because a parameter that did nothing went unnoticed.** The first version
    /// of this file set <see cref="IsolationOptions.WidthNm"/> and named its cases after it, while
    /// the check reads <see cref="IsolationOptions.EffectiveWidthNm"/> — which is the bit's width at
    /// the depth it is cutting, and never looks at `WidthNm` at all. Every case ran at the same
    /// default cut, and two that read as a wide cut against a narrow one were one cut twice.
    ///
    /// Nothing caught it, because every other assertion here quantifies over whatever came back.
    /// This one is about the relationship between the setting and the answer: a deeper cut is a
    /// wider cut, a wider cut bridges more, and a locator that ignored its options would return the
    /// same list both times.
    /// </summary>
    [Fact]
    public void AWiderCutFindsMorePlacesTheCopperStaysJoined()
    {
        var (layer, shallow) = Copper(RealBoards.ArduinoMega, 0.05);
        var (_, deep) = Copper(RealBoards.ArduinoMega, 0.25);

        var few = ElectricalCheck.Gaps(layer.Area, layer.Nets, shallow);
        var many = ElectricalCheck.Gaps(layer.Area, layer.Nets, deep);

        output.WriteLine($"{Width(shallow)} cut: {few.Count} gap(s); {Width(deep)} cut: {many.Count} gap(s)");

        // The guard that makes the comparison mean anything: the two settings really are two
        // different cuts. Without it a change that stopped depth reaching the width would make both
        // sides equal and the assertion below would be comparing a list against itself.
        Assert.True(
            deep.EffectiveWidthNm > shallow.EffectiveWidthNm,
            $"both settings cut {Width(shallow)}, so there is no wider cut to compare");

        Assert.True(
            many.Count > few.Count,
            $"a {Width(deep)} cut found {many.Count} place(s) and a {Width(shallow)} cut found "
            + $"{few.Count}; a wider cut cannot bridge less copper");
    }

    /// <summary>
    /// A board whose copper comes apart reports nothing shorted.
    ///
    /// **The case that catches a locator which cannot say "clean".** Every assertion above
    /// quantifies over whatever came back, so a routine that always returned something would pass
    /// all of them while putting phantom faults in front of an operator.
    ///
    /// **The board is the variable here, not the cut** — both this and the Mega above run at the
    /// same default 0.05 mm depth and therefore the same cut width. An earlier version of this file
    /// framed it as a wide cut against a narrow one, which was never true: it set `WidthNm`, which
    /// does not reach the check. PogoTest1 is simply a sparse board, and `CheckSourceTests` records
    /// that it produces nothing but advice at this setting.
    /// </summary>
    [Fact]
    public void ABoardWhoseCopperComesApartReportsNoShorts()
    {
        var (layer, options) = Copper(RealBoards.PogoTest1);

        var gaps = ElectricalCheck.Gaps(layer.Area, layer.Nets, options);

        output.WriteLine(string.Join(
            "\n",
            gaps.Select(g => $"{string.Join(" and ", g.Between)} at {Nm.ToMillimetreString(g.At.X, 3)}, {Nm.ToMillimetreString(g.At.Y, 3)}")));

        Assert.DoesNotContain(gaps, g => g.IsShort);
    }

    /// <summary>
    /// Locating the gaps costs enough to belong behind a button, and not so much that the button is
    /// useless.
    ///
    /// **This test is the argument for the architecture, which is why it asserts a number.** The
    /// whole reason `Gaps` is not called by `ElectricalCheck.Isolation` is that it is an offset per
    /// piece of copper and a boolean per surviving pair, against a check that runs on every export
    /// and every preview. If it were cheap it should simply be folded in; if it were ruinous the
    /// view could not offer it at all.
    ///
    /// **The measurement that shaped the implementation.** The first version paired every piece
    /// against every other and rejected the far ones by their bounding boxes, which is useless
    /// against a ground pour whose box is the whole board: every other piece survived the rejection
    /// and paid for a boolean against a two-hundred-ring pour. That took **1,205 ms** on the Mega's
    /// top copper at 0.4 mm. Restricting pairs to pieces that merged into the same grown region —
    /// pieces in different regions are provably further apart than the cut — took it to **266 ms**
    /// for the same eight gaps.
    ///
    /// The ceiling is deliberately loose at ten seconds. It is a guard against a change that makes
    /// this quadratic in the board again, not a benchmark: a machine slower than this one should
    /// not fail a test about architecture, and a regression to the original shape would blow
    /// through it on a denser board.
    /// </summary>
    [Fact]
    public void LocatingTheGapsIsWorthPuttingBehindAButton()
    {
        var (layer, options) = Copper(RealBoards.ArduinoMega);

        // Warmed, so the figure is the work rather than the first-call JIT of the offsetter.
        var warmed = ElectricalCheck.Gaps(layer.Area, layer.Nets, options);

        var clock = Stopwatch.StartNew();
        var gaps = ElectricalCheck.Gaps(layer.Area, layer.Nets, options);
        clock.Stop();

        output.WriteLine(
            $"{gaps.Count} gap(s) across {layer.Area.Count} contour(s) in {clock.ElapsedMilliseconds} ms");

        // **A timing test that asserts only a ceiling passes fastest when the work stops being
        // done.** Returning nothing instantly would satisfy the clock perfectly, so the answer has
        // to be there as well as quick. Pointed out by a `/describe-test` pass.
        Assert.NotEmpty(gaps);
        Assert.Equal(warmed.Count, gaps.Count);

        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds(10),
            $"locating the gaps took {clock.ElapsedMilliseconds} ms, which is too long to sit behind a button");
    }

    /// <summary>
    /// Whether a point is in this area, counting a point exactly on an edge as in it.
    ///
    /// **`IsOn` is deliberately not treated as outside**, and a `/describe-test` pass is what made
    /// the choice explicit. `Somewhere` falls back to a vertex of the overlap when the centroid is
    /// not inside it, so a located gap can legitimately sit on a boundary — counting that as
    /// outside would fail the "in the grown copper" assertion for a point that is exactly where the
    /// copper bridges. It also makes the "not on solid copper" assertion stricter, which is the
    /// direction that matters: it is the half that stops any point on the board passing.
    /// </summary>
    private static bool Inside(Clipper2Lib.Paths64 area, Clipper2Lib.Point64 at)
    {
        var crossings = 0;

        foreach (var ring in area)
        {
            if (Geometry.Polygons.PointIn(at, ring) != Clipper2Lib.PointInPolygonResult.IsOutside)
            {
                crossings++;
            }
        }

        // Odd means inside: in an outer ring and in one of its holes is outside again, which is what
        // puts a point in a pour's cutout on the island standing there instead.
        return crossings % 2 == 1;
    }
}
