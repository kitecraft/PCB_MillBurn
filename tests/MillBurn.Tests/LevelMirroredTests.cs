using MillBurn.Align;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// A height map is never applied to a program that is about to be flipped.
///
/// Found by reading a real export before running it. With a map imported, the export wrote
/// <c>Board-B_Cu.levelled.nc</c> — a file whose own header says <em>every Z follows a measured
/// surface</em> directly above <em>flip the stock left-to-right</em>. Those cannot both be true:
/// turning the stock over presents the other face, in coordinates that have since been mirrored,
/// so the correction lands on the wrong point of the wrong surface.
///
/// Nothing in the suite noticed, because every other levelling test levels a program nobody flips.
/// </summary>
public sealed class LevelMirroredTests(ITestOutputHelper output)
{
    [Fact]
    public void AMirroredProgramIsRefused()
    {
        var why = Leveller.WhyNotLevel(mirrored: true);

        output.WriteLine(why);

        Assert.NotNull(why);
        Assert.Contains("re-probe after the flip", why, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnmirroredProgramIsNot() =>
        Assert.Null(Leveller.WhyNotLevel(mirrored: false));

    /// <summary>
    /// And the planner still says which items are mirrored, which is what the refusal keys on. If
    /// this ever stopped being carried, the refusal would quietly stop happening.
    /// </summary>
    [Fact]
    public void ThePlanStillSaysWhichSideEachProgramIsFor()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        var bottom = plan.Items.Single(i => i.LayerFileName.Contains("B_Cu", StringComparison.Ordinal));
        var top = plan.Items.Single(i => i.LayerFileName.Contains("F_Cu", StringComparison.Ordinal));

        Assert.True(bottom.Mirrored, "the bottom copper is cut on the flipped stock");
        Assert.False(top.Mirrored);

        Assert.NotNull(Leveller.WhyNotLevel(bottom.Mirrored));
        Assert.Null(Leveller.WhyNotLevel(top.Mirrored));
    }
}
