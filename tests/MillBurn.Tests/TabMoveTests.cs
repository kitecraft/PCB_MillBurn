using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Geometry;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Tabs a hand moved, removed or added (roadmap 6.4, sprint 6 story 2).
///
/// However good the placement rule gets, the next odd outline puts one tab over a slot or against
/// a connector, and the recourse must not be fighting the planner. A <see cref="TabMove"/> is the
/// stored form of a drag in the viewport: it names a planned tab the way a size edit does — by its
/// place in the pristine plan — and says where the hand put it, as a distance along the cut
/// profile. What these tests pin is the contract that makes the gesture safe: a move changes the
/// tab it names and nothing else, the ordinal space survives any amount of moving and removing,
/// and the program's own header says what a hand chose.
/// </summary>
public sealed class TabMoveTests(ITestOutputHelper output)
{
    private static long Mm(double mm) => Nm.FromMillimetres(mm);

    private static Path64 Rect(double widthMm, double heightMm) =>
    [
        new(0, 0),
        new(Mm(widthMm), 0),
        new(Mm(widthMm), Mm(heightMm)),
        new(0, Mm(heightMm)),
    ];

    private static OutlineOptions Options(int tabs = 4) => new()
    {
        TabCount = tabs,
        BoardThicknessNm = Mm(0.8),
        DepthPerPassNm = Mm(1.0),
        Keep = [],
    };

    /// <summary>
    /// A moved tab goes where the hand put it, and nothing else moves.
    ///
    /// The same contract the size edits were given, for the same reason: an operator who slides the
    /// tab off a connector has not asked for the other three to shift. The landing point is checked
    /// against the contour itself — 5 mm along the walk from (0,0) on this rectangle is (5, 0) — so
    /// the test would catch a move applied in the wrong coordinate as well as one not applied.
    /// </summary>
    [Fact]
    public void AMovedTabGoesWhereTheHandPutItAndNothingElseMoves()
    {
        var rect = Rect(21, 37);
        var plain = TabPlan.For(rect, Options());

        var moved = TabPlan.For(rect, Options() with
        {
            TabMoves = [new TabMove { Index = 1, AlongNm = Mm(5) }],
        });

        foreach (var tab in moved)
        {
            output.WriteLine($"ordinal {tab.Ordinal} at {tab.At}, moved: {tab.Moved}");
        }

        Assert.Equal(4, plain.Count);
        Assert.Equal(4, moved.Count);

        // The named tab is at 5 mm along the loop — on the bottom edge, 5 mm from the corner.
        Assert.True(moved[1].Moved);
        Assert.Equal(0, moved[1].At.DistanceTo(new Point2(Mm(5), 0)), Mm(0.001));
        Assert.Equal(Mm(5), moved[1].AlongNm, Mm(0.001));

        // Its sizes are untouched, and every other tab is exactly the plain plan's.
        Assert.Equal(plain[1].WidthNm, moved[1].WidthNm);
        Assert.Equal(plain[1].HeightNm, moved[1].HeightNm);

        for (var i = 0; i < plain.Count; i++)
        {
            Assert.Equal(i, moved[i].Ordinal);

            if (i != 1)
            {
                Assert.Equal(plain[i], moved[i]);
            }
        }
    }

    /// <summary>
    /// A move and a size edit name the same tab the same way, and both apply.
    ///
    /// The two kinds of override live side by side in the settings and are applied in sequence —
    /// sizes first, positions after — so a tab that was widened for a connector and then slid away
    /// from it keeps the width through the slide.
    /// </summary>
    [Fact]
    public void AMoveComposesWithASizeEditOnTheSameTab()
    {
        var tabs = TabPlan.For(Rect(21, 37), Options() with
        {
            TabEdits = [new TabEdit { Index = 1, WidthNm = Mm(4.5) }],
            TabMoves = [new TabMove { Index = 1, AlongNm = Mm(5) }],
        });

        Assert.Equal(4, tabs.Count);
        Assert.Equal(Mm(4.5), tabs[1].WidthNm);
        Assert.True(tabs[1].Moved);
        Assert.Equal(0, tabs[1].At.DistanceTo(new Point2(Mm(5), 0)), Mm(0.001));
    }

    /// <summary>
    /// A removed tab leaves the others where they were, under the names they had.
    ///
    /// The ordinal is assigned on the pristine plan and never re-derived from the list, so removing
    /// tab 1 does not slide every later override onto its neighbour — which is exactly the fault
    /// the size edits' review named: an override must never land on whichever tab then sorts into
    /// that slot.
    /// </summary>
    [Fact]
    public void ARemovedTabLeavesTheOthersUnderTheNamesTheyHad()
    {
        var rect = Rect(21, 37);
        var plain = TabPlan.For(rect, Options());

        var remaining = TabPlan.For(rect, Options() with
        {
            TabMoves = [new TabMove { Index = 1 }],
        });

        foreach (var tab in remaining)
        {
            output.WriteLine($"ordinal {tab.Ordinal} at {tab.At}");
        }

        Assert.Equal(4, plain.Count);
        Assert.Equal(3, remaining.Count);
        Assert.Equal([0, 2, 3], remaining.Select(t => t.Ordinal).ToList());

        foreach (var tab in remaining)
        {
            Assert.Equal(plain[tab.Ordinal], tab);
        }
    }

