using MillBurn.Core;
using MillBurn.Gerber.Apertures;

namespace MillBurn.Cam;

/// <summary>
/// Outlines of the standard aperture templates, as closed artwork subpaths.
///
/// Only the four templates from the Gerber spec are here — circle, rectangle, obround, polygon.
/// Aperture macros are a different problem: their primitives compose with additive and subtractive
/// polarity, so they need the boolean stage rather than an outline, and are reported as
/// unrealised until it exists.
/// </summary>
public static class ApertureOutline
{
    /// <summary>
    /// The closed subpaths for one flashed aperture, outer contour first and the drilled hole (if
    /// any) after it. The hole is a subpath of the same shape so an even-odd fill turns it into a
    /// hole rather than a second solid disc.
    /// </summary>
    /// <returns>Null when the aperture kind cannot be outlined yet.</returns>
    public static IReadOnlyList<IReadOnlyList<ArtSegment>>? TryBuild(Aperture aperture, Point2 at)
    {
        ArgumentNullException.ThrowIfNull(aperture);

        var subpaths = new List<IReadOnlyList<ArtSegment>>();

        switch (aperture.Kind)
        {
            case ApertureKind.Circle:
                subpaths.Add(Circle(at, aperture.NominalWidthNm / 2));
                break;

            case ApertureKind.Rectangle:
                subpaths.Add(Rectangle(at, aperture.NominalWidthNm, aperture.NominalHeightNm));
                break;

            case ApertureKind.Obround:
                subpaths.Add(Obround(at, aperture.NominalWidthNm, aperture.NominalHeightNm));
                break;

            case ApertureKind.Polygon:
                subpaths.Add(Polygon(
                    at,
                    aperture.NominalWidthNm / 2,
                    (int)Param(aperture, 1, 3),
                    Param(aperture, 2, 0)));
                break;

            default:
                return null;
        }

        if (aperture.HoleDiameterNm > 0)
        {
            // The hole winds the *other* way from the outer contour.
            //
            // An even-odd fill would make it a hole regardless of direction, and that is what this
            // used to rely on. But even-odd treats *any* overlap as a hole, so two shapes that
            // touch punch a void where they cross — and it makes it unsafe to merge separate
            // shapes into one path, which some laser importers need (see SvgExportOptions).
            // Opposite winding means a non-zero fill gets the hole right and the overlap right.
            subpaths.Add(Reverse(Circle(at, aperture.HoleDiameterNm / 2)));
        }

        return subpaths;
    }

    /// <summary>
    /// A full circle as a single counter-clockwise arc whose endpoints coincide. The SVG writer
    /// splits it at the antipode, because SVG cannot express a 360-degree arc in one command.
    /// </summary>
    public static IReadOnlyList<ArtSegment> Circle(Point2 centre, long radiusNm)
    {
        var start = new Point2(centre.X + radiusNm, centre.Y);
        return [new ArtSegment(ArtSweep.CounterClockwise, start, start, centre)];
    }

    public static IReadOnlyList<ArtSegment> Rectangle(Point2 centre, long widthNm, long heightNm)
    {
        var hw = widthNm / 2;
        var hh = heightNm / 2;

        var a = new Point2(centre.X - hw, centre.Y - hh);
        var b = new Point2(centre.X + hw, centre.Y - hh);
        var c = new Point2(centre.X + hw, centre.Y + hh);
        var d = new Point2(centre.X - hw, centre.Y + hh);

        return [ArtSegment.Line(a, b), ArtSegment.Line(b, c), ArtSegment.Line(c, d), ArtSegment.Line(d, a)];
    }

    /// <summary>A capsule: the shorter axis is fully rounded, the longer one has straight flanks.</summary>
    public static IReadOnlyList<ArtSegment> Obround(Point2 centre, long widthNm, long heightNm)
    {
        if (widthNm == heightNm)
        {
            return Circle(centre, widthNm / 2);
        }

        if (widthNm > heightNm)
        {
            var r = heightNm / 2;
            var flank = (widthNm / 2) - r;

            var bottomLeft = new Point2(centre.X - flank, centre.Y - r);
            var bottomRight = new Point2(centre.X + flank, centre.Y - r);
            var topRight = new Point2(centre.X + flank, centre.Y + r);
            var topLeft = new Point2(centre.X - flank, centre.Y + r);

            return
            [
                ArtSegment.Line(bottomLeft, bottomRight),
                new ArtSegment(ArtSweep.CounterClockwise, bottomRight, topRight, new Point2(centre.X + flank, centre.Y)),
                ArtSegment.Line(topRight, topLeft),
                new ArtSegment(ArtSweep.CounterClockwise, topLeft, bottomLeft, new Point2(centre.X - flank, centre.Y)),
            ];
        }

        var rx = widthNm / 2;
        var flankY = (heightNm / 2) - rx;

        var rightBottom = new Point2(centre.X + rx, centre.Y - flankY);
        var rightTop = new Point2(centre.X + rx, centre.Y + flankY);
        var leftTop = new Point2(centre.X - rx, centre.Y + flankY);
        var leftBottom = new Point2(centre.X - rx, centre.Y - flankY);

        return
        [
            ArtSegment.Line(rightBottom, rightTop),
            new ArtSegment(ArtSweep.CounterClockwise, rightTop, leftTop, new Point2(centre.X, centre.Y + flankY)),
            ArtSegment.Line(leftTop, leftBottom),
            new ArtSegment(ArtSweep.CounterClockwise, leftBottom, rightBottom, new Point2(centre.X, centre.Y - flankY)),
        ];
    }

    /// <summary>Reverses a subpath's direction, turning an island into a hole and back.</summary>
    public static IReadOnlyList<ArtSegment> Reverse(IReadOnlyList<ArtSegment> subpath)
    {
        ArgumentNullException.ThrowIfNull(subpath);

        var reversed = new ArtSegment[subpath.Count];
        for (var i = 0; i < subpath.Count; i++)
        {
            var s = subpath[subpath.Count - 1 - i];
            reversed[i] = new ArtSegment(
                s.Sweep switch
                {
                    ArtSweep.Clockwise => ArtSweep.CounterClockwise,
                    ArtSweep.CounterClockwise => ArtSweep.Clockwise,
                    _ => ArtSweep.Linear,
                },
                s.To,
                s.From,
                s.Centre);
        }

        return reversed;
    }

    /// <summary>A regular polygon inscribed in the given radius, first vertex at the rotation angle.</summary>
    public static IReadOnlyList<ArtSegment> Polygon(
        Point2 centre, long radiusNm, int vertices, double rotationDegrees)
    {
        vertices = Math.Clamp(vertices, 3, 12);

        var points = new Point2[vertices];
        for (var i = 0; i < vertices; i++)
        {
            var angle = ((rotationDegrees + (360.0 * i / vertices)) * Math.PI) / 180.0;
            points[i] = new Point2(
                centre.X + (long)Math.Round(radiusNm * Math.Cos(angle), MidpointRounding.AwayFromZero),
                centre.Y + (long)Math.Round(radiusNm * Math.Sin(angle), MidpointRounding.AwayFromZero));
        }

        var segments = new ArtSegment[vertices];
        for (var i = 0; i < vertices; i++)
        {
            segments[i] = ArtSegment.Line(points[i], points[(i + 1) % vertices]);
        }

        return segments;
    }

    private static double Param(Aperture aperture, int index, double fallback) =>
        index < aperture.Parameters.Count ? aperture.Parameters[index] : fallback;
}
