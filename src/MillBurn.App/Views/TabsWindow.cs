using System.Collections.Immutable;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using MillBurn.Cam;
using MillBurn.Core;

namespace MillBurn.App.Views;

/// <summary>
/// The tabs holding this board in its stock: where they are, how wide, and how much they leave.
///
/// **From the workshop, on a 21 x 37 mm board:** *"They are large tabs and may not be in the best
/// locations."* The locations are answered by the planner — one centred on each straight edge — and
/// the sizes are answered here. Every tab starts at the size the board gives it, and any of them can
/// be set by hand without touching the others.
///
/// The positions are read-only: this window says where the tabs are, and moving one by hand is
/// 06 §6.4's third item, which needs the viewport to hit-test the profile. Showing the coordinate is
/// what makes that absence bearable — it is the same number the machine works in, measured from the
/// board's lower-left corner.
/// </summary>
public sealed class TabsWindow : Window
{
    private readonly IReadOnlyList<PlacedTab> _tabs;
    private readonly long _derivedWidthNm;
    private readonly long _derivedHeightNm;
    private readonly NumericUpDown _width;
    private readonly NumericUpDown _height;
    private readonly List<(NumericUpDown Width, NumericUpDown Height)> _rows = [];

    /// <summary>What was chosen, or null if it was called off.</summary>
    public Choice? Result { get; private set; }

    /// <summary>
    /// The sizes chosen: the board-wide pair, and the tabs that differ from it.
    ///
    /// Nulls mean "as the board gives it", which is not the same as a number that happens to match
    /// today — a board resized after this dialog was used should move with its own rule rather than
    /// keep a figure typed against the old size.
    /// </summary>
    public sealed record Choice(long? WidthNm, long? HeightNm, ImmutableArray<TabEdit> Edits);

    public TabsWindow(
        string boardLabel,
        IReadOnlyList<PlacedTab> tabs,
        long? widthNm,
        long? heightNm,
        long derivedWidthNm,
        long derivedHeightNm)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        _tabs = tabs;
        _derivedWidthNm = derivedWidthNm;
        _derivedHeightNm = derivedHeightNm;

        Title = "Tabs";
        AppIcon.Apply(this);
        Width = 560;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PageBackground");

        _width = Box(widthNm ?? derivedWidthNm, 0.5, 2, 0.5, 20);
        _height = Box(heightNm ?? derivedHeightNm, 0.05, 2, 0.05, 2);

