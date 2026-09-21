using MillBurn.Core;
using MillBurn.Gcode;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The operator's own lines at the start and end of every program.
///
/// This is somebody typing raw G-code into a text box and having it put into every file we write,
/// so the tests are about two things: that the lines land where they cannot do the damage they
/// could otherwise do, and that the two mistakes producing a file which looks fine and is not — a
/// stray bracket and an early program end — are refused rather than warned about.
/// </summary>
public sealed class ProgramFramingTests(ITestOutputHelper output)
{
    private static Job Job() => new()
    {
        Name = "test",
        Toolpaths =
        [
            new Toolpath
            {
                Kind = ToolpathKind.Drill,
                Label = "holes",
                Tool = Tool.DefaultDrill,
                Drills = [new DrillTarget(Point2.Origin, Nm.FromMillimetres(1.9), 0)],
            },
        ],
    };

    private static string Emit(ProgramFraming framing) =>
        GcodeEmitter.Emit(Job(), new GcodeOptions { Framing = framing }).Text;

    private static int LineOf(string text, string needle) =>
        Array.FindIndex(text.Split('\n'), l => l.Contains(needle, StringComparison.Ordinal));

    // ------------------------------------------------------------------ where the lines land

    /// <summary>
    /// The start block goes in before the modal setup, and that is the whole safety argument for
    /// this feature: whatever somebody leaves the machine in, the next two lines put it back into
    /// millimetres, absolute distance and the XY plane. The mistake that would silently reinterpret
    /// every coordinate in the file is made impossible rather than merely warned about.
    /// </summary>
    [Fact]
    public void TheStartBlockRunsBeforeTheProgramSetsItsModes()
    {
        var text = Emit(new ProgramFraming { Start = "G20\nG91" });

        output.WriteLine(string.Join("\n", text.Split('\n').Take(8)));

        var theirs = LineOf(text, "G91");
        var ours = LineOf(text, "G21 G90 G94");

        Assert.True(theirs >= 0 && ours > theirs, "our modal block must come after theirs");

        // And the program really is in millimetres and absolute afterwards, as the parser sees it.
        var parsed = GcodeParser.Parse(text);
        Assert.Equal(Nm.FromMillimetres(1.9), -parsed.Moves.Min(m => m.ToZNm));
    }

    /// <summary>Anything after M30 is never read, so the end block has to precede it.</summary>
    [Fact]
    public void TheEndBlockRunsBeforeTheProgramEnd()
    {
        var text = Emit(new ProgramFraming { End = "M9\nG53 G0 Z-5.000" });

        var theirs = LineOf(text, "M9");
        var end = LineOf(text, "M30");

        Assert.True(theirs >= 0, "the end block should be in the file");
        Assert.True(end > theirs, "M30 must come last");
    }

    /// <summary>And after the spindle stops, so nothing runs with it still turning.</summary>
    [Fact]
    public void TheEndBlockRunsAfterTheSpindleStops()
    {
        var text = Emit(new ProgramFraming { End = "M9" });

        Assert.True(LineOf(text, "M5") < LineOf(text, "M9"));
    }

