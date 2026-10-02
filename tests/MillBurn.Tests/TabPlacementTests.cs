using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Geometry;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Where the tabs go, now that the board has a say in it.
///
/// **From the workshop, on a 21 x 37 mm board**, after the bug that lost two of four tabs was fixed
/// and all four appeared: *"They are large tabs and may not be in the best locations."* Both halves
/// of that follow from the same thing — a tab was a count and two global numbers. The perimeter was
/// divided by the count and a tab went every spacing from whatever vertex Clipper's offset happened
/// to start at, all the same width, all leaving the same material. Nothing looked at the board, so a
/// tab could land across a corner, and 3 mm was 3 mm whether the edge was 21 mm long or 100.
///
/// What these tests pin is the replacement: one tab centred on each straight edge, sized from the
/// board, and never on a corner. Recorded in
/// [06 §6.4](../../Documentation/06-Roadmap-and-Risks.md).
/// </summary>
public sealed class TabPlacementTests(ITestOutputHelper output)
{
    private static long Mm(double mm) => Nm.FromMillimetres(mm);

    private static Path64 Rect(double widthMm, double heightMm) =>
    [
        new(0, 0),
        new(Mm(widthMm), 0),
        new(Mm(widthMm), Mm(heightMm)),
        new(0, Mm(heightMm)),
    ];

    /// <summary>A circle, as a closed polygon: the shape with no straight edge to centre a tab on.</summary>
    private static Path64 Circle(double radiusMm, int points = 180)
    {
        var path = new Path64();

        for (var i = 0; i < points; i++)
        {
            var angle = 2 * Math.PI * i / points;
            path.Add(new Point64(
                (long)Math.Round(Mm(radiusMm) * Math.Cos(angle)),
                (long)Math.Round(Mm(radiusMm) * Math.Sin(angle))));
        }

        return path;
    }

    private static OutlineOptions Options(int tabs = 4) => new()
    {
        TabCount = tabs,
        BoardThicknessNm = Mm(0.8),
        DepthPerPassNm = Mm(1.0),
        Keep = [],
    };

    /// <summary>The distance from a point to the nearest corner of a rectangle that size.</summary>
    private static double ToNearestCorner(Point2 at, double widthMm, double heightMm)
    {
        Point2[] corners =
        [
            new(0, 0),
            new(Mm(widthMm), 0),
            new(Mm(widthMm), Mm(heightMm)),
            new(0, Mm(heightMm)),
        ];

        return corners.Min(c => c.DistanceTo(at));
    }

    /// <summary>
    /// Four tabs on a rectangle are one centred on each side.
    ///
    /// **The thing the workshop draws by hand**, and the reason the default count is four. Asserted
    /// against the four side midpoints rather than against four remembered coordinates, so it holds
    /// for any rectangle and fails if a tab slides along its edge.
    /// </summary>
    [Fact]
    public void FourTabsOnARectangleAreOneCentredOnEachSide()
    {
        var tabs = TabPlan.For(Rect(21, 37), Options());

        foreach (var tab in tabs)
        {
            output.WriteLine(
                $"edge {tab.Edge} at {tab.At}, {Nm.ToMillimetreString(tab.WidthNm, 1)} mm wide");
        }

        Assert.Equal(4, tabs.Count);

        // One per edge: four tabs on four different edges, which is the placement rule stated in the
        // only terms that cannot be satisfied by luck.
        Assert.Equal(4, tabs.Select(t => t.Edge).Distinct().Count());

        Point2[] middles =
        [
            new(Mm(10.5), 0),
            new(Mm(21), Mm(18.5)),
            new(Mm(10.5), Mm(37)),
            new(0, Mm(18.5)),
        ];

        foreach (var middle in middles)
        {
            Assert.Contains(tabs, t => t.At.DistanceTo(middle) < Mm(0.01));
        }
    }

