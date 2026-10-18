using System.Text.Json;
using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// What sprint 6's `xhigh` review found could cut a tab nobody chose — and the debt story that
/// closed it (sprint 6 story 6).
///
/// Four ways for the picture, the header and the cut to part company, each a test here by name:
/// hand-set changes outliving the plan they numbered; tabs dropped over each other and cut as
/// something neither the header nor the picture described; the program and the window deciding
/// apart which profile the changes belong to; and the header blaming a hand for the edges' own
/// shortfall. And two smaller ones: added tabs re-lettered by an earlier removal, and the walk
/// along the loop sped up without moving a single point.
/// </summary>
public sealed class TabSafetyTests(ITestOutputHelper output)
{
    private static long Mm(double mm) => Nm.FromMillimetres(mm);

    private static Path64 Rect(double widthMm, double heightMm, double xMm = 0, double yMm = 0) =>
    [
        new(Mm(xMm), Mm(yMm)),
        new(Mm(xMm + widthMm), Mm(yMm)),
        new(Mm(xMm + widthMm), Mm(yMm + heightMm)),
        new(Mm(xMm), Mm(yMm + heightMm)),
    ];

    private static OutlineOptions Options(int tabs = 4) => new()
    {
        TabCount = tabs,
        BoardThicknessNm = Mm(0.8),
        DepthPerPassNm = Mm(1.0),
        Keep = [],
    };

    /// <summary>The fingerprint of the plan an outline's hand-set changes would be made against.</summary>
    private static string KeyFor(Paths64 outline, OutlineOptions options) =>
        OutlineOperation.PlanTabs(outline, options with { TabEdits = [], TabMoves = [] }).PristineKey!;

    /// <summary>The gaps the first-depth runs of a tabbed toolpath leave, as (from, to) pairs.</summary>
    private static List<(Point2 From, Point2 To)> GapsCut(Toolpath toolpath)
    {
        var runs = toolpath.Passes
            .Where(p => !p.Closed)
            .GroupBy(p => p.DepthNm)
            .OrderBy(g => g.Key)
            .First()
            .ToList();

        return [.. Enumerable.Range(0, runs.Count)
            .Select(r => (runs[r].Path[^1].To, runs[(r + 1) % runs.Count].Path[0].From))];
    }

    // ------------------------------------------------------------------ the plan's fingerprint

    /// <summary>
    /// The same plan has the same fingerprint, a different width or count a different one, and a
    /// tab's height — set directly, or through the board thickness it is derived from — none at
    /// all: it moves no tab, so a thickness change never throws away a hand's work.
    /// </summary>
    [Fact]
    public void AFingerprintFollowsWhereTheTabsAreAndNothingElse()
    {
        var rect = Rect(21, 37);
        var key = TabPlan.KeyOf(TabPlan.For(rect, Options()));

        Assert.Equal(key, TabPlan.KeyOf(TabPlan.For(rect, Options())));
        Assert.Equal(key, TabPlan.KeyOf(TabPlan.For(rect, Options() with { BoardThicknessNm = Mm(1.6) })));
        Assert.Equal(key, TabPlan.KeyOf(TabPlan.For(rect, Options() with { TabHeightNm = Mm(0.45) })));
        Assert.NotEqual(key, TabPlan.KeyOf(TabPlan.For(rect, Options() with { TabWidthNm = Mm(3) })));
        Assert.NotEqual(key, TabPlan.KeyOf(TabPlan.For(rect, Options(tabs: 6))));
        Assert.Equal(12, key.Length);
    }

    /// <summary>
    /// **A change with no fingerprint is not applied** — a project from before the fingerprint,
    /// whose tab numbers belong to a plan this build may no longer make. The program cuts the
    /// planner's own tabs and says why in its header, and the window is told the same.
    /// </summary>
    [Fact]
    public void ChangesFromAnUnknownPlanAreRefusedAndSaidToBe()
    {
        Paths64 outline = [Rect(21, 37)];
        var pristine = OutlineOperation.PlanTabs(outline, Options());
        var options = Options() with { TabEdits = [new TabEdit { Index = 1, WidthNm = Mm(4) }] };

        var unstamped = OutlineOperation.PlanTabs(outline, options);
        Assert.True(unstamped.OverridesStale);
        Assert.Equal(pristine.Tabs.Select(t => t.WidthNm), unstamped.Tabs.Select(t => t.WidthNm));

        var toolpath = OutlineOperation.Build(outline, options);
        Assert.Contains(toolpath.Notes, n => n.Contains("made against a different layout", StringComparison.Ordinal));
        Assert.DoesNotContain(GapsCut(toolpath), g => Math.Abs(g.From.DistanceTo(g.To) - Mm(4)) < Mm(0.01));

        var stamped = OutlineOperation.PlanTabs(outline, options with { TabPlanKey = KeyFor(outline, options) });
        Assert.False(stamped.OverridesStale);
        Assert.Equal(Mm(4), stamped.Tabs[1].WidthNm);
    }

