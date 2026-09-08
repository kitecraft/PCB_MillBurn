using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// Draws a <see cref="BoardScene"/>. UI-free, so the app, a headless PNG export and a benchmark all
/// share one renderer and there is no second implementation to drift.
/// </summary>
public static class BoardRenderer
{
    private static readonly float[] GridSteps = [0.1f, 0.5f, 1f, 5f, 10f, 25f, 50f, 100f, 250f];

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
        SKColor gridColor)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(scene);

        canvas.Save();
        canvas.ClipRect(viewport);
        canvas.Clear(background);

        DrawGrid(canvas, viewport, view, gridColor);

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
                // An outline layer marks where the board *ends*; filling it would paint a slab over
                // the copper it is meant to frame.
                stroke.Color = style.Fill.WithAlpha(alpha);

                // Constant on screen regardless of zoom: the canvas is scaled, so undo it.
                stroke.StrokeWidth = 1.5f / view.Scale;
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

        canvas.Restore();
        return new DrawResult(layersDrawn, verticesDrawn);
    }

    /// <summary>
    /// A millimetre grid whose spacing steps up as you zoom out, so it stays readable and never
    /// degenerates into a solid wash of lines at low zoom.
    /// </summary>
    private static void DrawGrid(SKCanvas canvas, SKRect viewport, ViewTransform view, SKColor color)
    {
        if (color.Alpha == 0)
        {
            return;
        }

        var step = GridSteps[^1];
        foreach (var candidate in GridSteps)
        {
            if (candidate * view.Scale >= 12f)
            {
                step = candidate;
                break;
            }
        }

        var world = VisibleWorldRect(viewport, view);

        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = 1f,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
        };

        var startX = MathF.Floor(world.Left / step) * step;
        for (var x = startX; x <= world.Right; x += step)
        {
            var sx = (x * view.Scale) + view.OffsetX;
            canvas.DrawLine(sx, viewport.Top, sx, viewport.Bottom, paint);
        }

        var startY = MathF.Floor(world.Top / step) * step;
        for (var y = startY; y <= world.Bottom; y += step)
        {
            var sy = (y * view.Scale) + view.OffsetY;
            canvas.DrawLine(viewport.Left, sy, viewport.Right, sy, paint);
        }
    }
}
