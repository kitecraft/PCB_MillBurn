using System.Text.RegularExpressions;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The commands that would rebuild an export, which the project page carries at its foot.
///
/// **From the workshop**: *"can we add a new section at the bottom for the cli command used to
/// create the export… or maybe, a list of cli commands that would be the fastest way to replicate
/// the outputs using the cli."* Recorded in
/// [06 §6.15](../../Documentation/06-Roadmap-and-Risks.md).
///
/// The standard the feature is held to is byte-for-byte: paste the block into a shell in a folder
/// holding the Gerbers and the same programs come out. That whole claim cannot be made from here —
/// these tests cannot run the CLI, which is a separate program this project deliberately does not
/// reference (06 §6.50) — so it was checked by hand, three ways, and recorded in the sprint's
/// closure. What is pinned here is everything that can be: that the flags named are flags the CLI
/// parses, that the numbers in them come from the plan rather than from the settings, and that the
/// block says what it cannot carry.
/// </summary>
public sealed class CliScriptTests(ITestOutputHelper output)
{
    private static Board TestBoard() =>
        BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

    /// <summary>Every layer exported the way its role says, with room to change one.</summary>
    private static Dictionary<string, LayerOutputSettings> SettingsFor(
        Board board, Func<BoardLayer, LayerOutputSettings, LayerOutputSettings>? change = null)
    {
        var settings = new Dictionary<string, LayerOutputSettings>(StringComparer.Ordinal);

        foreach (var layer in board.Layers)
        {
            var setting = new LayerOutputSettings
            {
                FileName = layer.FileName,
                Output = LayerOperations.DefaultFor(layer.Role),
                IsolationWidthNm = Nm.FromMillimetres(0.4),
            };

            settings[layer.FileName] = change is null ? setting : change(layer, setting);
        }

        return settings;
    }

    /// <summary>
    /// The context the planner would have built, for the cases that cannot go through it — a project
    /// source, or an alignment.
    ///
    /// **The job has to be passed here as well as to the planner**, which cost a failing test to
    /// learn: the planner reads the stock off the plan and the milled holes off the job it was
    /// given, and a context built without the job is a context the planner would never produce.
    /// </summary>
    private static CliScriptContext Context(
        Board board,
        IReadOnlyDictionary<string, LayerOutputSettings> settings,
        string? source = null,
        JobOptions? job = null) =>
        new()
        {
            Source = source ?? Path.GetFileName(board.Source!),
            Board = board,
            Settings = settings,
            Library = ToolLibrary.Default,
            BoardThicknessNm = Nm.FromMillimetres(0.8),
            Job = job ?? new JobOptions(),
        };

    /// <summary>The one command in a block that has one.</summary>
    private static string Only(IReadOnlyList<CliStep> steps)
    {
        Assert.Single(steps);
        return steps[0].Command;
    }

