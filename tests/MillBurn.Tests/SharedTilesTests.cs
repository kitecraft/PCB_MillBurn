using MillBurn.Core;
using MillBurn.Pipeline;
using MillBurn.Viewer;
using SkiaSharp;

namespace MillBurn.Tests;

/// <summary>
/// A big stroked layer's tiles, built once per geometry and shared by the scenes drawn from it.
///
/// **Found by sprint 6's `xhigh` review:** tiling simplifies a layer five times over, and it ran in
/// every scene build — every preview landing, every colour change — on the UI thread, though a
/// colour changes nothing the tiles hold. Shared, a rebuild over the same geometry takes the tiles
/// it has. What these pin is that sharing is safe: the scene being replaced is disposed after its
/// successor is built, and the successor's tiles must outlive it.
/// </summary>
public sealed class SharedTilesTests
{
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

    private static BoardScene Scene(IReadOnlyList<IReadOnlyList<Point2>> rings, SKColor colour) =>
        BoardScene.Build([("layer", "Layer", BoardPalette.For(LayerRole.Outline) with { Fill = colour }, rings)]);

    /// <summary>Every tile path of the finest tier, read — which a freed tile could not survive.</summary>
    private static int Drawn(BoardScene scene) =>
        scene.Layer("layer")!.TilePaths(0, new SKRect(-1e6f, -1e6f, 1e6f, 1e6f)).Sum(p => p.PointCount);

    private static readonly int Big = (BoardScene.TiledStrokeBudget / 4) + 1;

    /// <summary>
    /// A recolour over the same geometry takes the tiles already built, and they outlive the scene
    /// that built them: the old scene is disposed and the new one still draws every point.
    /// </summary>
    [Fact]
    public void ARecolourKeepsTheTilesAndOutlivesTheOldScene()
    {
        var rings = Squares(Big);

        var first = Scene(rings, SKColors.Orange);
        var expected = Drawn(first);
        using var second = Scene(rings, SKColors.Teal);

        Assert.Same(first.Layer("layer")!.Tiles, second.Layer("layer")!.Tiles);

        first.Dispose();
        Assert.Equal(expected, Drawn(second));
        Assert.True(expected > 0);
    }

    /// <summary>
    /// Once every scene holding them is gone, the tiles are freed, and the next build over the same
    /// geometry makes new ones rather than handing out freed ones.
    /// </summary>
    [Fact]
    public void FreedTilesAreRebuiltNotReused()
    {
        var rings = Squares(Big);

        var first = Scene(rings, SKColors.Orange);
        var tiles = first.Layer("layer")!.Tiles;
        var expected = Drawn(first);
        Assert.True(expected > 0);
        first.Dispose();

        using var again = Scene(rings, SKColors.Orange);
        Assert.NotSame(tiles, again.Layer("layer")!.Tiles);
        Assert.Equal(expected, Drawn(again));
    }

    /// <summary>
    /// Different geometry is never served another's tiles — not even geometry with the same points
    /// in a different list, since the cache cannot know the two will stay equal.
    /// </summary>
    [Fact]
    public void OtherGeometryGetsItsOwnTiles()
    {
        using var first = Scene(Squares(Big), SKColors.Orange);
        using var second = Scene(Squares(Big), SKColors.Orange);

        Assert.NotSame(first.Layer("layer")!.Tiles, second.Layer("layer")!.Tiles);
    }

    /// <summary>
    /// A board layer hands back the same rings each time it is asked — which is what lets a
    /// rebuild of the board find its tiles, since the scene's cache is keyed on the rings.
    /// </summary>
    [Fact]
    public void ABoardLayersRingsAreTheSameObjectEachTime()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));
        var copper = board.Layers.First(l => l.Role == LayerRole.TopCopper);

        Assert.Same(copper.Rings(), copper.Rings());
        Assert.Same(copper.Rings(), (copper with { RoleGuessed = !copper.RoleGuessed }).Rings());
    }
}
