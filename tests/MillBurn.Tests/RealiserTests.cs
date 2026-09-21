using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Gerber;

namespace MillBurn.Tests;

/// <summary>
/// Gerber to filled area.
///
/// Almost everything that can go wrong here produces a *plausible* result rather than an error: a
/// hole filled solid, a pad erased by a clearance that should have preceded it, a thermal relief
/// closed into a disc. So the assertions are areas and ring counts, which are the two things that
/// actually distinguish those cases.
/// </summary>
public sealed class RealiserTests
{
    private const string Header = "%FSLAX46Y46*%\n%MOMM*%\n";

    private static RealisedLayer Realise(string body, RealisationOptions? options = null) =>
        GerberRealiser.Realise(GerberParser.Parse(Header + body + "M02*\n"), options);

    private static double Mm2(RealisedLayer layer) => layer.AreaMm2;

    /// <summary>
    /// Area of a curved shape, to within the tessellation tolerance.
    ///
    /// A fixed number of decimal places is the wrong assertion for anything with a curve in it: it
    /// demands a tighter absolute error from a big shape than a small one, for no reason, and it
    /// trips over the deliberate sub-0.03% bias that makes a built circle slightly large and a
    /// flattened arc slightly small. A relative bound says what is actually required — the polygon
    /// is the curve to within the chord tolerance — and still fails loudly if that stops holding.
    /// </summary>
    private static void AssertCurvedArea(double expectedMm2, double actualMm2)
    {
        var error = Math.Abs(actualMm2 - expectedMm2) / expectedMm2;
        Assert.True(
            error < 0.001,
            $"expected {expectedMm2:F6} mm^2 within 0.1%, got {actualMm2:F6} ({error:P4} out)");
    }

    // ------------------------------------------------------------------ flashes

    [Fact]
    public void ACircularFlashIsADisc()
    {
        var layer = Realise("%ADD10C,2.0*%\nD10*\nX0Y0D03*\n");

        AssertCurvedArea(Math.PI, Mm2(layer));
        Assert.Equal(1, layer.RingCount);
    }

    [Fact]
    public void ARectangularFlashIsExact()
    {
        Assert.Equal(6.0, Mm2(Realise("%ADD10R,3.0X2.0*%\nD10*\nX0Y0D03*\n")), 6);
    }

    [Fact]
    public void AnObroundIsARectangleWithTwoSemicircularCaps()
    {
        // 4 x 2 mm capsule: a 2 x 2 rectangle between two r=1 half-discs.
        var layer = Realise("%ADD10O,4.0X2.0*%\nD10*\nX0Y0D03*\n");

        AssertCurvedArea(4.0 + Math.PI, Mm2(layer));
        Assert.Equal(1, layer.RingCount);
    }

    /// <summary>
    /// The optional hole parameter has to punch through, not add a second disc. Getting this
    /// backwards fills every via's clearance with copper.
    /// </summary>
    [Fact]
    public void AnApertureHoleIsSubtractedNotAdded()
    {
        var solid = Realise("%ADD10C,2.0*%\nD10*\nX0Y0D03*\n");
        var drilled = Realise("%ADD10C,2.0X1.0*%\nD10*\nX0Y0D03*\n");

        AssertCurvedArea(Math.PI - (Math.PI * 0.25), Mm2(drilled));
        Assert.True(Mm2(drilled) < Mm2(solid));
        Assert.Equal(2, drilled.RingCount);
    }

    [Fact]
    public void FlashesLandWhereTheFileSaysTheyDo()
    {
        var layer = Realise("%ADD10R,2.0X2.0*%\nD10*\nX10000000Y5000000D03*\n");

        Assert.Equal(new Bounds(9_000_000, 4_000_000, 11_000_000, 6_000_000), layer.Bounds);
    }

    // ------------------------------------------------------------------ strokes

    /// <summary>
    /// A stroke is the aperture dragged along the path, so a 10 mm line with a 1 mm round pen
    /// covers <c>10 x 1</c> plus the two half-disc caps.
    /// </summary>
    [Fact]
    public void ACircularStrokeIsTheSweptDisc()
    {
        var layer = Realise("%ADD10C,1.0*%\nD10*\nX0Y0D02*\nX10000000Y0D01*\n");

        AssertCurvedArea(10.0 + (Math.PI * 0.25), Mm2(layer));
    }

