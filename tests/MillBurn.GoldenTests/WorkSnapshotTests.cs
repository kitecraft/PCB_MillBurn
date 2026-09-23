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
/// </summary>
public sealed class WorkSnapshotTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(RealBoards.PogoTest1, 0, "PogoTest1-work")]
    [InlineData(RealBoards.MillburnTestBoard, 0, "Millburn_Test_Board-work")]
    [InlineData(RealBoards.Panel, 0, "GridStripConnector_Panelized-work")]

    // A moat wide enough to take several laps, which is what anybody actually cuts. The cases above
    // all clear their copper in a single pass, so the multi-pass machinery — the extra laps, and
    // PassLinker deciding whether the tool may be dragged from one to the next — barely runs: this
    // board plans 140 booleans over 120,668 vertices at the default moat and 798 over 384,078 here.
    // ProgramSnapshotTests added a wide-moat case for the same reason, and the same argument applies
    // to what that work costs. Five of six is not much of a sample either way; RealBoards has ten.
    [InlineData(RealBoards.MillburnTestBoard, 400_000, "Millburn_Test_Board-wide-moat-work")]
    [InlineData(RealBoards.ArduinoMega, 0, "Arduino_Mega_2560-work")]
    public void TheWorkThisBoardCostsIsUnchanged(string board, long isolationWidthNm, string snapshot)
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

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
                IsolationWidthNm = isolationWidthNm,
            },
            StringComparer.Ordinal);

        watch.Restart();

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

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

        // Unlike every other line here, this one is not a measurement: it is the theory's own
        // argument written back out, so it cannot move unless the InlineData does. It earns its
        // place anyway — it is what stops the two test-board cases being pointed at each other's
        // baseline, which is otherwise a silent swap — but do not read it as something observed.
        Line(report, "moat (nm)", isolationWidthNm);
        report.Append('\n');

        report.Append("realising\n");
        Line(report, "  booleans", afterLoad.Booleans);
        Line(report, "  point tests", afterLoad.PointTests);
        Line(report, "  vertices", afterLoad.Vertices);
        report.Append('\n');

        report.Append("planning\n");
        Line(report, "  booleans", afterPlan.Booleans);
        Line(report, "  point tests", afterPlan.PointTests);
        Line(report, "  vertices", afterPlan.Vertices);

        Snapshot.Match(snapshot, report.ToString());
    }

    private static void Line(StringBuilder into, string name, long value) =>
        into.Append(name.PadRight(16))
            .Append(value.ToString("N0", CultureInfo.InvariantCulture))
            .Append('\n');
}
