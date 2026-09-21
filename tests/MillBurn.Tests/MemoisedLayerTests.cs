using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Geometry;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Sprint 1 story 2: changing one layer stops redoing all of them.
///
/// A realised layer is a pure function of its bytes, its role and the realisation options, so a
/// preview that follows a ticked checkbox changes none of those and need build nothing at all.
/// These check that the work is genuinely skipped rather than merely fast, and — the part that
/// matters more — that skipping it changes no answer.
/// </summary>
[Collection("memo")]
public sealed class MemoisedLayerTests(ITestOutputHelper output)
{
    private static List<(string FileName, byte[] Content, LayerRole Role)> Sources()
    {
        var folder = RealBoards.Directory(RealBoards.PogoTest1);

        return [.. Directory.EnumerateFiles(folder)
            .Where(BoardLoader.IsBoardFile)
            .Order(StringComparer.Ordinal)
            .Select(f => (
                Path.GetFileName(f),
                File.ReadAllBytes(f),
                LayerRoles.FromFileName(Path.GetFileName(f))))];
    }

    /// <summary>
    /// The same bytes in different arrays are the same layer.
    ///
    /// This is the property the application actually depends on and the one every other test here
    /// misses: `ProjectFile.ToBoard` calls `.AsSpan().ToArray()` on every source, every time, so
    /// the running program never hands the same array to two loads. A memo keyed on array identity
    /// would pass every other test in this file and never once hit in the app.
    /// </summary>
    [Fact]
    public void TheSameBytesInADifferentArrayAreTheSameLayer()
    {
        var sources = Sources();
        Assert.NotEmpty(sources);

        RealisedLayers.Forget();

        var first = BoardLoader.LoadSources("test", sources);

        // Copied, as ToBoard copies them — same bytes, different arrays.
        var copies = sources.Select(s => (s.FileName, (byte[])[.. s.Content], s.Role)).ToList();

        Assert.All(
            copies.Zip(sources),
            pair => Assert.False(ReferenceEquals(pair.First.Item2, pair.Second.Content)));

        var second = BoardLoader.LoadSources("test", copies);

        output.WriteLine($"{RealisedLayers.Hits} served from memory across a copy of the bytes");

        Assert.Equal(sources.Count, RealisedLayers.Hits);
        Assert.Equal(sources.Count, RealisedLayers.Misses);

        for (var i = 0; i < first.Layers.Count; i++)
        {
            Assert.Same(first.Layers[i], second.Layers[i]);
        }
    }

    /// <summary>
    /// The same board built twice builds nothing the second time, and hands back the very same
    /// layers.
    ///
    /// <c>Assert.Same</c> is the point. Equal geometry could be equal because it was realised again
    /// and came out the same; the same instance can only mean the work was not done.
    /// </summary>
    [Fact]
    public void ABoardBuiltTwiceIsOnlyBuiltOnce()
    {
        var sources = Sources();
        Assert.NotEmpty(sources);

        RealisedLayers.Forget();

        var first = BoardLoader.LoadSources("test", sources);
        var built = RealisedLayers.Misses;

        var second = BoardLoader.LoadSources("test", sources);

        output.WriteLine($"{built} realised, then {RealisedLayers.Hits} served from memory");

        Assert.Equal(sources.Count, built);
        Assert.Equal(sources.Count, RealisedLayers.Hits);
        Assert.Equal(built, RealisedLayers.Misses);

        for (var i = 0; i < first.Layers.Count; i++)
        {
            Assert.Same(first.Layers[i], second.Layers[i]);
        }
    }

