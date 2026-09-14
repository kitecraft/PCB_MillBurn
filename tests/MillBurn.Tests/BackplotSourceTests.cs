using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// One source layer, more than one program, and every drawn path still reachable by its controls.
///
/// Found in the window: with the Long rapids chip off, the rapids of a routing program stayed on
/// screen. A drilling layer writes two programs — drilling and routing — and each program's drawn
/// layers were given ids made of the kind of move and the source layer, so the two programs got the
/// same ids. The scene finds a layer by id and takes the first, so the chips and the layer's own row
/// reached the drilling program's paths and never the routing program's.
/// </summary>
public sealed class BackplotSourceTests(ITestOutputHelper output)
{
    private static IReadOnlyList<BackplotMove> Classify(string gcode) =>
        GcodeBackplot.Classify(GcodeParser.Parse(gcode));

    /// <summary>Two programs from one layer: one set of ids, holding both programs' runs.</summary>
    [Fact]
    public void TwoProgramsFromOneLayerShareOneSetOfLayers()
    {
        // Each: a long rapid out from zero, then a short cut. Different places, so their runs differ.
        var drilling = Classify("G21 G90\nG0 Z2\nG0 X50 Y0\nG1 Z-0.1 F60\nG1 X51 Y0 F200\nG0 Z2\n");
        var routing = Classify("G21 G90\nG0 Z2\nG0 X0 Y60\nG1 Z-0.1 F60\nG1 X1 Y60 F200\nG0 Z2\n");

        var layers = BackplotBuilder.BuildPerProgram(
        [
            new BackplotBuilder.Program("Board-PTH-drl.gbr", "Plated holes", drilling),
            new BackplotBuilder.Program("Board-PTH-drl.gbr", "Plated holes", routing),
        ]);

        foreach (var layer in layers)
        {
            output.WriteLine($"{layer.Id}: {layer.Runs.Count} run(s)");
        }

        Assert.Equal(layers.Count, layers.Select(l => l.Id).Distinct(StringComparer.Ordinal).Count());

        var rapids = Assert.Single(layers, l => l.ColourKey == "gcode-long-travel");
        var cuts = Assert.Single(layers, l => l.ColourKey == "gcode-cut");

        Assert.Equal(2, rapids.Runs.Count);
        Assert.Equal(2, cuts.Runs.Count);
        Assert.Equal("Plated holes · Long rapids", rapids.Label);
    }

    /// <summary>
    /// On a real board whose plated-holes layer writes a drilling program and a routing program: no
    /// id is used twice, and every long rapid in either program is in the one layer the chip controls.
    /// </summary>
    [Fact]
    public void OnARealBoardEveryDrawnLayerHasItsOwnId()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoUno));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        // The same programs the window builds its preview from.
        var programs = plan.Items
            .Where(i => i.Output == OutputKind.Gcode)
            .Select(i => new BackplotBuilder.Program(
                i.LayerFileName, i.LayerLabel, Classify(i.Content), i.Mirrored))
            .ToList();

        var shared = programs.GroupBy(p => p.Source).Where(g => g.Count() > 1).ToList();

        Assert.NotEmpty(shared);

        var layers = BackplotBuilder.BuildPerProgram(programs);

        Assert.Equal(layers.Count, layers.Select(l => l.Id).Distinct(StringComparer.Ordinal).Count());

        foreach (var source in shared)
        {
            var expected = source.Sum(p =>
                BackplotBuilder.Build(p.Moves).FirstOrDefault(l => l.Id == "gcode-long-travel").Runs?.Count ?? 0);

            var drawn = layers.Single(l => l.Id == "gcode-long-travel:" + source.Key);

            output.WriteLine($"{source.Key}: {source.Count()} programs, {drawn.Runs.Count} long rapids in one layer");

            Assert.True(expected > 1, "the fixture should have long rapids in more than one program here");
            Assert.Equal(expected, drawn.Runs.Count);
        }
    }
}
