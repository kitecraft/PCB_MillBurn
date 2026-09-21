using MillBurn.Core;
using MillBurn.Gcode;
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

        var depths = report.Lines.Where(l => !l.IsRepeat && !l.IsLadder).Select(l => Math.Round(l.DepthMm, 3)).ToList();

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
    public void ItCutsOnePassPerPass()
    {
        var (text, report) = TestCut.Generate(Depth);

        output.WriteLine(text);

        var parsed = GcodeParser.Parse(text);
        var cuts = parsed.Moves.Where(m => !m.IsRapid && m.MovesInPlane).ToList();

        // A line is a band: as many traverses along X as it has passes, and a stepover in Y between
        // each pair of them. Nothing lifts in the middle — retracting between passes would cost a
        // plunge apiece for nothing.
        var expected = report.Lines.Sum(l => l.PassCount + (l.PassCount - 1));

        Assert.Equal(expected, cuts.Count);
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

    // ------------------------------------------------------------------ measurable by a caliper

    /// <summary>
    /// The reason the bands exist, stated as an assertion.
    ///
    /// A single pass of a 60° V-bit runs 0.150 mm at 0.02 deep and 0.266 at 0.12 — you cannot get a
    /// caliper jaw onto either, and the whole spread across the series is 0.116 mm against an
    /// honest accuracy of about 0.02. Twenty passes put the same information on a band a caliper
    /// can sit against.
    /// </summary>
    [Fact]
    public void EveryLineIsWideEnoughToGetACaliperOnto()
    {
        var (_, report) = TestCut.Generate(Depth with { Tool = Tool.DefaultVBit with { IncludedAngleDegrees = 60 } });

        Assert.All(report.Lines.Where(l => !l.IsLadder), l => Assert.True(
            l.BandWidthMm > 1.0,
            $"line {l.Number} is only {l.BandWidthMm:F3} mm across"));
    }

    /// <summary>
    /// And the constant that turns a band back into a cut width is exact and identical on every
    /// line — which is what makes the differences between lines trustworthy, because a caliper's
    /// error is mostly a fixed bias and a fixed bias cancels between two readings.
    /// </summary>
    [Fact]
    public void TheSteppedOverGroundIsTheSameOnEveryLine()
    {
        var (_, report) = TestCut.Generate(Depth);

        var series = report.Lines.Where(l => !l.IsLadder).ToList();

        Assert.Single(series.Select(l => l.SteppedMm).Distinct());

        foreach (var line in report.Lines)
        {
            Assert.Equal(line.PredictedWidthMm, line.BandWidthMm - line.SteppedMm, 9);
        }
    }

    /// <summary>
    /// The stepover has to sit under the narrowest cut in the series, or the passes stop
    /// overlapping and the band is not a band.
    /// </summary>
    [Fact]
    public void ThePassesOverlapAtTheShallowestLine()
    {
        var (_, report) = TestCut.Generate(Depth);
        var shallowest = report.Lines.Where(l => !l.IsLadder).MinBy(l => l.DepthMm);

        Assert.True(
            shallowest.StepoverMm < shallowest.PredictedWidthMm,
            $"stepping {shallowest.StepoverMm:F3} mm across a {shallowest.PredictedWidthMm:F3} mm cut leaves ribs");
    }

    /// <summary>
    /// Widening the lines must not let them run into each other: the spacing is a gap between
    /// bands, not a distance between centres.
    /// </summary>
    [Fact]
    public void BandsDoNotTouch()
    {
        var (_, report) = TestCut.Generate(Depth with { LineSpacingMm = 2 });

        for (var i = 1; i < report.Lines.Count; i++)
        {
            var below = report.Lines[i - 1];
            var gap = report.Lines[i].YMm - (below.YMm + below.SteppedMm);

            Assert.True(gap > 1.0, $"only {gap:F2} mm between line {i} and line {i + 1}");
        }
    }

    /// <summary>
    /// A feed test stays one pass however many are asked for. What it asks is what an edge looks
    /// like, and a band of overlapping passes hides every edge but the outer two.
    /// </summary>
    [Fact]
    public void AFeedSeriesIsAlwaysASinglePass() =>
        Assert.All(
            TestCut.Generate(Feed with { PassesPerLine = 20 }).Report.Lines,
            l => Assert.Equal(1, l.PassCount));

    /// <summary>A stepover given by hand is used as given.</summary>
    [Fact]
    public void AStepoverCanBeSetByHand() =>
        Assert.All(
            TestCut.Generate(Depth with { StepoverMm = 0.07 }).Report.Lines.Where(l => !l.IsLadder),
            l => Assert.Equal(0.07, l.StepoverMm));

    // ------------------------------------------------------------------ the width ladder

    /// <summary>
    /// The ladder brackets the believed width from both sides, which is the whole of its value: the
    /// bottom rung comes out solid whatever the bit really does and the top rung comes out ribbed
    /// whatever it really does, so the operator is never asked to judge a single sample in
    /// isolation — only to find where a row of them changes.
    /// </summary>
    [Fact]
    public void TheLadderBracketsWhatTheLibraryClaims()
    {
        var (_, report) = TestCut.Generate(Depth);
        var rungs = report.Lines.Where(l => l.IsLadder).ToList();

        Assert.True(rungs.Count >= 3, "a ladder needs rungs either side of the answer");

        Assert.True(rungs[0].PredictedRibMm < 0, "the bottom rung must come out solid");
        Assert.True(rungs[^1].PredictedRibMm > 0, "the top rung must come out ribbed");

        // And it climbs, so "the first ribbed one" is a meaningful thing to look for.
        for (var i = 1; i < rungs.Count; i++)
        {
            Assert.True(rungs[i].StepoverMm > rungs[i - 1].StepoverMm);
        }
    }

    /// <summary>Every rung is the same depth and the same passes; only the stepover moves.</summary>
    [Fact]
    public void OnlyTheStepoverChangesUpTheLadder()
    {
        var rungs = TestCut.Generate(Depth).Report.Lines.Where(l => l.IsLadder).ToList();

        Assert.Single(rungs.Select(r => r.DepthMm).Distinct());
        Assert.Single(rungs.Select(r => r.PassCount).Distinct());
        Assert.Equal(rungs.Count, rungs.Select(r => r.StepoverMm).Distinct().Count());
    }

    /// <summary>
    /// The repeat stays at the far end of the coupon. That distance is the whole of what it
    /// measures, so the ladder goes before it rather than past it.
    /// </summary>
    [Fact]
    public void TheLadderDoesNotDisplaceTheRepeat()
    {
        var lines = TestCut.Generate(Depth).Report.Lines;

        Assert.True(lines[^1].IsRepeat);
        Assert.All(lines.Where(l => l.IsLadder), r => Assert.True(r.YMm < lines[^1].YMm));
    }

    /// <summary>A feed test asks a different question and gets no ladder.</summary>
    [Fact]
    public void AFeedSeriesHasNoLadder() =>
        Assert.DoesNotContain(TestCut.Generate(Feed).Report.Lines, l => l.IsLadder);

    /// <summary>
    /// The ladder on its own, which is a whole test: it fixes the tip width, which is the number
    /// most often wrong. No series lines, and the ladder still gets its repeat at the far end —
    /// a tilt shifts the effective depth, which moves the transition, so the check still earns its
    /// four seconds.
    /// </summary>
    [Fact]
    public void TheLadderAloneIsAWholeTest()
    {
        var (text, report) = TestCut.Generate(Depth with { LineCount = 0 });

        Assert.All(report.Lines, l => Assert.True(l.IsLadder));
        Assert.Equal(Depth.LadderRungs + 1, report.Lines.Count);
        Assert.True(report.Lines[^1].IsRepeat);

        Assert.DoesNotContain(report.Warnings, w => w.Contains("cuts nothing", StringComparison.Ordinal));
        Assert.Contains("LADDER RUNGS", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Lines get deeper as they go", text, StringComparison.Ordinal);
    }

    /// <summary>The series on its own, which is the other whole test: it fixes the included angle.</summary>
    [Fact]
    public void TheSeriesAloneIsAWholeTest()
    {
        var (text, report) = TestCut.Generate(Depth with { LadderRungs = 0 });

        Assert.DoesNotContain(report.Lines, l => l.IsLadder);
        Assert.DoesNotContain("LADDER", text, StringComparison.Ordinal);
        Assert.Contains("Lines get deeper as they go", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Neither half is not a crash. The dialog recalculates on every keystroke and one of those
    /// keystrokes is a zero on the way to a number.
    /// </summary>
    [Fact]
    public void NeitherHalfSaysSoRatherThanThrowing()
    {
        var (text, report) = TestCut.Generate(Depth with { LineCount = 0, LadderRungs = 0 });

        Assert.Empty(report.Lines);
        Assert.Equal(string.Empty, text);
        Assert.Contains(report.Warnings, w => w.Contains("Nothing to cut", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLadderCanBeTurnedOff() =>
        Assert.DoesNotContain(
            TestCut.Generate(Depth with { LadderRungs = 0 }).Report.Lines,
            l => l.IsLadder);

    /// <summary>
    /// A series that cuts nothing is still flagged, even though the ladder sits at its own depth
    /// and does cut. The ladder answering does not make the series meaningful.
    /// </summary>
    [Fact]
    public void ASeriesThatCutsNothingIsStillFlaggedWithALadderPresent() =>
        Assert.Contains(
            TestCut.Generate(Depth with { StartDepthMm = 0, DepthStepMm = 0 }).Report.Warnings,
            w => w.Contains("cuts nothing", StringComparison.Ordinal));

    /// <summary>
    /// The coupon has to be big enough for what it now cuts. Shipping a program that runs off the
    /// end of the stock the page told you to find would be worse than the narrow lines were.
    /// </summary>
    [Fact]
    public void TheStockSizeCoversTheBands()
    {
        var (_, report) = TestCut.Generate(Depth);
        var last = report.Lines[^1];

        Assert.True(report.StockHeightMm >= last.YMm + last.SteppedMm);
    }
}
