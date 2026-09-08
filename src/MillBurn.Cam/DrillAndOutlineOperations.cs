using System.Globalization;
using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Gerber.Excellon;
using MillBurn.Geometry;

namespace MillBurn.Cam;

/// <summary>How to drill.</summary>
public sealed record DrillOptions
{
    /// <summary>How far past the board the drill goes, so the hole breaks through cleanly.</summary>
    public long BreakThroughNm { get; init; } = Nm.FromMillimetres(0.3);

    public long BoardThicknessNm { get; init; } = Nm.FromMillimetres(1.6);

    /// <summary>
    /// Peck depth. Zero drills straight through. FR4 is abrasive and dusty rather than stringy, so
    /// pecking matters less than it does in metal — but a 0.5 mm bit at full depth still packs its
    /// flutes and snaps, and a snapped bit in a plated hole ends the board.
    /// </summary>
    public long PeckNm { get; init; } = Nm.FromMillimetres(0.8);

    public long DepthNm => BoardThicknessNm + BreakThroughNm;
}

/// <summary>How to cut the board out.</summary>
public sealed record OutlineOptions
{
    public Tool Tool { get; init; } = Tool.DefaultOutlineMill;

    public long BoardThicknessNm { get; init; } = Nm.FromMillimetres(1.6);

    public long BreakThroughNm { get; init; } = Nm.FromMillimetres(0.3);

    /// <summary>Depth taken per pass. The whole thickness in one go is what breaks 1 mm cutters.</summary>
    public long DepthPerPassNm { get; init; } = Nm.FromMillimetres(0.4);

    /// <summary>Width of each holding tab.</summary>
    public long TabWidthNm { get; init; } = Nm.FromMillimetres(3.0);

    /// <summary>Material left under a tab, for snapping the board out afterwards.</summary>
    public long TabHeightNm { get; init; } = Nm.FromMillimetres(0.5);

    /// <summary>How many tabs to space around the outline. Zero cuts the board fully free.</summary>
    public int TabCount { get; init; } = 4;

    /// <summary>Cut outside the profile line, so the finished board is nominal size.</summary>
    public bool CutOutside { get; init; } = true;

    public long SagittaNm { get; init; } = Tessellate.DefaultSagittaNm;

    public long TotalDepthNm => BoardThicknessNm + BreakThroughNm;
}

