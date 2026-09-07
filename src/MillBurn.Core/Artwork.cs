namespace MillBurn.Core;

/// <summary>How a segment gets from its start to its end.</summary>
public enum ArtSweep
{
    Linear,
    Clockwise,
    CounterClockwise,
}

/// <summary>
/// What a piece of geometry is *for*. This is the only thing an exporter needs in order to decide
/// which layer, colour and burn mode a shape belongs to, which is why it is a role and not a
/// colour: the mapping from role to the target's palette lives in the export profile
/// (Documentation/04, section 2.3).
/// </summary>
public enum ArtRole
{
    /// <summary>Line work burned at 1x — silkscreen legends, engraved marks.</summary>
    Mark,

    /// <summary>Closed regions to be cleared — mask removal, pad openings.</summary>
    Fill,

    /// <summary>The edge of a fill region, run first at low power for a crisp boundary.</summary>
    Boundary,

    /// <summary>Registration marks. Usually burned; sometimes only a placement aid.</summary>
    Registration,

    /// <summary>Drawn but never burned — board outline, underlays, dimensions.</summary>
    Reference,
}

/// <summary>
/// One segment of an artwork path. Arcs keep their centre rather than being flattened, so an SVG
/// arc command can carry them through unchanged — the same reason the Gerber parser keeps them
/// (Documentation/02, section 2). Flattening here would turn a 4-command circle into 200 line
/// segments for no gain.
/// </summary>
public readonly record struct ArtSegment(ArtSweep Sweep, Point2 From, Point2 To, Point2 Centre)
{
    public static ArtSegment Line(Point2 from, Point2 to) => new(ArtSweep.Linear, from, to, default);

    public bool IsArc => Sweep is ArtSweep.Clockwise or ArtSweep.CounterClockwise;

    /// <summary>Mean of the two endpoint radii; they differ slightly after coordinate rounding.</summary>
    public double RadiusNm => IsArc
        ? (Centre.DistanceTo(From) + Centre.DistanceTo(To)) / 2.0
        : 0.0;

    /// <summary>
    /// Magnitude of the swept angle in radians, always in (0, 2*pi]. A segment whose endpoints
    /// coincide is a full circle, which is a real case: KiCad draws circular silk outlines that
    /// way.
    /// </summary>
    public double SweptAngle()
    {
        if (!IsArc)
        {
            return 0.0;
        }

        var a0 = Math.Atan2(From.Y - Centre.Y, From.X - Centre.X);
        var a1 = Math.Atan2(To.Y - Centre.Y, To.X - Centre.X);
        var delta = a1 - a0;

        if (Sweep == ArtSweep.CounterClockwise)
        {
            while (delta <= 0)
            {
                delta += 2 * Math.PI;
            }

            return delta;
        }

        while (delta >= 0)
        {
            delta -= 2 * Math.PI;
        }

        return -delta;
    }
}

/// <summary>
/// One drawable object: a set of subpaths that share a style. Holes belong here, as further
/// subpaths of the same shape, because an even-odd fill of one path is the only way a hole
/// survives an import into a laser program (Documentation/04, section 2.3).
/// </summary>
public sealed record ArtShape
{
    public required IReadOnlyList<IReadOnlyList<ArtSegment>> Subpaths { get; init; }

    /// <summary>Stroke width in nanometres; 0 means a hairline (fills use this).</summary>
    public long StrokeWidthNm { get; init; }

    public bool Filled { get; init; }

    /// <summary>Optional human label, emitted as a title so a viewer can show it on hover.</summary>
    public string? Title { get; init; }
}

/// <summary>A named group of shapes sharing a role.</summary>
public sealed record ArtLayer
{
    /// <summary>Stable machine id, used as the SVG element id. Must be unique in an artwork.</summary>
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required ArtRole Role { get; init; }

    public required IReadOnlyList<ArtShape> Shapes { get; init; }

    public int ShapeCount => Shapes.Count;

