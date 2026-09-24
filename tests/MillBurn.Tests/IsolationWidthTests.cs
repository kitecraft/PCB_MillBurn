using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// How wide the moat is, rather than how many laps it takes.
///
/// Isolation cut one lap and stopped. With a 30° V-bit at 0.05 mm deep that is 0.127 mm — which
/// separates the nets and is also a gap you cannot see, cannot solder across without bridging, and
/// can close by handling the board. There has been a pass count since Phase 2, but a pass count is
/// a question about the machine; how wide the gap is, is the question about the board.
///
/// So the width is typed and the passes are derived, for the same reason the V-bit's cut width is
/// derived from its depth rather than typed: it is a consequence, and making somebody compute a
/// consequence in their head is how they get it wrong.
/// </summary>
public sealed class IsolationWidthTests(ITestOutputHelper output)
{
    private static readonly long Cut = Nm.FromMillimetres(0.127);

    // ------------------------------------------------------------------ the arithmetic

    /// <summary>
    /// Passes overlap by 15 %, so each one past the first widens the moat by 0.85 of a cut rather
    /// than a whole one. Stepping by the full width would leave a hairline ridge between passes
    /// wherever the machine is a few microns out, and a ridge of copper in an isolation moat is a
    /// short.
    /// </summary>
    [Theory]
    [InlineData(1, 0.127)]
    [InlineData(2, 0.235)]
    [InlineData(3, 0.343)]
    [InlineData(4, 0.451)]
    [InlineData(6, 0.667)]
    public void EachPassPastTheFirstAddsOneStepNotOneWidth(int passes, double expectedMm)
    {
        var cleared = IsolationOptions.ClearedBy(passes, Cut, 0.15, 0);

        output.WriteLine($"{passes} passes -> {Nm.ToMillimetreString(cleared, 3)} mm");
        Assert.Equal(expectedMm, Nm.ToMillimetres(cleared), 3);
    }

