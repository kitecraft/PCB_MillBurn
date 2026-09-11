using MillBurn.Core;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// A V-bit tip that is too fine to be a real tool.
///
/// From the workshop, and it had already reached a board. A product listing gave "Tip Width: 0.005"
/// in a specification where every other dimension was inches — shank 1/8 inch, length 1-1/2 inch —
/// and it went into a millimetre field as 0.005 mm. The tool is 0.005 <em>inch</em>: 0.127 mm, and
/// twenty-five times coarser than the app believed.
///
/// Nothing about the result looked wrong, which is why it is worth a notice. The cut width was
/// computed faithfully from the tip, the pass count faithfully from the cut width, and the first
/// isolation pass placed half a cut-width clear of the copper — so the real cutter, wider than the
/// app believed, would have taken that margin out of the trace rather than out of the gap beside
/// it. Every trace narrower than drawn, and no sign of it anywhere on screen.
/// </summary>
public sealed class TipUnitsTests(ITestOutputHelper output)
{
    private static Tool VBit(double tipMm) => Tool.DefaultVBit with { TipNm = Nm.FromMillimetres(tipMm) };

    [Fact]
    public void TheRealCaseIsNoticedAndTheInchReadingOffered()
    {
        var said = ToolAdvice.TipLooksTooFine(VBit(0.005));

        output.WriteLine(said);

        Assert.NotNull(said);
        Assert.Contains("0.005 in is 0.127 mm", said, StringComparison.Ordinal);
        Assert.Contains("millimetres", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gentle on purpose: a notice about units, not an accusation. Fine bits exist and the operator
    /// is the one holding it.
    /// </summary>
    [Fact]
    public void ItIsANoticeRatherThanAComplaint()
    {
        var said = ToolAdvice.TipLooksTooFine(VBit(0.005))!;

        Assert.DoesNotContain("wrong", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invalid", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error", said, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worth a second look", said, StringComparison.Ordinal);
    }

    /// <summary>Real bits say nothing at all. A notice that fires on ordinary tools is noise.</summary>
    [Theory]
    [InlineData(0.05)]
    [InlineData(0.1)]
    [InlineData(0.127)]
    [InlineData(0.2)]
    [InlineData(0.5)]
    public void OrdinaryBitsAreLeftAlone(double tipMm) =>
        Assert.Null(ToolAdvice.TipLooksTooFine(VBit(tipMm)));

    /// <summary>Only V-bits have a tip to get wrong.</summary>
    [Fact]
    public void OtherKindsAreNotJudgedOnATheyDoNotHave()
    {
        Assert.Null(ToolAdvice.TipLooksTooFine(Tool.DefaultOutlineMill));
        Assert.Null(ToolAdvice.TipLooksTooFine(Tool.DefaultDrill));
    }

    /// <summary>
    /// The inch reading is offered only when it lands somewhere a real bit could be. Telling
    /// somebody that 0.0001 mm might be 0.0025 mm helps nobody.
    /// </summary>
    [Fact]
    public void AnAbsurdValueIsNoticedWithoutASuggestion()
    {
        var said = ToolAdvice.TipLooksTooFine(VBit(0.0001));

        output.WriteLine(said);

        Assert.NotNull(said);
        Assert.DoesNotContain(" in is ", said, StringComparison.Ordinal);
        Assert.Contains("millimetres", said, StringComparison.Ordinal);
    }

    /// <summary>And it travels with the tool, so an export says it too rather than only the editor.</summary>
    [Fact]
    public void TheExportSaysItAsWell() =>
        Assert.Contains(
            ToolAdvice.For(VBit(0.005)),
            a => a.Contains("very fine for a carbide bit", StringComparison.Ordinal));
}
