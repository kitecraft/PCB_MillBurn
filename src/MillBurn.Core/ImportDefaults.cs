using System.Text.Json.Serialization;

namespace MillBurn.Core;

/// <summary>
/// What each kind of layer becomes when a folder is first imported.
///
/// Held per *family* rather than per role, because nobody decides differently about top copper and
/// bottom copper — they decide about copper. Seven families is a settings page somebody will read;
/// thirteen roles is a form they will skip.
///
/// The whole point of making these settings is that the right answer is not the same for everybody.
/// Someone milling wants copper as G-code; someone laser-etching wants the same layer as SVG. The
/// shipped defaults suit milling because that is what this app is first — and one visit here makes
/// them permanently something else.
/// </summary>
public sealed record ImportDefaults
{
    /// <summary>Top and bottom copper. Isolation routing as G-code, or artwork as SVG.</summary>
    public OutputKind Copper { get; init; } = OutputKind.Gcode;

    /// <summary>
    /// Plated and non-plated holes.
    /// </summary>
    public OutputKind Drills { get; init; } = OutputKind.Gcode;

    /// <summary>The board profile. Cut out as G-code, or drawn as SVG.</summary>
    public OutputKind Outline { get; init; } = OutputKind.Gcode;

    /// <summary>
    /// Soldermask. G-code mills the mask off the pads; SVG burns the openings.
    ///
    /// Off by default. Mask relief is a real workflow but a minority one, and it is the operation
    /// that most needs height mapping to work at all — so producing a program for it on every
    /// import would put a file in front of people that most of them should not run.
    /// </summary>
    public OutputKind Mask { get; init; } = OutputKind.None;

    /// <summary>Silkscreen. Engraved by the mill, or burned by the laser.</summary>
    public OutputKind Silk { get; init; } = OutputKind.None;

    /// <summary>Solder paste apertures.</summary>
    public OutputKind Paste { get; init; } = OutputKind.None;

    /// <summary>
    /// Inner copper, and always <see cref="OutputKind.None"/>.
    ///
    /// Not a setting, and deliberately so: a cutter cannot reach a layer inside the board. The
    /// layers are still read, drawn and listed — they are part of the design and worth seeing —
    /// but nothing this app makes can produce one.
    /// </summary>
    [JsonIgnore]
    public static OutputKind Inner => OutputKind.None;

    /// <summary>Everything set up to mill a board: copper, holes and the profile.</summary>
    public static ImportDefaults Milling { get; } = new();

    /// <summary>
    /// Copper as artwork for the laser, with the holes and the profile still milled.
    ///
    /// The board is still cut and drilled on the mill — what changes is that the copper becomes a
    /// drawing to burn a resist with rather than a path to cut.
    /// </summary>
    public static ImportDefaults LaserEtching { get; } = new()
    {
        Copper = OutputKind.Svg,
        Silk = OutputKind.Svg,
    };

    /// <summary>Nothing on. For somebody who would rather choose every layer themselves.</summary>
    public static ImportDefaults Nothing { get; } = new()
    {
        Copper = OutputKind.None,
        Drills = OutputKind.None,
        Outline = OutputKind.None,
    };

    /// <summary>What a layer of this role becomes on import.</summary>
    public OutputKind For(LayerRole role) => role switch
    {
        LayerRole.TopCopper or LayerRole.BottomCopper => Copper,
        LayerRole.InnerCopper => Inner,
        LayerRole.PlatedDrill or LayerRole.NonPlatedDrill => Drills,
        LayerRole.Outline => Outline,
        LayerRole.TopMask or LayerRole.BottomMask => Mask,
        LayerRole.TopSilk or LayerRole.BottomSilk => Silk,
        LayerRole.TopPaste or LayerRole.BottomPaste => Paste,

        // A drill map and a fabrication drawing are documents. Nothing makes them.
        _ => OutputKind.None,
    };

    /// <summary>Which of the shipped presets this matches, or null when it is somebody's own mix.</summary>
    public string? PresetName => this switch
    {
        _ when this == Milling => "Milling",
        _ when this == LaserEtching => "Laser etching",
        _ when this == Nothing => "Nothing",
        _ => null,
    };
}
