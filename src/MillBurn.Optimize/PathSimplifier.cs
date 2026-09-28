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
    /// How far a fitted arc may stray from the middle of a segment it replaces.
    ///
    /// **An arc replaces segments, not points**, and checked only at its vertices it is
    /// unconstrained everywhere between them. On the Arduino Mega that let eight arcs bow across
    /// straight runs — the worst of them 464 µm off a twelve-millimetre segment, cutting into copper
    /// that was supposed to stay.
    ///
    /// Ten microns, and the number is measured rather than chosen. Across the 4,246 arcs fitted to
    /// that board's top copper, the honest ones stray 1.1 µm at the median and 4.6 µm at the 99th
    /// percentile; the eight bad ones stray 74 to 464 µm. **Nothing at all lies between 5 and
    /// 74 µm**, so this sits in an empty gap: twice the worst honest arc, a seventh of the mildest
    /// bad one.
    ///
    /// Looser than <see cref="ToleranceNm"/> on purpose. The points arrive already thinned, so a
    /// retained chord spans several original samples and its middle sits below the true curve by
    /// construction — judging it as strictly as a vertex rejects a third of the arcs on a real
    /// board, which is not a fix but a trade: a wrong curve for a program half as large again.
    ///
    /// **So the end-to-end bound is not <see cref="ToleranceNm"/> alone**, and saying otherwise
    /// would be the kind of stated guarantee nobody checks. A vertex stays within `ToleranceNm` of
    /// the original; the middle of a chord may sit five times that away. At the default that is
    /// 10 µm, which is about eight percent of a 0.127 mm cut rather than the two percent the vertex
    /// bound buys — worth knowing before anyone tightens `ToleranceNm` expecting the whole error to
    /// follow it down.
    ///
    /// **So it really is a multiple, not a constant that happens to equal one.** It was written as
    /// the literal `0.002 mm × 5` and read as scaling, which is a trap rather than a bug: today only
    /// the default is used, and the two numbers agree. A caller who loosens `ToleranceNm` to 0.05 mm
    /// for a rough pass would get a 25 µm vertex bound against a 10 µm chord bound — the chord check
    /// stricter than the vertex one, the documented order inverted, and honest arcs refused. One who
    /// tightens it to 0.5 µm would leave the chord free to stray forty times further than the number
    /// they set. Overriding it is still allowed, and then it is exactly what was asked for.
    /// </summary>
    public long ChordToleranceNm
    {
        // Floored, so that `None with { FitArcs = true }` — the one composition that reaches the
        // fitter with no vertex tolerance to scale from — cannot hand it a bound of zero, which
        // would refuse every candidate and quietly emit line moves instead. `Reduce` returns before
        // that today, and this is so it does not have to be the only thing that does.
        get => _chordToleranceNm ?? (ToleranceNm > long.MaxValue / ChordMultiple
            ? long.MaxValue
            : Math.Max(ToleranceNm * ChordMultiple, 1));

        // Refused here rather than in `Simplify.FitArcs`, which would throw part-way through an
        // export and name a parameter the caller never wrote.
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _chordToleranceNm = value;
        }
    }

    /// <summary>
    /// How much looser the chord bound is than the vertex bound. See above for the five.
    ///
    /// **Derived from `Simplify`'s own default rather than written beside it.** The two have to
    /// agree, and today they do — but only because `Reduce` splits the budget and hands the fitter
    /// half of this tolerance, so ten times a half is five times the whole. Written as two
    /// literals they would be equal by coincidence, and the first change to that split would part
    /// them by a factor of two, in the loosening direction, with every test still green.
    /// </summary>
    public const int ChordMultiple = Simplify.DefaultChordMultiple / BudgetShare;

    /// <summary>
    /// The division above has to come out whole, or the derivation quietly lies.
    ///
    /// Integer division rounds: make `DefaultChordMultiple` 15 and this becomes 7 rather than 7.5,
    /// a chord bound seven per cent tighter than the geometry layer's own — in the direction that
    /// refuses honest arcs, and with nothing able to notice. Checked here rather than in a test
    /// because it is a fact about two constants: dividing by zero in a constant expression is a
    /// compile error, so it fails at the edit that breaks it rather than at the next full run.
    /// </summary>
    private const int WholeOrTheDerivationIsWrong =
        1 / (Simplify.DefaultChordMultiple % BudgetShare == 0 ? 1 : 0);

    /// <summary>
    /// How the tolerance is split between thinning and fitting. `Reduce` gives each half, because
    /// the two stages compose — see the comment there, which is where the number is spent.
    /// </summary>
    public const int BudgetShare = 2;

    private readonly long? _chordToleranceNm;

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

    /// <summary>Nothing simplified yet: the identity to fold several toolpaths onto.</summary>
    public static SimplifyResult Nothing { get; } = new()
    {
        SegmentsBefore = 0,
        SegmentsAfter = 0,
        Arcs = 0,
        LengthBeforeMm = 0,
        LengthAfterMm = 0,
    };

    /// <summary>
    /// Two toolpaths' worth of simplification, reported as one.
    ///
    /// A drilling operation is one file but several toolpaths, one per bit, and the operator wants
    /// a line about the file rather than a line per bit.
    /// </summary>
    public static SimplifyResult operator +(SimplifyResult a, SimplifyResult b) => new()
    {
        SegmentsBefore = a.SegmentsBefore + b.SegmentsBefore,
        SegmentsAfter = a.SegmentsAfter + b.SegmentsAfter,
        Arcs = a.Arcs + b.Arcs,
        LengthBeforeMm = a.LengthBeforeMm + b.LengthBeforeMm,
        LengthAfterMm = a.LengthAfterMm + b.LengthAfterMm,
    };

    public static SimplifyResult Add(SimplifyResult left, SimplifyResult right) => left + right;
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
        var half = Math.Max(options.ToleranceNm / SimplifyOptions.BudgetShare, 1);
        var simplified = Simplify.DouglasPeucker(points, half);

        // A closed contour must still close. Douglas–Peucker keeps the first and last points, and
        // they are the same point here, so this holds — but a contour that has been reduced to a
        // degenerate sliver has to be dropped rather than emitted as a cut to nowhere.
        if (simplified.Count < (closed ? 4 : 2))
        {
            return path;
        }

        var segments = options.FitArcs
            ? Simplify.FitArcs(simplified, half, options.MinimumArcPoints, options.ChordToleranceNm)
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
