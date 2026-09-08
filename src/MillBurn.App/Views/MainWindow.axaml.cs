using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using MillBurn.App.ViewModels;
using MillBurn.Pipeline;

namespace MillBurn.App.Views;

public partial class MainWindow : Window
{
    private readonly bool _fpsTest;

    public MainWindow()
    {
        InitializeComponent();

        Viewport.StatsUpdated += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ReportFrame(Viewport.LastFrameMs, Viewport.LastLayersDrawn, Viewport.LastVerticesDrawn);
            }
        };

        ToolpathViewport.StatsUpdated += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ReportFrame(ToolpathViewport.AverageFrameMs, ToolpathViewport.LastStats);
            }
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.RedrawRequested += (_, _) => Viewport.InvalidateVisual();
            }
        };

        SetUpDragAndDrop();

        var args = Environment.GetCommandLineArgs();
        _fpsTest = args.Contains("--fpstest", StringComparer.OrdinalIgnoreCase);

        Opened += (_, _) => OnOpened(args);
    }

    private void OnOpened(string[] args)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (_fpsTest)
        {
            Viewport.IsVisible = false;
            ToolpathViewport.IsVisible = true;
            vm.LoadSyntheticToolpath();
            StartFpsTest();
            return;
        }

        // A folder on the command line loads straight away, which is what makes the app usable
        // from a shell and scriptable in a smoke test.
        var folder = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-') && Directory.Exists(a));
        if (folder is not null)
        {
            vm.LoadFolder(folder);
        }

        var shot = ShotPath(args);
        if (shot is not null)
        {
            CaptureAndExit(shot);
        }
    }

    private static string? ShotPath(string[] args)
    {
        var index = Array.FindIndex(args, a => a.Equals("--shot", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>
    /// Renders the whole window to a PNG and exits.
    ///
    /// The CLI's own board render already covers the scene and the renderer, but not this: the
    /// XAML, the bindings, the layer panel, and the custom draw operation running inside a real
    /// Avalonia visual tree. A binding that silently resolves to nothing produces a window that
    /// builds, launches, and shows an empty viewport — which no unit test and no headless render
    /// will ever notice.
    /// </summary>
    private void CaptureAndExit(string path)
    {
        // One layout pass has to complete before there is anything to capture.
        DispatcherTimer.RunOnce(
            () =>
            {
                try
                {
                    var size = new PixelSize((int)Math.Max(ClientSize.Width, 1), (int)Math.Max(ClientSize.Height, 1));
                    using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
                    bitmap.Render(this);

                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                    using var file = File.Create(path);
                    bitmap.Save(file, new PngBitmapEncoderOptions());

                    Console.WriteLine($"shot: {path} ({size.Width}x{size.Height})");
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException)
                {
                    Console.Error.WriteLine($"shot failed: {ex.Message}");
                }

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life)
                {
                    life.Shutdown();
                }
            },
            TimeSpan.FromMilliseconds(600));
    }

    /// <summary>
    /// Dropping a folder is the primary way in, so it accepts what people actually drop: a folder,
    /// or any file inside one. Someone selecting all their Gerbers and dragging them across should
    /// not be told to try again with the folder.
    /// </summary>
    private void SetUpDragAndDrop()
    {
        DragDrop.SetAllowDrop(this, true);

        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        });

        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            e.Handled = true;

            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            var folder = FolderFrom(e.DataTransfer);
            if (folder is null)
            {
                vm.StatusMessage = "Drop a folder of Gerber files, or a file from inside one.";
                return;
            }

            vm.LoadFolder(folder);
        });
    }

    private static string? FolderFrom(IDataTransfer data)
    {
        foreach (var item in data.TryGetFiles() ?? [])
        {
            var path = item.TryGetLocalPath();
            if (path is null)
            {
                continue;
            }

            if (Directory.Exists(path))
            {
                return path;
            }

            if (File.Exists(path) && BoardLoader.IsBoardFile(path))
            {
                return Path.GetDirectoryName(path);
            }
        }

        return null;
    }

    private async void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a Gerber export folder",
            AllowMultiple = false,
        });

        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path is not null)
        {
            vm.LoadFolder(path);
        }
    }

    private void OnFitClicked(object? sender, RoutedEventArgs e)
    {
        if (ToolpathViewport.IsVisible)
        {
            ToolpathViewport.FitToContent();
            return;
        }

        Viewport.FitToContent();
    }

    /// <summary>
    /// Proves the token system: one toggle restyles the whole UI *including* the Skia-rendered
    /// board, because the viewport resolves its colours from the same ThemeDictionaries.
    /// </summary>
    private void OnToggleThemeClicked(object? sender, RoutedEventArgs e)
    {
        RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        Viewport.InvalidateVisual();
        ToolpathViewport.InvalidateVisual();
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
        var startFrames = ToolpathViewport.FramesRendered;
        var baseScale = 0.0f;

        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1),
        };

        timer.Tick += (_, _) =>
        {
            if (baseScale <= 0)
            {
                ToolpathViewport.FitToContent();
                baseScale = ToolpathViewport.Scale;
                return;
            }

            var t = sw.Elapsed.TotalSeconds / DurationSeconds;
            if (t >= 1.0)
            {
                timer.Stop();

                var frames = ToolpathViewport.FramesRendered - startFrames;
                var seconds = sw.Elapsed.TotalSeconds;
                var fps = frames / seconds;

                Console.WriteLine(FormattableString.Invariant(
                    $"fpstest: {frames} frames in {seconds:F2}s = {fps:F1} fps (vsync-capped)"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"         last frame: tier {ToolpathViewport.LastStats.Tier}, " +
                    $"{ToolpathViewport.LastStats.SegmentsInView:N0} segments in view, " +
                    $"{ToolpathViewport.LastStats.DrawCalls} draw calls, " +
                    $"{ToolpathViewport.AverageFrameMs:F2} ms avg record time"));
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
            ToolpathViewport.SetScale(baseScale * (1f + (39f * (float)phase)));
        };

        timer.Start();
    }
}
