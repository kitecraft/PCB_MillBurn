using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// A program that traces the same path in the air.
///
/// This is a safety feature, so the tests are the feature. The claim is narrow and absolute — the
/// tool never goes below the stated height and the spindle never starts — and a dry run that is
/// wrong is worse than none, because it is the thing people trust before committing a board.
/// </summary>
public sealed class DryRunTests(ITestOutputHelper output)
{
    private static string Real(string board)
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

        return plan.Items.First(i => i.Operation == OperationKind.Isolation).Content;
    }

    // ------------------------------------------------------------------ the promise

    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.GridStripConnector)]
    [InlineData(RealBoards.Panel)]
    public void NothingEverGoesBelowTheHeight(string board)
    {
        var (text, report) = DryRun.Rewrite(Real(board));

        output.WriteLine($"{board}: lowest Z {report.LowestZMm:F3} mm, "
            + $"{report.LinesRewritten:N0} lines rewritten");

        Assert.Null(report.Refusal);
        Assert.True(report.StaysClear);

        // Measured independently of the report, from the file that would run: nothing travels
        // sideways below the height, at either end of the move. The preamble's rise from wherever
        // the machine is sitting up to the safe height is exempt, and is the point.
        var parsed = GcodeParser.Parse(text);

        Assert.All(
            parsed.Moves.Where(m => m.MovesInPlane),
            m => Assert.True(
                m.FromZNm >= Nm.FromMillimetres(5) - 1 && m.ToZNm >= Nm.FromMillimetres(5) - 1,
                $"a move travelled at {m.DeepestZNm / (double)Nm.PerMillimetre:F3} mm"));

        Assert.Equal(5.0, report.LowestZMm, 3);
    }

    /// <summary>
    /// A tool spinning inches above the work can still take a finger off, and it removes the one
    /// advantage of doing this at all.
    /// </summary>
    [Fact]
    public void TheSpindleNeverStarts()
    {
        var (text, report) = DryRun.Rewrite(Real(RealBoards.PogoTest1));

        Assert.True(report.SpindleCommandsRemoved > 0, "the real program should have started it");

        // As whole words. M30 is the program end and contains "M3" as a substring — the same trap
        // the implementation has to avoid, so the test must not fall into it either.
        foreach (var line in text.Split('\n'))
        {
            var code = line.Split('(')[0].Trim();
            Assert.DoesNotMatch("[Mm]3(?![0-9.])", code);
            Assert.DoesNotMatch("[Mm]4(?![0-9.])", code);
        }

        Assert.Contains("M5", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The path has to be the same one, or the dry run proves something about a different program.
    /// Compared as the full sequence of XY positions.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.GridStripConnector)]
    public void TheHorizontalPathIsUnchanged(string board)
    {
        var original = Real(board);
        var (dry, _) = DryRun.Rewrite(original);

        static List<Point2> Path(string text) =>
        [
            .. GcodeParser.Parse(text).Moves
                .Where(m => m.MovesInPlane)
                .Select(m => m.To),
        ];

        var before = Path(original);
        var after = Path(dry);

        Assert.NotEmpty(before);
        Assert.Equal(before.Count, after.Count);

        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i], after[i]);
        }
    }

    /// <summary>
    /// The rise is in the preamble rather than trusted to the program. A dry run is exactly what
    /// you point at a file you are unsure of, and "safe as long as the file was already sensible"
    /// is not a guarantee worth making.
    /// </summary>
    [Fact]
    public void ItRisesToTheHeightBeforeAnythingMovesSideways()
    {
        var (text, report) = DryRun.Rewrite("""
            G21 G90
            G0 X50 Y50
            G1 X60 Y50 F200
            M30
            """);

        Assert.True(report.StaysClear);

        var moves = GcodeParser.Parse(text).Moves.ToList();
        var firstInPlane = moves.FindIndex(m => m.MovesInPlane);
        var firstVertical = moves.FindIndex(m => m.IsVertical);

        Assert.True(firstVertical >= 0, "the preamble should command a Z move");
        Assert.True(firstVertical < firstInPlane, "it should rise before it travels");
    }

    /// <summary>
    /// A program in inches means its Z words are inches, so the substituted height has to be too.
    /// Converting beats refusing: refusing would push someone towards running the real one to see
    /// what it does.
    /// </summary>
    [Fact]
    public void AnInchProgramGetsAnInchHeight()
    {
        var (text, report) = DryRun.Rewrite("""
            G20 G90
            G1 Z-0.075 F60
            G1 X1.0 F10
            M30
            """, new DryRunOptions { HeightMm = 25.4 });

        Assert.True(report.StaysClear);
        Assert.Contains("G20 G90", text, StringComparison.Ordinal);
        Assert.Contains("Z1.0000", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ItSaysWhatItIsAtTheTop()
    {
        var (text, _) = DryRun.Rewrite(Real(RealBoards.PogoTest1));
        var head = text.Split('\n').Take(8);

        Assert.Contains(head, l => l.Contains("DRY RUN", StringComparison.Ordinal));
        Assert.Contains(head, l => l.Contains("cuts nothing", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ what it refuses

    /// <summary>
    /// In incremental mode a Z word is a change, not a position, so substituting an absolute
    /// height sends the tool somewhere nobody asked for. Refusing is the only safe answer: a wrong
    /// dry run is worse than none, because it is trusted.
    /// </summary>
    [Fact]
    public void IncrementalModeIsRefusedRatherThanGuessedAt()
    {
        var (text, report) = DryRun.Rewrite("""
            G21 G90
            G0 X0 Y0
            G91
            G1 Z-1.0 F60
            M30
            """);

        Assert.NotNull(report.Refusal);
        Assert.Contains("G91", report.Refusal, StringComparison.Ordinal);
        Assert.False(report.StaysClear);

        // And the original is handed back untouched, so nothing half-rewritten can be run.
        Assert.Contains("Z-1.0", text, StringComparison.Ordinal);
    }

    /// <summary>G91.1 sets arc-centre mode and has nothing to do with distance mode.</summary>
    [Fact]
    public void ArcCentreModeIsNotMistakenForIncrementalMode()
    {
        var (_, report) = DryRun.Rewrite("""
            G21 G90 G91.1
            G0 X0 Y0
            G1 Z-1.0 F60
            M30
            """);

        Assert.Null(report.Refusal);
        Assert.True(report.StaysClear);
    }

    // ------------------------------------------------------------------ the rewrite itself

    [Fact]
    public void EveryZBecomesTheHeightAndNothingElseMoves()
    {
        var (text, _) = DryRun.Rewrite("""
            G21 G90
            G0 Z2.000
            G0 X10.500 Y-3.250
            G1 Z-1.900 F60
            G1 X20.000 Y-3.250 F200
            G0 Z2.000
            M30
            """, new DryRunOptions { HeightMm = 4 });

        Assert.Contains("G0 Z4.000", text, StringComparison.Ordinal);
        Assert.Contains("G21 G90", text, StringComparison.Ordinal);
        Assert.Contains("G1 Z4.000 F60", text, StringComparison.Ordinal);

        // X, Y and F are untouched.
        Assert.Contains("G0 X10.500 Y-3.250", text, StringComparison.Ordinal);
        Assert.Contains("G1 X20.000 Y-3.250 F200", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Z in a comment is prose. Rewriting it would corrupt the notes that say what the program
    /// does — including the ones stating how deep the real cut goes.
    /// </summary>
    [Fact]
    public void CommentsAreLeftAlone()
    {
        var (text, _) = DryRun.Rewrite("""
            ( cuts to Z-1.900 through the back )
            G21 G90
            G1 Z-1.900 F60
            M30
            """);

        Assert.Contains("( cuts to Z-1.900 through the back )", text, StringComparison.Ordinal);
        Assert.Contains("G1 Z5.000 F60", text, StringComparison.Ordinal);
    }

    /// <summary>M30 is the program end, not a spindle command; M3 must not match it.</summary>
    [Fact]
    public void TheProgramEndSurvives()
    {
        var (text, report) = DryRun.Rewrite("""
            G21 G90
            M3 S12000
            G1 Z-1.0 F60
            M30
            """);

        Assert.Equal(1, report.SpindleCommandsRemoved);
        Assert.Contains("M30", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FeedsAreKeptSoTheTimeIsHonest()
    {
        var (kept, _) = DryRun.Rewrite("G1 X10 F200", new DryRunOptions());
        var (fast, _) = DryRun.Rewrite(
            "G1 X10 F200", new DryRunOptions { KeepFeeds = false, RapidMmPerMin = 2000 });

        Assert.Contains("F200", kept, StringComparison.Ordinal);
        Assert.Contains("F2000", fast, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyProgramIsNotAnError()
    {
        var (_, report) = DryRun.Rewrite(string.Empty);

        Assert.Null(report.Refusal);
        Assert.True(report.StaysClear);
    }
}
