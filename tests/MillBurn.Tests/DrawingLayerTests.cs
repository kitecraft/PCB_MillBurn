using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// A drawing of the board is not the board.
///
/// KiCad's drill map is a chart for a person to read: a symbol per hole, a legend, and — this is
/// the part that bites — the board profile drawn underneath it all, channels and slots included.
/// It was being piled in with the copper as evidence that a profile encloses something worth
/// keeping, so on a panel every routed channel had a copy of its own outline sitting inside it,
/// answered "yes, something is in here", and was called a piece. Pieces are cut on the outside, so
/// the program took a groove out of the board on each side of every channel and left the channel
/// standing: the panel never comes apart and every board is a cutter-radius undersize.
///
/// The same panel with no drill map exported correctly, which is why this survived: the corpus had
/// panels, and it had boards with drill maps, and no board that was both.
/// </summary>
public sealed class DrawingLayerTests(ITestOutputHelper output)
{
    /// <summary>The panel as loaded, and the same panel with KiCad's drill map alongside it.</summary>
    private static Board Panel(bool withDrillMap)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.Panel));

        if (!withDrillMap)
        {
            return loaded;
        }

        // What KiCad actually writes: the profile, redrawn as the backdrop of the hole chart.
        var outline = loaded.Layers.Single(l => l.Role == LayerRole.Outline);

        return loaded with
        {
            Layers =
            [
                .. loaded.Layers,
                new BoardLayer
                {
                    FileName = "Panel-PTH-drl_map.gbr",
                    Role = LayerRole.DrillMap,
                    RoleGuessed = false,
                    Area = new Paths64(outline.Area),
                    Bounds = outline.Bounds,
                    ObjectCount = outline.ObjectCount,
                },
            ],
        };
    }

    private static string OutlineSummary(Board board)
    {
        var settings = board.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            board, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        var outline = plan.Items.Single(i => i.Operation == OperationKind.Outline);

        return string.Join("\n", outline.Summary);
    }

    /// <summary>
    /// The channels are cut from the inside whether or not a drill map is in the folder.
    ///
    /// Asserted as "the two exports say the same thing" rather than against a count, because the
    /// number of channels is a property of the fixture and the claim here is only that adding a
    /// drawing changes nothing.
    /// </summary>
    [Fact]
    public void ADrillMapDoesNotChangeWhichSideTheCutterRunsOn()
    {
        var plain = OutlineSummary(Panel(withDrillMap: false));
        var charted = OutlineSummary(Panel(withDrillMap: true));

        output.WriteLine($"without a drill map:\n{plain}\n\nwith one:\n{charted}");

        Assert.Contains("cut from the inside", plain, StringComparison.Ordinal);
        Assert.Equal(plain, charted);
    }
}
