using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Sprint 1 story 1: the preview is built off the UI thread, and an edit that supersedes another
/// cancels it rather than letting it finish and overwrite the newer answer.
///
/// The freezing itself is not what these guard — a stopwatch around a pipeline is a flaky test on
/// somebody else's machine. What they guard is the two things that make the fix correct: the work
/// is reachable without a window, and a superseded run stops.
/// </summary>
public sealed class CancellableWorkTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ one run at a time

    /// <summary>Beginning a second run cancels the first, and only the first.</summary>
    [Fact]
    public void ANewRunCancelsTheOneItSupersedes()
    {
        using var runs = new LatestRun();

        var first = runs.Begin();
        Assert.False(first.IsCancellationRequested);

        var second = runs.Begin();

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
        Assert.True(runs.IsRunning);
    }

    /// <summary>
    /// A superseded run cannot clear the run that replaced it.
    ///
    /// This is the race the whole class exists for: the old run finishes a moment after the new one
    /// starts, and if finishing meant "clear whatever is current" it would report the newer run
    /// idle while it is still going.
    /// </summary>
    [Fact]
    public void AFinishedRunThatIsNoLongerCurrentChangesNothing()
    {
        using var runs = new LatestRun();

        var first = runs.Begin();
        var second = runs.Begin();

        // False is the answer the caller acts on: a superseded run must not publish its result
        // either, and this is where it finds out that it has been replaced.
        Assert.False(runs.Finish(first));
        Assert.True(runs.IsRunning);

        Assert.True(runs.Finish(second));
        Assert.False(runs.IsRunning);

        // And finishing twice is not a second chance to publish.
        Assert.False(runs.Finish(second));
    }

    /// <summary>
    /// A stale run finishing *after* the one that replaced it cannot undo it.
    ///
    /// The order is the whole test: the newer run finishes first and clears itself, and then the
    /// older one arrives. Turning it away has to mean leaving things alone — not reporting a run
    /// in progress again, and not returning true to a caller that would then publish its picture
    /// over the newer one's.
    ///
    /// It is worth saying what this does *not* reach, because the name used to claim it did. The
    /// dangerous case is a run that completes without ever noticing it was cancelled, and
    /// <see cref="LatestRun"/> cannot produce one — <c>Begin</c> cancels the run it supersedes, so
    /// every stale token here is a cancelled token. That case lives where a task's continuation is
    /// pumped after a newer run has started, which is the view model's ordering and not this
    /// class's.
    /// </summary>
    [Fact]
    public void AStaleRunFinishingLastCannotUndoTheOneThatReplacedIt()
    {
        using var runs = new LatestRun();

        var slow = runs.Begin();
        var quick = runs.Begin();

        // The quick one lands first and is allowed to publish.
        Assert.True(runs.Finish(quick));

        // The slow one now completes. It never observed its cancellation — but it is not current,
        // so it is refused, and it cannot clear what the quick one left behind.
        Assert.False(runs.Finish(slow));
        Assert.False(runs.IsRunning);
    }

    /// <summary>Disposing stops what is running, and anything begun afterwards is already over.</summary>
    [Fact]
    public void DisposingStopsTheRunAndRefusesNewOnes()
    {
        var runs = new LatestRun();
        var running = runs.Begin();

        runs.Dispose();

        Assert.True(running.IsCancellationRequested);
        Assert.True(runs.Begin().IsCancellationRequested);
        Assert.False(runs.IsRunning);
    }

    // ------------------------------------------------------------------ the work itself

    private static ExportPlan TestBoardPlan()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        var settings = board.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);

        return ExportPlanner.Plan(
            board, settings, ToolLibrary.Default, Nm.FromMillimetres(0.8), OutputKind.Gcode);
    }

    /// <summary>
    /// The preview is built from a plan and nothing else — no window, no view model, no dispatcher.
    ///
    /// That is the property that lets it run on the thread pool. A test that can build one from a
    /// plain plan is the same test as "this no longer needs the UI thread".
    /// </summary>
    [Fact]
    public void ThePreviewIsBuiltFromAPlanAndNothingElse()
    {
        var plan = TestBoardPlan();
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        var preview = PreviewBuild.From(
            plan, board.Bounds, new MachineSettings().Profile, CancellationToken.None);

        output.WriteLine(preview.Summary);

        Assert.Equal(plan.Count, preview.ProgramCount);
        Assert.NotEmpty(preview.Backplot);
        Assert.NotEmpty(preview.Gcode);
        Assert.Contains("mm cut", preview.Summary, StringComparison.Ordinal);

        // Every program's text reaches the viewer's copy, which is what the G-code pane shows.
        foreach (var item in plan.Items)
        {
            Assert.Contains(item.Content, preview.Gcode, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A cancelled run stops rather than returning a half-built answer.
    ///
    /// It throws instead of returning null or an empty preview, so a caller cannot mistake "the
    /// operator moved on" for "this board has nothing to cut" and write that to the status line.
    /// </summary>
    [Fact]
    public void ACancelledPreviewStopsInsteadOfReturningSomethingWrong()
    {
        var plan = TestBoardPlan();
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => PreviewBuild.From(plan, board.Bounds, new MachineSettings().Profile, cancelled.Token));
    }

    /// <summary>
    /// A run already under way stops where it is, rather than finishing the board it has started.
    ///
    /// Cancelling before the call proves only that the token is read once, at the top. This drives
    /// the cancellation from inside the plan's own enumeration, so the run is provably part-way
    /// through when it is superseded — which is the case the operator actually creates, by editing
    /// again while the last preview is still going.
    /// </summary>
    [Fact]
    public void APreviewCancelledPartWayThroughStopsWhereItIs()
    {
        var plan = TestBoardPlan();
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        // Two programs in, and enough of them left that stopping is distinguishable from finishing.
        Assert.True(plan.Items.Count > 3, $"a plan of {plan.Items.Count} cannot show an early stop");

        using var stop = new CancellationTokenSource();
        var items = new CancelsPartWayThrough(plan.Items, after: 2, stop);

        Assert.Throws<OperationCanceledException>(
            () => PreviewBuild.From(
                plan with { Items = items }, board.Bounds, new MachineSettings().Profile, stop.Token));

        output.WriteLine($"read {items.Read} of {plan.Items.Count} programs before stopping");

        Assert.Equal(3, items.Read);
        Assert.True(items.Read < plan.Items.Count);
    }

    /// <summary>
    /// A plan whose items cancel the run once it has read past <paramref name="after"/> of them.
    ///
    /// The cancellation happens as the next item is handed over, so the run is genuinely in the
    /// middle of the list when its token turns — no sleeping, no racing, and the same answer on a
    /// slow machine as on a fast one.
    /// </summary>
    /// <param name="items">The real plan's items, handed out in order.</param>
    /// <param name="after">How many to hand over before cancelling.</param>
    /// <param name="stop">Cancelled once that many have been read.</param>
    private sealed class CancelsPartWayThrough(
        IReadOnlyList<ExportItem> items, int after, CancellationTokenSource stop)
        : IReadOnlyList<ExportItem>
    {
        /// <summary>How many items the run asked for before it stopped.</summary>
        public int Read { get; private set; }

        public int Count => items.Count;

        public ExportItem this[int index] => items[index];

        public IEnumerator<ExportItem> GetEnumerator()
        {
            foreach (var item in items)
            {
                Read++;

                if (Read > after)
                {
                    stop.Cancel();
                }

                yield return item;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    /// <summary>The same plan built twice gives the same answer, whichever thread it ran on.</summary>
    [Fact]
    public async Task BuildingOnTheThreadPoolGivesTheSameAnswerAsBuildingHere()
    {
        var plan = TestBoardPlan();
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));
        var profile = new MachineSettings().Profile;

        var here = PreviewBuild.From(plan, board.Bounds, profile, CancellationToken.None);
        var there = await Task.Run(
            () => PreviewBuild.From(plan, board.Bounds, profile, CancellationToken.None));

        Assert.Equal(here.Summary, there.Summary);
        Assert.Equal(here.Gcode, there.Gcode);
        Assert.Equal(here.Backplot.Count, there.Backplot.Count);
        Assert.Equal(here.GougeCount, there.GougeCount);
    }
}
