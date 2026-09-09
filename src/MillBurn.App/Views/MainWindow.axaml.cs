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
using MillBurn.Core;
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
        SetUpShortcuts();
        SetUpWindowPlacement();

        var args = Environment.GetCommandLineArgs();
        _fpsTest = args.Contains("--fpstest", StringComparer.OrdinalIgnoreCase);

        Opened += (_, _) => OnOpened(args);
    }

    // ------------------------------------------------------------------ window placement

    /// <summary>
    /// The placement the window had while it was a normal window, not a maximised one.
    ///
    /// Recorded as the same <c>Width</c>/<c>Height</c> that get set on restore, rather than as the
    /// frame size. Storing the frame and restoring the client area adds the border thickness back
    /// every launch, which is how a window grows a few pixels each time it is opened.
    /// </summary>
    private WindowPlacement? _normalBounds;

    /// <summary>
    /// True for a run that exists to produce a screenshot. Such a run must not save its placement:
    /// its size was dictated on the command line, and writing it back would silently resize the
    /// window the user actually works in.
    /// </summary>
    private bool _transientSize;

    private void SetUpWindowPlacement()
    {
        void Remember()
        {
            if (WindowState == WindowState.Normal && Width > 0 && Height > 0)
            {
                _normalBounds = new WindowPlacement
                {
                    X = Position.X,
                    Y = Position.Y,
                    Width = Width,
                    Height = Height,
                };
            }
        }

        PositionChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();

        // Saved on the way out rather than as it changes: dragging a window across a desk should
        // not write to disk on every frame.
        Closing += (_, _) => SavePlacement();
    }

    private void SavePlacement()
    {
        if (_transientSize || DataContext is not MainViewModel vm)
        {
            return;
        }

        var bounds = _normalBounds ?? new WindowPlacement
        {
            X = Position.X,
            Y = Position.Y,
            Width = Width,
            Height = Height,
        };

        vm.SaveWindowPlacement(bounds with
        {
            Width = Math.Max(bounds.Width, 640),
            Height = Math.Max(bounds.Height, 480),
            Maximised = WindowState is WindowState.Maximized or WindowState.FullScreen,
        });
    }

    /// <summary>
    /// Puts the window back where it was, if that is still somewhere it can be reached.
    ///
    /// The check is the point. A window restored onto a monitor that has since been unplugged is
    /// invisible and cannot be dragged back, and the only fix is editing a settings file the user
    /// does not know exists — so a placement is only honoured while enough of its title bar still
    /// lands on a screen to grab.
    /// </summary>
    private void RestorePlacement(WindowPlacement placement)
    {
        var frame = new PixelRect(
            placement.X, placement.Y, (int)placement.Width, (int)placement.Height);

        var grabbable = new PixelRect(frame.X, frame.Y, frame.Width, Math.Min(frame.Height, 40));

        if (!Screens.All.Any(s => s.WorkingArea.Intersects(grabbable)))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = placement.Width;
        Height = placement.Height;
        Position = new PixelPoint(placement.X, placement.Y);
        _normalBounds = placement with { Maximised = false };

        if (placement.Maximised)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// Makes the shortcuts printed beside the menu items real.
    ///
    /// <c>MenuItem.InputGesture</c> only draws the text: a menu can advertise Ctrl+S and do nothing
    /// when it is pressed, which is worse than not offering it. The menu items carry Click handlers
    /// rather than commands, so the keys are dispatched to the same handlers here.
    ///
    /// Bubbling, not tunnelling: a control that wants the key gets it first, so this can never
    /// swallow a keystroke out from under a text box.
    /// </summary>
    private void SetUpShortcuts()
    {
        AddHandler(KeyDownEvent, (_, e) =>
        {
            var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            Action? act = (e.Key, control, shift) switch
            {
                (Key.N, true, false) => () => OnNewClicked(this, new RoutedEventArgs()),
                (Key.O, true, false) => () => OnOpenProjectClicked(this, new RoutedEventArgs()),
                (Key.I, true, false) => () => OnOpenFolderClicked(this, new RoutedEventArgs()),
                (Key.S, true, false) => () => OnSaveClicked(this, new RoutedEventArgs()),
                (Key.S, true, true) => () => OnSaveAsClicked(this, new RoutedEventArgs()),
                (Key.E, true, false) => () => OnExportClicked(this, new RoutedEventArgs()),
                (Key.T, true, false) => () => OnEditToolsClicked(this, new RoutedEventArgs()),
                (Key.D0, true, false) => () => OnFitClicked(this, new RoutedEventArgs()),
                (Key.F5, false, false) => () => OnPreviewClicked(this, new RoutedEventArgs()),
                (Key.F1, false, false) => () => OnHelpClicked(this, new RoutedEventArgs()),
                _ => null,
            };

            if (act is null)
            {
                return;
            }

            e.Handled = true;
            act();
        }, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// Closing with unsaved work asks first. The close is cancelled, the question asked, and the
    /// window closed again only once it is answered — the dialog cannot block a close synchronously.
    /// </summary>
    private void SetUpCloseGuard()
    {
        Closing += async (_, e) =>
        {
            if (_closeConfirmed || DataContext is not MainViewModel vm || !vm.Project.NeedsSaving)
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

        // A saved placement is only honoured for a normal run. A screenshot dictates its own size,
        // and letting a saved one win would make the captures non-reproducible.
        _transientSize = Argument(args, "--size") is not null || ShotPath(args) is not null;

        if (!_transientSize && vm.Settings.Window is { } placement)
        {
            RestorePlacement(placement);
        }

        // A screenshot only shows what fits, so a panel that runs past the bottom of a 800px window
        // hides exactly the controls worth checking. Resizing is cheaper than scripting a scroll.
        if (Argument(args, "--size") is { } size
            && size.Split('x') is [var w, var h]
            && int.TryParse(w, CultureInfo.InvariantCulture, out var width)
            && int.TryParse(h, CultureInfo.InvariantCulture, out var height))
        {
            Width = width;
            Height = height;
        }

        // --theme lets a screenshot prove the dark variant actually flips, which is the only
        // way this class of bug gets caught: it produces a half-styled window, never an error.
        // Given explicitly it wins over the saved one, so a capture is reproducible.
        var theme = Argument(args, "--theme") ?? vm.Settings.Theme;
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

        if (args.Contains("--mill", StringComparer.OrdinalIgnoreCase)
            || args.Contains("--preview", StringComparer.OrdinalIgnoreCase))
        {
            vm.Preview();
        }

        // Opens the export confirmation for a screenshot, so the dialog that decides what actually
        // gets written is checkable headlessly like everything else.
        if (args.Contains("--export", StringComparer.OrdinalIgnoreCase) && vm.PlanExport() is { } plan)
        {
            var window = new ExportWindow(plan, Environment.CurrentDirectory)
            {
                RequestedThemeVariant = ActualThemeVariant,
            };

            window.Show(this);
            _captureInstead = window;
        }

        // Opens the colour picker for a screenshot: it lives in a separate package with its own
        // theme resources, so "does it render at all" is a real question rather than a formality.
        if (args.Contains("--colour", StringComparer.OrdinalIgnoreCase) && vm.Layers.Count > 0)
        {
            // The last layer, so a --preview --colour run opens on a backplot layer: those are the
            // ones whose colour most often needs changing, and the ones that could not be changed.
            var row = vm.Layers[^1];
            var picker = new ColourWindow(row.Label, vm.ColourOf(row))
            {
                RequestedThemeVariant = ActualThemeVariant,
            };

            picker.Show(this);
            _captureInstead = picker;
        }

        // Opens every layer's settings, so a screenshot can show the expanded row rather than only
        // the shut one. The rows are the part most likely to lay out wrongly.
        if (args.Contains("--expand", StringComparer.OrdinalIgnoreCase))
        {
            ExpandLayers(true);
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
        if (DataContext is not MainViewModel vm || !vm.Project.NeedsSaving)
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

    private void OnPreviewClicked(object? sender, RoutedEventArgs e) =>
        (DataContext as MainViewModel)?.Preview();

    /// <summary>
    /// Plans the export, shows exactly what would be written, and writes only if that is confirmed.
    ///
    /// The confirmation is the point rather than a formality: which layer became which file, what
    /// tool it assumes and how deep it goes are the facts that decide whether the right thing is
    /// about to be cut, and they are invisible once the files are on disk.
    /// </summary>
    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (vm.PlanExport() is not { } plan)
        {
            return;
        }

        if (plan.Count == 0)
        {
            vm.StatusMessage = "Nothing to export: no layer is set to produce a file.";
            return;
        }

        var folder = vm.Settings.LastExportFolder is { } last && Directory.Exists(last)
            ? last
            : vm.Project.OriginFolder ?? Environment.CurrentDirectory;

        if (await ExportWindow.AskAsync(this, plan, folder) is { } chosen)
        {
            vm.WriteExport(plan, chosen);
        }
    }

    /// <summary>
    /// Changing a layer's colour. Global, and saved immediately: which colours read well is a fact
    /// about the operator's monitor, not about this board.
    /// </summary>
    private async void OnSwatchClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm
            || sender is not Control { DataContext: LayerRow row })
        {
            return;
        }

        var (colour, reset) = await ColourWindow.AskAsync(this, row.Label, vm.ColourOf(row));

        if (reset)
        {
            vm.ResetColour(row);
        }
        else if (colour is { } picked)
        {
            vm.SetColour(row, picked);
        }
    }

    private void OnResetColoursClicked(object? sender, RoutedEventArgs e) =>
        (DataContext as MainViewModel)?.ResetColours();

    private void OnShowAllLayersClicked(object? sender, RoutedEventArgs e) => SetAllLayers(true);

    private void OnExpandLayersClicked(object? sender, RoutedEventArgs e) => ExpandLayers(true);

    private void OnCollapseLayersClicked(object? sender, RoutedEventArgs e) => ExpandLayers(false);

    private void ExpandLayers(bool expanded)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        foreach (var row in vm.Layers)
        {
            row.IsExpanded = expanded;
        }
    }

    private void OnHideAllLayersClicked(object? sender, RoutedEventArgs e) => SetAllLayers(false);

    private void SetAllLayers(bool visible)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        foreach (var row in vm.Layers)
        {
            row.IsVisible = visible;
        }
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnHelpClicked(object? sender, RoutedEventArgs e) => OpenHelp("index.html");

    private void OnFaqClicked(object? sender, RoutedEventArgs e) => OpenHelp("faq.html");

    /// <summary>
    /// Opens a shipped help page in the user's own browser.
    ///
    /// Plain files next to the executable, opened by the OS: help has to work on a workshop machine
    /// with no network, and an in-app browser would be a second rendering engine to maintain for
    /// no gain. If the page is missing the app says which file it wanted rather than doing nothing,
    /// because a Help menu that silently does nothing is worse than no Help menu.
    /// </summary>
    private void OpenHelp(string page)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Help", page);

        if (!File.Exists(path))
        {
            if (DataContext is MainViewModel missing)
            {
                missing.StatusMessage = $"Help is not installed: {path} is missing.";
            }

            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or IOException or System.PlatformNotSupportedException)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.StatusMessage = $"Could not open help: {ex.Message}. It is at {path}.";
            }
        }
    }

    private async void OnAboutClicked(object? sender, RoutedEventArgs e) =>
        await ConfirmWindow.NoteAsync(
            this,
            "PCB_MillBurn",
            "Gerber to G-code for the mill and SVG for the laser. MIT licensed.");

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
        var dark = ActualThemeVariant != ThemeVariant.Dark;
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        // Saved on the toggle rather than on close: someone who tries dark and closes the app
        // expects it back, and a crash should not lose the one thing they changed.
        if (!_transientSize && DataContext is MainViewModel vm)
        {
            vm.SaveTheme(dark ? "Dark" : "Light");
        }

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
