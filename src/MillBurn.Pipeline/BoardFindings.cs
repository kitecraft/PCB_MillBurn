using System.Diagnostics;
using MillBurn.Cam;
using MillBurn.Core;

namespace MillBurn.Pipeline;

/// <summary>
/// Every layer's electrical findings, collected in one place, with the expensive answers in them.
///
/// **The three ways the warning channel was straining, and this is the answer to all three.**
/// *The format capped what could be said* — eight groups, six names each, because one piece of
/// copper holding twenty-three nets becomes a six-hundred-character sentence. *The findings were
/// scattered* — each isolation program reported its own, so a two-sided board answered in two
/// places and nothing collected them. *And it diluted the list* — seventeen items before sprint 1's
/// story 4, eleven more on a dense board.
///
/// A list read at a glance and a full account of a board are different documents, and trying to be
/// both is what was straining. So the list stays short and points here, and here there is room.
///
/// **What can live here that cannot live in a warning line.** Where a gap actually is, from
/// <see cref="ElectricalCheck.Gaps"/> — an offset per piece of copper, unjustifiable for a sentence
/// and reasonable for something somebody opened. Story 3's split shown as places rather than
/// counted as a tally. And reasoning across layers, which per-program warnings cannot do at all
/// because no program sees another.
///
/// **What deliberately does not live here yet**, and the line is worth holding: the rest of DRC —
/// drill-to-copper clearance, an outline cut that severs a trace, a pocket that removes part of a
/// net. They have nowhere to live today and this is where they will live, which makes it tempting
/// to add one while the file is open. They are a sprint of their own and each needs its own
/// argument about when it can fire. **And the severed-net family stays out entirely** until the
/// operation that can sever is the one being checked: isolation cuts outside the copper edge and
/// cannot sever anything, and a check that can never fire is the failure this repository has been
/// bitten by twice.
///
/// Recorded in [06 §6.44](../../Documentation/06-Roadmap-and-Risks.md).
/// </summary>
public static class BoardFindings
{
    /// <summary>What one copper layer had to say.</summary>
    /// <param name="Label">The layer as the operator names it, for the heading.</param>
    /// <param name="Role">Its role, so the view can colour the heading as the panel does.</param>
    /// <param name="FileName">The file, which is the only thing about a layer that is unique.</param>
    /// <param name="Verdict">The one-line summary — never "all separated" unless it is true.</param>
    /// <param name="WidthNm">The cut this was checked against, because every answer depends on it.</param>
    /// <param name="Joins">The groups of nets left connected, uncapped: there is room here.</param>
    /// <param name="Gaps">
    /// Where the copper actually bridges, which is what a warning line cannot say.
    ///
    /// **In work coordinates — measured from the board's lower-left corner** — and not in the frame
    /// the Gerbers were drawn in. Every emitted program says *"Work zero is the board's lower-left
    /// corner; the Gerber origin was at 149.835, -107.975 mm"*, and the first version of this view
    /// printed the raw figures: a short reported at "107.35, -92.13 mm" on a board whose own
    /// coordinates run from zero. A negative coordinate on a board that has no negative corner is
    /// not a place an operator can go to, which makes it worse than no coordinate at all.
    /// </param>
    /// <param name="NetsSeen">How many nets were named on this layer at all.</param>
    /// <param name="Silent">
    /// Why the check could not run, when it could not. Empty when it did. A layer that was not
    /// checked must never read as a layer that came back clean.
    /// </param>
    public sealed record Layer(
        string Label,
        LayerRole Role,
        string FileName,
        string Verdict,
        long WidthNm,
        IReadOnlyList<NetJoin> Joins,
        IReadOnlyList<NetGap> Gaps,
        int NetsSeen,
        string Silent)
    {
        /// <summary>Bridges that short two different nets together.</summary>
        public IReadOnlyList<NetGap> Shorts => [.. Gaps.Where(g => g.IsShort)];

        /// <summary>
        /// Bridges between two pieces of one net — **story 3's split, shown rather than counted.**
        ///
        /// These short nothing: the two pieces were one conductor before the cut and are one
        /// after it. They are here because copper left where the design wanted none still matters
        /// for soldering and for probing, and because an operator told "25 gaps" had no way to know
        /// how many of them mattered.
        /// </summary>
        public IReadOnlyList<NetGap> SameNet => [.. Gaps.Where(g => g.IsSameNet)];

        /// <summary>
        /// Bridges in copper carrying no net name — the other half of the split, and the half
        /// nothing here can decide about. Saying so is the point.
        /// </summary>
        public IReadOnlyList<NetGap> Nameless => [.. Gaps.Where(g => !g.IsShort && !g.IsSameNet)];

        /// <summary>Whether the check ran at all. See <see cref="Silent"/>.</summary>
        public bool Ran => Silent.Length == 0;
    }

