using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using MillBurn.App.Controls;
using MillBurn.App.ViewModels;

namespace MillBurn.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Viewport.StatsUpdated += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ReportFrame(Viewport.AverageFrameMs, Viewport.LastStats);
            }
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                GcodeEditor.Text = vm.GcodePreview;
            }
        };

        if (Environment.GetCommandLineArgs()
            .Contains("--fpstest", StringComparer.OrdinalIgnoreCase))
        {
            Opened += (_, _) => StartFpsTest();
        }
    }

    /// <summary>
    /// Drives a continuous zoom sweep through the real Avalonia render loop and reports how many
    /// frames actually reached the screen. This is the honest form of the Phase 0 acceptance test:
    /// the offscreen benchmark measures Skia on the CPU, this measures the GPU path users get.
    /// Capped by vsync, so ~60 fps on a 60 Hz display is a pass.
    /// </summary>
    private void StartFpsTest()
    {
        const double DurationSeconds = 8.0;

        var sw = Stopwatch.StartNew();
        var startFrames = Viewport.FramesRendered;
        var baseScale = 0.0f;

        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1),
        };

        timer.Tick += (_, _) =>
        {
            if (baseScale <= 0)
            {
                Viewport.FitToContent();
                baseScale = Viewport.Scale;
                return;
            }

            var t = sw.Elapsed.TotalSeconds / DurationSeconds;
            if (t >= 1.0)
            {
                timer.Stop();

                var frames = Viewport.FramesRendered - startFrames;
                var seconds = sw.Elapsed.TotalSeconds;
                var fps = frames / seconds;

                Console.WriteLine(FormattableString.Invariant(
                    $"fpstest: {frames} frames in {seconds:F2}s = {fps:F1} fps (vsync-capped)"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"         last frame: tier {Viewport.LastStats.Tier}, " +
                    $"{Viewport.LastStats.SegmentsInView:N0} segments in view, " +
                    $"{Viewport.LastStats.DrawCalls} draw calls, " +
                    $"{Viewport.AverageFrameMs:F2} ms avg record time"));
                Console.WriteLine(fps >= 55
                    ? "         PASS  sustained ~60 fps through the real render loop."
                    : "         FAIL  did not sustain 60 fps.");

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life)
                {
                    life.Shutdown();
                }

                return;
            }

            // Sweep fit -> 40x -> fit so every LOD tier is exercised, in both directions.
            var phase = t < 0.5 ? t * 2 : (1 - t) * 2;
            Viewport.SetScale(baseScale * (1f + (39f * (float)phase)));
        };

        timer.Start();
    }

    private void OnFitClicked(object? sender, RoutedEventArgs e) => Viewport.FitToContent();

    /// <summary>
    /// Proves the token system: one toggle restyles the whole UI *including* the Skia-rendered
    /// toolpaths, because the viewport resolves its colours from the same ThemeDictionaries.
    /// </summary>
    private void OnToggleThemeClicked(object? sender, RoutedEventArgs e)
    {
        RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        Viewport.InvalidateVisual();
    }
}