    /// <summary>
    /// **A different cutter makes a different plan, and the stamp says so.** The profile is offset
    /// by the cutter's radius before tabs are placed, so editing a bit's diameter in the library
    /// renumbers the tabs under a stored change — the review's own example. The change made against
    /// the 1 mm plan is refused on the 2 mm one.
    /// </summary>
    [Fact]
    public void AnEditedCutterInvalidatesTheStamp()
    {
        Paths64 outline = [Rect(21, 37)];
        var thin = Options() with { Tool = Options().Tool with { DiameterNm = Mm(1.0) } };
        var thick = Options() with { Tool = Options().Tool with { DiameterNm = Mm(2.0) } };

        var moves = new[] { new TabMove { Index = 0 } };
        var key = KeyFor(outline, thin);

        Assert.False(OutlineOperation.PlanTabs(outline, thin with { TabMoves = moves, TabPlanKey = key }).OverridesStale);
        Assert.True(OutlineOperation.PlanTabs(outline, thick with { TabMoves = moves, TabPlanKey = key }).OverridesStale);
    }

    // ------------------------------------------------------------------ overlapping tabs

    /// <summary>
    /// Lone tabs make one gap each, exactly their own width; overlapping ones make one gap, their
    /// union; a narrow tab inside a wide one makes the wide one's gap. All three are what the
    /// splitter now cuts.
    /// </summary>
    [Fact]
    public void GapsAreTheUnionOfWhatOverlaps()
    {
        static PlacedTab Tab(string letter, double alongMm, double widthMm) => new()
        {
            AlongNm = Mm(alongMm), WidthNm = Mm(widthMm), HeightNm = Mm(0.3), At = default, Edge = -1, EdgeLengthNm = 0,
            Letter = letter,
        };

        var gaps = TabPlan.Gaps(
            [Tab("A", 10, 2), Tab("B", 30, 4), Tab("C", 32, 4), Tab("D", 60, 10), Tab("E", 61, 2)],
            Mm(100));

        Assert.Equal(3, gaps.Count);
        Assert.Equal(Mm(2), gaps[0].LengthNm);
        Assert.Equal(["B", "C"], gaps[1].Tabs.Select(t => t.Letter));
        Assert.Equal(Mm(6), gaps[1].LengthNm);
        Assert.Equal(["D", "E"], gaps[2].Tabs.Select(t => t.Letter));
        Assert.Equal(Mm(10), gaps[2].LengthNm);
    }

    /// <summary>Two tabs either side of the start vertex that overlap are one gap across the seam.</summary>
    [Fact]
    public void GapsMergeAcrossTheSeam()
    {
        static PlacedTab Tab(string letter, double alongMm) => new()
        {
            AlongNm = Mm(alongMm), WidthNm = Mm(4), HeightNm = Mm(0.3), At = default, Edge = -1, EdgeLengthNm = 0,
            Letter = letter,
        };

        var gaps = TabPlan.Gaps([Tab("A", 1), Tab("B", 50), Tab("C", 98)], Mm(100));

        Assert.Equal(2, gaps.Count);
        var seam = Assert.Single(gaps, g => g.Tabs.Count == 2);
        Assert.Equal(["C", "A"], seam.Tabs.Select(t => t.Letter));
        Assert.Equal(Mm(7), seam.LengthNm);
    }