    /// <summary>
    /// And no tab is anywhere near a corner.
    ///
    /// **The half of the report this answers directly.** Equal arc length from an arbitrary seam puts
    /// a tab wherever it lands, and a tab across a corner is the worst place for one: it holds badly,
    /// it is the hardest to clean up, and on a rounded corner the cut either side of it runs at an
    /// angle to the snap.
    ///
    /// The bound is the tab's own width, which is the weakest claim worth making — what it rules out
    /// is a tab *touching* a corner at all, since a tab is centred on its edge and the shortest edge
    /// here is 21 mm.
    /// </summary>
    [Fact]
    public void NoTabIsPlacedNearACorner()
    {
        var tabs = TabPlan.For(Rect(21, 37), Options());

        // Without this the loop below is vacuous: no tabs, nothing near a corner, green.
        Assert.Equal(4, tabs.Count);

        foreach (var tab in tabs)
        {
            var clear = ToNearestCorner(tab.At, 21, 37);

            output.WriteLine(
                $"{tab.At} is {Nm.ToMillimetreString((long)clear, 2)} mm from the nearest corner");

            Assert.True(
                clear > tab.WidthNm,
                $"a {Nm.ToMillimetreString(tab.WidthNm, 1)} mm tab sits {Nm.ToMillimetreString((long)clear, 2)} mm from a corner");
        }
    }

    /// <summary>
    /// More tabs than edges go to the longest edges first.
    ///
    /// The count stays a count: asking for six on a rectangle gets six, two on each long edge and one
    /// on each short one, rather than four and a shrug. What it is not any more is a spacing — the
    /// edges decide where, the count decides how many.
    /// </summary>
    [Fact]
    public void MoreTabsThanEdgesGoToTheLongestEdgesFirst()
    {
        var tabs = TabPlan.For(Rect(21, 37), Options(6));

        var perEdge = tabs.GroupBy(t => t.Edge).Select(g => g.Count()).OrderDescending().ToList();

        output.WriteLine($"{tabs.Count} tabs over {perEdge.Count} edges: {string.Join(", ", perEdge)}");

        Assert.Equal(6, tabs.Count);
        Assert.Equal([2, 2, 1, 1], perEdge);

        // The two that carry a pair are the long ones. Stated in millimetres because "the long edge"
        // is a fact about the board and not about the order edges happen to be walked in.
        foreach (var pair in tabs.GroupBy(t => t.Edge).Where(g => g.Count() == 2))
        {
            Assert.Equal(Mm(37), (long)Math.Round(pair.First().EdgeLengthNm));
        }
    }

    /// <summary>
    /// A profile with no straight edge still gets its tabs, and says that nothing chose where.
    ///
    /// A round board is a real shape — the app has cut one — and the honest answer is not to refuse:
    /// a profile with no tabs throws the piece on its last pass. So the old rule stays as the
    /// fallback, and the tab says of itself that it was spaced rather than placed, which is what lets
    /// the program report the difference instead of implying a choice nobody made.
    /// </summary>
    [Fact]
    public void ARoundProfileFallsBackToEvenSpacing()
    {
        var tabs = TabPlan.For(Circle(15), Options());

        output.WriteLine($"{tabs.Count} tabs, spaced: {tabs.Count(t => t.Spaced)}");

        Assert.Equal(4, tabs.Count);
        Assert.All(tabs, t => Assert.True(t.Spaced, $"a tab on edge {t.Edge} of a circle"));

        // Evenly, which is the only property the fallback claims: **a quarter of the circumference
        // apart**, within a tenth of a millimetre — the closing gap included, and measured against
        // the circumference rather than against the other gaps.
        //
        // A `/describe-test` pass is why it is written that way. Comparing the gaps only with each
        // other left two holes: three equal gaps of any size satisfied it, and the gap from the last
        // tab round to the first was never computed at all — so four tabs crowded into one arc, or
        // three spread evenly with a fourth anywhere, would both have passed a test named for even
        // spacing.
        var circumference = 0.0;
        var circle = Circle(15);

        for (var i = 0; i < circle.Count; i++)
        {
            var a = circle[i];
            var b = circle[(i + 1) % circle.Count];

            circumference += new Point2(a.X, a.Y).DistanceTo(new Point2(b.X, b.Y));
        }

        var quarter = circumference / 4;

        var gaps = tabs
            .Zip(tabs.Skip(1), (a, b) => b.AlongNm - a.AlongNm)
            .Append(circumference - tabs[^1].AlongNm + tabs[0].AlongNm)
            .ToList();

        output.WriteLine(
            $"{Nm.ToMillimetreString((long)circumference, 2)} mm round, a quarter is "
            + $"{Nm.ToMillimetreString((long)quarter, 2)} mm");

        Assert.Equal(4, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(quarter, g, Mm(0.1)));
    }

