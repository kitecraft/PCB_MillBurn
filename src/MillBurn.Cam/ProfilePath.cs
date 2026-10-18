using Clipper2Lib;
using MillBurn.Core;

namespace MillBurn.Cam;

/// <summary>
/// One closed cut profile, measured along itself — the walk the planner uses, made public (6.4).
///
/// A tab's position *is* "how far along the loop": the planner places by arc length, the splitter
/// cuts runs by arc length, and a hand moving a tab in the viewport is choosing an arc length too.
/// This is the one copy of that arithmetic. <see cref="Project"/> turns a pointer into the nearest
/// place on the loop — which is what makes a drag a constrained slide along the profile rather
/// than a free move: around a corner is allowed, off the outline is not possible.
/// </summary>
public sealed class ProfilePath
{
    private readonly IReadOnlyList<ArtSegment> _segments;
    private readonly double[] _starts;
    private readonly double[] _lengths;

    /// <summary>Where each segment ends: <c>_starts[i] + _lengths[i]</c>, kept for the search in <see cref="At"/>.</summary>
    private readonly double[] _ends;

    /// <summary>The loop's segments, for the planner's edge finding and the splitter's runs.</summary>
    internal IReadOnlyList<ArtSegment> Segments => _segments;

    /// <summary>Where each segment starts, as a distance along the loop.</summary>
    internal double[] Starts => _starts;

    /// <summary>Each segment's length.</summary>
    internal double[] Lengths => _lengths;

    public ProfilePath(Path64 contour)
    {
        ArgumentNullException.ThrowIfNull(contour);

        _segments = IsolationOperation.ToSegments(contour);
        _starts = new double[_segments.Count];
        _lengths = new double[_segments.Count];

        for (var i = 0; i < _segments.Count; i++)
        {
            _starts[i] = PerimeterNm;
            _lengths[i] = _segments[i].From.DistanceTo(_segments[i].To);
            PerimeterNm += _lengths[i];
        }

        _ends = new double[_segments.Count];

        for (var i = 0; i < _segments.Count; i++)
        {
            _ends[i] = _starts[i] + _lengths[i];
        }
    }

    /// <summary>The whole way round, in nanometres.</summary>
    public double PerimeterNm { get; }

    /// <summary>
    /// Where a distance along the loop lands. Any distance: the loop is closed, so it wraps —
    /// negatives included, which is what a drag backwards past the start vertex produces.
    /// </summary>
    public Point2 PointAt(double alongNm)
    {
        if (PerimeterNm <= 0 || _segments.Count == 0)
        {
            return _segments.Count > 0 ? _segments[0].From : default;
        }

        return At(Wrap(alongNm));
    }

    /// <summary>
    /// The point at a distance already on the loop, without wrapping it first.
    ///
    /// The planner's own positions are taken modulo the perimeter before they get here, and
    /// wrapping them a second time is not free: <c>(a + p) % p</c> is not exactly <c>a</c> in
    /// floating point. Kept apart so the planner's arithmetic is the one it always was, and the
    /// programs cut from it do not move by a rounding.
    /// </summary>
    internal Point2 At(double at)
    {
        var n = _segments.Count;

        if (n == 0)
        {
            return default;
        }

        // **A binary search, answering exactly what the old scan answered.** The scan walked every
        // segment from the start for the first with length whose end is at or past `at`; the
        // planner asks three times per straight edge, and on the 66-up panel's frame — thousands of
        // segments, its corners tessellated — that was about 0.3 s per plan, on the UI thread, per
        // drag (measured in sprint 6 story 6). The ends never decrease, so the first end at or past
        // `at` is found by halving; the segments before it all end short of `at`, which is why the
        // scan passed them too. From there the first segment with length is the scan's answer.
        var lo = 0;
        var hi = n;

        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;

            if (_ends[mid] >= at)
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }

        for (var i = lo; i < n; i++)
        {
            if (_lengths[i] > 0)
            {
                return Point(i, at);
            }
        }

        // Past every end: the scan's last chance was the final segment, taken only if it has length.
        return _lengths[n - 1] > 0 ? Point(n - 1, at) : _segments[0].From;
    }

    private Point2 Point(int i, double at)
    {
        var t = Math.Clamp((at - _starts[i]) / _lengths[i], 0, 1);

        return new Point2(
            _segments[i].From.X + (long)Math.Round((_segments[i].To.X - _segments[i].From.X) * t),
            _segments[i].From.Y + (long)Math.Round((_segments[i].To.Y - _segments[i].From.Y) * t));
    }

    /// <summary>
    /// The nearest place on the loop to a point: how far along it is, and how far away the point
    /// was. The distance is what lets a caller decide whether a click was *at* the profile or
    /// merely near it.
    /// </summary>
    public (double AlongNm, double DistanceNm) Project(Point2 point)
    {
        var bestAlong = 0.0;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < _segments.Count; i++)
        {
            if (_lengths[i] <= 0)
            {
                continue;
            }

            var from = _segments[i].From;
            var to = _segments[i].To;

            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double px = point.X - from.X;
            double py = point.Y - from.Y;

            var t = Math.Clamp(((px * dx) + (py * dy)) / ((dx * dx) + (dy * dy)), 0, 1);

            var nx = px - (t * dx);
            var ny = py - (t * dy);
            var distance = Math.Sqrt((nx * nx) + (ny * ny));

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestAlong = _starts[i] + (t * _lengths[i]);
            }
        }

        return (bestAlong, bestDistance);
    }

    /// <summary>A distance along the loop brought back onto it, negatives included.</summary>
    public double Wrap(double alongNm) =>
        PerimeterNm <= 0 ? 0 : ((alongNm % PerimeterNm) + PerimeterNm) % PerimeterNm;

    /// <summary>
    /// The stretch of the loop from one distance to another, as segments: whole segments where it
    /// covers them, cut ones at its ends. At most two laps — a stretch can begin part-way round and
    /// end part-way round again, and nothing longer than one whole profile is ever asked for.
    /// </summary>
    internal List<ArtSegment> Sub(double from, double to)
    {
        var run = new List<ArtSegment>();

        for (var lap = 0; lap < 2; lap++)
        {
            for (var i = 0; i < _segments.Count; i++)
            {
                if (_lengths[i] <= 0)
                {
                    continue;
                }

                var s0 = _starts[i] + (lap * PerimeterNm);
                var s1 = s0 + _lengths[i];

                var a = Math.Max(from, s0);
                var b = Math.Min(to, s1);

                if (b - a <= 1e-9)
                {
                    continue;
                }

                run.Add(a <= s0 + 1e-9 && b >= s1 - 1e-9
                    ? _segments[i]
                    : ArtSegment.Line(
                        Along(_segments[i], (a - s0) / _lengths[i]),
                        Along(_segments[i], (b - s0) / _lengths[i])));
            }
        }

        return run;
    }

    private static Point2 Along(ArtSegment segment, double t) => new(
        segment.From.X + (long)Math.Round((segment.To.X - segment.From.X) * t),
        segment.From.Y + (long)Math.Round((segment.To.Y - segment.From.Y) * t));
}
