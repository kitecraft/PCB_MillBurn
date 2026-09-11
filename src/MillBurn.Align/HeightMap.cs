using System.Globalization;
using MathNet.Numerics.LinearAlgebra;
using MillBurn.Core;

namespace MillBurn.Align;

/// <summary>One touch: where the probe went down, and how high the surface turned out to be.</summary>
/// <param name="At">Where in the plane, in work coordinates.</param>
/// <param name="ZNm">The surface height there. Zero means "exactly where work zero says it is".</param>
public readonly record struct ProbeSample(Point2 At, long ZNm);

/// <summary>What kind of surface the samples could support.</summary>
public enum HeightMapFit
{
    /// <summary>One sample, or several that all agree: a flat offset.</summary>
    Constant,

    /// <summary>Too few samples, or all in a line — a least-squares plane is all that is justified.</summary>
    Plane,

    /// <summary>Thin-plate spline through every sample.</summary>
    Spline,
}

/// <summary>How to read a set of probe results.</summary>
public sealed record HeightMapOptions
{
    /// <summary>
    /// A point the surface is defined to pass through at exactly zero, or null to take the
    /// numbers as they are.
    ///
    /// Defaults to the board's own origin corner, because that is where work zero is
    /// (Help/faq.html, "Where is work zero?") and therefore the one place the surface height is
    /// known in advance. It does two jobs at once: it corrects a log recorded in machine
    /// coordinates — GRBL's <c>[PRB:]</c> reports are — and it absorbs a touch-off that was a few
    /// hundredths out. Either way the offset applied is reported rather than hidden.
    /// </summary>
    public Point2? ZeroAt { get; init; } = Point2.Origin;

    /// <summary>
    /// How far to relax the fit, from 0 (through every sample) to about 1 (barely more than a
    /// plane).
    ///
    /// A dial rather than a distance, and deliberately not called millimetres: the term goes on the
    /// diagonal of the spline's system, where it is only meaningful against the size of the kernel
    /// entries beside it — a figure that depends on how far apart the probe points are. It is
    /// scaled by those entries here so that the same number means the same amount of relaxation on
    /// a 20 mm board and a 200 mm one.
    ///
    /// Worth turning up because a touch probe repeats to perhaps 5 µm, and a surface forced exactly
    /// through noise that size ripples between the samples in a way the board does not.
    /// </summary>
    public double Smoothing { get; init; }

    /// <summary>Samples closer together than this are treated as one. Duplicates make the fit singular.</summary>
    public double MergeWithinMm { get; init; } = 0.05;
}

/// <summary>
/// The measured shape of a piece of stock, and what it is safe to say about places between the
/// measurements.
///
/// Isolation milling cuts 0.05 mm deep. A 100 mm board that is 0.15 mm out of flat — which is an
/// ordinary piece of FR4 held down by clamps at its corners — will not cut at all across a third of
/// its area and will cut through the copper across another third. No amount of care with the
/// toolpath fixes that; the only fix is to measure the surface and follow it.
///
/// **We do not drive the machine** ([01 §1.1](../../Documentation/01-Architecture.md)). The probing
/// routine is emitted as a file the operator runs in their own sender, and the results come back as
/// a log we import. That also means a map probed by anything — UGS, Candle, bCNC, a text file typed
/// by hand — is as good as one from a routine we wrote.
/// </summary>
public sealed class HeightMap
{
    private readonly Point2[] _points;
    private readonly double[] _weights;
    private readonly double _a0;
    private readonly double _ax;
    private readonly double _ay;
    private readonly Point2[] _hull;

    private HeightMap(
        Point2[] points,
        double[] weights,
        double a0,
        double ax,
        double ay,
        Point2[] hull,
        HeightMapFit fit,
        Bounds bounds,
        long minZ,
        long maxZ,
        long offset,
        IReadOnlyList<string> notes)
    {
        _points = points;
        _weights = weights;
        _a0 = a0;
        _ax = ax;
        _ay = ay;
        _hull = hull;

        Fit = fit;
        Bounds = bounds;
        MinZNm = minZ;
        MaxZNm = maxZ;
        AppliedOffsetNm = offset;
        Notes = notes;
        PointCount = points.Length;
    }

