using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Every layer's electrical findings, collected in one place.
///
/// **What this file guards is the difference between a view and a report.** The CHECK panel is read
/// at a glance, caps what it says, and is fed one program at a time; this collects the whole board
/// so that the answers a warning line cannot carry have somewhere to live. The window that shows it
/// is in `MillBurn.App` and unreachable from here — 6.50 again — so everything below is about the
/// thing the window draws rather than the drawing.
///
/// Recorded in [06 §6.44](../../Documentation/06-Roadmap-and-Risks.md).
/// </summary>
public sealed class BoardFindingsTests(ITestOutputHelper output)
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
    /// A two-sided board answers in one place, with both sides in it.
    ///
    /// **This is the "scattered" half of the problem, stated as a test.** Each isolation program
    /// used to report its own findings, so a board with copper on both sides answered in two places
    /// in the export listing and nothing collected them. Reasoning across layers was impossible
    /// because no program saw another.
    /// </summary>
    [Fact]
    public void BothSidesOfABoardAnswerInOnePlace()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);

        var findings = BoardFindings.For(board, settings, ToolLibrary.Default);

        foreach (var layer in findings.Layers)
        {
            output.WriteLine(
                $"{layer.Label}: {layer.Joins.Count} group(s), {layer.Shorts.Count} short(s), "
                + $"{layer.SameNet.Count} same-net, {layer.Nameless.Count} nameless");
        }

        Assert.False(findings.Cancelled);
        Assert.Equal(2, findings.Layers.Count);
        Assert.Contains(findings.Layers, l => l.Role == LayerRole.TopCopper);
        Assert.Contains(findings.Layers, l => l.Role == LayerRole.BottomCopper);
    }

    /// <summary>
    /// On a job built on stock, a gap is reported from the stock's corner — because that is where
    /// work zero is.
    ///
    /// **Found by 6.56, which is the first thing to consume these coordinates geometrically.** The
    /// shift was the board's bounds unconditionally, so on a stock job the list printed a number in
    /// one frame while the checks panel three lines below said work zero was another: *"Cut the
    /// stock before anything else, and keep it the right way up — its lower-left corner is work
    /// zero for every other file in this export."* Printed, the error looked like a plausible
    /// coordinate and had been on screen since the findings view shipped. Rung on the board, it put
    /// the mark ten millimetres from the bridge it named.
    ///
    /// The border is asserted as a difference rather than as a remembered pair of numbers, so this
    /// catches a missing shift, a doubled one and a sign error, and does not have to be edited when
    /// the Mega's copper changes.
    /// </summary>
    [Fact]
    public void OnStockAGapIsReportedFromTheStocksCorner()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);

        var border = Nm.FromMillimetres(10);
        var stock = new Bounds(
            board.Bounds.MinX - border,
            board.Bounds.MinY - border,
            board.Bounds.MaxX + border,
            board.Bounds.MaxY + border);

        var plain = BoardFindings.For(board, settings, ToolLibrary.Default);
        var onStock = BoardFindings.For(board, settings, ToolLibrary.Default, stock);

        // Flattened in layer order and walked in lockstep, which assumes the two runs find the same
        // gaps in the same order. They do — the same board, the same settings, the same bit, and
        // the shift is the last thing applied — and the count check below is what would catch it if
        // that ever stopped being true.
        var from = plain.Layers.SelectMany(l => l.Gaps).ToList();
        var to = onStock.Layers.SelectMany(l => l.Gaps).ToList();

        Assert.NotEmpty(from);
        Assert.Equal(from.Count, to.Count);

        foreach (var (a, b) in from.Zip(to))
        {
            Assert.Equal(a.At.X + border, b.At.X);
            Assert.Equal(a.At.Y + border, b.At.Y);
        }
    }

    /// <summary>
    /// No frame given means the board's own, which is what a job with no stock uses.
    ///
    /// **`default(Bounds)` is not the sentinel it looks like**, and that is what this guards. It is
    /// `(0, 0, 0, 0)` — a zero-area rectangle at the origin — and `Bounds.IsEmpty` tests
    /// `MinX > MaxX`, so it reads as a real frame whose corner happens to be zero. Written as
    /// `Bounds frame = default` first, which silently gave every existing caller a shift of
    /// nothing; the parameter is nullable for that reason and this says so.
    /// </summary>
    [Fact]
    public void NoFrameMeansTheBoardsOwn()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);

        // **This board's artwork does not start at the origin, and the last assertion needs that.**
        // Shifting by `default(Bounds)` means shifting by nothing, which is only distinguishable
        // from shifting by the board's corner when that corner is somewhere else. Stated rather
        // than assumed, so a fixture change breaks it here with a reason rather than three lines
        // down with none.
        Assert.True(
            board.Bounds.MinX != 0 || board.Bounds.MinY != 0,
            "this board is drawn from the origin, so it cannot tell the two shifts apart");

        var implied = BoardFindings.For(board, settings, ToolLibrary.Default);
        var stated = BoardFindings.For(board, settings, ToolLibrary.Default, board.Bounds);

        var a = implied.Layers.SelectMany(l => l.Gaps).Select(g => g.At).ToList();
        var b = stated.Layers.SelectMany(l => l.Gaps).Select(g => g.At).ToList();

        Assert.NotEmpty(a);
        Assert.Equal(a, b);

        // And it is not the zero-area rectangle at the origin, which would shift by nothing and
        // hand back the artwork's own coordinates.
        var wrong = BoardFindings.For(board, settings, ToolLibrary.Default, default(Bounds));

        Assert.NotEqual(a, wrong.Layers.SelectMany(l => l.Gaps).Select(g => g.At).ToList());
    }

    /// <summary>
    /// A gap is reported where the operator's machine can be sent, not where the Gerber was drawn.
    ///
    /// **Caught by looking at a screenshot, and nothing else would have caught it.** The first
    /// version handed the located coordinates straight through, so the Mega's shorts were reported
    /// at places like *"107.35, -92.13 mm"* — correct in the frame the Gerbers were drawn in, and
    /// useless on a board whose own coordinates run from zero. Every emitted program says work zero
    /// is the board's lower-left corner; a negative coordinate on a board with no negative corner
    /// is worse than no coordinate, because somebody will try to go there.
    ///
    /// Asserted against the board's own extents rather than against a remembered number, so it
    /// catches a sign error, a missing shift, and a shift applied twice.
    /// </summary>
    [Fact]
    public void AGapIsReportedInTheFrameTheMachineWorksIn()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);

        var findings = BoardFindings.For(board, settings, ToolLibrary.Default);
        var seen = 0;

        foreach (var layer in findings.Layers)
        {
            foreach (var gap in layer.Gaps)
            {
                Assert.InRange(gap.At.X, 0, board.Bounds.Width);
                Assert.InRange(gap.At.Y, 0, board.Bounds.Height);
                seen++;
            }
        }

        output.WriteLine(
            $"{seen} gap(s) inside a board {Nm.ToMillimetreString(board.Bounds.Width, 2)} × "
            + $"{Nm.ToMillimetreString(board.Bounds.Height, 2)} mm");

        Assert.True(seen > 0, "no gap was located, so no coordinate was examined");
    }

    /// <summary>
    /// A located bridge belongs to exactly one of the groups the named check found.
    ///
    /// **The defect this pins made the board look worse than it is.** The window matched a bridge to
    /// a group by the two nets either side of it, which sounds right and is not: a ground pour
    /// reaches many separate pieces of merged copper, so one GND–USHIELD bridge was filed under
    /// every group holding both names and appeared twice on screen. Matching on the whole region's
    /// net names — which is what `NetGap.RegionNets` carries — files it once.
    ///
    /// Written over every group on both layers rather than over the pair that misbehaved, because
    /// "appears under exactly one group" is the property, and the pair was only how it was noticed.
    /// </summary>
    [Fact]
    public void ALocatedBridgeBelongsToExactlyOneGroup()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);

        var findings = BoardFindings.For(board, settings, ToolLibrary.Default);
        var checked_ = 0;

        foreach (var layer in findings.Layers)
        {
            foreach (var gap in layer.Shorts)
            {
                var owning = layer.Joins.Count(
                    join => gap.RegionNets.SequenceEqual(join.Nets, StringComparer.Ordinal));

                Assert.True(
                    owning == 1,
                    $"a bridge at {Nm.ToMillimetreString(gap.At.X, 2)}, "
                    + $"{Nm.ToMillimetreString(gap.At.Y, 2)} mm belongs to {owning} group(s), not one");

                checked_++;
            }
        }

        output.WriteLine($"{checked_} bridge(s), each in exactly one group");

        Assert.True(checked_ > 0, "no short was located, so nothing was filed anywhere");
    }

    /// <summary>
    /// The split is places, not a tally.
    ///
    /// Story 3 separated the residual into copper carrying no net name and two pieces of one net
    /// meeting itself, because those mean opposite amounts of trouble. It could only report them as
    /// numbers. Here each one is a coordinate, which is the difference this view exists to make:
    /// an operator told "25 gaps" cannot act, and an operator given a place can go and look.
    ///
    /// The board is the one this repository already records as having same-net gaps at its default
    /// cut — the Arduino Uno, from the sweep that stopped `SameNet` shipping as a field that could
    /// never fire.
    ///
    /// **Written wrongly first, and a `/describe-test` pass said exactly how.** It asserted
    /// `Assert.Single(gap.Between)` over the same-net list and that no nameless gap had any names —
    /// which are restatements of the partition itself, since `IsSameNet` *is* `Between.Count == 1`
    /// and the nameless bucket *is* the rest. Those two assertions could not fail. And the test
    /// named for showing places never touched a coordinate: the locations were printed and nothing
    /// more. What is asserted now is the thing the name claims.
    /// </summary>
    [Fact]
    public void TheSplitIsShownAsPlacesRatherThanCounted()
    {
        var (board, settings) = Load(RealBoards.ArduinoUno);

        var findings = BoardFindings.For(board, settings, ToolLibrary.Default);

        var sameNet = findings.Layers.SelectMany(l => l.SameNet).ToList();

        output.WriteLine($"{sameNet.Count} same-net place(s)");

        foreach (var gap in sameNet)
        {
            output.WriteLine(
                $"  {string.Join(", ", gap.Between)} at "
                + $"{Nm.ToMillimetreString(gap.At.X, 2)}, {Nm.ToMillimetreString(gap.At.Y, 2)} mm");
        }

        Assert.NotEmpty(sameNet);

        // **Each one is a place on this board**, which is the whole difference from the tally story
        // 3 could report. A bucket full of default coordinates would pass every assertion the first
        // version made.
        foreach (var gap in sameNet)
        {
            Assert.InRange(gap.At.X, 0, board.Bounds.Width);
            Assert.InRange(gap.At.Y, 0, board.Bounds.Height);
        }

        // **And they are distinct places.** The tally said "n gaps"; if this list were n copies of
        // one coordinate it would be a tally wearing a coordinate, and an operator sent to look
        // would find one thing and believe the rest were the same.
        Assert.Equal(
            sameNet.Count,
            sameNet.Select(g => (g.At.X, g.At.Y)).Distinct().Count());

        // The same net either side is what makes this bucket safe by construction, and it is worth
        // pinning against the *board* rather than against the partition: every name here must be a
        // net that layer actually declares.
        var declared = board.Layers
            .SelectMany(l => l.Nets)
            .Select(n => n.Net)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var gap in sameNet)
        {
            Assert.All(gap.Between, n => Assert.Contains(n, declared));
        }
    }

    /// <summary>
    /// A layer that is not being cut is not reported on.
    ///
    /// The same rule <see cref="BoardCheck"/> follows and for the same reason: the cut width that
    /// decides every answer here belongs to an operation that is not going to happen, so reporting
    /// on it gives the operator something they cannot act on.
    /// </summary>
    [Fact]
    public void ALayerThatIsNotBeingCutIsNotReportedOn()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);

        // The guard that stops this passing for the wrong reason: an empty answer proves the filter
        // only if the same board answers with something when its copper is switched on.
        var withCopper = BoardFindings.For(board, settings, ToolLibrary.Default);

        foreach (var key in settings.Keys.ToList())
        {
            settings[key] = settings[key] with { Output = OutputKind.None };
        }

        var findings = BoardFindings.For(board, settings, ToolLibrary.Default);

        output.WriteLine($"copper on: {withCopper.Layers.Count} layer(s); everything off: {findings.Layers.Count}");

        Assert.NotEmpty(withCopper.Layers);
        Assert.Empty(findings.Layers);
    }

    /// <summary>
    /// A run stopped part-way through says so rather than reporting a clean board.
    ///
    /// **This window is where somebody goes to satisfy themselves a board is sound**, which makes a
    /// partial answer shown as a complete one worse here than anywhere else in the application. The
    /// distinction is sprint 2's story 3, and the cancel is aimed the way `BoardCheckTests` learned
    /// to aim it: at the first isolated layer's own settings lookup, so the run genuinely starts on
    /// the board and stops with work left.
    /// </summary>
    [Fact]
    public void ARunStoppedPartWayThroughSaysSo()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);

        var whole = BoardFindings.For(board, settings, ToolLibrary.Default);

        Assert.True(
            whole.Layers.Count > 1,
            $"this board reports on {whole.Layers.Count} layer(s); one cannot be stopped part way");

        using var stop = new CancellationTokenSource();

        // **Cancel after the first layer's lookup, not before it.**
        //
        // This used to pass `after: first.Index` — the position of the first isolation layer in
        // `board.Layers`, which on the Mega is zero. `CancelsAfterLayers` cancels once `Reached`
        // exceeds `after`, so the very first lookup turned the token and the abandoned run reported
        // **no layers at all**. Both assertions below passed on that: it was cancelled, and zero is
        // fewer than two. A test called *a run stopped part way through says so* was proving that a
        // run stopped before it started says so, which a single check at the top of the method
        // satisfies — the same fault `LatestRun` had, and the one `PlanningCancellationFixture`
        // warns about in so many words: *"A test that cancels after two and calls that 'two layers
        // in' has actually stopped the run before it started planning anything."*
        //
        // One is the number that means something here: the run must have finished a layer and then
        // been stopped. `Assert.NotEmpty` below is what says so, and is why `after` is a literal
        // rather than an index computed from the board.
        var abandoned = new CancelsAfterLayers(settings, after: 1, stop);

        var result = BoardFindings.For(board, abandoned, ToolLibrary.Default, token: stop.Token);

        output.WriteLine(
            $"whole run reported on {whole.Layers.Count} layer(s); the abandoned one on {result.Layers.Count}");

        Assert.True(result.Cancelled, "a run that stopped part way reported itself complete");
        Assert.NotEmpty(result.Layers);
        Assert.True(result.Layers.Count < whole.Layers.Count, "the run was not actually stopped");

        // And it carries the banner the window will show over its partial list — asserted on the
        // genuinely stopped run, not on a record built for the purpose, so this is the line that
        // fails if a stopped run ever stops announcing itself (6.54).
        Assert.NotNull(result.StoppedBanner);
    }

    /// <summary>
    /// A stopped run carries its own banner, and a finished one carries none (6.54).
    ///
    /// The sentence lives on the result rather than in the window, which is the whole fix's test
    /// story: the banner was written, correct and unreachable for three releases, and nothing
    /// could say so because the words sat in Avalonia where no test reaches. The window now shows
    /// exactly this string, so a banner that stops appearing is this test failing — or a one-line
    /// window change that a screenshot catches.
    /// </summary>
    [Fact]
    public void AStoppedRunCarriesItsOwnBannerAndAFinishedOneDoesNot()
    {
        var stopped = new BoardFindings.Result([], TimeSpan.FromSeconds(1), Cancelled: true);
        var finished = new BoardFindings.Result([], TimeSpan.FromSeconds(1), Cancelled: false);

        output.WriteLine(stopped.StoppedBanner ?? "(none)");

        Assert.NotNull(stopped.StoppedBanner);
        Assert.Contains("stopped before it finished", stopped.StoppedBanner, StringComparison.Ordinal);
        Assert.Contains("part of the answer", stopped.StoppedBanner, StringComparison.Ordinal);
        Assert.Null(finished.StoppedBanner);
    }

    /// <summary>
    /// Pressing F7 again abandons the run in progress instead of waiting it out — 06 §6.55.
    ///
    /// **The token is turned inside the first isolated layer's own settings lookup**, which is after
    /// the loop's check at the top and before any of that layer's geometry. The run is then in the
    /// middle of a layer, which is the only place this ever mattered: the token used to be read
    /// only in the gap search's pair loop, which a board that is mostly fine skips, so the whole
    /// layer — the offset of the copper, the check, the search — ran to the end before anything
    /// looked. Measured on the Mega before the fix, a cancel 50 ms in returned 500 ms later.
    ///
    /// **Measured against this machine's own layer**, not against a number: the abandoned run must
    /// take under a quarter of one layer's share of the whole run. Before the fix it took all of
    /// it — 610 ms against 554 — and failed; after, it stops at the first whole-layer step, 0.1 ms.
    ///
    /// **What this proves and what it does not.** It proves a run whose token turns at the start of
    /// a layer does none of that layer's geometry. It would also pass if the token were read only
    /// once, straight after the lookup — it cannot land a cancel *between* two Clipper operations,
    /// which needs a hook in the production code. That finer claim, that `GrownCopper.Of` reads the
    /// token before each whole-layer operation, rests on a measurement recorded in 06 §6.55: a
    /// cancel anywhere in a run now returns within one offset, 119 ms at worst, where it used to
    /// wait out the layer.
    /// </summary>
    [Fact]
    public void ASecondPressAbandonsTheRunInsteadOfWaitingItOut()
    {
        var (board, settings) = Load(RealBoards.ArduinoMega);

        // Warmed, so neither figure below is the first call's compilation.
        BoardFindings.For(board, settings, ToolLibrary.Default);
        var whole = BoardFindings.For(board, settings, ToolLibrary.Default);

        // A whole run of a two-sided board: two layers, uncancelled, so the share below means one.
        Assert.False(whole.Cancelled);
        Assert.Equal(2, whole.Layers.Count);

        var oneLayer = whole.Elapsed / whole.Layers.Count;

        // The position of the first layer the findings view examines, so the cancel lands on its
        // lookup: `CancelsAfterLayers` turns the token once more lookups than `after` have been made.
        var first = board.Layers
            .Select((layer, index) => (layer, index))
            .First(p => settings.TryGetValue(p.layer.FileName, out var s)
                && LayerOperations.For(p.layer.Role, s.Output) == OperationKind.Isolation)
            .index;

        using var stop = new CancellationTokenSource();
        var abandoned = new CancelsAfterLayers(settings, after: first, stop);

        var result = BoardFindings.For(board, abandoned, ToolLibrary.Default, token: stop.Token);

        output.WriteLine(
            $"one layer's share of a whole run: {oneLayer.TotalMilliseconds:F0} ms; the abandoned run: {result.Elapsed.TotalMilliseconds:F1} ms");

        // It really was stopped inside that layer: the lookup that turned the token was its own.
        Assert.Equal(first + 1, abandoned.Reached);
        Assert.True(result.Cancelled, "the abandoned run reported itself complete");
        Assert.Empty(result.Layers);
        Assert.True(
            result.Elapsed < oneLayer / 4,
            $"the abandoned run took {result.Elapsed.TotalMilliseconds:F0} ms against {oneLayer.TotalMilliseconds:F0} ms for a whole layer: it waited the layer out");
    }
}
