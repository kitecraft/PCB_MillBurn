using MillBurn.Geometry;
using System.IO.Hashing;
using System.Text;
using MillBurn.Cam;
using MillBurn.Core;

namespace MillBurn.Pipeline;

/// <summary>
/// Realised layers, kept so that work already done is not done again.
///
/// Realising a layer is a pure function of three things: the bytes of the file, the role it is
/// being read as, and the realisation options. Nothing else reaches it — no clock, no counter, no
/// machine setting — so the same three inputs always give the same geometry, and a layer that has
/// not changed need not be built twice.
///
/// **The key is structural, never an identity.** Two byte arrays holding the same Gerber are the
/// same layer whether or not they are the same array, and a project reopened on another machine
/// must hit the same entries as the one that saved it. Keying on an object reference or a file
/// timestamp would be faster to compute and would quietly make the answer depend on how the caller
/// happened to get there, which is the opposite of what this pipeline promises.
///
/// Hashing the content costs a pass over the bytes. Realising it costs Clipper, so the trade is not
/// close: on the six-layer board in 6.30 the hash is a few milliseconds against seconds of work.
/// </summary>
public static class RealisedLayers
{
    /// <summary>
    /// How many points of geometry to keep — about 64 MB of them.
    ///
    /// Weighed, not counted, for the reason the plan cache is: entries differ by more than an
    /// order of magnitude. This is the cache holding the actual geometry, so counting was the
    /// worse place to do it — forty-eight layers of a small board is a few megabytes, and
    /// forty-eight of the 13 MB six-layer board in 6.30 is hundreds, held for the life of the
    /// process because nothing evicts when a project closes.
    /// </summary>
    private const long KeepVertices = 4L * 1024 * 1024;

    /// <summary>A ceiling on entries too, for boards small enough never to reach the size bound.</summary>
    private const int KeepCount = 256;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, BoardLayer> Built = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();
    private static long _held;

    /// <summary>Layers served from memory rather than realised again, for the tests to assert on.</summary>
    public static long Hits { get; private set; }

    /// <summary>Layers that had to be built.</summary>
    public static long Misses { get; private set; }

    /// <summary>
    /// The layer for these bytes, built by <paramref name="realise"/> only if it is not already
    /// known.
    /// </summary>
    /// <param name="fileName">Named in the key, because the same bytes under two names are two layers.</param>
    /// <param name="content">The file, hashed in full.</param>
    /// <param name="role">What it is being read as; the same file read two ways realises twice.</param>
    /// <param name="options">The realisation options, which change the geometry that comes out.</param>
    /// <param name="realise">Builds the layer. Called at most once per distinct key.</param>
    public static BoardLayer Get(
        string fileName,
        byte[] content,
        LayerRole role,
        RealisationOptions options,
        Func<BoardLayer> realise)
    {
        var key = KeyFor(fileName, content, role, options);

        lock (Gate)
        {
            if (Built.TryGetValue(key, out var known))
            {
                Hits++;
                return known;
            }
        }

        // Built outside the lock. Realising is the slow part, and holding a lock across it would
        // turn "two layers at once" into "one layer at a time" — which is the shape story 1 just
        // spent its effort undoing. Two threads racing on the same key both build it and the second
        // result is discarded; that costs one layer's work, once, and keeps the rest concurrent.
        // Stamped with the key it was built under, so a later stage can ask whether this is the
        // same geometry it saw last time without looking at the geometry.
        var made = realise() with { Fingerprint = key };

        lock (Gate)
        {
            Misses++;

            if (Built.TryAdd(key, made))
            {
                Order.Enqueue(key);
                _held += Weigh(made);
            }

            // Never the newest: a board too big to keep one layer of is the board that most needs
            // the next preview to skip the work.
            while (Order.Count > KeepCount || (_held > KeepVertices && Order.Count > 1))
            {
                if (Built.Remove(Order.Dequeue(), out var dropped))
                {
                    _held -= Weigh(dropped);
                }
            }

            return Built.TryGetValue(key, out var shared) ? shared : made;
        }
    }

    /// <summary>Forgets everything, for a test that needs to know what was built rather than found.</summary>
    public static void Forget()
    {
        lock (Gate)
        {
            Built.Clear();
            Order.Clear();
            _held = 0;
            Hits = 0;
            Misses = 0;
        }
    }

    /// <summary>The points a layer is holding on to, which is what its memory cost is made of.</summary>
    private static long Weigh(BoardLayer layer) =>
        Polygons.VertexCount(layer.Area) + (layer.Drill?.Hits.Count ?? 0);

    private static string KeyFor(
        string fileName, byte[] content, LayerRole role, RealisationOptions options)
    {
        var hash = new XxHash128();

        var scratch = new byte[sizeof(long)];

        void Append(long value)
        {
            BitConverter.TryWriteBytes(scratch, value);
            hash.Append(scratch);
        }

        // Length-prefixed, so the name and the content cannot run together into one stretch of
        // bytes. Without it a file whose name is a prefix of another's could share its entry, given
        // content shifted to match — an input that changes the answer failing to change the key.
        Append(fileName.Length);
        hash.Append(Encoding.UTF8.GetBytes(fileName));
        Append(content.Length);
        hash.Append(content);

        Append((long)role);
        Append(options.SagittaNm);
        Append(options.Canonicalise ? 1 : 0);

        return Convert.ToHexString(hash.GetCurrentHash());
    }
}
