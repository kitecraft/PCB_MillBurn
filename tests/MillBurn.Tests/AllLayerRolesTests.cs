using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// The roles only the full export reaches: solder paste, and a user layer.
///
/// Paste and user layers had no real board behind them until this export was added, so the code
/// that handles them had only ever been exercised by its own defaults. A role that is never seen on
/// a real file is a role nobody knows is broken.
/// </summary>
public sealed class AllLayerRolesTests
{
    private static Board Board() =>
        BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1AllLayers));

    [Fact]
    public void PasteLayersAreRecognisedOnBothSides()
    {
        var board = Board();

        Assert.Contains(board.Layers, l => l.Role == LayerRole.TopPaste);
        Assert.Contains(board.Layers, l => l.Role == LayerRole.BottomPaste);
    }

    /// <summary>
    /// Paste is offered both ways, because both are real: burned as a stencil, or milled as mask
    /// relief — cutting the cured soldermask off the pads so they can be soldered.
    ///
    /// This was originally written the other way round, asserting paste could never be milled. That
    /// was an assumption about what people do rather than a fact about the geometry, and it was
    /// wrong.
    /// </summary>
    [Fact]
    public void PasteCanBeCutAsAStencilOrMilledAsMaskRelief()
    {
        Assert.Contains(OutputKind.Svg, LayerOperations.Available(LayerRole.TopPaste));
        Assert.Contains(OutputKind.Gcode, LayerOperations.Available(LayerRole.TopPaste));

        Assert.Equal(OperationKind.MaskOpen, LayerOperations.For(LayerRole.TopPaste, OutputKind.Svg));
        Assert.Equal(OperationKind.Pocket, LayerOperations.For(LayerRole.TopPaste, OutputKind.Gcode));
    }

    /// <summary>
    /// A user layer is not a board layer, and it must never be cut or burned as whatever it might
    /// have been mistaken for. That is what this has always guarded and still does.
    ///
    /// It used to expect <see cref="LayerRole.Unknown"/>, on the reading that anything else would
    /// be a guess. It is not one: the file declares <c>Other,User</c>, which the standard defines
    /// as a file that is none of the board layers it lists, and every layer in this same board that
    /// does get made says so with a function of its own. So it is documentation, identified rather
    /// than shrugged at — and documentation cannot be exported either.
    /// </summary>
    [Fact]
    public void AUserLayerIsDocumentationAndCannotBeMade()
    {
        var user = Board().Layers.SingleOrDefault(l => l.FileName.Contains("User_1", StringComparison.Ordinal));

        Assert.NotNull(user);
        Assert.Equal(LayerRole.Documentation, user.Role);
        Assert.Equal([OutputKind.None], LayerOperations.Available(user.Role));

        // Named after the layer it came from, so a board carrying several tells them apart.
        Assert.Equal("User 1", user.Label);
    }

    /// <summary>
    /// The two exports are the same board at different revisions, not the same file twice: this one
    /// has two more plated holes and two more copper objects.
    ///
    /// That makes the pair useful beyond the extra roles — it is a real before-and-after of one
    /// design, which is what the refresh and fingerprint machinery exists to tell apart.
    /// </summary>
    [Fact]
    public void ItIsALaterRevisionOfTheSameBoard()
    {
        var full = Board();
        var plain = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        Assert.Equal(plain.Bounds.Width, full.Bounds.Width);
        Assert.Equal(plain.Bounds.Height, full.Bounds.Height);

        var holes = (Board b) => b.Layers.Where(l => l.Drill is not null).Sum(l => l.Drill!.Hits.Count);
        Assert.True(holes(full) > holes(plain), "expected the fuller export to be a later revision");

        var copper = (Board b) => b.Layers.First(l => l.Role == LayerRole.TopCopper).AreaMm2;
        Assert.True(copper(full) > copper(plain));
    }

    /// <summary>
    /// The paste layers on this board are empty — the design has no stencil apertures — and an
    /// empty layer must produce no file at all.
    ///
    /// Writing an empty SVG would be worse than writing nothing: it looks like a successful export
    /// and cuts a stencil with no openings in it.
    /// </summary>
    [Fact]
    public void AnEmptyPasteLayerProducesNoFile()
    {
        var board = Board();

        Assert.All(
            board.Layers.Where(l => l.Role is LayerRole.TopPaste or LayerRole.BottomPaste),
            l => Assert.Equal(0, l.ObjectCount));

        var settings = board.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = l.Role is LayerRole.TopPaste or LayerRole.BottomPaste
                    ? OutputKind.Svg
                    : OutputKind.None,
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            board, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6));

        Assert.Equal(0, plan.Count);
        Assert.Equal(2, plan.Skipped.Count);
    }
}
