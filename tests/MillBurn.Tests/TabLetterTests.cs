using System.Collections.Immutable;
using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Geometry;

namespace MillBurn.Tests;

/// <summary>
/// Tabs named by letter, and one tab's own width — sprint 6 story 5.
///
/// **From the bench, 2026-10-18:** a label near each handle, *"A - Z (then AA - ZZ, ...)"*, the
/// same letters in the tabs window instead of its numbered list, and a click on a handle that sets
/// that tab's width alone. What these tests pin is that a planned tab's letter is its own name — it
/// stays on the tab through moves and removals around it — and that an added tab, which nothing
/// could size before, now carries its own sizes on its add entry, written by the window's rule.
/// </summary>
public sealed class TabLetterTests
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

    /// <summary>A spreadsheet's column names: after Z comes AA, after AZ comes BA, after ZZ comes AAA.</summary>
    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void LettersRunLikeSpreadsheetColumns(int place, string letter)
    {
        Assert.Equal(letter, TabPlan.LetterOf(place));
    }

    [Fact]
    public void ANegativePlaceHasNoLetter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TabPlan.LetterOf(-1));
    }

    /// <summary>The planned tabs are lettered in the pristine plan's order: ordinal 0 is A.</summary>
    [Fact]
    public void PlannedTabsAreLetteredInPlanOrder()
    {
        var tabs = TabPlan.For(Rect(21, 37), Options());

        Assert.Equal(["A", "B", "C", "D"], tabs.Select(t => t.Letter));
        Assert.Equal([0, 1, 2, 3], tabs.Select(t => t.Ordinal));
    }

    /// <summary>
    /// A letter stays on its tab: dragging B past its neighbours leaves it B, and removing A
    /// re-letters nobody — so "tab C" in the window is the tab under the C in the viewport,
    /// whatever the hand has done.
    /// </summary>
    [Fact]
    public void ALetterStaysWithItsTabThroughMovesAndRemovals()
    {
        var rect = Rect(21, 37);
        var plain = TabPlan.For(rect, Options());

        var tabs = TabPlan.For(rect, Options() with
        {
            TabMoves =
            [
                new TabMove { Index = 0 },
                new TabMove { Index = 1, AlongNm = plain[3].AlongNm + Mm(4) },
            ],
        });

        Assert.Equal(["B", "C", "D"], tabs.Select(t => t.Letter));

        var b = Assert.Single(tabs, t => t.Letter == "B");
        Assert.True(b.Moved);
        Assert.Equal(1, b.Ordinal);

        // And it is where the hand put it — 4 mm past D, wrapped round the 116 mm loop.
        Assert.Equal((plain[3].AlongNm + Mm(4)) % Mm(116), b.AlongNm, Mm(0.001));
    }

    /// <summary>
    /// Added tabs are lettered after the planned ones, in the order they were added.
    /// </summary>
    [Fact]
    public void AddedTabsAreLetteredAfterThePlannedOnes()
    {
        var tabs = TabPlan.For(Rect(21, 37), Options() with
        {
            TabMoves =
            [
                new TabMove { Index = -1, AlongNm = Mm(3) },
                new TabMove { Index = -1, AlongNm = Mm(17) },
            ],
        });

        Assert.Equal(["A", "B", "C", "D", "E", "F"], tabs.Select(t => t.Letter));
        Assert.Equal(0, tabs[5].At.DistanceTo(new Point2(Mm(17), 0)), Mm(0.001));
    }

    /// <summary>
    /// An added tab can be sized, by sizes carried on its own add entry — and the planned tab with
    /// the same number is left alone. Before this story an added tab could not be sized at all.
    /// </summary>
    [Fact]
    public void AnAddedTabCarriesItsOwnSizes()
    {
        var tabs = TabPlan.For(Rect(21, 37), Options() with
        {
            TabMoves = [new TabMove { Index = -1, AlongNm = Mm(3), WidthNm = Mm(4), HeightNm = Mm(0.4) }],
        });

        var added = Assert.Single(tabs, t => t.Added);
        Assert.Equal(Mm(4), added.WidthNm);
        Assert.Equal(Mm(0.4), added.HeightNm);

        // Planned tab 0 — the same number, the other space — keeps the board's 2.0 / 0.25.
        var a = Assert.Single(tabs, t => !t.Added && t.Ordinal == 0);
        Assert.Equal(Mm(2.0), a.WidthNm);
        Assert.Equal(Mm(0.25), a.HeightNm);
    }

    /// <summary>
    /// A non-positive size on an add entry is no size, as it is in an edit: a hand-edited file
    /// holding a zero must not cut the profile clean through at that tab.
    /// </summary>
    [Fact]
    public void AnAddedTabsNonPositiveSizeIsIgnored()
    {
        var tabs = TabPlan.For(Rect(21, 37), Options() with
        {
            TabMoves = [new TabMove { Index = -1, AlongNm = Mm(3), WidthNm = 0, HeightNm = -5 }],
        });

        var added = Assert.Single(tabs, t => t.Added);
        Assert.Equal(Mm(2.0), added.WidthNm);
        Assert.Equal(Mm(0.25), added.HeightNm);
    }

    /// <summary>
    /// A planned tab's edit does not reach the first added tab, though both are number 0.
    /// </summary>
    [Fact]
    public void APlannedTabsEditDoesNotReachAnAddedOne()
    {
        var tabs = TabPlan.For(Rect(21, 37), Options() with
        {
            TabMoves = [new TabMove { Index = -1, AlongNm = Mm(3) }],
            TabEdits = [new TabEdit { Index = 0, WidthNm = Mm(4) }],
        });

        Assert.Equal(Mm(4), Assert.Single(tabs, t => !t.Added && t.Ordinal == 0).WidthNm);
        Assert.Equal(Mm(2.0), Assert.Single(tabs, t => t.Added).WidthNm);
    }

    /// <summary>
    /// What is left after the first of two sized adds is removed — the view model takes the
    /// removed add's entry out, size and all — plans as the survivor alone: its own 3 mm, and the
    /// letter E, moved up from F as its place among the adds did. Stored apart and keyed by place,
    /// as the first build stored it, the removed add's 4 mm would have stayed behind and landed on
    /// this tab; an entry cannot hold a size for any tab but its own.
    /// </summary>
    [Fact]
    public void TheSurvivingAddKeepsItsOwnSizeAndTakesTheFreedLetter()
    {
        var tabs = TabPlan.For(Rect(21, 37), Options() with
        {
            // What is left after the first of two sized adds — 4 mm at 3, 3 mm at 17 — is removed.
            TabMoves = [new TabMove { Index = -1, AlongNm = Mm(17), WidthNm = Mm(3) }],
        });

        var survivor = Assert.Single(tabs, t => t.Added);
        Assert.Equal("E", survivor.Letter);
        Assert.Equal(Mm(3), survivor.WidthNm);
    }

    /// <summary>
    /// The gap in the emitted program is the width the add entry gave its tab: the splitter reads
    /// the planned list, so a size applied to the picture and not the cut would show a 4 mm tab and
    /// cut a 2 mm one. Measured off the toolpath, as the gap between the cut runs either side.
    /// </summary>
    [Fact]
    public void TheProgramCutsAnAddedTabAtItsOwnWidth()
    {
        var profile = Rect(21, 37);

        var cut = Polygons.Inflate(
            [profile],
            Options().Tool.DiameterNm / 2,
            JoinType.Round,
            EndType.Polygon,
            arcTolerance: Options().SagittaNm);

        // In planned tab A's place, with A removed: the centre of a straight edge, so the gap's
        // chord is its length along the path.
        var plain = TabPlan.For(cut[0], Options());

        var options = Options() with
        {
            TabMoves =
            [
                new TabMove { Index = 0 },
                new TabMove { Index = -1, AlongNm = plain[0].AlongNm, WidthNm = Mm(4) },
            ],
        };

        var planned = TabPlan.For(cut[0], options);
        var added = Assert.Single(planned, t => t.Added);

        // A is gone — or its own 2 mm gap would sit inside the 4 mm one and hide in it.
        Assert.DoesNotContain(planned, t => !t.Added && t.Ordinal == 0);
        Assert.Equal(4, planned.Count);

        var toolpath = OutlineOperation.Build([profile], options);

        var runs = toolpath.Passes
            .Where(p => !p.Closed)
            .GroupBy(p => p.DepthNm)
            .OrderBy(g => g.Key)
            .First()
            .ToList();

        var gaps = Enumerable.Range(0, runs.Count)
            .Select(r => (From: runs[r].Path[^1].To, To: runs[(r + 1) % runs.Count].Path[0].From))
            .ToList();

        var gap = gaps.MinBy(g =>
            new Point2((g.From.X + g.To.X) / 2, (g.From.Y + g.To.Y) / 2).DistanceTo(added.At));

        Assert.Equal(4.0, Nm.ToMillimetres((long)gap.From.DistanceTo(gap.To)), 2);
    }

    /// <summary>
    /// What a click on a planned tab's handle writes: an edit for that tab alone, its own height
    /// kept, the positions untouched — and the board's width, or none, is not an edit at all: the
    /// rule the tabs window applies, so the two doors write the same project file for the same
    /// choice.
    /// </summary>
    [Fact]
    public void WithWidthOnAPlannedTabWritesItsEditByTheWindowsRule()
    {
        var b = new PlacedTab
        {
            AlongNm = 0, WidthNm = Mm(2), HeightNm = Mm(0.25), At = default, Edge = 1, EdgeLengthNm = 0,
            Ordinal = 1, Letter = "B",
        };

        ImmutableArray<TabEdit> edits =
        [
            new TabEdit { Index = 0, WidthNm = Mm(3) },
            new TabEdit { Index = 1, HeightNm = Mm(0.4) },
        ];

        ImmutableArray<TabMove> moves = [new TabMove { Index = -1, AlongNm = Mm(5), WidthNm = Mm(5) }];

        var (widened, movesAfter) = TabPlan.WithWidth(edits, moves, b, Mm(4), boardWidthNm: Mm(2));

        Assert.Equal(2, widened.Length);
        Assert.Contains(new TabEdit { Index = 1, WidthNm = Mm(4), HeightNm = Mm(0.4) }, widened);
        Assert.Contains(new TabEdit { Index = 0, WidthNm = Mm(3) }, widened);
        Assert.Equal<TabMove>(moves, movesAfter);

        // Back to the board's width: the width goes, the height it had stays.
        var (reset, _) = TabPlan.WithWidth(widened, moves, b, Mm(2), boardWidthNm: Mm(2));
        Assert.Equal(2, reset.Length);
        Assert.Contains(new TabEdit { Index = 1, HeightNm = Mm(0.4) }, reset);
        Assert.Contains(new TabEdit { Index = 0, WidthNm = Mm(3) }, reset);

        // With no height either, nothing is left to say about the tab: no edit is made, and an
        // edit that held only a width goes.
        var (bare, _) = TabPlan.WithWidth([], [], b, null, boardWidthNm: Mm(2));
        Assert.Empty(bare);

        var (gone, _) = TabPlan.WithWidth([new TabEdit { Index = 1, WidthNm = Mm(4) }], [], b, Mm(2), boardWidthNm: Mm(2));
        Assert.Empty(gone);
    }

    /// <summary>
    /// And on an added tab's handle: the width goes onto that add's own entry — the second add
    /// here, not the first — its height kept, and the edits untouched.
    /// </summary>
    [Fact]
    public void WithWidthOnAnAddedTabWritesItsEntry()
    {
        var f = new PlacedTab
        {
            AlongNm = 0, WidthNm = Mm(2), HeightNm = Mm(0.3), At = default, Edge = -1, EdgeLengthNm = 0,
            Ordinal = 1, Letter = "F", Added = true,
        };

        ImmutableArray<TabEdit> edits = [new TabEdit { Index = 1, WidthNm = Mm(3) }];

        // An add entry with no position adds nothing — the planner does not count it — so it
        // must not be counted here either, or the width lands one add early.
        ImmutableArray<TabMove> moves =
        [
            new TabMove { Index = 2 },
            new TabMove { Index = -1 },
            new TabMove { Index = -1, AlongNm = Mm(3) },
            new TabMove { Index = -1, AlongNm = Mm(17), HeightNm = Mm(0.3) },
        ];

        var (editsAfter, widened) = TabPlan.WithWidth(edits, moves, f, Mm(4), boardWidthNm: Mm(2));

        Assert.Equal<TabEdit>(edits, editsAfter);
        Assert.Equal<TabMove>(
            [
                new TabMove { Index = 2 },
                new TabMove { Index = -1 },
                new TabMove { Index = -1, AlongNm = Mm(3) },
                new TabMove { Index = -1, AlongNm = Mm(17), WidthNm = Mm(4), HeightNm = Mm(0.3) },
            ],
            widened);

        var (_, reset) = TabPlan.WithWidth(edits, widened, f, Mm(2), boardWidthNm: Mm(2));
        Assert.Equal<TabMove>(moves, reset);
    }

    /// <summary>
    /// The two halves of a click agreeing end to end: what <see cref="TabPlan.WithWidth"/> writes
    /// for an added tab is what the planner then cuts that tab at, and no other tab moves.
    /// </summary>
    [Fact]
    public void AClickedWidthOnAnAddedTabReachesThePlan()
    {
        var rect = Rect(21, 37);
        var options = Options() with { TabMoves = [new TabMove { Index = -1, AlongNm = Mm(3) }] };
        var before = TabPlan.For(rect, options);
        var added = Assert.Single(before, t => t.Added);

        var (edits, moves) = TabPlan.WithWidth([], options.TabMoves, added, Mm(3.5), boardWidthNm: added.WidthNm);
        var tabs = TabPlan.For(rect, options with { TabEdits = edits, TabMoves = moves });

        Assert.Equal(Mm(3.5), Assert.Single(tabs, t => t.Added).WidthNm);
        Assert.All(tabs.Where(t => !t.Added), t => Assert.Equal(Mm(2.0), t.WidthNm));

        // And nothing moved: every tab is where it was before the click, the clicked one included.
        Assert.Equal(before.Select(t => t.At), tabs.Select(t => t.At));
    }
}
