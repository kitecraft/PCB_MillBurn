using MillBurn.Core;
using MillBurn.Gcode;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The checks that measure the machine (roadmap 6.22).
///
/// What these tests guard is the one thing a reader cannot verify by looking at the coupon: that the
/// approach directions are what the rows claim. A backlash check whose rows all approach from the
/// same side measures nothing, cuts cleanly, and reads "no backlash" — the most convincing wrong
/// answer this app could give.
/// </summary>
public sealed class MachineCheckTests(ITestOutputHelper output)
{
    private static readonly Tool EndMill = new()
    {
        Id = Guid.NewGuid(),
        Name = "0.8 mm end mill",
        Kind = ToolKind.EndMill,
        DiameterNm = Nm.FromMillimetres(0.8),
        PlungeMmPerMin = 50,
        SpindleRpm = 10000,
    };

    private static (string Text, MachineCheckReport Report) Check(
        MachineCheckKind kind, CheckAxis axis = CheckAxis.X, double span = 60) =>
        MachineCheck.Generate(new MachineCheckOptions
        {
            Tool = EndMill,
            Kind = kind,
            Axis = axis,
            SpanMm = span,
        });

    // ------------------------------------------------------------------ backlash

    /// <summary>
    /// Three rows of two, and the rows differ in exactly one thing. Row 1 approaches both holes from
    /// the same side; rows 2 and 3 are opposites of each other. That is the whole experiment.
    /// </summary>
    [Theory]
    [InlineData(CheckAxis.X)]
    [InlineData(CheckAxis.Y)]
    public void TheBacklashRowsDifferOnlyInApproachDirection(CheckAxis axis)
    {
        var (_, report) = Check(MachineCheckKind.Backlash, axis);

        Assert.Equal(6, report.Holes.Count);
        Assert.Equal(3, report.Pairs.Count);

        // Which side each hole is approached from, along the axis under test.
        double Side(CheckHole hole) => axis == CheckAxis.X
            ? Math.Sign(hole.FromXMm - hole.XMm)
            : Math.Sign(hole.FromYMm - hole.YMm);

        var sides = report.Holes.Select(Side).ToList();

        output.WriteLine(string.Join("\n", report.Holes.Select(h =>
            $"{h.Label}: at ({h.XMm}, {h.YMm}) from ({h.FromXMm}, {h.FromYMm})")));

        // Row 1: both the same. Rows 2 and 3: each mixed, and mirror images of one another.
        Assert.Equal(sides[0], sides[1]);
        Assert.NotEqual(sides[2], sides[3]);
        Assert.NotEqual(sides[4], sides[5]);
        Assert.Equal(sides[2], -sides[4]);
        Assert.Equal(sides[3], -sides[5]);

        // And nothing else moves: every pair is the same nominal distance apart, on one axis only.
        foreach (var pair in report.Pairs)
        {
            var a = report.Holes[pair.First];
            var b = report.Holes[pair.Second];

            Assert.Equal(60, pair.NominalMm);
            Assert.Equal(60, axis == CheckAxis.X ? b.XMm - a.XMm : b.YMm - a.YMm, 6);
            Assert.Equal(0, axis == CheckAxis.X ? b.YMm - a.YMm : b.XMm - a.XMm, 6);
        }
    }

    /// <summary>The approach only ever moves along the axis being measured; the other stays put.</summary>
    [Theory]
    [InlineData(CheckAxis.X)]
    [InlineData(CheckAxis.Y)]
    public void TheApproachMovesAlongTheAxisUnderTestAndNoOther(CheckAxis axis)
    {
        var (_, report) = Check(MachineCheckKind.Backlash, axis);

        foreach (var hole in report.Holes)
        {
            if (axis == CheckAxis.X)
            {
                Assert.Equal(hole.YMm, hole.FromYMm, 6);
                Assert.Equal(4, Math.Abs(hole.FromXMm - hole.XMm), 6);
            }
            else
            {
                Assert.Equal(hole.XMm, hole.FromXMm, 6);
                Assert.Equal(4, Math.Abs(hole.FromYMm - hole.YMm), 6);
            }
        }
    }

    // ------------------------------------------------------------------ squareness

