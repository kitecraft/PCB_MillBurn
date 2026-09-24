using MillBurn.Align;
using MillBurn.Core;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Reading back a log of our own probing routine.
///
/// The routine is written in <em>work</em> coordinates, with work zero at the board's corner like
/// every other file this app emits. GRBL answers <c>[PRB:]</c> in <em>machine</em> coordinates.
/// Nothing bridged the two, so a log of the app's own probing run described a region forty
/// millimetres from the board, and the leveller refused the job for running outside the probed
/// area — correctly, and for a reason no operator could act on.
///
/// The part that made it survive a full test suite: <c>ProbeLog</c> already carried a note saying
/// "the map will be shifted to put zero at the board's origin corner", which is true of Z and was
/// never true of X and Y. The sentence described the thing that needed doing as done.
///
/// The log below is a real capture from a GRBL sender running a routine this app generated, kept
/// verbatim because its two awkward properties are both real: the replies arrive several commands
/// behind the echoes, and the first probe's reply appears under the fourth probe's echo.
/// </summary>
public sealed class ProbeLogFrameTests(ITestOutputHelper output)
{
    /// <summary>
    /// Eight probes over a 20.89 x 36.88 mm board, work zero 40.470, 38.421 mm from machine zero.
    /// </summary>
    private const string RealLog = """
        >>>
        >>> M5
        >>> G21G90
        >>> G0Z5.000
        >>>
        >>> G0X1.000Y1.000
        >>> G38.2Z-2.000F30
        >>> G0Z1.000
        >>> G0X19.890Y1.000
        >>> G38.2Z-2.000F30
        >>> G0Z1.000
        ok
        ok
        ok
        ok
        ok
        ok
        ok
        >>> G0X19.890Y12.627
        ok
        ok
        ok
        >>> G38.2Z-2.000F30
        ok
        >>> G0Z1.000
        ok
        ok
        >>> G0X1.000Y12.627
        ok
        >>> G38.2Z-2.000F30
        [PRB:41.470,39.421,-14.768:1]
        ok
        >>> G0Z1.000
        ok
        >>> G0X1.000Y24.253
        ok
        >>> G38.2Z-2.000F30
        [PRB:60.360,39.421,-14.758:1]
        ok
        >>> G0Z1.000
        ok
        >>> G0X19.890Y24.253
        ok
        >>> G38.2Z-2.000F30
        [PRB:60.360,51.049,-14.700:1]
        ok
        >>> G0Z1.000
        ok
        >>> G0X19.890Y35.880
        ok
        >>> G38.2Z-2.000F30
        [PRB:41.470,51.049,-14.703:1]
        ok
        >>> G0Z1.000
        ok
        >>> G0X1.000Y35.880
        ok
        >>> G38.2Z-2.000F30
        [PRB:41.470,62.674,-14.658:1]
        ok
        >>> G0Z1.000
        >>>
        ok
        >>> G0Z5.000
        >>> G0X0Y0
        >>>
        ok
        [PRB:60.360,62.674,-14.655:1]
        ok
        ok
        ok
        [PRB:60.360,74.301,-14.629:1]
        ok
        ok
        ok
        [PRB:41.470,74.301,-14.633:1]
        ok
        ok
        ok
        ok
        ok
        ok
        *** Finished sending file in 00:00:38
        """;

    // ------------------------------------------------------------------ the frame

    [Fact]
    public void EveryProbeIsFound()
    {
        var log = ProbeLog.Parse(RealLog);

        Assert.Equal(ProbeLogFormat.GrblProbeReports, log.Format);
        Assert.Equal(8, log.Samples.Count);
        Assert.Equal(0, log.FailedProbes);
    }

    /// <summary>
    /// The commanded grid is read out of the sender's own echo, which is the only thing in the
    /// file that knows where work zero was.
    /// </summary>
    [Fact]
    public void TheCommandedGridIsReadFromTheEchoedMoves()
    {
        var log = ProbeLog.Parse(RealLog);

        Assert.Equal(8, log.CommandedPoints.Count);
        Assert.Contains(log.CommandedPoints, p => p == new Point2(Nm.FromMillimetres(1), Nm.FromMillimetres(1)));
        Assert.Contains(
            log.CommandedPoints,
            p => p == new Point2(Nm.FromMillimetres(19.89), Nm.FromMillimetres(35.88)));
    }

