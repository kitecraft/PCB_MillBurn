using Clipper2Lib;
using MillBurn.Core;

namespace MillBurn.Cam;

/// <summary>Where one holding tab goes, how wide it is, and how much material it leaves.</summary>
public sealed record PlacedTab
{
    /// <summary>Distance from the profile's start vertex to the tab's centre, along the path.</summary>
    public required double AlongNm { get; init; }

    /// <summary>The gap left in the cut, measured along the path.</summary>
    public required long WidthNm { get; init; }

    /// <summary>Material left under it, for snapping the board out afterwards.</summary>
    public required long HeightNm { get; init; }

    /// <summary>Where the centre of the tab is, for the report and for the picture.</summary>
    public required Point2 At { get; init; }

    /// <summary>
    /// Which straight edge of the profile this tab sits on, counted round the profile from its start
    /// vertex, or <c>-1</c> for a tab that was spaced round a profile with no edge long enough to
    /// hold one.
    /// </summary>
    public required int Edge { get; init; }

    /// <summary>How long that edge is, which is what decides how many tabs it can hold.</summary>
    public required double EdgeLengthNm { get; init; }

    /// <summary>True when nothing about the profile chose this position — see <see cref="Edge"/>.</summary>
    public bool Spaced => Edge < 0;
}

/// <summary>
/// What an outline’s tabs are, and what the board would have made them.
///
/// The second pair is what lets a dialog tell "left alone" from "set to the same number", and
/// what it shows beside the boxes so that "the board decides" is a figure somebody can see.
/// </summary>
/// <param name="Tabs">The tabs, in the order they come round the profile.</param>
/// <param name="DerivedWidthNm">The width this profile would give a tab.</param>
/// <param name="DerivedHeightNm">The material this thickness would leave under one.</param>
public sealed record TabPlanning(
    IReadOnlyList<PlacedTab> Tabs,
    long DerivedWidthNm,
    long DerivedHeightNm);

/// <summary>One tab's width or height, set by hand rather than derived.</summary>
public sealed record TabEdit
{
    /// <summary>Which tab, counted round the profile from its start vertex, from zero.</summary>
    public required int Index { get; init; }

    /// <summary>Null leaves the derived width alone.</summary>
    public long? WidthNm { get; init; }

    /// <summary>Null leaves the derived height alone.</summary>
    public long? HeightNm { get; init; }
}

/// <summary>
/// Where the tabs go, decided from the board rather than from a count and a seam.
///
/// **What this replaces, and why.** Tabs used to be a count plus two global numbers: the perimeter
/// was divided by <see cref="OutlineOptions.TabCount"/> and a tab went every <c>spacing</c> from
/// whatever vertex the offset happened to start at, each the same width and each leaving the same
/// material. Nothing looked at the board. Reported from the workshop on a 21 x 37 mm board, after
/// the bug that lost two of four tabs was fixed and all four appeared: *"They are large tabs and may
/// not be in the best locations."*
///
/// Equal arc length from an arbitrary seam puts a tab wherever it lands — across a corner, which is
/// the one place a tab is hardest to clean up, or on the edge a connector sits on. **One tab centred
/// on each straight edge is what anybody draws by hand**, and on a rectangle that is exactly what
/// the default four produce.
///
/// Recorded in [06 §6.4](../../Documentation/06-Roadmap-and-Risks.md).
/// </summary>
public static class TabPlan
{
    /// <summary>
    /// Narrowest tab worth cutting: below this it is a nick that snaps while the board is still
    /// being cut.
    /// </summary>
    public static long MinWidthNm { get; } = Nm.FromMillimetres(1.5);

    /// <summary>
    /// Widest tab worth cutting. Past this the tab stops being something a hand can break and
    /// becomes something a saw has to, and every millimetre of it is copper the cutter did not free.
    /// </summary>
    public static long MaxWidthNm { get; } = Nm.FromMillimetres(5.0);

    /// <summary>Thinnest web that survives the rest of the cut.</summary>
    public static long MinHeightNm { get; } = Nm.FromMillimetres(0.2);

    /// <summary>Thickest web a hand can still break without tearing the laminate.</summary>
    public static long MaxHeightNm { get; } = Nm.FromMillimetres(0.6);

