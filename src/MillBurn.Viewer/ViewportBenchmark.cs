using System.Diagnostics;

using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// Headless measurement of the Phase 0 acceptance criterion: 60 fps pan/zoom on a 500k-segment
/// scene (Documentation/06-Roadmap-and-Risks.md).
///
/// Runs the same <see cref="ToolpathRenderer"/> the window uses, against an offscreen surface, so
/// the reported number is the real cost of the real draw code. Invoked with
/// <c>MillBurn.App.exe --bench</c>.
/// </summary>
public static class ViewportBenchmark
{
    public static int Run(int segments = 500_000, int frames = 240, int width = 1600, int height = 900)
    {
        Console.WriteLine("PCB_MillBurn viewport benchmark");
        Line($"  surface     {width}x{height}");
        Line($"  target      {segments:N0} segments");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        var polylines = SyntheticToolpath.Generate(segments);
        var generateMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        using var scene = ToolpathScene.Build(polylines);
        var buildMs = sw.Elapsed.TotalMilliseconds;

        Line(
            $"  generated   {scene.SourceSegmentCount:N0} segments in {generateMs:F0} ms");
        Line(
            $"  LOD build   4 tiers in {buildMs:F0} ms");
        for (var t = 0; t < 4; t++)
        {
            Line(
                $"    tier {t}    {scene.SegmentsAtTier(t),10:N0} segments");
        }

        Console.WriteLine();

        var palette = new Dictionary<SegmentStyle, SKColor>
        {
            [SegmentStyle.Isolation] = SKColors.DodgerBlue,
            [SegmentStyle.Pocket] = SKColors.Teal,
            [SegmentStyle.Drill] = SKColors.IndianRed,
            [SegmentStyle.Outline] = SKColors.MediumPurple,
            [SegmentStyle.Laser] = SKColors.OrangeRed,
            [SegmentStyle.Fiducial] = SKColors.SkyBlue,
            [SegmentStyle.Travel] = SKColors.Gray,
            [SegmentStyle.RapidLong] = SKColors.Orange,
        };

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            Console.Error.WriteLine("Could not create an offscreen Skia surface.");
            return 1;
        }

        var viewport = SKRect.Create(width, height);

        // Fit the board, then sweep zoom and pan across the frames so the benchmark exercises
        // every LOD tier rather than sitting at one convenient scale.
        var b = scene.Bounds;
        var fitScale = 0.92f * MathF.Min(width / MathF.Max(b.Width, 0.001f), height / MathF.Max(b.Height, 0.001f));

        var times = new double[frames];

        // Warm up: first frames pay for lazy Skia allocation and JIT.
        for (var i = 0; i < 20; i++)
        {
            ToolpathRenderer.Draw(surface.Canvas, viewport, scene,
                ViewFor(fitScale, i / 20f, b, width, height), palette, SKColors.Black, SKColors.DimGray);
        }

        surface.Canvas.Flush();

        var peakInView = 0;
        var peakDrawCalls = 0;

        for (var i = 0; i < frames; i++)
        {
            var view = ViewFor(fitScale, i / (float)frames, b, width, height);

            var t0 = Stopwatch.GetTimestamp();
            var r = ToolpathRenderer.Draw(surface.Canvas, viewport, scene, view, palette, SKColors.Black, SKColors.DimGray);
            surface.Canvas.Flush();
            times[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;

            peakInView = Math.Max(peakInView, r.SegmentsInView);
            peakDrawCalls = Math.Max(peakDrawCalls, r.DrawCalls);
        }

        Array.Sort(times);
        var mean = times.Average();
        var p50 = times[frames / 2];
        var p95 = times[(int)(frames * 0.95)];
        var worst = times[^1];

        Line($"  frames      {frames} (zoom sweep from fit to 40x)");
        Line($"  peak visible{peakInView,10:N0} segments, {peakDrawCalls} draw calls");
        Line($"  mean        {mean,7:F2} ms   ({1000.0 / mean,6:F0} fps)");
        Line($"  p50         {p50,7:F2} ms   ({1000.0 / p50,6:F0} fps)");
        Line($"  p95         {p95,7:F2} ms   ({1000.0 / p95,6:F0} fps)");
        Line($"  worst       {worst,7:F2} ms   ({1000.0 / worst,6:F0} fps)");
        Console.WriteLine();

        var pass = p95 <= 16.67;
        Console.WriteLine(pass
            ? "  PASS  p95 within the 16.67 ms budget for 60 fps."
            : "  FAIL  p95 exceeds the 16.67 ms budget for 60 fps.");
        Console.WriteLine("  (CPU raster. On screen this runs on the GPU, which is faster.)");

        return pass ? 0 : 1;
    }

    /// <summary>Writes an interpolated line using invariant formatting.</summary>
    private static void Line(FormattableString text) =>
        Console.WriteLine(FormattableString.Invariant(text));

    private static ViewTransform ViewFor(float fitScale, float t, SKRect bounds, int width, int height)
    {
        // Zoom from the fitted view up to 40x, panning toward the board's top-right as we go,
        // which is the motion a user makes when inspecting isolation detail.
        var scale = fitScale * (1f + (39f * t));
        var focusX = bounds.Left + (bounds.Width * (0.5f + (0.35f * t)));
        var focusY = bounds.Top + (bounds.Height * (0.5f + (0.30f * t)));

        return new ViewTransform(
            scale,
            (width / 2f) - (focusX * scale),
            (height / 2f) + (focusY * scale));
    }
}
