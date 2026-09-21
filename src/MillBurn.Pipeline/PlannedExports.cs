using System.IO.Hashing;
using System.Text;
using System.Text.Json;

namespace MillBurn.Pipeline;

/// <summary>
/// Export plans, kept so that planning a board that has not changed is not planned again.
///
/// Planning is the expensive half: it isolates the copper, orders the travel, builds every program
/// and writes the text of each. It is also a pure function of its arguments — a board, what each
/// layer should become, a tool library, a thickness, and a handful of settings records, every one
/// of them immutable. So the same arguments give the same plan, and the plan can be remembered.
///
/// **The board is identified by its layers' fingerprints, not by its geometry.** Each layer already
/// carries the hash it was realised under (see <see cref="RealisedLayers"/>), so asking whether this
/// is the same board is comparing a few dozen short strings rather than walking millions of points.
/// A board with any layer that has no fingerprint — realised by a path that does not memoise — is
/// not identifiable, and then nothing is remembered rather than something being guessed.
///
/// The settings go in as JSON. It is slower than a hash code and it is the only way to get a key
/// that means the same thing on the next run and the next machine: <c>string.GetHashCode</c> is
/// randomised per process by design, so a key built from it would be correct today and quietly
/// wrong the moment anything cached across a restart.
/// </summary>
public static class PlannedExports
{
    /// <summary>
    /// How much emitted text to keep, in characters — about 64 MB _held.
    ///
    /// Bounded by size rather than by count, because plans are not the same size as each other.
    /// The author's Arduino Mega plans to 0.6 MB of programs and the six-layer board in 6.30 to
    /// 7.2 MB, a twelvefold spread, so any fixed number of plans is either wasteful for one or
    /// useless for the other. A count kept four of the big ones — and four of the small ones too,
    /// which threw away work worth two seconds because the operator had nudged a setting five
    /// times instead of three.
    /// </summary>
    private const long KeepChars = 32L * 1024 * 1024;

    /// <summary>
    /// A ceiling on the number of entries as well, for boards small enough that the size bound
    /// would never be reached. Nothing here should grow without limit.
    /// </summary>
    private const int KeepCount = 64;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, ExportPlan> Planned = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();
    private static long _held;

    /// <summary>Plans served from memory rather than planned again.</summary>
    public static long Hits { get; private set; }

    /// <summary>Plans that had to be built.</summary>
    public static long Misses { get; private set; }

    /// <summary>Plans not looked for, because the board could not be identified.</summary>
    public static long Unidentifiable { get; private set; }

    /// <summary>Forgets everything, for a test that needs to know what was planned rather than found.</summary>
    public static void Forget()
    {
        lock (Gate)
        {
            Planned.Clear();
            Order.Clear();
            _held = 0;
            Hits = 0;
            Misses = 0;
            Unidentifiable = 0;
        }
    }

    /// <summary>
    /// The plan for these inputs, built by <paramref name="plan"/> only if it is not already known.
    /// </summary>
    /// <param name="board">The board, identified by its layers' fingerprints.</param>
    /// <param name="inputs">Everything else the planner reads, in a form that serialises.</param>
    /// <param name="plan">Builds the plan. Called at most once per distinct key.</param>
    internal static ExportPlan Get(Board board, object inputs, Func<ExportPlan> plan)
    {
        if (KeyFor(board, inputs) is not { } key)
        {
            lock (Gate)
            {
                Unidentifiable++;
            }

            return plan();
        }

        lock (Gate)
        {
            if (Planned.TryGetValue(key, out var known))
            {
                Hits++;
                return known;
            }
        }

        // Outside the lock, for the same reason RealisedLayers builds outside its own: planning is
        // the slow part, and a lock _held across it would serialise the very work story 1 moved off
        // the UI thread so that it could overlap.
        var made = plan();

        lock (Gate)
        {
            Misses++;

            if (Planned.TryAdd(key, made))
            {
                Order.Enqueue(key);
                _held += Weigh(made);
            }

            while (Order.Count > KeepCount || (_held > KeepChars && Order.Count > 1))
            {
                var oldest = Order.Dequeue();

                if (Planned.Remove(oldest, out var dropped))
                {
                    _held -= Weigh(dropped);
                }
            }

            return Planned.TryGetValue(key, out var shared) ? shared : made;
        }
    }

    /// <summary>
    /// The emitted text a plan is holding on to.
    ///
    /// Never evicts the only entry, however large: a board too big to keep one plan of is the board
    /// that most needs the second preview to be quick.
    /// </summary>
    private static long Weigh(ExportPlan plan)
    {
        long chars = plan.Page?.Content.Length ?? 0;

        foreach (var item in plan.Items)
        {
            chars += item.Content.Length + (item.Companion?.Content.Length ?? 0);
        }

        return chars;
    }

    /// <summary>The key, or null when the board cannot be identified from its layers alone.</summary>
    private static string? KeyFor(Board board, object inputs)
    {
        var hash = new XxHash128();
        var scratch = new byte[sizeof(long)];

        // Each field goes in with its length ahead of it. Appended raw, a name and the content
        // after it are one run of bytes, and a file whose name is a prefix of another's could share
        // an entry with it — an input that changes the answer failing to change the key, which is
        // the one fault a cache must not have.
        void Append(ReadOnlySpan<byte> bytes)
        {
            BitConverter.TryWriteBytes(scratch, (long)bytes.Length);
            hash.Append(scratch);
            hash.Append(bytes);
        }

        // Where the board came from is part of the answer, not just of the bookkeeping: the stock
        // program is named from it, and it is printed as the board's name on the project page and
        // on the drill and routing guides. Two projects holding the same Gerbers — a Save As copy,
        // or one export folder imported twice, which 6.32 says is easily done — would otherwise
        // share an entry, and the second would be handed the first one's file names and paperwork.
        Append(Encoding.UTF8.GetBytes(board.Source));

        foreach (var layer in board.Layers)
        {
            if (string.IsNullOrEmpty(layer.Fingerprint))
            {
                return null;
            }

            Append(Encoding.UTF8.GetBytes(layer.FileName));
            Append(Encoding.UTF8.GetBytes(layer.Fingerprint));

            BitConverter.TryWriteBytes(scratch, (long)layer.Role);
            hash.Append(scratch);
        }

        try
        {
            hash.Append(JsonSerializer.SerializeToUtf8Bytes(inputs, JsonOptions));
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or JsonException)
        {
            // Better to plan every time than to key on a description that leaves part of the input
            // out. The net is wide because the obvious exception is not the likely one: a settings
            // field holding infinity — which `double.TryParse("1e999")` produces, and nothing in
            // SettingsCheck rejects — is an ArgumentException from the serialiser, not a
            // NotSupportedException. Caught narrowly, one over-large number typed into Settings
            // would have turned every preview after it into "Preview failed".
            return null;
        }

        return Convert.ToHexString(hash.GetCurrentHash());
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Not for reading. Included so that a field added to a settings record cannot go missing
        // from the key just because it happens to hold its default.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        IncludeFields = true,
    };
}