    /// <summary>Every isolated layer's findings, and what the run cost.</summary>
    /// <param name="Layers">One entry per layer that would be isolated, in board order.</param>
    /// <param name="Elapsed">How long it took, for the view to say out loud.</param>
    /// <param name="Cancelled">
    /// True when the run was stopped, superseded or abandoned. A partial answer shown as a
    /// complete one is the distinction sprint 2 spent a story on, and it matters more here than
    /// anywhere: this view is where somebody goes to satisfy themselves a board is sound.
    /// </param>
    public sealed record Result(IReadOnlyList<Layer> Layers, TimeSpan Elapsed, bool Cancelled)
    {
        /// <summary>
        /// The sentence the findings window puts over a stopped run's partial list, or null when
        /// the run finished.
        ///
        /// On the result rather than in the window (6.54): the window shows what the result says
        /// of itself, the way a program carries its own notes — and the one test that guards the
        /// banner can then reach both the condition and the words with no Avalonia in sight,
        /// which is what let the banner sit unreachable for three releases without any test able
        /// to say so.
        /// </summary>
        public string? StoppedBanner => Cancelled
            ? "This run was stopped before it finished. What is below is part of the answer, not "
                + "all of it — nothing here says the rest of the board is clean."
            : null;
    }

    /// <summary>
    /// Collect every isolated layer's findings, locating the gaps as it goes.
    ///
    /// **The same layers, the same tool and the same options as the export**, through
    /// <see cref="ExportPlanner.ToolFor"/> and <see cref="ExportPlanner.IsolationFor"/> — for the
    /// reason <see cref="BoardCheck"/> gives: an answer about a different cut is worse than no
    /// answer, because it would be believed.
    /// </summary>
    /// <param name="board">The board as it currently stands.</param>
    /// <param name="settings">What each layer is set to become.</param>
    /// <param name="library">The operator's tools, so this uses the bit the export would.</param>
    /// <param name="token">
    /// Read between layers **and inside the gap search**, which is the expensive part and the
    /// reason this needs one at all. A quarter of a second per layer is long enough that a window
    /// which cannot abandon an answer it no longer wants will be noticed.
    /// </param>
    /// <param name="frame">
    /// The rectangle the emitted programs are referenced to, whose lower-left corner every
    /// coordinate in the result is measured from. Null means the board's own bounds, which is what
    /// a job with no stock uses and what every caller that does not care should pass.
    ///
    /// **Nullable rather than defaulting to <see cref="Bounds.Empty"/>**, which an optional
    /// parameter cannot do — it is not a compile-time constant. `default(Bounds)` looks like the
    /// right sentinel and is not: it is `(0, 0, 0, 0)`, a zero-area rectangle at the origin, and
    /// `IsEmpty` tests `MinX > MaxX`, so it reads as a real frame whose corner is zero. Written
    /// that way first, and every three-argument caller silently got a shift of nothing — which the
    /// suite caught, because `AGapIsReportedInTheFrameTheMachineWorksIn` asserts a located gap
    /// lands inside the board rather than against a remembered number.
    /// </param>
    public static Result For(
        Board board,
        IReadOnlyDictionary<string, LayerOutputSettings> settings,
        ToolLibrary library,
        Bounds? frame = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);

        var started = Stopwatch.GetTimestamp();
        var layers = new List<Layer>();

