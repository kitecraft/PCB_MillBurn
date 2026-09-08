using Clipper2Lib;
using MillBurn.Core;

namespace MillBurn.Geometry;

/// <summary>
/// Turns curves into line segments for the boolean stage.
///
/// Flattening happens **here and nowhere earlier**. The parser keeps arcs as arcs, and the emitter
/// re-fits G2/G3 from the toolpath, so a curve is only ever a polyline while Clipper2 is holding
/// it. Flattening at parse time is why pcb2gcode can never emit an arc and produces megabytes of
/// G01.
///
/// Every method here is driven by a **sagitta tolerance** — the greatest distance between the true
/// curve and the chord that replaces it — rather than a fixed segment count. A fixed count is
/// wrong at both ends: 32 segments is wasteful on a 0.2 mm via and visibly faceted on a 50 mm
/// board outline.
/// </summary>
public static class Tessellate
{
    /// <summary>
    /// Default chord tolerance: 1 µm. Three orders of magnitude below what a hobby machine
    /// resolves, and small enough that the faceting is invisible at any sane zoom.
    /// </summary>
    public const long DefaultSagittaNm = 1_000;

    /// <summary>
    /// Segment count for an arc of the given radius and swept angle at a sagitta tolerance.
    ///
    /// From <c>s = r(1 - cos(θ/2))</c> for a chord subtending θ: solve for θ and divide the sweep
    /// by it. Never fewer than one segment, and clamped for radii at or below the tolerance, where
    /// the formula stops being meaningful.
    /// </summary>
    public static int SegmentsForArc(double radiusNm, double sweptRadians, long sagittaNm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sagittaNm);

        var sweep = Math.Abs(sweptRadians);
        if (sweep <= 0 || radiusNm <= sagittaNm)
        {
            return Math.Max(1, (int)Math.Ceiling(sweep / (Math.PI / 4)));
        }

        var maxStep = 2 * Math.Acos(1 - (sagittaNm / radiusNm));
        return Math.Max(1, (int)Math.Ceiling(sweep / maxStep));
    }

    /// <summary>
    /// A closed circle as a polygon, starting at angle zero and running counter-clockwise.
    ///
    /// <paramref name="containing"/> pushes the vertices out by <c>1/cos(π/n)</c> so the polygon
    /// *contains* the true circle rather than being inscribed in it. That is the safe direction for
    /// copper: isolation milling offsets outward from the copper boundary, so a pad modelled
    /// slightly small puts the cutter slightly close to real copper. The error either way is under
    /// the tolerance, but only one of the two errs toward a board that works.
    ///
    /// Starting at a fixed angle keeps this deterministic: the same radius always produces the same
    /// polygon, so identical pads union cleanly and the output hashes stably.
    /// </summary>
    public static Path64 Circle(
        Point2 centre, long radiusNm, long sagittaNm = DefaultSagittaNm, bool containing = true)
    {
        var count = Math.Max(3, SegmentsForArc(radiusNm, 2 * Math.PI, sagittaNm));
        var radius = containing ? radiusNm / Math.Cos(Math.PI / count) : radiusNm;

        var path = new Path64(count);
        for (var i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * i / count;
            path.Add(new Point64(
                centre.X + (long)Math.Round(radius * Math.Cos(angle), MidpointRounding.AwayFromZero),
                centre.Y + (long)Math.Round(radius * Math.Sin(angle), MidpointRounding.AwayFromZero)));
        }

        return path;
    }

    /// <summary>
    /// Appends an arc to an open path, excluding its start point (assumed already present) and
    /// including its end point exactly as given — so consecutive segments join without a gap even
    /// where the stored radius and the stored endpoints disagree slightly after coordinate
    /// rounding, which in real Gerbers they routinely do.
    /// </summary>
    public static void AppendArc(Path64 into, ArtSegment segment, long sagittaNm = DefaultSagittaNm)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (!segment.IsArc)
        {
            into.Add(ToPoint64(segment.To));
            return;
        }

        var radius = segment.RadiusNm;
        var swept = segment.SweptAngle();
        var steps = SegmentsForArc(radius, swept, sagittaNm);

        var start = Math.Atan2(segment.From.Y - segment.Centre.Y, segment.From.X - segment.Centre.X);
        var direction = segment.Sweep == ArtSweep.CounterClockwise ? 1.0 : -1.0;

        for (var i = 1; i < steps; i++)
        {
            var angle = start + (direction * swept * i / steps);
            into.Add(new Point64(
                segment.Centre.X + (long)Math.Round(radius * Math.Cos(angle), MidpointRounding.AwayFromZero),
                segment.Centre.Y + (long)Math.Round(radius * Math.Sin(angle), MidpointRounding.AwayFromZero)));
        }

        into.Add(ToPoint64(segment.To));
    }

    /// <summary>
    /// Flattens a run of connected segments into a polyline. The first point comes from the first
    /// segment's start; every segment then contributes its end.
    /// </summary>
    public static Path64 Flatten(IReadOnlyList<ArtSegment> segments, long sagittaNm = DefaultSagittaNm)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var path = new Path64(segments.Count + 1);
        if (segments.Count == 0)
        {
            return path;
        }

        path.Add(ToPoint64(segments[0].From));
        foreach (var segment in segments)
        {
            AppendArc(path, segment, sagittaNm);
        }

        return path;
    }

    public static Point64 ToPoint64(Point2 p) => new(p.X, p.Y);

    public static Point2 ToPoint2(Point64 p) => new(p.X, p.Y);
}
