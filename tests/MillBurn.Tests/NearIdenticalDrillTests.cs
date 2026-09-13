using System.Text.RegularExpressions;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Two drill sizes that are the same bit, and the page that told the operator otherwise.
///
/// Found on a real board. A KiCad export carried <c>1.00000</c> and <c>1.00076</c> mm as two
/// apertures — 0.76 µm apart, the residue of a 0.0394" pad laid out in inches. They became two
/// toolpaths and two blocks of G-code, both printing as "Drill 1.00 mm", and because the emitter
/// compares tool *names* it saw no change of tool and emitted no stop between them.
///
/// The drilling guide then split the program on <c>M0</c> and zipped the four labels onto the three
/// sections by index. Every entry after the collision named the wrong bit, and the last one told
/// the operator to fit a <b>1.00 mm drill for fifteen 0.40 mm holes</b>. Follow the page and the
/// board is scrap.
///
/// Two defects, fixed separately, because either alone is enough to do it again: sizes that close
/// together are one bit, and the guide reads each section's own label instead of counting.
/// </summary>
public sealed class NearIdenticalDrillTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "millburn-nearbits-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the test is not a test failure.
        }
    }

    /// <summary>
    /// The shape the real board had: a big size, two sizes a micron apart, and a small one. The
    /// order matters — the small bit is last, which is where the mis-numbering landed.
    /// </summary>
    private const string Drill = """
        %TF.FileFunction,Plated,1,2,PTH,Drill*%
        %FSLAX46Y46*%
        %MOMM*%
        %LPD*%
        G01*
        %ADD10C,1.300000*%
        %ADD11C,1.000000*%
        %ADD12C,1.000760*%
        %ADD13C,0.400000*%
        D10*
        X2000000Y2000000D03*
        D11*
        X4000000Y2000000D03*
        X5000000Y2000000D03*
        D12*
        X6000000Y2000000D03*
        D13*
        X2000000Y6000000D03*
        X3000000Y6000000D03*
        X4000000Y6000000D03*
        M02*
        """;

    private const string Outline = """
        %TF.FileFunction,Profile,NP*%
        %FSLAX46Y46*%
        %MOMM*%
        %LPD*%
        G01*
        %ADD10C,0.100000*%
        D10*
        X0Y0D02*
        X10000000Y0D01*
        X10000000Y8000000D01*
        X0Y8000000D01*
        X0Y0D01*
        M02*
        """;

    private ExportItem Drilling()
    {
        File.WriteAllText(Path.Combine(_folder, "Board-PTH-drl.gbr"), Drill);
        File.WriteAllText(Path.Combine(_folder, "Board-Edge_Cuts.gbr"), Outline);

        var board = BoardLoader.LoadFolder(_folder);

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

        return Assert.Single(plan.Items, i => i.Operation == OperationKind.Drilling);
    }

    /// <summary>
    /// Three bits, not four. Nothing distinguishes 1.00000 from 1.00076, and a program that stops
    /// to have you swap a bit for the one already in the spindle is worse than wrong — it teaches
    /// the operator to ignore the stops.
    /// </summary>
    [Fact]
    public void SizesAMicronApartAreOneBit()
    {
        var item = Drilling();
        var labels = Labels(item.Content);

        output.WriteLine(string.Join(" · ", labels));
        output.WriteLine(string.Join("\n", item.Summary));

        Assert.Equal(3, labels.Count);
        Assert.Contains(item.Summary, s => s.Contains("7 holes in 3 sizes", StringComparison.Ordinal));

        // Merged to the larger of the two. A hole a micron over is a hole; a hole a micron under is
        // a part that does not fit.
        Assert.Contains("1.00 mm [3 holes]", item.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one that scraps a board: the page has to name, for each stop, the bit the program uses
    /// after it.
    ///
    /// Asserted as an equality between the two rather than against fixed strings, because the
    /// failure mode is *drift* between them — any assertion that pins them separately passes while
    /// they disagree.
    /// </summary>
    [Fact]
    public void TheGuideNamesTheBitTheProgramActuallyUses()
    {
        var item = Drilling();
        var guide = item.Companion!.Content;

        var fromProgram = Labels(item.Content);
        var fromGuide = Regex.Matches(guide, "<td><strong>([^<]*)</strong></td>")
            .Select(m => m.Groups[1].Value)
            .ToList();

        output.WriteLine("program: " + string.Join(" · ", fromProgram));
        output.WriteLine("guide:   " + string.Join(" · ", fromGuide));

        Assert.Equal(fromProgram, fromGuide);
    }

    /// <summary>
    /// And the same property on every committed board, because the corpus is where the shapes come
    /// from that nobody thought to write a fixture for.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.GridStripConnector)]
    [InlineData(RealBoards.ArduinoUno)]
    [InlineData(RealBoards.ArduinoMega)]
    public void TheGuideAndTheProgramAgreeOnEveryBoard(string board)
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

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        // Not every board has a drill file — GridStripConnector puts its holes nowhere this path
        // sees — and a board with nothing to drill is not a failure of the thing being asserted.
        var drilling = plan.Items.Where(i => i.Operation == OperationKind.Drilling).ToList();

        output.WriteLine($"{drilling.Count} drilling program(s)");

        foreach (var item in drilling)
        {
            var fromProgram = Labels(item.Content);
            var fromGuide = Regex.Matches(item.Companion?.Content ?? string.Empty,
                    "<td><strong>([^<]*)</strong></td>")
                .Select(m => m.Groups[1].Value)
                .ToList();

            output.WriteLine($"{item.TargetName}: {string.Join(" · ", fromProgram)}");

            Assert.Equal(fromProgram, fromGuide);
        }
    }

    /// <summary>
    /// The program's own bit labels, with a run of the same bit collapsed — which is what the
    /// program does by not stopping between them.
    /// </summary>
    private static List<string> Labels(string program)
    {
        var found = new List<string>();

        foreach (var line in program.Split('\n').Select(l => l.Trim()))
        {
            if (!line.StartsWith("( Drill ", StringComparison.Ordinal))
            {
                continue;
            }

            var bracket = line.IndexOf('[', StringComparison.Ordinal);
            var name = (bracket > 0 ? line[..bracket] : line.TrimEnd(')', ' '))[1..].Trim();

            if (found.Count == 0 || !string.Equals(found[^1], name, StringComparison.Ordinal))
            {
                found.Add(name);
            }
        }

        return found;
    }
}