        foreach (var layer in board.Layers)
        {
            if (token.IsCancellationRequested)
            {
                return new Result(layers, Stopwatch.GetElapsedTime(started), Cancelled: true);
            }

            if (!settings.TryGetValue(layer.FileName, out var setting))
            {
                continue;
            }

            if (LayerOperations.For(layer.Role, setting.Output) != OperationKind.Isolation)
            {
                continue;
            }

            var tool = ExportPlanner.ToolFor(setting, OperationKind.Isolation, library);
            var options = ExportPlanner.IsolationFor(setting, tool);

            // **Grown once and asked twice** — 06 §6.55. The check and the gap search both need the
            // layer offset by half the cut and split into the regions it merges into, and each used to
            // build it: byte-identical inputs, identical results, and the round offset of a dense layer
            // is the dominant cost of this whole view. The token is read before each whole-layer
            // operation inside it, which is where the time goes, so a second press of F7 no longer
            // waits out the first.
            //
            // **Asked whether the check will run before growing anything.** A layer with no net
            // names, or one, is refused before any geometry and used to cost this view nothing;
            // growing it first made exactly the boards the view can say least about the slowest.
            ElectricalCheck.GrownCopper? grown = null;

            if (ElectricalCheck.CannotRun(layer.Nets, options) is null
                && (grown = ElectricalCheck.GrownCopper.Of(layer.Area, options, token)) is null)
            {
                return new Result(layers, Stopwatch.GetElapsedTime(started), Cancelled: true);
            }

            var check = ElectricalCheck.Isolation(layer.Area, layer.Nets, options, grown);

            if (token.IsCancellationRequested)
            {
                return new Result(layers, Stopwatch.GetElapsedTime(started), Cancelled: true);
            }

            // **Only located when there is something to locate.** A layer the check could not run
            // on has no groups to point at, and paying a quarter of a second to find bridges nobody
            // can name would be spending the operator's time on an answer this view cannot use.
            var gaps = check.Ran
                ? Shifted(
                    ElectricalCheck.Gaps(layer.Area, layer.Nets, options, grown, token),
                    frame is { IsEmpty: false } f ? f : board.Bounds)
                : [];

            // **`Gaps` hands back what it has when the token turns, so the check belongs here and
            // not only at the top of the loop.** The loop's own test never runs again after the
            // last isolated layer, so a run cancelled inside the gap search on that layer used to
            // return `Cancelled: false` with a half-located list — and the window then shows a
            // layer whose bridges were partly found as one that was fully examined, with no banner.
            // That is the exact failure this record's `Cancelled` flag and the window's banner both
            // exist to prevent, arriving through the one door neither was watching.
            if (token.IsCancellationRequested)
            {
                return new Result(layers, Stopwatch.GetElapsedTime(started), Cancelled: true);
            }

            // The check above, reused: running it again here is a second full offset of the
            // copper for an answer already in hand.
            var findings = ElectricalFindings.For(
                layer, options, CheckSource.Layer(layer.Role, layer.Label, layer.FileName), check);

            layers.Add(new Layer(
                layer.Label,
                layer.Role,
                layer.FileName,
                findings.Verdict,
                options.EffectiveWidthNm,
                check.Joins,
                gaps,
                check.NetsSeen,
                check.Silent));
        }

        return new Result(layers, Stopwatch.GetElapsedTime(started), Cancelled: false);
    }

    /// <summary>
    /// Moves located gaps into the frame the operator's machine is in: the lower-left corner of the
    /// rectangle every emitted program is referenced to.
    ///
    /// **Which is not always the board's.** It was the board's unconditionally, and on a job built
    /// on stock that is wrong by the border — ten millimetres on the Mega's default, in both axes.
    /// The exporter shifts by the blank's rectangle when there is one, and the checks panel says so
    /// in words three lines from this window: *"Cut the stock before anything else, and keep it the
    /// right way up — its lower-left corner is work zero for every other file in this export."* So
    /// the list was printing a number in one frame while naming another, and an operator typing it
    /// into a controller would have driven to the wrong place.
    ///
    /// Found by 6.56, which is the first thing to consume these coordinates geometrically rather
    /// than print them: the ring landed ten millimetres from the bridge it was labelled with, which
    /// at the zoom it goes to is most of the viewport. Printed, the same error had been on screen
    /// since 6.44 and looked like a plausible number.
    ///
    /// **Done here rather than in <see cref="ElectricalCheck"/>**, which knows about copper and
    /// nothing about boards. A shift applied down there would be a geometry module guessing at a
    /// convention owned by the exporter, and the same coordinates are wanted unshifted by anything
    /// comparing against the artwork — the tests that verify a gap is really in the grown copper do
    /// exactly that.
    /// </summary>
    private static IReadOnlyList<NetGap> Shifted(IReadOnlyList<NetGap> gaps, Bounds bounds)
    {
        if (bounds.IsEmpty)
        {
            // Nothing sensible to measure from. Left alone rather than shifted by a sentinel, and
            // the view says which frame it is showing.
            return gaps;
        }

        return [.. gaps.Select(g => g with
        {
            At = new Point2(g.At.X - bounds.MinX, g.At.Y - bounds.MinY),
        })];
    }
}