    /// <summary>
    /// A generously rounded corner is not an edge, however finely it is drawn.
    ///
    /// **Found by `/code-review`, and the arithmetic is the whole point.** Straightness was tested
    /// between each segment and the next, which is a limit on one turn rather than on the sum of
    /// them. How finely a curve is drawn is set by the sagitta — 0.001 mm by default — so the turn
    /// per segment is about `2·acos(1 − 0.001/r)`, which falls below the pairwise tolerance at a
    /// radius of about 11 mm. A 15 mm corner radius therefore chained into one long "straight edge"
    /// of 23 mm, which is room for a tab and then some: the tab would have been centred on a curve
    /// while the program said *"never across a corner"* and the dialog said *"on a 23.6 mm edge"*.
    ///
    /// The board here is a 60 x 40 mm rectangle with 15 mm corners — a shape somebody actually
    /// draws — and what is asserted is that every tab lands on one of the four flats rather than
    /// anywhere on the quarter-circles.
    /// </summary>
    [Fact]
    public void ATabIsNeverPlacedOnARoundedCorner()
    {
        var tabs = TabPlan.For(RoundedRect(60, 40, 15), Options());

        foreach (var tab in tabs)
        {
            output.WriteLine(
                $"{tab.At} on a {Nm.ToMillimetreString((long)tab.EdgeLengthNm, 1)} mm edge");
        }

        Assert.NotEmpty(tabs);

        // The flats: 30 mm across the middle of the long sides, 10 mm up the middle of the short
        // ones. A tab on a corner has both coordinates outside those bands.
        foreach (var tab in tabs)
        {
            var onLongSide = tab.At.X > Mm(15) && tab.At.X < Mm(45);
            var onShortSide = tab.At.Y > Mm(15) && tab.At.Y < Mm(25);

            Assert.True(
                onLongSide || onShortSide,
                $"a tab at {tab.At} is on one of the 15 mm corner radii");
        }
    }

    /// <summary>A rectangle with quarter-circle corners, drawn at the tessellation the cutter uses.</summary>
    private static Path64 RoundedRect(double widthMm, double heightMm, double radiusMm)
    {
        var path = new Path64();
        var r = Mm(radiusMm);

        // Centres of the four corner arcs, in the order they are swept.
        (long X, long Y, double From)[] corners =
        [
            (Mm(widthMm) - r, r, -Math.PI / 2),
            (Mm(widthMm) - r, Mm(heightMm) - r, 0),
            (r, Mm(heightMm) - r, Math.PI / 2),
            (r, r, Math.PI),
        ];

        // Fine enough that each step is well under a degree, which is the case the pairwise test
        // could not see.
        const int Steps = 120;

        foreach (var (cx, cy, from) in corners)
        {
            for (var i = 0; i <= Steps; i++)
            {
                var angle = from + (Math.PI / 2 * i / Steps);

                path.Add(new Point64(
                    cx + (long)Math.Round(r * Math.Cos(angle)),
                    cy + (long)Math.Round(r * Math.Sin(angle))));
            }
        }

        return path;
    }

    /// <summary>
    /// The default width comes off the board's narrow side, and the default height off its thickness.
    ///
    /// **"They are large tabs"**, on a board 21 mm across. A tenth of the narrow side makes a tab look
    /// like a tab at both ends of the range this application is used at, and the cap stops a big panel
    /// growing tabs a hand cannot break.
    ///
    /// The 30 x 50 mm case is here because it is the one that must *not* move: it gives 3.0 mm, which
    /// is what every board got before this rule existed, so an ordinary board's programs are
    /// unchanged and the goldens that cover them say so.
    /// </summary>
    [Theory]
    [InlineData(21, 37, 2.0)]
    [InlineData(30, 50, 3.0)]
    [InlineData(80, 100, 5.0)]
    public void TheDefaultWidthComesOffTheBoardsNarrowSide(double widthMm, double heightMm, double expectedMm)
    {
        var width = TabPlan.DefaultWidthFor(Rect(widthMm, heightMm));

        output.WriteLine(
            $"{widthMm} x {heightMm} mm gives a {Nm.ToMillimetreString(width, 1)} mm tab");

        Assert.Equal(Mm(expectedMm), width);
    }

