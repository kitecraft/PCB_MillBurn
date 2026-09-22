using MillBurn.Core;
using MillBurn.Gerber.Excellon;

namespace MillBurn.Tests;

/// <summary>
/// Excellon has no single authority behind it, so the dialects differ in the one place that
/// matters: how a coordinate becomes a number. These fixtures cover the three that appear in
/// practice — explicit decimals (KiCad), leading-zero suppression, and trailing-zero suppression
/// (Altium, Eagle) — because guessing wrong scales the drill pattern by a power of ten while the
/// file still parses cleanly.
/// </summary>
public sealed class ExcellonParserTests
{
    private static void AssertNoErrors(ExcellonFile file) =>
        Assert.DoesNotContain(file.Diagnostics, d => d.IsError);

    [Fact]
    public void ParsesKicadMetricDecimalFormat()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            ; DRILL file KiCad 10.0.0
            ; #@! TF.FileFunction,Plated,1,2,PTH
            FMAT,2
            METRIC
            ; #@! TA.AperFunction,Plated,PTH,ComponentDrill
            T1C1.000
            T2C1.700
            %
            G90
            G05
            T1
            X155.465Y-101.075
            X158.005Y-103.615
            T2
            X153.586Y-74.916
            M30
            """);

        AssertNoErrors(file);

        Assert.Equal(LengthUnit.Millimetres, file.Unit);
        Assert.Equal(HolePlating.Plated, file.Plating);
        Assert.Equal(2, file.Tools.Count);
        Assert.Equal(1_000_000, file.Tools[1].DiameterNm);
        Assert.Equal(1_700_000, file.Tools[2].DiameterNm);
        Assert.Equal("Plated,PTH,ComponentDrill", file.Tools[1].Function);

        Assert.Equal(3, file.Hits.Count);
        Assert.Equal(new Point2(155_465_000, -101_075_000), file.Hits[0].At);
        Assert.Equal(2, file.Hits[2].Tool);
    }

    [Fact]
    public void DetectsNonPlatedFiles()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            ; #@! TF.FileFunction,NonPlated,1,2,NPTH
            METRIC
            T1C2.200
            %
            T1
            X154.15Y-93.95
            M30
            """);

        AssertNoErrors(file);
        Assert.Equal(HolePlating.NonPlated, file.Plating);
        Assert.Equal(2_200_000, file.Tools[1].DiameterNm);
    }

    /// <summary>
    /// "TZ" means trailing zeros are *present*, so leading ones are suppressed and the value is
    /// right-aligned. The naming is famously back to front.
    /// </summary>
    [Fact]
    public void DecodesLeadingZeroSuppressedInches()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            INCH,TZ
            T1C0.0400
            %
            T1
            X010000Y005000
            M30
            """);

        AssertNoErrors(file);

        // 2.4 inch format: 010000 = 1.0000 inch, 005000 = 0.5000 inch.
        Assert.Equal(Nm.FromInches(1.0), file.Hits[0].At.X);
        Assert.Equal(Nm.FromInches(0.5), file.Hits[0].At.Y);
    }

    [Fact]
    public void DecodesTrailingZeroSuppressedInches()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            INCH,LZ
            T1C0.0400
            %
            T1
            X01Y005
            M30
            """);

        AssertNoErrors(file);

        // Left-aligned on 2.4 format: "01" -> 010000 = 1.0000 inch, "005" -> 005000 = 0.5 inch.
        Assert.Equal(Nm.FromInches(1.0), file.Hits[0].At.X);
        Assert.Equal(Nm.FromInches(0.5), file.Hits[0].At.Y);
    }

    [Fact]
    public void HonoursAnExplicitFormatDeclaration()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            METRIC,000.000
            T1C1.0
            %
            T1
            X155465Y101075
            M30
            """);

        AssertNoErrors(file);
        Assert.Equal(155_465_000, file.Hits[0].At.X);
    }

    [Fact]
    public void CoordinatesAreModal()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            METRIC
            T1C1.0
            %
            T1
            X10.0Y20.0
            X15.0
            M30
            """);

        AssertNoErrors(file);
        Assert.Equal(new Point2(15_000_000, 20_000_000), file.Hits[1].At);
    }

    [Fact]
    public void ParsesG85Slots()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            METRIC
            T1C1.0
            %
            T1
            X10.0Y10.0G85X20.0Y10.0
            M30
            """);

        AssertNoErrors(file);

        var slot = Assert.Single(file.Slots);
        Assert.Equal(new Point2(10_000_000, 10_000_000), slot.From);
        Assert.Equal(new Point2(20_000_000, 10_000_000), slot.To);
        Assert.Empty(file.Hits);
    }

    [Fact]
    public void ParsesRoutedSlotsBetweenToolDownAndUp()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            METRIC
            T1C1.0
            %
            T1
            G00X10.0Y10.0
            M15
            G01X20.0Y10.0
            G01X20.0Y15.0
            M16
            M30
            """);

        AssertNoErrors(file);
        Assert.Equal(2, file.Slots.Count);
        Assert.Equal(new Point2(20_000_000, 15_000_000), file.Slots[1].To);
    }

    [Fact]
    public void GroupsHolesByToolLargestFirst()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            METRIC
            T1C0.8
            T2C3.2
            %
            T1
            X1.0Y1.0
            X2.0Y1.0
            T2
            X3.0Y1.0
            M30
            """);

        AssertNoErrors(file);

        var order = file.ByTool().ToList();
        Assert.Equal(2, order[0].Tool.Number);   // 3.2 mm first
        Assert.Equal(1, order[0].Count);
        Assert.Equal(2, order[1].Count);
    }

    [Fact]
    public void ReportsAToolUsedWithoutADiameter()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            METRIC
            %
            T7
            X1.0Y1.0
            M30
            """);

        Assert.Contains(file.Diagnostics, d => d.IsError && d.Message.Contains("T7", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsMissingUnitDeclaration()
    {
        // Without METRIC or INCH the whole pattern could be off by 25.4x.
        var file = ExcellonParser.Parse("M48\nT1C1.0\n%\nT1\nX1.0Y1.0\nM30\n");
        Assert.Contains(file.Diagnostics, d => d.IsError && d.Message.Contains("METRIC", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ M71 and M72

    /// <summary>
    /// <c>M72</c> says inches, and nothing else in the file does.
    ///
    /// 6.29: an Olimex export states its units this way and no other, so every tool in it was read
    /// as millimetres — a 0.254 mm drill reported as a 0.01 mm one, which is not a drill that
    /// exists. The shape here is that file's: <c>M48</c>, <c>FMAT,2</c>, <c>M72</c>, then an
    /// ordinary tool table.
    /// </summary>
    [Fact]
    public void M72MeansInches()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            FMAT,2
            M72
            T01C0.0100
            T02C0.0709
            %
            G90
            T01
            X1.0Y-2.0
            M30
            """);

        AssertNoErrors(file);
        Assert.Equal(LengthUnit.Inches, file.Unit);

        // 0.0100 in is 0.254 mm and 0.0709 in is 1.80 mm: the smallest and the largest drill on
        // the board this was found on, both of which it reported at 1/25.4 of their size — 0.01 mm
        // and 0.07 mm, neither of which is a drill that exists.
        Assert.Equal(Nm.FromMillimetres(0.254), file.Tools[1].DiameterNm);
        Assert.Equal(Nm.FromMillimetres(1.80086), file.Tools[2].DiameterNm);

        // The coordinates are inches too. Nothing had proved that either way before this.
        var hit = Assert.Single(file.Hits);
        Assert.Equal(Nm.FromMillimetres(25.4), hit.At.X);
        Assert.Equal(Nm.FromMillimetres(-50.8), hit.At.Y);
    }

    /// <summary>
    /// A unit stated after the tool table still reaches the tools it was stated after.
    ///
    /// Legal, and what some older outputs do. A parser that converts each diameter as it reads it
    /// cannot go back for them afterwards, which is why the raw numbers are kept.
    ///
    /// Stated in inches deliberately. The first version of this test declared <c>M71</c> and
    /// checked the 1.000 mm tool came back as 1 mm — which a parser that ignored the line entirely
    /// would also produce, since millimetres is the default. It asserted the default, not the
    /// reaching back. Here the diameter has to move by 25.4 for the test to pass at all.
    /// </summary>
    [Fact]
    public void AUnitDeclaredAfterTheToolTableStillReachesTheTools()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            FMAT,2
            T01C1.000
            %
            M72
            G90
            T01
            X1.0Y-2.0
            M30
            """);

        AssertNoErrors(file);
        Assert.Equal(LengthUnit.Inches, file.Unit);
        Assert.Equal(Nm.FromMillimetres(25.4), file.Tools[1].DiameterNm);
    }

    /// <summary>And the same the other way, so neither direction rests on the default.</summary>
    [Fact]
    public void AMetricUnitDeclaredAfterTheToolTableAlsoReachesTheTools()
    {
        var file = ExcellonParser.Parse(
            """
            M48
            FMAT,2
            M72
            T01C1.000
            %
            M71
            G90
            T01
            X10.0Y-20.0
            M30
            """);

        AssertNoErrors(file);
        Assert.Equal(LengthUnit.Millimetres, file.Unit);
        Assert.Equal(Nm.FromMillimetres(1.0), file.Tools[1].DiameterNm);
    }

    /// <summary>
    /// The same holes written twice — once in millimetres, once in inches — read back the same.
    ///
    /// `tests/boards/PogoTest1-Inch` is PogoTest1's two drill files converted arithmetically: every
    /// coordinate and diameter divided by 25.4, `METRIC` replaced by `M72`, nothing else touched. A
    /// conversion is not a second opinion, so this cannot catch a parser wrong about inches in some
    /// way the converter was also wrong about. What it catches is what actually happened — one of
    /// the two units not being read at all.
    ///
    /// The tolerance is real and small: a decimal inch cannot express 155.465 mm exactly, so the
    /// fixture carries eight decimal places and lands within a nanometre or two of the original.
    /// </summary>
    [Theory]
    [InlineData("PogoTest1-PTH.drl")]
    [InlineData("PogoTest1-NPTH.drl")]
    public void AnInchFileAndAMetricFileOfTheSameBoardAgree(string name)
    {
        var metric = ExcellonParser.ParseFile(
            Path.Combine(RealBoards.Directory(RealBoards.PogoTest1), name));
        var inches = ExcellonParser.ParseFile(
            Path.Combine(RealBoards.Directory("PogoTest1-Inch"), name));

        AssertNoErrors(metric);
        AssertNoErrors(inches);

        Assert.Equal(LengthUnit.Millimetres, metric.Unit);
        Assert.Equal(LengthUnit.Inches, inches.Unit);

        const long Tolerance = 50;

        // Everything below compares one parse against the other, which two empty parses would
        // satisfy perfectly. These two lines are what stops that: the files hold what they hold.
        Assert.NotEmpty(metric.Tools);
        Assert.NotEmpty(metric.Hits);

        Assert.Equal(metric.Tools.Count, inches.Tools.Count);
        foreach (var (number, tool) in metric.Tools)
        {
            Assert.InRange(
                inches.Tools[number].DiameterNm, tool.DiameterNm - Tolerance, tool.DiameterNm + Tolerance);
        }

        Assert.Equal(metric.Hits.Count, inches.Hits.Count);
        for (var i = 0; i < metric.Hits.Count; i++)
        {
            Assert.Equal(metric.Hits[i].Tool, inches.Hits[i].Tool);
            Assert.InRange(inches.Hits[i].At.X, metric.Hits[i].At.X - Tolerance, metric.Hits[i].At.X + Tolerance);
            Assert.InRange(inches.Hits[i].At.Y, metric.Hits[i].At.Y - Tolerance, metric.Hits[i].At.Y + Tolerance);
        }
    }
}