    /// <summary>
    /// Every flag the generator can emit is a flag the CLI parses.
    ///
    /// **The one check that reaches across the gap these tests cannot step over.** The block is
    /// generated in Pipeline and run by Cli, and nothing in the suite runs the CLI — so a flag
    /// renamed on one side and not the other produces a block that fails on the first line, and
    /// every other test here would still pass. Read out of the CLI's own source, which is the thing
    /// that would have changed.
    ///
    /// Collected from several blocks rather than one, because most of these flags only appear when
    /// the export that produced them had the thing they describe.
    /// </summary>
    [Fact]
    public void EveryFlagItEmitsIsOneTheCliParses()
    {
        var board = TestBoard();
        var settings = SettingsFor(board);
        var library = ToolLibrary.Default;
        var thickness = Nm.FromMillimetres(0.8);

        var plain = ExportPlanner.Plan(board, settings, library, thickness);

        var stocked = ExportPlanner.Plan(
            board, settings, library, thickness,
            job: new JobOptions
            {
                MillLargeHoles = true,
                MillAboveMm = 2.5,
                Blank = new BlankOptions { Enabled = true, AlignmentHoles = true },
            });

        var alignment = new DrillAlignment(Nm.FromMillimetres(0.08), Nm.FromMillimetres(-0.11), Outline: true)
        {
            RotationDegrees = 0.42,
            PivotNm = new Point2(Nm.FromMillimetres(12.5), Nm.FromMillimetres(9)),
        };

        var aligned = ExportPlanner.Plan(
            board, settings, library, thickness, OutputKind.Gcode, alignment: alignment);

        var blocks = new[]
        {
            CliScript.Text(plain.Commands),
            CliScript.Text(stocked.Commands),
            CliScript.Text(aligned.Commands),
        };

        // The flags on the command lines only. A comment may name a flag as advice — "add
        // --start-gcode start.nc" — and those are prose, not something that has to parse.
        var flags = blocks
            .SelectMany(b => b.Split('\n'))
            .Where(l => l.StartsWith(CliScript.Program, StringComparison.Ordinal))
            .SelectMany(l => Regex.Matches(l, @"(?<!\S)--[a-z][a-z-]*").Select(m => m.Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        output.WriteLine(string.Join(" ", flags));

        // Enough of them that a generator which silently stopped emitting would fail here too.
        Assert.True(flags.Count >= 12, $"only {flags.Count} flags came out: {string.Join(" ", flags)}");

        var program = File.ReadAllText(CliSource());

        foreach (var flag in flags)
        {
            Assert.Contains('"' + flag + '"', program, StringComparison.Ordinal);
        }

        // And each one is a flag of `export` rather than of some other command: `mill` takes
        // --depth and --passes and `export` does not, so a generator that emitted one of those
        // would satisfy the check above and fail on the first line of the block. Read off the help
        // for `export`, which is the one list that distinguishes them — and which therefore cannot
        // fall behind the generator either.
        var from = program.IndexOf("\"  export <folder-or-project>", StringComparison.Ordinal);
        var to = program.IndexOf("\"  align <folder-or-project>", StringComparison.Ordinal);

        // Asserted rather than left to throw: the slice is cut on two headings, and a reworded
        // heading should say so rather than come out as an out-of-range argument.
        Assert.True(from > 0 && to > from, "the help for `export` was not found in Program.cs");

        var help = program[from..to];

        foreach (var flag in flags)
        {
            // Whole flag, not a substring: --align is a flag of its own and also the first eight
            // characters of --align-outline, so plain containment would let a dropped --align pass
            // on the strength of its longer relative still being listed.
            Assert.Matches(Regex.Escape(flag) + "(?![a-z-])", help);
        }
    }

    /// <summary>
    /// The tab count comes from the board outline's layer, not from the first program that happens
    /// to be an outline operation.
    ///
    /// **Found by rebuilding a real project and diffing the programs.** Routed slots are written as
    /// an outline operation on a drill layer — <c>IsBoardOutline</c> says so in its own doc — and
    /// taking the first outline item picked the test board's plated slots, whose layer carries the
    /// untouched default of four. A board with three tabs got <c>--tabs 4</c>, and the rebuilt
    /// outline program differed in its tab count, its tab positions and one extra retract.
    ///
    /// Asserted with the trap present: the plan is checked to contain a <c>.slots.</c> program
    /// before the count is read, so this cannot pass by the board having lost its slots.
    /// </summary>
    [Fact]
    public void TheTabCountComesFromTheOutlineLayerNotTheRoutedSlots()
    {
        var board = TestBoard();

        var settings = SettingsFor(board, (layer, setting) => layer.Role == LayerRole.Outline
            ? setting with { TabCount = 3 }
            : setting);

        var plan = ExportPlanner.Plan(board, settings, ToolLibrary.Default, Nm.FromMillimetres(0.8));

        Assert.Contains(plan.Items, i => i.TargetName.Contains(".slots.", StringComparison.Ordinal));
        Assert.Contains(plan.Items, ExportPlanner.IsBoardOutline);

        var command = Only(CliScript.For(plan, Context(board, settings)));

        output.WriteLine(command);

        Assert.Contains("--tabs 3", command, StringComparison.Ordinal);
        Assert.DoesNotContain("--tabs 4", command, StringComparison.Ordinal);
    }

    /// <summary>
    /// The stock is written as the four borders the plan resolved, and those four borders resolve
    /// back to the same rectangle.
    ///
    /// Borders rather than the size it was asked for, because a stated size places the board in the
    /// middle of it and the command line cannot say otherwise: a stock stated as 100 x 100 with the
    /// board pushed into one corner would come back centred. Four borders say both the size and the
    /// placement.
    ///
    /// Checked by resolving a grown blank from the numbers in the block and requiring the same
    /// rectangle — which is what the CLI does with them, through the same function.
    /// </summary>
    [Fact]
    public void TheStockIsTheRectangleThePlanResolvedWhicheverWayItWasAskedFor()
    {
        var board = TestBoard();
        var settings = SettingsFor(board);

        // Stated, and deliberately off-centre: the case that a size alone cannot carry.
        var plan = ExportPlanner.Plan(
            board, settings, ToolLibrary.Default, Nm.FromMillimetres(0.8),
            job: new JobOptions
            {
                Blank = new BlankOptions
                {
                    Enabled = true,
                    Sizing = BlankSizing.Stated,
                    WidthMm = Nm.ToMillimetres(board.Bounds.Width) + 30,
                    HeightMm = Nm.ToMillimetres(board.Bounds.Height) + 30,
                    // Fractional, and to more decimals than two. Whole millimetres would round-trip
                    // through any formatting at all, including one that printed the borders to the
                    // nearest tenth and moved the rectangle under the board.
                    PlaceXMm = 20.123456,
                    PlaceYMm = 8.07654,
                },
            });

        Assert.True(plan.Blank.Resolved, string.Join(" ", plan.Blank.Refusals));

        var command = Only(CliScript.For(plan, Context(board, settings)));
        output.WriteLine(command);

        var borders = Regex.Match(command, @"--stock (\S+),(\S+),(\S+),(\S+)");
        Assert.True(borders.Success, command);

        double Number(int group) => double.Parse(
            borders.Groups[group].Value, System.Globalization.CultureInfo.InvariantCulture);

        // The order the CLI reads them in: left, bottom, right, top.
        var rebuilt = Blanks.Resolve(
            new BlankOptions
            {
                Enabled = true,
                LeftMm = Number(1),
                BottomMm = Number(2),
                RightMm = Number(3),
                TopMm = Number(4),
            },
            board.Bounds,
            Nm.FromMillimetres(1));

        Assert.True(rebuilt.Resolved, string.Join(" ", rebuilt.Refusals));
        Assert.Equal(plan.Blank.Bounds, rebuilt.Bounds);

        // And it really was off-centre, so the placement the borders carry is not the one a stated
        // size would have given back anyway.
        Assert.NotEqual(plan.Blank.LeftNm(board.Bounds), plan.Blank.RightNm(board.Bounds));
    }

    /// <summary>
    /// Every layer the board has is named, with its name quoted, and named once.
    ///
    /// Every layer because the defaults belong to whichever machine runs the block: Settings › Import
    /// decides what a mask layer becomes, so a block that leaves the mask out exports whatever the
    /// next machine thinks a mask is. Quoted because layer names have spaces in them — the Uno's do,
    /// which is why it is the board used here.
    /// </summary>
    [Fact]
    public void EveryLayerIsNamedOnceAndQuoted()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoUno));
        var settings = SettingsFor(board);

