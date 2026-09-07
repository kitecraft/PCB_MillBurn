using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using MillBurn.Viewer;
using SkiaSharp;

namespace MillBurn.App.Rendering;

/// <summary>
/// Draws a <see cref="ToolpathScene"/> straight onto Avalonia's own Skia canvas.
///
/// This is the whole reason for choosing Avalonia (Documentation/07-UI-Framework-Decision.md):
/// no intermediate bitmap, no per-frame copy. We lease the same GPU-backed SKCanvas the rest of
/// the UI is drawn with.
/// </summary>
internal sealed class ToolpathDrawOperation(
    Rect bounds,
    ToolpathScene scene,
    ViewTransform view,
    IReadOnlyDictionary<SegmentStyle, SKColor> palette,
    SKColor background,
    SKColor gridColor,
    Action<FrameStats> onFrameRendered) : ICustomDrawOperation
{
    public Rect Bounds => bounds;

    public bool HitTest(Point p) => bounds.Contains(p);

    // Every frame carries a fresh view transform, so operations are never equivalent.
    public bool Equals(ICustomDrawOperation? other) => false;

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature feature)
        {
            return;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        using var lease = feature.Lease();

        var result = ToolpathRenderer.Draw(
            lease.SkCanvas,
            new SKRect((float)bounds.X, (float)bounds.Y, (float)bounds.Right, (float)bounds.Bottom),
            scene,
            view,
            palette,
            background,
            gridColor);

        var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
            * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        onFrameRendered(new FrameStats(elapsedMs, result.Tier, result.SegmentsInView, result.DrawCalls));
    }

    public void Dispose()
    {
        // The scene owns its SKPaths and outlives this per-frame operation.
    }
}
