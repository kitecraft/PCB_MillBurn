using System.Globalization;
using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;
using MillBurn.Gerber.Excellon;

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

    /// <summary>
    /// Hole diameters this program must leave alone, because something else is making them.
    ///
    /// A hole bigger than any drill the operator owns is milled out instead, in the routing
    /// program. Filtering here rather than deleting afterwards keeps one rule: the drilling file
    /// contains exactly what a drill makes, and the companion page that lists the bits stays true
    /// without knowing anything about end mills.
    /// </summary>
    public IReadOnlyCollection<long> MilledNm { get; init; } = [];
}

/// <summary>How to cut the board out.</summary>
public sealed record OutlineOptions
{
    public Tool Tool { get; init; } = Tool.DefaultOutlineMill;

    public long BoardThicknessNm { get; init; } = Nm.FromMillimetres(1.6);

    public long BreakThroughNm { get; init; } = Nm.FromMillimetres(0.3);

    /// <summary>Depth taken per pass. The whole thickness in one go is what breaks 1 mm cutters.</summary>
    public long DepthPerPassNm { get; init; } = Nm.FromMillimetres(0.4);

    /// <summary>
    /// Width of each holding tab. Null derives it from the board — see
    /// <see cref="TabPlan.DefaultWidthFor"/>.
    /// </summary>
    public long? TabWidthNm { get; init; }

    /// <summary>
    /// Material left under a tab, for snapping the board out afterwards. Null derives it from the
    /// thickness — see <see cref="TabPlan.DefaultHeightFor"/>.
    /// </summary>
    public long? TabHeightNm { get; init; }

    /// <summary>
    /// How many tabs to put round the outline. Zero cuts the board fully free.
    ///
    /// A target rather than a spacing: the tabs go one to a straight edge, longest edge first, so
    /// four on a rectangle is one centred on each side. A count larger than the board's edges can
    /// carry places what fits and the program says how many that was.
    /// </summary>
    public int TabCount { get; init; } = 4;

    /// <summary>
    /// Tabs whose width or height was set by hand, keyed by the tab's place in the planned order.
    ///
    /// Empty is the ordinary case: everything derived. An edit changes the tab it names and nothing
    /// else, which is why it is applied after the positions are settled rather than before.
    /// </summary>
    public IReadOnlyList<TabEdit> TabEdits { get; init; } = [];

    /// <summary>
    /// Tabs a hand moved, removed or added (6.4), keyed like <see cref="TabEdits"/> and applied
    /// after them. Empty is the ordinary case: the planner's positions stand.
    /// </summary>
    public IReadOnlyList<TabMove> TabMoves { get; init; } = [];

    /// <summary>Cut outside the profile line, so the finished board is nominal size.</summary>
    public bool CutOutside { get; init; } = true;

    /// <summary>
    /// The board's own artwork, which is how a piece is told from a void.
    ///
    /// A profile nested inside another is nearly always waste — a slot, a window, the routed
    /// channel between the boards of a panel — and the cutter has to run *inside* it. The exception
    /// is a hand-panelised file, where the stock is one rectangle and each board is another
    /// rectangle inside it: those are nested too, and they are still pieces. What separates the two
    /// is whether there is anything on the board inside the profile. Empty means waste.
    ///
    /// Null or empty means there is no evidence either way, and then every profile is cut on the
    /// <see cref="CutOutside"/> side. Nesting alone must not decide it: a slot and a board inside a
    /// hand-cut panel frame are nested identically, and guessing between them is how a panel gets
    /// quietly destroyed in one direction or the other.
    /// </summary>
    public Paths64? Keep { get; init; }

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
    /// <summary>
    /// How far apart two diameters can be and still be the same bit: 10 µm.
    ///
    /// Real drill sets step in tenths of a millimetre at this size; nothing distinguishes 1.00 from
    /// 1.01. What this absorbs is not a design choice but an arithmetic one — a board laid out in
    /// inches and written in millimetres produces sizes like 1.00076 mm, which is 0.0394".
    /// </summary>
    public static long SameBitNm { get; } = Nm.FromMillimetres(0.01);

    /// <summary>
    /// The sizes actually in the file, with near-identical ones merged, biggest first.
    ///
    /// A real board found this: a KiCad export carried <c>1.00000</c> and <c>1.00076</c> mm as two
    /// apertures — 0.76 µm apart, the residue of a 0.0394" pad — and they became two toolpaths, two
    /// blocks of G-code and two entries in the drilling guide, both reading "Drill 1.00 mm". The
    /// operator is told to change to a bit they already have in the spindle, and the guide's step
    /// numbering slips out of step with the program's for every bit after it.
    ///
    /// Merged to the <b>larger</b> of the group. A hole a micron over is a hole; a hole a micron
    /// under is a part that does not fit.
    /// </summary>
    private static List<(DrillTool Tool, HashSet<int> Numbers)> SizesOf(ExcellonFile drill)
    {
        var groups = new List<(long Smallest, DrillTool Tool, HashSet<int> Numbers)>();

        // Ascending, and each candidate is measured against the *smallest* member of its group
        // rather than against the group's current representative. Comparing against the
        // representative — which grows as the group takes larger members — lets a run of sizes
        // creeping upward chain into one group spanning far more than the tolerance.
        foreach (var (tool, _) in drill.ByTool().OrderBy(x => x.Tool.DiameterNm))
        {
            var at = groups.FindIndex(g => tool.DiameterNm - g.Smallest <= SameBitNm);

            if (at < 0)
            {
                groups.Add((tool.DiameterNm, tool, [tool.Number]));
                continue;
            }

            groups[at].Numbers.Add(tool.Number);

            if (tool.DiameterNm > groups[at].Tool.DiameterNm)
            {
                groups[at] = (groups[at].Smallest, tool, groups[at].Numbers);
            }
        }

        groups.Reverse();

        return [.. groups.Select(g => (g.Tool, g.Numbers))];
    }

