using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;
using MillBurn.Gerber.Apertures;

namespace MillBurn.Cam;

/// <summary>
/// Turns an aperture into filled area.
///
/// This lives in CAM rather than in Geometry because it is the one place that needs to know both
/// what a Gerber aperture means and how to build a polygon; Geometry stays free of any Gerber
/// concept, and the parser stays free of Clipper2.
///
/// A flashed aperture is the most common object on a board — a copper layer is mostly pads — so
/// this is also the hot path, and the reason apertures are cached by code rather than rebuilt per
/// flash.
/// </summary>
public static class ApertureShapes
{
    /// <summary>
    /// The aperture's area, centred on the origin, resolved so that holes are real holes.
    ///
    /// Returns null for an aperture whose geometry cannot be built — which after this change means
    /// only a malformed macro, since every standard template and every specified macro primitive
    /// is handled.
    /// </summary>
    public static Paths64? TryBuild(Aperture aperture, long sagittaNm = Tessellate.DefaultSagittaNm)
    {
        ArgumentNullException.ThrowIfNull(aperture);

        var contours = aperture.Kind switch
        {
            ApertureKind.Circle => Polygons.From(Tessellate.Circle(Point2.Origin, aperture.NominalWidthNm / 2, sagittaNm)),
            ApertureKind.Rectangle => Polygons.From(Rectangle(Point2.Origin, aperture.NominalWidthNm, aperture.NominalHeightNm)),
            ApertureKind.Obround => Obround(Point2.Origin, aperture.NominalWidthNm, aperture.NominalHeightNm, sagittaNm),
            ApertureKind.Polygon => Polygons.From(RegularPolygon(
                Point2.Origin,
                aperture.NominalWidthNm / 2,
                (int)Param(aperture, 1, 3),
                Param(aperture, 2, 0))),
            ApertureKind.Macro => BuildMacro(aperture, sagittaNm),
            _ => null,
        };

        if (contours is null)
        {
            return null;
        }

        // The standard templates take an optional drilled hole as their last parameter. Adding it
        // as another contour and resolving even-odd is what turns it into a hole rather than a
        // second solid disc — the same rule the SVG writer relies on.
        if (aperture.Kind != ApertureKind.Macro && aperture.HoleDiameterNm > 0)
        {
            contours.Add(Tessellate.Circle(Point2.Origin, aperture.HoleDiameterNm / 2, sagittaNm));
        }

        return Polygons.ResolveEvenOdd(contours);
    }

    /// <summary>Translates a prebuilt aperture shape to a flash position.</summary>
    public static Paths64 Translate(Paths64 shape, Point2 to)
    {
        ArgumentNullException.ThrowIfNull(shape);

        var result = new Paths64(shape.Count);
        foreach (var path in shape)
        {
            var moved = new Path64(path.Count);
            foreach (var p in path)
            {
                moved.Add(new Point64(p.X + to.X, p.Y + to.Y));
            }

            result.Add(moved);
        }

        return result;
    }

    // ------------------------------------------------------------------ standard templates

    public static Path64 Rectangle(Point2 centre, long widthNm, long heightNm)
    {
        var hw = widthNm / 2;
        var hh = heightNm / 2;

        return
        [
            new Point64(centre.X - hw, centre.Y - hh),
            new Point64(centre.X + hw, centre.Y - hh),
            new Point64(centre.X + hw, centre.Y + hh),
            new Point64(centre.X - hw, centre.Y + hh),
        ];
    }

    /// <summary>A capsule: the shorter axis fully rounded, the longer one with straight flanks.</summary>
    public static Paths64 Obround(Point2 centre, long widthNm, long heightNm, long sagittaNm)
    {
        if (widthNm == heightNm)
        {
            return Polygons.From(Tessellate.Circle(centre, widthNm / 2, sagittaNm));
        }

        var horizontal = widthNm > heightNm;
        var radius = (horizontal ? heightNm : widthNm) / 2;
        var flank = ((horizontal ? widthNm : heightNm) / 2) - radius;

        var capA = horizontal
            ? new Point2(centre.X + flank, centre.Y)
            : new Point2(centre.X, centre.Y + flank);
        var capB = horizontal
            ? new Point2(centre.X - flank, centre.Y)
            : new Point2(centre.X, centre.Y - flank);

        // Two end caps plus the rectangle between them, unioned. Cheaper to reason about than
        // stitching four arcs in the right order, and Clipper resolves the seams exactly.
        var body = horizontal
            ? Rectangle(centre, 2 * flank, 2 * radius)
            : Rectangle(centre, 2 * radius, 2 * flank);

        return Polygons.UnionSelf(Polygons.From(
            Tessellate.Circle(capA, radius, sagittaNm),
            Tessellate.Circle(capB, radius, sagittaNm),
            body));
    }

