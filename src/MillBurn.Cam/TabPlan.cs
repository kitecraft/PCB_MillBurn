using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
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
    /// vertex, or <c>-1</c> for a tab whose position no edge chose: one spaced round a profile with
    /// no edge long enough to hold one, or one a hand put where it wanted (see <see cref="Moved"/>
    /// and <see cref="Added"/>).
    /// </summary>
    public required int Edge { get; init; }

    /// <summary>How long that edge is, which is what decides how many tabs it can hold.</summary>
    public required double EdgeLengthNm { get; init; }

    /// <summary>
    /// Which tab this is, in the terms an edit names one: its place in the pristine planned order
    /// for a planner's tab, or its place among the hand-added ones when <see cref="Added"/>. The
    /// two spaces stay stable however tabs are later moved or removed, which is what lets a stored
    /// edit mean the same tab on every reopen of the same plan.
    /// </summary>
    public int Ordinal { get; init; }

    /// <summary>
    /// The tab's name as the operator sees it — A, B … Z, AA, AB … — beside its handle in the
    /// viewport and on its row in the tabs window. Given out with <see cref="Ordinal"/>: the
    /// planned tabs in the pristine plan's order, then the hand-added ones after them. A planned
    /// tab's letter stays with it however far it is dragged and whatever is removed around it; an
    /// added tab's moves up one when an add before it is removed, as its ordinal does.
    /// </summary>
    public string Letter { get; init; } = string.Empty;

    /// <summary>A planner's tab whose position a hand overrode (6.4).</summary>
    public bool Moved { get; init; }

    /// <summary>A tab a hand put on the loop where the planner put none (6.4).</summary>
    public bool Added { get; init; }

    /// <summary>
    /// True when nothing chose this position: the fallback spacing, not a straight edge and not a
    /// hand.
    /// </summary>
    public bool Spaced => Edge < 0 && !Moved && !Added;
}

/// <summary>
/// One hand-set change to where the tabs are (6.4): a planned tab moved, a planned tab removed, or
/// a new tab added.
///
/// Three shapes in one record, told apart by which fields are set — the same JSON-friendly shape
/// as <see cref="TabEdit"/>, living beside it in the settings and clearing through the same doors,
/// because a position override belongs to the plan it overrode exactly as a size does:
///
/// - <see cref="Index"/> ≥ 0 with <see cref="AlongNm"/> set **moves** that planned tab there.
/// - <see cref="Index"/> ≥ 0 with no <see cref="AlongNm"/> **removes** that planned tab.
/// - <see cref="Index"/> &lt; 0 with <see cref="AlongNm"/> set **adds** a tab there, at the
///   profile's derived sizes unless it carries its own (<see cref="WidthNm"/>,
///   <see cref="HeightNm"/>).
///
/// An entry that names no planned tab and adds nothing is ignored, like a size edit out of range: a
/// hand-edited project file is not taken at its word. A removal of a tab outranks a move of the
/// same tab, whatever order the entries come in.
/// </summary>
public sealed record TabMove
{
    /// <summary>Which planned tab, counted as <see cref="TabEdit.Index"/> is; -1 adds one instead.</summary>
    public required int Index { get; init; }

    /// <summary>
    /// The tab's new centre as a distance along the cut profile, in nanometres — the coordinate
    /// <see cref="PlacedTab.AlongNm"/> uses and <see cref="ProfilePath"/> measures. Any distance:
    /// the loop is closed, so it wraps. Null on a named tab removes it.
    /// </summary>
    public double? AlongNm { get; init; }

    /// <summary>
    /// An added tab's own width, or null for the board's. Only an add reads it.
    ///
    /// **On the add itself, not in a <see cref="TabEdit"/>**, and `/code-review` is why. An edit
    /// names a tab by an index, and an added tab's index is its place among the adds — so removing
    /// one shifted every later add's index, and a size stored apart either went with the wrong tab
    /// or outlived the one it was for; and a build from before this field would have read an edit
    /// for an add as one for the planned tab with that number. Kept here, the size is removed with
    /// its tab, and an older build that does not know the field reads an add at the board's size —
    /// a missing choice rather than a misplaced one.
    /// </summary>
    public long? WidthNm { get; init; }

    /// <summary>An added tab's own height, or null for the board's. Only an add reads it.</summary>
    public long? HeightNm { get; init; }

