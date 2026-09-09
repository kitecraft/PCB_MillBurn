using MillBurn.Align;
using MillBurn.Core;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The measured shape of a piece of stock.
///
/// The failure that matters here is quiet: a map that is plausible everywhere and wrong somewhere
/// produces a board that cuts through the copper in one corner, and nothing about the file looks
/// odd. So the tests are mostly about what the map says between and beyond the points it was given.
/// </summary>
public sealed class HeightMapTests(ITestOutputHelper output)
{
    private static ProbeSample At(double xMm, double yMm, double zMm) => new(
        new Point2(Nm.FromMillimetres(xMm), Nm.FromMillimetres(yMm)),
        Nm.FromMillimetres(zMm));

    private static double Mm(long nm) => nm / (double)Nm.PerMillimetre;

    private static HeightMap Raw(params ProbeSample[] samples) =>
        HeightMap.Build(samples, new HeightMapOptions { ZeroAt = null });

    // ------------------------------------------------------------------ what it promises

    /// <summary>
    /// With no smoothing asked for, the surface goes through every measurement. Anything else means
    /// the map is quietly disagreeing with the probe, and the probe is the only thing here that has
    /// actually touched the board.
    /// </summary>
    [Fact]
    public void ItPassesThroughEveryProbePoint()
    {
        ProbeSample[] samples =
        [
            At(0, 0, 0.00), At(50, 0, -0.04), At(100, 0, 0.03),
            At(0, 40, 0.05), At(50, 40, -0.09), At(100, 40, 0.02),
            At(0, 80, 0.01), At(50, 80, 0.06), At(100, 80, -0.03),
        ];

        var map = Raw(samples);

        Assert.Equal(HeightMapFit.Spline, map.Fit);

        foreach (var sample in samples)
        {
            Assert.Equal(Mm(sample.ZNm), Mm(map.SampleNm(sample.At)), 4);
        }
    }

    /// <summary>
    /// A tilted board is the common case — stock that is not quite parallel to the machine — and a
    /// thin-plate spline reproduces a plane exactly, so between the points there is nothing to
    /// argue about. If this drifts, the interpolation is inventing curvature.
    /// </summary>
    [Fact]
    public void ATiltedPlaneIsReproducedExactlyBetweenThePoints()
    {
        static double Plane(double x, double y) => 0.002 * x - 0.001 * y + 0.01;

        var samples = new List<ProbeSample>();

        for (var x = 0; x <= 100; x += 25)
        {
            for (var y = 0; y <= 60; y += 20)
            {
                samples.Add(At(x, y, Plane(x, y)));
            }
        }

        var map = HeightMap.Build(samples, new HeightMapOptions { ZeroAt = null });
        var worst = 0.0;

        for (var x = 3; x < 100; x += 7)
        {
            for (var y = 3; y < 60; y += 5)
            {
                var at = new Point2(Nm.FromMillimetres(x), Nm.FromMillimetres(y));
                worst = Math.Max(worst, Math.Abs(Mm(map.SampleNm(at)) - Plane(x, y)));
            }
        }

        output.WriteLine($"worst deviation from the plane: {worst * 1000:F4} µm");
        Assert.True(worst < 0.0005, $"drifted {worst:F6} mm from a plane it was given exactly");
    }

    /// <summary>
    /// Past the edge of what was measured, the map holds the nearest measured value rather than
    /// carrying the trend onwards.
    ///
    /// A spline's linear term will happily run a tilt off to infinity. On a board that is 0.1 mm out
    /// of flat, extrapolating a few centimetres past the last probe point can produce a correction
    /// of millimetres — which drives the cutter through the board rather than into it.
    /// </summary>
    [Fact]
    public void OutsideTheMeasuredAreaItHoldsTheEdgeInsteadOfExtrapolating()
    {
        var map = Raw(
            At(0, 0, 0.00), At(20, 0, 0.10), At(0, 20, 0.05), At(20, 20, 0.15),
            At(10, 10, 0.08));

        // A long way outside in every direction. Nothing may exceed what was actually measured.
        foreach (var (x, y) in ((double, double)[])[(-200, -200), (500, 0), (0, 900), (300, 300)])
        {
            var z = Mm(map.SampleNm(new Point2(Nm.FromMillimetres(x), Nm.FromMillimetres(y))));

            Assert.InRange(z, Mm(map.MinZNm) - 1e-6, Mm(map.MaxZNm) + 1e-6);
        }

        Assert.Equal(0, map.OutsideByMm(new Point2(Nm.FromMillimetres(10), Nm.FromMillimetres(10))), 3);
        Assert.True(map.OutsideByMm(new Point2(Nm.FromMillimetres(30), Nm.FromMillimetres(10))) > 9);
    }

