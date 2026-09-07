using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// The actual Skia drawing, independent of Avalonia. Shared by the on-screen draw operation and
/// the headless benchmark, so the number the benchmark reports is the cost of the code that
/// really runs on screen.
/// </summary>
public static class ToolpathRenderer
{
    private static readonly float[] GridSteps = [0.1f, 0.5f, 1f, 5f, 10f, 25f, 50f, 100f, 250f];

    /// <summary>Travel first, so cut moves read on top of it.</summary>
    public static readonly SegmentStyle[] DrawOrder =
    [
        SegmentStyle.Travel,
        SegmentStyle.RapidLong,
        SegmentStyle.Pocket,
        SegmentStyle.Isolation,
        SegmentStyle.Laser,
        SegmentStyle.Outline,
        SegmentStyle.Drill,
        SegmentStyle.Fiducial,
    ];

    public static DrawResult Draw(
        SKCanvas canvas,
        SKRect viewport,
        ToolpathScene scene,
        ViewTransform view,
        IReadOnlyDictionary<SegmentStyle, SKColor> palette,
        SKColor background,
        SKColor gridColor,
        RenderOptions? options = null)
    {
        var opt = options ?? RenderOptions.Default;
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(palette);

        canvas.Save();
        canvas.ClipRect(viewport);
        canvas.Clear(background);

        DrawGrid(canvas, viewport, view, gridColor);

        var tier = ToolpathScene.TierForScale(view.Scale);
        var visibleWorld = opt.Cull
            ? VisibleWorldRect(viewport, view)
            : new SKRect(float.MinValue / 4, float.MinValue / 4, float.MaxValue / 4, float.MaxValue / 4);
        var inView = scene.SegmentsInView(tier, visibleWorld);

        // Draw in world space: the canvas matrix does the pan/zoom, so no geometry is rebuilt
        // per frame. Stroke widths are divided by the scale to stay constant on screen.
        canvas.Save();
        canvas.Translate(view.OffsetX, view.OffsetY);
        canvas.Scale(view.Scale, -view.Scale); // Y up, as CNC coordinates are

        // Antialiasing and dashing are both per-segment costs. Above roughly a screenful of
        // detail they buy nothing visible and cost everything, so they are spent only when the
        // frame is cheap enough to afford them.
        // Once tile culling is correct, antialiasing and geometric stroking cost about 4% on a
        // 500k-segment board (measured with --probe), so they stay on and the scene looks right.
        // This remains as a safety valve for pathological files far past the design target.
        var detailAffordable = opt.AllowDetail && inView <= DetailBudgetSegments;
        var hairline = opt.Hairline || !detailAffordable;

        using var paint = new SKPaint
        {
            IsAntialias = opt.Antialias && detailAffordable,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Butt,
            StrokeJoin = opt.Join,
        };

        var drawn = 0;

        foreach (var style in DrawOrder)
        {
            paint.Color = palette.TryGetValue(style, out var c) ? c : SKColors.Gray;
            // Hairline (width 0) takes Skia's dedicated fast line rasteriser instead of
            // expanding every segment into a stroked polygon.
            paint.StrokeWidth = hairline ? 0f : ScreenWidthFor(style) / view.Scale;

            var dashed = detailAffordable && style is SegmentStyle.Travel or SegmentStyle.RapidLong;
            paint.PathEffect?.Dispose();
            paint.PathEffect = dashed
                ? SKPathEffect.CreateDash([3f / view.Scale, 3f / view.Scale], 0)
                : null;

            foreach (var path in scene.VisiblePaths(tier, style, visibleWorld))
            {
                canvas.DrawPath(path, paint);
                drawn++;
            }
        }

        paint.PathEffect?.Dispose();
        paint.PathEffect = null;

        canvas.Restore();
        canvas.Restore();

        return new DrawResult(tier, inView, drawn);
    }

    /// <summary>Knobs the benchmark sweeps to find the fast configuration.</summary>
    public readonly record struct RenderOptions(
        bool Cull,
        bool Antialias,
        bool Hairline,
        SKStrokeJoin Join,
        bool AllowDetail)
    {
        public static RenderOptions Default { get; } =
            new(Cull: true, Antialias: true, Hairline: false, SKStrokeJoin.Round, AllowDetail: true);
    }

    /// <summary>Above this many visible segments, drop antialiasing and dashes.</summary>
    private const int DetailBudgetSegments = 25_000;

    /// <summary>The world-space rectangle currently on screen, used for tile culling.</summary>
    public static SKRect VisibleWorldRect(SKRect viewport, ViewTransform view)
    {
        var left = (viewport.Left - view.OffsetX) / view.Scale;
        var right = (viewport.Right - view.OffsetX) / view.Scale;
        var a = -(viewport.Top - view.OffsetY) / view.Scale;
        var b = -(viewport.Bottom - view.OffsetY) / view.Scale;
        return new SKRect(left, MathF.Min(a, b), right, MathF.Max(a, b));
    }

    private static void DrawGrid(SKCanvas canvas, SKRect viewport, ViewTransform view, SKColor color)
    {
        // Spacing snaps to a 1/5/10/50 mm ladder so it stays readable across the zoom range.
        const float TargetPx = 60f;
        var wanted = TargetPx / view.Scale;
        var step = GridSteps[^1];
        foreach (var candidate in GridSteps)
        {
            if (candidate >= wanted)
            {
                step = candidate;
                break;
            }
        }

        using var paint = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            Color = color,
        };

        var worldLeft = (viewport.Left - view.OffsetX) / view.Scale;
        var worldRight = (viewport.Right - view.OffsetX) / view.Scale;
        var worldTop = -(viewport.Top - view.OffsetY) / view.Scale;
        var worldBottom = -(viewport.Bottom - view.OffsetY) / view.Scale;

        // Guard against a pathological zoom producing millions of grid lines.
        if ((worldRight - worldLeft) / step > 4000 || (worldTop - worldBottom) / step > 4000)
        {
            return;
        }

        for (var x = MathF.Floor(worldLeft / step) * step; x <= worldRight; x += step)
        {
            var sx = (x * view.Scale) + view.OffsetX;
            canvas.DrawLine(sx, viewport.Top, sx, viewport.Bottom, paint);
        }

        for (var y = MathF.Floor(worldBottom / step) * step; y <= worldTop; y += step)
        {
            var sy = (-y * view.Scale) + view.OffsetY;
            canvas.DrawLine(viewport.Left, sy, viewport.Right, sy, paint);
        }
    }

    /// <summary>What one <see cref="Draw"/> call actually did, for the viewport overlay.</summary>
    public readonly record struct DrawResult(int Tier, int SegmentsInView, int DrawCalls);

    private static float ScreenWidthFor(SegmentStyle style) => style switch
    {
        SegmentStyle.Travel => 0.75f,
        SegmentStyle.RapidLong => 1.25f,
        SegmentStyle.Outline => 2.0f,
        SegmentStyle.Drill => 2.0f,
        _ => 1.4f,
    };
}
