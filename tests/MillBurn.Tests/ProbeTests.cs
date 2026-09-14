using MillBurn.Align;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Generating a probing routine, and reading back whatever the sender wrote down.
///
/// The log is the whole interface between this program and the machine — we never open a serial
/// port — so being generous about what a log may look like is not politeness, it is the feature.
/// </summary>
public sealed class ProbeTests(ITestOutputHelper output)
{
    private static Bounds Board(double widthMm, double heightMm) =>
        new(0, 0, Nm.FromMillimetres(widthMm), Nm.FromMillimetres(heightMm));

    private static double Mm(long nm) => nm / (double)Nm.PerMillimetre;

    /// <summary>
    /// Where the probe actually went down: the start of each descent below zero.
    ///
    /// Not simply "every move that goes somewhere", which would count the park move home at the
    /// end as a twenty-fifth touch on a twenty-four point grid.
    /// </summary>
    private static List<Point2> Touches(string text) =>
    [
        .. GcodeParser.Parse(text).Moves
            .Where(m => m.IsVertical && m.ToZNm < 0)
            .Select(m => m.From),
    ];

    // ------------------------------------------------------------------ the routine

    /// <summary>
    /// A probe tip half over the edge of the board reads the table, and the table is 1.6 mm down.
    /// One reading like that drags the whole corner of the map with it.
    /// </summary>
    [Fact]
    public void EveryTouchLandsWellInsideTheBoard()
    {
        var (text, report) = ProbeRoutine.Generate(Board(60, 40));
        var touches = Touches(text);

        output.WriteLine($"{report.Columns} x {report.Rows} = {report.PointCount} points, "
            + $"{report.SpacingMm:F1} mm apart, about {report.EstimatedSeconds / 60:F0} min");

        Assert.Equal(report.PointCount, touches.Count);

        foreach (var touch in touches)
        {
            Assert.InRange(Mm(touch.X), 1 - 1e-6, 59 + 1e-6);
            Assert.InRange(Mm(touch.Y), 1 - 1e-6, 39 + 1e-6);
        }
    }

    /// <summary>
    /// The touches have to cover the board, or the map holds an edge value out over ground nobody
    /// measured — which is the quiet failure this whole feature exists to avoid.
    /// </summary>
    [Fact]
    public void TheGridReachesBothEndsOfTheBoard()
    {
        var touches = Touches(ProbeRoutine.Generate(Board(60, 40)).Text);

        Assert.Equal(1, Mm(touches.Min(t => t.X)), 3);
        Assert.Equal(59, Mm(touches.Max(t => t.X)), 3);
        Assert.Equal(1, Mm(touches.Min(t => t.Y)), 3);
        Assert.Equal(39, Mm(touches.Max(t => t.Y)), 3);
    }

    /// <summary>
    /// Rather than stopping partway across a big board. A grid that covers two thirds of the stock
    /// is worse than a coarse one that covers all of it, because the third it missed is where the
    /// map will be guessing.
    /// </summary>
    [Fact]
    public void ABigBoardGetsCoarserSpacingRatherThanAClippedGrid()
    {
        var (text, report) = ProbeRoutine.Generate(
            Board(300, 200), new ProbeRoutineOptions { SpacingMm = 5, MaxPoints = 120 });

        output.WriteLine($"{report.PointCount} points at {report.SpacingMm:F1} mm");

        Assert.True(report.PointCount <= 120, $"asked for at most 120, planned {report.PointCount}");
        Assert.True(report.SpacingMm > 5, "the spacing should have opened up");
        Assert.Contains(report.Notes, n => n.Contains("opened", StringComparison.Ordinal));

        var touches = Touches(text);
        Assert.Equal(299, Mm(touches.Max(t => t.X)), 3);
        Assert.Equal(199, Mm(touches.Max(t => t.Y)), 3);
    }

    /// <summary>Every other row runs backwards, so the tool never traverses the board to start one.</summary>
    [Fact]
    public void TheGridIsWalkedInSerpentineOrder()
    {
        var (text, report) = ProbeRoutine.Generate(
            Board(50, 30), new ProbeRoutineOptions { SpacingMm = 12 });

        var xs = Touches(text).Select(t => Mm(t.X)).ToList();

        // The first row goes left to right and the second right to left, so the second row's first
        // touch is at the far end rather than back at the start.
        Assert.Equal(xs[report.Columns - 1], xs[report.Columns], 3);
    }

