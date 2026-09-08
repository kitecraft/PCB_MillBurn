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
        SetUpCloseGuard();

        var args = Environment.GetCommandLineArgs();
        _fpsTest = args.Contains("--fpstest", StringComparer.OrdinalIgnoreCase);

        Opened += (_, _) => OnOpened(args);
    }

    /// <summary>
    /// Closing with unsaved work asks first. The close is cancelled, the question asked, and the
    /// window closed again only once it is answered — the dialog cannot block a close synchronously.
    /// </summary>
    private void SetUpCloseGuard()
    {
        Closing += async (_, e) =>
        {
            if (_closeConfirmed || DataContext is not MainViewModel vm || !vm.Project.IsDirty)
            {
                return;
            }

            e.Cancel = true;

            var answer = await ConfirmWindow.AskAsync(
                this,
                "Unsaved changes",
                $"{vm.Project.DisplayName} has unsaved changes. Save before closing?",
                "Save",
                "Discard");

            if (answer == ConfirmResult.Cancel || (answer == ConfirmResult.Save && !await SaveAsync()))
            {
                return;
            }

            _closeConfirmed = true;
            Close();
        };
    }

    private bool _closeConfirmed;

    /// <summary>A dialog to capture instead of the main window, for checking one headlessly.</summary>
    private Window? _captureInstead;

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

        // A project or a folder on the command line opens straight away, which is what makes the
        // app usable from a shell and scriptable in a smoke test.
        var target = args.Skip(1).FirstOrDefault(a =>
            !a.StartsWith('-') && (Directory.Exists(a) || File.Exists(a)));

        if (target is not null)
        {
            if (target.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                vm.OpenProject(target);
            }
            else if (Directory.Exists(target))
            {
                vm.LoadFolder(target);
            }
        }

        // --theme lets a screenshot prove the dark variant actually flips, which is the only
        // way this class of bug gets caught: it produces a half-styled window, never an error.
        var theme = Argument(args, "--theme");
        if (theme is not null)
        {
            RequestedThemeVariant = theme.Equals("dark", StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
        }

        // Opens the tool editor for a screenshot, so the dialog is checkable headlessly like
        // everything else. Non-modal on purpose: a modal one would block the capture.
        if (args.Contains("--tools", StringComparer.OrdinalIgnoreCase))
        {
            var editor = new ToolLibraryWindow(vm.Library) { RequestedThemeVariant = ActualThemeVariant };
            editor.Show(this);
            _captureInstead = editor;
        }

        if (args.Contains("--mill", StringComparer.OrdinalIgnoreCase))
        {
            vm.Mill();
        }

        var shot = ShotPath(args);
        if (shot is not null)
        {
            CaptureAndExit(shot);
        }
    }

    private static string? ShotPath(string[] args) => Argument(args, "--shot");

    private static string? Argument(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
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
                    var target = _captureInstead ?? (Visual)this;
                    var client = _captureInstead?.ClientSize ?? ClientSize;
                    var size = new PixelSize((int)Math.Max(client.Width, 1), (int)Math.Max(client.Height, 1));
                    using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
                    bitmap.Render(target);

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

        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.Handled = true;

            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            var dropped = PathFrom(e.DataTransfer);
            if (dropped is null)
            {
                vm.StatusMessage = "Drop a folder of Gerber files, a file from inside one, or a .millburn project.";
                return;
            }

            // Dropping a project opens it; dropping a folder imports it. Both replace what is
            // open, so both go through the same guard — and that guard stays silent when there is
            // nothing to lose, because the frictionless path is the point of drag and drop.
            if (!await ConfirmReplaceAsync())
            {
                return;
            }

            if (dropped.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                vm.OpenProject(dropped);
                return;
            }

            vm.LoadFolder(dropped);
        });
    }

    /// <summary>
    /// What was dropped: a project file, a folder, or the folder containing a dropped board file.
    /// Someone selecting all their Gerbers and dragging them across should not be told to try again
    /// with the folder.
    /// </summary>
    private static string? PathFrom(IDataTransfer data)
    {
        foreach (var item in data.TryGetFiles() ?? [])
        {
            var path = item.TryGetLocalPath();
            if (path is null)
            {
                continue;
            }

            if (File.Exists(path) && path.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return path;
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

    // ------------------------------------------------------------------ unsaved-work guard

    /// <summary>
    /// Asks before throwing work away, and — just as importantly — stays out of the way when there
    /// is none. A fresh board with nothing configured costs nothing to replace, so prompting there
    /// would only teach people to dismiss the prompt without reading it.
    /// </summary>
    private async Task<bool> ConfirmReplaceAsync()
    {
        if (DataContext is not MainViewModel vm || !vm.Project.IsDirty)
        {
            return true;
        }

        var answer = await ConfirmWindow.AskAsync(
            this,
            "Unsaved changes",
            $"{vm.Project.DisplayName} has unsaved changes. Save before replacing it?",
            "Save",
            "Discard");

        return answer switch
        {
            ConfirmResult.Save => await SaveAsync(),
            ConfirmResult.Discard => true,
            _ => false,
        };
    }

    /// <summary>Saves, asking for a path the first time. False means the user backed out.</summary>
    private async Task<bool> SaveAsync()
    {
        if (DataContext is not MainViewModel vm)
        {
            return false;
        }

        return vm.Project.FilePath is { } path ? vm.SaveProject(path) : await SaveAsAsync();
    }

    private async Task<bool> SaveAsAsync()
    {
        if (DataContext is not MainViewModel vm)
        {
            return false;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save project",
            SuggestedFileName = vm.Project.DisplayName,
            DefaultExtension = ProjectFile.Extension.TrimStart('.'),
            FileTypeChoices = [ProjectFileType],
        });

        var path = file?.TryGetLocalPath();
        return path is not null && vm.SaveProject(path);
    }

    private static FilePickerFileType ProjectFileType { get; } = new("PCB_MillBurn project")
    {
        Patterns = ["*" + ProjectFile.Extension],
    };

    // ------------------------------------------------------------------ commands

    private async void OnNewClicked(object? sender, RoutedEventArgs e)
    {
        if (await ConfirmReplaceAsync() && DataContext is MainViewModel vm)
        {
            vm.NewProject();
        }
    }

    private async void OnOpenProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !await ConfirmReplaceAsync())
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open project",
            AllowMultiple = false,
            FileTypeFilter = [ProjectFileType],
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is not null)
        {
            vm.OpenProject(path);
        }
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e) => await SaveAsAsync();

    private void OnRefreshClicked(object? sender, RoutedEventArgs e) =>
        (DataContext as MainViewModel)?.InspectRefresh();

    private async void OnEditToolsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        // A dialog is its own top level, so it does not inherit the variant the user chose.
        var editor = new ToolLibraryWindow(vm.Library) { RequestedThemeVariant = ActualThemeVariant };
        await editor.ShowDialog(this);
        vm.ReloadLibrary();
    }

    private void OnMillClicked(object? sender, RoutedEventArgs e) =>
        (DataContext as MainViewModel)?.Mill();

    private async void OnSaveGcodeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (vm.Gcode is null)
        {
            vm.Mill();
        }

        if (vm.Gcode is not { } text)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save G-code",
            SuggestedFileName = vm.Project.DisplayName,
            DefaultExtension = "nc",
            FileTypeChoices = [new FilePickerFileType("G-code") { Patterns = ["*.nc", "*.gcode", "*.tap"] }],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            await File.WriteAllTextAsync(path, text);
            vm.StatusMessage = $"Saved {Path.GetFileName(path)}.";
        }
    }

    private void OnApplyRefreshClicked(object? sender, RoutedEventArgs e) =>
        (DataContext as MainViewModel)?.ApplyRefresh();

    private void OnCancelRefreshClicked(object? sender, RoutedEventArgs e) =>
        (DataContext as MainViewModel)?.CancelRefresh();

    private async void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !await ConfirmReplaceAsync())
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
