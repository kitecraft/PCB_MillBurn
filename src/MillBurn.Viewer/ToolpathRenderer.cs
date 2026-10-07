using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// The actual Skia drawing, independent of Avalonia. Shared by the on-screen draw operation and
/// the headless benchmark, so the number the benchmark reports is the cost of the code that
/// really runs on screen.
/// </summary>
public static class ToolpathRenderer
{
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
        RenderOptions? options = null,
        bool ruler = false)
    {
        var opt = options ?? RenderOptions.Default;
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(palette);

        canvas.Save();
        canvas.ClipRect(viewport);
        canvas.Clear(background);

        // **Work zero is this world's own zero.** A toolpath is parsed out of emitted G-code, and
        // every coordinate in an emitted program is already measured from work zero — so unlike the
        // board view, whose world is the artwork's arbitrary origin, there is nothing to shift. (The
        // only scene this view is handed today is the synthetic benchmark, which has no board at
        // all; its own zero is the only honest answer there too.)
        var origin = SKPoint.Empty;

        var step = GridLadder.StepFor(view.Scale, 60f);

        DrawGrid(canvas, viewport, view, gridColor, origin, step);

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

        // Over the toolpaths, as the board view draws it over the copper and for the same reason:
        // zoomed in far enough, the thing being measured fills the viewport.
        if (ruler)
        {
            Ruler.Draw(
                canvas,
                viewport,
                new RulerFrame(view.Scale, view.OffsetX, view.OffsetY),
                step,
                background);
        }

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

    private static void DrawGrid(
        SKCanvas canvas, SKRect viewport, ViewTransform view, SKColor color, SKPoint origin,
        float step)
    {
        // `step` is handed in rather than worked out again, so the grid and the ruler over it
        // cannot be drawn to different numbers.
        //
        // Its caller asks for sixty pixels rather than the board view's twelve, which is a
        // difference the two have always had and which nothing said: the same nine spacings were
        // declared in both files and read with different thresholds, so the two viewports drew
        // grids of different densities at the same zoom. The ladder is shared now and the threshold
        // is still each view's own — a toolpath is read for its shape and a dense grid competes
        // with it.
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

        // From work zero, as the board view does and for the same reason: the ruler's ticks have to
        // land on these lines.
        for (var x = origin.X + (MathF.Floor((worldLeft - origin.X) / step) * step); x <= worldRight; x += step)
        {
            var sx = (x * view.Scale) + view.OffsetX;
            canvas.DrawLine(sx, viewport.Top, sx, viewport.Bottom, paint);
        }

        for (var y = origin.Y + (MathF.Floor((worldBottom - origin.Y) / step) * step); y <= worldTop; y += step)
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
