using MillBurn.Core;
using MillBurn.Gerber;
using MillBurn.Gerber.Apertures;
using MillBurn.Gerber.Model;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Fixtures are written here from the Ucamco specification rather than copied from any existing
/// tool's test data, both to keep the repository clean of GPL-3.0 material and because a fixture
/// you wrote is a fixture you understand.
/// </summary>
public sealed class GerberParserTests
{
    private const string Header = "%FSLAX46Y46*%\n%MOMM*%\n%LPD*%\nG01*\n";

    private static GerberImage Parse(string body) => GerberParser.Parse(Header + body + "M02*\n");

    private static void AssertNoErrors(GerberImage image) =>
        Assert.DoesNotContain(image.Diagnostics, d => d.IsError);

    // ------------------------------------------------------------------ coordinates

    [Theory]
    [InlineData("1500000", 4, 6, 1_500_000L)]      // 1.5 mm in 4.6 format
    [InlineData("-1500000", 4, 6, -1_500_000L)]
    [InlineData("0", 4, 6, 0L)]
    [InlineData("100", 2, 3, 100_000L)]            // 0.1 mm in 2.3 format
    public void DecodesMillimetreCoordinates(string field, int ints, int decimals, long expectedNm)
    {
        var format = new CoordinateFormat(ints, decimals);
        Assert.Equal(expectedNm, format.Decode(field, LengthUnit.Millimetres));
    }

    [Fact]
    public void DecodesInchCoordinates()
    {
        // 2.3 format, "1000" = 1.000 inch = 25.4 mm.
        var format = new CoordinateFormat(2, 3);
        Assert.Equal(25_400_000L, format.Decode("1000", LengthUnit.Inches));
    }

    [Fact]
    public void CoordinatesAreModal()
    {
        var image = Parse("%ADD10C,0.1*%\nD10*\nX0Y0D02*\nX1000000D01*\nY2000000D01*\n");
        AssertNoErrors(image);

        var draw = Assert.Single(image.Objects.OfType<DrawObject>());
        Assert.Equal(2, draw.Segments.Count);

        // The second segment omitted X, so it must keep 1.0 mm.
        Assert.Equal(new Point2(1_000_000, 0), draw.Segments[0].To);
        Assert.Equal(new Point2(1_000_000, 2_000_000), draw.Segments[1].To);
    }

    // ------------------------------------------------------------------ apertures

    [Fact]
    public void ParsesStandardApertures()
    {
        var image = Parse(
            "%ADD10C,0.5*%\n%ADD11R,1.0X2.0*%\n%ADD12O,1.0X2.0X0.3*%\n%ADD13P,1.5X6X30*%\n");
        AssertNoErrors(image);

        Assert.Equal(ApertureKind.Circle, image.Apertures[10].Kind);
        Assert.Equal(500_000, image.Apertures[10].NominalWidthNm);

        Assert.Equal(ApertureKind.Rectangle, image.Apertures[11].Kind);
        Assert.Equal(1_000_000, image.Apertures[11].NominalWidthNm);
        Assert.Equal(2_000_000, image.Apertures[11].NominalHeightNm);

        Assert.Equal(ApertureKind.Obround, image.Apertures[12].Kind);
        Assert.Equal(300_000, image.Apertures[12].HoleDiameterNm);

        Assert.Equal(ApertureKind.Polygon, image.Apertures[13].Kind);
    }

    /// <summary>
    /// Regression: the closing '*' of an extended command was being left on the body, so the last
    /// parameter of every aperture definition parsed as "0.5*" and the whole layer was lost.
    /// This produced a file that reported "0 objects" with no obvious cause.
    /// </summary>
    [Fact]
    public void StripsTheStatementTerminatorFromExtendedCommands()
    {
        var image = Parse("%ADD10C,0.5*%\n");
        AssertNoErrors(image);
        Assert.Equal(500_000, image.Apertures[10].NominalWidthNm);
    }

    /// <summary>
    /// Regression: files in the wild write a bare "D10" with no '*' terminator, which the lexer
    /// merges with the following line into "D10X0Y0D03". Deferring aperture selection to the end
    /// of the block let the D03 overwrite the D10, and every flash then found no aperture.
    /// </summary>
    [Fact]
    public void AppliesApertureSelectionWhereItAppears()
    {
        var image = Parse("%ADD10C,0.5*%\nD10\nX0Y0D03*\n");
        AssertNoErrors(image);

        var flash = Assert.Single(image.Objects.OfType<FlashObject>());
        Assert.Equal(10, flash.Aperture.Code);
    }

    // ------------------------------------------------------------------ macros

