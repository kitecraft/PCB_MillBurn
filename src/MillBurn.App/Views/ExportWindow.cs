using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MillBurn.Pipeline;

namespace MillBurn.App.Views;

/// <summary>
/// The last look before anything is written: every layer, the file it becomes, and what that file
/// will do.
///
/// Built in code rather than XAML because the list is entirely data-driven and the window has no
/// state of its own beyond the chosen folder. It is a confirmation, not an editor — filenames are
/// derived from the layers on purpose, so the only decision left is where they land.
/// </summary>
public sealed class ExportWindow : Window
{
    private readonly ExportPlan _plan;
    private readonly TextBlock _folderText;
    private string _folder;

    /// <summary>The folder chosen, or null if the export was called off.</summary>
    public string? Result { get; private set; }

    public ExportWindow(ExportPlan plan, string folder)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _plan = plan;
        _folder = folder;

        Title = "Export";
        Width = 640;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PageBackground");

        _folderText = new TextBlock
        {
            Text = folder,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Content = BuildBody();
    }

    private Grid BuildBody()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            Margin = new Thickness(18),
        };

        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(new TextBlock
        {
            Text = _plan.Count == 1 ? "1 file will be written" : $"{_plan.Count} files will be written",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
        });
        heading.Children.Add(Token(new TextBlock
        {
            Text = "One file per layer, all sharing the board's lower-left corner as work zero.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        }, "TextSecondary"));
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var list = new StackPanel { Spacing = 2, Margin = new Thickness(0, 12) };
        foreach (var item in _plan.Items)
        {
            list.Children.Add(Row(item));
        }

        foreach (var skipped in _plan.Skipped)
        {
            list.Children.Add(Token(new TextBlock
            {
                Text = "Skipped: " + skipped,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            }, "TextSecondary"));
        }

        var scroller = new ScrollViewer { Content = list };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);

        var folderRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 6),
        };

        var label = Token(new TextBlock
        {
            Text = "Folder",
            Width = 52,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        }, "TextSecondary");
        Grid.SetColumn(label, 0);
        folderRow.Children.Add(label);

        Grid.SetColumn(_folderText, 1);
        folderRow.Children.Add(_folderText);

        var browse = new Button { Content = "Choose…" };
        ToolTip.SetTip(browse, "Pick where these files land");
        browse.Click += async (_, _) => await ChooseFolderAsync();
        Grid.SetColumn(browse, 2);
        folderRow.Children.Add(browse);

        Grid.SetRow(folderRow, 2);
        root.Children.Add(folderRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();

        var write = new Button
        {
            Content = "Write files",
            IsDefault = true,
            IsEnabled = _plan.Count > 0,
        };
        write.Click += (_, _) =>
        {
            Result = _folder;
            Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(write);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        return root;
    }

    /// <summary>
    /// One file: what it is called, which layer it came from, and the facts that decide whether it
    /// is the right file — depths, counts, cut widths — with anything questionable in view.
    /// </summary>
    private static StackPanel Row(ExportItem item)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 5) };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock
        {
            Text = item.TargetName,
            FontFamily = new FontFamily("Consolas,monospace"),
        });
        header.Children.Add(Token(new TextBlock
        {
            Text = $"{item.LayerLabel} · {LayerOperations.Label(item.Operation)} · {item.Bytes:N0} bytes",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        }, "TextSecondary"));
        panel.Children.Add(header);

        foreach (var line in item.Summary)
        {
            panel.Children.Add(Token(new TextBlock
            {
                Text = line,
                FontSize = 11,
                Margin = new Thickness(12, 1, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            }, "TextSecondary"));
        }

        foreach (var warning in item.Warnings)
        {
            panel.Children.Add(Token(new TextBlock
            {
                Text = "CHECK  " + warning,
                FontSize = 11,
                Margin = new Thickness(12, 1, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            }, "DrcViolation"));
        }

        return panel;
    }

    private async Task ChooseFolderAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Where should the exported files go?",
            AllowMultiple = false,
        });

        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            _folder = path;
            _folderText.Text = path;
        }
    }

    /// <summary>
    /// Binds a colour token rather than resolving one.
    ///
    /// A dialog is built before its theme variant is applied, so resolving a token in the
    /// constructor picks the light palette and keeps it — a window that quietly ignores the theme
    /// the user chose. A dynamic reference re-resolves when the variant lands.
    /// </summary>
    private static T Token<T>(T control, string token)
        where T : TextBlock
    {
        control[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(token);
        return control;
    }

    /// <summary>Shows the plan and returns the folder to write to, or null if it was called off.</summary>
    public static async Task<string?> AskAsync(Window owner, ExportPlan plan, string folder)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // A dialog is its own top level, so it does not inherit the variant the user chose.
        var window = new ExportWindow(plan, folder) { RequestedThemeVariant = owner.ActualThemeVariant };
        await window.ShowDialog(owner);
        return window.Result;
    }
}