    /// <summary>A regular polygon inscribed in the radius, first vertex at the rotation angle.</summary>
    public static Path64 RegularPolygon(Point2 centre, long radiusNm, int vertices, double rotationDegrees)
    {
        vertices = Math.Clamp(vertices, 3, 12);

        var path = new Path64(vertices);
        for (var i = 0; i < vertices; i++)
        {
            var angle = ((rotationDegrees + (360.0 * i / vertices)) * Math.PI) / 180.0;
            path.Add(new Point64(
                centre.X + (long)Math.Round(radiusNm * Math.Cos(angle), MidpointRounding.AwayFromZero),
                centre.Y + (long)Math.Round(radiusNm * Math.Sin(angle), MidpointRounding.AwayFromZero)));
        }

        return path;
    }

    // ------------------------------------------------------------------ macros

    /// <summary>
    /// Replays a macro program against its parameters.
    ///
    /// A macro is not a shape but a *sequence*: primitives composite in order, and a primitive
    /// whose exposure is 0 subtracts from everything already drawn. Unioning them all and hoping
    /// would fill in every clearance a footprint carefully cut — the "thermal relief becomes a
    /// solid disc" failure, which shorts a pad to its pour and does it silently.
    /// </summary>
    private static Paths64? BuildMacro(Aperture aperture, long sagittaNm)
    {
        if (aperture.Macro is null)
        {
            return null;
        }

        var scale = aperture.Unit == LengthUnit.Inches ? (double)Nm.PerInch : Nm.PerMillimetre;
        var parameters = new List<double>(aperture.Parameters);
        var result = Polygons.Empty();

        foreach (var statement in aperture.Macro.Statements)
        {
            switch (statement)
            {
                case MacroAssignment assignment:
                    {
                        while (parameters.Count <= assignment.Index)
                        {
                            parameters.Add(0.0);
                        }

                        parameters[assignment.Index] = assignment.Value.Evaluate(parameters);
                        break;
                    }

                case MacroPrimitive primitive:
                    {
                        var (paths, exposed) = BuildPrimitive(primitive, parameters, scale, sagittaNm);
                        if (paths is null || paths.Count == 0)
                        {
                            break;
                        }

                        var resolved = Polygons.ResolveEvenOdd(paths);
                        result = exposed
                            ? Polygons.Union(result, resolved)
                            : Polygons.Difference(result, resolved);
                        break;
                    }

                default:
                    break;
            }
        }

        return result;
    }

    private static (Paths64? Paths, bool Exposed) BuildPrimitive(
        MacroPrimitive primitive, IReadOnlyList<double> p, double scale, long sagittaNm)
    {
        long Nm_(double value) => (long)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        Point2 At(double x, double y) => new(Nm_(x), Nm_(y));
        double A(int i, double fallback = 0.0) => primitive.Arg(p, i, fallback);

        switch (primitive.Kind)
        {
            case MacroPrimitiveKind.Circle:
                {
                    // exposure, diameter, x, y, [rotation]
                    var paths = Polygons.From(Tessellate.Circle(At(A(2), A(3)), Nm_(A(1)) / 2, sagittaNm));
                    return (Rotate(paths, A(4)), A(0) != 0);
                }

            case MacroPrimitiveKind.VectorLine:
                {
                    // exposure, width, x1, y1, x2, y2, [rotation]
                    var width = Nm_(A(1));
                    var from = At(A(2), A(3));
                    var to = At(A(4), A(5));
                    return (Rotate(VectorLine(from, to, width), A(6)), A(0) != 0);
                }

            case MacroPrimitiveKind.CenterLine:
                {
                    // exposure, width, height, x, y, [rotation]
                    var paths = Polygons.From(Rectangle(At(A(3), A(4)), Nm_(A(1)), Nm_(A(2))));
                    return (Rotate(paths, A(5)), A(0) != 0);
                }

            case MacroPrimitiveKind.LowerLeftLine:
                {
                    // exposure, width, height, lower-left x, lower-left y, [rotation] — deprecated,
                    // and the only primitive whose position is a corner rather than a centre.
                    var w = Nm_(A(1));
                    var h = Nm_(A(2));
                    var ll = At(A(3), A(4));
                    var paths = Polygons.From(Rectangle(new Point2(ll.X + (w / 2), ll.Y + (h / 2)), w, h));
                    return (Rotate(paths, A(5)), A(0) != 0);
                }

            case MacroPrimitiveKind.Outline:
                {
                    // exposure, vertex count, x0, y0, x1, y1, ... , [rotation]
                    var count = (int)A(1);
                    if (count < 1)
                    {
                        return (null, false);
                    }

                    var path = new Path64(count + 1);
                    for (var i = 0; i <= count; i++)
                    {
                        path.Add(Tessellate.ToPoint64(At(A(2 + (2 * i)), A(3 + (2 * i)))));
                    }

                    // The spec requires the polygon to close, so a repeated final point is normal
                    // and must not become a zero-length edge.
                    if (path.Count > 1 && path[0] == path[^1])
                    {
                        path.RemoveAt(path.Count - 1);
                    }

                    return (Rotate(Polygons.From(path), A(4 + (2 * count))), A(0) != 0);
                }

            case MacroPrimitiveKind.Polygon:
                {
                    // exposure, vertices, centre x, centre y, diameter, [rotation]
                    var paths = Polygons.From(RegularPolygon(At(A(2), A(3)), Nm_(A(4)) / 2, (int)A(1), 0));
                    return (Rotate(paths, A(5)), A(0) != 0);
                }

            case MacroPrimitiveKind.Thermal:
                {
                    // centre x, centre y, outer diameter, inner diameter, gap, [rotation].
                    // Always exposed — a thermal has no exposure parameter.
                    return (Rotate(Thermal(At(A(0), A(1)), Nm_(A(2)) / 2, Nm_(A(3)) / 2, Nm_(A(4)), sagittaNm), A(5)), true);
                }

            case MacroPrimitiveKind.Moire:
                {
                    // centre x, y, outer diameter, ring thickness, gap, max rings,
                    // crosshair thickness, crosshair length, [rotation]. Deprecated, always exposed.
                    return (Rotate(
                        Moire(
                            At(A(0), A(1)), Nm_(A(2)) / 2, Nm_(A(3)), Nm_(A(4)), (int)A(5),
                            Nm_(A(6)), Nm_(A(7)), sagittaNm),
                        A(8)), true);
                }

            default:
                return (null, false);
        }
    }

