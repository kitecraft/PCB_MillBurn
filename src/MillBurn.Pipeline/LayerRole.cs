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

        // A file that says what it is has not been guessed at — including when what it says is
        // "I am a drawing". Falling through to the filename is how "PogoTest1-PTH-drl_map.gbr"
        // became 688 plated holes: the name said PTH, the file said drill map, and the name won.
        var declared = FromFileFunction(image.FileFunction);

        return declared != LayerRole.Unknown
            ? (declared, false)
            : (FromFileName(fileName), true);
    }

    /// <summary>
    /// Functions that describe documentation rather than a layer to be made.
    ///
    /// Listed rather than inferred, because the cost of getting this wrong is asymmetric: treating
    /// a real layer as documentation loses it visibly, while treating documentation as a real layer
    /// cuts it.
    /// </summary>
    public static bool DeclaresNonBoardFunction(string? fileFunction)
    {
        if (string.IsNullOrWhiteSpace(fileFunction))
        {
            return false;
        }

        var kind = fileFunction.Split(',', StringSplitOptions.TrimEntries)[0];

        return kind.Equals("Drillmap", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("FabricationDrawing", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("AssemblyDrawing", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("ArrayDrawing", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("OtherDrawing", StringComparison.OrdinalIgnoreCase)

            // "Other" is the standard's own catch-all for a file that is none of the board layers
            // it defines, and it is what KiCad writes for courtyards, comments and the rest of the
            // drawing layers — "Other,Comment", "Other,User". Every layer that does get made says
            // so with a function of its own, so nothing that is cut can land here.
            || kind.Equals("Other", StringComparison.OrdinalIgnoreCase);
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

        // A drill map is a chart of the holes, not the holes: symbols and text showing an operator
        // where they go. Reading it as a drill layer would put several hundred holes through the
        // legend, so it is named explicitly rather than left to fall through to a filename guess.
        if (kind.Equals("Drillmap", StringComparison.OrdinalIgnoreCase))
        {
            return LayerRole.DrillMap;
        }

        if (DeclaresNonBoardFunction(fileFunction))
        {
            return LayerRole.Documentation;
        }

        // KiCad can write drill files as Gerber X2 instead of Excellon: same holes, same
        // attributes, different container. The file function is the only reliable way to tell,
        // because the extension is .gbr like everything else.
        if (fields.Contains("Drill", StringComparer.OrdinalIgnoreCase))
        {
            return kind.Equals("NonPlated", StringComparison.OrdinalIgnoreCase)
                ? LayerRole.NonPlatedDrill
                : LayerRole.PlatedDrill;
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
    /// The fallback, for a file that carries no attribute saying what it is.
    ///
    /// The name is split into words on `_`, `-`, `.` and spaces, and whole words are matched — not
    /// substrings. That is not tidiness: `Top_Mask` contains `Top`, so a substring rule that knows
    /// bare `Top` means copper turns every mask, silk and paste layer into copper, and copper is
    /// the layer that gets cut. Whole words cannot make that mistake.
    ///
    /// Two vocabularies meet here. KiCad writes the side as `F` and `B` and the kind as `Cu`,
    /// `Mask`, `Silkscreen`; Altium, Eagle and Olimex write `Top` and `Bot` and `Dimension`, and
    /// name the copper layer with the side alone. Both are read, and anything in neither stays
    /// <see cref="LayerRole.Unknown"/> — widening what can be read is not the same as widening what
    /// gets guessed.
    /// </summary>
    public static LayerRole FromFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName).TrimStart('.');
        var words = name.Split(['_', '-', '.', ' '], StringSplitOptions.RemoveEmptyEntries);

        // A chart of the holes is not the holes. "Drill" appears in the name of both, so the map
        // has to be recognised before the drill rule claims it and turns a legend and its dimension
        // lines into several hundred drill targets.
        var charted = Array.Exists(words, DrawingWords.Contains);

        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];

            if (DrillWords.TryGetValue(word, out var drill))
            {
                return charted ? LayerRole.DrillMap : drill;
            }

            if (IsOutlineWord(word))
            {
                return LayerRole.Outline;
            }

            if (KindWords.TryGetValue(word, out var kind))
            {
                var side = SideNear(words, i);

                return kind switch
                {
                    // Copper is the one kind with a third answer: a layer that names a number
                    // instead of a face — `In2`, `Ln3`, `L4` — is inside the board. Copper that
                    // names neither is still Unknown, and deliberately. Reading it as inner would
                    // be tidier to look at and worse to use: inner copper is never exported, so the
                    // layer would sit there looking recognised and produce nothing, where Unknown
                    // says plainly that nobody could read it and can be corrected on the row.
                    LayerKind.Copper => side switch
                    {
                        NamedSide.Top => LayerRole.TopCopper,
                        NamedSide.Bottom => LayerRole.BottomCopper,
                        NamedSide.Inner => LayerRole.InnerCopper,
                        _ => LayerRole.Unknown,
                    },

                    // The rest need one. A mask whose face nobody stated is not a top mask; it is a
                    // mask we cannot place, and guessing "top" would etch a bottom layer on the
                    // front of the board and look entirely normal doing it.
                    LayerKind.Mask => side switch
                    {
                        NamedSide.Top => LayerRole.TopMask,
                        NamedSide.Bottom => LayerRole.BottomMask,
                        _ => LayerRole.Unknown,
                    },
                    LayerKind.Silk => side switch
                    {
                        NamedSide.Top => LayerRole.TopSilk,
                        NamedSide.Bottom => LayerRole.BottomSilk,
                        _ => LayerRole.Unknown,
                    },
                    _ => side switch
                    {
                        NamedSide.Top => LayerRole.TopPaste,
                        NamedSide.Bottom => LayerRole.BottomPaste,
                        _ => LayerRole.Unknown,
                    },
                };
            }
        }

        // No word said what kind of layer this is. A name that is *nothing but* the side —
        // `Top.gbr`, `Bot.gbr` — is the copper itself, which is how the Top/Bot exporters write it.
        // One word, not merely a word at the end: `Assembly_Top.gbr` and `Panel_Top.gbr` are
        // drawings, and cutting a drawing as copper is the mistake this whole method is shaped to
        // avoid.
        if (words.Length == 1 && SideNear(words, 1) is var only
            && only is NamedSide.Top or NamedSide.Bottom)
        {
            return only == NamedSide.Top ? LayerRole.TopCopper : LayerRole.BottomCopper;
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
            "DRL" or "XLN" or "XNC" or "TXT" => LayerRole.PlatedDrill,
            _ => LayerRole.Unknown,
        };
    }

    /// <summary>What a layer is, before a side is put to it.</summary>
    private enum LayerKind
    {
        Copper,
        Mask,
        Silk,
        Paste,
    }

    /// <summary>
    /// The side, from whichever word says it.
    ///
    /// `Ln1` is Olimex and Altium for layer one of the stack, where KiCad writes `In1` for inner
    /// one; both mean a layer no cutter can reach, and neither is the top or the bottom.
    /// </summary>
    /// <summary>A side as a name states it, including the two ways of not stating one.</summary>
    private enum NamedSide
    {
        /// <summary>No word in the name says which face this is.</summary>
        Unstated,
        Top,
        Bottom,

        /// <summary>Stated, and stated to be inside the board — `In2`, `Ln3`.</summary>
        Inner,
    }

    /// <summary>
    /// The side belonging to the word at <paramref name="kindIndex"/>.
    ///
    /// Backwards first, because that is where the side normally sits — `F_Cu`, `Top_Mask` — and
    /// taking the nearest one means a board whose own name contains an `F` or a `B` cannot steal
    /// the answer from the word that was actually describing the layer. Then forwards, because
    /// Eagle and several Altium templates write it the other way round, and `Copper_Top` read as an
    /// inner layer is the top copper of a board silently exporting nothing at all.
    /// </summary>
    /// <param name="words">The name, split into words.</param>
    /// <param name="kindIndex">Where the word naming the kind sits.</param>
    private static NamedSide SideNear(string[] words, int kindIndex)
    {
        for (var i = Math.Min(kindIndex, words.Length) - 1; i >= 0; i--)
        {
            if (SideOf(words[i]) is var behind && behind != NamedSide.Unstated)
            {
                return behind;
            }
        }

        for (var i = kindIndex + 1; i < words.Length; i++)
        {
            if (SideOf(words[i]) is var ahead && ahead != NamedSide.Unstated)
            {
                return ahead;
            }
        }

        return NamedSide.Unstated;
    }

    private static NamedSide SideOf(string word)
    {
        if (TopWords.Contains(word))
        {
            return NamedSide.Top;
        }

        if (BottomWords.Contains(word))
        {
            return NamedSide.Bottom;
        }

        // `In1`, `Ln4`, `L2` — a numbered layer of the stack, which is inside the board by
        // definition. Altium writes the bare `L` form, and a board whose inner layers read as
        // nothing at all is one where the operator has to work out for themselves why four of its
        // files have no role.
        if (word.Length > 2
            && (word.StartsWith("In", StringComparison.OrdinalIgnoreCase)
                || word.StartsWith("Ln", StringComparison.OrdinalIgnoreCase))
            && char.IsAsciiDigit(word[2]))
        {
            return NamedSide.Inner;
        }

        return word.Length > 1
            && (word[0] == 'L' || word[0] == 'l')
            && char.IsAsciiDigit(word[1])
                ? NamedSide.Inner
                : NamedSide.Unstated;
    }

    /// <summary>
    /// Whether a word names the board's edge.
    ///
    /// The suffix forms matter as much as the exact ones: a panelising tool writes `PanelOutline`
    /// and `BoardProfile` as single words, and an outline that reads as nothing is a board that
    /// never gets cut free, with nothing said about why.
    /// </summary>
    private static bool IsOutlineWord(string word) =>
        OutlineWords.Contains(word)
        || word.EndsWith("Outline", StringComparison.OrdinalIgnoreCase)
        || word.EndsWith("Profile", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> TopWords =
        new(StringComparer.OrdinalIgnoreCase) { "F", "Front", "Top" };

    private static readonly HashSet<string> BottomWords =
        new(StringComparer.OrdinalIgnoreCase) { "B", "Back", "Bot", "Bottom" };

    private static readonly HashSet<string> OutlineWords =
        new(StringComparer.OrdinalIgnoreCase) { "Cuts", "EdgeCuts", "Dimension", "Contour", "GKO" };

    /// <summary>
    /// Words that make a file a picture of something rather than the thing.
    ///
    /// A drill map carries the word "drill" as surely as a drill file does, and it is the one that
    /// must not be read as holes.
    /// </summary>
    private static readonly HashSet<string> DrawingWords =
        new(StringComparer.OrdinalIgnoreCase) { "Map", "Drawing", "Chart", "Report" };

    /// <summary>
    /// Drills are read before anything else, because `NPTH` and `PTH` say both the kind and that
    /// there is no side to look for.
    /// </summary>
    private static readonly Dictionary<string, LayerRole> DrillWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["NPTH"] = LayerRole.NonPlatedDrill,
            ["PTH"] = LayerRole.PlatedDrill,
            ["Drill"] = LayerRole.PlatedDrill,
            ["Drills"] = LayerRole.PlatedDrill,
        };

    private static readonly Dictionary<string, LayerKind> KindWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cu"] = LayerKind.Copper,
            ["Copper"] = LayerKind.Copper,
            ["Mask"] = LayerKind.Mask,
            ["Soldermask"] = LayerKind.Mask,
            ["Silk"] = LayerKind.Silk,
            ["SilkS"] = LayerKind.Silk,
            ["Silkscreen"] = LayerKind.Silk,
            ["Legend"] = LayerKind.Silk,
            ["Paste"] = LayerKind.Paste,
            ["Solderpaste"] = LayerKind.Paste,
        };

}
