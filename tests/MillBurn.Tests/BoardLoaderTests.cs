using MillBurn.Core;
using MillBurn.Pipeline;
using MillBurn.Viewer;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Loading a whole export folder: working out what each file is, realising it, and handing the
/// result to a scene.
///
/// The role is the part worth guarding. Everything downstream branches on it, so mistaking the
/// outline for copper does not produce an error — it produces a job that mills traces around the
/// board edge.
/// </summary>
public sealed class BoardLoaderTests
{
    // ------------------------------------------------------------------ role detection

    [Theory]
    [InlineData("Copper,L1,Top", LayerRole.TopCopper)]
    [InlineData("Copper,L2,Bot", LayerRole.BottomCopper)]
    [InlineData("Soldermask,Top", LayerRole.TopMask)]
    [InlineData("Soldermask,Bot", LayerRole.BottomMask)]
    [InlineData("Legend,Top", LayerRole.TopSilk)]
    [InlineData("Legend,Bot", LayerRole.BottomSilk)]
    [InlineData("Paste,Top", LayerRole.TopPaste)]
    [InlineData("Profile,NP", LayerRole.Outline)]
    public void FileFunctionDecidesTheRole(string fileFunction, LayerRole expected) =>
        Assert.Equal(expected, LayerRoles.FromFileFunction(fileFunction));

    /// <summary>
    /// The side comes from the Top/Bot field, never from the layer number. On a four-layer board
    /// <c>Copper,L2,Inr</c> is an inner layer; reading "L2" as "the bottom" because it is on a
    /// two-layer board is an assumption that survives every test until a real stackup arrives.
    /// </summary>
    [Theory]
    [InlineData("Copper,L2,Inr", LayerRole.InnerCopper)]
    [InlineData("Copper,L3,Inr", LayerRole.InnerCopper)]
    [InlineData("Copper,L4,Bot", LayerRole.BottomCopper)]
    public void InnerCopperIsNotMistakenForTheBottom(string fileFunction, LayerRole expected) =>
        Assert.Equal(expected, LayerRoles.FromFileFunction(fileFunction));

    [Theory]
    [InlineData("board-F_Cu.gbr", LayerRole.TopCopper)]
    [InlineData("board-B_Cu.gbr", LayerRole.BottomCopper)]
    [InlineData("board-F_Silkscreen.gbr", LayerRole.TopSilk)]
    [InlineData("board-B_Mask.gbr", LayerRole.BottomMask)]
    [InlineData("board-Edge_Cuts.gbr", LayerRole.Outline)]
    [InlineData("board-NPTH.drl", LayerRole.NonPlatedDrill)]
    [InlineData("board-PTH.drl", LayerRole.PlatedDrill)]
    [InlineData("board.gtl", LayerRole.TopCopper)]
    [InlineData("board.gbl", LayerRole.BottomCopper)]
    [InlineData("board.gko", LayerRole.Outline)]
    public void FilenamesAreTheFallback(string fileName, LayerRole expected) =>
        Assert.Equal(expected, LayerRoles.FromFileName(fileName));

    [Fact]
    public void AnUnrecognisableNameIsUnknownRatherThanAGuess() =>
        Assert.Equal(LayerRole.Unknown, LayerRoles.FromFileName("mystery.gbr"));

    /// <summary>
    /// Silk before copper before what is printed on it, holes after, outline last. Painting copper
    /// over silk hides the legend that is printed on it.
    /// </summary>
    [Fact]
    public void DrawOrderPutsCopperUnderSilkAndOutlineOnTop()
    {
        Assert.True(LayerRoleInfo.DrawOrder(LayerRole.BottomCopper) < LayerRoleInfo.DrawOrder(LayerRole.TopCopper));
        Assert.True(LayerRoleInfo.DrawOrder(LayerRole.TopCopper) < LayerRoleInfo.DrawOrder(LayerRole.TopSilk));
        Assert.True(LayerRoleInfo.DrawOrder(LayerRole.TopSilk) < LayerRoleInfo.DrawOrder(LayerRole.PlatedDrill));
        Assert.True(LayerRoleInfo.DrawOrder(LayerRole.PlatedDrill) < LayerRoleInfo.DrawOrder(LayerRole.Outline));
    }

