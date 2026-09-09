using MillBurn.Core;
using MillBurn.Gerber.Excellon;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// A hole gets drilled by a bit its own size.
///
/// Obvious, and it was wrong: the planner built one toolpath per drill size and then folded them
/// into one, which kept the first tool and threw the rest away. A board with 1.70 mm and 1.00 mm
/// holes had all sixteen drilled at 1.70 mm, with nothing in the file, the summary or the preview
/// to say so — the hole count was right, the positions were right, and the sizes were silently
/// wrong. Nothing in 495 other tests noticed.
/// </summary>
public sealed class DrillSizeTests(ITestOutputHelper output)
{
    private static ExportPlan Plan(string board)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        return ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);
    }

    private static ExcellonFile? DrillOf(string board, string targetName)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));

        return loaded.Layers
            .Where(l => l.Drill is not null)
            .Select(l => l.Drill)
            .FirstOrDefault(d => d!.Tools.Count > 0 && targetName.Contains(
                Path.GetFileNameWithoutExtension(
                    loaded.Layers.First(l => l.Drill == d).FileName),
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every distinct diameter in the drill file gets named in the program, with its own hole
    /// count, and a stop between each so the operator can swap the bit.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.PogoTest1AllLayers)]
    public void EverySizeGetsItsOwnBitAndItsOwnToolChange(string board)
    {
        var drilling = Plan(board).Items
            .Where(i => i.Operation == OperationKind.Drilling)
            .ToList();

        Assert.NotEmpty(drilling);

        var checkedMultiSize = false;

        foreach (var item in drilling)
        {
            var drill = DrillOf(board, item.TargetName);

            if (drill is null)
            {
                continue;
            }

            var sizes = drill.Tools.Count;
            var stops = item.Content.Split('\n').Count(l => l.Trim() == "M0");

            output.WriteLine($"{item.TargetName}: {sizes} size(s), {stops} tool change stop(s)");

            // One bit goes in before the program starts, so a change is only needed for the rest.
            Assert.Equal(sizes - 1, stops);

            foreach (var (tool, _) in drill.ByTool())
            {
                var holes = drill.Hits.Count(h => h.Tool == tool.Number);
                var millimetres = Nm.ToMillimetreString(tool.DiameterNm, 2);

                if (holes == 0)
                {
                    continue;
                }

                Assert.Contains(
                    $"Drill {millimetres} mm [{holes} holes]",
                    item.Content,
                    StringComparison.Ordinal);
            }

            checkedMultiSize |= sizes > 1;
        }

        Assert.True(checkedMultiSize, "this board was supposed to have a layer with several sizes");
    }

    /// <summary>
    /// The holes themselves are all still there and still where they were. Splitting the operation
    /// per bit must not lose any, and it is exactly the kind of change that could.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.PogoTest1AllLayers)]
    public void NoHoleIsLostBySplittingTheOperation(string board)
    {
        foreach (var item in Plan(board).Items.Where(i => i.Operation == OperationKind.Drilling))
        {
            var drill = DrillOf(board, item.TargetName);

            if (drill is null)
            {
                continue;
            }

            // A drilled hole is one descent below zero, however many pecks it takes to get there:
            // count the distinct XY positions the program plunges at.
            var plunges = GcodeParser.Parse(item.Content).Moves
                .Where(m => m.IsVertical && m.ToZNm < 0)
                .Select(m => m.From)
                .Distinct()
                .Count();

            output.WriteLine($"{item.TargetName}: {drill.Hits.Count} holes in the file, {plunges} drilled");
            Assert.Equal(drill.Hits.Count, plunges);
        }
    }

    /// <summary>
    /// Biggest first. A small bit wanders when it starts on a surface a larger one has already
    /// broken, so the usual small-to-large reasoning is backwards here — and the delicate bits
    /// should spend the least time in the spindle.
    /// </summary>
    [Fact]
    public void TheBiggestBitGoesFirst()
    {
        var item = Plan(RealBoards.PogoTest1).Items
            .First(i => i.Operation == OperationKind.Drilling && i.Content.Contains("M0", StringComparison.Ordinal));

        var order = item.Content.Split('\n')
            .Where(l => l.StartsWith("( Drill ", StringComparison.Ordinal))
            .Select(l => double.Parse(
                l.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        output.WriteLine("bit order: " + string.Join(", ", order));

        Assert.True(order.Count > 1, "expected more than one bit");
        Assert.Equal(order.OrderByDescending(d => d), order);
    }
}