    public HeightMapFit Fit { get; }

    /// <summary>The area actually measured. Outside it, the map is guessing (see <see cref="OutsideByMm"/>).</summary>
    public Bounds Bounds { get; }

    public int PointCount { get; }

    /// <summary>Lowest and highest measured surface height, after any normalisation.</summary>
    public long MinZNm { get; }

    public long MaxZNm { get; }

    /// <summary>How far out of flat the stock is, over the measured area.</summary>
    public double RangeMm => (MaxZNm - MinZNm) / (double)Nm.PerMillimetre;

    /// <summary>What <see cref="HeightMapOptions.ZeroAt"/> shifted the whole map by, if anything.</summary>
    public long AppliedOffsetNm { get; }

    /// <summary>Things worth saying out loud about this map. Never fatal, always shown.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>
    /// The surface height at a point, in nanometres.
    ///
    /// Outside the measured area the query is pulled back to the nearest measured edge first. A
    /// thin-plate spline has no opinion at all about ground it has not been shown, and its linear
    /// term will happily carry a tilt off into space; holding the edge value is the conservative
    /// answer, and <see cref="OutsideByMm"/> says when it happened so the caller can complain.
    /// </summary>
    public long SampleNm(Point2 at)
    {
        var q = Clamp(at);

        var x = q.X / (double)Nm.PerMillimetre;
        var y = q.Y / (double)Nm.PerMillimetre;

        var z = _a0 + (_ax * x) + (_ay * y);

        for (var i = 0; i < _points.Length; i++)
        {
            var dx = x - (_points[i].X / (double)Nm.PerMillimetre);
            var dy = y - (_points[i].Y / (double)Nm.PerMillimetre);

            z += _weights[i] * Kernel((dx * dx) + (dy * dy));
        }

        return (long)Math.Round(z * Nm.PerMillimetre);
    }

    /// <summary>How far outside the measured area a point is, in millimetres. Zero when inside.</summary>
    public double OutsideByMm(Point2 at) => at.DistanceTo(Clamp(at)) / Nm.PerMillimetre;