    [Theory]
    [InlineData("1", 1.0)]
    [InlineData("1+2", 3.0)]
    [InlineData("2x3", 6.0)]
    [InlineData("2X3", 6.0)]
    [InlineData("6/3", 2.0)]
    [InlineData("1+2x3", 7.0)]          // multiplication binds tighter
    [InlineData("(1+2)x3", 9.0)]
    [InlineData("-2+5", 3.0)]
    [InlineData("$1+$1", 0.5)]          // the expression KiCad's RoundRect actually uses
    [InlineData("$1x2+$2", 1.5)]
    public void EvaluatesMacroExpressions(string text, double expected)
    {
        var expr = MacroExpression.Parse(text);
        Assert.Equal(expected, expr.Evaluate([0.25, 1.0]), 9);
    }

    [Fact]
    public void UnsetMacroParametersEvaluateToZero()
    {
        var expr = MacroExpression.Parse("$7+1");
        Assert.Equal(1.0, expr.Evaluate([0.25]), 9);
    }

    [Fact]
    public void ParsesMacroWithCommentsAndExpressions()
    {
        // Structurally what KiCad emits for a rounded rectangle pad.
        var image = Parse(
            "%AMRoundRect*\n" +
            "0 Rectangle with rounded corners*\n" +
            "0 $1 Rounding radius*\n" +
            "4,1,4,$2,$3,$4,$5,$6,$7,$8,$9,$2,$3,0*\n" +
            "1,1,$1+$1,$2,$3*\n" +
            "20,1,$1+$1,$2,$3,$4,$5,0*%\n" +
            "%ADD10RoundRect,0.3X0.7X-1.7X0.7X1.7X-0.7X1.7X-0.7X-1.7X0*%\n");

        AssertNoErrors(image);

        var aperture = image.Apertures[10];
        Assert.Equal(ApertureKind.Macro, aperture.Kind);
        Assert.Equal("RoundRect", aperture.Macro!.Name);

        // Three primitives: the comments must not become statements.
        Assert.Equal(3, aperture.Macro.Statements.Count);
        Assert.Equal(10, aperture.Parameters.Count);
        Assert.Equal(-1.7, aperture.Parameters[2], 9);
    }

    [Fact]
    public void ParsesMacroVariableAssignment()
    {
        var image = Parse("%AMSized*\n$4=$1x0.75*\n1,1,$4,0,0*%\n%ADD10Sized,2.0*%\n");
        AssertNoErrors(image);

        var macro = image.Apertures[10].Macro!;
        Assert.Equal(2, macro.Statements.Count);
        var assignment = Assert.IsType<MacroAssignment>(macro.Statements[0]);
        Assert.Equal(3, assignment.Index);
        Assert.Equal(1.5, assignment.Value.Evaluate([2.0]), 9);
    }

    // ------------------------------------------------------------------ polarity and regions

    /// <summary>
    /// Clear polarity only erases what precedes it, so object order must survive parsing intact.
    /// Compositing as "union of darks minus union of clears" silently deletes copper.
    /// </summary>
    [Fact]
    public void PreservesPolarityOrdering()
    {
        var image = Parse(
            "%ADD10C,1.0*%\nD10*\n" +
            "X0Y0D03*\n" +
            "%LPC*%\nX1000000Y0D03*\n" +
            "%LPD*%\nX2000000Y0D03*\n");

        AssertNoErrors(image);

        var flashes = image.Objects.OfType<FlashObject>().ToList();
        Assert.Equal(3, flashes.Count);
        Assert.Equal(Polarity.Dark, flashes[0].Polarity);
        Assert.Equal(Polarity.Clear, flashes[1].Polarity);
        Assert.Equal(Polarity.Dark, flashes[2].Polarity);
    }

    [Fact]
    public void ParsesRegionsWithMultipleContours()
    {
        var image = Parse(
            "G36*\n" +
            "X0Y0D02*\nX1000000D01*\nY1000000D01*\nX0D01*\nY0D01*\n" +
            "X250000Y250000D02*\nX750000D01*\nY750000D01*\nX250000D01*\nY250000D01*\n" +
            "G37*\n");

        AssertNoErrors(image);

        var region = Assert.Single(image.Objects.OfType<RegionObject>());
        Assert.Equal(2, region.Contours.Count);
        Assert.Equal(4, region.Contours[0].Count);
        Assert.Equal(4, region.Contours[1].Count);
    }

    // ------------------------------------------------------------------ arcs

    [Fact]
    public void KeepsMultiQuadrantArcsAsArcs()
    {
        var image = Parse(
            "%ADD10C,0.1*%\nD10*\nG75*\nG03*\nX0Y0D02*\nX2000000Y0I1000000J0D01*\n");

        AssertNoErrors(image);

        var draw = Assert.Single(image.Objects.OfType<DrawObject>());
        var segment = Assert.Single(draw.Segments);
        Assert.True(segment.IsArc);
        Assert.Equal(SegmentKind.CounterClockwiseArc, segment.Kind);
        Assert.Equal(new Point2(1_000_000, 0), segment.Centre);
    }

