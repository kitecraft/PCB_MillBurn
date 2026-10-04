using System.Globalization;
using System.Text;
using MillBurn.Core;

namespace MillBurn.Pipeline;

/// <summary>What the commands need that the finished plan cannot say for itself.</summary>
public sealed record CliScriptContext
{
    /// <summary>
    /// The folder the Gerbers were read from — name only, never a path.
    ///
    /// The command is meant to be run from beside that folder, which is the only place a relative
    /// name is right. An absolute path from the machine that exported is wrong on the machine that
    /// runs it, and on this one too once the folder moves.
    /// </summary>
    public required string Source { get; init; }

    public required Board Board { get; init; }

    public required IReadOnlyDictionary<string, LayerOutputSettings> Settings { get; init; }

    /// <summary>For naming a tool that was chosen by id.</summary>
    public required ToolLibrary Library { get; init; }

    public long BoardThicknessNm { get; init; }

    public OutputKind? Only { get; init; }

    public JobOptions Job { get; init; } = new();

    /// <summary>Set only for the aligned re-export, which is a second command.</summary>
    public DrillAlignment? Alignment { get; init; }

    public SvgPlacingLayers Marks { get; init; }

    public ProgramFraming Framing { get; init; } = ProgramFraming.None;

    /// <summary>The folder the files go in, relative.</summary>
    public string Out { get; init; } = "out";

    /// <summary>
    /// Whether <see cref="Source"/> is a project rather than a folder of Gerbers, which changes
    /// what has to be said: a project carries the thickness, the job and every layer's settings, so
    /// a command that names it needs nothing else.
    /// </summary>
    public bool FromProject =>
        Source.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One command, what it produces, and anything about this export it cannot carry.
/// </summary>
/// <param name="Says">A line of prose, before the command.</param>
/// <param name="Command">The command itself, one line.</param>
public sealed record CliStep(string Says, string Command)
{
    /// <summary>
    /// What this export does that the command line cannot say — rendered as comments beside it.
    ///
    /// Named rather than dropped. A block that silently leaves a setting out is worse than no block,
    /// because somebody runs it and gets a different job with no sign that it differs.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// The CLI commands that would rebuild an export.
///
/// Asked for from the workshop, and the second of the two things asked for: not a record of the
/// command that was run — most exports come from the window, where there was none — but the commands
/// that *would* reproduce what was just written. That works either way round, and is most useful in
/// the case that matters, which is somebody who set a job up by clicking, liked it, and now wants it
/// repeatable.
///
/// **Derived from the finished plan, never from the settings.** The settings hold what was asked
/// for; the plan holds what happened. A block written from the settings would name flags the export
/// ignored — a stock that was refused, a layer whose role cannot do what it was set to — and
/// somebody would run it.
/// </summary>
public static class CliScript
{
    /// <summary>The name the CLI is installed as, beside the app.</summary>
    public const string Program = "millburn-cli";