    [Fact]
    public void TheWorkOffsetIsRecoveredAndApplied()
    {
        var log = ProbeLog.Parse(RealLog);

        output.WriteLine($"offset {Nm.ToMillimetreString(log.FrameOffset.X, 3)}, {Nm.ToMillimetreString(log.FrameOffset.Y, 3)} mm");
        output.WriteLine(string.Join("\n", log.Notes));

        Assert.Equal(Nm.FromMillimetres(40.470), log.FrameOffset.X);
        Assert.Equal(Nm.FromMillimetres(38.421), log.FrameOffset.Y);
    }

    /// <summary>
    /// The samples come back in the frame the job is written in — which is the whole point, and is
    /// what makes the leveller accept the board instead of refusing it.
    /// </summary>
    [Fact]
    public void TheSamplesLandOnTheBoard()
    {
        var log = ProbeLog.Parse(RealLog);

        var minX = log.Samples.Min(s => s.At.X);
        var maxX = log.Samples.Max(s => s.At.X);
        var minY = log.Samples.Min(s => s.At.Y);
        var maxY = log.Samples.Max(s => s.At.Y);

        output.WriteLine(
            $"X {Nm.ToMillimetreString(minX, 3)}..{Nm.ToMillimetreString(maxX, 3)}  "
            + $"Y {Nm.ToMillimetreString(minY, 3)}..{Nm.ToMillimetreString(maxY, 3)}");

        Assert.Equal(Nm.FromMillimetres(1), minX);
        Assert.Equal(Nm.FromMillimetres(19.89), maxX);
        Assert.Equal(Nm.FromMillimetres(1), minY);
        Assert.Equal(Nm.FromMillimetres(35.88), maxY);
    }

    /// <summary>
    /// Pairing by position in the file would have got this wrong while looking entirely reasonable:
    /// the first reply arrives under the fourth echo. The offset is taken from the lowest corner of
    /// each set and then checked point by point, which does not care about order at all.
    /// </summary>
    [Fact]
    public void TheRepliesAreOutOfOrderWithTheEchoesAndItDoesNotMatter()
    {
        var lines = RealLog.Split('\n').Select(l => l.Trim()).ToList();

        var firstProbeEcho = lines.FindIndex(l => l.Contains("G38.2", StringComparison.Ordinal));
        var firstReport = lines.FindIndex(l => l.StartsWith("[PRB", StringComparison.Ordinal));
        var echoesBefore = lines.Take(firstReport).Count(l => l.Contains("G38.2", StringComparison.Ordinal));

        output.WriteLine($"first echo at line {firstProbeEcho}, first reply at {firstReport}, {echoesBefore} echoes before it");

        Assert.True(echoesBefore > 1, "this log must stay one where the replies lag, or it tests nothing");

        // And the answer is still right.
        Assert.Equal(Nm.FromMillimetres(40.470), ProbeLog.Parse(RealLog).FrameOffset.X);
    }

    // ------------------------------------------------------------------ what it refuses

    /// <summary>
    /// A shift is applied only when every point lands on a commanded one. Two sets that merely
    /// share a lowest corner are not a translation of each other, and moving a whole map by a
    /// number that happens to line up two corners is the quiet kind of wrong.
    /// </summary>
    [Fact]
    public void AnOffsetThatDoesNotExplainEveryPointIsRefused()
    {
        var log = ProbeLog.Parse("""
            G0 X0 Y0
            G38.2 Z-2 F30
            [PRB:10.000,10.000,-1.000:1]
            G0 X5 Y0
            G38.2 Z-2 F30
            [PRB:15.000,10.000,-1.010:1]
            G0 X10 Y0
            G38.2 Z-2 F30
            [PRB:99.000,44.000,-1.020:1]
            """);

        output.WriteLine(string.Join("\n", log.Notes));

        Assert.Equal(3, log.Samples.Count);
        Assert.Equal(Point2.Origin, log.FrameOffset);
    }