    /// <summary>
    /// How wide a tab should be on this profile: a tenth of the board's narrow side.
    ///
    /// **3 mm is a lot on a 21 mm edge and not much on a 100 mm one**, which is the half of the
    /// workshop's report that a fixed default cannot answer. The narrow side rather than the
    /// perimeter, because it is the dimension that decides whether a tab looks like a tab or like a
    /// bridge: a 21 x 37 mm board gets 2.0 mm, a 30 x 50 mm one gets 3.0 mm, and anything from
    /// 50 mm up is capped at <see cref="MaxWidthNm"/>.
    ///
    /// Rounded to the nearest half millimetre so the number in the program is one somebody can hold
    /// in their head and check with a rule.
    /// </summary>
    public static long DefaultWidthFor(Path64 contour)
    {
        ArgumentNullException.ThrowIfNull(contour);

        if (contour.Count == 0)
        {
            return Nm.FromMillimetres(3.0);
        }

        var minX = long.MaxValue;
        var minY = long.MaxValue;
        var maxX = long.MinValue;
        var maxY = long.MinValue;

        foreach (var point in contour)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        var narrow = Math.Min(maxX - minX, maxY - minY);
        var half = Nm.FromMillimetres(0.5);
        var rounded = (long)Math.Round(narrow / 10.0 / half) * half;

        return Math.Clamp(rounded, MinWidthNm, MaxWidthNm);
    }

    /// <summary>
    /// How much material a tab should leave: a third of the board's thickness.
    ///
    /// **0.5 mm under a 0.8 mm board is most of the board.** The old fixed default was chosen on
    /// 1.6 mm laminate, where a third is 0.53 mm and the old number was right; on thin stock it left
    /// a web thicker than the cut above it, which does not snap so much as tear. Rounded *down* to
    /// five hundredths, so the program never promises a thinner web than it leaves, and clamped at
    /// both ends: under <see cref="MinHeightNm"/> a tab breaks while the cutter is still working,
    /// over <see cref="MaxHeightNm"/> it needs a saw.
    ///
    /// 1.6 mm gives 0.50 mm, which is what this default was before it depended on anything — so an
    /// ordinary board's programs do not move.
    /// </summary>
    public static long DefaultHeightFor(long thicknessNm)
    {
        var step = Nm.FromMillimetres(0.05);
        var floored = thicknessNm / 3 / step * step;

        return Math.Clamp(floored, MinHeightNm, MaxHeightNm);
    }

    /// <summary>
    /// The tabs for one closed profile, in the order they come round it.
    ///
    /// Empty when none are asked for, and empty when they would consume the whole profile — the
    /// caller cuts it as one pass and says so, which is the honest answer to "six 5 mm tabs on a
    /// 20 mm square".
    /// </summary>
    public static IReadOnlyList<PlacedTab> For(Path64 contour, OutlineOptions options)
    {
        ArgumentNullException.ThrowIfNull(contour);
        ArgumentNullException.ThrowIfNull(options);

        if (options.TabCount <= 0 || contour.Count < 3)
        {
            return [];
        }

        var segments = IsolationOperation.ToSegments(contour);
        var width = options.TabWidthNm ?? DefaultWidthFor(contour);
        var height = options.TabHeightNm ?? DefaultHeightFor(options.BoardThicknessNm);

        var starts = new double[segments.Count];
        var lengths = new double[segments.Count];
        var perimeter = 0.0;

        for (var i = 0; i < segments.Count; i++)
        {
            starts[i] = perimeter;
            lengths[i] = segments[i].From.DistanceTo(segments[i].To);
            perimeter += lengths[i];
        }

        if (perimeter <= 0 || width * (double)options.TabCount >= perimeter)
        {
            return [];
        }

        var edges = StraightEdges(segments, starts, lengths);
        var placed = Allocate(edges, options.TabCount, width);

        var tabs = new List<PlacedTab>();

        for (var e = 0; e < edges.Count; e++)
        {
            for (var k = 0; k < placed[e]; k++)
            {
                var at = edges[e].Start + (edges[e].Length * (k + 1) / (placed[e] + 1.0));

                tabs.Add(new PlacedTab
                {
                    AlongNm = at % perimeter,
                    WidthNm = width,
                    HeightNm = height,
                    At = PointAt(segments, starts, lengths, at % perimeter),
                    Edge = e,
                    EdgeLengthNm = edges[e].Length,
                });
            }
        }

        // Nothing on this profile is straight enough, or long enough, to hold a tab: a circular
        // board, or a board whose every edge is shorter than a tab plus its clearance. Falling back
        // to the old rule is better than refusing, because a profile cut with no tabs at all throws
        // the piece on the last pass — and the caller says in the program that the positions were
        // not chosen by anything.
        if (tabs.Count == 0)
        {
            for (var k = 0; k < options.TabCount; k++)
            {
                var at = perimeter * k / options.TabCount;

                tabs.Add(new PlacedTab
                {
                    AlongNm = at,
                    WidthNm = width,
                    HeightNm = height,
                    At = PointAt(segments, starts, lengths, at),
                    Edge = -1,
                    EdgeLengthNm = 0,
                });
            }
        }

        tabs.Sort((a, b) => a.AlongNm.CompareTo(b.AlongNm));

        return Edited(tabs, options.TabEdits);
    }

