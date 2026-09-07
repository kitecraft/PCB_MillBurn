using MillBurn.Core;
using MillBurn.Gerber.Apertures;

namespace MillBurn.Gerber.Model;

/// <summary>Dark adds copper, clear removes it from what came before.</summary>
public enum Polarity
{
    Dark,
    Clear,
}

/// <summary>Interpolation mode in force when a segment was drawn.</summary>
public enum SegmentKind
{
    Linear,
    ClockwiseArc,
    CounterClockwiseArc,
}

/// <summary>
/// One drawn segment. Arcs keep their centre rather than being flattened here — flattening early
/// is why pcb2gcode can never re-fit G2/G3 on output and emits megabytes of G01
/// (Documentation/02, section 2).
/// </summary>
public readonly record struct GerberSegment(
    SegmentKind Kind,
    Point2 From,
    Point2 To,
    Point2 Centre)
{
    public static GerberSegment Line(Point2 from, Point2 to) =>
        new(SegmentKind.Linear, from, to, default);

    public bool IsArc => Kind is SegmentKind.ClockwiseArc or SegmentKind.CounterClockwiseArc;
}

/// <summary>Common state carried by every graphic object.</summary>
public abstract record GraphicObject
{
    public required Polarity Polarity { get; init; }

    public required int SourceLine { get; init; }

    /// <summary>Object attributes (<c>%TO%</c>) in force, e.g. <c>.N</c> net, <c>.P</c> pin.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Net name from the <c>.N</c> object attribute, when the writer supplied one.</summary>
    public string? Net => Attributes.TryGetValue(".N", out var v) ? v : null;
}

/// <summary>A stroked path: the aperture is dragged along the segments.</summary>
public sealed record DrawObject : GraphicObject
{
    public required Aperture Aperture { get; init; }

    public required IReadOnlyList<GerberSegment> Segments { get; init; }
}

/// <summary>An aperture stamped at a point — a <c>D03</c>. Almost always a pad.</summary>
public sealed record FlashObject : GraphicObject
{
    public required Aperture Aperture { get; init; }

    public required Point2 At { get; init; }
}

/// <summary>
/// A filled region from <c>G36</c>/<c>G37</c>. Contours after the first are holes only by
/// winding; the geometry stage resolves that with an even-odd fill.
/// </summary>
public sealed record RegionObject : GraphicObject
{
    public required IReadOnlyList<IReadOnlyList<GerberSegment>> Contours { get; init; }
}

/// <summary>A problem found while parsing that did not stop the parse.</summary>
public readonly record struct GerberDiagnostic(string Message, int Line, bool IsError)
{
    public override string ToString() => $"{(IsError ? "error" : "warning")} (line {Line}): {Message}";
}

/// <summary>
/// The result of parsing one Gerber file: an ordered list of graphic objects plus everything the
/// file said about itself.
///
/// Order matters and is preserved exactly. Clear-polarity objects only erase what precedes them,
/// so compositing is a sequential accumulate, not a bulk union of darks minus a bulk union of
/// clears. Getting that backwards silently deletes copper.
/// </summary>
public sealed class GerberImage
{
    public required IReadOnlyList<GraphicObject> Objects { get; init; }

    public required IReadOnlyDictionary<int, Aperture> Apertures { get; init; }

    /// <summary>File attributes (<c>%TF%</c>), including <c>.FileFunction</c>.</summary>
    public required IReadOnlyDictionary<string, string> FileAttributes { get; init; }

    public required LengthUnit Unit { get; init; }

    public required CoordinateFormat Format { get; init; }

    public required Bounds Bounds { get; init; }

    public required IReadOnlyList<GerberDiagnostic> Diagnostics { get; init; }

    /// <summary>True when the writer supplied X2 attributes, so pad selection can be exact.</summary>
    public bool HasExtendedAttributes =>
        FileAttributes.Count > 0 || Apertures.Values.Any(a => a.Attributes.Count > 0);

    public string? FileFunction =>
        FileAttributes.TryGetValue(".FileFunction", out var v) ? v : null;

    /// <summary>True when <c>%TF.FilePolarity,Negative%</c> was declared.</summary>
    public bool IsNegative =>
        FileAttributes.TryGetValue(".FilePolarity", out var v)
        && v.StartsWith("Negative", StringComparison.OrdinalIgnoreCase);
}
