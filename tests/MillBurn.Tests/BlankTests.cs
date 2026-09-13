using System.Text.RegularExpressions;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The piece of stock the job is built on.
///
/// The app cuts you a rectangle, or you tell it about the one you already have, and either way the
/// edges of that rectangle are the datum for every machine and every step after it. Three things
/// downstream read it and all three are wrong without it: work zero is its lower-left corner, the
/// shared SVG page is its bounds, and a mirrored layer flips about *its* centreline.
///
/// That last one is the dangerous one and has its own test. A rectangle seats identically whichever
/// way round it went in, so a flip about the wrong axis produces a file that looks entirely correct
/// and a board that is scrap.
/// </summary>
public sealed class BlankTests(ITestOutputHelper output)
{
    private static readonly Bounds Board = new(
        Nm.FromMillimetres(100), Nm.FromMillimetres(50),
        Nm.FromMillimetres(140), Nm.FromMillimetres(80));

    private static readonly long Cutter = Nm.FromMillimetres(1);

    // ------------------------------------------------------------------ stated size

    /// <summary>
    /// The case this was asked for: pre-cut stock, and a board laid out to fit it.
    ///
    /// "My project is one little board panelised into 11 rows and 6 columns. This is deliberate so
    /// that the full board fits onto some pre-cut copper-clad boards I have that are 183 x 122 mm.
    /// So, much better to just enter those dimensions directly."
    /// </summary>
    [Fact]
    public void AStatedBlankIsExactlyThatSizeWithTheBoardCentred()
    {
        var plan = Blanks.Resolve(
            new BlankOptions { Enabled = true, Sizing = BlankSizing.Stated, WidthMm = 183, HeightMm = 122 },
            Board,
            Cutter);

        Assert.True(plan.Resolved);
        Assert.Equal(183, Nm.ToMillimetres(plan.Bounds.Width), 3);
        Assert.Equal(122, Nm.ToMillimetres(plan.Bounds.Height), 3);

        // Centred: the borders match on each axis.
        Assert.Equal(plan.LeftNm(Board), plan.RightNm(Board));
        Assert.Equal(plan.BottomNm(Board), plan.TopNm(Board));

        output.WriteLine(string.Join("\n", plan.Notes));
        Assert.Contains(plan.Notes, n => n.Contains("centred", StringComparison.Ordinal));
    }

