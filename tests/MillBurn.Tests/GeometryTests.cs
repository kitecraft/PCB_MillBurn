using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// The boolean layer, tested on its own before any Gerber gets near it.
///
/// Two properties matter more than any specific shape: **area is right**, because everything
/// downstream is measured in millimetres of copper, and **orientation is preserved**, because a
/// reversed ring is a hole and a hole in the wrong place is a short. Neither produces an error
/// when it goes wrong.
/// </summary>
public sealed class GeometryTests
{
    private static long Mm(double mm) => Nm.FromMillimetres(mm);

    private static Path64 Square(double cxMm, double cyMm, double sizeMm)
    {
        var h = Mm(sizeMm) / 2;
        var cx = Mm(cxMm);
        var cy = Mm(cyMm);
        return
        [
            new Point64(cx - h, cy - h),
            new Point64(cx + h, cy - h),
            new Point64(cx + h, cy + h),
            new Point64(cx - h, cy + h),
        ];
    }

    // ------------------------------------------------------------------ tessellation

    [Fact]
    public void FinerToleranceProducesMoreSegments()
    {
        var coarse = Tessellate.SegmentsForArc(Mm(5), 2 * Math.PI, Mm(0.01));
        var fine = Tessellate.SegmentsForArc(Mm(5), 2 * Math.PI, Mm(0.001));

        Assert.True(fine > coarse, $"expected finer tolerance to add segments; got {fine} vs {coarse}");
    }

    [Fact]
    public void BiggerRadiusNeedsMoreSegmentsForTheSameTolerance()
    {
        var small = Tessellate.SegmentsForArc(Mm(1), 2 * Math.PI, Mm(0.001));
        var large = Tessellate.SegmentsForArc(Mm(50), 2 * Math.PI, Mm(0.001));

        Assert.True(large > small, $"expected a larger circle to need more segments; got {large} vs {small}");
    }

    /// <summary>
    /// A polygon that *contains* the true circle, not one inscribed in it. Copper modelled slightly
    /// large puts an isolation pass slightly further from real copper; modelled slightly small puts
    /// the cutter slightly into it. Only one of those errs toward a board that works.
    /// </summary>
    [Fact]
    public void CircleContainsTheTrueCircle()
    {
        var radius = Mm(2);
        var circle = Tessellate.Circle(Point2.Origin, radius, Mm(0.001));

        // The inradius — the closest approach of any edge to the centre — must be at least the true
        // radius, and only just, or we are wasting material.
        var inradius = double.MaxValue;
        for (var i = 0; i < circle.Count; i++)
        {
            var a = circle[i];
            var b = circle[(i + 1) % circle.Count];
            inradius = Math.Min(inradius, DistanceToSegment(a, b));
        }

        Assert.True(inradius >= radius - 1, $"edge came within {inradius} of centre, inside r={radius}");
        Assert.True(inradius < radius + Mm(0.002), $"polygon is {inradius - radius} nm oversized");
    }

    /// <summary>
    /// Two conventions live side by side here, deliberately, and this pins the first:
    ///
    /// A circle *built* as an aperture is *containing* — a hair larger than the true circle, which
    /// is the safe direction for copper. A circle *flattened* from a file's arc is *inscribed* — a
    /// hair smaller — because it has no choice: it must pass through the endpoints the file
    /// stored. Both errors are far below any machine's resolution; what matters is not mistaking
    /// one for a bug in the other.
    /// </summary>
    [Fact]
    public void BuiltCircleIsAHairLargerThanTheTrueCircle()
    {
        var area = Polygons.AreaMm2(Polygons.From(Tessellate.Circle(Point2.Origin, Mm(3), Mm(0.001))));
        var exact = Math.PI * 9;

        Assert.True(area > exact, $"a containing polygon must exceed pi.r^2; got {area} vs {exact}");
        Assert.True(area < exact * 1.001, $"and only just; got {area}, {(area / exact) - 1:P4} over");
    }

    [Fact]
    public void CircleIsDeterministic()
    {
        var a = Tessellate.Circle(new Point2(1234, 5678), Mm(1.5), Mm(0.001));
        var b = Tessellate.Circle(new Point2(1234, 5678), Mm(1.5), Mm(0.001));

        Assert.Equal(a, b);
    }

    /// <summary>
    /// A flattened arc has to land exactly on its stored endpoint. In real Gerbers the radius
    /// implied by the centre and the radius implied by the endpoints differ by a nanometre or two
    /// after coordinate rounding, and following the radius instead of the endpoint leaves a gap
    /// that turns a closed region into an open one.
    /// </summary>
    [Fact]
    public void FlattenedArcsEndExactlyOnTheStoredEndpoint()
    {
        var quarter = new ArtSegment(
            ArtSweep.CounterClockwise, new Point2(Mm(10), 0), new Point2(0, Mm(10)), Point2.Origin);

        var path = Tessellate.Flatten([quarter], Mm(0.001));

        Assert.Equal(new Point64(Mm(10), 0), path[0]);
        Assert.Equal(new Point64(0, Mm(10)), path[^1]);
    }

