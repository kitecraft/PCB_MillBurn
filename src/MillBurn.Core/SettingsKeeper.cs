namespace MillBurn.Core;

/// <summary>
/// Which preference an edit changes — one member per handler that writes one.
/// </summary>
/// <remarks>
/// Named for the setting rather than for when it is written, so that a new one has to be added here
/// — and adding it here is what makes *"and when is that written?"* a question with a place to be
/// answered, rather than a choice each handler makes alone. The shape is
/// <c>StaleBecause</c>'s, for the same reason.
/// </remarks>
public enum SettingChange
{
    /// <summary>Where the window was left, saved as it closes.</summary>
    WindowPlacement,

    /// <summary>How wide the board pane was left, saved as the window closes.</summary>
    PanelWidth,

    /// <summary>Light, dark, or following the system.</summary>
    Theme,

    /// <summary>The View menu's scale strips.</summary>
    Ruler,

    /// <summary>The Settings window's machine, dry-run, probe, levelling and default numbers.</summary>
    Machine,

    /// <summary>The start and end lines that top and tail every program.</summary>
    Framing,

    /// <summary>A project added to or taken off File ▸ Open recent.</summary>
    RecentProjects,

    /// <summary>The alignment test's hover height.</summary>
    AlignHover,

    /// <summary>Where the alignment files were last written.</summary>
    AlignFolder,

    /// <summary>Where the last export went, and whether it wrote dry runs.</summary>
    Export,

    /// <summary>A layer's or a scene layer's colour, chosen or put back.</summary>
    Colours,

    /// <summary>
    /// The board thickness a new board starts at — set by the thickness slider, one call per
    /// 0.1 mm tick.
    /// </summary>
    BoardThickness,
}

/// <summary>When a changed setting reaches the settings file.</summary>
public enum Persistence
{
    /// <summary>Written to the file as part of the change. Right for a discrete event: one write each.</summary>
    WrittenNow,

    /// <summary>
    /// Held in memory and written *with the next write*, whatever setting that write is for.
    ///
    /// **It still survives a restart**, because closing the window writes the placement and a write
    /// is always of the whole record. What it loses is the value from a session that ended in a
    /// crash — the trade 6.49 made for a slider that called the write twenty-eight times a drag.
    /// Not to be confused with <see cref="SettingsKeeper.OverrideRulerForThisSession"/>, which is
    /// never written at all: the two were once the same method, and that was the fault.
    /// </summary>
    WrittenWithTheNextWrite,
}

/// <summary>
/// The operator's settings, and the one place that decides when a change to them is written (6.50).
///
/// **About twenty writes across a dozen handlers used to decide this alone**, through two methods
/// with one doc between them: <c>SaveSettings</c>, which wrote, and <c>RememberSettings</c>, which
/// held the value until the next write. The second was then used for the <c>--no-ruler</c>
/// screenshot override, where "written with the next write" is the hazard rather than the feature:
/// the close path would have turned the operator's rulers off for good (6.62's neighbour, caught
/// only because a guard in another file happened to skip the close-write on a shot). So there are
/// three contracts, and each has its own name here:
///
/// * <see cref="Change"/> with a setting whose <see cref="PersistenceOf"/> is
///   <see cref="Persistence.WrittenNow"/> — written as part of the change;
/// * the same with <see cref="Persistence.WrittenWithTheNextWrite"/> — kept, and written with
///   whatever is written next;
/// * <see cref="OverrideRulerForThisSession"/> — held apart from <see cref="Stored"/>, so no write
///   can carry it.
///
/// No Avalonia in it, so every one of those is a unit test. The view model is left the assignments
/// and the status line its write delegate sets when the disk says no.
///
/// **One method per setting, so the label cannot be wrong** (sprint 6 story 6). The first version
/// took a <see cref="SettingChange"/> and an edit from the caller, and nothing tied the two
/// together: a slider that called itself <c>Theme</c> would have written on every tick again and
/// passed every test. Each setter now names its own setting inside the keeper, where the test that
/// calls it sees exactly what the view model sees.
///
/// **And a change that changes nothing writes nothing** — unless a held value is waiting, which
/// then rides on it as it would on any other write.
/// </summary>
/// <param name="stored">What the settings file holds now.</param>
/// <param name="write">Writes the whole record and says whether it reached the disk. Its own
/// failures are its own to report; a false is what makes the next write try again.</param>
public sealed class SettingsKeeper(AppSettings stored, Func<AppSettings, bool> write)
{
    private readonly Func<AppSettings, bool> _write = write ?? throw new ArgumentNullException(nameof(write));