    [Fact]
    public void RecoversSignsForSingleQuadrantArcs()
    {
        // G74: I and J are unsigned magnitudes; the correct centre must be recovered.
        var image = Parse(
            "%ADD10C,0.1*%\nD10*\nG74*\nG03*\nX1000000Y0D02*\nX0Y1000000I1000000J0D01*\n");

        AssertNoErrors(image);

        var segment = Assert.Single(Assert.Single(image.Objects.OfType<DrawObject>()).Segments);
        Assert.True(segment.IsArc);
        Assert.Equal(new Point2(0, 0), segment.Centre);
    }

    [Fact]
    public void TreatsArcWithoutOffsetsAsALineAndWarns()
    {
        // Real files leave G03 in force and then draw plain segments. Every renderer treats these
        // as lines; failing the layer instead would be worse than the warning.
        var image = Parse("%ADD10C,0.1*%\nD10*\nG03*\nX0Y0D02*\nX1000000Y0D01*\n");

        AssertNoErrors(image);
        Assert.Contains(image.Diagnostics, d => !d.IsError && d.Message.Contains("straight line", StringComparison.Ordinal));

        var segment = Assert.Single(Assert.Single(image.Objects.OfType<DrawObject>()).Segments);
        Assert.False(segment.IsArc);
    }

    // ------------------------------------------------------------------ X2 attributes

    [Fact]
    public void ReadsExtendedAttributesFromBlocks()
    {
        var image = Parse(
            "%TF.FileFunction,Copper,L1,Top*%\n" +
            "%TA.AperFunction,SMDPad,CuDef*%\n%ADD10C,1.0*%\n%TD*%\n" +
            "%TA.AperFunction,ViaPad*%\n%ADD11C,0.6*%\n%TD*%\n" +
            "D10*\n%TO.N,GND*%\nX0Y0D03*\n%TD*%\n");

        AssertNoErrors(image);

        Assert.Equal("Copper,L1,Top", image.FileFunction);
        Assert.True(image.HasExtendedAttributes);

        Assert.Equal("SMDPad", image.Apertures[10].Function);
        Assert.True(image.Apertures[10].IsPad);
        Assert.False(image.Apertures[10].IsVia);

        Assert.True(image.Apertures[11].IsVia);

        var flash = Assert.Single(image.Objects.OfType<FlashObject>());
        Assert.Equal("GND", flash.Net);
    }

    /// <summary>
    /// KiCad 9 emits attributes as "G04 #@! TF...*" comments rather than %TF% blocks. Missing
    /// this form loses every attribute on files from the most widely used EDA tool there is.
    /// KiCad 10 went back to real blocks, so both have to work.
    /// </summary>
    [Fact]
    public void ReadsExtendedAttributesFromCommentForm()
    {
        var image = Parse(
            "G04 #@! TF.FileFunction,Copper,L2,Bot*\n" +
            "G04 #@! TA.AperFunction,ViaPad*\n%ADD10C,0.6*%\n");

        AssertNoErrors(image);
        Assert.Equal("Copper,L2,Bot", image.FileFunction);
        Assert.True(image.Apertures[10].IsVia);
    }

    [Fact]
    public void DetectsNegativeFilePolarity()
    {
        var image = Parse("%TF.FilePolarity,Negative*%\n");
        Assert.True(image.IsNegative);
    }

    // ------------------------------------------------------------------ step and repeat

    [Fact]
    public void ExpandsStepAndRepeat()
    {
        var image = Parse(
            "%ADD10C,1.0*%\nD10*\n%SRX3Y2I5.0J4.0*%\nX0Y0D03*\n%SR*%\n");

        AssertNoErrors(image);

        var flashes = image.Objects.OfType<FlashObject>().ToList();
        Assert.Equal(6, flashes.Count);

        var positions = flashes.Select(f => f.At).ToHashSet();
        Assert.Contains(new Point2(0, 0), positions);
        Assert.Contains(new Point2(10_000_000, 4_000_000), positions);
    }

    // ------------------------------------------------------------------ diagnostics

    [Fact]
    public void ReportsMissingFormatSpecification()
    {
        // Without %FS% the whole board could be scaled by a power of ten, so it is an error
        // rather than a silent default.
        var image = GerberParser.Parse("%MOMM*%\n%ADD10C,1.0*%\nD10*\nX0Y0D03*\nM02*\n");
        Assert.Contains(image.Diagnostics, d => d.IsError && d.Message.Contains("%FS%", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsUseOfUndefinedAperture()
    {
        var image = Parse("D42*\nX0Y0D03*\n");
        Assert.Contains(image.Diagnostics, d => d.IsError && d.Message.Contains("D42", StringComparison.Ordinal));
    }

    [Fact]
    public void ContinuesAfterAMalformedCommand()
    {
        // One bad command must not lose the rest of the board.
        var image = Parse("%ADD10C,notanumber*%\n%ADD11C,1.0*%\nD11*\nX0Y0D03*\n");

        Assert.Contains(image.Diagnostics, d => d.IsError);
        Assert.Single(image.Objects.OfType<FlashObject>());
        Assert.Equal(11, image.Objects.OfType<FlashObject>().Single().Aperture.Code);
    }
}
