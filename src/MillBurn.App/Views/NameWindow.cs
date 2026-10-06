using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MillBurn.App.Views;

/// <summary>
/// A small modal that asks for one name.
///
/// **Asked for from the workshop, for copying a tool:** *"a small dialog for the user to enter the
/// new name."* The button used to make the copy, suffix its name and leave the name box focused and
/// selected — which works if you notice, and leaves a tool called "1.0 mm end mill (copy)" in the
/// library if you do not. Asking first means the copy arrives named.
///
/// Beside <see cref="ConfirmWindow"/> rather than inside it: that one answers a question with a
/// button, this one answers it with a string, and folding the two together would give every
/// confirmation an unused text box.
/// </summary>
public sealed class NameWindow : Window
{
    private readonly TextBox _name = new();
    private string? _result;

    private NameWindow(string title, string message, string proposed, string acceptText)
    {
        Title = title;
        AppIcon.Apply(this);
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        MinWidth = 380;

        _name.Text = proposed;

        var accept = new Button { Content = acceptText, IsDefault = true, MinWidth = 92 };
        accept.Click += (_, _) => Finish(_name.Text);

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 92 };
        cancel.Click += (_, _) => Finish(null);

        // A name of nothing is not a name, and the tool form refuses one anyway — so the button
        // says so before the dialog is dismissed rather than after.
        void Check() => accept.IsEnabled = !string.IsNullOrWhiteSpace(_name.Text);

        _name.TextChanged += (_, _) => Check();
        Check();

        Content = new StackPanel
        {
            Margin = new Thickness(22, 20, 22, 18),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                _name,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, accept },
                },
            },
        };

        // Focused and selected, so the first keystroke replaces the proposal: the proposal is a
        // starting point for somebody who wants to edit it and an obstacle for somebody who does
        // not.
        Opened += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    private void Finish(string? name)
    {
        _result = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Close(_result);
    }

    /// <summary>The name that was typed, or null if the dialog was dismissed.</summary>
    public static async Task<string?> AskAsync(
        Window owner, string title, string message, string proposed, string acceptText = "OK")
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new NameWindow(title, message, proposed, acceptText)
        {
            RequestedThemeVariant = owner.ActualThemeVariant,
        };

        return await dialog.ShowDialog<string?>(owner) ?? dialog._result;
    }

    /// <summary>
    /// The same dialog, built but not shown, so a screenshot can check the wording of a question
    /// that is otherwise only reachable by clicking through a window.
    /// </summary>
    internal static Window Preview(string title, string message, string proposed, string acceptText) =>
        new NameWindow(title, message, proposed, acceptText);
}