    /// <summary>
    /// The <c>--no-ruler</c> override, held apart from <see cref="Stored"/> on purpose: a field the
    /// settings record never sees cannot be written out by anything.
    /// </summary>
    private bool? _rulerForThisSession;

    /// <summary>
    /// True while something stored has not reached the disk: a
    /// <see cref="Persistence.WrittenWithTheNextWrite"/> change held back, or a write that failed —
    /// which is what makes "nothing changed" a reason to skip a write only when nothing is waiting.
    /// </summary>
    private bool _unwritten;

    /// <summary>
    /// The settings as the file holds them, or will once the next write happens.
    ///
    /// **Never carries a session-only override**, which is why it is safe to build the next record
    /// from it — and why the ruler is read through <see cref="ShowRuler"/> instead.
    /// </summary>
    public AppSettings Stored { get; private set; } =
        stored ?? throw new ArgumentNullException(nameof(stored));

    /// <summary>
    /// When a change to this setting is written — as one table.
    /// </summary>
    /// <remarks>
    /// Everything but the thickness is a discrete event — a theme picked, a file opened, a folder
    /// chosen, a window closed — and is written as it happens. The thickness is a slider snapping to
    /// 0.1 mm across 0.4–3.2 mm, for a value read only when the next board is imported, and writing
    /// it per tick was 6.49: twenty-eight writes on the UI thread for one drag, twenty-seven of them
    /// superseded at once. Panel width looks like a second continuous one and is not: it is saved
    /// only from the close handler, behind a one-pixel threshold.
    /// </remarks>
    public static Persistence PersistenceOf(SettingChange what) => what switch
    {
        SettingChange.WindowPlacement
            or SettingChange.PanelWidth
            or SettingChange.Theme
            or SettingChange.Ruler
            or SettingChange.Machine
            or SettingChange.Framing
            or SettingChange.RecentProjects
            or SettingChange.AlignHover
            or SettingChange.AlignFolder
            or SettingChange.Export
            or SettingChange.Colours => Persistence.WrittenNow,

        SettingChange.BoardThickness => Persistence.WrittenWithTheNextWrite,

        _ => throw new ArgumentOutOfRangeException(nameof(what), what, "No rule for this setting."),
    };

    /// <summary>Where the window was left, saved as it closes.</summary>
    public void SetWindowPlacement(WindowPlacement placement) =>
        Change(SettingChange.WindowPlacement, s => s with { Window = placement });

    /// <summary>How wide the board pane was left, saved as the window closes.</summary>
    public void SetPanelWidth(double width) =>
        Change(SettingChange.PanelWidth, s => s with { PanelWidth = width });

    /// <summary>Light, dark, or null to follow the system.</summary>
    public void SetTheme(string? theme) =>
        Change(SettingChange.Theme, s => s with { Theme = theme });

    /// <summary>The Settings window's numbers, all at once.</summary>
    public void SetMachine(
        MachineSettings machine, DryRunSettings dryRun, ProbeSettings probe, LevelSettings level,
        ImportDefaults import, MillingDefaults milling) =>
        Change(SettingChange.Machine, s => s with
        {
            Machine = machine,
            DryRun = dryRun,
            Probe = probe,
            Level = level,
            Import = import,
            Milling = milling,
        });

    /// <summary>The start and end lines that top and tail every program.</summary>
    public void SetFraming(ProgramFraming framing) =>
        Change(SettingChange.Framing, s => s with { Framing = framing });

    /// <summary>A project opened or saved, put at the top of File ▸ Open recent.</summary>
    public void RememberRecent(string path) =>
        Change(SettingChange.RecentProjects, s => s.WithRecent(path));

    /// <summary>A project taken off the recent list.</summary>
    public void ForgetRecent(string path) =>
        Change(SettingChange.RecentProjects, s => s.WithoutRecent(path));

    /// <summary>The alignment test's hover height.</summary>
    public void SetAlignHover(double hoverMm) =>
        Change(SettingChange.AlignHover, s => s with { Align = s.Align with { HoverMm = hoverMm } });

    /// <summary>Where the alignment files were last written.</summary>
    public void SetAlignFolder(string folder) =>
        Change(SettingChange.AlignFolder, s => s with { Align = s.Align with { LastFolder = folder } });

