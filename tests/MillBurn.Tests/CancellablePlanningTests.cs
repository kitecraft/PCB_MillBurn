using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Sprint 2 story 3 — 6.33: a superseded plan stops, rather than stopping being watched.
///
/// **What sprint 1 actually shipped.** `CancellableWorkTests` proves a superseded preview is not
/// published and that `PreviewBuild` stops between programs. Neither is planning. `ExportPlanner`
/// took no token, so the abandoned run planned the whole board on its pool thread and then found
/// nobody wanted it. From the bench, on a six-layer i.MX8M board where a preview takes seven to
/// eight seconds: *"the new click does interrupt the in-progress and cause a 'remembered in 0.11 s'
/// message."* The window was right and the message was right; the interruption was not one.
///
/// **What that cost.** Nothing bounded how many abandoned runs were in flight. Five nudges on a
/// board that plans in sixteen seconds left five full plans running at once, each holding its own
/// intermediate geometry, while the operator watched a sixth — and the symptom is not a
/// cancellation bug, it is the application going slow and hungry some minutes after they stopped
/// doing anything unusual.
///
/// **Everything here is local to its own run.** The work counters and the memo's hit and miss
/// tallies are global by necessity, and this assembly runs its classes in parallel — so what those
/// numbers say about cancellation lives in `CancelledPlanningWorkTests`, over in the assembly that
/// runs one test at a time for exactly that reason. Found the way that bug is always found: they
/// passed under `--filter` and failed the moment the suite ran.
/// </summary>
public sealed class CancellablePlanningTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ it stops where it is

    /// <summary>
    /// A plan stops where it is, rather than running the board to the end.
    ///
    /// **Cancelling before the call would prove nothing**, and that is not a hypothetical: the
    /// sprint-1 cancellation test did exactly that, so a single check at the top of the method
    /// satisfied it and nothing proved a running job stopped. Neither would cancelling on the
    /// second settings lookup, which is the mistake the first draft of this test made — resolving
    /// the blank scans the settings before the layer loop runs, so the first three asks on this
    /// board are not layers, and "two layers in" was in fact "before any layer at all".
    ///
    /// **So a whole run is measured first and the stop is put halfway through it.** The number is
    /// a property of this board rather than a constant worth writing down, and taking half of it
    /// lands well inside the layer loop however the blank's scan changes length.
    ///
    /// **Both thicknesses belong to this test alone**, and that is not tidiness. The memo is keyed
    /// on the settings, and a plan another test left behind at the same thickness would be handed
    /// back before `Build` ran at all — the wrapper would never be asked for a layer, nothing
    /// would cancel, and this would fail for a reason that has nothing to do with the planner.
    /// xUnit fixes no order within a class, so it would fail on some runs and not others.
    /// </summary>
    [Fact]
    public void PlanningStopsBeforeTheEndOfTheBoard()
    {
        var board = PlanningFixture.Board();
        var outputs = PlanningFixture.Outputs(board);

        using var never = new CancellationTokenSource();
        var whole = new CancelsAfterLayers(outputs, int.MaxValue, never);

        _ = ExportPlanner.Plan(
            board, whole, ToolLibrary.Default, Nm.FromMillimetres(5.6), OutputKind.Gcode);

        Assert.True(whole.Reached > 8, $"a run of {whole.Reached} lookups cannot show an early stop");

        using var stop = new CancellationTokenSource();
        var settings = new CancelsAfterLayers(outputs, whole.Reached / 2, stop);

        Assert.Throws<OperationCanceledException>(
            () => ExportPlanner.Plan(
                board, settings, ToolLibrary.Default, Nm.FromMillimetres(5.5), OutputKind.Gcode,
                token: stop.Token));

        output.WriteLine(
            $"a whole run asks about {whole.Reached} settings; this one stopped after "
            + $"{settings.Reached}");

        Assert.True(
            settings.Reached < whole.Reached,
            $"the run made all {settings.Reached} of a whole board's lookups before it threw, so "
            + "it finished and then reported a cancellation rather than stopping at one.");
    }

    /// <summary>
    /// A cancelled run leaves nothing half-built behind it for the next one to find.
    ///
    /// **This is the hazard, not the housekeeping.** The memo stores a plan under the settings that
    /// made it, and a plan abandoned five layers into a fifteen-layer board is a plan with most of
    /// the board missing. Stored, the next preview at those settings would be served it —
    /// instantly, and looking entirely normal — and the operator would export a job with no bottom
    /// copper and no outline. `PlannedExports.Get` calls the builder outside its lock and stores
    /// only what the builder returns, so a throw stores nothing. Pinned here because the tempting
    /// optimisation is to keep the part that was finished.
    ///
    /// Asserted against the last layer in draw order that makes G-code, which a truncated plan
    /// cannot have reached. Nothing here reads a counter, so it holds however many other tests are
    /// planning beside it.
    /// </summary>
    [Fact]
    public void ACancelledRunLeavesNoHalfBuiltPlanBehind()
    {
        var board = PlanningFixture.Board();
        var outputs = PlanningFixture.Outputs(board);
        var thickness = Nm.FromMillimetres(3.2);

        // **The assumption this rests on, checked rather than trusted.** The cancelled run passes a
        // wrapper and the run after it passes the raw dictionary; if those produced different memo
        // keys, a truncated plan stored under the first could never be served to the second and
        // the assertion at the end would pass while proving nothing. At a thickness nothing else
        // here uses, so it cannot answer the sequence below from the memo.
        using var never = new CancellationTokenSource();
        var probe = Nm.FromMillimetres(4.0);

        Assert.Same(
            ExportPlanner.Plan(board, outputs, ToolLibrary.Default, probe, OutputKind.Gcode),
            ExportPlanner.Plan(
                board, new CancelsAfterLayers(outputs, int.MaxValue, never), ToolLibrary.Default,
                probe, OutputKind.Gcode));

        var last = board.InDrawOrder()
            .Where(l => outputs.TryGetValue(l.FileName, out var s) && s.Output == OutputKind.Gcode)
            .Select(l => l.FileName)
            .Last();

        using var stop = new CancellationTokenSource();
        var abandoned = new CancelsAfterLayers(outputs, after: 5, stop);

        Assert.Throws<OperationCanceledException>(
            () => ExportPlanner.Plan(
                board, abandoned, ToolLibrary.Default, thickness, OutputKind.Gcode, token: stop.Token));

        var after = ExportPlanner.Plan(board, outputs, ToolLibrary.Default, thickness, OutputKind.Gcode);

        output.WriteLine(
            $"abandoned {abandoned.Reached} layers in; the plan that followed has {after.Count} "
            + $"program(s) and reaches {last}");

        Assert.Contains(
            after.Items,
            i => string.Equals(i.LayerFileName, last, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ how many at once

    /// <summary>
    /// Four previews in a row leave no plan running to the end but the last.
    ///
    /// **The ceiling 6.33 asked for, and it is not a count of how many start.** Four clicks start
    /// four plans however this is written; what changed is what they then do. Before this story all
    /// four ran the board to the end, so five nudges on a sixteen-second board meant eighty seconds
    /// of pool time nobody would ever read, and the memory to go with it. Now the superseded ones
    /// leave at their next check.
    ///
    /// Nothing is enforced — a semaphore would serialise previews, which is what sprint 1 moved off
    /// the UI thread to avoid — so the ceiling is a property rather than a number: the run nobody
    /// superseded produces a plan, and at least one that was superseded does not. Not all three,
    /// because a run that finishes before the cancellation reaches it is behaving correctly, and
    /// 6.33 says to keep what it built.
    ///
    /// `PeakPlansInFlight` is printed and not asserted. It is global, this assembly runs in
    /// parallel, and it counts how quickly this machine started four tasks — none of which is a
    /// fact about the fix.
    /// </summary>
    [Fact]
    public async Task SupersededRunsDoNotRunToTheEnd()
    {
        var board = PlanningFixture.Board();
        var outputs = PlanningFixture.Outputs(board);

        // Where "under way" is, measured the same way and for the same reason as above.
        using var never = new CancellationTokenSource();
        var whole = new CancelsAfterLayers(outputs, int.MaxValue, never);

        _ = ExportPlanner.Plan(
            board, whole, ToolLibrary.Default, Nm.FromMillimetres(6.4), OutputKind.Gcode);

        var midway = whole.Reached / 2;

        // **The same guard the test above has, and for a sharper reason here.** If this measuring
        // run were ever answered from the memo it would make no lookups, `midway` would be zero,
        // and `SignalsAtLayer` would signal on the very first one — which is the blank being
        // resolved, not a layer. Every run would then be superseded before it entered the layer
        // loop, the "reached the middle of the board" assertion below would pass on nothing, and
        // this would quietly become the cancel-at-the-top test it was written to replace. It would
        // still be green.
        Assert.True(
            whole.Reached > 8,
            $"a run of {whole.Reached} lookups cannot put anything halfway into the board.");

        using var runs = new LatestRun();
        var started = new List<Task<bool>>();
        var wrappers = new List<SignalsAtLayer>();

        // One gate per run, so each is held inside the board until its successor has taken its
        // place. Without them this test is a race it loses on a slow machine — see SignalsAtLayer.
        var holds = new List<ManualResetEventSlim>();

        // Four settings distinct from each other and from every other test's, so none can be
        // answered from the memo — the case where the work is real and abandoning it is worth
        // something, and the only case where the wait below can complete.
        for (var i = 0; i < 4; i++)
        {
            var thickness = Nm.FromMillimetres(6.0 + (0.1 * i));
            var underWay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hold = new ManualResetEventSlim(false);
            var settings = new SignalsAtLayer(outputs, midway, underWay, hold);
            var token = runs.Begin();

            holds.Add(hold);
            wrappers.Add(settings);

            started.Add(Task.Run(
                () =>
                {
                    try
                    {
                        _ = ExportPlanner.Plan(
                            board, settings, ToolLibrary.Default, thickness, OutputKind.Gcode,
                            token: token);

                        return true;
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                },
                CancellationToken.None));

            // **Before superseding it.** Starting four and cancelling them as fast as the loop goes
            // round supersedes most of them before the pool has begun them, and then a token read
            // once at the top of `Plan` would satisfy the assertions below — the sprint-1 failure
            // mode, reproduced in the test written to catch it. Waiting here means each run is
            // halfway through the board's settings when the next one takes its place.
            // Bounded, because the failure is otherwise a hang rather than a message: the signal
            // comes from the settings wrapper, so a run served out of the memo never reaches it
            // and this would wait for the harness to give up. Generous enough that a loaded
            // machine is slow here rather than red.
            await underWay.Task.WaitAsync(TimeSpan.FromSeconds(60));

            // This run is now stopped inside the board, and `Begin` above has already cancelled
            // its predecessor — which is also stopped inside the board. Let that one go: it
            // resumes, reaches its next check, and finds a token that is already cancelled.
            if (i > 0)
            {
                holds[i - 1].Set();
            }
        }

        // And the last, which nobody superseded, so it runs to a plan.
        holds[^1].Set();

        var finished = await Task.WhenAll(started);

        foreach (var hold in holds)
        {
            hold.Dispose();
        }

        output.WriteLine(
            $"{finished.Count(f => f)} of {finished.Length} ran to a plan; reached "
            + $"{string.Join(", ", wrappers.Select(w => w.Reached))} layers; peak in flight "
            + $"{ExportPlanner.PeakPlansInFlight}");

        Assert.All(
            wrappers,
            w => Assert.True(
                w.Reached >= midway,
                $"a run made only {w.Reached} of the {midway} lookups that put it inside the board, "
                + "so being cancelled says nothing about stopping part-way."));
        Assert.True(finished[^1], "the run nobody superseded did not produce a plan.");
        Assert.Contains(false, finished);
    }
}
