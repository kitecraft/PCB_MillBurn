using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using static System.FormattableString;

namespace MillBurn.App.Views;

/// <summary>What the operator asked for, and whether the coupon gets a probing routine too.</summary>
/// <param name="Options">The test itself.</param>
/// <param name="WithProbe">
/// Whether to write a probing routine sized to this coupon. Deliberately separate from the board's
/// height map: a map describes one piece of stock as it was clamped, and a test coupon is a
/// different piece in a different place.
/// </param>
public sealed record TestCutChoice(TestCutOptions Options, bool WithProbe);

/// <summary>
/// Test cuts for dialling a bit in on scrap.
///
/// Needs no board, which is why it is offered whether or not one is open: what it tests is the tool
/// library's claim about a physical object, and that claim is the same whichever design happens to
/// be loaded.
/// </summary>
public sealed class TestCutWindow : Window
{
    private readonly ComboBox _kind = new() { Width = 200 };
    private readonly ComboBox _tool = new() { Width = 260 };

    private readonly Dictionary<string, NumericUpDown> _numbers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _flags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _rows = new(StringComparer.Ordinal);

    private readonly TextBlock _effect = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly MachineSettings _machine;

    public TestCutWindow(ToolLibrary library, MachineSettings machine, Tool? preferred = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(machine);

        _machine = machine;

        Title = "Test cuts";
        AppIcon.Apply(this);
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PageBackground");

        _kind.ItemsSource = new[] { "Depth and width", "Feed rate" };
        _kind.SelectedIndex = 0;

        _tool.ItemsSource = library.Tools.ToList();
        _tool.SelectedItem = preferred is not null && library.Tools.Any(t => t.Id == preferred.Id)
            ? library.Tools.First(t => t.Id == preferred.Id)
            : LayerOperations.DefaultToolFor(OperationKind.Isolation, library.Tools);

        Content = Build();

        _kind.SelectionChanged += (_, _) =>
        {
            ShowRowsForKind();
            Refresh();
        };

        _tool.SelectionChanged += (_, _) => Refresh();

        ShowRowsForKind();
        Refresh();
    }

    public TestCutChoice? Result { get; private set; }

    public static async Task<TestCutChoice?> AskAsync(
        Window owner, ToolLibrary library, MachineSettings machine, Tool? preferred = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var window = new TestCutWindow(library, machine, preferred)
        {
            RequestedThemeVariant = owner.ActualThemeVariant,
        };

        await window.ShowDialog(owner);
        return window.Result;
    }

    // ------------------------------------------------------------------ layout

