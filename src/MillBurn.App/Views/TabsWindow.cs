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
/// what makes that absence bearable — it is the same number the machine works in, which means work
/// zero: the stock's lower-left corner on a job built on stock, and the board's when there is none.
/// </summary>
public sealed class TabsWindow : Window
{
    private readonly IReadOnlyList<PlacedTab> _tabs;
    private readonly long _derivedWidthNm;
    private readonly long _derivedHeightNm;
    private readonly NumericUpDown _width;
    private readonly NumericUpDown _height;
    private readonly List<(NumericUpDown Width, NumericUpDown Height)> _rows = [];

    /// <summary>What the board-wide boxes held last, so a change can carry the rows that agreed.</summary>
    private decimal? _lastWidth;
    private decimal? _lastHeight;

    /// <summary>
    /// The width the tabs below were placed at, and what the rows held then.
    ///
    /// **A tab's number is its place in a list the width decides**, which a `/code-review` pass
    /// found: the width is what tells the planner how many tabs an edge can hold, so widening it
    /// past what a short edge can take drops that edge and re-places every tab. An edit made against
    /// the old list then lands on whichever tab now sorts third — a thin web nobody chose — or is
    /// dropped for being out of range. Only the width does this; the height is applied after the
    /// positions are settled.
    /// </summary>
    private readonly decimal? _openingWidth;
    private readonly List<(decimal? Width, decimal? Height)> _opening = [];

    /// <summary>Shown while the board-wide width is not the one the rows were placed at.</summary>
    private readonly TextBlock _stale = new()
    {
        Text = "The width changed, so the tabs will be placed again when you save and the numbering "
            + "below is for the old width. Save, then reopen this window to size them one at a time.",
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,
    };

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

        // **Changing the box at the top moves the tabs that agreed with it**, and a `/code-review`
        // pass is why. Without this the control was inert and worse than inert: `Apply` decides a
        // tab has been set by hand by comparing its box with the board-wide one, so typing 3.0 at
        // the top left every row holding 2.0 — which then came out as four explicit 2.0 mm edits
        // nobody made, applied after the width and overriding it. The board-wide number changed
        // and every tab was cut at the old one.
        //
        // A row the operator has already changed keeps what they set: it no longer agrees with the
        // board-wide value, which is exactly what "set by hand" means here.
        _lastWidth = _width.Value;
        _lastHeight = _height.Value;

        // Taken after the rows exist, so returning the width to what it was brings back the hand
        // edits rather than leaving them wiped by a number typed and typed back.
        _openingWidth = _width.Value;
        _opening.AddRange(_rows.Select(r => (r.Width.Value, r.Height.Value)));

        _width.ValueChanged += (_, _) => FollowWidth();
        _height.ValueChanged += (_, _) => FollowHeight();
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

    /// <summary>
    /// What a box holds, or null when it holds nothing — which means "as the board gives it".
    ///
    /// **It used to read an empty box as zero**, found by `/code-review`: clearing the Width box and
    /// pressing Enter — Save is the default button — wrote a tab width of 0, which is not a tab. The
    /// outline then cut the board fully free while the program's header said "4 tabs, 0.0 mm wide".
    /// Nothing clamps it on the way through: the Minimum does not stop a box being emptied, and
    /// zero is not equal to the derived width so it was taken for the operator's own number.
    /// </summary>
    private static long? Nanometres(NumericUpDown box) =>
        box.Value is { } value ? Nm.FromMillimetres((double)value) : null;

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
            body.Children.Add(Token(_stale, "StatusWarning"));

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

    /// <summary>
    /// Moves every per-tab box that still held the old board-wide value to the new one.
    ///
    /// The previous value is remembered here rather than read off the event, which keeps this
    /// independent of how the control reports a change.
    /// </summary>
    private void FollowWidth()
    {
        foreach (var row in _rows.Where(r => r.Width.Value == _lastWidth))
        {
            row.Width.Value = _width.Value;
        }

        _lastWidth = _width.Value;
        Replaced();
    }

    /// <summary>
    /// Once the width is not the one the tabs were placed at, the rows stop being about them.
    ///
    /// So every row takes the board-wide pair — not only the ones that agreed with it — and the
    /// per-tab boxes are closed until the window is opened again on the new layout. Dropping an
    /// edit the operator can see they are losing, with a line saying why, beats keeping one that
    /// silently moves to another tab.
    ///
    /// Reversible, because "let the board decide" and a number typed back are both this: when the
    /// width returns to the one the window opened with, the layout is the one on screen again and
    /// the rows come back as they were.
    /// </summary>
    private void Replaced()
    {
        var stale = _width.Value != _openingWidth;

        // **Snapshot on the way out, not once at the start**, which a `/code-review` pass caught:
        // the rows were captured in the constructor, so a size set by hand *in this session* and
        // then lost to a width change did not come back when the width was typed back — the method
        // restored the state the window opened in, and its own doc claimed otherwise. Taken at the
        // moment the rows stop being about the tabs below them, which is the last moment they are
        // still the operator's.
        if (stale && !_stale.IsVisible)
        {
            _opening.Clear();
            _opening.AddRange(_rows.Select(r => (r.Width.Value, r.Height.Value)));
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            var (width, height) = _rows[i];

            width.Value = stale ? _width.Value : _opening[i].Width;
            height.Value = stale ? _height.Value : _opening[i].Height;
            width.IsEnabled = !stale;
            height.IsEnabled = !stale;
        }

        _stale.IsVisible = stale;
    }

    private void FollowHeight()
    {
        foreach (var row in _rows.Where(r => r.Height.Value == _lastHeight))
        {
            row.Height.Value = _height.Value;
        }

        _lastHeight = _height.Value;
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

        // An empty box and a box holding the derived number mean the same thing, and it is the same
        // null either way.
        var chosenWidth = widthNm == _derivedWidthNm ? null : widthNm;
        var chosenHeight = heightNm == _derivedHeightNm ? null : heightNm;

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
