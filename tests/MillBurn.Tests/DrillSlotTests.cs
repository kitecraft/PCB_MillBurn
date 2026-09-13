using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Slotted holes, end to end — from the file on disk to the sentence the operator reads.
///
/// A slot is a stroke in the drill file, not a flash. For a long time nothing routed one: the slots
/// were parsed, realised, drawn on screen and counted in the layer's object total, so the picture
/// was entirely correct, and then they were dropped from the drilling program with nothing said
/// anywhere. The first symptom available to anybody was a connector that would not fit a finished
/// board.
///
/// They are routed now — <see cref="SlotRoutingTests"/> covers the cutting — and these tests pin
/// what the *drilling* item says about them, which is the half that was silent. The drilling
/// program still does not make them, because a run that alternates drills and end mills is a tool
/// change the companion page cannot describe honestly. It points at the file that does.
///
/// Found on a real Arduino Mega export — seven plated slots, silently absent. Neither board in the
/// committed corpus has a slot in it, which is why the fixture here is written rather than loaded.
/// </summary>
public sealed class DrillSlotTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "millburn-slots-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the test is not a test failure.
        }
    }

    /// <summary>Two holes at 1.0 mm and two slots at 0.6 mm, as KiCad writes them.</summary>
    private const string Drill = """
        %TF.FileFunction,Plated,1,2,PTH,Drill*%
        %FSLAX46Y46*%
        %MOMM*%
        %LPD*%
        G01*
        %ADD10C,1.000000*%
        %ADD11C,0.600000*%
        D10*
        X2000000Y2000000D03*
        X8000000Y2000000D03*
        D11*
        X2000000Y6000000D02*
        X3200000Y6000000D01*
        X6000000Y6000000D02*
        X7200000Y6000000D01*
        M02*
        """;

    private const string Outline = """
        %TF.FileFunction,Profile,NP*%
        %FSLAX46Y46*%
        %MOMM*%
        %LPD*%
        G01*
        %ADD10C,0.100000*%
        D10*
        X0Y0D02*
        X10000000Y0D01*
        X10000000Y8000000D01*
        X0Y8000000D01*
        X0Y0D01*
        M02*
        """;

    private ExportPlan Plan()
    {
        File.WriteAllText(Path.Combine(_folder, "slotted-PTH-drl.gbr"), Drill);
        File.WriteAllText(Path.Combine(_folder, "slotted-Edge_Cuts.gbr"), Outline);

        var loaded = BoardLoader.LoadFolder(_folder);

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        return ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);
    }

    private ExportItem Drilling() =>
        Plan().Items.Single(i => i.Operation == OperationKind.Drilling);

    // ------------------------------------------------------------------ the file is read whole

    [Fact]
    public void TheSlotsSurviveLoadingTheFolder()
    {
        File.WriteAllText(Path.Combine(_folder, "slotted-PTH-drl.gbr"), Drill);
        File.WriteAllText(Path.Combine(_folder, "slotted-Edge_Cuts.gbr"), Outline);

        var drill = BoardLoader.LoadFolder(_folder).Layers
            .Select(l => l.Drill)
            .OfType<MillBurn.Gerber.Excellon.ExcellonFile>()
            .Single();

        Assert.Equal(2, drill.Hits.Count);
        Assert.Equal(2, drill.Slots.Count);
    }

    // ------------------------------------------------------------------ and the program says so

    /// <summary>
    /// The whole point. Whatever else is true, the drilling item must not be silent about a feature
    /// it is not making — it now says where it *is* made.
    /// </summary>
    [Fact]
    public void TheExportSaysWhereTheSlotsAreMade()
    {
        var item = Drilling();

        output.WriteLine(string.Join("\n", item.Summary));
        output.WriteLine(string.Join("\n", item.Warnings));

        Assert.Contains(
            item.Summary,
            s => s.Contains("2 slots", StringComparison.Ordinal)
                && s.Contains(".slots.nc", StringComparison.Ordinal));
    }

    /// <summary>Said on the summary line, which is what the export window shows first.</summary>
    [Fact]
    public void TheSummaryCountsThemSeparatelyFromTheHoles()
    {
        var item = Drilling();

        Assert.Contains(item.Summary, s => s.Contains("2 holes in 1 size", StringComparison.Ordinal));
        Assert.Contains(item.Summary, s => s.Contains("2 slots", StringComparison.Ordinal));
    }

    /// <summary>
    /// A slot's width is a tool in the file but it is not a size any hole is drilled at, and
    /// counting it as one claimed a size that never gets used. The board that found this reported
    /// "258 holes in 7 sizes" while drilling six.
    /// </summary>
    [Fact]
    public void ASlotWidthIsNotReportedAsAHoleSize()
    {
        Assert.DoesNotContain(
            Drilling().Summary,
            s => s.Contains("2 sizes", StringComparison.Ordinal));
    }

    /// <summary>
    /// And the holes are still drilled. A warning that arrived at the cost of the program would
    /// be a poor trade.
    /// </summary>
    [Fact]
    public void TheHolesAreStillDrilled()
    {
        var moves = MillBurn.Gcode.GcodeParser.Parse(Drilling().Content).Moves;

        var plunges = moves
            .Where(m => !m.IsRapid && m.IsVertical && m.ToZNm < 0)
            .Select(m => m.From)
            .Distinct()
            .ToList();

        // Asserted as a separation rather than as two absolute coordinates: the outline is stroked
        // with a 0.1 mm pen, so the board's own corner — and therefore work zero — sits half a pen
        // width outside the rectangle the file draws.
        Assert.Equal(2, plunges.Count);
        Assert.Equal(Nm.FromMillimetres(6), Math.Abs(plunges[0].X - plunges[1].X));
        Assert.Equal(plunges[0].Y, plunges[1].Y);
    }

    // ------------------------------------------------------------------ and on the real board

    /// <summary>
    /// The board it was actually found on, end to end. The written fixture above pins the shape of
    /// the answer; this pins that a real KiCad export still produces it — which is the half that
    /// was wrong, since every synthetic drill fixture in this suite was made of flashes only.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.ArduinoUno, 153, 5)]
    [InlineData(RealBoards.ArduinoMega, 258, 6)]
    public void TheRealBoardsReportTheirSevenSlots(string board, int holes, int sizes)
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

        var plated = plan.Items.Single(
            i => i.Operation == OperationKind.Drilling && i.Role == LayerRole.PlatedDrill);

        output.WriteLine(string.Join("\n", plated.Summary));
        output.WriteLine(string.Join("\n", plated.Warnings));

        Assert.Contains(
            plated.Summary,
            s => s.Contains($"{holes} holes in {sizes} sizes", StringComparison.Ordinal));

        Assert.Contains(plated.Summary, s => s.Contains("7 slots", StringComparison.Ordinal));

        // And a routing program exists beside it, cutting the three the shipped library can reach
        // and naming the four it cannot.
        var slots = plan.Items.Single(i => i.TargetName.EndsWith(".slots.nc", StringComparison.Ordinal));

        output.WriteLine(string.Join("\n", slots.Summary));
        output.WriteLine(string.Join("\n", slots.Warnings));

        Assert.Contains(slots.Summary, s => s.Contains("3 of 7 slots", StringComparison.Ordinal));
        Assert.Contains(slots.Warnings, w => w.Contains("are NOT cut", StringComparison.Ordinal));
    }

    /// <summary>
    /// A board with no slots gains no warning and no extra summary line. A safety message that
    /// appears on every export is one nobody reads.
    /// </summary>
    [Fact]
    public void ABoardWithoutSlotsSaysNothingAboutThem()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

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

        Assert.DoesNotContain(
            plan.Items.SelectMany(i => i.Warnings.Concat(i.Summary)),
            s => s.Contains("slot", StringComparison.OrdinalIgnoreCase));
    }
}