    /// <summary>
    /// An edit changes the tab it names and nothing else.
    ///
    /// **Including where the others are**, which is the part worth pinning: width decides how many
    /// tabs an edge can hold, so an edit applied *before* placement would let one widened tab move
    /// every other tab on the board. An operator who widens the tab on the connector edge has not
    /// asked for the other three to shift.
    /// </summary>
    [Fact]
    public void AnEditChangesTheTabItNamesAndNothingElse()
    {
        var plain = TabPlan.For(Rect(21, 37), Options());

        var edited = TabPlan.For(
            Rect(21, 37),
            Options() with
            {
                TabEdits = [new TabEdit { Index = 1, WidthNm = Mm(4.5), HeightNm = Mm(0.3) }],
            });

        for (var i = 0; i < plain.Count; i++)
        {
            output.WriteLine(
                $"{i}: {Nm.ToMillimetreString(plain[i].WidthNm, 1)} mm -> "
                + $"{Nm.ToMillimetreString(edited[i].WidthNm, 1)} mm at {edited[i].At}");
        }

        // Both plans are real: comparing two empty lists element by element proves nothing, and every
        // loop below would be vacuous.
        Assert.Equal(4, plain.Count);
        Assert.Equal(plain.Count, edited.Count);
        Assert.Equal(Mm(4.5), edited[1].WidthNm);
        Assert.Equal(Mm(0.3), edited[1].HeightNm);

        // Every position, and every other size, exactly as it was.
        for (var i = 0; i < plain.Count; i++)
        {
            Assert.Equal(plain[i].At, edited[i].At);

            if (i != 1)
            {
                Assert.Equal(plain[i].WidthNm, edited[i].WidthNm);
                Assert.Equal(plain[i].HeightNm, edited[i].HeightNm);
            }
        }
    }

    /// <summary>
    /// The gaps in the emitted program are where the plan said the tabs were.
    ///
    /// **This is the clause the story is judged on** — *the emitted gaps land where the picture says
    /// they will* — and it is checked against the toolpath rather than against the planner, because
    /// the planner and the splitter walk the profile independently. A tab is a gap between two runs,
    /// so every gap's midpoint is compared with a planned tab's centre, and every tab has to be
    /// accounted for.
    ///
    /// The tolerance is a tenth of a millimetre: the gap is measured across the chord between two run
    /// ends, which on a straight edge is the tab's own width and cuts slightly inside the path
    /// anywhere else.
    /// </summary>
    [Fact]
    public void TheGapsInTheProgramAreWhereThePlanPutTheTabs()
    {
        var profile = Rect(21, 37);
        var options = Options();

        // The profile the cutter actually follows, which is what both halves are computed from: the
        // planner is given the offset contour inside Build, so the comparison has to be too.
        var cut = Polygons.Inflate(
            [profile],
            options.Tool.DiameterNm / 2,
            JoinType.Round,
            EndType.Polygon,
            arcTolerance: options.SagittaNm);

        var planned = TabPlan.For(cut[0], options);
        var toolpath = OutlineOperation.Build([profile], options);

        var runs = toolpath.Passes
            .Where(p => !p.Closed)
            .GroupBy(p => p.DepthNm)
            .OrderBy(g => g.Key)
            .First()
            .ToList();

        // Four, said as a number rather than as "the same as each other": two empty lists agree
        // perfectly and say nothing.
        Assert.Equal(4, planned.Count);
        Assert.Equal(planned.Count, runs.Count);

        // Each gap is between the end of one run and the start of the next, the last of them wrapping
        // round to the first.
        var gaps = new List<Point2>();

        for (var r = 0; r < runs.Count; r++)
        {
            var from = runs[r].Path[^1].To;
            var to = runs[(r + 1) % runs.Count].Path[0].From;

            gaps.Add(new Point2((from.X + to.X) / 2, (from.Y + to.Y) / 2));

            output.WriteLine(
                $"gap {r}: {Nm.ToMillimetreString((long)from.DistanceTo(to), 3)} mm wide, "
                + $"centred at {gaps[^1]}");
        }

        foreach (var tab in planned)
        {
            Assert.Contains(gaps, g => g.DistanceTo(tab.At) < Mm(0.1));
        }

        // And each gap is the width of the tab that made it, which is what the emitter is told to
        // expect: a crossing wider than its tab is a reordered move and must not be taken low.
        for (var r = 0; r < runs.Count; r++)
        {
            var across = runs[r].Path[^1].To.DistanceTo(runs[(r + 1) % runs.Count].Path[0].From);

            var limit = runs[r].HopWithinNm ?? 0;

            // **Both bounds.** No wider than the tab, or a reordered move could be taken at hop
            // height; and no narrower, because a gap of nothing is a tab that was not left. Every tab
            // on this board is centred on a straight edge, so the chord across it is the tab's own
            // width to within a rounding.
            Assert.True(
                across <= limit + Mm(0.01),
                $"a gap of {Nm.ToMillimetreString((long)across, 3)} mm against a hop limit of "
                + $"{Nm.ToMillimetreString(limit, 3)} mm");

            Assert.True(
                across >= limit - Mm(0.01),
                $"a gap of only {Nm.ToMillimetreString((long)across, 3)} mm where the tab is "
                + $"{Nm.ToMillimetreString(limit, 3)} mm wide");
        }
    }

