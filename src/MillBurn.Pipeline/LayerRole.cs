using MillBurn.Core;
using MillBurn.Gerber.Model;

namespace MillBurn.Pipeline;

/// <summary>
/// Works out what each exported file is.
///
/// **The X2 <c>.FileFunction</c> attribute is asked first, and it is not a nicety.** Filename
/// conventions are a guess dressed up as a rule: <c>.gbl</c> means bottom copper to one tool and
/// "Gerber bottom layer" to another that also writes <c>.gbo</c> for silk; Altium, Eagle and
/// KiCad each name the same layer differently; and a user who renames a file to something tidy
/// silently changes what the tool believes it is. Getting the role wrong routes the wrong geometry
/// to the wrong operation, which mills traces where the outline should be.
///
/// So filename matching exists only as a fallback for files with no attributes, and when it is
/// used the caller is told, because a guess should look like a guess.
/// </summary>
public static class LayerRoles
{
    /// <summary>
    /// The role, and whether it had to be guessed from the filename.
    /// </summary>
    public static (LayerRole Role, bool Guessed) Detect(GerberImage image, string fileName)
    {
        ArgumentNullException.ThrowIfNull(image);

        var declared = FromFileFunction(image.FileFunction);
        return declared != LayerRole.Unknown
            ? (declared, false)
            : (FromFileName(fileName), true);
    }

    /// <summary>
    /// Reads <c>%TF.FileFunction%</c>, e.g. <c>Copper,L1,Top</c>, <c>Soldermask,Bot</c>,
    /// <c>Profile,NP</c>.
    ///
    /// The side comes from the <c>Top</c>/<c>Bot</c> field and never from the layer number: on a
    /// four-layer board <c>Copper,L2,Inr</c> is an inner layer, and treating "L2" as "the bottom"
    /// because it does on a two-layer board is exactly the kind of assumption that survives every
    /// test until someone opens a real stackup.
    /// </summary>
    public static LayerRole FromFileFunction(string? fileFunction)
    {
        if (string.IsNullOrWhiteSpace(fileFunction))
        {
            return LayerRole.Unknown;
        }

        var fields = fileFunction.Split(',', StringSplitOptions.TrimEntries);
        var kind = fields[0];

        if (kind.Equals("Profile", StringComparison.OrdinalIgnoreCase))
        {
            return LayerRole.Outline;
        }

        var side = SideField(fields);

        return kind.ToUpperInvariant() switch
        {
            "COPPER" => side switch
            {
                BoardSide.Top => LayerRole.TopCopper,
                BoardSide.Bottom => LayerRole.BottomCopper,
                _ => LayerRole.InnerCopper,
            },
            "SOLDERMASK" => side == BoardSide.Bottom ? LayerRole.BottomMask : LayerRole.TopMask,
            "LEGEND" => side == BoardSide.Bottom ? LayerRole.BottomSilk : LayerRole.TopSilk,
            "PASTE" => side == BoardSide.Bottom ? LayerRole.BottomPaste : LayerRole.TopPaste,
            _ => LayerRole.Unknown,
        };
    }

    private static BoardSide SideField(string[] fields)
    {
        foreach (var field in fields)
        {
            if (field.Equals("Top", StringComparison.OrdinalIgnoreCase))
            {
                return BoardSide.Top;
            }

            if (field.Equals("Bot", StringComparison.OrdinalIgnoreCase))
            {
                return BoardSide.Bottom;
            }
        }

        return BoardSide.None;
    }

    /// <summary>
    /// The fallback. Matches the long KiCad names first, then the classic three-letter extensions,
    /// then the looser abbreviations — most specific first, because "gbl" is a substring of
    /// nothing but "F_Cu" and "B_Cu" differ by one character.
    /// </summary>
    public static LayerRole FromFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName).TrimStart('.');

        foreach (var (pattern, role) in NamePatterns)
        {
            if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return role;
            }
        }

        return extension.ToUpperInvariant() switch
        {
            "GTL" => LayerRole.TopCopper,
            "GBL" => LayerRole.BottomCopper,
            "GTS" => LayerRole.TopMask,
            "GBS" => LayerRole.BottomMask,
            "GTO" => LayerRole.TopSilk,
            "GBO" => LayerRole.BottomSilk,
            "GTP" => LayerRole.TopPaste,
            "GBP" => LayerRole.BottomPaste,
            "GKO" or "GM1" => LayerRole.Outline,
            "DRL" or "XLN" or "TXT" => LayerRole.PlatedDrill,
            _ => LayerRole.Unknown,
        };
    }

    private static readonly (string Pattern, LayerRole Role)[] NamePatterns =
    [
        ("Edge_Cuts", LayerRole.Outline),
        ("EdgeCuts", LayerRole.Outline),
        ("F_Silkscreen", LayerRole.TopSilk),
        ("B_Silkscreen", LayerRole.BottomSilk),
        ("F_SilkS", LayerRole.TopSilk),
        ("B_SilkS", LayerRole.BottomSilk),
        ("F_Mask", LayerRole.TopMask),
        ("B_Mask", LayerRole.BottomMask),
        ("F_Paste", LayerRole.TopPaste),
        ("B_Paste", LayerRole.BottomPaste),
        ("F_Cu", LayerRole.TopCopper),
        ("B_Cu", LayerRole.BottomCopper),
        ("In1_Cu", LayerRole.InnerCopper),
        ("In2_Cu", LayerRole.InnerCopper),
        ("NPTH", LayerRole.NonPlatedDrill),
        ("PTH", LayerRole.PlatedDrill),
        ("outline", LayerRole.Outline),
        ("profile", LayerRole.Outline),
    ];
}
