using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;
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
    /// Each SVG gets its own numbers, from its own drawing. Measured in the workshop on the test
    /// board in 80 × 80 stock: Falcon imported the top copper at 68.63 × 66.09, the legend at
    /// 67.10 × 64.01 and the mask at 34.85 × 57.29 — and the page's one offset, the board's, was
    /// right only for the copper.
    /// </summary>
    [Fact]
    public void EachSvgSaysWhereItsOwnDrawingSits()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = l.Role is LayerRole.TopCopper or LayerRole.TopSilk or LayerRole.TopMask
                    ? OutputKind.Svg
                    : LayerOperations.DefaultFor(l.Role),

                // Inverted, for the etch resist, as in the workshop: everything inside the board
                // edge except the copper, so this one drawing is exactly the board.
                Invert = l.Role == LayerRole.TopCopper,
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(0.9),
            job: new JobOptions { Blank = new BlankOptions { Enabled = true, Sizing = BlankSizing.Stated, WidthMm = 80, HeightMm = 80, Cut = false } });

        string Size(string layer)
        {
            var item = plan.Items.Single(i => i.TargetName.EndsWith(layer + ".svg", StringComparison.Ordinal));
            var d = item.Drawing!.Value;
            return Nm.ToMillimetreString(d.Width, 2) + " × " + Nm.ToMillimetreString(d.Height, 2);
        }

        output.WriteLine($"copper {Size("F_Cu")}, legend {Size("F_Silkscreen")}, mask {Size("F_Mask")}");

        Assert.Equal("68.63 × 66.09", Size("F_Cu"));
        Assert.Equal("67.10 × 64.01", Size("F_Silkscreen"));
        Assert.Equal("34.85 × 57.29", Size("F_Mask"));

        // The copper is the board, so its centre from the board's corner is half the board.
        var html = plan.Page!.Content;
        output.WriteLine(html[html.IndexOf("<h2>Placing", StringComparison.Ordinal)..]);
        Assert.Contains("34.31 right, 33.05 up", html, StringComparison.Ordinal);
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
