using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace MillBurn.App.Views;

/// <summary>
/// One tab's own width, asked for beside its handle — a click on the circle, not a drag (6.4).
///
/// **From the bench, 2026-10-18:** *"I'd also like to be able to single click on a tab (just the
/// circle) and have it pop up a hover dialog for 'width'. The width would be applied to only that
/// specified tab."* The tabs window can already do this, a long way from the tab; this is the same
/// edit made where the tab is, and it writes the same project entry the window would.
///
/// Light-dismissed: clicking away or pressing Escape changes nothing. Enter or *Apply* takes the
/// number; an emptied box, or *Board's width*, puts the tab back to what every tab has.
/// </summary>
public static class TabWidthFlyout
{
    /// <summary>
    /// The flyout, ready to show.
    /// </summary>
    /// <param name="letter">The tab's name, as its handle shows it.</param>
    /// <param name="widthMm">The tab's width now.</param>
    /// <param name="boardWidthMm">The width every tab has unless one says otherwise.</param>
    /// <param name="apply">Called with the chosen width, or null for the board's.</param>
    public static Flyout For(string letter, double widthMm, double boardWidthMm, Action<double?> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

        var flyout = new Flyout
        {
            Placement = PlacementMode.Pointer,
            ShowMode = FlyoutShowMode.Standard,
        };

        var width = (decimal)widthMm;

        // The range stretches to hold what it was given, as the tabs window's boxes do: a width set
        // elsewhere outside 0.5–20 must open as itself, not clamped into a number nobody chose.
        var box = new NumericUpDown
        {
            Value = width,
            Increment = 0.5m,
            Minimum = Math.Min(0.5m, width),
            Maximum = Math.Max(20m, width),
            FormatString = "0.00",
            Width = 128,
            VerticalAlignment = VerticalAlignment.Center,
        };

        void Take(double? chosen)
        {
            flyout.Hide();
            apply(chosen);
        }

        double? Chosen() => box.Value is { } value ? (double)value : null;

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Take(Chosen());
            }
        };

        var heading = new TextBlock
        {
            Text = $"Tab {letter}",
            FontWeight = FontWeight.SemiBold,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Token(new TextBlock
        {
            Text = "Width (mm)",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        }, "TextSecondary"));
        row.Children.Add(box);

        var note = Token(new TextBlock
        {
            Text = string.Create(
                CultureInfo.InvariantCulture,
                $"This tab only. Every other tab keeps its own; the board's width is {boardWidthMm:F2} mm."),
            FontSize = 11,
            MaxWidth = 240,
            TextWrapping = TextWrapping.Wrap,
        }, "TextSecondary");

        var board = new Button { Content = "Board's width", Padding = new Thickness(8, 2) };
        board.Click += (_, _) => Take(null);

        var ok = new Button { Content = "Apply", Padding = new Thickness(8, 2), Classes = { "primary" } };
        ok.Click += (_, _) => Take(Chosen());

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
        };
        buttons.Children.Add(board);
        buttons.Children.Add(ok);

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(4) };
        body.Children.Add(heading);
        body.Children.Add(row);
        body.Children.Add(note);
        body.Children.Add(buttons);

        flyout.Content = body;

        // The number is what the click was for, so it has the keyboard the moment the popup is up.
        flyout.Opened += (_, _) => box.Focus();

        return flyout;
    }

    /// <summary>
    /// Binds a colour token rather than resolving one, for the same reason the tabs window does: a
    /// popup is built before its theme variant is applied.
    /// </summary>
    private static TextBlock Token(TextBlock text, string token)
    {
        text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(token);
        return text;
    }
}
