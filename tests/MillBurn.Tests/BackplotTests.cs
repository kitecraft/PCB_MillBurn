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
}
