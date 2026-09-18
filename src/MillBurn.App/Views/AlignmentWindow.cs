using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MillBurn.Align;
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
/// One hole gives a shift, which is right as long as the board sits square to the machine. When it
/// does not — a jig with a little play in it, a board re-clamped between steps — a second hole gives
/// the rotation as well: measure both, and the line between them says how far the board is turned.
/// </summary>
public sealed class AlignmentWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly List<ExportItem> _files;
    private List<AlignmentTarget> _targets = [];

    private readonly ComboBox _file = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _hole = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _hole2 = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    // Where the hole really is, as the machine reads it — not how far that is from where the program
    // puts it. Jog the bit until it sits dead centre, read the two numbers off the sender, type them
    // in. Nobody has to subtract anything, and the correction is worked out from the pair.
    private readonly NumericUpDown _x = Number(-2000, 2000, 0.01, "F3");
    private readonly NumericUpDown _y = Number(-2000, 2000, 0.01, "F3");
    private readonly NumericUpDown _x2 = Number(-2000, 2000, 0.01, "F3");
    private readonly NumericUpDown _y2 = Number(-2000, 2000, 0.01, "F3");
    private readonly NumericUpDown _hover = Number(0.02, 5, 0.05, "F2");

    private readonly CheckBox _useSecond = new()
    {
        Content = "Measure a second hole as well, to correct rotation",
        FontSize = 12,
    };

    private readonly StackPanel _rotation = new() { Spacing = 0, IsVisible = false };
    private readonly TextBlock _fit = Caption();

    /// <summary>The correction the typed position comes to, so the number is still visible.</summary>
    private readonly TextBlock _correction = Caption();

    // Named "First hole" only once there is a second one to tell it from.
    private readonly TextBlock _holeLabel = new()
    {
        Text = "Hole",
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 12,
    };

    private readonly CheckBox _waste = new()
    {
        Content = "Use the stock's two waste holes instead of holes in the board",
        FontSize = 12,
    };

    private readonly CheckBox _flipped = new()
    {
        Content = "The board is flipped over, and I am measuring from the back",
        FontSize = 12,
    };

    /// <summary>One tick per program group in the export: what gets written again, aligned.</summary>
    private readonly StackPanel _moves = new() { Spacing = 0 };

    private readonly List<(MovableProgram Program, CheckBox Box)> _movable = [];

    private readonly TextBlock _bit = Caption();
    private readonly TextBlock _where = Caption();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    private readonly Button _test = new() { Content = "Write test", Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _test2 = new() { Content = "Write test", Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _write = new() { Content = "Write aligned files" };

    private readonly ExportItem? _stock;
    private readonly List<Point2> _wasteHoles = [];
    private readonly long _frameWidthNm;
    private string _folder;
    private string _last = string.Empty;

    /// <summary>True while the boxes are being filled in code, so that does not read as a measurement.</summary>
    private bool _filling;

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

        // The boxes are filled from the chosen hole once it is known — see Fill.
        _useSecond.IsChecked = vm.AlignmentUseSecond;
        _rotation.IsVisible = vm.AlignmentUseSecond;
        _hover.Value = (decimal)vm.Settings.Align.HoverMm;

        _stock = vm.StockProgram();
        _wasteHoles = [.. vm.WasteHoles()];
        _frameWidthNm = vm.FrameWidthNm();

        _waste.IsEnabled = _stock is not null && _wasteHoles.Count > 0;
        _waste.IsChecked = _waste.IsEnabled && vm.AlignmentWasteHoles;

        if (!_waste.IsEnabled)
        {
            _waste.Content = "Use the stock's waste holes (this job cuts none — Project info ▸ Alignment holes in the waste)";
        }

        _flipped.IsChecked = vm.AlignmentFlipped;

        BuildMoves(vm);

        _file.ItemsSource = _files.Select(f => f.TargetName).ToList();

        Content = Build();

        _file.SelectionChanged += (_, _) => ShowHoles();
        _hole.SelectionChanged += (_, _) => { Fill(); Refresh(); };
        _hole2.SelectionChanged += (_, _) => { Fill(); Refresh(); };
        _x.ValueChanged += (_, _) => Remember();
        _y.ValueChanged += (_, _) => Remember();
        _x2.ValueChanged += (_, _) => Remember();
        _y2.ValueChanged += (_, _) => Remember();

        _useSecond.IsCheckedChanged += (_, _) =>
        {
            _vm.AlignmentUseSecond = _useSecond.IsChecked == true;
            _rotation.IsVisible = _vm.AlignmentUseSecond;
            Refresh();
        };

        _waste.IsCheckedChanged += (_, _) =>
        {
            _vm.AlignmentWasteHoles = _waste.IsChecked == true;
            ShowHoles();
        };

        _flipped.IsCheckedChanged += (_, _) =>
        {
            _vm.AlignmentFlipped = _flipped.IsChecked == true;
            ShowHoles();
        };

        _hover.ValueChanged += (_, _) =>
        {
            _vm.SaveAlignHover((double)(_hover.Value ?? 0.1m));
            Refresh();
        };

        _file.SelectedIndex = _files.Count > 0 ? 0 : -1;
        ShowHoles();
    }

    /// <summary>The program the holes are read from: the stock's, in waste-hole mode, else the chosen file.</summary>
    private ExportItem? Selected => Waste
        ? _stock
        : _file.SelectedIndex >= 0 && _file.SelectedIndex < _files.Count ? _files[_file.SelectedIndex] : null;

    private bool Waste => _waste.IsChecked == true && _stock is not null && _wasteHoles.Count > 0;

    private bool Flipped => _flipped.IsChecked == true;

    /// <summary>
    /// Where a hole is now, as against where the program puts it.
    ///
    /// The same place, until the board is turned over. Flipped left-to-right about the stock's
    /// vertical centreline — the axis the app mirrors a bottom-side program about, and the only axis
    /// that puts the stock back in the same corner — a hole at X is at the stock's width minus X. Y
    /// does not move. So the two waste holes, cut once, can be hovered over from either side.
    /// </summary>
    private Point2 Known(AlignmentTarget target) => Flipped && _frameWidthNm > 0
        ? new Point2(_frameWidthNm - target.At.X, target.At.Y)
        : target.At;

    /// <summary>The target as the machine will be sent to it, which is what the test program wants.</summary>
    private AlignmentTarget AsMeasured(AlignmentTarget target) => target with { At = Known(target) };

    private AlignmentTarget? Target => At(_hole);

    private AlignmentTarget? Target2 => At(_hole2);

    /// <summary>What was typed for the first hole: where it really is.</summary>
    private Point2 Measured => new(
        Nm.FromMillimetres((double)(_x.Value ?? 0)),
        Nm.FromMillimetres((double)(_y.Value ?? 0)));

    private Point2 Measured2 => new(
        Nm.FromMillimetres((double)(_x2.Value ?? 0)),
        Nm.FromMillimetres((double)(_y2.Value ?? 0)));

    /// <summary>How far that is from where the program puts it: the correction, derived.</summary>
    private Point2 Offset => Target is { } target ? Measured - Known(target) : Point2.Origin;

    private Point2 Offset2 => Target2 is { } target ? Measured2 - Known(target) : Point2.Origin;

    private bool Rotating => _useSecond.IsChecked == true;

    private AlignmentTarget? At(ComboBox box) =>
        box.SelectedIndex >= 0 && box.SelectedIndex < _targets.Count ? _targets[box.SelectedIndex] : null;

    /// <summary>
    /// What the two measurements say, or null when only one hole is being used.
    ///
    /// Both offsets are read from where the program puts each hole, not from each other: the operator
    /// runs each test, jogs to the centre, and reads that hole's own numbers off the machine. Nothing
    /// carries over between them, so a mistake at one hole cannot quietly bias the other.
    /// </summary>
    private RigidFitResult? Fit()
    {
        if (!Rotating || Target is not { } first || Target2 is not { } second)
        {
            return null;
        }

        if (first.Number == second.Number)
        {
            return new RigidFitResult(
                0, default, Known(first), 0,
                "Both rows are the same hole. Pick a second hole at the other end of the board.");
        }

        // Against where each hole is *now* — mirrored, on a flipped board — because that is the frame
        // the operator read their numbers in, and the frame the programs being moved are written in.
        return RigidFit.Solve(
            Known(first), Known(first) + Offset, Known(second), Known(second) + Offset2);
    }

    /// <summary>What the ticks in the list add up to.</summary>
    private System.Collections.Immutable.ImmutableArray<string> Chosen() =>
        [.. _movable.Where(m => m.Box.IsChecked == true).Select(m => m.Program.Key)];

    /// <summary>The correction to write with, or null when nothing would be written.</summary>
    private DrillAlignment? Correction()
    {
        var chosen = Chosen();

        if (chosen.Length == 0)
        {
            return null;
        }

        if (Fit() is not { } fit)
        {
            return Rotating ? null : new DrillAlignment(Offset.X, Offset.Y) { Moved = chosen };
        }

        return fit.Found
            ? new DrillAlignment(fit.OffsetNm.X, fit.OffsetNm.Y)
            {
                RotationDegrees = fit.RotationDegrees,
                PivotNm = fit.PivotNm,
                Moved = chosen,
            }
            : null;
    }

    /// <summary>
    /// The list of what gets written again: one tick per program the export would write.
    ///
    /// Drilling and routing start ticked, and the board outline with them, because that is what this
    /// dialog has always moved and what the first side of a board needs. Everything else starts
    /// unticked and is there for the workflows that need it — the flipped board whose bottom copper
    /// has to land on holes already drilled, most of all.
    /// </summary>
    private void BuildMoves(MainViewModel vm)
    {
        var programs = vm.MovablePrograms();
        var saved = vm.AlignmentMoved;

        foreach (var program in programs)
        {
            var box = new CheckBox
            {
                // "Board outline · Board outline" says nothing twice.
                Content = (program.What == program.LayerLabel
                        ? program.What
                        : Invariant($"{program.What} · {program.LayerLabel}"))
                    + (program.Files > 1 ? Invariant($" ({program.Files} files)") : string.Empty)
                    + (program.Mirrored ? "  — flipped side" : string.Empty),
                FontSize = 12,
                IsChecked = saved is { } chosen
                    ? chosen.Contains(program.Key)
                    : program.MovedByDefault || (vm.AlignmentOutline && program.What == "Board outline"),
            };

            box.IsCheckedChanged += (_, _) =>
            {
                _vm.AlignmentMoved = Chosen();
                Refresh();
            };

            _movable.Add((program, box));
            _moves.Children.Add(box);
        }

        if (programs.Count == 0)
        {
            _moves.Children.Add(Secondary("This export writes no G-code programs to move.", 12));
        }
    }

    // ------------------------------------------------------------------ layout

    private StackPanel Build()
    {
        var body = new StackPanel { Spacing = 4, Margin = new Thickness(18) };

        body.Children.Add(Secondary(
            "Hover the bit over a real hole with the spindle off, jog until the tip sits dead centre, "
            + "and type the X and Y the machine shows. Then write the programs you tick again, moved to "
            + "match, so they follow what is already on the board. Drilling, routing and the outline are "
            + "ticked to start with; the copper is not, because on a first side the copper is what you "
            + "are measuring against. One hole is enough for a board sitting square to the machine; "
            + "measure a second, at the other end, and the turn is corrected as well.", 12));

        body.Children.Add(Secondary(
            "1. Fit the bit the file uses, and zero Z on your usual spot.\n"
            + "2. Write test, then open or reload the test file in your sender and run it.\n"
            + "3. Jog the tip to the middle of the hole, and type where the machine says it is.\n"
            + "4. Write the test again to check it lands there on its own.\n"
            + "5. Write the aligned files, and run those instead of the originals.", 12));

        if (_vm.SavedAlignment is { } saved)
        {
            body.Children.Add(Saved(saved));
        }

        body.Children.Add(new Border { Height = 8 });

        body.Children.Add(Row("Holes to use", _waste));
        body.Children.Add(Row(string.Empty, _flipped));
        body.Children.Add(Row("Test with", _file));
        body.Children.Add(Row(string.Empty, _bit));
        body.Children.Add(Row(_holeLabel, With(_hole, _test)));
        body.Children.Add(Row("It is really at X (mm)", _x));
        body.Children.Add(Row("It is really at Y (mm)", _y));
        body.Children.Add(Row(string.Empty, _correction));

        body.Children.Add(Row("Also measure", _useSecond));

        _rotation.Children.Add(Row("Second hole", With(_hole2, _test2)));
        _rotation.Children.Add(Row("It is really at X (mm)", _x2));
        _rotation.Children.Add(Row("It is really at Y (mm)", _y2));
        _rotation.Children.Add(Row(string.Empty, _fit));

        body.Children.Add(_rotation);

        body.Children.Add(Row("Stop above the surface (mm)", _hover));
        body.Children.Add(Row(
            new TextBlock
            {
                Text = "Write again, aligned",
                // Top, not centre: the list beside it can be a dozen rows long.
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 12,
            },
            _moves));

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

        _test.Click += (_, _) => WriteTest(Target, Offset, "first");
        _test2.Click += (_, _) => WriteTest(Target2, Offset2, "second");
        _write.Click += (_, _) => WriteAligned();

        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { close, _write },
        });

        return body;
    }

    /// <summary>A hole list with its own test button: each hole is measured on its own.</summary>
    private static Grid With(ComboBox hole, Button test)
    {
        Grid.SetColumn(test, 1);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { hole, test },
        };
    }

    /// <summary>
    /// The correction this project already carries, with the date it was found.
    ///
    /// Offered rather than applied: it is true only while the board has not moved since, and the
    /// operator is the only one who knows that. The date is there so a correction from last week is
    /// recognisably from last week.
    /// </summary>
    private Border Saved(AlignmentRecord saved)
    {
        var text = saved.RotationDegrees == 0
            ? Invariant($"Saved with this project: X{Signed(saved.XMm)} Y{Signed(saved.YMm)} mm")
            : Invariant($"Saved with this project: X{Signed(saved.XMm)} Y{Signed(saved.YMm)} mm, turned {saved.RotationDegrees:0.####}°");

        if (saved.Found is { } found)
        {
            text += Invariant($", found {found.LocalDateTime:d MMM yyyy, HH:mm}");
        }

        var use = new Button { Content = "Use it", FontSize = 11, Padding = new Thickness(8, 2) };

        use.Click += (_, _) =>
        {
            if (_vm.WriteAlignedFiles(_folder, saved.ToAlignment()) > 0)
            {
                Close();
                return;
            }

            _last = _vm.StatusMessage;
            Refresh();
        };

        Grid.SetColumn(use, 1);

        var caption = Caption();
        caption.Text = text + ". Only still true if the board has not moved since — run the test again if it has.";
        caption.VerticalAlignment = VerticalAlignment.Center;
        caption.Margin = new Thickness(0, 0, 8, 0);

        return new Border
        {
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0x9A, 0x9A, 0x9A)),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children = { caption, use },
            },
        };
    }

    private static string Signed(double mm) =>
        (mm < 0 ? "-" : "+") + Math.Abs(mm).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ behaviour

    private void ShowHoles()
    {
        var file = Selected;
        var found = file is null ? [] : AlignmentTest.Targets(file.Content);

        // In waste-hole mode, only the two holes: the stock cuts its own edges below the surface too,
        // and on square stock that perimeter reads as one more round feature. Which features are the
        // holes comes from the stock plan; where they are still comes from the program.
        _targets = Waste
            ? [.. found.Where(t => _wasteHoles.Any(h => h.DistanceTo(t.At) <= Nm.FromMillimetres(0.5)))]
            : [.. found];

        var names = _targets
            .Select(t => Invariant($"{(t.Kind == "slot" ? "Slot" : "Hole")} {t.Number}  ·  X {Mm(Known(t).X)}   Y {Mm(Known(t).Y)}"))
            .ToList();

        _hole.ItemsSource = names;
        _hole2.ItemsSource = names.ToList();

        _hole.SelectedIndex = _targets.Count > 0 ? 0 : -1;

        // The far hole, not the next one: the two want to be as far apart as the board allows, and the
        // last one the program reaches is usually at the other end of it.
        _hole2.SelectedIndex = _targets.Count > 1 ? Furthest(_targets[0]) : -1;

        // The file picker is for board holes; the waste holes are only ever in the stock's program.
        _file.IsEnabled = !Waste;

        _bit.Text = file is null
            ? string.Empty
            : Waste
                ? Invariant($"Read from {file.TargetName}. Fit the {BitOf(file)} — the bit that cut the holes — for this one.")
                : "Fit the " + BitOf(file) + " for this one.";

        // Two holes is exactly what the waste pair is for, so it starts on.
        if (Waste && _targets.Count > 1 && !Rotating)
        {
            _useSecond.IsChecked = true;
        }

        Fill();
        Refresh();
    }

    /// <summary>Which target is furthest from the given one, by index.</summary>
    private int Furthest(AlignmentTarget from)
    {
        var best = 0;
        var far = -1d;

        for (var i = 0; i < _targets.Count; i++)
        {
            var distance = from.At.DistanceTo(_targets[i].At);

            if (distance > far)
            {
                (far, best) = (distance, i);
            }
        }

        return best;
    }

    /// <summary>
    /// Puts the hole's own coordinates in the boxes, plus whatever correction is already in hand.
    ///
    /// So the boxes always start by saying where the test is about to send the bit, and the operator
    /// changes them to where it should have gone. Switching holes, turning the board over or moving to
    /// the waste holes all re-fill them, because each is a different place on the table.
    /// </summary>
    private void Fill()
    {
        _filling = true;

        try
        {
            if (Target is { } first)
            {
                var at = Known(first) + new Point2(
                    Nm.FromMillimetres(_vm.AlignmentXMm), Nm.FromMillimetres(_vm.AlignmentYMm));

                _x.Value = (decimal)Nm.ToMillimetres(at.X);
                _y.Value = (decimal)Nm.ToMillimetres(at.Y);
            }

            if (Target2 is { } second)
            {
                var at = Known(second) + new Point2(
                    Nm.FromMillimetres(_vm.AlignmentSecondXMm), Nm.FromMillimetres(_vm.AlignmentSecondYMm));

                _x2.Value = (decimal)Nm.ToMillimetres(at.X);
                _y2.Value = (decimal)Nm.ToMillimetres(at.Y);
            }
        }
        finally
        {
            _filling = false;
        }
    }

    private void Remember()
    {
        // Filling the boxes is not the operator typing in them.
        if (_filling)
        {
            return;
        }

        var offset = Offset;
        var offset2 = Offset2;

        _vm.AlignmentXMm = Nm.ToMillimetres(offset.X);
        _vm.AlignmentYMm = Nm.ToMillimetres(offset.Y);
        _vm.AlignmentSecondXMm = Nm.ToMillimetres(offset2.X);
        _vm.AlignmentSecondYMm = Nm.ToMillimetres(offset2.Y);
        Refresh();
    }

    private void Refresh()
    {
        _where.Text = _folder;
        ToolTip.SetTip(_where, _folder);
        _test.IsEnabled = Target is not null;
        _test2.IsEnabled = Target2 is not null;
        _write.IsEnabled = _movable.Count > 0 && Correction() is not null;
        _useSecond.IsEnabled = _targets.Count > 1;
        _holeLabel.Text = Rotating ? "First hole" : "Hole";

        // The correction is still shown, because its size is the thing worth a second look: a few
        // hundredths is an alignment, half a millimetre is a hole read wrongly or the wrong hole.
        _correction.Text = Target is { } picked
            ? Invariant(
                $"The program puts it at X {Mm(Known(picked).X)}  Y {Mm(Known(picked).Y)} — so this moves everything X{AlignmentTest.FormatOffset(Offset.X)} Y{AlignmentTest.FormatOffset(Offset.Y)} mm.")
            : string.Empty;

        DescribeFit();

        if (_movable.Count == 0)
        {
            _status.Text = "This job writes no G-code programs to align. Set a layer to G-code first.";
            return;
        }

        if (Target is not { } target)
        {
            _status.Text = Waste
                ? "The stock's program has no alignment holes in it. Tick Alignment holes in the waste under Project info, and cut the stock again."
                : "No holes found in this file.";

            return;
        }

        var at = Known(target) + Offset;
        var hover = (double)(_hover.Value ?? 0.1m);

        var plan = Invariant(
            $"The test moves to X {Mm(at.X)}  Y {Mm(at.Y)} and stops {hover:0.00} mm above the surface, spindle off.");

        if (SideWarning() is { } warning)
        {
            plan += "\n" + warning;
        }

        _status.Text = _last.Length == 0 ? plan : plan + "\n" + _last;
    }

    /// <summary>
    /// Says so when the correction is being measured on one side of the board and written into
    /// programs for the other.
    ///
    /// Not a refusal — drilling from the back of a flipped board is a real thing to want, and so is
    /// leaving the outline for a later setup. But a correction measured with the board turned over
    /// describes the board turned over, and writing it into a program for the other side without
    /// saying so moves that program the wrong way across the stock.
    /// </summary>
    private string? SideWarning()
    {
        var chosen = _movable.Where(m => m.Box.IsChecked == true).Select(m => m.Program).ToList();

        if (chosen.Count == 0)
        {
            return "Nothing is ticked under “Write again, aligned”, so there is nothing to write.";
        }

        var wrongSide = chosen.Where(p => p.Mirrored != Flipped).ToList();

        if (wrongSide.Count == 0)
        {
            return null;
        }

        var names = string.Join(", ", wrongSide.Select(p => p.What + " · " + p.LayerLabel));
        var isAre = wrongSide.Count == 1 ? "is" : "are";

        return Flipped
            ? $"Measured on the flipped board, but {names} {isAre} written for the board the right way up. Tick those only if you meant to."
            : $"{names} {isAre} written for the flipped board, and this correction was measured the right way up. Tick those only if you meant to.";
    }

    /// <summary>
    /// What the two measurements came to, under the second hole: the turn, and the check that comes
    /// free with it — two holes a known distance apart, measured.
    /// </summary>
    private void DescribeFit()
    {
        if (Fit() is not { } fit)
        {
            _fit.Text = _targets.Count > 1
                ? "Measure the second hole the same way: run its test, jog to the centre, and type what the machine says."
                : "This file has only one hole, so there is nothing to measure a turn against.";

            return;
        }

        if (!fit.Found)
        {
            _fit.Text = fit.Refusal;
            return;
        }

        var turn = fit.RotationDegrees == 0
            ? "square to the machine"
            : Invariant($"turned {fit.RotationDegrees:0.####}° {(fit.RotationDegrees > 0 ? "anticlockwise" : "clockwise")}");

        var check = fit.SeparationErrorNm == 0
            ? "the two holes measured exactly their known distance apart"
            : Invariant($"the two holes measured {AlignmentTest.FormatOffset(fit.SeparationErrorNm)} mm against their known distance apart");

        _fit.Text = Invariant($"The board is {turn}, and {check}. Every hole is moved to match.");
    }

    /// <summary>
    /// The test for one of the two holes.
    ///
    /// Separate files, named for which hole they are: with two tests in play, one file overwritten by
    /// the other is a test run against the wrong hole, and the reading it produces is wrong in a way
    /// that looks perfectly reasonable.
    /// </summary>
    private void WriteTest(AlignmentTarget? target, Point2 offset, string which)
    {
        if (Selected is not { } file || target is null)
        {
            return;
        }

        var suffix = Rotating ? Invariant($".align-test-{which}.nc") : ".align-test.nc";
        var path = Path.Combine(_folder, SafeName(_vm.Project.DisplayName) + suffix);

        _last = _vm.WriteAlignmentTest(path, file, AsMeasured(target), offset)
            ? Invariant($"Wrote {Path.GetFileName(path)}. Open or reload it in your sender and run it.")
            : _vm.StatusMessage;

        Refresh();
    }

    private void WriteAligned()
    {
        if (Correction() is not { } correction)
        {
            _last = Fit()?.Refusal ?? "Pick two different holes before writing the files.";
            Refresh();
            return;
        }

        if (_vm.WriteAlignedFiles(_folder, correction) > 0)
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

            // Opening at the folder already chosen, rather than wherever the picker was last, so
            // picking the one beside it is two clicks instead of a walk down the tree.
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_folder),
        });

        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            _folder = path;

            // Kept for next time. Chosen here rather than when the files are written, because the
            // choice is the operator's answer to "where do these go" either way.
            _vm.SaveAlignFolder(path);
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

            // The stock program's own line: "( Fit the 1.0 mm end mill: the Board outline layer's bit… )".
            const string fit = "( Fit the ";

            if (line.StartsWith(fit, StringComparison.Ordinal))
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);

                return (colon > 0 ? line[fit.Length..colon] : line[fit.Length..].TrimEnd(')', ' ', '.')).Trim();
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

    private static Grid Row(string label, Control editor) => Row(
        new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
        editor);

    private static Grid Row(Control label, Control editor)
    {
        Grid.SetColumn(editor, 1);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            Margin = new Thickness(0, 3),
            Children = { label, editor },
        };
    }
}
