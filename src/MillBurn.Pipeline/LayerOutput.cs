using MillBurn.Core;

namespace MillBurn.Pipeline;

/// <summary>What a layer actually becomes once its output kind is known.</summary>
public enum OperationKind
{
    None,
    Isolation,
    Drilling,
    Outline,
    Engrave,
    MaskOpen,
    Vector,

    /// <summary>
    /// Clearing an area rather than tracing round it — milling cured soldermask off the pads.
    ///
    /// The paste layer's apertures are exactly where solder is meant to go, which is where the mask
    /// must not be, so they are the right geometry to clear.
    /// </summary>
    Pocket,
}

/// <summary>
/// One layer's output settings.
///
/// **Per layer, and one file each.** That is not a packaging preference: a job crosses tools and
/// often machines, and a single program containing isolation, three drill sizes and the outline
/// assumes one operator babysitting one long run. Separate files mean a broken bit costs the
/// drilling and not the board, and they are the only shape that works at all when the silkscreen
/// goes to a laser and the outline goes to the mill.
/// </summary>
public sealed record LayerOutputSettings
{
    public required string FileName { get; init; }

    public OutputKind Output { get; init; } = OutputKind.None;

    /// <summary>Which tool cuts it. Ignored for SVG.</summary>
    public Guid? ToolId { get; init; }

    /// <summary>
    /// How far past the underside to go, for anything that must cut all the way through.
    ///
    /// Meaningless for isolation, which is a scratch into the copper, and essential for drilling
    /// and the outline, where stopping exactly at the nominal thickness leaves a skin that tears.
    /// </summary>
    public long BreakThroughNm { get; init; } = Nm.FromMillimetres(0.3);

    /// <summary>
    /// How deep to cut. Null takes the default for whatever operation this layer becomes.
    ///
    /// Nullable because one number cannot be right for both: isolation is a scratch into copper
    /// where the depth chooses the cut width, and mask relief has to stop inside a film two
    /// microns thicker than nothing. A shared 0.05 mm default is fine for the first and cuts
    /// straight through the second into the copper underneath.
    /// </summary>
    public long? DepthNm { get; init; }

    /// <summary>What this layer's depth actually resolves to.</summary>
    public long DepthFor(OperationKind operation) =>
        DepthNm ?? LayerOperations.DefaultDepthNm(operation);

    /// <summary>
    /// Isolation only, and only for a project saved before widths existed.
    ///
    /// Kept so such a project still exports what it exported, rather than quietly widening every
    /// moat on a board somebody had already cut once. Ignored whenever
    /// <see cref="IsolationWidthNm"/> is set.
    /// </summary>
    public int Passes { get; init; } = 1;

    /// <summary>
    /// Isolation only: how wide a moat to clear either side of the copper, in nanometres.
    ///
    /// Passes are a consequence of this and of what the bit cuts at its depth, in the same way that
    /// the cut width is a consequence of the depth. Asking for laps is asking about the machine;
    /// asking for a width is asking about the board, which is the question that has an answer.
    ///
    /// Zero falls back to <see cref="Passes"/>.
    /// </summary>
    public long IsolationWidthNm { get; init; }

    /// <summary>Outline only. Zero cuts the board fully free.</summary>
    public int TabCount { get; init; } = 4;

    /// <summary>
    /// Drilling only: write a page beside the program explaining how to run it.
    ///
    /// On by default. A drilling program is the one operation with tool changes in it, so it is the
    /// one where the file alone does not tell you what to do — and somebody who does not want the
    /// page can untick it, which costs less than somebody not knowing it was available.
    /// </summary>
    public bool WriteDrillGuide { get; init; } = true;

    /// <summary>
    /// Whether to reflect this layer, for work done on a flipped board. Null takes the default for
    /// the layer's side.
    ///
    /// Nullable rather than a plain <c>false</c> so that a caller which knows nothing about
    /// mirroring — the CLI, a test, an older project file — still gets the physically correct
    /// answer instead of silently cutting the bottom side backwards.
    ///
    /// Overridable because the default is only right for the usual workflow. Burning a mask onto a
    /// transparency that will be laid face-down wants the opposite of engraving the same layer
    /// directly, and a single-sided board laid out on the bottom copper wants neither.
    /// </summary>
    public bool? Mirrored { get; init; }

