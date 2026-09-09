namespace MillBurn.Core;

/// <summary>What a board file is for. Everything downstream branches on this.</summary>
public enum LayerRole
{
    Unknown,
    TopCopper,
    InnerCopper,
    BottomCopper,
    TopMask,
    BottomMask,
    TopSilk,
    BottomSilk,
    TopPaste,
    BottomPaste,
    Outline,
    PlatedDrill,
    NonPlatedDrill,

    /// <summary>
    /// A chart of where the holes go: symbols, a legend and text, for a person to read.
    ///
    /// A separate role from <see cref="Unknown"/> because it is not unknown at all — the file says
    /// what it is. Calling it unknown makes an identified thing look like a failure to identify,
    /// and leaves the operator wondering which of their layers went wrong.
    /// </summary>
    DrillMap,

    /// <summary>
    /// A fabrication, assembly or array drawing. Documentation for a person, not a layer to make.
    /// </summary>
    Documentation,
}

/// <summary>Which physical side a layer belongs to, for mirroring and for grouping the UI.</summary>
public enum BoardSide
{
    None,
    Top,
    Bottom,
}

/// <summary>
/// Facts about a role that need no Gerber and no renderer, so the loader, the viewer, the CLI and
/// the exporters all agree without one of them owning the others.
/// </summary>
public static class LayerRoleInfo
{
    public static BoardSide SideOf(LayerRole role) => role switch
    {
        LayerRole.TopCopper or LayerRole.TopMask or LayerRole.TopSilk or LayerRole.TopPaste => BoardSide.Top,
        LayerRole.BottomCopper or LayerRole.BottomMask or LayerRole.BottomSilk or LayerRole.BottomPaste => BoardSide.Bottom,
        _ => BoardSide.None,
    };

    public static bool IsCopper(LayerRole role) =>
        role is LayerRole.TopCopper or LayerRole.InnerCopper or LayerRole.BottomCopper;

    public static bool IsDrill(LayerRole role) =>
        role is LayerRole.PlatedDrill or LayerRole.NonPlatedDrill;

    /// <summary>
    /// Back-to-front paint order. Bottom-side layers first, then copper, then what is printed on
    /// top of it, then the holes, and the outline last so it frames everything. Painting copper
    /// over silk would hide the legend it is printed on.
    /// </summary>
    public static int DrawOrder(LayerRole role) => role switch
    {
        LayerRole.BottomSilk => 0,
        LayerRole.BottomPaste => 1,
        LayerRole.BottomMask => 2,
        LayerRole.BottomCopper => 3,
        LayerRole.InnerCopper => 4,
        LayerRole.TopCopper => 5,
        LayerRole.TopMask => 6,
        LayerRole.TopPaste => 7,
        LayerRole.TopSilk => 8,
        LayerRole.PlatedDrill => 9,
        LayerRole.NonPlatedDrill => 10,
        LayerRole.Outline => 11,

        // Drawings last of all, on the rare occasion anyone turns one on.
        LayerRole.DrillMap => 13,
        LayerRole.Documentation => 14,
        _ => 12,
    };

    public static string Label(LayerRole role) => role switch
    {
        LayerRole.TopCopper => "Top copper",
        LayerRole.InnerCopper => "Inner copper",
        LayerRole.BottomCopper => "Bottom copper",
        LayerRole.TopMask => "Top soldermask",
        LayerRole.BottomMask => "Bottom soldermask",
        LayerRole.TopSilk => "Top silkscreen",
        LayerRole.BottomSilk => "Bottom silkscreen",
        LayerRole.DrillMap => "Drill map",
        LayerRole.Documentation => "Documentation",
        LayerRole.TopPaste => "Top paste",
        LayerRole.BottomPaste => "Bottom paste",
        LayerRole.Outline => "Board outline",
        LayerRole.PlatedDrill => "Plated holes",
        LayerRole.NonPlatedDrill => "Non-plated holes",
        _ => "Unknown",
    };

    /// <summary>Which layers are on by default: enough to recognise the board, not everything.</summary>
    public static bool VisibleByDefault(LayerRole role) => role switch
    {
        LayerRole.BottomSilk or LayerRole.BottomPaste or LayerRole.TopPaste => false,
        LayerRole.BottomMask or LayerRole.TopMask => false,

        // Off by default. A drill map is several hundred symbols and a page of text; drawn over the
        // board it hides the thing it is describing.
        LayerRole.DrillMap or LayerRole.Documentation => false,
        _ => true,
    };
}
