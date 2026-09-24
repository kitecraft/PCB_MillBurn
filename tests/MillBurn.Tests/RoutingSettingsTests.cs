using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Roadmap 6.13's settings: what a routed hole or slot does with a depth that is not a whole number
/// of laps, and whether a through cut gets a flat lap anyway.
///
/// The case they were asked for is the test board: 0.8 mm thick, 0.3 mm of break-through, an 0.8 mm
/// end mill that steps 0.5 mm — 1.10 mm in laps of 0.50 + 0.50 + 0.10, the last one a sliver.
/// </summary>
public sealed class RoutingSettingsTests(ITestOutputHelper output)
{
    private static long Mm(double mm) => Nm.FromMillimetres(mm);

    private static readonly Tool EndMill = new()
    {
        Id = Guid.NewGuid(),
        Name = "0.8 mm end mill",
        Kind = ToolKind.EndMill,
        DiameterNm = Mm(0.8),
        StepdownNm = Mm(0.5),
    };

    private static SlotOptions TestBoard(ShortLastLap lap = ShortLastLap.OwnLap, bool finishing = false) => new()
    {
        BoardThicknessNm = Mm(0.8),
        BreakThroughNm = Mm(0.3),
        ToolId = EndMill.Id,
        ShortLastLap = lap,
        FinishingLapOnThroughCuts = finishing,
    };

    /// <summary>A 2.2 mm hole, the size the test board mills.</summary>
    private static SlotPlan Hole(SlotOptions options) => SlotOperation.Holes(
        [new DrillSlotTarget(0, new Point2(Mm(5), Mm(5)), new Point2(Mm(5), Mm(5)), Mm(2.2))],
        new ToolLibrary { Tools = [EndMill] },
        options);

    // ------------------------------------------------------------------ the arithmetic

    [Theory]
    [InlineData(ShortLastLap.OwnLap, new[] { 0.5, 1.0, 1.1 })]
    [InlineData(ShortLastLap.SpreadEvenly, new[] { 0.3667, 0.7333, 1.1 })]
    [InlineData(ShortLastLap.FoldIn, new[] { 0.5, 1.1 })]
    public void TheShortLastLapIsWhatTheSettingSays(ShortLastLap policy, double[] expectedMm)
    {
        var depths = Laps.Depths(Mm(1.1), Mm(0.5), policy);

        output.WriteLine($"{policy}: {Laps.Describe(depths)}");

        Assert.Equal(expectedMm.Length, depths.Count);

        for (var i = 0; i < depths.Count; i++)
        {
            Assert.Equal(expectedMm[i], Nm.ToMillimetres(depths[i]), 3);
        }

        // Always exactly the depth asked for, never a micron past it.
        Assert.Equal(Mm(1.1), depths[^1]);
    }

    /// <summary>Spread evenly never asks more of a cutter than its stepdown.</summary>
    [Fact]
    public void SpreadEvenlyIsNeverDeeperThanTheStepdown()
    {
        foreach (var depth in new[] { 0.3, 1.1, 1.3, 1.9, 2.0, 3.35 })
        {
            var depths = Laps.Depths(Mm(depth), Mm(0.5), ShortLastLap.SpreadEvenly);
            var previous = 0L;

            foreach (var d in depths)
            {
                Assert.True(d - previous <= Mm(0.5), $"{depth} mm: a lap of {Nm.ToMillimetreString(d - previous, 3)} mm");
                previous = d;
            }
        }
    }

    /// <summary>
    /// Fold in only folds a sliver. A last lap of a quarter of a step or more is a real lap, and
    /// folding it would ask the cutter for a step and a quarter or more.
    /// </summary>
    [Fact]
    public void FoldInLeavesALastLapOfAQuarterStepOrMoreAlone()
    {
        Assert.Equal(
            Laps.Depths(Mm(1.3), Mm(0.5)),
            Laps.Depths(Mm(1.3), Mm(0.5), ShortLastLap.FoldIn));

        // And a depth that is already a whole number of laps is the same whatever the setting.
        Assert.Equal(Laps.Depths(Mm(1.0), Mm(0.5)), Laps.Depths(Mm(1.0), Mm(0.5), ShortLastLap.FoldIn));
        Assert.Equal(Laps.Depths(Mm(1.0), Mm(0.5)), Laps.Depths(Mm(1.0), Mm(0.5), ShortLastLap.SpreadEvenly));
    }

    // ------------------------------------------------------------------ the routing

    /// <summary>
    /// The laps the program cuts follow the setting, and the flat lap follows the floor: skipped
    /// only when the last ramp already starts below the board, unless the settings ask for it.
    /// </summary>
    [Theory]
    [InlineData(ShortLastLap.OwnLap, false, new[] { 0.5, 1.0, 1.1 }, false)]
    [InlineData(ShortLastLap.SpreadEvenly, false, new[] { 0.3667, 0.7333, 1.1 }, true)]
    [InlineData(ShortLastLap.FoldIn, false, new[] { 0.5, 1.1 }, true)]
    [InlineData(ShortLastLap.OwnLap, true, new[] { 0.5, 1.0, 1.1 }, true)]
    public void TheHolesLapsFollowTheSettings(ShortLastLap policy, bool finishing, double[] rampsMm, bool flat)
    {
        var plan = Hole(TestBoard(policy, finishing));
        var passes = Assert.Single(plan.Toolpaths).Passes;

        output.WriteLine(string.Join("\n", passes.Select(p =>
            $"{(p.Ramps ? "ramp" : "flat")} to {Nm.ToMillimetreString(p.DepthNm, 3)}")));

        var ramps = passes.Where(p => p.Ramps).ToList();
        Assert.Equal(rampsMm.Length, ramps.Count);

        for (var i = 0; i < ramps.Count; i++)
        {
            Assert.Equal(rampsMm[i], Nm.ToMillimetres(ramps[i].DepthNm), 3);
        }

        Assert.Equal(flat, passes.Any(p => !p.Ramps));
        Assert.Equal(Mm(1.1), passes.Max(p => p.DepthNm));
    }