/// <summary>Drilling: one operation per tool, biggest first.</summary>
public static class DrillOperation
{
    /// <summary>
    /// Builds one toolpath per drill size.
    ///
    /// Biggest first, which is the opposite of what feels natural. A small bit wanders when it
    /// starts on a surface that a larger one has already broken, so the usual reason to go
    /// small-to-large does not apply here — and every size change is a manual tool change on a
    /// hobby machine, so the ordering that minimises regret is the one where the delicate bits go
    /// in last and spend the least time in the spindle.
    /// </summary>
    public static IReadOnlyList<Toolpath> Build(
        ExcellonFile drill, DrillOptions options, Tool? template = null)
    {
        ArgumentNullException.ThrowIfNull(drill);
        ArgumentNullException.ThrowIfNull(options);

        var toolpaths = new List<Toolpath>();

        foreach (var (tool, _) in drill.ByTool())
        {
            var hits = drill.Hits.Where(h => h.Tool == tool.Number).ToList();
            if (hits.Count == 0)
            {
                continue;
            }

            var notes = new List<string>();
            if (options.PeckNm > 0 && options.DepthNm > options.PeckNm)
            {
                var peck = Nm.ToMillimetreString(options.PeckNm, 2);
                var depth = Nm.ToMillimetreString(options.DepthNm, 2);
                notes.Add(Invariant($"Pecking {peck} mm at a time through {depth} mm."));
            }

            toolpaths.Add(new Toolpath
            {
                Kind = ToolpathKind.Drill,
                Label = Invariant($"Drill {Nm.ToMillimetreString(tool.DiameterNm, 2)} mm ({hits.Count} holes)"),
                Tool = Tool.DrillOf(tool.DiameterNm, template),
                Drills = [.. hits.Select(h => new DrillTarget(h.At, options.DepthNm, options.PeckNm))],
                Notes = notes,
            });
        }

        return toolpaths;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Cutting the board out, with tabs to hold it while it happens.</summary>
public static class OutlineOperation
{
    /// <summary>
    /// The profile, offset by the cutter radius and stepped down in depth, with gaps left where the
    /// tabs go.
    ///
    /// Two things decide whether this works. The offset must be **outside** the profile, or the
    /// board comes out a cutter-width small on every edge. And the last pass must not be the first
    /// time the board is free: without tabs, the cut-out piece lifts on the cutter somewhere near
    /// the end and is thrown, usually taking the cutter with it.
    /// </summary>
    public static Toolpath Build(Paths64 outline, OutlineOptions options, string label = "Outline")
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(options);

        var radius = options.Tool.DiameterNm / 2;
        var offset = options.CutOutside ? radius : -radius;

        var contours = Clipper.InflatePaths(
            outline, offset, JoinType.Round, EndType.Polygon, arcTolerance: options.SagittaNm);

        var notes = new List<string>();
        var passes = new List<ToolpathPass>();

        if (contours.Count == 0)
        {
            notes.Add("The outline vanished when offset by the cutter radius; the profile may be smaller than the tool.");
            return new Toolpath
            {
                Kind = ToolpathKind.Outline,
                Label = label,
                Tool = options.Tool,
                Notes = notes,
            };
        }

        var steps = Math.Max(1, (int)Math.Ceiling(options.TotalDepthNm / (double)options.DepthPerPassNm));

        for (var step = 1; step <= steps; step++)
        {
            var depth = Math.Min(options.TotalDepthNm, step * options.DepthPerPassNm);

            // Tabs only bite on the passes that would otherwise cut through them.
            var tabbed = options.TabCount > 0 && depth > options.TotalDepthNm - options.TabHeightNm - options.BreakThroughNm;

            foreach (var contour in contours)
            {
                if (contour.Count < 3)
                {
                    continue;
                }

                if (!tabbed)
                {
                    passes.Add(new ToolpathPass
                    {
                        Path = IsolationOperation.ToSegments(contour),
                        DepthNm = depth,
                        Closed = true,
                    });
                    continue;
                }

                foreach (var run in SplitForTabs(contour, options))
                {
                    passes.Add(new ToolpathPass { Path = run, DepthNm = depth, Closed = false });
                }
            }
        }

        var perPass = Nm.ToMillimetreString(options.DepthPerPassNm, 2);
        var total = Nm.ToMillimetreString(options.TotalDepthNm, 2);
        notes.Add(Invariant($"{steps} passes of {perPass} mm to {total} mm."));

        if (options.TabCount > 0)
        {
            var tabWidth = Nm.ToMillimetreString(options.TabWidthNm, 1);
            var tabHeight = Nm.ToMillimetreString(options.TabHeightNm, 2);
            notes.Add(Invariant(
                $"{options.TabCount} tabs, {tabWidth} mm wide, {tabHeight} mm of material left under each."));
        }
        else
        {
            notes.Add("No tabs: the board comes free on the last pass. Hold it down.");
        }

        return new Toolpath
        {
            Kind = ToolpathKind.Outline,
            Label = label,
            Tool = options.Tool,
            Passes = passes,
            Notes = notes,
        };
    }

    /// <summary>
    /// Breaks a closed contour into the runs between tabs, spaced evenly by arc length.
    ///
    /// By length rather than by vertex, because a contour from an offset has vertices bunched at
    /// the corners: spacing tabs by index would put most of them on one corner and leave a long
    /// edge unsupported.
    /// </summary>
    private static IEnumerable<IReadOnlyList<ArtSegment>> SplitForTabs(Path64 contour, OutlineOptions options)
    {
        var segments = IsolationOperation.ToSegments(contour);

        var perimeter = 0.0;
        foreach (var s in segments)
        {
            perimeter += s.From.DistanceTo(s.To);
        }

        if (perimeter <= 0 || options.TabWidthNm * options.TabCount >= perimeter)
        {
            // The tabs would consume the whole outline. Cutting it as one closed pass is wrong, but
            // so is emitting nothing, and a caller can see the note.
            yield return segments;
            yield break;
        }

        var spacing = perimeter / options.TabCount;
        var half = options.TabWidthNm / 2.0;

        var run = new List<ArtSegment>();
        var travelled = 0.0;

        foreach (var segment in segments)
        {
            var length = segment.From.DistanceTo(segment.To);
            var midpoint = travelled + (length / 2);

            // Distance from this segment's midpoint to the nearest tab centre.
            var nearest = Math.Abs(((midpoint + (spacing / 2)) % spacing) - (spacing / 2));

            if (nearest < half)
            {
                if (run.Count > 0)
                {
                    yield return run;
                    run = [];
                }
            }
            else
            {
                run.Add(segment);
            }

            travelled += length;
        }

        if (run.Count > 0)
        {
            yield return run;
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
