using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MillBurn.Align;
using MillBurn.Core;
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
    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    private readonly ExportPlan _plan;
    private readonly TextBlock _folderText;
    private readonly CheckBox _dryRun;
    private readonly CheckBox _level;
    private readonly string? _surfaceProblem;
    private string _folder;

    /// <summary>What was chosen, or null if the export was called off.</summary>
    public ExportChoice? Result { get; private set; }

    public ExportWindow(
        ExportPlan plan, string folder, bool dryRun, HeightMap? surface, string? surfaceProblem)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _plan = plan;
        _folder = folder;

        _dryRun = new CheckBox
        {
            Content = "Also write a dry run of each program",
            IsChecked = dryRun,
            IsEnabled = plan.Items.Any(i => i.Output == OutputKind.Gcode),
        };

        ToolTip.SetTip(
            _dryRun,
            "A second copy of each .nc that holds the tool 5 mm up and never starts the spindle, "
            + "so you can watch the whole job run before it touches anything.");

        // Offered rather than assumed, and off unless a surface has actually been measured. The
        // heading says what the map is, because levelling to the wrong board's surface is worse
        // than not levelling at all and there is nothing in the file to warn you.
        var hasSurface = surface is not null
            && surfaceProblem is null
            && plan.Items.Any(i => i.Output == OutputKind.Gcode);

        var measured = surface is null
            ? null
            : Invariant($"{surface.PointCount} points, {surface.RangeMm:F3} mm out of flat");

        _level = new CheckBox
        {
            Content = measured is null
                ? "Level to a height map  (none imported — Job ▸ Import height map…)"
                : $"Level to the imported height map  ({measured})",
            IsChecked = hasSurface,
            IsEnabled = hasSurface,
        };

        _surfaceProblem = surfaceProblem;

        ToolTip.SetTip(
            _level,
            "A copy of each .nc whose depth follows the measured surface. Work zero must be exactly "
            + "where it was when you probed.");

        Title = "Export";
        AppIcon.Apply(this);
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
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"),
            Margin = new Thickness(18),
        };

        var heading = new StackPanel { Spacing = 4 };

        // The count follows the checkbox. This window exists to say what will be written, so a
        // heading that stays at four while eight files land would be the one thing it got wrong.
        var count = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold };
        UpdateCount(count);
        _dryRun.IsCheckedChanged += (_, _) => UpdateCount(count);
        _level.IsCheckedChanged += (_, _) => UpdateCount(count);
        heading.Children.Add(count);
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

        var options = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 8) };
        options.Children.Add(_dryRun);
        options.Children.Add(_level);

        if (_surfaceProblem is not null)
        {
            options.Children.Add(Token(new TextBlock
            {
                Text = _surfaceProblem,
                FontSize = 11,
                Margin = new Thickness(26, -2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            }, "DrcViolation"));
        }

        Grid.SetRow(options, 3);
        root.Children.Add(options);

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
            Result = new ExportChoice(
                _folder, _dryRun.IsChecked == true, _level.IsChecked == true && _level.IsEnabled);
            Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(write);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        return root;
    }

    private void UpdateCount(TextBlock text)
    {
        var programs = _plan.Items.Count(i => i.Output == OutputKind.Gcode);
        var extra = _plan.Items.Count(i => i.Companion is not null)
            + (_dryRun.IsChecked == true ? programs : 0)
            + (_level.IsChecked == true && _level.IsEnabled ? programs : 0);

        var total = _plan.Count + extra;
        text.Text = total == 1 ? "1 file will be written" : $"{total} files will be written";
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

        if (item.Companion is { } guide)
        {
            panel.Children.Add(Token(new TextBlock
            {
                Text = $"+ {guide.TargetName} · {guide.Description}",
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

    /// <summary>Shows the plan and returns what to do, or null if it was called off.</summary>
    public static async Task<ExportChoice?> AskAsync(
        Window owner, ExportPlan plan, string folder, bool dryRun,
        HeightMap? surface, string? surfaceProblem)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // A dialog is its own top level, so it does not inherit the variant the user chose.
        var window = new ExportWindow(plan, folder, dryRun, surface, surfaceProblem)
        {
            RequestedThemeVariant = owner.ActualThemeVariant,
        };

        await window.ShowDialog(owner);
        return window.Result;
    }
}

/// <summary>Where the files go, and what goes with them.</summary>
/// <param name="Folder">The folder to write into.</param>
/// <param name="DryRun">Whether each program gets a companion that cuts nothing.</param>
/// <param name="Level">Whether each program gets a companion that follows the measured surface.</param>
public sealed record ExportChoice(string Folder, bool DryRun, bool Level);