    [Fact]
    public void ARectangularStrokeSweepsTheRectangle()
    {
        // A 2 x 2 pen along 10 mm sweeps a 12 x 2 rectangle.
        var layer = Realise("%ADD10R,2.0X2.0*%\nD10*\nX0Y0D02*\nX10000000Y0D01*\n");

        Assert.Equal(24.0, Mm2(layer), 6);
    }

    /// <summary>
    /// A stroke that never moves is a dot, and the offsetter has nothing to offset. Real files do
    /// this; dropping it loses a via stub silently.
    /// </summary>
    [Fact]
    public void AZeroLengthStrokeStampsTheAperture()
    {
        var layer = Realise("%ADD10C,2.0*%\nD10*\nX0Y0D02*\nX0Y0D01*\n");

        AssertCurvedArea(Math.PI, Mm2(layer));
    }

    [Fact]
    public void ClosedStrokesFormARingNotACappedLine()
    {
        var layer = Realise(
            """
            %ADD10C,1.0*%
            D10*
            X0Y0D02*
            X10000000Y0D01*
            X10000000Y10000000D01*
            X0Y10000000D01*
            X0Y0D01*
            """);

        // A 40 mm perimeter swept by a 1 mm pen, with the corners rounded.
        Assert.Equal(40.0, Mm2(layer), 0);
        Assert.Equal(2, layer.RingCount);
    }

    [Fact]
    public void ArcsInStrokesAreSwept()
    {
        var layer = Realise(
            """
            %ADD10C,1.0*%
            D10*
            G03*
            X5000000Y0D02*
            X5000000Y0I-5000000J0D01*
            """);

        // A full 5 mm-radius circle traced with a 1 mm pen: an annulus from 4.5 to 5.5.
        AssertCurvedArea(Math.PI * ((5.5 * 5.5) - (4.5 * 4.5)), Mm2(layer));
        Assert.Equal(2, layer.RingCount);
    }

    // ------------------------------------------------------------------ regions

    [Fact]
    public void ARegionIsFilled()
    {
        var layer = Realise(
            """
            %ADD10C,0.1*%
            D10*
            G36*
            X0Y0D02*
            X10000000Y0D01*
            X10000000Y10000000D01*
            X0Y10000000D01*
            X0Y0D01*
            G37*
            """);

        Assert.Equal(100.0, Mm2(layer), 4);
        Assert.Equal(1, layer.RingCount);
    }

    // ------------------------------------------------------------------ polarity

    /// <summary>
    /// **The most important test in this file.**
    ///
    /// A Gerber is a sequence, not a set. A clear-polarity object erases only what precedes it, so
    /// a pad flashed *after* a clearance survives. Collecting all darks, collecting all clears and
    /// subtracting once is the intuitive implementation, and it deletes that pad — leaving a board
    /// that looks perfect and has a missing pad.
    ///
    /// The two halves below differ only in the order of the last two statements, and a set-based
    /// implementation gives them the same answer.
    /// </summary>
    [Fact]
    public void ClearPolarityErasesOnlyWhatPrecedesIt()
    {
        const string Plane = "%ADD10R,10.0X10.0*%\n%ADD11C,4.0*%\nD10*\nX0Y0D03*\n";
        const string Clear = "%LPC*%\nD11*\nX0Y0D03*\n";
        const string DarkPad = "%LPD*%\nD11*\nX0Y0D03*\n";

        var clearedLast = Realise(Plane + DarkPad + Clear);
        var padLast = Realise(Plane + Clear + DarkPad);

        // Clearance applied last: the plane keeps a 4 mm hole.
        AssertCurvedArea(100 - (Math.PI * 4), Mm2(clearedLast));
        Assert.Equal(2, clearedLast.RingCount);

        // Pad flashed last: it fills the hole straight back in.
        Assert.Equal(100.0, Mm2(padLast), 4);
        Assert.Equal(1, padLast.RingCount);
    }

