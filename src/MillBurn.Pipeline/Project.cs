using System.Collections.Immutable;
using MillBurn.Core;

namespace MillBurn.Pipeline;

/// <summary>What kind of thing a user selection points at.</summary>
public enum SelectionKind
{
    Net,
    Component,
    Pad,
    Aperture,

    /// <summary>A bare position. The last resort — see <see cref="SelectionRef"/>.</summary>
    Point,
}

/// <summary>
/// A reference to something the user picked out of the board.
///
/// **Stored by identity, never by position or index.** This is the rule that decides whether a
/// project survives the user re-exporting from KiCad after an edit. "The 47th flash" is the worst
/// possible key — it changes if anything at all is added anywhere earlier in the file. Coordinates
/// are only slightly better: nudging a component by 0.1 mm silently detaches every setting that
/// pointed at it.
///
/// Net names and component references are the designer's *own* identifiers, and X2 puts them in
/// the Gerber (<c>.N</c>, <c>.C</c>, <c>.P</c>), so a selection made against them still resolves
/// after an edit. That is the third distinct payoff from keeping the X2 attributes.
///
/// <see cref="SelectionKind.Point"/> exists for the honest exception: a board outline has no
/// component references, so a holding tab can only be a position. Those are snapped to the nearest
/// outline segment on reload and flagged if they moved far.
/// </summary>
public sealed record SelectionRef
{
    public required SelectionKind Kind { get; init; }

    /// <summary>Net name from <c>.N</c>.</summary>
    public string? Net { get; init; }

    /// <summary>Component reference from <c>.C</c>, e.g. "U3".</summary>
    public string? Component { get; init; }

    /// <summary>Pin from <c>.P</c>, e.g. "2".</summary>
    public string? Pin { get; init; }

    /// <summary>Aperture D-code, for a selection scoped to one aperture.</summary>
    public int? ApertureCode { get; init; }

    /// <summary>Only meaningful for <see cref="SelectionKind.Point"/>.</summary>
    public Point2? At { get; init; }

    public static SelectionRef ForNet(string net) =>
        new() { Kind = SelectionKind.Net, Net = net };

    public static SelectionRef ForComponent(string component) =>
        new() { Kind = SelectionKind.Component, Component = component };

    public static SelectionRef ForPad(string component, string pin) =>
        new() { Kind = SelectionKind.Pad, Component = component, Pin = pin };

    public static SelectionRef ForPoint(Point2 at) =>
        new() { Kind = SelectionKind.Point, At = at };

    public override string ToString() => Kind switch
    {
        SelectionKind.Net => $"net {Net}",
        SelectionKind.Component => $"component {Component}",
        SelectionKind.Pad => $"{Component} pin {Pin}",
        SelectionKind.Aperture => $"aperture D{ApertureCode}",
        _ => At is { } p ? $"point {p}" : "point",
    };
}

/// <summary>
/// One source file, embedded in the project.
///
/// The bytes are the truth: a project carries its own copy so that opening it in six months shows
/// the board it was built against, whatever has happened to the folder it came from. The two
/// hashes answer two different questions — see <see cref="GeometryHash"/>.
/// </summary>
public sealed record ProjectSource
{
    public required string FileName { get; init; }

    public required ImmutableArray<byte> Content { get; init; }

    /// <summary>Hash of the bytes. Answers "has this file been touched at all?".</summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Fingerprint of the realised geometry. Answers "has the board actually changed?", which is
    /// the only question worth putting in front of the user: an EDA tool stamps a creation date
    /// into every file, so every re-export changes every byte of every layer even when nothing
    /// moved.
    /// </summary>
    public required string GeometryHash { get; init; }

    public required LayerRole Role { get; init; }

    /// <summary>The user corrected the detected role, so a refresh must not undo that.</summary>
    public bool RoleOverridden { get; init; }
}

/// <summary>
/// Job settings. Deliberately thin: Phase 2 fills it with tools, depths, feeds and operations.
///
/// It exists now so those land in a container that already knows how to be saved, reopened and
/// refreshed, rather than being retrofitted into one later.
/// </summary>
public sealed record ProjectSettings
{
    /// <summary>Selections the user has made, by identity. See <see cref="SelectionRef"/>.</summary>
    public ImmutableArray<SelectionRef> Exclusions { get; init; } = [];

    /// <summary>
    /// The tools this job is cut with — a **copy**, not a reference into the library.
    ///
    /// Same reasoning as embedding the Gerbers. A project pointing at the library by name would
    /// have its toolpaths change the day someone adjusted a tip width to suit a newly bought bit,
    /// and a job that cut correctly last month would quietly stop doing so. The library is where a
    /// tool is *chosen* from; what the project keeps is what it was cut with.
    /// </summary>
    public ImmutableArray<Tool> Tools { get; init; } = [];

