using MillBurn.Core;

namespace MillBurn.Tests;

/// <summary>
/// The motion model, checked against closed-form answers rather than against itself.
///
/// This is the cost the optimizer minimises, so being plausible is not enough: if it is wrong, the
/// optimizer confidently produces the wrong order and the estimate that says so is wrong in the
/// same direction.
/// </summary>
public sealed class MotionPlannerTests
{
    private static readonly MachineProfile Machine = new()
    {
        RapidMmPerMin = 2400,       // 40 mm/s
        AccelerationMmPerSecondSquared = 200,
        JunctionDeviationMm = 0.01,
        MinimumJunctionMmPerMin = 60,
    };

    // ------------------------------------------------------------------ single moves

    /// <summary>
    /// Long enough to reach full speed: accelerate, cruise, decelerate. Total time is the cruise
    /// time plus one whole ramp, because the acceleration and deceleration halves together cost
    /// exactly the same as one full ramp at speed.
    /// </summary>
    [Fact]
    public void ALongRapidReachesFullSpeed()
    {
        // v=40, a=200 -> ramp takes 0.2 s and covers 4 mm; both ramps cover 8 mm.
        // 100 mm: 92 mm cruising at 40 = 2.3 s, plus 0.4 s of ramping.
        Assert.Equal(2.7, MotionPlanner.RapidSeconds(100, Machine), 6);
    }

    /// <summary>
    /// The case that makes a distance-based optimizer wrong: below the distance needed to reach
    /// full speed, time goes as the square root, so a move half as long is nowhere near half as
    /// quick.
    /// </summary>
    [Fact]
    public void AShortRapidNeverReachesFullSpeedAndCostsDisproportionately()
    {
        // 2*sqrt(d/a) for anything under 8 mm.
        Assert.Equal(2 * Math.Sqrt(4.0 / 200), MotionPlanner.RapidSeconds(4, Machine), 9);
        Assert.Equal(2 * Math.Sqrt(1.0 / 200), MotionPlanner.RapidSeconds(1, Machine), 9);

        // Quartering the distance does not quarter the time — it halves it.
        var quarter = MotionPlanner.RapidSeconds(1, Machine);
        var whole = MotionPlanner.RapidSeconds(4, Machine);
        Assert.Equal(0.5, quarter / whole, 6);
    }

    [Fact]
    public void TheTwoFormulaeAgreeAtTheCrossover()
    {
        // At exactly v²/a = 8 mm the triangle just touches full speed.
        var below = MotionPlanner.RapidSeconds(8 - 1e-9, Machine);
        var above = MotionPlanner.RapidSeconds(8 + 1e-9, Machine);

        Assert.Equal(below, above, 6);
        Assert.Equal(2 * 40.0 / 200, below, 6);
    }

    [Fact]
    public void NoDistanceIsNoTime()
    {
        Assert.Equal(0, MotionPlanner.RapidSeconds(0, Machine));
        Assert.Equal(0, MotionPlanner.RapidSeconds(-5, Machine));
    }

    /// <summary>Z is its own, slower axis; costing a lift at the XY rapid understates it.</summary>
    [Fact]
    public void ZUsesItsOwnRapid()
    {
        var machine = Machine with { ZRapidMmPerMin = 600 };

        Assert.True(MotionPlanner.ZSeconds(5, machine) > MotionPlanner.RapidSeconds(5, machine));
    }

    // ------------------------------------------------------------------ continuous paths

    private static IReadOnlyList<Point2> Line(params (double X, double Y)[] mm) =>
        [.. mm.Select(p => new Point2(Nm.FromMillimetres(p.X), Nm.FromMillimetres(p.Y)))];

