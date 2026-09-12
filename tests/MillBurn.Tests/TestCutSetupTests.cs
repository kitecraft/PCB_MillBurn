using System.Collections.Immutable;
using MillBurn.Core;
using MillBurn.Gcode;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Finishing a test cut in a second visit with the numbers it was started with.
///
/// A coupon that gets probed first is written in one visit and cut in another, and the dialog
/// closes in between. Retyping the settings from memory is not merely annoying: a depth series
/// whose two halves disagree about the line spacing, or the bit, measures nothing at all. So the
/// settings are written beside the program.
/// </summary>
public sealed class TestCutSetupTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "millburn-testcut-" + Guid.NewGuid().ToString("N")[..8]);

    private static TestCutOptions Custom(Tool tool) => new()
    {
        Tool = tool,
        Kind = TestCutKind.Feed,
        LineCount = 9,
        LineLengthMm = 22,
        LineSpacingMm = 3,
        StartDepthMm = 0.03,
        DepthStepMm = 0.015,
        DepthMm = 0.07,
        FeedStepMmPerMin = 75,
        RepeatFirstLine = false,
    };

    private string Path_(string name) => Path.Combine(_folder, name);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void EveryChoiceComesBack()
    {
        var options = Custom(Tool.DefaultVBit);
        var file = Path_("t" + TestCutSetup.Extension);

        TestCutSetup.From(options, probeWritten: true, "t.nc").Save(file);

        var back = TestCutSetup.Load(file);

        Assert.NotNull(back);
        Assert.Equal(TestCutKind.Feed, back.Kind);
        Assert.Equal(9, back.LineCount);
        Assert.Equal(22, back.LineLengthMm);
        Assert.Equal(3, back.LineSpacingMm);
        Assert.Equal(0.03, back.StartDepthMm);
        Assert.Equal(0.015, back.DepthStepMm);
        Assert.Equal(0.07, back.DepthMm);
        Assert.Equal(75, back.FeedStepMmPerMin);
        Assert.False(back.RepeatFirstLine);
        Assert.True(back.ProbeWritten);
        Assert.Equal("t.nc", back.Program);
    }

    /// <summary>
    /// The bit is found by id first and by name second, because the two fail differently: an id
    /// survives a rename, a name survives a library rebuilt from scratch.
    /// </summary>
    [Fact]
    public void TheBitIsFoundByIdOrByName()
    {
        var file = Path_("t" + TestCutSetup.Extension);
        TestCutSetup.From(Custom(Tool.DefaultVBit), probeWritten: false).Save(file);

        var saved = TestCutSetup.Load(file);
        Assert.NotNull(saved);

        var renamed = Tool.DefaultVBit with { Name = "the one in the spindle" };
        Assert.Equal(
            renamed.Id,
            saved.ToolIn(new ToolLibrary { Tools = [renamed] })?.Id);

        var reissued = Tool.DefaultVBit with { Id = Guid.NewGuid() };
        Assert.Equal(
            reissued.Id,
            saved.ToolIn(new ToolLibrary { Tools = [reissued] })?.Id);
    }

    /// <summary>
    /// A bit that is gone must come back as nothing, not as something near enough. A depth series
    /// measured with a different cone is a page of numbers about a different tool.
    /// </summary>
    [Fact]
    public void ABitThatIsGoneIsNotSubstituted()
    {
        var file = Path_("t" + TestCutSetup.Extension);
        TestCutSetup.From(Custom(Tool.DefaultVBit), probeWritten: false).Save(file);

        var saved = TestCutSetup.Load(file);
        Assert.NotNull(saved);

        var others = new ToolLibrary
        {
            Tools = ImmutableArray.Create(Tool.DefaultOutlineMill, Tool.DefaultDrill),
        };

        Assert.Null(saved.ToolIn(others));
    }

    [Fact]
    public void ItSitsBesideTheProgram() => Assert.Equal(
        "depth-test" + TestCutSetup.Extension,
        Path.GetFileName(TestCutSetup.PathBeside(Path_("depth-test.nc"))));

    /// <summary>Anything that is not one of these comes back as null rather than as defaults.</summary>
    [Theory]
    [InlineData("G21 G90\nG0 X0 Y0\n")]
    [InlineData("{ \"SchemaVersion\": 1, \"LineCount\": 0, \"LadderRungs\": 0 }")]
    [InlineData("{ \"SchemaVersion\": 1, \"LineCount\": 6, \"LineLengthMm\": 0 }")]
    [InlineData("{ \"SchemaVersion\": 1, \"LineCount\": 6, \"LineLengthMm\": 15 }")]
    [InlineData("not json at all")]
    public void AFileThatIsNotOneIsRefused(string content)
    {
        Directory.CreateDirectory(_folder);

        var file = Path_("x" + TestCutSetup.Extension);
        File.WriteAllText(file, content);

        Assert.Null(TestCutSetup.Load(file));
    }

    /// <summary>
    /// Either half of the test may be left out, and a count of zero is how that is recorded — so a
    /// setup with no depth series is a real setup, not a broken one.
    /// </summary>
    [Theory]
    [InlineData(0, 5)]
    [InlineData(6, 0)]
    public void HalfATestIsStillATest(int lines, int rungs)
    {
        var file = Path_("half" + TestCutSetup.Extension);

        TestCutSetup
            .From(Custom(Tool.DefaultVBit) with { LineCount = lines, LadderRungs = rungs }, false)
            .Save(file);

        var back = TestCutSetup.Load(file);

        Assert.NotNull(back);
        Assert.Equal(lines, back.LineCount);
        Assert.Equal(rungs, back.LadderRungs);
    }

    [Fact]
    public void AMissingFileIsNotACrash() =>
        Assert.Null(TestCutSetup.Load(Path_("nothing-here" + TestCutSetup.Extension)));

    /// <summary>
    /// The machine's own numbers are not in here.
    ///
    /// Safe height, approach and decimals belong to the machine as it is set up now, not to a test
    /// written last week, and restoring a stale safe height is the one thing in this file that
    /// could put a tool somewhere unexpected.
    /// </summary>
    [Fact]
    public void TheMachinesOwnSettingsAreNotSaved()
    {
        var file = Path_("t" + TestCutSetup.Extension);
        TestCutSetup.From(Custom(Tool.DefaultVBit) with { SafeZMm = 11, Decimals = 5 }, false).Save(file);

        var json = File.ReadAllText(file);

        Assert.DoesNotContain("SafeZ", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Approach", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Decimals", json, StringComparison.OrdinalIgnoreCase);
    }
}
