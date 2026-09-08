using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Core;
using MillBurn.Cam;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using MillBurn.Viewer;

namespace MillBurn.App.ViewModels;

/// <summary>
/// The shell's state: one open project, the board it realises to, and how the viewport is coping.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    /// <summary>The Phase 0 acceptance target from Documentation/06-Roadmap-and-Risks.md.</summary>
    public const int TargetSegments = 500_000;

    private MillBurnProject _project = MillBurnProject.Empty();

    [ObservableProperty]
    public partial BoardScene? Scene { get; set; }

    [ObservableProperty]
    public partial ToolpathScene? ToolpathScene { get; set; }

    [ObservableProperty]
    public partial string BoardSummary { get; set; } = "No board loaded";

    [ObservableProperty]
    public partial string BoardTitle { get; set; } = "Drop a Gerber folder here";

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "PCB_MillBurn";

    [ObservableProperty]
    public partial string FrameSummary { get; set; } = "—";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Drag an export folder onto the window, or use Open folder.";

    [ObservableProperty]
    public partial bool HasBoard { get; set; }

    [ObservableProperty]
    public partial bool CanRefresh { get; set; }

    [ObservableProperty]
    public partial bool CanMill { get; set; }

    [ObservableProperty]
    public partial string GcodeSummary { get; set; } = string.Empty;

    /// <summary>The emitted program, so it can be saved without regenerating it.</summary>
    public string? Gcode { get; private set; }

    [ObservableProperty]
    public partial string RefreshSummary { get; set; } = string.Empty;

    public ObservableCollection<LayerToggle> Layers { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public ObservableCollection<RefreshItem> RefreshItems { get; } = [];

    public ObservableCollection<ToolChoice> ToolChoices { get; } = [];

    /// <summary>The saved library, reloaded when the tool editor changes it.</summary>
    public ToolLibrary Library { get; private set; } = ToolLibrary.LoadOrDefault();

    /// <summary>Isolation depth, which for a V-bit is the same thing as choosing the cut width.</summary>
    [ObservableProperty]
    public partial double IsolationDepthMm { get; set; } = 0.05;

    /// <summary>Raised when a layer is toggled, so the view can repaint without a scene swap.</summary>
    public event EventHandler? RedrawRequested;

    public MillBurnProject Project => _project;

    /// <summary>Where a refresh would read from, or null when there is nowhere to read.</summary>
    public string? RefreshSource =>
        _project.OriginFolder is { } folder && Directory.Exists(folder) ? folder : null;

    public MainViewModel()
    {
        AttachProject(_project);
        BuildToolChoices();
    }

    // ------------------------------------------------------------------ tools

    /// <summary>
    /// Rebuilds the per-operation tool pickers from the library.
    ///
    /// Per operation because traces and edge cuts want genuinely different tools, not because
    /// someone might prefer it: a V-bit's width follows its depth, which is what makes a 0.13 mm
    /// isolation cut possible at all, and the same bit taken to 1.9 mm for the outline would be
    /// millimetres wide at the surface.
    /// </summary>
    public void BuildToolChoices()
    {
        var previous = ToolChoices.ToDictionary(c => c.Label, c => c.Selected.Id, StringComparer.Ordinal);
        ToolChoices.Clear();

        // What was chosen last, then the built-in default if the library still has it, then
        // anything of the right kind. "First of the right kind" alone picks whatever happens to
        // sort first, which is how the outline ends up defaulting to the most fragile end mill in
        // the drawer.
        Tool Pick(string label, ToolKind kind, Tool fallback) =>
            (previous.TryGetValue(label, out var id) ? Library.Tools.FirstOrDefault(t => t.Id == id) : null)
            ?? Library.Tools.FirstOrDefault(t => t.Id == fallback.Id)
            ?? Library.OfKind(kind).FirstOrDefault()
            ?? fallback;

        var isolation = new ToolChoice(
            "Isolation",
            Library.OfKind(ToolKind.VBit).Concat(Library.OfKind(ToolKind.EndMill)),
            Pick("Isolation", ToolKind.VBit, Tool.DefaultVBit),
            t => ToolChoice.DescribeIsolation(t, Nm.FromMillimetres(IsolationDepthMm)));

        var outline = new ToolChoice(
            "Outline",
            Library.OfKind(ToolKind.EndMill),
            Pick("Outline", ToolKind.EndMill, Tool.DefaultOutlineMill),
            ToolChoice.DescribeOutline);

        var drill = new ToolChoice(
            "Drilling",
            Library.OfKind(ToolKind.Drill),
            Pick("Drilling", ToolKind.Drill, Tool.DefaultDrill),
            ToolChoice.DescribeDrill);

        foreach (var choice in new[] { isolation, outline, drill })
        {
            choice.Changed += (_, _) => OnToolChanged();
            ToolChoices.Add(choice);
        }
    }

    public void ReloadLibrary()
    {
        Library = ToolLibrary.LoadOrDefault();
        BuildToolChoices();
    }

    private void OnToolChanged()
    {
        // Choosing a tool is a document change: it changes what gets cut.
        _project.Touch();

        // Any program on screen was made with the old tool, so it is no longer a picture of this
        // job. Leaving it there would be the most convincing kind of wrong.
        if (_backplot.Count > 0)
        {
            _backplot = [];
            Gcode = null;
            GcodeSummary = string.Empty;
            Rebuild(TimeSpan.Zero);
            StatusMessage = "Tool changed. Mill again to see the new program.";
        }
    }

    partial void OnIsolationDepthMmChanged(double value)
    {
        _ = value;
        foreach (var choice in ToolChoices)
        {
            choice.Refresh();
        }

        OnToolChanged();
    }

    private ToolSelection CurrentTools() => new()
    {
        Isolation = ToolChoices.FirstOrDefault(c => c.Label == "Isolation")?.Selected ?? Tool.DefaultVBit,
        Outline = ToolChoices.FirstOrDefault(c => c.Label == "Outline")?.Selected ?? Tool.DefaultOutlineMill,
        Drill = ToolChoices.FirstOrDefault(c => c.Label == "Drilling")?.Selected ?? Tool.DefaultDrill,
    };

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
        BoardTitle = "Drop a Gerber folder here";
        BoardSummary = "No board loaded";
        StatusMessage = "New project. Drag an export folder onto the window.";
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
    /// opened a project to look at last week's job, and announcing "no changes" every single time
    /// is noise that teaches people to ignore the one time it matters.
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

    private RefreshPlan? _plan;

    private IReadOnlyList<BackplotLayer> _backplot = [];

    // ------------------------------------------------------------------ milling

    /// <summary>
    /// Builds the job, emits the program, and draws it back over the board.
    ///
    /// The drawing comes from **parsing the emitted file**, not from the toolpaths that produced
    /// it. Those two agree right up until the emitter has a bug, and only one of them is what the
    /// machine will run (Documentation/05, section 2.1).
    /// </summary>
    public void Mill()
    {
        if (_project.Sources.Length == 0)
        {
            return;
        }

        try
        {
            var board = ProjectFile.ToBoard(_project);
            var tools = CurrentTools();
            var job = JobBuilder.Build(board, new MillOptions
            {
                Tools = tools,
                Isolation = new IsolationOptions { DepthNm = Nm.FromMillimetres(IsolationDepthMm) },
            });

            // The project keeps a copy of what it was cut with, not a pointer into the library.
            _project.Settings = _project.Settings with
            {
                Tools = [.. tools.All],
                IsolationToolId = tools.Isolation.Id,
                OutlineToolId = tools.Outline.Id,
                DrillToolId = tools.Drill.Id,
            };
            var (text, _) = GcodeEmitter.Emit(job, new GcodeOptions());

            var program = GcodeParser.Parse(text);
            var classified = GcodeBackplot.Classify(program);
            var measured = GcodeBackplot.Measure(classified);

            Gcode = text;
            _backplot = BackplotBuilder.Build(
                classified, new Point2(-job.OriginShift.X, -job.OriginShift.Y));

            GcodeSummary = string.Create(
                CultureInfo.InvariantCulture,
                $"{measured.CutMm:F0} mm cut · {measured.TravelMm:F0} mm travel · " +
                $"{measured.PlungeCount} plunges · {measured.TimeRange()}");

            Rebuild(TimeSpan.Zero);

            foreach (var note in job.Notes.Where(n => !n.Contains("lower-left", StringComparison.Ordinal)))
            {
                Warnings.Add(note);
            }

            StatusMessage = measured.GougeCount > 0
                ? $"{measured.GougeCount} rapid move(s) at cutting depth — do not run this."
                : $"Milled {BoardTitle}. {measured.LongTravelCount} rapids over 10 mm.";
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not generate G-code: {ex.Message}";
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
            HasBoard = false;
            CanRefresh = false;
            CanMill = false;
            GcodeSummary = string.Empty;
            Gcode = null;
            _backplot = [];
            RefreshTitles();
            return;
        }

        var board = ProjectFile.ToBoard(_project);

        var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            board.Bounds,
            backplot: _backplot.Count > 0 ? _backplot : null);

        ApplyViewState(scene);

        // Look the file up rather than demanding one: the scene also carries a synthetic substrate
        // layer that belongs to no file.
        var byName = board.Layers.ToDictionary(l => l.FileName, StringComparer.Ordinal);
        var backplotRuns = _backplot.ToDictionary(b => b.Id, b => b.Runs.Count, StringComparer.Ordinal);

        Layers.Clear();
        foreach (var layer in scene.Layers)
        {
            string detail;
            if (byName.TryGetValue(layer.Id, out var source))
            {
                detail = DetailFor(source);
            }
            else if (backplotRuns.TryGetValue(layer.Id, out var runs))
            {
                detail = string.Create(CultureInfo.InvariantCulture, $"From the emitted G-code · {runs:N0} runs");
            }
            else
            {
                detail = "The board material, drawn under everything";
            }

            Layers.Add(new LayerToggle(layer, detail, OnLayerToggled));
        }

        Scene = scene;
        HasBoard = true;
        CanRefresh = RefreshSource is not null;
        CanMill = true;

        // Swapping the scene first and disposing after means the old SKPaths are never released
        // while a frame in flight is still drawing them.
        previous?.Dispose();

        BoardTitle = _project.DisplayName;
        var size = $"{Nm.ToMillimetreString(board.Bounds.Width, 2)} x {Nm.ToMillimetreString(board.Bounds.Height, 2)} mm";
        var timing = elapsed > TimeSpan.Zero
            ? string.Create(CultureInfo.InvariantCulture, $" · loaded in {elapsed.TotalMilliseconds:F0} ms")
            : string.Empty;

        BoardSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{board.Layers.Count} layers · {board.TotalObjects:N0} objects · {scene.TotalVertices:N0} vertices · {size}{timing}");

        RefreshWarnings(board);
        RefreshTitles();

        if (Warnings.Count > 0)
        {
            StatusMessage = $"Loaded {BoardTitle} with {Warnings.Count} thing(s) worth checking.";
        }
        else if (StatusMessage.Length == 0 || !StatusMessage.StartsWith("Refreshed", StringComparison.Ordinal))
        {
            StatusMessage = $"Loaded {BoardTitle}.";
        }
    }

    /// <summary>
    /// Toggling a layer is session state, so it repaints and updates the stored view state without
    /// marking the document dirty. Prompting to save because someone looked under a layer teaches
    /// people to dismiss the prompt.
    /// </summary>
    private void OnLayerToggled()
    {
        _project.ViewState = CaptureViewState();
        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    private ProjectViewState CaptureViewState() => new()
    {
        HiddenLayers = [.. Layers.Where(l => !l.IsVisible).Select(l => l.Id)],
    };

    private void ApplyViewState(BoardScene scene)
    {
        if (_project.ViewState.HiddenLayers.IsDefaultOrEmpty)
        {
            return;
        }

        var hidden = _project.ViewState.HiddenLayers.ToHashSet(StringComparer.Ordinal);
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

    private static string DetailFor(BoardLayer layer)
    {
        var detail = string.Create(
            CultureInfo.InvariantCulture,
            $"{layer.FileName} · {layer.ObjectCount:N0} objects · {layer.AreaMm2:F2} mm²");

        return layer.DeclaredNegative ? detail + " · negative" : detail;
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
