using System.Collections.Frozen;
using MillBurn.Core;

namespace MillBurn.Export;

/// <summary>Which consumer the SVG is shaped for.</summary>
public enum SvgFlavour
{
    /// <summary>
    /// The laser output. Colours are written on every element, because that is how the target
    /// assigns cut layers and a stylesheet is not guaranteed to survive the import.
    /// </summary>
    LightBurn,

    /// <summary>Human-facing: layer groups, CSS classes, one editable palette.</summary>
    Inkscape,
}

/// <summary>
/// The role-to-colour map for one target.
///
/// For LightBurn the colours are not decoration: an imported object is assigned to the cut layer
/// whose palette colour its stroke matches, so these RGB values are effectively a wire format.
/// They live in data rather than in the writer so a profile can correct them without a rebuild —
/// **verify them against the installed version** rather than trusting this table
/// (Documentation/04, section 2.3).
/// </summary>
public sealed record SvgProfile
{
    public required SvgFlavour Flavour { get; init; }

    public required FrozenDictionary<ArtRole, string> Colours { get; init; }

    /// <summary>Names shown next to each colour, e.g. LightBurn's "C01". Purely informational.</summary>
    public required FrozenDictionary<ArtRole, string> LayerNames { get; init; }

    public string ColourFor(ArtRole role) =>
        Colours.TryGetValue(role, out var c) ? c : "#000000";

    public string LayerNameFor(ArtRole role) =>
        LayerNames.TryGetValue(role, out var n) ? n : role.ToString();

    /// <summary>
    /// LightBurn's first palette entries. C00 black, C01 blue, C02 red, C03 green, C08 grey.
    /// </summary>
    public static SvgProfile LightBurn { get; } = new()
    {
        Flavour = SvgFlavour.LightBurn,
        Colours = new Dictionary<ArtRole, string>
        {
            [ArtRole.Registration] = "#000000",
            [ArtRole.Fill] = "#0000FF",
            [ArtRole.Boundary] = "#FF0000",
            [ArtRole.Mark] = "#00E000",
            [ArtRole.Reference] = "#B4B4B4",
        }.ToFrozenDictionary(),
        LayerNames = new Dictionary<ArtRole, string>
        {
            [ArtRole.Registration] = "C00",
            [ArtRole.Fill] = "C01",
            [ArtRole.Boundary] = "C02",
            [ArtRole.Mark] = "C03",
            [ArtRole.Reference] = "C08",
        }.ToFrozenDictionary(),
    };

    /// <summary>A legible palette for reading the drawing rather than burning it.</summary>
    public static SvgProfile Inkscape { get; } = new()
    {
        Flavour = SvgFlavour.Inkscape,
        Colours = new Dictionary<ArtRole, string>
        {
            [ArtRole.Registration] = "#111111",
            [ArtRole.Fill] = "#1E88E5",
            [ArtRole.Boundary] = "#E53935",
            [ArtRole.Mark] = "#2E7D32",
            [ArtRole.Reference] = "#9E9E9E",
        }.ToFrozenDictionary(),
        LayerNames = new Dictionary<ArtRole, string>
        {
            [ArtRole.Registration] = "registration",
            [ArtRole.Fill] = "fill",
            [ArtRole.Boundary] = "boundary",
            [ArtRole.Mark] = "mark",
            [ArtRole.Reference] = "reference",
        }.ToFrozenDictionary(),
    };
}

/// <summary>Everything about an export that is not the geometry.</summary>
public sealed record SvgExportOptions
{
    public SvgProfile Profile { get; init; } = SvgProfile.LightBurn;

    /// <summary>Mirror about the page's vertical centreline, for bottom-side layers.</summary>
    public bool Mirror { get; init; }

    /// <summary>
    /// Collapse the whole drawing into one group and as few path elements as possible.
    ///
    /// Some laser software creates one of its own cut layers per imported object, which turns a
    /// board into hundreds of layers — one per pad — each needing its power and speed set by hand.
    /// That makes the file unusable, and it is not a hypothetical: Creality Falcon does it.
    ///
    /// So this mode emits a single group with no Inkscape layer markup, one colour, one filled
    /// path, and one stroked path. It is the opposite of what LightBurn wants — there, colour *is*
    /// the layer assignment and separate layers are the point — so the two are alternatives, not
    /// defaults, and the export report says how many elements were written either way.
    /// </summary>
    public bool SingleLayer { get; init; }

    /// <summary>
    /// Written into the file's description. Null omits it, which is what the golden tests use —
    /// a timestamp is the one thing that would stop two runs producing identical bytes.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }

    public string? Title { get; init; }

    /// <summary>
    /// Layers written after the drawing, each in a colour of its own, for placing the file rather
    /// than for burning: the board outline, the stock and its holes. See <see cref="SvgReference"/>.
    /// </summary>
    public IReadOnlyList<SvgReference> References { get; init; } = [];
}

/// <summary>
/// A layer that is there to place the drawing, not to be burned.
///
/// Laser software that imports by content keeps only the drawing's own box, and every layer's box
/// is different — a mask is its outermost openings, a legend stops short of the edge. Drawing the
/// same outline into every file makes that box the same for all of them, so one placement works
/// for each. Tried by hand in Creality Falcon and LightBurn before it was built: both import each
/// colour as a layer of its own, and switching one off does not move the rest.
///
/// Always a real layer with an explicit colour, even in <see cref="SvgExportOptions.SingleLayer"/>
/// mode — being a separate, visible, switch-off-able layer is the whole point of it.
/// </summary>
/// <param name="Id">The SVG element id.</param>
/// <param name="Label">The layer's name, as the laser software shows it.</param>
/// <param name="Colour">A colour the drawing does not use, so it arrives as its own layer.</param>
/// <param name="Shapes">Hairlines, in source coordinates.</param>
public sealed record SvgReference(string Id, string Label, string Colour, IReadOnlyList<ArtShape> Shapes);
