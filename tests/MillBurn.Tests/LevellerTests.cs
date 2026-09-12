using MillBurn.Align;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Bending a finished program to follow a measured surface.
///
/// The whole point is depth, so the tests are about depth: the cut has to end up the same distance
/// below the copper everywhere, the path across the board must not move, and where the map does not
/// know the answer the whole thing has to stop rather than guess. A leveller that is subtly wrong
/// ruins boards while looking like it is helping.
/// </summary>
public sealed class LevellerTests(ITestOutputHelper output)
{
    private static double Mm(long nm) => nm / (double)Nm.PerMillimetre;

    private static Point2 P(double xMm, double yMm) =>
        new(Nm.FromMillimetres(xMm), Nm.FromMillimetres(yMm));

    /// <summary>A surface that tilts 0.2 mm across 100 mm of X, measured on a nine-point grid.</summary>
    private static HeightMap Tilted(double perMm = 0.002)
    {
        var samples = new List<ProbeSample>();

        for (var x = 0; x <= 100; x += 50)
        {
            for (var y = 0; y <= 100; y += 50)
            {
                samples.Add(new ProbeSample(P(x, y), Nm.FromMillimetres(perMm * x)));
            }
        }

        return HeightMap.Build(samples, new HeightMapOptions { ZeroAt = null });
    }

    private static HeightMap Flat()
    {
        var samples = new List<ProbeSample>();

        for (var x = 0; x <= 100; x += 50)
        {
            for (var y = 0; y <= 100; y += 50)
            {
                samples.Add(new ProbeSample(P(x, y), 0));
            }
        }

        return HeightMap.Build(samples, new HeightMapOptions { ZeroAt = null });
    }

