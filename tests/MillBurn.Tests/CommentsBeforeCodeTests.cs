using MillBurn.Core;
using MillBurn.Gcode;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// A comment before the code on a line, which three readers disagreed about.
///
/// `GcodeParser` removed balanced `(...)` spans and kept the rest. `DryRun` and `ProgramFraming`
/// each carried a six-line copy that truncated the line at its first `(` instead — so
/// `( touch off ) G1 Z-1.0 F50` read as completely blank to both of them, while the machine, the
/// backplot and the parser all saw a real cutting move.
///
/// **It voided every safety check built on top of them at once**, which is why it gets a file
/// rather than a case in an existing one. The dry run's refusals went silent, its rewrite skipped
/// the line, and its clearance check never measured it — so it handed back a program containing a
/// verbatim plunge into the work under a header promising every Z three millimetres higher. The
/// framing check lost its errors the same way, on the real program.
///
/// Every test here fails against the truncating version. They are written against the *behaviour*
/// rather than the helper, because the helper is not the point: three call sites believed
/// something about G-code that was not true.
/// </summary>
public sealed class CommentsBeforeCodeTests(ITestOutputHelper output)
{
    private static readonly DryRunOptions Raised = new();

    /// <summary>
    /// The refusals see a command that is preceded by a comment.
    ///
    /// Each of these is refused when written bare. Behind a comment they were not refused at all,
    /// and the dry run was written and reported as safe.
    /// </summary>
    [Theory]
    [InlineData("G92 X0 Y0 Z0", "G92")]
    [InlineData("G10 L20 P1 Z0", "G10")]
    [InlineData("G38.2 Z-0.2 F50", "G38")]
    [InlineData("G43.1 Z-0.050", "G43")]
    [InlineData("G28", "G28")]
    public void ACommentDoesNotHideACommandFromTheRefusals(string command, string named)
    {
        var (_, bare) = DryRun.Rewrite($"G21 G90\nG0 Z2.000\n{command}\nG1 Z-0.050 F60", Raised);
        var (_, hidden) = DryRun.Rewrite(
            $"G21 G90\nG0 Z2.000\n( set up ) {command}\nG1 Z-0.050 F60", Raised);

        output.WriteLine($"bare:   {bare.Refusal ?? "(not refused)"}");
        output.WriteLine($"hidden: {hidden.Refusal ?? "(not refused)"}");

        Assert.NotNull(bare.Refusal);
        Assert.NotNull(hidden.Refusal);
        Assert.Contains(named, hidden.Refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The worst shape of it: a cutting move that survives into the dry run unraised.
    ///
    /// The file claimed a lowest point of 2.950 mm and contained `G1 Z-1.000 F50`. An operator who
    /// set work zero as the header instructs, and watched, would have seen the cutter go a
    /// millimetre into the board.
    /// </summary>
    [Fact]
    public void ACommentDoesNotStopAZBeingRaised()
    {
        var (text, report) = DryRun.Rewrite(
            """
            G21 G90
            G0 Z2.000
            ( touch off ) G1 Z-1.000 F50
            G1 X10.000 F200
            M30
            """,
            Raised);

        output.WriteLine(text);

        Assert.Null(report.Refusal);

        // Raised like any other Z: -1.000 plus the 3 mm rise.
        Assert.Contains("( touch off ) G1 Z2.000 F50", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Z-1.000", text, StringComparison.Ordinal);

        // And the measurement saw it. Under the old reader this line was not merely unraised — it
        // fell outside the measured range entirely, so the report described a program that was not
        // the one in the file.
        Assert.Equal(2.0, report.LowestZMm, 3);
        Assert.True(report.StaysClear);
    }

    /// <summary>The comment does not have to lead the line; it can sit between the words.</summary>
    [Fact]
    public void ACommentBetweenTheWordsDoesNotHideTheRest()
    {
        var (text, report) = DryRun.Rewrite(
            "G21 G90\nG0 Z2.000\nG0 ( rapid ) Z-1.000\nM30", Raised);

        output.WriteLine(text);

        Assert.Null(report.Refusal);
        Assert.Contains("G0 ( rapid ) Z2.000", text, StringComparison.Ordinal);
        Assert.Equal(2.0, report.LowestZMm, 3);
    }

    /// <summary>
    /// The flat run reads lines with the same helper, so it gained the same fix.
    ///
    /// It is the worse of the two to get wrong: it has no refusal path at all, so a Z it cannot see
    /// is simply left at its cutting depth under a header stating that every Z is held clear.
    /// </summary>
    [Fact]
    public void TheFlatRunAlsoSeesPastAComment()
    {
        var (text, report) = DryRun.Rewrite(
            "G21 G90\n( touch off ) G1 Z-1.000 F50\nM30",
            new DryRunOptions { Style = DryRunStyle.Flat, HeightMm = 5 });

        output.WriteLine(text);

        Assert.Null(report.Refusal);
        Assert.True(report.StaysClear);
        Assert.Contains("( touch off ) G1 Z5.000 F50", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Z-1.000", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The framing check, which guards the real program rather than the dry run.
    ///
    /// `M30` in a start block ends the program before the job runs, and is an error that stops the
    /// export. Behind a comment it produced no error, no check and no warning, and the export went
    /// on to write a program whose job is unreachable.
    /// </summary>
    [Theory]
    [InlineData("M30")]
    [InlineData("( all done ) M30")]
    [InlineData("M30 ( all done )")]
    public void TheFramingCheckSeesPastAComment(string line)
    {
        var issues = ProgramFraming.Check(line);

        output.WriteLine(string.Join(
            "\n", issues.Select(i => $"line {i.Line} {(i.IsError ? "ERROR" : "check")}: {i.Message}")));

        Assert.Contains(issues, i => i.IsError && i.Message.Contains("ends the program", StringComparison.Ordinal));
    }

    /// <summary>
    /// What the helper does, stated directly — the two spellings and the unclosed case.
    /// </summary>
    [Theory]
    [InlineData("G1 Z-1.0 F50", "G1 Z-1.0 F50")]
    [InlineData("( note ) G1 Z-1.0", "G1 Z-1.0")]
    [InlineData("G1 ( note ) Z-1.0", "G1  Z-1.0")]
    [InlineData("G1 Z-1.0 ( note )", "G1 Z-1.0")]
    [InlineData("( a ) G1 ( b ) Z-1.0 ( c )", "G1  Z-1.0")]
    [InlineData("G1 Z-1.0 ; note", "G1 Z-1.0")]
    // An unclosed bracket takes the rest of the line, which is what a controller does with it.
    [InlineData("G1 Z-1.0 ( unclosed", "G1 Z-1.0")]
    [InlineData("( unclosed G1 Z-1.0", "")]
    public void WithoutCommentsKeepsTheCodeAndDropsTheProse(string line, string expected) =>
        Assert.Equal(expected, GcodeText.WithoutComments(line));
}
