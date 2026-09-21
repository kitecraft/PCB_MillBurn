using MillBurn.Core;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The numbers a flute count makes computable, and the advice they support.
///
/// The whole justification for storing flutes is chipload, so the tests are about whether the
/// derived numbers are right and whether the advice fires on the cases it exists for and stays
/// quiet otherwise. Advice that cries wolf gets ignored, and then it is worse than none.
/// </summary>
public sealed class ToolAdviceTests(ITestOutputHelper output)
{
    private static Tool EndMill(long feed, int rpm, int flutes = 2, double diameterMm = 0.8) => new()
    {
        Name = "test",
        Kind = ToolKind.EndMill,
        DiameterNm = Nm.FromMillimetres(diameterMm),
        FeedMmPerMin = feed,
        SpindleRpm = rpm,
        Flutes = flutes,
    };

    // ------------------------------------------------------------------ the arithmetic

    /// <summary>
    /// 600 mm/min, 10,000 rpm, two flutes: 600 / 20,000 = 0.03 mm a tooth. The one number that
    /// decides whether a small cutter survives, and it is nowhere in the inputs.
    /// </summary>
    [Fact]
    public void ChipLoadIsFeedOverSpeedTimesFlutes()
    {
        Assert.Equal(30_000, EndMill(600, 10_000).ChipLoadNm, 0);
        Assert.Equal(15_000, EndMill(600, 10_000, flutes: 4).ChipLoadNm, 0);
        Assert.Equal(60_000, EndMill(1200, 10_000).ChipLoadNm, 0);
    }

    [Fact]
    public void SurfaceSpeedIsPiTimesDiameterTimesSpeed()
    {
        // π × 0.8 mm × 10,000 rpm = 25,133 mm/min = 25.1 m/min.
        Assert.Equal(25.13, EndMill(600, 10_000).SurfaceSpeedMPerMin, 2);
    }

    /// <summary>A V-bit's cutting diameter depends on depth, so it has no one surface speed.</summary>
    [Fact]
    public void AVBitHasNoSurfaceSpeed()
    {
        var vbit = EndMill(200, 12_000) with { Kind = ToolKind.VBit, DiameterNm = 0 };

        Assert.Equal(0, vbit.SurfaceSpeedMPerMin);
    }

    [Fact]
    public void PlungePerRevolutionIsPlungeOverSpeed()
    {
        var drill = new Tool
        {
            Name = "0.3 mm drill",
            Kind = ToolKind.Drill,
            DiameterNm = Nm.FromMillimetres(0.3),
            PlungeMmPerMin = 100,
            SpindleRpm = 12_000,
        };

        // 100 mm/min at 12,000 rpm is 8.3 µm a turn, which is fine...
        Assert.Equal(8_333, drill.PlungePerRevNm, 0);

        // ...and the same feed at 3,000 rpm is 33 µm, which is not. Nothing in the feed says so.
        Assert.Equal(33_333, (drill with { SpindleRpm = 3_000 }).PlungePerRevNm, 0);
    }

    /// <summary>Two different reasons a tool stops working at depth; it stops at the first.</summary>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3.0, 0, 3.0)]
    [InlineData(0, 1.0, 1.0)]
    [InlineData(3.0, 1.0, 1.0)]
    [InlineData(0.5, 1.0, 0.5)]
    public void UsableDepthIsTheTighterOfFluteAndCone(double fluteMm, double coneMm, double expectedMm)
    {
        var tool = EndMill(600, 10_000) with
        {
            FluteLengthNm = Nm.FromMillimetres(fluteMm),
            MaxDepthNm = Nm.FromMillimetres(coneMm),
        };

        Assert.Equal(Nm.FromMillimetres(expectedMm), tool.UsableDepthNm);
    }

    // ------------------------------------------------------------------ when it speaks up

    [Fact]
    public void ItSaysNothingAboutASensiblySetUpTool()
    {
        var advice = ToolAdvice.For(EndMill(600, 10_000));

        Assert.Empty(advice);
    }

    /// <summary>
    /// The failure that does not look like one. Running a bit slowly feels careful; it means the
    /// edge rubs rather than cuts, which blunts it faster than working properly would.
    /// </summary>
    [Fact]
    public void RubbingIsCalledOut()
    {
        var advice = ToolAdvice.For(EndMill(50, 24_000));

        output.WriteLine(string.Join("\n", advice));
        Assert.Contains(advice, a => a.Contains("rubbing", StringComparison.Ordinal));
    }

    /// <summary>
    /// Scaled by diameter, because what breaks is the shank and its strength goes with the cube of
    /// it: 40 µm a tooth is nothing on a 3 mm cutter and destroys a 0.4 mm one.
    /// </summary>
    [Fact]
    public void TooMuchPerToothIsScaledToTheDiameter()
    {
        var small = ToolAdvice.For(EndMill(1600, 20_000, diameterMm: 0.4));
        var large = ToolAdvice.For(EndMill(1600, 20_000, diameterMm: 3.0));

        output.WriteLine("0.4 mm: " + string.Join("; ", small));
        output.WriteLine("3.0 mm: " + string.Join("; ", large));

        Assert.Contains(small, a => a.Contains("a lot for this diameter", StringComparison.Ordinal));
        Assert.Empty(large);
    }

