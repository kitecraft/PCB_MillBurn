using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using MillBurn.Core;
using MillBurn.Gcode;
using static System.FormattableString;

namespace MillBurn.App.Views;

/// <summary>
/// Checks that measure the machine rather than the board.
///
/// Needs no board open, for the same reason the test cuts do not: what it measures belongs to the
/// mill, not to whatever design happens to be loaded. The numbers it produces are the ones the app
/// otherwise assumes — that a position reached from the left is the position reached from the
/// right, and that X and Y meet at ninety degrees.
/// </summary>
public sealed class MachineCheckWindow : Window
{
    private readonly ComboBox _kind = new() { Width = 300 };
    private readonly ComboBox _axis = new() { Width = 120 };
    private readonly ComboBox _tool = new() { Width = 260 };

    private readonly Dictionary<string, NumericUpDown> _numbers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _rows = new(StringComparer.Ordinal);

    private readonly TextBlock _effect = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _warning = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, IsVisible = false };
    private readonly Button _write = new() { Content = "Write files…", IsDefault = true };

    private readonly MachineSettings _machine;

    public MachineCheckWindow(ToolLibrary library, MachineSettings machine)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(machine);

        _machine = machine;

        Title = "Machine checks";
        AppIcon.Apply(this);
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PageBackground");

        _kind.ItemsSource = new[] { "Backlash — slack in one axis", "Squareness — X against Y" };
        _kind.SelectedIndex = 0;

        _axis.ItemsSource = new[] { "X", "Y" };
        _axis.SelectedIndex = 0;

        // An end mill, plunged: a twist drill wanders as it enters by about as much as either
        // check is trying to measure. The list still holds everything, so an operator who knows
        // better can say so.
        _tool.ItemsSource = library.Tools.ToList();
        _tool.SelectedItem = library.Tools.FirstOrDefault(t => t.Kind == ToolKind.EndMill)
            ?? library.Tools.FirstOrDefault();

        Content = Build();

        _kind.SelectionChanged += (_, _) => { ShowRowsForKind(); Refresh(); };
        _axis.SelectionChanged += (_, _) => Refresh();
        _tool.SelectionChanged += (_, _) => Refresh();

        ShowRowsForKind();
        Refresh();
    }

    /// <summary>What was chosen, or null if the dialog was called off.</summary>
    public MachineCheckOptions? Result { get; private set; }

    /// <summary>Opens on one check or the other, so each can be seen in a screenshot.</summary>
    internal void Show(MachineCheckKind kind)
    {
        _kind.SelectedIndex = kind == MachineCheckKind.Squareness ? 1 : 0;
        ShowRowsForKind();
        Refresh();
    }

    public static async Task<MachineCheckOptions?> AskAsync(Window owner, ToolLibrary library, MachineSettings machine)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var window = new MachineCheckWindow(library, machine)
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
            Text = "Drill a few holes in scrap, stand pins in them, and measure. The test cuts ask "
                + "what a bit really does; these ask what the machine really does.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12),
            [!ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
        });

        body.Children.Add(Row("Check", _kind));
        body.Children.Add(_rows["axis"] = Row("Axis", _axis));
        body.Children.Add(Row("Bit", _tool));

        body.Children.Add(Gap());

        Add(body, "span", "Holes apart (mm)", 60, 20, 400, 5, "F0");
        Add(body, "overshoot", "Approach from (mm past)", 4, 0.5, 25, 0.5, "F1");
        Add(body, "depth", "Hole depth (mm)", 1.7, 0.3, 10, 0.1, "F1");
        Add(body, "pecks", "Pecks per hole", 4, 1, 20, 1, "F0");

        body.Children.Add(new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0x5A, 0xA9, 0xF5)),
            Child = _effect,
        });

        _warning[!ForegroundProperty] = new DynamicResourceExtension("DrcViolation");
        body.Children.Add(new Border { Margin = new Thickness(0, 8, 0, 0), Child = _warning });

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();

        _write.Click += (_, _) =>
        {
            Result = Read();
            Close();
        };

        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { cancel, _write },
        });

        return body;
    }

    private static Border Gap() => new() { Height = 10 };

    private static Grid Row(string label, Control editor)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };

        Grid.SetColumn(editor, 1);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            Margin = new Thickness(0, 3),
            Children = { text, editor },
        };
    }

    private void Add(Panel into, string key, string label, double value, double min, double max, double step, string format)
    {
        var box = new NumericUpDown
        {
            Value = (decimal)value,
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = (decimal)step,
            FormatString = format,
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        box.ValueChanged += (_, _) => Refresh();

        _numbers[key] = box;
        _rows[key] = Row(label, box);

        into.Children.Add(_rows[key]);
    }

    /// <summary>
    /// The axis only means anything to the backlash check; the square uses both.
    ///
    /// The span moves with the check for a reason worth stating: backlash is a fixed offset, so a
    /// short coupon measures it as well as a long one, while a skew grows with distance and a short
    /// square cannot see a small angle. Only a span still sitting on the other check's default is
    /// moved — a number somebody has typed is theirs.
    /// </summary>
    private void ShowRowsForKind()
    {
        _rows["axis"].IsVisible = Kind == MachineCheckKind.Backlash;

        var (mine, theirs) = Kind == MachineCheckKind.Squareness
            ? (SquarenessSpanMm, BacklashSpanMm)
            : (BacklashSpanMm, SquarenessSpanMm);

        if (Math.Abs(Value("span") - theirs) < 0.001)
        {
            _numbers["span"].Value = (decimal)mine;
        }
    }

    /// <summary>Enough to straddle with a caliper; backlash does not grow with distance.</summary>
    private const double BacklashSpanMm = 60;

    /// <summary>As long as an offcut usually allows: a skew of a tenth of a degree is 0.17 mm here.</summary>
    private const double SquarenessSpanMm = 100;

    private MachineCheckKind Kind => _kind.SelectedIndex == 1
        ? MachineCheckKind.Squareness
        : MachineCheckKind.Backlash;

    private double Value(string key) => (double)(_numbers[key].Value ?? 0);

    private MachineCheckOptions Read() => new()
    {
        Tool = _tool.SelectedItem as Tool ?? new Tool { Name = "end mill", Kind = ToolKind.EndMill },
        Kind = Kind,
        Axis = _axis.SelectedIndex == 1 ? CheckAxis.Y : CheckAxis.X,
        SpanMm = Value("span"),
        OvershootMm = Value("overshoot"),
        DepthMm = Value("depth"),
        Pecks = (int)Value("pecks"),
        SafeZMm = _machine.SafeZMm,
        ApproachZMm = _machine.ApproachZMm,
        Decimals = _machine.Decimals,
    };

    /// <summary>
    /// What this check will come to, before it is run: the coupon it needs, what each pair should
    /// read, and the arithmetic that turns those readings into a number.
    ///
    /// Shown here rather than only on the page beside the file, because the span is a choice with
    /// consequences — a squareness check on a short coupon cannot see a small angle — and the place
    /// to learn that is while the number is still being typed.
    /// </summary>
    private void Refresh()
    {
        if (_tool.SelectedItem is not Tool)
        {
            _effect.Text = "The tool library has no bits in it. Edit ▸ Tool library… first.";
            _write.IsEnabled = false;
            return;
        }

        var (_, report) = MachineCheck.Generate(Read());

        _write.IsEnabled = true;

        var lines = new List<string>
        {
            Invariant($"{report.Holes.Count} holes on a piece of scrap at least {report.StockWidthMm:F0} × {report.StockHeightMm:F0} mm, about {Math.Max(1, Math.Round(report.EstimatedSeconds)):F0} seconds."),
            Invariant($"Afterwards: stand two {report.PinMm:F2} mm pins in a pair and measure."),
        };

        foreach (var pair in report.Pairs)
        {
            lines.Add(report.Kind == MachineCheckKind.Backlash
                ? Invariant($"  {pair.Name}: {pair.NominalMm:F2} mm between centres plus one pin — {pair.Meaning}")
                : Invariant($"  {pair.Name}: {pair.NominalMm:F2} mm between centres — {pair.Meaning}"));
        }

        lines.Add(report.Kind == MachineCheckKind.Backlash
            ? "Then: backlash = ( row 2 − row 3 ) ÷ 2."
            : "Then: skew = arcsin( ( one diagonal − the other ) ÷ the diagonal ).");

        _effect.Text = string.Join('\n', lines);

        _warning.Text = string.Join("\n", report.Warnings);
        _warning.IsVisible = report.Warnings.Count > 0;
    }
}
