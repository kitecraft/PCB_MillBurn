using MillBurn.Align;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Two holes measured on the machine, and what they say about where the board is.
///
/// The thing being protected here is that the fit is *rigid*: a turn and a move, and never a stretch.
/// A fit that quietly absorbed a disagreement in the distance between the holes would turn one badly
/// read hole into every hole on the board being wrong, and nothing on screen would say so.
/// </summary>
public sealed class RigidFitTests(ITestOutputHelper output)
{
    private static Point2 Mm(double x, double y) => new(Nm.FromMillimetres(x), Nm.FromMillimetres(y));

    /// <summary>Turns a point about another, the way a board sitting crooked on the table is turned.</summary>
    private static Point2 Turn(Point2 at, Point2 about, double degrees, Point2 then)
    {
        var (sin, cos) = Math.SinCos(degrees * Math.PI / 180);
        double dx = at.X - about.X;
        double dy = at.Y - about.Y;

        return new Point2(
            about.X + (long)Math.Round((dx * cos) - (dy * sin)) + then.X,
            about.Y + (long)Math.Round((dx * sin) + (dy * cos)) + then.Y);
    }

    /// <summary>A board that has only slid over: no turn, and the shift is what was measured.</summary>
    [Fact]
    public void AShiftedBoardIsNotTurned()
    {
        var first = Mm(10, 10);
        var second = Mm(70, 40);
        var by = Mm(0.12, -0.05);

        var fit = RigidFit.Solve(first, first + by, second, second + by);

        Assert.True(fit.Found);
        Assert.Equal(0, fit.RotationDegrees, 9);
        Assert.Equal(by, fit.OffsetNm);
        Assert.Equal(0, fit.SeparationErrorNm);
    }

    /// <summary>
    /// A board turned on the table reads as a turn, not as a bigger shift — and the correction it
    /// produces puts both holes where they really are.
    /// </summary>
    [Theory]
    [InlineData(0.35)]
    [InlineData(-0.72)]
    [InlineData(2.5)]
    public void ATurnedBoardReadsAsATurn(double degrees)
    {
        var first = Mm(10, 10);
        var second = Mm(70, 40);
        var by = Mm(0.08, 0.15);

        var firstFound = Turn(first, first, degrees, by);
        var secondFound = Turn(second, first, degrees, by);

        var fit = RigidFit.Solve(first, firstFound, second, secondFound);

        output.WriteLine($"{degrees}° -> {fit.RotationDegrees}°, offset {fit.OffsetNm}");

        Assert.True(fit.Found);
        Assert.Equal(degrees, fit.RotationDegrees, 4);

        // And what the fit hands the exporter lands both holes on their measured positions.
        var correction = new DrillAlignment(fit.OffsetNm.X, fit.OffsetNm.Y)
        {
            RotationDegrees = fit.RotationDegrees,
            PivotNm = fit.PivotNm,
        };

        Assert.True(correction.Apply(first).DistanceTo(firstFound) <= 1);
        Assert.True(correction.Apply(second).DistanceTo(secondFound) <= Nm.FromMillimetres(0.001));
    }

    /// <summary>
    /// Anticlockwise is positive, the way the machine's own coordinates run. A sign the wrong way round
    /// doubles the error instead of removing it, and does it silently.
    /// </summary>
    [Fact]
    public void AnticlockwiseIsPositive()
    {
        var first = Mm(0, 0);
        var second = Mm(50, 0);

        // The far hole measured 1 mm north of where the program puts it: the board leans anticlockwise.
        var fit = RigidFit.Solve(first, first, second, second + Mm(0, 1));

        Assert.True(fit.Found);
        Assert.True(fit.RotationDegrees > 0);
        Assert.Equal(Math.Atan2(1, 50) * 180 / Math.PI, fit.RotationDegrees, 3);
    }

    /// <summary>
    /// Two holes almost on top of each other cannot measure an angle: a hole read a hundredth out swings
    /// the answer further than the answer itself. It refuses rather than reporting nonsense confidently.
    /// </summary>
    [Fact]
    public void HolesTooCloseTogetherAreRefused()
    {
        var first = Mm(20, 20);
        var second = Mm(22, 21);

        var fit = RigidFit.Solve(first, first, second, second);

        output.WriteLine(fit.Refusal);

        Assert.False(fit.Found);
        Assert.Contains("apart", fit.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, fit.RotationDegrees);
    }

    /// <summary>
    /// The free check: the distance between two holes in a piece of copper-clad does not change, so a
    /// measured distance that disagrees is a misreading, and fitting it would spread that misreading
    /// across the board.
    /// </summary>
    [Fact]
    public void AMeasuredDistanceThatDisagreesIsRefused()
    {
        var first = Mm(10, 10);
        var second = Mm(70, 10);

        // The second hole read 2 mm further out than it can be: the wrong hole was measured.
        var fit = RigidFit.Solve(first, first, second, second + Mm(2, 0));

        output.WriteLine(fit.Refusal);

        Assert.False(fit.Found);
        Assert.Equal(Nm.FromMillimetres(2), fit.SeparationErrorNm);
        Assert.Equal(0, fit.RotationDegrees);
    }

    /// <summary>A reading a few hundredths out is measurement, not a mistake: it fits, and it says so.</summary>
    [Fact]
    public void ASmallDisagreementStillFits()
    {
        var first = Mm(10, 10);
        var second = Mm(70, 10);

        var fit = RigidFit.Solve(first, first, second, second + Mm(0.03, 0));

        Assert.True(fit.Found);
        Assert.Equal(Nm.FromMillimetres(0.03), fit.SeparationErrorNm);
    }
}
