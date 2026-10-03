using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The tabs that hold the board while it is cut out, and how much material is left under them.
///
/// Reported from the workshop after a set of edge cuts: the tabs were left at the full thickness of
/// the board, so the piece would not come free and had to be sawn out. The cause is that the tab
/// region is cut only by the passes *shallower* than the tab, and nothing made sure there was one:
/// a 0.8 mm board cut 0.90 mm deep with a cutter whose step down is 1.00 mm has exactly one pass, it
/// is deeper than the tab, and so every pass jumps the gap and no cutter ever touches the tab.
///
/// The number these tests defend is the one the program's own header promises — "0.50 mm of material
/// left under each" — because that is what the operator reads before starting.
/// </summary>
public sealed class OutlineTabTests(ITestOutputHelper output)
{
    private static Paths64 Board() =>
    [
        [
            new(0, 0),
            new(Nm.FromMillimetres(50), 0),
            new(Nm.FromMillimetres(50), Nm.FromMillimetres(30)),
            new(0, Nm.FromMillimetres(30)),
        ],
    ];

    /// <summary>
    /// One cut of the 50 x 30 mm board, at a thickness and a step down.
    ///
    /// A null tab height takes the derived one. The cases below that are about the workshop's own
    /// numbers pass the height they were measured with, because the default now depends on the
    /// thickness — see <see cref="TheDerivedHeightIsThinnerOnThinStock"/> — and a fixture whose
    /// expected value moves with a rule it is not testing proves nothing.
    /// </summary>
    private static Toolpath Cut(
        double thicknessMm,
        double stepdownMm,
        int tabs = 4,
        double? tabHeightMm = null)
    {
        var cutter = Tool.DefaultOutlineMill with { StepdownNm = Nm.FromMillimetres(stepdownMm) };

        return OutlineOperation.Build(
            Board(),
            new OutlineOptions
            {
                Tool = cutter,
                BoardThicknessNm = Nm.FromMillimetres(thicknessMm),
                DepthPerPassNm = Nm.FromMillimetres(stepdownMm),
                TabCount = tabs,
                TabHeightNm = tabHeightMm is { } mm ? Nm.FromMillimetres(mm) : null,
                Keep = [],
            });
    }

    /// <summary>
    /// How deep the cut goes where the tabs are: the deepest pass that runs right round, uninterrupted.
    ///
    /// A tabbed pass is broken into runs that jump the gaps, so the passes that cross a tab are the
    /// unbroken ones. The deepest of those is what decides the tab's height.
    /// </summary>
    private static long OverTheTabs(Toolpath toolpath) => toolpath.Passes
        .Where(p => p.Closed)
        .Select(p => p.DepthNm)
        .DefaultIfEmpty(0)
        .Max();

    /// <summary>
    /// The case from the workshop: one pass, deeper than the tab. Before the fix nothing cut the tab
    /// at all and the board stayed attached by its full thickness.
    /// </summary>
    [Fact]
    public void AStepDownDeeperThanTheWholeCutStillCutsTheTabs()
    {
        var toolpath = Cut(thicknessMm: 0.8, stepdownMm: 1.0, tabHeightMm: 0.5);
        var overTabs = OverTheTabs(toolpath);

        output.WriteLine($"depths: {string.Join(", ", toolpath.Passes.Select(p => Nm.ToMillimetreString(p.DepthNm, 2)).Distinct())}");
        output.WriteLine($"cut to {Nm.ToMillimetreString(overTabs, 2)} mm over the tabs");

        // 0.8 mm board, 0.5 mm tabs: the tab region is cut to 0.30 mm, leaving 0.50 mm under it.
        Assert.Equal(Nm.FromMillimetres(0.3), overTabs);

        // **And the board is cut through**, which a `/describe-test` pass found nothing checking:
        // the line above is satisfied by a program consisting of one closed 0.30 mm pass and
        // nothing else, which cuts a groove and leaves the board in the stock. (A second line
        // asserting 0.5 == 0.8 - overTabs used to sit here, which restates the line above and
        // cannot fail once it has passed.)
        Assert.Contains(toolpath.Passes, p => p.DepthNm > Nm.FromMillimetres(0.8));
    }