    /// <summary>
    /// What each layer becomes, keyed by the layer's own file name.
    ///
    /// This is the document. A project that remembers its Gerbers but not that the soldermask is an
    /// inverted SVG, that the bottom copper is mirrored, or that the outline wants six tabs is a
    /// project that has to be set up again every time it is opened — and the second setup is the
    /// one that quietly differs from the first.
    ///
    /// Keyed by file name because that is what identifies a layer across a refresh: roles can
    /// repeat, indices move when a file is added, and the name is what the EDA tool will write
    /// again next time.
    /// </summary>
    public ImmutableArray<LayerOutputSettings> LayerOutputs { get; init; } = [];

    /// <summary>
    /// This project's own start and end G-code, or nulls to use the machine's.
    ///
    /// Saved in the project so a job that needed something unusual keeps it — moved to another
    /// machine, or opened a year later, it still carries the lines it was cut with. Null means
    /// "whatever the machine says"; empty means "deliberately nothing", and the two must stay
    /// distinguishable or a project told to add nothing starts adding something when the machine
    /// default changes.
    /// </summary>
    public ProgramFraming Framing { get; init; } = ProgramFraming.None;

    /// <summary>
    /// Choices about this job: how it is allowed to make a feature, rather than which feature.
    ///
    /// Project-level rather than machine-level because the answer depends on the board. Whether a
    /// 3.2 mm hole should be spiralled out with an end mill is a question about *this* design and
    /// what the operator is willing to have happen to it, not about the machine — the same machine
    /// cuts the next board where the same hole is a mounting slot and wants a different answer.
    /// </summary>
    public JobOptions Job { get; init; } = new();

    /// <summary>
    /// How thick this board's stock is, in millimetres, or null for a project that has never
    /// recorded one.
    ///
    /// The project's, not the app's. It used to be an app setting, which meant the last board worked
    /// on decided the depth of the next: open a 1.6 mm project after cutting a 0.8 mm board and every
    /// hole and outline came out 0.8 mm short, with nothing on screen to say so. And the CLI read
    /// neither, so the same project cut to different depths from the window and from a script.
    ///
    /// Null is kept apart from any number, and is not written to the file: a project saved before
    /// this existed reads back exactly like one that never set it, and both are told apart from one
    /// that says 1.6.
    /// </summary>
    public double? BoardThicknessMm { get; init; }

    /// <summary>
    /// The last drill alignment found for this board, or null if none has been.
    ///
    /// Kept with the project because the correction belongs to this board on this jig: the numbers
    /// are found at the machine, late, and a board that goes back on for its outline an hour later
    /// should not need them typed again from memory. What it is not is a licence to skip the test —
    /// it is offered with the date it was found, and the operator can run the hover again in seconds.
    /// </summary>
    public AlignmentRecord? Alignment { get; init; }

    /// <summary>Which of <see cref="Tools"/> does what. Null falls back to the built-in default.</summary>
    public Guid? IsolationToolId { get; init; }

    public Guid? OutlineToolId { get; init; }

    public Guid? DrillToolId { get; init; }

    public string? Notes { get; init; }

    /// <summary>Resolves the stored selection, falling back to the built-ins.</summary>
    public Tool ToolFor(Guid? id, Tool fallback) =>
        id is { } wanted ? Tools.FirstOrDefault(t => t.Id == wanted) ?? fallback : fallback;

    /// <summary>What this layer was set to, or nothing if it has never been set.</summary>
    public LayerOutputSettings? OutputFor(string fileName) =>
        LayerOutputs.FirstOrDefault(o => string.Equals(o.FileName, fileName, StringComparison.Ordinal));

    /// <summary>
    /// Records one layer's settings, replacing whatever was there.
    ///
    /// Ordered by file name so two projects configured the same way save the same bytes — a
    /// manifest whose order depends on which layer the user happened to touch last cannot be
    /// diffed.
    /// </summary>
    public ProjectSettings WithOutput(LayerOutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var others = LayerOutputs.Where(
            o => !string.Equals(o.FileName, settings.FileName, StringComparison.Ordinal));

        return this with
        {
            LayerOutputs = [.. others.Append(settings).OrderBy(o => o.FileName, StringComparer.Ordinal)],
        };
    }
}

/// <summary>
/// A drill alignment as the project file keeps it: millimetres and degrees, because a project file is
/// read by people.
/// </summary>
public sealed record AlignmentRecord
{
    public double XMm { get; init; }

    public double YMm { get; init; }

    /// <summary>Zero for a board that was square to the machine, or never measured twice.</summary>
    public double RotationDegrees { get; init; }

