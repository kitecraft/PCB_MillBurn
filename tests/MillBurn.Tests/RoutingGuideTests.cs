using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The page that travels with a routing program.
///
/// The drilling program has had one since Phase 5; the routing program had nothing, which is the
/// worse gap of the two. Drilling is familiar — fit a bit, touch off, go. Routing is the file that
/// looks wrong if nobody has told you what it is doing: the tool descends while it is moving
/// instead of plunging, it goes round a slot rather than down the middle, and some of the features
/// drawn on the board are deliberately missing from it.
///
/// That last one is why the page exists at all. A program cannot describe what is absent from it.
/// </summary>
public sealed class RoutingGuideTests(ITestOutputHelper output)
{
    private static ExportItem Routing(string board, ToolLibrary? library = null)
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
            loaded, settings, library ?? ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        // The first of the layer's routing files: one per cutter, and the page is on the first.
        return plan.Items.First(i => i.TargetName.Contains(".slots.", StringComparison.Ordinal));
    }

    /// <summary>A routing program gets a page, sharing the program's own name so the two sort together.</summary>
    [Fact]
    public void EveryRoutingProgramGetsAPage()
    {
        var item = Routing(RealBoards.ArduinoUno);
        var guide = Assert.IsType<ExportCompanion>(item.Companion);

        output.WriteLine(guide.TargetName + " — " + guide.Description);

        Assert.Equal("Arduino UNO-PTH-drl.slots.html", guide.TargetName);
        Assert.Contains("1 cutter", guide.Description, StringComparison.Ordinal);
        Assert.Contains("4 not cut", guide.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// The cutter to fit, read back out of the program's own comments — the same discipline as
    /// reading the bits out of a drilling program.
    /// </summary>
    [Fact]
    public void ThePageNamesTheCutterAndWhatItMakes()
    {
        var html = Routing(RealBoards.ArduinoUno).Companion!.Content;

        Assert.Contains("1.0 mm end mill", html, StringComparison.Ordinal);
        Assert.Contains("3 slots 1.00 mm wide", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusals are the reason the page exists, and the headline count is the number of missing
    /// *features* rather than the number of lines it took to describe them.
    /// </summary>
    [Fact]
    public void ThePageNamesWhatWillNotBeCut()
    {
        var html = Routing(RealBoards.ArduinoUno).Companion!.Content;

        Assert.Contains("Some features are not in this program", html, StringComparison.Ordinal);
        Assert.Contains("0.60 mm", html, StringComparison.Ordinal);
        Assert.Contains("0.8 mm end mill", html, StringComparison.Ordinal);

        // Four slots, described on one line. The strip says four.
        Assert.Contains("<span>Not cut</span><strong>4</strong>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// With a cutter narrow enough in the library there is nothing to refuse, and the page says
    /// nothing about it — a warning box that appears on every page is one nobody reads.
    /// </summary>
    [Fact]
    public void WithACutterThatFitsThereIsNothingToWarnAbout()
    {
        var library = new ToolLibrary
        {
            Tools =
            [
                .. ToolLibrary.Default.Tools,
                new Tool
                {
                    Name = "0.5 mm end mill",
                    Kind = ToolKind.EndMill,
                    DiameterNm = Nm.FromMillimetres(0.5),
                    StepdownNm = Nm.FromMillimetres(0.2),
                },
            ],
        };

        var item = Routing(RealBoards.ArduinoUno, library);
        var guide = item.Companion!;

        output.WriteLine(guide.Description);

        Assert.DoesNotContain("not cut", guide.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Some features are not in this program", guide.Content, StringComparison.Ordinal);

        // Two cutters now — the 1.0 mm for the wide slots, the 0.5 mm for the narrow ones — so the
        // page has to carry the tool change and the re-zero that comes with it.
        Assert.Contains("2 cutters", guide.Description, StringComparison.Ordinal);
        Assert.Contains("Z is the one you have to set again", guide.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two things about this file that surprise somebody who has only run drilling programs.
    /// </summary>
    [Fact]
    public void ThePageExplainsWhyItLooksDifferent()
    {
        var html = Routing(RealBoards.ArduinoUno).Companion!.Content;

        Assert.Contains("descends while it is moving", html, StringComparison.Ordinal);
        Assert.Contains("after drilling and before the outline", html, StringComparison.Ordinal);
    }

    /// <summary>Self-contained: one file, no network, no stylesheet beside it.</summary>
    [Fact]
    public void ThePageStandsAlone()
    {
        var html = Routing(RealBoards.ArduinoUno).Companion!.Content;

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<link", html, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ reading the program

    /// <summary>
    /// The layout the emitter actually writes: each feature's heading comes *before* its tool
    /// change, so the stop sits inside the feature it is for.
    /// </summary>
    private const string TwoCuttersThreeFeatures = """
        ( PCB_MillBurn - Board-PTH-drl — Routed slots )
        G21 G90 G94
        G0 Z2.000

        ( Plated holes — 1.00 mm )
        ( 3 slots 1.00 mm wide, cut with the 1.0 mm end mill. )
        M3 S12000
        G0 X1 Y1
        G1 Z-1.100 F60
        G1 X3 Y1 F750
        G0 Z2.000

        ( Plated holes — 2.20 mm )
        ( 1 hole 2.20 mm across, cut with the 1.2 mm end mill. )
        G0 Z2.000
        M5
        ( Change tool to 1.2 mm end mill [1.200 mm] )
        M0
        M3 S10000
        G0 X10 Y10
        G1 Z-1.100 F60
        G1 X10.5 Y10 F600
        G0 Z2.000

        ( Plated holes — 2.50 mm )
        ( 1 hole 2.50 mm across, cut with the 1.2 mm end mill. )
        G0 X20 Y10
        G1 Z-1.100 F60
        G1 X20.5 Y10 F600
        G0 Z2.000
        M5
        M2
        """;

    private static RoutingGuideContext Context => new()
    {
        BoardName = "Board",
        LayerLabel = "Plated holes",
        ProgramName = "Board-PTH-drl.slots.nc",
    };

    /// <summary>
    /// Every feature under a cutter is listed, and each one under the cutter that cuts it.
    ///
    /// Splitting at the stop alone put the 2.20 mm hole's heading under the 1.0 mm end mill, and
    /// taking one comment per step dropped the rest — the page named one hole for a cutter that
    /// made two, and got its size wrong.
    /// </summary>
    [Fact]
    public void EachCutterListsEveryFeatureItMakes()
    {
        var (_, report) = RoutingGuide.Build(TwoCuttersThreeFeatures, Context);

        foreach (var step in report.Steps)
        {
            output.WriteLine($"{step.Order}. {step.Cutter} from line {step.Line}: {string.Join(" / ", step.Makes)}");
        }

        Assert.Equal(2, report.Steps.Count);
        Assert.Equal(1, report.Changes);

        Assert.Equal("1.0 mm end mill", report.Steps[0].Cutter);
        Assert.Equal(["3 slots 1.00 mm wide"], report.Steps[0].Makes);

        Assert.Equal("1.2 mm end mill", report.Steps[1].Cutter);
        Assert.Equal(["1 hole 2.20 mm across", "1 hole 2.50 mm across"], report.Steps[1].Makes);

        // The second step starts at its first feature's heading, not after the stop.
        Assert.Equal(13, report.Steps[1].Line);
    }

    /// <summary>
    /// On the board that found it: every feature the program's own comments name is on the page.
    ///
    /// Derived from the program rather than counted by hand, so the test is about the page agreeing
    /// with the file — which is the whole claim — and not about how many holes this board has today.
    /// </summary>
    [Fact]
    public void OnTheTestBoardThePageNamesEveryFeatureInTheProgram()
    {
        var endMill = new Tool
        {
            Id = Guid.NewGuid(),
            Name = "1.2 mm end mill",
            Kind = ToolKind.EndMill,
            DiameterNm = Nm.FromMillimetres(1.2),
            StepdownNm = Nm.FromMillimetres(0.6),
        };

        var library = new ToolLibrary { Tools = [.. ToolLibrary.Default.Tools, endMill] };
        var job = new JobOptions { MillLargeHoles = true, MillDrillToolId = endMill.Id };

        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, library, Nm.FromMillimetres(1.6), OutputKind.Gcode, job: job);

        var routed = plan.Items
            .Where(i => i.TargetName.Contains(".slots.", StringComparison.Ordinal))
            .GroupBy(i => i.LayerFileName)
            .ToList();

        Assert.NotEmpty(routed);

        var listedTogether = false;
        var split = false;

        foreach (var layer in routed)
        {
            var files = layer.ToList();

            // One page per layer, on its first file, naming every file the layer was written as.
            var page = Assert.IsType<ExportCompanion>(files[0].Companion);
            Assert.All(files.Skip(1), f => Assert.Null(f.Companion));
            Assert.Equal(Path.GetFileNameWithoutExtension(layer.Key) + ".slots.html", page.TargetName);

            var features = files.SelectMany(f => f.Content.Split('\n'))
                .Select(l => l.Trim())
                .Where(l => l.StartsWith('(') && l.Contains(", cut with the ", StringComparison.Ordinal))
                .Select(l => l[1..l.IndexOf(", cut with the ", StringComparison.Ordinal)].Trim())
                .ToList();

            output.WriteLine($"{layer.Key}: {files.Count} file(s), {features.Count} features — {page.Description}");

            Assert.NotEmpty(features);
            Assert.All(features, f => Assert.Contains(f, page.Content, StringComparison.Ordinal));
            Assert.All(files, f => Assert.Contains(f.TargetName, page.Content, StringComparison.Ordinal));
            Assert.All(files, f => Assert.DoesNotContain(f.Content.Split('\n'), l => l.Trim() == "M0"));

            listedTogether |= page.Content.Contains("<br>", StringComparison.Ordinal);
            split |= files.Count > 1;
        }

        Assert.True(split, "some layer on this board needs more than one cutter, and so more than one file");

        Assert.True(listedTogether, "some cutter on this board makes more than one feature");
    }

    /// <summary>
    /// With a chosen cutter that fits every slot and hole, each layer's routing is one file, all of it
    /// cut with that cutter.
    ///
    /// Found on this board in the workshop: a 0.8 mm end mill chosen, and the plated layer still came
    /// out as <c>slots.bit1-1.00mm.nc</c> for the 1 mm slots and <c>slots.bit2-0.80mm.nc</c> for the
    /// holes — a tool change the chosen cutter did not need.
    /// </summary>
    [Fact]
    public void OnTheTestBoardAChosenCutterThatFitsEverythingIsOneFilePerLayer()
    {
        var endMill = new Tool
        {
            Id = Guid.NewGuid(),
            Name = "0.8 mm end mill",
            Kind = ToolKind.EndMill,
            DiameterNm = Nm.FromMillimetres(0.8),
            StepdownNm = Nm.FromMillimetres(0.5),
        };

        var library = new ToolLibrary { Tools = [.. ToolLibrary.Default.Tools, endMill] };
        var job = new JobOptions { MillLargeHoles = true, MillDrillToolId = endMill.Id };

        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, library, Nm.FromMillimetres(1.6), OutputKind.Gcode, job: job);

        var routed = plan.Items
            .Where(i => i.TargetName.Contains(".slots.", StringComparison.Ordinal))
            .GroupBy(i => i.LayerFileName)
            .ToList();

        Assert.NotEmpty(routed);

        foreach (var layer in routed)
        {
            var file = Assert.Single(layer);

            var cutters = file.Content.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith('(') && l.Contains(", cut with the ", StringComparison.Ordinal))
                .Select(l => l[(l.IndexOf(", cut with the ", StringComparison.Ordinal) + ", cut with the ".Length)..].TrimEnd(')', ' ', '.'))
                .Distinct()
                .ToList();

            output.WriteLine($"{file.TargetName}: {string.Join(", ", cutters)} — {file.Companion?.Description}");

            Assert.Equal(Path.GetFileNameWithoutExtension(layer.Key) + ".slots.nc", file.TargetName);
            Assert.Equal([endMill.Name], cutters);
            Assert.DoesNotContain(file.Warnings, w => w.Contains("are NOT cut", StringComparison.Ordinal));
        }

        // The plated layer is the one that has slots as well as milled holes.
        Assert.Contains(routed, l => l.Single().Content.Contains("slot", StringComparison.OrdinalIgnoreCase)
            && l.Single().Content.Contains("hole", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// On a blank the pages say the blank's corner, because every program beside them uses it.
    /// Zeroing on the board's corner, as the pages used to say, puts every hole a border's width out.
    /// </summary>
    [Fact]
    public void OnABlankThePagesPutWorkZeroOnTheBlank()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoUno));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);

        string[] Pages(JobOptions job) =>
        [
            .. ExportPlanner.Plan(loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode, job: job)
                .Items.Where(i => i.Companion is not null)
                .Select(i => i.Companion!.Content),
        ];

        var plain = Pages(new JobOptions());
        var blank = Pages(new JobOptions { Blank = new BlankOptions { Enabled = true } });

        Assert.NotEmpty(plain);
        Assert.Equal(plain.Length, blank.Length);

        Assert.All(plain, p => Assert.Contains("lower-left corner of the board", p, StringComparison.Ordinal));
        Assert.All(blank, p => Assert.Contains("lower-left corner of the stock", p, StringComparison.Ordinal));
    }
}
