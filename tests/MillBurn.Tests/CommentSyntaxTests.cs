using MillBurn.Align;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;

namespace MillBurn.Tests;

/// <summary>
/// A G-code comment runs from its opening bracket to the first closing one, and it may not contain
/// another opening bracket.
///
/// This is not a style question. An innocent "about 12 minute(s)" ends the comment early and leaves
/// <c>of standing and watching. )</c> to be read as code; LinuxCNC rejects the nested opening
/// bracket outright and refuses the file. It got into a header exactly that way, so every generator
/// that writes prose into a program is checked here rather than trusted.
/// </summary>
public sealed class CommentSyntaxTests
{
    /// <summary>Every comment opens once, closes once, and closes before the line ends.</summary>
    private static void AssertCommentsAreWellFormed(string program, string what)
    {
        var line = 0;

        foreach (var raw in program.Split('\n'))
        {
            line++;

            var open = raw.IndexOf('(', StringComparison.Ordinal);

            if (open < 0)
            {
                Assert.DoesNotContain(")", raw, StringComparison.Ordinal);
                continue;
            }

            var close = raw.IndexOf(')', open);

            Assert.True(close > open, $"{what} line {line} opens a comment and never closes it: {raw}");

            var inner = raw[(open + 1)..close];

            Assert.False(
                inner.Contains('(', StringComparison.Ordinal),
                $"{what} line {line} nests a bracket inside a comment: {raw}");

            // And nothing after the comment but more comment.
            var rest = raw[(close + 1)..].Trim();

            Assert.True(
                rest.Length == 0 || rest.StartsWith('('),
                $"{what} line {line} has code after a comment: {raw}");
        }
    }

    private static Bounds Board(double widthMm, double heightMm) =>
        new(0, 0, Nm.FromMillimetres(widthMm), Nm.FromMillimetres(heightMm));

    private static HeightMap Surface()
    {
        var samples = new List<ProbeSample>();

        for (var x = 0; x <= 40; x += 20)
        {
            for (var y = 0; y <= 40; y += 20)
            {
                samples.Add(new ProbeSample(
                    new Point2(Nm.FromMillimetres(x), Nm.FromMillimetres(y)),
                    Nm.FromMillimetres(0.001 * x)));
            }
        }

        return HeightMap.Build(samples, new HeightMapOptions { ZeroAt = null });
    }

    /// <summary>
    /// Sizes chosen so the counts and durations in the header land on one digit and on two. The
    /// bug that prompted this only appeared once a board was big enough to need "12 minutes".
    /// </summary>
    [Theory]
    [InlineData(12, 9)]
    [InlineData(30, 20)]
    [InlineData(300, 200)]
    public void TheProbeRoutineHeaderIsWellFormed(double widthMm, double heightMm) =>
        AssertCommentsAreWellFormed(
            ProbeRoutine.Generate(Board(widthMm, heightMm)).Text, "probe routine");

    /// <summary>
    /// Every dry-run header, named rather than defaulted.
    ///
    /// This was one call with no options, which meant it checked whichever header happened to be
    /// the default — and when V31 made that the raised one, it stopped checking the flat one
    /// without failing. There are now three: flat, raised, and raised with the feeds replaced,
    /// which says something different about the time and so is a different block of text.
    /// </summary>
    [Theory]
    [InlineData(DryRunStyle.Flat, true)]
    [InlineData(DryRunStyle.Flat, false)]
    [InlineData(DryRunStyle.Raised, true)]
    [InlineData(DryRunStyle.Raised, false)]
    public void TheDryRunHeaderIsWellFormed(DryRunStyle style, bool keepFeeds)
    {
        var (text, report) = DryRun.Rewrite(
            "G21 G90\nG0 Z2.000\nG1 Z-0.05 F60\nM30",
            new DryRunOptions { Style = style, KeepFeeds = keepFeeds });

        Assert.Null(report.Refusal);

        AssertCommentsAreWellFormed(text, $"dry run ({style}, feeds {(keepFeeds ? "kept" : "replaced")})");
    }

    /// <summary>
    /// The raised header claims the run takes the real program's time. That is only true while the
    /// feeds are the real ones, and turning them off is an option — so the sentence has to go when
    /// the feeds do. It is the one line the operator reads standing at the machine.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheRaisedHeaderOnlyPromisesTheRealTimeWhenTheFeedsAreReal(bool keepFeeds)
    {
        var (text, report) = DryRun.Rewrite(
            "G21 G90\nG0 Z2.000\nG1 Z-0.05 F60\nM30",
            new DryRunOptions { Style = DryRunStyle.Raised, KeepFeeds = keepFeeds });

        // Or a refusal hands back the input, which contains neither sentence, and the half of this
        // test that asks for an absence passes without the header existing at all.
        Assert.Null(report.Refusal);

        // The comment block, taken as the comment block. Cutting at the first `M5` would mean that
        // if the preamble ever stopped emitting one, "the header" became the whole program and a
        // sentence anywhere in it would satisfy the search.
        var header = string.Join(
            "\n", text.Split('\n').TakeWhile(l => l.StartsWith('(')));

        Assert.StartsWith("( ***", header, StringComparison.Ordinal);

        Assert.Equal(keepFeeds, header.Contains("the time the real program takes", StringComparison.Ordinal));

        // And with them off it says so, rather than simply going quiet on the question.
        Assert.Equal(!keepFeeds, header.Contains("its time means", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLevelledHeaderIsWellFormed()
    {
        var (text, _) = Leveller.Apply("G21 G90\nG1 X10.000 Y10.000 Z-0.050 F60\nM30", Surface());

        AssertCommentsAreWellFormed(text, "levelled program");
    }

    /// <summary>
    /// And the programs themselves, which carry summaries of what each operation does — tab counts,
    /// pass depths, tool sizes — into comments on real boards.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.GridStripConnector)]
    [InlineData(RealBoards.Panel)]
    public void EveryEmittedProgramIsWellFormed(string board)
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
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        Assert.NotEmpty(plan.Items);

        foreach (var item in plan.Items)
        {
            AssertCommentsAreWellFormed(item.Content, item.TargetName);
        }
    }
}
