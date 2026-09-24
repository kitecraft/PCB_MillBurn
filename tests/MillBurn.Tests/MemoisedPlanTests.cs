using System.Diagnostics;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Sprint 1 story 2, the second half: planning a board that has not changed is not planned again.
///
/// Planning is the expensive part — isolating the copper, ordering the travel, writing every
/// program's text — and a preview repeats all of it even when nothing about the board or the
/// settings has moved. What these guard is that the work is skipped only when skipping it cannot
/// change the answer.
/// </summary>
[Collection("memo")]
public sealed class MemoisedPlanTests(ITestOutputHelper output)
{
    private static Board Board() =>
        BoardLoader.LoadSources(
            "test",
            Directory.EnumerateFiles(RealBoards.Directory(RealBoards.PogoTest1))
                .Where(BoardLoader.IsBoardFile)
                .Order(StringComparer.Ordinal)
                .Select(f => (
                    Path.GetFileName(f),
                    File.ReadAllBytes(f),
                    LayerRoles.FromFileName(Path.GetFileName(f)))));

    private static Dictionary<string, LayerOutputSettings> Outputs(Board board) =>
        board.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);

    private static ExportPlan Plan(Board board, Dictionary<string, LayerOutputSettings> outputs) =>
        ExportPlanner.Plan(board, outputs, ToolLibrary.Default, Nm.FromMillimetres(1.6));

    /// <summary>
    /// The same board and the same settings give back the plan already built, and give it back
    /// quickly enough to be worth having done.
    ///
    /// The timing is printed rather than asserted. A stopwatch bound is a flaky test on somebody
    /// else's machine, and <c>Assert.Same</c> already proves the work was skipped — the number is
    /// here so that whoever reads this knows what it bought.
    /// </summary>
    [Fact]
    public void PlanningTheSameBoardTwicePlansItOnce()
    {
        var board = Board();
        var outputs = Outputs(board);

        PlannedExports.Forget();

        var cold = Stopwatch.StartNew();
        var first = Plan(board, outputs);
        cold.Stop();

        var warm = Stopwatch.StartNew();
        var second = Plan(board, outputs);
        warm.Stop();

        output.WriteLine(
            $"planned in {cold.Elapsed.TotalMilliseconds:F0} ms, remembered in {warm.Elapsed.TotalMilliseconds:F1} ms");

        Assert.Same(first, second);
        Assert.Equal(1, PlannedExports.Misses);
        Assert.Equal(1, PlannedExports.Hits);
    }

    /// <summary>
    /// Changing one layer's output changes the plan, and is not served the old one.
    ///
    /// This is the direction that matters. A memo that is too eager to skip does not run slowly —
    /// it emits the previous board's G-code for the current board, which is a file that looks
    /// entirely normal and cuts the wrong thing.
    /// </summary>
    [Fact]
    public void ChangingWhatALayerBecomesChangesThePlan()
    {
        var board = Board();
        var outputs = Outputs(board);

        PlannedExports.Forget();

        var before = Plan(board, outputs);

        var silk = board.Layers.First(l => l.Role == LayerRole.TopSilk).FileName;
        outputs[silk] = outputs[silk] with { Output = OutputKind.Svg };

        var after = Plan(board, outputs);

        output.WriteLine($"{before.Count} file(s) became {after.Count}");

        Assert.NotSame(before, after);
        Assert.Equal(2, PlannedExports.Misses);
        Assert.Equal(0, PlannedExports.Hits);
        Assert.NotEqual(before.Count, after.Count);
    }

    /// <summary>
    /// A different thickness is a different plan, even though nothing about the board moved.
    ///
    /// Depths come from the thickness, so a plan served across a change to it would cut to the
    /// wrong depth while every picture of it looked right.
    /// </summary>
    [Fact]
    public void ADifferentThicknessIsADifferentPlan()
    {
        var board = Board();
        var outputs = Outputs(board);

        PlannedExports.Forget();

        var thin = ExportPlanner.Plan(board, outputs, ToolLibrary.Default, Nm.FromMillimetres(0.8));
        var thick = ExportPlanner.Plan(board, outputs, ToolLibrary.Default, Nm.FromMillimetres(1.6));

        Assert.NotSame(thin, thick);
        Assert.Equal(2, PlannedExports.Misses);

        // Two objects is also what "remembers nothing" produces, and the doc above claims this
        // guards against cutting to the wrong depth — so the programs themselves must differ.
        Assert.NotEqual(
            thin.Items.Select(i => i.Content).ToList(),
            thick.Items.Select(i => i.Content).ToList());
    }

    /// <summary>
    /// A setting nudged up and down is still remembered on the way back.
    ///
    /// From the bench, on the Arduino Mega: returning to a thickness used a moment earlier was
    /// instant, but returning to one used six changes ago planned again from scratch — two seconds
    /// of work thrown away because the cache counted plans rather than weighing them, and four was
    /// the count. That is the shape of real use: change a number, look at it, change it back.
    /// </summary>
    [Fact]
    public void ASettingNudgedAndPutBackIsStillRemembered()
    {
        var board = Board();
        var outputs = Outputs(board);

        PlannedExports.Forget();

        // Ten thicknesses, well past the old limit of four plans.
        var thicknesses = Enumerable.Range(8, 10).Select(mm => mm / 10.0).ToList();

        foreach (var mm in thicknesses)
        {
            ExportPlanner.Plan(board, outputs, ToolLibrary.Default, Nm.FromMillimetres(mm));
        }

        var planned = PlannedExports.Misses;
        Assert.Equal(thicknesses.Count, planned);

        // Back to the first one, which on a count of four was long gone.
        var again = ExportPlanner.Plan(
            board, outputs, ToolLibrary.Default, Nm.FromMillimetres(thicknesses[0]));

        output.WriteLine($"{planned} planned, then {PlannedExports.Hits} remembered on the way back");

        Assert.Equal(planned, PlannedExports.Misses);
        Assert.Equal(1, PlannedExports.Hits);
        Assert.NotEmpty(again.Items);
    }

    /// <summary>
    /// The story's own acceptance criterion, on the largest board in the corpus.
    ///
    /// The timing is printed and not asserted, for the reason the class comment gives — a stopwatch
    /// bound fails on somebody else's machine for reasons that have nothing to do with the code.
    /// <c>Assert.Same</c> is what proves the work was skipped; the number is what says by how much,
    /// and it belongs in the sprint document rather than in an assertion.
    /// </summary>
    [Fact]
    public void ThePanelIsPlannedOnceAndThenRemembered()
    {
        var folder = RealBoards.Directory(RealBoards.Panel);

        var board = BoardLoader.LoadSources(
            "panel",
            Directory.EnumerateFiles(folder)
                .Where(BoardLoader.IsBoardFile)
                .Order(StringComparer.Ordinal)
                .Select(f => (
                    Path.GetFileName(f),
                    File.ReadAllBytes(f),
                    LayerRoles.FromFileName(Path.GetFileName(f)))));

        var outputs = Outputs(board);

        PlannedExports.Forget();
        RealisedLayers.Forget();

        var cold = Stopwatch.StartNew();
        var first = ExportPlanner.Plan(board, outputs, ToolLibrary.Default, Nm.FromMillimetres(1.6));
        cold.Stop();

        var warm = Stopwatch.StartNew();
        var second = ExportPlanner.Plan(board, outputs, ToolLibrary.Default, Nm.FromMillimetres(1.6));
        warm.Stop();

        output.WriteLine(
            $"panel: {board.Layers.Count} layers, {first.Count} programs — " +
            $"planned in {cold.Elapsed.TotalMilliseconds:F0} ms, remembered in {warm.Elapsed.TotalMilliseconds:F1} ms");

        Assert.Same(first, second);

        // Assert.Same holds just as well for an empty plan, and an empty plan is what a board that
        // failed to load looks like.
        Assert.NotEmpty(first.Items);
    }

    /// <summary>
    /// Two projects holding the same Gerbers are still two projects.
    ///
    /// Where a board came from is not bookkeeping: the stock program is named from it, and it is
    /// printed as the board's name on the project page and on the drill and routing guides. Save a
    /// project under a second name, or import one export folder twice — which 6.32 says is easily
    /// done, the author's own recent list holding three entries called Millburn_Test_Board — and
    /// the layers are byte-identical while the paperwork must not be. Served from one entry, the
    /// second project writes the first one's file names beside programs about to be run.
    /// </summary>
    [Fact]
    public void TwoProjectsWithTheSameGerbersDoNotShareAPlan()
    {
        var first = Board();
        var outputs = Outputs(first);

        // The same layers exactly — same instances, same fingerprints — opened from elsewhere.
        var second = first with { Source = "somewhere/else/Millburn_Test_Board" };

        PlannedExports.Forget();

        var one = Plan(first, outputs);
        var two = Plan(second, outputs);

        output.WriteLine($"{PlannedExports.Misses} planned, {PlannedExports.Hits} remembered");

        Assert.NotSame(one, two);
        Assert.Equal(2, PlannedExports.Misses);
        Assert.Equal(0, PlannedExports.Hits);
    }

    /// <summary>
    /// A board whose layers cannot be identified is planned every time rather than guessed at.
    ///
    /// Layers realised by a path that does not memoise carry no fingerprint, so there is nothing to
    /// compare. Refusing to remember is the only safe answer: the alternative is keying on
    /// something weaker and occasionally handing back another board's programs.
    /// </summary>
    [Fact]
    public void ABoardWithoutFingerprintsIsNeverRemembered()
    {
        var board = Board();
        var outputs = Outputs(board);

        // As if realised by the folder path, which does not go through the memo.
        var anonymous = board with
        {
            Layers = [.. board.Layers.Select(l => l with { Fingerprint = string.Empty })],
        };

        PlannedExports.Forget();

        var first = Plan(anonymous, outputs);
        var second = Plan(anonymous, outputs);

        output.WriteLine($"{PlannedExports.Unidentifiable} plan(s) not looked for");

        Assert.NotSame(first, second);
        Assert.Equal(2, PlannedExports.Unidentifiable);
        Assert.Equal(0, PlannedExports.Hits);

        // And the answers still agree, which is what says the memo is an optimisation and not a
        // behaviour.
        Assert.NotEmpty(first.Items);
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(
            first.Items.Select(i => i.Content),
            second.Items.Select(i => i.Content));
    }
}
