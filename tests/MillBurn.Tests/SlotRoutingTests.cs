using System.Collections.Immutable;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Routing the oval holes a drill cannot make.
///
/// Slots were read, drawn, reported and not made — the export said so, which was the least it could
/// do and not much more. The failure it was covering for is silent in the worst way available: the
/// picture on screen shows the slots, so there is nothing to notice until the connector will not
/// fit.
///
/// The Arduino Uno is the acceptance test because it exercises both halves at once with the shipped
/// library, and that is not a coincidence — it is the board that found the omission.
/// </summary>
public sealed class SlotRoutingTests(ITestOutputHelper output)
{
    private static readonly long Thickness = Nm.FromMillimetres(1.6);

    private static SlotOptions Options => new() { BoardThicknessNm = Thickness };

    /// <summary>A slot 20 mm long, whatever width is asked for.</summary>
    private static DrillSlotTarget Slot(double widthMm, int index = 0) => new(
        index,
        new Point2(0, 0),
        new Point2(Nm.FromMillimetres(20), 0),
        Nm.FromMillimetres(widthMm));

    private static ToolLibrary With(params Tool[] extra) =>
        new() { Tools = [.. ToolLibrary.Default.Tools, .. extra] };

    private static Tool EndMill(double diameterMm, double stepdownMm = 0.3, long maxDepthNm = 0) => new()
    {
        Name = $"{diameterMm:0.0} mm end mill",
        Kind = ToolKind.EndMill,
        DiameterNm = Nm.FromMillimetres(diameterMm),
        StepdownNm = Nm.FromMillimetres(stepdownMm),
        MaxDepthNm = maxDepthNm,
    };

    // ------------------------------------------------------------------ the acceptance test

