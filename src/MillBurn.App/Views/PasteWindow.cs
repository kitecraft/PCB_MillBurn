using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace MillBurn.App.Views;

/// <summary>
/// Somewhere to paste a block of text the controller printed.
///
/// A file picker would be the obvious thing and is the wrong thing: what the operator has is a
/// selection in their sender's console, not a file. Asking them to save it first is a step at which
/// people give up, and the text is already on the clipboard.
/// </summary>
public sealed class PasteWindow : Window
{
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Consolas, monospace"),
        FontSize = 12,
        Height = 260,
        [!ScrollViewer.HorizontalScrollBarVisibilityProperty] =
            new DynamicResourceExtension("ScrollBarAuto"),
    };

    public string? Result { get; private set; }

    private PasteWindow(string title, string why)
    {
        Title = title;
        AppIcon.Apply(this);
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PageBackground");

        var ok = new Button { Content = "Read it", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        ok.Click += (_, _) =>
        {
            Result = _text.Text;
            Close();
        };

        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = why,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    [!ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
                },
                _text,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };

        Opened += (_, _) => _text.Focus();
    }

    /// <summary>Built but not shown, so a screenshot can check it without a click to get there.</summary>
    internal static Window Preview(string title, string why) => new PasteWindow(title, why);

    /// <summary>Shows it, and hands back what was pasted, or null if nothing was.</summary>
    public static async Task<string?> AskAsync(Window owner, string title, string why)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var window = new PasteWindow(title, why)
        {
            RequestedThemeVariant = owner.ActualThemeVariant,
        };

        await window.ShowDialog(owner);

        return string.IsNullOrWhiteSpace(window.Result) ? null : window.Result;
    }
}