    /// <summary>
    /// SVG only: burn everything <em>except</em> this layer, out to the board edge.
    ///
    /// Two opposite jobs use the same geometry. Etching a painted board wants the laser to clear
    /// the resist off everywhere the acid should reach, which is the complement of the copper
    /// inside the board outline. Removing soldermask from pads wants the openings themselves. The
    /// shapes in the file are the same; which of them is the target is a fact about the process,
    /// so it has to be a choice.
    /// </summary>
    public bool Invert { get; init; }

    /// <summary>What this layer's mirror setting actually resolves to.</summary>
    public bool MirrorFor(LayerRole role) => Mirrored ?? LayerOperations.MirrorByDefault(role);
}

/// <summary>
/// Which operations a layer can sensibly become.
///
/// Not every pairing means something — a laser cannot drill, and engraving a soldermask is not an
/// operation — so the app offers what is meaningful and says plainly when a choice is not, rather
/// than silently producing an empty file.
/// </summary>
public static class LayerOperations
{
    public static OperationKind For(LayerRole role, OutputKind output) => (role, output) switch
    {
        (_, OutputKind.None) => OperationKind.None,

        (LayerRole.TopCopper or LayerRole.BottomCopper or LayerRole.InnerCopper, OutputKind.Gcode)
            => OperationKind.Isolation,
        (LayerRole.TopCopper or LayerRole.BottomCopper or LayerRole.InnerCopper, OutputKind.Svg)
            => OperationKind.MaskOpen,

        (LayerRole.PlatedDrill or LayerRole.NonPlatedDrill, OutputKind.Gcode) => OperationKind.Drilling,
        (LayerRole.PlatedDrill or LayerRole.NonPlatedDrill, OutputKind.Svg) => OperationKind.Vector,

        (LayerRole.Outline, OutputKind.Gcode) => OperationKind.Outline,
        (LayerRole.Outline, OutputKind.Svg) => OperationKind.Vector,

        (LayerRole.TopSilk or LayerRole.BottomSilk, OutputKind.Gcode) => OperationKind.Engrave,
        (LayerRole.TopSilk or LayerRole.BottomSilk, OutputKind.Svg) => OperationKind.Engrave,

        (LayerRole.TopMask or LayerRole.BottomMask, OutputKind.Svg) => OperationKind.MaskOpen,

        // The soldermask layer's openings are, by definition, everywhere the mask is not meant to
        // be — so milling them is the same operation as milling the paste apertures, over a
        // superset of the geometry: vias and test points have mask openings and no paste.
        (LayerRole.TopMask or LayerRole.BottomMask, OutputKind.Gcode) => OperationKind.Pocket,
        (LayerRole.TopPaste or LayerRole.BottomPaste, OutputKind.Svg) => OperationKind.MaskOpen,

        // Milling the applied soldermask off the pads. A real workflow, and the one operation here
        // that usually needs height mapping to work at all: the mask is tens of microns thick, so
        // the depth error a flat-looking board carries is the whole cut.
        (LayerRole.TopPaste or LayerRole.BottomPaste, OutputKind.Gcode) => OperationKind.Pocket,

        _ => OperationKind.None,
    };

    /// <summary>What this layer can usefully be turned into.</summary>
    public static IReadOnlyList<OutputKind> Available(LayerRole role) => role switch
    {
        LayerRole.PlatedDrill or LayerRole.NonPlatedDrill => [OutputKind.None, OutputKind.Gcode],
        LayerRole.TopMask or LayerRole.BottomMask
            => [OutputKind.None, OutputKind.Svg, OutputKind.Gcode],

        // Paste can be burned as a stencil or milled as mask relief.
        LayerRole.TopPaste or LayerRole.BottomPaste
            => [OutputKind.None, OutputKind.Svg, OutputKind.Gcode],
        // Drawings are for reading. Offering to burn or mill one is offering to make a mistake.
        LayerRole.Unknown or LayerRole.DrillMap or LayerRole.Documentation => [OutputKind.None],
        _ => [OutputKind.None, OutputKind.Svg, OutputKind.Gcode],
    };

    /// <summary>The output a layer gets by default, which is what most boards want.</summary>
    /// <summary>
    /// What a layer becomes on import, when nobody has said otherwise.
    ///
    /// Delegates to <see cref="ImportDefaults"/> so the answer is the operator's rather than ours.
    /// The parameterless form keeps the shipped defaults, which is what tests and the CLI want when
    /// no settings file is in play.
    /// </summary>
    public static OutputKind DefaultFor(LayerRole role, ImportDefaults? defaults = null) =>
        (defaults ?? ImportDefaults.Milling).For(role);