    /// <summary>
    /// The check that pays for the feature: the alternative is finding out with the stock clamped.
    /// </summary>
    [Fact]
    public void ABoardThatDoesNotFitIsRefusedByHowMuch()
    {
        var plan = Blanks.Resolve(
            new BlankOptions { Enabled = true, Sizing = BlankSizing.Stated, WidthMm = 42, HeightMm = 122 },
            Board,
            Cutter);

        Assert.False(plan.Resolved);

        var refusal = Assert.Single(plan.Refusals);
        output.WriteLine(refusal);

        // 40 mm of board, 42 of blank, 3 mm of border needed each side: 4 mm short.
        Assert.Contains("4.00 mm too wide", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The floor is the cutter plus somewhere to hold the piece, and it is stated rather than
    /// asserted — a number that looks arbitrary is a number people override without thinking.
    /// </summary>
    [Fact]
    public void ABorderTooNarrowForTheCutterIsRefusedByName()
    {
        var plan = Blanks.Resolve(
            new BlankOptions { Enabled = true, LeftMm = 1, RightMm = 10, BottomMm = 10, TopMm = 10 },
            Board,
            Cutter);

        var refusal = Assert.Single(plan.Refusals);
        output.WriteLine(refusal);

        Assert.Contains("left border", refusal, StringComparison.Ordinal);
        Assert.Contains("3.00 mm is the floor", refusal, StringComparison.Ordinal);
    }

    /// <summary>Nothing happens unless it is asked for. It moves work zero; it must be deliberate.</summary>
    [Fact]
    public void NothingHappensUnlessItIsTurnedOn()
    {
        Assert.False(Blanks.Resolve(new BlankOptions(), Board, Cutter).Resolved);
        Assert.False(Blanks.Resolve(BlankOptions.Default, Board, Cutter).Resolved);
    }

    /// <summary>
    /// A declared blank says so, because everything a cut one guarantees it only asserts.
    /// </summary>
    [Fact]
    public void ADeclaredBlankSaysItIsAClaimRatherThanAMeasurement()
    {
        var plan = Blanks.Resolve(
            new BlankOptions
            {
                Enabled = true, Sizing = BlankSizing.Stated, WidthMm = 183, HeightMm = 122, Cut = false,
            },
            Board,
            Cutter);

        Assert.False(plan.Cut);
        Assert.Contains(plan.Notes, n => n.Contains("claim rather than a measurement", StringComparison.Ordinal));
        Assert.Contains(plan.Notes, n => n.Contains("mark the datum corner yourself", StringComparison.Ordinal));
    }

    /// <summary>Asymmetric sides are allowed and flagged when anything in the job is mirrored.</summary>
    [Fact]
    public void AsymmetricSidesAreFlaggedOnlyWhenSomethingIsMirrored()
    {
        var lopsided = new BlankOptions { Enabled = true, LeftMm = 20, RightMm = 5, BottomMm = 10, TopMm = 10 };

        Assert.DoesNotContain(
            Blanks.Resolve(lopsided, Board, Cutter, mirrored: false).Notes,
            n => n.Contains("centreline", StringComparison.Ordinal));

        var warned = Blanks.Resolve(lopsided, Board, Cutter, mirrored: true).Notes;
        output.WriteLine(string.Join("\n", warned));

        Assert.Contains(warned, n => n.Contains("15.00 mm from where equal borders", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ what it changes downstream

    private static ExportPlan Plan(
        BlankOptions blank, string board = RealBoards.PogoTest1, bool masksAsSvg = false)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,

                // The built-in default for a mask is not SVG, so the page test has to ask for one
                // explicitly rather than hope the defaults produce it.
                Output = masksAsSvg && l.Role is LayerRole.TopMask or LayerRole.BottomMask
                    ? OutputKind.Svg
                    : LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        return ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6),
            job: new JobOptions { Blank = blank });
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) Extents(string program)
    {
        double x = 0, y = 0;
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;

        foreach (var line in program.Split('\n'))
        {
            // The park move home at the end is not part of the work and would drag every extent to
            // zero, which is the one number this is trying to measure against.
            if (line.Trim() is "G0 X0.000 Y0.000")
            {
                continue;
            }

            var words = Regex.Matches(line, "([XY])(-?[0-9.]+)");

            foreach (Match word in words)
            {
                var v = double.Parse(word.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);

                if (word.Groups[1].Value == "X") { x = v; } else { y = v; }
            }

            if (words.Count == 0)
            {
                continue;
            }

            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }

        return (minX, maxX, minY, maxY);
    }

    /// <summary>Work zero moves to the blank's corner, so every program shifts by the border.</summary>
    [Fact]
    public void WorkZeroMovesToTheBlanksCorner()
    {
        var without = Plan(new BlankOptions());
        var with = Plan(new BlankOptions { Enabled = true, LeftMm = 10, BottomMm = 8, RightMm = 10, TopMm = 8 });

        var a = Extents(without.Items.First(i => i.TargetName.EndsWith("F_Cu.nc", StringComparison.Ordinal)).Content);
        var b = Extents(with.Items.First(i => i.TargetName.EndsWith("F_Cu.nc", StringComparison.Ordinal)).Content);

        output.WriteLine($"no blank: X from {a.MinX:F3}   with blank: X from {b.MinX:F3}");

        Assert.Equal(10, b.MinX - a.MinX, 2);
        Assert.Equal(8, b.MinY - a.MinY, 2);
    }

    /// <summary>The page is the blank, which is what [04 §4.3] has always wanted it to be.</summary>
    [Fact]
    public void TheSharedPageIsTheBlank()
    {
        // The all-layers export, because plain PogoTest1 has no mask layer and so produces no SVG
        // at all — and a page test with nothing on the page proves nothing. Sized generously: that
        // board's bounds include silkscreen, which overhangs the copper.
        var plan = Plan(
            new BlankOptions { Enabled = true, Sizing = BlankSizing.Stated, WidthMm = 90, HeightMm = 100 },
            RealBoards.PogoTest1AllLayers,
            masksAsSvg: true);

        foreach (var why in plan.Skipped)
        {
            output.WriteLine(why);
        }

        Assert.True(plan.Blank.Resolved);

        var svg = plan.Items.First(i => i.Output == OutputKind.Svg);

        output.WriteLine(string.Join("\n", svg.Summary));

        // Asserted against the blank rather than against the literal, so the test says "the page is
        // the blank" rather than "the page is 90 by 100".
        var expected = Nm.ToMillimetreString(plan.Blank.Bounds.Width, 2)
            + " × " + Nm.ToMillimetreString(plan.Blank.Bounds.Height, 2) + " mm page";

        Assert.Contains(svg.Summary, s => s.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>
    /// **The one that scraps a board.** A mirrored layer flips about the blank's centreline, not the
    /// board's — the operator turns the stock over and pushes it back into the same corner.
    ///
    /// Asserted as the property rather than against a number: whatever the top side occupies from
    /// the blank's left edge, the bottom side must occupy the same distance from its right edge.
    /// With borders of 20 and 5 the two differ by 15 mm, so a flip about the board's centreline
    /// fails this by exactly the asymmetry — and passes every test that uses equal borders, which is
    /// why the borders here are deliberately lopsided.
    /// </summary>
    [Fact]
    public void AMirroredLayerFlipsAboutTheBlanksCentreline()
    {
        // The same blank width, with the borders swapped between left and right. That is what makes
        // this decisive: mirroring about the *blank's* centreline puts a bottom-side layer at
        // `blank.MaxX - x`, which depends on the RIGHT border alone, so swapping moves it +15 mm.
        // Mirroring about the *board's* centreline puts it at `board.MaxX - x + left`, which
        // depends on the LEFT border alone and moves it −15 mm. Equal borders cannot tell the two
        // apart, which is exactly why every other test here would have missed this.
        var wide = Plan(new BlankOptions { Enabled = true, LeftMm = 20, RightMm = 5, BottomMm = 10, TopMm = 10 });
        var swapped = Plan(new BlankOptions { Enabled = true, LeftMm = 5, RightMm = 20, BottomMm = 10, TopMm = 10 });

        Assert.Equal(wide.Blank.Bounds.Width, swapped.Blank.Bounds.Width);

        var bottomA = Extents(Program(wide, "B_Cu.nc"));
        var bottomB = Extents(Program(swapped, "B_Cu.nc"));
        var topA = Extents(Program(wide, "F_Cu.nc"));
        var topB = Extents(Program(swapped, "F_Cu.nc"));

        output.WriteLine($"mirrored  X {bottomA.MinX:F3} -> {bottomB.MinX:F3}  (moved {bottomB.MinX - bottomA.MinX:+0.000;-0.000})");
        output.WriteLine($"unmirrored X {topA.MinX:F3} -> {topB.MinX:F3}  (moved {topB.MinX - topA.MinX:+0.000;-0.000})");

        // The mirrored layer follows the right border.
        Assert.Equal(15, bottomB.MinX - bottomA.MinX, 2);

        // The unmirrored one follows the left border, in the other direction. Asserted too, because
        // a bug that shifted *everything* by the same amount would satisfy the first check alone.
        Assert.Equal(-15, topB.MinX - topA.MinX, 2);
    }

    private static string Program(ExportPlan plan, string suffix) =>
        plan.Items.First(i => i.TargetName.EndsWith(suffix, StringComparison.Ordinal)).Content;

    // ------------------------------------------------------------------ the program that cuts it

    /// <summary>A declared blank emits no program: the stock is already that size.</summary>
    [Fact]
    public void ADeclaredBlankCutsNothing()
    {
        var plan = Plan(new BlankOptions
        {
            Enabled = true, Sizing = BlankSizing.Stated, WidthMm = 60, HeightMm = 70, Cut = false,
        });

        Assert.True(plan.Blank.Resolved);
        Assert.DoesNotContain(plan.Items, i => i.TargetName.EndsWith(".blank.nc", StringComparison.Ordinal));
    }

    /// <summary>A cut blank comes first, because everything else is referenced to what it makes.</summary>
    [Fact]
    public void ACutBlankIsTheFirstFileInTheExport()
    {
        var plan = Plan(new BlankOptions { Enabled = true });

        Assert.StartsWith("PogoTest1", plan.Items[0].TargetName, StringComparison.Ordinal);
        Assert.EndsWith(".blank.nc", plan.Items[0].TargetName, StringComparison.Ordinal);
        Assert.Contains(plan.Items[0].Warnings, w => w.Contains("before anything else", StringComparison.Ordinal));
    }

    /// <summary>
    /// **Nothing touches the datum edges.** A tab stub on the bottom or left is a few tenths of an
    /// obstruction that stops the blank seating — invisibly, because the piece simply sits at a
    /// slight angle and everything after it is wrong. It is the failure this feature exists to
    /// prevent, so it must not be the failure it introduces.
    /// </summary>
    [Fact]
    public void TabsAreNeverOnTheDatumEdges()
    {
        var plan = Plan(new BlankOptions { Enabled = true });
        var program = plan.Items[0].Content;
        var extents = Extents(program);

        // A tab gap is a rapid that lands partway along an edge and is followed by a cut that
        // continues along that *same* edge. The chamfer also starts partway along the bottom, and
        // its next move leaves the edge — which is how the two are told apart.
        var lines = program.Split('\n').Select(l => l.Trim()).ToList();
        var gaps = 0;

        for (var i = 0; i + 1 < lines.Count; i++)
        {
            var jump = Regex.Match(lines[i], @"^G0 X(-?[0-9.]+) Y(-?[0-9.]+)$");
            var cut = Regex.Match(lines[i + 1], @"^G1 X(-?[0-9.]+) Y(-?[0-9.]+)");

            if (!jump.Success || !cut.Success)
            {
                continue;
            }

            var alongBottom = Near(Value(jump, 2), extents.MinY) && Near(Value(cut, 2), extents.MinY);
            var alongLeft = Near(Value(jump, 1), extents.MinX) && Near(Value(cut, 1), extents.MinX);

            if ((alongBottom && Inside(Value(jump, 1), extents.MinX, extents.MaxX))
                || (alongLeft && Inside(Value(jump, 2), extents.MinY, extents.MaxY)))
            {
                output.WriteLine($"gap on a datum edge: {lines[i]} then {lines[i + 1]}");
                gaps++;
            }
        }

        Assert.Equal(0, gaps);
    }

    /// <summary>
    /// The corner is keyed, because a rectangle seats identically whichever way round it went in
    /// and a wrong flip is otherwise invisible until the board is scrap.
    /// </summary>
    [Fact]
    public void TheDatumCornerIsChamfered()
    {
        var plan = Plan(new BlankOptions { Enabled = true });
        var program = plan.Items[0].Content;
        var extents = Extents(program);

        // A cut that runs diagonally across the lower-left corner.
        var chamfered = program.Split('\n').Any(l =>
        {
            var m = Regex.Match(l.Trim(), @"^G1 X(-?[0-9.]+) Y(-?[0-9.]+)");

            if (!m.Success)
            {
                return false;
            }

            var x = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var y = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);

            return Math.Abs(x - extents.MinX) < 0.01 && y > extents.MinY + 1 && y < extents.MinY + 6;
        });

        Assert.True(chamfered, "the lower-left corner should be chamfered");
        Assert.Contains("chamfered", plan.Items[0].Content, StringComparison.Ordinal);
    }

    private static double Value(Match m, int group) =>
        double.Parse(m.Groups[group].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.01;

    private static bool Inside(double v, double lo, double hi) => v > lo + 1 && v < hi - 1;

    /// <summary>
    /// The blank is cut with the Board outline layer's bit, and everywhere a person looks says so.
    ///
    /// There is deliberately no picker beside the blank's settings — the blank and the board come
    /// out with one cutter — so the answer has to be written down: in the blank's program, in the
    /// outline's, in the export summary and on the project page. And it has to be the outline's bit
    /// specifically. The lookup used to take the first G-code layer with any bit chosen, so a job
    /// with a V-bit picked for its copper cut the blank with the V-bit.
    /// </summary>
    [Fact]
    public void TheBlankIsCutWithTheBoardOutlinesBit()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var endMill = new Tool
        {
            Id = Guid.NewGuid(),
            Name = "2.0 mm end mill",
            Kind = ToolKind.EndMill,
            DiameterNm = Nm.FromMillimetres(2),
            StepdownNm = Nm.FromMillimetres(0.5),
        };

        var library = new ToolLibrary { Tools = [.. ToolLibrary.Default.Tools, endMill] };

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
                ToolId = l.Role switch
                {
                    LayerRole.Outline => endMill.Id,
                    LayerRole.TopCopper or LayerRole.BottomCopper => Tool.DefaultVBit.Id,
                    _ => null,
                },
            },
            StringComparer.Ordinal);

        // The case that used to go wrong: a copper layer with a bit chosen, ahead of the outline.
        Assert.NotEqual(
            LayerRole.Outline,
            loaded.Layers.First(l => settings[l.FileName] is { ToolId: not null, Output: OutputKind.Gcode }).Role);

        var plan = ExportPlanner.Plan(
            loaded, settings, library, Nm.FromMillimetres(1.6),
            job: new JobOptions { Blank = new BlankOptions { Enabled = true } });

        var blank = plan.Items.Single(i => i.TargetName.EndsWith(".blank.nc", StringComparison.Ordinal));
        var outline = plan.Items.Single(i => i.Role == LayerRole.Outline);
        var page = plan.Page!.Content;

        output.WriteLine(string.Join("\n", blank.Summary));

        Assert.Contains("Fit the 2.0 mm end mill", blank.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(Tool.DefaultVBit.Name, blank.Content, StringComparison.Ordinal);
        Assert.Contains(blank.Summary, s => s.StartsWith("2.0 mm end mill, the Board outline's bit", StringComparison.Ordinal));

        Assert.Contains("with the 2.0 mm end mill", outline.Content, StringComparison.Ordinal);

        Assert.Contains("Cut the blank</strong> with the <strong>2.0 mm end mill</strong>", page, StringComparison.Ordinal);
        Assert.Contains("Cut the board out</strong> with the same bit as the blank", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The blank is cut with the outline's numbers as well as its bit: the bit's stepdown and the
    /// outline row's distance through.
    ///
    /// Found cutting a real blank. An 0.8 mm board, an 0.8 mm end mill with a 0.5 mm stepdown, and
    /// the outline row set 0.1 mm through: the board outline took two passes and the blank took
    /// three, because the blank used built-in values of its own — 0.4 mm passes, 0.3 mm through.
    /// </summary>
    [Fact]
    public void TheBlankCutsWithTheOutlinesStepdownAndDepth()
    {
        var plan = PlanWithOutlineBit(EndMill(0.8, stepdownMm: 0.5), breakThroughMm: 0.1, thicknessMm: 0.8);
        var blank = plan.Items.Single(i => i.TargetName.EndsWith(".blank.nc", StringComparison.Ordinal)).Content;

        var depths = Moves(blank).Where(m => !m.Rapid && m.Z < 0).Select(m => m.Z).Distinct().Order().ToList();

        output.WriteLine("cutting depths: " + string.Join(", ", depths));

        Assert.Equal([-0.9, -0.5], depths);
        Assert.Contains("0.90 mm deep in 0.50 mm passes", blank, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pass breaks only where there is a tab to jump.
    ///
    /// Each pass used to be built from separate runs — bottom and left, then each stretch of the top,
    /// then each stretch of the right — and where one run ended exactly where the next began, at the
    /// top-left and top-right corners, the tool lifted clear and came straight back down on the same
    /// spot. Two pointless lifts a pass, and a fresh plunge into the kerf each time.
    /// </summary>
    [Fact]
    public void TheToolNeverLiftsOnlyToComeDownInTheSamePlace()
    {
        var plan = PlanWithOutlineBit(EndMill(0.8, stepdownMm: 0.5), breakThroughMm: 0.1, thicknessMm: 0.8);
        var moves = Moves(plan.Items.Single(i => i.TargetName.EndsWith(".blank.nc", StringComparison.Ordinal)).Content);

        var pointless = 0;
        Move? lastCut = null;
        var lifted = false;

        foreach (var move in moves)
        {
            if (move.Rapid && move.Z > move.FromZ && move.FromZ < 0)
            {
                lifted = true;
                continue;
            }

            if (!move.Rapid && move.Z < 0 && move.X == move.FromX && move.Y == move.FromY)
            {
                // A plunge. Pointless if it lands where the last cut stopped, at the depth that cut was at.
                if (lifted && lastCut is { } last && Near(last.X, move.X) && Near(last.Y, move.Y) && Near(last.Z, move.Z))
                {
                    output.WriteLine($"lifted and came back down at X{move.X} Y{move.Y} Z{move.Z}");
                    pointless++;
                }

                lifted = false;
                continue;
            }

            if (!move.Rapid && move.Z < 0)
            {
                lastCut = move;
            }
        }

        Assert.Equal(0, pointless);
    }

    /// <summary>
    /// The chamfer is an edge of the cut, not a cut of its own afterwards.
    ///
    /// It used to be cut as separate passes once the rectangle was done — which, at full depth, frees a
    /// small triangle of board at the corner to be thrown by the cutter. As an edge of the same loop the
    /// corner stays with the sheet. And it is the size asked for on the piece: offset outward by half
    /// the cutter like every other edge, so its line in the program sits where a 3 mm chamfer's does.
    /// </summary>
    [Fact]
    public void TheChamferIsCutAsPartOfTheSamePass()
    {
        var plan = Plan(new BlankOptions { Enabled = true });
        var moves = Moves(plan.Items[0].Content);

        var depths = moves.Where(m => !m.Rapid && m.Z < 0).Select(m => m.Z).Distinct().Count();
        var diagonals = moves
            .Select((m, i) => (Move: m, Before: i > 0 ? moves[i - 1] : null))
            .Where(p => !p.Move.Rapid && p.Move.Z < 0
                && Math.Abs(p.Move.X - p.Move.FromX) > 0.01 && Math.Abs(p.Move.Y - p.Move.FromY) > 0.01)
            .ToList();

        Assert.Equal(depths, diagonals.Count);

        // A 1.0 mm cutter, so half is 0.5, and a 3 mm chamfer on the piece: its offset line is
        // x + y = 3 - 0.5 * sqrt(2) in work coordinates, where the blank's corner is zero.
        var line = 3 - (0.5 * Math.Sqrt(2));

        foreach (var (move, before) in diagonals)
        {
            output.WriteLine($"chamfer to X{move.X} Y{move.Y} at Z{move.Z}, arriving by {(before!.Rapid ? "rapid" : "cutting")}");

            // Arrived cutting along the bottom edge, at the same depth — not dropped in on its own.
            Assert.False(before.Rapid);
            Assert.True(Near(before.Z, move.Z));
            Assert.True(Near(before.Y, move.FromY) && Near(move.FromY, -0.5), "comes off the bottom edge");

            Assert.Equal(line, move.FromX + move.FromY, 2);
            Assert.Equal(line, move.X + move.Y, 2);
        }
    }

    private sealed record Move(bool Rapid, double FromX, double FromY, double FromZ, double X, double Y, double Z);

    /// <summary>Every G0 and G1, with where it started from — enough to see lifts, plunges and edges.</summary>
    private static List<Move> Moves(string program)
    {
        var moves = new List<Move>();
        double x = 0, y = 0, z = 0;

        foreach (var raw in program.Split('\n'))
        {
            var line = raw.Trim();
            var code = Regex.Match(line, @"^G([01])\b");

            if (!code.Success)
            {
                continue;
            }

            double nx = x, ny = y, nz = z;

            foreach (Match word in Regex.Matches(line, "([XYZ])(-?[0-9.]+)"))
            {
                var v = double.Parse(word.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);

                switch (word.Groups[1].Value)
                {
                    case "X": nx = v; break;
                    case "Y": ny = v; break;
                    default: nz = v; break;
                }
            }

            moves.Add(new Move(code.Groups[1].Value == "0", x, y, z, nx, ny, nz));
            (x, y, z) = (nx, ny, nz);
        }

        return moves;
    }

    private static Tool EndMill(double diameterMm, double stepdownMm) => new()
    {
        Id = Guid.NewGuid(),
        Name = Invariant($"{diameterMm:0.0} mm end mill"),
        Kind = ToolKind.EndMill,
        DiameterNm = Nm.FromMillimetres(diameterMm),
        StepdownNm = Nm.FromMillimetres(stepdownMm),
    };

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    /// <summary>PogoTest1 on a default blank, with the Board outline row set to this bit and depth.</summary>
    private static ExportPlan PlanWithOutlineBit(Tool cutter, double breakThroughMm, double thicknessMm)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var library = new ToolLibrary { Tools = [.. ToolLibrary.Default.Tools, cutter] };

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l =>
            {
                var setting = new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) };

                return l.Role == LayerRole.Outline
                    ? setting with { ToolId = cutter.Id, BreakThroughNm = Nm.FromMillimetres(breakThroughMm) }
                    : setting;
            },
            StringComparer.Ordinal);

        return ExportPlanner.Plan(
            loaded, settings, library, Nm.FromMillimetres(thicknessMm),
            job: new JobOptions { Blank = new BlankOptions { Enabled = true } });
    }

    // ------------------------------------------------------------------ it travels with the project

    /// <summary>
    /// A board reopened next year has to cut the way it cut, and the blank is the setting that most
    /// obviously must: it decides where work zero is, so a project that forgot it would put every
    /// program a border's width out from the one before.
    /// </summary>
    [Fact]
    public void TheBlankIsSavedWithTheProject()
    {
        var folder = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "millburn-blank-" + Guid.NewGuid().ToString("N"))).FullName;

        try
        {
            var path = Path.Combine(folder, "board.millburn");

            var project = MillBurnProject.FromSources(
                ProjectFile.ImportFolder(RealBoards.Directory(RealBoards.PogoTest1)),
                RealBoards.Directory(RealBoards.PogoTest1));

            project.Settings = project.Settings with
            {
                Job = project.Settings.Job with
                {
                    Blank = new BlankOptions
                    {
                        Enabled = true,
                        Sizing = BlankSizing.Stated,
                        WidthMm = 183,
                        HeightMm = 122,
                        Cut = false,
                    },
                },
            };

            ProjectFile.Save(project, path);

            var reopened = ProjectFile.Open(path).Settings.Job.Blank;

            output.WriteLine($"{reopened.Sizing} {reopened.WidthMm} x {reopened.HeightMm}, cut={reopened.Cut}");

            Assert.True(reopened.Enabled);
            Assert.Equal(BlankSizing.Stated, reopened.Sizing);
            Assert.Equal(183, reopened.WidthMm);
            Assert.Equal(122, reopened.HeightMm);
            Assert.False(reopened.Cut);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A temp directory that outlives the test is not a test failure.
            }
        }
    }
}