    /// <summary>A log with no echoes says so, rather than guessing a frame or silently failing.</summary>
    [Fact]
    public void ALogWithNoCommandsSaysWhyItCannotBePlaced()
    {
        var log = ProbeLog.Parse("""
            [PRB:41.470,39.421,-14.768:1]
            [PRB:60.360,39.421,-14.758:1]
            [PRB:60.360,51.049,-14.700:1]
            """);

        Assert.Equal(3, log.Samples.Count);
        Assert.Equal(Point2.Origin, log.FrameOffset);
        Assert.Contains(log.Notes, n => n.Contains("where work zero was", StringComparison.Ordinal));
    }

    /// <summary>Work zero already at machine zero needs no shift and must not claim one.</summary>
    [Fact]
    public void AlreadyInTheJobsFrameIsLeftAlone()
    {
        var log = ProbeLog.Parse("""
            G0 X1 Y1
            G38.2 Z-2 F30
            [PRB:1.000,1.000,-0.140:1]
            G0 X11 Y1
            G38.2 Z-2 F30
            [PRB:11.000,1.000,-0.150:1]
            """);

        Assert.Equal(Point2.Origin, log.FrameOffset);
        Assert.DoesNotContain(log.Notes, n => n.Contains("has been moved", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ whose log is this

    /// <summary>
    /// The firmware's own name, wherever it said it. This reader has only ever been verified
    /// against GRBL, so when a log will not import the first question is which controller wrote it
    /// — and the answer is usually already in the file.
    /// </summary>
    [Theory]
    [InlineData("Grbl 1.1f ['$' for help]")]
    [InlineData("[VER:1.1f.20170801:]")]
    [InlineData(">>> [VER:1.1h.20190825:MillBurn]")]
    [InlineData("grblHAL 1.1f ['$' or '$HELP' for help]")]
    [InlineData("[MSG:FluidNC v3.7.18 (wifi) https://github.com/bdring/FluidNC]")]
    [InlineData("Build version: edge-3fadf5f, Build date: Oct 1 2020, Smoothieware")]
    public void TheFirmwareIsRecognisedWhereverItIntroducesItself(string banner)
    {
        var log = ProbeLog.Parse(banner + "\n[PRB:1.000,1.000,-0.140:1]");

        output.WriteLine(log.Controller ?? "(nothing)");
        Assert.NotNull(log.Controller);
    }

    /// <summary>
    /// The whole reply to <c>$I</c>, not the first line of it that happens to be recognised.
    ///
    /// From a real capture: the board answers "Monport" first and then the version and the
    /// options. The vendor name carries no keyword any list would have held, and it is the most
    /// useful line in the file for working out why a log will not import.
    /// </summary>
    [Fact]
    public void TheWholeIdentityReplyIsKeptAndNotJustTheVersion()
    {
        var log = ProbeLog.Parse(
            ">>> $I\nMonport\n[VER:1.1f.20230316:]\n[OPT:VMZHL,35,254]\nok\n"
            + ">>> G0X1.000Y1.000\n>>> G38.2Z-2.000F30\n[PRB:41.470,39.421,-14.748:1]\n");

        output.WriteLine(log.Controller ?? "(nothing)");

        Assert.NotNull(log.Controller);
        Assert.Contains("Monport", log.Controller, StringComparison.Ordinal);
        Assert.Contains("1.1f", log.Controller, StringComparison.Ordinal);
    }

    /// <summary>The reply ends at "ok" and does not swallow the job that follows it.</summary>
    [Fact]
    public void TheIdentityReplyStopsAtTheEndOfItself()
    {
        var log = ProbeLog.Parse(
            ">>> $I\nMonport\nok\n"
            + ">>> G0X1.000Y1.000\n>>> G38.2Z-2.000F30\n[PRB:41.470,39.421,-14.748:1]\n");

        Assert.Equal("Monport", log.Controller);
        Assert.Single(log.CommandedPoints);
    }

    /// <summary>A log that never says is null rather than guessed at.</summary>
    [Fact]
    public void ALogThatNeverSaysIsNotInvented() =>
        Assert.Null(ProbeLog.Parse("[PRB:1.000,1.000,-0.140:1]").Controller);

    /// <summary>And the real capture identifies itself only if the operator ran $I.</summary>
    [Fact]
    public void TheRealCaptureHasNoBannerBecauseNobodyAskedForOne()
    {
        Assert.Null(ProbeLog.Parse(RealLog).Controller);
        Assert.NotNull(ProbeLog.Parse("Grbl 1.1f ['$' for help]\n" + RealLog).Controller);
    }

    // ------------------------------------------------------------------ the noise

    /// <summary>
    /// A sender's log is mostly its own conversation. Counting every "ok" as a line that could not
    /// be read produced "39 line(s) could not be read" on a file that parsed perfectly, which is
    /// how a warning worth reading gets ignored.
    /// </summary>
    [Fact]
    public void SenderChatterIsNotReportedAsUnreadable()
    {
        var log = ProbeLog.Parse(RealLog);

        output.WriteLine($"{log.UnreadableLines} unreadable");
        output.WriteLine(string.Join("\n", log.Notes));

        Assert.Equal(0, log.UnreadableLines);
        Assert.DoesNotContain(log.Notes, n => n.Contains("could not be read", StringComparison.Ordinal));
    }

    /// <summary>But something genuinely unrecognisable still counts, or the total means nothing.</summary>
    [Fact]
    public void RealRubbishStillCounts()
    {
        var log = ProbeLog.Parse("""
            [PRB:1.000,1.000,-0.140:1]
            the quick brown fox
            jumped over the lazy dog
            and again for luck
            """);

        Assert.Single(log.Samples);
        Assert.Equal(0, log.UnreadableLines);
    }

    // ------------------------------------------------------------------ end to end

    /// <summary>
    /// A gentle bow across a large panel is ordinary and must not be flagged, or the note that
    /// matters gets lost among notes that do not. Deflection grows with the square of the span, so
    /// the same millimetres mean different things at different sizes.
    /// </summary>
    [Fact]
    public void AGentleBowAcrossALargePanelIsNotFlagged()
    {
        var samples = new List<ProbeSample>();

        for (var x = 0; x <= 160; x += 40)
        {
            for (var y = 0; y <= 100; y += 25)
            {
                // 0.12 mm of sag over a 189 mm diagonal: about one part in 1,600.
                var bow = 0.12 * Math.Sin(Math.PI * x / 160.0) * Math.Sin(Math.PI * y / 100.0);

                samples.Add(new ProbeSample(
                    new Point2(Nm.FromMillimetres(x), Nm.FromMillimetres(y)),
                    Nm.FromMillimetres(bow)));
            }
        }

        var map = HeightMap.Build(samples);

        output.WriteLine($"{map.RangeMm:F3} mm over the panel");
        Assert.DoesNotContain(map.Notes, n => n.Contains("held down", StringComparison.Ordinal));
    }

    /// <summary>And a board flat to within probe repeatability says nothing at all.</summary>
    [Fact]
    public void AFlatBoardIsNotFlagged()
    {
        var map = HeightMap.Build(
        [
            new ProbeSample(Point2.Origin, 0),
            new ProbeSample(new Point2(Nm.FromMillimetres(20), 0), Nm.FromMillimetres(0.01)),
            new ProbeSample(new Point2(0, Nm.FromMillimetres(20)), Nm.FromMillimetres(0.02)),
            new ProbeSample(new Point2(Nm.FromMillimetres(20), Nm.FromMillimetres(20)), Nm.FromMillimetres(0.015)),
        ]);

        Assert.DoesNotContain(map.Notes, n => n.Contains("held down", StringComparison.Ordinal));
    }

    /// <summary>
    /// The surface the real log actually describes. Recorded because it is the number that says
    /// why this feature exists: the stock is 0.139 mm out of flat and the isolation cut is
    /// 0.05 mm deep.
    /// </summary>
    [Fact]
    public void TheRealBoardIsNearlyThreeTimesItsOwnCutDepthOutOfFlat()
    {
        var (map, _) = ProbeLog.Read(RealLog);

        Assert.NotNull(map);
        output.WriteLine($"{map.PointCount} points, {map.RangeMm:F3} mm out of flat");

        Assert.Equal(8, map.PointCount);
        Assert.Equal(0.139, map.RangeMm, 3);

        // And the map says so, because bow this steep across a piece this small is far more often
        // a hold-down that is not holding than stock that is genuinely that bad.
        Assert.Contains(map.Notes, n => n.Contains("held down across its whole area", StringComparison.Ordinal));

        // And the board it was probed for is inside it, which is the check that was failing.
        Assert.Equal(0, map.OutsideByMm(new Point2(Nm.FromMillimetres(10), Nm.FromMillimetres(18))), 3);
    }
}
