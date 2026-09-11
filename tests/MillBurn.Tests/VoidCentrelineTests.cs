using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Geometry;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// One pass down a channel, rather than a lap around the sliver left when it is offset inward.
///
/// A channel the width of the cutter, offset inward by the cutter's radius, leaves a ribbon a
/// tenth of a millimetre across. Running round that sends the cutter out along one side and back
/// along the other a hair away, and the return pass is air: on a 66-up panel with fifty channels,
/// 20,996 mm of cutting where 13,222 mm does the same work.
///
/// Everything here is about the ways that can go wrong rather than the happy path, because a
/// channel that is nearly cut is a panel that does not come apart — and that is found on the
/// machine, an hour in.
/// </summary>
public sealed class VoidCentrelineTests
{
    private const long Cutter = 1_000_000;

    private static Path64 Rect(double x, double y, double w, double h) =>
    [
        new(Nm.FromMillimetres(x), Nm.FromMillimetres(y)),
        new(Nm.FromMillimetres(x + w), Nm.FromMillimetres(y)),
        new(Nm.FromMillimetres(x + w), Nm.FromMillimetres(y + h)),
        new(Nm.FromMillimetres(x), Nm.FromMillimetres(y + h)),
    ];

    /// <summary>
    /// A channel as KiCad draws one: a plus, and 1.1 mm wide rather than 1.0 because the profile is
    /// the outside of the pen the outline was drawn with.
    /// </summary>
    private static Paths64 Plus() => Clipper.Union(
        [Rect(10, 19.45, 80, 1.1), Rect(49.45, 5, 1.1, 30)], FillRule.NonZero);

    private static Paths64 Inward(Paths64 channel, long diameter) => Clipper.InflatePaths(
        channel, -diameter / 2, JoinType.Round, EndType.Polygon,
        arcTolerance: Tessellate.DefaultSagittaNm);

    private static double LengthMm(Paths64 paths)
    {
        var total = 0.0;

        foreach (var path in paths)
        {
            for (var i = 1; i < path.Count; i++)
            {
                double dx = path[i].X - path[i - 1].X;
                double dy = path[i].Y - path[i - 1].Y;
                total += Math.Sqrt((dx * dx) + (dy * dy));
            }
        }

        return total / Nm.PerMillimetre;
    }

    [Fact]
    public void AChannelTheWidthOfTheCutterBecomesOnePass()
    {
        var loop = Inward(Plus(), Cutter);
        var axis = VoidCentreline.For(loop, Cutter, Tessellate.DefaultSagittaNm);

        Assert.NotNull(axis);
        Assert.Single(axis);
    }

    /// <summary>
    /// The check that matters: sweeping the cutter along what comes back must leave none of the
    /// ribbon behind. The ribbon is the void offset inward by the radius — exactly the places the
    /// cutter was meant to visit — so anything left there is channel that stays joined.
    /// </summary>
    [Fact]
    public void NothingTheCutterWasMeantToReachIsLeft()
    {
        var loop = Inward(Plus(), Cutter);
        var axis = VoidCentreline.For(loop, Cutter, Tessellate.DefaultSagittaNm);

        Assert.NotNull(axis);

        var swept = Clipper.InflatePaths(
            axis, Cutter / 2, JoinType.Round, EndType.Round,
            arcTolerance: Tessellate.DefaultSagittaNm);

        var left = Math.Abs(Clipper.Area(Clipper.Difference(loop, swept, FillRule.NonZero)))
            / (double)Nm.PerMillimetre / Nm.PerMillimetre;

        Assert.True(left < 0.01, $"{left:F4} mm² of the channel would be left uncut");
    }

    /// <summary>
    /// Every arm, to its end. The fold stops short at a tip — a cap is narrower than the step the
    /// boundary is resampled at — and half a millimetre of arm going uncut four times per cell is
    /// invisible in a preview and obvious on the stock.
    /// </summary>
    [Fact]
    public void EveryArmIsWalkedToItsEnd()
    {
        var loop = Inward(Plus(), Cutter);
        var axis = VoidCentreline.For(loop, Cutter, Tessellate.DefaultSagittaNm);

        Assert.NotNull(axis);

        var ribbon = Clipper.GetBounds(loop);
        var walked = Clipper.GetBounds(axis);

        // Within one resampling step of the ribbon's own ends, in all four directions.
        var slack = Nm.FromMillimetres(0.2);

        Assert.True(walked.left - ribbon.left < slack, "the left arm stops short");
        Assert.True(ribbon.right - walked.right < slack, "the right arm stops short");
        Assert.True(walked.top - ribbon.top < slack, "one vertical arm stops short");
        Assert.True(ribbon.bottom - walked.bottom < slack, "the other vertical arm stops short");
    }

