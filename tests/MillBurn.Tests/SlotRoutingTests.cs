using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
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
        // the floor the ramp left. The last ramp starts at 1.5 mm, still inside a 1.6 mm board, so
        // that floor is real.
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

    /// <summary>
    /// Under a through cut the flat lap may have nothing to flatten. On a 0.8 mm board with 0.3 mm
    /// break-through and a 0.5 mm step, the last ramp starts at 1.0 mm — already below the underside
    /// — so the laps are 0 to 0.5, 0.5 to 1.0 and 1.0 to 1.1 mm, and that is the whole cut.
    /// </summary>
    [Fact]
    public void AThroughCutWhoseLastRampIsBelowTheBoardHasNoFloorLap()
    {
        var library = new ToolLibrary { Tools = [EndMill(0.8, stepdownMm: 0.5)] };
        var options = new SlotOptions
        {
            BoardThicknessNm = Nm.FromMillimetres(0.8),
            BreakThroughNm = Nm.FromMillimetres(0.3),
        };

        var passes = SlotOperation.Build([Slot(1.0)], library, options).Toolpaths[0].Passes;

        output.WriteLine(string.Join(", ", passes.Select(p =>
            $"{Nm.ToMillimetreString(p.RampFromNm ?? p.DepthNm, 2)}→{Nm.ToMillimetreString(p.DepthNm, 2)}")));

        Assert.Equal(3, passes.Count);
        Assert.All(passes, p => Assert.True(p.Ramps));
        Assert.Equal(Nm.FromMillimetres(1.0), passes[^1].RampFromNm);
        Assert.Equal(Nm.FromMillimetres(1.1), passes[^1].DepthNm);
    }

    /// <summary>
    /// On the board that found it, through the whole export: each hole and slot is entered once, and
    /// its laps follow on without lifting.
    ///
    /// Counted from the emitted program — descents from above the surface into the material, against
    /// the features the program cuts — so it holds whatever the laps are. Before the fix every lap was
    /// its own entry: four per hole on this board.
    /// </summary>
    [Fact]
    public void OnTheTestBoardEachHoleAndSlotIsEnteredOnce()
    {
        var endMill = new Tool
        {
            Id = Guid.NewGuid(),
            Name = "0.8 mm end mill",
            Kind = ToolKind.EndMill,
            DiameterNm = Nm.FromMillimetres(0.8),
            StepdownNm = Nm.FromMillimetres(0.5),
        };

        var library = new ToolLibrary { Tools = [.. ToolLibrary.Default.Tools, endMill] };
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, library, Nm.FromMillimetres(0.8), OutputKind.Gcode,
            job: new JobOptions { MillLargeHoles = true, MillDrillToolId = endMill.Id });

        var routed = plan.Items.Where(i => i.TargetName.Contains(".slots.", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(routed);

        foreach (var file in routed)
        {
            var moves = GcodeParser.Parse(file.Content).Moves;
            var entries = moves.Count(m => m.FromZNm >= 0 && m.ToZNm < 0);
            var features = AlignmentTest.Targets(file.Content).Count;

            output.WriteLine($"{file.TargetName}: {features} features, {entries} entries into the material");

            Assert.True(features > 0);
            Assert.Equal(features, entries);
            Assert.Contains(file.Summary, s => s.StartsWith("One continuous descent per feature", StringComparison.Ordinal));

            // A lap that carries on starts where the tool already is, and writes no move to get there.
            Assert.DoesNotContain(moves, m => !m.IsRapid && m.From == m.To && m.FromZNm == m.ToZNm);
        }
    }

    // ------------------------------------------------------------------ mill-drill

    /// <summary>
    /// A hole is a slot whose two ends coincide, and the geometry falls out of that: inflating a
    /// zero-length line by the clearance gives a circle, and a circle in a ramped pass is a helix.
    /// </summary>
    [Fact]
    public void AHoleIsSpiralledOutAsACircle()
    {
        var library = new ToolLibrary { Tools = [EndMill(1.0)] };
        var hole = new DrillSlotTarget(0, new Point2(0, 0), new Point2(0, 0), Nm.FromMillimetres(3.2));

        var pass = SlotOperation.Holes([hole], library, Options).Toolpaths[0].Passes[0];

        Assert.True(pass.Closed);
        Assert.True(pass.Ramps);

        // Every point orbits at (3.2 - 1.0) / 2 from the centre.
        var radii = pass.Path
            .Select(seg => Math.Sqrt((seg.From.X * (double)seg.From.X) + (seg.From.Y * (double)seg.From.Y))
                / Nm.PerMillimetre)
            .ToList();

        output.WriteLine($"{pass.Path.Count} segments, radius {radii.Min():F3}..{radii.Max():F3} mm");
        Assert.Equal(1.1, radii.Max(), 2);
        Assert.Equal(1.1, radii.Min(), 2);
    }

    /// <summary>
    /// A cutter needs room to spiral. One the same size as the hole is a drill being asked to be a
    /// mill — it would descend on its own axis with every flute buried, which is the plunge this
    /// exists to avoid.
    /// </summary>
    [Fact]
    public void AHoleBarelyWiderThanTheCutterIsRefused()
    {
        var library = new ToolLibrary { Tools = [EndMill(1.0)] };
        var hole = new DrillSlotTarget(0, Point2.Origin, Point2.Origin, Nm.FromMillimetres(1.05));

        var refusal = Assert.Single(SlotOperation.Holes([hole], library, Options).Refusals);

        output.WriteLine(refusal.Reason);

        Assert.Contains("too big to drill and too small to mill", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Off by default. Turning it on changes a hole from drilled to milled — a different tool, a
    /// different motion and a different finish — and it must not happen to somebody who never asked.
    /// </summary>
    [Fact]
    public void NothingIsMilledUnlessTheProjectSaysSo()
    {
        var plan = PlanBoard(RealBoards.ArduinoUno, JobOptions.Default);

        Assert.DoesNotContain(plan.Items, i =>
            i.Summary.Any(s => s.Contains("across", StringComparison.Ordinal)));
    }

    /// <summary>
    /// With it on, holes bigger than the largest drill in the library leave the drilling program and
    /// arrive in the routing one. The threshold is the library because the library is already a
    /// claim about what is in the drawer.
    /// </summary>
    [Fact]
    public void LargeHolesMoveFromTheDrillProgramToTheRoutingOne()
    {
        // The stock library's only drill is 1.0 mm, so PogoTest1's 1.7 mm and 2.2 mm holes are both
        // bigger than anything the operator has listed.
        var off = PlanBoard(RealBoards.PogoTest1, JobOptions.Default);
        var on = PlanBoard(RealBoards.PogoTest1, new JobOptions { MillLargeHoles = true });

        var drilledOff = Drilled(off);
        var drilledOn = Drilled(on);

        output.WriteLine($"drilled with it off: {drilledOff}, on: {drilledOn}");

        Assert.True(drilledOn < drilledOff, "milling large holes should leave fewer to drill");

        var routed = on.Items
            .Where(i => i.TargetName.Contains(".slots.", StringComparison.Ordinal))
            .ToList();

        foreach (var file in routed)
        {
            output.WriteLine(file.TargetName + ": " + string.Join(" · ", file.Summary));
        }

        Assert.NotEmpty(routed);

        // A layer's routing may be one file per cutter; its own summary is on the first of them.
        Assert.All(
            routed.GroupBy(r => r.LayerFileName),
            layer => Assert.Contains(layer.First().Summary, s => s.Contains("mm across", StringComparison.Ordinal)));

        // The non-plated file's only size is 2.20 mm, so once that is milled it has nothing left to
        // drill at all. It must still get its routing program rather than being written off as
        // "nothing to cut" — which is about the drilling program, not about the layer.
        Assert.Contains(routed, r => r.TargetName.Contains("NPTH", StringComparison.Ordinal));
    }

    private static int Drilled(ExportPlan plan) => plan.Items
        .Where(i => i.Operation == OperationKind.Drilling)
        .Sum(i => GcodeParser.Parse(i.Content).Moves.Count(m => !m.IsRapid && m.ToZNm < 0 && !m.MovesInPlane));

    private static ExportPlan PlanBoard(string board, JobOptions job)
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

        return ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode, job: job);
    }

    // ------------------------------------------------------------------ the named cutter

    /// <summary>
    /// A cutter named in the project that is too wide for a slot does not starve it.
    ///
    /// Reported from the workshop, and it is the obvious situation rather than a corner: "I want
    /// holes larger than 2 mm milled with a 2 mm end mill. But the slots on the board are less than
    /// 2 mm, so when I choose the mill-drill option with a 2 mm end mill, the slots are left out."
    ///
    /// They were, and that was wrong. A slot's width is fixed by the design and the cutter has to
    /// fit inside it; an opinion about which cutter suits a 3 mm mounting hole says nothing
    /// whatsoever about a 0.8 mm slot on the same board.
    /// </summary>
    [Fact]
    public void NamingACutterForHolesDoesNotStarveTheSlots()
    {
        var library = new ToolLibrary { Tools = [EndMill(0.8), EndMill(2.0)] };
        var big = library.Tools[1];

        var options = Options with { ToolId = big.Id };

        var slots = SlotOperation.Build([Slot(1.0)], library, options);
        var holes = SlotOperation.Holes(
            [new DrillSlotTarget(0, Point2.Origin, Point2.Origin, Nm.FromMillimetres(3.0))],
            library,
            options);

        output.WriteLine($"slot: {(slots.Toolpaths.Count > 0 ? slots.Toolpaths[0].Tool.Name : "refused")}");
        output.WriteLine($"hole: {(holes.Toolpaths.Count > 0 ? holes.Toolpaths[0].Tool.Name : "refused")}");

        Assert.Empty(slots.Refusals);
        Assert.Equal(Nm.FromMillimetres(0.8), Assert.Single(slots.Toolpaths).Tool.DiameterNm);
        Assert.Equal(Nm.FromMillimetres(2.0), Assert.Single(holes.Toolpaths).Tool.DiameterNm);
    }

    /// <summary>
    /// And it is a preference, not a rule. A 2 mm cutter cannot bore a 2.2 mm hole — boring wants a
    /// cutter no more than three quarters of the hole — and refusing to make the hole at all
    /// because of a preference is worse than making it with a narrower cutter. The page beside the
    /// file lists a row per cutter, so the fallback is visible rather than silent.
    /// </summary>
    [Fact]
    public void ANamedCutterThatCannotBoreThisHoleFallsBack()
    {
        var library = new ToolLibrary { Tools = [EndMill(1.0), EndMill(2.0)] };
        var big = library.Tools[1];
        var options = Options with { ToolId = big.Id };

        var tight = SlotOperation.Holes(
            [new DrillSlotTarget(0, Point2.Origin, Point2.Origin, Nm.FromMillimetres(2.2))],
            library, options);

        var roomy = SlotOperation.Holes(
            [new DrillSlotTarget(0, Point2.Origin, Point2.Origin, Nm.FromMillimetres(3.0))],
            library, options);

        output.WriteLine($"2.2 mm hole: {Assert.Single(tight.Toolpaths).Tool.Name}");
        output.WriteLine($"3.0 mm hole: {Assert.Single(roomy.Toolpaths).Tool.Name}");

        Assert.Equal(Nm.FromMillimetres(1.0), tight.Toolpaths[0].Tool.DiameterNm);
        Assert.Equal(Nm.FromMillimetres(2.0), roomy.Toolpaths[0].Tool.DiameterNm);

        Assert.Empty(tight.Refusals);
    }

    /// <summary>
    /// But a named cutter that fits the slot cuts it.
    ///
    /// Reported from the workshop: with a 0.8 mm end mill chosen, the 1 mm slots still took the
    /// library's 1 mm cutter, and since routing is one file per cutter, one routing job became two
    /// files and a tool change that the chosen cutter would have saved.
    /// </summary>
    [Fact]
    public void ANamedCutterThatFitsTheSlotCutsIt()
    {
        var library = new ToolLibrary { Tools = [EndMill(0.8), EndMill(1.0)] };
        var chosen = library.Tools[0];

        var named = SlotOperation.Build([Slot(1.0)], library, Options with { ToolId = chosen.Id });
        var unnamed = SlotOperation.Build([Slot(1.0)], library, Options);

        output.WriteLine($"named: {Assert.Single(named.Toolpaths).Tool.Name}, unnamed: {Assert.Single(unnamed.Toolpaths).Tool.Name}");

        Assert.Empty(named.Refusals);
        Assert.Equal(chosen.Id, named.Toolpaths[0].Tool.Id);

        // Narrower than the slot, so it runs a racetrack inside it rather than a line down the middle.
        Assert.All(named.Toolpaths[0].Passes, p => Assert.True(p.Closed));

        // Left to itself, the widest that fits, as before.
        Assert.Equal(Nm.FromMillimetres(1.0), unnamed.Toolpaths[0].Tool.DiameterNm);
    }

    /// <summary>A named cutter that has since left the library is not a refusal either.</summary>
    [Fact]
    public void ACutterNoLongerInTheLibraryFallsBack()
    {
        var library = new ToolLibrary { Tools = [EndMill(1.0)] };
        var options = Options with { ToolId = Guid.NewGuid() };

        var plan = SlotOperation.Holes(
            [new DrillSlotTarget(0, Point2.Origin, Point2.Origin, Nm.FromMillimetres(3.0))],
            library, options);

        Assert.Empty(plan.Refusals);
        Assert.Equal(Nm.FromMillimetres(1.0), Assert.Single(plan.Toolpaths).Tool.DiameterNm);
    }
}