    /// <summary>
    /// An added tab takes the profile's own derived sizes and says a hand put it there.
    ///
    /// The derived pair, because nothing else has named a size for it: this board gives 2.0 mm
    /// wide leaving 0.25 mm, the same numbers every planned tab starts from.
    /// </summary>
    [Fact]
    public void AnAddedTabTakesTheProfilesOwnSizes()
    {
        var rect = Rect(21, 37);

        var tabs = TabPlan.For(rect, Options() with
        {
            TabMoves = [new TabMove { Index = -1, AlongNm = Mm(3) }],
        });

        Assert.Equal(5, tabs.Count);

        var added = Assert.Single(tabs, t => t.Added);

        Assert.Equal(0, added.Ordinal);
        Assert.False(added.Spaced);

        // Against the derived-size functions *and* against the literal numbers this board gives —
        // the functions alone would agree with themselves if the formula moved, and a
        // `/describe-test` pass called that out: 21 mm across is a 2.0 mm tab leaving 0.25 mm.
        Assert.Equal(TabPlan.DefaultWidthFor(rect), added.WidthNm);
        Assert.Equal(TabPlan.DefaultHeightFor(Mm(0.8)), added.HeightNm);
        Assert.Equal(Mm(2.0), added.WidthNm);
        Assert.Equal(Mm(0.25), added.HeightNm);
        Assert.Equal(0, added.At.DistanceTo(new Point2(Mm(3), 0)), Mm(0.001));
    }

    /// <summary>
    /// Any distance lands on the loop: the profile is closed, so a drag past the start vertex —
    /// forwards or backwards — wraps rather than falling off the end of an array nobody sees.
    /// </summary>
    [Theory]
    [InlineData(121.0)]
    [InlineData(-111.0)]
    public void AMoveWrapsRoundTheLoop(double alongMm)
    {
        // The rectangle's perimeter is 116 mm, so both distances mean "5 mm along the loop".
        var tabs = TabPlan.For(Rect(21, 37), Options() with
        {
            TabMoves = [new TabMove { Index = 1, AlongNm = Mm(alongMm) }],
        });

        output.WriteLine($"{alongMm} mm lands at {tabs[1].At}");

        Assert.Equal(0, tabs[1].At.DistanceTo(new Point2(Mm(5), 0)), Mm(0.001));
        Assert.Equal(Mm(5), tabs[1].AlongNm, Mm(0.001));
    }

    /// <summary>
    /// An entry that names no planned tab and adds nothing is ignored.
    ///
    /// The same rule the size edits follow, for the same route: a project file is hand-editable,
    /// and geometry that cuts a real board does not take its word for an index that does not exist
    /// or an addition with nowhere to go.
    /// </summary>
    [Fact]
    public void AMoveNamingNoTabIsIgnored()
    {
        var rect = Rect(21, 37);
        var plain = TabPlan.For(rect, Options());

        var unmoved = TabPlan.For(rect, Options() with
        {
            TabMoves =
            [
                new TabMove { Index = 99, AlongNm = Mm(5) },
                new TabMove { Index = -1 },
            ],
        });

        Assert.Equal(plain, unmoved);
    }

    /// <summary>
    /// A removal outranks a move of the same tab, whatever order the entries come in.
    ///
    /// The two can only coexist by way of a stale or hand-edited file; "the tab is gone" is the
    /// stronger claim and the safer one, because a removed tab that quietly came back at a moved
    /// position is a gap in the cut the operator did not plan for.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARemovalOutranksAMoveOfTheSameTab(bool removalFirst)
    {
        TabMove remove = new() { Index = 1 };
        TabMove move = new() { Index = 1, AlongNm = Mm(5) };

        var tabs = TabPlan.For(Rect(21, 37), Options() with
        {
            TabMoves = removalFirst ? [remove, move] : [move, remove],
        });

        Assert.Equal(3, tabs.Count);
        Assert.DoesNotContain(tabs, t => t.Ordinal == 1 && !t.Added);
    }

