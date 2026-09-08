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
/// Built in code rather than XAML because the form is entirely driven by the tool kind — a V-bit
/// needs an angle and a tip and no diameter, an end mill the reverse — and expressing that as
/// visibility bindings across two layouts is more code than writing the fields out.
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

    public ToolLibraryWindow(ToolLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        _library = library;

        Title = "Tools";
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
            BuildForm();
            UpdateEffect();
        };

        var add = new Button { Content = "New" };
        add.Click += (_, _) => Load(null);

        var save = new Button { Content = "Save", IsDefault = true };
        save.Click += (_, _) => Commit();

        var remove = new Button { Content = "Delete" };
        remove.Click += (_, _) => Remove();

        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close();

        Content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children =
            {
                Place(_list, 0, 0),
                Place(new ScrollViewer { Content = _form, Margin = new Thickness(16, 0, 0, 0) }, 0, 1),
                Place(
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Margin = new Thickness(0, 14, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { add, remove, save, close },
                    },
                    1,
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

        BuildForm();
        UpdateEffect();
    }

    private void BuildForm()
    {
        var kind = _kind.SelectedItem as ToolKind? ?? ToolKind.VBit;

        _form.Children.Clear();
        _form.Children.Add(Field("Name", _name));
        _form.Children.Add(Field("Kind", _kind));

        if (kind == ToolKind.VBit)
        {
            _form.Children.Add(Field("Included angle (°)", _angle));
            _form.Children.Add(Field("Tip width (mm)", _tip));
            _form.Children.Add(Field("Cone ends at (mm, 0 = unknown)", _maxDepth));
        }
        else
        {
            _form.Children.Add(Field("Diameter (mm)", _diameter));
            _form.Children.Add(Field("Stepdown (mm, 0 = default)", _stepdown));
        }

        _form.Children.Add(Field("Feed (mm/min)", _feed));
        _form.Children.Add(Field("Plunge (mm/min)", _plunge));
        _form.Children.Add(Field("Spindle (rpm)", _rpm));
        _form.Children.Add(Field("Notes", _notes));

        _form.Children.Add(new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(4),
            Child = _effect,
        });
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

            _effect.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Cuts {at5} mm wide at 0.05 mm deep, {at10} mm at 0.10 mm.\n" +
                $"Every 0.01 mm of depth error changes that by {per} mm — which is what decides whether this board needs height mapping.");
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
