using MillBurn.Core;
using MillBurn.Viewer;
using SkiaSharp;

namespace MillBurn.Tests;

/// <summary>
/// The tiled copy a big stroked layer carries (roadmap 6.71).
///
/// The 66-up panel previews two stroked layers of 108k and 42k vertices, and anti-aliased
/// stroking of their monolithic paths was 93 % of a 69 ms frame. Such a layer now also builds a
/// <see cref="ToolpathScene"/> — whose level-of-detail and culling have their own tests — and the
/// renderer draws through it. What is covered here is the seam: who gets tiles and who does not,
/// that the tiles hold exactly the segments the monolithic path draws, and that a frame through
/// the tiles really is culled rather than merely counted differently.
/// </summary>
public sealed class BoardStrokeTileTests
{
    /// <summary>A grid of small closed squares totalling <paramref name="count"/>·4 vertices.</summary>
    private static List<IReadOnlyList<Point2>> Squares(int count)
    {
        var size = Nm.FromMillimetres(0.3);
        var pitch = Nm.FromMillimetres(0.5);
        var perRow = (int)Math.Ceiling(Math.Sqrt(count));

        var rings = new List<IReadOnlyList<Point2>>(count);
        for (var i = 0; i < count; i++)
        {
            var x = i % perRow * pitch;
            var y = i / perRow * pitch;
            rings.Add(
            [
                new Point2(x, y),
                new Point2(x + size, y),
                new Point2(x + size, y + size),
                new Point2(x, y + size),
            ]);
        }

        return rings;
    }

    /// <summary>Line and close verbs, walked, because nothing on SKPath counts them.</summary>
    private static (int Lines, int Closes) Verbs(SKPath path)
    {
        var lines = 0;
        var closes = 0;

        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];

        for (var verb = iterator.Next(points); verb != SKPathVerb.Done; verb = iterator.Next(points))
        {
            switch (verb)
            {
                case SKPathVerb.Line: lines++; break;
                case SKPathVerb.Close: closes++; break;
                default: break;
            }
        }