    /// <summary>
    /// The Uno, with the shipped library: three of seven slots cut, four refused by width.
    ///
    /// Partial success is still success. A board with four impossible slots and three possible ones
    /// gets a program for the three — an all-or-nothing failure would leave the operator to route
    /// all seven by hand because of four.
    /// </summary>
    [Fact]
    public void TheUnoCutsThreeOfItsSevenSlotsAndNamesTheOtherFour()
    {
        var plan = Plan(RealBoards.ArduinoUno, ToolLibrary.Default);

        output.WriteLine($"{plan.CutCount} cut, {plan.RefusedCount} refused");
        foreach (var line in plan.Summary) { output.WriteLine("  " + line); }
        foreach (var r in plan.Refusals) { output.WriteLine("  refused: " + r.Reason); }

        Assert.Equal(3, plan.CutCount);
        Assert.Equal(4, plan.RefusedCount);

        var refusal = Assert.Single(plan.Refusals);
        Assert.Equal(Nm.FromMillimetres(0.6), refusal.WidthNm);
        Assert.Equal(4, refusal.Count);

        // Names the width and the cutter that would have to change, not just "no".
        Assert.Contains("0.80 mm", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the acceptance test: add a cutter narrow enough and all seven are cut,
    /// with nothing else about the program different.
    /// </summary>
    [Fact]
    public void AddingAHalfMillimetreEndMillCutsAllSeven()
    {
        var before = Plan(RealBoards.ArduinoUno, ToolLibrary.Default);
        var after = Plan(RealBoards.ArduinoUno, With(EndMill(0.5)));

        output.WriteLine($"before {before.CutCount}/{before.CutCount + before.RefusedCount}, "
            + $"after {after.CutCount}/{after.CutCount + after.RefusedCount}");

        Assert.Equal(7, after.CutCount);
        Assert.Empty(after.Refusals);

        // The three that were already being cut are cut the same way: a wider cutter fits the 1.0 mm
        // slots better than the new 0.5 mm one, and the chooser takes the largest that fits.
        Assert.Contains(after.Toolpaths, t => t.Tool.DiameterNm == Nm.FromMillimetres(1.0));
        Assert.Contains(after.Toolpaths, t => t.Tool.DiameterNm == Nm.FromMillimetres(0.5));
    }

    private static SlotPlan Plan(string board, ToolLibrary library)
    {
        var drill = BoardLoader.LoadFolder(RealBoards.Directory(board))
            .Layers.First(l => l.Drill is { Slots.Count: > 0 }).Drill!;

        var targets = drill.Slots
            .Select((s, i) => new DrillSlotTarget(i, s.From, s.To,
                drill.Tools.TryGetValue(s.Tool, out var t) ? t.DiameterNm : 0))
            .Where(s => s.WidthNm > 0)
            .ToList();

        return SlotOperation.Build(targets, library, Options);
    }

    // ------------------------------------------------------------------ the refusals

    /// <summary>
    /// A cutter that cannot reach through the board is refused for the depth, not the width — the
    /// two want different answers from the operator, a different cutter or a longer one.
    /// </summary>
    [Fact]
    public void ACutterThatCannotReachIsRefusedByDepth()
    {
        var library = new ToolLibrary
        {
            Tools = [EndMill(0.5, maxDepthNm: Nm.FromMillimetres(1.0))],
        };

        var plan = SlotOperation.Build([Slot(0.6)], library, Options);
        var refusal = Assert.Single(plan.Refusals);

        output.WriteLine(refusal.Reason);

        Assert.Contains("reaches", refusal.Reason, StringComparison.Ordinal);
        Assert.Contains("1.90", refusal.Reason, StringComparison.Ordinal);
        Assert.Empty(plan.Toolpaths);
    }

    /// <summary>A library with no end mills in it says so rather than naming a diameter.</summary>
    [Fact]
    public void ALibraryWithNoEndMillsSaysThat()
    {
        var library = new ToolLibrary { Tools = [Tool.DefaultVBit, Tool.DefaultDrill] };
        var refusal = Assert.Single(SlotOperation.Build([Slot(2.0)], library, Options).Refusals);

        Assert.Contains("no end mills", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Widest that fits, not narrowest available. A wider cutter clears the same slot in fewer
    /// passes and deflects less, and the constraint is only the slot's width.
    /// </summary>
    [Fact]
    public void TheWidestCutterThatFitsIsChosen()
    {
        var library = new ToolLibrary { Tools = [EndMill(0.5), EndMill(1.0), EndMill(3.0)] };
        var plan = SlotOperation.Build([Slot(2.0)], library, Options);

        Assert.Equal(Nm.FromMillimetres(1.0), Assert.Single(plan.Toolpaths).Tool.DiameterNm);
    }

    // ------------------------------------------------------------------ the geometry

    /// <summary>
    /// A cutter narrower than the slot runs a racetrack around the inside of it, not down the
    /// middle. Down the middle would leave the slot the width of the cutter.
    /// </summary>
    [Fact]
    public void ANarrowerCutterRunsARacetrack()
    {
        var library = new ToolLibrary { Tools = [EndMill(1.0)] };
        var pass = SlotOperation.Build([Slot(2.0)], library, Options).Toolpaths[0].Passes[0];

        Assert.True(pass.Closed, "a racetrack is a closed loop");

        // Every point on it is half a cutter inside the slot's own outline: 0.5 mm from the
        // centreline, which for a 2 mm slot and a 1 mm cutter is (2 - 1) / 2.
        var offsets = pass.Path.Select(s => Math.Abs(s.From.Y) / (double)Nm.PerMillimetre).ToList();

        output.WriteLine($"{pass.Path.Count} segments, offsets {offsets.Min():F3}..{offsets.Max():F3} mm");
        Assert.Equal(0.5, offsets.Max(), 2);
    }

    /// <summary>A cutter the slot's own width has nowhere to go but down the middle, in one line.</summary>
    [Fact]
    public void AnExactCutterGoesDownTheMiddle()
    {
        var library = new ToolLibrary { Tools = [EndMill(1.0)] };
        var pass = SlotOperation.Build([Slot(1.0)], library, Options).Toolpaths[0].Passes[0];

        Assert.False(pass.Closed);
        Assert.Single(pass.Path);
        Assert.Equal(Point2.Origin, pass.Path[0].From);
    }

    // ------------------------------------------------------------------ the motion

    /// <summary>
    /// It ramps rather than plunging. An end mill driven straight down into FR4 is how small
    /// cutters break, and a slot is already a path to descend along.
    /// </summary>
    [Fact]
    public void EveryDepthPassRampsExceptTheLast()
    {
        var library = new ToolLibrary { Tools = [EndMill(1.0, stepdownMm: 0.5)] };
        var passes = SlotOperation.Build([Slot(1.0)], library, Options).Toolpaths[0].Passes;

        output.WriteLine(string.Join(", ", passes.Select(p =>
            $"{Nm.ToMillimetreString(p.RampFromNm ?? 0, 2)}→{Nm.ToMillimetreString(p.DepthNm, 2)}")));

        // 1.9 mm at 0.5 mm a step is four ramped passes, then one flat lap to take the slope out of
        // the floor the ramp left.
        Assert.Equal(5, passes.Count);
        Assert.All(passes.Take(4), p => Assert.True(p.Ramps));
        Assert.False(passes[^1].Ramps);
        Assert.Equal(Nm.FromMillimetres(1.9), passes[^1].DepthNm);

        // Each one picks up where the last left off, so there is never a vertical entry mid-cut.
        for (var i = 1; i < 4; i++)
        {
            Assert.Equal(passes[i - 1].DepthNm, passes[i].RampFromNm);
        }
    }

    /// <summary>An open slot is cut out and back, so no pass has to rapid home before the next.</summary>
    [Fact]
    public void AnOpenSlotAlternatesDirection()
    {
        var library = new ToolLibrary { Tools = [EndMill(1.0, stepdownMm: 0.5)] };
        var passes = SlotOperation.Build([Slot(1.0)], library, Options).Toolpaths[0].Passes;

        for (var i = 1; i < passes.Count; i++)
        {
            Assert.Equal(passes[i - 1].End, passes[i].Start);
        }
    }

    /// <summary>
    /// The ramp reaches the G-code: a cutting move that changes Z along its length, ending exactly
    /// on the depth the pass asked for.
    ///
    /// Checked through the emitter and back out through the parser, like everything else here that
    /// makes a claim about a program — the toolpath and the file agree right up until the emitter
    /// has a bug, and only one of them is what the machine runs.
    /// </summary>
    [Fact]
    public void TheRampIsInTheEmittedProgram()
    {
        var library = new ToolLibrary { Tools = [EndMill(1.0, stepdownMm: 1.0)] };
        var plan = SlotOperation.Build([Slot(1.0)], library, Options);

        var (text, _) = GcodeEmitter.Emit(
            new Job { Name = "slots", Toolpaths = plan.Toolpaths }, new GcodeOptions());

        output.WriteLine(text);

        var sloped = GcodeParser.Parse(text).Moves
            .Where(m => !m.IsRapid && m.MovesInPlane && m.FromZNm != m.ToZNm)
            .ToList();

        Assert.NotEmpty(sloped);
        Assert.Contains(sloped, m => m.ToZNm == -Nm.FromMillimetres(1.9));

        // And the tool never enters virgin material straight down: the first time it goes below the
        // surface anywhere, it is moving in X as it does. Later vertical moves re-enter the slot at
        // a depth that slot has already been cut to, which is air.
        var first = GcodeParser.Parse(text).Moves.First(m => !m.IsRapid && m.ToZNm < m.FromZNm);

        Assert.True(first.MovesInPlane || first.ToZNm >= 0,
            $"the first descent was straight down to {Nm.ToMillimetreString(-first.ToZNm, 2)} mm");
    }
}
