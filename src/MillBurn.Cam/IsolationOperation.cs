using System.Globalization;
using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;

namespace MillBurn.Cam;

/// <summary>How to isolate the copper.</summary>
public sealed record IsolationOptions
{
    public Tool Tool { get; init; } = Tool.DefaultVBit;

    /// <summary>
    /// How deep the V-bit runs, which — for a V-bit — is the same thing as choosing the cut width.
    /// </summary>
    public long DepthNm { get; init; } = Nm.FromMillimetres(0.05);

    /// <summary>
    /// Cuts around each island, when the caller is counting laps rather than asking for a width.
    ///
    /// Ignored whenever <see cref="WidthNm"/> is set, which is the normal case — the number of
    /// passes is a consequence of how wide a moat you asked for and how much the bit cuts, in the
    /// same way that the cut width is a consequence of the depth. It stays because a project saved
    /// before widths existed has a pass count and nothing else.
    /// </summary>
    public int Passes { get; init; } = 1;

    /// <summary>
    /// How wide a moat to clear either side of the copper, measured from the copper's edge. Zero
    /// falls back to <see cref="Passes"/>.
    ///
    /// This is the number the operator actually cares about. One pass of a 30° V-bit at 0.05 mm
    /// deep isolates 0.127 mm — electrically fine, and a hairline you cannot see, cannot solder
    /// across without bridging, and can close by handling the board.
    /// </summary>
    public long WidthNm { get; init; }

    /// <summary>
    /// How much each pass overlaps the last, as a fraction of the cut width. Zero would leave
    /// hairline ridges between passes wherever the machine is a few microns out.
    /// </summary>
    public double Overlap { get; init; } = 0.15;

    /// <summary>Extra clearance between the copper edge and the cut, for etch-style margin.</summary>
    public long BiasNm { get; init; }

    public long SagittaNm { get; init; } = Tessellate.DefaultSagittaNm;

    /// <summary>The width this tool actually cuts at this depth.</summary>
    public long EffectiveWidthNm => Tool.WidthAtDepth(DepthNm);

    /// <summary>
    /// How far apart consecutive passes are. Less than a full cut width, so the passes overlap:
    /// stepping by the whole width would leave a hairline ridge between them wherever the machine
    /// is a few microns out, and a ridge of copper in an isolation moat is a short.
    /// </summary>
    public long StepNm => StepFor(EffectiveWidthNm, Overlap);

    /// <summary>
    /// How many passes this actually runs — derived from <see cref="WidthNm"/> when one is asked
    /// for, and taken from <see cref="Passes"/> when it is not.
    /// </summary>
    public int PassCount => WidthNm <= 0
        ? Math.Max(1, Passes)
        : PassesFor(WidthNm, EffectiveWidthNm, Overlap, BiasNm);

    /// <summary>
    /// The moat those passes really clear, which is at least what was asked for and usually a
    /// little more — passes come in whole numbers. Reported rather than assumed, because "you asked
    /// for 0.40 and you are getting 0.45" is the kind of thing an operator wants to be told once
    /// rather than measure later.
    /// </summary>
    public long AchievedWidthNm => ClearedBy(PassCount, EffectiveWidthNm, Overlap, BiasNm);

    /// <summary>Ceiling on how many passes a width may ask for. A backstop, not a design limit.</summary>
    public const int MaxPasses = 64;

    private static long StepFor(long cutNm, double overlap) =>
        Math.Max(1, (long)Math.Round(cutNm * (1.0 - Math.Clamp(overlap, 0, 0.9))));

    /// <summary>What a given number of passes clears, measured from the copper's edge outward.</summary>
    public static long ClearedBy(int passes, long cutNm, double overlap, long biasNm) =>
        cutNm <= 0 ? 0 : cutNm + biasNm + ((Math.Max(1, passes) - 1) * StepFor(cutNm, overlap));

    /// <summary>
    /// Passes needed to clear <paramref name="widthNm"/>. Always at least one, so asking for less
    /// than the bit cuts gives you the one pass you were going to get anyway.
    /// </summary>
    public static int PassesFor(long widthNm, long cutNm, double overlap, long biasNm)
    {
        if (cutNm <= 0)
        {
            return 1;
        }

        var remaining = widthNm - cutNm - biasNm;
        if (remaining <= 0)
        {
            return 1;
        }

        var step = StepFor(cutNm, overlap);
        return (int)Math.Min(MaxPasses, 1 + ((remaining + step - 1) / step));
    }
}

