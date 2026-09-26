using System.Diagnostics;
using System.Globalization;
using System.Text;
using MillBurn.Core;
using MillBurn.Pipeline;
using MillBurn.Tests;
using Xunit.Abstractions;

namespace MillBurn.GoldenTests;

/// <summary>
/// How much geometry work a board costs, written down so that a change in it has to be explained.
///
/// **Not a timing test, and deliberately not.** A wall clock measures the machine, not the program:
/// asserting on one either flaps under load or has its threshold raised until it guards nothing.
/// <c>OptimizerBenchmarkTests</c> says the same thing about pinning millimetres, and the optimizer's
/// own budget has been counted in moves rather than milliseconds since it was written. This counts
/// Clipper booleans, point-in-polygon questions, and the vertices handed to them — all of which are
/// the same on every machine for the same input.
///
/// **Snapshotted rather than bounded**, for the reason the program snapshots are. A threshold only
/// catches a regression big enough to trip it, and the expensive kind is a hundred small ones that
/// never do. A recorded number turns any change at all into a diff, which somebody then has to
/// account for in a commit message — the same contract as an emitted program, and the same escape
/// hatch: <c>MILLBURN_UPDATE_SNAPSHOTS=1</c> rewrites the baseline, and a baseline that changes is a
/// claim, not a formality.
///
/// **Portable between runs, not necessarily between machines.** The counts are a property of the
/// input *and* of the Clipper build, the runtime and the floating-point arithmetic underneath —
/// a different Clipper2 version or a different architecture can legitimately tessellate to a
/// different number of vertices on identical Gerbers. On one machine and one set of dependencies
/// they are exact; across a package bump or a new CI runner, a diff here may be reporting the
/// toolchain rather than the code, and the commit that updates the baseline should say which.
///
/// The seconds are printed and never asserted. They are for a human reading the output, and they
/// have no business deciding whether a build passes.
///
/// What prompted this: carrying net names through realisation cost 77 % on top of realising the
/// Arduino Mega at all — 72.6 ms to 128.4 ms — and nothing in the suite noticed. In the counts it is
/// unmissable, because the point tests went from about fifteen hundred to more than half a million.
///
/// **Offsets joined the report in 6.39**, and they are most of what building a toolpath is. Until
/// then the fifteen call sites went straight to Clipper, so these baselines described compositing
/// a board well and building programs from it not at all — work that doubled the offsetting moved
/// the vertex line and nothing said why. Every vertex total here rose when they were counted,
/// which is the measure of how much had been invisible: the Mega's planning went from 885,555 to
/// 1,273,512, so a third of the geometry that stage handles was going unrecorded.
/// </summary>
public sealed class WorkSnapshotTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(RealBoards.PogoTest1, 0, OutputKind.Gcode, "PogoTest1-work")]
    [InlineData(RealBoards.MillburnTestBoard, 0, OutputKind.Gcode, "Millburn_Test_Board-work")]
    [InlineData(RealBoards.Panel, 0, OutputKind.Gcode, "GridStripConnector_Panelized-work")]

    // **SVG, which was unmeasured until 6.40.** Every case here asked for G-code, and the two go
    // through different work: no depth, no passes, no linking, and no route search worth the name.
    // A regression in what an SVG export costs would have moved nothing in this folder.
    [InlineData(RealBoards.MillburnTestBoard, 0, OutputKind.Svg, "Millburn_Test_Board-svg-work")]

    // **Three more boards, and the choice is argued now rather than assumed.** The first four were
    // picked for size and shape, which is the right instinct and the wrong sample: the boards that
    // have broken things here were the awkward ones, not the big ones — 6.30 came off a real
    // export, and three bench faults came off one board nobody had run. The Uno is an ordinary
    // two-layer board of a kind nothing else here represents; the unpanelised connector is the
    // panel's own single board, so the pair says what panelising costs; and the all-layers pogo
    // set carries every role at once, which is the shape that finds role-dispatch mistakes.
    [InlineData(RealBoards.ArduinoUno, 0, OutputKind.Gcode, "Arduino_Uno-work")]
    [InlineData(RealBoards.GridStripConnector, 0, OutputKind.Gcode, "GridStripConnector-work")]
    [InlineData(RealBoards.PogoTest1AllLayers, 0, OutputKind.Gcode, "PogoTest1-AllLayers-work")]

    // A moat wide enough to take several laps, which is what anybody actually cuts. The cases above
    // all clear their copper in a single pass, so the multi-pass machinery — the extra laps, and
    // PassLinker deciding whether the tool may be dragged from one to the next — barely runs: this
    // board plans 140 booleans over 211,017 vertices at the default moat and 724 over 739,613 here.
    // ProgramSnapshotTests added a wide-moat case for the same reason, and the same argument applies
    // to what that work costs.
    //
    // (Those two figures were 140 over 120,668 and 798 over 384,078 when this was written, and both
    // vertex totals grew when 6.39 started counting offsets. Quoted numbers in a comment go stale
    // silently — nothing asserts against prose — so they are worth re-reading whenever the
    // baselines beside them move.)
    [InlineData(RealBoards.MillburnTestBoard, 400_000, OutputKind.Gcode, "Millburn_Test_Board-wide-moat-work")]
    [InlineData(RealBoards.ArduinoMega, 0, OutputKind.Gcode, "Arduino_Mega_2560-work")]
    public void TheWorkThisBoardCostsIsUnchanged(
        string board, long isolationWidthNm, OutputKind kind, string snapshot)
    {
        var folder = RealBoards.Directory(board);

        // Warm: the first load of anything pays for JIT and for whatever the file system was doing,
        // and neither is a property of the board.
        _ = BoardLoader.LoadFolder(folder);

        var before = Work.Taken;
        var watch = Stopwatch.StartNew();

        var loaded = BoardLoader.LoadFolder(folder);

        var realising = watch.Elapsed;
        var afterLoad = Work.Since(before);

        // **The SVG case is the laser workflow, not the milling one relabelled.** Under the milling
        // defaults nothing produces SVG — masks and silkscreen are off, because somebody milling
        // copper has no use for them — so asking for `OutputKind.Svg` there plans an empty job and
        // measures nothing. `LaserEtching` turns copper and silkscreen into artwork to burn a
        // resist with, which is the workflow that actually emits SVG and the one the product owner
        // runs. Found by the floor assertion below, which is what it is for.
        var defaults = kind == OutputKind.Svg ? ImportDefaults.LaserEtching : ImportDefaults.Milling;

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role, defaults),
                IsolationWidthNm = isolationWidthNm,
            },
            StringComparer.Ordinal);

        watch.Restart();

        var plan = ExportPlanner.Plan(
            // The settings say what each layer naturally produces; `kind` says which of those to
            // plan. So the SVG case is not the G-code case relabelled — it is the soldermask and
            // silkscreen layers on their own, which is a different pipeline end to end.
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), kind);

        var planning = watch.Elapsed;

        // Everything since the mark, less what the load already accounted for. The two baselines for
        // this board are the check on that subtraction being honest: the moat only reaches the
        // planner, so their realising blocks are identical to the digit while their planning blocks
        // differ five-fold. If a moat width ever moved a realising count, it would mean the setting
        // had reached the loader — or that this arithmetic is charging planning work to the load.
        var afterPlan = Work.Since(before) - afterLoad;

        output.WriteLine($"{board}: realised in {realising.TotalMilliseconds:F0} ms, planned in {planning.TotalMilliseconds:F0} ms");

        // A measurement of nothing is not a measurement, and this one could become one quietly.
        //
        // The warm-up above is safe only because `LoadFolder` does not go through `RealisedLayers`:
        // it reparses and rebuilds, so the second load pays full price. Route that path through the
        // memo one day — a reasonable thing to want — and the warm-up primes the cache instead, the
        // measured half collapses to nearly nothing, and the baseline gets updated once as a
        // caching win. After that this file would pass forever while watching an empty room.
        //
        // So the floor is asserted rather than assumed. It is not a number to tune: it says the
        // work happened at all.
        Assert.True(
            afterLoad.Booleans > 0 && afterLoad.Vertices > 0,
            $"realising {board} did {afterLoad} — no geometry at all, so either the counters have "
            + "come unhooked or the warm-up is now filling a cache that the measurement then reads.");

        Assert.True(
            afterPlan.Vertices > 0,
            $"planning {board} did {afterPlan} — the plan was served from memory, or the counters "
            + "are not attached to the planner.");

        var report = new StringBuilder();
        report.Append(board).Append('\n');
        report.Append(new string('=', board.Length)).Append("\n\n");

        Line(report, "layers", loaded.Layers.Count);
        Line(report, "objects", loaded.TotalObjects);
        Line(report, "programs", plan.Count);

        // Unlike the counted lines, these two are not measurements: they are the theory's own
        // arguments written back out, so they cannot move unless the InlineData does. They earn
        // their place anyway — between them they are what stops the three test-board cases being
        // pointed at each other's baselines, which is otherwise a silent swap — but do not read
        // either as something observed.
        Line(report, "moat (nm)", isolationWidthNm);
        report.Append("output".PadRight(16)).Append(kind.ToString()).Append(NewLine);
        report.Append('\n');

        report.Append("realising\n");
        Line(report, "  booleans", afterLoad.Booleans);
        Line(report, "  offsets", afterLoad.Offsets);
        Line(report, "  point tests", afterLoad.PointTests);
        Line(report, "  vertices", afterLoad.Vertices);
        report.Append('\n');

        report.Append("planning\n");
        Line(report, "  booleans", afterPlan.Booleans);
        Line(report, "  offsets", afterPlan.Offsets);
        Line(report, "  searches", afterPlan.Searches);
        Line(report, "  search steps", afterPlan.SearchSteps);
        Line(report, "  point tests", afterPlan.PointTests);
        Line(report, "  vertices", afterPlan.Vertices);

        Snapshot.Match(snapshot, report.ToString());
    }

    /// <summary>
    /// The path the application actually takes costs what the baselines above say it costs.
    ///
    /// **Every case above loads with `LoadFolder`, and the app does not always.** That was the
    /// fourth gap 6.40 wrote down. `LoadFolder` reparses and rebuilds from disk, which is what
    /// makes the warm-up in those cases honest and their floor assertion meaningful; reopening a
    /// project hands the bytes it already has to `LoadSources`. If the two did different amounts
    /// of geometry, this whole folder would be an account of a path the operator never takes —
    /// and nothing would have said so, because both produce a `Board` and neither complains.
    ///
    /// Asserted as equality rather than snapshotted, because the claim is a relationship and not
    /// a number: whatever a board costs to realise, it costs the same either way in. Both memos
    /// are forgotten in between, or the second reads the first's answers and the comparison is of
    /// caching.
    /// </summary>
    [Fact]
    public void TheAppsOwnLoadPathCostsTheSame()
    {
        var folder = RealBoards.Directory(RealBoards.MillburnTestBoard);

        _ = BoardLoader.LoadFolder(folder);

        RealisedLayers.Forget();
        var before = Work.Taken;
        var viaFolder = BoardLoader.LoadFolder(folder);
        var folderCost = Work.Since(before);

        var sources = Directory.EnumerateFiles(folder)
            .Where(BoardLoader.IsBoardFile)
            .Order(StringComparer.Ordinal)
            .Select(f => (
                Path.GetFileName(f),
                File.ReadAllBytes(f),
                LayerRoles.FromFileName(Path.GetFileName(f))))
            .ToList();

        RealisedLayers.Forget();
        before = Work.Taken;
        var viaSources = BoardLoader.LoadSources(RealBoards.MillburnTestBoard, sources);
        var sourcesCost = Work.Since(before);

        output.WriteLine($"LoadFolder:  {folderCost}");
        output.WriteLine($"LoadSources: {sourcesCost}");

        // A floor before the comparison, because two zeroes are equal. Everything below is relative
        // — it says the two paths cost the same, not what they cost — so if the counters ever came
        // unhooked, or both loads were served from a memo neither `Forget` reached, this would pass
        // while measuring nothing at all. The theory above has the numbers; this needs only to know
        // that work happened.
        Assert.True(
            folderCost.Booleans > 0 && folderCost.Offsets > 0 && folderCost.Vertices > 0,
            $"loading the board did {folderCost}, so there is nothing here for the two paths to "
            + "agree about.");

        Assert.Equal(viaFolder.Layers.Count, viaSources.Layers.Count);
        Assert.Equal(viaFolder.TotalObjects, viaSources.TotalObjects);
        Assert.Equal(folderCost, sourcesCost);
    }

    /// <summary>
    /// The report is built with `\n` throughout and normalised on comparison, so the baselines do
    /// not carry a platform in them.
    /// </summary>
    private const char NewLine = '\n';

    private static void Line(StringBuilder into, string name, long value) =>
        into.Append(name.PadRight(16))
            .Append(value.ToString("N0", CultureInfo.InvariantCulture))
            .Append('\n');
}
