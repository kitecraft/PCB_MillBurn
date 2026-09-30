using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Checking a board without exporting it.
///
/// **The fault.** Board-level checks run on load; the electrical ones arrive through the export
/// plan. So a board with a short on it shows nothing in the CHECK panel until somebody presses
/// Preview or Export — the operator most at risk being the one who looks at the panel, sees it
/// empty, and believes it.
///
/// These tests are about the check being reachable, using the cut the export would make, and
/// stopping when it is told to. The window's button is in `MillBurn.App`, which the tests cannot
/// reach, so that half is verified by running it.
///
/// Recorded in [06 §6.44](../../Documentation/06-Roadmap-and-Risks.md).
/// </summary>
public sealed class BoardCheckTests(ITestOutputHelper output)
{
    private static (Board Board, Dictionary<string, LayerOutputSettings> Settings) Load(string board)
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

        return (loaded, settings);
    }

    /// <summary>
    /// The Mega's short is found without planning anything.
    ///
    /// This is the whole story in one assertion. The board is loaded, the check is asked, and the
    /// copper that will not come apart is named — with no export, no preview, and no G-code
    /// generated at all.
    /// </summary>
    [Fact]
    public void AShortIsFoundWithoutExportingAnything()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);

        var result = BoardCheck.For(board, settings, ToolLibrary.Default);

        output.WriteLine($"{result.LayersChecked} layer(s) in {result.Elapsed.TotalMilliseconds:F0} ms");
        output.WriteLine(string.Join("\n", result.Checks.Select(c => $"[{c.Severity}] {c.Source.Label}: {c.Message}")));

        Assert.False(result.Cancelled);
        Assert.True(result.LayersChecked > 0, "no layer was checked, so nothing above means anything");

        Assert.Contains(
            result.Checks,
            c => c.Kind == CheckKind.Electrical && c.Severity == CheckSeverity.Refusal);
    }

    /// <summary>
    /// It answers about the cut the export would make, not about some other cut.
    ///
    /// **The failure this guards is a check that is worse than none**, because it would be
    /// believed. The tool and the isolation options come from the same two methods the planner
    /// uses; this pins that the findings that result are the ones the export shows for the same
    /// layer, which is the only way to know the operator is being told about the job they are
    /// about to run.
    /// </summary>
    [Fact]
    public void ItAnswersAboutTheCutTheExportWouldMake()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);
        var top = board.Layers.Single(l => l.Role == LayerRole.TopCopper);

        // Filtered to Electrical on both sides. It was only filtered on the export side at first,
        // which makes the comparison asymmetric: any non-electrical check the board check might
        // one day attribute to this layer would fail the test for a reason that has nothing to do
        // with the two paths agreeing about copper.
        var checkedNow = BoardCheck.For(board, settings, ToolLibrary.Default)
            .Checks
            .Where(c => c.Source.FileName == top.FileName && c.Kind == CheckKind.Electrical)
            .Select(c => c.Line)
            .ToList();

        var fromExport = ExportPlanner
            .Plan(board, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode)
            .Items
            .Where(i => i.LayerFileName == top.FileName)
            .SelectMany(i => i.Warnings)
            .Where(c => c.Kind == CheckKind.Electrical)
            .Select(c => c.Line)
            .ToList();

        output.WriteLine($"check: {checkedNow.Count} line(s), export: {fromExport.Count} line(s)");

        Assert.NotEmpty(fromExport);
        Assert.Equal(fromExport, checkedNow);
    }

    /// <summary>
    /// A layer that is not being cut is not checked.
    ///
    /// Reporting a short in copper nobody is going to machine gives the operator something they
    /// cannot act on, and the cut width that decides the answer belongs to an operation that is
    /// not going to happen.
    /// </summary>
    [Fact]
    public void ALayerThatIsNotBeingCutIsNotChecked()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);

        var withCopper = BoardCheck.For(board, settings, ToolLibrary.Default);

        foreach (var key in settings.Keys.ToList())
        {
            settings[key] = settings[key] with { Output = OutputKind.None };
        }

        var withNothing = BoardCheck.For(board, settings, ToolLibrary.Default);

        output.WriteLine($"copper on: {withCopper.LayersChecked} layer(s); everything off: {withNothing.LayersChecked}");

        Assert.True(withCopper.LayersChecked > 0);
        Assert.Equal(0, withNothing.LayersChecked);
        Assert.Empty(withNothing.Checks);
    }

    /// <summary>
    /// A run stopped **while it was under way** says it was cancelled rather than reporting a
    /// clean board.
    ///
    /// **The distinction sprint 2's story 3 was about**, applied here before it can go wrong: a run
    /// that stopped early has looked at some of the board, and a short list from it must never read
    /// as "nothing found". A caller that cannot tell the two apart shows an operator a clean panel
    /// for a board nobody finished checking.
    ///
    /// **Written wrongly first, in exactly the way CLAUDE.md warns about.** The first version
    /// cancelled the token before the call — which a single `IsCancellationRequested` at the top of
    /// the method satisfies, proving nothing about a *running* check stopping. That is the
    /// `LatestRun` failure verbatim, and a `/describe-test` pass said so: *"a run cancelled
    /// part-way through, which is the situation the doc comment describes, is not exercised."*
    ///
    /// `CancelsAfterLayers` is the repository's existing answer: it cancels on the *second* settings
    /// lookup, so the check has genuinely started on the board and finished one layer before the
    /// token turns. The assertions below are about that middle state, which is the only one worth
    /// testing.
    /// </summary>
    [Fact]
    public void ARunStoppedPartWayThroughSaysSoRatherThanLookingClean()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);

        var whole = BoardCheck.For(board, settings, ToolLibrary.Default);

        // The guard that stops this becoming the cancel-at-the-top test it replaced: there has to
        // be more than one layer to check, or "stopped part way" has no middle to stop in.
        Assert.True(
            whole.LayersChecked > 1,
            $"this board checks {whole.LayersChecked} layer(s); one cannot be stopped part way");

        using var stop = new CancellationTokenSource();

        // **Cut off on the first copper layer's own lookup**, which is the only place that leaves
        // the run genuinely half done. Counting in layers does not work here: the Mega has
        // thirteen layers and only two of them are isolated, so a cut-off expressed as "most of
        // the way through the list" lands after both have already been checked — which the first
        // attempt did, reaching thirteen lookups and checking both.
        //
        // The check reads the token *before* each layer's lookup, so cancelling on the first
        // copper layer's lookup lets that layer finish and stops the loop at the next one.
        var first = board.Layers
            .Select((l, i) => (Layer: l, Index: i))
            .First(x => settings.TryGetValue(x.Layer.FileName, out var s)
                && LayerOperations.For(x.Layer.Role, s.Output) == OperationKind.Isolation);

        output.WriteLine($"first isolated layer is {first.Layer.Label} at index {first.Index}");

        var abandoned = new CancelsAfterLayers(settings, after: first.Index, stop);

        var result = BoardCheck.For(board, abandoned, ToolLibrary.Default, stop.Token);

        output.WriteLine(
            $"whole run checked {whole.LayersChecked} layer(s); "
            + $"the abandoned one reached {abandoned.Reached} lookup(s) "
            + $"and checked {result.LayersChecked}");

        Assert.True(result.Cancelled, "a run that stopped part way reported itself complete");

        // **It got into the board and did not get through it**, and both halves matter: a run that
        // checked nothing proves only the top-of-method test — the flaw this test was rewritten to
        // remove — and a run that checked everything was never stopped at all.
        //
        // Stated in layers checked rather than in lookups. The first attempt at this line asserted
        // on the lookup count and failed on a board whose first layer is copper, where one lookup
        // is all it takes to be half done. Lookups are how the cut-off is *aimed*; layers are what
        // "part way" actually means.
        Assert.True(result.LayersChecked > 0, "the check stopped before it looked at any copper");
        Assert.True(
            result.LayersChecked < whole.LayersChecked,
            "the run was not actually stopped: it checked the whole board");
    }
}
