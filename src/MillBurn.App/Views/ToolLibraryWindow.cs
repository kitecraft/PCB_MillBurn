using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using MillBurn.Core;

namespace MillBurn.App.Views;

/// <summary>
/// Creating and editing saved tools.
///
/// The form changes with the tool kind — a V-bit needs an angle and a tip and no diameter, an end
/// mill the reverse — and the rows are built **once** and then shown or hidden. Rebuilding them was
/// the obvious approach and it crashed on open: clearing a panel's children detaches the row
/// wrappers, but the text boxes inside them stay parented to those discarded wrappers, so the next
/// build cannot re-parent them. Editors are long-lived; their containers are not.
///
/// The panel shows what the tool will actually *do* as the numbers are typed. For a V-bit the cut
/// width is a consequence of the depth rather than a setting, so a form of raw geometry with no
/// derived number would leave the operator doing trigonometry to find out whether the bit can
/// separate their traces.
/// </summary>
public sealed class ToolLibraryWindow : Window
{
    private readonly ListBox _list = new() { Width = 250 };
    private readonly StackPanel _form = new() { Spacing = 6, MinWidth = 300 };
    private readonly TextBlock _effect = new() { TextWrapping = TextWrapping.Wrap };

    private ToolLibrary _library;
    private Tool? _editing;

    private readonly TextBox _name = new();
    private readonly ComboBox _kind = new() { ItemsSource = Enum.GetValues<ToolKind>() };
    private readonly TextBox _diameter = new();
    private readonly TextBox _tip = new();
    private readonly TextBox _angle = new();
    private readonly TextBox _maxDepth = new();
    private readonly TextBox _stepdown = new();
    private readonly TextBox _feed = new();
    private readonly TextBox _plunge = new();
    private readonly TextBox _rpm = new();
    private readonly TextBox _notes = new() { AcceptsReturn = true, Height = 54 };

    private readonly List<(Control Row, ToolKind? OnlyFor)> _rows = [];

    /// <summary>
    /// Which kinds the list is showing, and how it is ordered. Neither is saved and neither touches
    /// `tools.json`: a sort is a way of looking at the library, not a fact about it.
    /// </summary>
    private ToolKind? _only;
    private ToolSort _sort = ToolSort.Library;

    /// <summary>
    /// Set while the list is being rebuilt, so the selection it loses and regains does not reload
    /// the form.
    ///
    /// **Changing the sort used to discard whatever was typed**, which a `/code-review` pass caught:
    /// reassigning `ItemsSource` clears the selection, the empty selection reloads the form from
    /// nothing, and the reselection reloads it from the stored tool — so a feed rate typed and not
    /// yet saved was gone the moment the operator sorted the list to find the next bit. Before the
    /// sort and the filter existed, `Refresh` only ran after a save, where that was harmless.
    /// </summary>
    private bool _rebuilding;

    private readonly List<(ToggleButton Button, ToolKind? Kind)> _filters = [];
    private readonly ComboBox _order = new() { FontSize = 11, Width = 104 };

    /// <summary>
    /// The tools the open project cuts with, so Delete can say what it is about to cost.
    ///
    /// A layer whose saved tool is gone falls back to a default the next time the project is
    /// opened — silently, and to a different bit. Knowing *that this one* is in use turns a general
    /// warning into a specific one, which is the difference between a dialog somebody reads and a
    /// dialog somebody learns to dismiss.
    /// </summary>
    private readonly IReadOnlyCollection<Guid> _inUse;