    /// <summary>
    /// A tab with its own height gets its own pass, and the others are not cut down to it.
    ///
    /// **The depth ladder used to have one rung for all the tabs**, because all the tabs were one
    /// height. Now each distinct height is a depth the cutter has to visit: the shallowest tab's top
    /// is cut while every other tab is still whole, and the deepest tab's top last. Getting this
    /// wrong in the direction that matters would cut every tab down to the thinnest of them, which
    /// reads on the bench as a board that falls out of the stock.
    /// </summary>
    [Fact]
    public void ATabWithItsOwnHeightIsCutToItsOwnDepth()
    {
        var options = Options() with
        {
            TabEdits = [new TabEdit { Index = 0, HeightNm = Mm(0.3) }],
        };

        var toolpath = OutlineOperation.Build([Rect(21, 37)], options);

        // 0.8 mm board and 0.3 mm of break-through is 1.1 mm of cut. The derived tabs leave 0.25 mm,
        // so their tops are at 0.55 mm; the edited one leaves 0.30 mm, so its top is at 0.50 mm.
        var byDepth = toolpath.Passes
            .GroupBy(p => p.DepthNm)
            .OrderBy(g => g.Key)
            .Select(g => (Depth: g.Key, Runs: g.Count(p => !p.Closed), Laps: g.Count(p => p.Closed)))
            .ToList();

        foreach (var (depth, openRuns, laps) in byDepth)
        {
            output.WriteLine($"{Nm.ToMillimetreString(depth, 2)} mm: {laps} lap(s), {openRuns} run(s)");
        }

        // Both tops are cut, each with the tabs above it still whole.
        Assert.Contains(byDepth, d => d.Depth == Mm(0.5) && d.Laps == 1);
        Assert.Contains(byDepth, d => d.Depth == Mm(0.55) && d.Runs == 1);

        // At 0.55 mm one tab is being jumped, so the profile is a single open run; below that all four
        // are, so there are four.
        Assert.Contains(byDepth, d => d.Depth > Mm(0.55) && d.Runs == 4);
    }

    /// <summary>
    /// Tabs that are not all the same size say so in the program, as a range.
    ///
    /// **The program's own header is where an operator reads what the tabs are**, and it used to say
    /// one width and one height because there was only ever one of each. A single figure over a board
    /// whose tabs differ would be a guess at which of them it meant, and the one that matters is
    /// whichever is about to be snapped by hand.
    /// </summary>
    [Fact]
    public void TabsOfDifferentSizesAreReportedAsARange()
    {
        var options = Options() with
        {
            TabEdits =
            [
                new TabEdit { Index = 0, WidthNm = Mm(4.0), HeightNm = Mm(0.4) },
            ],
        };

        var toolpath = OutlineOperation.Build([Rect(21, 37)], options);

        output.WriteLine(string.Join(Environment.NewLine, toolpath.Notes));

        // The derived pair on this board is 2.0 mm wide leaving 0.25 mm, and one tab has been set to
        // 4.0 mm leaving 0.40 mm.
        Assert.Contains(
            toolpath.Notes,
            n => n.Contains("2.0 to 4.0 mm wide", StringComparison.Ordinal)
                && n.Contains("0.25 to 0.40 mm of material left", StringComparison.Ordinal));
    }

