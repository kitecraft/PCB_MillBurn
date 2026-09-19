using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Parsing G-code back and classifying it.
///
/// The point of reading our own output rather than drawing the toolpaths that made it is that the
/// two agree right up until the emitter has a bug, and only one of them is what the machine will
/// run (Documentation/05, section 2.1). So these tests are as much about the emitter as the parser.
/// </summary>
public sealed class BackplotTests
{
    private static long Mm(double mm) => Nm.FromMillimetres(mm);

    // ------------------------------------------------------------------ modal state

    /// <summary>
    /// A line saying only <c>X10</c> inherits its motion mode, its feed and every axis it does not
    /// mention. Losing that produces a plausible backplot of a program that does something else.
    /// </summary>
    [Fact]
    public void MotionModeAndAxesAreModal()
    {
        var program = GcodeParser.Parse(
            """
            G21 G90
            G1 X10 Y0 F100
            X20
            Y5
            """);

        Assert.Equal(3, program.Moves.Count);
        Assert.All(program.Moves, m => Assert.Equal(MoveKind.Feed, m.Kind));
        Assert.All(program.Moves, m => Assert.Equal(100, m.FeedMmPerMin));

        Assert.Equal(new Point2(Mm(20), 0), program.Moves[1].To);
        Assert.Equal(new Point2(Mm(20), Mm(5)), program.Moves[2].To);
    }

    [Fact]
    public void IncrementalModeAddsToTheCurrentPosition()
    {
        var program = GcodeParser.Parse("G21\nG90\nG0 X10 Y10\nG91\nG1 X5 Y0 F100\n");

        Assert.Equal(new Point2(Mm(15), Mm(10)), program.Moves[^1].To);
    }

    [Fact]
    public void InchesAreConverted()
    {
        var program = GcodeParser.Parse("G20 G90\nG1 X1 Y0 F10\n");

        Assert.Equal(Nm.FromInches(1), program.Moves[0].To.X);

        // Feed is inches per minute too, which is easy to forget.
        Assert.Equal(254, program.Moves[0].FeedMmPerMin, 3);
    }

    [Theory]
    [InlineData("G1 X10 Y0 (a comment) F100")]
    [InlineData("G1 X10 Y0 F100 ; a comment")]
    [InlineData("(leading) G1 X10 Y0 F100")]
    public void CommentsAreStrippedInBothForms(string line)
    {
        var program = GcodeParser.Parse("G21 G90\n" + line + "\n");

        Assert.Single(program.Moves);
        Assert.Equal(Mm(10), program.Moves[0].To.X);
    }

    /// <summary>I and J are the centre offset from the arc's *start*, not from the origin.</summary>
    [Fact]
    public void ArcCentresAreRelativeToTheStart()
    {
        var program = GcodeParser.Parse("G21 G90\nG0 X10 Y0\nG3 X0 Y10 I-10 J0 F100\n");

        var arc = program.Moves[^1];

        Assert.Equal(MoveKind.ArcCounterClockwise, arc.Kind);
        Assert.Equal(Point2.Origin, arc.Centre);

        // A quarter of a 10 mm circle.
        Assert.Equal(Math.PI * 10 / 2, arc.LengthNm / Nm.PerMillimetre, 3);
    }

    /// <summary>
    /// Nothing here emits R-form arcs, and a radius has two possible centres. Drawing it as a line
    /// is visibly wrong, which beats drawing the wrong arc convincingly.
    /// </summary>
    [Fact]
    public void AnArcWithoutIOrJIsReportedAndDrawnAsALine()
    {
        var program = GcodeParser.Parse("G21 G90\nG0 X10 Y0\nG2 X0 Y10 R10 F100\n");

        Assert.Equal(MoveKind.Feed, program.Moves[^1].Kind);
        Assert.Contains(program.Diagnostics, d => d.Message.Contains("Arc without", StringComparison.Ordinal));
    }