    /// <summary>
    /// The gaps in the emitted program follow a hand-moved tab.
    ///
    /// The planner and the splitter walk the profile independently, so a position override applied
    /// in one and not the other would draw the tab in the moved place and cut it in the old one —
    /// the exact disagreement the viewport drag must never produce. Checked the way the original
    /// gaps test is: every planned centre has a gap at it, measured off the toolpath.
    /// </summary>
    [Fact]
    public void TheGapsInTheProgramFollowAHandMovedTab()
    {
        var profile = Rect(21, 37);

        var cut = Polygons.Inflate(
            [profile],
            Options().Tool.DiameterNm / 2,
            JoinType.Round,
            EndType.Polygon,
            arcTolerance: Options().SagittaNm);

        // Halfway between the first two planned tabs — a place that is certainly an empty stretch
        // of the loop, so the move tests the slide rather than the overlap-merging the splitter
        // already owns.
        var plain = TabPlan.For(cut[0], Options());
        var target = (plain[0].AlongNm + plain[1].AlongNm) / 2;

        var options = Options() with
        {
            TabMoves = [new TabMove { Index = 0, AlongNm = target }],
        };

        var planned = TabPlan.For(cut[0], options);
        var toolpath = OutlineOperation.Build([profile], options);

        var runs = toolpath.Passes
            .Where(p => !p.Closed)
            .GroupBy(p => p.DepthNm)
            .OrderBy(g => g.Key)
            .First()
            .ToList();

        Assert.Equal(4, planned.Count);
        Assert.Single(planned, t => t.Moved);
        Assert.Equal(planned.Count, runs.Count);

        var gaps = new List<Point2>();

        for (var r = 0; r < runs.Count; r++)
        {
            var from = runs[r].Path[^1].To;
            var to = runs[(r + 1) % runs.Count].Path[0].From;

            gaps.Add(new Point2((from.X + to.X) / 2, (from.Y + to.Y) / 2));

            output.WriteLine($"gap centred at {gaps[^1]}");
        }

        foreach (var tab in planned)
        {
            Assert.Contains(gaps, g => g.DistanceTo(tab.At) < Mm(0.1));
        }
    }

    /// <summary>
    /// Removing every tab on one profile does not remove the next profile's by the same numbers.
    ///
    /// **Found by `/code-review`, and it is the size edits' cross-profile leak reborn.** The
    /// overrides spend on the first profile that places tabs — but hand removals can empty that
    /// profile's list, and a latch that watches the *result* then hands the same removals to the
    /// next board in the same Edge_Cuts, which loses its tabs by the same ordinals. Both pieces
    /// come free on the last pass. The window refuses to remove the last tab, so this state is a
    /// hand-edited project file — the route `TabMove`'s own contract hardens against.
    /// </summary>
    [Fact]
    public void RemovingEveryTabOnOneProfileLeavesTheNextProfilesAlone()
    {
        Paths64 two = [Rect(40, 30), Shifted(Rect(44, 34), 60, 0)];

        var options = Options() with
        {
            TabMoves =
            [
                new TabMove { Index = 0 },
                new TabMove { Index = 1 },
                new TabMove { Index = 2 },
                new TabMove { Index = 3 },
            ],
        };

        var toolpath = OutlineOperation.Build(two, options);

        // The second profile's four tabs are the only ones left, so the shallowest tabbed depth
        // holds exactly four open runs — and every gap between them lies on the shifted profile,
        // which is what says the removals did not reach it.
        var runs = toolpath.Passes
            .Where(p => !p.Closed)
            .GroupBy(p => p.DepthNm)
            .OrderBy(g => g.Key)
            .FirstOrDefault()
            ?.ToList() ?? [];

        output.WriteLine($"{runs.Count} open run(s) at the shallowest tabbed depth");

        Assert.Equal(4, runs.Count);

        foreach (var run in runs)
        {
            var gapFrom = run.Path[^1].To;

            output.WriteLine($"gap after a run ends at {gapFrom}");
            Assert.True(
                gapFrom.X >= Mm(55),
                $"a tab gap at {gapFrom} is on the first profile, whose tabs were all removed");
        }
    }

    /// <summary>A path moved, for building a job out of two separate profiles.</summary>
    private static Path64 Shifted(Path64 path, double xMm, double yMm) =>
        [.. path.Select(p => new Point64(p.X + Mm(xMm), p.Y + Mm(yMm)))];

    /// <summary>
    /// The program's header says what a hand chose.
    ///
    /// The placement line answers "what chose these positions", and once a hand is one of the
    /// answers it has to be named: an operator about to snap a tab out deserves to know which ones
    /// nobody's rule vouches for. A removal changes the count's explanation too — blaming edge
    /// clearance for a tab somebody deleted would be the header lying.
    /// </summary>
    [Fact]
    public void TheHeaderSaysWhatAHandChose()
    {
        var withMove = OutlineOperation.Build([Rect(21, 37)], Options() with
        {
            TabMoves = [new TabMove { Index = 0, AlongNm = Mm(50) }],
        });

        output.WriteLine(string.Join(Environment.NewLine, withMove.Notes));

        Assert.Contains(
            withMove.Notes,
            n => n.Contains("1 placed by hand", StringComparison.Ordinal)
                && n.Contains("3 centred on", StringComparison.Ordinal));

        var withRemoval = OutlineOperation.Build([Rect(21, 37)], Options() with
        {
            TabMoves = [new TabMove { Index = 0 }],
        });

        output.WriteLine(string.Join(Environment.NewLine, withRemoval.Notes));

        Assert.Contains(
            withRemoval.Notes,
            n => n.Contains("4 were asked for: 3 after the hand-set changes", StringComparison.Ordinal));
    }
}
