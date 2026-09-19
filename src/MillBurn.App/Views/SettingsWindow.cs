using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using MillBurn.Core;

namespace MillBurn.App.Views;

/// <summary>What the settings dialog was asked to save.</summary>
/// <param name="Machine">How the machine moves and how its files are written.</param>
/// <param name="DryRun">What a dry run does.</param>
/// <param name="Probe">The probing grid.</param>
/// <param name="Level">How closely a levelled program follows the surface.</param>
/// <param name="Import">What each kind of layer becomes when a folder is imported.</param>
/// <param name="Milling">What a freshly imported layer starts out cutting.</param>
public sealed record SettingsChoice(
    MachineSettings Machine, DryRunSettings DryRun, ProbeSettings Probe, LevelSettings Level,
    ImportDefaults Import, MillingDefaults Milling);

/// <summary>
/// The numbers that describe the machine rather than the board.
///
/// Everything here was a constant in the source until now, which meant the most consequential of
/// them — the safe height every travel move crosses the board at — could only be changed by
/// recompiling. A clamp taller than it is struck at rapid.
///
/// Checked live, and Save is refused while anything is inconsistent: an approach height above the
/// safe height, or a dry run held lower than the job's own travel, are not preferences.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly Dictionary<string, NumericUpDown> _numbers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _flags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComboBox> _outputs = new(StringComparer.Ordinal);

    /// <summary>What a short last lap becomes, in <see cref="ShortLastLap"/> order.</summary>
    private readonly ComboBox _shortLap = new()
    {
        ItemsSource = new[]
        {
            "Keep it as its own lap",
            "Spread the depth evenly",
            "Fold it into the lap before",
        },
        Width = 230,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly StackPanel _problems = new() { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };

    private TabControl? _tabs;

    /// <summary>Each tab's header, by name, so a tab with a problem on it can say so.</summary>
    private readonly Dictionary<string, TextBlock> _tabHeaders = new(StringComparer.Ordinal);

    /// <summary>Which tab each section of the settings is on.</summary>
    private readonly Dictionary<SettingsSection, string> _tabOf = [];
    private readonly Button _save;

    public SettingsChoice? Result { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Title = "Settings";
        AppIcon.Apply(this);
        Width = 640;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PageBackground");

        _save = new Button { Content = "Save", IsDefault = true };

        Content = BuildBody(settings);
        Recheck();
    }

    private Grid BuildBody(AppSettings settings)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            Margin = new Thickness(18),
        };

        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(new TextBlock
        {
            Text = "Settings",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
        });
        heading.Children.Add(Muted(
            "How this machine behaves, and what a freshly imported board starts out doing. Both "
            + "stay put when you open a different project.", 12));

        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        // Tabs, because one long page had grown to six sections and a scroll. Each tab scrolls on its
        // own; the problems and the buttons stay underneath all of them, because Save is refused
        // while anything is wrong and a problem on a tab nobody is looking at must still be seen.
        var tabs = new TabControl { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0) };
        _tabs = tabs;

        var body = Tab(tabs, "Machine", SettingsSection.Machine);
        Section(body, "Heights");
        Number(body, "safeZ", "Safe height", "mm", settings.Machine.SafeZMm, 0.5, 50, 0.5,
            "Every travel move in every program crosses the board at this height. Raise it above "
            + "anything that stands proud of the stock — clamps, tape, a probe clip.");
        Number(body, "approachZ", "Approach height", "mm", settings.Machine.ApproachZMm, 0.05, 10, 0.05,
            "Where a rapid descent stops and the plunge feed takes over. Must be below the safe height.");

        Section(body, "Speed and acceleration");
        body.Children.Add(Muted(
            "These four decide how long a job is estimated to take. The controller knows all of "
            + "them, so reading them from it beats typing them in.", 11, new Thickness(0, 0, 0, 8)));
        body.Children.Add(FromDump());
        Number(body, "rapid", "Rapid rate", "mm/min", settings.Machine.RapidMmPerMin, 100, 20000, 100,
            "Used for the time estimates, and for a dry run with the programmed feeds turned off. "
            + "Never written into a file: G0 carries no feed word.");
        Number(body, "zrapid", "Z rapid rate", "mm/min", settings.Machine.ZRapidMmPerMin, 20, 20000, 50,
            "GRBL's $112. Usually far slower than X and Y — a leadscrew against gravity rather than "
            + "a belt — and a PCB job is mostly plunging and retracting, so this decides a large "
            + "part of how long one takes.");
        Number(body, "accel", "Acceleration", "mm/s²", settings.Machine.AccelerationMmPerSecondSquared,
            1, 5000, 10,
            "GRBL's $120, and the number that decides how long a program takes more than any feed "
            + "rate does. At 20 mm/s² a move must run 56 mm before it ever reaches 2000 mm/min, and "
            + "isolation moves are a millimetre. It also tells the travel optimizer how much a short "
            + "move really costs.");
        Number(body, "junction", "Junction deviation", "mm", settings.Machine.JunctionDeviationMm,
            0.001, 1, 0.005,
            "GRBL's $11. How far the controller may cut a corner to carry speed through it, which is "
            + "what puts a real machine between “stops at every vertex” and “never "
            + "slows down”.");

        Section(body, "What goes into the file");
        Number(body, "decimals", "Coordinate decimals", "", settings.Machine.Decimals, 2, 5, 1,
            "Three is one micron, which is past every machine this targets.");
        Flag(body, "canned", "Emit canned drilling cycles (G81/G83)", settings.Machine.CannedCycles,
            "GRBL does not implement these and ignores what it cannot parse, so a drill file would "
            + "travel the whole pattern without drilling anything. LinuxCNC and Mach3 do support them.");

        body = Tab(tabs, "Milling", SettingsSection.Milling);
        Section(body, "Isolation");
        Number(body, "isolation", "Isolation width", "mm", settings.Milling.IsolationWidthMm, 0, 5, 0.05,
            "How wide a gap to clear either side of every trace, on a layer that has just been "
            + "imported. One lap of a 30° V-bit at 0.05 mm deep is 0.127 mm — enough to separate "
            + "the nets and too narrow to see, solder across, or survive handling. Each layer can "
            + "be changed afterwards; this is only where it starts. Zero means one lap.");

        Section(body, "Routed holes and slots");
        body.Children.Add(Muted(
            "The depth of each lap is the cutter's stepdown, set in the tool library, and the "
            + "distance through the board is the drill layer's break-through. These two decide what "
            + "happens around them.", 11, new Thickness(0, 0, 0, 8)));
        Choice(body, _shortLap, "Short last lap", (int)settings.Machine.ShortLastLap,
            "When the depth is not a whole number of laps — 1.10 mm in 0.50 mm steps is 0.50 + 0.50 "
            + "+ 0.10. Spread evenly: three laps of 0.37, none deeper than the stepdown. Fold in: "
            + "0.50 + 0.60, one lap fewer and that lap a little over the stepdown, only when the "
            + "short one is under a quarter of a step. The stock's alignment holes are pecked the same way.");
        Flag(body, "finishingLap", "Finish through cuts with a flat lap", settings.Machine.FinishingLapOnThroughCuts,
            "A flat lap at full depth takes the slope out of the floor the last ramp left. On a "
            + "through cut whose last ramp starts below the board there is no floor, so it is left "
            + "out; tick this to cut it anyway.");

        body = Tab(tabs, "Dry run", SettingsSection.DryRun);
        Section(body, "The job, with nothing cut");
        body.Children.Add(Muted(
            "A dry run is a copy of the program held clear of the board: the same moves in the same "
            + "order, to watch before anything touches copper.", 11, new Thickness(0, 0, 0, 8)));
        Number(body, "dryHeight", "Held at", "mm", settings.DryRun.HeightMm, 1, 50, 0.5,
            "How far above work zero the tool is held. High enough to see daylight under it from "
            + "across the workshop, which is the point.");
        Flag(body, "keepFeeds", "Keep the programmed feeds", settings.DryRun.KeepFeeds,
            "So the dry run takes as long as the real job. Turning it off runs everything at the "
            + "rapid rate: quicker to watch, and it no longer tells you the time.");

        body = Tab(tabs, "Probing & levelling", SettingsSection.Probing, SettingsSection.Levelling);
        Section(body, "Probing");
        Number(body, "spacing", "Grid spacing", "mm", settings.Probe.SpacingMm, 1, 100, 1,
            "The bow of a clamped board is a long smooth shape, so 10 mm predicts the surface "
            + "between the points very well. Halving this quadruples the probing time.");
        Number(body, "probeFeed", "Probe feed", "mm/min", settings.Probe.FeedMmPerMin, 5, 500, 5,
            "Slow. The accuracy of the whole map rests on it.");
        Number(body, "probeDepth", "Search depth", "mm", settings.Probe.MaxDepthMm, 0.5, 20, 0.5,
            "How far below work zero a touch may search before giving up.");
        Number(body, "probeMargin", "Edge margin", "mm", settings.Probe.MarginMm, 0, 20, 0.5,
            "How far inside the board to keep the touches. A probe half over the edge reads the table.");
        Number(body, "maxPoints", "Most touches", "", settings.Probe.MaxPoints, 4, 2000, 20,
            "The spacing opens up rather than the grid being cropped. Roughly four seconds each.");

        Section(body, "Levelling");
        Number(body, "segment", "Segment length", "mm", settings.Level.SegmentMm, 0.1, 20, 0.1,
            "The longest a cutting move may be before it is broken up to follow the surface.");
        Number(body, "below", "Break up below", "mm", settings.Level.SubdivideBelowMm, 0, 10, 0.1,
            "Moves at or below this height get broken up; higher ones only have their ends corrected.");
        Number(body, "outside", "Refuse past", "mm", settings.Level.MaxOutsideMm, 0, 50, 0.5,
            "How far outside the probed area a job may stray before levelling is refused. Past the "
            + "measurements the map holds its edge value, which is a guess further out.");
        Number(body, "smoothing", "Smoothing", "", settings.Level.Smoothing, 0, 1, 0.05,
            "0 passes through every probe point; 1 is barely more than a plane. A little helps, "
            + "because a probe repeats to a few microns and a surface forced through that noise "
            + "ripples in a way the board does not.");

        body = Tab(tabs, "New boards");
        Section(body, "When a folder is imported");
        body.Children.Add(Muted(
            "What each kind of layer becomes before you change anything. Layers with nothing in "
            + "them are skipped either way, so turning a family on costs nothing on a board that "
            + "does not use it.", 11, new Thickness(0, 0, 0, 8)));

        Output(body, "copper", "Copper", settings.Import.Copper,
            "Both sides. Mill routes the isolation; SVG gives you the artwork to burn a resist with.");
        Output(body, "drills", "Drill files", settings.Import.Drills, null);
        Output(body, "outline", "Board outline", settings.Import.Outline, null);
        Output(body, "mask", "Soldermask", settings.Import.Mask,
            "Mill clears the cured mask off the pads — the operation that most needs a height map. "
            + "SVG burns the openings.");
        Output(body, "silk", "Silkscreen", settings.Import.Silk, null);
        Output(body, "paste", "Solder paste", settings.Import.Paste, null);

        body.Children.Add(Muted(
            "Inner copper is never exported: a cutter cannot reach a layer inside the board. It is "
            + "still read, drawn and listed.", 11, new Thickness(0, 4, 0, 6)));

        var presets = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 2, 0, 10),
        };

        foreach (var (name, preset) in ((string, ImportDefaults)[])
            [("Milling", ImportDefaults.Milling),
             ("Laser etching", ImportDefaults.LaserEtching),
             ("Nothing", ImportDefaults.Nothing)])
        {
            var button = new Button { Content = name, FontSize = 11 };
            button.Click += (_, _) => ApplyPreset(preset);
            presets.Children.Add(button);
        }

        body.Children.Add(presets);

        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        Grid.SetRow(_problems, 2);
        root.Children.Add(_problems);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var defaults = new Button { Content = "Restore defaults" };
        defaults.Click += (_, _) => Restore();

        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();

        _save.Click += (_, _) =>
        {
            Result = Chosen();
            Close();
        };

        buttons.Children.Add(defaults);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_save);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        return root;
    }

    // ------------------------------------------------------------------ building rows

    /// <summary>
    /// Takes the four numbers that decide a time estimate straight out of the controller.
    ///
    /// Every one of them describes a physical machine, and three of them had no way to be set at
    /// all — so they sat on defaults that were wrong by a factor of ten on the first machine they
    /// met. The controller knows all four and will say so for the asking, which makes typing them
    /// in by hand a step at which numbers go missing rather than a chore worth keeping.
    /// </summary>
    private StackPanel FromDump()
    {
        var read = new Button
        {
            Content = "Read from a $$ dump…",
            FontSize = 11,
            Padding = new Thickness(8, 3),
        };

        var said = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
            IsVisible = false,
            [!ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
        };

        read.Click += async (_, _) =>
        {
            if (await PasteWindow.AskAsync(
                    this,
                    "Paste a $$ dump",
                    "Send $$ to the controller and paste back everything it replied. Whatever is "
                    + "not in the paste is left alone, so a partial one is fine.")
                is not { } text)
            {
                return;
            }

            var dump = GrblSettings.Parse(text);

            if (dump.Rejection is { } why)
            {
                said.Text = why;
                said.IsVisible = true;

                return;
            }

            var (updated, changes) = dump.ApplyTo(Chosen().Machine);

            _numbers["rapid"].Value = (decimal)updated.RapidMmPerMin;
            _numbers["zrapid"].Value = (decimal)updated.ZRapidMmPerMin;
            _numbers["accel"].Value = (decimal)updated.AccelerationMmPerSecondSquared;
            _numbers["junction"].Value = (decimal)updated.JunctionDeviationMm;

            var lines = new List<string>(changes);
            lines.AddRange(dump.Notes());

            said.Text = lines.Count == 0
                ? $"Read {dump.Values.Count} settings. Everything this app uses already matched."
                : string.Join('\n', lines);

            said.IsVisible = true;
        };

        return new StackPanel
        {
            Margin = new Thickness(0, 2, 0, 12),
            Children = { read, said },
        };
    }

    /// <summary>Opens on the tab whose name starts with the text given, when there is one.</summary>
    public void ShowTab(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_tabs?.Items.OfType<TabItem>().FirstOrDefault(t =>
                t.Header is TextBlock h && h.Text?.StartsWith(name, StringComparison.OrdinalIgnoreCase) == true) is { } tab)
        {
            _tabs.SelectedItem = tab;
        }
    }

    /// <summary>A tab, and the panel its rows go into.</summary>
    private StackPanel Tab(TabControl tabs, string name, params SettingsSection[] sections)
    {
        var body = new StackPanel { Spacing = 2, Margin = new Thickness(2, 8, 14, 12) };

        // A TextBlock, not a string: Fluent draws a string header at a heading's size, which four
        // of these do not fit beside each other at.
        var header = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeight.SemiBold };
        _tabHeaders[name] = header;

        foreach (var section in sections)
        {
            _tabOf[section] = name;
        }

        tabs.Items.Add(new TabItem
        {
            Header = header,
            Padding = new Thickness(10, 4),
            MinHeight = 0,
            Content = new ScrollViewer { Content = body },
        });

        return body;
    }

    /// <summary>
    /// A heading within a tab: larger, in the accent colour and ruled off, so it reads as the start
    /// of a group rather than as one more label in the rows beneath it.
    /// </summary>
    private static void Section(Panel into, string title)
    {
        var rule = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 4),
            Margin = new Thickness(0, 18, 0, 8),
            Child = Token(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
            }, "InteractivePrimary"),
        };

        rule[!Border.BorderBrushProperty] = new DynamicResourceExtension("Border");
        into.Children.Add(rule);
    }

    private void Number(
        Panel into, string key, string label, string unit,
        double value, double min, double max, double step, string help)
    {
        var box = new NumericUpDown
        {
            Value = (decimal)value,
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = (decimal)step,
            FormatString = step >= 1 ? "0" : step >= 0.1 ? "0.0" : "0.00",
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        box.ValueChanged += (_, _) => Recheck();
        _numbers[key] = box;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170,Auto,*") };

        var name = new TextBlock
        {
            Text = unit.Length > 0 ? $"{label} ({unit})" : label,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(box, 1);
        row.Children.Add(name);
        row.Children.Add(box);

        into.Children.Add(row);
        into.Children.Add(Indented(help));
    }

    /// <summary>One layer family and what it becomes on import.</summary>
    private void Output(Panel into, string key, string label, OutputKind value, string? help)
    {
        var box = new ComboBox
        {
            ItemsSource = new[] { "Not exported", "SVG (laser)", "G-code (mill)" },
            SelectedIndex = value switch
            {
                OutputKind.Svg => 1,
                OutputKind.Gcode => 2,
                _ => 0,
            },
            Width = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _outputs[key] = box;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170,Auto,*") };
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(box, 1);
        row.Children.Add(name);
        row.Children.Add(box);

        into.Children.Add(row);
        into.Children.Add(help is null
            ? new TextBlock { Height = 6 }
            : Indented(help));
    }

    /// <summary>A labelled drop-down, laid out like the number rows, with its help underneath.</summary>
    private static void Choice(Panel into, ComboBox box, string label, int selected, string help)
    {
        box.SelectedIndex = selected;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170,Auto,*") };
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(box, 1);
        row.Children.Add(name);
        row.Children.Add(box);

        into.Children.Add(row);
        into.Children.Add(Indented(help));
    }

    private void ApplyPreset(ImportDefaults preset)
    {
        static int Index(OutputKind kind) => kind switch
        {
            OutputKind.Svg => 1,
            OutputKind.Gcode => 2,
            _ => 0,
        };

        _outputs["copper"].SelectedIndex = Index(preset.Copper);
        _outputs["drills"].SelectedIndex = Index(preset.Drills);
        _outputs["outline"].SelectedIndex = Index(preset.Outline);
        _outputs["mask"].SelectedIndex = Index(preset.Mask);
        _outputs["silk"].SelectedIndex = Index(preset.Silk);
        _outputs["paste"].SelectedIndex = Index(preset.Paste);
    }

    private OutputKind Chosen(string key) => _outputs[key].SelectedIndex switch
    {
        1 => OutputKind.Svg,
        2 => OutputKind.Gcode,
        _ => OutputKind.None,
    };

    private void Flag(Panel into, string key, string label, bool value, string help)
    {
        var box = new CheckBox { Content = label, IsChecked = value, MinHeight = 0 };
        box.IsCheckedChanged += (_, _) => Recheck();
        _flags[key] = box;

        into.Children.Add(box);
        into.Children.Add(Indented(help));
    }

    private static TextBlock Indented(string text) => Muted(text, 11, new Thickness(0, 1, 0, 10));

    private static TextBlock Muted(string text, double size, Thickness? margin = null) => Token(
        new TextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin ?? default,
        },
        "TextSecondary");

    // ------------------------------------------------------------------ checking

    private double Value(string key) => (double)(_numbers[key].Value ?? 0);

    private bool Flagged(string key) => _flags[key].IsChecked == true;

    private SettingsChoice Chosen() => new(
        new MachineSettings
        {
            SafeZMm = Value("safeZ"),
            ApproachZMm = Value("approachZ"),
            RapidMmPerMin = Value("rapid"),
            ZRapidMmPerMin = Value("zrapid"),
            AccelerationMmPerSecondSquared = Value("accel"),
            JunctionDeviationMm = Value("junction"),
            Decimals = (int)Value("decimals"),
            CannedCycles = Flagged("canned"),
            ShortLastLap = _shortLap.SelectedIndex switch
            {
                1 => ShortLastLap.SpreadEvenly,
                2 => ShortLastLap.FoldIn,
                _ => ShortLastLap.OwnLap,
            },
            FinishingLapOnThroughCuts = Flagged("finishingLap"),
        },
        new DryRunSettings
        {
            HeightMm = Value("dryHeight"),
            KeepFeeds = Flagged("keepFeeds"),
        },
        new ProbeSettings
        {
            SpacingMm = Value("spacing"),
            FeedMmPerMin = Value("probeFeed"),
            MaxDepthMm = Value("probeDepth"),
            MarginMm = Value("probeMargin"),
            MaxPoints = (int)Value("maxPoints"),
        },
        new LevelSettings
        {
            SegmentMm = Value("segment"),
            SubdivideBelowMm = Value("below"),
            MaxOutsideMm = Value("outside"),
            Smoothing = Value("smoothing"),
        },
        new ImportDefaults
        {
            Copper = Chosen("copper"),
            Drills = Chosen("drills"),
            Outline = Chosen("outline"),
            Mask = Chosen("mask"),
            Silk = Chosen("silk"),
            Paste = Chosen("paste"),
        },
        new MillingDefaults { IsolationWidthMm = Value("isolation") });

    /// <summary>
    /// Re-runs the checks and blocks saving on anything inconsistent.
    ///
    /// Refusing rather than clamping: silently correcting somebody's number leaves them believing
    /// the machine is set up one way while it is set up another.
    /// </summary>
    private void Recheck()
    {
        var chosen = Chosen();
        var problems = SettingsCheck.Found(
            chosen.Machine, chosen.DryRun, chosen.Probe, chosen.Level, chosen.Milling);
        var notes = SettingsCheck.Notes(chosen.Machine, chosen.Probe);

        _problems.Children.Clear();

        // Each problem says which tab it is on, and that tab's header is marked, so the number to
        // change can be found without opening every tab in turn.
        var marked = problems
            .Select(p => _tabOf.GetValueOrDefault(p.Section))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (name, header) in _tabHeaders)
        {
            header.Text = marked.Contains(name) ? name + " •" : name;
            header.ClearValue(TextBlock.ForegroundProperty);

            if (marked.Contains(name))
            {
                Token(header, "DrcViolation");
            }
        }

        foreach (var problem in problems)
        {
            var where = _tabOf.GetValueOrDefault(problem.Section);

            _problems.Children.Add(Token(
                new TextBlock
                {
                    Text = where is null ? problem.Text : where + ": " + problem.Text,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                },
                "DrcViolation"));
        }

        foreach (var note in notes)
        {
            _problems.Children.Add(Muted(note, 11));
        }

        _save.IsEnabled = problems.Count == 0;
    }

    private void Restore()
    {
        var machine = new MachineSettings();
        var dryRun = new DryRunSettings();
        var probe = new ProbeSettings();
        var level = new LevelSettings();

        _numbers["safeZ"].Value = (decimal)machine.SafeZMm;
        _numbers["approachZ"].Value = (decimal)machine.ApproachZMm;
        _numbers["rapid"].Value = (decimal)machine.RapidMmPerMin;
        _numbers["zrapid"].Value = (decimal)machine.ZRapidMmPerMin;
        _numbers["accel"].Value = (decimal)machine.AccelerationMmPerSecondSquared;
        _numbers["junction"].Value = (decimal)machine.JunctionDeviationMm;
        _numbers["decimals"].Value = machine.Decimals;
        _flags["canned"].IsChecked = machine.CannedCycles;

        _numbers["isolation"].Value = (decimal)new MillingDefaults().IsolationWidthMm;
        _shortLap.SelectedIndex = (int)machine.ShortLastLap;
        _flags["finishingLap"].IsChecked = machine.FinishingLapOnThroughCuts;

        _numbers["dryHeight"].Value = (decimal)dryRun.HeightMm;
        _flags["keepFeeds"].IsChecked = dryRun.KeepFeeds;

        _numbers["spacing"].Value = (decimal)probe.SpacingMm;
        _numbers["probeFeed"].Value = (decimal)probe.FeedMmPerMin;
        _numbers["probeDepth"].Value = (decimal)probe.MaxDepthMm;
        _numbers["probeMargin"].Value = (decimal)probe.MarginMm;
        _numbers["maxPoints"].Value = probe.MaxPoints;

        _numbers["segment"].Value = (decimal)level.SegmentMm;
        _numbers["below"].Value = (decimal)level.SubdivideBelowMm;
        _numbers["outside"].Value = (decimal)level.MaxOutsideMm;
        _numbers["smoothing"].Value = (decimal)level.Smoothing;

        ApplyPreset(new ImportDefaults());
    }

    /// <summary>
    /// Binds a colour token rather than resolving one: a dialog is built before its theme variant
    /// is applied, so resolving now would pick the light palette and keep it.
    /// </summary>
    private static T Token<T>(T control, string token)
        where T : TextBlock
    {
        control[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(token);
        return control;
    }

    /// <summary>Shows the dialog and returns what was chosen, or null if it was called off.</summary>
    public static async Task<SettingsChoice?> AskAsync(Window owner, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var window = new SettingsWindow(settings) { RequestedThemeVariant = owner.ActualThemeVariant };
        await window.ShowDialog(owner);

        return window.Result;
    }
}