    /// <summary>
    /// The whole script as one pasteable block: every line of prose a <c>#</c> comment, every
    /// command on its own line.
    ///
    /// **One renderer for the page and for the command line**, so that what <c>--commands</c> prints
    /// and what the project page shows cannot drift apart. The page escapes this and puts it in a
    /// <c>pre</c>; nothing else about it changes.
    ///
    /// **The comments are hard-wrapped.** A comment wrapped only by the browser pastes as one long
    /// line, and its tail runs as a command.
    /// </summary>
    public static string Text(IReadOnlyList<CliStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var text = new StringBuilder(2048);

        foreach (var step in steps)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            foreach (var line in Wrap(step.Says))
            {
                text.Append("# ").Append(line).Append('\n');
            }

            foreach (var note in step.Notes)
            {
                // Marked, because somebody skimming for the command has to see at a glance which
                // comments describe it and which say it is incomplete.
                foreach (var line in Wrap("Note: " + note))
                {
                    text.Append("# ").Append(line).Append('\n');
                }
            }

            text.Append(step.Command).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// A comment broken at spaces, so every line of it can carry its own <c>#</c>.
    ///
    /// Ninety characters because the block is monospaced in a 46rem column on the page: wider and
    /// the browser wraps it again, which is the thing the wrapping exists to prevent.
    /// </summary>
    private static IEnumerable<string> Wrap(string text)
    {
        const int Width = 90;

        var line = new StringBuilder(Width);

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > Width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    /// <summary>The commands, in the order they have to be run.</summary>
    public static IReadOnlyList<CliStep> For(ExportPlan plan, CliScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        var notes = new List<string>();
        var flags = new List<string>
        {
            Quote(context.Source),
            "-o " + Quote(context.Out),
            "--write",
        };

        // Everything a flag can express is stated, whatever the source was.
        //
        // **A project used to be left to carry its own settings**, which made the command one line
        // and made it wrong. `/code-review` found it: the window exports whatever is on screen, and
        // nothing stops that being an unsaved change, so a board whose tab count had just been
        // raised from four to six wrote a page saying `export "Board.millburn"` — a command that
        // rebuilds the *saved* four-tab job. That is precisely the fault this class exists to avoid,
        // and the one thing worse than no block is one somebody runs.
        //
        // So the flags are the authority and the project is only the input. What no flag reaches —
        // a layer's depth, its cutter — still comes out of the file, which is what the project note
        // below is about.
        if (context.BoardThicknessNm > 0)
        {
            flags.Add("--thickness " + Mm(context.BoardThicknessNm));
        }

        Stock(plan, context, flags, notes);
        Job(context, flags);
        Milling(plan, context, flags, notes);

        // The placing layers are Settings › Laser's, on whichever machine runs this: a project does
        // not carry them, so this one is stated either way.
        if (plan.Items.Any(i => i.Output == OutputKind.Svg))
        {
            flags.Add("--svg-marks " + Word(context.Marks));
        }

        if (context.Only is { } only)
        {
            flags.Add("--only " + Word(only));
        }

        flags.AddRange(Sets(context));
        Layers(plan, context, notes);
        Project(context, notes);
        Machine(context, notes);

        var export = new CliStep(
            "Rebuilds every file in this folder. Run it from beside "
                + context.Source + ", and the files land in " + context.Out + ".",
            Program + " export " + string.Join(" ", flags))
        {
            Notes = notes,
        };

        if (context.Alignment is not { } alignment)
        {
            return [export];
        }

        // The second command, and the order is the point of there being two: the numbers in it are a
        // measurement taken at the machine with the board already clamped, so it cannot be run until
        // that has happened. It writes only the programs the shift moves, under .aligned names.
        return
        [
            export,
            new CliStep(
                "Then, with the board in the machine, measure where it actually sits — Job › Drill "
                    + "alignment in the window, or the `align` command — and write the programs that "
                    + "have to follow it again, moved onto the copper that is already there.",
                Program + " export " + string.Join(" ", [.. flags, .. Aligned(alignment)])),
        ];
    }

    /// <summary>
    /// The stock, as the four borders it came out as.
    ///
    /// Written as borders whichever way it was asked for, because that is the form that reproduces
    /// the rectangle: a stated size places the board in the middle of it unless told otherwise, and
    /// the command line has no way to say otherwise. Borders say where the board is and how big the
    /// piece is in the same four numbers.
    /// </summary>
    private static void Stock(
        ExportPlan plan, CliScriptContext context, List<string> flags, List<string> notes)
    {
        if (!plan.Blank.Resolved)
        {
            return;
        }

        var board = context.Board.Bounds;
        var blank = plan.Blank;

        if (blank.HolesOnly)
        {
            // Holes-only is only honoured on stock of a stated size, so this one case has to use
            // that form — and with it the board is centred, which is the one thing it cannot say.
            flags.Add(Invariant($"--stock-size {Mm(blank.Bounds.Width)}x{Mm(blank.Bounds.Height)}"));
            flags.Add("--stock-holes-only");

            var offX = blank.LeftNm(board) - blank.RightNm(board);
            var offY = blank.BottomNm(board) - blank.TopNm(board);

            if (Math.Abs(offX) > Nm.FromMillimetres(0.005) || Math.Abs(offY) > Nm.FromMillimetres(0.005))
            {
                notes.Add(Invariant(
                    $"The board is not centred on the stock — it sits {Mm(blank.LeftNm(board))} mm from the left and {Mm(blank.BottomNm(board))} mm from the bottom. --stock-size centres it, and there is no flag for the placement: Project info holds it."));
            }

            return;
        }

        flags.Add(Invariant(
            $"--stock {Mm(blank.LeftNm(board))},{Mm(blank.BottomNm(board))},{Mm(blank.RightNm(board))},{Mm(blank.TopNm(board))}"));

        if (!blank.Cut)
        {
            flags.Add("--stock-have");
        }
        else if (blank.AlignmentHoles.Count > 0)
        {
            flags.Add("--stock-holes");
        }
    }

    private static void Job(CliScriptContext context, List<string> flags)
    {
        var job = context.Job;

        if (job.MillLargeHoles)
        {
            flags.Add("--mill-holes");
        }

        if (job.MillDrillToolId is { } id && context.Library.Tools.FirstOrDefault(t => t.Id == id) is { } tool)
        {
            flags.Add("--mill-tool " + Quote(tool.Name));
        }

        if (job.MillAboveMm > 0)
        {
            flags.Add(Invariant($"--mill-above {job.MillAboveMm:0.######}"));
        }
    }

    /// <summary>
    /// The numbers that shape the cuts: the isolation width, and the outline's tabs.
    ///
    /// Read off the layers the plan actually isolated and cut rather than off every layer that has
    /// the settings, so a board with no copper does not carry an isolation width and a board with no
    /// outline does not carry tabs.
    /// </summary>
    private static void Milling(
        ExportPlan plan, CliScriptContext context, List<string> flags, List<string> notes)
    {
        var isolated = plan.Items
            .Where(i => i.Operation == OperationKind.Isolation)
            .Select(i => Setting(context, i.LayerFileName)?.IsolationWidthNm)
            .Where(w => w is not null)
            .Select(w => w!.Value)
            .Distinct()
            .ToList();

        if (isolated.Count == 1)
        {
            flags.Add("--isolation-width " + Mm(isolated[0]));
        }
        else if (isolated.Count > 1)
        {
            notes.Add("the copper layers are isolated to different widths ("
                + string.Join(" and ", isolated.Order().Select(w => Mm(w) + " mm"))
                + "), and --isolation-width is one number for the whole board, so it is left off "
                + "and they come from the layers' own settings.");
        }

        // IsBoardOutline rather than the operation, which is the trap its own doc warns about: the
        // stock is cut as an outline and so are routed slots, and this found the slots program
        // first on the test board's project — so a board with three tabs got --tabs 4, off the
        // drill layer's untouched default. Caught by rebuilding a real project and diffing.
        var outlines = plan.Items
            .Where(ExportPlanner.IsBoardOutline)
            .Select(i => Setting(context, i.LayerFileName))
            .OfType<LayerOutputSettings>()
            .ToList();

        if (outlines.Count == 0)
        {
            return;
        }

        // All of them, and only when they agree. The three tab flags go on every outline layer, so
        // a board with two of them set differently would come back with both cut to one of the two
        // — a flag that reproduces half the export and silently changes the other half.
        Agreed("--tabs", outlines.Select(o => (long)o.TabCount).Distinct().ToList(),
            n => n.ToString(CultureInfo.InvariantCulture), "tab(s)");

        // Left off when they are null, because null is not a number here: it means the tab is sized
        // from the board, and that is what somebody who never touched them has.
        if (outlines.TrueForAll(o => o.TabWidthNm is not null))
        {
            Agreed("--tab-width", outlines.Select(o => o.TabWidthNm!.Value).Distinct().ToList(), Mm, "mm wide");
        }

        if (outlines.TrueForAll(o => o.TabHeightNm is not null))
        {
            Agreed("--tab-height", outlines.Select(o => o.TabHeightNm!.Value).Distinct().ToList(), Mm, "mm of material");
        }

        // `IsDefaultOrEmpty` as everywhere else that touches this array: a default
        // `ImmutableArray` throws on `Length`, and this runs on every export rather than only on
        // the outline path. The three sites agree on the guard; `ExportPlanner` has the note on why.
        var edited = outlines.Sum(o => o.TabEdits.IsDefaultOrEmpty ? 0 : o.TabEdits.Length);

        if (edited > 0)
        {
            notes.Add(Invariant(
                $"{edited} tab(s) were sized one at a time, in Tabs. The command line has one width and one height for all of them, so this export's tabs are not all the same as the ones above."));
        }

        void Agreed(string flag, List<long> values, Func<long, string> format, string what)
        {
            if (values.Count == 1)
            {
                flags.Add(flag + " " + format(values[0]));
            }
            else
            {
                notes.Add("the outline layers are cut with different tabs ("
                    + string.Join(" and ", values.Order().Select(v => format(v) + " " + what))
                    + "), and " + flag + " is one number for all of them, so it is left off and "
                    + "they come from the layers' own settings.");
            }
        }
    }

    /// <summary>
    /// One <c>--set</c> for every layer, whether or not it differs from the default.
    ///
    /// Every layer named, because the defaults are the running machine's: Settings › Import decides
    /// what a mask layer does by default, so a block that leaves the mask out exports whatever the
    /// *next* machine thinks a mask is. Naming all of them costs a long line and owes nothing to
    /// anybody's settings.
    /// </summary>
    private static IEnumerable<string> Sets(CliScriptContext context)
    {
        var named = context.Board.Layers
            .Where(l => context.Settings.ContainsKey(l.FileName))
            .Select(l => l.FileName)
            .ToList();

        // --set matches on a fragment, so a file name that is the tail of another file's name would
        // set both. Shortest first fixes it: whichever layer a shorter name reached, its own --set
        // comes later and wins.
        if (named.Any(a => named.Any(b => a != b && b.Contains(a, StringComparison.OrdinalIgnoreCase))))
        {
            named = [.. named.OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal)];
        }

        return named.Select(name => "--set " + Quote(name + "=" + Word(context.Settings[name])));
    }

    /// <summary>
    /// Per-layer settings with no flag behind them, named one by one.
    ///
    /// Only for layers this export actually wrote. A depth set on a layer that was never exported is
    /// not a difference between the window and the command line; it is a setting nobody used.
    /// </summary>
    private static void Layers(ExportPlan plan, CliScriptContext context, List<string> notes)
    {
        var written = plan.Items
            .Select(i => i.LayerFileName)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        // One note for all of them, not one each. Six layers with a depth on them used to produce
        // six notes carrying the same two closing sentences, which buries the command under its own
        // caveats and teaches a reader to skip them.
        var carried = new List<string>();

        foreach (var layer in context.Board.Layers.Where(l => written.Contains(l.FileName)))
        {
            if (Setting(context, layer.FileName) is not { } setting)
            {
                continue;
            }

            var differences = new List<string>();

            if (setting.DepthNm is { } depth)
            {
                differences.Add(Invariant($"cut {Mm(depth)} mm deep"));
            }

            if (setting.BreakThroughNm != Default.BreakThroughNm)
            {
                differences.Add(Invariant($"breaks {Mm(setting.BreakThroughNm)} mm through"));
            }

            if (setting.Passes != Default.Passes)
            {
                differences.Add(Invariant($"{setting.Passes} passes"));
            }

            if (setting.ToolId is { } id && context.Library.Tools.FirstOrDefault(t => t.Id == id) is { } tool)
            {
                differences.Add("cut with the " + tool.Name);
            }

            if (!setting.WriteDrillGuide)
            {
                differences.Add("no companion page");
            }

            if (setting.Mirrored is { } mirrored && mirrored != LayerOperations.MirrorByDefault(layer.Role))
            {
                differences.Add(mirrored ? "mirrored" : "not mirrored");
            }

            if (differences.Count > 0)
            {
                carried.Add(layer.FileName + " (" + string.Join(", ", differences) + ")");
            }
        }

        if (carried.Count > 0)
        {
            notes.Add("the command line sets a layer's output and nothing else about it, so these "
                + "come from the layer's own settings rather than from the command — "
                + string.Join("; ", carried) + ".");
        }
    }

    /// <summary>
    /// A project is read from the disk, not from the window.
    ///
    /// **The flags above pin everything they can reach**, so a block whose project is a version
    /// behind still rebuilds the right outputs, the right stock and the right tabs. What they cannot
    /// reach — a layer's depth, its cutter, its per-tab sizes — comes out of the file, and the
    /// window will happily export a change that has not been saved into it. So the block says which
    /// file it reads and that it reads the saved one.
    /// </summary>
    private static void Project(CliScriptContext context, List<string> notes)
    {
        if (!context.FromProject)
        {
            return;
        }

        notes.Add("this command reads " + context.Source + " as it was last saved. The flags above "
            + "pin what they can; anything named as coming from a layer's own settings comes from "
            + "that file, so save the project before relying on this.");
    }

    /// <summary>
    /// The one caveat that is true of every block: the machine's own settings are not in it.
    ///
    /// Safe height, decimals, feeds and rapids shape every line of every program and no flag names
    /// any of them — they are Settings › Machine, which belongs to the machine running this and not
    /// to the export. The tool library is the same kind of thing and matters more: the bit each
    /// layer is cut with is chosen out of it, so a machine without the cutter this export used gets
    /// a different one and no warning from the command line. "Byte for byte" is therefore a claim
    /// about the same machine, and the block has to say so, because somebody comparing two folders
    /// from two machines would otherwise read a difference in the third decimal as a bug here.
    ///
    /// Start and end lines are the same thing with a way out, so when there are any, the note says
    /// what it is.
    /// </summary>
    private static void Machine(CliScriptContext context, List<string> notes)
    {
        notes.Add("Settings › Machine — safe height, decimals, feeds — and your tool library come "
            + "from the machine this runs on, not from this export: the bit each layer is cut with "
            + "is chosen out of that library. On another machine, match those first."
            + (context.Framing.IsEmpty
                ? string.Empty
                : " These programs also carry start and end lines of their own, from Settings › "
                    + "G-code or from the project: save them beside the Gerbers and add "
                    + "--start-gcode start.nc --end-gcode end.nc to be sure of them."));
    }

    private static IEnumerable<string> Aligned(DrillAlignment alignment)
    {
        yield return Invariant($"--align {Mm(alignment.XNm)},{Mm(alignment.YNm)}");

        if (alignment.Outline)
        {
            yield return "--align-outline";
        }

        if (!alignment.IsShiftOnly)
        {
            yield return Invariant($"--align-turn {alignment.RotationDegrees:0.######}");
            yield return Invariant($"--align-about {Mm(alignment.PivotNm.X)},{Mm(alignment.PivotNm.Y)}");
        }

        if (alignment.Moved is { } moved && moved.Length > 0)
        {
            yield return "--align-moves " + Quote(string.Join(",", moved));
        }
    }

    private static LayerOutputSettings? Setting(CliScriptContext context, string layer) =>
        context.Settings.TryGetValue(layer, out var setting) ? setting : null;

    /// <summary>A layer's settings as they arrive, for comparing against.</summary>
    private static LayerOutputSettings Default { get; } = new() { FileName = string.Empty };

    private static string Word(LayerOutputSettings setting) => setting.Output switch
    {
        OutputKind.Svg => setting.Invert ? "svg-" : "svg",
        OutputKind.Gcode => "gcode",
        _ => "none",
    };

    private static string Word(OutputKind only) => only == OutputKind.Svg ? "svg" : "gcode";

    private static string Word(SvgPlacingLayers marks) => marks switch
    {
        SvgPlacingLayers.OutlineAndStock => "stock",
        SvgPlacingLayers.OutlineOnly => "outline",
        _ => "off",
    };

    /// <summary>
    /// Millimetres, with no more decimals than the number has.
    ///
    /// Six is exact — a nanometre is a millionth of a millimetre — and the trailing zeros come off,
    /// so a 10 mm border reads as 10 rather than as 10.000000.
    /// </summary>
    private static string Mm(long nm) =>
        Nm.ToMillimetres(nm).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// Quoted, always, for anything that came from a file name.
    ///
    /// Board folders have spaces in them — `Arduino Mega 2560` — and so do layer names, which go
    /// inside a --set value where a split would be silent rather than loud.
    /// </summary>
    private static string Quote(string text) => "\"" + text.Replace("\"", "'", StringComparison.Ordinal) + "\"";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
