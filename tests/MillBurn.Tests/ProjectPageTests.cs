using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// One page describing the whole export.
///
/// Requested from the workshop after an SVG landed a border's width out. Every fact on it is said
/// somewhere already — in the export window, in a program's comments, on a drilling page — and each
/// of those is a different place, none of which is open when somebody opens the folder next week.
/// </summary>
public sealed class ProjectPageTests(ITestOutputHelper output)
{
    private static ExportPlan Plan(BlankOptions? blank = null, bool masksAsSvg = true)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1AllLayers));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = masksAsSvg && l.Role is LayerRole.TopMask or LayerRole.BottomMask
                    ? OutputKind.Svg
                    : LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        return ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6),
            job: new JobOptions { Blank = blank ?? new BlankOptions() });
    }

    [Fact]
    public void EveryExportGetsOne()
    {
        var page = Plan().Page;

        Assert.NotNull(page);
        output.WriteLine(page.TargetName + " — " + page.Description);

        Assert.EndsWith(".project.html", page.TargetName, StringComparison.Ordinal);
        Assert.StartsWith("<!DOCTYPE html>", page.Content, StringComparison.Ordinal);
    }

    /// <summary>The order is the one thing a folder of files cannot show, and it is physical.</summary>
    [Fact]
    public void ItSaysWhatToRunFirst()
    {
        var html = Plan(new BlankOptions { Enabled = true }).Page!.Content;

        var blank = html.IndexOf("Cut the stock", StringComparison.Ordinal);
        var isolate = html.IndexOf("Isolate the copper", StringComparison.Ordinal);
        var outline = html.IndexOf("Cut the board out", StringComparison.Ordinal);

        Assert.True(blank >= 0 && isolate > blank && outline > isolate,
            $"blank {blank}, isolate {isolate}, outline {outline}");
    }

    /// <summary>
    /// Suggested, and said so. Most of the order is physics, but not all of it — and somebody
    /// meeting this for the first time must not be able to read a suggestion as an instruction.
    /// </summary>
    [Fact]
    public void TheOrderIsOfferedRatherThanPrescribed()
    {
        var html = Plan().Page!.Content;

        Assert.Contains("Suggested running order", html, StringComparison.Ordinal);
        Assert.Contains("A suggestion, not an instruction", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Run them in this order", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The laser steps are in it too. Missing them made the page read as though a mixed job were a
    /// milling job with some files left over — and a mixed job is the one where the order is
    /// hardest to guess and most expensive to get wrong.
    /// </summary>
    [Fact]
    public void TheLaserStepsAreInTheOrderToo()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1AllLayers));

        // The mixed workflow doc 04 calls Use Case 1: burn the resist, etch it, then drill and cut
        // out on the mill.
        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = l.Role switch
                {
                    LayerRole.TopCopper => OutputKind.Svg,
                    LayerRole.TopSilk => OutputKind.Svg,
                    _ => LayerOperations.DefaultFor(l.Role),
                },
            },
            StringComparer.Ordinal);

        var html = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6)).Page!.Content;

        var burn = html.IndexOf("burn the copper layer", StringComparison.Ordinal);
        var etch = html.IndexOf("Etch, then strip", StringComparison.Ordinal);
        var drill = html.IndexOf("<strong>Drill.", StringComparison.Ordinal);
        var silk = html.IndexOf("the silkscreen", StringComparison.Ordinal);
        var outline = html.IndexOf("Cut the board out", StringComparison.Ordinal);

        output.WriteLine($"burn {burn}, etch {etch}, drill {drill}, silk {silk}, outline {outline}");

        Assert.True(burn >= 0, "the laser's copper step is missing");
        Assert.True(etch > burn, "the etch step should follow the burn");
        Assert.True(drill > etch, "drilling comes after the copper exists");
        Assert.True(silk > drill && outline > silk,
            "silkscreen goes on before the board is cut loose");

        // And it says the work is changing machines, which is the part that has to be got right.
        Assert.Contains("goes into the same corner stop", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one it was asked for. An SVG page bigger than its artwork lands at the origin in software
    /// that imports by content, which is out by the border and looks right.
    /// </summary>
    [Fact]
    public void ItGivesTheSvgOffsetAsANumber()
    {
        var html = Plan(new BlankOptions { Enabled = true, LeftMm = 10, BottomMm = 10, RightMm = 10, TopMm = 10 })
            .Page!.Content;

        Assert.Contains("imports the drawing", html, StringComparison.Ordinal);
        Assert.Contains("<th>Imports as</th>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The test board with its top copper (inverted, for the etch resist, as in the workshop — so
    /// that drawing is exactly the board), legend and mask as SVG, and the bottom copper inverted
    /// too. Mirrored, as a bottom layer is.
    /// </summary>
    private static ExportPlan TestBoard(SvgPlacingLayers marks, BlankOptions? blank = null)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = l.Role is LayerRole.TopCopper or LayerRole.TopSilk or LayerRole.TopMask or LayerRole.BottomCopper
                    ? OutputKind.Svg
                    : LayerOperations.DefaultFor(l.Role),
                Invert = l.Role is LayerRole.TopCopper or LayerRole.BottomCopper,
            },
            StringComparer.Ordinal);

        return ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(0.9),
            machineSettings: new MachineSettings { SvgPlacingLayers = marks },
            job: new JobOptions { Blank = blank ?? PreCut80 });
    }

    /// <summary>80 × 80 mm pre-cut stock, given its two alignment holes and nothing else.</summary>
    private static readonly BlankOptions PreCut80 = new()
    {
        Enabled = true, Sizing = BlankSizing.Stated, WidthMm = 80, HeightMm = 80,
        Cut = true, AlignmentHoles = true, HolesOnly = true,
    };

    private static string Size(ExportPlan plan, string layer)
    {
        var d = Svg(plan, layer).Drawing!.Value;
        return Nm.ToMillimetreString(d.Width, 2) + " × " + Nm.ToMillimetreString(d.Height, 2);
    }

    private static ExportItem Svg(ExportPlan plan, string layer) =>
        plan.Items.Single(i => i.TargetName.EndsWith(layer + ".svg", StringComparison.Ordinal));

    /// <summary>
    /// Each SVG gets its own numbers, from its own drawing. Measured in the workshop on the test
    /// board in 80 × 80 stock: Falcon imported the top copper at 68.63 × 66.09, the legend at
    /// 67.10 × 64.01 and the mask at 34.85 × 57.29 — and the page's one offset, the board's, was
    /// right only for the copper. This is the page with the placing layers turned off.
    /// </summary>
    [Fact]
    public void EachSvgSaysWhereItsOwnDrawingSits()
    {
        var plan = TestBoard(SvgPlacingLayers.None);

        output.WriteLine($"copper {Size(plan, "F_Cu")}, legend {Size(plan, "F_Silkscreen")}, mask {Size(plan, "F_Mask")}");

        Assert.Equal("68.63 × 66.09", Size(plan, "F_Cu"));
        // 64.06, not the 64.01 Falcon read: the legend was redrawn in KiCad on 2026-09-19, after that
        // measurement and before this board's Gerbers were re-exported into the corpus.
        Assert.Equal("67.10 × 64.06", Size(plan, "F_Silkscreen"));
        Assert.Equal("34.85 × 57.29", Size(plan, "F_Mask"));

        // The copper is the board, so its centre from the board's corner is half the board.
        var html = plan.Page!.Content;
        output.WriteLine(html[html.IndexOf("<h2>Placing", StringComparison.Ordinal)..]);
        Assert.Contains("34.31 right, 33.05 up", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<g id=\"board-outline\"", Svg(plan, "F_Mask").Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the placing layers are for: with the board outline and the stock in every file, every
    /// file imports at the stock's size, and one placement works for all of them. Tried by hand in
    /// Falcon and LightBurn before this was built.
    /// </summary>
    [Fact]
    public void WithThePlacingLayersEveryFileImportsAtTheSameSize()
    {
        var plan = TestBoard(SvgPlacingLayers.OutlineAndStock);

        foreach (var layer in new[] { "F_Cu", "F_Silkscreen", "F_Mask", "B_Cu" })
        {
            Assert.Equal("80.00 × 80.00", Size(plan, layer));

            var svg = Svg(plan, layer);
            Assert.Contains("<g id=\"board-outline\" inkscape:groupmode=\"layer\" inkscape:label=\"Board outline\">", svg.Content, StringComparison.Ordinal);
            Assert.Contains("<g id=\"stock\" inkscape:groupmode=\"layer\" inkscape:label=\"Stock and holes\">", svg.Content, StringComparison.Ordinal);
            Assert.Contains("stroke=\"#FF0000\"", svg.Content, StringComparison.Ordinal);
            Assert.Contains("stroke=\"#0000FF\"", svg.Content, StringComparison.Ordinal);
            Assert.Contains(svg.Summary, s => s.Contains("Settings › Laser", StringComparison.Ordinal));
        }

        // The centre is measured from the corner of what the file imports as — the stock's, here,
        // since the stock layer makes the box. From the board's corner it is a number the operator
        // cannot use, and the workshop hit exactly that.
        Assert.Contains("<th>Centre, from the stock's corner</th>", plan.Page!.Content, StringComparison.Ordinal);
        Assert.Contains("40.00 right, 40.00 up", plan.Page.Content, StringComparison.Ordinal);

        // And the project page gives one placement instead of a table to look things up in.
        Assert.Contains("Every SVG also carries the board outline, and the stock with its holes", plan.Page!.Content, StringComparison.Ordinal);
        Assert.Contains("puts the stock's corner on the laser's origin", plan.Page.Content, StringComparison.Ordinal);

        // The outline alone, for a board already cut out and put against a jig: the board's box,
        // though there is stock, and no stock layer.
        var cutOut = TestBoard(SvgPlacingLayers.OutlineOnly);
        Assert.Equal("68.63 × 66.09", Size(cutOut, "F_Mask"));
        Assert.DoesNotContain("<g id=\"stock\"", Svg(cutOut, "F_Mask").Content, StringComparison.Ordinal);
        Assert.Contains("puts the board's corner on the laser's origin, for a board already cut out", cutOut.Page!.Content, StringComparison.Ordinal);

        // With the outline alone the box is the board, so its centre is measured from the board.
        Assert.Contains("<th>Centre, from the board's corner</th>", cutOut.Page.Content, StringComparison.Ordinal);
        Assert.Contains("34.31 right, 33.05 up", cutOut.Page.Content, StringComparison.Ordinal);

        // Without stock, the box is the board's.
        var bare = TestBoard(SvgPlacingLayers.OutlineAndStock, new BlankOptions());
        Assert.Equal("68.63 × 66.09", Size(bare, "F_Mask"));
        Assert.DoesNotContain("<g id=\"stock\"", Svg(bare, "F_Mask").Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// An inverted bottom layer on stock the board is not centred in. The drawing is mirrored about
    /// the stock's centreline; the board region it is cut from used to be mirrored about the board's,
    /// which differ by half the difference in the margins — so the resist came out shifted by twice
    /// that. With 10 mm on the left and 5 on the right, 5 mm.
    /// </summary>
    [Fact]
    public void AnInvertedBottomLayerIsCutFromTheBoardWhereTheMirrorPutsIt()
    {
        var plan = TestBoard(SvgPlacingLayers.None, new BlankOptions
        {
            Enabled = true, LeftMm = 10, RightMm = 5, BottomMm = 10, TopMm = 5,
        });

        var bottom = Svg(plan, "B_Cu").Drawing!.Value;

        // Mirrored about the stock's centreline, the board's 10 mm on the left becomes 5.
        output.WriteLine($"bottom copper from {Nm.ToMillimetreString(bottom.MinX, 2)} to {Nm.ToMillimetreString(bottom.MaxX, 2)}");
        Assert.Equal("5.00", Nm.ToMillimetreString(bottom.MinX, 2));
        Assert.Equal("68.63", Nm.ToMillimetreString(bottom.Width, 2));

        // And the placing layer goes with it: in the same file, the red outline runs exactly along
        // the inverted copper's edge.
        var marked = Svg(TestBoard(SvgPlacingLayers.OutlineAndStock, new BlankOptions
        {
            Enabled = true, LeftMm = 10, RightMm = 5, BottomMm = 10, TopMm = 5,
        }), "B_Cu").Content;

        Assert.Equal(XRange(marked, "artwork"), XRange(marked, "board-outline"));
    }

    /// <summary>The least and greatest X in one group's paths, from M and L commands only.</summary>
    private static (double, double) XRange(string svg, string group)
    {
        var xs = System.Xml.Linq.XDocument.Parse(svg).Descendants()
            .Where(e => (string?)e.Attribute("id") == group)
            .SelectMany(g => g.Descendants())
            .Select(e => (string?)e.Attribute("d"))
            .Where(d => d is not null)
            .SelectMany(d => System.Text.RegularExpressions.Regex.Matches(d!, @"[ML]\s*(-?\d+(?:\.\d+)?)"))
            .Select(m => Math.Round(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 2))
            .ToList();

        return (xs.Min(), xs.Max());
    }

    /// <summary>With no blank, work zero is the board's corner and there is no offset to add.</summary>
    [Fact]
    public void WithNoBlankItSaysTheBoardsCorner()
    {
        var html = Plan().Page!.Content;

        Assert.Contains("lower-left corner of the board", html, StringComparison.Ordinal);
        Assert.DoesNotContain("mm right</strong> and", html, StringComparison.Ordinal);
    }

    /// <summary>Self-contained: one file, no network.</summary>
    [Fact]
    public void ItStandsAlone()
    {
        var html = Plan().Page!.Content;

        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<link", html, StringComparison.Ordinal);
    }
}
