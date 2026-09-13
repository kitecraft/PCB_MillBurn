using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The page that explains how to run a drilling program.
///
/// Its whole value is naming the right bit in the right order, so that is what the tests are about.
/// A guide that names the wrong bit is worse than no guide: somebody will follow it, and the board
/// gets a 1.7 mm hole where a 1.0 mm one belonged.
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

    private static ExportItem Drilling(string board, bool multiSize = true) =>
        Plan(board).Items
            .Where(i => i.Operation == OperationKind.Drilling)
            .First(i => multiSize == i.Content.Contains("M0", StringComparison.Ordinal));

    // ------------------------------------------------------------------ the sequence

    /// <summary>
    /// The bits, named and in order.
    ///
    /// This is the one that was wrong first time. The emitter writes a toolpath's label *before*
    /// the tool-change sequence that precedes it, so splitting the program at its stops leaves
    /// every label attached to the section before the one it names — and the guide confidently
    /// listed the second bit as "the bit already in the spindle".
    /// </summary>
    [Fact]
    public void EveryBitIsNamedAndTheyAreInOrder()
    {
        var item = Drilling(RealBoards.PogoTest1);

        Assert.NotNull(item.Companion);

        var (_, report) = DrillGuide.Build(item.Content, Context(item));

        output.WriteLine(string.Join("\n", report.Steps.Select(s => $"{s.Order}. {s.Bit} × {s.Holes}")));

        Assert.Equal(2, report.Steps.Count);
        Assert.Equal("Drill 1.70 mm", report.Steps[0].Bit);
        Assert.Equal("Drill 1.00 mm", report.Steps[1].Bit);
        Assert.DoesNotContain(report.Steps, s => s.Bit.Contains("already in the spindle", StringComparison.Ordinal));
    }

    /// <summary>Biggest first, so the guide tells the operator to do the right thing.</summary>
    [Fact]
    public void TheOrderIsBiggestFirst()
    {
        var item = Drilling(RealBoards.PogoTest1);
        var (_, report) = DrillGuide.Build(item.Content, Context(item));

        var diameters = report.Steps
            .Select(s => double.Parse(s.Bit.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(diameters.OrderByDescending(d => d), diameters);
    }

    /// <summary>
    /// The hole counts have to add up to the program's, or the page is describing a different run.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.PogoTest1AllLayers)]
    public void TheHoleCountsAccountForEveryHole(string board)
    {
        foreach (var item in Plan(board).Items.Where(i => i.Operation == OperationKind.Drilling))
        {
            var (_, report) = DrillGuide.Build(item.Content, Context(item));

            var drilled = GcodeParser.Parse(item.Content).Moves
                .Where(m => !m.IsRapid && m.IsVertical && m.ToZNm < 0)
                .Select(m => m.From)
                .Distinct()
                .Count();

            output.WriteLine($"{item.TargetName}: {report.Holes} across {report.Steps.Count} bits");
            Assert.Equal(drilled, report.Holes);
        }
    }

    /// <summary>One stop between each pair of bits, and none after the last.</summary>
    [Fact]
    public void ThereIsOneChangeFewerThanThereAreBits()
    {
        var item = Drilling(RealBoards.PogoTest1);
        var (_, report) = DrillGuide.Build(item.Content, Context(item));

        var stops = item.Content.Split('\n').Count(l => l.Trim() == "M0");

        Assert.Equal(stops, report.Changes);
        Assert.Equal(report.Steps.Count - 1, report.Changes);
    }

    /// <summary>The line numbers have to land on the program, so following along works.</summary>
    [Fact]
    public void EachSectionPointsAtARealLine()
    {
        var item = Drilling(RealBoards.PogoTest1);
        var (_, report) = DrillGuide.Build(item.Content, Context(item));
        var lines = item.Content.Split('\n');

        // The line a step points at is the line that *names* its bit, not the top of the file. It
        // used to be the start of the M0-delimited section, which for the first bit was line 1 —
        // true, and useless: the point of the number is "scroll to here to see this bit's block".
        foreach (var step in report.Steps)
        {
            Assert.InRange(step.Line, 1, lines.Length);
            Assert.Contains(step.Bit, lines[step.Line - 1], StringComparison.Ordinal);
        }

        // And they go forwards.
        Assert.Equal(report.Steps.Select(s => s.Line).Order(), report.Steps.Select(s => s.Line));
    }

    // ------------------------------------------------------------------ the page itself

    [Fact]
    public void ASingleBitJobDoesNotTalkAboutToolChanges()
    {
        var item = Drilling(RealBoards.PogoTest1, multiSize: false);
        var (html, report) = DrillGuide.Build(item.Content, Context(item));

        Assert.Single(report.Steps);
        Assert.Equal(0, report.Changes);
        Assert.DoesNotContain("stops on its own", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Re-zero Z after every bit change", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AMultiBitJobSaysToRezeroZ()
    {
        var item = Drilling(RealBoards.PogoTest1);
        var (html, _) = DrillGuide.Build(item.Content, Context(item));

        Assert.Contains("Z is the one you have to set again", html, StringComparison.Ordinal);
        Assert.Contains("sacrificial board", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page must not tell the operator two different things about re-zeroing.
    ///
    /// It did. "It does not need re-zeroing" — meaning the two axes that had not moved — sat
    /// directly above "re-zero Z after every bit change". Both sentences were defensible and the
    /// pair was not, and this is a page somebody reads standing at a machine, where an apparent
    /// contradiction means picking one and being wrong half the time.
    ///
    /// The reassurance has to name the axes it is about.
    /// </summary>
    [Fact]
    public void ItDoesNotContradictItselfAboutRezeroing()
    {
        var item = Drilling(RealBoards.PogoTest1);
        var (html, _) = DrillGuide.Build(item.Content, Context(item));

        Assert.DoesNotContain("does not need re-zeroing", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no re-zeroing", html, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("X and Y keep their zero", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// One file, openable anywhere. It is written into whatever folder the operator picked, next to
    /// a machine that may never have been online, so a link to a stylesheet or a font would leave a
    /// page that renders as unstyled text exactly when it is needed.
    /// </summary>
    [Fact]
    public void ThePageIsSelfContained()
    {
        var item = Drilling(RealBoards.PogoTest1);
        var (html, _) = DrillGuide.Build(item.Content, Context(item));

        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<link", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBoardAndProgramAreNamedOnIt()
    {
        var item = Drilling(RealBoards.PogoTest1);
        var (html, _) = DrillGuide.Build(item.Content, Context(item));

        Assert.Contains("PogoTest1-PTH.nc", html, StringComparison.Ordinal);
        Assert.Contains("Plated holes", html, StringComparison.Ordinal);
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

    [Fact]
    public void TheCompanionIsAttachedToDrillingAndNothingElse()
    {
        foreach (var item in Plan(RealBoards.PogoTest1).Items)
        {
            if (item.Operation == OperationKind.Drilling)
            {
                Assert.NotNull(item.Companion);
                Assert.EndsWith(".drilling.html", item.Companion.TargetName, StringComparison.Ordinal);
            }
            else
            {
                Assert.Null(item.Companion);
            }
        }
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

    private static DrillGuideContext Context(ExportItem item) => new()
    {
        BoardName = "PogoTest1",
        LayerLabel = item.LayerLabel,
        ProgramName = item.TargetName,
        BoardThicknessNm = Nm.FromMillimetres(1.6),
        BreakThroughNm = Nm.FromMillimetres(0.3),
    };
}