    [Fact]
    public void AFullCircleArcFlattensToAClosedRing()
    {
        var start = new Point2(Mm(5), 0);
        var full = new ArtSegment(ArtSweep.CounterClockwise, start, start, Point2.Origin);

        var path = Tessellate.Flatten([full], Mm(0.001));
        path.RemoveAt(path.Count - 1);

        // Inscribed, not containing — a flattened arc has to hit the endpoints the file stored.
        var area = Polygons.AreaMm2(Polygons.From(path));
        var exact = Math.PI * 25;

        Assert.True(area < exact, $"a flattened arc is inscribed; got {area} vs {exact}");
        Assert.True(area > exact * 0.999, $"and only just; got {area}, {1 - (area / exact):P4} under");
    }

    // ------------------------------------------------------------------ booleans

    /// <summary>
    /// The even-odd rule is what makes a contour inside another a hole. Get it wrong and an
    /// aperture's drill hole becomes a second solid disc filling the clearance.
    /// </summary>
    [Fact]
    public void EvenOddTurnsAnInnerContourIntoAHole()
    {
        var resolved = Polygons.ResolveEvenOdd(Polygons.From(Square(0, 0, 10), Square(0, 0, 4)));

        Assert.Equal(2, resolved.Count);
        Assert.Equal(100 - 16, Polygons.AreaMm2(resolved), 6);
    }

    [Fact]
    public void HolesSubtractFromArea()
    {
        var withHole = Polygons.ResolveEvenOdd(Polygons.From(Square(0, 0, 10), Square(0, 0, 4)));

        // Positive outer, negative hole — the sign is the whole mechanism.
        Assert.True(Clipper.Area(withHole[0]) * Clipper.Area(withHole[1]) < 0,
            "outer ring and hole should wind in opposite directions");
    }

    [Fact]
    public void UnionMergesOverlapAndDifferenceRemovesIt()
    {
        var a = Polygons.From(Square(0, 0, 10));
        var b = Polygons.From(Square(5, 0, 10));

        Assert.Equal(150, Polygons.AreaMm2(Polygons.Union(a, b)), 6);
        Assert.Equal(50, Polygons.AreaMm2(Polygons.Difference(a, b)), 6);
        Assert.Equal(50, Polygons.AreaMm2(Polygons.Intersect(a, b)), 6);
    }

    [Fact]
    public void InvertingNeedsAFrameAndProducesItsComplement()
    {
        var frame = Polygons.From(Polygons.Rectangle(new Bounds(0, 0, Mm(10), Mm(10))));
        var hole = Polygons.From(Square(5, 5, 4));

        Assert.Equal(100 - 16, Polygons.AreaMm2(Polygons.Invert(hole, frame)), 6);
    }

    /// <summary>
    /// Canonical form exists so a golden hash means "the geometry changed" and never "Clipper
    /// visited the edges in a different order".
    /// </summary>
    [Fact]
    public void CanonicalFormIsIndependentOfRingOrderAndStartVertex()
    {
        var first = Polygons.From(Square(0, 0, 10), Square(30, 0, 10));

        // Same two rings, listed in the other order, each starting at a different vertex.
        var rotated = new Path64(Square(30, 0, 10).Skip(2).Concat(Square(30, 0, 10).Take(2)));
        var second = Polygons.From(rotated, new Path64(Square(0, 0, 10).Skip(1).Concat(Square(0, 0, 10).Take(1))));

        Assert.Equal(Polygons.Canonicalise(first), Polygons.Canonicalise(second));
    }

    [Fact]
    public void CanonicalFormPreservesWindingAndArea()
    {
        var withHole = Polygons.ResolveEvenOdd(Polygons.From(Square(0, 0, 10), Square(0, 0, 4)));
        var canonical = Polygons.Canonicalise(withHole);

        Assert.Equal(Polygons.AreaMm2(withHole), Polygons.AreaMm2(canonical), 9);
    }

    [Fact]
    public void BoundsAndVertexCountsDescribeTheResult()
    {
        var paths = Polygons.From(Square(0, 0, 10));

        Assert.Equal(new Bounds(Mm(-5), Mm(-5), Mm(5), Mm(5)), Polygons.BoundsOf(paths));
        Assert.Equal(4, Polygons.VertexCount(paths));
    }

    private static double DistanceToSegment(Point64 a, Point64 b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 0)
        {
            return Math.Sqrt((double)((a.X * a.X) + (a.Y * a.Y)));
        }

        var t = Math.Clamp(-(((double)a.X * dx) + ((double)a.Y * dy)) / lengthSquared, 0, 1);
        var px = a.X + (t * dx);
        var py = a.Y + (t * dy);
        return Math.Sqrt((px * px) + (py * py));
    }
}
