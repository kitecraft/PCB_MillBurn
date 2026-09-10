using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// What the export report says about an inverted SVG.
///
/// Two senses of "negative" meet on this one dialog and they are not the same thing. A Gerber can
/// *declare* negative polarity, meaning its drawn shapes are where the material is absent; and the
/// operator can tick *Inverted*, which takes the complement against the board outline. The second
/// resolves the first — so saying both at once, as this did, tells somebody two contradictory
/// things about the file they are about to burn.
/// </summary>
public sealed class InvertedSvgTests(ITestOutputHelper output)
{
    private static ExportItem MaskSvg(bool inverted)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = l.Role == LayerRole.TopMask ? OutputKind.Svg : OutputKind.None,
                Invert = inverted,
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Svg);

        return Assert.Single(plan.Items);
    }

    /// <summary>The layer really is declared negative, or this whole file is testing nothing.</summary>
    [Fact]
    public void TheTestLayerIsActuallyDeclaredNegative()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var mask = loaded.Layers.First(l => l.Role == LayerRole.TopMask);

        Assert.True(mask.DeclaredNegative, "PogoTest1's top mask should declare negative polarity");
    }

    [Fact]
    public void ANegativeLayerSaysSoWhenItIsNotInverted()
    {
        var item = MaskSvg(inverted: false);

        output.WriteLine(string.Join("\n", item.Warnings));

        Assert.Contains(
            item.Warnings,
            w => w.Contains("shapes are its openings", StringComparison.Ordinal));
    }

    /// <summary>
    /// The bug as reported: the same warning appeared whether Inverted was ticked or not. Inverting
    /// against the board outline is exactly the step that turns those openings into material, so
    /// once it has been done the warning is describing the opposite of the file.
    /// </summary>
    [Fact]
    public void InvertingResolvesItAndTheWarningStops()
    {
        var item = MaskSvg(inverted: true);

        output.WriteLine(string.Join("\n", item.Warnings.Concat(item.Summary)));

        Assert.DoesNotContain(
            item.Warnings,
            w => w.Contains("shapes are its openings", StringComparison.Ordinal));

        Assert.Contains(
            item.Summary,
            s => s.Contains("the layer's material", StringComparison.Ordinal));
    }

    /// <summary>
    /// And the summary must not contradict itself either: "everything except this layer" describes
    /// the operation, and on a negative layer the result is the layer's own material.
    /// </summary>
    [Fact]
    public void TheSummaryDoesNotCallItEverythingExceptTheLayer()
    {
        Assert.DoesNotContain(
            MaskSvg(inverted: true).Summary,
            s => s.Contains("except this layer", StringComparison.Ordinal));
    }

    /// <summary>
    /// The counts describe the drawing, not the layer it came from.
    ///
    /// Inverting replaces the geometry entirely, so reporting the source layer's shape count and
    /// area for a drawing that is now the board minus those shapes is the wrong number in the one
    /// place somebody checks before writing the file. Same failure as a drill summary that counted
    /// the Excellon's tools rather than the program's.
    /// </summary>
    [Fact]
    public void TheShapeCountAndAreaDescribeWhatIsInTheFile()
    {
        var plain = MaskSvg(inverted: false);
        var flipped = MaskSvg(inverted: true);

        static (int Shapes, double AreaMm2) Read(ExportItem item)
        {
            var line = item.Summary.First(s => s.Contains("shapes,", StringComparison.Ordinal));
            var parts = line.Split(' ');

            return (
                int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
        }

        var before = Read(plain);
        var after = Read(flipped);

        output.WriteLine($"plain {before.Shapes} shapes {before.AreaMm2:F2} mm²");
        output.WriteLine($"inverted {after.Shapes} shapes {after.AreaMm2:F2} mm²");

        // The inverted drawing gains the board outline as a ring, and its area is the board's
        // less the openings — so it must be much larger, not identical.
        Assert.Equal(before.Shapes + 1, after.Shapes);
        Assert.True(
            after.AreaMm2 > before.AreaMm2 * 4,
            $"inverted area {after.AreaMm2:F2} should dwarf {before.AreaMm2:F2}");

        // Board minus openings, to the millimetre.
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1)).Bounds;
        var boardMm2 = board.Width / (double)Nm.PerMillimetre * (board.Height / (double)Nm.PerMillimetre);

        Assert.Equal(boardMm2 - before.AreaMm2, after.AreaMm2, 0);
    }

    /// <summary>A positive layer keeps the plain description, which is accurate for it.</summary>
    [Fact]
    public void APositiveLayerIsDescribedAsEverythingExceptItself()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var silk = loaded.Layers.FirstOrDefault(l => l.Role == LayerRole.TopSilk && !l.DeclaredNegative);

        Assert.NotNull(silk);

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = l.FileName == silk.FileName ? OutputKind.Svg : OutputKind.None,
                Invert = true,
            },
            StringComparer.Ordinal);

        var item = Assert.Single(ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Svg).Items);

        Assert.Contains(item.Summary, s => s.Contains("except this layer", StringComparison.Ordinal));
        Assert.DoesNotContain(item.Warnings, w => w.Contains("openings", StringComparison.Ordinal));
    }
}