    /// <summary>
    /// And it is actually shorter, which is the entire point. A tree has to be retraced except
    /// along one path through it, so the saving is the longest arm-to-arm run — here the 80 mm
    /// horizontal — off a lap that walks everything twice.
    /// </summary>
    [Fact]
    public void OnePassBeatsTheLap()
    {
        var loop = Inward(Plus(), Cutter);
        var axis = VoidCentreline.For(loop, Cutter, Tessellate.DefaultSagittaNm);

        Assert.NotNull(axis);

        // LengthMm walks a path without closing it, so the lap is measured a segment short. That
        // only makes this assertion harder to pass.
        var lap = LengthMm(loop);
        var middle = LengthMm(axis);

        Assert.True(middle < lap * 0.75, $"one pass is {middle:F0} mm against a lap's {lap:F0} mm");
    }

    /// <summary>
    /// A channel the cutter does not span keeps both of its sides. One pass down the middle of a
    /// 3 mm channel with a 1 mm cutter leaves a millimetre standing either side, which is the
    /// board coming out oversize rather than the channel being cut.
    /// </summary>
    [Fact]
    public void AChannelWiderThanTheCutterIsLeftAlone()
    {
        Paths64 wide = [Rect(10, 18.5, 80, 3.0)];

        Assert.Null(VoidCentreline.For(Inward(wide, Cutter), Cutter, Tessellate.DefaultSagittaNm));
    }

    /// <summary>Nothing to walk is not a crash.</summary>
    [Fact]
    public void AnEmptyLoopIsNotACrash() =>
        Assert.Null(VoidCentreline.For([], Cutter, Tessellate.DefaultSagittaNm));

    /// <summary>
    /// End to end: a panel whose channel is the width of the cutter comes out as open passes, and
    /// the board profile around them stays a closed lap.
    /// </summary>
    [Fact]
    public void ThePanelsChannelsBecomeOpenPasses()
    {
        Paths64 panel = [Rect(0, 0, 100, 40), .. Plus()];
        Paths64 copper = [Rect(12, 8, 30, 10), Rect(58, 8, 30, 10)];

        var toolpath = OutlineOperation.Build(
            panel,
            new OutlineOptions
            {
                Tool = Tool.DefaultOutlineMill,
                BoardThicknessNm = Nm.FromMillimetres(1.6),
                TabCount = 0,
                Keep = copper,
            });

        Assert.Contains(toolpath.Passes, p => !p.Closed);
        Assert.Contains(toolpath.Passes, p => p.Closed);
        Assert.Contains(toolpath.Notes, n => n.Contains("down its middle", StringComparison.Ordinal));
    }

    /// <summary>
    /// Open passes alternate direction as they go deeper.
    ///
    /// A closed contour ends where it began, so repeating it costs nothing; an open one ends at the
    /// far end of itself, and always taking it the same way round means travelling its whole length
    /// back before the next pass — 7 m of it on the panel, which is most of what one pass down the
    /// middle had just saved.
    /// </summary>
    [Fact]
    public void DeeperPassesTurnRoundRatherThanDriveBack()
    {
        Paths64 panel = [Rect(0, 0, 100, 40), .. Plus()];
        Paths64 copper = [Rect(12, 8, 30, 10), Rect(58, 8, 30, 10)];

        var toolpath = OutlineOperation.Build(
            panel,
            new OutlineOptions
            {
                Tool = Tool.DefaultOutlineMill,
                BoardThicknessNm = Nm.FromMillimetres(1.6),
                TabCount = 0,
                Keep = copper,
            });

        var open = toolpath.Passes.Where(p => !p.Closed).GroupBy(p => p.Stack).First().ToList();

        Assert.True(open.Count > 1, "there should be a pass per depth");

        for (var i = 1; i < open.Count; i++)
        {
            Assert.Equal(open[i - 1].Path[^1].To, open[i].Path[0].From);
        }
    }
}