    /// <summary>
    /// Whether a layer is reflected unless the user says otherwise.
    ///
    /// Bottom-side layers are drawn as seen *through* the board, so producing them as they come
    /// gives a mirror image. The usual workflow turns the stock over, which means the usual answer
    /// is to mirror — but it is a workflow question, not a fact about the file, so it is a default
    /// rather than a rule.
    /// </summary>
    public static bool MirrorByDefault(LayerRole role) =>
        LayerRoleInfo.SideOf(role) == BoardSide.Bottom;

    /// <summary>
    /// How deep an operation goes unless it is told otherwise.
    ///
    /// Mask relief is the odd one: cured soldermask is 20-40 µm, so the depth that suits isolation
    /// goes through it and into the copper.
    /// </summary>
    public static long DefaultDepthNm(OperationKind operation) => operation switch
    {
        OperationKind.Pocket => Nm.FromMillimetres(0.035),
        _ => Nm.FromMillimetres(0.05),
    };

    /// <summary>Does this operation cut all the way through?</summary>
    public static bool GoesThrough(OperationKind operation) =>
        operation is OperationKind.Drilling or OperationKind.Outline;

    /// <summary>Which tool kind suits this operation, for filtering the dropdown.</summary>
    public static ToolKind? ToolKindFor(OperationKind operation) => operation switch
    {
        OperationKind.Isolation or OperationKind.Engrave => ToolKind.VBit,

        // Both kinds are used for mask relief in practice: a V-bit for its fine tip, a small flat
        // end mill for a level floor. Neither is wrong, so neither is filtered out.
        OperationKind.Pocket => null,
        OperationKind.Outline => ToolKind.EndMill,
        OperationKind.Drilling => ToolKind.Drill,
        _ => null,
    };

    /// <summary>
    /// The bit a layer is cut with when nobody has picked one — from the library first.
    ///
    /// One rule, called by both the planner and the board panel, so a board exported from the
    /// window and the same board exported from the command line are cut with the same tool. They
    /// used to disagree: the planner fell straight through to a hard-coded bit whenever a layer
    /// named none, which is every layer of a folder the window has never opened, while the drawer
    /// took the first suitable one in the operator's own library. On one real library that was a
    /// 0.127 mm cut against a 0.032 mm one — four isolation passes against fifteen, an hour against
    /// three — with both files looking entirely correct.
    ///
    /// **The built-in is preferred when the library holds it**, rather than simply taking the first
    /// of the right kind. Editing a shipped tool keeps its identity, so somebody who changed the tip
    /// on the 30° V-bit gets their version; and somebody who merely added a finer bit does not
    /// silently have every outline re-cut with whatever happens to sort first.
    ///
    /// The built-in is the last resort, for a library with nothing of the right kind in it at all. A
    /// plan that refused to exist because nobody had entered a drill yet would be worse than one
    /// that assumes a 1 mm drill and says so.
    /// </summary>
    public static Tool DefaultToolFor(OperationKind operation, IReadOnlyList<Tool> library)
    {
        ArgumentNullException.ThrowIfNull(library);

        var builtIn = operation switch
        {
            OperationKind.Outline => Tool.DefaultOutlineMill,
            OperationKind.Drilling => Tool.DefaultDrill,
            _ => Tool.DefaultVBit,
        };

        if (library.FirstOrDefault(t => t.Id == builtIn.Id) is { } owned)
        {
            return owned;
        }

        return ToolKindFor(operation) is { } kind
            && library.FirstOrDefault(t => t.Kind == kind) is { } suitable
                ? suitable
                : builtIn;
    }

    public static string Label(OperationKind operation) => operation switch
    {
        OperationKind.Isolation => "Isolation routing",
        OperationKind.Drilling => "Drilling",
        OperationKind.Outline => "Cut out",
        OperationKind.Engrave => "Engrave",
        OperationKind.MaskOpen => "Mask openings",
        OperationKind.Pocket => "Mask relief",
        OperationKind.Vector => "Vector outline",
        _ => "Not exported",
    };

    /// <summary>What a layer becomes, for a caller that wants to say it in a word.</summary>
    public static string ShortName(OperationKind operation) => operation switch
    {
        OperationKind.Isolation => "isolation",
        OperationKind.Drilling => "drilling",
        OperationKind.Outline => "cut out",
        OperationKind.Engrave => "engraving",
        OperationKind.MaskOpen => "openings",
        OperationKind.Vector => "outline",
        OperationKind.Pocket => "mask relief",
        _ => "nothing",
    };
}
