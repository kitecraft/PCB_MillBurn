using MillBurn.Core;
using MillBurn.Pipeline;
using MillBurn.Tests;
using Xunit.Abstractions;

namespace MillBurn.GoldenTests;

/// <summary>
/// What a cancelled plan costs, and what it leaves in the memo — 6.33's other half.
///
/// **Here rather than beside the rest of story 3's tests, and `Parallelism.cs` is the reason.** Both
/// questions are answered by globals: `Work` counts every Clipper call in the process, and
/// `PlannedExports.Hits`/`Misses` count every plan anybody asks for. In `MillBurn.Tests`, where
/// classes run side by side, a second class planning a board while one of these measures lands in
/// the reading — and the earlier draft of these tests proved it, passing under `--filter` and
/// failing the moment the suite ran. This assembly runs one test at a time, for exactly the reason
/// `WorkSnapshotTests` needed it to.
/// </summary>
public sealed class CancelledPlanningWorkTests(ITestOutputHelper output)
{
    private static ExportPlan Plan(
        Board board,
        IReadOnlyDictionary<string, LayerOutputSettings> settings,
        double thicknessMm,
        CancellationToken token = default) =>
        ExportPlanner.Plan(
            board, settings, ToolLibrary.Default, Nm.FromMillimetres(thicknessMm), OutputKind.Gcode,
            token: token);

    /// <summary>
    /// A run stopped part-way does a fraction of the geometry, not all of it and then a throw.
    ///
    /// **This is the assertion the defect is really about.** Checking the token only at the end
    /// would satisfy every test that looks at the exception: the token would have turned, the
    /// exception would arrive, and the sixteen seconds would have been spent anyway. What tells the
    /// two apart is how much geometry each did — and `Work`'s counters are a property of the input
    /// and the Clipper build rather than of the machine, so this is a measurement and not a
    /// stopwatch.
    ///
    /// **Five lookups is where this board has something to say, and they are lookups rather than
    /// layers.** Resolving the blank scans the settings before the layer loop runs, so the first
    /// three asks on this board are not layers at all; the sixth is, and it is the third of them.
    /// Stopping any earlier does no counted geometry, which is indistinguishable from never
    /// starting; stopping one later is exactly half the vertices, which the bound below would call
    /// a failure. Here it is 12,426 of 133,908 — a little over nine per cent — so the run is
    /// provably under way and provably not finishing. Both memos are forgotten before each half,
    /// or the second would be reading the first's answers and this would be measuring caching.
    /// </summary>
    [Fact]
    public void ACancelledPlanDoesAFractionOfTheWork()
    {
        var board = PlanningFixture.Board();
        var outputs = PlanningFixture.Outputs(board);

        PlannedExports.Forget();
        RealisedLayers.Forget();

        using var stop = new CancellationTokenSource();
        var settings = new CancelsAfterLayers(outputs, after: 5, stop);
        var before = Work.Taken;

        Assert.Throws<OperationCanceledException>(() => { _ = Plan(board, settings, 0.8, stop.Token); });

        var cancelled = Work.Since(before);

        PlannedExports.Forget();
        RealisedLayers.Forget();

        before = Work.Taken;
        _ = Plan(board, outputs, 0.8);
        var whole = Work.Since(before);

        output.WriteLine($"stopped after {settings.Reached} lookup(s); did {cancelled}");
        output.WriteLine($"the whole board:          {whole}");

        // Where it got to, said rather than inferred. The vertex floor below implies the run
        // started, but not that it started *here* — a check that fired earlier while some other
        // layer's geometry happened to land in the counters would satisfy it.
        Assert.Equal(6, settings.Reached);

        // It ran. A run that had done nothing would mean the token was read before the work rather
        // than during it, and the comparison below would be measuring an early return dressed up as
        // a cancellation.
        Assert.True(
            cancelled.Vertices > 0,
            "a run stopped six lookups in did no geometry at all, so this is comparing a plan "
            + "against a method that returned before it started.");

        Assert.True(
            cancelled.Vertices * 2 < whole.Vertices,
            $"a run stopped six lookups in handed Clipper {cancelled.Vertices} vertices against "
            + $"{whole.Vertices} for the whole board, which is not a run that stopped.");
    }

