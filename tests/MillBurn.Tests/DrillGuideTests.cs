using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The files a drilling layer is written as, and the page that explains how to run them.
///
/// Its whole value is naming the right bit in the right order, so that is what the tests are about.
/// A guide that names the wrong bit is worse than no guide: somebody will follow it, and the board
/// gets a 1.7 mm hole where a 1.0 mm one belonged.
///
/// A layer that needs several bits is **one file per bit**. It used to be one file that stopped
/// between bits with <c>M0</c>, which leaves GRBL on hold — and a controller on hold will not jog or
/// probe, so the page's "change the bit, then resume" could not be done at the machine.
/// </summary>
public sealed class DrillGuideTests(ITestOutputHelper output)
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

    /// <summary>One layer's drilling files, in the order they run. PogoTest1's plated holes need two bits.</summary>
    private static List<ExportItem> Files(string board, LayerRole role = LayerRole.PlatedDrill) =>
        [.. Plan(board).Items.Where(i => i.Operation == OperationKind.Drilling && i.Role == role)];

    private static List<GuideProgram> Programs(IEnumerable<ExportItem> files) =>
        [.. files.Select(f => new GuideProgram(f.TargetName, f.Content))];

    // ------------------------------------------------------------------ the files

    /// <summary>One file per bit, named in the order they run, and none of them stops for a change.</summary>
    [Fact]
    public void EachBitIsItsOwnFileAndNoFileStops()
    {
        var files = Files(RealBoards.PogoTest1);

        foreach (var file in files)
        {
            output.WriteLine($"{file.TargetName} · {file.Bit}");
        }

        Assert.Equal(2, files.Count);
        Assert.EndsWith(".bit1-1.70mm.nc", files[0].TargetName, StringComparison.Ordinal);
        Assert.EndsWith(".bit2-1.00mm.nc", files[1].TargetName, StringComparison.Ordinal);

        Assert.All(files, f => Assert.DoesNotContain(f.Content.Split('\n'), l => l.Trim() == "M0"));
    }

    /// <summary>
    /// Each file says which bit it is for and which file comes next, at the top — the part a sender
    /// shows when the file is opened, and the thing to check before pressing start.
    /// </summary>
    [Fact]
    public void EachFileSaysWhichBitAndWhatComesNext()
    {
        var files = Files(RealBoards.PogoTest1);

        Assert.Contains("Bit 1 of 2: fit the 1.70 mm drill", files[0].Content, StringComparison.Ordinal);
        Assert.Contains("Next: " + files[1].TargetName, files[0].Content, StringComparison.Ordinal);

        Assert.Contains("Bit 2 of 2: fit the 1.00 mm drill", files[1].Content, StringComparison.Ordinal);
        Assert.Contains("This is the last of them.", files[1].Content, StringComparison.Ordinal);

        Assert.Equal("Bit 1 of 2 · 1.70 mm drill", files[0].Bit);
        Assert.Equal("Bit 2 of 2 · 1.00 mm drill", files[1].Bit);
    }

    /// <summary>A layer with one bit is the one file it always was, under the same name, with no bit label.</summary>
    [Fact]
    public void ALayerWithOneBitIsStillOneFile()
    {
        var file = Assert.Single(Files(RealBoards.PogoTest1, LayerRole.NonPlatedDrill));

        Assert.Equal("PogoTest1-NPTH.nc", file.TargetName);
        Assert.Null(file.Bit);
        Assert.DoesNotContain("Bit 1 of", file.Content, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the sequence

    /// <summary>
    /// The bits, named and in order, each against the file it is in.
    ///
    /// The bit names were wrong first time: the emitter writes a toolpath's label *before* the
    /// tool-change sequence that precedes it, so splitting a program at its stops left every label
    /// attached to the section before the one it names.
    /// </summary>
    [Fact]
    public void EveryBitIsNamedAndTheyAreInOrder()
    {
        var files = Files(RealBoards.PogoTest1);

        Assert.NotNull(files[0].Companion);

        var (_, report) = DrillGuide.Build(Programs(files), Context(files));

        output.WriteLine(string.Join("\n", report.Steps.Select(s => $"{s.Order}. {s.Bit} × {s.Holes} in {s.Program}")));

        Assert.Equal(2, report.Steps.Count);
        Assert.Equal("Drill 1.70 mm", report.Steps[0].Bit);
        Assert.Equal("Drill 1.00 mm", report.Steps[1].Bit);
        Assert.Equal(files.Select(f => f.TargetName), report.Steps.Select(s => s.Program));
        Assert.DoesNotContain(report.Steps, s => s.Bit.Contains("already in the spindle", StringComparison.Ordinal));
    }

    /// <summary>Biggest first, so the guide suggests the right thing.</summary>
    [Fact]
    public void TheOrderIsBiggestFirst()
    {
        var files = Files(RealBoards.PogoTest1);
        var (_, report) = DrillGuide.Build(Programs(files), Context(files));

        var diameters = report.Steps
            .Select(s => double.Parse(s.Bit.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(diameters.OrderByDescending(d => d), diameters);
    }

    /// <summary>
    /// The hole counts have to add up to the files', or the page is describing a different run.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.PogoTest1AllLayers)]
    public void TheHoleCountsAccountForEveryHole(string board)
    {
        var layers = Plan(board).Items
            .Where(i => i.Operation == OperationKind.Drilling)
            .GroupBy(i => i.LayerFileName);

        foreach (var layer in layers)
        {
            var files = layer.ToList();
            var (_, report) = DrillGuide.Build(Programs(files), Context(files));

            var drilled = files.Sum(f => GcodeParser.Parse(f.Content).Moves
                .Where(m => !m.IsRapid && m.IsVertical && m.ToZNm < 0)
                .Select(m => m.From)
                .Distinct()
                .Count());

            output.WriteLine($"{layer.Key}: {report.Holes} across {report.Steps.Count} bits in {files.Count} files");
            Assert.Equal(drilled, report.Holes);
        }
    }

    /// <summary>The line numbers have to land on the right file's program, so following along works.</summary>
    [Fact]
    public void EachSectionPointsAtARealLine()
    {
        var files = Files(RealBoards.PogoTest1);
        var (_, report) = DrillGuide.Build(Programs(files), Context(files));

        // The line a step points at is the line that *names* its bit, in the file it is in.
        foreach (var step in report.Steps)
        {
            var lines = files.Single(f => f.TargetName == step.Program).Content.Split('\n');

            Assert.InRange(step.Line, 1, lines.Length);
            Assert.Contains(step.Bit, lines[step.Line - 1], StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------ the page itself

    [Fact]
    public void ASingleBitJobDoesNotTalkAboutChangingBits()
    {
        var file = Assert.Single(Files(RealBoards.PogoTest1, LayerRole.NonPlatedDrill));
        var (html, report) = DrillGuide.Build(Programs([file]), Context([file]));

        Assert.Single(report.Steps);
        Assert.Equal(0, report.Changes);
        Assert.DoesNotContain("stops on its own", html, StringComparison.Ordinal);
        Assert.DoesNotContain("One file per bit", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page for a multi-bit layer: one file per bit in the suggested order, Z set before each, and
    /// nothing left over from when the program stopped and asked to be resumed.
    /// </summary>
    [Fact]
    public void AMultiBitPageListsTheFilesAndSaysToSetZEachTime()
    {
        var files = Files(RealBoards.PogoTest1);
        var html = files[0].Companion!.Content;

        Assert.Contains("Suggested order", html, StringComparison.Ordinal);
        Assert.Contains("One file per bit", html, StringComparison.Ordinal);
        Assert.Contains("Z is the one you have to set again", html, StringComparison.Ordinal);
        Assert.Contains("same spot each time", html, StringComparison.Ordinal);
        Assert.Contains("sacrificial board", html, StringComparison.Ordinal);
        Assert.All(files, f => Assert.Contains(f.TargetName, html, StringComparison.Ordinal));

        Assert.DoesNotContain("stops on its own", html, StringComparison.Ordinal);
        Assert.DoesNotContain("resume in", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page must not tell the operator two different things about re-zeroing.
    ///
    /// It did. "It does not need re-zeroing" — meaning the two axes that had not moved — sat
    /// directly above "re-zero Z after every bit change". The reassurance has to name the axes it is
    /// about.
    /// </summary>
    [Fact]
    public void ItDoesNotContradictItselfAboutRezeroing()
    {
        var html = Files(RealBoards.PogoTest1)[0].Companion!.Content;

        Assert.DoesNotContain("does not need re-zeroing", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no re-zeroing", html, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("X and Y keep their zero", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// One file, openable anywhere. It is written into whatever folder the operator picked, next to
    /// a machine that may never have been online.
    /// </summary>
    [Fact]
    public void ThePageIsSelfContained()
    {
        var html = Files(RealBoards.PogoTest1)[0].Companion!.Content;

        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<link", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLayerAndItsFilesAreNamedOnIt()
    {
        var files = Files(RealBoards.PogoTest1);
        var html = files[0].Companion!.Content;

        Assert.Contains("Plated holes", html, StringComparison.Ordinal);
        Assert.Contains("2 files", html, StringComparison.Ordinal);
        Assert.All(files, f => Assert.Contains(f.TargetName, html, StringComparison.Ordinal));
    }

    /// <summary>A layer label with an ampersand in it must not produce broken markup.</summary>
    [Fact]
    public void TextFromTheBoardIsEscaped()
    {
        var (html, _) = DrillGuide.Build(
            "( Drill 1.00 mm [1 holes] )\nM3 S1000\nG0 X1.000 Y1.000\nG1 Z-1.000 F60\nM30",
            new DrillGuideContext
            {
                BoardName = "Bell & Howell <test>",
                LayerLabel = "Plated holes",
                ProgramName = "a.nc",
            });

        Assert.Contains("Bell &amp; Howell &lt;test&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<test>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedPositionsAreExplained()
    {
        var (html, _) = DrillGuide.Build(
            "( Drill 1.00 mm [1 holes] )\nM3 S1000\nG0 X1.000 Y1.000\nG1 Z-1.000 F60\nM30",
            new DrillGuideContext
            {
                BoardName = "b",
                LayerLabel = "Plated holes",
                ProgramName = "a.nc",
                RepeatedPositions = 8,
            });

        Assert.Contains("8 positions", html, StringComparison.Ordinal);
        Assert.DoesNotContain("position(s)", html, StringComparison.Ordinal);
    }

    /// <summary>A program with no holes in it gets no page rather than an empty one.</summary>
    [Fact]
    public void NothingToDrillMeansNoCompanion()
    {
        var (_, report) = DrillGuide.Build("G21 G90\nM30", new DrillGuideContext
        {
            BoardName = "b",
            LayerLabel = "l",
            ProgramName = "a.nc",
        });

        Assert.Empty(report.Steps);
    }

    // ------------------------------------------------------------------ wiring

    /// <summary>One page per drilling layer, on its first file, and no page on anything else.</summary>
    [Fact]
    public void EachDrillingLayerGetsOnePage()
    {
        var items = Plan(RealBoards.PogoTest1).Items;

        foreach (var layer in items.Where(i => i.Operation == OperationKind.Drilling).GroupBy(i => i.LayerFileName))
        {
            var files = layer.ToList();

            Assert.NotNull(files[0].Companion);
            Assert.Equal(Path.GetFileNameWithoutExtension(layer.Key) + ".drilling.html", files[0].Companion!.TargetName);
            Assert.All(files.Skip(1), f => Assert.Null(f.Companion));
        }

        Assert.All(items.Where(i => i.Operation != OperationKind.Drilling), i => Assert.Null(i.Companion));
    }

    [Fact]
    public void TurningItOffLeavesNoPage()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
                WriteDrillGuide = false,
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        Assert.All(plan.Items, i => Assert.Null(i.Companion));
    }

    private static DrillGuideContext Context(List<ExportItem> files) => new()
    {
        BoardName = "PogoTest1",
        LayerLabel = files[0].LayerLabel,
        ProgramName = files[0].TargetName,
        BoardThicknessNm = Nm.FromMillimetres(1.6),
        BreakThroughNm = Nm.FromMillimetres(0.3),
    };
}
