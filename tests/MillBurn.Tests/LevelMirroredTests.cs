using MillBurn.Align;
using MillBurn.Core;
using MillBurn.Pipeline;
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
    /// <summary>
    /// The rule in full: a program and a map either belong to the same side of the stock or they do
    /// not, and only the operator knows which side was probed.
    /// </summary>
    [Theory]
    [InlineData(false, false, true)]   // top program, top map
    [InlineData(true, true, true)]     // bottom program, bottom map
    [InlineData(true, false, false)]   // bottom program, top map — the one that shipped
    [InlineData(false, true, false)]   // top program, bottom map
    public void AProgramAndAMapMustBeForTheSameSide(bool mirrored, bool flippedMap, bool allowed)
    {
        var why = Leveller.WhyNotLevel(mirrored, flippedMap);

        output.WriteLine($"program {(mirrored ? "bottom" : "top")}, map {(flippedMap ? "bottom" : "top")} -> {why ?? "allowed"}");

        Assert.Equal(allowed, why is null);
    }

    /// <summary>The refusal says which way round each of them is, not merely that they disagree.</summary>
    [Fact]
    public void TheRefusalSaysWhichWayRoundBothAre()
    {
        var bottomProgram = Leveller.WhyNotLevel(true, false);
        var bottomMap = Leveller.WhyNotLevel(false, true);

        output.WriteLine(bottomProgram);
        output.WriteLine(bottomMap);

        Assert.Contains("cut on the flipped stock", bottomProgram, StringComparison.Ordinal);
        Assert.Contains("probed with the board top-up", bottomProgram, StringComparison.Ordinal);

        Assert.Contains("cut with the board top-up", bottomMap, StringComparison.Ordinal);
        Assert.Contains("probed with the stock flipped", bottomMap, StringComparison.Ordinal);
    }

    /// <summary>Top-up is the default, because it is what almost everybody does.</summary>
    [Fact]
    public void TheDefaultAssumesTheBoardWasProbedTheWayUpItWasImported()
    {
        Assert.Null(Leveller.WhyNotLevel(programMirrored: false));
        Assert.NotNull(Leveller.WhyNotLevel(programMirrored: true));
    }

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

        // And the other way round when the operator says they probed the flipped stock.
        Assert.Null(Leveller.WhyNotLevel(bottom.Mirrored, mapOfFlippedStock: true));
        Assert.NotNull(Leveller.WhyNotLevel(top.Mirrored, mapOfFlippedStock: true));
    }
}