    /// <summary>
    /// And on an ordinary board, where the step down does reach past the tab but never lands on it:
    /// 1.6 mm in 0.4 mm steps used to leave the tab 1.10 mm thick, not the 0.50 mm the header promised.
    /// </summary>
    [Fact]
    public void TheTabIsAsTallAsTheProgramSaysItIs()
    {
        var toolpath = Cut(thicknessMm: 1.6, stepdownMm: 0.4);
        var overTabs = OverTheTabs(toolpath);

        output.WriteLine($"cut to {Nm.ToMillimetreString(overTabs, 2)} mm over the tabs");

        Assert.Equal(Nm.FromMillimetres(0.5), Nm.FromMillimetres(1.6) - overTabs);
    }

    /// <summary>
    /// And on thin stock the derived height is thinner, which is the whole reason it is derived.
    ///
    /// **0.5 mm under a 0.8 mm board is most of the board.** The fixed default was chosen on 1.6 mm
    /// laminate, where a third of the thickness is what it already was, so an ordinary board's
    /// programs do not move; on 0.8 mm stock it left a web thicker than the cut above it, which tears
    /// rather than snaps.
    ///
    /// Asserted against <see cref="TabPlan.DefaultHeightFor"/> rather than against 0.25 mm, and then
    /// against the *board* — the web must be less than half the thickness — so the test fails if the
    /// rule is changed to something that stops being thin, not merely if the arithmetic moves.
    /// </summary>
    [Fact]
    public void TheDerivedHeightIsThinnerOnThinStock()
    {
        var thin = Nm.FromMillimetres(0.8);
        var toolpath = Cut(thicknessMm: 0.8, stepdownMm: 1.0);
        var left = thin - OverTheTabs(toolpath);

        output.WriteLine($"{Nm.ToMillimetreString(left, 2)} mm left under a 0.80 mm board");

        Assert.Equal(TabPlan.DefaultHeightFor(thin), left);
        Assert.True(left < thin / 2, "the web under a tab is more than half the board");

        // Unchanged where it was chosen. A rule that quietly rewrote every 1.6 mm board's programs
        // would be a different decision from the one this is.
        Assert.Equal(Nm.FromMillimetres(0.5), TabPlan.DefaultHeightFor(Nm.FromMillimetres(1.6)));
    }

    /// <summary>The extra pass is only for tabs: asked for none, the schedule is the step down's.</summary>
    [Fact]
    public void WithoutTabsTheDepthsAreJustTheStepDown()
    {
        var toolpath = Cut(thicknessMm: 0.8, stepdownMm: 1.0, tabs: 0);

        // 0.8 mm of board and the default 0.30 mm of break-through, in 1.00 mm steps.
        var depths = toolpath.Passes.Select(p => p.DepthNm).Distinct().Order().ToList();

        Assert.Equal([Nm.FromMillimetres(1.0), Nm.FromMillimetres(1.1)], depths);
    }

    /// <summary>
    /// A tab taller than the cut cannot be cut down at all. It says so in the program rather than
    /// quietly leaving a board that will not come out.
    ///
    /// **The height is passed in on purpose.** With a derived height this case cannot be reached from
    /// the thickness alone — a third of 0.3 mm is 0.1 mm, clamped up to the 0.2 mm floor, which the
    /// 0.6 mm cut can still reach — so the fixture is an operator who typed 0.5 mm on stock a third
    /// of a millimetre thick.
    ///
    /// **And the phrase asserted had to change.** It used to look for "full thickness of the board",
    /// which now also appears in the note about the extra pass cut at the tab's top — so the test
    /// went on passing after the height stopped being 0.5 mm by default and this case stopped
    /// happening at all. What it looks for now is the sentence that only this case writes.
    /// </summary>
    [Fact]
    public void ATabTallerThanTheCutSaysSo()
    {
        // 0.3 mm of board and 0.3 mm of break-through: 0.6 mm of cut, less than the 0.5 mm tab plus
        // the break-through it sits above.
        var toolpath = Cut(thicknessMm: 0.3, stepdownMm: 1.0, tabHeightMm: 0.5);

        output.WriteLine(string.Join("\n", toolpath.Notes));

        Assert.Contains(
            toolpath.Notes,
            n => n.Contains("have to be cut by hand", StringComparison.Ordinal));

        // And nothing was cut down to a tab, which is the physical claim behind the sentence.
        //
        // **Asserted on the passes as well as on the prose**, because a note is what the program says
        // and the depths are what it does: 0.3 mm of board and 0.3 mm of break-through is 0.6 mm of
        // cut in one step, and if a tab-top pass had been added anyway there would be a second depth
        // here.
        Assert.DoesNotContain(toolpath.Notes, n => n.Contains("One extra pass at", StringComparison.Ordinal));

        var depths = toolpath.Passes.Select(p => p.DepthNm).Distinct().Order().ToList();

        output.WriteLine($"depths: {string.Join(", ", depths.Select(d => Nm.ToMillimetreString(d, 2)))}");

        Assert.Equal([Nm.FromMillimetres(0.6)], depths);
    }

