using System.Globalization;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Viewer;

namespace MillBurn.Pipeline;

/// <summary>What a preview works out, with nothing in it that has to be built on the UI thread.</summary>
/// <param name="Backplot">The drawable layers, one group per source layer.</param>
/// <param name="Gcode">Every program's text, in plan order.</param>
/// <param name="Summary">The one-line count of programs, cut, travel and plunges.</param>
/// <param name="ProgramCount">How many programs the plan holds.</param>
/// <param name="GougeCount">Rapid moves found at cutting depth — a reason not to run the file.</param>
/// <param name="Warnings">Each distinct warning the plan's items carry.</param>
public sealed record PreviewResult(
    IReadOnlyList<BackplotLayer> Backplot,
    string Gcode,
    string Summary,
    int ProgramCount,
    int GougeCount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// The arithmetic behind a preview: parse each emitted program, classify its moves, measure them,
/// and build the drawable layers.
///
/// It lives here, apart from the view model, because it is the slow half and none of it needs a
/// window. Given a plan it reads nothing else, which is what lets it run on the thread pool while
/// the UI thread stays free to draw and to accept the next edit.
/// </summary>
public static class PreviewBuild
{
    /// <summary>
    /// Builds the preview, abandoning the work as soon as <paramref name="token"/> is cancelled.
    ///
    /// The check sits between programs rather than inside the parse: a single program is short
    /// enough that finishing one costs less than the plumbing to interrupt it, and a plan's cost is
    /// in how many it holds.
    /// </summary>
    /// <param name="plan">The planned export, already filtered to G-code.</param>
    /// <param name="boardBounds">The board's own extent, for the frame when there is no blank.</param>
    /// <param name="profile">The machine the measurements assume.</param>
    /// <param name="token">Cancelled when a later edit has made this run pointless.</param>
    public static PreviewResult From(
        ExportPlan plan, Bounds boardBounds, MachineProfile profile, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // The frame the plan used, not the board's own corner. With a blank the programs are
        // written to the blank's lower-left, so undoing the board's shift instead draws every
        // toolpath a border's width up and to the right of the copper it cuts — a picture that is
        // wrong in a way the file is not, which is the worst kind of wrong a viewer can be.
        var frame = plan.FrameFor(boardBounds);
        var shift = new Point2(frame.MinX, frame.MinY);
        var programs = new List<BackplotBuilder.Program>(plan.Items.Count);
        var cut = 0.0;
        var travel = 0.0;
        var plunges = 0;
        var gouges = 0;

        foreach (var item in plan.Items)
        {
            token.ThrowIfCancellationRequested();

            var classified = GcodeBackplot.Classify(GcodeParser.Parse(item.Content));
            var measured = GcodeBackplot.Measure(classified, profile);

            // Kept apart by source layer rather than poured into one list. Merged, the viewer can
            // only ever show every program's cuts at once — and looking at one layer's toolpath is
            // the reason to open a backplot at all.
            programs.Add(new BackplotBuilder.Program(
                item.LayerFileName, item.LayerLabel, classified, item.Mirrored));

            cut += measured.CutMm;
            travel += measured.TravelMm;
            plunges += measured.PlungeCount;
            gouges += measured.GougeCount;
        }

        token.ThrowIfCancellationRequested();

        // A mirrored program is written for the flipped stock, so it is flipped back for the
        // drawing: what the picture is being asked is where the cuts land on *this* board, and a
        // bottom-copper path drawn straight lands on the mirror image of the traces it isolates.
        var backplot = BackplotBuilder.BuildPerProgram(
            programs, shift, mirrorSumXNm: frame.MinX + frame.MaxX);

        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{plan.Count} programs · {cut:F0} mm cut · {travel:F0} mm travel · {plunges} plunges");

        return new PreviewResult(
            backplot,
            string.Join("\n", plan.Items.Select(i => i.Content)),
            summary,
            plan.Count,
            gouges,
            [.. plan.Items.SelectMany(i => i.Warnings).Distinct(StringComparer.Ordinal)]);
    }
}
