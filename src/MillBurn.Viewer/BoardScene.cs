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

    public void Dispose() => Path.Dispose();
}

/// <summary>
/// The board as filled area, in millimetres, ready for Skia.
///
/// Deliberately simpler than <see cref="ToolpathScene"/>: no level-of-detail, no spatial tiling.
/// Those exist because a toolpath can run to hundreds of thousands of segments; a board's copper
/// does not. The largest layer in the corpus is 12,310 vertices and a real two-sided board is
/// under 20,000 in total, which Skia fills without noticing.
///
/// This is a measurement, not an assumption — <c>--boardbench</c> reports the real frame cost, and
/// the tiling machinery is one class away if a panelised board ever needs it. Phase 0's lesson was
/// that guessing at this produced a 48x48 grid that made things 140x slower, so the rule now is to
/// build the simple thing and let the number decide.
///
/// **Fill rule is winding, not even-odd.** Clipper2 emits outer rings counter-clockwise and holes
/// clockwise, so winding respects them. Even-odd would too here, but only by luck: it treats any
/// overlap as a hole, so two pads that touch would punch a void where they cross.
/// </summary>
public sealed class BoardScene : IDisposable
{
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
