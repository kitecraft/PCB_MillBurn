using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using Xunit;
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

    private static Toolpath Cut(double thicknessMm, double stepdownMm, int tabs = 4)
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
        var toolpath = Cut(thicknessMm: 0.8, stepdownMm: 1.0);
        var overTabs = OverTheTabs(toolpath);

        output.WriteLine($"depths: {string.Join(", ", toolpath.Passes.Select(p => Nm.ToMillimetreString(p.DepthNm, 2)).Distinct())}");
        output.WriteLine($"cut to {Nm.ToMillimetreString(overTabs, 2)} mm over the tabs");

        // 0.8 mm board, 0.5 mm tabs: the tab region is cut to 0.30 mm, leaving 0.50 mm under it.
        Assert.Equal(Nm.FromMillimetres(0.3), overTabs);
        Assert.Equal(Nm.FromMillimetres(0.5), Nm.FromMillimetres(0.8) - overTabs);
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
    /// </summary>
    [Fact]
    public void ATabTallerThanTheCutSaysSo()
    {
        // 0.3 mm of board and 0.1 mm of break-through: less than the 0.5 mm tab.
        var toolpath = Cut(thicknessMm: 0.3, stepdownMm: 1.0);

        output.WriteLine(string.Join("\n", toolpath.Notes));

        Assert.Contains(toolpath.Notes, n => n.Contains("full thickness of the board", StringComparison.Ordinal));
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

        Assert.All(
            toolpath.Passes.Where(p => !p.Closed),
            p => Assert.True(p.DepthNm > overTabs, $"a broken pass at {Nm.ToMillimetreString(p.DepthNm, 2)} mm"));
    }
}
