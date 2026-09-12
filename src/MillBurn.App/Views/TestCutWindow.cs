using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using static System.FormattableString;

namespace MillBurn.App.Views;

/// <summary>What to do about the coupon not being flat.</summary>
public enum TestCutLevelling
{
    /// <summary>Cut it as it lies. Right whenever the stock is properly held down, which is most of the time.</summary>
    None,

    /// <summary>
    /// Write only the probing routine, sized to this coupon.
    ///
    /// Its own routine, never the board's height map: a map describes one piece of stock as it was
    /// clamped, and a coupon is a different piece in a different place.
    /// </summary>
    WriteProbe,

    /// <summary>Cut it, levelled to a log from the routine above.</summary>
    FromLog,
}

/// <summary>What the operator asked for.</summary>
/// <param name="Options">The test itself.</param>
/// <param name="Levelling">Whether the coupon is being probed, levelled, or neither.</param>
/// <param name="LogPath">The probe log to level against, when there is one.</param>
public sealed record TestCutChoice(
    TestCutOptions Options, TestCutLevelling Levelling, string? LogPath);

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
    private readonly Dictionary<string, Control> _sections = new(StringComparer.Ordinal);

    private readonly TextBlock _effect = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly MachineSettings _machine;

    private readonly RadioButton _asItLies = new() { Content = "Cut it as it lies", IsChecked = true, FontSize = 12, GroupName = "surface" };
    private readonly RadioButton _probeFirst = new() { Content = "Probe the coupon first, and come back", FontSize = 12, GroupName = "surface" };
    private readonly RadioButton _fromLog = new() { Content = "Level to a probe log", FontSize = 12, GroupName = "surface" };

    private readonly TextBlock _logName = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _chooseLog = new() { Content = "Choose…", FontSize = 11, Padding = new Thickness(8, 2) };
    private readonly Button _write = new() { Content = "Write files…", IsDefault = true };

    private readonly ToolLibrary _library;

    private string? _logPath;

    /// <summary>What reopening a saved setup had to say, if anything. Shown in the summary.</summary>
    private string? _reopened;

    public TestCutWindow(
        ToolLibrary library,
        MachineSettings machine,
        Tool? preferred = null,
        TestCutLevelling surface = TestCutLevelling.None)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(machine);

        _machine = machine;
        _library = library;

        Title = "Test cuts";
        AppIcon.Apply(this);
        Width = 940;
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

        _probeFirst.IsChecked = surface == TestCutLevelling.WriteProbe;
        _fromLog.IsChecked = surface == TestCutLevelling.FromLog;
        _asItLies.IsChecked = surface == TestCutLevelling.None;

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

        body.Children.Add(Reopen());

        body.Children.Add(Row("Test", _kind));
        body.Children.Add(Row("Bit", _tool));

        // Two columns, because one was 1,300 pixels tall and ran off the top and bottom of a
        // 1080-high screen. What each test asks for goes on the left; what the coupon and the
        // machine need goes on the right, under the summary that both of them feed.
        var left = new StackPanel { Spacing = 4 };
        var right = new StackPanel { Spacing = 4 };

        // Two experiments that happen to share a coupon, headed by what each one calibrates.
        //
        // They are not two views of one test, and running them together is a convenience rather
        // than a requirement. The width of a V-cut is `tip + 2 x depth x tan(angle/2)`: the ladder
        // measures the width at one depth, which fixes the **tip** once the angle is known, and the
        // series measures how width changes with depth, whose slope is the **angle** and does not
        // involve the tip at all. Saying so in the heading is the difference between two boxes of
        // numbers and knowing which one to run.
        left.Children.Add(_sections["useLadder"] = Section(
            "useLadder",
            "Width ladder",
            "calibrates the tip width",
            "Short bands at one depth, each stepping over a little more than the last. The first "
            + "one with copper left standing in it is what this bit really cuts — found by eye, "
            + "with a loupe, no caliper. Given the included angle, that fixes the tip width: the "
            + "number most often wrong, and the one no caliper can reach."));

        Add(left, "rungs", "Rungs", 5, 2, 20, 1, "F0");
        Add(left, "ladderAt", "Cut at (mm deep)", 0.05, 0.001, 3, 0.01, "F3");

        // A feed test has neither of the two sections — it calibrates nothing, it shows you an edge
        // — so its rows would otherwise sit in the left column under no heading at all.
        left.Children.Add(_sections["feed"] = Heading("Feed series — finds the cleanest edge"));

        left.Children.Add(_sections["useSeries"] = Section(
            "useSeries",
            "Depth series",
            "calibrates the included angle",
            "Bands at increasing depth. Plot measured width against depth and the slope is "
            + "2 × tan(angle / 2) — the angle, with the tip cancelling out of it entirely. This one "
            + "wants a caliper, which is why each line is cut as a wide band rather than a groove."));

        Add(left, "lines", "Lines", 6, 1, 40, 1, "F0");
        Add(left, "from", "First line at (mm)", 0.02, 0, 3, 0.01, "F3");
        Add(left, "step", "Deeper each line by (mm)", 0.05, 0.001, 1, 0.01, "F3");
        Add(left, "depth", "Depth (mm)", 0.05, 0.001, 3, 0.01, "F3");
        Add(left, "feedStep", "Feed changes by (mm/min)", 50, 1, 2000, 10, "F0");
        Add(left, "passes", "Passes per line", 20, 1, 200, 1, "F0");
        Add(left, "stepover", "Passes step over (mm)", 0, 0, 5, 0.01, "F3");

        right.Children.Add(Heading("The coupon"));

        Add(right, "length", "Line length (mm)", 15, 2, 200, 1, "F0");
        Add(right, "spacing", "Lines apart (mm)", 2, 0.5, 20, 0.5, "F1");

        // Not part of either test: it asks whether the *stock* held still, which is the question
        // that decides whether anything else on the coupon can be believed. On a ladder-only
        // coupon it repeats the bottom rung, where a tilt shows up as the transition moving.
        right.Children.Add(Flag(
            "repeat",
            "Cut the first line again at the far end",
            "Two identical cuts at opposite ends of the coupon should measure the same. When they "
            + "do not, the stock is tilted or Z moved, and nothing else on it can be trusted.",
            true));

        right.Children.Add(Surface());

        right.Children.Add(new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0x5A, 0xA9, 0xF5)),
            Child = _effect,
        });

        var columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,28,*"),
            Margin = new Thickness(0, 4, 0, 0),
            Children = { Place(left, 0), Place(right, 2) },
        };

        body.Children.Add(columns);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();

        _write.Click += (_, _) =>
        {
            Result = new TestCutChoice(Read(), Levelling, _logPath);
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

    /// <summary>
    /// What to do about the coupon's own flatness — and the reason this is three choices rather
    /// than a checkbox.
    ///
    /// Probing and levelling cannot happen in one pass: the routine has to be run and its log kept
    /// before there is anything to level against. Writing both files at once produced a test cut
    /// that could not benefit from the probe sitting beside it, which is a trap dressed as a
    /// convenience. So it is two visits — write the probe, run it, come back with the log.
    /// </summary>
    private StackPanel Surface()
    {
        var picked = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(26, 2, 0, 0),
            Children = { _chooseLog, _logName },
        };

        _logName.VerticalAlignment = VerticalAlignment.Center;
        _logName[!ForegroundProperty] = new DynamicResourceExtension("TextSecondary");

        _chooseLog.Click += async (_, _) => await ChooseLogAsync();

        foreach (var option in new[] { _asItLies, _probeFirst, _fromLog })
        {
            option.IsCheckedChanged += (_, _) =>
            {
                picked.IsVisible = _fromLog.IsChecked == true;
                Refresh();
            };
        }

        picked.IsVisible = false;
        _logName.Text = "no log chosen";

        return new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                new TextBlock { Text = "The coupon's surface", FontSize = 12, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "Hold the scrap down across its whole area and the first option is right. "
                        + "The others are for stock that will not sit flat — and never for the board's "
                        + "height map, which describes a different piece in a different place.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                    Margin = new Thickness(0, 1, 0, 6),
                    [!ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
                },
                _asItLies,
                _probeFirst,
                _fromLog,
                picked,
            },
        };
    }

    /// <summary>
    /// The way back into a test that was set up once already.
    ///
    /// A coupon that gets probed first is written in one visit and cut in another, and in between
    /// this dialog closes and takes every number in it with it. The settings are saved beside the
    /// program precisely so the second visit does not start from memory — a test whose two halves
    /// disagree measures nothing — so the way to load them belongs at the top, before anything is
    /// typed rather than after.
    /// </summary>
    private StackPanel Reopen()
    {
        var open = new Button { Content = "Reopen a saved test…", FontSize = 11, Padding = new Thickness(8, 3) };

        open.Click += async (_, _) => await ReopenAsync();

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                open,
                new TextBlock
                {
                    Text = "Every test writes its settings beside its program.",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    [!ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
                },
            },
        };
    }

    private async Task ReopenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Reopen a saved test",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Test cut settings") { Patterns = ["*" + TestCutSetup.Extension] },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        if (TestCutSetup.Load(path) is not { } saved)
        {
            _reopened = Path.GetFileName(path) + " is not a saved test cut.";
            Refresh();
            return;
        }

        Restore(saved);
    }

    /// <summary>Puts a saved setup back into the dialog, and says what it could not put back.</summary>
    internal void Restore(TestCutSetup saved)
    {
        _kind.SelectedIndex = saved.Kind == TestCutKind.Depth ? 0 : 1;

        _numbers["length"].Value = (decimal)saved.LineLengthMm;
        _numbers["spacing"].Value = (decimal)saved.LineSpacingMm;
        _numbers["from"].Value = (decimal)saved.StartDepthMm;
        _numbers["step"].Value = (decimal)saved.DepthStepMm;
        _numbers["depth"].Value = (decimal)saved.DepthMm;
        _numbers["feedStep"].Value = (decimal)saved.FeedStepMmPerMin;
        _numbers["passes"].Value = saved.PassesPerLine;
        _numbers["stepover"].Value = (decimal)saved.StepoverMm;
        _numbers["ladderAt"].Value = (decimal)saved.DepthMm;

        // A count of zero is how "leave this half out" is stored, so it comes back as the switch
        // rather than as a zero in a box the operator then has to work out how to undo.
        _flags["useLadder"].IsChecked = saved.LadderRungs > 0;
        _flags["useSeries"].IsChecked = saved.LineCount > 0;

        if (saved.LadderRungs > 0)
        {
            _numbers["rungs"].Value = saved.LadderRungs;
        }

        if (saved.LineCount > 0)
        {
            _numbers["lines"].Value = saved.LineCount;
        }
        _flags["repeat"].IsChecked = saved.RepeatFirstLine;

        // The bit is the one thing that can fail to come back, and the one thing that must not fail
        // quietly: a depth series measured with a different cone is a page of numbers about nothing.
        if (saved.ToolIn(_library) is { } tool)
        {
            _tool.SelectedItem = _library.Tools.FirstOrDefault(t => t.Id == tool.Id);
            _reopened = null;
        }
        else
        {
            _reopened = FormattableString.Invariant(
                $"The bit this was set up with, “{saved.ToolName ?? "unnamed"}”, is not in the library any more. Pick the one you are testing.");
        }

        // Straight to the half the operator came back for.
        if (saved.ProbeWritten)
        {
            _fromLog.IsChecked = true;
        }

        ShowRowsForKind();
        Refresh();
    }

    private TestCutLevelling Levelling => _probeFirst.IsChecked == true
        ? TestCutLevelling.WriteProbe
        : _fromLog.IsChecked == true ? TestCutLevelling.FromLog : TestCutLevelling.None;

    private async Task ChooseLogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Probe log for this coupon",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Probe log") { Patterns = ["*.log", "*.txt", "*.csv", "*.nc"] },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            _logPath = path;
            _logName.Text = Path.GetFileName(path);
        }

        Refresh();
    }

    private static Border Gap() => new() { Height = 10 };

    private static Grid Row(string label, Control editor) => new()
    {
        ColumnDefinitions = new ColumnDefinitions("170,*"),
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

    /// <summary>
    /// A heading that turns one of the two tests on and off, and says what it is for.
    ///
    /// A checkbox rather than a radio pair because both together is the normal answer and either
    /// alone is a reasonable one — and because the rows beneath a heading grey out rather than
    /// vanish, which keeps the dialog the same shape however it is set.
    /// </summary>
    private StackPanel Section(string key, string title, string calibrates, string what)
    {
        var box = new CheckBox
        {
            IsChecked = true,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock
                    {
                        Text = "— " + calibrates,
                        FontWeight = FontWeight.Normal,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        [!ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
                    },
                },
            },
        };

        box.IsCheckedChanged += (_, _) =>
        {
            ShowRowsForKind();
            Refresh();
        };

        _flags[key] = box;

        return new StackPanel
        {
            Margin = new Thickness(0, 16, 0, 4),
            Children =
            {
                new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 0, 0, 10),
                    [!BackgroundProperty] = new DynamicResourceExtension("BorderSubtle"),
                },
                box,
                new TextBlock
                {
                    Text = what,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                    Margin = new Thickness(26, 2, 0, 8),
                    [!ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
                },
            },
        };
    }

    /// <summary>A plain heading for the rows that belong to neither test.</summary>
    private static StackPanel Heading(string title) => new()
    {
        Margin = new Thickness(0, 16, 0, 4),
        Children =
        {
            new Border
            {
                Height = 1,
                Margin = new Thickness(0, 0, 0, 10),
                [!BackgroundProperty] = new DynamicResourceExtension("BorderSubtle"),
            },
            new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold },
        },
    };

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

    /// <summary>
    /// Only the numbers this test actually uses, and only enabled where they apply.
    ///
    /// Rows grey out rather than disappear when their half is switched off, so the dialog keeps its
    /// shape and it stays obvious that the other test is a thing that exists. Rows that belong to
    /// the *other kind* of test do disappear, because those are noise rather than a choice.
    /// </summary>
    private void ShowRowsForKind()
    {
        var depth = _kind.SelectedIndex == 0;

        _rows["from"].IsVisible = depth;
        _rows["step"].IsVisible = depth;
        _rows["depth"].IsVisible = !depth;
        _rows["feedStep"].IsVisible = !depth;

        // A feed test is one pass by definition: what it asks is what an edge looks like, and a
        // band of overlapping passes hides every edge but the outer two. It has no ladder either —
        // a ladder asks about width, and a feed test deliberately holds width still.
        _rows["passes"].IsVisible = depth;
        _rows["stepover"].IsVisible = depth;
        _rows["rungs"].IsVisible = depth;
        _rows["ladderAt"].IsVisible = depth;

        _sections["useLadder"].IsVisible = depth;
        _sections["useSeries"].IsVisible = depth;
        _sections["feed"].IsVisible = !depth;

        // Off means the rows are still there to read, just not in play.
        var ladder = !depth || _flags["useLadder"].IsChecked == true;
        var series = !depth || _flags["useSeries"].IsChecked == true;

        _rows["rungs"].IsEnabled = ladder;
        _rows["ladderAt"].IsEnabled = ladder;

        foreach (var key in new[] { "lines", "from", "step", "passes", "stepover" })
        {
            _rows[key].IsEnabled = series;
        }
    }

    // ------------------------------------------------------------------ the numbers

    private double Value(string key) => (double)(_numbers[key].Value ?? 0);

    /// <summary>
    /// Whether one of the two tests is in. Always true for a feed test, which has neither: its
    /// lines are its own thing and the ladder does not apply to it at all.
    /// </summary>
    private bool Included(string key) =>
        _kind.SelectedIndex != 0 || _flags[key].IsChecked == true;

    private TestCutOptions Read() => new()
    {
        Tool = _tool.SelectedItem as Tool ?? Tool.DefaultVBit,
        Kind = _kind.SelectedIndex == 0 ? TestCutKind.Depth : TestCutKind.Feed,
        LineCount = Included("useSeries") ? (int)Value("lines") : 0,
        LineLengthMm = Value("length"),
        LineSpacingMm = Value("spacing"),
        StartDepthMm = Value("from"),
        DepthStepMm = Value("step"),
        PassesPerLine = (int)Value("passes"),
        StepoverMm = Value("stepover"),
        LadderRungs = Included("useLadder") ? (int)Value("rungs") : 0,
        DepthMm = _kind.SelectedIndex == 0 ? Value("ladderAt") : Value("depth"),
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

        // Neither half in. Everything below would divide by an empty coupon, and there is nothing
        // to say about it except which switch to put back on.
        if (report.Lines.Count == 0)
        {
            _effect.Text = "Nothing to cut. Switch on the width ladder, the depth series, or both.";
            _write.IsEnabled = false;

            return;
        }

        // The two halves are described separately, because either may be missing and each one's
        // figures are meaningless applied to the other. The repeat is excluded from the rungs: it
        // is a copy of the bottom one, and counting it made a five-rung ladder claim six and report
        // its stepover range as running from the bottom rung to the bottom rung.
        var series = report.Lines.Where(l => !l.IsLadder).ToList();
        var rungs = report.Lines.Where(l => l.IsLadder && !l.IsRepeat).ToList();

        var text = options.Kind != TestCutKind.Depth
            ? Invariant(
                $"{report.Lines.Count} lines, {report.Lines.Min(l => l.FeedMmPerMin):F0} to {report.Lines.Max(l => l.FeedMmPerMin):F0} mm/min.\n")
            : series.Count > 0
                ? Invariant(
                    $"{series.Count} lines, {series.Min(l => l.DepthMm):F3} to {series.Max(l => l.DepthMm):F3} mm deep")
                    + (rungs.Count > 0 ? Invariant($", plus {rungs.Count} ladder rungs.\n") : ".\n")
                : Invariant($"{rungs.Count} ladder rungs, all {rungs[0].DepthMm:F3} mm deep.\n");

        text += Invariant($"Needs {report.StockWidthMm:F0} × {report.StockHeightMm:F0} mm of bare copper, ")
            + Invariant($"about {Math.Max(1, Math.Round(report.EstimatedSeconds)):F0} seconds.");

        if (rungs.Count > 1)
        {
            text += Invariant(
                $"\n\nThe ladder steps {rungs[0].StepoverMm:F3} to {rungs[^1].StepoverMm:F3} mm. The first rung with copper left standing in it is what this bit really cuts — that is the tip width, read by eye.");
        }

        // What the operator will actually put a caliper on, and the number they take off it. The
        // stepover is normally left at zero, meaning "work one out from the shallowest line", so it
        // has to be said here rather than left as a zero sitting in a box.
        if (series.Count > 0 && series[0].PassCount > 1)
        {
            var band = series[0];

            text += Invariant(
                $"\n\nEach series line is {band.PassCount} passes stepping {band.StepoverMm:F3} mm: measure a band of about {band.BandWidthMm:F2} mm and subtract {band.SteppedMm:F3} mm. One pass alone would be {band.PredictedWidthMm:F3} mm, which no caliper can read.");
        }

        text += Levelling switch
        {
            TestCutLevelling.WriteProbe =>
                "\n\nWrites the probing routine only. Run it, keep your sender's log, then come "
                + "back here and choose \u201cLevel to a probe log\u201d.",
            TestCutLevelling.FromLog when _logPath is null =>
                "\n\nChoose the probe log for this coupon.",
            TestCutLevelling.FromLog =>
                "\n\nEvery Z will follow the surface in " + Path.GetFileName(_logPath) + ".",
            _ => string.Empty,
        };

        if (_reopened is { } note)
        {
            text += "\n\n" + note;
        }

        foreach (var warning in report.Warnings)
        {
            text += "\n\n" + warning;
        }

        _effect.Text = text;

        // Nothing to write until there is a log to write against, and nothing to write at all
        // without one of the two tests.
        _write.IsEnabled = Levelling != TestCutLevelling.FromLog || _logPath is not null;
    }
}