    /// <summary>
    /// Subpaths across every shape — the count that means "how many separate strokes are on this
    /// layer", which is what an operator recognises. Shapes are merged for file size, so the shape
    /// count on its own says nothing useful.
    /// </summary>
    public int SubpathCount => Shapes.Sum(s => s.Subpaths.Count);
}

/// <summary>
/// A resolution-independent vector drawing, in nanometres, in source (Y-up) coordinates.
///
/// This is the hand-off between CAM and the exporters: CAM decides *what* geometry exists and what
/// it is for, exporters decide how to write it down. Keeping the model free of any SVG or G-code
/// concept is what lets one artwork feed the SVG writer, the DXF writer and the viewer without
/// three parallel conversions.
/// </summary>
public sealed record Artwork
{
    public required IReadOnlyList<ArtLayer> Layers { get; init; }

    /// <summary>Where the drawn geometry actually is, stroke widths included.</summary>
    public required Bounds ContentBounds { get; init; }

    /// <summary>
    /// Things the operator needs to know that are not geometry: compensations applied, objects
    /// that could not be realised, thresholds that were crossed. Surfaced by the CLI and the
    /// export dialog, and embedded in the file, so nothing is silently dropped.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>What this was made from, for the file's own metadata.</summary>
    public string? Source { get; init; }
}

/// <summary>Measurement helpers for artwork geometry.</summary>
public static class ArtGeometry
{
    /// <summary>
    /// Bounds of a run of segments, correct for arcs.
    ///
    /// Endpoints alone are not enough and the failure is not subtle: a full circle drawn as one
    /// arc has coincident endpoints, so an endpoint-only box collapses to a point and the SVG page
    /// comes out the wrong size.
    /// </summary>
    public static Bounds Measure(IEnumerable<ArtSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var bounds = Bounds.Empty;
        foreach (var s in segments)
        {
            bounds = bounds.Include(s.From).Include(s.To);
            if (s.IsArc)
            {
                bounds = IncludeArcExtremes(bounds, s);
            }
        }

        return bounds;
    }

    public static Bounds Measure(ArtShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        var bounds = Bounds.Empty;
        foreach (var subpath in shape.Subpaths)
        {
            bounds = bounds.Union(Measure(subpath));
        }

        // A stroke straddles its centreline, so half of it hangs outside on every side.
        return bounds.IsEmpty ? bounds : bounds.Inflate(shape.StrokeWidthNm / 2);
    }

    public static Bounds Measure(IEnumerable<ArtLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var bounds = Bounds.Empty;
        foreach (var layer in layers)
        {
            foreach (var shape in layer.Shapes)
            {
                bounds = bounds.Union(Measure(shape));
            }
        }

        return bounds;
    }

    /// <summary>
    /// Adds the cardinal points of the circle that the arc actually passes through. An arc only
    /// reaches beyond its endpoints where it crosses 0, 90, 180 or 270 degrees.
    /// </summary>
    private static Bounds IncludeArcExtremes(Bounds bounds, ArtSegment s)
    {
        var radius = s.RadiusNm;
        var swept = s.SweptAngle();
        var start = Math.Atan2(s.From.Y - s.Centre.Y, s.From.X - s.Centre.X);

        for (var quadrant = 0; quadrant < 4; quadrant++)
        {
            var angle = quadrant * Math.PI / 2;

            // Angle travelled from the start point to this cardinal direction, measured in the
            // direction the arc is actually going.
            var travelled = s.Sweep == ArtSweep.CounterClockwise ? angle - start : start - angle;
            travelled -= Math.Floor(travelled / (2 * Math.PI)) * 2 * Math.PI;

            if (travelled > swept)
            {
                continue;
            }

            bounds = bounds.Include(new Point2(
                s.Centre.X + (long)Math.Round(radius * Math.Cos(angle), MidpointRounding.AwayFromZero),
                s.Centre.Y + (long)Math.Round(radius * Math.Sin(angle), MidpointRounding.AwayFromZero)));
        }

        return bounds;
    }
}
