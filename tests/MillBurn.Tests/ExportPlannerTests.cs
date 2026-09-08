using System.Xml.Linq;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// One file per layer.
///
/// Not a packaging preference: a job crosses tools and often machines, and a single program holding
/// isolation, three drill sizes and the outline assumes one operator watching one long run.
/// Separate files mean a broken bit costs the drilling rather than the board, and they are the only
/// shape that works when the silkscreen goes to a laser and the outline goes to the mill.
/// </summary>
public sealed class ExportPlannerTests
{
    private static Board Board() => BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

    private static Dictionary<string, LayerOutputSettings> Defaults(Board board) =>
        board.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

    private static ExportPlan Plan(
        Board board, Dictionary<string, LayerOutputSettings> settings, OutputKind? only = null) =>
        ExportPlanner.Plan(board, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), only);

    // ------------------------------------------------------------------ what a layer becomes

    [Theory]
    [InlineData(LayerRole.TopCopper, OutputKind.Gcode, OperationKind.Isolation)]
    [InlineData(LayerRole.TopCopper, OutputKind.Svg, OperationKind.MaskOpen)]
    [InlineData(LayerRole.PlatedDrill, OutputKind.Gcode, OperationKind.Drilling)]
    [InlineData(LayerRole.Outline, OutputKind.Gcode, OperationKind.Outline)]
    [InlineData(LayerRole.Outline, OutputKind.Svg, OperationKind.Vector)]
    [InlineData(LayerRole.TopSilk, OutputKind.Svg, OperationKind.Engrave)]
    [InlineData(LayerRole.TopMask, OutputKind.Svg, OperationKind.MaskOpen)]
    public void ALayerRoleAndOutputDecideTheOperation(
        LayerRole role, OutputKind output, OperationKind expected) =>
        Assert.Equal(expected, LayerOperations.For(role, output));

    /// <summary>
    /// A laser cannot drill, so that pairing is not offered. Offering it and then producing an
    /// empty file would be worse than saying so.
    /// </summary>
    [Fact]
    public void MeaninglessPairingsAreNotOffered()
    {
        Assert.DoesNotContain(OutputKind.Svg, LayerOperations.Available(LayerRole.PlatedDrill));
        Assert.DoesNotContain(OutputKind.Gcode, LayerOperations.Available(LayerRole.TopMask));
        Assert.Equal([OutputKind.None], LayerOperations.Available(LayerRole.Unknown));
    }

    [Fact]
    public void DefaultsCutTheUsualThingsAndLeaveTheRest()
    {
        Assert.Equal(OutputKind.Gcode, LayerOperations.DefaultFor(LayerRole.TopCopper));
        Assert.Equal(OutputKind.Gcode, LayerOperations.DefaultFor(LayerRole.Outline));
        Assert.Equal(OutputKind.Gcode, LayerOperations.DefaultFor(LayerRole.PlatedDrill));
        Assert.Equal(OutputKind.None, LayerOperations.DefaultFor(LayerRole.TopMask));
        Assert.Equal(OutputKind.None, LayerOperations.DefaultFor(LayerRole.BottomSilk));
    }

    // ------------------------------------------------------------------ the plan

    [Fact]
    public void EachExportedLayerGetsItsOwnFile()
    {
        var board = Board();
        var plan = Plan(board, Defaults(board));

        Assert.Equal(4, plan.Count);
        Assert.Equal(plan.Count, plan.Items.Select(i => i.TargetName).Distinct(StringComparer.Ordinal).Count());

        Assert.Contains(plan.Items, i => i.TargetName == "PogoTest1-F_Cu.iso.nc");
        Assert.Contains(plan.Items, i => i.TargetName == "PogoTest1-PTH.drill.nc");
        Assert.Contains(plan.Items, i => i.TargetName == "PogoTest1-NPTH.drill.nc");
        Assert.Contains(plan.Items, i => i.TargetName == "PogoTest1-Edge_Cuts.cutout.nc");
    }

    /// <summary>
    /// The output keeps the layer's own stem so it sorts next to the file it came from, and gains a
    /// tag so it is obvious which files a machine should be fed.
    /// </summary>
    [Fact]
    public void FilenamesKeepTheLayerStemAndSayWhatTheyAre()
    {
        Assert.Equal(
            "PogoTest1-F_Cu.iso.nc",
            ExportPlanner.TargetNameFor("PogoTest1-F_Cu.gbr", OperationKind.Isolation, OutputKind.Gcode));

        Assert.Equal(
            "PogoTest1-F_Silkscreen.engrave.svg",
            ExportPlanner.TargetNameFor("PogoTest1-F_Silkscreen.gbr", OperationKind.Engrave, OutputKind.Svg));
    }

    [Fact]
    public void PlanningWritesNothing()
    {
        var board = Board();
        var before = Directory.GetFiles(RealBoards.Directory(RealBoards.PogoTest1)).Length;

        Plan(board, Defaults(board));

        Assert.Equal(before, Directory.GetFiles(RealBoards.Directory(RealBoards.PogoTest1)).Length);
    }

    [Fact]
    public void TheFilterTakesOnlyOneKind()
    {
        var board = Board();
        var settings = Defaults(board);
        settings["PogoTest1-F_Silkscreen.gbr"] = settings["PogoTest1-F_Silkscreen.gbr"] with
        {
            Output = OutputKind.Svg,
        };

        Assert.All(Plan(board, settings, OutputKind.Svg).Items, i => Assert.Equal(OutputKind.Svg, i.Output));
        Assert.All(Plan(board, settings, OutputKind.Gcode).Items, i => Assert.Equal(OutputKind.Gcode, i.Output));
        Assert.Equal(5, Plan(board, settings).Count);
    }

    /// <summary>A mill job and a laser job out of one board, which is what this exists for.</summary>
    [Fact]
    public void MillAndLaserOutputsComeOutOfOneExport()
    {
        var board = Board();
        var settings = Defaults(board);

        foreach (var name in new[] { "PogoTest1-F_Silkscreen.gbr", "PogoTest1-F_Mask.gbr" })
        {
            settings[name] = settings[name] with { Output = OutputKind.Svg };
        }

        var plan = Plan(board, settings);

        Assert.Contains(plan.Items, i => i.Output == OutputKind.Svg);
        Assert.Contains(plan.Items, i => i.Output == OutputKind.Gcode);
        Assert.All(plan.Items, i => Assert.NotEmpty(i.Content));
    }

    /// <summary>
    /// Every SVG in one export shares a page, so the layers overlay when they are imported. Cropping
    /// each to its own extents is the mistake that puts a second burn out by the difference between
    /// two crops.
    /// </summary>
    [Fact]
    public void EverySvgInOneExportSharesAPage()
    {
        var board = Board();
        var settings = Defaults(board);

        foreach (var name in new[] { "PogoTest1-F_Silkscreen.gbr", "PogoTest1-F_Mask.gbr", "PogoTest1-Edge_Cuts.gbr" })
        {
            settings[name] = settings[name] with { Output = OutputKind.Svg };
        }

        var pages = Plan(board, settings, OutputKind.Svg).Items
            .Select(i => XDocument.Parse(i.Content).Root!.Attribute("viewBox")!.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(3, Plan(board, settings, OutputKind.Svg).Count);
        Assert.Single(pages);
    }

    /// <summary>
    /// Every file is referenced to the board's own corner, so they all share one work zero the
    /// operator can touch off on — and each says so, because they are handed to the machine
    /// separately.
    /// </summary>
    [Fact]
    public void EveryProgramSharesOneWorkZeroAndSaysSo()
    {
        var board = Board();

        foreach (var item in Plan(board, Defaults(board), OutputKind.Gcode).Items)
        {
            Assert.Contains("lower-left corner", item.Content, StringComparison.Ordinal);

            // Nothing lands more than a cutter-radius outside the board.
            foreach (var line in item.Content.Split('\n').Where(l => l.StartsWith("G0 X", StringComparison.Ordinal)))
            {
                var x = double.Parse(
                    line.Split(' ')[1][1..], System.Globalization.CultureInfo.InvariantCulture);
                Assert.InRange(x, -1.0, Nm.ToMillimetres(board.Bounds.Width) + 1.0);
            }
        }
    }

    // ------------------------------------------------------------------ what the plan tells you

    [Fact]
    public void EachFileSaysWhatItWillDo()
    {
        var board = Board();
        var plan = Plan(board, Defaults(board));

        var isolation = plan.Items.Single(i => i.Operation == OperationKind.Isolation);
        Assert.Contains(isolation.Summary, s => s.Contains("0.127 mm wide", StringComparison.Ordinal));

        var drill = plan.Items.First(i => i.Operation == OperationKind.Drilling);
        Assert.Contains(drill.Summary, s => s.Contains("through the back", StringComparison.Ordinal));

        var outline = plan.Items.Single(i => i.Operation == OperationKind.Outline);
        Assert.Contains(outline.Summary, s => s.Contains("outside the profile", StringComparison.Ordinal));
    }

    /// <summary>
    /// The break-through belongs to the layer, because it only means something for the operations
    /// that go all the way through. Isolation is a scratch into the copper and has no such number.
    /// </summary>
    [Fact]
    public void TheBreakThroughDistanceChangesTheDrillDepth()
    {
        var board = Board();

        string Depth(double throughMm)
        {
            var settings = Defaults(board);
            settings["PogoTest1-PTH.drl"] = settings["PogoTest1-PTH.drl"] with
            {
                BreakThroughNm = Nm.FromMillimetres(throughMm),
            };

            return Plan(board, settings, OutputKind.Gcode).Items
                .Single(i => i.LayerFileName == "PogoTest1-PTH.drl")
                .Content;
        }

        Assert.Contains("Z-1.900", Depth(0.3), StringComparison.Ordinal);
        Assert.Contains("Z-2.600", Depth(1.0), StringComparison.Ordinal);
    }

    [Fact]
    public void CuttingTheOutlineWithoutTabsIsCalledOut()
    {
        var board = Board();
        var settings = Defaults(board);
        settings["PogoTest1-Edge_Cuts.gbr"] = settings["PogoTest1-Edge_Cuts.gbr"] with { TabCount = 0 };

        var outline = Plan(board, settings).Items.Single(i => i.Operation == OperationKind.Outline);

        Assert.Contains(outline.Warnings, w => w.Contains("thrown by the cutter", StringComparison.Ordinal));
    }

    [Fact]
    public void ANegativeLayerExportedAsSvgSaysWhatItsShapesAre()
    {
        var board = Board();
        var settings = Defaults(board);
        settings["PogoTest1-F_Mask.gbr"] = settings["PogoTest1-F_Mask.gbr"] with { Output = OutputKind.Svg };

        var mask = Plan(board, settings).Items.Single(i => i.LayerFileName == "PogoTest1-F_Mask.gbr");

        Assert.Contains(mask.Warnings, w => w.Contains("openings, not the material", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the flip

    private static ExportItem BottomCopper(Board board)
    {
        var settings = Defaults(board);
        settings["PogoTest1-B_Cu.gbr"] = settings["PogoTest1-B_Cu.gbr"] with
        {
            Output = OutputKind.Gcode,
        };

        return Plan(board, settings).Items.Single(i => i.LayerFileName == "PogoTest1-B_Cu.gbr");
    }

    /// <summary>
    /// A bottom layer is drawn as seen *through* the board, so cutting its coordinates as they come
    /// produces a mirror image — a program that looks completely correct on screen and scraps the
    /// board. The mirror is baked into the file rather than left to the operator.
    ///
    /// Checked against the same geometry planned as a top-side layer, which is the only comparison
    /// that isolates the flip: everything else about the two programs is identical, so any
    /// difference beyond the reflection is the transform being wrong.
    /// </summary>
    [Fact]
    public void ABottomSideProgramIsExactlyTheMirrorOfTheSameGeometryCutFromTheTop()
    {
        var board = Board();
        var widthMm = Nm.ToMillimetres(board.Bounds.Width);

        var asTop = board with
        {
            Layers = [.. board.Layers.Select(l =>
                l.FileName == "PogoTest1-B_Cu.gbr" ? l with { Role = LayerRole.TopCopper } : l)],
        };

        var settings = Defaults(asTop);
        settings["PogoTest1-B_Cu.gbr"] = settings["PogoTest1-B_Cu.gbr"] with { Output = OutputKind.Gcode };

        var unflipped = CutXValues(
            Plan(asTop, settings).Items.Single(i => i.LayerFileName == "PogoTest1-B_Cu.gbr").Content);
        var flipped = CutXValues(BottomCopper(board).Content);

        Assert.NotEmpty(unflipped);
        Assert.Equal(unflipped.Count, flipped.Count);

        // Sorted, because ordering starts from the origin and the reflection moves which pass is
        // nearest to it. The set of coordinates is what the flip is about.
        var expected = unflipped.Select(x => widthMm - x).Order().ToList();
        var actual = flipped.Order().ToList();

        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i], 3);
        }
    }

    /// <summary>
    /// Same box, so work zero is still the board's lower-left corner — which is the corner the
    /// operator can see and touch off on after the stock has been turned over.
    /// </summary>
    [Fact]
    public void TheFlipKeepsWorkZeroAtTheSameCorner()
    {
        var board = Board();
        var widthMm = Nm.ToMillimetres(board.Bounds.Width);

        foreach (var x in CutXValues(BottomCopper(board).Content))
        {
            Assert.InRange(x, -1.0, widthMm + 1.0);
        }
    }

    /// <summary>
    /// The flip is the one step that cannot be recovered from once the cut has started, and these
    /// files are handed to the machine one at a time.
    /// </summary>
    [Fact]
    public void ABottomSideProgramSaysWhichWayToFlipTheStock()
    {
        var item = BottomCopper(Board());

        Assert.Contains("BOTTOM SIDE", item.Content, StringComparison.Ordinal);
        Assert.Contains("left-to-right", item.Content, StringComparison.Ordinal);
        Assert.Contains(item.Warnings, w => w.Contains("flipped left-to-right", StringComparison.Ordinal));
        Assert.Contains(item.Summary, s => s.Contains("Mirrored for the bottom side", StringComparison.Ordinal));
    }

    /// <summary>Top-side files are untouched: there is nothing to flip.</summary>
    [Fact]
    public void ATopSideProgramIsNotMirroredAndSaysNothingAboutFlipping()
    {
        var board = Board();
        var top = Plan(board, Defaults(board)).Items.Single(i => i.Operation == OperationKind.Isolation);

        Assert.DoesNotContain("BOTTOM SIDE", top.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(top.Warnings, w => w.Contains("flipped", StringComparison.Ordinal));
    }

    private static List<double> CutXValues(string program) =>
    [
        .. program.Split('\n')
            .Where(l => l.StartsWith("G1 X", StringComparison.Ordinal))
            .Select(l => double.Parse(
                l.Split(' ')[1][1..], System.Globalization.CultureInfo.InvariantCulture)),
    ];

    [Fact]
    public void NothingIsExportedWhenEveryLayerIsOff()
    {
        var board = Board();
        var settings = board.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = OutputKind.None },
            StringComparer.Ordinal);

        Assert.Equal(0, Plan(board, settings).Count);
    }
}