    /// <summary>
    /// **The program cuts the union and says so.** The review's case: a narrow tab dropped inside a
    /// wide one used to start the next run inside the wide one's gap, cutting it short while the
    /// header still promised the full width. Measured off the toolpath, the merged gap is the wide
    /// tab's whole width; the header names both tabs.
    /// </summary>
    [Fact]
    public void ATabInsideAnotherIsCutAsTheOuterOnesGapAndNamed()
    {
        Paths64 outline = [Rect(21, 37)];
        var plain = OutlineOperation.PlanTabs(outline, Options()).Tabs;

        // Tab B widened to 8 mm, and an added tab dropped 1 mm off its centre, inside it.
        var options = Options() with
        {
            TabEdits = [new TabEdit { Index = 1, WidthNm = Mm(8) }],
            TabMoves = [new TabMove { Index = -1, AlongNm = plain[1].AlongNm + Mm(1), Number = 0 }],
        };

        options = options with { TabPlanKey = KeyFor(outline, options) };

        var toolpath = OutlineOperation.Build(outline, options);

        foreach (var note in toolpath.Notes)
        {
            output.WriteLine(note);
        }

        var gaps = GapsCut(toolpath);
        Assert.Equal(4, gaps.Count);
        Assert.Contains(gaps, g => Math.Abs(g.From.DistanceTo(g.To) - Mm(8)) < Mm(0.01));
        Assert.Contains(toolpath.Notes, n => n.Contains("Tabs B and E overlap", StringComparison.Ordinal)
            && n.Contains("8.0 mm", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ one owner, two readers

    /// <summary>
    /// **The window and the program agree on whose tabs the changes are.** Two boards in one
    /// outline; changes that remove every tab on the first. The window used to move on and show
    /// them on the second board, while the program cut the second board untouched. Both now say:
    /// the first board, emptied, and the second as the planner placed it.
    /// </summary>
    [Fact]
    public void TheWindowAndTheProgramGiveTheChangesToTheSameBoard()
    {
        // The second board is a different size, so its tabs are its own and cannot be mistaken
        // for the first board's in the cut.
        Paths64 two = [Rect(21, 37), Rect(25, 41, xMm: 40)];
        var plain = OutlineOperation.PlanTabs(two, Options());

        var options = Options() with
        {
            TabMoves = [.. plain.Tabs.Select(t => new TabMove { Index = t.Ordinal })],
        };

        options = options with { TabPlanKey = KeyFor(two, options) };

        var window = OutlineOperation.PlanTabs(two, options);
        Assert.Empty(window.Tabs);
        Assert.Equal(plain.Profile, window.Profile);

        var toolpath = OutlineOperation.Build(two, options);

        // Only the second board is cut in tabbed runs: every run ends on it, past x = 40 mm. The
        // first board, its tabs removed, is cut as whole laps and has no runs at all.
        var runs = toolpath.Passes.Where(p => !p.Closed).ToList();
        Assert.NotEmpty(runs);
        Assert.All(runs, r => Assert.True(r.Path[^1].To.X > Mm(39), "a tabbed run on the first board"));
    }

    // ------------------------------------------------------------------ the header's account

    /// <summary>
    /// The edges' shortfall is clearance and a hand's is a choice, said apart. Forty asked of a
    /// board whose edges hold fewer: a plain move blames nothing on the hand; a removal says both.
    /// </summary>
    [Fact]
    public void TheHeaderSaysWhoseShortfallItIs()
    {
        Paths64 outline = [Rect(21, 37)];
        var forty = Options(tabs: 40);
        var plain = OutlineOperation.PlanTabs(outline, forty).Tabs;
        Assert.True(plain.Count < 40, $"the board was meant to hold fewer than forty; it held {plain.Count}");

        var moved = forty with { TabMoves = [new TabMove { Index = 0, AlongNm = plain[0].AlongNm + Mm(2) }] };
        moved = moved with { TabPlanKey = KeyFor(outline, moved) };
        var movedNotes = OutlineOperation.Build(outline, moved).Notes;

        Assert.Contains(movedNotes, n => n == $"40 were asked for: the straight edges hold {plain.Count} with clearance either side of each.");

        var removed = forty with { TabMoves = [new TabMove { Index = 0 }] };
        removed = removed with { TabPlanKey = KeyFor(outline, removed) };
        var removedNotes = OutlineOperation.Build(outline, removed).Notes;

        Assert.Contains(removedNotes, n => n == $"40 were asked for: the straight edges hold {plain.Count} with clearance either side of each, and {plain.Count - 1} after the hand-set changes.");
    }

    /// <summary>
    /// "Every tab was removed by hand" only when a hand's removals were applied — not when they
    /// were refused as stale, which leaves the planner's tabs in place.
    /// </summary>
    [Fact]
    public void OnlyAppliedRemovalsAreBlamedOnTheHand()
    {
        Paths64 outline = [Rect(21, 37)];
        var plain = OutlineOperation.PlanTabs(outline, Options()).Tabs;
        var all = Options() with { TabMoves = [.. plain.Select(t => new TabMove { Index = t.Ordinal })] };

        var applied = OutlineOperation.Build(outline, all with { TabPlanKey = KeyFor(outline, all) }).Notes;
        Assert.Contains(applied, n => n.StartsWith("Every tab was removed by hand", StringComparison.Ordinal));

        var refused = OutlineOperation.Build(outline, all).Notes;
        Assert.DoesNotContain(refused, n => n.StartsWith("Every tab was removed by hand", StringComparison.Ordinal));
        Assert.Contains(refused, n => n.StartsWith($"{plain.Count} tabs", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ added tabs keep their names

    /// <summary>
    /// Adds are numbered as they are made, and removing one leaves the others' numbers — and so
    /// their letters — where they were: F stays F.
    /// </summary>
    [Fact]
    public void RemovingAnAddLeavesTheOthersLetters()
    {
        var rect = Rect(21, 37);

        var moves = TabPlan.WithAdd([], Mm(3));
        moves = TabPlan.WithAdd(moves, Mm(17));
        Assert.Equal([0, 1], moves.Select(m => m.Number!.Value));

        var both = TabPlan.For(rect, Options() with { TabMoves = moves });
        Assert.Equal(["E", "F"], both.Where(t => t.Added).Select(t => t.Letter));

        var without = TabPlan.WithoutAdd(moves, 0);
        var survivor = Assert.Single(TabPlan.For(rect, Options() with { TabMoves = without }), t => t.Added);
        Assert.Equal("F", survivor.Letter);
        Assert.Equal(1, survivor.Ordinal);

        // And the next add takes a fresh number rather than the freed one.
        Assert.Equal(2, TabPlan.WithAdd(without, Mm(10))[^1].Number);
    }

    /// <summary>
    /// An add from before the numbering — no <see cref="TabMove.Number"/> — reads as its place
    /// among the adds, which is what it was; removing an earlier one pins the survivors' numbers so
    /// they do not shift on the way out.
    /// </summary>
    [Fact]
    public void UnnumberedAddsKeepTheirPlaces()
    {
        TabMove[] moves =
        [
            new() { Index = -1, AlongNm = Mm(3) },
            new() { Index = -1, AlongNm = Mm(17) },
        ];

        var rect = Rect(21, 37);
        Assert.Equal(["E", "F"], TabPlan.For(rect, Options() with { TabMoves = moves }).Where(t => t.Added).Select(t => t.Letter));

        var without = TabPlan.WithoutAdd(moves, 0);
        Assert.Equal(1, Assert.Single(without).Number);
        Assert.Equal("F", Assert.Single(TabPlan.For(rect, Options() with { TabMoves = without }), t => t.Added).Letter);
    }

    /// <summary>A moved add keeps its number — the thing its letter is made from — and its own size.</summary>
    [Fact]
    public void AMovedAddKeepsItsNumberAndSize()
    {
        var moves = TabPlan.WithAddSize(TabPlan.WithAdd([], Mm(3)), 0, Mm(4), null);
        var moved = TabPlan.WithAddMoved(moves, 0, Mm(12));

        var entry = Assert.Single(moved);
        Assert.Equal(0, entry.Number);
        Assert.Equal(Mm(12), entry.AlongNm);
        Assert.Equal(Mm(4), entry.WidthNm);
    }

    /// <summary>
    /// The two convenience questions on a move are not written into the project file: a file is
    /// the entry's data, and a property computed from it would be one more thing to disagree with.
    /// </summary>
    [Fact]
    public void AnEntrysKindIsNotWrittenToTheFile()
    {
        var add = new TabMove { Index = -1, AlongNm = 3, Number = 2 };
        Assert.True(add.IsAdd);
        Assert.False(add.IsRemoval);

        var json = JsonSerializer.Serialize(add);

        Assert.DoesNotContain("IsAdd", json, StringComparison.Ordinal);
        Assert.DoesNotContain("IsRemoval", json, StringComparison.Ordinal);
        Assert.Contains("\"Number\":2", json, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the walk, faster and the same

    /// <summary>
    /// **The binary search answers exactly what the linear scan answered**, on a loop with
    /// zero-length segments, at every segment end and between them, and for distances that wrap —
    /// so the planner's points, and the programs placed from them, cannot have moved. The scan is
    /// kept here as the reference.
    /// </summary>
    [Fact]
    public void TheFasterWalkFindsTheSamePoints()
    {
        Path64 loop =
        [
            new(0, 0), new(Mm(10), 0), new(Mm(10), 0), new(Mm(10), Mm(5)),
            new(Mm(3), Mm(7)), new(Mm(3), Mm(7)), new(0, Mm(4)),
        ];

        var path = new ProfilePath(loop);
        var segments = IsolationOperation.ToSegments(loop);
        var starts = new double[segments.Count];
        var lengths = new double[segments.Count];
        var total = 0.0;

        for (var i = 0; i < segments.Count; i++)
        {
            starts[i] = total;
            lengths[i] = segments[i].From.DistanceTo(segments[i].To);
            total += lengths[i];
        }

        Point2 Scan(double at)
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

        var probes = new List<double> { -5, 0, total, total + 1e6 };
        probes.AddRange(starts);
        probes.AddRange(starts.Select((s, i) => s + lengths[i]));

        for (var k = 0; k <= 400; k++)
        {
            probes.Add(total * k / 400.0);
        }

        foreach (var at in probes)
        {
            Assert.Equal(Scan(path.Wrap(at)), path.PointAt(at));
        }
    }
}
