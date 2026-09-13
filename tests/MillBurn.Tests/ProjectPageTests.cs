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

        var blank = html.IndexOf("Cut the blank", StringComparison.Ordinal);
        var isolate = html.IndexOf("Isolate the copper", StringComparison.Ordinal);
        var outline = html.IndexOf("Cut the board out", StringComparison.Ordinal);

        Assert.True(blank >= 0 && isolate > blank && outline > isolate,
            $"blank {blank}, isolate {isolate}, outline {outline}");
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

        Assert.Contains("Do not centre them", html, StringComparison.Ordinal);
        Assert.Contains("10.00 mm right and 10.00 mm up", html, StringComparison.Ordinal);
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
