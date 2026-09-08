using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using MillBurn.Viewer;
using SkiaSharp;
using PipelineSummary = MillBurn.Pipeline.BoardSummary;

namespace MillBurn.App.ViewModels;

/// <summary>
/// The shell's state: one open project, its layers and what each of them becomes, and how the
/// viewport is coping.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    /// <summary>The Phase 0 acceptance target from Documentation/06-Roadmap-and-Risks.md.</summary>
    public const int TargetSegments = 500_000;

    private MillBurnProject _project = MillBurnProject.Empty();
    private Board? _board;
    private IReadOnlyList<BackplotLayer> _backplot = [];
    private RefreshPlan? _plan;
    private bool _suspendOutputChanges;

    [ObservableProperty]
    public partial BoardScene? Scene { get; set; }

    [ObservableProperty]
    public partial ToolpathScene? ToolpathScene { get; set; }

    [ObservableProperty]
    public partial string BoardSummary { get; set; } = "No board loaded";

    [ObservableProperty]
    public partial string BoardTitle { get; set; } = "No project";

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "PCB_MillBurn";

    [ObservableProperty]
    public partial string FrameSummary { get; set; } = "—";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Start a project on the left, or drag a Gerber folder onto the window.";

    [ObservableProperty]
    public partial bool HasBoard { get; set; }

    [ObservableProperty]
    public partial bool CanRefresh { get; set; }

    [ObservableProperty]
    public partial string GcodeSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RefreshSummary { get; set; } = string.Empty;

    /// <summary>
    /// The stock's thickness, which is what sets the depth for drilling and for cutting out.
    ///
    /// Global rather than per layer because it is one physical fact about the material: every
    /// operation that goes through goes through the same board. What varies per layer is how far
    /// past the back to break, which is a property of the operation.
    /// </summary>
    [ObservableProperty]
    public partial double BoardThicknessMm { get; set; } = 1.6;

    /// <summary>What Export will write: everything, or only one kind.</summary>
    [ObservableProperty]
    public partial string SelectedExportFilter { get; set; } = "Both";

    public IReadOnlyList<string> ExportFilters { get; } = ["Both", "SVG only", "G-code only"];

    public ObservableCollection<LayerRow> Layers { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public ObservableCollection<RefreshItem> RefreshItems { get; } = [];

    /// <summary>A handful of facts that confirm the right board loaded.</summary>
    public ObservableCollection<string> Facts { get; } = [];

    /// <summary>The saved library, reloaded when the tool editor changes it.</summary>
    public ToolLibrary Library { get; private set; } = ToolLibrary.LoadOrDefault();

    /// <summary>Preferences that belong to the person, not to the board.</summary>
    public AppSettings Settings { get; private set; } = AppSettings.LoadOrDefault();

    public MillBurnProject Project => _project;

    public Board? Board => _board;

    /// <summary>The most recent preview, so it can be saved without regenerating it.</summary>
    public string? Gcode { get; private set; }

    /// <summary>Raised when a layer is toggled, so the view repaints without a scene swap.</summary>
    public event EventHandler? RedrawRequested;

    /// <summary>Where a refresh would read from, or null when there is nowhere to read.</summary>
    public string? RefreshSource =>
        _project.OriginFolder is { } folder && Directory.Exists(folder) ? folder : null;

    public MainViewModel()
    {
        BoardThicknessMm = Settings.BoardThicknessMm;
        AttachProject(_project);
    }

    // ------------------------------------------------------------------ loading

    public void LoadFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        try
        {
            var sw = Stopwatch.StartNew();
            var project = MillBurnProject.FromSources(ProjectFile.ImportFolder(folder), folder);

            if (project.Sources.Length == 0)
            {
                StatusMessage = $"No Gerber or drill files in '{Path.GetFileName(folder)}'.";
                return;
            }

            Adopt(project, sw.Elapsed);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not read '{folder}': {ex.Message}";
        }
    }

    public void OpenProject(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            var sw = Stopwatch.StartNew();
            Adopt(ProjectFile.Open(path), sw.Elapsed);
            SaveSettings(Settings.WithRecent(path));

            // Opening is exactly when someone wants to know their board moved on without them.
            // Passive: it offers, it never applies.
            CheckSourceQuietly();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not open '{Path.GetFileName(path)}': {ex.Message}";
        }
    }

    public bool SaveProject(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            // The view state is session state, but it is worth keeping: capture it at save time
            // rather than letting it mark the document dirty as it changes.
            _project.ViewState = CaptureViewState();
            ProjectFile.Save(_project, path);
            SaveSettings(Settings.WithRecent(path));
            RefreshTitles();
            StatusMessage = $"Saved {Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not save '{Path.GetFileName(path)}': {ex.Message}";
            return false;
        }
    }

    public void NewProject()
    {
        Adopt(MillBurnProject.Empty(), TimeSpan.Zero);
        BoardTitle = "No project";
        BoardSummary = "No board loaded";
        StatusMessage = "New project. Import a Gerber folder to begin.";
    }

    // ------------------------------------------------------------------ export

    public OutputKind? CurrentFilter => SelectedExportFilter switch
    {
        "SVG only" => OutputKind.Svg,
        "G-code only" => OutputKind.Gcode,
        _ => null,
    };

    /// <summary>
    /// Works out every file this export would write, without writing any of them.
    ///
    /// Planning and writing stay separate because the check is the point: which layer became which
    /// file, what tool it assumes, and whether anything about the combination is wrong.
    /// </summary>
    public ExportPlan? PlanExport(OutputKind? filter = null)
    {
        if (_board is null)
        {
            return null;
        }

        var settings = Layers
            .Where(r => r.Layer is not null)
            .ToDictionary(r => r.FileName, r => r.ToSettings(), StringComparer.Ordinal);

        return ExportPlanner.Plan(
            _board, settings, Library, Nm.FromMillimetres(BoardThicknessMm), filter ?? CurrentFilter);
    }

    public bool WriteExport(ExportPlan plan, string folder)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(folder);

        try
        {
            Directory.CreateDirectory(folder);
            foreach (var item in plan.Items)
            {
                File.WriteAllText(Path.Combine(folder, item.TargetName), item.Content);
            }

            SaveSettings(Settings with { LastExportFolder = folder });
            StatusMessage = $"Wrote {plan.Count} file(s) to {folder}.";
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not write to '{folder}': {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Builds every G-code layer and draws the result back over the board.
    ///
    /// The drawing comes from **parsing the emitted programs**, not from the toolpaths that produced
    /// them. Those two agree right up until the emitter has a bug, and only one of them is what the
    /// machine will run (Documentation/05, section 2.1).
    /// </summary>
    public void Preview()
    {
        if (PlanExport(OutputKind.Gcode) is not { } plan || _board is null)
        {
            return;
        }

        if (plan.Count == 0)
        {
            StatusMessage = "No layer is set to produce G-code.";
            return;
        }

        var shift = new Point2(_board.Bounds.MinX, _board.Bounds.MinY);
        var moves = new List<BackplotMove>();
        var cut = 0.0;
        var travel = 0.0;
        var plunges = 0;
        var gouges = 0;

        foreach (var item in plan.Items)
        {
            var classified = GcodeBackplot.Classify(GcodeParser.Parse(item.Content));
            var measured = GcodeBackplot.Measure(classified);

            moves.AddRange(classified);
            cut += measured.CutMm;
            travel += measured.TravelMm;
            plunges += measured.PlungeCount;
            gouges += measured.GougeCount;
        }

        Gcode = string.Join("\n", plan.Items.Select(i => i.Content));
        _backplot = BackplotBuilder.Build(moves, shift);

        GcodeSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{plan.Count} programs · {cut:F0} mm cut · {travel:F0} mm travel · {plunges} plunges");

        Rebuild(TimeSpan.Zero);

        foreach (var warning in plan.Items.SelectMany(i => i.Warnings).Distinct(StringComparer.Ordinal))
        {
            Warnings.Add(warning);
        }

        StatusMessage = gouges > 0
            ? $"{gouges} rapid move(s) at cutting depth — do not run this."
            : $"Previewing {plan.Count} program(s).";
    }

    // ------------------------------------------------------------------ refresh

    /// <summary>Compares the project against its source folder. Reads only; changes nothing.</summary>
    public void InspectRefresh()
    {
        RefreshItems.Clear();
        RefreshSummary = string.Empty;

        if (RefreshSource is not { } folder)
        {
            StatusMessage = _project.OriginFolder is null
                ? "This project has no source folder recorded."
                : $"The source folder '{_project.OriginFolder}' is not there any more.";
            return;
        }

        try
        {
            var plan = ProjectRefresh.Inspect(_project, folder);
            _plan = plan;
            RefreshSummary = plan.Summary();

            foreach (var change in plan.Actionable)
            {
                RefreshItems.Add(new RefreshItem(change));
            }

            StatusMessage = plan.HasChanges
                ? $"Reviewing changes in {Path.GetFileName(folder)}."
                : $"{Path.GetFileName(folder)} matches this project.";
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not read '{folder}': {ex.Message}";
        }
    }

    public void ApplyRefresh()
    {
        if (_plan is not { } plan)
        {
            return;
        }

        var taking = RefreshItems.Where(i => i.Selected).Select(i => i.FileName).ToList();
        if (taking.Count == 0)
        {
            CancelRefresh();
            return;
        }

        ProjectRefresh.Apply(_project, plan, taking);
        Rebuild(TimeSpan.Zero);
        CancelRefresh();
        StatusMessage = $"Refreshed {taking.Count} file(s). Save to keep this.";
    }

    /// <summary>
    /// Looks for source changes without saying anything when there are none.
    ///
    /// Detection is passive on purpose. Auto-applying would swap the geometry under someone who
    /// opened a project to look at last week's job, and announcing "no changes" every single time is
    /// noise that teaches people to ignore the one time it matters.
    /// </summary>
    public void CheckSourceQuietly()
    {
        if (RefreshSource is null)
        {
            return;
        }

        var status = StatusMessage;
        InspectRefresh();

        if (RefreshItems.Count == 0)
        {
            CancelRefresh();
            StatusMessage = status;
            return;
        }

        StatusMessage = $"{status} The source folder has changed.";
    }

    public void CancelRefresh()
    {
        _plan = null;
        RefreshItems.Clear();
        RefreshSummary = string.Empty;
    }

    // ------------------------------------------------------------------ colours

    /// <summary>
    /// Overrides a layer's colour, everywhere and for good.
    ///
    /// A global setting rather than a per-project one: which colours read well is a fact about the
    /// operator's eyes and monitor, not about the board, so it must not travel with a project or
    /// change when one is opened.
    /// </summary>
    public void SetColour(LayerRow row, Color colour)
    {
        ArgumentNullException.ThrowIfNull(row);

        var hex = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

        SaveSettings(row.Role is { } role
            ? Settings.WithColour(role, hex)
            : Settings.WithSubstrateColour(hex));

        Rebuild(TimeSpan.Zero);
    }

    /// <summary>Puts one layer back to the built-in palette.</summary>
    public void ResetColour(LayerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        SaveSettings(row.Role is { } role
            ? Settings.WithoutColour(role)
            : Settings with { SubstrateColour = null });

        Rebuild(TimeSpan.Zero);
    }

    /// <summary>Puts every layer back to the built-in palette.</summary>
    public void ResetColours()
    {
        SaveSettings(Settings with
        {
            LayerColours = System.Collections.Immutable.ImmutableDictionary<LayerRole, string>.Empty,
            SubstrateColour = null,
        });

        Rebuild(TimeSpan.Zero);
        StatusMessage = "Layer colours reset.";
    }

    public Color ColourOf(LayerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var fill = row.Role is { } role ? Palette(role).Fill : SubstrateStyle().Fill;
        return Color.FromArgb(0xFF, fill.Red, fill.Green, fill.Blue);
    }

    private BoardLayerStyle Palette(LayerRole role)
    {
        var style = BoardPalette.For(role);

        return Settings.LayerColours.TryGetValue(role, out var hex) && SKColor.TryParse(hex, out var colour)
            ? style with { Fill = colour }
            : style;
    }

    private BoardLayerStyle SubstrateStyle() =>
        Settings.SubstrateColour is { } hex && SKColor.TryParse(hex, out var colour)
            ? BoardPalette.Substrate with { Fill = colour }
            : BoardPalette.Substrate;

    public void ReloadLibrary()
    {
        Library = ToolLibrary.LoadOrDefault();
        Rebuild(TimeSpan.Zero);
    }

    private void SaveSettings(AppSettings settings)
    {
        Settings = settings;

        try
        {
            settings.Save();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not save settings: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------ presenting

    private void Adopt(MillBurnProject project, TimeSpan elapsed)
    {
        // A program made from the previous board is not a program for this one.
        _backplot = [];
        Gcode = null;
        GcodeSummary = string.Empty;

        DetachProject(_project);
        _project = project;
        AttachProject(project);
        CancelRefresh();

        Rebuild(elapsed);
    }

    /// <summary>Realises the project's sources and rebuilds everything the window shows.</summary>
    private void Rebuild(TimeSpan elapsed)
    {
        var previous = Scene;

        if (_project.Sources.Length == 0)
        {
            Scene = null;
            previous?.Dispose();
            Layers.Clear();
            Warnings.Clear();
            Facts.Clear();
            _board = null;
            _backplot = [];
            Gcode = null;
            GcodeSummary = string.Empty;
            HasBoard = false;
            CanRefresh = false;
            RefreshTitles();
            return;
        }

        var hidden = Layers.Where(r => !r.IsVisible).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var board = ProjectFile.ToBoard(_project);
        _board = board;

        var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            board.Bounds,
            Palette,
            _backplot.Count > 0 ? _backplot : null,
            SubstrateStyle());

        ApplyViewState(scene, hidden);
        BuildRows(board, scene);

        Scene = scene;
        HasBoard = true;
        CanRefresh = RefreshSource is not null;

        // Swapping the scene first and disposing after means the old SKPaths are never released
        // while a frame in flight is still drawing them.
        previous?.Dispose();

        BoardTitle = _project.DisplayName;
        var timing = elapsed > TimeSpan.Zero
            ? string.Create(CultureInfo.InvariantCulture, $" · loaded in {elapsed.TotalMilliseconds:F0} ms")
            : string.Empty;

        BoardSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{board.Layers.Count} layers · {board.TotalObjects:N0} objects · {scene.TotalVertices:N0} vertices{timing}");

        RefreshFacts(board);
        RefreshWarnings(board);
        RefreshTitles();

        if (elapsed > TimeSpan.Zero)
        {
            StatusMessage = Warnings.Count == 0
                ? $"Loaded {BoardTitle}."
                : $"Loaded {BoardTitle} with {Warnings.Count} thing(s) worth checking.";
        }
    }

    /// <summary>
    /// Rebuilds the layer rows from the scene, so the panel lists exactly what is drawn — including
    /// the substrate and any backplot, which have no file behind them.
    /// </summary>
    private void BuildRows(Board board, BoardScene scene)
    {
        var byName = board.Layers.ToDictionary(l => l.FileName, StringComparer.Ordinal);
        var backplotRuns = _backplot.ToDictionary(b => b.Id, b => b.Runs.Count, StringComparer.Ordinal);

        var chosen = Layers
            .Where(r => r.Layer is not null)
            .ToDictionary(r => r.FileName, r => r.ToSettings(), StringComparer.Ordinal);

        _suspendOutputChanges = true;
        Layers.Clear();

        foreach (var layer in scene.Layers)
        {
            if (byName.TryGetValue(layer.Id, out var source))
            {
                var settings = chosen.TryGetValue(layer.Id, out var existing)
                    ? existing
                    : new LayerOutputSettings
                    {
                        FileName = layer.Id,
                        Output = LayerOperations.DefaultFor(source.Role),
                    };

                Layers.Add(new LayerRow(
                    source, layer, settings, Library.Tools, OnLayerVisibilityChanged, OnOutputChanged));
            }
            else if (backplotRuns.TryGetValue(layer.Id, out var runs))
            {
                var detail = string.Create(
                    CultureInfo.InvariantCulture, $"From the emitted G-code · {runs:N0} runs");
                Layers.Add(new LayerRow(layer, detail, OnLayerVisibilityChanged));
            }
            else
            {
                Layers.Add(new LayerRow(
                    layer, "The board material, drawn under everything", OnLayerVisibilityChanged));
            }
        }

        _suspendOutputChanges = false;
    }

    private void RefreshFacts(Board board)
    {
        var facts = PipelineSummary.Facts(board);

        Facts.Clear();
        Facts.Add(string.Create(
            CultureInfo.InvariantCulture, $"{facts.WidthMm:F2} × {facts.HeightMm:F2} mm"));
        Facts.Add(string.Create(
            CultureInfo.InvariantCulture, $"{facts.Layers} layers · {board.TotalObjects:N0} objects"));

        if (facts.Holes > 0)
        {
            Facts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{facts.Holes} holes in {facts.HoleSizes} sizes, {facts.SmallestHoleMm:F2}–{facts.LargestHoleMm:F2} mm"));
        }

        if (facts.CopperIslands > 0)
        {
            Facts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{facts.CopperIslands} copper islands · {facts.CopperCoverage:P0} coverage"));
        }
    }

    /// <summary>
    /// Toggling a layer is session state, so it repaints and updates the stored view state without
    /// marking the document dirty. Prompting to save because someone looked under a layer teaches
    /// people to dismiss the prompt.
    /// </summary>
    private void OnLayerVisibilityChanged()
    {
        _project.ViewState = CaptureViewState();
        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Changing what a layer produces is a document change, and it invalidates any preview: a
    /// program made with the old settings is no longer a picture of this job, and leaving it on
    /// screen would be the most convincing kind of wrong.
    /// </summary>
    private void OnOutputChanged()
    {
        if (_suspendOutputChanges)
        {
            return;
        }

        _project.Touch();

        if (_backplot.Count == 0)
        {
            return;
        }

        _backplot = [];
        Gcode = null;
        GcodeSummary = string.Empty;
        Rebuild(TimeSpan.Zero);
        StatusMessage = "Output changed. Preview again to see the new programs.";
    }

    partial void OnBoardThicknessMmChanged(double value)
    {
        SaveSettings(Settings with { BoardThicknessMm = value });
        OnOutputChanged();
    }

    partial void OnSelectedExportFilterChanged(string value)
    {
        _ = value;
        OnPropertyChanged(nameof(CurrentFilter));
    }

    private ProjectViewState CaptureViewState() => new()
    {
        HiddenLayers = [.. Layers.Where(l => !l.IsVisible).Select(l => l.Id)],
    };

    private void ApplyViewState(BoardScene scene, HashSet<string> alreadyHidden)
    {
        var hidden = alreadyHidden.Count > 0
            ? alreadyHidden
            : _project.ViewState.HiddenLayers.IsDefaultOrEmpty
                ? null
                : _project.ViewState.HiddenLayers.ToHashSet(StringComparer.Ordinal);

        if (hidden is null)
        {
            return;
        }

        foreach (var layer in scene.Layers)
        {
            layer.Visible = !hidden.Contains(layer.Id);
        }
    }

    private void AttachProject(MillBurnProject project)
    {
        project.DirtyChanged += OnDirtyChanged;
        RefreshTitles();
    }

    private void DetachProject(MillBurnProject project) => project.DirtyChanged -= OnDirtyChanged;

    private void OnDirtyChanged(object? sender, EventArgs e) => RefreshTitles();

    private void RefreshTitles()
    {
        var name = _project.Sources.Length == 0 ? "PCB_MillBurn" : _project.DisplayName;
        WindowTitle = _project.IsDirty ? $"{name} * — PCB_MillBurn" : $"{name} — PCB_MillBurn";
    }

    /// <summary>
    /// Anything the operator should look at before trusting what is on screen. These are surfaced
    /// rather than logged because a board that is quietly missing a layer still looks like a board.
    /// </summary>
    private void RefreshWarnings(Board board)
    {
        Warnings.Clear();

        foreach (var failure in board.Failures)
        {
            Warnings.Add($"Could not read {failure}");
        }

        foreach (var layer in board.Layers.Where(l => l.HasErrors))
        {
            var first = layer.Diagnostics.First(d => d.IsError);
            Warnings.Add($"{layer.FileName}: {first.Message}");
        }

        foreach (var layer in board.Layers.Where(l => l.RoleGuessed))
        {
            Warnings.Add($"{layer.FileName} declares no file function; role guessed as {layer.Label}.");
        }

        if (!board.Layers.Any(l => l.Role == LayerRole.Outline))
        {
            Warnings.Add("No board outline: extents are taken from the drawn geometry.");
        }

        foreach (var group in board.Layers
            .Where(l => l.Role != LayerRole.Unknown)
            .GroupBy(l => l.Role)
            .Where(g => g.Count() > 1))
        {
            Warnings.Add(
                $"{group.Count()} files claim to be {LayerRoleInfo.Label(group.Key)}: " +
                string.Join(", ", group.Select(l => l.FileName)));
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or DirectoryNotFoundException or UnauthorizedAccessException
            or InvalidDataException or InvalidOperationException or ArgumentException;

    // ------------------------------------------------------------------ frame stats

    public void ReportFrame(double elapsedMs, int layers, int vertices)
    {
        var fps = elapsedMs <= 0 ? 0 : 1000.0 / elapsedMs;
        FrameSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{elapsedMs:F2} ms/frame · {fps:F0} fps · {layers} layers · {vertices:N0} vertices");
    }

    public void ReportFrame(double averageMs, FrameStats stats)
    {
        var fps = averageMs <= 0 ? 0 : 1000.0 / averageMs;
        FrameSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{averageMs:F2} ms/frame · {fps:F0} fps · LOD tier {stats.Tier} · " +
            $"{stats.SegmentsInView:N0} segments in view · {stats.DrawCalls} draw calls");
    }

    /// <summary>
    /// Builds the synthetic 500k-segment toolpath for the fps harness.
    ///
    /// Lazy on purpose: it costs about a second, and there is no reason to pay that on every launch
    /// for a measurement almost nobody runs.
    /// </summary>
    public void LoadSyntheticToolpath()
    {
        var sw = Stopwatch.StartNew();
        var polylines = SyntheticToolpath.Generate(TargetSegments);
        var generated = sw.Elapsed;

        sw.Restart();
        var scene = MillBurn.Viewer.ToolpathScene.Build(polylines);

        ToolpathScene = scene;
        BoardSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{scene.SourceSegmentCount:N0} synthetic segments · generated in {generated.TotalMilliseconds:F0} ms · " +
            $"4 LOD tiers built in {sw.Elapsed.TotalMilliseconds:F0} ms");
        BoardTitle = "Viewport benchmark";
    }
}
