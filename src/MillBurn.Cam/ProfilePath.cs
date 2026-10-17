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

        var at = Wrap(alongNm);

        for (var i = 0; i < _segments.Count; i++)
        {
            if (_lengths[i] <= 0)
            {
                continue;
            }

            if (at <= _starts[i] + _lengths[i] || i == _segments.Count - 1)
            {
                var t = Math.Clamp((at - _starts[i]) / _lengths[i], 0, 1);

                return new Point2(
                    _segments[i].From.X
                        + (long)Math.Round((_segments[i].To.X - _segments[i].From.X) * t),
                    _segments[i].From.Y
                        + (long)Math.Round((_segments[i].To.Y - _segments[i].From.Y) * t));
            }
        }

        return _segments[0].From;
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
}
