using MillBurn.Core;
using MillBurn.Gcode;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Programs for dialling a bit in on scrap.
///
/// Every number this app computes about a cut rests on the tool library, and every one of them is a
/// claim about a physical object. A V-bit's cut width is derived from a tip diameter somebody typed
/// in — and one read off a product listing has already been wrong by a factor of twenty-five, in
/// units rather than in digits, with nothing on screen to show it. These cut a few lines so the
/// claim can be checked against a caliper.
/// </summary>
public sealed class TestCutTests(ITestOutputHelper output)
{
    private static TestCutOptions Depth => new()
    {
        Tool = Tool.DefaultVBit,
        Kind = TestCutKind.Depth,
        LineCount = 5,
        StartDepthMm = 0.02,
        DepthStepMm = 0.02,
    };

    private static TestCutOptions Feed => new()
    {
        Tool = Tool.DefaultVBit,
        Kind = TestCutKind.Feed,
        LineCount = 5,
        DepthMm = 0.05,
        FeedStepMmPerMin = 50,
    };

    // ------------------------------------------------------------------ what it plans

    [Fact]
    public void ADepthSeriesStepsDownOneLineAtATime()
    {
        var (_, report) = TestCut.Generate(Depth);

        var depths = report.Lines.Where(l => !l.IsRepeat).Select(l => Math.Round(l.DepthMm, 3)).ToList();

        output.WriteLine(string.Join(", ", depths));
        Assert.Equal([0.02, 0.04, 0.06, 0.08, 0.10], depths);
    }

    /// <summary>
    /// Centred on the tool's own feed, so the middle line is what the library already believes and
    /// the operator is comparing against a baseline rather than against nothing.
    /// </summary>
    [Fact]
    public void AFeedSeriesBracketsTheToolsOwnFeed()
    {
        var (_, report) = TestCut.Generate(Feed);

        var feeds = report.Lines.Where(l => !l.IsRepeat).Select(l => l.FeedMmPerMin).ToList();

        output.WriteLine(string.Join(", ", feeds));

        Assert.Equal(Tool.DefaultVBit.FeedMmPerMin, feeds[2]);
        Assert.Equal([100, 150, 200, 250, 300], feeds);
    }

    /// <summary>Every line in a feed series is the same depth, or it is measuring two things.</summary>
    [Fact]
    public void AFeedSeriesChangesNothingButTheFeed()
    {
        var (_, report) = TestCut.Generate(Feed);

        Assert.Single(report.Lines.Select(l => l.DepthMm).Distinct());
    }

    /// <summary>
    /// The repeat is the cheapest check on the whole coupon: two identical cuts at opposite ends,
    /// which should measure the same. When they do not, nothing else on the coupon means anything.
    /// </summary>
    [Fact]
    public void TheFirstLineIsCutAgainAtTheFarEnd()
    {
        var (_, report) = TestCut.Generate(Depth);

        var first = report.Lines[0];
        var last = report.Lines[^1];

        Assert.True(last.IsRepeat);
        Assert.Equal(first.DepthMm, last.DepthMm);
        Assert.Equal(first.FeedMmPerMin, last.FeedMmPerMin);
        Assert.True(last.YMm > first.YMm, "the repeat belongs at the other end of the stock");
    }

    [Fact]
    public void TheRepeatCanBeTurnedOff() =>
        Assert.DoesNotContain(
            TestCut.Generate(Depth with { RepeatFirstLine = false }).Report.Lines,
            l => l.IsRepeat);

