using MillBurn.Core;
using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// How one layer is painted. Colours are the caller's business, not the scene's.
///
/// <paramref name="StrokePixels"/> is in *screen* pixels, not millimetres, and is undone against
/// the zoom when drawn. A toolpath line is an annotation about where the tool went, not a picture
/// of how wide the cut is — drawing it to scale would make it invisible at fit-zoom and absurd at
/// 40x. The cut width is reported as a number instead, where it can be read.
///
/// <paramref name="Outlined"/> and <paramref name="ClosedRings"/> are separate questions, and
/// conflating them is a bug this code has already had. "Stroke rather than fill" is about paint;
/// "these rings close" is about what the geometry *is*. The board outline is both — a closed area,
/// drawn as a line — and while one flag meant both, every outline ring was drawn missing exactly
/// the segment that would have closed it: one gap per ring, in the same place on all thirty-two
/// cells of a panel.
///
/// So <c>Outlined</c> means only "stroke rather than fill", and <c>ClosedRings</c> means "each ring
/// is a closed area". The latter is false for a backplot, whose rings are runs the tool travelled:
/// closing one draws a segment from the end of the program back to its start that the machine never
/// makes.
/// </summary>
public sealed record BoardLayerStyle(
    SKColor Fill,
    float Opacity = 1f,
    bool Outlined = false,
    float StrokePixels = 1.5f,
    float DashPixels = 0f,
    bool ClosedRings = true);

/// <summary>One layer's geometry, ready to draw.</summary>
public sealed class BoardSceneLayer : IDisposable
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required BoardLayerStyle Style { get; init; }

    public required SKPath Path { get; init; }

    public required int RingCount { get; init; }

    public required int VertexCount { get; init; }

    public bool Visible { get; set; } = true;

    /// <summary>
    /// The same strokes again, tiled and tiered, for a layer whose monolithic <see cref="Path"/>
    /// is too big to stroke every frame. Null for every layer under the budget — which is every
    /// layer of an ordinary board — and for every filled layer at any size, because a fill is
    /// about 8× cheaper a vertex and has never been the problem (6.71's measurement).
    /// </summary>
    public ToolpathScene? Tiles { get; init; }

    /// <summary>
    /// The prebuilt stroke paths of one tier that intersect the view. Empty — not an error —
    /// for a layer with no tiles, so a caller walking every layer's geometry does not have to
    /// know the budget rule to stay safe.
    /// </summary>
    public IEnumerable<SKPath> TilePaths(int tier, SKRect visibleWorld) =>
        Tiles?.VisiblePaths(tier, BoardScene.TileStyle, visibleWorld) ?? [];

    public void Dispose()
    {
        Path.Dispose();
        Tiles?.Dispose();
    }
}

/// <summary>
/// The board as filled area, in millimetres, ready for Skia.
///
/// Deliberately simpler than <see cref="ToolpathScene"/> for everything filled: no
/// level-of-detail, no spatial tiling. Those exist because a toolpath can run to hundreds of
/// thousands of segments; a board's copper does not. The largest layer in the corpus is 12,310
/// vertices and a real two-sided board is under 20,000 in total, which Skia fills without
/// noticing.
///
/// The panelised board the old note here promised did arrive (6.71): the 66-up panel previews two
/// *stroked* layers of 108k and 42k vertices, and anti-aliased geometric stroking is a per-vertex
/// cost that made them 93 % of a 69 ms frame while all the fills together cost 2.5 ms. So a
/// stroked layer at or past <see cref="TiledStrokeBudget"/> also gets the toolpath treatment — a
/// <see cref="ToolpathScene"/> built beside its monolithic path, tiered and tiled — and the
/// renderer draws whichever the frame can afford. Fills stay monolithic at any size, and so do
/// ordinary boards' strokes, whose code path this does not touch. Phase 0's lesson stands: the
/// budget is from measurement (<c>--boardbench</c>, and 6.71's per-layer timing), not a guess.
///
/// **Fill rule is winding, not even-odd.** Clipper2 emits outer rings counter-clockwise and holes
/// clockwise, so winding respects them. Even-odd would too here, but only by luck: it treats any
/// overlap as a hole, so two pads that touch would punch a void where they cross.
/// </summary>
public sealed class BoardScene : IDisposable
{
    /// <summary>
    /// Vertices at which a stroked layer gains its tiled copy. Between the whole of a real
    /// two-sided board (under 20k) and the panel's two slow layers (42k and 108k), so ordinary
    /// boards keep the exact path they always drew and only the boards that were slow change.
    /// </summary>
    public const int TiledStrokeBudget = 20_000;

    /// <summary>
    /// The one style key a layer's tiles file under. <see cref="ToolpathScene"/> buckets per
    /// (tile, style) because a toolpath mixes styles in one scene; a board layer is one style, so
    /// any key works and this one is it.
    /// </summary>
    internal const SegmentStyle TileStyle = SegmentStyle.Isolation;

    private readonly List<BoardSceneLayer> _layers = [];

    public IReadOnlyList<BoardSceneLayer> Layers => _layers;