    /// <summary>
    /// Four corners, all approached the same way — which is what makes the reading squareness rather
    /// than squareness plus slack — and two diagonals that should measure the same.
    /// </summary>
    [Fact]
    public void TheSquarenessHolesAreACornerEachAndApproachedAlike()
    {
        var (_, report) = Check(MachineCheckKind.Squareness, span: 60);

        Assert.Equal(4, report.Holes.Count);

        foreach (var hole in report.Holes)
        {
            Assert.True(hole.FromXMm < hole.XMm, $"{hole.Label} is not approached from the left");
            Assert.True(hole.FromYMm < hole.YMm, $"{hole.Label} is not approached from below");
        }

        var (ac, bd) = (report.Pairs[0], report.Pairs[1]);

        output.WriteLine($"{ac.Name} {ac.NominalMm:F3} mm, {bd.Name} {bd.NominalMm:F3} mm");

        Assert.Equal(Math.Sqrt(2) * 60, ac.NominalMm, 6);
        Assert.Equal(ac.NominalMm, bd.NominalMm, 9);

        // The pairs really are the diagonals rather than two sides.
        foreach (var pair in new[] { ac, bd })
        {
            var a = report.Holes[pair.First];
            var b = report.Holes[pair.Second];

            Assert.Equal(60, Math.Abs(b.XMm - a.XMm), 6);
            Assert.Equal(60, Math.Abs(b.YMm - a.YMm), 6);
        }
    }

    /// <summary>
    /// A squareness error grows with distance, so a short square cannot see a small one. The check
    /// says so rather than handing back a number that means nothing — the rule 09 §1 arrived at the
    /// hard way.
    /// </summary>
    [Fact]
    public void AShortSquarenessCheckWarnsThatItCannotSeeMuch()
    {
        var (_, small) = Check(MachineCheckKind.Squareness, span: 30);
        var (_, large) = Check(MachineCheckKind.Squareness, span: 100);

        Assert.Contains(small.Warnings, w => w.Contains("grows with distance", StringComparison.Ordinal));
        Assert.Empty(large.Warnings);

        // Backlash does not grow with distance, so a short span there is fine and says nothing.
        var (_, backlash) = Check(MachineCheckKind.Backlash, span: 30);
        Assert.Empty(backlash.Warnings);
    }

    // ------------------------------------------------------------------ the program

    /// <summary>
    /// The emitted program does what the report describes: every hole approached from its own
    /// starting point, then moved to, then pecked — never positioned in one move.
    /// </summary>
    [Fact]
    public void EveryHoleIsApproachedThenPlunged()
    {
        var (text, report) = Check(MachineCheckKind.Backlash);

        output.WriteLine(text[..text.IndexOf("\nG21", StringComparison.Ordinal)]);

        foreach (var hole in report.Holes)
        {
            var from = $"G0 X{hole.FromXMm:F3} Y{hole.FromYMm:F3}";
            var at = $"G0 X{hole.XMm:F3} Y{hole.YMm:F3}";

            var approach = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(approach >= 0, $"no approach move for {hole.Label}: expected {from}");
            Assert.True(
                text.IndexOf(at, approach, StringComparison.Ordinal) > approach,
                $"{hole.Label} is not moved to after its approach");
        }

        // Four pecks per hole, six holes, and the spindle stopped at the end.
        Assert.Equal(24, text.Split("G1 Z-").Length - 1);
        Assert.Contains("\nM5\n", text, StringComparison.Ordinal);
        Assert.EndsWith("M30\n", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The arithmetic is in the file, because the coupon will be measured at a bench with the page
    /// somewhere else — and "(row 2 − row 3) / 2" is the one thing nobody reconstructs correctly.
    /// </summary>
    [Fact]
    public void TheProgramSaysHowToReadIt()
    {
        var (backlash, _) = Check(MachineCheckKind.Backlash);
        var (square, _) = Check(MachineCheckKind.Squareness, span: 60);

        Assert.Contains("backlash = ( row 2 - row 3 ) / 2", backlash, StringComparison.Ordinal);
        Assert.Contains("OVER THE OUTSIDES", backlash, StringComparison.Ordinal);
        Assert.Contains("0.80 mm pin", backlash, StringComparison.Ordinal);

        Assert.Contains("84.853 mm", square, StringComparison.Ordinal);
        Assert.Contains("approached from below-left", square, StringComparison.Ordinal);

        // Both say what they cannot see.
        foreach (var program in new[] { backlash, square })
        {
            Assert.Contains("under 0.1 mm is not a measurement", program, StringComparison.Ordinal);
        }
    }
}