/// <summary>
/// Isolation routing: cut a moat around every copper island so the nets are electrically separate.
///
/// The geometry is one Clipper offset per pass, and the only subtlety is what to offset. The cut
/// must not touch the copper, so the *centreline* of the first pass sits half a cut-width outside
/// the copper boundary — offsetting by the full width would leave a gap of bare board between the
/// cutter and the trace, and offsetting by nothing would cut the trace in half.
///
/// Offsetting the union of all copper, rather than each island separately, is what makes the
/// awkward cases come out right for free: where two traces are closer together than the tool is
/// wide, their offsets merge and no path is produced between them — which is the truth. The tool
/// cannot fit, and inventing a path there would cut both traces.
/// </summary>
public static class IsolationOperation
{
    public static Toolpath Build(Paths64 copper, IsolationOptions options, string label = "Isolation")
    {
        ArgumentNullException.ThrowIfNull(copper);
        ArgumentNullException.ThrowIfNull(options);

        var width = options.EffectiveWidthNm;
        var notes = new List<string>();
        var passes = new List<ToolpathPass>();

        if (width <= 0)
        {
            notes.Add("The tool cuts nothing at this depth.");
            return Empty(options, label, notes);
        }

        var step = options.StepNm;
        var count = options.PassCount;

        for (var pass = 0; pass < count; pass++)
        {
            // Centreline of pass n: half a width clear of the copper, then one step per extra pass.
            var offset = (width / 2) + options.BiasNm + (pass * step);
            var contours = Clipper.InflatePaths(
                copper, offset, JoinType.Round, EndType.Polygon, arcTolerance: options.SagittaNm);

            if (contours.Count == 0)
            {
                notes.Add(Invariant($"Pass {pass + 1} produced nothing; the copper vanishes at this offset."));
                break;
            }

            foreach (var contour in contours)
            {
                if (contour.Count < 3)
                {
                    continue;
                }

                passes.Add(new ToolpathPass
                {
                    Path = ToSegments(contour),
                    DepthNm = options.DepthNm,
                    Closed = true,
                });
            }
        }

        var depthMm = Nm.ToMillimetreString(options.DepthNm, 3);
        var widthMm = Nm.ToMillimetreString(width, 3);
        notes.Add(Invariant($"{options.Tool.Name} at {depthMm} mm deep cuts {widthMm} mm wide."));

        if (options.WidthNm > 0)
        {
            var asked = Nm.ToMillimetreString(options.WidthNm, 3);
            var got = Nm.ToMillimetreString(options.AchievedWidthNm, 3);
            var laps = count == 1 ? "1 pass" : Invariant($"{count} passes");

            notes.Add(Invariant($"{asked} mm of isolation asked for; {laps} clears {got} mm."));

            if (count >= IsolationOptions.MaxPasses)
            {
                notes.Add(Invariant(
                    $"Capped at {IsolationOptions.MaxPasses} passes. Reaching {asked} mm with a {widthMm} mm cut needs more than that; use a wider tool or a deeper cut."));
            }
        }

        if (options.Tool.WidthPerDepth > 0)
        {
            // The number that decides whether this board needs height mapping. A shallower V is
            // more sensitive, not less, which is the opposite of most people's intuition.
            var perTenth = Nm.ToMillimetreString(
                (long)(options.Tool.WidthPerDepth * Nm.FromMillimetres(0.01)), 4);
            notes.Add(Invariant($"0.01 mm of depth error changes that by {perTenth} mm."));
        }

        return new Toolpath
        {
            Kind = ToolpathKind.Isolation,
            Label = label,
            Tool = options.Tool,
            Passes = passes,
            Notes = notes,
        };
    }

    /// <summary>
    /// Where the tool is too wide to fit: the copper, grown by half a cut width, that has merged
    /// with copper it should have stayed clear of.
    ///
    /// This is the check that matters, and it is cheap because it falls out of the same offset the
    /// toolpath uses. Two islands whose grown outlines touch have no path between them, so after
    /// this job runs they are still connected — a short that the picture will not show, because the
    /// toolpath simply has nothing drawn in the gap.
    /// </summary>
    public static int UnreachableGaps(Paths64 copper, IsolationOptions options)
    {
        ArgumentNullException.ThrowIfNull(copper);
        ArgumentNullException.ThrowIfNull(options);

        var islandsBefore = OuterRingCount(Polygons.UnionSelf(copper));

        var grown = Clipper.InflatePaths(
            copper, options.EffectiveWidthNm / 2, JoinType.Round, EndType.Polygon,
            arcTolerance: options.SagittaNm);

        var islandsAfter = OuterRingCount(Polygons.UnionSelf(grown));

        return Math.Max(0, islandsBefore - islandsAfter);
    }

    private static int OuterRingCount(Paths64 paths)
    {
        var outer = 0;
        foreach (var path in paths)
        {
            // Positive area is an island; negative is a hole in one.
            if (Clipper.Area(path) > 0)
            {
                outer++;
            }
        }

        return outer;
    }

    public static IReadOnlyList<ArtSegment> ToSegments(Path64 contour)
    {
        var segments = new ArtSegment[contour.Count];
        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            segments[i] = ArtSegment.Line(new Point2(a.X, a.Y), new Point2(b.X, b.Y));
        }

        return segments;
    }

    private static Toolpath Empty(IsolationOptions options, string label, List<string> notes) => new()
    {
        Kind = ToolpathKind.Isolation,
        Label = label,
        Tool = options.Tool,
        Notes = notes,
    };

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
