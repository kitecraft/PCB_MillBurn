using System.Collections;
using MillBurn.Pipeline;

namespace MillBurn.Tests;

/// <summary>
/// The board and the settings wrapper that story 3's tests cancel a plan with.
///
/// **Shared by linking, the way `RealBoards.cs` is**, and for the same reason the project file
/// gives there: one definition, so the two assemblies cannot disagree about what they are
/// measuring. It has to be shared because the tests are split across them on purpose —
/// `CancellablePlanningTests` asserts things local to its own run and can live beside the rest of
/// `MillBurn.Tests`, while `CancelledPlanningWorkTests` reads the global work and memo counters and
/// belongs where classes run one at a time. Same fixture, two homes.
/// </summary>
internal static class PlanningFixture
{
    /// <summary>
    /// The test board, loaded from its bytes rather than from its folder.
    ///
    /// `LoadFolder` gives layers with no fingerprint, and a board the memo cannot identify is
    /// planned in full every time — so anything asserted about remembering would read zero and pass
    /// by saying nothing. `MemoisedPlanTests` loads the same way, for the same reason.
    /// </summary>
    public static Board Board() =>
        BoardLoader.LoadSources(
            "test",
            Directory.EnumerateFiles(RealBoards.Directory(RealBoards.MillburnTestBoard))
                .Where(BoardLoader.IsBoardFile)
                .Order(StringComparer.Ordinal)
                .Select(f => (
                    Path.GetFileName(f),
                    File.ReadAllBytes(f),
                    LayerRoles.FromFileName(Path.GetFileName(f)))));

    public static Dictionary<string, LayerOutputSettings> Outputs(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);

        return board.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);
    }
}

/// <summary>
/// The layer settings, cancelling once the planner has asked about enough of them.
///
/// **The same trick `CancelsPartWayThrough` plays on the preview's programs**, and for the same
/// reason: the run is in the middle of its work when its token turns, with no sleeping and no
/// racing, and the answer is the same on a slow machine as on a fast one.
///
/// **<see cref="Reached"/> counts lookups, not layers, and the difference matters.** Resolving the
/// blank scans the settings before the layer loop ever runs, with a short-circuiting `Any`, so the
/// first few asks are not layers at all — on the test board three of them are not. A test that
/// cancels after two and calls that "two layers in" has actually stopped the run before it started
/// planning anything, which is the failure mode these tests exist to rule out. Callers that need
/// to be inside the board should measure a whole run's lookups first and take a fraction of it,
/// as <c>CancellablePlanningTests.PlanningStopsBeforeTheEndOfTheBoard</c> does.
///
/// Only <see cref="TryGetValue"/> cancels. `ExportPlanner.Plan` enumerates the whole dictionary to
/// build its memo key, and that is not the planner asking about a layer.
/// </summary>
internal sealed class CancelsAfterLayers(
    IReadOnlyDictionary<string, LayerOutputSettings> inner, int after, CancellationTokenSource stop)
    : IReadOnlyDictionary<string, LayerOutputSettings>
{
    /// <summary>How many settings lookups the planner made before it stopped.</summary>
    public int Reached { get; private set; }

    public bool TryGetValue(string key, out LayerOutputSettings value)
    {
        Reached++;

        if (Reached > after)
        {
            stop.Cancel();
        }

        return inner.TryGetValue(key, out value!);
    }

    public LayerOutputSettings this[string key] => inner[key];

    public IEnumerable<string> Keys => inner.Keys;

    public IEnumerable<LayerOutputSettings> Values => inner.Values;

    public int Count => inner.Count;

    public bool ContainsKey(string key) => inner.ContainsKey(key);

    public IEnumerator<KeyValuePair<string, LayerOutputSettings>> GetEnumerator() => inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// The layer settings, signalling once the planner has asked about enough of them.
///
/// **So that a run can be superseded while it is provably under way.** Starting four plans and
/// cancelling them as fast as the loop goes round supersedes most of them before the thread pool
/// has begun them, and a token checked once at the top of the method would satisfy a test written
/// that way — which is the sprint-1 failure mode exactly. Waiting for this signal before
/// superseding means the run is past its opening check and into the board.
///
/// <paramref name="at"/> is a count of lookups, with the same caveat as
/// <see cref="CancelsAfterLayers"/>: the first few are the blank being resolved, not layers.
/// </summary>
internal sealed class SignalsAtLayer(
    IReadOnlyDictionary<string, LayerOutputSettings> inner, int at, TaskCompletionSource reached)
    : IReadOnlyDictionary<string, LayerOutputSettings>
{
    /// <summary>How many settings lookups the planner has made.</summary>
    public int Reached { get; private set; }

    public bool TryGetValue(string key, out LayerOutputSettings value)
    {
        Reached++;

        if (Reached >= at)
        {
            reached.TrySetResult();
        }

        return inner.TryGetValue(key, out value!);
    }

    public LayerOutputSettings this[string key] => inner[key];

    public IEnumerable<string> Keys => inner.Keys;

    public IEnumerable<LayerOutputSettings> Values => inner.Values;

    public int Count => inner.Count;

    public bool ContainsKey(string key) => inner.ContainsKey(key);

    public IEnumerator<KeyValuePair<string, LayerOutputSettings>> GetEnumerator() => inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
