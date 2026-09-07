using MillBurn.Core;

namespace MillBurn.Export;

/// <summary>
/// The page every SVG in a job is drawn on: a fixed rectangle in source (Y-up, nanometre)
/// coordinates.
///
/// This exists as its own type for one reason, and it is the most important rule in the laser
/// path (Documentation/04, section 4.3): **every export belonging to one job must share a page.**
/// If the etch-resist export is cropped to the copper extents and the pad export is cropped to
/// the pad extents, the two drawings do not overlay when imported, and the second burn lands
/// offset by the difference between the two crops. Nothing about the files looks wrong; the board
/// is just ruined. Passing one <see cref="SvgPage"/> to every export makes that impossible.
/// </summary>
public sealed record SvgPage
{
    public required Bounds Frame { get; init; }

    /// <summary>A page around some content, with an equal margin on every side.</summary>
    public static SvgPage ForContent(Bounds content, long marginNm)
    {
        if (content.IsEmpty)
        {
            throw new ArgumentException("Cannot build a page around empty content.", nameof(content));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(marginNm);
        return new SvgPage { Frame = content.Inflate(marginNm) };
    }

    public double WidthMm => Nm.ToMillimetres(Frame.Width);

    public double HeightMm => Nm.ToMillimetres(Frame.Height);

    /// <summary>
    /// Source coordinates to page coordinates: shift to the frame origin and flip Y, because
    /// Gerber counts Y upward and SVG counts it downward.
    /// </summary>
    public Point2 ToPage(Point2 p, bool mirror = false) => new(
        mirror ? Frame.MaxX - p.X : p.X - Frame.MinX,
        Frame.MaxY - p.Y);

    public override string ToString() =>
        FormattableString.Invariant($"{WidthMm:F3} x {HeightMm:F3} mm at {Frame}");
}
