using Avalonia;
using Avalonia.Controls;

namespace MillBurn.App.Controls;

/// <summary>
/// The status bar's row: the message first, then summaries that each show in full when there is
/// room, and give way — the last one first — when there is not.
///
/// **The message wins.** Its first child is the status message, and it is never squeezed to make
/// room for a summary: with a Grid, Auto columns are sized to their content before the star column
/// gets anything, and three summaries once left the message a 54 px slot reading "No ..." — which is
/// how a refused height-map import came to look exactly like a successful one.
///
/// **The summaries show in full when they fit.** Capping each summary's column fixed the message
/// and cut the summaries off at every window size instead, including the 1600 px the README's
/// screenshots are taken at; no amount of window made them readable. A Grid cannot say "as wide as
/// the content, unless there is no room", so this panel does.
///
/// When the row does not fit, summaries go from the last (frame timing) to the first (the program
/// summary), each to nothing rather than to a stub, and the message trims only once all of them
/// have gone. Every one keeps its tooltip, so nothing hidden is lost.
///
/// **Give the children <c>ClipToBounds</c>.** A hidden summary is arranged to zero width, and a
/// trimming TextBlock at zero width still draws its ellipsis — the first version left a stray "…"
/// at the right-hand edge for every summary it had hidden.
/// </summary>
public sealed class StatusBarPanel : Panel
{
    /// <summary>A summary given less than this is hidden rather than shown as a few letters and "…".</summary>
    private const double Smallest = 60;

    private double[] _desired = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        _desired = new double[Children.Count];
        var height = 0.0;

        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(double.PositiveInfinity, availableSize.Height));
            _desired[i] = Children[i].DesiredSize.Width;
            height = Math.Max(height, Children[i].DesiredSize.Height);
        }

        // Measured at their natural width only — never again at the narrower width each one gets.
        // The first version did, to lay out the ellipsis early, and it pinned each child to that
        // width: when the frame timing replaced its one-character placeholder, the TextBlock was
        // re-measured on its own against the old 12 px, reported no change in size, never asked
        // this panel to lay out again, and showed "…" at every window size. The narrowing happens
        // in the arrange, where a TextBlock trims to the width it is actually given.
        return new Size(double.IsInfinity(availableSize.Width) ? _desired.Sum() : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var widths = Allocate(_desired, finalSize.Width);
        var x = 0.0;

        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
            x += widths[i];
        }

        return finalSize;
    }

    /// <summary>
    /// How wide each child gets: everyone in full if it fits; otherwise the summaries give way from
    /// the last, and the message — the first — only once they all have. Room to spare goes to the
    /// message, which keeps the summaries against the right-hand edge.
    /// </summary>
    internal static double[] Allocate(double[] desired, double available)
    {
        var widths = (double[])desired.Clone();

        if (widths.Length == 0 || double.IsInfinity(available))
        {
            return widths;
        }

        var over = widths.Sum() - available;

        for (var i = widths.Length - 1; i >= 1 && over > 0; i--)
        {
            var keep = Math.Max(0, widths[i] - over);

            if (keep < Smallest)
            {
                keep = 0;
            }

            over -= widths[i] - keep;
            widths[i] = keep;
        }

        widths[0] = Math.Max(0, widths[0] - over);

        return widths;
    }
}