    /// <summary>
    /// GRBL does not implement canned cycles and ignores what it cannot parse, so a program full of
    /// them drills no holes on the machine. Saying nothing here would let a viewer show holes the
    /// machine will not make — or, worse, agree that there are none.
    /// </summary>
    [Fact]
    public void CannedCyclesAreReportedRatherThanSilentlySkipped()
    {
        var program = GcodeParser.Parse("G21 G90\nG0 X5 Y5\nG81 Z-2 R0.5 F100\nG80\n");

        Assert.Contains(program.Diagnostics, d => d.Message.Contains("canned cycle", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ classification

    private static IReadOnlyList<BackplotMove> Classify(string gcode) =>
        GcodeBackplot.Classify(GcodeParser.Parse(gcode));

    /// <summary>
    /// A hole the program plunges straight into is marked, because a plunge has no extent in plan
    /// and would otherwise draw nothing at all. Found on the stock's waste holes: drilled rather than
    /// spiralled, they showed only as the rapid that went to them. Pecks into one hole are one mark,
    /// and a plunge that goes on to cut sideways is the start of a contour, which draws itself.
    /// </summary>
    [Fact]
    public void APlungedHoleIsMarkedAndAContourStartIsNot()
    {
        var moves = Classify(string.Join(
            '\n',
            "G21 G90",
            "G0 Z2.000",

            // A hole, pecked twice.
            "G0 X5.000 Y5.000",
            "G0 Z0.500",
            "G1 Z-1.000 F60",
            "G0 Z0.500",
            "G1 Z-1.100 F60",
            "G0 Z2.000",

            // A contour: down, then along.
            "G0 X20.000 Y5.000",
            "G0 Z0.500",
            "G1 Z-0.100 F60",
            "G1 X30.000 Y5.000 F600",
            "G0 Z2.000",
            "M30"));

        var layers = BackplotBuilder.Build(moves);
        var holes = Assert.Single(layers, l => l.Id == "gcode-plunge");
        var mark = Assert.Single(holes.Runs);

        // A ring round the hole, closed.
        Assert.Equal(mark[0], mark[^1]);
        Assert.All(mark, p => Assert.InRange(p.DistanceTo(new Point2(Mm(5), Mm(5))), Mm(0.49), Mm(0.51)));
    }

    /// <summary>
    /// A cut is a cut wherever the stock top happens to be, not only below Z zero.
    ///
    /// Found on a levelled coupon. Levelling writes the measured height of the surface into every
    /// Z, so over a high spot the commanded Z is *positive* and the cut is still exactly its
    /// nominal depth below the copper — by construction, since the emitted Z is nominal plus the
    /// correction and the correction is the surface. Classifying by "Z below zero" drew a quarter
    /// of that line as travel, which read as a cut that had not happened, and under-reported the
    /// cutting distance with it: 2497 mm against the true 2565 mm.
    ///
    /// Zeroing Z on the spoilboard instead of the stock does the same thing to an ordinary program,
    /// and plenty of people work that way.
    /// </summary>
    [Fact]
    public void APositiveZAfterAPlungeIsStillCutting()
    {
        var moves = Classify(string.Join(
            '\n',
            "G21 G90",
            "G0 Z2.000",
            "G0 X0.000 Y0.000",
            "G0 Z0.500",
            "G1 Z0.004 F60",
            "G1 X10.000 Y0.000 F600",
            "G1 X10.000 Y0.100",
            "G0 Z2.000",
            "M30"));

        var cuts = moves.Where(m => m.Role == BackplotRole.Cut).ToList();

        Assert.Equal(2, cuts.Count);
        Assert.All(cuts, m => Assert.True(m.Move.DeepestZNm > 0, "these are the positive-Z ones"));

        // And the retract still ends the cut, so what follows is travel again.
        Assert.Equal(BackplotRole.Retract, moves[^1].Role);
    }

    /// <summary>
    /// Above the work and never plunged is travel, however the program is written. The state has to
    /// be established by a feed move going down, or every rapid across a board becomes a cut.
    /// </summary>
    [Fact]
    public void AProgramThatNeverPlungesHasNoCuts()
    {
        var moves = Classify(string.Join(
            '\n',
            "G21 G90",
            "G0 Z2.000",
            "G0 X0.000 Y0.000",
            "G1 X10.000 Y0.000 F600",
            "G1 X10.000 Y10.000",
            "M30"));

        Assert.DoesNotContain(moves, m => m.Role == BackplotRole.Cut);
    }

    /// <summary>A retract ends it: a feed move after lifting is travel, not a cut at height.</summary>
    [Fact]
    public void LiftingOutEndsTheCut()
    {
        var moves = Classify(string.Join(
            '\n',
            "G21 G90",
            "G0 Z2.000",
            "G1 Z-0.050 F60",
            "G1 X10.000 Y0.000 F600",
            "G0 Z2.000",
            "G1 X20.000 Y0.000 F600",
            "M30"));

        var inPlane = moves.Where(m => m.Move.MovesInPlane).ToList();

        Assert.Equal(BackplotRole.Cut, inPlane[0].Role);
        Assert.NotEqual(BackplotRole.Cut, inPlane[^1].Role);
    }

    [Fact]
    public void MovesAreClassifiedByWhatTheyAreDoing()
    {
        var moves = Classify(
            """
            G21 G90
            G0 Z2
            G0 X5 Y5
            G1 Z-0.05 F60
            G1 X15 Y5 F200
            G0 Z2
            G0 X50 Y50
            """);

        Assert.Equal(BackplotRole.Retract, moves[0].Role);
        Assert.Equal(BackplotRole.Travel, moves[1].Role);
        Assert.Equal(BackplotRole.Plunge, moves[2].Role);
        Assert.Equal(BackplotRole.Cut, moves[3].Role);
        Assert.Equal(BackplotRole.Retract, moves[4].Role);
        Assert.Equal(BackplotRole.LongTravel, moves[5].Role);
    }

    /// <summary>
    /// **A rapid below Z0 is the tool crossing the board at cutting depth.** Never legitimate, and
    /// found by reading the emitted file rather than by trusting the emitter that wrote it.
    /// </summary>
    [Fact]
    public void ARapidAtCuttingDepthIsFlagged()
    {
        var moves = Classify("G21 G90\nG1 Z-0.05 F60\nG0 X20 Y0\n");

        Assert.Contains(moves, m => m.Role == BackplotRole.Gouge);
        Assert.Equal(1, GcodeBackplot.Measure(moves).GougeCount);
    }

    [Fact]
    public void AGoodProgramHasNoGouges()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var job = JobBuilder.Build(board, new MillOptions());
        var (text, _) = GcodeEmitter.Emit(job, new GcodeOptions());

        var measured = GcodeBackplot.Measure(GcodeBackplot.Classify(GcodeParser.Parse(text)));

        Assert.Equal(0, measured.GougeCount);
    }

    // ------------------------------------------------------------------ round trip

    /// <summary>
    /// The whole reason the backplot parses the file: what comes back has to be what went in. A
    /// discrepancy here is an emitter bug, and it is the only place one would show.
    /// </summary>
    [Fact]
    public void CutLengthSurvivesTheRoundTrip()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var job = JobBuilder.Build(board, new MillOptions());
        var (text, emitted) = GcodeEmitter.Emit(job, new GcodeOptions());

        var measured = GcodeBackplot.Measure(GcodeBackplot.Classify(GcodeParser.Parse(text)));

        // Coordinates go out at three decimals, so a few thousand of them accumulate a little
        // rounding. A relative bound says what is actually required — the file describes the
        // toolpath that made it — where a fixed decimal place would just be a bet on how long this
        // particular board happens to be.
        var error = Math.Abs(measured.CutMm - emitted.CutLengthMm) / emitted.CutLengthMm;

        Assert.True(
            error < 0.0005,
            $"round trip lost {error:P4}: emitted {emitted.CutLengthMm:F3} mm, read back {measured.CutMm:F3} mm");
    }

    // ------------------------------------------------------------------ drawable layers

    /// <summary>
    /// Consecutive moves of one role join into a single run. A contour of two thousand short
    /// segments has to become one polyline, not two thousand.
    /// </summary>
    [Fact]
    public void ConsecutiveMovesOfOneRoleBecomeOneRun()
    {
        var moves = Classify(
            """
            G21 G90
            G1 Z-0.05 F60
            G1 X1 Y0 F200
            X2
            X3
            X4
            """);

        var cut = BackplotBuilder.Build(moves).Single(l => l.Id == "gcode-cut");

        Assert.Single(cut.Runs);
        Assert.Equal(5, cut.Runs[0].Count);
    }

    /// <summary>
    /// The offset puts a corner-referenced job back where the board is. Applying it to the points
    /// but not to the continuity check splits every move into its own run — the paths still draw
    /// correctly, so nothing looks wrong; only the count gives it away.
    /// </summary>
    [Fact]
    public void AnOffsetShiftsTheRunsWithoutFragmentingThem()
    {
        var moves = Classify("G21 G90\nG1 Z-0.05 F60\nG1 X1 Y0 F200\nX2\nX3\n");
        var offset = new Point2(Mm(150), Mm(-90));

        var plain = BackplotBuilder.Build(moves).Single(l => l.Id == "gcode-cut");
        var shifted = BackplotBuilder.Build(moves, offset).Single(l => l.Id == "gcode-cut");

        Assert.Equal(plain.Runs.Count, shifted.Runs.Count);
        Assert.Equal(plain.Runs[0].Count, shifted.Runs[0].Count);
        Assert.Equal(plain.Runs[0][0] + offset, shifted.Runs[0][0]);
    }

    [Fact]
    public void TravelIsOffByDefaultAndTheLoudLayersAreOn()
    {
        var moves = Classify(
            """
            G21 G90
            G1 Z-0.05 F60
            G1 X1 Y0 F200
            G0 Z2
            G0 X2 Y0
            G0 X60 Y60
            """);

        var layers = BackplotBuilder.Build(moves).ToDictionary(l => l.Id, l => l.VisibleByDefault);

        Assert.False(layers["gcode-travel"]);
        Assert.True(layers["gcode-long-travel"]);
        Assert.True(layers["gcode-cut"]);
    }

    // ------------------------------------------------------------------ costing

    /// <summary>
    /// A single time figure would be wrong in a way nobody could see. Junction handling decides
    /// where in the range a real machine lands, and modelling it is Phase 3's job — so both bounds
    /// are reported and neither is dressed up as the answer.
    /// </summary>
    [Fact]
    public void TimeIsReportedAsABracketNotAGuess()
    {
        var measured = GcodeBackplot.Measure(Classify(
            "G21 G90\nG1 Z-0.05 F60\nG1 X100 Y0 F200\n"));

        Assert.True(measured.OptimisticTime < measured.PessimisticTime);
        Assert.Contains("–", measured.TimeRange(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Most of a PCB job is segments far too short to reach the programmed feed, which is why
    /// distance divided by feed underestimates so badly. Many short moves must cost more than one
    /// long one of the same total length.
    /// </summary>
    [Fact]
    public void ManyShortMovesCostMoreThanOneLongOne()
    {
        var oneLong = "G21 G90\nG1 Z-0.05 F60\nG1 X100 Y0 F1000\n";

        var many = new System.Text.StringBuilder("G21 G90\nG1 Z-0.05 F60\n");
        for (var x = 1; x <= 1000; x++)
        {
            many.Append(System.Globalization.CultureInfo.InvariantCulture, $"G1 X{x * 0.1:F1} Y0 F1000\n");
        }

        var single = GcodeBackplot.Measure(Classify(oneLong));
        var chopped = GcodeBackplot.Measure(Classify(many.ToString()));

        Assert.Equal(single.CutMm, chopped.CutMm, 1);
        Assert.True(
            Math.Abs((single.OptimisticTime - chopped.OptimisticTime).TotalMilliseconds) < 50,
            $"feed-only time should not care how the line is chopped up: {single.OptimisticTime} vs {chopped.OptimisticTime}");
        Assert.True(
            chopped.PessimisticTime > single.PessimisticTime * 3,
            $"short moves should cost far more: {chopped.PessimisticTime} vs {single.PessimisticTime}");
    }

    // ------------------------------------------------------------------ what the picture says about depth

    /// <summary>
    /// The cut lines are split by how deep they go, so a tab reads as a gap.
    ///
    /// Reported from the workshop, right after the tabs themselves were fixed: the outline's cut line
    /// stopped showing where the tabs were. Before the fix nothing cut the tab at all, so every pass
    /// jumped the gap and the gap was in the picture by accident. Cutting the tab down properly put a
    /// shallow pass across it, and drawing every pass in one colour let that shallow pass paint over
    /// the gap the deep ones leave.
    ///
    /// So the passes that reach the program's full depth are one layer and the shallower ones another,
    /// dimmed underneath: the bright line is where the cutter goes through, which is exactly the
    /// question "where does this board stay attached" — and nothing is hidden.
    /// </summary>
    [Fact]
    public void TabsShowAsGapsInTheFullDepthCutLine()
    {
        // 30 x 20 mm of 0.8 mm board, cut with tabs: two depth passes plus the tab pass.
        var outline = OutlineOperation.Build(
            [[new(0, 0), new(Mm(30), 0), new(Mm(30), Mm(20)), new(0, Mm(20))]],
            new OutlineOptions
            {
                Tool = Tool.DefaultOutlineMill,
                BoardThicknessNm = Mm(0.8),
                DepthPerPassNm = Mm(0.5),
                TabCount = 4,
                Keep = [],
            });

        var (text, _) = GcodeEmitter.Emit(
            new Job { Name = "tabs", Toolpaths = [outline] }, new GcodeOptions());

        var layers = BackplotBuilder.Build(GcodeBackplot.Classify(GcodeParser.Parse(text)));

        var cut = layers.Single(l => l.Id == "gcode-cut");
        var partial = layers.Single(l => l.Id == "gcode-cut-partial");

        // Four tabs break the deepest passes into four runs each; the pass over the tabs is whole.
        Assert.Equal(0, cut.Runs.Count % 4);
        Assert.All(cut.Runs, r => Assert.NotEqual(r[0], r[^1]));
        Assert.Contains(partial.Runs, r => r[0] == r[^1]);

        // The full-depth line is what is drawn; the shallower passes are there to be turned on, not
        // laid over the board. A ramped program is nearly all part-depth runs, and showing those by
        // default tints the whole picture instead of saying anything.
        Assert.True(cut.VisibleByDefault);
        Assert.False(partial.VisibleByDefault);
    }

    /// <summary>
    /// A program that cuts everything at one depth — isolation, engraving — has no shallower passes
    /// to dim, so its picture is exactly what it was.
    /// </summary>
    [Fact]
    public void OneDepthMeansOneCutLayer()
    {
        var layers = BackplotBuilder.Build(Classify(
            """
            G21 G90
            G1 Z-0.05 F60
            G1 X10 Y0 F200
            G1 X10 Y10
            G0 Z2
            """));

        Assert.Contains(layers, l => l.Id == "gcode-cut");
        Assert.DoesNotContain(layers, l => l.Id == "gcode-cut-partial");
    }

    /// <summary>
    /// A merged program — the single file the Mill button writes — is not split, because isolation at
    /// 0.05 mm is not a part-depth version of an outline that goes through at 0.9 mm.
    /// </summary>
    [Fact]
    public void AMergedProgramIsNotSplitByDepth()
    {
        var moves = Classify(
            """
            G21 G90
            G1 Z-0.05 F60
            G1 X10 Y0 F200
            G0 Z2
            G0 X0 Y0
            G1 Z-0.9 F60
            G1 X10 Y10 F200
            G0 Z2
            """);

        Assert.Contains(BackplotBuilder.Build(moves), l => l.Id == "gcode-cut-partial");
        Assert.DoesNotContain(
            BackplotBuilder.Build(moves, splitByDepth: false), l => l.Id == "gcode-cut-partial");
    }
}