    public static IReadOnlyList<Toolpath> Build(
        ExcellonFile drill, DrillOptions options, Tool? template = null)
    {
        ArgumentNullException.ThrowIfNull(drill);
        ArgumentNullException.ThrowIfNull(options);

        var toolpaths = new List<Toolpath>();

        foreach (var (tool, numbers) in SizesOf(drill))
        {
            if (options.MilledNm.Any(d => Math.Abs(d - tool.DiameterNm) <= Nm.FromMillimetres(0.001)))
            {
                continue;
            }

            // Coincident hits are dropped. A drill file that lists the same position twice for the
            // same bit — which real ones do, and the IceZUM board does eight times — otherwise
            // drills the hole, then drills the empty hole again. The second pass cuts nothing, and
            // a drill dropped into a hole it already made is the one most likely to grab and snap.
            //
            // Same tool only. The same position under two different diameters is a hole being
            // opened out, which is a deliberate thing somebody might mean.
            var seen = new HashSet<Point2>();
            var hits = drill.Hits
                .Where(h => numbers.Contains(h.Tool))
                .Where(h => seen.Add(h.At))
                .ToList();

            if (hits.Count == 0)
            {
                continue;
            }

            var repeats = drill.Hits.Count(h => numbers.Contains(h.Tool)) - hits.Count;
            var notes = new List<string>();

            if (repeats > 0)
            {
                notes.Add(Invariant(
                    $"{repeats} repeated position(s) in the drill file were only drilled once."));
            }
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
    /// Two things decide whether this works. The offset must be on the waste side of every profile
    /// — outside a piece, inside a void — or the board comes out a cutter-width small on every
    /// edge it got wrong. And the last pass must not be the first time the board is free: without
    /// tabs, the cut-out piece lifts on the cutter somewhere near the end and is thrown, usually
    /// taking the cutter with it.
    /// </summary>
    public static Toolpath Build(Paths64 outline, OutlineOptions options, string label = "Outline")
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(options);

        var (contours, depths, closed, pieces, voids, centrelines) = Shape(outline, options);

        var notes = new List<string>();
        List<ToolpathPass> passes = [];

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

        // How deeply each profile sits inside the others. On a panel the boards are one level
        // inside the frame, and they have to be cut first: take the frame out first and everything
        // still attached to it is loose while the cutter is still working.
        var deepest = depths.Count == 0 ? 0 : depths.Max();

        // The cutter's own ladder, before the tabs have a say in it.
        var stepDepths = new List<long>();

        for (var step = 1; step <= steps; step++)
        {
            stepDepths.Add(Math.Min(options.TotalDepthNm, step * options.DepthPerPassNm));
        }

        // Every tab this program cuts, so the notes can describe them.
        //
        // **All of the profiles, not the first one that gets any**, which is what it used to be on
        // the grounds that every profile on a board is cut with the same plan. A `/code-review`
        // pass pointed out that this story made that false: the width is derived from the
        // profile’s own narrow side, so a sheet holding a 21 x 37 mm board and a 60 x 60 mm one
        // gets 2.0 mm tabs on the first and 5.0 mm on the second — and the header said 2.0 for
        // both.
        var planned = new List<PlacedTab>();
        var edgesUsed = 0;
        var profilesTabbed = 0;
        var profilesEligible = 0;
        var addedTops = new List<long>();
        var tabsSwallowProfile = false;

        // Per-tab edits belong to one profile: the first that places any, which is the one the tabs
        // window lists and numbers. Found by `/code-review`: the edits were handed to every
        // outermost profile and applied by index inside each, so "tab 3, 0.30 mm" set a thin web on
        // tab 3 of a second board in the same Edge_Cuts that nobody had looked at. Hand-set
        // positions (6.4) are keyed the same way and spend with the same profile, for the same
        // reason.
        var edits = options.TabEdits;
        var moves = options.TabMoves;
        var editsSpent = false;

        for (var c = 0; c < contours.Count; c++)
        {
            var contour = contours[c];
            if (contour.Count < (closed[c] ? 3 : 2))
            {
                continue;
            }

            // One stack per profile: everything at this place, cut deeper each time, kept together
            // so the tool finishes here before it moves.
            var group = deepest - depths[c];

            // Tabs go on the outermost profiles only.
            //
            // A tab holds a piece to the stock around it, so only the boundary between the job and
            // the material it sits in needs one. Everything nested inside that boundary is already
            // held — on a panel the individual boards are joined to each other by the tabs the
            // designer drew, and adding more on all four sides of every one of them leaves a panel
            // that has to be cut apart by hand.
            var outermost = depths[c] == 0;

            // Where this profile's tabs go, decided from its own edges.
            //
            // Per profile rather than per board, because "one centred on each straight edge" is a
            // fact about the shape in front of it. On a panel only the frame is tabbed anyway, and a
            // board whose outline is a rounded rectangle gets a different answer from one that is a
            // plain rectangle of the same size.
            var tabs = outermost && closed[c]
                ? TabPlan.For(
                    contour,
                    editsSpent ? options with { TabEdits = [], TabMoves = [] } : options)
                : [];

            if (outermost && closed[c] && contour.Count >= 3)
            {
                profilesEligible++;
            }

            if (tabs.Count > 0)
            {
                editsSpent = editsSpent || edits.Count > 0 || moves.Count > 0;
            }
            else if (!editsSpent
                && outermost && closed[c]
                && moves.Any(m => m.Index >= 0 && m.AlongNm is null)
                && TabPlan.For(contour, options with { TabEdits = [], TabMoves = [] }).Count > 0)
            {
                // The overrides belong to the first profile whose *pristine* plan placed tabs —
                // and hand removals can empty that profile's list, which the latch above cannot
                // see. Found by `/code-review`: a file holding removals for every one of profile
                // one's ordinals left `editsSpent` false, and the same removals then deleted the
                // tabs of the next board in the same Edge_Cuts, by the same numbers. The pristine
                // re-plan runs only in this corner — a removal-bearing file whose first tabbed
                // profile came back empty — so the ordinary export never pays for it. The window
                // refuses to remove the last tab; this is the route that does not go through the
                // window.
                editsSpent = true;
            }
            var walk = closed[c] ? new ProfileWalk(contour) : null;

            // **The full laps begin at the near side of the first tab, not at the profile's own start
            // vertex.**
            //
            // Measured on PogoTest1: without this the last lap finishes wherever Clipper's offset
            // happened to start and the first tabbed run begins at the middle of an edge twenty
            // millimetres away — further than a tab is wide, so the emitter rightly climbs to the safe
            // height and traverses.
            //
            // **Measured on the panel’s outline, and the baseline matters:** 337 mm of rapid before
            // this story, 646 mm with the tabs centred on edges and the lap still starting at the
            // seam, 521 mm with it starting beside the first tab. So this recovers 125 mm of the
            // 184 mm that moving the tabs cost, rather than making the panel cheaper than it was.
            // The remainder is the route between the frame and the boards inside it, which moved
            // with the frame’s entry point; on a one-profile board it goes the other way (the
            // connector strip: 83 mm to 73 mm).
            //
            // The old placement never showed the problem because a tab always straddled the seam,
            // so the run after it started where the lap ended.
            //
            // **The near side and not the far side**, which is a one-character difference with two
            // consequences. Ending the lap exactly where run zero begins would let the emitter carry
            // straight on without lifting — cheaper still — and the two would arrive in the backplot
            // as *one* run, whose depth is the deeper of them. The picture classifies a run by the
            // deepest point in it, so the shallow lap would be drawn in the full-depth colour and
            // would paint over the very gaps the tabs leave. That is the workshop's own complaint
            // ("the outline's cut line stopped showing where the tabs were"), reintroduced to save a
            // few millimetres. Ending a tab's width short leaves the lap a closed loop of its own and
            // costs one hop over a tab the cutter has just cut down.
            var start = tabs.Count > 0 && walk is not null
                ? tabs[0].AlongNm - (tabs[0].WidthNm / 2.0)
                : 0;

            if (start < 0)
            {
                start += walk!.Perimeter;
            }

            // An open run is cut in alternate directions as it goes deeper.
            //
            // A closed contour ends where it began, so the next pass down starts where the last one
            // finished and costs nothing to repeat. An open one ends at the far end of itself, and
            // taking it the same way round every time means travelling its whole length back first
            // — 7 m of it across this panel. Turning round instead is free, and a slot is cut at
            // full engagement on both sides anyway, so there is no climb-or-conventional to lose.
            var forward = walk is null
                ? Along(contour)
                : start > 0
                    ? walk.Sub(start, start + walk.Perimeter)
                    : IsolationOperation.ToSegments(contour);

            var backward = closed[c] ? forward : Along(Reversed(contour));
            var turn = false;

            if (tabs.Count > 0)
            {
                planned.AddRange(tabs);
                profilesTabbed++;

                // Counted per profile: an edge index is a place on its own profile, so the same
                // number means different edges on two of them. By the index being a real edge
                // rather than by not-Spaced, because a hand-placed tab (6.4) is neither spaced nor
                // on an edge the planner chose — its -1 must not count as one more edge.
                edgesUsed += tabs.Where(t => t.Edge >= 0).Select(t => t.Edge).Distinct().Count();
            }

            // Tabs were asked for and the planner could not place any: they would have consumed the
            // whole profile. Recorded so the notes can say it, because what happens instead is that
            // the profile is cut clean through and the piece comes free on the last pass.
            if (tabs.Count == 0 && outermost && closed[c] && options.TabCount > 0)
            {
                tabsSwallowProfile = true;
            }

            // A tab is only a tab if something cut down to its top.
            //
            // Reported from the workshop: a 0.8 mm board, 0.90 mm of total depth, a cutter whose step
            // down is 1.00 mm — one pass, deeper than the tab, so the gap was jumped on the only pass
            // the profile had and every "tab" was left at the full thickness of the board. The piece
            // does not come free, and nothing in the file says why. The step down is the cutter's and
            // the tab height is ours, so the two need not divide into each other at all: cut the depth
            // each tab's top sits at, whatever the steps work out to.
            //
            // One depth per *distinct* height, now that tabs need not be the same height as each
            // other: a board with a 0.5 mm tab and a 0.3 mm tab is cut down to 0.3 mm of material
            // everywhere the second one is, and to 0.5 mm where the first one is.
            var depthsHere = new List<long>(stepDepths);

            foreach (var top in tabs.Select(t => TabTop(t, options)).Where(t => t > 0).Distinct())
            {
                if (!depthsHere.Contains(top))
                {
                    depthsHere.Add(top);

                    if (!addedTops.Contains(top))
                    {
                        addedTops.Add(top);
                    }
                }
            }

            depthsHere.Sort();

            // The split is the same for every depth that jumps the same tabs, and the set of tabs a
            // pass jumps only grows as it goes deeper — so the count is enough to know whether the
            // last answer still holds.
            var splitFor = -1;
            List<(IReadOnlyList<ArtSegment> Run, long GapAfterNm)> runs = [];

            foreach (var depth in depthsHere)
            {
                // Which tabs this pass has to jump: the ones whose top it is already past.
                var active = tabs.Where(t => depth > TabTop(t, options)).ToList();

                if (active.Count == 0)
                {
                    passes.Add(new ToolpathPass
                    {
                        Path = turn ? backward : forward,
                        DepthNm = depth,
                        Closed = closed[c],
                        Group = group,
                        Stack = c,
                    });

                    turn = !closed[c] && !turn;
                    continue;
                }

                if (active.Count != splitFor)
                {
                    runs = SplitAt(walk!, active);
                    splitFor = active.Count;
                }

                // The tabbed passes go depth by depth, all runs at one depth before the next.
                //
                // The opposite — one run taken to full depth before moving on — sounds tidier and is
                // much worse: an open run ends at the far end of itself, so repeating it at the next
                // depth means travelling its whole length back first. Going round the profile instead
                // costs only the tab gap between one run and the next, and the last run's end is
                // already next to the first run's start.
                foreach (var run in runs)
                {
                    if (run.Run.Count == 0)
                    {
                        continue;
                    }

                    passes.Add(new ToolpathPass
                    {
                        Path = run.Run,
                        DepthNm = depth,
                        Closed = false,
                        Group = group,
                        Stack = c,

                        // A run that follows another is separated from it by one tab, so a gap of
                        // a tab's width is a tab and anything longer is not. Stated as the distance
                        // rather than as "this one is next to that one", because the ordering this
                        // lays down is not the ordering that gets cut — see HopWithinNm.
                        //
                        // Exactly the tab's width, with nothing added. SplitAt cuts the already-offset
                        // contour at half a tab either side of each tab centre, so the gap it leaves
                        // is the tab and no more: measured on a 3.0 mm tab, 3.000 mm along a straight
                        // edge and 2.321 mm across a corner, where the chord cuts inside the path. An
                        // earlier version added the cutter's diameter on the theory that the run ends
                        // sit half a cutter either side, which is not what that method does — and the
                        // slack was not free, because every millimetre of it widens the window in
                        // which a reordered move that is not a tab gets taken at hop height.
                        //
                        // **The width of the tab this run ends at**, not the widest on the board.
                        // Tabs need not be the same size as each other any more, and one wide tab
                        // would otherwise open the window for every crossing on the profile.
                        //
                        // Set on every run, including the first of each depth. That one used to be
                        // left out, on the grounds that what precedes it — a full lap, or the last
                        // run of the depth above — need not be along the profile, so the straight
                        // line to it might cut a corner. True, and it stopped mattering once the
                        // emitter began checking the distance itself: at the hop height the tool is
                        // *above* the stock, so a short move there cuts nothing whatever it passes
                        // over, and a long one is refused on its length rather than on which run it
                        // happens to be.
                        //
                        // From the bench, which is how it was found: "Tabs still seem to go to the
                        // safe height." Six crossings between 1.43 mm and 2.84 mm on the panel were
                        // climbing the full two millimetres, and the only thing marking them out was
                        // being first in their depth.
                        HopWithinNm = run.GapAfterNm,
                    });
                }
            }
        }

        var perPass = Nm.ToMillimetreString(options.DepthPerPassNm, 2);
        var total = Nm.ToMillimetreString(options.TotalDepthNm, 2);
        // The bit, by name, in the program itself. The outline was the one milling program that never
        // said what to put in the spindle — and on a job with a blank it is the same bit that cut the
        // blank, which is worth being able to check against the file rather than the window.
        notes.Add(Invariant($"{steps} passes of {perPass} mm to {total} mm with the {options.Tool.Name}."));

        if (centrelines > 0)
        {
            notes.Add(Invariant(
                $"{centrelines} of those are no wider than the cutter, so each is one pass down its middle rather than a lap around both of its sides."));
        }

        if (voids > 0)
        {
            // Counted in profiles, not contours: one profile can offset into more than one contour.
            notes.Add(Invariant(
                $"Cut on the outside: {pieces.Count(p => p)}. Cut from the inside: {voids}, because they enclose nothing — so the pieces either side of them keep their size."));
        }

        if (options.TabCount > 0 && planned.Count == 0)
        {
            // Asked for and not placed, which is three different sentences at the machine: every
            // tab was removed by hand, or the tabs would have taken up the whole profile, or there
            // was no profile to put them on. All three end with a piece that comes free on the last
            // pass, and that is the part the operator has to know — blaming the profile for a
            // removal somebody chose would be the header lying about who decided.
            notes.Add(options.TabMoves.Any(m => m.Index >= 0 && m.AlongNm is null)
                ? "Every tab was removed by hand, so nothing holds the piece on the last pass. Hold it down."
                : tabsSwallowProfile
                    ? Invariant($"The {options.TabCount} tabs asked for are wider than the profile they would hold, so it is cut right through and the piece comes free on the last pass. Hold it down.")
                    : "No profile here takes tabs, so nothing holds the piece on the last pass. Hold it down.");
        }
        else if (planned.Count > 0)
        {
            var outerCount = depths.Count(d => d == 0);
            var widths = planned.Select(t => t.WidthNm).Distinct().Order().ToList();
            var heights = planned.Select(t => t.HeightNm).Distinct().Order().ToList();

            // One number when they all agree, a range when they do not, because a tab is allowed to
            // be its own size now and a single figure would be a guess at which one.
            var wide = widths.Count == 1
                ? Invariant($"{Nm.ToMillimetreString(widths[0], 1)} mm wide")
                : Invariant($"{Nm.ToMillimetreString(widths[0], 1)} to {Nm.ToMillimetreString(widths[^1], 1)} mm wide");

            var under = heights.Count == 1
                ? Invariant($"{Nm.ToMillimetreString(heights[0], 2)} mm of material left under each")
                : Invariant($"{Nm.ToMillimetreString(heights[0], 2)} to {Nm.ToMillimetreString(heights[^1], 2)} mm of material left under them");

            notes.Add(Invariant($"{planned.Count} tabs, {wide}, {under}."));

            // Where they are, and why there. This is the line that answers the report this work came
            // from — "they may not be in the best locations" — so it says what chose the positions:
            // the edges, the fallback spacing, or a hand (6.4). An operator about to snap a tab out
            // deserves to know which ones nobody's rule vouches for.
            var where = profilesTabbed > 1
                ? Invariant($"{edgesUsed} straight edge(s) across {profilesTabbed} profiles")
                : Invariant($"{edgesUsed} straight edge(s) of the profile");

            var byHand = planned.Count(t => t.Moved || t.Added);
            var spacedEvenly = planned.Count(t => t.Spaced);
            var centred = planned.Count - byHand - spacedEvenly;

            if (byHand == 0)
            {
                notes.Add(spacedEvenly == planned.Count
                    ? "Spaced evenly: no edge long enough to centre a tab on."
                    : Invariant($"Centred on {where}, never across a corner."));
            }
            else
            {
                var account = new List<string>();

                if (centred > 0)
                {
                    account.Add(Invariant($"{centred} centred on {where}, never across a corner"));
                }

                if (spacedEvenly > 0)
                {
                    account.Add(Invariant($"{spacedEvenly} spaced evenly"));
                }

                account.Add(Invariant($"{byHand} placed by hand"));

                notes.Add(string.Join("; ", account) + ".");
            }

            // Against the profiles that could have had tabs, not the ones that got them. Found by
            // `/code-review`: a profile too small to hold any was left out of the multiplier, so a
            // board of two profiles where one held three and the other none reported "4 were asked
            // for" — and the guard could suppress the note altogether.
            var asked = options.TabCount * Math.Max(profilesEligible, 1);

            if (planned.Count < asked)
            {
                // With hand changes in play the shortfall is not the edges' doing: a removed tab
                // is a choice, and blaming clearance for it would be the header lying.
                notes.Add(options.TabMoves.Count > 0
                    ? Invariant($"{asked} were asked for: {planned.Count} after the hand-set changes.")
                    : Invariant($"{asked} were asked for: the straight edges hold {planned.Count} with clearance either side of each."));
            }

            if (addedTops.Count == 1)
            {
                notes.Add(Invariant(
                    $"One extra pass at {Nm.ToMillimetreString(addedTops[0], 2)} mm, which is where the tabs start: the step down does not reach it on its own, and a tab nothing cuts down to is the full thickness of the board."));
            }
            else if (addedTops.Count > 1)
            {
                var tops = string.Join(", ", addedTops.Order().Select(t => Nm.ToMillimetreString(t, 2)));

                notes.Add(Invariant(
                    $"Extra passes at {tops} mm, which is where the tabs start: the step down reaches none of them on its own, and a tab nothing cuts down to is the full thickness of the board."));
            }

            // A tab as tall as the board is the board. Said plainly, because what it looks like at
            // the machine is a cut that simply did not release the piece.
            var uncut = planned.Where(t => TabTop(t, options) <= 0).ToList();

            if (uncut.Count > 0)
            {
                var tallest = Nm.ToMillimetreString(uncut.Max(t => t.HeightNm), 2);
                var which = uncut.Count == planned.Count
                    ? "The tabs are"
                    : Invariant($"{uncut.Count} of the tabs are");

                notes.Add(Invariant(
                    $"{which} up to {tallest} mm tall and this cut is only {total} mm deep, so nothing is cut away at them: they hold the full thickness of the board and have to be cut by hand."));
            }

            if (depths.Count > outerCount)
            {
                var inner = depths.Count - outerCount;
                notes.Add(
                    Invariant($"Tabs on the {outerCount} outer profile(s) only; the {inner} inside ")
                    + "are held by whatever joins them in the design.");
            }
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
    /// <summary>
    /// Which profiles bound a piece to keep, and which bound a void to cut away.
    ///
    /// Two rules, in order.
    ///
    /// **An outermost profile is a piece.** Nothing encloses it, so the waste is the stock around
    /// it, and the cutter belongs outside.
    ///
    /// **A nested profile is a void unless it holds something.** A slot, a window, the routed
    /// channel between the boards of a panel — all of them are enclosed by the profile around them
    /// and all of them are material to remove. The counter-example is the hand-panelised file where
    /// the stock is one rectangle and each board is another rectangle inside it; those are nested
    /// and they are still pieces. The difference that matters is not size or shape, because a
    /// channel can be wide and a board can be small: it is that a board has artwork on it and a
    /// channel has nothing. Empty means waste.
    ///
    /// With no artwork to test against there is no evidence, so everything is a piece and the cut
    /// stays where it has always been. Nesting on its own is not enough to flip a profile: a slot
    /// and a board inside a hand-cut frame nest identically, and the two want opposite sides.
    ///
    /// Public because the export review has to say which way round it went before anybody presses
    /// go, and a claim about the cut that is computed from the same rule that made it is better
    /// than a claim written by hand beside it.
    /// </summary>
    /// <param name="outline">The profiles, as drawn.</param>
    /// <param name="keep">The board's artwork. Null or empty means no evidence.</param>
    /// <param name="reachNm">
    /// The cutter's diameter — the band a cut inside a profile sweeps in from its boundary.
    /// </param>
    public static bool[] PiecesAmong(Paths64 outline, Paths64? keep, long reachNm)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var pieces = new bool[outline.Count];
        Array.Fill(pieces, true);

        if (keep is null || keep.Count == 0)
        {
            return pieces;
        }

        var nesting = NestingOf(outline);
        var artwork = keep.Where(k => k.Count > 2).ToList();
        var artBounds = artwork.Select(k => Clipper.GetBounds(new Paths64 { k })).ToList();

        for (var i = 0; i < outline.Count; i++)
        {
            pieces[i] = nesting.GetValueOrDefault(i) == 0
                || Holds(outline[i], reachNm, artwork, artBounds);
        }

        return pieces;
    }

    /// <summary>
    /// Whether cutting inside this profile would destroy any of the board.
    ///
    /// That is the question, rather than "is there artwork inside it", and the difference is the
    /// whole of this method. Two earlier attempts got it wrong in the same place. A vertex test
    /// failed because a ground pour runs right to the board edge, so on a panel the copper ring
    /// beside a routed channel begins a micron inside it — nine rings per channel on a real 66-up
    /// panel. An overlapping-area test failed one step along for the same reason: the profile is
    /// the *outside* of the pen the outline was drawn with, so it overhangs the true edge by half a
    /// pen width, and the pour sitting in that overhang measured 0.49 mm2 per channel against a
    /// 0.39 mm2 threshold. Both were measuring the edge, and the edge is where every board's copper
    /// ends.
    ///
    /// So look where the cut would actually go. A cutter run inside a profile sweeps the band from
    /// its boundary inward by one diameter, and what lies further in than that is what such a cut
    /// would spare. A routed channel is narrower than the cutter and has nothing left at all; a
    /// board inside a frame still has almost all of itself.
    /// </summary>
    private static bool Holds(Path64 profile, long reachNm, List<Path64> artwork, List<Rect64> artBounds)
    {
        if (profile.Count < 3)
        {
            return false;
        }

        var inner = Polygons.Inflate(
            new Paths64 { profile }, -Math.Max(1, reachNm), JoinType.Miter, EndType.Polygon);

        if (inner.Count == 0)
        {
            return false;
        }

        var region = Clipper.GetBounds(inner);
        var near = new Paths64();

        for (var i = 0; i < artwork.Count; i++)
        {
            if (artBounds[i].Intersects(region))
            {
                near.Add(artwork[i]);
            }
        }

        if (near.Count == 0)
        {
            return false;
        }


        var shared = Math.Abs(Clipper.Area(Polygons.Intersect(near, inner)));

        // A hundredth of a square millimetre, to ignore the slivers an offset leaves behind. Both
        // real cases are orders of magnitude away from it, in opposite directions.
        return shared > 0.01 * Nm.PerMillimetre * Nm.PerMillimetre;
    }

    private static Path64 Reversed(Path64 path)
    {
        var back = new Path64(path);
        back.Reverse();

        return back;
    }

    /// <summary>An open path's segments, with no closing move back to the start.</summary>
    private static ArtSegment[] Along(Path64 path)
    {
        var segments = new ArtSegment[path.Count - 1];

        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = ArtSegment.Line(
                new Point2(path[i].X, path[i].Y), new Point2(path[i + 1].X, path[i + 1].Y));
        }

        return segments;
    }

    /// <summary>
    /// How many of the other profiles each profile sits inside.
    ///
    /// A bounds test first, because on a 66-up panel that rejects almost every pair immediately;
    /// only the survivors pay for a point-in-polygon test.
    /// </summary>
    private static Dictionary<int, int> NestingOf(Paths64 contours)
    {
        var nesting = new Dictionary<int, int>();
        var bounds = contours.Select(c => Clipper.GetBounds(new Paths64 { c })).ToList();

        for (var i = 0; i < contours.Count; i++)
        {
            if (contours[i].Count == 0)
            {
                continue;
            }

            var depth = 0;
            for (var j = 0; j < contours.Count; j++)
            {
                if (i == j || contours[j].Count < 3 || !bounds[j].Contains(bounds[i]))
                {
                    continue;
                }

                if (Polygons.PointIn(contours[i][0], contours[j]) == PointInPolygonResult.IsInside)
                {
                    depth++;
                }
            }

            nesting[i] = depth;
        }

        return nesting;
    }

    /// <summary>
    /// The tabs this outline would be cut with, and the sizes the board would give them if nobody
    /// had, without building the program.
    ///
    /// **So that the dialog and the file cannot disagree.** The tabs dialog has to show where the
    /// tabs are before anything is emitted, and the only honest way to do that is to ask the same
    /// question the emitter asks: the same offset contours, the same planner, the same options. A
    /// second implementation in the window would be a picture of a board nobody is going to cut.
    ///
    /// **One call rather than two**, because the expensive half is the shaping — offsetting every
    /// profile to the waste side — and the window needs both answers at once. A `/code-review` pass
    /// counted it: the panel shapes 102 profiles, and asking the two questions separately did all of
    /// that twice, on the UI thread, per click.
    ///
    /// The outermost closed profile's tabs, which is the one that gets them — the board, or a panel's
    /// frame.
    /// </summary>
    public static TabPlanning PlanTabs(Paths64 outline, OutlineOptions options)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(options);

        var height = TabPlan.DefaultHeightFor(options.BoardThicknessNm);
        var (contours, depths, closed, _, _, _) = Shape(outline, options);

        var width = TabPlan.DefaultWidthFor([]);
        var found = false;

        for (var c = 0; c < contours.Count; c++)
        {
            if (depths[c] != 0 || !closed[c] || contours[c].Count < 3)
            {
                continue;
            }

            var candidate = TabPlan.DefaultWidthFor(contours[c]);

            // The first profile that could take tabs decides the derived sizes when none are
            // placed anywhere: a board that cannot hold the tabs asked for still has a width its
            // own narrow side would give, and that is the number the dialog shows beside the boxes.
            if (!found)
            {
                width = candidate;
                found = true;
            }

            var tabs = TabPlan.For(contours[c], options);

            // **The width comes from the profile the tabs came from.** Found by `/code-review`: it
            // used to be the first *eligible* profile's, so a board whose first profile held no
            // tabs reported one profile's derived width beside another profile's tabs. The dialog
            // seeds its board-wide box from this number and each row from its own tab's width, so
            // every row read as hand-set and pressing Save with no changes wrote an explicit edit
            // for every tab.
            if (tabs.Count > 0)
            {
                return new TabPlanning(tabs, candidate, height, contours[c]);
            }
        }

        return new TabPlanning([], width, height);
    }

