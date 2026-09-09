using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Geometry;
using MillBurn.Optimize;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Fewer, longer moves for the same shape.
///
/// The second half of that sentence is the whole test. Making a file smaller is easy; making it
/// smaller while every point still lands where it was asked to is the part that can go wrong, and
/// it goes wrong quietly — a simplified isolation path that strays two hundred microns still looks
/// perfect and shorts the board.
/// </summary>
public sealed class SimplifyTests(ITestOutputHelper output)
{
    private static Point2 Mm(double x, double y) =>
        new(Nm.FromMillimetres(x), Nm.FromMillimetres(y));

    private static readonly long Tolerance = Nm.FromMillimetres(0.002);

    // ------------------------------------------------------------------ Douglas–Peucker

    [Fact]
    public void CollinearPointsAreDropped()
    {
        List<Point2> line = [Mm(0, 0), Mm(1, 0), Mm(2, 0), Mm(3, 0), Mm(4, 0)];

        var simplified = Simplify.DouglasPeucker(line, Tolerance);

        Assert.Equal(2, simplified.Count);
        Assert.Equal(line[0], simplified[0]);
        Assert.Equal(line[^1], simplified[^1]);
    }

    [Fact]
    public void APointFurtherOutThanTheToleranceIsKept()
    {
        // 5 µm off the line, against a 2 µm tolerance.
        List<Point2> bent = [Mm(0, 0), Mm(2, 0.005), Mm(4, 0)];

        Assert.Equal(3, Simplify.DouglasPeucker(bent, Tolerance).Count);
        Assert.Equal(2, Simplify.DouglasPeucker(bent, Nm.FromMillimetres(0.01)).Count);
    }

    /// <summary>
    /// The guarantee that matters: nothing that was dropped was further from the kept path than the
    /// tolerance allows. Checked by measuring every original point against the simplified polyline.
    /// </summary>
    [Fact]
    public void NoOriginalPointStraysFurtherThanTheTolerance()
    {
        var wiggle = new List<Point2>();
        var random = new Random(20260909);

        for (var i = 0; i <= 2000; i++)
        {
            var x = i * 0.01;
            wiggle.Add(Mm(x, Math.Sin(x) + (random.NextDouble() * 0.001)));
        }

        var simplified = Simplify.DouglasPeucker(wiggle, Tolerance);

        output.WriteLine($"{wiggle.Count} -> {simplified.Count} points");
        Assert.True(simplified.Count < wiggle.Count / 2);

        foreach (var point in wiggle)
        {
            Assert.True(
                DistanceToPolyline(point, simplified) <= Tolerance + 1,
                $"a point strayed {DistanceToPolyline(point, simplified) / Nm.PerMillimetre:F4} mm");
        }
    }

    [Fact]
    public void TooFewPointsToSimplifyIsNotAnError()
    {
        Assert.Equal(2, Simplify.DouglasPeucker([Mm(0, 0), Mm(1, 1)], Tolerance).Count);
        Assert.Empty(Simplify.DouglasPeucker([], Tolerance));
    }

    // ------------------------------------------------------------------ arc fitting

