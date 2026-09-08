using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Milling cured soldermask off the pads.
///
/// A real workflow, and the one operation here whose whole depth is smaller than the flatness of an
/// ordinary piece of copper-clad. That is not a detail to mention in passing: at 20–40 µm of mask, a
/// board that looks flat will leave mask on one pad and cut into the copper of another from the
/// same program.
/// </summary>
public sealed class PocketOperationTests
{
    private static Path64 Square(double x, double y, double side) =>
    [
        new(Nm.FromMillimetres(x), Nm.FromMillimetres(y)),
        new(Nm.FromMillimetres(x + side), Nm.FromMillimetres(y)),
        new(Nm.FromMillimetres(x + side), Nm.FromMillimetres(y + side)),
        new(Nm.FromMillimetres(x), Nm.FromMillimetres(y + side)),
    ];

    /// <summary>A 0.5 mm flat cutter: simple to reason about, unlike a V-bit whose width follows depth.</summary>
    private static readonly PocketOptions Options = new()
    {
        Tool = Tool.DefaultOutlineMill with { DiameterNm = Nm.FromMillimetres(0.5), Kind = ToolKind.EndMill },
        DepthNm = Nm.FromMillimetres(0.04),
        Stepover = 0.6,
    };

    // ------------------------------------------------------------------ what it produces

    [Fact]
    public void AnOpeningIsClearedByConcentricRings()
    {
        var toolpath = PocketOperation.Build([Square(0, 0, 3)], Options);

        Assert.Equal(ToolpathKind.Pocket, toolpath.Kind);
        Assert.True(toolpath.Passes.Count > 1, "a 3 mm pad needs more than one ring at 0.5 mm wide");
        Assert.All(toolpath.Passes, p => Assert.True(p.Closed));
        Assert.All(toolpath.Passes, p => Assert.Equal(Nm.FromMillimetres(0.04), p.DepthNm));
    }

    /// <summary>
    /// Outside-in: the pass that defines the edge of the cleared area is cut first, in undisturbed
    /// material, and every later pass takes a partial width.
    /// </summary>
    [Fact]
    public void TheRingsWorkInwardsFromTheBoundary()
    {
        var lengths = PocketOperation.Build([Square(0, 0, 4)], Options)
            .Passes.Select(p => p.LengthNm).ToList();

        for (var i = 1; i < lengths.Count; i++)
        {
            Assert.True(lengths[i] < lengths[i - 1], "each ring should be shorter than the last");
        }
    }

    /// <summary>
    /// The first ring's centreline sits half a cut width inside the boundary, so the tool's edge
    /// lands on the edge of the opening — any further in and mask is left round the rim.
    /// </summary>
    [Fact]
    public void TheFirstRingIsHalfACutWidthInsideTheOpening()
    {
        var toolpath = PocketOperation.Build([Square(0, 0, 4)], Options);
        var first = toolpath.Passes[0];

        var xs = first.Path.Select(s => s.From.X / (double)Nm.PerMillimetre).ToList();

        // A 4 mm square, inset by the 0.25 mm cutter radius, spans 0.25 to 3.75.
        Assert.Equal(0.25, xs.Min(), 2);
        Assert.Equal(3.75, xs.Max(), 2);
    }

    /// <summary>
    /// A pad the tool cannot fit inside keeps its mask, and nothing about the picture would say so —
    /// so it is counted and reported rather than quietly skipped.
    /// </summary>
    [Fact]
    public void AnOpeningSmallerThanTheToolIsReportedNotApproximated()
    {
        Paths64 openings = [Square(0, 0, 3), Square(10, 0, 0.3)];

        var toolpath = PocketOperation.Build(openings, Options);

        Assert.Equal(1, PocketOperation.UnreachableOpenings(openings, Options));
        Assert.Contains(toolpath.Notes, n => n.Contains("smaller than the tool", StringComparison.Ordinal));
    }

    /// <summary>Each pad is finished before the tool moves to the next, not one ring at a time.</summary>
    [Fact]
    public void EachOpeningIsItsOwnStack()
    {
        var toolpath = PocketOperation.Build(
            [Square(0, 0, 3), Square(10, 0, 3), Square(20, 0, 3)], Options);

        Assert.Equal(3, toolpath.Passes.Select(p => p.Stack).Distinct().Count());

        var seen = new HashSet<int>();
        var current = int.MinValue;

        foreach (var pass in toolpath.Passes)
        {
            if (pass.Stack == current)
            {
                continue;
            }

            Assert.True(seen.Add(pass.Stack), $"stack {pass.Stack} was left and returned to");
            current = pass.Stack;
        }
    }

