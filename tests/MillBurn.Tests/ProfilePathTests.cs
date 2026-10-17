using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The public walk round a cut profile (roadmap 6.4).
///
/// A tab's position is a distance along the loop, and the viewport's drag is a projection onto it:
/// the pointer lands wherever on the profile is nearest, which is what makes the gesture a
/// constrained slide — around a corner is allowed, off the outline is not possible. These tests pin
/// the two directions of that mapping against coordinates worked out by hand on a rectangle, where
/// every answer is checkable with a rule.
/// </summary>
public sealed class ProfilePathTests(ITestOutputHelper output)
{
    private static long Mm(double mm) => Nm.FromMillimetres(mm);

    /// <summary>A 20 × 10 mm rectangle from (0,0), walked anticlockwise: bottom, right, top, left.</summary>
    private static Path64 Rect() =>
    [
        new(0, 0),
        new(Mm(20), 0),
        new(Mm(20), Mm(10)),
        new(0, Mm(10)),
    ];

    [Fact]
    public void ThePerimeterIsTheWholeWayRound()
    {
        var path = new ProfilePath(Rect());

        Assert.Equal(Mm(60), path.PerimeterNm, Mm(0.001));
    }

    /// <summary>
    /// A distance along the loop lands where a rule laid against the board says it should: 25 mm
    /// from (0,0) on this rectangle is 5 mm up the right-hand side.
    /// </summary>
    [Theory]
    [InlineData(5.0, 5.0, 0.0)]
    [InlineData(25.0, 20.0, 5.0)]
    [InlineData(35.0, 15.0, 10.0)]
    [InlineData(55.0, 0.0, 5.0)]
    public void ADistanceAlongTheLoopLandsWhereItSays(double alongMm, double xMm, double yMm)
    {
        var path = new ProfilePath(Rect());
        var at = path.PointAt(Mm(alongMm));

        output.WriteLine($"{alongMm} mm -> {at}");

        Assert.Equal(0, at.DistanceTo(new Point2(Mm(xMm), Mm(yMm))), Mm(0.001));
    }

    /// <summary>
    /// The loop is closed, so any distance wraps onto it — a lap further round and a walk backwards
    /// both land where the plain distance does. This is what lets a drag cross the start vertex
    /// without the tab jumping.
    /// </summary>
    [Theory]
    [InlineData(65.0)]
    [InlineData(-55.0)]
    public void AnyDistanceWrapsOntoTheLoop(double alongMm)
    {
        var path = new ProfilePath(Rect());

        Assert.Equal(0, path.PointAt(Mm(alongMm)).DistanceTo(path.PointAt(Mm(5))), Mm(0.001));
        Assert.Equal(Mm(5), path.Wrap(Mm(alongMm)), Mm(0.001));
    }

    /// <summary>
    /// A point projects to the nearest place on the loop, with its distance said honestly.
    ///
    /// The pointer is rarely on the path: a drag holds it near the profile and the projection does
    /// the rest. A point outside the bottom edge, one inside the board, and one beyond a corner —
    /// the corner case is the one that matters, because the nearest place to a point past the end
    /// of two edges is the corner itself, which is how a slide follows the pointer round a turn.
    /// </summary>
    [Theory]
    [InlineData(5.0, -2.0, 5.0, 2.0)]
    [InlineData(5.0, 3.0, 5.0, 3.0)]
    [InlineData(22.0, -2.0, 20.0, 2.828)]
    public void APointProjectsToTheNearestPlaceOnTheLoop(
        double xMm, double yMm, double alongMm, double distanceMm)
    {
        var path = new ProfilePath(Rect());

        var (along, distance) = path.Project(new Point2(Mm(xMm), Mm(yMm)));

        output.WriteLine($"({xMm}, {yMm}) mm -> {along / 1e6:F3} mm along, {distance / 1e6:F3} mm away");

        Assert.Equal(Mm(alongMm), along, Mm(0.01));
        Assert.Equal(Mm(distanceMm), distance, Mm(0.01));
    }

    /// <summary>
    /// The two directions agree: wherever a distance lands, projecting that point comes back to the
    /// same distance. Walked at every half-millimetre of the loop, because the seams between
    /// segments are exactly where one direction could be off by a segment.
    /// </summary>
    [Fact]
    public void PointAtAndProjectRoundTrip()
    {
        var path = new ProfilePath(Rect());

        for (var along = 0.0; along < path.PerimeterNm; along += Mm(0.5))
        {
            var (back, distance) = path.Project(path.PointAt(along));

            Assert.Equal(0, distance, Mm(0.001));
            Assert.Equal(along, back, Mm(0.001));
        }
    }
}
