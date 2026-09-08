using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using MillBurn.Viewer;
using SkiaSharp;

namespace MillBurn.App.Rendering;

/// <summary>
/// Draws a <see cref="BoardScene"/> onto Avalonia's own Skia canvas — the same lease the toolpath
/// operation uses, and the same reason: no intermediate bitmap, no per-frame copy.
/// </summary>
internal sealed class BoardDrawOperation(
    Rect bounds,
    BoardScene scene,
    ViewTransform view,
    SKColor background,
    SKColor gridColor,
    Action<double, BoardRenderer.DrawResult> onFrameRendered) : ICustomDrawOperation
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

        var result = BoardRenderer.Draw(
            lease.SkCanvas,
            new SKRect((float)bounds.X, (float)bounds.Y, (float)bounds.Right, (float)bounds.Bottom),
            scene,
            view,
            background,
            gridColor);

        var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
            * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        onFrameRendered(elapsedMs, result);
    }

    public void Dispose()
    {
        // The scene owns its SKPaths and outlives this per-frame operation.
    }
}
