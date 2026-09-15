using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MillBurn.App.ViewModels;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using static System.FormattableString;

namespace MillBurn.App.Views;

/// <summary>
/// Job › Drill alignment: hover a bit over a real hole, nudge the origin until it sits dead centre,
/// then write every drilling and routing file again with that shift.
///
/// **It stays open while tests are written.** Finding the offset is a loop — write, run, look, adjust,
/// write again — and a dialog that closed on every write would have the operator reopening it and
/// retyping the numbers each time round. It closes when the aligned files are written, which is the end
/// of the job it exists for.
///
/// The board is assumed square to the machine, so the adjustment is an origin shift and nothing else:
/// one hole right means every hole right.
/// </summary>
public sealed class AlignmentWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly List<ExportItem> _files;
    private List<AlignmentTarget> _targets = [];

    private readonly ComboBox _file = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _hole = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly NumericUpDown _x = Number(-10, 10, 0.01, "F3");
    private readonly NumericUpDown _y = Number(-10, 10, 0.01, "F3");
    private readonly NumericUpDown _hover = Number(0.02, 5, 0.05, "F2");

    private readonly CheckBox _outline = new()
    {
        Content = "Board outline too, so it cuts round the same copper",
        FontSize = 12,
    };

    private readonly TextBlock _bit = Caption();
    private readonly TextBlock _where = Caption();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    private readonly Button _test = new() { Content = "Write test" };
    private readonly Button _write = new() { Content = "Write aligned files" };

    private readonly bool _hasOutline;
    private string _folder;
    private string _last = string.Empty;

    public AlignmentWindow(MainViewModel vm, string folder)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(folder);

        _vm = vm;
        _folder = folder;
        _files = [.. vm.DrillFiles()];

        Title = "Drill alignment";
        AppIcon.Apply(this);
        // Resizable, because the folder at the bottom can be a long path; the lists and the path widen
        // with the window. Height still fits the content until the window is dragged.
        Width = 640;
        MinWidth = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PageBackground");

        _x.Value = (decimal)vm.AlignmentXMm;
        _y.Value = (decimal)vm.AlignmentYMm;
        _hover.Value = (decimal)vm.Settings.Align.HoverMm;

        _hasOutline = vm.HasOutlineProgram();
        _outline.IsChecked = _hasOutline && vm.AlignmentOutline;
        _outline.IsEnabled = _hasOutline;

        if (!_hasOutline)
        {
            _outline.Content = "Board outline too (this job has no outline program)";
        }

        _file.ItemsSource = _files.Select(f => f.TargetName).ToList();

        Content = Build();

        _file.SelectionChanged += (_, _) => ShowHoles();
        _hole.SelectionChanged += (_, _) => Refresh();
        _x.ValueChanged += (_, _) => Remember();
        _y.ValueChanged += (_, _) => Remember();

        _outline.IsCheckedChanged += (_, _) =>
        {
            if (_hasOutline)
            {
                _vm.AlignmentOutline = _outline.IsChecked == true;
            }
        };

        _hover.ValueChanged += (_, _) =>
        {
            _vm.SaveAlignHover((double)(_hover.Value ?? 0.1m));
            Refresh();
        };

        _file.SelectedIndex = _files.Count > 0 ? 0 : -1;
        ShowHoles();
    }

    private ExportItem? Selected =>
        _file.SelectedIndex >= 0 && _file.SelectedIndex < _files.Count ? _files[_file.SelectedIndex] : null;

    private AlignmentTarget? Target =>
        _hole.SelectedIndex >= 0 && _hole.SelectedIndex < _targets.Count ? _targets[_hole.SelectedIndex] : null;

    private Point2 Offset => new(
        Nm.FromMillimetres((double)(_x.Value ?? 0)),
        Nm.FromMillimetres((double)(_y.Value ?? 0)));

    // ------------------------------------------------------------------ layout

    private StackPanel Build()
    {
        var body = new StackPanel { Spacing = 4, Margin = new Thickness(18) };

        body.Children.Add(Secondary(
            "Hover the bit over a real hole with the spindle off, and move the origin until the tip sits "
            + "dead centre. Then write the drilling, routing and outline files again, moved by the same "
            + "amount, so they follow the copper already on the board. The copper files are not moved. "
            + "The board is assumed square, so one hole right means every hole right.", 12));

        body.Children.Add(Secondary(
            "1. Fit the bit the file uses, and zero Z on your usual spot.\n"
            + "2. Write test, then open or reload the test file in your sender and run it.\n"
            + "3. If the tip is off-centre, change X or Y and write the test again.\n"
            + "4. When it sits right, write the aligned files, and run those instead of the originals.", 12));

        body.Children.Add(new Border { Height = 8 });

        body.Children.Add(Row("Test with", _file));
        body.Children.Add(Row(string.Empty, _bit));
        body.Children.Add(Row("Hole", _hole));
        body.Children.Add(Row("Move X by (mm)", _x));
        body.Children.Add(Row("Move Y by (mm)", _y));
        body.Children.Add(Row("Stop above the surface (mm)", _hover));
        body.Children.Add(Row("Also move", _outline));

        var change = new Button { Content = "Change…", FontSize = 11, Padding = new Thickness(8, 2) };
        change.Click += async (_, _) => await ChooseFolderAsync();

        // One line, trimmed from the middle of the path so the folder's own name — the end — stays in
        // view; widen the window to see more, or hover for all of it.
        _where.VerticalAlignment = VerticalAlignment.Center;
        _where.TextWrapping = TextWrapping.NoWrap;
        _where.TextTrimming = TextTrimming.PathSegmentEllipsis;

        // A grid rather than a horizontal stack: a stack gives the path unlimited width, so it never
        // trims and runs off the edge instead.
        Grid.SetColumn(_where, 1);
        _where.Margin = new Thickness(8, 0, 0, 0);

        body.Children.Add(Row("Files go to", new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { change, _where },
        }));

        body.Children.Add(new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0x5A, 0xA9, 0xF5)),
            Child = _status,
        });

        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close();

        _test.Click += (_, _) => WriteTest();
        _write.Click += (_, _) => WriteAligned();

        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { close, _test, _write },
        });

        return body;
    }

    // ------------------------------------------------------------------ behaviour

    private void ShowHoles()
    {
        var file = Selected;

        _targets = file is null ? [] : [.. AlignmentTest.Targets(file.Content)];

        _hole.ItemsSource = _targets
            .Select(t => Invariant($"{(t.Kind == "slot" ? "Slot" : "Hole")} {t.Number}  ·  X {Mm(t.At.X)}   Y {Mm(t.At.Y)}"))
            .ToList();

        _hole.SelectedIndex = _targets.Count > 0 ? 0 : -1;
        _bit.Text = file is null ? string.Empty : "Fit the " + BitOf(file) + " for this one.";

        Refresh();
    }

    private void Remember()
    {
        _vm.AlignmentXMm = (double)(_x.Value ?? 0);
        _vm.AlignmentYMm = (double)(_y.Value ?? 0);
        Refresh();
    }

    private void Refresh()
    {
        _where.Text = _folder;
        ToolTip.SetTip(_where, _folder);
        _test.IsEnabled = Target is not null;
        _write.IsEnabled = _files.Count > 0;

        if (_files.Count == 0)
        {
            _status.Text = "This job has no drilling or routing files to align. Set a drill layer to G-code first.";
            return;
        }

        if (Target is not { } target)
        {
            _status.Text = "No holes found in this file.";
            return;
        }

        var at = new Point2(target.At.X + Offset.X, target.At.Y + Offset.Y);
        var hover = (double)(_hover.Value ?? 0.1m);

        var plan = Invariant(
            $"The test moves to X {Mm(at.X)}  Y {Mm(at.Y)} and stops {hover:0.00} mm above the surface, spindle off.");

        _status.Text = _last.Length == 0 ? plan : plan + "\n" + _last;
    }

    private void WriteTest()
    {
        if (Selected is not { } file || Target is not { } target)
        {
            return;
        }

        var path = Path.Combine(_folder, SafeName(_vm.Project.DisplayName) + ".align-test.nc");

        _last = _vm.WriteAlignmentTest(path, file, target, Offset)
            ? Invariant($"Wrote {Path.GetFileName(path)}. Open or reload it in your sender and run it.")
            : _vm.StatusMessage;

        Refresh();
    }

    private void WriteAligned()
    {
        if (_vm.WriteAlignedFiles(_folder, new DrillAlignment(Offset.X, Offset.Y, _outline.IsChecked == true)) > 0)
        {
            Close();
            return;
        }

        _last = _vm.StatusMessage;
        Refresh();
    }

    private async Task ChooseFolderAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Where the alignment files go",
            AllowMultiple = false,
        });

        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            _folder = path;
            Refresh();
        }
    }

    /// <summary>The bit a file uses, as its own header or labels name it.</summary>
    private static string BitOf(ExportItem file)
    {
        if (file.Bit is { } bit && bit.IndexOf('·', StringComparison.Ordinal) is var dot and >= 0)
        {
            return bit[(dot + 1)..].Trim();
        }

        foreach (var line in file.Content.Split('\n').Select(l => l.Trim()))
        {
            if (line.StartsWith("( Drill ", StringComparison.Ordinal))
            {
                var bracket = line.IndexOf('[', StringComparison.Ordinal);
                return (bracket > 0 ? line[2..bracket] : line[2..].TrimEnd(')', ' ')).Trim().ToLowerInvariant();
            }

            const string marker = ", cut with the ";
            var at = line.IndexOf(marker, StringComparison.Ordinal);

            if (at >= 0)
            {
                return line[(at + marker.Length)..].TrimEnd(')', ' ', '.').Trim();
            }
        }

        return "bit this file uses";
    }

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static string Mm(long nm) => Nm.ToMillimetreString(nm, 3);

    private static NumericUpDown Number(double min, double max, double increment, string format) => new()
    {
        Minimum = (decimal)min,
        Maximum = (decimal)max,
        Increment = (decimal)increment,
        FormatString = format,
        Width = 150,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static TextBlock Caption() => new()
    {
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        [!ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
    };

    private static TextBlock Secondary(string text, double size) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = size,
        Margin = new Thickness(0, 0, 0, 8),
        [!ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
    };

    private static Grid Row(string label, Control editor)
    {
        Grid.SetColumn(editor, 1);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            Margin = new Thickness(0, 3),
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
                editor,
            },
        };
    }
}
