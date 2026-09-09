using MillBurn.Core;

namespace MillBurn.Geometry;

/// <summary>
/// Fewer, longer moves for the same shape.
///
/// This is the change that makes a program *run* faster rather than merely travel less
/// (Documentation/03, section 7.3). A controller decelerates into every block it cannot see past:
/// an isolation contour delivered as three thousand one-micron segments never reaches its
/// programmed feed at all, so the machine crawls through geometry it could have taken at speed.
///
/// Two steps, in order. Douglas–Peucker throws away points that say nothing — a straight run
/// arriving as forty collinear points is forty blocks for one move. Arc fitting then recovers the
/// curves: Clipper rounds every corner into a fan of tiny segments, and those fans are exactly what
/// <c>G2</c>/<c>G3</c> exists to express.
///
/// Both are bounded by a tolerance in nanometres and neither may exceed it, so the worst case is
/// known rather than hoped for.
/// </summary>
public static class Simplify
{
    /// <summary>
    /// Removes points that lie within <paramref name="toleranceNm"/> of the line they sit on.
    ///
    /// Iterative rather than recursive: a contour can carry tens of thousands of points, and a
    /// recursion that deep is a stack overflow on a real board rather than a theoretical one.
    /// </summary>
    public static IReadOnlyList<Point2> DouglasPeucker(IReadOnlyList<Point2> points, long toleranceNm)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 3 || toleranceNm <= 0)
        {
            return points;
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        var pending = new Stack<(int From, int To)>();
        pending.Push((0, points.Count - 1));

        while (pending.Count > 0)
        {
            var (from, to) = pending.Pop();
            if (to <= from + 1)
            {
                continue;
            }

            var worst = 0.0;
            var at = -1;

            for (var i = from + 1; i < to; i++)
            {
                var distance = DistanceToSegment(points[i], points[from], points[to]);
                if (distance > worst)
                {
                    worst = distance;
                    at = i;
                }
            }

            if (at < 0 || worst <= toleranceNm)
            {
                continue;
            }

            keep[at] = true;
            pending.Push((from, at));
            pending.Push((at, to));
        }

        var result = new List<Point2>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                result.Add(points[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Perpendicular distance from a point to a segment, in nanometres.
    ///
    /// Doubles throughout: the cross product of two board-sized vectors in nanometres overflows a
    /// 64-bit integer at around 3 metres, which a panel is not but a mistake here would be silent.
    /// </summary>
    private static double DistanceToSegment(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared <= 0)
        {
            return p.DistanceTo(a);
        }

        var t = (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        var cx = a.X + (t * dx);
        var cy = a.Y + (t * dy);

        return Math.Sqrt(((p.X - cx) * (p.X - cx)) + ((p.Y - cy) * (p.Y - cy)));
    }

    /// <summary>
    /// Replaces runs of points that lie on a common circle with a single arc.
    ///
    /// Greedy and forward-only: start from each point, extend while everything still sits within
    /// tolerance of one circle, and take the longest run found. A run has to be worth it — enough
    /// points and enough sweep — because turning three nearly-collinear points into an arc of
    /// enormous radius is a worse instruction than the line it replaced, and some controllers
    /// reject it outright.
    ///
    /// <paramref name="toleranceNm"/> is this stage's share of the budget, not the whole of it. The
    /// two stages compose: an arc within its tolerance of a point that was itself within tolerance
    /// of the original can be twice as far from the original as either number suggests. Splitting
    /// the budget is what makes the stated bound the one that actually holds.
    /// </summary>
    public static IReadOnlyList<ArtSegment> FitArcs(
        IReadOnlyList<Point2> points, long toleranceNm, int minimumPoints = 5)
    {
        ArgumentNullException.ThrowIfNull(points);

        var segments = new List<ArtSegment>();
        if (points.Count < 2)
        {
            return segments;
        }

        var i = 0;
        while (i < points.Count - 1)
        {
            var end = LongestArcFrom(points, i, toleranceNm, minimumPoints, out var centre, out var clockwise);

            if (end > i)
            {
                segments.Add(new ArtSegment(
                    clockwise ? ArtSweep.Clockwise : ArtSweep.CounterClockwise,
                    points[i],
                    points[end],
                    centre));

                i = end;
                continue;
            }

            segments.Add(ArtSegment.Line(points[i], points[i + 1]));
            i++;
        }

        return segments;
    }

    /// <summary>
    /// How far an arc starting at <paramref name="start"/> can reach. Returns the start index when
    /// no arc is worth making.
    /// </summary>
    private static int LongestArcFrom(
        IReadOnlyList<Point2> points, int start, long toleranceNm, int minimumPoints,
        out Point2 centre, out bool clockwise)
    {
        centre = default;
        clockwise = false;

        var best = start;

        for (var end = start + minimumPoints - 1; end < points.Count; end++)
        {
            var middle = points[start + ((end - start) / 2)];
            if (!Circumcentre(points[start], middle, points[end], out var c, out var radius))
            {
                break;
            }

            // A radius far larger than the board is a straight line pretending to be a curve.
            if (radius > MaxArcRadiusNm)
            {
                break;
            }

            var fits = true;
            for (var k = start; k <= end; k++)
            {
                if (Math.Abs(points[k].DistanceTo(c) - radius) > toleranceNm)
                {
                    fits = false;
                    break;
                }
            }

            if (!fits)
            {
                break;
            }

            // Every step must turn the same way, or the "arc" doubles back through its own centre.
            if (!ConsistentSweep(points, start, end, c, out var sweep))
            {
                break;
            }

            // Too little sweep is not a reason to stop looking — it is a reason to keep extending.
            // Breaking here meant an arc was only ever found when the first few points already
            // spanned enough angle, which on a finely tessellated circle is never: 180 steps around
            // a 2 mm circle covers eight degrees in five points, and the fitter walked away from a
            // perfect circle having fitted nothing.
            if (Math.Abs(sweep) < MinimumSweepRadians)
            {
                continue;
            }

            best = end;
            centre = c;
            clockwise = sweep < 0;
        }

        return best;
    }

    /// <summary>A radius past this is a line. Ten metres is far larger than any board.</summary>
    private const double MaxArcRadiusNm = 1e10;

    /// <summary>
    /// Whether the run sweeps consistently around the centre, and which way.
    ///
    /// Points can sit on a circle and still not form an arc — a zig-zag between two nearby radii
    /// fits perfectly and is not a curve. Checking that the angle only ever moves one way is what
    /// separates the two.
    /// </summary>
    private static bool ConsistentSweep(
        IReadOnlyList<Point2> points, int start, int end, Point2 centre, out double sweep)
    {
        sweep = 0;
        var total = 0.0;

        for (var k = start; k < end; k++)
        {
            var a = Math.Atan2(points[k].Y - centre.Y, points[k].X - centre.X);
            var b = Math.Atan2(points[k + 1].Y - centre.Y, points[k + 1].X - centre.X);

            var step = b - a;
            while (step > Math.PI)
            {
                step -= 2 * Math.PI;
            }

            while (step < -Math.PI)
            {
                step += 2 * Math.PI;
            }

            if (total != 0 && Math.Sign(step) != Math.Sign(total) && step != 0)
            {
                return false;
            }

            total += step;
        }

        sweep = total;
        return true;
    }

    /// <summary>
    /// About 17 degrees. Below this an arc buys nothing over the chord, and a shallow arc fitted
    /// through nearly-straight points has a radius so large that rounding its centre to nanometres
    /// moves the curve more than the tolerance allows.
    /// </summary>
    private const double MinimumSweepRadians = 0.3;

    /// <summary>The centre and radius of the circle through three points, or false if collinear.</summary>
    private static bool Circumcentre(Point2 a, Point2 b, Point2 c, out Point2 centre, out double radius)
    {
        centre = default;
        radius = 0;

        double ax = a.X, ay = a.Y, bx = b.X, by = b.Y, cx = c.X, cy = c.Y;

        var d = 2 * ((ax * (by - cy)) + (bx * (cy - ay)) + (cx * (ay - by)));
        if (Math.Abs(d) < 1e-6)
        {
            return false;
        }

        var aSquared = (ax * ax) + (ay * ay);
        var bSquared = (bx * bx) + (by * by);
        var cSquared = (cx * cx) + (cy * cy);

        var ux = ((aSquared * (by - cy)) + (bSquared * (cy - ay)) + (cSquared * (ay - by))) / d;
        var uy = ((aSquared * (cx - bx)) + (bSquared * (ax - cx)) + (cSquared * (bx - ax))) / d;

        if (!double.IsFinite(ux) || !double.IsFinite(uy)
            || Math.Abs(ux) > MaxArcRadiusNm || Math.Abs(uy) > MaxArcRadiusNm)
        {
            return false;
        }

        centre = new Point2((long)Math.Round(ux), (long)Math.Round(uy));
        radius = centre.DistanceTo(a);

        return radius > 0;
    }
}