    /// <summary>
    /// Every pass that jumps the gaps is below the one that crosses them — the tab is cut once, from
    /// above, and never re-cut deeper.
    /// </summary>
    [Fact]
    public void TheTabbedPassesAreAllDeeperThanTheOneThatCrossesThem()
    {
        var toolpath = Cut(thicknessMm: 1.6, stepdownMm: 0.4);
        var overTabs = OverTheTabs(toolpath);
        var broken = toolpath.Passes.Where(p => !p.Closed).ToList();

        output.WriteLine(
            $"{broken.Count} broken pass(es) below {Nm.ToMillimetreString(overTabs, 2)} mm");

        // **The guard, added after a `/describe-test` pass.** `Assert.All` over an empty sequence
        // passes, so a change that stopped breaking the passes at all — tabs dropped, every pass a
        // full lap, the board cut free on the last one — would have left this test green while
        // removing the thing it is named for.
        Assert.NotEmpty(broken);

        // **And the other side of the same hole**, from the `/describe-test` pass that read this
        // back: with no closed pass at all `OverTheTabs` returns 0, every real depth beats it, and
        // both assertions pass while nothing ever crosses a tab.
        Assert.Contains(toolpath.Passes, p => p.Closed);
        Assert.True(overTabs > 0, "no pass laps right round, so nothing cuts the tabs");

        Assert.All(
            broken,
            p => Assert.True(p.DepthNm > overTabs, $"a broken pass at {Nm.ToMillimetreString(p.DepthNm, 2)} mm"));
    }
    /// <summary>
    /// What was asked for is counted against the profiles that could have had tabs, not the ones
    /// that got them.
    ///
    /// **Found by `/code-review`.** The multiplier was the number of profiles that received at least
    /// one tab, so a profile too small to hold any was left out of it: a job of two profiles where
    /// one holds four tabs and the other none reported "4 were asked for: the straight edges hold
    /// 4" — a sentence that says nothing happened when half the job went untabbed — and when the
    /// counts happened to match it suppressed the note entirely.
    ///
    /// Two separate rectangles, the second a 2 x 2 mm square that twenty tabs cannot fit on. The
    /// note has to account for all forty.
    /// </summary>
    [Fact]
    public void WhatWasAskedForCountsEveryProfileThatCouldHaveHadTabs()
    {
        Paths64 two =
        [
            Board()[0],
            [
                new(Nm.FromMillimetres(70), 0),
                new(Nm.FromMillimetres(72), 0),
                new(Nm.FromMillimetres(72), Nm.FromMillimetres(2)),
                new(Nm.FromMillimetres(70), Nm.FromMillimetres(2)),
            ],
        ];

        var toolpath = OutlineOperation.Build(
            two,
            new OutlineOptions
            {
                Tool = Tool.DefaultOutlineMill,
                BoardThicknessNm = Nm.FromMillimetres(1.6),
                DepthPerPassNm = Nm.FromMillimetres(0.4),
                TabCount = 20,
                Keep = [],
            });

        output.WriteLine(string.Join(" | ", toolpath.Notes));

        var asked = Assert.Single(toolpath.Notes, n => n.Contains("were asked for", StringComparison.Ordinal));

        // Forty: twenty on each of the two profiles. Twenty is what the old arithmetic said, the
        // count for the one profile that got any, so the two cannot be confused.
        Assert.StartsWith("40 were asked for", asked, StringComparison.Ordinal);
    }
}
