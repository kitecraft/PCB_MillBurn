using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Geometry;
using MillBurn.Optimize;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// An arc replaces segments, not points — so it has to follow them.
///
/// **The fault this exists for.** On the Arduino Mega, eight isolation passes were emitted as
/// shallow arcs across runs of copper that are straight. The worst bowed **464 µm** off a
/// twelve-millimetre segment: at the middle of the bow it cut into the region it was supposed to
/// leave alone, and at the ends it left copper it was supposed to take. Reported from the bench as
/// *"one cut line near the middle-bottom of the board is not straight, it's an arc"*.
///
/// **How it happened.** An offset emits a rounded corner as three vertices about 18 µm apart, and
/// then one straight run of twelve millimetres with nothing in between. Six such points — three at
/// each end — sit within 0.8 µm of a 39 mm circle, because anything nearly collinear fits a huge
/// circle. The fitter checked only those six, so the arc through them was unconstrained across the
/// whole gap, and bowed through empty space where no vertex contradicted it.
///
/// **Why 17.6° was a red herring.** All eight subtended about that, which looked like something
/// quantising. It is simply `MinimumSweepRadians = 0.3` rad = 17.19°: the fitter refuses anything
/// shallower, so every gentle curve it emits comes out just over it. The constant angle was the
/// acceptance threshold, not the fault.
/// </summary>
public sealed class ArcFittingTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every arc on the Mega's copper, after the fix, has a radius of 2.03 mm or less — it is a
    /// board of pads and corners, and its largest round feature is small. The eight bad arcs ran
    /// from 6.11 to 39.46 mm. Three millimetres sits in that gap: half again the largest honest arc
    /// measured, and half the smallest invented one.
    ///
    /// Tighter than the 5 mm the bench test plan quotes, and deliberately. Five is the number a
    /// person can check by eye against "no pad on this board is that big"; three is what the
    /// distribution actually supports, and it also catches a bow chopped into smaller arcs, which
    /// five would let through.
    ///
    /// **It is a proxy, and only sound for this board.** What the fix actually enforces is the
    /// departure of an arc from the middle of each segment it replaces — not the arc's own radius,
    /// and not its own sagitta either: a pad's perimeter is a 1 mm arc bowing a full millimetre off
    /// its chord, and that is correct. A board carrying a genuinely gentle curve of large radius
    /// would fail this assertion while the software was behaving, and the failure would be this
    /// number rather than the code. It is asserted here because it is the one an operator can also
    /// check, on the one board where the fault was found.
    /// </summary>
    private static readonly long ImplausibleRadiusNm = Nm.FromMillimetres(3);

    private static double Bow(ArtSegment arc)
    {
        var r = arc.From.DistanceTo(arc.Centre);
        var chord = arc.From.DistanceTo(arc.To);
        var sweep = 2 * Math.Asin(Math.Min(1.0, chord / (2 * r)));
        return r * (1 - Math.Cos(sweep / 2));
    }

    /// <summary>
    /// The fault in miniature: two clusters of points with a long straight segment between them,
    /// which is what an offset leaves where a rounded corner meets a straight run.
    ///
    /// **Both halves pass an explicit chord tolerance**, rather than one of them relying on the
    /// parameter's default. Leaning on the default would pin today's default in place as a
    /// requirement — a later change that applied the check everywhere by default is strictly
    /// better, and a test that fails on it would be arguing for the bug.
    /// </summary>
    [Fact]
    public void AnArcIsNotDrawnAcrossAStraightSegment()
    {
        // Three points curving into the run at each end, 12 mm apart — the real geometry, rounded.
        var points = new List<Point2>
        {
            new(Nm.FromMillimetres(0.0000), Nm.FromMillimetres(0.0000)),
            new(Nm.FromMillimetres(0.0188), Nm.FromMillimetres(0.0037)),
            new(Nm.FromMillimetres(0.0337), Nm.FromMillimetres(0.0052)),
            new(Nm.FromMillimetres(12.0337), Nm.FromMillimetres(0.0052)),
            new(Nm.FromMillimetres(12.0486), Nm.FromMillimetres(0.0037)),
            new(Nm.FromMillimetres(12.0674), Nm.FromMillimetres(0.0000)),
        };

        var tolerance = Nm.FromMillimetres(0.001);

        var unchecked_ = Simplify.FitArcs(points, tolerance, 5, long.MaxValue);
        var guarded = Simplify.FitArcs(points, tolerance, 5, Nm.FromMillimetres(0.01));

        output.WriteLine(
            $"unchecked: {unchecked_.Count} segment(s), {unchecked_.Count(s => s.IsArc)} arc(s); "
            + $"guarded: {guarded.Count} segment(s), {guarded.Count(s => s.IsArc)} arc(s)");

        // With no chord limit this is one enormous arc — the shape of the original fault, and proof
        // that the fixture still reproduces it.
        var bad = Assert.Single(unchecked_, s => s.IsArc && s.From.DistanceTo(s.Centre) > ImplausibleRadiusNm);
        output.WriteLine($"    unchecked bow: {Bow(bad) / 1000:F0} µm");
        Assert.True(Bow(bad) > Nm.FromMillimetres(0.4), "the fixture no longer reproduces the fault.");

        // With the limit, nothing spans the straight middle — and the path is still a path.
        Assert.DoesNotContain(guarded, s => s.IsArc && s.From.DistanceTo(s.Centre) > ImplausibleRadiusNm);

        // Said explicitly, because DoesNotContain is satisfied by an empty list: a fitter that
        // returned nothing at all would pass the assertion above and emit no toolpath.
        // And the two-argument form — the one a future caller reaches for without thinking — is
        // guarded too, because the bound defaults to a multiple of the tolerance rather than to off.
        Assert.DoesNotContain(
            Simplify.FitArcs(points, tolerance),
            s => s.IsArc && s.From.DistanceTo(s.Centre) > ImplausibleRadiusNm);

        Assert.NotEmpty(guarded);
        Assert.Equal(points[0], guarded[0].From);
        Assert.Equal(points[^1], guarded[^1].To);
    }

    /// <summary>
    /// A real circle is still fitted, and fitted correctly. The check must refuse invented curves
    /// without refusing the ones arc fitting exists for — a pad's perimeter is a circle, and
    /// emitting it as three hundred short moves is what this stage was written to stop.
    /// </summary>
    [Fact]
    public void ARealCircleIsStillFittedAndIsStillThatCircle()
    {
        var points = new List<Point2>();
        var radius = (double)Nm.FromMillimetres(1.0);

        for (var i = 0; i <= 180; i++)
        {
            var a = i * 2 * Math.PI / 180;
            points.Add(new Point2((long)(radius * Math.Cos(a)), (long)(radius * Math.Sin(a))));
        }

        var thinned = Simplify.DouglasPeucker(points, Nm.FromMillimetres(0.001));
        var fitted = Simplify.FitArcs(thinned, Nm.FromMillimetres(0.001), 5, Nm.FromMillimetres(0.01));

        var arcs = fitted.Where(s => s.IsArc).ToList();
        output.WriteLine($"a 1 mm circle of 181 points thins to {thinned.Count} and fits {arcs.Count} arc(s)");

        Assert.NotEmpty(arcs);
        Assert.True(
            fitted.Count < 40,
            $"the circle came back as {fitted.Count} segments, so arc fitting has stopped earning its place.");

        // And they are *this* circle. Counting arcs says nothing about whether they are the right
        // ones: three arcs of the wrong radius round the wrong centre would satisfy a count.
        foreach (var arc in arcs)
        {
            Assert.InRange(arc.From.DistanceTo(arc.Centre), Nm.FromMillimetres(0.99), Nm.FromMillimetres(1.01));
            Assert.InRange(arc.Centre.DistanceTo(new Point2(0, 0)), 0, Nm.FromMillimetres(0.01));
        }
    }

    /// <summary>
    /// The board it was found on, through the pipeline the application actually runs.
    ///
    /// **This is the only test here that goes through `PathSimplifier`**, so it is the only one that
    /// would notice the call site quietly ceasing to pass a chord tolerance. The other two hand
    /// `Simplify` the value themselves and would pass happily while the product shipped the bug.
    /// </summary>
    [Theory]
    [InlineData(LayerRole.TopCopper, 4_100)]
    [InlineData(LayerRole.BottomCopper, 2_050)]
    public void TheMegaHasNoArcSweepingAcrossStraightCopper(LayerRole role, int arcsExpected)
    {
        var layer = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoMega))
            .Layers.Single(l => l.Role == role);

        var raw = IsolationOperation.Build(
            layer.Area,
            new IsolationOptions
            {
                Tool = Tool.DefaultVBit,
                DepthNm = Nm.FromMillimetres(0.1),
                WidthNm = Nm.FromMillimetres(0.4),
            },
            layer.Label);

        var (simplified, result) = PathSimplifier.Apply(raw);

        var wild = simplified.Passes
            .SelectMany(p => p.Path)
            .Where(s => s.IsArc && s.From.DistanceTo(s.Centre) > ImplausibleRadiusNm)
            .Select(s => $"radius {s.From.DistanceTo(s.Centre) / 1e6:F2} mm bowing {Bow(s) / 1000:F0} µm "
                + $"from ({s.From.X / 1e6:F3}, {s.From.Y / 1e6:F3})")
            .ToList();

        var widest = simplified.Passes.SelectMany(p => p.Path).Where(s => s.IsArc)
            .Select(s => s.From.DistanceTo(s.Centre)).DefaultIfEmpty(0).Max();

        output.WriteLine(
            $"{role}: {result.Arcs} arcs, widest radius {widest / 1e6:F2} mm, {wild.Count} implausible");

        Assert.True(
            wild.Count == 0,
            $"{role} carries {wild.Count} arc(s) of implausible radius:\n  " + string.Join("\n  ", wild));

        // And the fix must not have paid for that by giving up simplification. The floors are within
        // about three percent of what this board actually fits — 4,245 on the top copper and 2,127
        // on the bottom — because the earlier drafts of this fix lost 11 % and 19 % of the arcs
        // while still passing anything slacker, and that is precisely the regression worth catching.
        // Close enough to bite, far enough not to flap on a Clipper version that tessellates a
        // little differently.
        Assert.True(
            result.Arcs >= arcsExpected,
            $"only {result.Arcs} arcs were fitted on the {role}, against {arcsExpected} expected: the "
            + "check is now refusing honest curves too, and the program will have grown to match.");
    }
}