    /// <summary>Where the last export went, and whether it wrote dry runs.</summary>
    public void SetExport(string folder, bool dryRun) =>
        Change(SettingChange.Export, s => s with { LastExportFolder = folder, WriteDryRun = dryRun });

    /// <summary>A board layer's colour, by its role, as <c>#RRGGBB</c>.</summary>
    public void SetLayerColour(LayerRole role, string hex) =>
        Change(SettingChange.Colours, s => s.WithColour(role, hex));

    /// <summary>A board layer back to the built-in colour.</summary>
    public void ResetLayerColour(LayerRole role) =>
        Change(SettingChange.Colours, s => s.WithoutColour(role));

    /// <summary>A scene layer's colour — one no file produces — by its id.</summary>
    public void SetSceneColour(string id, string hex) =>
        Change(SettingChange.Colours, s => s.WithSceneColour(id, hex));

    /// <summary>A scene layer back to the built-in colour.</summary>
    public void ResetSceneColour(string id) =>
        Change(SettingChange.Colours, s => s.WithoutSceneColour(id));

    /// <summary>Every layer back to the built-in palette.</summary>
    public void ResetColours() =>
        Change(SettingChange.Colours, s => s with
        {
            LayerColours = System.Collections.Immutable.ImmutableDictionary<LayerRole, string>.Empty,
            SceneColours = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
        });

    /// <summary>Where the next new board's thickness starts — the slider's, so held, not written.</summary>
    public void SetBoardThickness(double thicknessMm) =>
        Change(SettingChange.BoardThickness, s => s with { BoardThicknessMm = thicknessMm });

    /// <summary>
    /// Applies an edit to the stored settings, and writes them when <paramref name="what"/> says to.
    ///
    /// The edit is given the stored record rather than handed a finished one, so nothing can build
    /// the next record from a copy that a session-only override has been folded into. Private: the
    /// setters above are the only way in, each naming its own setting.
    /// </summary>
    private void Change(SettingChange what, Func<AppSettings, AppSettings> edit)
    {
        var persistence = PersistenceOf(what);
        var next = edit(Stored);

        // Record equality, which compares the immutable collections inside by reference: an edit
        // that builds a new list with the same contents counts as a change and is written. That
        // errs on the side of writing, which is the safe side.
        var changed = !Equals(next, Stored);
        Stored = next;

        if (persistence == Persistence.WrittenWithTheNextWrite)
        {
            _unwritten |= changed;
            return;
        }

        if (!changed && !_unwritten)
        {
            return;
        }

        // A write the disk refused leaves the record unwritten, so the next write — even of the
        // same values, a second press of Save once the file is free — tries again rather than
        // being skipped as no change (the review of sprint 6 story 6).
        _unwritten = !_write(Stored);
    }

    /// <summary>Whether the viewport carries its scale strips: the session's override, else the stored choice.</summary>
    public bool ShowRuler => _rulerForThisSession ?? Stored.ShowRuler;

    /// <summary>
    /// Turns the rulers on or off for this session only, for the <c>--no-ruler</c> screenshot.
    ///
    /// Session-only on purpose: a shot is taken against whatever settings file is on the machine,
    /// and taking one must not leave the operator's rulers turned off afterwards.
    /// </summary>
    public void OverrideRulerForThisSession(bool shown) => _rulerForThisSession = shown;

    /// <summary>
    /// The operator's own choice from the View menu: stored, and written.
    /// </summary>
    /// <remarks>
    /// **Compared against what <see cref="ShowRuler"/> returns, not against the stored value** —
    /// 6.62. Compared with the setting alone, a session started with <c>--no-ruler</c> could never
    /// turn them back on: the override says off, the file says on, so the menu renders unticked and
    /// ticking it asks for <c>true</c>, which equals the stored value and changes nothing. And the
    /// override is dropped the moment the menu is used, or the stored value would stay unreachable
    /// for the rest of the session. The write follows the keeper's rule: a choice the file already
    /// holds — the rulers back on, when the file said on all along — writes nothing.
    /// </remarks>
    /// <returns>True if what the viewport shows changed, so the caller knows to say so.</returns>
    public bool ChooseRuler(bool shown)
    {
        if (ShowRuler == shown)
        {
            return false;
        }

        _rulerForThisSession = null;
        Change(SettingChange.Ruler, s => s with { ShowRuler = shown });
        return true;
    }
}
