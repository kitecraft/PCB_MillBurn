using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Which bit a layer is cut with when nobody has picked one.
///
/// The planner and the layer drawer used to answer this differently. The planner fell straight
/// through to a hard-coded tool whenever a layer named none — which is every layer of a folder the
/// window has never opened — while the drawer took the first suitable tool in the operator's own
/// library. On one real library that was a 0.127 mm cut against a 0.032 mm one: four isolation
/// passes against fifteen, an hour of cutting against three, and a different set of gaps reported
/// as unreachable. Both files looked entirely correct, and the README claimed a job exported either
/// way came out identical.
/// </summary>
public sealed class DefaultToolTests(ITestOutputHelper output)
{
    /// <summary>A library holding the shipped tools, with the 30° V-bit's tip ground finer.</summary>
    private static IReadOnlyList<Tool> EditedLibrary =>
    [
        Tool.DefaultVBit with { TipNm = Nm.FromMillimetres(0.005) },
        .. ToolLibrary.Default.Tools.Where(t => t.Id != Tool.DefaultVBit.Id),
    ];

    /// <summary>
    /// Editing a shipped tool keeps its identity, so the edited version is what gets used. This is
    /// the case that actually happens: somebody grinds the tip on their 30° bit, corrects the number
    /// in the library, and expects every export to know.
    /// </summary>
    [Fact]
    public void AnEditedShippedToolIsStillTheDefault()
    {
        var tool = LayerOperations.DefaultToolFor(OperationKind.Isolation, EditedLibrary);

        output.WriteLine($"{tool.Name} cuts {Nm.ToMillimetreString(tool.WidthAtDepth(Nm.FromMillimetres(0.05)), 3)} mm at 0.05 mm");

        Assert.Equal(Tool.DefaultVBit.Id, tool.Id);
        Assert.Equal(Nm.FromMillimetres(0.005), tool.TipNm);
    }

    /// <summary>
    /// The built-in is preferred over merely-first when the library holds it. Otherwise adding one
    /// finer V-bit would silently re-cut every isolation job with it, and adding a 0.8 mm end mill
    /// would re-cut every outline with a cutter thinner than the one that was chosen.
    /// </summary>
    [Fact]
    public void AddingAToolDoesNotHijackTheDefault()
    {
        List<Tool> withExtras =
        [
            Tool.DefaultVBit with { Id = Guid.NewGuid(), Name = "10° engraver", IncludedAngleDegrees = 10 },
            .. ToolLibrary.Default.Tools,
        ];

        Assert.Equal(Tool.DefaultVBit.Id, LayerOperations.DefaultToolFor(OperationKind.Isolation, withExtras).Id);
        Assert.Equal(Tool.DefaultOutlineMill.Id, LayerOperations.DefaultToolFor(OperationKind.Outline, withExtras).Id);
        Assert.Equal(Tool.DefaultDrill.Id, LayerOperations.DefaultToolFor(OperationKind.Drilling, withExtras).Id);
    }

    /// <summary>
    /// With the shipped tool deleted, the first one of the right kind is used — the library is what
    /// the operator owns, and a plan cut with a bit they threw away is no use to anybody.
    /// </summary>
    [Fact]
    public void WithoutTheShippedToolTheLibraryStillDecides()
    {
        List<Tool> mine =
        [
            Tool.DefaultVBit with { Id = Guid.NewGuid(), Name = "45° V", IncludedAngleDegrees = 45 },
            Tool.DefaultOutlineMill with { Id = Guid.NewGuid(), Name = "3.0 mm end mill", DiameterNm = Nm.FromMillimetres(3) },
        ];

        Assert.Equal("45° V", LayerOperations.DefaultToolFor(OperationKind.Isolation, mine).Name);
        Assert.Equal("3.0 mm end mill", LayerOperations.DefaultToolFor(OperationKind.Outline, mine).Name);
    }

    /// <summary>An empty library falls back to the built-in rather than refusing to plan.</summary>
    [Fact]
    public void AnEmptyLibraryStillGetsAPlan()
    {
        Assert.Equal(Tool.DefaultVBit.Id, LayerOperations.DefaultToolFor(OperationKind.Isolation, []).Id);
        Assert.Equal(Tool.DefaultOutlineMill.Id, LayerOperations.DefaultToolFor(OperationKind.Outline, []).Id);
        Assert.Equal(Tool.DefaultDrill.Id, LayerOperations.DefaultToolFor(OperationKind.Drilling, []).Id);
    }

    /// <summary>
    /// And the planner honours it end to end: a board whose layers name no tool is cut with the
    /// library's bit, not with a constant in the source.
    /// </summary>
    [Fact]
    public void ThePlannerCutsWithTheLibrarysBit()
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
            loaded,
            settings,
            new ToolLibrary { Tools = [.. EditedLibrary] },
            Nm.FromMillimetres(1.6),
            OutputKind.Gcode);

        var isolation = plan.Items.First(i => i.Operation == OperationKind.Isolation);

        output.WriteLine(string.Join("\n", isolation.Summary));

        // The finer tip cuts 0.032 mm, not the shipped 0.127 mm.
        Assert.Contains(isolation.Summary, s => s.Contains("0.032 mm", StringComparison.Ordinal));
    }
}
