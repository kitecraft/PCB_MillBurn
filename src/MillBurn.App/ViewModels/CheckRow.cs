using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Core;

namespace MillBurn.App.ViewModels;

/// <summary>
/// One check, as the panel shows it: a mark, a source in the source's own colour, and the message.
///
/// **Why the check is not bound directly.** <see cref="Check"/> is in `Core` and renders to a single
/// string, which is right for the companion page, the CLI and a golden baseline and is exactly wrong
/// for the panel — the panel colours one half of the line and not the other, so it needs the halves
/// apart. Two more things are true only in the window: the colour a layer is drawn in can have been
/// overridden by the operator, and a check can be the one a status message is pointing at.
///
/// **The colour comes from wherever the layer list got its swatch**, not from
/// <see cref="Viewer.BoardPalette"/> directly. A check labelled "Top copper" in the palette's orange
/// while the layer row two inches above it shows the operator's chosen green is two controls
/// disagreeing about one layer, which is the thing this project's style rules name first. So the
/// brush is resolved by the view model, which is where the overrides live, and passed in.
/// </summary>
public sealed partial class CheckRow : ObservableObject
{
    public CheckRow(Check check, IBrush? labelBrush)
    {
        ArgumentNullException.ThrowIfNull(check);

        Check = check;
        LabelBrush = labelBrush;
    }

    /// <summary>The check itself, which is what every filter in the panel matches on.</summary>
    public Check Check { get; }

    /// <summary>What this is about: "Top copper", "Stock", "Export".</summary>
    public string Label => Check.Source.Label;

    /// <summary>What is wrong with it. Never begins by naming the label again.</summary>
    public string Message => Check.Message;

    /// <summary>
    /// The layer's own colour, or null when the check is not about a layer.
    ///
    /// **Null on purpose rather than a default brush.** A null `Foreground` inherits, so "Export"
    /// and "Project" take the panel's text colour and follow a theme change with it; a brush
    /// resolved from a theme token at construction would be frozen at whatever the theme was when
    /// the board loaded. Layer colours are palette values and do not have that problem, which is
    /// why they can be resolved once.
    /// </summary>
    public IBrush? LabelBrush { get; }

    /// <summary>
    /// The mark in front of the line, which is what makes severity readable without reading.
    ///
    /// Three characters, chosen so the column has one width and the shapes differ at eleven point:
    /// a full stop of a bullet for advice, which should recede; an exclamation for a refusal; and a
    /// filled triangle for "do not run this", which is the only one that should stop a reader.
    /// </summary>
    public string Mark => Check.Severity switch
    {
        CheckSeverity.Error => "▲",
        CheckSeverity.Refusal => "!",
        _ => "·",
    };

    /// <summary>Drives the styling that tells advice from the rest. See <see cref="Check.IsRefusal"/>.</summary>
    public bool IsRefusal => Check.IsRefusal;

    public bool IsError => Check.IsError;

    /// <summary>
    /// Whether this is the check a status message is currently pointing at.
    ///
    /// **This is the answer to a complaint from the bench that naming the thing did not fix.** The
    /// export's status line said *"1 thing(s) refused — see the checks"*, and the word "refused"
    /// appeared nowhere in the list, so there was nothing to look for: *"I don't know which check it
    /// is referring to."* Naming what was refused was the first attempt and the verdict on it was
    /// *"it's there, but the link from status message to check is not really clear"* — because a
    /// reader is still matching one sentence against a list of sentences.
    ///
    /// Marking the check itself is the only shape of the three 06 offered that two similarly worded
    /// checks cannot defeat, and it became cheap the moment a check was a thing rather than a
    /// string.
    /// </summary>
    [ObservableProperty]
    public partial bool IsHighlighted { get; set; }
}