    /// <summary>
    /// The profiles as the cutter will follow them: offset to the waste side, nested, and with a
    /// channel no wider than the cutter replaced by one pass down its middle.
    /// </summary>
    private static (Paths64 Contours, List<int> Depths, List<bool> Closed, IReadOnlyList<bool> Pieces, int Voids, int Centrelines) Shape(
        Paths64 outline,
        OutlineOptions options)
    {
        var radius = options.Tool.DiameterNm / 2;

        // Nesting is worked out on the profiles as drawn, before any offsetting, because the offset
        // now depends on it. It used to be derived from the offset contours, which was harmless
        // only while every profile moved the same way.
        var nesting = NestingOf(outline);
        var pieces = PiecesAmong(outline, options.Keep, options.Tool.DiameterNm);

        // Each profile is offset on its own, not as one polygon set.
        //
        // Offsetting them together makes Clipper fill the whole set: a panel frame's own rectangle
        // then covers every board inside it and they disappear, which is a program that cuts the
        // frame out and leaves every board attached to it. Independently, each profile keeps its
        // own boundary — and its own side.
        var contours = new Paths64();
        var depths = new List<int>();
        var closed = new List<bool>();
        var voids = 0;
        var centrelines = 0;

        for (var p = 0; p < outline.Count; p++)
        {
            // Which side of the line the cutter runs on, per profile.
            //
            // A piece keeps its size when the cutter passes outside it; a void keeps its size when
            // the cutter stays inside it. Getting this backwards on a panel's routed channels does
            // not merely cut in the wrong place — it cuts two grooves through the boards either
            // side and leaves the channel itself uncut, so the panel never comes apart.
            var outward = pieces[p] == options.CutOutside;

            if (!pieces[p])
            {
                voids++;
            }

            var grown = Polygons.Inflate(
                new Paths64 { outline[p] },
                outward ? radius : -radius,
                JoinType.Round,
                EndType.Polygon,
                arcTolerance: options.SagittaNm);

            var depth = nesting.GetValueOrDefault(p);

            // A channel about as wide as the cutter is one pass down its middle, not a lap around
            // the sliver left when it is offset inward. Only when the middle demonstrably covers
            // what the lap would have: VoidCentreline checks and hands back null if it does not.
            var middle = pieces[p]
                ? null
                : VoidCentreline.For(grown, options.Tool.DiameterNm, options.SagittaNm);

            if (middle is not null)
            {
                centrelines++;
            }

            foreach (var contour in middle ?? grown)
            {
                contours.Add(contour);
                depths.Add(depth);
                closed.Add(middle is null);
            }
        }

        return (contours, depths, closed, pieces, voids, centrelines);
    }
    /// <summary>
    /// One closed profile, measured along itself.
    ///
    /// The tabs, the runs between them and the full laps are all expressed as distances round the
    /// profile, so the segment lengths and their running totals are worth computing once: the planner
    /// and the splitter would otherwise walk the same contour twice for every depth on a board.
    /// </summary>
    private sealed class ProfileWalk
    {
        public ProfileWalk(Path64 contour)
        {
            Segments = IsolationOperation.ToSegments(contour);
            Starts = new double[Segments.Count];
            Lengths = new double[Segments.Count];

            for (var i = 0; i < Segments.Count; i++)
            {
                Starts[i] = Perimeter;
                Lengths[i] = Segments[i].From.DistanceTo(Segments[i].To);
                Perimeter += Lengths[i];
            }
        }

