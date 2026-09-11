using MillBurn.Align;

namespace MillBurn.Tests;

/// <summary>
/// Telling a probe log from a file that merely sits in the same folder.
///
/// The reader is forgiving on purpose — three numbers a line is a probe log, even one typed by
/// hand — and it used to answer a file it could make nothing of with a quiet null. In the app that
/// looked like success: nothing changed on screen, the menu items stayed as they were, and any map
/// imported earlier was still in force. So the interesting cases here are not the obvious rubbish
/// but the near misses: a G-code program, a page of notes with a number on it, a run of probes
/// that all failed. Each has to come back with a sentence saying which one it was.
/// </summary>
public sealed class ProbeLogRejectionTests
{
    [Fact]
    public void ProseIsNotAProbeLog()
    {
        var log = ProbeLog.Parse("This is not a real probe test results file....");

        Assert.NotNull(log.Rejection);
        Assert.Contains("Nothing in this file is probe data", log.Rejection, StringComparison.Ordinal);
        Assert.Equal(1, log.LinesRead);
    }

    [Fact]
    public void GcodeIsNotAProbeLog()
    {
        var program = string.Join(
            '\n',
            "G21 G90",
            "G0 Z5.000",
            "G0 X1.000 Y1.000",
            "G38.2 Z-2.000 F60",
            "G0 Z5.000",
            "G0 X11.000 Y1.000",
            "M30");

        var log = ProbeLog.Parse(program);

        // The moves are read — that is how the frame offset is recovered — but a program that was
        // never run has no answers in it, and answers are the whole point.
        Assert.NotEmpty(log.CommandedPoints);
        Assert.NotNull(log.Rejection);
    }

    [Fact]
    public void NotesWithOneNumberInThemAreNotAProbeLog()
    {
        var text = string.Join(
            '\n',
            "notes from the shop",
            "probe the board",
            "1 2 3",
            "remember to clamp it",
            "the bit was blunt");

        var log = ProbeLog.Parse(text);

        Assert.Single(log.Samples);
        Assert.NotNull(log.Rejection);
        Assert.Contains("Only 1 of 5", log.Rejection, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryProbeFailingIsSaidPlainly()
    {
        var text = string.Join(
            '\n',
            "[PRB:10.000,10.000,-25.000:0]",
            "[PRB:20.000,10.000,-25.000:0]",
            "[PRB:30.000,10.000,-25.000:0]");

        var log = ProbeLog.Parse(text);

        Assert.Equal(3, log.FailedProbes);
        Assert.NotNull(log.Rejection);
        Assert.Contains("failed to touch the surface", log.Rejection, StringComparison.Ordinal);
    }

    [Fact]
    public void ProbingOneSpotRepeatedlyIsNotASurface()
    {
        var text = string.Join('\n', "5,5,-0.01", "5,5,-0.02", "5,5,-0.03");

        var log = ProbeLog.Parse(text);

        Assert.NotNull(log.Rejection);
        Assert.Contains("same X and Y", log.Rejection, StringComparison.Ordinal);
    }

    [Fact]
    public void AZSpanNoBoardCouldHaveIsRefused()
    {
        var text = string.Join('\n', "0,0,-0.01", "10,0,-0.02", "0,10,4.0", "10,10,-30.0");

        var log = ProbeLog.Parse(text);

        Assert.NotNull(log.Rejection);
        Assert.Contains("34.0 mm", log.Rejection, StringComparison.Ordinal);
    }

    [Fact]
    public void ARealLogIsNotRefused()
    {
        var text = string.Join(
            '\n',
            "X,Y,Z",
            "0,0,-0.010",
            "10,0,-0.032",
            "20,0,-0.028",
            "0,10,0.005",
            "10,10,-0.011",
            "20,10,-0.019");

        var (map, log) = ProbeLog.Read(text);

        Assert.Null(log.Rejection);
        Assert.NotNull(map);
        Assert.Equal(6, map.PointCount);
    }

    [Fact]
    public void AHandTypedRowOfThreeIsStillAcceptedAsATilt()
    {
        var (map, log) = ProbeLog.Read("0 0 0.00\n30 0 0.04");

        Assert.Null(log.Rejection);
        Assert.NotNull(map);
        Assert.Equal(HeightMapFit.Plane, map.Fit);
    }

    [Fact]
    public void ReadRefusesToBuildAMapFromARejectedFile()
    {
        var (map, log) = ProbeLog.Read("nothing here but words");

        Assert.Null(map);
        Assert.NotNull(log.Rejection);
    }
}