        var plan = ExportPlanner.Plan(board, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6));
        var command = Only(CliScript.For(plan, Context(board, settings)));

        output.WriteLine(command);

        Assert.Contains(board.Layers, l => l.FileName.Contains(' ', StringComparison.Ordinal));

        foreach (var layer in board.Layers)
        {
            var quoted = Regex.Matches(command, @"--set ""([^""]*)""")
                .Select(m => m.Groups[1].Value)
                .Count(v => v.StartsWith(layer.FileName + "=", StringComparison.Ordinal));

            Assert.Equal(1, quoted);
        }

        // One --set per layer and no more, so nothing is named twice under a different spelling.
        Assert.Equal(board.Layers.Count, Regex.Count(command, "--set "));
    }

    /// <summary>
    /// A project source pins everything a flag can reach, and says it reads the saved file.
    ///
    /// **Found by `/code-review`, and it was the one fault in this story's own code.** The block
    /// used to leave a project to carry its own settings, which made the command one line and made
    /// it wrong: the window exports what is on screen, and nothing stops that being an unsaved
    /// change, so a board whose tab count had just been raised from four to six wrote a page saying
    /// `export "Board.millburn"` — a command that rebuilds the saved four-tab job. The flags are the
    /// authority now, and what no flag reaches is named twice: once as the layer's own setting, once
    /// as a warning that the file is read as last saved.
    ///
    /// Asserted with the settings deliberately ahead of any plausible saved file: three tabs rather
    /// than four, a depth, a job and a stock, so a command that had deferred to the project would
    /// be missing all four.
    /// </summary>
    [Fact]
    public void AProjectSourcePinsWhatAFlagCanReach()
    {
        var board = TestBoard();
        var settings = SettingsFor(board, (layer, setting) => layer.Role switch
        {
            LayerRole.Outline => setting with { TabCount = 3, DepthNm = Nm.FromMillimetres(0.05) },

            // One layer for the laser, so the export has an SVG in it and the placing layers are
            // something the block has to say.
            LayerRole.TopMask => setting with { Output = OutputKind.Svg },
            _ => setting,
        });

        var job = new JobOptions { MillLargeHoles = true, Blank = new BlankOptions { Enabled = true } };

        var plan = ExportPlanner.Plan(
            board, settings, ToolLibrary.Default, Nm.FromMillimetres(0.8), job: job);

        Assert.Contains(plan.Items, i => i.Output == OutputKind.Svg);

        var steps = CliScript.For(
            plan, Context(board, settings, "Millburn_Test_Board.millburn", job));
        var command = Only(steps);

        output.WriteLine(command);
        output.WriteLine(string.Join("\n", steps[0].Notes));

        Assert.Contains("export \"Millburn_Test_Board.millburn\"", command, StringComparison.Ordinal);

        // Everything the window could have changed since the file was written, pinned on the line.
        Assert.Contains("--thickness 0.8", command, StringComparison.Ordinal);
        Assert.Contains("--tabs 3", command, StringComparison.Ordinal);
        Assert.Contains("--mill-holes", command, StringComparison.Ordinal);
        Assert.Contains("--stock ", command, StringComparison.Ordinal);
        Assert.Contains("--svg-marks", command, StringComparison.Ordinal);
        Assert.Equal(board.Layers.Count, Regex.Count(command, "--set "));

        // And the depth, which no flag reaches: named as the layer's own setting, and the file it
        // will be read out of named with it.
        Assert.Contains(steps[0].Notes, n => n.Contains("0.05", StringComparison.Ordinal));

        Assert.Contains(
            steps[0].Notes,
            n => n.Contains("Millburn_Test_Board.millburn as it was last saved", StringComparison.Ordinal));
    }

    /// <summary>
    /// The aligned export is a second command, after the first, and only when there is an alignment.
    ///
    /// The order is the point of there being two: the numbers in the second are a measurement taken
    /// with the board already clamped, so it cannot run until the first has run and the board has
    /// gone back in. The first must not carry the shift, or the programs it writes would already be
    /// moved and the measurement would be of a board that had been corrected twice.
    /// </summary>
    [Fact]
    public void TheAlignedExportIsASecondCommandAndTheFirstIsNotMoved()
    {
        var board = TestBoard();
        var settings = SettingsFor(board);
        var plan = ExportPlanner.Plan(board, settings, ToolLibrary.Default, Nm.FromMillimetres(0.8));

        Assert.Single(CliScript.For(plan, Context(board, settings)));

        var alignment = new DrillAlignment(Nm.FromMillimetres(0.08), Nm.FromMillimetres(-0.11), Outline: true);

        var steps = CliScript.For(
            plan, Context(board, settings) with { Alignment = alignment });

        Assert.Equal(2, steps.Count);
        output.WriteLine(steps[1].Command);

        Assert.DoesNotContain("--align", steps[0].Command, StringComparison.Ordinal);
        Assert.Contains("--align 0.08,-0.11", steps[1].Command, StringComparison.Ordinal);
        Assert.Contains("--align-outline", steps[1].Command, StringComparison.Ordinal);

        // A shift with no turn says nothing about a turn: --align-turn without --align-about turns
        // the board about work zero, which is not where anything was measured.
        Assert.DoesNotContain("--align-turn", steps[1].Command, StringComparison.Ordinal);

        var turned = CliScript.For(plan, Context(board, settings) with
        {
            Alignment = alignment with
            {
                RotationDegrees = 0.42,
                PivotNm = new Point2(Nm.FromMillimetres(12.5), Nm.FromMillimetres(9)),
            },
        });

        Assert.Contains("--align-turn 0.42", turned[1].Command, StringComparison.Ordinal);
        Assert.Contains("--align-about 12.5,9", turned[1].Command, StringComparison.Ordinal);
    }

    /// <summary>
    /// A layer setting the command line cannot carry is named, with its value.
    ///
    /// The export is then not reproducible from the block, and that has to be said rather than left
    /// to be discovered: somebody runs a block that looks complete and gets copper cut to a
    /// different depth. Named for the layers this export wrote and no others — a depth set on a
    /// layer nothing exported is a setting nobody used, not a difference.
    /// </summary>
    [Fact]
    public void ALayerSettingWithNoFlagIsNamedWithItsValue()
    {
        var board = TestBoard();

        var settings = SettingsFor(board, (layer, setting) => layer.Role switch
        {
            LayerRole.TopCopper => setting with { DepthNm = Nm.FromMillimetres(0.07), Passes = 2 },

            // Set on a layer that is not exported, so it must not be mentioned.
            LayerRole.TopPaste => setting with { DepthNm = Nm.FromMillimetres(0.5) },
            _ => setting,
        });

        var plan = ExportPlanner.Plan(board, settings, ToolLibrary.Default, Nm.FromMillimetres(0.8));
        var notes = CliScript.For(plan, Context(board, settings))[0].Notes;

        foreach (var note in notes)
        {
            output.WriteLine(note);
        }

        var copper = board.Layers.First(l => l.Role == LayerRole.TopCopper).FileName;
        var paste = board.Layers.First(l => l.Role == LayerRole.TopPaste).FileName;

        // One note for all of them, so the layer, its depth and its pass count are in the same
        // sentence rather than in one note each.
        var named = Assert.Single(notes, n => n.Contains(copper, StringComparison.Ordinal));
        Assert.Contains("0.07", named, StringComparison.Ordinal);
        Assert.Contains("2 passes", named, StringComparison.Ordinal);

        Assert.DoesNotContain(notes, n => n.Contains(paste, StringComparison.Ordinal));

        // And the caveat that is true of every block, because no flag names Settings › Machine.
        Assert.Contains(notes, n => n.Contains("Machine", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every line of the block is either a command or a comment carrying its own hash.
    ///
    /// **The paste hazard, and the reason the comments are wrapped here rather than by the browser.**
    /// A long comment left for the page to wrap looks fine on screen and pastes as one line; a shell
    /// takes the whole of it as a comment, which is harmless — but a comment the generator wrapped
    /// without re-marking each line would paste as a comment followed by a command made of its own
    /// tail. So every line is checked, and checked to be short enough that the page does not wrap it
    /// again.
    /// </summary>
    [Fact]
    public void EveryCommentLineCarriesItsOwnHash()
    {
        var board = TestBoard();

        var settings = SettingsFor(board, (layer, setting) => layer.Role == LayerRole.BottomCopper
            ? setting with { DepthNm = Nm.FromMillimetres(0.07), Mirrored = false, Passes = 3 }
            : setting);

        var plan = ExportPlanner.Plan(
            board, settings, ToolLibrary.Default, Nm.FromMillimetres(0.8),
            framing: new ProgramFraming { Start = "G21", End = "M5" },
            job: new JobOptions { Blank = new BlankOptions { Enabled = true } });

        var text = CliScript.Text(plan.Commands);
        output.WriteLine(text);

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Long enough that the wrapping is actually being exercised: several comments, and more than
        // one line of at least one of them.
        Assert.True(lines.Count(l => l.StartsWith('#')) >= 6, text);

        foreach (var line in lines)
        {
            if (line.StartsWith(CliScript.Program, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.StartsWith("# ", line, StringComparison.Ordinal);
            Assert.True(line.Length <= 95, $"{line.Length} characters: {line}");
        }
    }

    /// <summary>
    /// The page's block is the generator's text and nothing else.
    ///
    /// One renderer for the page and for <c>export --commands</c>, so what somebody reads in the
    /// folder and what they get on the terminal cannot come to disagree. Asserted by finding the
    /// block in the page and requiring it to be the generator's own output, escaped.
    /// </summary>
    [Fact]
    public void ThePageCarriesTheSameBlockTheGeneratorWrote()
    {
        var board = TestBoard();
        var settings = SettingsFor(board);
        var plan = ExportPlanner.Plan(board, settings, ToolLibrary.Default, Nm.FromMillimetres(0.8));

        Assert.NotEmpty(plan.Commands);

        var page = plan.Page!.Content;
        var block = Regex.Match(page, "<pre class=\"cli\"><code>(.*?)</code></pre>", RegexOptions.Singleline);

        Assert.True(block.Success, "the page has no command block");

        var expected = CliScript.Text(plan.Commands)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

        Assert.Equal(expected, block.Groups[1].Value);

        // And the command names the folder the Gerbers are in, relative: an absolute path from the
        // machine that exported is wrong on the machine that runs it.
        Assert.Contains(
            "export \"" + Path.GetFileName(board.Source!) + "\"",
            plan.Commands[0].Command,
            StringComparison.Ordinal);

        Assert.DoesNotContain(Path.GetDirectoryName(board.Source!)!, plan.Commands[0].Command, StringComparison.Ordinal);
    }

    /// <summary>
    /// The CLI's own source, found the way <see cref="RealBoards"/> finds the boards.
    ///
    /// Read rather than referenced because the test project does not reference Cli and should not
    /// (06 §6.50): a test that pulled in a second entry point would pull in its dependencies and its
    /// static state with it.
    /// </summary>
    private static string CliSource()
    {
        var dir = AppContext.BaseDirectory;

        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "MillBurn.Cli", "Program.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new FileNotFoundException(
            "src/MillBurn.Cli/Program.cs was not found above " + AppContext.BaseDirectory);
    }
}
