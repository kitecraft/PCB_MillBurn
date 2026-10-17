using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// Draws a <see cref="BoardScene"/>. UI-free, so the app, a headless PNG export and a benchmark all
/// share one renderer and there is no second implementation to drift.
/// </summary>
public static class BoardRenderer
{
    public readonly record struct DrawResult(int LayersDrawn, int VerticesDrawn);

    public static SKRect VisibleWorldRect(SKRect viewport, ViewTransform view)
    {
        var left = (viewport.Left - view.OffsetX) / view.Scale;
        var top = (viewport.Top - view.OffsetY) / view.Scale;
        var right = (viewport.Right - view.OffsetX) / view.Scale;
        var bottom = (viewport.Bottom - view.OffsetY) / view.Scale;
        return new SKRect(left, top, right, bottom);
    }

    public static DrawResult Draw(
        SKCanvas canvas,
        SKRect viewport,
        BoardScene scene,
        ViewTransform view,
        SKColor background,
        SKColor gridColor,
        bool ruler = false,
        RulerReadout? readout = null,
        RulerMeasure? measure = null,
        BoardMarker? marker = null,
        TabOverlay? tabs = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(scene);

        canvas.Save();
        canvas.ClipRect(viewport);
        canvas.Clear(background);

        // Work zero in this view's world coordinates: the frame's lower-left corner, which the
        // scene was built around. World y is the board's negated, so the corner's y is the
        // rectangle's *bottom*.
        // Read once. `BoardScene.Reframe` assigns this from the UI thread while a slider is being
        // dragged, and two reads of it on the render thread could mix an old x with a new y — one
        // frame with its ruler origin in a place neither frame had. The same care `Rebuild` takes
        // swapping the scene before disposing the old one.
        var extent = scene.Bounds;
        var origin = new SKPoint(extent.Left, extent.Bottom);
        var step = GridLadder.StepFor(view.Scale);

        // One frame for the ruler and the marker both, so a mark cannot land somewhere the scale
        // beside it disagrees with.
        var frame = new RulerFrame(
            view.Scale,
            (origin.X * view.Scale) + view.OffsetX,
            (origin.Y * view.Scale) + view.OffsetY);

        DrawGrid(canvas, viewport, view, gridColor, origin, step);

        canvas.Save();
        canvas.Translate(view.OffsetX, view.OffsetY);
        canvas.Scale(view.Scale);

        var layersDrawn = 0;
        var verticesDrawn = 0;

        // The tiled layers' frame, worked out once: which level of detail this zoom draws, what
        // part of the world is on screen, and whether what is left after culling fits the detail
        // budget. The budget is ToolpathRenderer's own, because these layers are drawn by its
        // rules (6.71): anti-aliasing, geometric stroking and dashes are per-vertex costs, spent
        // while the frame can afford them and dropped for the hairline rasteriser when it
        // cannot.
        //
        // Each tiled layer's in-view count is kept as it is found: the budget needs the sum
        // before the first tiled layer draws, and the per-layer number is also what that layer
        // reports as drawn — walking the tiles twice for it would spend bookkeeping on the path
        // this exists to cheapen. Null on an ordinary board, which has no tiled layer to count.
        var visibleWorld = VisibleWorldRect(viewport, view);
        int[]? tiledInView = null;
        var tiledTotal = 0;

        var tier = TiledTier(view.Scale, candidate =>
        {
            tiledTotal = 0;
            for (var i = 0; i < scene.Layers.Count; i++)
            {
                var layer = scene.Layers[i];
                if (layer.Visible && layer.Tiles is { } t)
                {
                    tiledInView ??= new int[scene.Layers.Count];
                    tiledInView[i] = t.SegmentsInView(candidate, visibleWorld);
                    tiledTotal += tiledInView[i];
                }
            }

            return tiledTotal;
        });

        var detailAffordable = tiledTotal <= ToolpathRenderer.DetailBudgetSegments;

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

        for (var i = 0; i < scene.Layers.Count; i++)
        {
            var layer = scene.Layers[i];
            if (!layer.Visible)
            {
                continue;
            }

            var style = layer.Style;
            var alpha = (byte)Math.Clamp(style.Fill.Alpha * style.Opacity, 0, 255);

            if (style.Outlined)
            {
                // An outline layer marks where the board *ends*, and a toolpath layer marks where
                // the tool went; filling either would paint a slab over what it is meant to
                // annotate.
                stroke.Color = style.Fill.WithAlpha(alpha);

                if (layer.Tiles is not null)
                {
                    stroke.IsAntialias = detailAffordable;

                    // Width 0 is Skia's dedicated fast line rasteriser, as in ToolpathRenderer.
                    stroke.StrokeWidth = detailAffordable ? style.StrokePixels / view.Scale : 0f;

                    // A dash restarts its phase at every contour, and a tile boundary starts a
                    // contour — the same behaviour the toolpath view has always had for its
                    // dashed travel styles. No stroked board layer in the corpus is both dashed
                    // and over the budget; if one arrives, its dashes will shift at the seams
                    // before anyone decides dashing a path that size is worth the cost.
                    stroke.PathEffect?.Dispose();
                    stroke.PathEffect = detailAffordable && style.DashPixels > 0
                        ? SKPathEffect.CreateDash(
                            [style.DashPixels / view.Scale, style.DashPixels / view.Scale], 0)
                        : null;

                    foreach (var path in layer.TilePaths(tier, visibleWorld))
                    {
                        canvas.DrawPath(path, stroke);
                    }

                    layersDrawn++;

                    // What was actually submitted, not what the layer holds: the whole point of
                    // the tiles is that those differ, and the status bar should say so. (An
                    // untiled layer's whole path is submitted whatever the zoom, so its full
                    // count below remains the honest number for it.)
                    verticesDrawn += tiledInView![i];
                    continue;
                }

                stroke.IsAntialias = true;

                // Constant on screen regardless of zoom: the canvas is scaled, so undo it.
                stroke.StrokeWidth = style.StrokePixels / view.Scale;

                stroke.PathEffect?.Dispose();
                stroke.PathEffect = style.DashPixels > 0
                    ? SKPathEffect.CreateDash(
                        [style.DashPixels / view.Scale, style.DashPixels / view.Scale], 0)
                    : null;

                canvas.DrawPath(layer.Path, stroke);
            }
            else
            {
                fill.Color = style.Fill.WithAlpha(alpha);
                canvas.DrawPath(layer.Path, fill);
            }

            layersDrawn++;
            verticesDrawn += layer.VertexCount;
        }

        stroke.PathEffect?.Dispose();
        stroke.PathEffect = null;

        canvas.Restore();

        // **Over the board, which is what an overlay means.** It was under it, and that reads fine
        // on a whole board — the strips sit in the margin and nothing reaches them — but a shot at
        // bridge zoom showed no ruler at all, because at that zoom copper fills the viewport and the
        // ruler is exactly what you want. The mockup chosen on 2026-09-30 was the translucent strip
        // floating over the board for this reason; the cost is the outermost millimetre or so, and
        // only where the board is panned right to the edge.
        //
        // After the restore, so the readout and the measuring line can work in screen pixels: both
        // are window furniture at a fixed size, not artwork that scales with the zoom.
        if (ruler)
        {
            Ruler.Draw(canvas, viewport, frame, step, background, readout, measure);
        }

        // After the ruler for the same reason it is, and before the marker: a handle is something
        // to take hold of, a mark is something to look at, and when the two coincide the answer to
        // "where is the fault" goes on top.
        if (tabs is not null)
        {
            TabHandles.Draw(canvas, viewport, frame, tabs);
        }

        // After the ruler, so the crosshairs and the two strip pointers land on top of the strips
        // they are pointing at rather than under them.
        if (marker is { } place)
        {
            Marker.Draw(canvas, viewport, frame, place, ruler);
        }

        canvas.Restore();
        return new DrawResult(layersDrawn, verticesDrawn);
    }