    /// <summary>
    /// A straight line broken into pieces is still a straight line. If the planner slowed at those
    /// invented corners, every flattened arc in the job would be over-costed.
    /// </summary>
    [Fact]
    public void SplittingAStraightMoveChangesNothing()
    {
        var whole = MotionPlanner.PathSeconds(Line((0, 0), (100, 0)), 40, Machine);
        var split = MotionPlanner.PathSeconds(
            Line((0, 0), (25, 0), (50, 0), (75, 0), (100, 0)), 40, Machine);

        Assert.Equal(whole, split, 6);
    }

    /// <summary>A single uninterrupted move is the same whether costed as a path or as a rapid.</summary>
    [Fact]
    public void APathOfOneSegmentMatchesTheClosedForm()
    {
        Assert.Equal(
            MotionPlanner.SecondsAtRest(100, 40, 200),
            MotionPlanner.PathSeconds(Line((0, 0), (100, 0)), 40, Machine),
            6);
    }

    /// <summary>
    /// A right angle costs time; a gentle bend costs almost none. This is the whole reason a single
    /// estimate can be honest — the corner speed comes from the machine's own deviation setting
    /// rather than from a guess about how much a controller "usually" slows down.
    /// </summary>
    [Fact]
    public void SharperCornersCostMore()
    {
        var straight = MotionPlanner.PathSeconds(Line((0, 0), (50, 0), (100, 0)), 40, Machine);
        var gentle = MotionPlanner.PathSeconds(Line((0, 0), (50, 0), (100, 5)), 40, Machine);
        var right = MotionPlanner.PathSeconds(Line((0, 0), (50, 0), (50, 50)), 40, Machine);
        var reversal = MotionPlanner.PathSeconds(Line((0, 0), (50, 0), (0, 0)), 40, Machine);

        Assert.True(gentle > straight, "a bend should cost more than a straight line");
        Assert.True(right > gentle, "a right angle should cost more than a gentle bend");
        Assert.True(reversal > right, "a full reversal should cost the most");
    }

    /// <summary>
    /// The estimate has to sit between the two bounds it replaces, or one of the three is wrong.
    /// A thousand short segments is exactly where the old range was uselessly wide.
    /// </summary>
    [Fact]
    public void TheEstimateSitsBetweenTheBoundsItReplaces()
    {
        var points = new List<Point2>();
        for (var i = 0; i <= 1000; i++)
        {
            points.Add(new Point2(Nm.FromMillimetres(i * 0.1), 0));
        }

        var total = 100.0;
        var optimistic = total / 40.0;
        var pessimistic = 1000 * MotionPlanner.SecondsAtRest(0.1, 40, 200);
        var actual = MotionPlanner.PathSeconds(points, 40, Machine);

        Assert.InRange(actual, optimistic, pessimistic);

        // And it is much nearer the optimistic end, because a straight run of short segments is
        // exactly what look-ahead exists to carry speed through.
        Assert.True(actual < optimistic * 1.2);
    }

    [Fact]
    public void AZeroLengthHopDoesNotInventACorner()
    {
        var withHop = MotionPlanner.PathSeconds(Line((0, 0), (50, 0), (50, 0), (100, 0)), 40, Machine);
        var without = MotionPlanner.PathSeconds(Line((0, 0), (100, 0)), 40, Machine);

        Assert.Equal(without, withHop, 6);
    }

    [Fact]
    public void FeedIsNeverExceeded()
    {
        var seconds = MotionPlanner.PathSeconds(Line((0, 0), (1000, 0)), 10, Machine);

        Assert.True(seconds >= 1000.0 / 10.0);
    }

    /// <summary>The infinite-acceleration profile is the pure distance-over-feed lower bound.</summary>
    [Fact]
    public void AnInstantMachineIsJustDistanceOverFeed()
    {
        Assert.Equal(
            2.5, MotionPlanner.PathSeconds(Line((0, 0), (50, 0), (100, 0)), 40, MachineProfile.Instant), 9);

        Assert.Equal(2.5, MotionPlanner.RapidSeconds(100, MachineProfile.Instant with { RapidMmPerMin = 2400 }), 9);
    }
}
