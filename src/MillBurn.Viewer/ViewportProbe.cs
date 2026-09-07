using System.Diagnostics;
using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// Sweeps render configurations against a fixed scene so the fast path is found by measurement
/// rather than by guesswork. Invoked with <c>MillBurn.App.exe --probe</c>.
/// </summary>
public static class ViewportProbe
{
    public static int Run(int segments = 500_000, int width = 1600, int height = 900)
    {
        Console.WriteLine("PCB_MillBurn render configuration probe");
        Line($"  {segments:N0} segments, {width}x{height}, CPU raster");
        Console.WriteLine();

        var polylines = SyntheticToolpath.Generate(segments);
        using var scene = ToolpathScene.Build(polylines);

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
        var b = scene.Bounds;
        var fitScale = 0.92f * MathF.Min(width / MathF.Max(b.Width, 0.001f), height / MathF.Max(b.Height, 0.001f));

        var views = new (string Name, ViewTransform View)[]
        {
            ("fit", ViewAt(fitScale, b, width, height, 0.5f, 0.5f)),
            ("8x", ViewAt(fitScale * 8, b, width, height, 0.5f, 0.5f)),
            ("40x", ViewAt(fitScale * 40, b, width, height, 0.5f, 0.5f)),
        };

        var configs = new (string Name, ToolpathRenderer.RenderOptions Options)[]
        {
            ("stroke round AA   ", new(true, true, false, SKStrokeJoin.Round, true)),
            ("stroke round noAA ", new(true, false, false, SKStrokeJoin.Round, true)),
            ("stroke bevel noAA ", new(true, false, false, SKStrokeJoin.Bevel, true)),
            ("stroke miter noAA ", new(true, false, false, SKStrokeJoin.Miter, true)),
            ("hairline AA       ", new(true, true, true, SKStrokeJoin.Bevel, true)),
            ("hairline noAA     ", new(true, false, true, SKStrokeJoin.Bevel, true)),
            ("hairline noAA nocull", new(false, false, true, SKStrokeJoin.Bevel, true)),
        };

        Console.WriteLine("  config                    " + string.Join("", views.Select(v => $"{v.Name,12}")));
        Console.WriteLine("  " + new string('-', 26 + (12 * views.Length)));

        foreach (var (name, opt) in configs)
        {
            var cells = new List<string>();
            foreach (var (_, view) in views)
            {
                // Warm up, then take the median of a short run.
                for (var i = 0; i < 3; i++)
                {
                    ToolpathRenderer.Draw(surface.Canvas, viewport, scene, view, palette,
                        SKColors.Black, SKColors.DimGray, opt);
                }

                surface.Canvas.Flush();

                var samples = new double[9];
                for (var i = 0; i < samples.Length; i++)
                {
                    var t0 = Stopwatch.GetTimestamp();
                    ToolpathRenderer.Draw(surface.Canvas, viewport, scene, view, palette,
                        SKColors.Black, SKColors.DimGray, opt);
                    surface.Canvas.Flush();
                    samples[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                }

                Array.Sort(samples);
                cells.Add($"{samples[samples.Length / 2],10:F1}ms");
            }

            Console.WriteLine($"  {name,-26}{string.Join("", cells)}");
        }

        Console.WriteLine();
        Console.WriteLine("  Segments in view and draw calls per view:");
        foreach (var (name, view) in views)
        {
            var r = ToolpathRenderer.Draw(surface.Canvas, viewport, scene, view, palette,
                SKColors.Black, SKColors.DimGray, ToolpathRenderer.RenderOptions.Default);
            Line($"    {name,-6} tier {r.Tier}  {r.SegmentsInView,10:N0} segments  {r.DrawCalls,6} draw calls");
        }

        return 0;
    }

    private static ViewTransform ViewAt(float scale, SKRect bounds, int width, int height, float fx, float fy)
    {
        var focusX = bounds.Left + (bounds.Width * fx);
        var focusY = bounds.Top + (bounds.Height * fy);
        return new ViewTransform(scale, (width / 2f) - (focusX * scale), (height / 2f) + (focusY * scale));
    }

    private static void Line(FormattableString text) =>
        Console.WriteLine(FormattableString.Invariant(text));
}