    [Fact]
    public void ItProbesDownAndRetractsBetweenTouches()
    {
        var (text, _) = ProbeRoutine.Generate(Board(30, 20), new ProbeRoutineOptions { SpacingMm = 15 });

        Assert.Contains("G38.2 Z-2.000 F30", text, StringComparison.Ordinal);
        Assert.Contains("M5", text, StringComparison.Ordinal);
        Assert.Contains("G21 G90", text, StringComparison.Ordinal);

        // Nothing travels sideways below the retract height, so the tool is never dragged across
        // the surface it is measuring.
        foreach (var move in GcodeParser.Parse(text).Moves.Where(m => m.MovesInPlane))
        {
            Assert.True(move.FromZNm >= Nm.FromMillimetres(1) - 1, "travelled below the retract height");
        }
    }

    /// <summary>
    /// A real board's Gerber coordinates start wherever it happened to sit on the EDA canvas —
    /// PogoTest1 lands at X150, Y minus 107. Every exported file is shifted to the board's own
    /// corner, and the probing routine has to be shifted with them: a surface measured in one frame
    /// and a toolpath cut in another produce levelling that is pure noise, while both files look
    /// entirely reasonable on their own.
    /// </summary>
    [Fact]
    public void ItProbesInWorkCoordinatesWhereverTheGerbersHappenedToSit()
    {
        var far = new Bounds(
            Nm.FromMillimetres(150), Nm.FromMillimetres(-107),
            Nm.FromMillimetres(190), Nm.FromMillimetres(-77));

        var touches = Touches(ProbeRoutine.Generate(far).Text);

        Assert.Equal(1, Mm(touches.Min(t => t.X)), 3);
        Assert.Equal(1, Mm(touches.Min(t => t.Y)), 3);
        Assert.Equal(39, Mm(touches.Max(t => t.X)), 3);
        Assert.Equal(29, Mm(touches.Max(t => t.Y)), 3);
    }

    /// <summary>
    /// On a blank the header puts work zero on the blank's corner, and says why the border is not
    /// probed — somebody who has just cut a blank will reasonably wonder.
    /// </summary>
    [Fact]
    public void OnABlankTheHeaderSaysWhereZeroIsAndWhyTheBorderIsNotProbed()
    {
        var text = ProbeRoutine.Generate(Board(80, 60), new ProbeRoutineOptions { OnBlank = true }).Text;

        Assert.Contains("STOCK's lower-left corner", text, StringComparison.Ordinal);
        Assert.Contains("not the stock's border", text, StringComparison.Ordinal);
        Assert.DoesNotContain("board's lower-left", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A job on a blank probes the board, in the blank's frame.
    ///
    /// The board, because that is where everything shallow enough to need levelling is cut: the blank
    /// is cut through before anything is probed. The blank's frame, because every program is
    /// referenced to its corner — probed from the board's corner, the map sits a border's width from
    /// where every correction is applied, while both files look entirely reasonable on their own.
    /// </summary>
    [Fact]
    public void AJobOnABlankProbesTheBoardInTheBlanksFrame()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var settings = board.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);

        var job = new JobOptions { Blank = new BlankOptions { Enabled = true } };

        var plan = ExportPlanner.Plan(
            board, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode, job: job);

        var blank = ExportPlanner.BlankFor(board, settings, ToolLibrary.Default, job);

        Assert.True(blank.Resolved);
        Assert.Equal(plan.FrameFor(board.Bounds), blank.Bounds);
        Assert.NotEqual(board.Bounds, blank.Bounds);

        var touches = Touches(ProbeRoutine.Generate(board.Bounds, new ProbeRoutineOptions
        {
            WorkZero = new Point2(blank.Bounds.MinX, blank.Bounds.MinY),
            OnBlank = true,
        }).Text);

        // Where the board sits on the blank, in the programs' coordinates.
        var left = Mm(board.Bounds.MinX - blank.Bounds.MinX);
        var bottom = Mm(board.Bounds.MinY - blank.Bounds.MinY);

        output.WriteLine($"board at {left:F2}, {bottom:F2} on the blank; {touches.Count} touches");

        Assert.True(left > 1 && bottom > 1, "the test needs a border to mean anything");

        Assert.Equal(left + 1, Mm(touches.Min(t => t.X)), 3);
        Assert.Equal(bottom + 1, Mm(touches.Min(t => t.Y)), 3);
        Assert.Equal(left + Mm(board.Bounds.Width) - 1, Mm(touches.Max(t => t.X)), 3);
        Assert.Equal(bottom + Mm(board.Bounds.Height) - 1, Mm(touches.Max(t => t.Y)), 3);
    }