    /// <summary>
    /// An added tab's own number among the adds — what its ordinal and its letter come from.
    ///
    /// **Given once and never re-derived.** Counted by position instead, removing an earlier add
    /// moved every later add up a letter, so "tab F" became E under the operator's eyes — sprint
    /// 6's own debt list. A new add takes one more than the highest in use
    /// (<see cref="TabPlan.WithAdd"/>). Null, on an entry from before the field, reads as its place
    /// among the adds, which is what it was then.
    /// </summary>
    public int? Number { get; init; }

    /// <summary>True for an entry that adds a tab: no planned tab named, and a place to put it.</summary>
    [JsonIgnore]
    public bool IsAdd => Index < 0 && AlongNm is not null;

    /// <summary>True for an entry that removes a planned tab: one named, and no place given.</summary>
    [JsonIgnore]
    public bool IsRemoval => Index >= 0 && AlongNm is null;
}

/// <summary>
/// What an outline’s tabs are, and what the board would have made them.
///
/// The second pair is what lets a dialog tell "left alone" from "set to the same number", and
/// what it shows beside the boxes so that "the board decides" is a figure somebody can see.
/// </summary>
/// <param name="Tabs">The tabs, in the pristine plan's order — the order an edit's index names,
/// which a hand-moved tab keeps however far it slides.</param>
/// <param name="DerivedWidthNm">The width this profile would give a tab.</param>
/// <param name="DerivedHeightNm">The material this thickness would leave under one.</param>
/// <param name="Profile">The offset contour the tabs sit on — the cut path, which is what the
/// viewport hit-tests and a drag slides along (6.4). Null when no profile took tabs.</param>
/// <param name="PristineKey">The fingerprint of the plan the hand-set changes belong to, before
/// any of them — what a caller stamps beside a change it writes (<see cref="TabPlan.KeyOf"/>).</param>
/// <param name="OverridesStale">True when the stored hand-set changes were made against a
/// different plan and so were not applied: <see cref="Tabs"/> is the planner's own answer.</param>
public sealed record TabPlanning(
    IReadOnlyList<PlacedTab> Tabs,
    long DerivedWidthNm,
    long DerivedHeightNm,
    Path64? Profile = null,
    string? PristineKey = null,
    bool OverridesStale = false);

/// <summary>
/// One gap the cut leaves: where it starts and ends along the loop, and the tabs it holds — more
/// than one when tabs overlap, and then the gap is their union, which is what is cut.
/// </summary>
/// <param name="StartNm">The near end, as a distance along the loop; may be negative for a gap
/// that straddles the start vertex.</param>
/// <param name="EndNm">The far end, past <paramref name="StartNm"/>.</param>
/// <param name="Tabs">The tabs in it, in order along the loop.</param>
public sealed record TabGap(double StartNm, double EndNm, IReadOnlyList<PlacedTab> Tabs)
{
    /// <summary>
    /// How long the gap is: a lone tab's own width, exactly, or the union's length rounded to the
    /// nanometre. The lone case is said exactly because it reaches the program as a hop window, and
    /// a width recomputed from its ends could differ from the one the tab was given by a rounding.
    /// </summary>
    public long LengthNm => Tabs.Count == 1 ? Tabs[0].WidthNm : (long)Math.Round(EndNm - StartNm);
}

/// <summary>One tab's width or height, set by hand rather than derived.</summary>
public sealed record TabEdit
{
    /// <summary>
    /// Which planned tab, by its place in the pristine planned order. An added tab's sizes live on
    /// its own <see cref="TabMove"/> instead.
    /// </summary>
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
/// **And *where* an edge is outranks how long it is.** The first version ranked by length alone,
/// which is one-per-side on a rectangle by accident of there being four edges — on the 66-up
/// panel, whose outline is many short stretches with the longest all along one side, it put every
/// default tab on the frame's bottom edge (6.72). An edge's quarter of the compass, taken round
/// the profile's centre, is now the first key and its length the second.
///
/// Recorded in [06 §6.4](../../Documentation/06-Roadmap-and-Risks.md) and 06 §6.72.
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