    /// <summary>
    /// Going back to the setting you just left is still instant, which is the half of this 6.33
    /// said not to break.
    ///
    /// **And it is instant for a reason the bug report got slightly wrong.** The abandoned run being
    /// kept was credited with the *"remembered in 0.11 s"* the bench saw. It was not. The operator
    /// changed a setting, previewed, changed it back, and previewed again — so the plan remembered
    /// is the one for the setting they returned *to*, built and cached before any of this started.
    /// The abandoned plan was for the setting they left, and it would only ever pay if they went
    /// back to that. Which is why stopping early costs so little: this test is the common journey,
    /// and it is a memo hit either way.
    /// </summary>
    [Fact]
    public void ReturningToTheSettingYouLeftIsStillInstant()
    {
        var board = PlanningFixture.Board();
        var outputs = PlanningFixture.Outputs(board);

        // Forgotten first, so the first line below really is a build. Without it this board may
        // already be in the memo from another test, and "where the operator started" would be a
        // hit dressed as a plan — which does not break the assertions, all of which are sampled
        // later, but does make the story the test tells untrue.
        PlannedExports.Forget();

        // Where the operator started, planned and shown.
        var shown = Plan(board, outputs, 0.8);

        // They change something, and that plan is abandoned part-way.
        using var stop = new CancellationTokenSource();
        var nudged = new CancelsAfterLayers(outputs, after: 5, stop);

        Assert.Throws<OperationCanceledException>(() => { _ = Plan(board, nudged, 1.6, stop.Token); });

        // And they change it straight back.
        var hits = PlannedExports.Hits;
        var misses = PlannedExports.Misses;
        var again = Plan(board, outputs, 0.8);

        output.WriteLine(
            $"returning cost {PlannedExports.Misses - misses} miss(es) and "
            + $"{PlannedExports.Hits - hits} hit(s)");

        Assert.Same(shown, again);
        Assert.Equal(misses, PlannedExports.Misses);
        Assert.Equal(hits + 1, PlannedExports.Hits);
    }

    /// <summary>
    /// What was given up, said plainly: a plan that was cancelled is not remembered.
    ///
    /// Before this story it was — the run finished and cached on its way out — so an operator who
    /// nudged a setting, nudged back, and then nudged *forward* again got the second trip free. They
    /// pay for it now. That is the trade, it is the smaller half of the journey above, and it is
    /// asserted rather than left as a claim so a later change cannot quietly undo it in either
    /// direction. What must never be cached instead of nothing is a truncated plan, which
    /// `CancellablePlanningTests.ACancelledRunLeavesNoHalfBuiltPlanBehind` holds.
    /// </summary>
    [Fact]
    public void ACancelledPlanIsNotRemembered()
    {
        var board = PlanningFixture.Board();
        var outputs = PlanningFixture.Outputs(board);

        PlannedExports.Forget();

        using var stop = new CancellationTokenSource();
        var abandoned = new CancelsAfterLayers(outputs, after: 5, stop);

        Assert.Throws<OperationCanceledException>(() => { _ = Plan(board, abandoned, 2.4, stop.Token); });

        var misses = PlannedExports.Misses;
        var hits = PlannedExports.Hits;
        var rebuilt = Plan(board, outputs, 2.4);

        // And a third time, which must be free. Without this the assertion above reads "the second
        // request was a miss", which is equally true of a memo that remembers nothing at all — so
        // it would keep passing if caching broke altogether, and the fault it exists for is the
        // opposite one. The pair together say: this plan is cacheable, and the cancelled run did
        // not cache it.
        var free = Plan(board, outputs, 2.4);

        output.WriteLine(
            $"planning it again cost {PlannedExports.Misses - misses} miss(es); "
            + $"the trip after that cost {PlannedExports.Hits - hits} hit(s)");

        Assert.Equal(misses + 1, PlannedExports.Misses);
        Assert.Same(rebuilt, free);
        Assert.Equal(hits + 1, PlannedExports.Hits);
    }
}
