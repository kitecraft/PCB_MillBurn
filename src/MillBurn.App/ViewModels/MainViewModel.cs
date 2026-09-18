using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Core;
using MillBurn.Align;
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

    partial void OnHasBoardChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ShowingNothing));
        OnPropertyChanged(nameof(CanWriteBlank));
    }

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

    public ObservableCollection<LayerRow> Layers { get; } = [];

    /// <summary>
    /// Kinds of move, and the substrate — a filter across the whole drawing rather than a list of
    /// layers, which is what these were pretending to be while they sat among the real ones.
    /// </summary>
    public ObservableCollection<MoveKindRow> MoveKinds { get; } = [];

    /// <summary>
    /// Which kinds of move are showing, held across rebuilds.
    ///
    /// The chips are rebuilt from the backplot every time a program is emitted, and without this
    /// they would come back on their defaults — so turning travel off and pressing Preview once
    /// more would turn it back on, which is exactly when you least want it.
    /// </summary>
    private readonly Dictionary<string, bool> _kindVisible = new(StringComparer.Ordinal);

    /// <summary>
    /// The same rows as <see cref="Layers"/>, bucketed by which part of the board they belong to.
    /// This is what the panel lists; <see cref="Layers"/> stays flat for everything that has to
    /// walk every row regardless of where it sits.
    /// </summary>
    public ObservableCollection<LayerGroup> LayerGroups { get; } = [];

    /// <summary>
    /// How many checks there are, for the heading — because the list is capped and scrolled, and a
    /// panel showing three of nine with nothing saying so is worse than one showing all nine.
    /// </summary>
    public string WarningCount => Warnings.Count > 1
        ? string.Create(CultureInfo.InvariantCulture, $"· {Warnings.Count}")
        : string.Empty;

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
        // Restoring a saved preference is not the user changing anything, so it must not mark the
        // document dirty. Without this the app opens with unsaved changes it invented itself, and
        // the very first click asks whether to save an empty project.
        _suspendOutputChanges = true;
        BoardThicknessMm = Settings.BoardThicknessMm;
        _suspendOutputChanges = false;

        AttachProject(_project);

        // The heading counts the list, so it has to hear about the list changing.
        Warnings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(WarningCount));
    }

    /// <summary>Remembers where the window was, so it opens where it was left.</summary>
    public void SaveWindowPlacement(WindowPlacement placement) =>
        SaveSettings(Settings with { Window = placement });

    /// <summary>Remembers light or dark, so the app opens the way it was closed.</summary>
    public void SaveTheme(string? theme) => SaveSettings(Settings with { Theme = theme });

    /// <summary>
    /// Remembers how wide the board pane was left.
    ///
    /// Only when it has actually changed: this runs on every close, and rewriting the settings file
    /// to store the number it already held is work for nothing.
    /// </summary>
    public void SavePanelWidth(double width)
    {
        if (width < 100 || Math.Abs(width - Settings.PanelWidth) < 1)
        {
            return;
        }

        SaveSettings(Settings with { PanelWidth = width });
    }

    /// <summary>
    /// The lines that top and tail every program: the project's where it has them, the machine's
    /// otherwise.
    /// </summary>
    public ProgramFraming Framing => _project.Settings.Framing.Over(Settings.Framing);

    /// <summary>
    /// Keeps the machine's numbers, and rebuilds anything already on screen from them.
    ///
    /// The preview is emitted G-code, so a changed safe height or decimal count changes it — and a
    /// preview that still shows the old numbers after the settings were saved is the kind of thing
    /// somebody would only notice at the machine.
    /// </summary>
    public void SaveMachineSettings(
        MachineSettings machine, DryRunSettings dryRun, ProbeSettings probe, LevelSettings level,
        ImportDefaults import, MillingDefaults milling)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(milling);

        SaveSettings(Settings with
        {
            Machine = machine,
            DryRun = dryRun,
            Probe = probe,
            Level = level,
            Import = import,
            Milling = milling,
        });

        if (Gcode is not null && !HasProgram)
        {
            Preview();
        }

        StatusMessage = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Settings saved. Safe height {machine.SafeZMm:F2} mm, dry run held at {dryRun.HeightMm:F2} mm.");
    }

    public void SaveFraming(ProgramFraming framing)
    {
        ArgumentNullException.ThrowIfNull(framing);

        SaveSettings(Settings with { Framing = framing });
        OnPropertyChanged(nameof(Framing));

        StatusMessage = framing.IsEmpty
            ? "Start and end G-code cleared."
            : "Start and end G-code saved. It goes into every program this machine writes.";
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

    /// <summary>Recently opened and saved projects, most recent first: what File ▸ Open recent lists.</summary>
    public IReadOnlyList<string> RecentProjects => Settings.RecentProjects;

    /// <summary>
    /// Takes a project off the recent list, and says so. Called when one turns out not to be there
    /// any more — the file is the only thing that can tell us, and only when somebody asks for it.
    /// </summary>
    public void ForgetRecent(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        SaveSettings(Settings.WithoutRecent(path));
        StatusMessage = $"{Path.GetFileName(path)} is not there any more, so it has been taken off the recent list.";
    }

    public bool SaveProject(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            // The view state is session state, but it is worth keeping: capture it at save time
            // rather than letting it mark the document dirty as it changes.
            _project.ViewState = CaptureViewState();
            RecordOutputs();
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



    // ------------------------------------------------------------------ opening a program

    /// <summary>The extents of a program opened on its own, or null when none is.</summary>
    private Bounds? _programBounds;

    private string? _programPath;

    /// <summary>True when a standalone program is being shown rather than a board's own output.</summary>
    public bool HasProgram => _programPath is not null;

    /// <summary>
    /// True when the viewport has nothing in it at all.
    ///
    /// Distinct from "no board": a program opened on its own is something to look at, and the
    /// empty-state panel and the drop hint were both drawing over one.
    /// </summary>
    public bool ShowingNothing => !HasBoard && !HasProgram;

    /// <summary>
    /// Draws any G-code file, ours or anybody's.
    ///
    /// The parser, the classifier and the scene are all built on emitted text rather than on the
    /// toolpaths that produced it, so they already work on a program from anywhere. This is mostly
    /// the file picker they were missing — and it is what makes the <c>.dryrun.nc</c>,
    /// <c>.levelled.nc</c> and <c>.probe.nc</c> files we now write something you can look at.
    /// </summary>
    public bool OpenProgram(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not read '{path}': {ex.Message}";
            return false;
        }

        var parsed = GcodeParser.Parse(text);

        if (parsed.Moves.Count == 0)
        {
            StatusMessage = $"{Path.GetFileName(path)} has no motion in it.";
            return false;
        }

        var classified = GcodeBackplot.Classify(parsed);
        var measured = GcodeBackplot.Measure(classified, Settings.Machine.Profile);

        // Drawn against the board when there is one, so a program opened over the board it came
        // from lands on it. On its own it stands in work coordinates, which is where it was
        // written.
        //
        // Against the *frame* rather than the board, because a project with a blank writes its
        // programs to the blank's corner — and a file opened over the board it came from has to
        // land where it was cut, not where the artwork happens to sit.
        var shift = _board is null
            ? Point2.Origin
            : new Point2(Frame.MinX, Frame.MinY);

        _backplot = BackplotBuilder.Build(classified, shift);
        _programBounds = WithAir(parsed.Bounds);
        _programPath = path;

        Gcode = text;
        GcodeSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)} · {measured.CutMm:F0} mm cut · {measured.TravelMm:F0} mm travel "
            + $"· {measured.PlungeCount} plunges · {parsed.LineCount:N0} lines");

        Rebuild(TimeSpan.Zero);
        OnPropertyChanged(nameof(HasProgram));
        OnPropertyChanged(nameof(ShowingNothing));

        Warnings.Clear();

        // Anything the parser could not make sense of. On somebody else's file this is the useful
        // part: it says which lines are not being drawn, so an empty-looking picture has a reason.
        foreach (var diagnostic in parsed.Diagnostics.Take(20))
        {
            Warnings.Add(diagnostic.ToString());
        }

        if (measured.GougeCount > 0)
        {
            Warnings.Add($"{measured.GougeCount} rapid move(s) at cutting depth. Do not run this.");
        }

        StatusMessage = measured.GougeCount > 0
            ? $"{Path.GetFileName(path)}: {measured.GougeCount} rapid move(s) at cutting depth — do not run this."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Showing {Path.GetFileName(path)} · {measured.TimeRange()}");

        return true;
    }

    /// <summary>
    /// A little space around a program's own extents.
    ///
    /// A board is framed by its outline and the copper sits inside that, so fitting to a board
    /// leaves room by itself. A program has no such frame — its extent *is* the outermost thing it
    /// draws — so fitted raw it sits against the edges of the window, with the stroke width of the
    /// outermost move half over the side.
    /// </summary>
    private static Bounds WithAir(Bounds bounds)
    {
        if (bounds.IsEmpty)
        {
            return bounds;
        }

        var air = Math.Max(Nm.FromMillimetres(1), (long)(Math.Max(bounds.Width, bounds.Height) * 0.03));

        return new Bounds(
            bounds.MinX - air, bounds.MinY - air,
            bounds.MaxX + air, bounds.MaxY + air);
    }

    /// <summary>Puts the viewport back to whatever board is loaded.</summary>
    public void CloseProgram()
    {
        if (_programPath is null)
        {
            return;
        }

        ForgetProgram();
        Rebuild(TimeSpan.Zero);
        OnPropertyChanged(nameof(HasProgram));
        OnPropertyChanged(nameof(ShowingNothing));
        StatusMessage = "Closed the program.";
    }

    private void ForgetProgram()
    {
        _backplot = [];
        _programBounds = null;
        _programPath = null;
        Gcode = null;
        GcodeSummary = string.Empty;
        Warnings.Clear();
    }

    // ------------------------------------------------------------------ height mapping

    /// <summary>
    /// The measured surface of the stock, once a probe log has been imported.
    ///
    /// Held on the view model rather than in the project because it belongs to the *setup*, not to
    /// the design: it describes the piece of FR4 currently clamped to the table, and it stops being
    /// true the moment that piece is unclamped. Saving it into a project would invite someone to
    /// reuse it a week later on a different board, which is worse than not having it.
    /// </summary>
    public HeightMap? Surface { get; private set; }

    /// <summary>What to say about the imported surface, or null when there is none.</summary>
    public string? SurfaceSummary => Surface is not { } map
        ? null
        : string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{map.PointCount} probe points · {map.RangeMm:F3} mm out of flat");

    /// <summary>Writes a probing routine for the loaded board.</summary>
    public bool WriteProbeRoutine(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_board is null)
        {
            return false;
        }

        try
        {
            // Over the board, in the frame the programs are written in. On a blank the grid still
            // covers only the board — the blank is cut through before anything is probed and needs no
            // map — but its coordinates are measured from the blank's corner. From the board's corner
            // instead, the map sits a border's width from where every correction is applied.
            var frame = Frame;
            var onBlank = frame != _board.Bounds;

            var (text, report) = ProbeRoutine.Generate(_board.Bounds, new ProbeRoutineOptions
            {
                WorkZero = new Point2(frame.MinX, frame.MinY),
                SpacingMm = Settings.Probe.SpacingMm,
                FeedMmPerMin = Settings.Probe.FeedMmPerMin,
                MaxDepthMm = Settings.Probe.MaxDepthMm,
                MarginMm = Settings.Probe.MarginMm,
                MaxPoints = Settings.Probe.MaxPoints,
                SafeHeightMm = Settings.Machine.SafeZMm > 5 ? Settings.Machine.SafeZMm : 5,
                OnBlank = onBlank,
            });
            File.WriteAllText(path, text);

            var minutes = report.EstimatedSeconds / 60;

            StatusMessage = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Wrote a {report.Columns} x {report.Rows} probing grid ({report.PointCount} touches, "
                + $"about {minutes:F0} min{(onBlank ? ", zeroed on the stock's corner" : string.Empty)}) to {path}.");

            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not write '{path}': {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Whether Job › Cut stock to size… has something to write: a board, built on a blank that the
    /// mill cuts. A declared blank is stock already on the table, and has no program.
    /// </summary>
    public bool CanWriteBlank => _board is not null && UseBlank && CutTheBlank;

    /// <summary>The name the blank program is offered under, without its extension.</summary>
    public string BlankProgramName => _board is null
        ? "stock"
        : Path.GetFileNameWithoutExtension(ExportPlanner.BlankFileName(_board));

    /// <summary>
    /// Writes just the program that cuts the blank — the same file a full export puts first.
    ///
    /// The blank is cut on its own schedule: first, and often before the rest of the job is ready.
    /// Planning every layer to write one rectangle would make that wait on the slowest layer.
    /// </summary>
    public bool WriteBlankProgram(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_board is null)
        {
            return false;
        }

        RecordOutputs();

        var settings = _project.Settings.LayerOutputs.ToDictionary(
            o => o.FileName, o => o, StringComparer.Ordinal);

        try
        {
            var (item, blank) = ExportPlanner.PlanBlank(
                _board, settings, Library, Nm.FromMillimetres(BoardThicknessMm),
                Framing, Settings.Machine, _project.Settings.Job);

            if (item is null)
            {
                // Said, not silently skipped: the item can be reached a moment after the border was
                // narrowed below what the cutter needs, and then the refusal is the answer.
                StatusMessage = blank.Refusals.Count > 0
                    ? "Nothing to cut: " + string.Join(" ", blank.Refusals)
                    : "Nothing to cut: this project is not built on stock that the mill cuts to size.";
                return false;
            }

            File.WriteAllText(path, item.Content);

            StatusMessage = string.Create(
                CultureInfo.InvariantCulture,
                $"Wrote {Path.GetFileName(path)}: a {Nm.ToMillimetreString(blank.Bounds.Width, 2)} × "
                + $"{Nm.ToMillimetreString(blank.Bounds.Height, 2)} mm stock, cut with the "
                + $"{ExportPlanner.OutlineCutter(_board, settings, Library).Name}.");

            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not write '{path}': {ex.Message}";
            return false;
        }
    }

    // ------------------------------------------------------------------ drill alignment

    /// <summary>
    /// The drilling and routing files the current export would write — what Job › Drill alignment
    /// offers to test with, and what it writes again with the offset.
    /// </summary>
    public IReadOnlyList<ExportItem> DrillFiles() => PlanExport(OutputKind.Gcode) is { } plan
        ? [.. plan.Items.Where(ExportPlanner.IsDrillOrRouting)]
        : [];

    /// <summary>The offsets last typed into the alignment dialog, kept for the session so reopening it keeps them.</summary>
    public double AlignmentXMm { get; set; }

    /// <inheritdoc cref="AlignmentXMm"/>
    public double AlignmentYMm { get; set; }

    /// <summary>Whether the aligned files include the board outline. On unless unticked this session.</summary>
    public bool AlignmentOutline { get; set; } = true;

    /// <inheritdoc cref="AlignmentXMm"/>
    public double AlignmentSecondXMm { get; set; }

    /// <inheritdoc cref="AlignmentXMm"/>
    public double AlignmentSecondYMm { get; set; }

    /// <summary>Whether a second hole was measured this session, which is what finds the rotation.</summary>
    public bool AlignmentUseSecond { get; set; }

    /// <summary>The alignment saved with this project, or null if this board has never had one found.</summary>
    public AlignmentRecord? SavedAlignment => _project.Settings.Alignment;

    /// <summary>
    /// Keeps the alignment with the project, so the next session starts from what the machine already
    /// told this board rather than from zero.
    /// </summary>
    private void RememberAlignment(DrillAlignment alignment)
    {
        _project.Settings = _project.Settings with
        {
            Alignment = AlignmentRecord.From(alignment, AlignmentFlipped),
        };

        _project.Touch();
    }

    /// <summary>
    /// The stock's own program, when this job builds on stock — where the waste holes are read from.
    ///
    /// From the emitted program, like every other hole the dialog offers, so the coordinates are the
    /// ones the machine will be sent to rather than the ones the planner started from.
    /// </summary>
    public ExportItem? StockProgram() => PlanExport(OutputKind.Gcode) is { } plan
        ? plan.Items.FirstOrDefault(i => i.LayerFileName == ExportPlanner.StockLayer)
        : null;

    /// <summary>
    /// Where the stock's alignment holes are in its program's own coordinates.
    ///
    /// Used to pick them out of that program: the stock cuts its perimeter below the surface too, and
    /// on square stock that reads as one more round feature. The coordinates still come from the
    /// emitted program — these say which of its features are the holes.
    /// </summary>
    public IReadOnlyList<Point2> WasteHoles()
    {
        if (PlanExport(OutputKind.Gcode) is not { } plan || _board is null || !plan.Blank.Resolved)
        {
            return [];
        }

        var frame = plan.FrameFor(_board.Bounds);

        return [.. plan.Blank.AlignmentHoles.Select(h => new Point2(h.X - frame.MinX, h.Y - frame.MinY))];
    }

    /// <summary>Every G-code program this export would write, as Drill alignment offers them.</summary>
    public IReadOnlyList<MovableProgram> MovablePrograms() => PlanExport(OutputKind.Gcode) is { } plan
        ? ExportPlanner.Movable(plan)
        : [];

    /// <summary>
    /// How wide the frame every program is referenced to is: the stock's, or the board's without one.
    ///
    /// The axis a flipped board mirrors about. A hole at X in the program is at this width minus X
    /// once the stock is turned over left-to-right and put back in the same corner.
    /// </summary>
    public long FrameWidthNm() => PlanExport(OutputKind.Gcode) is { } plan && _board is { } board
        ? plan.FrameFor(board.Bounds).Width
        : 0;

    /// <summary>What the operator last chose to move, or null while the default rule stands.</summary>
    public System.Collections.Immutable.ImmutableArray<string>? AlignmentMoved { get; set; }

    /// <summary>Whether the alignment is being measured with the board flipped over.</summary>
    public bool AlignmentFlipped { get; set; }

    /// <summary>Whether the holes being measured are the stock's waste holes rather than the board's.</summary>
    public bool AlignmentWasteHoles { get; set; }

    /// <summary>Whether this export has a board outline program for the alignment to move.</summary>
    public bool HasOutlineProgram() => PlanExport(OutputKind.Gcode) is { } plan
        && plan.Items.Any(ExportPlanner.IsBoardOutline);

    /// <summary>Remembers the alignment test's hover height, which is a setting rather than a per-job number.</summary>
    public void SaveAlignHover(double hoverMm) =>
        SaveSettings(Settings with { Align = Settings.Align with { HoverMm = hoverMm } });

    /// <summary>Writes the alignment test for one hole of one file, overwriting the last one.</summary>
    public bool WriteAlignmentTest(
        string path, ExportItem file, MillBurn.Gcode.AlignmentTarget target, Point2 offsetNm)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(target);

        try
        {
            var text = MillBurn.Gcode.AlignmentTest.Generate(target, offsetNm, file.TargetName, new MillBurn.Gcode.AlignmentTestOptions
            {
                HoverMm = Settings.Align.HoverMm,
                FeedMmPerMin = Settings.Align.FeedMmPerMin,
                SafeZMm = Settings.Machine.SafeZMm,
                Decimals = Settings.Machine.Decimals,
            });

            File.WriteAllText(path, text);

            StatusMessage = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Wrote {Path.GetFileName(path)}: over {target.Kind} {target.Number} of {file.TargetName}, "
                + $"moved X{MillBurn.Gcode.AlignmentTest.FormatOffset(offsetNm.X)} Y{MillBurn.Gcode.AlignmentTest.FormatOffset(offsetNm.Y)} mm.");

            return true;
        }
        catch (Exception ex) when (IsExpected(ex) || ex is ArgumentOutOfRangeException)
        {
            StatusMessage = $"Could not write '{path}': {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Writes every drilling and routing file again, moved by the alignment, under the aligned names,
    /// with their pages. Returns how many files were written.
    /// </summary>
    public int WriteAlignedFiles(string folder, DrillAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(alignment);

        if (PlanExport(OutputKind.Gcode, alignment: alignment) is not { } plan)
        {
            return 0;
        }

        var files = plan.Items.Where(alignment.Moves).ToList();

        if (files.Count == 0)
        {
            StatusMessage = "Nothing to align: no drilling, routing or outline files in this export.";
            return 0;
        }

        try
        {
            Directory.CreateDirectory(folder);
            var written = 0;

            foreach (var item in files)
            {
                File.WriteAllText(Path.Combine(folder, item.TargetName), item.Content);
                written++;

                if (item.Companion is { } page)
                {
                    File.WriteAllText(Path.Combine(folder, page.TargetName), page.Content);
                    written++;
                }
            }

            var turn = alignment.IsShiftOnly
                ? string.Empty
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $" and turned {alignment.RotationDegrees:0.####}°");

            var moved = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"moved X{MillBurn.Gcode.AlignmentTest.FormatOffset(alignment.XNm)} Y{MillBurn.Gcode.AlignmentTest.FormatOffset(alignment.YNm)} mm{turn}");

            RememberAlignment(alignment);

            StatusMessage = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Wrote {written} aligned file(s), {moved}, to {folder}. Run the .aligned files instead of the originals.");

            return written;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not write to '{folder}': {ex.Message}";
            return 0;
        }
    }

    /// <summary>Reads a sender's probe log and keeps the surface it describes.</summary>
    public bool ImportHeightMap(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not read '{path}': {ex.Message}";
            return false;
        }

        var (map, log) = ProbeLog.Read(
            text, new HeightMapOptions { Smoothing = Settings.Level.Smoothing });

        if (map is null)
        {
            // Why it failed, and — just as important — what the app is still holding. A refused
            // import leaves the previous surface in place, so "Forget height map" stays enabled
            // and the export dialog still offers levelling. Without this sentence that reads as
            // the import having worked.
            ImportProblem = log.Rejection ?? "Nothing in that file is probe data.";

            if (Surface is { } kept)
            {
                ImportProblem += string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $" The map you imported before is still loaded — {kept.PointCount} points, "
                    + $"{kept.RangeMm:F3} mm out of flat — and exports will still use it.");
            }

            StatusMessage = ImportProblem;
            return false;
        }

        ImportProblem = null;

        Surface = map;
        OnPropertyChanged(nameof(Surface));
        OnPropertyChanged(nameof(HasSurface));
        OnPropertyChanged(nameof(SurfaceSummary));
        OnPropertyChanged(nameof(SurfaceProblem));

        var notes = log.Notes.Concat(map.Notes).FirstOrDefault();

        var summary = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Imported {map.PointCount} probe points: {map.RangeMm:F3} mm out of flat.");

        StatusMessage = notes is null ? summary : summary + " " + notes;

        return true;
    }

    public bool HasSurface => Surface is not null;

    /// <summary>
    /// Why the last import was refused, or null when the last one worked.
    ///
    /// Held on the view model rather than raised from the import itself so that the headless
    /// startup path and the menu item both end up with the same sentence, and so a test can read
    /// it without a dialog being on screen.
    /// </summary>
    public string? ImportProblem { get; private set; }

    /// <summary>
    /// Why the imported surface cannot be used for this board, or null when it can.
    ///
    /// Checked here rather than left to the leveller's own refusal, because by then the operator
    /// has already picked a folder and pressed the button. A map probed for a different board is
    /// the easy mistake to make — the file sits there between sessions and nothing about it says
    /// which piece of stock it measured.
    /// </summary>
    public string? SurfaceProblem
    {
        get
        {
            if (Surface is not { } map || _board is null)
            {
                return null;
            }

            // The board in work coordinates: offset from the frame's corner, which is the origin of
            // every export — the blank's when there is one. A map probed before the blank existed,
            // or by a routine that ignored it, sits a border's width off and is caught here.
            var frame = Frame;
            var x = _board.Bounds.MinX - frame.MinX;
            var y = _board.Bounds.MinY - frame.MinY;

            var corners = new[]
            {
                new Point2(x, y),
                new Point2(x + _board.Bounds.Width, y),
                new Point2(x, y + _board.Bounds.Height),
                new Point2(x + _board.Bounds.Width, y + _board.Bounds.Height),
            };

            var outside = corners.Max(map.OutsideByMm);

            return outside <= 3
                ? null
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"This map covers {Nm.ToMillimetreString(map.Bounds.Width, 1)} x "
                    + $"{Nm.ToMillimetreString(map.Bounds.Height, 1)} mm — the board runs "
                    + $"{outside:F0} mm outside it. Probe this board.");
        }
    }

    public void ForgetHeightMap()
    {
        Surface = null;
        ImportProblem = null;
        OnPropertyChanged(nameof(Surface));
        OnPropertyChanged(nameof(HasSurface));
        OnPropertyChanged(nameof(SurfaceSummary));
        OnPropertyChanged(nameof(SurfaceProblem));
        StatusMessage = "Height map cleared. Exports will not be levelled.";
    }

    // ------------------------------------------------------------------ export

    /// <summary>
    /// Works out every file this export would write, without writing any of them.
    ///
    /// Planning and writing stay separate because the check is the point: which layer became which
    /// file, what tool it assumes, and whether anything about the combination is wrong.
    ///
    /// <paramref name="filter"/> is for Preview, which only ever wants the G-code. An export takes
    /// no filter: what each layer produces is that layer's own setting, said on its own row, and a
    /// second control that could disagree with six rows at once is one answer too many.
    /// </summary>
    public ExportPlan? PlanExport(
        OutputKind? filter = null, string? onlyLayer = null, DrillAlignment? alignment = null)
    {
        if (_board is null)
        {
            return null;
        }

        RecordOutputs();

        var settings = _project.Settings.LayerOutputs.ToDictionary(
            o => o.FileName, o => o, StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            _board, settings, Library, Nm.FromMillimetres(BoardThicknessMm), filter,
            framing: Framing, machineSettings: Settings.Machine, job: _project.Settings.Job, alignment: alignment);

        if (onlyLayer is null)
        {
            return plan;
        }

        // "Export only this layer" narrows the *plan*, not the project. It used to set every other
        // layer to Not exported and keep a snapshot to undo with, which is a state machine with
        // edges: change a third layer while one is isolated and the snapshot describes a board that
        // no longer exists. Filtering one plan has no such state — the settings are untouched, so
        // there is nothing to put back.
        return plan with
        {
            Items = [.. plan.Items.Where(i => string.Equals(i.LayerFileName, onlyLayer, StringComparison.Ordinal))],
            Skipped = [],
        };
    }

    /// <summary>
    /// Writes the plan, optionally with a dry run of each program beside it.
    ///
    /// The dry runs are built from the emitted text, not from the toolpaths, so what you watch in
    /// the air is the file you are about to run — the same reasoning as the backplot.
    /// </summary>
    public bool WriteExport(
        ExportPlan plan,
        string folder,
        bool dryRun = false,
        bool level = false,
        bool levelFlipped = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(folder);

        try
        {
            Directory.CreateDirectory(folder);
            var pages = 0;

            foreach (var item in plan.Items)
            {
                File.WriteAllText(Path.Combine(folder, item.TargetName), item.Content);

                if (item.Companion is { } guide)
                {
                    File.WriteAllText(Path.Combine(folder, guide.TargetName), guide.Content);
                    pages++;
                }
            }

            // One page for the whole export, beside the files it describes.
            if (plan.Page is { } overview)
            {
                File.WriteAllText(Path.Combine(folder, overview.TargetName), overview.Content);
                pages++;
            }

            var extra = pages;

            // Collected as a file and a reason rather than as finished sentences, so identical
            // reasons can be said once with every file that shares them named alongside.
            var refusals = new List<(string What, string File, string Reason)>();

            if (level && Surface is { } surface)
            {
                // Not the blank: it is cut through before anything is probed. See Levellable.
                foreach (var item in plan.Items.Where(i => i.Output == OutputKind.Gcode && i.Levellable))
                {
                    // A map measured before the stock was turned over describes the other face, in
                    // coordinates that have since been mirrored. One export cannot level both
                    // sides from one map, and the side it cannot level is the flipped one.
                    if (Leveller.WhyNotLevel(item.Mirrored, levelFlipped) is { } why)
                    {
                        refusals.Add(("Not levelled", item.TargetName, why));
                        continue;
                    }

                    var (text, report) = Leveller.Apply(item.Content, surface, new LevelOptions
                    {
                        SegmentMm = Settings.Level.SegmentMm,
                        SubdivideBelowMm = Settings.Level.SubdivideBelowMm,
                        MaxOutsideMm = Settings.Level.MaxOutsideMm,
                    });

                    if (report.Refusal is not null)
                    {
                        refusals.Add(("Not levelled", item.TargetName, report.Refusal));
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(item.TargetName)
                        + ".levelled" + Path.GetExtension(item.TargetName);

                    File.WriteAllText(Path.Combine(folder, name), text);
                    extra++;
                }
            }

            if (dryRun)
            {
                foreach (var item in plan.Items.Where(i => i.Output == OutputKind.Gcode))
                {
                    var (text, report) = DryRun.Rewrite(item.Content, new DryRunOptions
                    {
                        HeightMm = Settings.DryRun.HeightMm,
                        KeepFeeds = Settings.DryRun.KeepFeeds,
                        RapidMmPerMin = Settings.Machine.RapidMmPerMin,
                    });

                    // A refusal is not a failure of the export: the real program is written and
                    // correct. It only means this one could not be traced in the air, and saying
                    // so is far better than writing a dry run that might not be one.
                    if (report.Refusal is not null)
                    {
                        refusals.Add(("No dry run", item.TargetName, report.Refusal));
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(item.TargetName)
                        + ".dryrun" + Path.GetExtension(item.TargetName);

                    File.WriteAllText(Path.Combine(folder, name), text);
                    extra++;
                }
            }

            SaveSettings(Settings with { LastExportFolder = folder, WriteDryRun = dryRun });

            ReplaceExportWarnings(refusals);

            StatusMessage = refusals.Count > 0
                ? $"Wrote {plan.Count + extra} file(s) to {folder}. {refusals.Count} thing(s) refused — see the checks."
                : $"Wrote {plan.Count + extra} file(s) to {folder}.";

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

        // The frame the plan used, not the board's own corner. With a blank the programs are
        // written to the blank's lower-left, so undoing the board's shift instead draws every
        // toolpath a border's width up and to the right of the copper it cuts — a picture that is
        // wrong in a way the file is not, which is the worst kind of wrong a viewer can be.
        var frame = plan.FrameFor(_board.Bounds);
        var shift = new Point2(frame.MinX, frame.MinY);
        var programs = new List<BackplotBuilder.Program>();
        var cut = 0.0;
        var travel = 0.0;
        var plunges = 0;
        var gouges = 0;

        foreach (var item in plan.Items)
        {
            var classified = GcodeBackplot.Classify(GcodeParser.Parse(item.Content));
            var measured = GcodeBackplot.Measure(classified, Settings.Machine.Profile);

            // Kept apart by source layer rather than poured into one list. Merged, the viewer can
            // only ever show every program's cuts at once — and looking at one layer's toolpath is
            // the reason to open a backplot at all.
            programs.Add(new BackplotBuilder.Program(
                item.LayerFileName, item.LayerLabel, classified, item.Mirrored));

            cut += measured.CutMm;
            travel += measured.TravelMm;
            plunges += measured.PlungeCount;
            gouges += measured.GougeCount;
        }

        Gcode = string.Join("\n", plan.Items.Select(i => i.Content));
        // A mirrored program is written for the flipped stock, so it is flipped back for the
        // drawing: what the picture is being asked is where the cuts land on *this* board, and a
        // bottom-copper path drawn straight lands on the mirror image of the traces it isolates.
        _backplot = BackplotBuilder.BuildPerProgram(
            programs, shift, mirrorSumXNm: frame.MinX + frame.MaxX);

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

        // A board layer is identified by its role, so the choice follows that role across every
        // project. A layer the scene invented has no role, so it is identified by its own id.
        SaveSettings(row.Role is { } role
            ? Settings.WithColour(role, hex)
            : Settings.WithSceneColour(row.Id, hex));

        Rebuild(TimeSpan.Zero);
    }

    /// <summary>Puts one layer back to the built-in palette.</summary>
    public void ResetColour(LayerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        SaveSettings(row.Role is { } role
            ? Settings.WithoutColour(role)
            : Settings.WithoutSceneColour(row.Id));

        Rebuild(TimeSpan.Zero);
    }

    /// <summary>Puts every layer back to the built-in palette.</summary>
    public void ResetColours()
    {
        SaveSettings(Settings with
        {
            LayerColours = System.Collections.Immutable.ImmutableDictionary<LayerRole, string>.Empty,
            SceneColours = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
        });

        Rebuild(TimeSpan.Zero);
        StatusMessage = "Layer colours reset.";
    }

    /// <summary>
    /// The same three operations for a move-kind chip. These carry no role — there is no file
    /// behind a rapid — so they are identified by the colour key the palette already saves under.
    /// </summary>
    public void SetColour(MoveKindRow kind, Color colour)
    {
        ArgumentNullException.ThrowIfNull(kind);

        SaveSettings(Settings.WithSceneColour(kind.Id, $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}"));
        Rebuild(TimeSpan.Zero);
    }

    public void ResetColour(MoveKindRow kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        SaveSettings(Settings.WithoutSceneColour(kind.Id));
        Rebuild(TimeSpan.Zero);
    }

    public static Color ColourOf(MoveKindRow kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        return kind.Swatch is SolidColorBrush brush ? brush.Color : Colors.Gray;
    }

    public Color ColourOf(LayerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Role is { } role)
        {
            var fill = Palette(role).Fill;
            return Color.FromArgb(0xFF, fill.Red, fill.Green, fill.Blue);
        }

        // A scene layer's swatch already carries whatever it is currently drawn in, override
        // included, which is exactly what the picker should open on.
        return row.Swatch is SolidColorBrush brush ? brush.Color : Colors.Gray;
    }

    private BoardLayerStyle Palette(LayerRole role)
    {
        var style = BoardPalette.For(role);

        return Settings.LayerColours.TryGetValue(role, out var hex) && SKColor.TryParse(hex, out var colour)
            ? style with { Fill = colour }
            : style;
    }

    /// <summary>A scene layer's style, with the operator's override applied if there is one.</summary>
    private BoardLayerStyle SceneStyle(string id, BoardLayerStyle fallback) =>
        Settings.SceneColours.TryGetValue(id, out var hex) && SKColor.TryParse(hex, out var colour)
            ? fallback with { Fill = colour }
            : fallback;

    /// <summary>
    /// Applies overrides to a backplot before it reaches the scene.
    ///
    /// These layers are the ones most likely to need it: the palette picks hues the board does not
    /// use, but "does not use" depends on which layers are showing and on the monitor in front of
    /// the operator.
    /// </summary>
    private IReadOnlyList<BackplotLayer> Recoloured(IReadOnlyList<BackplotLayer> layers) =>
        Settings.SceneColours.IsEmpty
            ? layers
            : [.. layers.Select(l => l with { Style = SceneStyle(l.Palette, l.Style) })];

    public void ReloadLibrary()
    {
        Library = ToolLibrary.LoadOrDefault();

        // The job options offer a list *derived* from the library, so a tool bought and entered
        // between two exports has to reach them. Without this the cutter list is whatever the
        // library held when the project was opened, and a freshly added end mill is missing from
        // the one place somebody just went looking for it.
        //
        // The selection is re-resolved rather than kept: a tool is matched by id, and an id that is
        // no longer in the library has to clear rather than hold a stale object that nothing else
        // in the app can see any more.
        ApplyJobOptions();

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
        ForgetProgram();

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
            _board = null;
            HasBoard = false;
            CanRefresh = false;

            // With no board, a program opened on its own is the whole picture, and its own extents
            // are the only frame there is to draw it in.
            if (_backplot.Count > 0 && _programBounds is { } extent)
            {
                Scene = BoardSceneBuilder.Build(
                    [],
                    extent,
                    Palette,
                    Recoloured(_backplot),
                    SceneStyle(BoardSceneBuilder.SubstrateId, BoardPalette.Substrate));

                // Everything on, which is the opposite of the board view's default and right here.
                // Travel is hidden over a board because it clutters the copper; over nothing it is
                // most of the picture — a dry run is *all* travel by construction, and opened with
                // the default it showed two dashed lines and looked broken.
                foreach (var layer in Scene.Layers)
                {
                    layer.Visible = true;
                }

                foreach (var key in _backplot.Select(b => b.Palette).Append(BoardSceneBuilder.SubstrateId))
                {
                    _kindVisible[key] = true;
                }

                previous?.Dispose();
                BuildRows(null, Scene);
                Facts.Clear();
                RefreshTitles();
                return;
            }

            Scene = null;
            previous?.Dispose();
            Layers.Clear();
            BuildGroups();
            MoveKinds.Clear();
            HasMoveKinds = false;
            Warnings.Clear();
            Facts.Clear();
            _backplot = [];
            Gcode = null;
            GcodeSummary = string.Empty;
            RefreshTitles();
            return;
        }

        var hidden = Layers.Where(r => !r.IsVisible).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var board = ProjectFile.ToBoard(_project);
        _board = board;

        // The blank is described against the board's own bounds, and the board only exists here —
        // a project that carries one arrives with its fields filled in and its summary blank until
        // this runs.
        DescribeBlank(_project.Settings.Job.Blank);

        // Fitted to the stock rather than to the board, when there is stock. The board outline is a
        // better answer than the union of whatever happens to be drawn, and the blank is a better
        // answer still: it is the piece on the table, and the cut that makes it lies outside the
        // board — so fitting to the board put the blank's own toolpath just off screen, which is
        // the one thing you would want to look at before running it.
        var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            Frame.IsEmpty ? board.Bounds : Frame,
            Palette,
            _backplot.Count > 0 ? Recoloured(_backplot) : null,
            SceneStyle(BoardSceneBuilder.SubstrateId, BoardPalette.Substrate));

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
    private void BuildRows(Board? board, BoardScene scene)
    {
        // Null when a program is being shown on its own: there are no file-backed layers to match,
        // only the backplot's own.
        var byName = board is null
            ? new Dictionary<string, BoardLayer>(StringComparer.Ordinal)
            : board.Layers.ToDictionary(l => l.FileName, StringComparer.Ordinal);

        // Which scene layer belongs to which program, and to which kind of move. The backplot is
        // built per program now, so both questions have answers and the panel can ask them
        // separately: a row governs *its* layer's paths, a chip governs *a kind* across all of them.
        var bySource = _backplot
            .Where(b => b.Source.Length > 0)
            .GroupBy(b => b.Source, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<BoardSceneLayer>)[.. g.Select(b => scene.Layer(b.Id)).OfType<BoardSceneLayer>()],
                StringComparer.Ordinal);

        // What the panel looked like, before the rows themselves cease to exist.
        //
        // Every rebuild builds new LayerRow objects, so anything held on a row and not in the
        // project is lost — and Preview rebuilds. Opening three layers, pressing Preview and
        // watching all three shut is a small thing that happens on every single iteration of the
        // loop this app is used in. Which toolpaths were showing goes the same way, and for the
        // same reason: you isolate one layer's cuts, change something, press Preview to look at
        // the change, and the thing you were looking at is gone.
        var expanded = Layers.Where(r => r.IsExpanded).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        // Held as the rows that were *off*, so a layer appearing for the first time is on. The same
        // reasoning as the hidden-layer set: the default has to survive, and only the departures
        // from it are worth carrying.
        var pathsOff = Layers.Where(r => !r.ShowToolpath).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        _suspendOutputChanges = true;
        Layers.Clear();

        foreach (var layer in scene.Layers)
        {
            if (!byName.TryGetValue(layer.Id, out var source))
            {
                continue;
            }

            // The project is the only place these live. Keeping a second copy in the view would
            // mean two answers to "what does this layer become", and the one that got saved
            // would be whichever was updated last.
            var settings = _project.Settings.OutputFor(layer.Id)
                ?? new LayerOutputSettings
                {
                    FileName = layer.Id,
                    Output = LayerOperations.DefaultFor(source.Role, Settings.Import),
                    IsolationWidthNm = Settings.Milling.IsolationWidthNm,
                };

            var row = new LayerRow(
                source, layer, settings, Library.Tools, OnLayerVisibilityChanged, OnOutputChanged);

            if (bySource.TryGetValue(layer.Id, out var paths))
            {
                row.AttachToolpath(paths);
            }

            row.IsExpanded = expanded.Contains(layer.Id);
            row.ShowToolpath = !pathsOff.Contains(layer.Id);

            Layers.Add(row);
        }

        BuildMoveKinds(scene);
        ApplyToolpathVisibility();
        BuildGroups();

        _suspendOutputChanges = false;
    }

    /// <summary>
    /// Re-buckets the rows by which part of the board they belong to. The flat list stays the one
    /// everything else works from; this is a view of it, so there is still only one row object per
    /// layer and no way for the two to disagree.
    /// </summary>
    private void BuildGroups()
    {
        foreach (var group in LayerGroups)
        {
            group.Detach();
        }

        LayerGroups.Clear();

        foreach (var group in LayerGroup.Build(Layers))
        {
            LayerGroups.Add(group);
        }

        HasLayerRows = Layers.Count > 0;
        HasMoveKinds = MoveKinds.Count > 0;
    }

    /// <summary>Whether there is a layer list to show at all — a lone .nc file has none.</summary>
    [ObservableProperty]
    public partial bool HasLayerRows { get; set; }

    [ObservableProperty]
    public partial bool HasMoveKinds { get; set; }

    /// <summary>
    /// The chips: one per kind of move, plus the substrate.
    ///
    /// Built from the scene rather than from a fixed list, so a kind that this job never produced —
    /// a gouge, usually — does not sit there as a control for nothing.
    /// </summary>
    private void BuildMoveKinds(BoardScene scene)
    {
        MoveKinds.Clear();

        // Not filtered to per-layer programs: a .nc opened on its own has no layer behind it at
        // all, and the chips are then the only control it has.
        foreach (var group in _backplot.GroupBy(b => b.Palette, StringComparer.Ordinal))
        {
            var layers = group
                .Select(b => scene.Layer(b.Id))
                .OfType<BoardSceneLayer>()
                .ToList();

            if (layers.Count == 0)
            {
                continue;
            }

            var first = group.First();

            MoveKinds.Add(new MoveKindRow(
                group.Key,
                // "Top copper · Cutting moves" carries the layer's name, which a chip must not.
                first.Label[(first.Label.LastIndexOf('·') + 1)..].Trim(),
                layers,
                LayerRow.ToBrush(SceneStyle(group.Key, first.Style).Fill),
                _kindVisible.TryGetValue(group.Key, out var showing) ? showing : first.VisibleByDefault,
                OnToolpathFilterChanged));
        }

        // The substrate is not a move, but it is the same kind of thing as these: a drawing-wide
        // switch with no file behind it and nothing to export.
        if (scene.Layer(BoardSceneBuilder.SubstrateId) is { } substrate)
        {
            MoveKinds.Add(new MoveKindRow(
                BoardSceneBuilder.SubstrateId,
                "Substrate",
                [substrate],
                LayerRow.ToBrush(substrate.Style.Fill),
                _kindVisible.TryGetValue(BoardSceneBuilder.SubstrateId, out var on) ? on : substrate.Visible,
                OnToolpathFilterChanged));
        }

        RememberKinds();
    }

    private void RememberKinds()
    {
        foreach (var kind in MoveKinds)
        {
            _kindVisible[kind.Id] = kind.IsVisible;
        }
    }

    /// <summary>
    /// Combines the two axes onto the scene.
    ///
    /// A toolpath layer is drawn when its own layer's paths are on **and** its kind of move is on.
    /// Neither axis alone is enough, which is exactly why they are two controls and not one.
    /// </summary>
    private void ApplyToolpathVisibility()
    {
        // Which row owns which drawn path. The substrate has no owner, and neither does a program
        // opened on its own — both are governed by their chip alone, which is the same rule with
        // one of the two terms missing rather than a second rule.
        var owner = new Dictionary<string, LayerRow>(StringComparer.Ordinal);

        foreach (var row in Layers)
        {
            foreach (var layer in row.ToolpathLayers)
            {
                owner[layer.Id] = row;
            }
        }

        foreach (var kind in MoveKinds)
        {
            foreach (var layer in kind.Layers)
            {
                layer.Visible = kind.IsVisible
                    && (!owner.TryGetValue(layer.Id, out var row) || row.ShowToolpath);
            }
        }
    }

    private void OnToolpathFilterChanged()
    {
        RememberKinds();
        ApplyToolpathVisibility();
        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Turns every layer's paths on or off at once.</summary>
    public void ShowAllToolpaths(bool visible)
    {
        foreach (var row in Layers)
        {
            row.ShowToolpath = visible;
        }
    }

    /// <summary>
    /// Shows one layer's paths and nobody else's — the gesture behind "let me look at this cut".
    /// </summary>
    public void OnlyToolpath(LayerRow only)
    {
        ArgumentNullException.ThrowIfNull(only);

        foreach (var row in Layers)
        {
            row.ShowToolpath = ReferenceEquals(row, only);
        }
    }

    /// <summary>
    /// Puts every layer back to what a freshly imported board starts with.
    ///
    /// The whole per-layer record, not merely the output kind: tool, depth, passes, tabs, mirror,
    /// break-through and the rest all go back to the value the import would have given them, which
    /// is what "as if I had just opened this folder" has to mean to be worth having.
    ///
    /// Board-level choices are deliberately left alone — thickness describes the stock in your hand
    /// and colours describe your eyes, and neither becomes untrue because the layer settings did.
    /// </summary>
    public void ResetLayerSettings()
    {
        if (_board is null)
        {
            return;
        }

        var settings = _project.Settings;

        foreach (var layer in _board.Layers)
        {
            settings = settings.WithOutput(new LayerOutputSettings
            {
                FileName = layer.FileName,
                Output = LayerOperations.DefaultFor(layer.Role, Settings.Import),
                IsolationWidthNm = Settings.Milling.IsolationWidthNm,
            });
        }

        _project.Settings = settings;
        _project.Touch();

        // The programs were made from the settings that just changed, so they are no longer a
        // picture of anything. Cleared rather than left to look current.
        _backplot = [];
        Gcode = null;
        GcodeSummary = string.Empty;

        Rebuild(TimeSpan.Zero);

        StatusMessage = "Every layer is back to what a freshly imported board starts with.";
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
            var sizes = facts.HoleSizes == 1
                ? "1 size"
                : string.Create(CultureInfo.InvariantCulture, $"{facts.HoleSizes} sizes");

            Facts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{facts.Holes} holes in {sizes}, {facts.SmallestHoleMm:F2}–{facts.LargestHoleMm:F2} mm"));
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
        ApplyToolpathVisibility();
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

        RecordOutputs();
        _project.Touch();

        // The blank's line names the outline's bit, so picking a different bit on that row has to
        // reach it.
        DescribeBlank(_project.Settings.Job.Blank);

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

    /// <summary>
    /// What the last export put in the checks panel.
    ///
    /// Held so the next export can take it back out. Without that, exporting twice leaves both
    /// sets on screen — and when the two runs disagreed about which side the map was probed on,
    /// the panel showed each file refused for two opposite reasons at once. Both were true when
    /// they were written and only one of them still was.
    /// </summary>
    private readonly List<string> _exportWarnings = [];

    /// <summary>
    /// Puts this export's refusals in the checks panel, in place of the last one's.
    ///
    /// **Grouped by reason, with the files named.** A refusal explains itself in a paragraph,
    /// which is right once and unreadable five times: an export that could not level four
    /// programs used to print the same ninety words four times over, filling the panel and
    /// pushing the layer list off the screen. The panel earns attention by being short enough to
    /// read, and a wall of repeated text is how it stops being read at all.
    /// </summary>
    private void ReplaceExportWarnings(List<(string What, string File, string Reason)> refusals)
    {
        foreach (var stale in _exportWarnings)
        {
            Warnings.Remove(stale);
        }

        _exportWarnings.Clear();

        foreach (var group in refusals.GroupBy(r => (r.What, r.Reason)))
        {
            var files = string.Join(", ", group.Select(r => r.File));
            var line = $"{group.Key.What}: {files} — {group.Key.Reason}";

            _exportWarnings.Add(line);
            Warnings.Add(line);
        }
    }

    /// <summary>
    /// Copies every layer's settings into the project.
    ///
    /// Whole rather than incremental: a layer whose output changed can also have had its tool
    /// swapped underneath it, and writing only the field that raised the event is how the two
    /// drift apart.
    /// </summary>
    private void RecordOutputs()
    {
        var settings = _project.Settings;

        foreach (var row in Layers.Where(r => r.Layer is not null))
        {
            settings = settings.WithOutput(row.ToSettings());
        }

        // The thickness on screen is the project's, and goes into it with everything else — on
        // every change and on save, so a project that never recorded one gains it the first time
        // it is saved.
        _project.Settings = settings with { BoardThicknessMm = BoardThicknessMm };
        DescribeThickness();
    }

    partial void OnBoardThicknessMmChanged(double value)
    {
        // Showing an opened project's own thickness is neither a change to that project nor a new
        // default for the next one.
        if (_loadingJob)
        {
            return;
        }

        // Still remembered app-wide, but only as where the next new board starts.
        SaveSettings(Settings with { BoardThicknessMm = value });
        OnOutputChanged();
    }

    /// <summary>
    /// Said under the thickness slider while the project has no thickness of its own: a project saved
    /// before it was kept, or a board just imported. The number shown then is only the last one set,
    /// which is exactly the value that used to cut a new board to the old board's depth.
    /// </summary>
    [ObservableProperty]
    public partial string ThicknessNote { get; set; } = string.Empty;

    private void DescribeThickness() =>
        ThicknessNote = _project.Sources.Length > 0 && _project.Settings.BoardThicknessMm is null
            ? "Not saved in this project yet: this is the last thickness you set. Check it; saving keeps it with the project."
            : string.Empty;

    // ------------------------------------------------------------------ job options

    /// <summary>
    /// Spiral out holes no drill in the library can make.
    ///
    /// Project-level, and saved with the project: whether a 3.2 mm hole should be milled is a
    /// question about this board and what the operator is willing to have happen to it, not about
    /// the machine — the same machine cuts the next board where the answer differs.
    /// </summary>
    [ObservableProperty]
    public partial bool MillLargeHoles { get; set; }

    /// <summary>Which end mill spirals them, or null for the widest in the library that fits.</summary>
    [ObservableProperty]
    public partial Tool? MillDrillTool { get; set; }

    /// <summary>The end mills available to choose from, plus a null entry meaning "pick for me".</summary>
    public IReadOnlyList<Tool?> MillDrillTools =>
        [null, .. Library.OfKind(ToolKind.EndMill).OrderBy(t => t.DiameterNm)];

    /// <summary>
    /// True while a project's own settings are being read into the view model.
    ///
    /// Without it, showing a project's saved options marks the project dirty: the setter fires the
    /// handler, the handler writes the value back and calls Touch, and a project nobody has
    /// touched asks to be saved on close.
    /// </summary>
    private bool _loadingJob;

    partial void OnMillLargeHolesChanged(bool value)
    {
        if (_loadingJob)
        {
            return;
        }

        _project.Settings = _project.Settings with
        {
            Job = _project.Settings.Job with { MillLargeHoles = value },
        };

        _project.Touch();
        OnOutputChanged();
    }

    partial void OnMillDrillToolChanged(Tool? value)
    {
        // Choosing the tool the project already has is not a change. Said explicitly because a
        // combo re-selects its item whenever its list is rebuilt, and each of those used to count.
        if (_loadingJob || value?.Id == _project.Settings.Job.MillDrillToolId)
        {
            return;
        }

        _project.Settings = _project.Settings with
        {
            Job = _project.Settings.Job with { MillDrillToolId = value?.Id },
        };

        _project.Touch();
        OnOutputChanged();
    }

    // ------------------------------------------------------------------ the blank

    /// <summary>
    /// The rectangle this project's programs are referenced to — the blank's, or the board's.
    ///
    /// One place, because three things have to agree about it: where a program is drawn over the
    /// board, which axis a mirrored program is flipped back about, and what the exporter shifted by.
    /// </summary>
    private Bounds Frame
    {
        get
        {
            if (_board is null)
            {
                return Bounds.Empty;
            }

            var cutter = OutlineCutter();
            var mirrors = Layers.Any(r => r.Layer is not null && r.Mirrored);
            var blank = Blanks.Resolve(_project.Settings.Job.Blank, _board.Bounds, cutter.DiameterNm, mirrors);

            return blank.Resolved ? blank.Bounds : _board.Bounds;
        }
    }

    /// <summary>
    /// Cut or declare the piece of stock the job is built on.
    ///
    /// It moves work zero, so it is off until somebody asks for it.
    /// </summary>
    [ObservableProperty]
    public partial bool UseBlank { get; set; }

    /// <summary>True when the blank is a stated rectangle rather than a border round the board.</summary>
    [ObservableProperty]
    public partial bool BlankIsStated { get; set; }

    /// <summary>False when the stock is already this size and nothing needs cutting.</summary>
    [ObservableProperty]
    public partial bool CutTheBlank { get; set; } = true;

    [ObservableProperty]
    public partial double BlankWidthMm { get; set; }

    [ObservableProperty]
    public partial double BlankHeightMm { get; set; }

    [ObservableProperty]
    public partial double BlankBorderMm { get; set; } = 10;

    /// <summary>
    /// Cut two small holes in the stock's waste border, as a reference for checking a later setup.
    /// Off by default: they are waste, but they are also time on the machine.
    /// </summary>
    [ObservableProperty]
    public partial bool StockAlignmentHoles { get; set; }

    /// <summary>What the blank works out to, or why it does not. Shown under the fields.</summary>
    [ObservableProperty]
    public partial string BlankSummary { get; set; } = string.Empty;

    partial void OnUseBlankChanged(bool value)
    {
        OnPropertyChanged(nameof(CanWriteBlank));
        SaveBlank();
    }

    partial void OnBlankIsStatedChanged(bool value) => SaveBlank();

    partial void OnCutTheBlankChanged(bool value)
    {
        OnPropertyChanged(nameof(CanWriteBlank));
        SaveBlank();
    }

    partial void OnBlankWidthMmChanged(double value) => SaveBlank();

    partial void OnBlankHeightMmChanged(double value) => SaveBlank();

    partial void OnBlankBorderMmChanged(double value) => SaveBlank();

    partial void OnStockAlignmentHolesChanged(bool value) => SaveBlank();

    private void SaveBlank()
    {
        if (_loadingJob)
        {
            return;
        }

        var blank = new BlankOptions
        {
            Enabled = UseBlank,
            Sizing = BlankIsStated ? BlankSizing.Stated : BlankSizing.GrownFromBoard,
            LeftMm = BlankBorderMm,
            RightMm = BlankBorderMm,
            BottomMm = BlankBorderMm,
            TopMm = BlankBorderMm,
            WidthMm = BlankWidthMm,
            HeightMm = BlankHeightMm,
            Cut = CutTheBlank,
            AlignmentHoles = StockAlignmentHoles,
        };

        _project.Settings = _project.Settings with { Job = _project.Settings.Job with { Blank = blank } };
        _project.Touch();

        DescribeBlank(blank);
        OnOutputChanged();
    }

    /// <summary>
    /// Says what the blank came to, right under the fields that decide it.
    ///
    /// Because the interesting answer is usually a refusal — a panel 1.4 mm too wide for the sheet
    /// it was laid out for — and a refusal that only appears in the export list is one somebody
    /// meets after they have stopped thinking about the number that caused it.
    /// </summary>
    private void DescribeBlank(BlankOptions blank)
    {
        if (_board is null || !blank.Enabled)
        {
            BlankSummary = string.Empty;
            return;
        }

        var mirrors = Layers.Any(r => r.Layer is not null && r.Mirrored);
        var cutter = OutlineCutter();
        var plan = Blanks.Resolve(blank, _board.Bounds, cutter.DiameterNm, mirrors);

        // Which bit cuts it, said where the blank is set up. There is deliberately no second picker
        // here — the blank and the board come out with one cutter — but that has to be visible, or
        // the only place the answer lives is a row further down that nobody connects with this.
        var cutWith = blank.Cut
            ? $"Cut with the Board outline's bit ({cutter.Name}). "
            : string.Empty;

        BlankSummary = plan.Resolved
            ? string.Create(CultureInfo.InvariantCulture,
                $"{Nm.ToMillimetreString(plan.Bounds.Width, 2)} × {Nm.ToMillimetreString(plan.Bounds.Height, 2)} mm · ")
                + cutWith
                + string.Join(" ", plan.Notes)
            : string.Join(" ", plan.Refusals);
    }

    /// <summary>The Board outline layer's bit, the same one the export will use for the blank.</summary>
    private Tool OutlineCutter()
    {
        if (_board is null)
        {
            return LayerOperations.DefaultToolFor(OperationKind.Outline, Library.Tools);
        }

        var settings = _project.Settings.LayerOutputs.ToDictionary(
            o => o.FileName, o => o, StringComparer.Ordinal);

        return ExportPlanner.OutlineCutter(_board, settings, Library);
    }

    /// <summary>Reads the job options back out of a project that has just been opened.</summary>
    private void ApplyJobOptions()
    {
        var job = _project.Settings.Job;

        _loadingJob = true;

        try
        {
            // The project's own thickness, or — for one that never recorded it — the last one set,
            // which the note under the slider then points out.
            BoardThicknessMm = _project.Settings.BoardThicknessMm ?? Settings.BoardThicknessMm;

            MillLargeHoles = job.MillLargeHoles;
            MillDrillTool = job.MillDrillToolId is { } id
                ? Library.Tools.FirstOrDefault(t => t.Id == id)
                : null;

            UseBlank = job.Blank.Enabled;
            BlankIsStated = job.Blank.Sizing == BlankSizing.Stated;
            CutTheBlank = job.Blank.Cut;
            BlankWidthMm = job.Blank.WidthMm;
            BlankHeightMm = job.Blank.HeightMm;
            BlankBorderMm = job.Blank.LeftMm;
            StockAlignmentHoles = job.Blank.AlignmentHoles;

            // Inside the guard, not after it. A new list is a new source for the "Spiral with"
            // combo, and a combo given a new source clears its selection and then restores it —
            // pushing null and then the same tool back through the two-way binding. Both writes
            // used to land after loading had finished, so opening any project with a spiral tool
            // chosen marked it changed and asked to be saved on close, with nothing touched.
            OnPropertyChanged(nameof(MillDrillTools));
        }
        finally
        {
            _loadingJob = false;
        }

        DescribeBlank(job.Blank);
        DescribeThickness();
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
        ApplyJobOptions();
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

        // Two files claiming to be the top copper is a real problem; two drill maps is a normal
        // export, because KiCad writes one per drill file. Warning about the second teaches people
        // to skim past the first.
        foreach (var group in board.Layers
            .Where(l => l.Role is not (LayerRole.Unknown or LayerRole.DrillMap or LayerRole.Documentation))
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