        Content = Body(boardLabel);
    }

    /// <summary>
    /// One editable size.
    ///
    /// **The range stretches to hold what it was given, and nothing is rounded away.** A
    /// `/code-review` pass found the opposite: the boxes clamped to their own limits and rounded
    /// to one decimal on construction, so opening this window on a board whose tabs had been set
    /// from the command line — which has no upper bound — and pressing Save wrote the clamped
    /// number back as the operator’s own choice, and 2.25 mm became 2.3. Silently correcting
    /// somebody’s number leaves them believing the machine is set up one way while it is set up
    /// another, which is the reason the settings window refuses rather than clamps.
    /// </summary>
    private static NumericUpDown Box(long valueNm, double increment, int decimals, double min, double max)
    {
        var mm = (decimal)Nm.ToMillimetres(valueNm);

        return new NumericUpDown
        {
            Value = mm,
            Increment = (decimal)increment,
            Minimum = Math.Min((decimal)min, mm),
            Maximum = Math.Max((decimal)max, mm),
            FormatString = decimals == 1 ? "0.0" : "0.00",

            // Wide enough for the number and both of its buttons: at 110 the second decimal was
            // clipped, so "0.35" read as "0.3!" in a window whose whole job is to show numbers.
            Width = 128,
        };
    }

    private static long Nanometres(NumericUpDown box) =>
        Nm.FromMillimetres((double)(box.Value ?? 0));

    private Grid Body(string boardLabel)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(18),
        };

        var heading = new StackPanel { Spacing = 4 };

        heading.Children.Add(new TextBlock
        {
            Text = "Tabs",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
        });

        heading.Children.Add(Token(new TextBlock
        {
            Text = _tabs.Count == 0
                ? $"{boardLabel} has no tabs: either none were asked for, or they would take up the whole profile. "
                    + "The piece comes free on the last pass."
                : $"{_tabs.Count} on {boardLabel}, one centred on each straight edge the profile has room for. "
                    + "Each position is on the cut line, half a cutter outside the board, measured from the "
                    + "board's lower-left corner — which is where the machine's zero is.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        }, "TextSecondary"));

        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(0, 14) };

        body.Children.Add(Label("Every tab, unless it says otherwise"));

        var board = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto"),
            Margin = new Thickness(0, 2, 0, 0),
        };

        board.Children.Add(Cell(Caption("Width (mm)"), 0));
        board.Children.Add(Cell(_width, 1));
        board.Children.Add(Cell(Caption("Leaves (mm)"), 2, left: 14));
        board.Children.Add(Cell(_height, 3));
        body.Children.Add(board);

        body.Children.Add(Token(new TextBlock
        {
            Text = $"The board's own sizes are {Nm.ToMillimetreString(_derivedWidthNm, 1)} mm wide, "
                + $"leaving {Nm.ToMillimetreString(_derivedHeightNm, 2)} mm — a tenth of its narrow side and a "
                + "third of its thickness. Typing those exact numbers is the same as leaving them alone.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        }, "TextSecondary"));

        if (_tabs.Count > 0)
        {
            body.Children.Add(Label("One at a time"));

            for (var i = 0; i < _tabs.Count; i++)
            {
                body.Children.Add(Row(i, _tabs[i]));
            }
        }

        var scroller = new ScrollViewer { Content = body };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var reset = new Button { Content = "Let the board decide" };
        reset.Click += (_, _) => ResetToDerived();

        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();

        var save = new Button { Content = "Save", IsDefault = true, Classes = { "primary" } };
        save.Click += (_, _) => Apply();

        buttons.Children.Add(reset);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        return root;
    }

    /// <summary>One tab: which it is, where it is, and its own two numbers.</summary>
    private Grid Row(int index, PlacedTab tab)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(0, 2, 0, 0),
        };

        var where = tab.Spaced
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{index + 1}. spaced round the profile, at {Nm.ToMillimetreString(tab.At.X, 2)}, {Nm.ToMillimetreString(tab.At.Y, 2)} mm")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{index + 1}. on a {Nm.ToMillimetreString((long)tab.EdgeLengthNm, 1)} mm edge, at {Nm.ToMillimetreString(tab.At.X, 2)}, {Nm.ToMillimetreString(tab.At.Y, 2)} mm");

        var label = new TextBlock
        {
            Text = where,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 10, 0),
        };

        var width = Box(tab.WidthNm, 0.5, 2, 0.5, 20);
        var height = Box(tab.HeightNm, 0.05, 2, 0.05, 2);

        _rows.Add((width, height));

        Grid.SetColumn(label, 0);
        Grid.SetColumn(width, 1);
        Grid.SetColumn(height, 2);

        height.Margin = new Thickness(8, 0, 0, 0);

        grid.Children.Add(label);
        grid.Children.Add(width);
        grid.Children.Add(height);

        return grid;
    }

    private void ResetToDerived()
    {
        _width.Value = (decimal)Nm.ToMillimetres(_derivedWidthNm);
        _height.Value = (decimal)Nm.ToMillimetres(_derivedHeightNm);

        foreach (var (width, height) in _rows)
        {
            width.Value = _width.Value;
            height.Value = _height.Value;
        }
    }

    /// <summary>
    /// Reads the boxes back into a choice.
    ///
    /// **A number equal to the derived one is not an override**, and a tab equal to the board-wide
    /// pair is not an edit. Keeping it that way is what lets a board whose thickness or size changes
    /// later move with its own rule instead of carrying a figure typed against the old one — and it
    /// keeps the project file empty of settings nobody made.
    /// </summary>
    private void Apply()
    {
        var widthNm = Nanometres(_width);
        var heightNm = Nanometres(_height);

        var chosenWidth = widthNm == _derivedWidthNm ? (long?)null : widthNm;
        var chosenHeight = heightNm == _derivedHeightNm ? (long?)null : heightNm;

        var edits = ImmutableArray.CreateBuilder<TabEdit>();

        for (var i = 0; i < _rows.Count; i++)
        {
            var perTabWidth = Nanometres(_rows[i].Width);
            var perTabHeight = Nanometres(_rows[i].Height);

            if (perTabWidth == widthNm && perTabHeight == heightNm)
            {
                continue;
            }

            edits.Add(new TabEdit
            {
                Index = i,
                WidthNm = perTabWidth == widthNm ? null : perTabWidth,
                HeightNm = perTabHeight == heightNm ? null : perTabHeight,
            });
        }

        Result = new Choice(chosenWidth, chosenHeight, edits.ToImmutable());
        Close();
    }

    private static Control Cell(Control control, int column, int left = 0)
    {
        control.Margin = new Thickness(left, 0, 6, 0);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, column);

        return control;
    }

    private static TextBlock Caption(string text) => Token(new TextBlock
    {
        Text = text,
        FontSize = 12,
    }, "TextSecondary");

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 10, 0, 0),
    };

    /// <summary>
    /// Binds a colour token rather than resolving one: a dialog is built before its theme variant is
    /// applied, so resolving now would pick the light palette and keep it.
    /// </summary>
    private static T Token<T>(T control, string token)
        where T : TextBlock
    {
        control[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(token);
        return control;
    }

    /// <summary>Shows the window and returns what was chosen, or null if it was called off.</summary>
    public static async Task<Choice?> AskAsync(
        Window owner,
        string boardLabel,
        IReadOnlyList<PlacedTab> tabs,
        long? widthNm,
        long? heightNm,
        long derivedWidthNm,
        long derivedHeightNm)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var window = new TabsWindow(boardLabel, tabs, widthNm, heightNm, derivedWidthNm, derivedHeightNm)
        {
            RequestedThemeVariant = owner.ActualThemeVariant,
        };

        await window.ShowDialog(owner);

        return window.Result;
    }
}