    /// <summary>
    /// And on a real board's outline, every tab is on an edge of it.
    ///
    /// The fixtures above are rectangles with sharp corners; a board's outline reaches the tab planner
    /// after being offset by the cutter's radius, which rounds every corner into a fan of short
    /// segments. Those are the segments the old bug hid in, and they are the ones a tab must not be
    /// placed on — not by a rule about corners, but because a tab needs an edge long enough to sit on
    /// with clearance either side.
    /// </summary>
    [Fact]
    public void OnARealBoardEveryTabSitsOnAStraightEdge()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));
        var outline = board.Layers.Single(l => l.Role == LayerRole.Outline);

        var options = Options() with { BoardThicknessNm = Mm(0.8) };

        var cut = Polygons.Inflate(
            outline.Area,
            options.Tool.DiameterNm / 2,
            JoinType.Round,
            EndType.Polygon,
            arcTolerance: options.SagittaNm);

        var tabs = TabPlan.For(cut[0], options);

        foreach (var tab in tabs)
        {
            output.WriteLine(
                $"edge {tab.Edge}, {Nm.ToMillimetreString((long)tab.EdgeLengthNm, 1)} mm long, "
                + $"tab at {tab.At}");
        }

        Assert.NotEmpty(tabs);
        Assert.All(tabs, t => Assert.False(t.Spaced, "a tab on this board was spaced, not placed"));

        // **The profile really is straight where each tab sits**, checked against the contour rather
        // than against the planner's own report of it.
        //
        // A `/describe-test` pass is why: the assertion here used to be
        // `EdgeLengthNm >= 3 * WidthNm`, which is the planner's own placement rule restated using the
        // planner's own numbers. It could only have failed if the planner contradicted itself, and it
        // looked at no coordinates at all — on a test named for tabs sitting on straight edges.
        //
        // What is checked now is that the contour from one end of the tab to the other is a straight
        // line: the middle of it is on the line between its ends, to within a micron. A tab across a
        // corner fails that by a millimetre or more, which is the fault the placement exists to
        // prevent.
        var contour = cut[0];

        foreach (var tab in tabs)
        {
            var (a, b) = EitherSide(contour, tab);
            var off = a.DistanceTo(tab.At) + tab.At.DistanceTo(b) - a.DistanceTo(b);

            output.WriteLine(
                $"tab at {tab.At}: ends {a} and {b}, bowing {Nm.ToMillimetreString((long)off, 4)} mm");

            Assert.True(
                off < Mm(0.001),
                $"the profile bends by {Nm.ToMillimetreString((long)off, 3)} mm under a tab at {tab.At}");
        }
    }

    /// <summary>
    /// The two points on the profile half a tab either side of its centre.
    ///
    /// Walked from the contour itself, in the same arc-length terms the planner used, so the caller
    /// can ask whether the copper between them is straight without trusting anything the planner
    /// said about it.
    /// </summary>
    private static (Point2 From, Point2 To) EitherSide(Path64 contour, PlacedTab tab)
    {
        var segments = IsolationOperation.ToSegments(contour);
        var perimeter = segments.Sum(s => s.From.DistanceTo(s.To));

        Point2 At(double along)
        {
            var travelled = 0.0;

            foreach (var segment in segments)
            {
                var length = segment.From.DistanceTo(segment.To);

                if (length > 0 && along <= travelled + length)
                {
                    var t = (along - travelled) / length;

                    return new Point2(
                        segment.From.X + (long)Math.Round((segment.To.X - segment.From.X) * t),
                        segment.From.Y + (long)Math.Round((segment.To.Y - segment.From.Y) * t));
                }

                travelled += length;
            }

            return segments[^1].To;
        }

        var half = tab.WidthNm / 2.0;

        return (
            At((tab.AlongNm - half + perimeter) % perimeter),
            At((tab.AlongNm + half) % perimeter));
    }
}
