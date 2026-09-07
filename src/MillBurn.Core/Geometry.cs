using System.Globalization;

namespace MillBurn.Core;

/// <summary>A point in integer nanometres.</summary>
public readonly record struct Point2(long X, long Y)
{
    public static Point2 Origin => default;

    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);

    public double DistanceTo(Point2 other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({Nm.ToMillimetreString(X)}, {Nm.ToMillimetreString(Y)}) mm");
}

/// <summary>An axis-aligned bounding box in nanometres. Empty until a point is added.</summary>
public readonly record struct Bounds(long MinX, long MinY, long MaxX, long MaxY)
{
    public static Bounds Empty => new(long.MaxValue, long.MaxValue, long.MinValue, long.MinValue);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    public long Width => IsEmpty ? 0 : MaxX - MinX;

    public long Height => IsEmpty ? 0 : MaxY - MinY;

    public Point2 Centre => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public Bounds Include(Point2 p) => new(
        Math.Min(MinX, p.X), Math.Min(MinY, p.Y),
        Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public Bounds Union(Bounds other)
    {
        if (other.IsEmpty)
        {
            return this;
        }

        return IsEmpty
            ? other
            : new Bounds(
                Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
                Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
    }

    public Bounds Inflate(long by) => IsEmpty
        ? this
        : new Bounds(MinX - by, MinY - by, MaxX + by, MaxY + by);

    public override string ToString() => IsEmpty
        ? "(empty)"
        : string.Create(CultureInfo.InvariantCulture,
            $"[{Nm.ToMillimetreString(MinX)}, {Nm.ToMillimetreString(MinY)}] .. " +
            $"[{Nm.ToMillimetreString(MaxX)}, {Nm.ToMillimetreString(MaxY)}] mm");
}

/// <summary>
/// A 2D affine transform, stored row-major as
/// <c>[A C E; B D F]</c> matching the SVG/PostScript convention.
///
/// This is the type the fiducial alignment produces and the post-processor bakes into every
/// emitted coordinate (Documentation/04, section 4.2), so it also has to represent a mirror —
/// hence a full affine rather than rotation plus translation.
/// </summary>
public readonly record struct Transform2(double A, double B, double C, double D, double E, double F)
{
    public static Transform2 Identity => new(1, 0, 0, 1, 0, 0);

    public static Transform2 Translation(long dx, long dy) => new(1, 0, 0, 1, dx, dy);

    public static Transform2 Scaling(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static Transform2 Rotation(double radians)
    {
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return new Transform2(c, s, -s, c, 0, 0);
    }

    public static Transform2 RotationDegrees(double degrees) => Rotation(degrees * Math.PI / 180.0);

    /// <summary>Mirror about the Y axis (negates X). Used for bottom-side layers.</summary>
    public static Transform2 MirrorX => new(-1, 0, 0, 1, 0, 0);

    public Point2 Apply(Point2 p) => new(
        (long)Math.Round((A * p.X) + (C * p.Y) + E, MidpointRounding.AwayFromZero),
        (long)Math.Round((B * p.X) + (D * p.Y) + F, MidpointRounding.AwayFromZero));

    /// <summary>Applies the linear part only — for directions and offsets, not positions.</summary>
    public Point2 ApplyVector(Point2 v) => new(
        (long)Math.Round((A * v.X) + (C * v.Y), MidpointRounding.AwayFromZero),
        (long)Math.Round((B * v.X) + (D * v.Y), MidpointRounding.AwayFromZero));

    /// <summary>Returns <c>this</c> followed by <paramref name="then"/>.</summary>
    public Transform2 Then(Transform2 then) => new(
        (A * then.A) + (B * then.C),
        (A * then.B) + (B * then.D),
        (C * then.A) + (D * then.C),
        (C * then.B) + (D * then.D),
        (E * then.A) + (F * then.C) + then.E,
        (E * then.B) + (F * then.D) + then.F);

    public double Determinant => (A * D) - (B * C);

    /// <summary>True when the transform flips handedness — an accidental one scraps a board.</summary>
    public bool IsMirrored => Determinant < 0;

    /// <summary>Uniform scale factor, valid for similarity transforms.</summary>
    public double Scale => Math.Sqrt(Math.Abs(Determinant));

    /// <summary>Rotation in degrees, valid for similarity transforms.</summary>
    public double RotationDegreesValue => Math.Atan2(B, A) * 180.0 / Math.PI;
}
