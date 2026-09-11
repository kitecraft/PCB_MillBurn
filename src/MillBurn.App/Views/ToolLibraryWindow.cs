using System.Globalization;
using Avalonia;
using Avalonia.Controls;
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
    private readonly ListBox _list = new() { Width = 220 };
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

    public ToolLibraryWindow(ToolLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        _library = library;

        Title = "Tools";
        AppIcon.Apply(this);
        Width = 720;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _list.SelectionChanged += (_, _) => Load(_list.SelectedItem as Tool);
        _list.ItemTemplate = new FuncDataTemplate<Tool>((tool, _) =>
            new TextBlock { Text = tool?.Name ?? string.Empty, Margin = new Thickness(4, 3) });

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

        var add = new Button { Content = "New" };
        add.Click += (_, _) => Load(null);

        var save = new Button { Content = "Save", IsDefault = true };
        save.Click += (_, _) => Commit();

        var remove = new Button { Content = "Delete" };
        remove.Click += (_, _) => Remove();

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
                Place(_list, 0, 0),
                Place(new ScrollViewer { Content = _form, Margin = new Thickness(16, 0, 0, 0) }, 0, 1),
                Place(effectPanel, 1, 1),
                Place(
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Margin = new Thickness(0, 14, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { add, remove, save, close },
                    },
                    2,
                    1),
            },
        };

        Refresh();
        Load(_library.Tools.FirstOrDefault());
    }

    private static Control Place(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    private void Refresh()
    {
        var selected = (_list.SelectedItem as Tool)?.Id;
        _list.ItemsSource = _library.Tools.ToList();
        _list.SelectedItem = _library.Tools.FirstOrDefault(t => t.Id == selected);
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
        _rpm.Text = (tool?.SpindleRpm ?? 12_000).ToString(CultureInfo.InvariantCulture);
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
            SpindleRpm = (int)Number(_rpm, 12_000),
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
        Refresh();
        _list.SelectedItem = _library.Tools.FirstOrDefault(t => t.Id == tool.Id);
    }

    private void Remove()
    {
        if (_editing is null)
        {
            return;
        }

        _library = _library.Without(_editing.Id);
        _library.Save();

        Refresh();
        Load(_library.Tools.FirstOrDefault());
    }

    private static string Mm(long nm) => Nm.ToMillimetreString(nm, 3);
}
