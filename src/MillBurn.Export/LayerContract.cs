using MillBurn.Core;

namespace MillBurn.Export;

/// <summary>How a laser program burns what is on a layer. The SVG cannot say; the layer does.</summary>
public enum LaserMode
{
    /// <summary>Scanned back and forth to clear the inside of a closed shape. LightBurn's "Fill".</summary>
    Fill,

    /// <summary>Traced along the path. LightBurn's "Line".</summary>
    Line,
}

/// <summary>
/// What one role's layer is for, and how it is set up once: Documentation/04 §2.3.
///
/// **One table for both of the places that state it** — each SVG's own description, and the project
/// page's *Laser layers* table. Two copies of a contract are two things that can disagree, and the one
/// the operator reads on the page is then not the one in the file.
/// </summary>
/// <param name="Role">The geometry this layer receives.</param>
/// <param name="Description">The sentence the SVG's description carries, after the layer's name.</param>
/// <param name="Mode">Fill or Line.</param>
/// <param name="Output">
/// Whether the layer burns at all. Off for the two that are there to be looked at: the board outline,
/// and the stock with its holes, which are on one layer and whose rectangle is the edge of the piece.
/// </param>
/// <param name="Name">What is on it, in a few words, for the page's table.</param>
public sealed record LayerContract(ArtRole Role, string Description, LaserMode Mode, bool Output, string Name)
{
    /// <summary>Every role, in run order.</summary>
    public static IReadOnlyList<LayerContract> All { get; } =
    [
        new(ArtRole.Boundary, "the edges of those regions; Line, low power, run first", LaserMode.Line, true, "edges, run first"),
        new(ArtRole.Fill, "regions to clear; set to Fill", LaserMode.Fill, true, "regions to clear"),
        new(ArtRole.Mark, "centrelines; Line", LaserMode.Line, true, "centrelines"),
        new(ArtRole.Registration, "for placing and registering the work, not for burning; set not to output", LaserMode.Line, false, "stock and holes, not burned"),
        new(ArtRole.Reference, "for reference only; set not to output", LaserMode.Line, false, "board outline, not burned"),
    ];

    public static LayerContract For(ArtRole role) =>
        All.FirstOrDefault(c => c.Role == role)
        ?? throw new ArgumentOutOfRangeException(nameof(role), role, "No layer contract for this role.");
}
