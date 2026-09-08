using MillBurn.Core;

namespace MillBurn.Pipeline;

/// <summary>What a layer is turned into, if anything.</summary>
public enum OutputKind
{
    /// <summary>Shown, but not cut or exported.</summary>
    None,

    /// <summary>Vectors for a laser program.</summary>
    Svg,

    /// <summary>A program for the mill.</summary>
    Gcode,
}

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

    /// <summary>Isolation only: how deep, which for a V-bit is the same as how wide.</summary>
    public long DepthNm { get; init; } = Nm.FromMillimetres(0.05);

    /// <summary>Isolation only.</summary>
    public int Passes { get; init; } = 1;

    /// <summary>Outline only. Zero cuts the board fully free.</summary>
    public int TabCount { get; init; } = 4;

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
        (LayerRole.TopPaste or LayerRole.BottomPaste, OutputKind.Svg) => OperationKind.MaskOpen,

        _ => OperationKind.None,
    };

    /// <summary>What this layer can usefully be turned into.</summary>
    public static IReadOnlyList<OutputKind> Available(LayerRole role) => role switch
    {
        LayerRole.PlatedDrill or LayerRole.NonPlatedDrill => [OutputKind.None, OutputKind.Gcode],
        LayerRole.TopMask or LayerRole.BottomMask or LayerRole.TopPaste or LayerRole.BottomPaste
            => [OutputKind.None, OutputKind.Svg],
        LayerRole.Unknown => [OutputKind.None],
        _ => [OutputKind.None, OutputKind.Svg, OutputKind.Gcode],
    };

    /// <summary>The output a layer gets by default, which is what most boards want.</summary>
    public static OutputKind DefaultFor(LayerRole role) => role switch
    {
        LayerRole.TopCopper => OutputKind.Gcode,
        LayerRole.PlatedDrill or LayerRole.NonPlatedDrill => OutputKind.Gcode,
        LayerRole.Outline => OutputKind.Gcode,
        _ => OutputKind.None,
    };

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

    /// <summary>Does this operation cut all the way through?</summary>
    public static bool GoesThrough(OperationKind operation) =>
        operation is OperationKind.Drilling or OperationKind.Outline;

    /// <summary>Which tool kind suits this operation, for filtering the dropdown.</summary>
    public static ToolKind? ToolKindFor(OperationKind operation) => operation switch
    {
        OperationKind.Isolation or OperationKind.Engrave => ToolKind.VBit,
        OperationKind.Outline => ToolKind.EndMill,
        OperationKind.Drilling => ToolKind.Drill,
        _ => null,
    };

    public static string Label(OperationKind operation) => operation switch
    {
        OperationKind.Isolation => "Isolation routing",
        OperationKind.Drilling => "Drilling",
        OperationKind.Outline => "Cut out",
        OperationKind.Engrave => "Engrave",
        OperationKind.MaskOpen => "Mask openings",
        OperationKind.Vector => "Vector outline",
        _ => "Not exported",
    };

    /// <summary>
    /// The short tag that goes in an exported filename.
    ///
    /// Output lands in the same folder as the Gerbers often enough that it has to be obvious at a
    /// glance which files a machine should be fed and which came from the EDA tool.
    /// </summary>
    public static string FileTag(OperationKind operation) => operation switch
    {
        OperationKind.Isolation => "iso",
        OperationKind.Drilling => "drill",
        OperationKind.Outline => "cutout",
        OperationKind.Engrave => "engrave",
        OperationKind.MaskOpen => "mask",
        OperationKind.Vector => "vector",
        _ => "out",
    };
}