    [Fact]
    public void ConsecutiveObjectsOfOnePolarityBatchIntoOneRun()
    {
        var single = Realise("%ADD10C,1.0*%\nD10*\nX0Y0D03*\nX2000000Y0D03*\nX4000000Y0D03*\n");
        var alternating = Realise(
            "%ADD10R,10.0X10.0*%\n%ADD11C,2.0*%\nD10*\nX0Y0D03*\n" +
            "%LPC*%\nD11*\nX-3000000Y0D03*\n" +
            "%LPD*%\nD11*\nX3000000Y0D03*\n");

        Assert.Equal(1, single.PolarityRuns);
        Assert.Equal(3, alternating.PolarityRuns);
    }

    /// <summary>
    /// A negative file is reported, never silently inverted: inverting needs the board outline,
    /// which is in another file, and the drawn area is what CAM wants from a mask layer anyway.
    /// </summary>
    [Fact]
    public void NegativeFilePolarityIsReportedAndNotApplied()
    {
        var layer = GerberRealiser.Realise(GerberParser.Parse(
            "%TF.FilePolarity,Negative*%\n" + Header + "%ADD10C,2.0*%\nD10*\nX0Y0D03*\nM02*\n"));

        Assert.True(layer.DeclaredNegative);
        AssertCurvedArea(Math.PI, Mm2(layer));
        Assert.Contains(layer.Notes, n => n.Contains("ABSENT", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ macros

    [Fact]
    public void MacroCirclePrimitiveIsRealised()
    {
        var layer = Realise("%AMDOT*\n1,1,2.0,0,0*%\n%ADD10DOT*%\nD10*\nX0Y0D03*\n");

        AssertCurvedArea(Math.PI, Mm2(layer));
    }

    /// <summary>
    /// A primitive with exposure 0 subtracts from what the macro has drawn so far. Unioning
    /// everything instead turns a thermal relief into a solid disc, shorting a pad to its pour.
    /// </summary>
    [Fact]
    public void MacroExposureZeroSubtracts()
    {
        var layer = Realise("%AMRING*\n1,1,4.0,0,0*\n1,0,2.0,0,0*%\n%ADD10RING*%\nD10*\nX0Y0D03*\n");

        AssertCurvedArea((Math.PI * 4) - Math.PI, Mm2(layer));
        Assert.Equal(2, layer.RingCount);
    }

    /// <summary>
    /// Rotation is about the **macro origin**, not the primitive's own centre. The difference is a
    /// rotated footprint versus a footprint whose parts have each spun in place.
    /// </summary>
    [Fact]
    public void MacroRotationTurnsAboutTheMacroOriginNotThePrimitive()
    {
        // A 2 x 2 square centred at (5, 0), rotated 90 degrees.
        var layer = Realise("%AMOFF*\n21,1,2.0,2.0,5.0,0,90*%\n%ADD10OFF*%\nD10*\nX0Y0D03*\n");

        // About the macro origin it lands at (0, 5). About its own centre it would have stayed put.
        Assert.Equal(new Bounds(-1_000_000, 4_000_000, 1_000_000, 6_000_000), layer.Bounds);
    }

    /// <summary>
    /// Primitive 22 gives its rectangle by the lower-left corner rather than the centre; 21 gives
    /// the same rectangle by its centre. Placed equivalently they must agree exactly.
    /// </summary>
    [Fact]
    public void LowerLeftLineMatchesCenterLinePlacedEquivalently()
    {
        var centre = Realise("%AMA*\n21,1,4.0,2.0,2.0,1.0,0*%\n%ADD10A*%\nD10*\nX0Y0D03*\n");
        var lowerLeft = Realise("%AMB*\n22,1,4.0,2.0,0,0,0*%\n%ADD10B*%\nD10*\nX0Y0D03*\n");

        Assert.Equal(8.0, Mm2(centre), 6);
        Assert.Equal(Mm2(centre), Mm2(lowerLeft), 6);
        Assert.Equal(centre.Bounds, lowerLeft.Bounds);
    }

    [Fact]
    public void MacroOutlinePrimitiveIsRealised()
    {
        var layer = Realise(
            "%AMTRI*\n4,1,3,0,0,4.0,0,0,3.0,0,0,0*%\n%ADD10TRI*%\nD10*\nX0Y0D03*\n");

        Assert.Equal(6.0, Mm2(layer), 3);
    }

    [Fact]
    public void MacroVectorLinePrimitiveIsRealised()
    {
        var layer = Realise("%AMBAR*\n20,1,2.0,0,0,10.0,0,0*%\n%ADD10BAR*%\nD10*\nX0Y0D03*\n");

        // Butt ends, so exactly 10 x 2 with no caps.
        Assert.Equal(20.0, Mm2(layer), 3);
    }

    /// <summary>
    /// A thermal is an annulus cut into four quadrants by a cross. If the gap fails to separate
    /// them the pad shorts to the pour, which is the specific failure that makes a thermal relief
    /// worth testing at all.
    /// </summary>
    [Fact]
    public void ThermalPrimitiveSeparatesIntoFourQuadrants()
    {
        var layer = Realise("%AMTH*\n7,0,0,6.0,4.0,0.5,0*%\n%ADD10TH*%\nD10*\nX0Y0D03*\n");

        Assert.Equal(4, layer.RingCount);

        // Ring area less the cross that cuts it: pi(3^2 - 2^2) minus four 0.5-wide slots.
        var ring = Math.PI * ((3.0 * 3.0) - (2.0 * 2.0));
        Assert.True(Mm2(layer) < ring, "the cross must remove area");
        Assert.True(Mm2(layer) > ring * 0.8, $"the cross removed far too much: {Mm2(layer)} of {ring}");
    }

    // ------------------------------------------------------------------ robustness

    [Fact]
    public void AnUnrealisableApertureIsCountedNotSilentlyDropped()
    {
        // A macro referring to a parameter the aperture never supplies evaluates to zero, so the
        // primitive has no area. The layer must say so rather than come back quietly empty.
        var layer = Realise("%AMEMPTY*\n1,1,$9,0,0*%\n%ADD10EMPTY*%\nD10*\nX0Y0D03*\n");

        Assert.Equal(0, layer.RingCount);
        Assert.Equal(0, layer.ObjectCount);
    }

    [Fact]
    public void OutputIsDeterministic()
    {
        const string Board =
            "%ADD10C,1.0*%\n%ADD11R,2.0X3.0*%\nD10*\nX0Y0D02*\nX7654321Y1234567D01*\nD11*\nX3000000Y3000000D03*\n";

        var options = new RealisationOptions { Canonicalise = true };
        var first = Realise(Board, options);
        var second = Realise(Board, options);

        Assert.Equal(first.Area, second.Area);
        Assert.Equal(first.AreaMm2, second.AreaMm2, 9);
    }

    [Fact]
    public void ACoarserToleranceProducesFewerVerticesForTheSameShape()
    {
        const string Board = "%ADD10C,5.0*%\nD10*\nX0Y0D03*\n";

        var fine = Realise(Board, new RealisationOptions { SagittaNm = Nm.FromMillimetres(0.0005) });
        var coarse = Realise(Board, new RealisationOptions { SagittaNm = Nm.FromMillimetres(0.02) });

        Assert.True(coarse.VertexCount < fine.VertexCount,
            $"coarser tolerance should need fewer vertices; got {coarse.VertexCount} vs {fine.VertexCount}");

        // The knob has to buy something: accuracy must track the tolerance, not merely change.
        // Both still describe the same disc, the coarse one less exactly, and both err on the
        // containing side.
        var exact = Math.PI * 6.25;
        var fineError = (fine.AreaMm2 - exact) / exact;
        var coarseError = (coarse.AreaMm2 - exact) / exact;

        AssertCurvedArea(exact, fine.AreaMm2);
        Assert.True(coarseError > fineError,
            $"a coarser polygon should deviate more; got {coarseError:P4} vs {fineError:P4}");
        Assert.True(coarseError < 0.02,
            $"but 0.02 mm of chord error should stay well under 2%; got {coarseError:P4}");
    }
}