    [Fact]
    public void NothingToClearIsNotAnError()
    {
        var toolpath = PocketOperation.Build([], Options);

        Assert.Empty(toolpath.Passes);
        Assert.Equal(0, PocketOperation.UnreachableOpenings([], Options));
    }

    // ------------------------------------------------------------------ how it is offered

    [Fact]
    public void PasteCanNowBeMilledAsWellAsBurned()
    {
        Assert.Contains(OutputKind.Gcode, LayerOperations.Available(LayerRole.TopPaste));
        Assert.Contains(OutputKind.Svg, LayerOperations.Available(LayerRole.TopPaste));

        Assert.Equal(OperationKind.Pocket, LayerOperations.For(LayerRole.TopPaste, OutputKind.Gcode));
        Assert.Equal(OperationKind.Pocket, LayerOperations.For(LayerRole.BottomPaste, OutputKind.Gcode));
    }

    /// <summary>
    /// Both a V-bit and a small flat end mill are used for this in practice — one for its fine tip,
    /// the other for a level floor — so neither is filtered out of the tool list.
    /// </summary>
    [Fact]
    public void EitherKindOfToolIsOffered()
    {
        Assert.Null(LayerOperations.ToolKindFor(OperationKind.Pocket));
    }

    [Fact]
    public void ItIsNamedAndTaggedAsItsOwnThing()
    {
        Assert.Equal("Mask relief", LayerOperations.Label(OperationKind.Pocket));

        Assert.Equal(
            "MyBoard-F_Paste.relief.nc",
            ExportPlanner.TargetNameFor("MyBoard-F_Paste.gbr", OperationKind.Pocket, OutputKind.Gcode));

        // Distinct from the stencil that the same layer produces as SVG.
        Assert.Equal(
            "MyBoard-F_Paste.mask.svg",
            ExportPlanner.TargetNameFor("MyBoard-F_Paste.gbr", OperationKind.MaskOpen, OutputKind.Svg));
    }

    /// <summary>
    /// The depth default follows the operation. One shared number cannot be right for both: the
    /// 0.05 mm that suits isolation goes clean through 0.02-0.04 mm of cured soldermask and into
    /// the copper underneath.
    /// </summary>
    [Fact]
    public void MaskReliefHasItsOwnDepthDefault()
    {
        var relief = LayerOperations.DefaultDepthNm(OperationKind.Pocket);
        var isolation = LayerOperations.DefaultDepthNm(OperationKind.Isolation);

        Assert.True(relief < isolation, "mask relief must default shallower than isolation");
        Assert.InRange(Nm.ToMillimetres(relief), 0.02, 0.04);
    }

    /// <summary>
    /// A caller that says nothing about depth gets the right one for what the layer becomes; one
    /// that names a depth gets exactly that, whatever the operation.
    /// </summary>
    [Fact]
    public void AnUnsetDepthFollowsTheOperationAndAChosenOneDoesNot()
    {
        var unset = new LayerOutputSettings { FileName = "x.gbr" };

        Assert.Equal(
            LayerOperations.DefaultDepthNm(OperationKind.Pocket),
            unset.DepthFor(OperationKind.Pocket));

        Assert.Equal(
            LayerOperations.DefaultDepthNm(OperationKind.Isolation),
            unset.DepthFor(OperationKind.Isolation));

        var chosen = unset with { DepthNm = Nm.FromMillimetres(0.08) };

        Assert.Equal(Nm.FromMillimetres(0.08), chosen.DepthFor(OperationKind.Pocket));
        Assert.Equal(Nm.FromMillimetres(0.08), chosen.DepthFor(OperationKind.Isolation));
    }

    /// <summary>The mask does not go through the board, so there is no break-through to set.</summary>
    [Fact]
    public void MaskReliefDoesNotGoThroughTheBoard()
    {
        Assert.False(LayerOperations.GoesThrough(OperationKind.Pocket));
    }
}
