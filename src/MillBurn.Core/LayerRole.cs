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
    /// Whether the layer describes something that ends up on the finished board, as opposed to a
    /// picture of the board drawn for a person to read.
    ///
    /// The distinction matters wherever geometry is used as evidence rather than as a target. A
    /// drill map traces the board profile as part of its drawing, so anything asking "is there
    /// something here worth keeping?" gets the answer "yes, everywhere" if a drill map is in the
    /// pile — including inside the routed channels that are meant to be cut away.
    /// </summary>
    public static bool IsFabricated(LayerRole role) =>
        role is not (LayerRole.DrillMap or LayerRole.Documentation);

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

    /// <summary>
    /// Which part of the board a layer belongs to, and where that part sits in a list. Lower is
    /// higher up.
    ///
    /// Deliberately not <see cref="DrawOrder"/>. Paint order is back-to-front, which puts the
    /// bottom silkscreen at the top of the list and sets the two sides' soldermasks four rows
    /// apart with nothing to tell them apart but their labels. Reading order starts at the side
    /// most work happens on and never interleaves the two.
    /// </summary>
    public static (int Order, string Title) PanelGroup(LayerRole role) => role switch
    {
        LayerRole.TopCopper or LayerRole.TopMask or LayerRole.TopPaste or LayerRole.TopSilk
            => (0, "Top side"),

        LayerRole.InnerCopper => (1, "Inner layers"),

        LayerRole.BottomCopper or LayerRole.BottomMask or LayerRole.BottomPaste or LayerRole.BottomSilk
            => (2, "Bottom side"),

        LayerRole.PlatedDrill or LayerRole.NonPlatedDrill or LayerRole.Outline
            => (3, "Holes and outline"),

        _ => (4, "Other files"),
    };

    /// <summary>
    /// Where a layer sits within its group: the copper first, then what is printed on top of it.
    /// The copper is what gets cut, so it is what the pointer should land on.
    /// </summary>
    public static int PanelOrder(LayerRole role) => role switch
    {
        LayerRole.TopCopper or LayerRole.InnerCopper or LayerRole.BottomCopper => 0,
        LayerRole.TopMask or LayerRole.BottomMask => 1,
        LayerRole.TopPaste or LayerRole.BottomPaste => 2,
        LayerRole.TopSilk or LayerRole.BottomSilk => 3,

        LayerRole.PlatedDrill => 0,
        LayerRole.NonPlatedDrill => 1,
        LayerRole.Outline => 2,

        _ => 9,
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

    /// <summary>
    /// The label, made specific by the file name where the role alone cannot be.
    ///
    /// A board has two drill maps and KiCad writes both as plain <c>%TF.FileFunction,Drillmap*%</c>
    /// with nothing to say which holes each one charts, so the panel listed two rows called "Drill
    /// map" and neither said which was which. The file name is the only thing that tells them apart.
    ///
    /// It decides the label and nothing else. The role still comes from what the file declares —
    /// letting a name containing "PTH" win over a declared drill map is what once read a chart of
    /// the holes as 688 plated holes to drill.
    ///
    /// Reading the name is what works today, not what should work forever: the job file lists each
    /// drill file with the holes it covers, and once that is parsed it can say which map belongs to
    /// which, for the boards whose names carry no hint at all.
    /// </summary>
    /// <param name="role">The layer's role, as detected.</param>
    /// <param name="fileName">The file it was read from, used only to tell two drill maps apart.</param>
    public static string Label(LayerRole role, string? fileName)
    {
        if (role != LayerRole.DrillMap || string.IsNullOrEmpty(fileName))
        {
            return Label(role);
        }

        // NPTH before PTH, because the one contains the other.
        return fileName.Contains("NPTH", StringComparison.OrdinalIgnoreCase) ? "Non-plated drill map"
            : fileName.Contains("PTH", StringComparison.OrdinalIgnoreCase) ? "Plated drill map"
            : Label(role);
    }

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