    /// <summary>Extent in millimetres, Y already flipped into screen sense.</summary>
    public SKRect Bounds { get; private set; } = SKRect.Empty;

    public int TotalVertices { get; private set; }

    public static BoardScene Build(
        IEnumerable<(string Id, string Label, BoardLayerStyle Style, IReadOnlyList<IReadOnlyList<Point2>> Rings)> layers,
        Bounds? extent = null)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var scene = new BoardScene();
        var bounds = SKRect.Empty;
        var first = true;

        foreach (var (id, label, style, rings) in layers)
        {
            var path = new SKPath { FillType = SKPathFillType.Winding };
            var vertices = 0;
            var ringCount = 0;

            // Two points are a line and three are the least that can enclose anything, so what
            // counts as a drawable ring follows from whether the rings close.
            var closeRings = style.ClosedRings;
            var minimum = closeRings ? 3 : 2;

            foreach (var ring in rings)
            {
                if (ring.Count < minimum)
                {
                    continue;
                }

                path.MoveTo(ToMm(ring[0].X), -ToMm(ring[0].Y));
                for (var i = 1; i < ring.Count; i++)
                {
                    path.LineTo(ToMm(ring[i].X), -ToMm(ring[i].Y));
                }

                if (closeRings)
                {
                    path.Close();
                }

                vertices += ring.Count;
                ringCount++;
            }

            if (ringCount == 0)
            {
                path.Dispose();
                continue;
            }

            scene._layers.Add(new BoardSceneLayer
            {
                Id = id,
                Label = label,
                Style = style,
                Path = path,
                RingCount = ringCount,
                VertexCount = vertices,
                Tiles = style.Outlined && vertices >= TiledStrokeBudget
                    ? BuildTiles(rings, closeRings, minimum)
                    : null,
            });

            scene.TotalVertices += vertices;

            bounds = first ? path.Bounds : SKRect.Union(bounds, path.Bounds);
            first = false;
        }

        // An explicit extent wins, because the board outline is a better answer than the union of
        // whatever happens to be drawn: copper is pulled back from the edge, so fitting to copper
        // shows a board smaller than it is.
        scene.Bounds = extent is { IsEmpty: false } e
            ? new SKRect(ToMm(e.MinX), -ToMm(e.MaxY), ToMm(e.MaxX), -ToMm(e.MinY))
            : bounds;

        return scene;
    }

    /// <summary>
    /// Moves the rectangle this scene is measured from, without touching the geometry.
    ///
    /// **Because the extent and the paths are independent, and the extent is the cheap half.**
    /// Building a scene walks every ring of every layer into an `SKPath`; the extent is one
    /// assignment at the end of it. Changing the stock's border moves the extent and nothing else,
    /// and rebuilding the whole scene for it costs **about 7 ms on the Mega** against a reframe too
    /// small to time — per tick of a slider. Measured by
    /// `ReframingIsOrdersOfMagnitudeCheaperThanRebuilding`, best of five, which prints both.
    ///
    /// The caller keeps the same scene object, so a view that re-fits whenever it meets a new
    /// scene will not re-fit on this; whether to fit again is the caller's to say, and it has more
    /// to go on than this does.
    /// </summary>
    public void Reframe(Bounds extent)
    {
        if (extent.IsEmpty)
        {
            return;
        }

        Bounds = new SKRect(ToMm(extent.MinX), -ToMm(extent.MaxY), ToMm(extent.MaxX), -ToMm(extent.MinY));
    }

    public BoardSceneLayer? Layer(string id) =>
        _layers.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.Ordinal));

    public int VisibleVertices
    {
        get
        {
            var total = 0;
            foreach (var layer in _layers)
            {
                if (layer.Visible)
                {
                    total += layer.VertexCount;
                }
            }

            return total;
        }
    }

    /// <summary>
    /// The layer's rings again, as the polylines <see cref="ToolpathScene"/> tiles. Same
    /// millimetres, same flipped Y, so the tiles draw under the exact transform the monolithic
    /// path does. A closed ring closes by repeating its first point, because a polyline has no
    /// close verb — the segment count is identical either way.
    /// </summary>
    private static ToolpathScene BuildTiles(
        IReadOnlyList<IReadOnlyList<Point2>> rings, bool closeRings, int minimum)
    {
        var polylines = new List<Polyline>(rings.Count);

        foreach (var ring in rings)
        {
            if (ring.Count < minimum)
            {
                continue;
            }

            var points = new float[(ring.Count + (closeRings ? 1 : 0)) * 2];
            for (var i = 0; i < ring.Count; i++)
            {
                points[i * 2] = ToMm(ring[i].X);
                points[(i * 2) + 1] = -ToMm(ring[i].Y);
            }

            if (closeRings)
            {
                points[^2] = points[0];
                points[^1] = points[1];
            }

            polylines.Add(new Polyline(TileStyle, points));
        }

        return ToolpathScene.Build(polylines);
    }

    private static float ToMm(long nm) => (float)(nm / (double)Nm.PerMillimetre);

    public void Dispose()
    {
        foreach (var layer in _layers)
        {
            layer.Dispose();
        }

        _layers.Clear();
    }
}