        return (lines, closes);
    }

    /// <summary>
    /// The budget divides stroked layers, and only stroked ones: a fill is about 8× cheaper a
    /// vertex and was never the problem, so a filled layer of any size stays monolithic — as does
    /// every layer of an ordinary board, whose drawing this change must not touch.
    /// </summary>
    [Fact]
    public void OnlyABigStrokedLayerCarriesTiles()
    {
        var big = Squares((BoardScene.TiledStrokeBudget / 4) + 1);
        var small = Squares(100);

        using var scene = BoardScene.Build(
        [
            ("big-stroked", "Big stroked", BoardPalette.For(LayerRole.Outline), big),
            ("big-filled", "Big filled", BoardPalette.For(LayerRole.TopCopper), big),
            ("small-stroked", "Small stroked", BoardPalette.For(LayerRole.Outline), small),
        ]);

        Assert.True(scene.Layer("big-stroked")!.VertexCount >= BoardScene.TiledStrokeBudget);
        Assert.NotNull(scene.Layer("big-stroked")!.Tiles);
        Assert.Null(scene.Layer("big-filled")!.Tiles);
        Assert.Null(scene.Layer("small-stroked")!.Tiles);
    }

    /// <summary>
    /// At the finest tier with everything in view, the tiles draw segment-for-segment what the
    /// monolithic path draws. A closed ring's close verb becomes an explicit repeated point in
    /// its polyline, so the counts meet as lines + closes — a tile set that dropped or invented a
    /// segment would show up here as a board whose cut lines differ by zoom level.
    /// </summary>
    [Fact]
    public void TheTilesHoldEverySegmentThePathDraws()
    {
        using var scene = BoardScene.Build(
        [
            ("outline", "Outline", BoardPalette.For(LayerRole.Outline),
                Squares((BoardScene.TiledStrokeBudget / 4) + 1)),
        ]);

        var layer = scene.Layer("outline")!;
        var (lines, closes) = Verbs(layer.Path);

        var everything = new SKRect(-1_000_000, -1_000_000, 1_000_000, 1_000_000);
        var tiled = 0;
        foreach (var path in layer.TilePaths(0, everything))
        {
            tiled += Verbs(path).Lines;
        }

        Assert.Equal(lines + closes, tiled);
        Assert.Equal(lines + closes, layer.Tiles!.SegmentsInView(0, everything));

        // And in the same place: counts alone would pass with the tiles mirrored or shifted —
        // the Y flip between board nanometres and scene millimetres is exactly the mistake a
        // rebuild of this geometry could make.
        var monolithic = layer.Path.Bounds;
        var tileExtent = layer.Tiles.Bounds;
        Assert.Equal(monolithic.Left, tileExtent.Left, 3);
        Assert.Equal(monolithic.Top, tileExtent.Top, 3);
        Assert.Equal(monolithic.Right, tileExtent.Right, 3);
        Assert.Equal(monolithic.Bottom, tileExtent.Bottom, 3);
    }

    /// <summary>
    /// A frame that shows one corner draws one corner. Asserted through
    /// <see cref="BoardRenderer.Draw"/> itself — not on the scene — so the assertion fails if the
    /// renderer ever quietly goes back to the monolithic path, which is exactly how 6.71's cost
    /// would creep back without the picture changing.
    /// </summary>
    [Fact]
    public void AFrameThroughTheTilesIsCulledToItsView()
    {
        using var scene = BoardScene.Build(
        [
            ("outline", "Outline", BoardPalette.For(LayerRole.Outline),
                Squares((BoardScene.TiledStrokeBudget / 4) + 1)),
        ]);

        var layer = scene.Layer("outline")!;

        using var surface = SKSurface.Create(new SKImageInfo(300, 300));
        var viewport = SKRect.Create(300, 300);

        // 100 px/mm selects tier 0, and the offset puts the world's top-left 3 × 3 mm —
        // a few dozen of the squares — on screen.
        var corner = new ViewTransform(Scale: 100f, OffsetX: 0f, OffsetY: 300f);

        var result = BoardRenderer.Draw(
            surface.Canvas, viewport, scene, corner, SKColors.Black, SKColors.Transparent);

        Assert.Equal(1, result.LayersDrawn);
        Assert.InRange(result.VerticesDrawn, 1, layer.VertexCount / 10);
    }

    /// <summary>
    /// The degradation ladder runs invisible → nearly invisible → hairline, in that order.
    ///
    /// The half-pixel tier is relaxed up to one pixel of simplification error before the frame
    /// gives up anti-aliasing and stroke widths — because the panel's fit zoom sat exactly on a
    /// tier boundary, and a few pixels of window width used to decide which of two very
    /// different-looking frames the hero got. Counts are supplied by hand so each rung is
    /// exercised deliberately; the real counting is covered by the tests above.
    /// </summary>
    [Fact]
    public void TheTierRelaxesByAtMostOnePixelBeforeGoingHairline()
    {
        // 7 px/mm: the half-pixel tier is 3. Under budget there: stays.
        Assert.Equal(3, BoardRenderer.TiledTier(7f, _ => 10_000));

        // Over budget at tier 3, under at tier 4, whose error at 7 px/mm is 0.56 px — within
        // the one-pixel allowance, so the frame trades error for its anti-aliasing.
        Assert.Equal(4, BoardRenderer.TiledTier(7f, t => t >= 4 ? 20_000 : 30_000));

        // Over budget everywhere: relax as far as the ladder goes, then stop — the renderer
        // draws that coarsest tier hairline rather than looping or lying.
        Assert.Equal(4, BoardRenderer.TiledTier(7f, _ => 30_000));

        // 60 px/mm: the half-pixel tier is 0. Tier 1 is 0.6 px — allowed; tier 2 is 1.2 px —
        // not. An over-budget view relaxes exactly one rung and no further.
        Assert.Equal(1, BoardRenderer.TiledTier(60f, _ => 30_000));
    }
}