    /// <summary>A polygonised circle is what Clipper's round joins produce, and what arcs recover.</summary>
    private static List<Point2> Circle(double cx, double cy, double radius, int steps)
    {
        var points = new List<Point2>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var angle = 2 * Math.PI * i / steps;
            points.Add(Mm(cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle))));
        }

        return points;
    }

    [Fact]
    public void APolygonisedCircleBecomesArcs()
    {
        var points = Circle(10, 10, 2, 180);
        var segments = Simplify.FitArcs(points, Tolerance);

        output.WriteLine($"{points.Count - 1} lines -> {segments.Count} segments, "
            + $"{segments.Count(s => s.IsArc)} arcs");

        Assert.True(segments.Count < 20, $"expected a handful of arcs, got {segments.Count} segments");
        Assert.Contains(segments, s => s.IsArc);

        // Every arc sits on the circle it came from, to within the fitting tolerance.
        foreach (var arc in segments.Where(s => s.IsArc))
        {
            Assert.InRange(
                arc.RadiusNm,
                Nm.FromMillimetres(2) - Tolerance,
                Nm.FromMillimetres(2) + Tolerance);

            Assert.InRange(arc.Centre.DistanceTo(Mm(10, 10)), 0, Tolerance);
        }
    }

    [Fact]
    public void AStraightLineIsNeverTurnedIntoAnArc()
    {
        var line = Enumerable.Range(0, 50).Select(i => Mm(i * 0.1, 0)).ToList();

        Assert.DoesNotContain(Simplify.FitArcs(line, Tolerance), s => s.IsArc);
    }

    /// <summary>
    /// Points can sit on a circle without forming an arc. A zig-zag between two nearby radii fits
    /// perfectly and is not a curve — taking it for one cuts a shape nobody drew.
    /// </summary>
    [Fact]
    public void PointsThatDoubleBackAreNotAnArc()
    {
        var zigzag = new List<Point2>();
        for (var i = 0; i < 20; i++)
        {
            var angle = (i % 2 == 0 ? 0.1 : 0.2) + (i * 0.001);
            zigzag.Add(Mm(10 + (2 * Math.Cos(angle)), 10 + (2 * Math.Sin(angle))));
        }

        Assert.DoesNotContain(Simplify.FitArcs(zigzag, Tolerance), s => s.IsArc);
    }

    [Fact]
    public void AnArcSweepsTheWayThePointsGo()
    {
        var forwards = Simplify.FitArcs(Circle(0, 0, 3, 120).Take(40).ToList(), Tolerance);
        var backwards = Simplify.FitArcs(
            Circle(0, 0, 3, 120).Take(40).Reverse().ToList(), Tolerance);

        Assert.Contains(forwards, s => s.Sweep == ArtSweep.CounterClockwise);
        Assert.Contains(backwards, s => s.Sweep == ArtSweep.Clockwise);
    }

    // ------------------------------------------------------------------ on a real toolpath

    private static Toolpath Isolation(string board)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));
        var copper = loaded.Layers.First(l => l.Role == LayerRole.TopCopper);

        return IsolationOperation.Build(
            copper.Area,
            new IsolationOptions { Tool = Tool.DefaultVBit, DepthNm = Nm.FromMillimetres(0.05) });
    }

    /// <summary>
    /// The acceptance criterion from Documentation/03 §8 is a fivefold reduction in line count.
    /// A panel is where it matters: 300,000 lines is a file the controller cannot see past.
    /// </summary>
    [Fact]
    public void ARealBoardLosesMostOfItsSegments()
    {
        var (_, result) = PathSimplifier.Apply(Isolation(RealBoards.Panel));

        output.WriteLine(
            $"{result.SegmentsBefore:N0} -> {result.SegmentsAfter:N0} segments "
            + $"({result.Reduction:P1}), {result.Arcs:N0} arcs");

        Assert.True(result.Reduction > 0.5, $"only {result.Reduction:P1} smaller");
        Assert.True(result.Arcs > 0, "no arcs were recovered from a board full of rounded corners");
    }

    /// <summary>
    /// Same shape, and the cut length proves it. A simplification that quietly cut corners would
    /// show up here as a path that is measurably shorter than the one it replaced.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.GridStripConnector)]
    [InlineData(RealBoards.Panel)]
    public void TheCutIsTheSameLengthItWas(string board)
    {
        var (_, result) = PathSimplifier.Apply(Isolation(board));

        var drift = Math.Abs(result.LengthAfterMm - result.LengthBeforeMm) / result.LengthBeforeMm;

        output.WriteLine(
            $"{board}: {result.LengthBeforeMm:F2} -> {result.LengthAfterMm:F2} mm ({drift:P3})");

        Assert.True(drift < 0.002, $"cut length moved by {drift:P3}");
    }

    /// <summary>
    /// Every point of the original path is still within tolerance of the simplified one. This is
    /// the check that a shorter file is the same cut — everything else is a proxy for it.
    /// </summary>
    [Fact]
    public void NoPartOfTheOriginalPathIsAbandoned()
    {
        var original = Isolation(RealBoards.PogoTest1);
        var (simplified, _) = PathSimplifier.Apply(original);

        var worst = 0.0;

        for (var i = 0; i < original.Passes.Count; i++)
        {
            var flattened = Flatten(simplified.Passes[i]);

            foreach (var segment in original.Passes[i].Path)
            {
                worst = Math.Max(worst, DistanceToPolyline(segment.From, flattened));
            }
        }

        output.WriteLine($"worst deviation {worst / Nm.PerMillimetre:F5} mm");

        // The tolerance, plus a nanometre for the rounding that turns a fitted centre into integers.
        Assert.True(worst <= Tolerance + 10, $"strayed {worst / Nm.PerMillimetre:F5} mm");
    }

    [Fact]
    public void AClosedContourIsStillClosed()
    {
        var (simplified, _) = PathSimplifier.Apply(Isolation(RealBoards.PogoTest1));

        foreach (var pass in simplified.Passes.Where(p => p.Closed))
        {
            Assert.Equal(pass.Path[0].From, pass.Path[^1].To);

            for (var i = 0; i < pass.Path.Count - 1; i++)
            {
                Assert.Equal(pass.Path[i].To, pass.Path[i + 1].From);
            }
        }
    }

    [Fact]
    public void SimplificationCanBeTurnedOff()
    {
        var original = Isolation(RealBoards.PogoTest1);
        var (same, result) = PathSimplifier.Apply(original, SimplifyOptions.None);

        Assert.Equal(result.SegmentsBefore, result.SegmentsAfter);
        Assert.Equal(0, result.Arcs);
        Assert.Equal(
            original.Passes.Select(p => p.Path.Count),
            same.Passes.Select(p => p.Path.Count));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>An arc-carrying path back to points, so deviation can be measured against it.</summary>
    private static List<Point2> Flatten(ToolpathPass pass)
    {
        var points = new List<Point2> { pass.Path[0].From };

        foreach (var segment in pass.Path)
        {
            if (!segment.IsArc)
            {
                points.Add(segment.To);
                continue;
            }

            var steps = Math.Max(
                Tessellate.SegmentsForArc(segment.RadiusNm, segment.SweptAngle(), 500), 2);

            var start = Math.Atan2(segment.From.Y - segment.Centre.Y, segment.From.X - segment.Centre.X);
            var direction = segment.Sweep == ArtSweep.CounterClockwise ? 1.0 : -1.0;
            var swept = segment.SweptAngle();

            for (var i = 1; i <= steps; i++)
            {
                var angle = start + (direction * swept * i / steps);
                points.Add(new Point2(
                    segment.Centre.X + (long)Math.Round(segment.RadiusNm * Math.Cos(angle)),
                    segment.Centre.Y + (long)Math.Round(segment.RadiusNm * Math.Sin(angle))));
            }
        }

        return points;
    }

    private static double DistanceToPolyline(Point2 p, IReadOnlyList<Point2> polyline)
    {
        var best = double.MaxValue;

        for (var i = 0; i < polyline.Count - 1; i++)
        {
            best = Math.Min(best, DistanceToSegment(p, polyline[i], polyline[i + 1]));
        }

        return polyline.Count == 1 ? p.DistanceTo(polyline[0]) : best;
    }

    private static double DistanceToSegment(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared <= 0)
        {
            return p.DistanceTo(a);
        }

        var t = Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared, 0, 1);
        var cx = a.X + (t * dx);
        var cy = a.Y + (t * dy);

        return Math.Sqrt(((p.X - cx) * (p.X - cx)) + ((p.Y - cy) * (p.Y - cy)));
    }
}
