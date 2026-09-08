using Clipper2Lib;
using MillBurn.Core;

namespace MillBurn.Geometry;

/// <summary>
/// Boolean operations on filled areas, in integer nanometres.
///
/// Clipper2's own types are the currency here rather than a wrapper of ours. Wrapping would be
/// ceremony: <c>Path64</c> is a list of integer points, which is exactly what we want to say, and
/// every hop through an adapter is a chance to lose orientation — the one property the whole
/// even-odd / non-zero story depends on.
///
/// **Orientation is meaning.** After a resolving union, an outer contour runs counter-clockwise
/// (positive area) and a hole runs clockwise (negative). Reversing a path turns a pad into a hole
/// in the copper, silently and with no error anywhere, so paths are never reordered or reversed
/// except by the deliberate helpers here.
/// </summary>
public static class Polygons
{
    public static Paths64 Empty() => [];

    public static Paths64 From(params Path64[] paths) => [.. paths];

    /// <summary>
    /// Resolves a set of raw contours into properly oriented, non-overlapping areas using the
    /// even-odd rule — the rule Gerber regions and aperture holes are defined by, where a contour
    /// inside another cuts a hole in it.
    ///
    /// Everything else in the pipeline takes non-zero input, so this is the gateway: raw contours
    /// come in, resolved areas go out, and orientation is correct from there on.
    /// </summary>
    public static Paths64 ResolveEvenOdd(Paths64 contours)
    {
        ArgumentNullException.ThrowIfNull(contours);
        return contours.Count == 0 ? [] : Clipper.Union(contours, FillRule.EvenOdd);
    }

    public static Paths64 Union(Paths64 a, Paths64 b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Count == 0)
        {
            return b;
        }

        return b.Count == 0 ? a : Clipper.Union(a, b, FillRule.NonZero);
    }

    /// <summary>Self-union: merges overlaps within one already-oriented set.</summary>
    public static Paths64 UnionSelf(Paths64 paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Count == 0 ? [] : Clipper.Union(paths, FillRule.NonZero);
    }

    public static Paths64 Difference(Paths64 subject, Paths64 clip)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(clip);

        if (subject.Count == 0 || clip.Count == 0)
        {
            return subject;
        }

        return Clipper.Difference(subject, clip, FillRule.NonZero);
    }

    public static Paths64 Intersect(Paths64 subject, Paths64 clip)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(clip);

        if (subject.Count == 0 || clip.Count == 0)
        {
            return [];
        }

        return Clipper.Intersect(subject, clip, FillRule.NonZero);
    }

    /// <summary>Signed area in square nanometres; holes subtract.</summary>
    public static double AreaNm2(Paths64 paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Clipper.Area(paths);
    }

    /// <summary>Area in square millimetres, which is the only unit a human can judge.</summary>
    public static double AreaMm2(Paths64 paths) =>
        AreaNm2(paths) / ((double)Nm.PerMillimetre * Nm.PerMillimetre);

    /// <summary>
    /// Everything inside <paramref name="frame"/> that is not in <paramref name="area"/>.
    ///
    /// This is how a negative layer becomes a positive one, and the frame is the whole question:
    /// it should be the board outline, not a bounding box, because a frame that cuts through real
    /// features produces geometry that looks fine and is missing copper at the edges. The realiser
    /// deliberately does not guess one.
    /// </summary>
    public static Paths64 Invert(Paths64 area, Paths64 frame)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(frame);
        return Difference(frame, area);
    }

    /// <summary>A closed rectangle, counter-clockwise so it reads as filled area.</summary>
    public static Path64 Rectangle(Bounds b) =>
    [
        new Point64(b.MinX, b.MinY),
        new Point64(b.MaxX, b.MinY),
        new Point64(b.MaxX, b.MaxY),
        new Point64(b.MinX, b.MaxY),
    ];

    public static Bounds BoundsOf(Paths64 paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var bounds = Bounds.Empty;
        foreach (var path in paths)
        {
            foreach (var p in path)
            {
                bounds = bounds.Include(new Point2(p.X, p.Y));
            }
        }

        return bounds;
    }

    public static int VertexCount(Paths64 paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var total = 0;
        foreach (var path in paths)
        {
            total += path.Count;
        }

        return total;
    }

    /// <summary>
    /// Puts a result into a canonical form: each ring rotated to start at its lowest point, and the
    /// rings sorted.
    ///
    /// Clipper2 is deterministic for identical input, but the *order* it emits rings in follows its
    /// internal sweep, so an unrelated change upstream can permute the output while the geometry is
    /// untouched. That would make a golden hash fire on a non-change and train everyone to ignore
    /// it. Canonicalising first means a hash difference always means a geometry difference.
    ///
    /// Rotation preserves winding, so orientation — and therefore hole-ness — survives.
    /// </summary>
    public static Paths64 Canonicalise(Paths64 paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var rotated = new List<Path64>(paths.Count);
        foreach (var path in paths)
        {
            rotated.Add(RotateToLowestPoint(path));
        }

        rotated.Sort(ComparePaths);
        return [.. rotated];
    }

    private static Path64 RotateToLowestPoint(Path64 path)
    {
        if (path.Count < 2)
        {
            return path;
        }

        var best = 0;
        for (var i = 1; i < path.Count; i++)
        {
            if (Less(path[i], path[best]))
            {
                best = i;
            }
        }

        if (best == 0)
        {
            return path;
        }

        var result = new Path64(path.Count);
        for (var i = 0; i < path.Count; i++)
        {
            result.Add(path[(best + i) % path.Count]);
        }

        return result;
    }

    private static bool Less(Point64 a, Point64 b) => a.Y != b.Y ? a.Y < b.Y : a.X < b.X;

    private static int ComparePaths(Path64 a, Path64 b)
    {
        var shared = Math.Min(a.Count, b.Count);
        for (var i = 0; i < shared; i++)
        {
            if (a[i].Y != b[i].Y)
            {
                return a[i].Y.CompareTo(b[i].Y);
            }

            if (a[i].X != b[i].X)
            {
                return a[i].X.CompareTo(b[i].X);
            }
        }

        return a.Count.CompareTo(b.Count);
    }
}
