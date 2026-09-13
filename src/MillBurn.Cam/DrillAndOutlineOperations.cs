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

    /// <summary>Width of each holding tab.</summary>
    public long TabWidthNm { get; init; } = Nm.FromMillimetres(3.0);

    /// <summary>Material left under a tab, for snapping the board out afterwards.</summary>
    public long TabHeightNm { get; init; } = Nm.FromMillimetres(0.5);

    /// <summary>How many tabs to space around the outline. Zero cuts the board fully free.</summary>
    public int TabCount { get; init; } = 4;

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

            var grown = Clipper.InflatePaths(
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

        var shallowDepths = new List<long>();
        var tabbedDepths = new List<long>();
        var allDepths = new List<long>();

        for (var step = 1; step <= steps; step++)
        {
            var depth = Math.Min(options.TotalDepthNm, step * options.DepthPerPassNm);
            var tabbed = options.TabCount > 0
                && depth > options.TotalDepthNm - options.TabHeightNm - options.BreakThroughNm;

            (tabbed ? tabbedDepths : shallowDepths).Add(depth);
            allDepths.Add(depth);
        }

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

            // An open run is cut in alternate directions as it goes deeper.
            //
            // A closed contour ends where it began, so the next pass down starts where the last one
            // finished and costs nothing to repeat. An open one ends at the far end of itself, and
            // taking it the same way round every time means travelling its whole length back first
            // — 7 m of it across this panel. Turning round instead is free, and a slot is cut at
            // full engagement on both sides anyway, so there is no climb-or-conventional to lose.
            var forward = closed[c] ? IsolationOperation.ToSegments(contour) : Along(contour);
            var backward = closed[c] ? forward : Along(Reversed(contour));
            var turn = false;

            foreach (var depth in outermost ? shallowDepths : allDepths)
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
            }

            // The tabbed passes go depth by depth, all runs at one depth before the next.
            //
            // The opposite — one run taken to full depth before moving on — sounds tidier and is
            // much worse: an open run ends at the far end of itself, so repeating it at the next
            // depth means travelling its whole length back first. Going round the profile instead
            // costs only the tab gap between one run and the next, and the last run's end is
            // already next to the first run's start.
            var runs = !outermost || !closed[c] || tabbedDepths.Count == 0
                ? []
                : SplitForTabs(contour, options).ToList();

            foreach (var depth in tabbedDepths)
            {
                foreach (var run in runs)
                {
                    passes.Add(new ToolpathPass
                    {
                        Path = run,
                        DepthNm = depth,
                        Closed = false,
                        Group = group,
                        Stack = c,
                    });
                }
            }
        }

        var perPass = Nm.ToMillimetreString(options.DepthPerPassNm, 2);
        var total = Nm.ToMillimetreString(options.TotalDepthNm, 2);
        notes.Add(Invariant($"{steps} passes of {perPass} mm to {total} mm."));

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

        if (options.TabCount > 0)
        {
            var tabWidth = Nm.ToMillimetreString(options.TabWidthNm, 1);
            var tabHeight = Nm.ToMillimetreString(options.TabHeightNm, 2);
            var outerCount = depths.Count(d => d == 0);

            notes.Add(Invariant(
                $"{options.TabCount} tabs, {tabWidth} mm wide, {tabHeight} mm of material left under each."));

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

        var inner = Clipper.InflatePaths(
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

        var shared = Math.Abs(Clipper.Area(Clipper.Intersect(near, inner, FillRule.NonZero)));

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

                if (Clipper.PointInPolygon(contours[i][0], contours[j]) == PointInPolygonResult.IsInside)
                {
                    depth++;
                }
            }

            nesting[i] = depth;
        }

        return nesting;
    }

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

            if (length <= 0)
            {
                continue;
            }

            // Every place inside this segment where a tab starts or ends, so the segment can be cut
            // there rather than kept or dropped whole.
            //
            // This is what was wrong. The test used to be made once per segment, against the
            // segment's own midpoint — which works only while segments are short. An offset
            // rectangle is four straight edges and four tessellated corners, so a 37 mm edge is a
            // single segment and a tab landing anywhere along it was simply not noticed. Asking for
            // four tabs on a 21 x 37 mm board produced two, both at corners, in opposing corners,
            // because the corners were the only places with segments short enough to be seen.
            var marks = new List<double> { travelled };

            for (var k = (long)Math.Floor(travelled / spacing) - 1;
                k <= (long)Math.Ceiling((travelled + length) / spacing) + 1;
                k++)
            {
                foreach (var edge in new[] { (k * spacing) - half, (k * spacing) + half })
                {
                    if (edge > travelled + 1e-9 && edge < travelled + length - 1e-9)
                    {
                        marks.Add(edge);
                    }
                }
            }

            marks.Add(travelled + length);
            marks.Sort();

            for (var i = 0; i + 1 < marks.Count; i++)
            {
                var from = marks[i];
                var to = marks[i + 1];

                if (to - from <= 1e-9)
                {
                    continue;
                }

                if (UnderTab((from + to) / 2, spacing, half))
                {
                    if (run.Count > 0)
                    {
                        yield return run;
                        run = [];
                    }

                    continue;
                }

                // An arc is kept whole: the offset corners are tessellated to lines before they get
                // here, so nothing that reaches this is curved, and slicing one by distance would
                // be wrong if anything ever did.
                run.Add(segment.IsArc
                    ? segment
                    : ArtSegment.Line(
                        Along(segment, (from - travelled) / length),
                        Along(segment, (to - travelled) / length)));
            }

            travelled += length;
        }

        if (run.Count > 0)
        {
            yield return run;
        }
    }

    /// <summary>
    /// Whether a point this far around the contour is under a tab.
    ///
    /// Tab centres sit at whole multiples of the spacing, so the one at zero straddles the seam
    /// where the contour closes — which is right, and is why <c>TabCount</c> gaps come out of
    /// <c>TabCount</c> centres rather than one more.
    /// </summary>
    private static bool UnderTab(double at, double spacing, double half) =>
        Math.Abs(((at + (spacing / 2)) % spacing) - (spacing / 2)) < half;

    private static Point2 Along(ArtSegment segment, double t) => new(
        segment.From.X + (long)Math.Round((segment.To.X - segment.From.X) * t),
        segment.From.Y + (long)Math.Round((segment.To.Y - segment.From.Y) * t));

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
