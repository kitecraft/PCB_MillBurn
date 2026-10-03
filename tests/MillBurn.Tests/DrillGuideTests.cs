using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The files a drilling layer is written as, and the page that explains how to run them.
///
/// Its whole value is naming the right bit in the right order, so that is what the tests are about.
/// A guide that names the wrong bit is worse than no guide: somebody will follow it, and the board
/// gets a 1.7 mm hole where a 1.0 mm one belonged.
///
/// A layer that needs several bits is **one file per bit**. It used to be one file that stopped
/// between bits with <c>M0</c>, which leaves GRBL on hold — and a controller on hold will not jog or
/// probe, so the page's "change the bit, then resume" could not be done at the machine.
/// </summary>
public sealed class DrillGuideTests(ITestOutputHelper output)
{
    private static ExportPlan Plan(string board)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        return ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);
    }

    /// <summary>One layer's drilling files, in the order they run. PogoTest1's plated holes need two bits.</summary>
    private static List<ExportItem> Files(string board, LayerRole role = LayerRole.PlatedDrill) =>
        [.. Plan(board).Items.Where(i => i.Operation == OperationKind.Drilling && i.Role == role)];

    private static List<GuideProgram> Programs(IEnumerable<ExportItem> files) =>
        [.. files.Select(f => new GuideProgram(f.TargetName, f.Content))];

    // ------------------------------------------------------------------ the files

    /// <summary>One file per bit, named in the order they run, and none of them stops for a change.</summary>
    [Fact]
    public void EachBitIsItsOwnFileAndNoFileStops()
    {
        var files = Files(RealBoards.PogoTest1);

        foreach (var file in files)
        {
            output.WriteLine($"{file.TargetName} · {file.Bit}");
        }

        Assert.Equal(2, files.Count);
        Assert.EndsWith(".bit1-1.70mm.nc", files[0].TargetName, StringComparison.Ordinal);
        Assert.EndsWith(".bit2-1.00mm.nc", files[1].TargetName, StringComparison.Ordinal);

        Assert.All(files, f => Assert.DoesNotContain(f.Content.Split('\n'), l => l.Trim() == "M0"));
    }

    /// <summary>
    /// Each file says which bit it is for and which file comes next, at the top — the part a sender
    /// shows when the file is opened, and the thing to check before pressing start.
    /// </summary>
    [Fact]
    public void EachFileSaysWhichBitAndWhatComesNext()
    {
        var files = Files(RealBoards.PogoTest1);

        Assert.Contains("Bit 1 of 2: fit the 1.70 mm drill", files[0].Content, StringComparison.Ordinal);
        Assert.Contains("Next: " + files[1].TargetName, files[0].Content, StringComparison.Ordinal);

        Assert.Contains("Bit 2 of 2: fit the 1.00 mm drill", files[1].Content, StringComparison.Ordinal);
        Assert.Contains("This is the last of them.", files[1].Content, StringComparison.Ordinal);

        Assert.Equal("Bit 1 of 2 · 1.70 mm drill", files[0].Bit);
        Assert.Equal("Bit 2 of 2 · 1.00 mm drill", files[1].Bit);
    }

    /// <summary>A layer with one bit is the one file it always was, under the same name, with no bit label.</summary>
    [Fact]
    public void ALayerWithOneBitIsStillOneFile()
    {
        var file = Assert.Single(Files(RealBoards.PogoTest1, LayerRole.NonPlatedDrill));

        Assert.Equal("PogoTest1-NPTH.nc", file.TargetName);
        Assert.Null(file.Bit);
        Assert.DoesNotContain("Bit 1 of", file.Content, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the sequence

    /// <summary>
    /// The bits, named and in order, each against the file it is in.
    ///
    /// The bit names were wrong first time: the emitter writes a toolpath's label *before* the
    /// tool-change sequence that precedes it, so splitting a program at its stops left every label
    /// attached to the section before the one it names.
    /// </summary>
    [Fact]
    public void EveryBitIsNamedAndTheyAreInOrder()
    {
        var files = Files(RealBoards.PogoTest1);

        Assert.NotNull(files[0].Companion);

        var (_, report) = DrillGuide.Build(Programs(files), Context(files));

        output.WriteLine(string.Join("\n", report.Steps.Select(s => $"{s.Order}. {s.Bit} × {s.Holes} in {s.Program}")));

        Assert.Equal(2, report.Steps.Count);
        Assert.Equal("Drill 1.70 mm", report.Steps[0].Bit);
        Assert.Equal("Drill 1.00 mm", report.Steps[1].Bit);
        Assert.Equal(files.Select(f => f.TargetName), report.Steps.Select(s => s.Program));
        Assert.DoesNotContain(report.Steps, s => s.Bit.Contains("already in the spindle", StringComparison.Ordinal));
    }

    /// <summary>Biggest first, so the guide suggests the right thing.</summary>
    [Fact]
    public void TheOrderIsBiggestFirst()
    {
        var files = Files(RealBoards.PogoTest1);
        var (_, report) = DrillGuide.Build(Programs(files), Context(files));

        var diameters = report.Steps
            .Select(s => double.Parse(s.Bit.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(diameters.OrderByDescending(d => d), diameters);
    }

    /// <summary>
    /// The hole counts have to add up to the files', or the page is describing a different run.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.PogoTest1AllLayers)]
    public void TheHoleCountsAccountForEveryHole(string board)
    {
        var layers = Plan(board).Items
            .Where(i => i.Operation == OperationKind.Drilling)
            .GroupBy(i => i.LayerFileName);

        foreach (var layer in layers)
        {
            var files = layer.ToList();
            var (_, report) = DrillGuide.Build(Programs(files), Context(files));

            var drilled = files.Sum(f => GcodeParser.Parse(f.Content).Moves
                .Where(m => !m.IsRapid && m.IsVertical && m.ToZNm < 0)
                .Select(m => m.From)
                .Distinct()
                .Count());

            output.WriteLine($"{layer.Key}: {report.Holes} across {report.Steps.Count} bits in {files.Count} files");
            Assert.Equal(drilled, report.Holes);
        }
    }

    /// <summary>The line numbers have to land on the right file's program, so following along works.</summary>
    [Fact]
    public void EachSectionPointsAtARealLine()
    {
        var files = Files(RealBoards.PogoTest1);
        var (_, report) = DrillGuide.Build(Programs(files), Context(files));

        // The line a step points at is the line that *names* its bit, in the file it is in.
        foreach (var step in report.Steps)
        {
            var lines = files.Single(f => f.TargetName == step.Program).Content.Split('\n');

            Assert.InRange(step.Line, 1, lines.Length);
            Assert.Contains(step.Bit, lines[step.Line - 1], StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------ the page itself

    [Fact]
    public void ASingleBitJobDoesNotTalkAboutChangingBits()
    {
        var file = Assert.Single(Files(RealBoards.PogoTest1, LayerRole.NonPlatedDrill));
        var (html, report) = DrillGuide.Build(Programs([file]), Context([file]));

        Assert.Single(report.Steps);
        Assert.Equal(0, report.Changes);
        Assert.DoesNotContain("stops on its own", html, StringComparison.Ordinal);
        Assert.DoesNotContain("One file per bit", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page for a multi-bit layer: one file per bit in the suggested order, Z set before each, and
    /// nothing left over from when the program stopped and asked to be resumed.
    /// </summary>
    [Fact]
    public void AMultiBitPageListsTheFilesAndSaysToSetZEachTime()
    {
        var files = Files(RealBoards.PogoTest1);
        var html = files[0].Companion!.Content;

        Assert.Contains("Suggested order", html, StringComparison.Ordinal);
        Assert.Contains("One file per bit", html, StringComparison.Ordinal);
        Assert.Contains("Z is the one you have to set again", html, StringComparison.Ordinal);
        Assert.Contains("same spot each time", html, StringComparison.Ordinal);
        Assert.Contains("sacrificial board", html, StringComparison.Ordinal);
        Assert.All(files, f => Assert.Contains(f.TargetName, html, StringComparison.Ordinal));

        Assert.DoesNotContain("stops on its own", html, StringComparison.Ordinal);
        Assert.DoesNotContain("resume in", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page must not tell the operator two different things about re-zeroing.
    ///
    /// It did. "It does not need re-zeroing" — meaning the two axes that had not moved — sat
    /// directly above "re-zero Z after every bit change". The reassurance has to name the axes it is
    /// about.
    /// </summary>
    [Fact]
    public void ItDoesNotContradictItselfAboutRezeroing()
    {
        var html = Files(RealBoards.PogoTest1)[0].Companion!.Content;

        Assert.DoesNotContain("does not need re-zeroing", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no re-zeroing", html, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("X and Y keep their zero", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// One file, openable anywhere. It is written into whatever folder the operator picked, next to
    /// a machine that may never have been online.
    /// </summary>
    [Fact]
    public void ThePageIsSelfContained()
    {
        var html = Files(RealBoards.PogoTest1)[0].Companion!.Content;

        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<link", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLayerAndItsFilesAreNamedOnIt()
    {
        var files = Files(RealBoards.PogoTest1);
        var html = files[0].Companion!.Content;

        Assert.Contains("Plated holes", html, StringComparison.Ordinal);
        Assert.Contains("2 files", html, StringComparison.Ordinal);
        Assert.All(files, f => Assert.Contains(f.TargetName, html, StringComparison.Ordinal));
    }

    /// <summary>A layer label with an ampersand in it must not produce broken markup.</summary>
    [Fact]
    public void TextFromTheBoardIsEscaped()
    {
        var (html, _) = DrillGuide.Build(
            "( Drill 1.00 mm [1 holes] )\nM3 S1000\nG0 X1.000 Y1.000\nG1 Z-1.000 F60\nM30",
            new DrillGuideContext
            {
                BoardName = "Bell & Howell <test>",
                LayerLabel = "Plated holes",
                ProgramName = "a.nc",
            });

        Assert.Contains("Bell &amp; Howell &lt;test&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<test>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedPositionsAreExplained()
    {
        var (html, _) = DrillGuide.Build(
            "( Drill 1.00 mm [1 holes] )\nM3 S1000\nG0 X1.000 Y1.000\nG1 Z-1.000 F60\nM30",
            new DrillGuideContext
            {
                BoardName = "b",
                LayerLabel = "Plated holes",
                ProgramName = "a.nc",
                RepeatedPositions = 8,
            });

        Assert.Contains("8 positions", html, StringComparison.Ordinal);
        Assert.DoesNotContain("position(s)", html, StringComparison.Ordinal);
    }

    /// <summary>A program with no holes in it gets no page rather than an empty one.</summary>
    [Fact]
    public void NothingToDrillMeansNoCompanion()
    {
        var (_, report) = DrillGuide.Build("G21 G90\nM30", new DrillGuideContext
        {
            BoardName = "b",
            LayerLabel = "l",
            ProgramName = "a.nc",
        });

        Assert.Empty(report.Steps);
    }

    // ------------------------------------------------------------------ wiring

    /// <summary>One page per drilling layer, on its first file, and no page on anything else.</summary>
    [Fact]
    public void EachDrillingLayerGetsOnePage()
    {
        var items = Plan(RealBoards.PogoTest1).Items;

        foreach (var layer in items.Where(i => i.Operation == OperationKind.Drilling).GroupBy(i => i.LayerFileName))
        {
            var files = layer.ToList();

            Assert.NotNull(files[0].Companion);
            Assert.Equal(Path.GetFileNameWithoutExtension(layer.Key) + ".drilling.html", files[0].Companion!.TargetName);
            Assert.All(files.Skip(1), f => Assert.Null(f.Companion));
        }

        Assert.All(items.Where(i => i.Operation != OperationKind.Drilling), i => Assert.Null(i.Companion));
    }

    [Fact]
    public void TurningItOffLeavesNoPage()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
                WriteDrillGuide = false,
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        Assert.All(plan.Items, i => Assert.Null(i.Companion));
    }

    /// <summary>
    /// The page a real export writes carries a drawing for every bit, and each drawing has as many
    /// marks as that bit has holes.
    ///
    /// **This is the clause the story is judged on** — *a board with several bits produces one
    /// drawing per bit with its holes numbered in run order* — and it is asserted against the page
    /// the planner actually writes rather than against a figure built by hand: the numbers come off
    /// the emitted program, so a drawing that disagreed with the table would mean the two had been
    /// read from different places.
    /// </summary>
    [Fact]
    public void ThePageDrawsEveryBitsHoles()
    {
        var plan = Plan(RealBoards.MillburnTestBoard);

        var page = plan.Items
            .Select(i => i.Companion)
            .FirstOrDefault(c => c is not null && c.TargetName.EndsWith(".drilling.html", StringComparison.Ordinal));

        Assert.NotNull(page);

        var figures = System.Text.RegularExpressions.Regex.Count(page.Content, "<svg class=\"figure\"");

        output.WriteLine($"{figures} drawing(s) on {page.TargetName}");

        Assert.True(figures > 1, $"a board drilled with several bits drew {figures} picture(s)");

        // Every figure is about one bit, and its caption says which — so the drawings and the table
        // name the same bits in the same order.
        var captions = System.Text.RegularExpressions.Regex
            .Matches(page.Content, "<figcaption><strong>([^<]*)</strong>")
            .Select(m => m.Groups[1].Value)
            .ToList();

        output.WriteLine(string.Join(", ", captions));

        Assert.Equal(figures, captions.Count);
        Assert.Equal(captions, captions.Distinct(StringComparer.Ordinal));

        // **One drawing per bit, and each drawing holding that bit's holes** — which a
        // `/describe-test` pass found this test claiming and not checking: it counted figures and
        // captions against each other and never opened a figure or asked the program how many holes
        // a bit drills. A numbered figure draws one circle per hole; a crowded one draws a ring on
        // the first as well, which is the only reason the two counts differ.
        var (_, report) = DrillGuide.Build(
            [.. Files(RealBoards.MillburnTestBoard).Select(f => new GuideProgram(f.TargetName, f.Content))],
            Context(Files(RealBoards.MillburnTestBoard)));

        var drawn = System.Text.RegularExpressions.Regex
            .Matches(page.Content, "<svg class=\"figure\".*?</svg>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => System.Text.RegularExpressions.Regex.Count(m.Value, "<circle"))
            .ToList();

        var holes = report.Steps.Where(s => s.At.Count > 0).Select(s => s.Holes).ToList();

        output.WriteLine($"holes per bit: {string.Join(", ", holes)}");
        output.WriteLine($"marks per figure: {string.Join(", ", drawn)}");

        Assert.Equal(holes.Count, drawn.Count);

        for (var i = 0; i < holes.Count; i++)
        {
            var expected = holes[i] > GuideFigure.MostToNumber ? holes[i] + 1 : holes[i];

            Assert.Equal(expected, drawn[i]);
        }

        // And nothing was fetched to draw them: the page is one file, which the rest of this class
        // already pins, and the figures are part of it.
        Assert.DoesNotContain("http", page.Content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A bit with more holes than can be numbered says so instead of numbering them.
    ///
    /// The Arduino Mega drills 98 holes with its 1.00 mm bit, and that drawing is dots with the
    /// travel between them and a ring on the first. The caption carries the count either way, which
    /// is what makes the picture safe to degrade: the text is the authority.
    /// </summary>
    [Fact]
    public void ABitWithTooManyHolesDrawsTheRunInstead()
    {
        var plan = Plan(RealBoards.ArduinoMega);

        var page = plan.Items
            .Select(i => i.Companion)
            .FirstOrDefault(c => c is not null && c.TargetName.EndsWith(".drilling.html", StringComparison.Ordinal));

        Assert.NotNull(page);

        var crowded = System.Text.RegularExpressions.Regex
            .Matches(page.Content, "<figcaption><strong>([^<]*)</strong> — (\\d+) holes?([^<]*)")
            .Select(m => (Bit: m.Groups[1].Value, Holes: int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), Says: m.Groups[3].Value))
            .ToList();

        foreach (var (bit, holes, says) in crowded)
        {
            output.WriteLine($"{bit}: {holes} holes —{says}");
        }

        Assert.NotEmpty(crowded);

        // The rule, stated against the page rather than against the figure: over the limit the
        // caption promises dots, under it the caption promises numbers.
        foreach (var (bit, holes, says) in crowded)
        {
            Assert.Equal(
                holes > GuideFigure.MostToNumber,
                says.Contains("too many to number", StringComparison.Ordinal));
        }

        Assert.Contains(crowded, c => c.Holes > GuideFigure.MostToNumber);
        Assert.Contains(crowded, c => c.Holes <= GuideFigure.MostToNumber);

        // **And the figures do what their captions promise**, which the captions alone cannot show:
        // over the limit a drawing carries no numbers at all, under it every mark has one. A
        // `/describe-test` pass pointed out that the phrase in the caption was the only evidence
        // being taken for the drawing having changed.
        var figures = System.Text.RegularExpressions.Regex
            .Matches(page.Content, "<svg class=\"figure\".*?</svg>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => m.Value)
            .ToList();

        Assert.Equal(crowded.Count, figures.Count);

        for (var i = 0; i < figures.Count; i++)
        {
            var numbers = System.Text.RegularExpressions.Regex.Count(figures[i], "<text");

            output.WriteLine($"{crowded[i].Bit}: {crowded[i].Holes} holes, {numbers} number(s) drawn");

            if (crowded[i].Holes > GuideFigure.MostToNumber)
            {
                Assert.Equal(0, numbers);
            }
            else
            {
                Assert.Equal(crowded[i].Holes, numbers);
            }
        }
    }

    /// <summary>
    /// On a mirrored layer the picture's board is mirrored with its holes.
    ///
    /// **Found by `/code-review`, and it is the worst thing a drawing can do.** The marks come out of
    /// the emitted program, which `AssembleEach` mirrors about the frame's centreline when the layer
    /// is flipped; the backdrop was only ever translated. On an asymmetric board the holes would land
    /// off the profile — visible, and recoverable. On a symmetric one they would look perfectly
    /// plausible and be the mirror image of the truth, which is the half of this that makes it worth
    /// a test rather than a glance.
    ///
    /// Asserted by drawing the same board both ways and requiring the two backdrops to differ: a
    /// profile that did not move when its holes did is exactly the fault. The test board is used
    /// because its outline is the least symmetric in the corpus — a comb of notches down one side and
    /// a chamfer on one corner — so a mirror cannot be mistaken for a shift.
    /// </summary>
    [Fact]
    public void AMirroredLayerMirrorsThePictureToo()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        string Page(bool mirrored)
        {
            var settings = board.Layers.ToDictionary(
                l => l.FileName,
                l => new LayerOutputSettings
                {
                    FileName = l.FileName,
                    Output = LayerOperations.DefaultFor(l.Role),
                    Mirrored = l.Role == LayerRole.PlatedDrill ? mirrored : null,
                },
                StringComparer.Ordinal);

            var plan = ExportPlanner.Plan(
                board, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

            var companion = plan.Items
                .Select(i => i.Companion)
                .First(c => c is not null && c.TargetName.EndsWith(".drilling.html", StringComparison.Ordinal));

            return System.Text.RegularExpressions.Regex
                .Match(companion!.Content, "<path class=\"board\"[^>]*d=\"([^\"]*)\"")
                .Groups[1]
                .Value;
        }

        var plain = Page(mirrored: false);
        var flipped = Page(mirrored: true);

        output.WriteLine($"plain   {plain[..Math.Min(90, plain.Length)]}");
        output.WriteLine($"flipped {flipped[..Math.Min(90, flipped.Length)]}");

        Assert.NotEmpty(plain);
        Assert.NotEqual(plain, flipped);

        // And it is a mirror rather than any other difference: the profile keeps its width, and every
        // x is the frame's width less the x it had. Read off the two paths' own numbers, so this does
        // not depend on knowing the board's size.
        var left = Numbers(plain);
        var right = Numbers(flipped);

        Assert.Equal(left.Count, right.Count);

        var width = left.Where((_, i) => i % 2 == 0).Max() + left.Where((_, i) => i % 2 == 0).Min();

        for (var i = 0; i < left.Count; i += 2)
        {
            Assert.Equal(width - left[i], right[i], 0.01);
            Assert.Equal(left[i + 1], right[i + 1], 0.01);
        }
    }

    /// <summary>Every number in a path's `d`, in order, for comparing two drawings of one board.</summary>
    private static List<double> Numbers(string path) =>
    [
        .. System.Text.RegularExpressions.Regex
            .Matches(path, "-?\\d+(?:\\.\\d+)?")
            .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)),
    ];

    private static DrillGuideContext Context(List<ExportItem> files) => new()
    {
        BoardName = "PogoTest1",
        LayerLabel = files[0].LayerLabel,
        ProgramName = files[0].TargetName,
        BoardThicknessNm = Nm.FromMillimetres(1.6),
        BreakThroughNm = Nm.FromMillimetres(0.3),
    };
}