    public double PivotXMm { get; init; }

    public double PivotYMm { get; init; }

    /// <summary>Whether the outline was moved along with the drilling and routing.</summary>
    public bool Outline { get; init; } = true;

    /// <summary>
    /// Exactly which programs were written again, as <see cref="DrillAlignment.Moved"/> keys, or null
    /// when the correction was left to the default rule.
    /// </summary>
    public ImmutableArray<string>? Moved { get; init; }

    /// <summary>
    /// Whether it was measured with the board flipped over.
    ///
    /// Recorded because it decides what the numbers mean: a correction found on the flipped board
    /// describes the flipped board, and reusing it on the first side would move every program the
    /// wrong way across the stock.
    /// </summary>
    public bool Flipped { get; init; }

    /// <summary>
    /// When it was found. Shown when the correction is offered again, because an alignment is only
    /// true while the board has not moved: yesterday's numbers on a board re-clamped this morning are
    /// worse than none.
    /// </summary>
    public DateTimeOffset? Found { get; init; }

    public static AlignmentRecord From(DrillAlignment alignment, bool flipped = false)
    {
        ArgumentNullException.ThrowIfNull(alignment);

        return new AlignmentRecord
        {
            XMm = Nm.ToMillimetres(alignment.XNm),
            YMm = Nm.ToMillimetres(alignment.YNm),
            RotationDegrees = alignment.RotationDegrees,
            PivotXMm = Nm.ToMillimetres(alignment.PivotNm.X),
            PivotYMm = Nm.ToMillimetres(alignment.PivotNm.Y),
            Outline = alignment.Outline,
            Moved = alignment.Moved,
            Flipped = flipped,
            Found = DateTimeOffset.Now,
        };
    }

    public DrillAlignment ToAlignment() => new(
        Nm.FromMillimetres(XMm), Nm.FromMillimetres(YMm), Outline)
    {
        RotationDegrees = RotationDegrees,
        PivotNm = new Point2(Nm.FromMillimetres(PivotXMm), Nm.FromMillimetres(PivotYMm)),
        Moved = Moved,
    };
}

/// <summary>
/// Which layers are shown. Session state, not document state.
///
/// It is persisted — you want the same layers next time you open this board — but changing it must
/// never mark the project dirty. If peeking under a layer prompts a save, people learn to hit
/// Discard reflexively, which is worse than never prompting at all.
/// </summary>
public sealed record ProjectViewState
{
    public ImmutableArray<string> HiddenLayers { get; init; } = [];
}

/// <summary>
/// The open document: the sources, the settings, and whether either has been touched since the
/// last save.
///
/// Mutable by design. It is what the window binds to and edits, and modelling a document as an
/// immutable value means rebuilding and rebinding the world on every keystroke.
/// </summary>
public sealed class MillBurnProject
{
    /// <summary>Bumped whenever the on-disk shape changes. Migrations are cheap when planned.</summary>
    public const int CurrentSchemaVersion = 1;

    private bool _isDirty;

    public required ImmutableArray<ProjectSource> Sources { get; set; }

    public required ProjectSettings Settings { get; set; }

    public ProjectViewState ViewState { get; set; } = new();

    /// <summary>Where the sources came from, so a refresh knows where to look. A hint, not a link.</summary>
    public string? OriginFolder { get; set; }

    /// <summary>Where this was last saved, or null if it never has been.</summary>
    public string? FilePath { get; set; }

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value)
            {
                return;
            }

            _isDirty = value;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? DirtyChanged;

    public string DisplayName => FilePath is null
        ? OriginFolder is null ? "Untitled" : Path.GetFileName(OriginFolder.TrimEnd(Path.DirectorySeparatorChar))
        : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>
    /// Whether there is anything a prompt could save.
    ///
    /// A project holding no sources holds nothing, however dirty it believes itself to be. Asking
    /// about it teaches people to dismiss the question without reading it, which costs them the one
    /// time it matters.
    /// </summary>
    public bool NeedsSaving => IsDirty && Sources.Length > 0;

    /// <summary>Marks the document changed. View state deliberately does not call this.</summary>
    public void Touch() => IsDirty = true;

    public void MarkSaved() => IsDirty = false;

    /// <summary>
    /// A project holding sources but never saved — what dropping a folder produces. Not dirty:
    /// nothing has been configured yet, so replacing it costs the user nothing and should not
    /// interrupt them.
    /// </summary>
    public static MillBurnProject FromSources(IEnumerable<ProjectSource> sources, string? originFolder) =>
        new()
        {
            Sources = [.. sources],
            Settings = new ProjectSettings(),
            OriginFolder = originFolder,
        };

    public static MillBurnProject Empty() => FromSources([], null);
}