    public ToolLibraryWindow(ToolLibrary library, IReadOnlyCollection<Guid>? inUse = null)
    {
        ArgumentNullException.ThrowIfNull(library);

        _library = library;
        _inUse = inUse ?? [];

        Title = "Tools";
        AppIcon.Apply(this);
        Width = 720;

        // Tall enough for the longest form the window can show, so nobody has to resize it to read
        // the field they came for.
        //
        // 560 was a hair short, and it was short in the one place that matters: a V-bit has the most
        // rows — name, kind, included angle, tip width, cone depth, feed, plunge, spindle, notes —
        // and the spindle speed sat half below the fold with the scrollbar the only clue. Nine rows
        // at 54 px, the notes box at 74, the cut-width panel and the buttons under them.
        Height = 690;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _list.SelectionChanged += (_, _) =>
        {
            if (!_rebuilding)
            {
                Load(_list.SelectedItem as Tool);
            }
        };
        _list.ItemTemplate = new FuncDataTemplate<Tool>((tool, _) => Row(tool));

        foreach (var box in new[] { _diameter, _tip, _angle, _maxDepth, _stepdown, _feed, _plunge, _rpm })
        {
            box.TextChanged += (_, _) => UpdateEffect();
        }

        _kind.SelectionChanged += (_, _) =>
        {
            ShowFieldsForKind();
            UpdateEffect();
        };

        BuildForm();

        _order.ItemsSource = new[] { "Library", "Name", "Kind", "Size" };
        _order.SelectedIndex = 0;
        _order.SelectionChanged += (_, _) =>
        {
            _sort = (ToolSort)Math.Max(0, _order.SelectedIndex);
            Refresh();
        };

        var add = new Button { Content = "New" };
        add.Click += (_, _) =>
        {
            Reveal(null);
            Load(null);
        };

        // Copy, which the workshop asked for and which is the one of the three with no design to it:
        // tools come in families, and entering the second from scratch is retyping the first.
        var copy = new Button { Content = "Copy" };
        copy.Click += async (_, _) => await CopyAsync();

        var save = new Button { Content = "Save", IsDefault = true };
        save.Click += (_, _) => Commit();

        var remove = new Button { Content = "Delete" };
        remove.Click += async (_, _) => await RemoveAsync();

        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close();

        // The effect panel sits outside the scroller, on its own row. It is the most useful thing
        // in the window — the number the operator cannot work out from the geometry above — and
        // inside the scroller it was the first thing pushed below the fold.
        var effectPanel = new Border
        {
            Margin = new Thickness(16, 10, 0, 0),
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0x5A, 0xA9, 0xF5)),
            Child = _effect,
        };

        Content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children =
            {
                Place(Sidebar(), 0, 0),
                Place(new ScrollViewer { Content = _form, Margin = new Thickness(16, 0, 0, 0) }, 0, 1),
                Place(effectPanel, 1, 1),
                Place(
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Margin = new Thickness(0, 14, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { add, copy, remove, save, close },
                    },
                    2,
                    1),
            },
        };

        Refresh();
        Load(_library.Tools.FirstOrDefault());
    }

    /// <summary>
    /// The list, with the two controls that decide what is in it and in what order.
    ///
    /// **The filter is buttons rather than a dropdown**, because a dropdown hides which filter is
    /// on, and a filter you cannot see is how somebody concludes their tools have vanished. All
    /// four are on screen with the active one pressed, so the list always has its explanation
    /// beside it.
    /// </summary>
    private Grid Sidebar()
    {
        var filters = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
        };

        void Filter(string label, ToolKind? kind)
        {
            var button = new ToggleButton
            {
                Content = label,
                FontSize = 11,
                Padding = new Thickness(8, 3),
                IsChecked = kind == _only,
            };

            button.Click += (_, _) =>
            {
                _only = kind;
                ShowFilter();
                Refresh();
            };

            _filters.Add((button, kind));
            filters.Children.Add(button);
        }

        Filter("All", null);
        Filter("V-bits", ToolKind.VBit);
        Filter("End mills", ToolKind.EndMill);
        Filter("Drills", ToolKind.Drill);

        var sort = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 6),
            Children =
            {
                new TextBlock
                {
                    Text = "Sort",
                    FontSize = 11,
                    Opacity = 0.75,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                _order,
            },
        };

        // **A Grid, not a StackPanel**, and a `/code-review` pass is why — then a screenshot, which
        // showed it was worse than the review had reasoned. A vertical `StackPanel` measures its
        // children with infinite height, so the `ListBox` reported the height of every row it had
        // and its own scroller never engaged: on a thirty-tool library the list ran straight off the
        // bottom of the window with no scrollbar and sixteen tools unreachable. The one case this
        // whole story exists for, broken by the layout it was given.
        //
        // The two controls take their own `Auto` rows and the list takes the `*`, so it is measured
        // against what is left and scrolls inside it.
        var sidebar = new Grid
        {
            Width = 250,
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
        };

        sidebar.Children.Add(Place(filters, 0, 0));
        sidebar.Children.Add(Place(sort, 1, 0));
        sidebar.Children.Add(Place(_list, 2, 0));

        return sidebar;
    }

    /// <summary>
    /// Sets the filter and the sort, for a screenshot.
    ///
    /// Both are private and deliberately so; this is the one way in from outside, and it goes
    /// through the same refresh a click does rather than round it.
    /// </summary>
    internal void ShowOnly(ToolKind? only, ToolSort sort)
    {
        _only = only;
        _sort = sort;
        _order.SelectedIndex = (int)sort;

        ShowFilter();
        Refresh();
    }

    /// <summary>Which filter button looks pressed. Exactly one, always.</summary>
    private void ShowFilter()
    {
        foreach (var (button, kind) in _filters)
        {
            button.IsChecked = kind == _only;
        }
    }

    /// <summary>
    /// Shows a tool that a filter would otherwise hide, by turning the filter off.
    ///
    /// Not called `Show`: that overloads `Window.Show`, and `Show(null)` bound to this one only
    /// because C# drops base-class candidates once a derived one applies. It read as showing the
    /// window twice at the call site, and any future `Show(x)` in this class would have been one
    /// implicit conversion from binding to the wrong method.
    ///
    /// **A new or copied tool has to appear**, or the button looks broken. Clearing the filter is
    /// the honest way to do that: an exception to the filter would leave the list showing something
    /// the buttons say is not there, which is the same confusion one step further in.
    /// </summary>
    private void Reveal(Tool? tool)
    {
        if (_only is { } kind && (tool is null || tool.Kind != kind))
        {
            _only = null;
            ShowFilter();
            Refresh();
        }
    }

    /// <summary>
    /// A copy of the selected tool, with its own id.
    ///
    /// **The id is the whole risk.** `Tool.Id` is what a project stores, so two tools sharing one
    /// are the same tool to everything downstream — a job naming the copy would cut with the
    /// original, and nothing anywhere would say so. A new `Guid`, a suffixed name, and the name box
    /// focused and selected so the first keystroke renames it.
    ///
    /// Saved immediately, because a copy that exists only in the form is indistinguishable from an
    /// edit of the tool it came from: pressing Save would be the first moment the two were separate,
    /// and until then Delete would be aimed at the wrong one.
    /// </summary>
    private async Task CopyAsync()
    {
        if (_editing is not { } original)
        {
            return;
        }

        // **Asked for before the copy exists**, which the workshop asked for: the button used to
        // make the copy and leave the name box selected, which works if you notice and leaves a
        // "1.0 mm end mill (copy)" in the library if you do not. The proposal is still the suffixed
        // name, selected, so the first keystroke replaces it.
        var name = await NameWindow.AskAsync(
            this,
            "Copy tool",
            $"A copy of “{original.Name}”, with its own feeds and notes. What is it called?",
            original.Name + " (copy)",
            "Copy");

        if (name is null)
        {
            return;
        }

        // **What is on screen, not what was last loaded.** A `/code-review` pass found the copy
        // taken from the stored tool, so feeds typed and not yet saved went into neither the copy
        // nor anywhere else: the operator adjusts the 0.8 mm mill, presses Copy meaning "and one
        // like this", and gets the old numbers twice. `Read` is the form as it stands; the id it
        // carries is the original's and `Copy` replaces it regardless.
        //
        // The original keeps its saved values — the edits go to the copy, which is where the
        // operator was looking when they pressed the button.
        var copy = ToolView.Copy(Read() ?? original, name);

        _library = _library.With(copy);
        _library.Save();

        Reveal(copy);
        Refresh();

        _list.SelectedItem = _library.Tools.FirstOrDefault(t => t.Id == copy.Id);

        _name.Focus();
        _name.SelectAll();
    }

    /// <summary>
    /// One tool in the list: what it is, what it is called, and how big it is.
    ///
    /// **Built before the sorting and the filtering, deliberately.** The entry was the name alone,
    /// and a name is whatever somebody typed — this machine's own library has `1mm Corn Endmill`
    /// and `1.2 mm end mill` three rows apart, with no common shape to scan down. A kind and a size
    /// in columns of their own make the list readable without reordering it, and the two features
    /// that reorder it are then for a library that is genuinely long rather than merely untidy.
    ///
    /// The size is the number somebody would say out loud about the bit: a V-bit's tip, an end
    /// mill's diameter. A drill's diameter comes from the drill file rather than from the library,
    /// so the shipped drill has none and the column is left empty rather than showing 0.00.
    /// </summary>
    private static Control Row(Tool? tool)
    {
        if (tool is null)
        {
            return new TextBlock();
        }

        var size = ToolView.SizeOf(tool);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(4, 3),
            Children =
            {
                Place(Mark(tool.Kind), 0, 0),
                Place(
                    new TextBlock
                    {
                        Text = tool.Name,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 6, 0),
                    },
                    0,
                    1),
                Place(
                    new TextBlock
                    {
                        Text = size > 0 ? Nm.ToMillimetreString(size, 2) : string.Empty,
                        FontSize = 11,
                        Opacity = 0.75,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    0,
                    2),
            },
        };
    }

    /// <summary>
    /// A letter in a box, coloured by kind.
    ///
    /// One glyph rather than a word: the word is longer than most of the names it would sit beside,
    /// and the three kinds are few enough to learn in one look at the filter buttons above, which
    /// spell them out.
    /// </summary>
    private static Border Mark(ToolKind kind)
    {
        var (letter, colour) = kind switch
        {
            ToolKind.VBit => ("V", Color.FromRgb(0xE8, 0x8A, 0x3C)),
            ToolKind.EndMill => ("E", Color.FromRgb(0x5A, 0xA9, 0xF5)),
            _ => ("D", Color.FromRgb(0x8A, 0xC9, 0x6A)),
        };

        return new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(colour, 0.22),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = letter,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(colour),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private static Control Place(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    /// <summary>
    /// The list, filtered and ordered.
    ///
    /// The rule is <see cref="ToolView.Arrange"/>, in Core beside the library it describes — a
    /// library of thirty can be arranged in a test, and this window is the four controls over it.
    /// </summary>
    private void Refresh()
    {
        var selected = (_list.SelectedItem as Tool)?.Id;

        var shown = ToolView.Arrange(_library.Tools, _only, _sort);

        _rebuilding = true;

        try
        {
            _list.ItemsSource = shown;
            _list.SelectedItem = shown.FirstOrDefault(t => t.Id == selected);
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void Load(Tool? tool)
    {
        _editing = tool;


        _name.Text = tool?.Name ?? "New tool";
        _kind.SelectedItem = tool?.Kind ?? ToolKind.VBit;
        _diameter.Text = Mm(tool?.DiameterNm ?? Nm.FromMillimetres(1.0));
        _tip.Text = Mm(tool?.TipNm ?? Nm.FromMillimetres(0.1));
        _angle.Text = (tool?.IncludedAngleDegrees ?? 30).ToString("F0", CultureInfo.InvariantCulture);
        _maxDepth.Text = Mm(tool?.MaxDepthNm ?? 0);
        _stepdown.Text = Mm(tool?.StepdownNm ?? 0);
        _feed.Text = (tool?.FeedMmPerMin ?? 200).ToString(CultureInfo.InvariantCulture);
        _plunge.Text = (tool?.PlungeMmPerMin ?? 60).ToString(CultureInfo.InvariantCulture);
        _rpm.Text = (tool?.SpindleRpm ?? 10_000).ToString(CultureInfo.InvariantCulture);
        _notes.Text = tool?.Notes ?? string.Empty;

        ShowFieldsForKind();
        UpdateEffect();
    }

    /// <summary>Builds every row once. Called from the constructor and never again.</summary>
    private void BuildForm()
    {
        void Row(string label, Control editor, ToolKind? onlyFor = null)
        {
            var row = Field(label, editor);
            _rows.Add((row, onlyFor));
            _form.Children.Add(row);
        }

        Row("Name", _name);
        Row("Kind", _kind);
        Row("Included angle (°)", _angle, ToolKind.VBit);
        Row("Tip width (mm)", _tip, ToolKind.VBit);
        Row("Cone ends at (mm, 0 = unknown)", _maxDepth, ToolKind.VBit);
        Row("Diameter (mm)", _diameter, ToolKind.EndMill);
        Row("Stepdown (mm, 0 = default)", _stepdown, ToolKind.EndMill);
        Row("Feed (mm/min)", _feed);
        Row("Plunge (mm/min)", _plunge);
        Row("Spindle (rpm)", _rpm);
        Row("Notes", _notes);

    }

    private void ShowFieldsForKind()
    {
        var kind = _kind.SelectedItem as ToolKind? ?? ToolKind.VBit;

        foreach (var (row, onlyFor) in _rows)
        {
            // A drill has a diameter too, but it comes from the drill file rather than from here,
            // so the geometry rows belong to the two kinds whose size the operator chooses.
            row.IsVisible = onlyFor switch
            {
                null => true,
                ToolKind.VBit => kind == ToolKind.VBit,
                _ => kind != ToolKind.VBit,
            };
        }
    }

    private static StackPanel Field(string label, Control editor) => new()
    {
        Spacing = 2,
        Children =
        {
            new TextBlock { Text = label, FontSize = 11, Opacity = 0.75 },
            editor,
        },
    };

    /// <summary>
    /// Shows what the tool will do, live. For a V-bit that is the cut width at a representative
    /// depth, because the width is not something the operator gets to type in.
    /// </summary>
    private void UpdateEffect()
    {
        var tool = Read();
        if (tool is null)
        {
            _effect.Text = "Fill in the numbers above.";
            return;
        }

        if (tool.Kind == ToolKind.VBit)
        {
            var at5 = Nm.ToMillimetreString(tool.WidthAtDepth(Nm.FromMillimetres(0.05)), 3);
            var at10 = Nm.ToMillimetreString(tool.WidthAtDepth(Nm.FromMillimetres(0.10)), 3);
            var per = Nm.ToMillimetreString((long)(tool.WidthPerDepth * Nm.FromMillimetres(0.01)), 4);

            var effect = string.Create(
                CultureInfo.InvariantCulture,
                $"Cuts {at5} mm wide at 0.05 mm deep, {at10} mm at 0.10 mm.\n" +
                $"Every 0.01 mm of depth error changes that by {per} mm — which is what decides whether this board needs height mapping.");

            // Said here, beside the number that caused it, rather than only in an export somewhere
            // later. This panel already updates as the fields are typed, so the notice arrives at
            // the moment the tip is entered — which is the moment it can still be a typo.
            _effect.Text = ToolAdvice.TipLooksTooFine(tool) is { } tip
                ? effect + "\n\n" + tip
                : effect;

            return;
        }

        _effect.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Cuts {Nm.ToMillimetreString(tool.DiameterNm, 3)} mm wide at any depth.");
    }

    private Tool? Read()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            return null;
        }

        double Number(TextBox box, double fallback) =>
            double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        return new Tool
        {
            // Editing keeps the id, so projects cut with this tool can still be matched to it.
            Id = _editing?.Id ?? Guid.NewGuid(),
            Name = _name.Text!.Trim(),
            Kind = _kind.SelectedItem as ToolKind? ?? ToolKind.VBit,
            DiameterNm = Nm.FromMillimetres(Number(_diameter, 1.0)),
            TipNm = Nm.FromMillimetres(Number(_tip, 0.1)),
            IncludedAngleDegrees = Number(_angle, 30),
            MaxDepthNm = Nm.FromMillimetres(Number(_maxDepth, 0)),
            StepdownNm = Nm.FromMillimetres(Number(_stepdown, 0)),
            FeedMmPerMin = (long)Number(_feed, 200),
            PlungeMmPerMin = (long)Number(_plunge, 60),
            SpindleRpm = (int)Number(_rpm, 10_000),

            // **Carried rather than defaulted**, which is a defect found while adding Copy: the
            // form has no field for either, so a tool whose `Flutes` had been set by hand in
            // `tools.json` lost it the moment it was saved here — and `Flutes` is what the chip
            // load is divided by, so the number would have come back wrong rather than absent.
            // Nothing in the app can set them yet; this stops the window from unsetting them.
            Flutes = _editing?.Flutes ?? 2,
            FluteLengthNm = _editing?.FluteLengthNm ?? 0,
            Notes = string.IsNullOrWhiteSpace(_notes.Text) ? null : _notes.Text!.Trim(),
        };
    }

    private void Commit()
    {
        if (Read() is not { } tool)
        {
            return;
        }

        _library = _library.With(tool);
        _library.Save();

        _editing = tool;

        // **The filter has to give way here as it does for New and Copy**, which a `/code-review`
        // pass caught. Change a tool's kind while filtered to the kind it used to be, press Save,
        // and the tool was written correctly and then vanished: it was no longer in the filtered
        // list, so nothing could be selected, and the empty selection reset the form to a blank new
        // tool. Saved, gone from the list, and the form apparently cleared — three wrong signals
        // from one correct write.
        Reveal(tool);
        Refresh();

        _list.SelectedItem = _library.Tools.FirstOrDefault(t => t.Id == tool.Id);
    }

    /// <summary>
    /// Deletes the selected tool, after asking.
    ///
    /// **Asked for from the workshop**, and it is the one button in this window that cannot be
    /// undone: there is no history, the file is written immediately, and the only way back is to
    /// retype the tool. The question names the bit rather than saying "this tool", because the
    /// list and the form are both still on screen behind the dialog and a question that does not
    /// name its subject is one somebody answers from memory.
    ///
    /// **And it says what the loss costs.** A layer that named this tool falls back to a default
    /// the next time its project is opened — a different bit, chosen silently — so when the open
    /// project cuts with it, the dialog says so.
    /// </summary>
    private async Task RemoveAsync()
    {
        if (_editing is not { } doomed)
        {
            return;
        }

        var used = _inUse.Contains(doomed.Id)
            ? " The open project cuts with it; those layers will fall back to a default bit."
            : string.Empty;

        var answer = await ConfirmWindow.AskAsync(
            this,
            "Delete tool",
            $"Delete “{doomed.Name}”?{used} This cannot be undone.",
            "Delete",
            discardText: null);

        if (answer != ConfirmResult.Save)
        {
            return;
        }

        _library = _library.Without(doomed.Id);
        _library.Save();

        Refresh();

        // The first tool *in the list*, not in the library: with a filter on, the library's first
        // tool may not be shown, and loading it left the form editing something no row pointed at
        // while Delete stayed live against it. A second click would then have deleted a tool the
        // operator never selected — the confirmation names it, which was the only thing in the way.
        var next = _list.Items.OfType<Tool>().FirstOrDefault();

        _list.SelectedItem = next;
        Load(next);
    }

    private static string Mm(long nm) => Nm.ToMillimetreString(nm, 3);
}
