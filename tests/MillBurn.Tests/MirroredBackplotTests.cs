using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Where a mirrored program is drawn.
///
/// A bottom-side job is flipped left-to-right before it is written, because the stock is turned
/// over before it is cut. The file is right; the picture was not. Translated straight onto the
/// board, its cuts landed on the mirror image of the traces they isolate — a bottom copper backplot
/// that looked, correctly, like a bug, in an app whose whole promise is that the preview is the
/// file.
///
/// Found by looking at a real board with one layer's artwork and one layer's toolpath showing, and
/// nothing else. No test in the suite could have found it, because every one of them checked the
/// emitted text.
/// </summary>
public sealed class MirroredBackplotTests(ITestOutputHelper output)
{
    private static (Board Board, ExportItem Item, BoardLayer Layer) Isolate(bool mirrored)
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var layer = board.Layers.First(l => l.Role == LayerRole.BottomCopper);

        var settings = new Dictionary<string, LayerOutputSettings>(StringComparer.Ordinal)
        {
            [layer.FileName] = new()
            {
                FileName = layer.FileName,
                Output = OutputKind.Gcode,
                Mirrored = mirrored,
            },
        };

        var plan = ExportPlanner.Plan(
            board, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        return (board, plan.Items.Single(i => i.Operation == OperationKind.Isolation), layer);
    }

    private static IReadOnlyList<Point2> Drawn(Board board, ExportItem item, bool correct)
    {
        var layers = BackplotBuilder.BuildPerProgram(
            [new BackplotBuilder.Program(
                item.LayerFileName, item.LayerLabel,
                GcodeBackplot.Classify(GcodeParser.Parse(item.Content)),
                item.Mirrored)],
            new Point2(board.Bounds.MinX, board.Bounds.MinY),
            mirrorSumXNm: correct ? board.Bounds.MinX + board.Bounds.MaxX : 0);

        return
        [
            .. layers
                .Where(l => l.Palette == "gcode-cut")
                .SelectMany(l => l.Runs)
                .SelectMany(r => r),
        ];
    }

    /// <summary>
    /// The centre of mass of a set of points.
    ///
    /// Bounds are not enough, and that is the whole reason this file exists. PogoTest1's bottom
    /// copper reaches within 60 µm of the same distance from each edge, so its extent is very
    /// nearly symmetrical about the board's centreline and a mirrored copy of it has *the same
    /// bounding box*. The artwork inside is not symmetrical at all. A test that compared extents
    /// would have watched this bug go past.
    /// </summary>
    private static Point2 Centroid(IReadOnlyList<Point2> points) => new(
        (long)points.Average(p => (double)p.X),
        (long)points.Average(p => (double)p.Y));

    /// <summary>The planner has to say so, or nothing downstream can put the program anywhere.</summary>
    [Fact]
    public void ThePlanSaysWhichProgramsWereFlipped()
    {
        Assert.True(Isolate(mirrored: true).Item.Mirrored);
        Assert.False(Isolate(mirrored: false).Item.Mirrored);
    }

    /// <summary>
    /// The test that would have caught it. An isolation path surrounds the copper it isolates, so
    /// it must sit within a tool's width of that layer's own extent — mirrored or not.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnIsolationPathLandsOnTheCopperItIsolates(bool mirrored)
    {
        var (board, item, layer) = Isolate(mirrored);

        var copper = Centroid([.. layer.Rings().SelectMany(r => r)]);
        var drawn = Centroid(Drawn(board, item, correct: true));

        output.WriteLine($"copper {Mm(copper)}");
        output.WriteLine($"drawn  {Mm(drawn)}");

        // The path runs around the outside of every island, so its centre of mass sits close to
        // the copper's but not on it. Half a millimetre is a long way on a 21 mm board and a very
        // short way compared to being on the other side of it.
        var slack = Nm.FromMillimetres(0.5);

        Assert.InRange(drawn.X, copper.X - slack, copper.X + slack);
        Assert.InRange(drawn.Y, copper.Y - slack, copper.Y + slack);
    }

    /// <summary>
    /// And that the correction is doing something: drawn straight, the mirrored program lands
    /// somewhere else entirely. Without this the test above would pass on a board that happened to
    /// be symmetrical about its own centreline, which is most test fixtures and no real board.
    /// </summary>
    [Fact]
    public void DrawnStraightTheMirroredProgramLandsOnTheWrongCopper()
    {
        var (board, item, layer) = Isolate(mirrored: true);

        var copper = Centroid([.. layer.Rings().SelectMany(r => r)]);
        var straight = Centroid(Drawn(board, item, correct: false));
        var placed = Centroid(Drawn(board, item, correct: true));

        output.WriteLine($"copper   {Mm(copper)}");
        output.WriteLine($"straight {Mm(straight)}  off by {Off(straight, copper)}");
        output.WriteLine($"placed   {Mm(placed)}  off by {Off(placed, copper)}");

        // Drawn straight it is not merely a little out — it is on the wrong side of the board.
        Assert.True(
            Math.Abs(straight.X - copper.X) > 10 * Math.Abs(placed.X - copper.X),
            "the uncorrected drawing should be nowhere near the copper");

        // The two are reflections of one another about the board's centreline, which is the whole
        // claim: nothing was moved, only turned over. A nanometre of slack for the averaging.
        var sum = board.Bounds.MinX + board.Bounds.MaxX;

        Assert.InRange(placed.X, sum - straight.X - 1, sum - straight.X + 1);

        // Y is untouched by a left-to-right flip.
        Assert.Equal(straight.Y, placed.Y);
    }

    private static string Off(Point2 a, Point2 b) =>
        Nm.ToMillimetreString(Math.Abs(a.X - b.X), 3) + " mm in X";

    /// <summary>A program that was never flipped must not be moved by any of this.</summary>
    [Fact]
    public void AnUnmirroredProgramIsDrawnExactlyWhereItWasBefore()
    {
        var (board, item, _) = Isolate(mirrored: false);

        Assert.Equal(
            Centroid(Drawn(board, item, correct: false)),
            Centroid(Drawn(board, item, correct: true)));
    }

    private static string Mm(Point2 p) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"X {Nm.ToMillimetreString(p.X, 3)}  Y {Nm.ToMillimetreString(p.Y, 3)}");
}