        public IReadOnlyList<ArtSegment> Segments { get; }

        public double[] Starts { get; }

        public double[] Lengths { get; }

        public double Perimeter { get; }

        /// <summary>
        /// The stretch between two distances along the profile, wrapping past the start vertex if it
        /// has to.
        ///
        /// A segment wholly inside the stretch is kept exactly as it was rather than rebuilt from its
        /// endpoints, so a run crossing a tessellated corner keeps that corner's own vertices to the
        /// nanometre.
        /// </summary>
        public List<ArtSegment> Sub(double from, double to)
        {
            var run = new List<ArtSegment>();

            // Two laps at most: a stretch can begin part-way round and end part-way round again, and
            // nothing longer than one whole profile is ever asked for.
            for (var lap = 0; lap < 2; lap++)
            {
                for (var i = 0; i < Segments.Count; i++)
                {
                    if (Lengths[i] <= 0)
                    {
                        continue;
                    }

                    var s0 = Starts[i] + (lap * Perimeter);
                    var s1 = s0 + Lengths[i];

                    var a = Math.Max(from, s0);
                    var b = Math.Min(to, s1);

                    if (b - a <= 1e-9)
                    {
                        continue;
                    }

                    run.Add(a <= s0 + 1e-9 && b >= s1 - 1e-9
                        ? Segments[i]
                        : ArtSegment.Line(
                            Along(Segments[i], (a - s0) / Lengths[i]),
                            Along(Segments[i], (b - s0) / Lengths[i])));
                }
            }

            return run;
        }
    }

