using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Gerber;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Every net the file names, placed on the copper it belongs to.
///
/// This is what an electrical check stands on. The Gerbers already say which net each pad and trace
/// belongs to — `TO.N` on the object — and until now the realiser threw that away when it unioned
/// the copper into one shape. Keeping a point per object is enough to answer the only question the
/// check asks: which piece of copper is this net in.
///
/// **The point has to be strictly inside**, and that is the part worth testing rather than assuming.
/// A flash's centre and a stroke's midpoint are inside by construction. A region's vertices are not:
/// they lie on its own outline, where a point is neither in nor out, and the first vertex of a pour
/// can finish outside the union once neighbouring copper has merged into it. Measured on the Arduino
/// Mega before this was fixed: 15 of 21 regions misplaced — and a pour is exactly where a short
/// hides, so those are the 21 that matter most.
/// </summary>
public sealed class NetPointTests(ITestOutputHelper output)
{
    private static BoardLayer Copper(string board, LayerRole role = LayerRole.TopCopper) =>
        BoardLoader.LoadFolder(RealBoards.Directory(board)).Layers.Single(l => l.Role == role);

    /// <summary>
    /// Every point sits strictly inside the copper — not outside it, and not on its edge.
    ///
    /// On the edge is refused as firmly as outside: a point on a boundary belongs to no piece in
    /// particular, and a net placed there would be claimed by whichever side the arithmetic fell
    /// towards. That is a coin toss deciding whether two nets are reported as shorted.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.MillburnTestBoard)]
    [InlineData("Arduino_Mega_2560")]
    public void EveryNetPointIsInsideTheCopper(string board)
    {
        var layer = Copper(board);

        Assert.NotEmpty(layer.Nets);

        var strays = layer.Nets
            .Where(n => Inside(layer.Area, n.At) != PointInPolygonResult.IsInside)
            .ToList();

        output.WriteLine($"{board}: {layer.Nets.Count} net point(s) over {layer.Area.Count} ring(s)");

        Assert.True(
            strays.Count == 0,
            $"{strays.Count} net point(s) are not inside the copper, e.g. {strays.FirstOrDefault().Net} "
            + $"at {Nm.ToMillimetres(strays.FirstOrDefault().At.X):F3}, "
            + $"{Nm.ToMillimetres(strays.FirstOrDefault().At.Y):F3} mm");
    }

    /// <summary>
    /// The board's own nets, all of them, and no others.
    ///
    /// Read from the file rather than written out here: the test board is the author's and gains a
    /// net whenever he redraws it, and a hard-coded list would make that a failing test rather than
    /// a bigger board.
    /// </summary>
    [Fact]
    public void EveryNetTheFileNamesIsAccountedFor()
    {
        var folder = RealBoards.Directory(RealBoards.MillburnTestBoard);
        var file = Path.Combine(folder, "Millburn_Test_Board-F_Cu.gbr");

        var declared = GerberParser.ParseFile(file).Objects
            .Select(o => o.Net)
            .Where(n => n is not null)
            .Select(n => n!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var carried = Copper(RealBoards.MillburnTestBoard).Nets
            .Select(n => n.Net)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        output.WriteLine($"declared {declared.Count}, carried {carried.Count}");

        Assert.NotEmpty(declared);
        Assert.Equal(declared, carried);
    }

    /// <summary>
    /// Regions specifically, because they are the kind that was wrong and the kind that matters.
    ///
    /// The Mega is the only board here with poured regions carrying nets. If the realiser ever goes
    /// back to taking a region's first vertex, this is the test that says so — the count drops.
    /// </summary>
    [Fact]
    public void APouredRegionGetsANetPointToo()
    {
        var folder = RealBoards.Directory("Arduino_Mega_2560");
        var file = Path.Combine(folder, "Arduino Mega 2560-F_Cu.gbr");

        var regions = GerberParser.ParseFile(file).Objects
            .OfType<Gerber.Model.RegionObject>()
            .Count(r => r.Net is not null);

        Assert.True(regions > 0, "the fixture no longer has net-bearing regions to test with");

        var layer = Copper("Arduino_Mega_2560");
        var declared = GerberParser.ParseFile(file).Objects.Count(o => o.Net is not null);

        output.WriteLine($"{regions} net-bearing region(s) of {declared} net-bearing object(s)");

        // Every one of them, not most: a region that loses its net is a pour with no identity, and
        // the check built on this would report the pour as belonging to whatever else it touches.
        Assert.Equal(declared, layer.Nets.Count);
    }

    /// <summary>
    /// A file that names no nets carries none, and says nothing rather than guessing.
    ///
    /// `Millburn_Test_Board_Protel` is the same board exported with X2 switched off. Its layers are
    /// still identified — the roles come from `G04 #@! TF` comments — but no object declares a net,
    /// so there is nothing here to check a board against, and the check that follows must say so
    /// instead of reporting a board with no nets as a board with no shorts.
    /// </summary>
    [Fact]
    public void AFileWithoutNetsCarriesNone()
    {
        var layer = Copper("Millburn_Test_Board_Protel");

        Assert.NotEmpty(layer.Area);
        Assert.Empty(layer.Nets);
    }

    /// <summary>Where a point sits, counting a ring inside a ring as a hole.</summary>
    private static PointInPolygonResult Inside(Paths64 area, Point2 p)
    {
        var point = new Point64(p.X, p.Y);
        var crossings = 0;

        foreach (var ring in area)
        {
            var where = Clipper.PointInPolygon(point, ring);

            if (where == PointInPolygonResult.IsOn)
            {
                return PointInPolygonResult.IsOn;
            }

            if (where == PointInPolygonResult.IsInside)
            {
                crossings++;
            }
        }

        return crossings % 2 == 1 ? PointInPolygonResult.IsInside : PointInPolygonResult.IsOutside;
    }
}