    /// <summary>
    /// The predicted width is the claim being tested, so it has to be in the report and in the
    /// file. A test that does not say what it expects is a test you cannot fail.
    /// </summary>
    [Fact]
    public void EachLineSaysWhatWidthItShouldMeasure()
    {
        var (text, report) = TestCut.Generate(Depth);

        var at6 = report.Lines.Single(l => Math.Abs(l.DepthMm - 0.06) < 1e-9);

        output.WriteLine($"{at6.DepthMm:F3} mm deep -> {at6.PredictedWidthMm:F3} mm wide");

        Assert.Equal(
            Nm.ToMillimetres(Tool.DefaultVBit.WidthAtDepth(Nm.FromMillimetres(0.06))),
            at6.PredictedWidthMm,
            6);

        Assert.Contains("predicted", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the program

    [Fact]
    public void ItCutsOneLinePerLine()
    {
        var (text, report) = TestCut.Generate(Depth);

        output.WriteLine(text);

        var parsed = GcodeParser.Parse(text);
        var cuts = parsed.Moves.Where(m => !m.IsRapid && m.MovesInPlane).ToList();

        Assert.Equal(report.Lines.Count, cuts.Count);
    }

    /// <summary>Nothing crosses the stock at depth: every traverse is above the surface.</summary>
    [Fact]
    public void NothingTravelsAtDepth()
    {
        var parsed = GcodeParser.Parse(TestCut.Generate(Depth).Text);

        Assert.DoesNotContain(
            parsed.Moves.Where(m => m.IsRapid && m.MovesInPlane),
            m => m.FromZNm < 0 || m.ToZNm < 0);
    }

    /// <summary>The spindle is started and stopped, because this one really does cut.</summary>
    [Fact]
    public void TheSpindleRuns()
    {
        var text = TestCut.Generate(Depth).Text;

        Assert.Contains("M3 S", text, StringComparison.Ordinal);
        Assert.Contains("M5", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bracket inside a G-code comment ends it early on GRBL and is rejected outright by
    /// LinuxCNC, so no comment may contain one however the prose was written.
    /// </summary>
    [Fact]
    public void NoCommentContainsANestedBracket()
    {
        foreach (var line in TestCut.Generate(Feed).Text.Split('\n').Where(l => l.StartsWith('(')))
        {
            Assert.Equal(1, line.Count(c => c == '('));
            Assert.Equal(1, line.Count(c => c == ')'));
        }
    }

    /// <summary>The header says how much copper to find, before somebody clamps the wrong offcut.</summary>
    [Fact]
    public void ItSaysHowMuchStockItNeeds()
    {
        var (text, report) = TestCut.Generate(Depth);

        Assert.True(report.StockWidthMm > Depth.LineLengthMm);
        Assert.True(report.StockHeightMm > 0);
        Assert.Contains("of bare copper", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ what it says out loud

    [Fact]
    public void ADepthPastTheConeIsFlagged()
    {
        var (_, report) = TestCut.Generate(Depth with
        {
            Tool = Tool.DefaultVBit with { MaxDepthNm = Nm.FromMillimetres(0.05) },
        });

        output.WriteLine(string.Join("\n", report.Warnings));
        Assert.Contains(report.Warnings, w => w.Contains("cone ends at", StringComparison.Ordinal));
    }

    [Fact]
    public void ATestThatCutsNothingIsFlagged() =>
        Assert.Contains(
            TestCut.Generate(Depth with { StartDepthMm = 0, DepthStepMm = 0 }).Report.Warnings,
            w => w.Contains("cuts nothing", StringComparison.Ordinal));

    /// <summary>
    /// A feed series deliberately runs feeds that are too slow — that is the end of the range being
    /// looked for — so it is a note rather than a warning.
    /// </summary>
    [Fact]
    public void ARubbingFeedIsExplainedRatherThanWarnedAbout()
    {
        var (_, report) = TestCut.Generate(Feed with { FeedStepMmPerMin = 80 });

        output.WriteLine(string.Join("\n", report.Notes));
        Assert.Contains(report.Notes, n => n.Contains("deliberate here", StringComparison.Ordinal));
    }

    /// <summary>An end mill cuts one width at any depth, and the report says so rather than implying otherwise.</summary>
    [Fact]
    public void ADepthSeriesOnAnEndMillSaysWhatItIsActuallyShowing() =>
        Assert.Contains(
            TestCut.Generate(Depth with { Tool = Tool.DefaultOutlineMill }).Report.Notes,
            n => n.Contains("one width at any depth", StringComparison.Ordinal));
}
