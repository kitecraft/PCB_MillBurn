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
        RulerMeasure? measure = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(scene);

        canvas.Save();
        canvas.ClipRect(viewport);
        canvas.Clear(background);

        // Work zero in this view's world coordinates: the frame's lower-left corner, which the
        // scene was built around. World y is the board's negated, so the corner's y is the
        // rectangle's *bottom*.
        var origin = new SKPoint(scene.Bounds.Left, scene.Bounds.Bottom);
        var step = GridLadder.StepFor(view.Scale);

        DrawGrid(canvas, viewport, view, gridColor, origin, step);

        canvas.Save();
        canvas.Translate(view.OffsetX, view.OffsetY);
        canvas.Scale(view.Scale);

        var layersDrawn = 0;
        var verticesDrawn = 0;

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

        foreach (var layer in scene.Layers)
        {
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
            Ruler.Draw(
                canvas,
                viewport,
                new RulerFrame(
                    view.Scale,
                    (origin.X * view.Scale) + view.OffsetX,
                    (origin.Y * view.Scale) + view.OffsetY),
                step,
                background,
                readout,
                measure);
        }

        canvas.Restore();
        return new DrawResult(layersDrawn, verticesDrawn);
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