    private static string Isolation(string board)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        return plan.Items.First(i => i.Operation == OperationKind.Isolation).Content;
    }

    // ------------------------------------------------------------------ the promise

    /// <summary>
    /// The claim, stated as depth rather than as Z: everywhere the tool is cutting, it sits the
    /// same distance below the measured surface as the original program asked it to sit below zero.
    ///
    /// Checked against the map itself at every emitted point, not only at the ends of the original
    /// moves — the whole reason for subdividing is the ground in between.
    /// </summary>
    [Fact]
    public void EveryCuttingPointKeepsItsDepthBelowTheMeasuredSurface()
    {
        var map = Tilted();

        var (text, report) = Leveller.Apply("""
            G21 G90
            G0 Z2.000
            G0 X0.000 Y10.000
            G1 Z-0.050 F60
            G1 X100.000 Y10.000 F200
            G0 Z2.000
            M30
            """, map);

        Assert.Null(report.Refusal);
        output.WriteLine($"rise {report.MaxRiseMm:F3} mm, fall {report.MaxFallMm:F3} mm, "
            + $"{report.SegmentsAdded} segments added");

        foreach (var move in GcodeParser.Parse(text).Moves.Where(m => m.ToZNm < 0))
        {
            var surface = Mm(map.SampleNm(move.To));
            Assert.Equal(-0.050, Mm(move.ToZNm) - surface, 4);
        }

        // The cut climbs with the board: 0.2 mm over the 100 mm run.
        Assert.Equal(0.200, report.MaxRiseMm, 3);
    }

    /// <summary>
    /// The tool has to go over the same ground. Levelling changes how deep the cut is, never where
    /// it is — a leveller that nudged X or Y would be quietly moving the isolation off the copper.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.GridStripConnector)]
    public void TheHorizontalPathIsUnchanged(string board)
    {
        var original = Isolation(board);
        var (levelled, report) = Leveller.Apply(original, Tilted(0.0015));

        Assert.Null(report.Refusal);

        var before = GcodeParser.Parse(original).Moves.Where(m => m.MovesInPlane).Select(m => m.To).ToList();
        var after = GcodeParser.Parse(levelled).Moves.Where(m => m.MovesInPlane).Select(m => m.To).ToList();

        output.WriteLine($"{board}: {before.Count} moves became {after.Count}, "
            + $"{report.ArcsExpanded} arcs expanded");

        // Subdivision adds points along the way, but every original destination is still visited,
        // in the same order.
        Assert.NotEmpty(before);

        var at = 0;

        foreach (var point in after)
        {
            if (at < before.Count && point == before[at])
            {
                at++;
            }
        }

        Assert.Equal(before.Count, at);
    }

    /// <summary>A map that measured a flat board should leave the program alone.</summary>
    [Fact]
    public void AFlatSurfaceChangesNothingMeasurable()
    {
        var original = Isolation(RealBoards.PogoTest1);
        var (levelled, report) = Leveller.Apply(original, Flat());

        Assert.Equal(0, report.MaxRiseMm, 6);
        Assert.Equal(0, report.MaxFallMm, 6);

        var before = GcodeParser.Parse(original).Moves.Min(m => m.ToZNm);
        var after = GcodeParser.Parse(levelled).Moves.Min(m => m.ToZNm);

        Assert.Equal(Mm(before), Mm(after), 4);
    }

    /// <summary>
    /// A move long enough to cross real curvature is broken up; one held in the air is not.
    ///
    /// Both halves matter. Without the first, the cut rides a straight line over a surface the map
    /// says is curved; without the second, every rapid across the board would be turned into a
    /// hundred lines to follow a shape it is nowhere near.
    /// </summary>
    [Fact]
    public void CuttingMovesAreBrokenUpAndTravelMovesAreNot()
    {
        var (text, _) = Leveller.Apply("""
            G21 G90
            G0 Z2.000
            G0 X0.000 Y0.000
            G1 Z-0.050 F60
            G1 X20.000 Y0.000 F200
            G0 Z2.000
            G0 X0.000 Y20.000
            M30
            """, Tilted(), new LevelOptions { SegmentMm = 1 });

        var moves = GcodeParser.Parse(text).Moves;

        // The 20 mm cut becomes twenty steps; the 20 mm retract-height traverse stays one move.
        Assert.Equal(20, moves.Count(m => m.MovesInPlane && m.ToZNm < 0));
        Assert.Equal(1, moves.Count(m => m.MovesInPlane && m.ToZNm > Nm.FromMillimetres(1)));
    }

    [Fact]
    public void ItSaysWhatItIsAtTheTop()
    {
        var (text, _) = Leveller.Apply("G21 G90\nG1 Z-0.05 F60\nM30", Tilted());
        var head = text.Split('\n').Take(8).ToList();

        Assert.Contains(head, l => l.Contains("LEVELLED", StringComparison.Ordinal));
        Assert.Contains(head, l => l.Contains("probe points", StringComparison.Ordinal));
        Assert.Contains(head, l => l.Contains("out of flat", StringComparison.Ordinal));
    }

    [Fact]
    public void CommentsSurvive()
    {
        var (text, _) = Leveller.Apply("""
            ( Board outline )
            G21 G90
            G1 X10.000 Y0.000 Z-0.050 F200  ( first pass )
            M30
            """, Tilted());

        Assert.Contains("( Board outline )", text, StringComparison.Ordinal);
        Assert.Contains("( first pass )", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ arcs

    /// <summary>
    /// A cutting arc cannot survive levelling: its Z now varies along its length in a way no
    /// <c>G2</c> can express. It becomes lines, which is the honest trade.
    /// </summary>
    [Fact]
    public void ACuttingArcBecomesLines()
    {
        var (text, report) = Leveller.Apply("""
            G21 G90
            G0 X10.000 Y0.000
            G1 Z-0.050 F60
            G2 X20.000 Y10.000 I0.000 J10.000 F200
            M30
            """, Tilted());

        Assert.Equal(1, report.ArcsExpanded);
        Assert.DoesNotContain("G2 ", text, StringComparison.Ordinal);

        // And the arc is still an arc in shape: every point stays on the circle it came from.
        var centre = P(10, 10);
        var points = GcodeParser.Parse(text).Moves.Where(m => m.MovesInPlane && m.ToZNm < 0).ToList();

        Assert.True(points.Count > 8, $"the arc should have become several lines, got {points.Count}");

        foreach (var move in points)
        {
            Assert.Equal(10, move.To.DistanceTo(centre) / Nm.PerMillimetre, 2);
        }
    }

    [Fact]
    public void AnArcInTheAirStaysAnArc()
    {
        var (text, _) = Leveller.Apply("""
            G21 G90
            G0 Z2.000
            G2 X20.000 Y10.000 I0.000 J10.000
            M30
            """, Tilted());

        Assert.Contains("G2 ", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ what it refuses

    /// <summary>
    /// Past the edge of the probed area the map holds its edge value, which is a good answer a
    /// millimetre out and a guess a centimetre out. Levelling a job that runs off the measured
    /// region would cut a whole area at the wrong depth — the exact failure levelling exists to
    /// prevent — so it stops instead.
    /// </summary>
    [Fact]
    public void APathThatRunsOffTheProbedAreaIsRefused()
    {
        var (text, report) = Leveller.Apply("""
            G21 G90
            G1 X0.000 Y0.000 Z-0.050 F200
            G1 X260.000 Y0.000 F200
            M30
            """, Tilted());

        Assert.NotNull(report.Refusal);
        Assert.Contains("outside the probed area", report.Refusal, StringComparison.Ordinal);

        // And the original comes back untouched, so nothing half-levelled can be run.
        Assert.Contains("X260.000", text, StringComparison.Ordinal);
    }

    /// <summary>Under G91 a Z word is a change rather than a position, so there is nothing to correct.</summary>
    [Fact]
    public void IncrementalModeIsRefused()
    {
        var (_, report) = Leveller.Apply("G21 G90\nG91\nG1 Z-1.0 F60\nM30", Tilted());

        Assert.NotNull(report.Refusal);
        Assert.Contains("G91", report.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ArcCentreModeIsNotMistakenForIncrementalMode()
    {
        var (_, report) = Leveller.Apply("G21 G90 G91.1\nG1 X1.000 Y1.000 Z-0.050 F60\nM30", Tilted());

        Assert.Null(report.Refusal);
    }

    /// <summary>
    /// A canned cycle's depth is one modal word shared by every hole on the lines after it. There
    /// is no way to give each hole its own without rewriting the cycle, and getting it wrong drills
    /// straight through the board — so it says what to do instead.
    /// </summary>
    [Fact]
    public void CannedDrillingCyclesAreRefusedWithSomethingToDoAboutIt()
    {
        var (_, report) = Leveller.Apply("""
            G21 G90
            G81 Z-1.900 R2.000 F60
            X10.000 Y10.000
            G80
            M30
            """, Tilted());

        Assert.NotNull(report.Refusal);
        Assert.Contains("G81", report.Refusal, StringComparison.Ordinal);
        Assert.Contains("canned cycles off", report.Refusal, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ on a real board

    /// <summary>
    /// The end-to-end shape of it: probe the board, read the log, level the program. The bow used
    /// here is 0.15 mm, which is what an ordinary piece of clamped FR4 does and three times the
    /// depth of the cut being levelled.
    /// </summary>
    [Fact]
    public void ARealBoardIsProbedAndLevelledEndToEnd()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var (routine, plan) = ProbeRoutine.Generate(board.Bounds);

        // Stand in for the operator: run the routine against a board bowed like a saddle, and write
        // the log GRBL would have written — machine coordinates and all.
        var log = string.Join("\n", GcodeParser.Parse(routine).Moves
            .Where(m => m.IsVertical && m.ToZNm < 0)
            .Select(m =>
            {
                // No shift applied here on purpose: the routine already emits work coordinates, so
                // if it ever stopped doing that this whole test would refuse rather than pass.
                var x = Mm(m.From.X);
                var y = Mm(m.From.Y);
                var z = -24.5 + (0.15 * Math.Sin(x / 12) * Math.Cos(y / 9));

                return FormattableString.Invariant($"[PRB:{x:F3},{y:F3},{z:F4}:1]");
            }));

        var (map, read) = ProbeLog.Read(log);

        Assert.NotNull(map);
        Assert.Equal(plan.PointCount, read.Samples.Count);
        output.WriteLine($"{plan.PointCount} touches, stock {map.RangeMm:F3} mm out of flat");

        var original = Isolation(RealBoards.PogoTest1);
        var (levelled, report) = Leveller.Apply(original, map);

        Assert.Null(report.Refusal);
        output.WriteLine($"corrections {report.MaxFallMm:F3} .. {report.MaxRiseMm:F3} mm, "
            + $"{report.MovesLevelled} moves, {report.SegmentsAdded} segments added");

        // Every cutting point sits its programmed depth below the measured surface, everywhere.
        var worst = 0.0;

        foreach (var move in GcodeParser.Parse(levelled).Moves.Where(m => m.ToZNm < 0 && m.MovesInPlane))
        {
            var depth = Mm(move.ToZNm) - Mm(map.SampleNm(move.To));
            worst = Math.Max(worst, Math.Abs(depth + 0.05));
        }

        output.WriteLine($"worst depth error: {worst * 1000:F2} µm");
        Assert.True(worst < 0.001, $"a point sat {worst:F4} mm off its intended depth");

        // Unlevelled, this board would have been cutting nothing in places and through the copper
        // in others: the bow is several times the depth of the cut.
        Assert.True(map.RangeMm > 0.1, "the test surface should be meaningfully bowed");
    }

    // ------------------------------------------------------------------ cuts that lift out

    /// <summary>
    /// A cut shallower than the stock is bowed comes out of the material once it is levelled, and
    /// the program has to say so.
    ///
    /// Found on a real coupon. A depth series started at 0.020 mm on scrap 0.027 mm out of flat;
    /// levelling faithfully raised every Z by the surface's own height and 79 segments of the first
    /// line ended up above the copper. Nothing on the machine could show it — the spindle turned,
    /// the axes moved, and a quarter of that line was cut in the air. The correction was right; the
    /// cut was never deep enough to survive it.
    /// </summary>
    [Fact]
    public void ACutShallowerThanTheBowIsReportedAsInTheAir()
    {
        var map = Tilted(0.0005);
        var program = string.Join(
            '\n',
            "G21 G90",
            "G0 X0 Y50 Z2",
            "G1 Z-0.020 F60",
            "G1 X100 Y50 F600",
            "G0 Z2");

        var (text, report) = Leveller.Apply(program, map);

        output.WriteLine($"{report.LiftedOut} lifted, worst {report.LiftedByMm:F4} mm, "
            + $"shallowest cut {report.ShallowestCutMm:F3} mm, bow {map.RangeMm:F3} mm");

        Assert.Null(report.Refusal);
        Assert.True(report.LiftedOut > 0, "a 0.020 mm cut on a 0.050 mm bow must lift out");
        Assert.True(report.LiftedByMm > 0);
        Assert.Equal(0.020, report.ShallowestCutMm, 3);

        // And it has to be in the file, because the file is what reaches the machine.
        Assert.Contains("in the air", text, StringComparison.Ordinal);
        Assert.Contains(report.Notes, n => n.Contains("at or above the surface", StringComparison.Ordinal));
    }

    /// <summary>A cut deeper than the bow stays in the material, and nothing is said.</summary>
    [Fact]
    public void ACutDeeperThanTheBowIsNotFlagged()
    {
        var map = Tilted(0.0005);
        var program = string.Join(
            '\n',
            "G21 G90",
            "G0 X0 Y50 Z2",
            "G1 Z-0.200 F60",
            "G1 X100 Y50 F600",
            "G0 Z2");

        var (text, report) = Leveller.Apply(program, map);

        Assert.Equal(0, report.LiftedOut);
        Assert.DoesNotContain("in the air", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rapid held above the work is meant to be above the work, and must not be counted.
    /// </summary>
    [Fact]
    public void RapidsAboveTheWorkAreNotCountedAsLiftedOut()
    {
        var map = Tilted(0.0005);
        var program = string.Join(
            '\n',
            "G21 G90",
            "G0 X0 Y50 Z2",
            "G0 X100 Y50 Z2",
            "G1 Z-0.200 F60",
            "G1 X0 Y50 F600",
            "G0 Z2");

        Assert.Equal(0, Leveller.Apply(program, map).Report.LiftedOut);
    }
}
