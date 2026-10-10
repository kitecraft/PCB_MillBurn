using System.Diagnostics;
using MillBurn.Core;
using MillBurn.Pipeline;
using MillBurn.Viewer;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Moving the rectangle a scene is measured from, without rebuilding it.
///
/// The frame a board is drawn in is the stock's when the job is built on stock and the board's
/// otherwise, and it moves while somebody is still deciding — a tick box and a slider. What the
/// picture is *made of* does not move with it, so the two are separated here.
/// </summary>
public sealed class BoardSceneReframeTests(ITestOutputHelper output)
{
    private static BoardScene Scene(Board board, Bounds extent) => BoardSceneBuilder.Build(
        board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
        extent);

    /// <summary>
    /// A reframed scene is in the same place as one built to that frame in the first place.
    ///
    /// **The whole claim, and the only one that matters.** The cheap path exists to avoid a
    /// rebuild; it earns that only by being indistinguishable from one. Compared against a scene
    /// built the expensive way rather than against a remembered rectangle, so this cannot drift
    /// from whatever `Build` decides the extent means — the sign on y, in particular, which is
    /// negated going into the viewer's world and is the thing most easily got wrong twice.
    /// </summary>
    [Fact]
    public void ReframingLandsWhereRebuildingWouldHave()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoMega));

        var border = Nm.FromMillimetres(10);
        var stock = new Bounds(
            board.Bounds.MinX - border,
            board.Bounds.MinY - border,
            board.Bounds.MaxX + border,
            board.Bounds.MaxY + border);

        using var built = Scene(board, stock);
        using var reframed = Scene(board, board.Bounds);

        Assert.NotEqual(built.Bounds, reframed.Bounds);

        reframed.Reframe(stock);

        Assert.Equal(built.Bounds, reframed.Bounds);
    }

    /// <summary>
    /// Reframing moves the extent and leaves the geometry exactly as it was.
    ///
    /// If it rebuilt anything it would not be worth having, and if it dropped a layer the board
    /// would quietly lose copper while somebody dragged a border slider.
    /// </summary>
    [Fact]
    public void ReframingDoesNotTouchTheGeometry()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoMega));

        using var scene = Scene(board, board.Bounds);

        var layers = scene.Layers.Count;
        var vertices = scene.TotalVertices;
        var paths = scene.Layers.Select(l => l.Path).ToList();

        // Every assertion below is "the same as before", which on an empty scene is satisfied by
        // nothing at all. The Mega has copper on both sides and an outline; say so, so that a
        // fixture or loader change cannot turn this into a test of two zeroes.
        Assert.True(layers > 1, $"this board built {layers} layer(s); there is nothing to preserve");
        Assert.True(vertices > 0, "this board built no vertices");

        scene.Reframe(new Bounds(0, 0, Nm.FromMillimetres(200), Nm.FromMillimetres(200)));

        Assert.Equal(layers, scene.Layers.Count);
        Assert.Equal(vertices, scene.TotalVertices);

        // **The same path objects, not merely equivalent ones.** A rebuild would hand back new
        // ones, and the old ones are what a frame already in flight is drawing. `Assert.Same`
        // rather than comparing the sequences, because a sequence comparison would fall back to
        // whatever `SKPath` does for equality — and if that is ever value-based, a rebuild
        // producing identical paths would pass the thing this exists to rule out.
        Assert.Equal(paths.Count, scene.Layers.Count);

        for (var i = 0; i < paths.Count; i++)
        {
            Assert.Same(paths[i], scene.Layers[i].Path);
        }
    }

    /// <summary>
    /// An empty frame is ignored rather than obeyed.
    ///
    /// `Bounds.Empty` means "there isn't one", and a scene collapsed to a zero rectangle is a
    /// viewport that cannot fit itself to anything — a blank window with no way back.
    /// </summary>
    [Fact]
    public void AnEmptyFrameIsIgnored()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoMega));

        using var scene = Scene(board, board.Bounds);
        var before = scene.Bounds;

        scene.Reframe(Bounds.Empty);

        Assert.Equal(before, scene.Bounds);
    }

    /// <summary>
    /// Reframing is cheap enough to do on every tick of a slider, and rebuilding is not.
    ///
    /// **The reason this path exists, stated as a measurement rather than as an adjective.** The
    /// stock's border is a slider; a full-range drag calls this repeatedly, and 6.49 is the entry
    /// about a slider doing expensive work per tick.
    ///
    /// Asserted as a ratio against a rebuild measured in the same run, because an absolute
    /// millisecond figure is a statement about the machine the suite happens to be on. A hundredth
    /// is far looser than the real difference and still fails if reframing ever starts rebuilding.
    /// The numbers it saw are printed, which is what to read when this is being looked at rather
    /// than run.
    /// </summary>
    [Fact]
    public void ReframingIsOrdersOfMagnitudeCheaperThanRebuilding()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoMega));

        var sources = board.Layers
            .Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings()))
            .ToList();

        // Once through each first, so neither timing pays for a cold path.
        using (var warm = BoardSceneBuilder.Build(sources, board.Bounds))
        {
            warm.Reframe(board.Bounds);
        }

        // **Best of five, not one.** A single timed build is noise: the first two runs of this
        // read 16.5 ms and 7.4 ms on the same machine, and the lower is the honest one — the
        // higher is whatever else the box was doing. Best-of is the right summary for "how long
        // does this take at all", and it is the figure quoted in the entry this test backs.
        var best = double.MaxValue;
        BoardScene? scene = null;

        for (var i = 0; i < 5; i++)
        {
            scene?.Dispose();

            var run = Stopwatch.StartNew();
            scene = BoardSceneBuilder.Build(sources, board.Bounds);
            run.Stop();

            best = Math.Min(best, run.Elapsed.TotalMilliseconds);
        }

        using var built = scene!;

        // **Two rectangles, alternating.** Passing the same one a hundred times would leave the
        // scene already holding it from the second call on, so an implementation that returned
        // early when the extent had not changed would be timed doing nothing — and this would
        // then be a measurement of a no-op dressed as a measurement of the work.
        var a = new Bounds(0, 0, Nm.FromMillimetres(200), Nm.FromMillimetres(200));
        var b = new Bounds(0, 0, Nm.FromMillimetres(150), Nm.FromMillimetres(150));

        var reframe = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            built.Reframe(i % 2 == 0 ? a : b);
        }

        reframe.Stop();

        // And it did move: a loop that timed nothing because the call does nothing would be the
        // other way to pass this, and test one is the only other thing guarding against it.
        Assert.NotEqual(0, built.Bounds.Width);

        var each = reframe.Elapsed.TotalMilliseconds / 100;

        output.WriteLine(
            $"rebuild {best:F1} ms (best of 5) · reframe {each:F4} ms · {best / Math.Max(each, 1e-6):F0}x");

        Assert.True(
            each < best / 100,
            $"reframing took {each:F4} ms against a rebuild's {best:F1} ms");
    }
}