        var path = new ProfilePath(contour);
        var segments = path.Segments;
        // A size of nothing is not a tab, and the last line against one: an outline cut with tabs
        // of no width releases the board on the final lap while the program's own header promises
        // it is held. The settings file refuses a non-positive number and so does the CLI, and the
        // dialog now reads an emptied box as "no choice" rather than as zero — but this is geometry
        // that cuts a real board, so it does not take anyone's word for it.
        var width = options.TabWidthNm is { } chosen and > 0 ? chosen : DefaultWidthFor(contour);

        var height = options.TabHeightNm is { } left and > 0
            ? left
            : DefaultHeightFor(options.BoardThicknessNm);

        var starts = path.Starts;
        var lengths = path.Lengths;
        var perimeter = path.PerimeterNm;

        if (perimeter <= 0 || width * (double)options.TabCount >= perimeter)
        {
            return [];
        }

        var edges = StraightEdges(segments, starts, lengths);
        var quarters = Quarters(path, edges, perimeter);
        var placed = Allocate(edges, quarters, options.TabCount, width);

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
                    At = path.At(at % perimeter),
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
                    At = path.At(at),
                    Edge = -1,
                    EdgeLengthNm = 0,
                });
            }
        }

        tabs.Sort((a, b) => a.AlongNm.CompareTo(b.AlongNm));

        // The ordinal is the name an edit knows a tab by, so it is assigned on the pristine sorted
        // plan — before any hand-set size or position — and never re-derived from the list order
        // afterwards. A moved tab that crosses its neighbour keeps its ordinal, and so does every
        // tab around a removed one, which is what stops a stored override landing on whichever tab
        // later sorts into that slot.
        for (var i = 0; i < tabs.Count; i++)
        {
            tabs[i] = tabs[i] with { Ordinal = i, Letter = LetterOf(i) };
        }

        return Moved(Edited(tabs, options.TabEdits), options.TabMoves, path, width, height);
    }

    /// <summary>
    /// A plan's fingerprint: how many tabs, and where and how wide each is, before any hand-set
    /// change.
    ///
    /// **What a hand-set change is stamped with, so it can tell when the plan under it moved**
    /// (sprint 6's `xhigh` review). Sizes, moves and removals name a tab by its number in the plan,
    /// and the plan can be renumbered underneath them: by this release's own placement rule on a
    /// project from an older one, by a revised outline refreshed from its folder, by a cutter's
    /// diameter edited in the library. The change then lands on whichever tab now has the number —
    /// a thin web somewhere nobody chose. With the stamp, a change made against another plan is not
    /// applied, and says so; refusing beats guessing which tab was meant.
    ///
    /// Positions to the micrometre and widths exactly, hashed so a project file carries twelve
    /// characters rather than the plan. Heights are left out: they move no tab, and a thickness
    /// change must not throw away a hand's positions.
    /// </summary>
    public static string KeyOf(IReadOnlyList<PlacedTab> pristine)
    {
        ArgumentNullException.ThrowIfNull(pristine);

        var text = new StringBuilder();
        text.Append(pristine.Count);

        foreach (var tab in pristine)
        {
            text.Append(';').Append((long)Math.Round(tab.AlongNm / 1000.0)).Append(':').Append(tab.WidthNm);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    /// <summary>
    /// The gaps the cut leaves round a loop, overlapping tabs merged into one.
    ///
    /// **The union, because that is the only honest gap.** Found by sprint 6's `xhigh` review: the
    /// splitter used to drop the run between two overlapping tabs and start the next run at the
    /// inner tab's far side — so a 2 mm tab dropped inside a 10 mm one cut 2 mm off the wide tab
    /// while the header and the picture still said 10. Merged first, the cut, the header and the
    /// window all describe the same gap. Lone tabs come back exactly as they were, so no program
    /// without an overlap moves.
    /// </summary>
    /// <param name="tabs">The tabs on one loop, in any order.</param>
    /// <param name="perimeterNm">The loop's length.</param>
    public static IReadOnlyList<TabGap> Gaps(IReadOnlyList<PlacedTab> tabs, double perimeterNm)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        var gaps = new List<(double Start, double End, List<PlacedTab> Tabs)>();

        foreach (var tab in tabs.OrderBy(t => t.AlongNm - (t.WidthNm / 2.0)))
        {
            var start = tab.AlongNm - (tab.WidthNm / 2.0);
            var end = tab.AlongNm + (tab.WidthNm / 2.0);

            if (gaps.Count > 0 && start < gaps[^1].End)
            {
                var last = gaps[^1];
                last.Tabs.Add(tab);
                gaps[^1] = (last.Start, Math.Max(last.End, end), last.Tabs);
            }
            else
            {
                gaps.Add((start, end, [tab]));
            }
        }

        // Round the seam: the last gap can run on past the loop's end into the first one.
        if (gaps.Count > 1 && perimeterNm > 0 && gaps[^1].End > gaps[0].Start + perimeterNm)
        {
            var last = gaps[^1];
            var first = gaps[0];
            gaps.RemoveAt(gaps.Count - 1);
            gaps[0] = (last.Start - perimeterNm, Math.Max(first.End, last.End - perimeterNm), [.. last.Tabs, .. first.Tabs]);
        }

        return [.. gaps.Select(g => new TabGap(g.Start, g.End, g.Tabs))];
    }

    /// <summary>
    /// The add entries in a list, each with its number — its own <see cref="TabMove.Number"/>, or
    /// its place among the adds when it has none. The one place "which entry is that added tab" is
    /// worked out; everything that moves, sizes or removes an add goes through it.
    /// </summary>
    private static IEnumerable<(int Index, int Number)> Adds(IReadOnlyList<TabMove> moves)
    {
        var position = 0;

        for (var i = 0; i < moves.Count; i++)
        {
            if (moves[i].IsAdd)
            {
                yield return (i, moves[i].Number ?? position);
                position++;
            }
        }
    }

    /// <summary>The list index of the add with this number, or -1.</summary>
    private static int AddIndex(IReadOnlyList<TabMove> moves, int number) =>
        Adds(moves).Where(a => a.Number == number).Select(a => a.Index).DefaultIfEmpty(-1).First();

    /// <summary>A tab added at a distance along the loop, numbered one past the highest in use.</summary>
    public static ImmutableArray<TabMove> WithAdd(IReadOnlyList<TabMove> moves, double alongNm)
    {
        ArgumentNullException.ThrowIfNull(moves);

        var next = Adds(moves).Select(a => a.Number + 1).DefaultIfEmpty(0).Max();

        return [.. moves, new TabMove { Index = -1, AlongNm = alongNm, Number = next }];
    }

    /// <summary>The added tab with this number, moved; its size and number go with it.</summary>
    public static ImmutableArray<TabMove> WithAddMoved(IReadOnlyList<TabMove> moves, int number, double alongNm)
    {
        ArgumentNullException.ThrowIfNull(moves);

        var index = AddIndex(moves, number);
        return [.. moves.Select((m, i) => i == index ? m with { AlongNm = alongNm, Number = number } : m)];
    }

    /// <summary>
    /// The added tab with this number, gone — and the size it carried with it. The adds after it
    /// keep their numbers, so keep their letters; the survivors are pinned to their numbers on the
    /// way through, in case they were counted by position.
    /// </summary>
    public static ImmutableArray<TabMove> WithoutAdd(IReadOnlyList<TabMove> moves, int number)
    {
        ArgumentNullException.ThrowIfNull(moves);

        var numbers = Adds(moves).ToDictionary(a => a.Index, a => a.Number);

        return
        [
            .. moves
                .Select((m, i) => (Move: m, Index: i))
                .Where(e => !(numbers.TryGetValue(e.Index, out var n) && n == number))
                .Select(e => numbers.TryGetValue(e.Index, out var n) ? e.Move with { Number = n } : e.Move),
        ];
    }

    /// <summary>A planned tab moved by hand: one entry for it, replacing any earlier move or removal.</summary>
    public static ImmutableArray<TabMove> WithPlannedMoved(IReadOnlyList<TabMove> moves, int ordinal, double alongNm)
    {
        ArgumentNullException.ThrowIfNull(moves);

        return [.. moves.Where(m => m.Index != ordinal), new TabMove { Index = ordinal, AlongNm = alongNm }];
    }

    /// <summary>A planned tab removed by hand: one entry for it, replacing any earlier move.</summary>
    public static ImmutableArray<TabMove> WithPlannedRemoved(IReadOnlyList<TabMove> moves, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(moves);

        return [.. moves.Where(m => m.Index != ordinal), new TabMove { Index = ordinal }];
    }

    /// <summary>
    /// A tab's letter from its place: 0 is A, 25 is Z, 26 is AA, 701 is ZZ and 702 is AAA — a
    /// spreadsheet's column names, which nobody has to be taught.
    /// </summary>
    public static string LetterOf(int place)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(place);

        var letters = new System.Text.StringBuilder();

        for (var n = place + 1; n > 0; n = (n - 1) / 26)
        {
            letters.Insert(0, (char)('A' + ((n - 1) % 26)));
        }

        return letters.ToString();
    }

    /// <summary>
    /// The hand-set changes with one tab's width set — what a click on a handle chose.
    ///
    /// <paramref name="widthNm"/> equal to <paramref name="boardWidthNm"/>, or null, means "as
    /// every tab is", and is not stored: the same rule the tabs window applies, so the two doors
    /// write the same project file for the same choice. The tab's own height, if one was set, is
    /// kept. A planned tab's width is a <see cref="TabEdit"/>, and an edit left with neither number
    /// goes; an added tab's is on its own add entry.
    /// </summary>
    /// <param name="edits">The size edits as they stand.</param>
    /// <param name="moves">The hand-set positions as they stand.</param>
    /// <param name="tab">The tab, named by its <see cref="PlacedTab.Ordinal"/> and
    /// <see cref="PlacedTab.Added"/>.</param>
    /// <param name="widthNm">The width chosen for it alone.</param>
    /// <param name="boardWidthNm">The width every tab has unless it says otherwise.</param>
    public static (ImmutableArray<TabEdit> Edits, ImmutableArray<TabMove> Moves) WithWidth(
        IReadOnlyList<TabEdit> edits,
        IReadOnlyList<TabMove> moves,
        PlacedTab tab,
        long? widthNm,
        long boardWidthNm)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(moves);
        ArgumentNullException.ThrowIfNull(tab);

        var width = widthNm is { } chosen and > 0 && chosen != boardWidthNm ? chosen : (long?)null;

        if (tab.Added)
        {
            var height = AddEntry(moves, tab.Ordinal)?.HeightNm;
            return ([.. edits], WithAddSize(moves, tab.Ordinal, width, height));
        }

        var kept = edits.Where(e => e.Index != tab.Ordinal);
        var ownHeight = edits.LastOrDefault(e => e.Index == tab.Ordinal)?.HeightNm;

        return (
            width is null && ownHeight is null
                ? [.. kept]
                : [.. kept, new TabEdit { Index = tab.Ordinal, WidthNm = width, HeightNm = ownHeight }],
            [.. moves]);
    }

    /// <summary>
    /// The positions with the added tab numbered <paramref name="add"/> given these sizes — null
    /// for "the board's". The number is the one <see cref="PlacedTab.Ordinal"/> carries for an
    /// added tab.
    /// </summary>
    public static ImmutableArray<TabMove> WithAddSize(
        IReadOnlyList<TabMove> moves, int add, long? widthNm, long? heightNm)
    {
        ArgumentNullException.ThrowIfNull(moves);

        var index = AddIndex(moves, add);

        return
        [
            .. moves.Select((m, i) => i == index ? m with { WidthNm = widthNm, HeightNm = heightNm, Number = add } : m),
        ];
    }

    /// <summary>The add entry numbered <paramref name="add"/>, or null.</summary>
    private static TabMove? AddEntry(IReadOnlyList<TabMove> moves, int add) =>
        AddIndex(moves, add) is var i and >= 0 ? moves[i] : null;

    /// <summary>
    /// Hand-set positions, applied after the sizes and addressed the same way (6.4).
    ///
    /// **After, for the same reason the sizes are**: a position decides nothing about any other
    /// tab, so it must not take part in placement — a dragged tab moves itself and nothing else.
    /// The list keeps the pristine plan's order rather than re-sorting by the new positions,
    /// because the ordinal space is the list's whole contract; the splitter orders by arc length
    /// itself, and a caller drawing handles cares where each tab *is*, not where it ranks.
    ///
    /// An added tab takes the profile's own derived sizes — the board-wide pair every planned tab
    /// starts from — unless its entry carries its own. Its letter follows the planned tabs': the
    /// pristine plan's count, plus its own number (<see cref="TabMove.Number"/>).
    /// </summary>
    private static List<PlacedTab> Moved(
        List<PlacedTab> tabs,
        IReadOnlyList<TabMove> moves,
        ProfilePath path,
        long width,
        long height)
    {
        if (moves.Count == 0)
        {
            return tabs;
        }

        var result = new List<PlacedTab>(tabs);
        var removed = new HashSet<int>();
        var numbers = Adds(moves).ToDictionary(a => a.Index, a => a.Number);

        for (var i = 0; i < moves.Count; i++)
        {
            var move = moves[i];

            if (move.Index >= 0 && move.Index < tabs.Count)
            {
                if (move.AlongNm is { } along)
                {
                    var at = path.Wrap(along);

                    result[move.Index] = result[move.Index] with
                    {
                        AlongNm = at,
                        At = path.At(at),
                        Edge = -1,
                        EdgeLengthNm = 0,
                        Moved = true,
                    };
                }
                else
                {
                    removed.Add(move.Index);
                }
            }
            else if (move.IsAdd)
            {
                var at = path.Wrap(move.AlongNm!.Value);
                var number = numbers[i];

                // Its own sizes when it carries them, guarded as an edit's are: a non-positive
                // number in a hand-edited file is no size at all.
                result.Add(new PlacedTab
                {
                    AlongNm = at,
                    WidthNm = move.WidthNm is { } ownWidth and > 0 ? ownWidth : width,
                    HeightNm = move.HeightNm is { } ownHeight and > 0 ? ownHeight : height,
                    At = path.At(at),
                    Edge = -1,
                    EdgeLengthNm = 0,
                    Ordinal = number,
                    Letter = LetterOf(tabs.Count + number),
                    Added = true,
                });
            }

            // An entry that names no planned tab and adds nothing is ignored — a project file is
            // geometry's input, not its instruction.
        }

        return removed.Count == 0
            ? result
            : [.. result.Where(t => t.Added || !removed.Contains(t.Ordinal))];
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

            // The same guard `For` applies to the board-wide pair, for the same reason and found
            // by the same `/code-review` habit of asking what a project file could hold. The dialog
            // cannot produce a zero — an emptied box reads as "no choice" — but a hand-edited
            // `"WidthNm": 0` would put two cut runs end to end at that tab, which is the profile cut
            // clean through while the program's header still counts it as a tab.
            tabs[edit.Index] = Sized(tabs[edit.Index], edit);
        }

        return tabs;
    }

    /// <summary>One tab with an edit's sizes, a non-positive number ignored as `For` ignores one.</summary>
    private static PlacedTab Sized(PlacedTab tab, TabEdit edit) => tab with
    {
        WidthNm = edit.WidthNm is { } width and > 0 ? width : tab.WidthNm,
        HeightNm = edit.HeightNm is { } height and > 0 ? height : tab.HeightNm,
    };

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

        // Which segments each edge is made of, which the seam check below needs and the edge's own
        // start and length cannot give back.
        var spans = new List<(int From, int To)>();
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
            spans.Add((from, to));
            from = to + 1;
        }

        // The first and last runs are one edge cut by the seam.
        //
        // **Both tolerances, and against the right pair of segments**, which took two
        // `/code-review` passes. The first added the overall check here after the pairwise one let
        // two runs that are each within two degrees of straight merge into an edge bending twice
        // that, with a tab centred on the bend. The second found that the check it added did
        // nothing: `SameWay` is a normalised dot product and so is symmetric in its arguments, so
        // asking it about the same two segments again with a looser threshold is a question that
        // cannot fail once the first has passed.
        //
        // The merged edge runs from the last run's *first* segment, through the seam, to the first
        // run's *last* one — so those are its ends, exactly as the loop above compares `from`
        // against each candidate `to + 1`.
        if (edges.Count > 1
            && SameWay(segments[^1], segments[0], Straight)
            && SameWay(segments[spans[^1].From], segments[spans[0].To], StraightOverall))
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
    /// Which side of the profile each edge faces — bottom, right, top or left — from the edge's own
    /// direction and which side of the centre it lies on.
    ///
    /// **This is what "spread" means, and length is blind to it (6.72).** A panel's outline is many
    /// short straight stretches between the boards, and the longest of them all lie along one side
    /// — so a rule that only ranks by length put every default tab on the panel frame's bottom
    /// edge, and a frame held along one edge pivots on it. The facing is the fact the rule was
    /// missing: *where* an edge is, said coarsely enough that a rectangle's four sides land in four
    /// different quarters whatever its proportions.
    ///
    /// **From the edge's direction first, not from its bearing alone.** A bearing round the centre,
    /// quantised at the box's diagonals, was the first try — and on the panel it called a top
    /// stretch near a corner "right", because the frame is wider than it is tall and the corner
    /// stretch's midpoint lies past the diagonal. Four tabs went one to each *bearing* and three of
    /// them sat on the top side, which is the defect back again in a hat. A horizontal edge is a
    /// top-or-bottom edge whatever its x; only which of the two it is comes from its position.
    /// </summary>
    private static int[] Quarters(
        ProfilePath path,
        List<(double Start, double Length)> edges,
        double perimeter)
    {
        var segments = path.Segments;

        var minX = long.MaxValue;
        var minY = long.MaxValue;
        var maxX = long.MinValue;
        var maxY = long.MinValue;

        foreach (var segment in segments)
        {
            minX = Math.Min(minX, segment.From.X);
            minY = Math.Min(minY, segment.From.Y);
            maxX = Math.Max(maxX, segment.From.X);
            maxY = Math.Max(maxY, segment.From.Y);
        }

        var centreX = (minX + maxX) / 2.0;
        var centreY = (minY + maxY) / 2.0;

        var quarters = new int[edges.Count];

        for (var e = 0; e < edges.Count; e++)
        {
            // The edge merged across the seam starts before the perimeter's end and finishes past
            // it, so its ends and midpoint are taken modulo the loop, exactly as its tab centres
            // are.
            var from = path.At(edges[e].Start % perimeter);
            var to = path.At((edges[e].Start + edges[e].Length) % perimeter);
            var mid = path.At((edges[e].Start + (edges[e].Length / 2)) % perimeter);

            // The chord across the whole run: an edge is straight to within two degrees, so this is
            // its direction.
            var horizontal = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);

            quarters[e] = horizontal
                ? (mid.Y - centreY >= 0 ? 0 : 1)
                : (mid.X - centreX >= 0 ? 2 : 3);
        }

        return quarters;
    }

    /// <summary>
    /// How many tabs each edge gets, which is the part that turns a count into a placement.
    ///
    /// One at a time, and each goes to the emptiest quarter of the profile first, to a bare edge
    /// within it second, and to the most length per tab last — so on a rectangle the first four go
    /// one to each side, the fifth goes to a long side, and the sixth to the other long one. Four
    /// on a rectangle is therefore one centred on every edge, which is the default and the thing
    /// the workshop draws by hand.
    ///
    /// **The quarter outranks the length (6.72)**: on the 66-up panel the longest edges are all
    /// stretches of the frame's bottom, and ranking by length alone put every tab there. Within a
    /// quarter the older rules hold — **every edge gets one before any edge gets two**, settled by
    /// the goldens when a plain length-per-tab ratio put two tabs on each long side of a 9 x 40 mm
    /// connector strip and left both ends free, because half of forty beats all of seven.
    ///
    /// An edge only accepts another tab if it can hold that many with clearance either side, so a
    /// count larger than the board can carry stops short rather than crowding one edge.
    /// </summary>
    private static int[] Allocate(
        List<(double Start, double Length)> edges, int[] quarters, int count, long width)
    {
        var placed = new int[edges.Count];
        var byQuarter = new int[4];

        for (var given = 0; given < count; given++)
        {
            var best = -1;

            for (var e = 0; e < edges.Count; e++)
            {
                if (!Fits(edges[e].Length, placed[e] + 1, width))
                {
                    continue;
                }

                if (best < 0)
                {
                    best = e;
                    continue;
                }

                // Emptiest quarter, then a bare edge over a tabbed one, then length per tab.
                var quarter = byQuarter[quarters[e]].CompareTo(byQuarter[quarters[best]]);
                var bare = (placed[e] > 0).CompareTo(placed[best] > 0);
                var score = edges[e].Length / (placed[e] + 1.0);
                var bestScore = edges[best].Length / (placed[best] + 1.0);

                if (quarter < 0
                    || (quarter == 0 && bare < 0)
                    || (quarter == 0 && bare == 0 && score > bestScore))
                {
                    best = e;
                }
            }

            if (best < 0)
            {
                break;
            }

            placed[best]++;
            byQuarter[quarters[best]]++;
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
}
