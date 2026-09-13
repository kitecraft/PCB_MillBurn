using MillBurn.Core;
using MillBurn.Pipeline;
using MillBurn.Viewer;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Whether a ring closes, which is a different question from whether it is stroked.
///
/// One flag used to answer both, and the board outline is the case where the two answers differ:
/// a closed area, drawn as a line. Every outline ring was drawn missing exactly the segment that
/// would have closed it — one gap per ring, at whatever vertex Clipper started that ring on, which
/// is why it landed in the same place on all thirty-two cells of a panelised board and looked like
/// a pattern rather than a bug. It never reached a file; the scene is for drawing only.
///
/// The trap on the other side is real too, so it is guarded here as well: a backplot's rings are
/// runs the tool travelled, and closing one draws a move from the end of the program back to its
/// start that the machine never makes.
/// </summary>
public sealed class ClosedRingTests(ITestOutputHelper output)
{
    /// <summary>
    /// Segments actually in the path, counted by walking it.
    ///
    /// A closed triangle is four segments and an open one is three, and no property of
    /// <see cref="SKPath"/> says which you have — the difference is a close verb, so the verbs are
    /// what gets counted.
    /// </summary>
    private static (int Moves, int Lines, int Closes) Verbs(SKPath path)
    {
        var moves = 0;
        var lines = 0;
        var closes = 0;

        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];

        for (var verb = iterator.Next(points); verb != SKPathVerb.Done; verb = iterator.Next(points))
        {
            switch (verb)
            {
                case SKPathVerb.Move: moves++; break;
                case SKPathVerb.Line: lines++; break;
                case SKPathVerb.Close: closes++; break;
                default: break;
            }
        }

        return (moves, lines, closes);
    }

    /// <summary>
    /// The regression, on the board that reported it: every ring of the outline closes.
    ///
    /// Asserted as one close per ring rather than by counting segments, because the number of
    /// segments is a property of the fixture and would change if the board ever did.
    /// </summary>
    [Fact]
    public void EveryOutlineRingIsDrawnClosed()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.Panel));

        using var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            board.Bounds);

        var outline = board.Layers.Single(l => l.Role == LayerRole.Outline);
        var layer = scene.Layer(outline.FileName)!;
        var (moves, lines, closes) = Verbs(layer.Path);

        output.WriteLine($"{layer.RingCount} rings: {moves} moves, {lines} lines, {closes} closes");

        Assert.True(layer.Style.Outlined, "the outline is stroked, not filled");
        Assert.Equal(layer.RingCount, moves);
        Assert.Equal(layer.RingCount, closes);
    }

    /// <summary>
    /// And the other half: a backplot stays open, so nothing draws a move the program never makes.
    /// </summary>
    [Fact]
    public void ABackplotRunIsDrawnOpen()
    {
        IReadOnlyList<IReadOnlyList<Point2>> run =
        [
            [
                new Point2(0, 0),
                new Point2(Nm.FromMillimetres(10), 0),
                new Point2(Nm.FromMillimetres(10), Nm.FromMillimetres(10)),
            ],
        ];

        using var scene = BoardScene.Build([("cuts", "Cuts", BackplotPalette.Cut, run)]);

        var (moves, lines, closes) = Verbs(scene.Layers[0].Path);

        output.WriteLine($"{moves} moves, {lines} lines, {closes} closes");

        Assert.Equal(1, moves);
        Assert.Equal(2, lines);
        Assert.Equal(0, closes);
    }

    /// <summary>
    /// The flag is set where it is decided, not remembered at each call site.
    ///
    /// Every board layer is an area whether or not it is stroked, and every backplot layer is a
    /// run. Stated here so that adding a style to either palette and forgetting which it is fails
    /// a test rather than producing a picture somebody has to notice.
    /// </summary>
    [Fact]
    public void ThePalettesAgreeOnWhatTheirRingsAre()
    {
        var roles = Enum.GetValues<LayerRole>();

        Assert.All(roles, r => Assert.True(
            BoardPalette.For(r).ClosedRings, $"{r} is an area, drawn from a realised region"));

        Assert.True(BoardPalette.Substrate.ClosedRings);

        BoardLayerStyle[] backplot =
        [
            BackplotPalette.Cut,
            BackplotPalette.Plunge,
            BackplotPalette.Travel,
            BackplotPalette.LongTravel,
            BackplotPalette.Gouge,
        ];

        Assert.All(backplot, s => Assert.False(s.ClosedRings, "a backplot run has no closing move"));
    }
}