    [Fact]
    public void ATinyBoardStillGetsProbed()
    {
        var (_, report) = ProbeRoutine.Generate(Board(3, 3));

        Assert.Equal(4, report.PointCount);
        Assert.Contains(report.Notes, n => n.Contains("too small", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ reading the log back

    /// <summary>What GRBL actually sends, wrapped in the chatter a sender logs around it.</summary>
    [Fact]
    public void GrblProbeReportsAreFoundInAmongTheChatter()
    {
        var log = ProbeLog.Parse("""
            Grbl 1.1f ['$' for help]
            >>> G38.2 Z-2.000 F30
            [PRB:1.000,1.000,-24.512:1]
            ok
            <Idle|MPos:1.000,1.000,1.000|FS:0,0>
            >>> G38.2 Z-2.000 F30
            [PRB:21.000,1.000,-24.480:1]
            ok
            [PRB:21.000,21.000,-24.503:1]
            """);

        Assert.Equal(ProbeLogFormat.GrblProbeReports, log.Format);
        Assert.Equal(3, log.Samples.Count);
        Assert.Equal(-24.512, Mm(log.Samples[0].ZNm), 3);
        Assert.Equal(21, Mm(log.Samples[2].At.X), 3);
    }

    /// <summary>
    /// A trailing <c>:0</c> means the probe ran to the end of its travel without touching anything.
    /// The position it reports is where it gave up, which is not a surface height — and keeping it
    /// would put a false reading exactly where the measurement failed.
    /// </summary>
    [Fact]
    public void AProbeThatNeverTouchedIsThrownAway()
    {
        var log = ProbeLog.Parse("""
            [PRB:0.000,0.000,-24.500:1]
            [PRB:20.000,0.000,-26.500:0]
            [PRB:40.000,0.000,-24.480:1]
            """);

        Assert.Equal(2, log.Samples.Count);
        Assert.Equal(1, log.FailedProbes);
        Assert.Contains(log.Notes, n => n.Contains("never touched", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Samples, s => Mm(s.ZNm) < -25);
    }

    [Theory]
    [InlineData("0,0,0.01\n20,0,-0.03\n0,20,0.02")]
    [InlineData("0\t0\t0.01\n20\t0\t-0.03\n0\t20\t0.02")]
    [InlineData("0 0 0.01\n20 0 -0.03\n0 20 0.02")]
    [InlineData("0;0;0.01\n20;0;-0.03\n0;20;0.02")]
    public void PlainTriplesAreReadHoweverTheyArePunctuated(string text)
    {
        var log = ProbeLog.Parse(text);

        Assert.Equal(ProbeLogFormat.Triples, log.Format);
        Assert.Equal(3, log.Samples.Count);
        Assert.Equal(0.01, Mm(log.Samples[0].ZNm), 4);
        Assert.Equal(-0.03, Mm(log.Samples[1].ZNm), 4);
    }

    [Fact]
    public void AHeaderRowAndCommentsAreNotAnError()
    {
        var log = ProbeLog.Parse("""
            # probed 2026-09-09, 1.5 mm ball probe
            x,y,z
            0,0,0.010
            20,0,-0.030
            0,20,0.020
            """);

        Assert.Equal(3, log.Samples.Count);
        Assert.Equal(1, log.UnreadableLines);
        Assert.Empty(log.Notes);
    }

    [Fact]
    public void SomethingThatIsNotALogIsEmptyRatherThanAnException()
    {
        var log = ProbeLog.Parse("the quick brown fox\njumped over\nthe lazy dog");

        Assert.True(log.IsEmpty);
        Assert.Equal(ProbeLogFormat.Unknown, log.Format);

        var (map, _) = ProbeLog.Read("nothing here");
        Assert.Null(map);
    }

    /// <summary>The round trip that matters: probe a board, read the log, get a usable surface.</summary>
    [Fact]
    public void ALogReadsStraightIntoAMap()
    {
        var (map, log) = ProbeLog.Read("""
            [PRB:0.000,0.000,-24.500:1]
            [PRB:40.000,0.000,-24.560:1]
            [PRB:0.000,30.000,-24.470:1]
            [PRB:40.000,30.000,-24.520:1]
            [PRB:20.000,15.000,-24.610:1]
            """);

        Assert.NotNull(map);
        Assert.Equal(5, log.Samples.Count);

        // Machine coordinates on the way in, a surface through zero on the way out.
        Assert.Equal(0, Mm(map.SampleNm(Point2.Origin)), 5);
        Assert.Equal(0.140, map.RangeMm, 3);
    }
}
