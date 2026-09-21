using Avalonia;
using Avalonia.Controls;
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

    /// <summary>
    /// Which face of the stock the imported map describes.
    ///
    /// The app cannot work this out and must not guess: a map is a measurement of whichever side
    /// was pointing up at the time, and the operator is the only one who knows which that was.
    /// Programs for that side are levelled; programs for the other side are refused, which is the
    /// whole of the rule.
    /// </summary>
    private readonly RadioButton _topSide;
    private readonly RadioButton _bottomSide;
    private readonly StackPanel _levelDetail;
    private readonly string? _surfaceProblem;
    private string _folder;

    /// <summary>What was chosen, or null if the export was called off.</summary>
    public ExportChoice? Result { get; private set; }

    public ExportWindow(
        ExportPlan plan,
        string folder,
        bool dryRun,
        HeightMap? surface,
        string? surfaceProblem,
        bool probedFlipped = false)
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

        _topSide = new RadioButton
        {
            Content = "top-up",
            GroupName = "probedSide",
            IsChecked = !probedFlipped,
            FontSize = 12,
            MinHeight = 0,
        };

        _bottomSide = new RadioButton
        {
            Content = "flipped",
            GroupName = "probedSide",
            IsChecked = probedFlipped,
            FontSize = 12,
            MinHeight = 0,
        };

        _levelDetail = new StackPanel { Spacing = 2, Margin = new Thickness(26, 2, 0, 0) };

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

        // One line, trimmed from the middle of the path so the folder's own name — the end, and the
        // part that says where the files are going — stays in view. Trimmed from the end, a long path
        // showed the drive and the first few folders and hid exactly that. Widen the window to see
        // more, or hover for all of it.
        _folderText = new TextBlock
        {
            Text = folder,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.PathSegmentEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(_folderText, folder);

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
            // The stock's corner when the job is built on stock: saying "the board's" there sends the
            // operator to zero a border's width away from where every file in the list expects.
            Text = _plan.Blank.Resolved
                ? "One file per layer (per bit, for drilling), all sharing the stock's lower-left corner as work zero."
                : "One file per layer (per bit, for drilling), all sharing the board's lower-left corner as work zero.",
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

        // Which side, and then exactly which files that decides. Named rather than summarised: the
        // point of this window is that nothing is written by surprise, and "4 of your 5 programs"
        // is a surprise waiting to happen on the one it left out.
        var side = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(26, 2, 0, 0),
        };

        side.Children.Add(Token(new TextBlock
        {
            Text = "The board was",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        }, "TextSecondary"));

        side.Children.Add(_topSide);
        side.Children.Add(_bottomSide);

        side.Children.Add(Token(new TextBlock
        {
            Text = "when you probed it",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        }, "TextSecondary"));

        options.Children.Add(side);
        options.Children.Add(_levelDetail);

        // The count follows the side as well as the checkbox. Levelling no longer adds one file per
        // program — it adds one per program for the side that was probed — so a heading that
        // ignored this radio would be exactly the thing this window exists not to do.
        _topSide.IsCheckedChanged += (_, _) =>
        {
            UpdateLevelDetail();
            UpdateCount(count);
        };

        _level.IsCheckedChanged += (_, _) =>
        {
            side.IsVisible = Levelling;
            UpdateLevelDetail();
        };

        side.IsVisible = Levelling;
        UpdateLevelDetail();

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
                _folder, _dryRun.IsChecked == true, Levelling, _bottomSide.IsChecked == true);
            Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(write);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        return root;
    }

    /// <summary>Whether a height map is going to be applied at all.</summary>
    private bool Levelling => _level.IsChecked == true && _level.IsEnabled;

    /// <summary>The programs this map may be applied to, given the side it was probed on.</summary>
    private IEnumerable<ExportItem> Levellable => _plan.Items.Where(i =>
        i.Output == OutputKind.Gcode
        && Leveller.WhyNotLevel(i.Mirrored, _bottomSide.IsChecked == true) is null);

    private void UpdateCount(TextBlock text)
    {
        var programs = _plan.Items.Count(i => i.Output == OutputKind.Gcode);

        // Counted rather than assumed: levelling no longer produces one extra file per program,
        // because the programs for the side that was not probed do not get one.
        var extra = _plan.Items.Count(i => i.Companion is not null)
            + (_dryRun.IsChecked == true ? programs : 0)
            + (Levelling ? Levellable.Count() : 0);

        var total = _plan.Count + extra;
        text.Text = total == 1 ? "1 file will be written" : $"{total} files will be written";
    }

    /// <summary>
    /// Which files the chosen side actually levels, by name.
    ///
    /// Drilling and the outline are top-side programs — they are cut with the board the way up it
    /// was imported — so they follow the top copper, and the single file they part company with is
    /// the mirrored one. Saying so beats leaving the operator to infer it.
    /// </summary>
    private void UpdateLevelDetail()
    {
        _levelDetail.Children.Clear();

        if (!Levelling)
        {
            return;
        }

        var will = Levellable.ToList();
        var wont = _plan.Items
            .Where(i => i.Output == OutputKind.Gcode)
            .Except(will)
            .ToList();

        _levelDetail.Children.Add(Token(new TextBlock
        {
            Text = will.Count == 0
                ? "Nothing will be levelled: every program here is cut the other way up."
                : "Levels " + Names(will) + ".",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        }, will.Count == 0 ? "DrcViolation" : "TextSecondary"));

        if (wont.Count > 0)
        {
            _levelDetail.Children.Add(Token(new TextBlock
            {
                Text = "Not " + Names(wont) + " — cut on the other side, so the map does not describe it.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            }, "TextSecondary"));
        }
    }

    private static string Names(List<ExportItem> items) => items.Count > 4
        ? string.Join(", ", items.Take(3).Select(i => i.TargetName)) + $" and {items.Count - 3} more"
        : string.Join(", ", items.Select(i => i.TargetName));

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
            Text = $"{item.LayerLabel} · {item.DoingLabel} · {item.Bytes:N0} bytes",
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
            ToolTip.SetTip(_folderText, path);
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
/// <param name="LevelFlipped">
/// Whether the map was probed with the stock flipped. Decides which programs it may be applied to:
/// the ones cut on that same side, and only those.
/// </param>
public sealed record ExportChoice(string Folder, bool DryRun, bool Level, bool LevelFlipped = false);