    private StackPanel Build()
    {
        var body = new StackPanel { Spacing = 4, Margin = new Thickness(18) };

        body.Children.Add(new TextBlock
        {
            Text = "Cut a few lines on scrap, measure them, and put the answer back in the tool "
                + "library. Everything this app computes about a cut rests on that number.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12),
            [!ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
        });

        body.Children.Add(Row("Test", _kind));
        body.Children.Add(Row("Bit", _tool));

        body.Children.Add(Gap());

        Add(body, "from", "First line at (mm)", 0.02, 0, 3, 0.01, "F3");
        Add(body, "step", "Deeper each line by (mm)", 0.02, 0.001, 1, 0.01, "F3");
        Add(body, "depth", "Depth (mm)", 0.05, 0.001, 3, 0.01, "F3");
        Add(body, "feedStep", "Feed changes by (mm/min)", 50, 1, 2000, 10, "F0");

        body.Children.Add(Gap());

        Add(body, "lines", "Lines", 6, 1, 40, 1, "F0");
        Add(body, "length", "Line length (mm)", 15, 2, 200, 1, "F0");
        Add(body, "spacing", "Lines apart (mm)", 2, 0.5, 20, 0.5, "F1");

        body.Children.Add(Flag(
            "repeat",
            "Cut line 1 again at the far end",
            "Two identical cuts at opposite ends of the coupon should measure the same. When they "
            + "do not, the stock is tilted or Z moved, and nothing else on it can be trusted.",
            true));

        body.Children.Add(Flag(
            "probe",
            "Also write a probing routine for the coupon",
            "Sized to this test's own area. Use it only if the scrap will not sit flat — and never "
            + "level a test cut with the board's height map, which describes a different piece of "
            + "stock in a different place.",
            false));

        var panel = new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0x5A, 0xA9, 0xF5)),
            Child = _effect,
        };

        body.Children.Add(panel);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();

        var write = new Button { Content = "Write files…", IsDefault = true };
        write.Click += (_, _) =>
        {
            Result = new TestCutChoice(Read(), _flags["probe"].IsChecked == true);
            Close();
        };

        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { cancel, write },
        });

        return body;
    }

    private static Border Gap() => new() { Height = 10 };

    private static Grid Row(string label, Control editor) => new()
    {
        ColumnDefinitions = new ColumnDefinitions("190,*"),
        Margin = new Thickness(0, 3),
        Children =
        {
            new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            },
            Place(editor, 1),
        },
    };

    private static Control Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private void Add(
        Panel into, string key, string label, double value,
        double min, double max, double increment, string format)
    {
        var box = new NumericUpDown
        {
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = (decimal)increment,
            Value = (decimal)value,
            FormatString = format,
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        box.ValueChanged += (_, _) => Refresh();

        _numbers[key] = box;

        var row = Row(label, box);
        _rows[key] = row;
        into.Children.Add(row);
    }

    private StackPanel Flag(string key, string label, string why, bool value)
    {
        var box = new CheckBox { Content = label, IsChecked = value, FontSize = 12 };
        box.IsCheckedChanged += (_, _) => Refresh();
        _flags[key] = box;

        return new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                box,
                new TextBlock
                {
                    Text = why,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                    Margin = new Thickness(26, 1, 0, 0),
                    [!ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
                },
            },
        };
    }

    /// <summary>Only the numbers this test actually uses. The rest would be noise to read past.</summary>
    private void ShowRowsForKind()
    {
        var depth = _kind.SelectedIndex == 0;

        _rows["from"].IsVisible = depth;
        _rows["step"].IsVisible = depth;
        _rows["depth"].IsVisible = !depth;
        _rows["feedStep"].IsVisible = !depth;
    }

    // ------------------------------------------------------------------ the numbers

    private double Value(string key) => (double)(_numbers[key].Value ?? 0);

    private TestCutOptions Read() => new()
    {
        Tool = _tool.SelectedItem as Tool ?? Tool.DefaultVBit,
        Kind = _kind.SelectedIndex == 0 ? TestCutKind.Depth : TestCutKind.Feed,
        LineCount = (int)Value("lines"),
        LineLengthMm = Value("length"),
        LineSpacingMm = Value("spacing"),
        StartDepthMm = Value("from"),
        DepthStepMm = Value("step"),
        DepthMm = Value("depth"),
        FeedStepMmPerMin = Value("feedStep"),
        RepeatFirstLine = _flags["repeat"].IsChecked == true,
        SafeZMm = _machine.SafeZMm,
        ApproachZMm = _machine.ApproachZMm,
        Decimals = _machine.Decimals,
    };

    /// <summary>
    /// What this test will actually do, recalculated as the fields are typed.
    ///
    /// The stock size matters most: the operator is about to go and find a piece of scrap, and
    /// discovering it was too small after clamping it is the one avoidable annoyance here.
    /// </summary>
    private void Refresh()
    {
        var options = Read();
        var (_, report) = TestCut.Generate(options);

        var range = options.Kind == TestCutKind.Depth
            ? Invariant($"{report.Lines.Min(l => l.DepthMm):F3} to {report.Lines.Max(l => l.DepthMm):F3} mm deep")
            : Invariant($"{report.Lines.Min(l => l.FeedMmPerMin):F0} to {report.Lines.Max(l => l.FeedMmPerMin):F0} mm/min");

        var text = Invariant(
            $"{report.Lines.Count} lines, {range}.\n")
            + Invariant($"Needs {report.StockWidthMm:F0} × {report.StockHeightMm:F0} mm of bare copper, ")
            + Invariant($"about {Math.Max(1, Math.Round(report.EstimatedSeconds)):F0} seconds.");

        foreach (var warning in report.Warnings)
        {
            text += "\n\n" + warning;
        }

        _effect.Text = text;
    }
}