    /// <summary>
    /// Builds a map from probe results.
    /// </summary>
    /// <remarks>
    /// The fit degrades honestly rather than failing. Three samples not in a line support a spline;
    /// fewer, or collinear ones, support only a plane; one supports only an offset. Claiming a
    /// surface the measurements cannot justify is how autolevelling produces a board that is worse
    /// than the unlevelled one.
    /// </remarks>
    public static HeightMap Build(IReadOnlyList<ProbeSample> samples, HeightMapOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        options ??= new HeightMapOptions();

        var notes = new List<string>();
        var merged = Merge(samples, Nm.FromMillimetres(options.MergeWithinMm), notes);

        if (merged.Count == 0)
        {
            throw new ArgumentException("A height map needs at least one probe point.", nameof(samples));
        }

        var map = Solve(merged, options, notes);
        Steepness(map, notes);

        if (options.ZeroAt is not { } zeroAt)
        {
            return map;
        }

        // Re-fit with every sample shifted, rather than shifting the result: the spline is linear
        // in its values, so the two agree, and shifting the inputs keeps every reported number —
        // Min, Max, the samples themselves — in one consistent frame.
        var offset = map.SampleNm(zeroAt);

        if (offset == 0)
        {
            return map;
        }

        var shifted = merged.Select(s => s with { ZNm = s.ZNm - offset }).ToList();
        var moved = Solve(shifted, options with { ZeroAt = null }, notes);

        var offsetMm = offset / (double)Nm.PerMillimetre;

        // Millimetres out at the origin is not a touch-off that drifted; it is a log recorded in
        // machine coordinates, which is what GRBL's [PRB:] reports are. Correcting it is right,
        // and saying nothing about it would not be.
        notes.Add(Math.Abs(offsetMm) > 2
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Shifted by {offsetMm:F3} mm to put zero at the origin — the log looks like machine coordinates.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Shifted by {offsetMm:F3} mm to put zero at the origin."));

        return new HeightMap(
            moved._points, moved._weights, moved._a0, moved._ax, moved._ay, moved._hull,
            moved.Fit, moved.Bounds, moved.MinZNm, moved.MaxZNm, offset, notes);
    }

    /// <summary>
    /// Says so when the stock is bowed steeply for its size.
    ///
    /// Judged against the probed span rather than against a fixed number of millimetres, because
    /// the same 0.15 mm is unremarkable across a 200 mm panel and a great deal across a 40 mm
    /// coupon. Deflection goes as the square of the span for a given curvature, so a small piece
    /// that is bowed at all is usually being *held* badly rather than being badly made.
    ///
    /// The reason it is worth a sentence: a map is the shape of the stock **as it was probed**. If
    /// only the edges are stuck down, what the map records is partly the sag of an unsupported
    /// middle — and the cutter, which pushes where the probe did not, meets a different shape. The
    /// correction is then applied to a board that is no longer there.
    /// </summary>
    private static void Steepness(HeightMap map, List<string> notes)
    {
        var range = map.MaxZNm - map.MinZNm;

        // Below this the reading is probe repeatability as much as it is the board.
        if (range < Nm.FromMillimetres(0.05))
        {
            return;
        }

        var span = Math.Sqrt(
            ((double)map.Bounds.Width * map.Bounds.Width) + ((double)map.Bounds.Height * map.Bounds.Height));

        if (span <= 0 || range / span < 1.0 / 500)
        {
            return;
        }

        var measured = string.Create(
            CultureInfo.InvariantCulture,
            $"{range / (double)Nm.PerMillimetre:F3} mm of bow across {span / Nm.PerMillimetre:F0} mm");

        notes.Add(measured
            + " is steep for a piece this size. Check the stock is held down across its whole area, "
            + "not just at the edges: a map of a board taped only at the corners is partly a map of "
            + "the sag between them, and the cutter presses where the probe did not.");
    }

    // ------------------------------------------------------------------ fitting

    private static HeightMap Solve(
        List<ProbeSample> samples, HeightMapOptions options, List<string> notes)
    {
        var bounds = samples.Aggregate(Bounds.Empty, (b, s) => b.Include(s.At));
        var minZ = samples.Min(s => s.ZNm);
        var maxZ = samples.Max(s => s.ZNm);
        var points = samples.Select(s => s.At).ToArray();
        var hull = ConvexHull(points);

        HeightMap Flat(double a0, double ax, double ay, HeightMapFit kind) => new(
            points, new double[points.Length], a0, ax, ay, hull, kind, bounds, minZ, maxZ, 0, notes);

        if (samples.Count == 1)
        {
            return Flat(samples[0].ZNm / (double)Nm.PerMillimetre, 0, 0, HeightMapFit.Constant);
        }

        // A spline needs three points that are not in a line: its system carries a plane, and a
        // plane through collinear points is not determined. Probing a single row is a reasonable
        // thing for someone to do on a long thin board, and it should give a tilt, not a failure.
        if (samples.Count < 3 || IsCollinear(points))
        {
            var (p0, px, py) = FitPlane(samples);

            notes.Add(samples.Count < 3
                ? $"Only {samples.Count} probe points: fitted a tilted plane, not a surface."
                : "Every probe point is in a line: fitted a tilted plane, not a surface.");

            return Flat(p0, px, py, HeightMapFit.Plane);
        }

        if (Spline(samples, options.Smoothing) is not { } solved)
        {
            var (p0, px, py) = FitPlane(samples);
            notes.Add("The spline fit was singular; fell back to a least-squares plane.");
            return Flat(p0, px, py, HeightMapFit.Plane);
        }

        var (weights, a0, ax, ay) = solved;

        return new HeightMap(
            points, weights, a0, ax, ay, hull,
            HeightMapFit.Spline, bounds, minZ, maxZ, 0, notes);
    }

    /// <summary>
    /// The thin-plate spline: the surface a thin sheet of metal would take if it were forced
    /// through every measured point and otherwise left alone.
    ///
    /// Chosen over bilinear interpolation on a grid because bilinear leaves creases along every
    /// grid line, and a crease in the Z of a cut is a visible line in the copper. The system is
    /// <c>[[K P],[Pᵀ 0]] [w a]ᵀ = [z 0]ᵀ</c> with <c>K_ij = U(r²)</c>; adding the smoothing term to
    /// K's diagonal relaxes it from interpolation towards approximation.
    /// </summary>
    private static (double[] Weights, double A0, double Ax, double Ay)? Spline(
        List<ProbeSample> samples, double smoothing)
    {
        var n = samples.Count;

        // Millimetres, not nanometres. r² log r² on values around 1e8 loses every digit that
        // matters before the solve even starts.
        var x = samples.Select(s => s.At.X / (double)Nm.PerMillimetre).ToArray();
        var y = samples.Select(s => s.At.Y / (double)Nm.PerMillimetre).ToArray();

        var a = Matrix<double>.Build.Dense(n + 3, n + 3);
        var b = Vector<double>.Build.Dense(n + 3);

        var kernelTotal = 0.0;
        var kernelCount = 0;

        for (var i = 0; i < n; i++)
        {
            b[i] = samples[i].ZNm / (double)Nm.PerMillimetre;

            for (var j = 0; j < n; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var dx = x[i] - x[j];
                var dy = y[i] - y[j];

                a[i, j] = Kernel((dx * dx) + (dy * dy));

                kernelTotal += Math.Abs(a[i, j]);
                kernelCount++;
            }

            a[i, n] = 1;
            a[i, n + 1] = x[i];
            a[i, n + 2] = y[i];

            a[n, i] = 1;
            a[n + 1, i] = x[i];
            a[n + 2, i] = y[i];
        }

        // The relaxation goes on the diagonal, scaled by the size of the entries it sits among.
        // Written as a bare number it would mean nothing: on a grid 5 mm apart the off-diagonal
        // entries are around 40, and on one 50 mm apart around 20,000, so the same constant would
        // be heavy smoothing on the first board and no smoothing at all on the second.
        var scale = kernelCount > 0 ? kernelTotal / kernelCount : 1;

        for (var i = 0; i < n; i++)
        {
            a[i, i] = smoothing * scale;
        }

        Vector<double> solution;

        try
        {
            solution = a.Solve(b);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return null;
        }

        if (solution.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
        {
            return null;
        }

        return (solution.SubVector(0, n).ToArray(), solution[n], solution[n + 1], solution[n + 2]);
    }

    /// <summary>U(r) = r² ln r², the 2D biharmonic kernel. Zero at zero, where the log diverges.</summary>
    private static double Kernel(double rSquared) =>
        rSquared <= 1e-12 ? 0 : rSquared * Math.Log(rSquared);

    /// <summary>Least squares through whatever is there. Falls back to a level plane if degenerate.</summary>
    private static (double A0, double Ax, double Ay) FitPlane(List<ProbeSample> samples)
    {
        var n = samples.Count;
        var a = Matrix<double>.Build.Dense(n, 3);
        var b = Vector<double>.Build.Dense(n);

        for (var i = 0; i < n; i++)
        {
            a[i, 0] = 1;
            a[i, 1] = samples[i].At.X / (double)Nm.PerMillimetre;
            a[i, 2] = samples[i].At.Y / (double)Nm.PerMillimetre;
            b[i] = samples[i].ZNm / (double)Nm.PerMillimetre;
        }

        try
        {
            var solution = a.Solve(b);

            if (!solution.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            {
                return (solution[0], solution[1], solution[2]);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Falls through to the flat average below.
        }

        return (samples.Average(s => s.ZNm) / Nm.PerMillimetre, 0, 0);
    }

    // ------------------------------------------------------------------ the measured area

    /// <summary>Pulls a point back onto the measured area, or returns it unchanged if it is inside.</summary>
    private Point2 Clamp(Point2 at)
    {
        if (_hull.Length < 3)
        {
            return _hull.Length == 0 ? at : NearestOnPath(at, _hull);
        }

        var inside = true;

        for (var i = 0; i < _hull.Length && inside; i++)
        {
            var a = _hull[i];
            var b = _hull[(i + 1) % _hull.Length];

            // The hull is counter-clockwise, so a point inside is left of every edge.
            inside = Cross(a, b, at) >= 0;
        }

        return inside ? at : NearestOnPath(at, _hull, closed: true);
    }

    private static Point2 NearestOnPath(Point2 at, Point2[] path, bool closed = false)
    {
        var best = path[0];
        var bestDistance = double.MaxValue;
        var last = closed ? path.Length : path.Length - 1;

        for (var i = 0; i < last; i++)
        {
            var candidate = NearestOnSegment(at, path[i], path[(i + 1) % path.Length]);
            var distance = at.DistanceTo(candidate);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private static Point2 NearestOnSegment(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared <= 0)
        {
            return a;
        }

        var t = (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        return new Point2((long)Math.Round(a.X + (t * dx)), (long)Math.Round(a.Y + (t * dy)));
    }

    /// <summary>Andrew's monotone chain, counter-clockwise.</summary>
    private static Point2[] ConvexHull(Point2[] points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();

        if (sorted.Length < 3)
        {
            return sorted;
        }

        var hull = new List<Point2>(sorted.Length * 2);

        foreach (var p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(p);
        }

        var lower = hull.Count + 1;

        for (var i = sorted.Length - 2; i >= 0; i--)
        {
            var p = sorted[i];

            while (hull.Count >= lower && Cross(hull[^2], hull[^1], p) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(p);
        }

        hull.RemoveAt(hull.Count - 1);

        return hull.Count >= 3 ? [.. hull] : sorted;
    }

    private static double Cross(Point2 a, Point2 b, Point2 c) =>
        (((double)b.X - a.X) * ((double)c.Y - a.Y)) - (((double)b.Y - a.Y) * ((double)c.X - a.X));

    private static bool IsCollinear(Point2[] points)
    {
        // Scaled against the spread, so "in a line" means what it means on this board rather than
        // to the last nanometre: a probe grid one row deep and 200 mm long is a line.
        var bounds = points.Aggregate(Bounds.Empty, (b, p) => b.Include(p));
        var spread = (double)Math.Max(bounds.Width, bounds.Height);
        var tolerance = Math.Max(spread * spread * 1e-6, 1);

        for (var i = 2; i < points.Length; i++)
        {
            if (Math.Abs(Cross(points[0], points[1], points[i])) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Collapses samples that share a spot.
    ///
    /// Two probes at the same place make the spline's system singular, and they turn up honestly:
    /// a log with a repeated point, a grid that lands twice on a corner, a file imported twice.
    /// The later reading wins on the assumption it was the deliberate re-probe.
    /// </summary>
    private static List<ProbeSample> Merge(
        IReadOnlyList<ProbeSample> samples, long withinNm, List<string> notes)
    {
        var kept = new List<ProbeSample>(samples.Count);
        var dropped = 0;

        foreach (var sample in samples)
        {
            var at = kept.FindIndex(k => k.At.DistanceTo(sample.At) <= withinNm);

            if (at >= 0)
            {
                kept[at] = sample;
                dropped++;
                continue;
            }

            kept.Add(sample);
        }

        if (dropped > 0)
        {
            notes.Add($"{dropped} probe point(s) repeated a spot already measured; kept the later reading.");
        }

        return kept;
    }
}