    /// <summary>
    /// The program says where every number came from: the cutter's stepdown and feed, the layer's
    /// break-through, and — when there is a short lap at all — the setting that decided it.
    /// </summary>
    [Fact]
    public void TheProgramSaysWhereEachNumberCameFrom()
    {
        var notes = Assert.Single(Hole(TestBoard(ShortLastLap.SpreadEvenly)).Toolpaths).Notes;

        output.WriteLine(string.Join("\n", notes));

        Assert.Contains(notes, n => n.Contains("laps of 0.37 + 0.37 + 0.37 mm", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Contains("the 0.8 mm end mill's 0.50 mm stepdown", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Contains("0.30 mm of it the layer's break-through", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Contains("spread evenly (Settings > Milling)", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Contains("one helix all the way down", StringComparison.Ordinal));

        // Fold in with a short lap too deep to fold — 1.20 mm is 0.50 + 0.50 + 0.20 — says it was
        // kept rather than claiming a fold the laps beside it contradict.
        var deep = Assert.Single(Hole(TestBoard(ShortLastLap.FoldIn) with { BoardThicknessNm = Mm(0.9) }).Toolpaths).Notes;
        output.WriteLine(string.Join("\n", deep));
        Assert.Contains(deep, n => n.Contains("laps of 0.50 + 0.50 + 0.20 mm", StringComparison.Ordinal));
        Assert.Contains(deep, n => n.Contains("too deep to fold in", StringComparison.Ordinal));
        Assert.DoesNotContain(deep, n => n.Contains("folded into", StringComparison.Ordinal));

        // With nothing short about it, the setting is not mentioned at all.
        var even = Assert.Single(Hole(TestBoard() with { BreakThroughNm = Mm(0.2) }).Toolpaths).Notes;
        Assert.DoesNotContain(even, n => n.Contains("Settings > Milling", StringComparison.Ordinal));
    }

    /// <summary>The whole way through the planner: the machine settings reach the routing file.</summary>
    [Fact]
    public void TheMachineSettingsReachTheRoutingFile()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, new ToolLibrary { Tools = [.. ToolLibrary.Default.Tools, EndMill] },
            Mm(0.8), OutputKind.Gcode,
            machineSettings: new MachineSettings { ShortLastLap = ShortLastLap.SpreadEvenly },
            job: new JobOptions { MillLargeHoles = true, MillDrillToolId = EndMill.Id });

        var routed = plan.Items.Where(i => i.TargetName.Contains(".slots.", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(routed);

        foreach (var file in routed)
        {
            Assert.Contains("spread evenly", file.Content, StringComparison.Ordinal);

            // Still one continuous descent per feature, whatever the laps are.
            var entries = GcodeParser.Parse(file.Content).Moves.Count(m => m.FromZNm >= 0 && m.ToZNm < 0);
            Assert.Equal(AlignmentTest.Targets(file.Content).Count, entries);
        }
    }

    /// <summary>
    /// The stock's alignment holes are pecked by the same rule. With the built-in outline bit's 0.4 mm
    /// stepdown a 1.1 mm hole was 0.4 + 0.4 + 0.3; spread evenly it is three pecks of 0.367.
    /// </summary>
    [Fact]
    public void TheStocksHolesArePeckedByTheSameRule()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);

        var job = new JobOptions
        {
            Blank = new BlankOptions
            {
                Enabled = true, LeftMm = 10, RightMm = 10, BottomMm = 10, TopMm = 10, AlignmentHoles = true,
            },
        };

        string Pecks(ShortLastLap lap)
        {
            var (item, _) = ExportPlanner.PlanBlank(
                loaded, settings, ToolLibrary.Default, Mm(0.8),
                machineSettings: new MachineSettings { ShortLastLap = lap }, job: job);

            Assert.NotNull(item);

            // The plunges into the first hole: every feed straight down, until the tool leaves it.
            var holes = item.Content[item.Content.IndexOf("( Alignment holes )", StringComparison.Ordinal)..];
            var first = holes[..holes.IndexOf("G0 Z2.000", StringComparison.Ordinal)];

            return string.Join(", ", first.Split('\n').Where(l => l.StartsWith("G1 Z", StringComparison.Ordinal)));
        }

        var own = Pecks(ShortLastLap.OwnLap);
        var spread = Pecks(ShortLastLap.SpreadEvenly);

        output.WriteLine($"own lap: {own}\nspread:  {spread}");

        Assert.Equal("G1 Z-0.400 F60, G1 Z-0.800 F60, G1 Z-1.100 F60", own);
        Assert.Equal("G1 Z-0.367 F60, G1 Z-0.733 F60, G1 Z-1.100 F60", spread);
    }
}