    /// <summary>
    /// A drill only ever plunges, so its lateral feed says nothing about how it is working. Judging
    /// one on chipload flagged a perfectly sensible drill as rubbing, on a number that drill will
    /// never use.
    /// </summary>
    [Fact]
    public void ADrillIsNotJudgedOnItsLateralFeed()
    {
        var drill = new Tool
        {
            Name = "1.0 mm drill",
            Kind = ToolKind.Drill,
            DiameterNm = Nm.FromMillimetres(1.0),
            FeedMmPerMin = 100,
            PlungeMmPerMin = 100,
            SpindleRpm = 12_000,
        };

        Assert.Empty(ToolAdvice.For(drill));

        // But it is judged on the plunge, which is the number that actually breaks it.
        var pushed = drill with { PlungeMmPerMin = 600, SpindleRpm = 3_000 };

        Assert.Contains(
            ToolAdvice.For(pushed),
            a => a.Contains("per revolution on the plunge", StringComparison.Ordinal));
    }

    [Fact]
    public void ADepthPastTheFlutesIsFlagged()
    {
        var tool = EndMill(600, 10_000) with { FluteLengthNm = Nm.FromMillimetres(1.5) };

        Assert.Null(ToolAdvice.DepthConcern(tool, Nm.FromMillimetres(1.4)));

        var concern = ToolAdvice.DepthConcern(tool, Nm.FromMillimetres(2.2));

        Assert.NotNull(concern);
        Assert.Contains("1.50 mm of flute", concern, StringComparison.Ordinal);
    }

    /// <summary>Nothing known about the flutes means nothing to say, not a guess.</summary>
    [Fact]
    public void AnUnknownFluteLengthIsNotAComplaint() =>
        Assert.Null(ToolAdvice.DepthConcern(EndMill(600, 10_000), Nm.FromMillimetres(50)));

    // ------------------------------------------------------------------ what reaches the disk

    /// <summary>
    /// Through the real save and load, not a serializer configured here: a test that builds its own
    /// options proves those options work, which is not the question.
    /// </summary>
    private static (string Json, ToolLibrary Reloaded) RoundTrip(params Tool[] tools)
    {
        var path = Path.Combine(Path.GetTempPath(), "millburn-tools-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            new ToolLibrary { Tools = [.. tools] }.Save(path);

            return (File.ReadAllText(path), ToolLibrary.LoadOrDefault(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Derived numbers stay out of the saved file. A stored derivation is one somebody can edit
    /// into disagreeing with its own inputs, after which it is silently ignored.
    /// </summary>
    [Fact]
    public void DerivedNumbersAreNotPersisted()
    {
        var (json, _) = RoundTrip(EndMill(600, 10_000));

        output.WriteLine(json);

        Assert.Contains("\"Flutes\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ChipLoad", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SurfaceSpeed", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PlungePerRev", json, StringComparison.Ordinal);
        Assert.DoesNotContain("UsableDepth", json, StringComparison.Ordinal);
        Assert.DoesNotContain("WidthPerDepth", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNewFieldsSurviveSaveAndLoad()
    {
        var saved = EndMill(600, 10_000, flutes: 3) with
        {
            FluteLengthNm = Nm.FromMillimetres(2.5),
        };

        var (_, reloaded) = RoundTrip(saved);
        var tool = Assert.Single(reloaded.Tools);

        Assert.Equal(3, tool.Flutes);
        Assert.Equal(Nm.FromMillimetres(2.5), tool.FluteLengthNm);
    }

    /// <summary>
    /// A library written before these fields existed still loads, with sane defaults rather than
    /// zeros — a flute count of zero would make chipload undefined for every tool anyone already
    /// had saved.
    /// </summary>
    [Fact]
    public void AToolSavedBeforeFlutesExistedStillLoads()
    {
        var path = Path.Combine(Path.GetTempPath(), "millburn-old-" + Guid.NewGuid().ToString("N") + ".json");

        File.WriteAllText(path, """
            {
              "SchemaVersion": 1,
              "Tools": [
                {
                  "Name": "0.8 mm end mill",
                  "Kind": "EndMill",
                  "DiameterNm": 800000,
                  "FeedMmPerMin": 600,
                  "SpindleRpm": 10000
                }
              ]
            }
            """);

        try
        {
            var tool = Assert.Single(ToolLibrary.LoadOrDefault(path).Tools);

            Assert.Equal(2, tool.Flutes);
            Assert.Equal(0, tool.FluteLengthNm);
            Assert.Equal(30_000, tool.ChipLoadNm, 0);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
