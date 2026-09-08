using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MillBurn.App.Views;

/// <summary>What the user chose in a confirmation.</summary>
public enum ConfirmResult
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// A small modal confirmation. Avalonia ships no message box, and this needs exactly three
/// answers, so it is built in code rather than dragging in a dialog library for forty lines.
///
/// The wording matters more than the widget: buttons say what they *do* ("Save", "Discard"), never
/// "Yes"/"No", because a person reading fast should not have to reconstruct the question from the
/// answer.
/// </summary>
public sealed class ConfirmWindow : Window
{
    private ConfirmResult _result = ConfirmResult.Cancel;

    private ConfirmWindow(string title, string message, string saveText, string? discardText)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        MinWidth = 380;

        var save = new Button { Content = saveText, IsDefault = true, MinWidth = 92 };
        save.Click += (_, _) => Finish(ConfirmResult.Save);

        var discard = new Button
        {
            Content = discardText ?? string.Empty,
            MinWidth = 92,

            // A note has one answer. Leaving the other two buttons present but unlabelled would be
            // worse than not having them.
            IsVisible = discardText is not null,
        };
        discard.Click += (_, _) => Finish(ConfirmResult.Discard);

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 92,
            IsVisible = discardText is not null,
        };
        cancel.Click += (_, _) => Finish(ConfirmResult.Cancel);

        Content = new StackPanel
        {
            Margin = new Thickness(22, 20, 22, 18),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, discard, save },
                },
            },
        };
    }

    private void Finish(ConfirmResult result)
    {
        _result = result;
        Close(result);
    }

    /// <summary>Asks about unsaved work. Cancel is the safe default if the window is dismissed.</summary>
    public static async Task<ConfirmResult> AskAsync(
        Window owner,
        string title,
        string message,
        string saveText = "Save",
        string? discardText = "Discard")
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new ConfirmWindow(title, message, saveText, discardText)
        {
            RequestedThemeVariant = owner.ActualThemeVariant,
        };
        var result = await dialog.ShowDialog<ConfirmResult?>(owner);
        return result ?? dialog._result;
    }

    /// <summary>Says something and waits for it to be acknowledged. One button, one answer.</summary>
    public static Task NoteAsync(Window owner, string title, string message) =>
        AskAsync(owner, title, message, "OK", null);
}
