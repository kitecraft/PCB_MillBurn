using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using MillBurn.Viewer;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Drawing a G-code file on its own, with no board behind it.
///
/// The parser, the classifier and the scene were all built on emitted text rather than on the
/// toolpaths that produced it, so this needed no new geometry — only a scene with no layers in it
/// but a backplot, and an extent that comes from the program rather than from a board outline.
/// These tests cover that path, which is the one thing about it that was new.
/// </summary>
public sealed class ProgramViewTests(ITestOutputHelper output)
{
    private static string Program(string board, OperationKind operation)
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

        return plan.Items.First(i => i.Operation == operation).Content;
    }

    private static BoardScene SceneFor(string text)
    {
        var parsed = GcodeParser.Parse(text);
        var backplot = BackplotBuilder.Build(GcodeBackplot.Classify(parsed), Point2.Origin);

        return BoardSceneBuilder.Build([], parsed.Bounds, null, backplot);
    }

    /// <summary>
    /// A scene with no layers and only a backplot is the whole case: everything the viewer draws
    /// for a standalone program comes from the program.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1, OperationKind.Isolation)]
    [InlineData(RealBoards.PogoTest1, OperationKind.Drilling)]
    [InlineData(RealBoards.PogoTest1, OperationKind.Outline)]
    [InlineData(RealBoards.Panel, OperationKind.Isolation)]
    public void AProgramDrawsWithNoBoardBehindIt(string board, OperationKind operation)
    {
        var scene = SceneFor(Program(board, operation));

        output.WriteLine($"{board} {operation}: {scene.Layers.Count} layers, "
            + $"{scene.TotalVertices:N0} vertices, {scene.Bounds}");

        Assert.NotEmpty(scene.Layers);
        Assert.True(scene.TotalVertices > 0, "nothing was drawn");
        Assert.True(scene.Bounds.Width > 0 && scene.Bounds.Height > 0, "the scene has no extent");
    }

    /// <summary>
    /// The extent is the program's, so fitting the view shows the whole thing. With a board it
    /// comes from the outline; here there is no outline, and taking the union of what happens to be
    /// drawn is the only other answer — which is the same thing.
    /// </summary>
    [Fact]
    public void TheExtentCoversEveryMove()
    {
        var text = Program(RealBoards.PogoTest1, OperationKind.Isolation);
        var parsed = GcodeParser.Parse(text);
        var scene = SceneFor(text);

        // Scene space is millimetres with Y pointing down, so the program's Y range maps to the
        // negated one. Everything the program touches has to be inside it.
        Assert.True(scene.Bounds.Left <= (float)Nm.ToMillimetres(parsed.Bounds.MinX) + 0.001f);
        Assert.True(scene.Bounds.Right >= (float)Nm.ToMillimetres(parsed.Bounds.MaxX) - 0.001f);
        Assert.True(scene.Bounds.Top <= (float)-Nm.ToMillimetres(parsed.Bounds.MaxY) + 0.001f);
        Assert.True(scene.Bounds.Bottom >= (float)-Nm.ToMillimetres(parsed.Bounds.MinY) - 0.001f);
    }

    /// <summary>
    /// A dry run is *all* travel by construction — it never goes below the safe height, so nothing
    /// in it classifies as cutting. It still has to draw, which is why a standalone program turns
    /// every backplot layer on rather than keeping the board view's default of hiding travel.
    /// </summary>
    [Fact]
    public void ADryRunIsAllTravelAndStillHasSomethingToDraw()
    {
        var (dry, report) = DryRun.Rewrite(Program(RealBoards.PogoTest1, OperationKind.Isolation));

        Assert.Null(report.Refusal);

        var parsed = GcodeParser.Parse(dry);
        var classified = GcodeBackplot.Classify(parsed);
        var measured = GcodeBackplot.Measure(classified);

        output.WriteLine($"dry run: {measured.CutMm:F0} mm cut, {measured.TravelMm:F0} mm travel");

        Assert.Equal(0, measured.CutMm, 3);
        Assert.True(measured.TravelMm > 100, "the path should still be there");

        var scene = SceneFor(dry);

        // Vertices rather than runs: a run is one contiguous polyline, and a dry run is a single
        // unbroken travel path, so it collapses to a handful of runs holding the whole route.
        output.WriteLine($"drawn: {scene.Layers.Sum(l => l.RingCount)} runs, {scene.TotalVertices} vertices");

        Assert.True(
            scene.TotalVertices > 100,
            $"a dry run should draw its whole path, got {scene.TotalVertices} vertices");
    }

    /// <summary>Any G-code, not only ours — that is the point of parsing files rather than toolpaths.</summary>
    [Fact]
    public void SomebodyElsesGcodeDrawsToo()
    {
        var scene = SceneFor("""
            G21 G90
            G0 X0 Y0
            G1 Z-1.0 F60
            G1 X10 Y0 F200
            G2 X20 Y10 I0 J10
            G1 X20 Y20
            G0 Z5
            M30
            """);

        Assert.NotEmpty(scene.Layers);
        Assert.True(scene.TotalVertices > 0);
    }

    /// <summary>A file with no motion has nothing to show, and must not pretend otherwise.</summary>
    [Fact]
    public void AProgramWithNoMotionHasNoExtent()
    {
        var parsed = GcodeParser.Parse("( just a note )\nG21 G90\nM30");

        Assert.Empty(parsed.Moves);
        Assert.True(parsed.Bounds.IsEmpty);
    }
}