    [Theory]
    [InlineData(LayerRole.TopCopper, BoardSide.Top)]
    [InlineData(LayerRole.BottomSilk, BoardSide.Bottom)]
    [InlineData(LayerRole.Outline, BoardSide.None)]
    public void SidesAreDerivedFromTheRole(LayerRole role, BoardSide expected) =>
        Assert.Equal(expected, LayerRoleInfo.SideOf(role));

    // ------------------------------------------------------------------ loading a folder

    [Fact]
    public void EveryLayerOfARealBoardIsIdentifiedFromItsAttributes()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        Assert.Equal(9, board.Layers.Count);
        Assert.Empty(board.Failures);

        // Not one of them needed the filename fallback: KiCad declares every file function.
        Assert.DoesNotContain(board.Layers, l => l.RoleGuessed);
        Assert.DoesNotContain(board.Layers, l => l.Role == LayerRole.Unknown);

        Assert.Contains(board.Layers, l => l.Role == LayerRole.TopCopper);
        Assert.Contains(board.Layers, l => l.Role == LayerRole.BottomCopper);
        Assert.Contains(board.Layers, l => l.Role == LayerRole.Outline);
        Assert.Contains(board.Layers, l => l.Role == LayerRole.PlatedDrill);
        Assert.Contains(board.Layers, l => l.Role == LayerRole.NonPlatedDrill);
    }

    /// <summary>
    /// Copper is routinely pulled back from the board edge, so a union of the copper understates
    /// the board and a view fitted to it shows the wrong thing. The outline is the answer whenever
    /// there is one.
    /// </summary>
    [Fact]
    public void BoardExtentsComeFromTheOutlineNotTheCopper()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var outline = board.Layers.Single(l => l.Role == LayerRole.Outline);
        var copper = board.Layers.Single(l => l.Role == LayerRole.BottomCopper);

        Assert.Equal(outline.Bounds, board.Bounds);
        Assert.True(board.Bounds.Width > copper.Bounds.Width, "the outline should be wider than the pour");
    }

    /// <summary>
    /// Drill files become area like everything else, so the viewer and every later stage treat
    /// them uniformly instead of special-casing holes.
    /// </summary>
    [Fact]
    public void DrillFilesBecomeArea()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var plated = board.Layers.Single(l => l.Role == LayerRole.PlatedDrill);

        Assert.NotNull(plated.Drill);
        Assert.Equal(16, plated.Drill!.Hits.Count);
        Assert.Equal(16, plated.RingCount);
        Assert.True(plated.AreaMm2 > 0);

        // Two non-plated mounting holes, 2.2 mm across: pi * 1.1^2 each.
        var nonPlated = board.Layers.Single(l => l.Role == LayerRole.NonPlatedDrill);
        Assert.Equal(2, nonPlated.RingCount);
        Assert.Equal(2 * Math.PI * 1.1 * 1.1, nonPlated.AreaMm2, 1);
    }

    [Fact]
    public void AFolderWithNothingInItLoadsAsAnEmptyBoard()
    {
        var empty = Path.Combine(Path.GetTempPath(), "millburn-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);

        try
        {
            Assert.Empty(BoardLoader.LoadFolder(empty).Layers);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void AMissingFolderIsAnErrorRatherThanAnEmptyBoard() =>
        Assert.Throws<DirectoryNotFoundException>(
            () => BoardLoader.LoadFolder(Path.Combine(Path.GetTempPath(), "millburn-does-not-exist")));

    // ------------------------------------------------------------------ into a scene

    /// <summary>
    /// The whole view stack, headless: load a folder, build the scene the viewport builds, and
    /// check the board is actually in it. A board that loads with perfect counts and renders as an
    /// empty rectangle is a real outcome that no count catches.
    /// </summary>
    [Fact]
    public void ARealBoardBuildsIntoASceneWithGeometryInIt()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        using var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            board.Bounds);

        // One per file, plus the synthetic substrate the outline gives us.
        Assert.Equal(board.Layers.Count + 1, scene.Layers.Count);
        Assert.Equal(BoardSceneBuilder.SubstrateId, scene.Layers[0].Id);
        Assert.True(scene.TotalVertices > 1000, $"expected real geometry; got {scene.TotalVertices} vertices");

        // The scene's extent is the board's, in millimetres, with Y flipped into screen sense.
        Assert.Equal(Nm.ToMillimetres(board.Bounds.Width), scene.Bounds.Width, 3);
        Assert.Equal(Nm.ToMillimetres(board.Bounds.Height), scene.Bounds.Height, 3);
    }

    [Fact]
    public void LayersAreOrderedBackToFrontAndSomeStartHidden()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        using var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            board.Bounds);

        var ids = scene.Layers.Select(l => l.Id).ToList();
        Assert.True(
            ids.IndexOf("PogoTest1-B_Cu.gbr") < ids.IndexOf("PogoTest1-F_Cu.gbr"),
            "bottom copper should be painted before top copper");
        Assert.True(
            ids.IndexOf("PogoTest1-F_Cu.gbr") < ids.IndexOf("PogoTest1-F_Silkscreen.gbr"),
            "copper should be painted before the silk printed on it");

        // Soldermask defaults off: it covers the whole board and hides everything under it.
        Assert.False(scene.Layer("PogoTest1-F_Mask.gbr")!.Visible);
        Assert.True(scene.Layer("PogoTest1-F_Cu.gbr")!.Visible);
        Assert.True(scene.VisibleVertices < scene.TotalVertices);
    }

    /// <summary>
    /// The board gets its own material under every layer. Layer colours model physical reality —
    /// copper is copper, silk is white — so they cannot follow the UI theme, and white silk on a
    /// light background is invisible. The substrate is what keeps every layer legible whatever the
    /// rest of the window is doing.
    /// </summary>
    [Fact]
    public void TheOutlineGivesTheBoardASubstrateUnderneathEverything()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        using var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            board.Bounds);

        var substrate = scene.Layer(BoardSceneBuilder.SubstrateId);
        Assert.NotNull(substrate);
        Assert.True(substrate!.Visible);
        Assert.Equal(1, substrate.RingCount);

        // Painted first, so everything else lands on top of it.
        Assert.Equal(0, scene.Layers.ToList().IndexOf(substrate));

        // And it covers the board: the outline's outer ring, not one of the stroke's inner rings.
        var outline = board.Layers.Single(l => l.Role == LayerRole.Outline);
        Assert.Equal(Nm.ToMillimetres(outline.Bounds.Width), substrate.Path.Bounds.Width, 2);
    }

    [Fact]
    public void ABoardWithNoOutlineGetsNoSubstrate()
    {
        var board = BoardLoader.Load(
            "test",
            [RealBoards.File(RealBoards.PogoTest1, "PogoTest1-F_Cu.gbr")]);

        using var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            board.Bounds);

        Assert.Null(scene.Layer(BoardSceneBuilder.SubstrateId));
        Assert.Single(scene.Layers);
    }

    [Fact]
    public void FittingCentresTheBoardInTheViewport()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        using var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            board.Bounds);

        var viewport = new SkiaSharp.SKRect(0, 0, 800, 600);
        var view = BoardSceneBuilder.FitTo(scene.Bounds, viewport);

        // The board's centre lands on the viewport's centre.
        Assert.Equal(viewport.MidX, (scene.Bounds.MidX * view.Scale) + view.OffsetX, 2);
        Assert.Equal(viewport.MidY, (scene.Bounds.MidY * view.Scale) + view.OffsetY, 2);

        // And it fits, with a margin.
        Assert.True(scene.Bounds.Width * view.Scale <= viewport.Width);
        Assert.True(scene.Bounds.Height * view.Scale <= viewport.Height);
    }
}