    /// <summary>
    /// The tier a frame draws its tiled stroked layers at: the half-pixel tier, relaxed to at
    /// most one pixel of simplification error when that tier busts the detail budget.
    ///
    /// **The order of the trade matters, and it is the reverse of doing nothing.** Half a pixel
    /// of simplification is invisible and one pixel is nearly so; the hairline fallback — thin,
    /// unsoftened strokes — is neither. Without this, the 66-up panel's fit zoom sat exactly on
    /// the half-pixel boundary of the coarsest tier, and a few pixels of window width decided
    /// whether the hero drew anti-aliased or degraded (6.71). The budget still has the last
    /// word: a scene too big for the one-pixel tier goes hairline, at that coarser tier.
    /// </summary>
    /// <param name="scale">Pixels per millimetre, as <see cref="ViewTransform.Scale"/>.</param>
    /// <param name="tiledSegmentsInView">The culled segment count of every visible tiled layer
    /// at a candidate tier. Called once per tier tried; the last call made is for the tier
    /// returned, so a caller caching per-layer counts inside it holds the right ones.</param>
    public static int TiledTier(float scale, Func<int, int> tiledSegmentsInView)
    {
        ArgumentNullException.ThrowIfNull(tiledSegmentsInView);

        var tier = ToolpathScene.TierForScale(scale);
        while (tiledSegmentsInView(tier) > ToolpathRenderer.DetailBudgetSegments
            && tier + 1 < ToolpathScene.TierCount
            && ToolpathScene.SimplificationErrorPx(tier + 1, scale) <= 1f)
        {
            tier++;
        }

        return tier;
    }

    /// <summary>
    /// A millimetre grid whose spacing steps up as you zoom out, so it stays readable and never
    /// degenerates into a solid wash of lines at low zoom.
    /// </summary>
    private static void DrawGrid(
        SKCanvas canvas, SKRect viewport, ViewTransform view, SKColor color, SKPoint origin,
        float step)
    {
        // `step` is handed in rather than worked out again. This and the ruler asked `GridLadder`
        // separately and agreed — but two call sites agreeing today is exactly the state the two
        // copies of the ladder were in before they drifted, and a ruler ticking against a grid it
        // did not see drawn is the disagreement this whole feature is built to rule out.
        if (color.Alpha == 0)
        {
            return;
        }

        var world = VisibleWorldRect(viewport, view);

        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = 1f,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
        };

        // **From work zero, not from the artwork's own origin.** The grid used to step from world
        // zero, which is wherever the EDA tool happened to put the board, so its lines fell on no
        // number anybody cares about — and the ruler's ticks would then have sat between them.
        // Stepping from the frame's corner puts a line on every round millimetre the ruler labels.
        var startX = origin.X + (MathF.Floor((world.Left - origin.X) / step) * step);
        for (var x = startX; x <= world.Right; x += step)
        {
            var sx = (x * view.Scale) + view.OffsetX;
            canvas.DrawLine(sx, viewport.Top, sx, viewport.Bottom, paint);
        }

        var startY = origin.Y + (MathF.Floor((world.Top - origin.Y) / step) * step);
        for (var y = startY; y <= world.Bottom; y += step)
        {
            var sy = (y * view.Scale) + view.OffsetY;
            canvas.DrawLine(viewport.Left, sy, viewport.Right, sy, paint);
        }
    }
}
