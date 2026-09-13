using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;
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

        return Assert.Single(
            plan.Items,
            i => i.TargetName.EndsWith(".slots.nc", StringComparison.Ordinal));
    }

    /// <summary>A routing program gets a page, named for what it is rather than for the file.</summary>
    [Fact]
    public void EveryRoutingProgramGetsAPage()
    {
        var item = Routing(RealBoards.ArduinoUno);
        var guide = Assert.IsType<ExportCompanion>(item.Companion);

        output.WriteLine(guide.TargetName + " — " + guide.Description);

        Assert.Equal("Arduino UNO-PTH-drl.routing.html", guide.TargetName);
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
}