    /// <summary>A rectangle of the given width laid along a segment, with butt ends.</summary>
    private static Paths64 VectorLine(Point2 from, Point2 to, long widthNm)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        if (length <= 0)
        {
            return Polygons.From(Rectangle(from, widthNm, widthNm));
        }

        var nx = -dy / length * (widthNm / 2.0);
        var ny = dx / length * (widthNm / 2.0);

        long X(double bx, double ox) => (long)Math.Round(bx + ox, MidpointRounding.AwayFromZero);

        return Polygons.From(
        [
            new Point64(X(from.X, nx), X(from.Y, ny)),
            new Point64(X(to.X, nx), X(to.Y, ny)),
            new Point64(X(to.X, -nx), X(to.Y, -ny)),
            new Point64(X(from.X, -nx), X(from.Y, -ny)),
        ]);
    }

    /// <summary>
    /// An annulus split into four quadrants by a cross of the given gap width — the pad-to-pour
    /// connection that keeps a pad solderable without the pour sinking all the heat.
    /// </summary>
    private static Paths64 Thermal(
        Point2 centre, long outerRadiusNm, long innerRadiusNm, long gapNm, long sagittaNm)
    {
        var ring = Polygons.Difference(
            Polygons.From(Tessellate.Circle(centre, outerRadiusNm, sagittaNm)),
            Polygons.From(Tessellate.Circle(centre, innerRadiusNm, sagittaNm)));

        if (gapNm <= 0)
        {
            return ring;
        }

        var span = (2 * outerRadiusNm) + gapNm;
        var cross = Polygons.UnionSelf(Polygons.From(
            Rectangle(centre, span, gapNm),
            Rectangle(centre, gapNm, span)));

        return Polygons.Difference(ring, cross);
    }

    /// <summary>Concentric rings plus a crosshair — a fabrication registration target.</summary>
    private static Paths64 Moire(
        Point2 centre, long outerRadiusNm, long thicknessNm, long gapNm, int maxRings,
        long crosshairThicknessNm, long crosshairLengthNm, long sagittaNm)
    {
        var result = Polygons.Empty();
        var radius = outerRadiusNm;

        for (var i = 0; i < Math.Max(0, maxRings) && radius > 0; i++)
        {
            var inner = radius - thicknessNm;
            var ring = inner > 0
                ? Polygons.Difference(
                    Polygons.From(Tessellate.Circle(centre, radius, sagittaNm)),
                    Polygons.From(Tessellate.Circle(centre, inner, sagittaNm)))
                : Polygons.From(Tessellate.Circle(centre, radius, sagittaNm));

            result = Polygons.Union(result, ring);

            radius = inner - gapNm;
            if (thicknessNm + gapNm <= 0)
            {
                break;
            }
        }

        if (crosshairThicknessNm > 0 && crosshairLengthNm > 0)
        {
            result = Polygons.Union(result, Polygons.UnionSelf(Polygons.From(
                Rectangle(centre, crosshairLengthNm, crosshairThicknessNm),
                Rectangle(centre, crosshairThicknessNm, crosshairLengthNm))));
        }

        return result;
    }

    /// <summary>
    /// Rotates about the **macro origin**, not about the primitive's own centre. That is what the
    /// specification says, and it is the difference between a rotated footprint and a footprint
    /// whose parts have each spun in place.
    /// </summary>
    private static Paths64 Rotate(Paths64 paths, double degrees)
    {
        if (Math.Abs(degrees) < 1e-12)
        {
            return paths;
        }

        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var result = new Paths64(paths.Count);
        foreach (var path in paths)
        {
            var turned = new Path64(path.Count);
            foreach (var p in path)
            {
                turned.Add(new Point64(
                    (long)Math.Round((p.X * cos) - (p.Y * sin), MidpointRounding.AwayFromZero),
                    (long)Math.Round((p.X * sin) + (p.Y * cos), MidpointRounding.AwayFromZero)));
            }

            result.Add(turned);
        }

        return result;
    }

    private static double Param(Aperture aperture, int index, double fallback) =>
        index < aperture.Parameters.Count ? aperture.Parameters[index] : fallback;
}
