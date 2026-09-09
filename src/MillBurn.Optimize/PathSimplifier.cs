using MillBurn.Core;
using MillBurn.Geometry;

namespace MillBurn.Optimize;

/// <summary>How far a simplified path may stray from the one it replaces.</summary>
public sealed record SimplifyOptions
{
    /// <summary>
    /// The most a simplified path may deviate, in nanometres.
    ///
    /// Two microns, not the five the design sketch suggested. The number that matters is not how
    /// accurate the shape looks but how much of the *clearance* it spends: an isolation cut is
    /// offset half a tool-width from the copper, and every micron of simplification is a micron of
    /// that margin given away. Two microns is under two percent of a 0.127 mm cut, sits below the
    /// positional repeatability of the machines this targets, and still removes most of the file.
    /// </summary>
    public long ToleranceNm { get; init; } = Nm.FromMillimetres(0.002);

    /// <summary>Fit arcs as well as dropping points.</summary>
    public bool FitArcs { get; init; } = true;

    /// <summary>
    /// How many points an arc must span to be worth making.
    ///
    /// Low values turn measurement noise into arcs of implausible radius; a controller that takes
    /// one of those literally cuts a shape nobody drew.
    /// </summary>
    public int MinimumArcPoints { get; init; } = 5;

    /// <summary>Simplification off. Useful for proving what it changed.</summary>
    public static SimplifyOptions None { get; } = new() { ToleranceNm = 0, FitArcs = false };
}

/// <summary>What simplification did, so the size of the win is visible rather than claimed.</summary>
public sealed record SimplifyResult
{
    public required int SegmentsBefore { get; init; }

    public required int SegmentsAfter { get; init; }

    public required int Arcs { get; init; }

    /// <summary>Cut length before and after, to prove the shape did not change materially.</summary>
    public required double LengthBeforeMm { get; init; }

    public required double LengthAfterMm { get; init; }

    public double Reduction => SegmentsBefore <= 0 ? 0 : 1.0 - ((double)SegmentsAfter / SegmentsBefore);
}

/// <summary>
/// Applies <see cref="Simplify"/> to a whole toolpath.
///
/// Separate from the geometry so the policy — what tolerance, whether to fit arcs, what counts as
/// an arc worth making — is stated in one place and can be argued with, rather than being buried in
/// the maths that carries it out.
/// </summary>
public static class PathSimplifier
{
    public static (Toolpath Simplified, SimplifyResult Result) Apply(
        Toolpath toolpath, SimplifyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(toolpath);
        options ??= new SimplifyOptions();

        var before = toolpath.Passes.Sum(p => p.Path.Count);
        var lengthBefore = toolpath.Passes.Sum(p => p.LengthNm) / Nm.PerMillimetre;

        var passes = toolpath.Passes.Count == 0
            ? toolpath.Passes
            : [.. toolpath.Passes.Select(p => p with { Path = Reduce(p.Path, p.Closed, options) })];

        return (
            toolpath with { Passes = passes },
            new SimplifyResult
            {
                SegmentsBefore = before,
                SegmentsAfter = passes.Sum(p => p.Path.Count),
                Arcs = passes.Sum(p => p.Path.Count(s => s.IsArc)),
                LengthBeforeMm = lengthBefore,
                LengthAfterMm = passes.Sum(p => p.LengthNm) / Nm.PerMillimetre,
            });
    }

    /// <summary>
    /// One path: points out, simplified, arcs fitted, segments back.
    ///
    /// Anything already carrying an arc is left alone. Those came from the Gerber and are exact;
    /// flattening them to re-fit them could only lose accuracy.
    /// </summary>
    private static IReadOnlyList<ArtSegment> Reduce(
        IReadOnlyList<ArtSegment> path, bool closed, SimplifyOptions options)
    {
        if (path.Count < 2 || options.ToleranceNm <= 0 || path.Any(s => s.IsArc))
        {
            return path;
        }

        var points = new List<Point2>(path.Count + 1);
        points.Add(path[0].From);
        foreach (var segment in path)
        {
            points.Add(segment.To);
        }

        // Half the budget each, because the two stages compose: an arc within its tolerance of a
        // point that was itself within tolerance of the original sits at up to the sum of the two
        // from where the board actually needs the cutter. Splitting makes ToleranceNm the bound
        // that holds end to end rather than one that is quietly doubled.
        var half = Math.Max(options.ToleranceNm / 2, 1);
        var simplified = Simplify.DouglasPeucker(points, half);

        // A closed contour must still close. Douglas–Peucker keeps the first and last points, and
        // they are the same point here, so this holds — but a contour that has been reduced to a
        // degenerate sliver has to be dropped rather than emitted as a cut to nowhere.
        if (simplified.Count < (closed ? 4 : 2))
        {
            return path;
        }

        var segments = options.FitArcs
            ? Simplify.FitArcs(simplified, half, options.MinimumArcPoints)
            : Lines(simplified);

        return segments.Count == 0 ? path : segments;
    }

    private static List<ArtSegment> Lines(IReadOnlyList<Point2> points)
    {
        var segments = new List<ArtSegment>(points.Count - 1);
        for (var i = 0; i < points.Count - 1; i++)
        {
            segments.Add(ArtSegment.Line(points[i], points[i + 1]));
        }

        return segments;
    }
}