    /// <summary>The depth a tab's top sits at: below this, a pass has to jump it.</summary>
    private static long TabTop(PlacedTab tab, OutlineOptions options) =>
        options.TotalDepthNm - tab.HeightNm - options.BreakThroughNm;

    /// <summary>
    /// The profile broken into the runs between its tabs, each with the width of the gap that
    /// follows it.
    ///
    /// **Runs are the stretches between consecutive tabs**, taken in order, which is a different
    /// construction from the one this replaced and fixes two things by being so. The old version
    /// walked every segment asking "is this bit under a tab", which only worked because tab centres
    /// sat at whole multiples of the spacing — one of them always straddled the seam where the
    /// contour closes, so the walk started and finished inside a gap. Tabs centred on edges do not
    /// straddle anything, and that walk would have ended the last run at the seam and started the
    /// first one there: an extra lift, in the middle of an edge, for nothing.
    ///
    /// Here the seam is simply not a boundary. A run that spans it is one run, because a run's own
    /// ends are the two tabs either side of it.
    ///
    /// The gap after each run is the tab that ends it, which is what tells the emitter how far a
    /// crossing may be and still be a tab rather than a reordered move.
    /// </summary>
    private static List<(IReadOnlyList<ArtSegment> Run, long GapAfterNm)> SplitAt(
        ProfileWalk walk,
        List<PlacedTab> tabs)
    {
        var runs = new List<(IReadOnlyList<ArtSegment> Run, long GapAfterNm)>();

        if (walk.Perimeter <= 0 || tabs.Count == 0)
        {
            return runs;
        }

        var ordered = tabs.OrderBy(t => t.AlongNm).ToList();

        for (var t = 0; t < ordered.Count; t++)
        {
            var next = ordered[(t + 1) % ordered.Count];

            // From the far side of this tab to the near side of the next one.
            var from = ordered[t].AlongNm + (ordered[t].WidthNm / 2.0);
            var to = next.AlongNm - (next.WidthNm / 2.0);

            // **Only the last run spans the start vertex**, and a `/code-review` pass is why this is
            // said by position rather than by comparing the two distances. Wrapping whenever the
            // next tab came out earlier could not tell "this run crosses the seam" from "these two
            // tabs overlap" — and two tabs widened by hand until they met produced a run of nearly
            // the whole profile, cut at every tabbed depth, straight through every other tab on the
            // board. That is a piece cut fully free and thrown on the last pass, which is the one
            // thing tabs exist to prevent.
            if (t == ordered.Count - 1)
            {
                to += walk.Perimeter;
            }

            // Two tabs close enough to overlap leave no run between them, which can only happen when
            // one has been widened by hand past the room its edge has. Nothing is emitted for it and
            // the two become one wider gap; the notes carry the widths as they were set.
            if (to - from <= 0)
            {
                continue;
            }

            runs.Add((walk.Sub(from, to), next.WidthNm));
        }

        return runs;
    }

    private static Point2 Along(ArtSegment segment, double t) => new(
        segment.From.X + (long)Math.Round((segment.To.X - segment.From.X) * t),
        segment.From.Y + (long)Math.Round((segment.To.Y - segment.From.Y) * t));

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

}