    /// <summary>
    /// Changing one file rebuilds that layer and no other — which is the story's whole claim, and
    /// the thing a cache keyed on anything looser would get wrong.
    /// </summary>
    [Fact]
    public void ChangingOneLayerRebuildsOnlyThatLayer()
    {
        var sources = Sources();
        RealisedLayers.Forget();

        var before = BoardLoader.LoadSources("test", sources);
        var wasBuilt = RealisedLayers.Misses;

        // One layer's bytes change: a comment added, which a Gerber reader ignores but which makes
        // this a different file by any honest measure of "different".
        var changed = sources.FindIndex(s => s.FileName.Contains("F_Cu", StringComparison.Ordinal));
        Assert.True(changed >= 0);

        sources[changed] = (
            sources[changed].FileName,
            [.. sources[changed].Content, .. "G04 one more comment*\n"u8],
            sources[changed].Role);

        var after = BoardLoader.LoadSources("test", sources);

        output.WriteLine($"{RealisedLayers.Misses - wasBuilt} rebuilt of {sources.Count}");

        // Exactly one more layer was built.
        Assert.Equal(wasBuilt + 1, RealisedLayers.Misses);

        var edited = sources[changed].FileName;

        Assert.Equal(before.Layers.Count, after.Layers.Count);

        for (var i = 0; i < before.Layers.Count; i++)
        {
            // By name, not by position: the index came from the source list, and nothing here
            // promises the loader emits layers in that order or drops none.
            if (before.Layers[i].FileName == edited)
            {
                Assert.NotSame(before.Layers[i], after.Layers[i]);

                // A Gerber comment is inert, so the rebuilt layer has to be the same board.
                Assert.Equal(before.Layers[i].Bounds, after.Layers[i].Bounds);
                Assert.Equal(before.Layers[i].ObjectCount, after.Layers[i].ObjectCount);
            }
            else
            {
                Assert.Same(before.Layers[i], after.Layers[i]);
            }
        }
    }

    /// <summary>
    /// Different realisation options are a different answer, so they must not share an entry.
    ///
    /// This is the failure a key built from the filename alone would have: the geometry would be
    /// whatever the first caller asked for, and every later caller would silently get it.
    /// </summary>
    [Fact]
    public void DifferentOptionsAreNotTheSameLayer()
    {
        var sources = Sources();
        RealisedLayers.Forget();

        var fine = BoardLoader.LoadSources("test", sources, new RealisationOptions { SagittaNm = 1_000 });
        var coarse = BoardLoader.LoadSources("test", sources, new RealisationOptions { SagittaNm = 50_000 });

        Assert.Equal(sources.Count * 2, RealisedLayers.Misses);
        Assert.Equal(0, RealisedLayers.Hits);

        for (var i = 0; i < fine.Layers.Count; i++)
        {
            Assert.NotSame(fine.Layers[i], coarse.Layers[i]);
        }

        // Two instances would also be what "not caching at all" looks like, and a sagitta the
        // realiser ignored would look the same again. So: the coarser flattening must actually
        // produce fewer points somewhere, or these are two names for one answer.
        Assert.True(
            fine.Layers.Zip(coarse.Layers).Any(p => Polygons.VertexCount(p.First.Area)
                > Polygons.VertexCount(p.Second.Area)),
            "a 50 um sagitta should flatten curves more coarsely than a 1 um one");

        // And asking again for what was already built is a hit, which is what rules out
        // "remembers nothing" as the explanation for everything above.
        BoardLoader.LoadSources("test", sources, new RealisationOptions { SagittaNm = 1_000 });
        Assert.Equal(sources.Count, RealisedLayers.Hits);
    }

    /// <summary>
    /// The answer is the same whether it was built or remembered.
    ///
    /// The whole risk of a cache is that it is right about when to skip and wrong about what to
    /// hand back. A board read with an empty memo and the same board read with a full one have to
    /// be the same board, layer for layer.
    /// </summary>
    [Fact]
    public void RememberingChangesNoAnswer()
    {
        var sources = Sources();

        RealisedLayers.Forget();
        var built = BoardLoader.LoadSources("test", sources);

        // Forgotten in between, so the second board is genuinely realised again rather than being
        // the same instances. Comparing an object with itself proves nothing.
        RealisedLayers.Forget();
        var rebuilt = BoardLoader.LoadSources("test", sources);

        var remembered = BoardLoader.LoadSources("test", sources);

        Assert.NotSame(built.Layers[0], rebuilt.Layers[0]);
        Assert.Same(rebuilt.Layers[0], remembered.Layers[0]);

        Assert.Equal(built.Layers.Count, remembered.Layers.Count);

        for (var i = 0; i < built.Layers.Count; i++)
        {
            var a = built.Layers[i];
            var b = remembered.Layers[i];

            Assert.Equal(a.FileName, b.FileName);
            Assert.Equal(a.Role, b.Role);
            Assert.Equal(a.Bounds, b.Bounds);
            Assert.Equal(a.ObjectCount, b.ObjectCount);
            Assert.Equal(a.Drill?.Hits.Count, b.Drill?.Hits.Count);
        }
    }
}

/// <summary>
/// The memo is one table shared by the whole process, so these run on their own rather than beside
/// a test that is loading boards for its own reasons and would move the counts underneath them.
/// </summary>
[CollectionDefinition("memo", DisableParallelization = true)]
public sealed class MemoisedLayerFixture;