    [Fact]
    public void TheLinesAreMarkedSoTheyCanBeToldApart()
    {
        var text = Emit(new ProgramFraming { Start = "$H", End = "M9" });

        Assert.Contains("( Your start G-code )", text, StringComparison.Ordinal);
        Assert.Contains("( Your end G-code )", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoFramingChangesNothing()
    {
        var plain = Emit(ProgramFraming.None);
        var empty = Emit(new ProgramFraming { Start = "", End = "   " });

        Assert.Equal(plain, empty);
        Assert.DoesNotContain("Your start G-code", plain, StringComparison.Ordinal);
    }

    /// <summary>Somebody's blank line between two stanzas is theirs; a trailing newline is not.</summary>
    [Fact]
    public void InternalBlankLinesSurviveAndTrailingOnesDoNot()
    {
        var text = Emit(new ProgramFraming { Start = "$H\n\nG54\n\n\n" });
        var body = text.Split('\n');
        var at = LineOf(text, "$H");

        Assert.Equal("", body[at + 1]);
        Assert.Equal("G54", body[at + 2]);
        Assert.Equal("", body[at + 3]);
        Assert.Equal("G21 G90 G94", body[at + 4]);
    }

    // ------------------------------------------------------------------ what it refuses

    /// <summary>
    /// The two that produce a file which looks right and is not. Everything else is advice.
    /// </summary>
    [Theory]
    [InlineData("M30", "ends the program")]
    [InlineData("M2", "ends the program")]
    [InlineData("( oops (nested) )", "bracket inside a comment")]
    [InlineData("( never closed", "never closes it")]
    [InlineData("closes ) nothing", "never opened")]
    public void FatalMistakesAreErrors(string block, string expected)
    {
        var issues = ProgramFraming.Check(block);

        output.WriteLine(string.Join("\n", issues));

        Assert.Contains(issues, i => i.IsError && i.Message.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>
    /// G92 shifts every coordinate after it and the file gives no sign. Worth saying loudly, and
    /// not worth refusing: somebody with a reason to use it has a reason we do not know.
    /// </summary>
    [Fact]
    public void ACoordinateOffsetIsWarnedAboutButAllowed()
    {
        var issues = ProgramFraming.Check("G92 X0 Y0");

        Assert.Contains(issues, i => i.Message.Contains("G92", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, i => i.IsError);
    }

    /// <summary>
    /// A start block that travels in X or Y does so before the program has lifted anything. If the
    /// tool happens to be down, it is dragged.
    /// </summary>
    [Fact]
    public void MovingSidewaysInTheStartBlockIsFlagged()
    {
        var issues = ProgramFraming.Check("G0 X0 Y0");

        Assert.Contains(issues, i => i.Message.Contains("before the program has lifted", StringComparison.Ordinal));

        // Not in the end block: by then the program has already parked at safe Z.
        Assert.DoesNotContain(
            ProgramFraming.Check("G0 X0 Y0", isEnd: true),
            i => i.Message.Contains("before the program has lifted", StringComparison.Ordinal));
    }

    /// <summary>
    /// Saying "this will not do what you think" is more useful than silence. Somebody who typed
    /// G20 meant something by it and should know it does not survive.
    /// </summary>
    [Fact]
    public void ModesTheProgramOverridesAreNotedRatherThanIgnored()
    {
        var issues = ProgramFraming.Check("G20");

        Assert.Contains(issues, i => i.Message.Contains("will not affect the job", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, i => i.IsError);
    }

    [Fact]
    public void AnOrdinaryStartBlockIsAcceptedWithoutComment()
    {
        var issues = ProgramFraming.Check("( home and pick the fixture offset )\n$H\nG54\nM8\nG4 P2.0");

        Assert.Empty(issues);
    }

    [Fact]
    public void ErrorsComeFirst()
    {
        var issues = ProgramFraming.Check("G92 X0\nM30");

        Assert.True(issues[0].IsError);
    }

    // ------------------------------------------------------------------ whose lines win

    /// <summary>
    /// Null means "use the machine's"; empty means "deliberately none". Collapsing the two would
    /// make a project that was told to add nothing start adding something when the machine default
    /// changed, which is exactly the surprise this feature must not spring.
    /// </summary>
    [Fact]
    public void EmptyIsADecisionAndNullIsNot()
    {
        var machine = new ProgramFraming { Start = "$H", End = "M9" };

        var inherits = new ProgramFraming().Over(machine);
        Assert.Equal("$H", inherits.Start);
        Assert.Equal("M9", inherits.End);

        var declines = new ProgramFraming { Start = "" }.Over(machine);
        Assert.Equal("", declines.Start);
        Assert.Equal("M9", declines.End);
    }

    [Fact]
    public void AProjectOverridesTheMachineOnePieceAtATime()
    {
        var machine = new ProgramFraming { Start = "$H", End = "M9" };
        var project = new ProgramFraming { Start = "G28" };

        var resolved = project.Over(machine);

        Assert.Equal("G28", resolved.Start);
        Assert.Equal("M9", resolved.End);
    }

    /// <summary>Derived state stays out of the file, as everywhere else.</summary>
    [Fact]
    public void TheEmptyFlagIsNotPersisted()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new ProgramFraming { Start = "$H" });

        output.WriteLine(json);

        Assert.Contains("Start", json, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEmpty", json, StringComparison.Ordinal);
    }

    /// <summary>A comment inside somebody's block must not break the file it lands in.</summary>
    [Fact]
    public void AcceptedBlocksProduceWellFormedComments()
    {
        var text = Emit(new ProgramFraming
        {
            Start = "( home first )\n$H",
            End = "( park it )\nM9",
        });

        foreach (var line in text.Split('\n'))
        {
            var open = line.IndexOf('(', StringComparison.Ordinal);

            if (open < 0)
            {
                continue;
            }

            var close = line.IndexOf(')', open);

            Assert.True(close > open, $"unclosed comment: {line}");
            Assert.DoesNotContain('(', line[(open + 1)..close]);
        }
    }
}
