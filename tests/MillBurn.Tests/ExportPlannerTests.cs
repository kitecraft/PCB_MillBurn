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
        Assert.Equal([OutputKind.None], LayerOperations.Available(LayerRole.Unknown));
    }

    /// <summary>
    /// Soldermask goes either way: burned as a stencil, or milled as relief. Its openings are by
    /// definition everywhere the mask is not meant to be, which is a superset of the paste
    /// apertures — vias and test points have mask openings and no paste.
    /// </summary>
    [Fact]
    public void SoldermaskCanBeBurnedOrMilled()
    {
        Assert.Contains(OutputKind.Svg, LayerOperations.Available(LayerRole.TopMask));
        Assert.Contains(OutputKind.Gcode, LayerOperations.Available(LayerRole.TopMask));

        Assert.Equal(OperationKind.MaskOpen, LayerOperations.For(LayerRole.TopMask, OutputKind.Svg));
        Assert.Equal(OperationKind.Pocket, LayerOperations.For(LayerRole.BottomMask, OutputKind.Gcode));
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

    /// <summary>
    /// Both sides of the copper get the same answer.
    ///
    /// They did not: top copper defaulted to G-code and bottom copper to nothing, so a double-sided
    /// board arrived with one side silently not exporting. Nothing about that was a decision — and
    /// it costs nothing on a single-sided board, because a layer with nothing to cut is skipped.
    /// </summary>
    [Fact]
    public void BothSidesOfTheCopperAgree()
    {
        Assert.Equal(
            LayerOperations.DefaultFor(LayerRole.TopCopper),
            LayerOperations.DefaultFor(LayerRole.BottomCopper));
    }

    /// <summary>A cutter cannot reach a layer inside the board, whatever the settings say.</summary>
    [Fact]
    public void InnerCopperIsNeverExported()
    {
        Assert.Equal(OutputKind.None, LayerOperations.DefaultFor(LayerRole.InnerCopper));

        foreach (var preset in (ImportDefaults[])
            [ImportDefaults.Milling, ImportDefaults.LaserEtching, ImportDefaults.Nothing])
        {
            Assert.Equal(OutputKind.None, preset.For(LayerRole.InnerCopper));
        }
    }

    /// <summary>The defaults are the operator's, so a different set produces a different plan.</summary>
    [Fact]
    public void TheImportDefaultsDecideWhatALayerBecomes()
    {
        Assert.Equal(
            OutputKind.Svg,
            LayerOperations.DefaultFor(LayerRole.TopCopper, ImportDefaults.LaserEtching));

        Assert.Equal(
            OutputKind.None,
            LayerOperations.DefaultFor(LayerRole.TopCopper, ImportDefaults.Nothing));

        // The holes and the profile are still milled for a laser job: what changes is the copper.
        Assert.Equal(
            OutputKind.Gcode,
            LayerOperations.DefaultFor(LayerRole.Outline, ImportDefaults.LaserEtching));
    }

    // ------------------------------------------------------------------ the plan

    [Fact]
    public void EachExportedLayerGetsItsOwnFile()
    {
        var board = Board();
        var plan = Plan(board, Defaults(board));

        // Both coppers, both drill files and the profile.
        Assert.Equal(5, plan.Count);
        Assert.Equal(plan.Count, plan.Items.Select(i => i.TargetName).Distinct(StringComparer.Ordinal).Count());

        Assert.Contains(plan.Items, i => i.TargetName == "PogoTest1-B_Cu.nc");
        Assert.Contains(plan.Items, i => i.TargetName == "PogoTest1-F_Cu.nc");
        Assert.Contains(plan.Items, i => i.TargetName == "PogoTest1-PTH.nc");
        Assert.Contains(plan.Items, i => i.TargetName == "PogoTest1-NPTH.nc");
        Assert.Contains(plan.Items, i => i.TargetName == "PogoTest1-Edge_Cuts.nc");
    }

    /// <summary>
    /// The output keeps the layer's own stem, so it sorts next to the file it came from, and the
    /// extension says which machine wants it.
    ///
    /// No operation tag in the middle: a layer produces one output, so there is nothing for a tag
    /// to disambiguate, and it only made the names harder to read.
    /// </summary>
    [Fact]
    public void FilenamesKeepTheLayerStemAndTheExtensionSaysTheRest()
    {
        Assert.Equal(
            "PogoTest1-F_Cu.nc",
            ExportPlanner.TargetNameFor("PogoTest1-F_Cu.gbr", OperationKind.Isolation, OutputKind.Gcode));

        Assert.Equal(
            "PogoTest1-F_Silkscreen.svg",
            ExportPlanner.TargetNameFor("PogoTest1-F_Silkscreen.gbr", OperationKind.Engrave, OutputKind.Svg));

        // The same layer sent to both machines still gives two distinct names.
        Assert.NotEqual(
            ExportPlanner.TargetNameFor("B-F_Cu.gbr", OperationKind.Isolation, OutputKind.Gcode),
            ExportPlanner.TargetNameFor("B-F_Cu.gbr", OperationKind.MaskOpen, OutputKind.Svg));
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
        Assert.Equal(6, Plan(board, settings).Count);
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

        var isolation = plan.Items.Single(i => i.LayerFileName == "PogoTest1-F_Cu.gbr");
        Assert.Contains(isolation.Summary, s => s.Contains("0.127 mm isolated", StringComparison.Ordinal));

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

        Assert.Contains(mask.Warnings, w => w.Contains("openings, not its material", StringComparison.Ordinal));
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
        Assert.Contains(item.Summary, s => s.Contains("Mirrored", StringComparison.Ordinal));
    }

    /// <summary>Top-side files are untouched: there is nothing to flip.</summary>
    [Fact]
    public void ATopSideProgramIsNotMirroredAndSaysNothingAboutFlipping()
    {
        var board = Board();
        var top = Plan(board, Defaults(board)).Items.Single(i => i.LayerFileName == "PogoTest1-F_Cu.gbr");

        Assert.DoesNotContain("BOTTOM SIDE", top.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(top.Warnings, w => w.Contains("flipped", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ overriding the flip

    private static ExportItem Item(Board board, string file, Func<LayerOutputSettings, LayerOutputSettings> change)
    {
        var settings = Defaults(board);
        settings[file] = change(settings[file]);
        return Plan(board, settings).Items.Single(i => i.LayerFileName == file);
    }

    /// <summary>
    /// The default is right for the usual workflow and wrong for several real ones — burning a mask
    /// onto a transparency that will be laid face-down wants the opposite of engraving the same
    /// layer directly — so it is a default rather than a rule.
    /// </summary>
    [Fact]
    public void TheFlipCanBeTurnedOffOnABottomLayer()
    {
        var board = Board();
        var item = Item(board, "PogoTest1-B_Cu.gbr", s => s with
        {
            Output = OutputKind.Gcode,
            Mirrored = false,
        });

        Assert.DoesNotContain("BOTTOM SIDE", item.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(item.Summary, s => s.Contains("Mirrored", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFlipCanBeTurnedOnForATopLayer()
    {
        var board = Board();
        var plain = Item(board, "PogoTest1-F_Cu.gbr", s => s);
        var flipped = Item(board, "PogoTest1-F_Cu.gbr", s => s with { Mirrored = true });

        var widthMm = Nm.ToMillimetres(board.Bounds.Width);
        var expected = CutXValues(plain.Content).Select(x => widthMm - x).Order().ToList();
        var actual = CutXValues(flipped.Content).Order().ToList();

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i], 3);
        }
    }

    /// <summary>
    /// Both overrides are legitimate and both are dangerous, and neither is visible in the file
    /// that results — so the warning appears exactly when someone has left the default.
    /// </summary>
    [Fact]
    public void DepartingFromTheDefaultIsCalledOutInBothDirections()
    {
        var board = Board();

        Assert.Contains(
            Item(board, "PogoTest1-B_Cu.gbr", s => s with { Output = OutputKind.Gcode, Mirrored = false }).Warnings,
            w => w.Contains("come out reversed", StringComparison.Ordinal));

        Assert.Contains(
            Item(board, "PogoTest1-F_Cu.gbr", s => s with { Mirrored = true }).Warnings,
            w => w.Contains("only fit if the stock is flipped", StringComparison.Ordinal));

        // And says nothing at all when the setting is the one the layer's side implies.
        Assert.DoesNotContain(
            Item(board, "PogoTest1-F_Cu.gbr", s => s).Warnings,
            w => w.Contains("mirror", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// SVG needs the flip as much as G-code does: a bottom silkscreen burned onto a board that has
    /// been turned over is reversed for exactly the same reason, and getting one output right while
    /// the other is wrong would be worse than getting both wrong.
    ///
    /// Checked against the page rather than against the implementation. The page is the board plus
    /// a uniform margin, so it is centred on the mirror axis — which means a true reflection sends
    /// the drawing's left edge exactly as far from one side of the page as its right edge was from
    /// the other. Nothing here knows how the transform is written.
    /// </summary>
    [Fact]
    public void AMirroredSvgIsReflectedAboutTheCentreOfTheSharedPage()
    {
        var board = Board();

        var plain = Item(board, "PogoTest1-B_Silkscreen.gbr", s => s with
        {
            Output = OutputKind.Svg,
            Mirrored = false,
        });

        var flipped = Item(board, "PogoTest1-B_Silkscreen.gbr", s => s with { Output = OutputKind.Svg });

        Assert.Contains(flipped.Summary, s => s.Contains("Mirrored", StringComparison.Ordinal));

        var box = XDocument.Parse(plain.Content).Root!.Attribute("viewBox")!.Value;
        Assert.Equal(box, XDocument.Parse(flipped.Content).Root!.Attribute("viewBox")!.Value, StringComparer.Ordinal);

        var pageWidth = double.Parse(
            box.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture);

        var before = PathXValues(plain.Content);
        var after = PathXValues(flipped.Content);

        Assert.NotEmpty(before);
        Assert.Equal(before.Count, after.Count);

        // The silkscreen does not span the board, so a reflection has to move it.
        Assert.NotEqual(before.Min(), after.Min(), 2);

        Assert.Equal(pageWidth, after.Min() + before.Max(), 2);
        Assert.Equal(pageWidth, after.Max() + before.Min(), 2);
    }

    /// <summary>
    /// X values from the unambiguous path commands only.
    ///
    /// <c>M</c> and <c>L</c> take plain x,y pairs; an <c>A</c> puts five other numbers in front of
    /// its endpoint, so taking every other number across a whole path would silently read radii as
    /// coordinates.
    /// </summary>
    private static List<double> PathXValues(string svg) =>
    [
        .. XDocument.Parse(svg)
            .Descendants()
            .Select(e => e.Attribute("d")?.Value)
            .Where(d => d is not null)
            .SelectMany(d => System.Text.RegularExpressions.Regex.Matches(
                d!, @"[ML]\s*(-?\d+(?:\.\d+)?)"))
            .Select(m => double.Parse(
                m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)),
    ];

    private static List<double> CutXValues(string program) =>
    [
        .. program.Split('\n')
            .Where(l => l.StartsWith("G1 X", StringComparison.Ordinal))
            .Select(l => double.Parse(
                l.Split(' ')[1][1..], System.Globalization.CultureInfo.InvariantCulture)),
    ];

    // ------------------------------------------------------------------ the negative

    /// <summary>
    /// Two opposite jobs use the same shapes. Burning a soldermask stencil wants the openings;
    /// etching a painted board wants everything the acid should reach, which is the complement of
    /// the copper. Which one is the target is a fact about the process, not about the file.
    /// </summary>
    [Fact]
    public void AnInvertedLayerIsTheComplementInsideTheBoardEdge()
    {
        var board = Board();

        static double Area(string svg) => System.Xml.Linq.XDocument.Parse(svg)
            .Descendants().Count(e => e.Attribute("d") is not null);

        var settings = Defaults(board);
        settings["PogoTest1-F_Cu.gbr"] = settings["PogoTest1-F_Cu.gbr"] with { Output = OutputKind.Svg };

        var plain = Plan(board, settings).Items.Single(i => i.LayerFileName == "PogoTest1-F_Cu.gbr");

        settings["PogoTest1-F_Cu.gbr"] = settings["PogoTest1-F_Cu.gbr"] with { Invert = true };
        var inverted = Plan(board, settings).Items.Single(i => i.LayerFileName == "PogoTest1-F_Cu.gbr");

        Assert.NotEqual(plain.Content, inverted.Content, StringComparer.Ordinal);
        Assert.Contains(inverted.Summary, s => s.Contains("Inverted", StringComparison.Ordinal));
        Assert.DoesNotContain(plain.Summary, s => s.Contains("Inverted", StringComparison.Ordinal));

        // Both are drawings of the same board, so they share the page.
        Assert.Equal(
            System.Xml.Linq.XDocument.Parse(plain.Content).Root!.Attribute("viewBox")!.Value,
            System.Xml.Linq.XDocument.Parse(inverted.Content).Root!.Attribute("viewBox")!.Value,
            StringComparer.Ordinal);

        Assert.True(Area(inverted.Content) > 0);
    }

    /// <summary>
    /// Inverting is a drawing operation. A toolpath has nothing to be the complement of, so the
    /// setting is ignored rather than quietly producing a program that cuts the whole board away.
    /// </summary>
    [Fact]
    public void InvertingHasNoEffectOnGcode()
    {
        var board = Board();
        var settings = Defaults(board);

        var plain = Plan(board, settings).Items.Single(i => i.LayerFileName == "PogoTest1-F_Cu.gbr");

        settings["PogoTest1-F_Cu.gbr"] = settings["PogoTest1-F_Cu.gbr"] with { Invert = true };
        var inverted = Plan(board, settings).Items.Single(i => i.LayerFileName == "PogoTest1-F_Cu.gbr");

        Assert.Equal(plain.Content, inverted.Content, StringComparer.Ordinal);
    }

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
