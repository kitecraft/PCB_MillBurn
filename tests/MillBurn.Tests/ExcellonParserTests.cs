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
}
