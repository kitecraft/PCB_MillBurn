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
public sealed record SettingsChoice(
    MachineSettings Machine, DryRunSettings DryRun, ProbeSettings Probe, LevelSettings Level);

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
    private readonly StackPanel _problems = new() { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _save;

    public SettingsChoice? Result { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Title = "Settings";
        AppIcon.Apply(this);
        Width = 640;
        Height = 700;
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
            "These describe the machine, not the board, so they stay put when you open a different "
            + "project.", 12));

        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var body = new StackPanel { Spacing = 2, Margin = new Thickness(0, 12) };

        Section(body, "The machine");
        Number(body, "safeZ", "Safe height", "mm", settings.Machine.SafeZMm, 0.5, 50, 0.5,
            "Every travel move in every program crosses the board at this height. Raise it above "
            + "anything that stands proud of the stock — clamps, tape, a probe clip.");
        Number(body, "approachZ", "Approach height", "mm", settings.Machine.ApproachZMm, 0.05, 10, 0.05,
            "Where a rapid descent stops and the plunge feed takes over. Must be below the safe height.");
        Number(body, "rapid", "Rapid rate", "mm/min", settings.Machine.RapidMmPerMin, 100, 20000, 100,
            "Used for the time estimates, and for a dry run with the programmed feeds turned off. "
            + "Never written into a file: G0 carries no feed word.");
        Number(body, "decimals", "Coordinate decimals", "", settings.Machine.Decimals, 2, 5, 1,
            "Three is one micron, which is past every machine this targets.");
        Flag(body, "canned", "Emit canned drilling cycles (G81/G83)", settings.Machine.CannedCycles,
            "GRBL does not implement these and ignores what it cannot parse, so a drill file would "
            + "travel the whole pattern without drilling anything. LinuxCNC and Mach3 do support them.");

        Section(body, "Dry run");
        Number(body, "dryHeight", "Held at", "mm", settings.DryRun.HeightMm, 1, 50, 0.5,
            "How far above work zero the tool is held. High enough to see daylight under it from "
            + "across the workshop, which is the point.");
        Flag(body, "keepFeeds", "Keep the programmed feeds", settings.DryRun.KeepFeeds,
            "So the dry run takes as long as the real job. Turning it off runs everything at the "
            + "rapid rate: quicker to watch, and it no longer tells you the time.");

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

        var scroller = new ScrollViewer { Content = body };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);

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

    private static void Section(Panel into, string title) =>
        into.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 14, 0, 4),
        });

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
            Decimals = (int)Value("decimals"),
            CannedCycles = Flagged("canned"),
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
        });

    /// <summary>
    /// Re-runs the checks and blocks saving on anything inconsistent.
    ///
    /// Refusing rather than clamping: silently correcting somebody's number leaves them believing
    /// the machine is set up one way while it is set up another.
    /// </summary>
    private void Recheck()
    {
        var chosen = Chosen();
        var problems = SettingsCheck.Problems(chosen.Machine, chosen.DryRun, chosen.Probe, chosen.Level);
        var notes = SettingsCheck.Notes(chosen.Machine, chosen.Probe);

        _problems.Children.Clear();

        foreach (var problem in problems)
        {
            _problems.Children.Add(Token(
                new TextBlock { Text = problem, FontSize = 11, TextWrapping = TextWrapping.Wrap },
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
        _numbers["decimals"].Value = machine.Decimals;
        _flags["canned"].IsChecked = machine.CannedCycles;

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
