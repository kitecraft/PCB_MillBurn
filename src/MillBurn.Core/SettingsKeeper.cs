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
/// </summary>
/// <param name="stored">What the settings file holds now.</param>
/// <param name="write">Writes the whole record. Its own failures are its own to report.</param>
public sealed class SettingsKeeper(AppSettings stored, Action<AppSettings> write)
{
    private readonly Action<AppSettings> _write = write ?? throw new ArgumentNullException(nameof(write));

    /// <summary>
    /// The <c>--no-ruler</c> override, held apart from <see cref="Stored"/> on purpose: a field the
    /// settings record never sees cannot be written out by anything.
    /// </summary>
    private bool? _rulerForThisSession;

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

    /// <summary>
    /// Applies an edit to the stored settings, and writes them when <paramref name="what"/> says to.
    ///
    /// The edit is given the stored record rather than handed a finished one, so nothing can build
    /// the next record from a copy that a session-only override has been folded into.
    /// </summary>
    public void Change(SettingChange what, Func<AppSettings, AppSettings> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        // Decided before the edit runs, so a setting with no rule changes nothing.
        var persistence = PersistenceOf(what);

        Stored = edit(Stored);

        // **Any stored ruler choice ends the session override**, not only one made through
        // `ChooseRuler`: a later handler changing the ruler by this route would otherwise write
        // the file while the viewport went on showing the override — the stored value unreachable
        // for the rest of the session, which is 6.62 again through the seam built against it.
        if (what == SettingChange.Ruler)
        {
            _rulerForThisSession = null;
        }

        if (persistence == Persistence.WrittenNow)
        {
            _write(Stored);
        }
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
    /// override is dropped the moment the menu is used — by <see cref="Change"/>, for any stored
    /// ruler choice — or the stored value would stay unreachable for the rest of the session.
    /// </remarks>
    /// <returns>True if what the viewport shows changed, so the caller knows to say so.</returns>
    public bool ChooseRuler(bool shown)
    {
        if (ShowRuler == shown)
        {
            return false;
        }

        // `Change` drops the override along with the write.
        Change(SettingChange.Ruler, s => s with { ShowRuler = shown });
        return true;
    }
}