    /// <summary>
    /// Per-tab sizes, applied after the positions are settled.
    ///
    /// **Deliberately after, and the order matters.** The width decides how many tabs an edge can
    /// hold, so applying an edit before placement would let one widened tab move every other tab on
    /// the board. An edit therefore changes the tab it names and nothing else, and a tab edited
    /// wider than its edge is left that way with the caller free to say so — the alternative is an
    /// edit that silently does something other than what it says.
    /// </summary>
    private static List<PlacedTab> Edited(
        List<PlacedTab> tabs,
        IReadOnlyList<TabEdit> edits)
    {
        if (edits.Count == 0)
        {
            return tabs;
        }

        foreach (var edit in edits)
        {
            if (edit.Index < 0 || edit.Index >= tabs.Count)
            {
                continue;
            }

            tabs[edit.Index] = tabs[edit.Index] with
            {
                WidthNm = edit.WidthNm ?? tabs[edit.Index].WidthNm,
                HeightNm = edit.HeightNm ?? tabs[edit.Index].HeightNm,
            };
        }

        return tabs;
    }

    /// <summary>
    /// The profile's straight edges, as runs of segments going the same way.
    ///
    /// An offset rectangle is four long segments and four tessellated corners, so the corners come
    /// out as runs of their own — a few tenths of a millimetre each, which no tab fits on. That is
    /// how "never across a corner" is enforced: not by a rule about corners, but by a tab needing an
    /// edge to sit on.
    ///
    /// The run that straddles the start vertex is joined up, because a seam is not a corner: an
    /// offset profile begins wherever Clipper chose, which is frequently the middle of an edge, and
    /// treating that as two short edges would put two tabs where one belongs.
    ///
    /// **Measured against the run's own first direction as well as against the segment before**, and
    /// a `/code-review` pass is why. The pairwise test alone is a limit on each turn, not on the sum
    /// of them: tessellation density is set by the sagitta, and at the default 0.001 mm a curve of
    /// radius 11 mm or more turns by less than the pairwise tolerance at every step. A board with a
    /// generous corner radius, or a swept edge, would chain into one long "straight edge" — and a tab
    /// would be centred on a curve while the program said it was never across a corner. Clipper's own
    /// round joins at a 0.5 mm tool radius turn about 7° a segment, which is why nothing in the
    /// corpus showed it.
    /// </summary>
    private static List<(double Start, double Length)> StraightEdges(
        IReadOnlyList<ArtSegment> segments,
        double[] starts,
        double[] lengths)
    {
        // About a degree and a half between one segment and the next. Tighter than this and the last
        // segment of a tessellated corner joins the edge beside it; looser and a shallow curve reads
        // as straight.
        const double Straight = 0.99966;

        // And about two degrees across the whole run, which is what stops a long shallow curve
        // accumulating into an edge a tab can sit on.
        const double StraightOverall = 0.99939;

        var edges = new List<(double Start, double Length)>();
        var from = 0;

        while (from < segments.Count)
        {
            var to = from;

            while (to + 1 < segments.Count
                && SameWay(segments[to], segments[to + 1], Straight)
                && SameWay(segments[from], segments[to + 1], StraightOverall))
            {
                to++;
            }

            edges.Add((starts[from], starts[to] + lengths[to] - starts[from]));
            from = to + 1;
        }

        // The first and last runs are one edge cut by the seam.
        //
        // Both tolerances, as inside a run: a `/code-review` pass pointed out that testing only
        // the pairwise one here would let two runs that are each within two degrees of straight
        // merge into an edge bending twice that, with a tab centred on the bend.
        if (edges.Count > 1
            && SameWay(segments[^1], segments[0], Straight)
            && SameWay(segments[0], segments[^1], StraightOverall))
        {
            var last = edges[^1];
            var first = edges[0];

            edges.RemoveAt(edges.Count - 1);

            // The merged edge keeps the earlier of the two starts and runs past the end of the
            // perimeter; the caller takes each tab centre modulo the perimeter, so a tab on this
            // edge lands correctly either side of the seam.
            edges[0] = (last.Start, last.Length + first.Length);
            edges.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        return edges;
    }

    private static bool SameWay(ArtSegment a, ArtSegment b, double straight)
    {
        if (a.IsArc || b.IsArc)
        {
            return false;
        }

        double ax = a.To.X - a.From.X;
        double ay = a.To.Y - a.From.Y;
        double bx = b.To.X - b.From.X;
        double by = b.To.Y - b.From.Y;

        var la = Math.Sqrt((ax * ax) + (ay * ay));
        var lb = Math.Sqrt((bx * bx) + (by * by));

        if (la <= 0 || lb <= 0)
        {
            return false;
        }

        return (((ax * bx) + (ay * by)) / (la * lb)) >= straight;
    }

    /// <summary>
    /// How many tabs each edge gets, which is the part that turns a count into a placement.
    ///
    /// One at a time to whichever edge has the most length per tab it already holds — so on a
    /// rectangle the first four go one to each edge, the fifth goes to a long edge, and the sixth to
    /// the other long one. Four on a rectangle is therefore one centred on every edge, which is the
    /// default and the thing the workshop draws by hand; asking for more still gets what it asked
    /// for, spread by length rather than by an arbitrary seam.
    ///
    /// An edge only accepts another tab if it can hold that many with clearance either side, so a
    /// count larger than the board can carry stops short rather than crowding one edge.
    /// </summary>
    private static int[] Allocate(List<(double Start, double Length)> edges, int count, long width)
    {
        var placed = new int[edges.Count];
        var given = 0;

        // **Every edge gets one before any edge gets two**, and the goldens are what settled that: a
        // plain length-per-tab ratio put two tabs on each long side of a 9 x 40 mm connector strip
        // and left both ends free, because half of forty beats all of seven. A piece is held better
        // by a tab on every side, and one centred on each edge is the thing this work exists to
        // produce.
        foreach (var e in Enumerable.Range(0, edges.Count).OrderByDescending(e => edges[e].Length))
        {
            if (given >= count)
            {
                break;
            }

            if (!Fits(edges[e].Length, 1, width))
            {
                continue;
            }

            placed[e] = 1;
            given++;
        }

        // Then whatever is left over goes where there is most edge per tab already on it, which on a
        // rectangle asked for six puts the extra two on the long sides.
        while (given < count)
        {
            var best = -1;
            var bestScore = 0.0;

            for (var e = 0; e < edges.Count; e++)
            {
                if (!Fits(edges[e].Length, placed[e] + 1, width))
                {
                    continue;
                }

                var score = edges[e].Length / (placed[e] + 1.0);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }

            if (best < 0)
            {
                break;
            }

            placed[best]++;
            given++;
        }

        return placed;
    }

    /// <summary>
    /// Whether an edge that long can hold that many tabs of that width.
    ///
    /// The clearance either side is a tab's width, or 2 mm, whichever is more: the point of it is
    /// that a tab has cut material on both sides of it, and a tab whose neighbour is half a
    /// millimetre away is one tab with a scratch in it.
    /// </summary>
    private static bool Fits(double length, int tabs, long width)
    {
        var clearance = (double)Math.Max(width, Nm.FromMillimetres(2.0));

        return length >= (tabs * (double)width) + ((tabs + 1) * clearance);
    }

    /// <summary>The point this far round the profile.</summary>
    private static Point2 PointAt(
        IReadOnlyList<ArtSegment> segments,
        double[] starts,
        double[] lengths,
        double at)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            if (lengths[i] <= 0)
            {
                continue;
            }

            if (at <= starts[i] + lengths[i] || i == segments.Count - 1)
            {
                var t = Math.Clamp((at - starts[i]) / lengths[i], 0, 1);

                return new Point2(
                    segments[i].From.X + (long)Math.Round((segments[i].To.X - segments[i].From.X) * t),
                    segments[i].From.Y + (long)Math.Round((segments[i].To.Y - segments[i].From.Y) * t));
            }
        }

        return segments[0].From;
    }
}
