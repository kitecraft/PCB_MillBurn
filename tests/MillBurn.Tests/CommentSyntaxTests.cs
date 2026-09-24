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

    [Fact]
    public void TheDryRunHeaderIsWellFormed()
    {
        var (text, _) = DryRun.Rewrite("G21 G90\nG1 Z-0.05 F60\nM30");

        AssertCommentsAreWellFormed(text, "dry run");
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