    /// <summary>Whole laps only, so what you get is the width asked for rounded up.</summary>
    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(0.05, 1)]
    [InlineData(0.127, 1)]
    [InlineData(0.13, 2)]
    [InlineData(0.4, 4)]
    [InlineData(0.45, 4)]

    // Four passes of a round 0.127 mm clear 0.45085 mm, so asking for 0.451 needs a fifth. Pinned
    // because the boundary is where an off-by-one hides, and because 0.127 is itself the rounded
    // face of 0.126795 — the real bit clears a little less, and a test that reads a displayed
    // figure back as an exact one would be testing the formatter.
    [InlineData(0.451, 5)]
    [InlineData(1.0, 10)]
    public void ThePassCountIsWhatTheWidthCosts(double widthMm, int expected)
    {
        var passes = IsolationOptions.PassesFor(Nm.FromMillimetres(widthMm), Cut, 0.15, 0);

        output.WriteLine($"{widthMm:F3} mm -> {passes} passes");
        Assert.Equal(expected, passes);
    }

    /// <summary>Never less than the width asked for. Rounding the wrong way leaves a short.</summary>
    [Theory]
    [InlineData(0.2)]
    [InlineData(0.3)]
    [InlineData(0.4)]
    [InlineData(0.75)]
    [InlineData(1.2)]
    public void WhatIsClearedIsNeverLessThanWhatWasAsked(double widthMm)
    {
        var asked = Nm.FromMillimetres(widthMm);
        var passes = IsolationOptions.PassesFor(asked, Cut, 0.15, 0);

        Assert.True(IsolationOptions.ClearedBy(passes, Cut, 0.15, 0) >= asked);
    }

    /// <summary>
    /// A wider bit reaches the same moat in fewer passes. Obvious, and it is the reason the pass
    /// count cannot be stored: the same project on a different bit means a different number.
    /// </summary>
    [Fact]
    public void AWiderBitNeedsFewerPassesForTheSameMoat()
    {
        var narrow = IsolationOptions.PassesFor(Nm.FromMillimetres(0.5), Cut, 0.15, 0);
        var wide = IsolationOptions.PassesFor(Nm.FromMillimetres(0.5), Nm.FromMillimetres(0.258), 0.15, 0);

        output.WriteLine($"30° bit {narrow} passes, 60° bit {wide} passes");
        Assert.True(wide < narrow);
    }

    /// <summary>An absurd width is capped rather than allowed to generate thousands of laps.</summary>
    [Fact]
    public void AnAbsurdWidthIsCapped() =>
        Assert.Equal(
            IsolationOptions.MaxPasses,
            IsolationOptions.PassesFor(Nm.FromMillimetres(500), Cut, 0.15, 0));

    // ------------------------------------------------------------------ what it cuts

    private static Toolpath Isolate(long widthNm) => IsolationOperation.Build(
        BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1))
            .Layers.First(l => l.Role == LayerRole.TopCopper).Area,
        new IsolationOptions { WidthNm = widthNm });

    /// <summary>
    /// The point of the whole thing: asking for a wider moat cuts more copper away. Measured on a
    /// real board rather than asserted about the options record, because the geometry is where a
    /// pass could quietly produce nothing.
    /// </summary>
    [Fact]
    public void AWiderMoatCutsMoreCopperAway()
    {
        var one = Isolate(0).CutLengthMm;
        var wide = Isolate(Nm.FromMillimetres(0.4)).CutLengthMm;

        output.WriteLine($"1 lap {one:F0} mm, 0.4 mm moat {wide:F0} mm ({wide / one:F2}x)");

        // More than double and less than the naive four times, and the upper bound is the
        // interesting half: passes do not simply multiply, because once a narrow field is fully
        // cleared its contours stop being produced at all.
        Assert.True(wide > one * 2, "a 0.4 mm moat should cost far more than one lap");
        Assert.True(wide < one * 4, "four passes should cost less than four laps, because contours merge");
    }

    /// <summary>
    /// Zero means one lap, which is what every project saved before this existed asked for. A
    /// setting that silently widened the isolation on a board somebody had already cut once would
    /// be the wrong kind of improvement.
    /// </summary>
    [Fact]
    public void ZeroWidthIsTheOldSingleLap()
    {
        Assert.Equal(1, new IsolationOptions().PassCount);
        Assert.Equal(3, new IsolationOptions { Passes = 3 }.PassCount);

        // And an explicit width wins over a stored pass count, because it is the better question.
        Assert.Equal(
            4,
            new IsolationOptions { Passes = 3, WidthNm = Nm.FromMillimetres(0.4) }.PassCount);
    }

    // ------------------------------------------------------------------ end to end

    private static ExportItem Plan(long widthNm)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
                IsolationWidthNm = widthNm,
            },
            StringComparer.Ordinal);

        return ExportPlanner.Plan(
                loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode)
            .Items.First(i => i.Operation == OperationKind.Isolation);
    }

    /// <summary>
    /// The summary leads with the moat, because that is what the board ends up looking like. The
    /// cut width and the pass count follow as the arithmetic that got there.
    /// </summary>
    [Fact]
    public void TheSummarySaysWhatTheBoardEndsUpWith()
    {
        var item = Plan(Nm.FromMillimetres(0.4));

        output.WriteLine(string.Join("\n", item.Summary));

        Assert.Contains(
            item.Summary,
            s => s.Contains("0.450 mm isolated", StringComparison.Ordinal)
                && s.Contains("4 passes", StringComparison.Ordinal));
    }

    /// <summary>And the program that comes out is longer, not just the description of it.</summary>
    [Fact]
    public void TheEmittedProgramIsLongerForAWiderMoat()
    {
        var one = GcodeBackplot.Measure(GcodeBackplot.Classify(GcodeParser.Parse(Plan(0).Content)));
        var wide = GcodeBackplot.Measure(
            GcodeBackplot.Classify(GcodeParser.Parse(Plan(Nm.FromMillimetres(0.4)).Content)));

        output.WriteLine($"1 lap {one.CutMm:F0} mm cut, 0.4 mm moat {wide.CutMm:F0} mm cut");

        Assert.True(wide.CutMm > one.CutMm * 2);
    }

    // ------------------------------------------------------------------ settings

    [Fact]
    public void TheDefaultForAFreshBoardIsFourHundredMicrons() =>
        Assert.Equal(0.4, new MillingDefaults().IsolationWidthMm);

    [Fact]
    public void ItSurvivesSaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), "millburn-mill-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            new AppSettings { Milling = new MillingDefaults { IsolationWidthMm = 0.65 } }.Save(path);

            Assert.Equal(0.65, AppSettings.LoadOrDefault(path).Milling.IsolationWidthMm);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A settings file written before this existed loads with the new default.</summary>
    [Fact]
    public void ASettingsFileFromBeforeThisExistedGetsTheDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), "millburn-old-mill-" + Guid.NewGuid().ToString("N") + ".json");

        File.WriteAllText(path, """{ "SchemaVersion": 1, "BoardThicknessMm": 1.6 }""");

        try
        {
            Assert.Equal(0.4, AppSettings.LoadOrDefault(path).Milling.IsolationWidthMm);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(-1.0, "cannot be negative")]
    [InlineData(9.0, "most of the copper")]
    public void NonsenseWidthsAreRefused(double widthMm, string expected) =>
        Assert.Contains(
            SettingsCheck.Problems(
                new MachineSettings(), new DryRunSettings(), new ProbeSettings(), new LevelSettings(),
                new MillingDefaults { IsolationWidthMm = widthMm }),
            p => p.Contains(expected, StringComparison.Ordinal));
}
