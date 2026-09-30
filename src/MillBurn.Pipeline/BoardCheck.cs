using System.Diagnostics;
using MillBurn.Core;

namespace MillBurn.Pipeline;

/// <summary>
/// Check a board without exporting it.
///
/// **The fault this exists for.** The board-level checks — a layer that would not read, two files
/// claiming the same role, a missing outline — run when a board is opened. The electrical ones,
/// which are the valuable ones, arrive through the export plan, so they appear only once somebody
/// has pressed Preview or Export. Open a board with a short on it, look at the CHECK panel, and it
/// is not there. 06 §6.44 calls that *"closer to a bug than to an enhancement"*, and it is the
/// strongest argument for 6.43's button.
///
/// **It does not replace the automatic check, and that was the decision, not an oversight.** The
/// Arduino Mega's short was found because the app said so without being asked. A button only
/// protects an operator who thinks to press it, and the one most at risk is the one who does not
/// know there is a question. So this adds a way to ask and removes nothing: planning still checks,
/// and still refuses to let a board through quietly.
///
/// **It answers about the cut the export would make**, which is why the tool and the options come
/// from <see cref="ExportPlanner.ToolFor"/> and <see cref="ExportPlanner.IsolationFor"/> rather
/// than from anything built here. A check run against a different width answers a question nobody
/// asked, and would be worse than no check because it would be believed.
/// </summary>
public static class BoardCheck
{
    /// <summary>What the check found, and what it cost.</summary>
    /// <param name="Checks">Every finding, in layer order.</param>
    /// <param name="Layers">
    /// The file name of every layer that was looked at — **including the ones with nothing to
    /// say**, which is the part a count cannot carry.
    ///
    /// **It is evidence that the run really looked, and the panel no longer keys on it.** It was
    /// written for a caller replacing what it said last time, so that a layer which had come clean
    /// did not keep its old line for ever; that caller now clears every electrical line instead,
    /// because a layer switched off since the last press is absent from this list too. What
    /// survives is the guarantee: a name here means that layer was examined, which is what makes
    /// "nothing to report" safe to say, and what `LayersChecked` counts.
    /// </param>
    /// <param name="Elapsed">How long it took, for the caller to say out loud.</param>
    /// <param name="Cancelled">
    /// True when the run was superseded or abandoned, so the caller can avoid showing a partial
    /// answer as a complete one — the distinction sprint 2's story 3 was about.
    /// </param>
    public sealed record Result(
        IReadOnlyList<Check> Checks,
        IReadOnlyList<string> Layers,
        TimeSpan Elapsed,
        bool Cancelled)
    {
        public int LayersChecked => Layers.Count;
    }

    /// <summary>
    /// Run the electrical check over every layer this board would isolate.
    ///
    /// **Only the layers that would actually be cut.** Checking a layer switched off would report
    /// shorts in copper nobody is going to machine, and the operator would have no way to act on
    /// it — the cut width that decides the answer is a property of an operation that is not going
    /// to happen.
    /// </summary>
    /// <param name="board">The board as it currently stands.</param>
    /// <param name="settings">What each layer is set to become.</param>
    /// <param name="library">The operator's tools, so the check uses the bit the export would.</param>
    /// <param name="token">
    /// Read between layers. A six-layer board takes seconds, and a window that cannot abandon the
    /// answer it no longer wants is the fault sprint 2 spent a story on.
    /// </param>
    public static Result For(
        Board board,
        IReadOnlyDictionary<string, LayerOutputSettings> settings,
        ToolLibrary library,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);

        var started = Stopwatch.GetTimestamp();
        var checks = new List<Check>();
        var looked = new List<string>();

        foreach (var layer in board.Layers)
        {
            if (token.IsCancellationRequested)
            {
                return new Result(checks, looked, Stopwatch.GetElapsedTime(started), Cancelled: true);
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

            var findings = ElectricalFindings.For(
                layer, options, CheckSource.Layer(layer.Role, layer.Label, layer.FileName));

            checks.AddRange(findings.Checks);
            looked.Add(layer.FileName);
        }

        return new Result(checks, looked, Stopwatch.GetElapsedTime(started), Cancelled: false);
    }
}
