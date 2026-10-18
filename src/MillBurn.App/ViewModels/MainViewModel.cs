using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Align;
using MillBurn.Cam;
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
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    /// <summary>The Phase 0 acceptance target from Documentation/06-Roadmap-and-Risks.md.</summary>
    public const int TargetSegments = 500_000;

    private MillBurnProject _project = MillBurnProject.Empty();
    private Board? _board;
    private IReadOnlyList<BackplotLayer> _backplot = [];
    private RefreshPlan? _plan;
    private bool _suspendOutputChanges;

    /// <summary>The preview in flight, so that the next edit cancels it rather than queueing.</summary>
    private readonly LatestRun _previewRun = new();

    /// <summary>
    /// The on-demand board check, tracked separately from the preview.
    ///
    /// Its own run rather than sharing the preview's, because the two are different questions and
    /// asking one must not cancel the other: checking a board while a preview is building is a
    /// reasonable thing to do, and making it abandon the picture would be a surprise.
    /// </summary>
    private readonly LatestRun _checkRun = new();

    private readonly LatestRun _findingsRun = new();

    /// <summary>Stops a preview still in flight when the window holding this goes away.</summary>
    public void Dispose()
    {
        _previewRun.Dispose();
        _checkRun.Dispose();
        _findingsRun.Dispose();
    }

    // The readout's origin changes with the frame, and the frame is what the scene was built from,
    // so the one notification covers both — a blank turned on rebuilds the scene.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OriginName))]
    public partial BoardScene? Scene { get; set; }

    [ObservableProperty]
    public partial ToolpathScene? ToolpathScene { get; set; }

    /// <summary>True while a preview is being built, so the window can say so instead of freezing.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

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

    /// <summary>The Stock chip's id, which is also the key its state is remembered by.</summary>
    private const string StockChipId = "stock";

    /// <summary>The scene ids of the stock program's paths, which the Stock chip governs.</summary>
    private readonly HashSet<string> _stockPaths = new(StringComparer.Ordinal);

    /// <summary>
    /// The same rows as <see cref="Layers"/>, bucketed by which part of the board they belong to.
    /// This is what the panel lists; <see cref="Layers"/> stays flat for everything that has to
    /// walk every row regardless of where it sits.
    /// </summary>
    public ObservableCollection<LayerGroup> LayerGroups { get; } = [];

    /// <summary>
    /// What the heading says beside the word CHECK — because the list is capped and scrolled, and a
    /// panel showing three of nine with nothing saying so is worse than one showing all nine.
    ///
    /// **It always states the number now, and it names the refusals.** It used to fall silent at a
    /// count of one, which was fine while the list was always open and is not fine once the section
    /// folds: folded, this line is the only thing left, and *"a job with three checks and a job with
    /// none must not look the same"*. None hides the panel outright, so the pair that could collide
    /// is one and one — hence the number unconditionally.
    ///
    /// The refusal count is here for the same reason. Severity is readable in the list by its mark,
    /// and folding the list would take that away entirely; a reader who has folded the panel is
    /// still entitled to know whether anything in it was a refusal rather than a note.
    /// </summary>
    public string ChecksSummary
    {
        get
        {
            var refusals = Warnings.Count(w => w.IsRefusal);

            // Three cases rather than two, because the second reading of this on a real board said
            // "· 11, 11 to look at" — the Mega's checks are all refusals, and a number repeated is
            // a number a reader has to compare against itself to learn nothing.
            if (refusals == 0)
            {
                return string.Create(CultureInfo.InvariantCulture, $"· {Warnings.Count}");
            }

            return refusals == Warnings.Count
                ? string.Create(CultureInfo.InvariantCulture, $"· {Warnings.Count} to look at")
                : string.Create(CultureInfo.InvariantCulture, $"· {Warnings.Count}, {refusals} to look at");
        }
    }

    /// <summary>
    /// Whether the section is folded to its heading.
    ///
    /// **Not saved with the settings, and that is a decision rather than an omission.** A fold
    /// remembered across sessions would hide the checks on the next board the operator opens —
    /// one they have never seen, whose checks they have never read — and the state that caused it
    /// would have been set on some other board days earlier. Within a session it stays exactly
    /// where it was put, which is what somebody who folded it away wanted.
    ///
    /// **A new check does not unfold it either**, with one exception, in
    /// <see cref="Warn"/>: the count in the heading is what makes leaving it folded defensible, so
    /// an arriving check changes that count and a reader who folded the panel is not overruled for
    /// a note. An <see cref="CheckSeverity.Error"/> is not a note — it means do not run this — and
    /// that one reopens the section.
    /// </summary>
    [ObservableProperty]
    public partial bool ChecksFolded { get; set; }

    /// <summary>
    /// How many files a single export refusal names before it starts counting instead.
    ///
    /// Measured rather than chosen: the test board's export refuses a dry run for seventeen
    /// programs at once for one reason, and the line naming all seventeen was taller than the panel.
    /// </summary>
    private const int MaxNamedFiles = 3;

    public ObservableCollection<CheckRow> Warnings { get; } = [];

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

    /// <summary>
    /// The folder the Gerbers were read from, in full.
    ///
    /// Asked for from the workshop — *"We should display the gerber folder somewhere so the user
    /// knows where the project is from"* — because nothing in the window said it. The title carries
    /// the folder's last segment alone, and this machine has two boards called
    /// `Millburn_Test_Board` in different folders: a project opened from the wrong one looks exactly
    /// like a project opened from the right one, right up until its programs are cut.
    ///
    /// **In full.** A first attempt elided the middle to keep the panel narrow, and the product
    /// owner's answer to it was *"what I would like to know is where the source folder is"* — which
    /// a path with its middle removed does not say. The view wraps it instead.
    /// </summary>
    [ObservableProperty]
    public partial string? SourceFolder { get; private set; }

    /// <summary>
    /// The tabs this outline layer would be cut with as things stand.
    ///
    /// Asked of the planner rather than worked out here, and through the same options the export
    /// builds — so the tabs window shows the board that is going to be cut and not a second opinion
    /// about it. Empty when there is no board, when the layer is not being cut out, or when the tabs
    /// asked for would take up the whole profile.
    /// </summary>
    public TabPlanning? PlannedTabs(LayerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_board is null || row.Operation != OperationKind.Outline)
        {
            return null;
        }

        var layer = _board.Layers.FirstOrDefault(l => string.Equals(l.FileName, row.FileName, StringComparison.Ordinal));

        if (layer is null)
        {
            return null;
        }

        var options = ExportPlanner.OutlineOptionsFor(
            _board, row.ToSettings(), row.Tool, Nm.FromMillimetres(BoardThicknessMm));

        // **The profiles the export cuts, not the layer’s raw area.** The planner is shaped by
        // what it is given: a stroked Edge_Cuts realises as an annulus per outline, and handing it
        // the negative rings as well would nest and order the contours differently from the
        // program — so the tab an operator edited as "3" in this list would not be the third tab
        // in the file. Shaped once here and used for both questions.
        var profiles = ExportPlanner.OutlineProfiles(layer);

        var planned = OutlineOperation.PlanTabs(profiles, options);
        var tabs = planned.Tabs;

        // **Into the frame the machine works in.** The planner answers in the artwork's own
        // coordinates, and on this board that reads "183.41, -124.47 mm": right about the Gerber and
        // useless at the machine, where nothing is negative and nothing is at 183.
        //
        // Through the planner's own `Placed`, which a `/code-review` pass is the reason for: this
        // used to shift by the board's corner alone. On stock, work zero is the *stock's* corner and
        // every coordinate in the file is measured from there, so the window was out by the border;
        // and a mirrored outline layer has its x flipped about the frame's centreline in the file
        // and was not here. The one step left out is a drill alignment, which is a correction
        // measured later against copper already on the board rather than part of where the tabs are.
        var frame = Frame.IsEmpty ? _board.Bounds : Frame;

        if (!frame.IsEmpty)
        {
            var mirrored = row.ToSettings().MirrorFor(layer.Role);

            tabs = [.. tabs.Select(t => t with
            {
                At = ExportPlanner.Placed(t.At, frame, mirrored, alignment: null),
            })];
        }

        return planned with { Tabs = tabs };
    }

    /// <summary>
    /// The tabs as the viewport shows and grabs them, or null when the board offers none (6.4).
    ///
    /// Built from the same planner call the tabs window and the export use, so the handle an
    /// operator drags is the tab the program cuts — but in the frame's own millimetres, unmirrored,
    /// because that is what the viewport draws. The mirror belongs to the file, not the picture.
    /// </summary>
    [ObservableProperty]
    public partial TabOverlay? TabHandles { get; private set; }

    /// <summary>The plan behind the handles, in artwork coordinates, and the loop they slide on.</summary>
    private TabPlanning? _tabPlanning;
    private ProfilePath? _tabProfile;
    private LayerRow? _tabRow;

    /// <summary>
    /// What the overlay was last built from, so the next refresh can answer "nothing moved"
    /// without re-shaping the board. `OnOutputChanged` fires for every settings change on every
    /// layer, and `/code-review` priced the alternative: a drill-guide checkbox re-offsetting the
    /// 66-up panel's 102 profiles on the UI thread. The settings record compares by value — its
    /// immutable arrays by instance, which is exactly right, because an untouched row hands back
    /// the same instances and a gesture builds new ones.
    /// </summary>
    private (Board Board, LayerOutputSettings Settings, double ThicknessMm, Bounds Frame)? _tabHandlesFrom;

    /// <summary>
    /// Re-asks the planner what the tabs are and where the loop is.
    ///
    /// Called wherever the answer can have changed: a rebuilt board, and any output change — the
    /// gestures themselves arrive back here through <see cref="OnOutputChanged"/>, which is what
    /// makes a drop show its re-planned truth rather than the control's own projection.
    /// </summary>
    private void RefreshTabHandles()
    {
        var row = _board is null
            ? null
            : Layers.FirstOrDefault(r => r.Operation == OperationKind.Outline && r.Layer is not null);

        var layer = row is null || row.TabCount <= 0
            ? null
            : _board!.Layers.FirstOrDefault(
                l => string.Equals(l.FileName, row.FileName, StringComparison.Ordinal));

        if (layer is null)
        {
            _tabPlanning = null;
            _tabProfile = null;
            _tabRow = null;
            _tabHandlesFrom = null;
            TabHandles = null;
            return;
        }

        var settings = row!.ToSettings();
        var frame = Frame.IsEmpty ? _board!.Bounds : Frame;

        // Nothing the tabs depend on has moved: same board, same outline settings, same
        // thickness, same frame. The rows are rebuilt on every preview, so the row object itself
        // is not part of the signature — but a fresh row still has to be the one a gesture
        // writes to, which is why `_tabRow` is updated even on a hit.
        if (_tabHandlesFrom is { } from
            && ReferenceEquals(from.Board, _board)
            && from.Settings == settings
            && from.ThicknessMm == BoardThicknessMm
            && from.Frame == frame)
        {
            _tabRow = row;
            return;
        }

        var options = ExportPlanner.OutlineOptionsFor(
            _board!, settings, row.Tool, Nm.FromMillimetres(BoardThicknessMm));

        var planned = OutlineOperation.PlanTabs(ExportPlanner.OutlineProfiles(layer), options);

        if (planned.Profile is not { } profile || planned.Tabs.Count == 0)
        {
            _tabPlanning = null;
            _tabProfile = null;
            _tabRow = null;
            _tabHandlesFrom = null;
            TabHandles = null;
            return;
        }

        _tabPlanning = planned;
        var path = new ProfilePath(profile);
        _tabProfile = path;
        _tabRow = row;
        _tabHandlesFrom = (_board!, settings, BoardThicknessMm, frame);

        SKPoint OnBoard(long x, long y) => new(
            (float)((x - frame.MinX) / 1_000_000.0),
            (float)((y - frame.MinY) / 1_000_000.0));

        // Each tab's gap ends are walked along the loop, not offset from the centre: half a width
        // either way round the path is where the cut really stops and resumes, corners included,
        // and the splitter does the same arithmetic when it cuts there.
        TabHandle Handle(PlacedTab t)
        {
            var half = t.WidthNm / 2.0;
            var gapFrom = path.PointAt(t.AlongNm - half);
            var gapTo = path.PointAt(t.AlongNm + half);

            return new TabHandle(
                OnBoard(t.At.X, t.At.Y),
                (float)Nm.ToMillimetres(t.WidthNm),
                t.Moved || t.Added,
                OnBoard(gapFrom.X, gapFrom.Y),
                OnBoard(gapTo.X, gapTo.Y));
        }

        TabHandles = new TabOverlay
        {
            Profile = [.. profile.Select(p => OnBoard(p.X, p.Y))],
            Tabs = [.. planned.Tabs.Select(Handle)],
        };
    }

    /// <summary>A place the viewport reported, taken back into the artwork's own nanometres.</summary>
    private Point2 ArtworkPoint(double xMm, double yMm)
    {
        var frame = Frame.IsEmpty ? _board!.Bounds : Frame;

        return new Point2(
            frame.MinX + (long)Math.Round(xMm * 1_000_000.0),
            frame.MinY + (long)Math.Round(yMm * 1_000_000.0));
    }

    /// <summary>
    /// A tab was dropped: the planner's walk decides what the point means, and the override goes
    /// into the settings beside the hand-set sizes.
    /// </summary>
    public void MoveTab(int handle, double xMm, double yMm)
    {
        if (_tabProfile is not { } path)
        {
            return;
        }

        var (along, _) = path.Project(ArtworkPoint(xMm, yMm));

        if (ApplyTabMove(handle, along))
        {
            StatusMessage = string.Create(
                CultureInfo.InvariantCulture,
                $"Tab moved to {path.Wrap(along) / 1_000_000.0:F1} mm along the cut path — by hand now, until the tabs re-plan.");
        }
    }

    /// <summary>A click on an empty stretch of the loop: a tab belongs there.</summary>
    public void AddTab(double xMm, double yMm)
    {
        if (_tabProfile is not { } path || _tabRow is not { } row)
        {
            return;
        }

        var (along, _) = path.Project(ArtworkPoint(xMm, yMm));

        row.SetTabMoves([.. row.TabMoves, new TabMove { Index = -1, AlongNm = along }]);

        StatusMessage = string.Create(
            CultureInfo.InvariantCulture,
            $"Tab added at {path.Wrap(along) / 1_000_000.0:F1} mm along the cut path, at the board's own sizes.");
    }

    /// <summary>
    /// A handle was right-clicked. The last tab refuses: a board held by nothing comes free with
    /// the cutter still turning, and that choice belongs to the tab count, not to a slip of a
    /// click.
    /// </summary>
    public void RemoveTab(int handle)
    {
        if (_tabPlanning is not { } planning || _tabRow is not { } row
            || handle < 0 || handle >= planning.Tabs.Count)
        {
            return;
        }

        if (planning.Tabs.Count == 1)
        {
            StatusMessage = "The last tab is what holds the board. To cut it free, set the tab count to zero.";
            return;
        }

        var tab = planning.Tabs[handle];

        row.SetTabMoves(tab.Added
            ? RemoveAddEntry(row.TabMoves, tab.Ordinal)
            : [.. row.TabMoves.Where(m => m.Index != tab.Ordinal), new TabMove { Index = tab.Ordinal }]);

        StatusMessage = "Tab removed. The gap it left is cut through.";
    }

    /// <summary>
    /// The headless flag's route in: a tab by its place in the plan, moved by arc rather than by
    /// pointer (6.4). One line back for the capture log, refusals included.
    /// </summary>
    internal string MoveTabForShot(int tab, double alongMm)
    {
        if (_tabPlanning is not { } planning || _tabProfile is not { } path)
        {
            return "move tab: there are no tabs to move — load a board whose outline is cut with tabs";
        }

        if (tab < 0 || tab >= planning.Tabs.Count)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"move tab: there is no tab {tab} — the plan holds {planning.Tabs.Count}, numbered from 0");
        }

        ApplyTabMove(tab, alongMm * 1_000_000.0);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"move tab: tab {tab} is now at {path.Wrap(alongMm * 1_000_000.0) / 1_000_000.0:F1} mm along the cut path, by hand");
    }

    /// <summary>
    /// Writes one hand-set position: a planner's tab by its ordinal, an added tab by its place
    /// among the adds. False when there was nothing to move.
    /// </summary>
    private bool ApplyTabMove(int handle, double alongNm)
    {
        if (_tabPlanning is not { } planning || _tabRow is not { } row
            || handle < 0 || handle >= planning.Tabs.Count)
        {
            return false;
        }

        var tab = planning.Tabs[handle];

        row.SetTabMoves(tab.Added
            ? MoveAddEntry(row.TabMoves, tab.Ordinal, alongNm)
            : [.. row.TabMoves.Where(m => m.Index != tab.Ordinal), new TabMove { Index = tab.Ordinal, AlongNm = alongNm }]);

        return true;
    }

    /// <summary>The k-th add entry, moved. The adds keep their order, which is what names them.</summary>
    private static ImmutableArray<TabMove> MoveAddEntry(
        ImmutableArray<TabMove> moves, int add, double alongNm)
    {
        var seen = -1;

        return
        [
            .. moves.Select(m =>
                m.Index < 0 && m.AlongNm is not null && ++seen == add
                    ? m with { AlongNm = alongNm }
                    : m),
        ];
    }

    /// <summary>The k-th add entry, gone.</summary>
    private static ImmutableArray<TabMove> RemoveAddEntry(ImmutableArray<TabMove> moves, int add)
    {
        var seen = -1;

        return [.. moves.Where(m => !(m.Index < 0 && m.AlongNm is not null && ++seen == add))];
    }

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
        Warnings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ChecksSummary));
    }

    /// <summary>
    /// The board has been replaced or closed, so anything describing the old one is now wrong.
    ///
    /// **From the bench, 2026-10-07:** *"The findings dialog is not closed when opening a recent
    /// project or upon close project."* It is not modal — which is the property that makes it
    /// useful, so it can sit open beside the board — and nothing was holding it, so nothing could
    /// shut it. It stayed up over the next board, listing places on the last one.
    /// </summary>
    public event EventHandler? BoardReplaced;

    /// <summary>Remembers where the window was, so it opens where it was left.</summary>
    public void SaveWindowPlacement(WindowPlacement placement) =>
        SaveSettings(Settings with { Window = placement });

    /// <summary>Remembers light or dark, so the app opens the way it was closed.</summary>
    public void SaveTheme(string? theme) => SaveSettings(Settings with { Theme = theme });

    /// <summary>
    /// Whether the viewport carries its scale strips.
    ///
    /// Written through to the settings file on every change rather than on close, so a crash does
    /// not lose it and the View menu's tick is the stored value rather than a copy of it.
    /// </summary>
    public bool ShowRuler
    {
        get => _rulerForThisSession ?? Settings.ShowRuler;
        set
        {
            // **Against what the getter returns, not against the stored value.** Compared with the
            // setting alone, a session started with `--no-ruler` could never turn them back on:
            // the override says off, the file says on, so the menu renders unticked and ticking it
            // asks for `true`, which equals the stored value, returns early, raises nothing — and
            // the two-way binding snaps the tick straight back.
            if (ShowRuler == value)
            {
                return;
            }

            // The override has served its purpose the moment somebody uses the menu; leaving it
            // would make the stored value unreachable for the rest of the session.
            _rulerForThisSession = null;

            SaveSettings(Settings with { ShowRuler = value });
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// What the pointer readout calls zero, in the operator's words.
    ///
    /// The frame every emitted file is referenced to is the stock's rectangle when the job is built
    /// on stock and the board's otherwise, and the viewport measures from its lower-left corner. A
    /// coordinate is only worth reading if its origin is stated, and on a job with a blank the
    /// answer is not the one somebody would assume.
    /// </summary>
    public string OriginName =>
        Frame.IsEmpty || _board is null || Frame == _board.Bounds
            ? "from the board's corner"
            : "from the stock's corner";

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

        // **A preview still building captured the old numbers and has to go.** It read the machine
        // profile when its request was built, so left alone it finishes and publishes programs cut
        // to the safe height, decimals and rapid rate that were just replaced — and writes its own
        // status over the confirmation below. This reason does not clear the backplot, because the
        // branch below re-previews and the new run replaces it; see `Staleness.Of`.
        var interrupted = Invalidate(StaleBecause.MachineChanged);

        var confirmation = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Settings saved. Safe height {machine.SafeZMm:F2} mm, {DescribeDryRun(dryRun)}.");

        // The preview ends by writing its own status, and it now lands after this method has
        // returned — so the confirmation is said once the preview is done rather than into a line
        // the preview is about to overwrite. Saying it at all is the point: it repeats back the
        // numbers that were just changed.
        if ((Gcode is not null || interrupted) && !HasProgram)
        {
            _ = ConfirmAfterPreview(confirmation);
            return;
        }

        StatusMessage = confirmation;
    }

    /// <summary>
    /// What the dry run will be, in the words the operator chose it with.
    ///
    /// The confirmation repeats back the numbers that changed, so it has to say which kind of dry
    /// run they now have: "held at 5.00 mm" and "raised 3.00 mm" are different promises, and the
    /// one somebody is about to trust should not be inferred from a setting they cannot see.
    /// </summary>
    private static string DescribeDryRun(DryRunSettings dryRun) => dryRun.Style == DryRunStyle.Raised
        ? string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"dry run raised {dryRun.RiseMm:F2} mm above the real program")
        : string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"dry run held flat at {dryRun.HeightMm:F2} mm");

    /// <summary>Runs the preview, then says what was saved, so the confirmation is what is left up.</summary>
    /// <param name="confirmation">The message to leave on the status line.</param>
    private async Task ConfirmAfterPreview(string confirmation)
    {
        // Only over a preview that worked. A preview fails most readily on the settings that were
        // just changed, and that is the one moment when replacing its message with "Settings saved"
        // would hide the reason and look like success.
        if (await PreviewAsync())
        {
            StatusMessage = confirmation;
        }
    }

    public void SaveFraming(ProgramFraming framing)
    {
        ArgumentNullException.ThrowIfNull(framing);

        SaveSettings(Settings with { Framing = framing });
        OnPropertyChanged(nameof(Framing));

        // Framing is read when a request is built, so a preview in flight is writing the old
        // header and footer into every program — and a finished one is showing them. Both stop
        // being true the moment this returns, so neither may stay on screen.
        Invalidate(StaleBecause.FramingChanged);

        StatusMessage = framing.IsEmpty
            ? "Start and end G-code cleared. Preview again to see the programs without it."
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

    /// <summary>
    /// Puts the project away, back to the empty panel. The same as a new project underneath; it is
    /// here under its own name because that is the name somebody looks for in a File menu.
    /// </summary>
    public void CloseProject()
    {
        var name = _project.DisplayName;

        NewProject();
        StatusMessage = $"Closed {name}.";
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

        // The project's own corrections go back first. This list is about to become the program's
        // diagnostics, and a setting changed on the operator's behalf must not be the thing that
        // disappears because they opened a file to look at it.
        foreach (var note in _project.OpenNotes)
        {
            Warn(Check.Advice(CheckSource.Project, CheckKind.Project, note));
        }

        // Anything the parser could not make sense of. On somebody else's file this is the useful
        // part: it says which lines are not being drawn, so an empty-looking picture has a reason.
        //
        // The diagnostic already knows whether it is an error; that was a bool nothing outside the
        // parser could read, and it is the severity now.
        var opened = CheckSource.File(Path.GetFileName(path));

        foreach (var diagnostic in parsed.Diagnostics.Take(20))
        {
            Warn(new Check
            {
                Source = opened,
                Kind = CheckKind.Parse,
                Severity = diagnostic.IsError ? CheckSeverity.Error : CheckSeverity.Advice,
                Message = diagnostic.ToString(),
            });
        }

        if (measured.GougeCount > 0)
        {
            Warn(Check.Error(opened, CheckKind.Program,
                $"{measured.GougeCount} rapid move(s) at cutting depth. Do not run this."));
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

        // A preview still building had to go with it — it would have drawn itself back over what
        // this just cleared — so say so. Losing one silently is the same complaint as a preview
        // that completes when it should not have: the operator is owed an account of what the
        // window did, not just of what they asked for.
        var stopped = Invalidate(StaleBecause.ProgramClosed);

        StatusMessage = stopped
            ? "Closed the program, and stopped the preview that was building. Preview again when you want it."
            : "Closed the program.";
    }

    /// <summary>
    /// The programs on screen are no longer of this job. Says so once, for every reason there is.
    ///
    /// **The one method 6.51 asks for.** Six paths used to decide this for themselves and the
    /// decisions had drifted; what each reason means now lives in
    /// <see cref="Staleness.Of"/>, which has no window in it and can be read back in a test. This
    /// is the part that cannot: it touches bound properties and the scene.
    ///
    /// The status line stays with the caller. Each has something different to say, and several
    /// say it differently depending on whether a run was interrupted — which is why that is
    /// returned rather than announced here.
    /// </summary>
    /// <param name="why">What changed.</param>
    /// <returns>True if a preview was still building and has been stopped.</returns>
    private bool Invalidate(StaleBecause why)
    {
        var rule = Staleness.Of(why);

        // Asked before anything is cleared, because clearing is what makes the answer false.
        var drawn = _backplot.Count > 0 || Gcode is not null;

        if (!rule.Applies(drawn, _previewRun.IsRunning))
        {
            return false;
        }

        var stopped = rule.StopRun && StopPreview();

        // **Not a program that was opened off disk, unless this reason is forgetting it too.**
        // The backplot holds either a preview or an opened program — never both, since opening one
        // replaces it — so clearing it for a layer edit threw away a program nobody asked to
        // close, while `_programPath` survived and left *Close program* enabled over nothing
        // drawn. The rule's own words are that a settings change leaves an opened program alone,
        // and this is what makes them true. Pre-existing, and found by `/code-review` reading the
        // table against the code the moment the rule had one place to be stated in.
        if (rule.ClearPrograms && (rule.ForgetOpenedProgram || !HasProgram))
        {
            _backplot = [];
            Gcode = null;
            GcodeSummary = string.Empty;
        }

        if (rule.ForgetOpenedProgram)
        {
            _programBounds = null;
            _programPath = null;
            Warnings.Clear();

            // `HasProgram` and `ShowingNothing` are computed from `_programPath` and the backplot,
            // and both just changed. Two callers used to raise these and two did not, so a refresh
            // left the close-program button enabled over a program that had gone and the
            // empty-state panels showing the wrong thing. Raised where the fields are cleared, so
            // a caller cannot forget.
            OnPropertyChanged(nameof(HasProgram));
            OnPropertyChanged(nameof(ShowingNothing));
        }

        if (rule.Rebuild)
        {
            Rebuild(TimeSpan.Zero);
        }

        return stopped;
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

            var done = blank.HolesOnly ? "its alignment holes drilled with the" : "cut with the";

            StatusMessage = string.Create(
                CultureInfo.InvariantCulture,
                $"Wrote {Path.GetFileName(path)}: a {Nm.ToMillimetreString(blank.Bounds.Width, 2)} × "
                + $"{Nm.ToMillimetreString(blank.Bounds.Height, 2)} mm stock, {done} "
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

    /// <summary>
    /// Remembers where the alignment files go, so the next visit starts there.
    ///
    /// Kept apart from the export folder: these files are usually written into a folder of their
    /// own, over and over during one session, and choosing it again every time is the sort of thing
    /// that gets a file written next to the wrong programs.
    /// </summary>
    public void SaveAlignFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (!string.Equals(Settings.Align.LastFolder, folder, StringComparison.Ordinal))
        {
            SaveSettings(Settings with { Align = Settings.Align with { LastFolder = folder } });
        }
    }

    /// <summary>
    /// Where Job › Drill alignment opens: where it last wrote, then where the last export went, then
    /// the folder the board came from.
    /// </summary>
    public string AlignFolder()
    {
        if (Settings.Align.LastFolder is { } mine && Directory.Exists(mine))
        {
            return mine;
        }

        return Settings.LastExportFolder is { } last && Directory.Exists(last)
            ? last
            : Project.OriginFolder ?? Environment.CurrentDirectory;
    }

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
        OutputKind? filter = null, string? onlyLayer = null, DrillAlignment? alignment = null) =>
        ExportRequestFor(filter, onlyLayer, alignment)?.Plan(CancellationToken.None);

    /// <summary>
    /// Everything the planner will need, read off the view model in one go.
    ///
    /// The reading has to happen on the UI thread, because <see cref="RecordOutputs"/> writes each
    /// layer's setting back to the project first. The planning does not. Separating them is what
    /// lets the slow half run on the thread pool from a snapshot that cannot change underneath it
    /// while the operator carries on editing.
    /// </summary>
    private ExportRequest? ExportRequestFor(
        OutputKind? filter, string? onlyLayer, DrillAlignment? alignment)
    {
        if (_board is null)
        {
            return null;
        }

        RecordOutputs();

        return new ExportRequest(
            _board,
            _project.Settings.LayerOutputs.ToDictionary(o => o.FileName, o => o, StringComparer.Ordinal),
            Library,
            Nm.FromMillimetres(BoardThicknessMm),
            filter,
            Framing,
            Settings.Machine,
            _project.Settings.Job,
            alignment,
            onlyLayer);
    }

    /// <summary>A snapshot of the inputs to one export plan, and the planning it can then do.</summary>
    private sealed record ExportRequest(
        Board Board,
        Dictionary<string, LayerOutputSettings> Outputs,
        ToolLibrary Library,
        long ThicknessNm,
        OutputKind? Filter,
        ProgramFraming Framing,
        MachineSettings Machine,
        JobOptions Job,
        DrillAlignment? Alignment,
        string? OnlyLayer)
    {
        /// <summary>
        /// Plans this request, stopping if <paramref name="token"/> is cancelled.
        ///
        /// **The token has no default, and that is the whole point.** Sprint 1 sold a cancellable
        /// preview and delivered half of one: the token was read between programs in `PreviewBuild`,
        /// which is only reached once planning has finished, so a superseded run planned to the end
        /// on its pool thread. The planner takes a token now, but a token with a default is a token
        /// somebody forgets — the same shape as the chord tolerance in 6.25, where a call site that
        /// quietly stopped passing one would have shipped the bug with a green suite. Requiring it
        /// makes both callers say out loud whether their work can be abandoned: the preview passes
        /// its run's token, and <see cref="PlanExport"/> — which every synchronous caller goes
        /// through, on the UI thread, where there is nobody to abandon it for — says none.
        /// </summary>
        public ExportPlan Plan(CancellationToken token)
        {
            var plan = ExportPlanner.Plan(
                Board, Outputs, Library, ThicknessNm, Filter,
                framing: Framing, machineSettings: Machine, job: Job, alignment: Alignment,
                token: token);

            if (OnlyLayer is null)
            {
                return plan;
            }

            // "Export only this layer" narrows the *plan*, not the project. It used to set every
            // other layer to Not exported and keep a snapshot to undo with, which is a state
            // machine with edges: change a third layer while one is isolated and the snapshot
            // describes a board that no longer exists. Filtering one plan has no such state — the
            // settings are untouched, so there is nothing to put back.
            return plan with
            {
                Items =
                [
                    .. plan.Items.Where(
                        i => string.Equals(i.LayerFileName, OnlyLayer, StringComparison.Ordinal)),
                ],
                Skipped = [],
            };
        }
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
                        Style = Settings.DryRun.Style,
                        RiseMm = Settings.DryRun.RiseMm,
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

            // Named, not counted. "1 thing(s) refused — see the checks" sent the operator to a list
            // where the word "refused" does not appear — the line reads "Not levelled: ..." — with
            // nothing to tell a refusal from the ordinary advice beside it. From the bench: *"I
            // don't know which check it is referring to."* Saying what was refused is the whole of
            // the fix, and it costs nothing: the kinds are already in hand.
            var kinds = refusals.Select(r => r.What).Distinct(StringComparer.Ordinal).ToList();

            StatusMessage = refusals.Count switch
            {
                0 => $"Wrote {plan.Count + extra} file(s) to {folder}.",

                // One refusal: name the file too, so there is nothing left to look up.
                1 => $"Wrote {plan.Count + extra} file(s) to {folder}. "
                    + $"{refusals[0].What}: {refusals[0].File} — see the checks.",

                _ => $"Wrote {plan.Count + extra} file(s) to {folder}. "
                    + $"{refusals.Count} refused — {string.Join(", ", kinds)} — see the checks.",
            };

            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            StatusMessage = $"Could not write to '{folder}': {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Check the board as its own job, without exporting or previewing anything.
    ///
    /// **Why the button exists.** The board-level checks run on load; the electrical ones — the
    /// valuable ones — used to arrive only with an export plan, so a board with a short on it
    /// showed nothing in the panel until somebody pressed Preview. An operator who looked at an
    /// empty CHECK list and believed it was being told nothing by a list that had not looked.
    ///
    /// **The automatic check stays.** The Mega's short was found because the app spoke without
    /// being asked, and a button only protects somebody who thinks to press it. This adds a way to
    /// ask and takes nothing away.
    /// </summary>
    /// <summary>
    /// One sentence per stopped run, written once (6.54): each is reachable from two routes — the
    /// polite partial result and the cancellation that surfaced as an exception — and two copies
    /// of an operator-facing sentence drift apart the first time one is reworded.
    /// </summary>
    private const string CheckStopped = "Check stopped; the board was not fully checked.";

    private const string FindingsStoppedEmpty = "Stopped before the search found anything to show.";

    public async Task CheckBoardAsync()
    {
        if (ExportRequestFor(OutputKind.Gcode, onlyLayer: null, alignment: null) is not { } request)
        {
            return;
        }

        var token = _checkRun.Begin();

        if (token.IsCancellationRequested)
        {
            return;
        }

        StatusMessage = "Checking the board…";

        BoardCheck.Result result;

        try
        {
            result = await Task.Run(
                () => BoardCheck.For(request.Board, request.Outputs, request.Library, token), token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopped by the operator before any partial answer existed, or superseded by a later
            // press, or the window is closing — and `Finish` is what tells them apart (6.54): a
            // stopped run is still the current one and owes the operator a word, a superseded
            // run's status line is not worth writing over the newer one's.
            //
            // **Filtered on this run's own token**, for the reason `PreviewAsync` spells out: not
            // every cancellation means the operator moved on. An inner token, or a later stage
            // using one of its own, would otherwise land here with nothing cleared and nothing
            // said — `Finish` never called, so the run stays installed and the status line reads
            // "Checking the board…" for the rest of the session.
            if (_checkRun.Finish(token))
            {
                StatusMessage = CheckStopped;
            }

            return;
        }
        catch (Exception ex)
        {
            // **Everything else, not only the expected kinds.** This is reached from an `async
            // void` click handler, so anything that escapes is an unhandled exception on the UI
            // thread and the window closes with the operator's board unsaved. `PreviewAsync`
            // already catches broadly for exactly that reason; these two were narrower.
            //
            // A stopped run can land here too (6.54, found by `/code-review`): a stage that
            // reacts to the cancelled token by throwing something other than an
            // `OperationCanceledException` is still the operator's own stop, and telling them
            // their deliberate Escape was "Check failed" would be the status line lying about
            // who decided.
            if (_checkRun.Finish(token))
            {
                StatusMessage = token.IsCancellationRequested
                    ? CheckStopped
                    : $"Check failed: {ex.Message}";
            }

            return;
        }

        if (!_checkRun.Finish(token))
        {
            return;
        }

        // **A run that stopped early must never read as a clean board.** It looked at some of the
        // copper and not the rest, so publishing its findings would put a short list on screen
        // that means "so far" while looking like "all of it" — the same distinction sprint 2's
        // story 3 was about, arriving in a new place.
        if (result.Cancelled)
        {
            StatusMessage = CheckStopped;
            return;
        }

        ReplaceElectricalChecks(result);

        var found = result.Checks.Count;
        var layers = result.LayersChecked == 1 ? "1 layer" : $"{result.LayersChecked} layers";
        var took = $"{result.Elapsed.TotalSeconds:F2} s";

        // **The count is of findings; the panel now shows one line a layer.** Those are different
        // numbers — eleven findings on the Mega, two lines — and a status saying "11" over a panel
        // headed "2" is a contradiction a reader has to resolve. Naming where the eleven are is
        // what resolves it, and it is the same pointer the lines themselves carry.
        StatusMessage = found == 0
            ? $"Checked {layers} in {took}: nothing to report."
            : $"Checked {layers} in {took}: {found} thing(s) worth checking — Job ▸ Findings (F7) says where.";
    }

    /// <summary>
    /// Puts this check's findings in the panel in place of whatever was last said about the same
    /// layers.
    ///
    /// **This is the method that could not have been written before a check had a source.** The
    /// electrical findings for a layer are the same findings whether a preview produced them or
    /// the button did, so accumulating both would show every one of them twice — and the old way
    /// of taking a set back out, matching on the sentence, cannot tell "this layer's copy" from
    /// "the other layer's identical copy". Matching on the layer and the kind can.
    /// </summary>
    private void ReplaceElectricalChecks(BoardCheck.Result result)
    {
        // **Every electrical line goes, not only the ones about layers this run looked at.**
        //
        // Two earlier rules are worth keeping in view, because each was wrong in its own direction
        // and the comment describing the first outlived it. Taking the refreshed set from the
        // *findings* leaves a fixed layer's line on screen for ever, since a layer that has come
        // clean contributes no check to announce itself — exactly when the operator most wants to
        // watch it go. Keying on `result.Layers` fixes that and leaves one hole: a layer that
        // has been switched *off* since the last press is not in that list either, so its old
        // "3 finding(s) this cut cannot separate" row survived every subsequent check, for ever,
        // about copper nobody is cutting any more.
        //
        // A board check examines the whole board, so whatever it says about the copper supersedes
        // whatever was said before — by an earlier check or by a preview. That is a simpler rule
        // than the one it replaces as well as a complete one, and `result.Layers` is still what
        // makes it safe: it is the evidence that this run really did look, which is what the first
        // version of this method got wrong in the other direction.
        for (var i = Warnings.Count - 1; i >= 0; i--)
        {
            if (Warnings[i].Check.Kind == CheckKind.Electrical)
            {
                Warnings.RemoveAt(i);
            }
        }

        // This run's status line is about to be "so many things worth checking", which is about the
        // list rather than about any one line in it. Anything an earlier message was pointing at is
        // no longer what the operator is being told, so the pointing stops with the message.
        ClearHighlights();

        WarnAll(result.Checks);
    }

    /// <summary>
    /// Works out the full electrical findings for the board, for the view that shows them.
    ///
    /// **The expensive answer, asked for deliberately.** Locating where each gap is costs an offset
    /// per piece of copper — a quarter of a second per layer on the Arduino Mega — against a check
    /// that otherwise runs on every preview and every export. It is off the UI thread and
    /// cancellable for that reason, on the same `LatestRun` pattern as the preview and the board
    /// check: a second press supersedes the first, and the older answer is never shown over it.
    /// </summary>
    public async Task<BoardFindings.Result?> FindingsAsync()
    {
        if (ExportRequestFor(OutputKind.Gcode, onlyLayer: null, alignment: null) is not { } request)
        {
            return null;
        }

        // Read on this thread before the work goes to the pool: `Frame` walks the layer rows and
        // the project's settings, which belong to the UI.
        var frame = Frame;

        var token = _findingsRun.Begin();

        if (token.IsCancellationRequested)
        {
            return null;
        }

        StatusMessage = "Looking for where the copper stays joined…";

        BoardFindings.Result result;

        try
        {
            result = await Task.Run(
                () => BoardFindings.For(
                    request.Board, request.Outputs, request.Library, frame, token), token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // This run's own token only. See `CheckBoardAsync` and `PreviewAsync`: an unfiltered
            // catch here leaves the run installed and the window saying it is still looking.
            // `Finish` tells a stop from a supersession (6.54); a stop that landed before the
            // search produced anything has no window to show and says so where the operator is.
            if (_findingsRun.Finish(token))
            {
                StatusMessage = FindingsStoppedEmpty;
            }

            return null;
        }
        catch (Exception ex)
        {
            // Broad on purpose — this is awaited from an `async void` handler, so anything that
            // escapes closes the window. The gap search is the newest and least travelled geometry
            // in the application, which is the worst place to be narrow about it. A stop whose
            // cancellation surfaced as some other exception is still a stop, as in
            // `CheckBoardAsync` — the operator's Escape must not read as a failure.
            if (_findingsRun.Finish(token))
            {
                StatusMessage = token.IsCancellationRequested
                    ? FindingsStoppedEmpty
                    : $"Could not work out the findings: {ex.Message}";
            }

            return null;
        }

        if (!_findingsRun.Finish(token))
        {
            return null;
        }

        // A stop that landed before any layer finished came back politely rather than as an
        // exception, but it is the same outcome: nothing to put under the banner. A window
        // holding only an apology helps nobody, and its empty-state line would claim no layer is
        // set to be isolated — a lie about the settings (6.54, found by `/code-review`). Same
        // sentence as the exceptional route, because it is the same event.
        if (result.Cancelled && result.Layers.Count == 0)
        {
            StatusMessage = FindingsStoppedEmpty;
            return null;
        }

        var layers = result.Layers.Count == 1 ? "1 layer" : $"{result.Layers.Count} layers";
        var took = $"{result.Elapsed.TotalSeconds:F2} s";

        // **A stopped run is named as one**, here and again on the window itself. This is the view
        // somebody opens to satisfy themselves a board is sound, so a partial answer that reads as
        // a complete one is worse here than anywhere else in the application.
        StatusMessage = result.Cancelled
            ? "Stopped before the whole board was examined; the findings below are incomplete."
            : $"Examined {layers} in {took}.";

        return result;
    }

    /// <summary>
    /// Stops whichever check the operator is waiting on: the board check, the findings search, or
    /// both — Escape's meaning while either runs (6.54).
    ///
    /// Both rather than whichever seems active, because stopping is idempotent and the operator
    /// pressing Escape means "that is enough", not "that is enough of one of them". No status is
    /// written here: a stopped run is still the latest and announces itself on its own way out,
    /// which is what <see cref="LatestRun.Stop"/> exists to allow — and what keeps a stop that
    /// raced a natural finish from claiming a complete answer was cut short.
    /// </summary>
    /// <returns>True when something was running to stop, so a keystroke wired to this can fall
    /// through untouched when nothing was.</returns>
    public bool StopChecks()
    {
        // Both asked, neither short-circuited: `||` alone would leave the findings running
        // whenever the board check was the one that stopped.
        var check = _checkRun.Stop();
        var findings = _findingsRun.Stop();

        return check || findings;
    }

    /// <summary>
    /// Stops every check claiming to be the one a status message is about.
    ///
    /// **Tied to the message rather than to a timer or a click.** A highlight is only ever true
    /// relative to the sentence in the status bar; once that sentence is replaced the highlight is
    /// pointing at nothing, and a mark that survives the thing it explains is worse than none
    /// because it looks like a property of the check.
    /// </summary>
    private void ClearHighlights()
    {
        foreach (var row in Warnings)
        {
            row.IsHighlighted = false;
        }
    }

    /// <summary>
    /// Builds every G-code layer and draws the result back over the board, off the UI thread, and
    /// abandons a run that a later edit has already made pointless.
    ///
    /// The drawing comes from **parsing the emitted programs**, not from the toolpaths that produced
    /// them. Those two agree right up until the emitter has a bug, and only one of them is what the
    /// machine will run (Documentation/05, section 2.1).
    ///
    /// The window stays live throughout: the snapshot is taken here, the planning and the backplot
    /// happen on the thread pool, and only the applying of the result comes back. A second call
    /// cancels the first rather than queueing behind it, because the answer being computed
    /// describes a board the operator has already changed.
    ///
    /// **This was detached from its method and had to be put back.** Two summaries and the returns
    /// tag below ended up stacked above `CheckBoardAsync`, which returns a plain `Task` — so this
    /// method had no documentation at all, and the contract about the status line was published
    /// against the wrong one. The compiler does not object to a second `&lt;summary&gt;`, so nothing
    /// caught it; an `xhigh` review did.
    /// </summary>
    /// <returns>
    /// True when a preview was built, published, and had nothing to warn about. False when it
    /// failed, was superseded, had nothing to show, or found rapid moves at cutting depth — in
    /// every one of which the status line already carries something the operator needs more than
    /// a caller's own message.
    /// </returns>
    public async Task<bool> PreviewAsync()
    {
        if (ExportRequestFor(OutputKind.Gcode, onlyLayer: null, alignment: null) is not { } request
            || _board is null)
        {
            return false;
        }

        var bounds = _board.Bounds;
        var profile = Settings.Machine.Profile;
        var token = _previewRun.Begin();
        PreviewResult result;

        // Disposed already — shutdown, or a capture path that never raises ShutdownRequested. Set
        // IsBusy here and nothing would ever clear it, because the run is over before it starts.
        if (token.IsCancellationRequested)
        {
            return false;
        }

        IsBusy = true;
        var clock = Stopwatch.StartNew();

        // What gets built while this runs. Counted rather than asked of the plan, because "was this
        // remembered" is a question about this call and not about the object handed back — the same
        // plan instance goes to the run that built it and to every run that reuses it.
        //
        // Two previews overlapping can only push this the safe way: another run's work lands in the
        // count and this one says it built something. It cannot claim to have remembered work it
        // actually did.
        //
        // Unidentifiable counts with the misses. A board whose layers carry no fingerprint is
        // planned in full every time and increments nothing else — so leaving it out made the
        // message read "remembered in 2.98 s" in exactly the case where nothing was remembered,
        // which is the one thing it exists to not say.
        var builtBefore = RealisedLayers.Misses + PlannedExports.Misses + PlannedExports.Unidentifiable;

        try
        {
            result = await Task.Run(
                () => PreviewBuild.From(request.Plan(token), bounds, profile, token), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded by another preview, or stopped by an edit that made it pointless. Either
            // way whatever ended it owns the status and the busy flag — the next run sets both, and
            // <see cref="StopPreview"/> clears them when there is no next run — so this one says
            // nothing and clears nothing on its way out.
            //
            // **Filtered on this run's own token**, because planning can now throw one of these
            // too and not every one of them means "the operator moved on". An inner token, or a
            // future stage that uses one of its own, would otherwise take this branch: nothing
            // cleared, nothing said, and the window busy over a run that is not coming back. The
            // guard sends anything else to the failure path below, where it reaches somebody.
            return false;
        }
        catch (Exception ex)
        {
            // Anything else is the planner or the parser failing on this board. It has to reach the
            // operator: before this ran on a thread it came straight back out of the click, and
            // unhandled it would now either take the window down or vanish into an unobserved task.
            if (_previewRun.Finish(token))
            {
                IsBusy = false;
                StatusMessage = $"Preview failed: {ex.Message}";
            }

            return false;
        }

        // Finishing is also the question "am I still the latest?". A run can complete perfectly
        // well and still be stale — cancellation that arrives while the backplot is being built is
        // never seen, and a finished task's continuation can be pumped after a newer run has
        // already started. Publishing here would put this run's picture and G-code over the newer
        // one's and clear the busy flag it had just set.
        if (!_previewRun.Finish(token))
        {
            return false;
        }

        IsBusy = false;

        if (result.ProgramCount == 0)
        {
            StatusMessage = "No layer is set to produce G-code.";
            return false;
        }

        Apply(
            result,
            clock,
            remembered: RealisedLayers.Misses + PlannedExports.Misses + PlannedExports.Unidentifiable
                == builtBefore);

        // False when the picture came with a warning on it. A gouge is the most urgent thing this
        // application ever says — rapid moves at cutting depth, do not run this — and a caller that
        // takes true as licence to write its own message over the status line must not be given it
        // here. A failure is not the only message worth protecting.
        return result.GougeCount == 0;
    }

    /// <summary>
    /// Starts a preview without waiting for it, for the paths that have no way to await one.
    ///
    /// The result lands later, so a caller that sets its own status message afterwards will have it
    /// overwritten when the preview arrives — say the message first, or await
    /// <see cref="PreviewAsync"/> instead. Failures reach the status line rather than this caller,
    /// which is why discarding the task here is safe.
    /// </summary>
    public void Preview() => _ = PreviewAsync();

    /// <param name="result">What the run worked out.</param>
    /// <param name="remembered">
    /// True when nothing had to be built — board and settings both matched a run already done.
    /// Said on the status line because the application is now fast enough to be disbelieved: a
    /// preview that returns in seven hundredths of a second looks exactly like a button that did
    /// nothing, and an operator who thinks nothing happened presses it again.
    /// </param>
    /// <param name="clock">
    /// Running since the preview began, and read at the end rather than passed in already stopped.
    /// "Working…" tells the operator the window has not died; the number afterwards tells them
    /// whether twenty seconds was this board or a problem — so it has to count the rebuild below,
    /// which is still on this thread and is most of the wait on a large board. A number that left
    /// out the slow half would be quotable and wrong.
    /// </param>
    private void Apply(PreviewResult result, Stopwatch clock, bool remembered)
    {
        Gcode = result.Gcode;
        _backplot = result.Backplot;
        GcodeSummary = result.Summary;

        Rebuild(TimeSpan.Zero);

        WarnAll(result.Warnings);


        // The warning gets the line to itself. A board that is about to be cut at rapid speed is
        // not the moment to also report how briskly the picture was drawn.
        StatusMessage = result.GougeCount > 0
            ? $"{result.GougeCount} rapid move(s) at cutting depth — do not run this."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Previewing {result.ProgramCount} program(s), {(remembered ? "remembered" : "built")} in {clock.Elapsed.TotalSeconds:F2} s.");
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

    private void OnRoleChanged(LayerRow row, LayerRole role)
    {
        if (_suspendOutputChanges)
        {
            return;
        }

        // Posted, not run here. The combo box that raised this is in the middle of writing its
        // selection, and the rebuild below replaces the row it belongs to. On the UI thread there is
        // always a context to post to; without one — a script — there is nothing to be inside.
        if (SynchronizationContext.Current is { } context)
        {
            context.Post(_ => SetRole(row.FileName, role), null);
        }
        else
        {
            SetRole(row.FileName, role);
        }
    }

    /// <summary>
    /// Tells a layer what it is, by hand — 6.31. The way out of a name the matcher does not know, or
    /// reads wrong: *"if we do miss one, or mis-interpret one, the user has a way to fix it on their
    /// own."*
    ///
    /// **Stopped and cleared before the change, rebuilt after it**, the split a refresh uses and for
    /// the same reason (`StaleBecause.RoleChanged`): a run in flight is reading the project this is
    /// about to change. When the role really changes, the layer's own settings are forgotten with
    /// the old one, so it takes the defaults for the new one, as an imported layer would. Confirming
    /// a guess changes neither and touches nothing but the label.
    /// </summary>
    /// <returns>False when there is no such layer, or it already has that role by hand.</returns>
    public bool SetRole(string fileName, LayerRole role)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var current = _project.Sources.FirstOrDefault(s => string.Equals(s.FileName, fileName, StringComparison.Ordinal));

        if (current is null || (current.Role == role && current.RoleOverridden))
        {
            return false;
        }

        // **Refused when the file cannot be read as that role**, before anything is touched. Telling
        // an Excellon drill file it is copper hands it to the Gerber parser, and a layer that fails
        // is not on the board — so its row, and the picker that could undo the choice, went with it,
        // and the choice was marked as the operator's and survived every refresh. Found by review.
        //
        // Asked of that one file alone, and first: the second review found the first fix stopping
        // the preview, clearing the programs and marking the project unsaved for a change it then
        // took back.
        if (current.Role == role)
        {
            // Confirming a guess: the same role, so the same geometry and the same settings. Nothing
            // drawn is out of date, and stopping a preview for a label would cost the operator the
            // wait for nothing. Found by review.
            _project.SetRole(fileName, role);
            Rebuild(TimeSpan.Zero);
            StatusMessage = $"{fileName} is {LayerRoleInfo.Label(role)}, confirmed by hand. Save to keep this.";
            return true;
        }

        var trial = BoardLoader.LoadSources("role check", [(fileName, current.Content.AsSpan().ToArray(), role)]);

        if (trial.Failures is [var failure, ..])
        {
            // The picker already shows the refused role — the row set it before asking — and nothing
            // is rebuilt, so put it back by hand.
            Layers.FirstOrDefault(r => string.Equals(r.FileName, fileName, StringComparison.Ordinal))?.ShowRole(current.Role);
            StatusMessage = $"{fileName} cannot be read as {LayerRoleInfo.Label(role)}, so it was left as {LayerRoleInfo.Label(current.Role)}: {failure.Reason}";
            return false;
        }

        var stopped = Invalidate(StaleBecause.RoleChanged);

        _project.SetRole(fileName, role);
        Rebuild(TimeSpan.Zero);

        var said = $"{fileName} is now {LayerRoleInfo.Label(role)}, set by hand. Save to keep this.";
        StatusMessage = stopped ? said + " The preview that was building has been stopped." : said;
        return true;
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

        // The worst of the three: this swaps the source bytes, so a preview still building is
        // describing copper that no longer exists. Left running it publishes the old board's
        // programs against the new geometry and takes the status line with them — and unlike a
        // settings change, nothing here was clearing the backplot either.
        //
        // **Stopped and cleared before the swap, rebuilt after it.** That split is why this reason
        // carries `Rebuild: false`: a run in flight is reading the project `Apply` is about to
        // mutate, so the stop cannot wait — and a rebuild before the swap would draw the bytes
        // being discarded, then be thrown away by the real one below.
        var stopped = Invalidate(StaleBecause.SourceRefreshed);

        ProjectRefresh.Apply(_project, plan, taking);
        Rebuild(TimeSpan.Zero);
        CancelRefresh();

        StatusMessage = stopped
            ? $"Refreshed {taking.Count} file(s), and stopped the preview of the old ones. Save to keep this."
            : $"Refreshed {taking.Count} file(s). Save to keep this.";
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

        // Quiet about re-exports too. A file KiCad wrote again with nothing changed differs only in
        // its date stamp, and none of those is ticked, so Apply took nothing and the same list came
        // back on every open, for good. Refresh source still lists them, for anyone who wants them.
        if (RefreshItems.Count == 0 || _plan is { AffectsBoard: false })
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

    /// <summary>
    /// Keeps a setting for this session without writing the file — 6.49.
    ///
    /// **For the settings a continuous control changes.** Every other caller of
    /// <see cref="SaveSettings"/> is a discrete event: a theme picked, a file opened, a folder
    /// chosen. The board thickness is a slider, snapping to 0.1 mm across 0.4–3.2 mm, so a
    /// full-range drag called that twenty-eight times and wrote the JSON twenty-eight times — disk
    /// I/O on the UI thread, in a loop, for a value nobody reads until the next board is imported,
    /// with twenty-seven of the writes immediately superseded.
    ///
    /// **It still survives a restart**, because closing the window saves the placement, and that
    /// goes through `SaveSettings` and writes the whole object — this value with it. So does any
    /// other settings change made afterwards. What is lost is the value from a session that ended
    /// in a crash, and for a default meaning *"where the next new board starts"* that is the right
    /// trade against writing to disk twenty-eight times a drag.
    /// </summary>
    private void RememberSettings(AppSettings settings) => Settings = settings;

    /// <summary>
    /// Turns the rulers off for this session only, for the <c>--no-ruler</c> screenshot.
    ///
    /// Session-only on purpose: a shot is taken against whatever settings file is on the machine,
    /// and taking one must not leave the operator's rulers turned off afterwards.
    /// </summary>
    public void RememberRuler(bool shown)
    {
        _rulerForThisSession = shown;
        OnPropertyChanged(nameof(ShowRuler));
    }

    /// <summary>
    /// The <c>--no-ruler</c> override, held apart from <see cref="Settings"/> on purpose.
    ///
    /// **It went through `RememberSettings` first, and that was wrong.** That method's own doc says
    /// what the fault is: *"It still survives a restart, because closing the window saves the
    /// placement, and that goes through `SaveSettings` and writes the whole object — this value with
    /// it."* So a screenshot run would have turned the operator's rulers off for good. It did not,
    /// only because `SavePlacement` returns early when the window was given a `--shot` or a
    /// `--size` — a guard in a different file, for a different reason, that this was relying on
    /// without saying so. A field the settings record never sees cannot be written out by anything.
    /// </summary>
    private bool? _rulerForThisSession;

    // ------------------------------------------------------------------ presenting

    private void Adopt(MillBurnProject project, TimeSpan elapsed)
    {
        // A program made from the previous board is not a program for this one. No rebuild asked
        // for here: this swaps the project and rebuilds below with `fresh: true`, because the rows
        // on screen still belong to the project being replaced.
        Invalidate(StaleBecause.BoardReplaced);

        // **Nor is a window describing it.** Raised here because this is the one place a board is
        // replaced: importing a folder, opening a project, and closing one — which is
        // `NewProject`, which is this with an empty project — all arrive through it, and a layer
        // edit or a preview does not. Anything holding a view of the old board hears once, instead
        // of five call sites each remembering to say so.
        BoardReplaced?.Invoke(this, EventArgs.Empty);

        DetachProject(_project);
        _project = project;
        AttachProject(project);
        CancelRefresh();

        // Fresh: the rows on screen still belong to the project being replaced, and none of what
        // the operator hid in that one is a statement about this one. See 6.27.
        Rebuild(elapsed, fresh: true);
    }

    /// <summary>
    /// Realises the project's sources and rebuilds everything the window shows.
    ///
    /// <paramref name="fresh"/> marks the rebuild that follows a different project being opened, so
    /// that the rows on screen — still the old project's — are not read as this one's.
    /// </summary>
    private void Rebuild(TimeSpan elapsed, bool fresh = false)
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

        // Null means "nobody on screen has an opinion yet", which is not the same as "nothing is
        // hidden" — and reading the second as the first is 6.27. A project whose saved view state
        // hid four layers opened with all of them ticked, because the rows still on screen belonged
        // to the project before it; then the first Preview found no row hidden, fell back to the
        // saved state, and unticked four layers the operator had just been shown. The same
        // confusion ran the other way within one project: reveal every layer, change a setting, and
        // the saved state hid them again.
        var hidden = fresh || Layers.Count == 0
            ? null
            : Layers.Where(r => !r.IsVisible).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

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
        _sceneFrame = Frame.IsEmpty ? board.Bounds : Frame;

        var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            _sceneFrame.Value,
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
        RefreshTabHandles();

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

        var setByHand = _project.Sources
            .Where(s => s.RoleOverridden)
            .Select(s => s.FileName)
            .ToHashSet(StringComparer.Ordinal);

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

                    // Null unless the operator set a figure, in which case it is theirs and the
                    // board stops deriving one. Carried on every layer, like the isolation
                    // width, because only the outline reads it and a settings record that
                    // applied to some layers and not others would be a second rule to remember.
                    TabWidthNm = Settings.Milling.TabWidthNm,
                    TabHeightNm = Settings.Milling.TabHeightNm,
                };

            var row = new LayerRow(
                source, layer, settings, Library.Tools, OnLayerVisibilityChanged, OnOutputChanged,
                OnRoleChanged,
                roleSetByHand: setByHand.Contains(layer.Id));

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

        // The stock is not a layer — no file of the board's produces it, and it has nothing to set —
        // but its paths want hiding like one: without this, "show only this layer" still drew the
        // stock's cuts, holes and rapids over it. So it is a switch here, beside the substrate, and
        // not a row in the layer list.
        _stockPaths.Clear();

        var stockLayers = _backplot
            .Where(b => b.Source == ExportPlanner.StockLayer)
            .Select(b => scene.Layer(b.Id))
            .OfType<BoardSceneLayer>()
            .ToList();

        if (stockLayers.Count > 0)
        {
            _stockPaths.UnionWith(stockLayers.Select(l => l.Id));

            MoveKinds.Add(new MoveKindRow(
                StockChipId,
                "Stock",
                stockLayers,
                Avalonia.Media.Brushes.Transparent,
                !_kindVisible.TryGetValue(StockChipId, out var stockShowing) || stockShowing,
                OnToolpathFilterChanged,
                hasColour: false));
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

        // The stock has no row, so its chip stands in for one: the same rule, with the chip as the
        // owner. It is a switch over paths the kinds already govern, not a kind of its own.
        var stock = MoveKinds.FirstOrDefault(k => k.Id == StockChipId);

        foreach (var kind in MoveKinds.Where(k => !ReferenceEquals(k, stock)))
        {
            foreach (var layer in kind.Layers)
            {
                layer.Visible = kind.IsVisible
                    && (!owner.TryGetValue(layer.Id, out var row) || row.ShowToolpath)
                    && (stock is null || !_stockPaths.Contains(layer.Id) || stock.IsVisible);
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

        StockChip?.IsVisible = visible;
    }

    private MoveKindRow? StockChip => MoveKinds.FirstOrDefault(k => k.Id == StockChipId);

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

        // The stock is not that layer either.
        StockChip?.IsVisible = false;
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
                TabWidthNm = Settings.Milling.TabWidthNm,
                TabHeightNm = Settings.Milling.TabHeightNm,
            });
        }

        _project.Settings = settings;
        _project.Touch();

        // The programs were made from the settings that just changed, so they are no longer a
        // picture of anything. Unconditional, unlike an output change: this is one button press
        // rather than a slider tick, so the guard that reason carries would buy nothing.
        var stopped = Invalidate(StaleBecause.LayersReset);

        StatusMessage = stopped
            ? "Every layer is back to what a freshly imported board starts with, and the preview that was building has stopped."
            : "Every layer is back to what a freshly imported board starts with.";
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

        // A preview still building is building a picture of the settings that have just changed,
        // and this has to happen *above* the guard below rather than after it. While one is
        // building there is nothing drawn — the edit that started it cleared the last picture — so
        // `_backplot.Count == 0` was true in exactly the case that needed the most doing, and the
        // method returned before it could stop anything. From the bench: *"if I change a setting,
        // click preview, then change the setting back, the preview completes instead of being
        // interrupted on the setting change."*
        // **Invalidating is about a drawn preview; a moved frame is a different question.** With
        // nothing drawn there is nothing to clear, so `OutputChanged` carries a guard and does
        // nothing — except that the change may have moved the rectangle the scene itself was
        // built to, and then the picture on screen is referenced to a work zero the job no longer
        // uses.
        //
        // 6.57, found while building the ruler: ticking *Build on stock* left the viewport on the
        // board's frame while the panel beside it already said the stock was 88.63 × 86.09 mm, and
        // the pointer readout said *from the board's corner* over a job whose files would be
        // referenced to the stock's. Pressing Preview corrected both, which is what gave it away.
        //
        // Tested against the frame rather than against the blank's settings, so everything else
        // that moves it arrives here too: mirroring a layer, and changing the outline's bit, both
        // feed `Blanks.Resolve` and neither is a blank edit.
        var moved = _board is { } current && _sceneFrame is { } was
            && (Frame.IsEmpty ? current.Bounds : Frame) != was;

        // Asked before invalidating, not after: clearing the backplot is what makes this false,
        // so reading it afterwards would say "nothing was drawn" every single time and take the
        // early return on the one path that had just done the work. Caught by this method's own
        // golden run, which stopped printing "Output changed" — the kind of thing the guard
        // rewritten below is exactly as easy to get wrong the second time as the first.
        var drawn = _backplot.Count > 0 || Gcode is not null;
        var stopped = Invalidate(StaleBecause.OutputChanged);

        // Whatever else the change did, it may have re-planned the tabs: a moved frame moves the
        // millimetres the handles sit at, and every door that clears a hand-set position arrives
        // here. After the reframe, so the handles are built against the frame they will be drawn
        // in.
        if (!drawn && !stopped)
        {
            if (moved)
            {
                Reframe();
            }

            RefreshTabHandles();
            return;
        }

        RefreshTabHandles();
        StatusMessage = "Output changed. Preview again to see the new programs.";
    }

    /// <summary>
    /// Stops a preview that is still building, and takes on what it would have cleared.
    ///
    /// **A cancelled run says nothing on its way out**, by design: `Finish` tells it that something
    /// replaced it, so it publishes no result and clears no flag, because the run that replaced it
    /// owns both. When the thing that cancelled it is an *edit* rather than another preview there
    /// is no replacement, so the busy flag is this method's to clear — miss it and the window sits
    /// spinning over nothing until the next preview finishes.
    /// </summary>
    /// <returns>True if there was a run to stop.</returns>
    private bool StopPreview()
    {
        if (!_previewRun.IsRunning)
        {
            return false;
        }

        _previewRun.Cancel();
        IsBusy = false;
        return true;
    }

    /// <summary>
    /// Puts this export's refusals in the checks panel, in place of the last one's.
    ///
    /// **"In place of the last one's" is the whole job.** Without it, exporting twice leaves both
    /// sets on screen — and when two runs disagreed about which side the map was probed on, the
    /// panel showed each file refused for two opposite reasons at once. Both were true when they
    /// were written and only one of them still was.
    ///
    /// **Grouped by reason, with the files named.** A refusal explains itself in a paragraph,
    /// which is right once and unreadable five times: an export that could not level four
    /// programs used to print the same ninety words four times over, filling the panel and
    /// pushing the layer list off the screen. The panel earns attention by being short enough to
    /// read, and a wall of repeated text is how it stops being read at all.
    ///
    /// **"In place of the last one's" used to need a second list.** This method kept
    /// `_exportWarnings`, a parallel `List&lt;string&gt;` of the lines it had added, purely so it
    /// could find them again and `Remove` them by string equality — which is ambiguous the moment
    /// two checks read the same, and which is the whole argument for a check having a source.
    /// It has one now, so "the ones I put there" is a question the list can answer itself.
    /// </summary>
    private void ReplaceExportWarnings(List<(string What, string File, string Reason)> refusals)
    {
        // CheckSource.Export is this method's alone. Everything else in the panel is about a
        // layer, the stock, the board or the project, so nothing else can be caught by this.
        for (var i = Warnings.Count - 1; i >= 0; i--)
        {
            if (Warnings[i].Check.Source == CheckSource.Export)
            {
                Warnings.RemoveAt(i);
            }
        }

        // Nothing an earlier message pointed at is what the operator is about to be told.
        ClearHighlights();

        foreach (var group in refusals.GroupBy(r => (r.What, r.Reason)))
        {
            // **Capped, because uncapped it is the least readable thing in the panel.** One export
            // of the test board refuses a dry run for seventeen programs with the same reason, and
            // naming all seventeen filled the whole section with a comma-separated list that had to
            // be read to the end to learn one fact — that it was all of them. A screenshot of it
            // beside the rest of this story's work made the case on its own.
            //
            // Three then a count, on the same argument `ElectricalFindings` caps its named joins:
            // enough to recognise which programs are meant, not so many that the line stops being a
            // line. The reason follows, and it is the part that is actually actionable.
            var named = group.Select(r => r.File).Take(MaxNamedFiles).ToList();
            var files = group.Count() > MaxNamedFiles
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{string.Join(", ", named)} and {group.Count() - MaxNamedFiles} more")
                : string.Join(", ", named);

            // **The link the bench asked for twice.** The status line this export is about to set
            // names what was refused; these are the lines it means, so they say so themselves
            // rather than leaving a reader to match one sentence against a list of them. The mark
            // is put on here, where the set is known exactly, rather than inferred afterwards from
            // a severity — every refusal in the panel is not the same set as the ones this run just
            // produced, and highlighting a refusal from a previous export would point the message
            // at the wrong line.
            Warn(Check.Refusal(
                CheckSource.Export,
                CheckKind.Program,
                $"{group.Key.What}: {files} — {group.Key.Reason}")).IsHighlighted = true;
        }

        // **A message pointing into a folded panel is an instruction that cannot be followed.**
        // This is not the same rule as "a new check unfolds the section", which is deliberately not
        // the behaviour: an arriving note is reported by the count and a reader who folded the panel
        // is not overruled for it. But the status line is about to say *go and look at these*, and
        // if the list is folded there is nothing to look at.
        if (refusals.Count > 0)
        {
            ChecksFolded = false;
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

        // Still remembered app-wide, but only as where the next new board starts — and held in
        // memory rather than written, because this is a slider and the write was happening once
        // per tick. See `RememberSettings`, and 6.49.
        RememberSettings(Settings with { BoardThicknessMm = value });
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
    /// <summary>
    /// The rectangle the scene on screen was built to, or null before there is one.
    ///
    /// Kept so that <see cref="OnOutputChanged"/> can tell a change that moved work zero from one
    /// that did not. Recorded where the scene is built rather than computed on demand: the point is
    /// what the picture *was* drawn to, and <see cref="Frame"/> by then is what it should be.
    /// </summary>
    private Bounds? _sceneFrame;

    /// <summary>
    /// Moves the picture onto the frame the job now uses, without rebuilding it.
    ///
    /// **The geometry has not changed, only what it is measured from.** A full rebuild walks every
    /// ring of every layer into a path — about 7 ms on the Mega, measured — and the stock's border
    /// is a slider, so doing that per tick is the fault 6.49 describes in a different coat.
    /// Reframing is one assignment, too small to time, and a re-fit is arithmetic.
    ///
    /// **Re-fitted every time, not only when the stock appears.** Fitting only on the appearance
    /// was tried first, on the theory that a border nudged by a millimetre is not worth the view
    /// jumping — and it is wrong for the reason the scene is fitted to the blank at all: *"the cut
    /// that makes it lies outside the board — so fitting to the board put the blank's own toolpath
    /// just off screen, which is the one thing you would want to look at before running it."* Grow
    /// the border past the viewport without re-fitting and that cut goes off screen again, which
    /// is the very thing being guarded against. On a slider it reads as the board shrinking
    /// smoothly as the stock grows, which is feedback rather than a jump.
    /// </summary>
    private void Reframe()
    {
        if (_board is not { } board || Scene is not { } scene)
        {
            return;
        }

        var frame = Frame.IsEmpty ? board.Bounds : Frame;

        scene.Reframe(frame);
        _sceneFrame = frame;

        // The origin's name is read off this, and it is the half an operator reads rather than
        // sees.
        OnPropertyChanged(nameof(OriginName));

        FrameMoved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The rectangle the picture is measured from has moved under it.
    ///
    /// **Two things follow, which is why this is not called `FitRequested`.** The view has to fit
    /// again, and anything holding a coordinate in the old frame now means a different place.
    /// Named for what happened rather than for one of its consequences, because the first version
    /// was named for the fit and the second consequence was then simply forgotten.
    /// </summary>
    public event EventHandler? FrameMoved;

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

    /// <summary>
    /// On pre-cut stock, drill only the alignment holes and leave the edges alone. Offered only with
    /// Pre-cut size, Cut the stock to size and Alignment holes all ticked: anywhere else it means
    /// nothing, and hidden it cannot surprise anybody.
    /// </summary>
    [ObservableProperty]
    public partial bool StockHolesOnly { get; set; }

    /// <summary>Whether "Export only the alignment holes" is offered.</summary>
    public bool CanAskForHolesOnly => BlankIsStated && CutTheBlank && StockAlignmentHoles;

    /// <summary>What the blank works out to, or why it does not. Shown under the fields.</summary>
    [ObservableProperty]
    public partial string BlankSummary { get; set; } = string.Empty;

    partial void OnUseBlankChanged(bool value)
    {
        OnPropertyChanged(nameof(CanWriteBlank));
        SaveBlank();
    }

    partial void OnBlankIsStatedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAskForHolesOnly));
        SaveBlank();
    }

    partial void OnCutTheBlankChanged(bool value)
    {
        OnPropertyChanged(nameof(CanWriteBlank));
        OnPropertyChanged(nameof(CanAskForHolesOnly));
        SaveBlank();
    }

    partial void OnBlankWidthMmChanged(double value) => SaveBlank();

    partial void OnBlankHeightMmChanged(double value) => SaveBlank();

    partial void OnBlankBorderMmChanged(double value) => SaveBlank();

    partial void OnStockAlignmentHolesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAskForHolesOnly));
        SaveBlank();
    }

    partial void OnStockHolesOnlyChanged(bool value) => SaveBlank();

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
            HolesOnly = StockHolesOnly,
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
        var cutWith = plan.HolesOnly
            ? $"Holes drilled with the Board outline's bit ({cutter.Name}). "
            : plan.Cut
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
            StockHolesOnly = job.Blank.HolesOnly;

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

    /// <summary>
    /// Hides what should be hidden: what is hidden on screen now, or — when there is nothing on
    /// screen to ask — what the project was saved with. <see cref="LayerVisibility"/> holds the
    /// rule and the reason it is not a count.
    /// </summary>
    /// <param name="scene">The scene whose layers are being shown or hidden.</param>
    /// <param name="onScreen">
    /// The rows currently hidden, or null when the window holds no rows for this project yet. An
    /// empty set is an answer: it means every row is showing, and it must win over the saved state.
    /// </param>
    private void ApplyViewState(BoardScene scene, HashSet<string>? onScreen) =>
        LayerVisibility.Apply(
            scene,
            LayerVisibility.Hidden(
                onScreen,
                _project.ViewState.HiddenLayers.IsDefaultOrEmpty ? [] : _project.ViewState.HiddenLayers));

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

        // Beside the title rather than in it: the title bar is one line shared with the dirty mark
        // and the application's own name, and a path is the thing that does not fit there.
        // Absolute and in this platform's separators, because a path relative to a working
        // directory the operator never sees cannot answer the question the line was added for: a
        // board opened as `tests/boards/…` showed exactly that, forward slashes and all.
        SourceFolder = _project.OriginFolder is { Length: > 0 } where ? ProjectOrigin.Full(where) : null;
    }

    /// <summary>
    /// Anything the operator should look at before trusting what is on screen. These are surfaced
    /// rather than logged because a board that is quietly missing a layer still looks like a board.
    /// </summary>
    private void RefreshWarnings(Board board)
    {
        Warnings.Clear();

        // First, and kept for as long as the project is open rather than only at the moment it
        // opens: a setting this build will not honour was changed on the operator's behalf, and
        // that is the kind of thing somebody needs to see when they come back to the window, not
        // only in a status line they may have been looking away from.
        foreach (var note in _project.OpenNotes)
        {
            Warn(Check.Advice(CheckSource.Project, CheckKind.Project, note));
        }

        // A file that would not open at all. An error rather than a refusal: nothing declined to
        // do this, the board on screen is simply missing a piece of itself.
        foreach (var failure in board.Failures)
        {
            // The name alone to the source, the reason alone to the message. Passing `failure`
            // to both put "F_Cu.gbr: unexpected token at line 12" in a field whose entire job is
            // to be matched against a layer's file name, which nothing could ever equal.
            //
            // **And the reason rather than the whole sentence**, now that the panel renders the
            // source: this check's label *is* the file name, so `$"Could not read {failure}"` read
            // "F_Cu.gbr — Could not read F_Cu.gbr: unexpected token at line 12". Splitting
            // `BoardFailure` in two is what makes the half that is not already in the label
            // reachable, which is the second thing that split has paid for.
            Warn(Check.Error(CheckSource.File(failure.FileName), CheckKind.Board,
                $"Could not read this file: {failure.Reason}"));
        }

        foreach (var layer in board.Layers.Where(l => l.HasErrors))
        {
            var first = layer.Diagnostics.First(d => d.IsError);
            Warn(Check.Error(SourceFor(layer), CheckKind.Parse,
                $"{layer.FileName}: {first.Message}"));
        }

        var guessed = board.Layers.Where(l => l.RoleGuessed).ToList();

        if (guessed.Count == 1)
        {
            // The role is the label the panel now prints in front of this, so restating it here
            // said the same layer three times: "Top copper — F_Cu.gbr declares no file function;
            // role guessed as Top copper." The file name stays — it is the thing the operator
            // would open to check the guess, and it is the one fact the label does not carry.
            Warn(Check.Advice(SourceFor(guessed[0]), CheckKind.Board,
                $"{guessed[0].FileName} declares no file function; this role is a guess."));
        }
        else if (guessed.Count > 1)
        {
            // **One line for all of them, once the rows say it themselves** (6.31). A board whose
            // exporter writes no file functions guesses every layer, and a line apiece was nine
            // identical sentences pushing everything else out of the list — the dilution the
            // findings view was built to end. Each row is marked *guessed*, and its picker is where
            // the guess is put right.
            Warn(Check.Advice(CheckSource.Board, CheckKind.Board,
                $"{guessed.Count} files declare no file function, so their roles were read off their names. Each is marked guessed on its row; open the row to check it or set it by hand."));
        }

        if (!board.Layers.Any(l => l.Role == LayerRole.Outline))
        {
            Warn(Check.Advice(CheckSource.Board, CheckKind.Board,
                "No board outline: extents are taken from the drawn geometry."));
        }

        // Two files claiming to be the top copper is a real problem; two drill maps is a normal
        // export, because KiCad writes one per drill file. Warning about the second teaches people
        // to skim past the first.
        //
        // Inner copper is on that list for the same reason, and it was found the same way: the
        // Olimex board in 6.28 has Ln1_Cu and Ln2_Cu, and every four-layer board ever made has at
        // least two inner layers. The warning only became visible when 6.28 taught the matcher to
        // read those names, which is the usual shape of this — reading a board correctly for the
        // first time is what shows you the thing that was always going to be wrong about it.
        foreach (var group in board.Layers
            .Where(l => l.Role is not (LayerRole.Unknown or LayerRole.DrillMap
                or LayerRole.Documentation or LayerRole.InnerCopper))
            .GroupBy(l => l.Role)
            .Where(g => g.Count() > 1))
        {
            // The board, not any one of them: naming either file as the source would be picking
            // a culprit, and which of the two is wrong is exactly what is not known here.
            Warn(Check.Advice(CheckSource.Board, CheckKind.Board,
                $"{group.Count()} files claim to be {LayerRoleInfo.Label(group.Key)}: " +
                string.Join(", ", group.Select(l => l.FileName))));
        }
    }

    /// <summary>
    /// The layer a check is about, keyed by its file.
    ///
    /// The same shape the planner builds, and deliberately so: the panel holds checks from both,
    /// and a layer's checks are meant to group and colour together whichever side produced them.
    /// </summary>
    private static CheckSource SourceFor(BoardLayer layer) =>
        CheckSource.Layer(layer.Role, layer.Label, layer.FileName);

    /// <summary>
    /// Puts a set of checks in the panel, with the electrical ones collapsed to a line a layer.
    ///
    /// **This is the dilution 6.44 is about, fixed at the one place it can be.** Seventeen items
    /// before sprint 1's story 4; a dense board adds eleven more, every one of them naming nets.
    /// The Arduino Mega's top copper alone contributes six groups and two further lines, which
    /// buries the layer that would not parse and the tool that is missing a drill — and those are
    /// the checks with nowhere else to be said.
    ///
    /// The electrical findings do have somewhere else: <see cref="Views.FindingsWindow"/>, where
    /// there is room to name every net in a group and say where each gap actually is. So the panel
    /// keeps what only it can carry and points at the rest.
    ///
    /// **Collapsed here rather than in <see cref="ElectricalFindings"/>**, and that is the whole
    /// reason this method exists rather than a shorter check. The companion page and the CLI have
    /// no window to point at: a one-line summary ending "see the findings view" would be a
    /// dead end on a page read away from the app. They keep the full set; only the panel shortens.
    /// </summary>
    private void WarnAll(IEnumerable<Check> checks)
    {
        foreach (var check in checks)
        {
            if (check.Kind != CheckKind.Electrical)
            {
                Warn(check);
            }
        }

        // Grouped by source, so a two-sided board gets one line a side rather than one line. Which
        // layer a short is on decides which way up the stock goes to fix it.
        foreach (var layer in checks
            .Where(c => c.Kind == CheckKind.Electrical)
            .GroupBy(c => c.Source))
        {
            Warn(Collapsed(layer.Key, [.. layer]));
        }
    }

    /// <summary>
    /// One line standing for a layer's electrical findings, pointing at where they are said in
    /// full.
    ///
    /// **Severity is the worst of what it stands for**, never an average and never softened. This
    /// line is all the panel will show of a short, so a layer with copper left connected has to
    /// read as a refusal even though most of what it replaced was advice.
    /// </summary>
    private static Check Collapsed(CheckSource source, IReadOnlyList<Check> checks)
    {
        var refusals = checks.Count(c => c.IsRefusal);

        var message = refusals > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{refusals} finding(s) this cut cannot separate. Job ▸ Findings (F7) says where they are.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{checks.Count} note(s) about this copper. Job ▸ Findings (F7) says more.");

        // The kind stays Electrical, which is what lets a later run take this line back out again:
        // `ReplaceElectricalChecks` matches on the kind and the layer, and a summary that called
        // itself something else would accumulate one copy per press.
        return refusals > 0
            ? Check.Refusal(source, CheckKind.Electrical, message)
            : Check.Advice(source, CheckKind.Electrical, message);
    }

    /// <summary>
    /// Puts a check in the panel, dressed for it.
    ///
    /// **One door, so that a check cannot reach the panel without its colour.** Twelve places in
    /// this file produce checks and every one of them used to call `Warnings.Add` directly; with a
    /// row to build, that would be twelve chances to forget the brush and get a label in the
    /// default text colour that reads as though the layer has no colour rather than as though this
    /// line was built wrongly.
    /// </summary>
    private CheckRow Warn(Check check)
    {
        var row = new CheckRow(check, LabelBrushFor(check.Source));

        Warnings.Add(row);

        // The one thing that overrules a fold. See <see cref="ChecksFolded"/>: a note arriving
        // behind a folded heading is reported by the count and that is enough, but "do not run
        // this" is not a note, and a reader who has to open the panel to discover it has been
        // failed by the fold.
        if (check.IsError)
        {
            ChecksFolded = false;
        }

        return row;
    }

    /// <summary>
    /// The colour to print a check's label in: the layer's own, or null for anything that is not a
    /// layer.
    ///
    /// **Through <see cref="Palette"/> rather than <see cref="BoardPalette"/>,** so that a label
    /// follows the operator's colour override. The alternative is a check that names "Top copper"
    /// in a colour the top copper is not currently drawn in, two inches below a layer row showing
    /// the real one — which is precisely the disagreement this project's rules put first.
    /// </summary>
    private SolidColorBrush? LabelBrushFor(CheckSource source) =>
        source.Role is { } role ? LayerLabelBrush(role) : null;

    /// <summary>
    /// The colour a layer's name is written in, wherever it is written.
    ///
    /// **Public because a second window needed it and reached past it instead.** The findings view
    /// coloured its headings from <see cref="BoardPalette"/> directly, so an operator who had set
    /// top copper to green saw green in the layer list and in the CHECK panel and the built-in
    /// orange in the window those two point at — with a comment beside it claiming it matched. Two
    /// controls disagreeing about one layer is the first thing this project's style rules name, and
    /// the override and the legibility clamp both live on this side of the line.
    /// </summary>
    public SolidColorBrush LayerLabelBrush(LayerRole role) =>
        LayerRow.ToBrush(Readable(Palette(role).Fill));

    /// <summary>
    /// The same colour, brought into a range that can be read as text on either theme.
    ///
    /// **A colour chosen to look like the thing is not always a colour you can read the name in.**
    /// The drill roles are near-black on purpose — `PlatedDrill` is #141A1F, `NonPlatedDrill`
    /// #0A0D10 — because that is what a hole looks like on copper, and it is right in the viewport.
    /// As a word on the dark theme's panel it is invisible: the Mega's export lists "Plated holes"
    /// and "Non-plated holes" on every board, and both rows were unreadable while the copper rows
    /// beside them were fine. Story 5 asked for a layer's checks to carry that layer's colour in
    /// order to be *findable*, so a colour that hides the label defeats the thing it was for.
    ///
    /// **Clamped rather than replaced**, so the label keeps whatever identity its hue carries: a
    /// near-black drill becomes a muted blue-grey rather than the ordinary text colour, which is
    /// still recognisably not copper. The band is stated in terms of perceived luminance and is
    /// two-sided, because the silkscreen roles have the opposite problem — #F5F5F5 reads on dark
    /// and vanishes on light.
    ///
    /// Applied here rather than in <see cref="BoardPalette"/>, which is asked what a layer looks
    /// like when it is *drawn*, and must keep answering that.
    /// </summary>
    private static SKColor Readable(SKColor colour)
    {
        // Rec. 709 luma, which is what "how light does this look" means to an eye rather than to a
        // channel average: green carries most of the apparent brightness and blue almost none.
        var luma = ((0.2126 * colour.Red) + (0.7152 * colour.Green) + (0.0722 * colour.Blue)) / 255.0;

        // The band either theme can carry. Below it the label disappears into the dark panel, above
        // it into the light one; between, it reads on both.
        const double Floor = 0.38;
        const double Ceiling = 0.78;

        if (luma >= Floor && luma <= Ceiling)
        {
            return colour;
        }

        // Mixed toward white or black by however much is missing, so the hue survives the move. A
        // pure lighten keeps the ratios between the channels, which is what carries the colour.
        var target = luma < Floor ? Floor : Ceiling;
        var mix = luma < Floor
            ? (target - luma) / (1.0 - luma)
            : (luma - target) / luma;

        byte Blend(byte channel) => (byte)Math.Clamp(
            luma < Floor
                ? channel + ((255 - channel) * mix)
                : channel * (1 - mix),
            0,
            255);

        return new SKColor(Blend(colour.Red), Blend(colour.Green), Blend(colour.Blue), colour.Alpha);
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
            $"{MillBurn.Viewer.ToolpathScene.TierCount} LOD tiers built in {sw.Elapsed.TotalMilliseconds:F0} ms");
        BoardTitle = "Viewport benchmark";
    }
}