    /// <summary>
    /// GRBL reports <c>[PRB:]</c> in machine coordinates, so an imported log reads tens of
    /// millimetres below zero. Work zero is the board's own corner, on its surface, so the surface
    /// height there is zero by definition — which is enough to put the whole map back in the right
    /// frame without asking the operator for a number they would have to go and look up.
    /// </summary>
    [Fact]
    public void AMachineCoordinateLogIsShiftedSoTheOriginReadsZero()
    {
        var map = HeightMap.Build(
        [
            At(0, 0, -24.500), At(40, 0, -24.560), At(0, 30, -24.470),
            At(40, 30, -24.520), At(20, 15, -24.610),
        ]);

        Assert.Equal(0, Mm(map.SampleNm(Point2.Origin)), 5);

        // And the shape survives the shift: the board is still 0.14 mm out of flat.
        Assert.Equal(0.140, map.RangeMm, 3);
        Assert.Contains(map.Notes, n => n.Contains("machine coordinates", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAlreadyZeroedLogIsLeftWhereItIs()
    {
        var map = HeightMap.Build(
            [At(0, 0, 0), At(40, 0, -0.06), At(0, 30, 0.03), At(40, 30, -0.02), At(20, 15, -0.11)]);

        Assert.Equal(0, map.AppliedOffsetNm);
        Assert.DoesNotContain(map.Notes, n => n.Contains("machine coordinates", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ not enough to go on

    /// <summary>
    /// Probing a single row along a long thin board is a reasonable thing to do, and it justifies a
    /// tilt. It does not justify a surface, and a spline asked for one produces a singular system —
    /// so the fit steps down rather than failing, and says which one it used.
    /// </summary>
    [Fact]
    public void PointsInALineGiveATiltRatherThanAFailure()
    {
        var map = Raw(At(0, 10, 0), At(25, 10, -0.05), At(50, 10, -0.10), At(75, 10, -0.15));

        Assert.Equal(HeightMapFit.Plane, map.Fit);
        Assert.Contains(map.Notes, n => n.Contains("in a line", StringComparison.Ordinal));

        // The tilt itself is right: halfway along, halfway down.
        var middle = new Point2(Nm.FromMillimetres(37.5), Nm.FromMillimetres(10));
        Assert.Equal(-0.075, Mm(map.SampleNm(middle)), 3);
    }

    [Fact]
    public void TwoPointsGiveAPlaneAndSayWhy()
    {
        var map = Raw(At(0, 0, 0), At(50, 50, -0.1));

        Assert.Equal(HeightMapFit.Plane, map.Fit);
        Assert.Contains(map.Notes, n => n.Contains("2 probe points", StringComparison.Ordinal));
    }

    [Fact]
    public void OneProbePointIsAFlatOffset()
    {
        var map = Raw(At(10, 10, -0.08));

        Assert.Equal(HeightMapFit.Constant, map.Fit);
        Assert.Equal(-0.08, Mm(map.SampleNm(new Point2(Nm.FromMillimetres(90), 0))), 4);
    }

    [Fact]
    public void NoProbePointsAtAllIsAnError() =>
        Assert.Throws<ArgumentException>(() => HeightMap.Build([]));

    // ------------------------------------------------------------------ awkward input

    /// <summary>
    /// Two readings at the same spot make the spline's system singular. They arrive honestly — a
    /// log appended twice, a grid that lands on a corner from both directions — and should not take
    /// the whole map down with them.
    /// </summary>
    [Fact]
    public void ASpotProbedTwiceDoesNotMakeItSingular()
    {
        var map = Raw(
            At(0, 0, 0), At(40, 0, -0.05), At(0, 30, 0.02), At(40, 30, -0.01),
            At(20, 15, -0.09), At(20, 15, -0.07));

        Assert.Equal(HeightMapFit.Spline, map.Fit);
        Assert.Equal(5, map.PointCount);

        // The later reading is the one kept: it is the deliberate re-probe.
        Assert.Equal(-0.07, Mm(map.SampleNm(new Point2(Nm.FromMillimetres(20), Nm.FromMillimetres(15)))), 4);
        Assert.Contains(map.Notes, n => n.Contains("repeated a spot", StringComparison.Ordinal));
    }

    /// <summary>
    /// A touch probe repeats to a few microns. Forced exactly through that noise the surface
    /// develops ripples between the points which are not on the board, so smoothing has to actually
    /// relax the fit rather than merely being accepted as an argument.
    /// </summary>
    [Fact]
    public void SmoothingLetsTheSurfacePassNearThePointsRatherThanThrough()
    {
        ProbeSample[] noisy =
        [
            At(0, 0, 0.000), At(20, 0, 0.030), At(40, 0, -0.030), At(60, 0, 0.030),
            At(0, 20, -0.030), At(20, 20, 0.030), At(40, 20, -0.030), At(60, 20, 0.030),
            At(0, 40, 0.030), At(20, 40, -0.030), At(40, 40, 0.030), At(60, 40, -0.030),
        ];

        var exact = HeightMap.Build(noisy, new HeightMapOptions { ZeroAt = null });
        var eased = HeightMap.Build(noisy, new HeightMapOptions { ZeroAt = null, Smoothing = 0.05 });

        var at = new Point2(Nm.FromMillimetres(30), Nm.FromMillimetres(10));

        var exactMiss = Math.Abs(Mm(exact.SampleNm(noisy[0].At)) - Mm(noisy[0].ZNm));
        var easedMiss = Math.Abs(Mm(eased.SampleNm(noisy[0].At)) - Mm(noisy[0].ZNm));

        output.WriteLine($"at a probe point: exact misses by {exactMiss * 1000:F2} µm, "
            + $"smoothed by {easedMiss * 1000:F2} µm");
        output.WriteLine($"between points: exact {Mm(exact.SampleNm(at)) * 1000:F1} µm, "
            + $"smoothed {Mm(eased.SampleNm(at)) * 1000:F1} µm");

        Assert.True(exactMiss < 0.0005, "with no smoothing it should pass through the point");
        Assert.True(easedMiss > exactMiss, "smoothing should let it miss the point");

        // And the point of it: the surface between the samples stops swinging as far.
        Assert.True(
            Math.Abs(Mm(eased.SampleNm(at))) < Math.Abs(Mm(exact.SampleNm(at))),
            "smoothing should calm the surface between the samples");
    }

    /// <summary>The stock's bow, which is the number an operator actually wants to be told.</summary>
    [Fact]
    public void ItReportsHowFarOutOfFlatTheStockIs()
    {
        var map = Raw(At(0, 0, 0.06), At(50, 0, -0.05), At(0, 50, 0.02), At(50, 50, -0.09), At(25, 25, 0.01));

        Assert.Equal(0.15, map.RangeMm, 3);
    }
}
