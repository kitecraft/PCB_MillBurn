using System.Globalization;
using System.Xml.Linq;
using MillBurn.Core;
using MillBurn.Export;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// The SVG writer carries the entire laser output path (Documentation/04, section 1), so it is
/// tested like an emitter and not like a preview: units, scale, orientation, arc direction and
/// byte-for-byte determinism.
///
/// Every failure mode here is silent. A wrong scale produces a file that opens perfectly and burns
/// a board 4% small; a wrong sweep flag draws an arc the long way round; a wrong page origin means
/// two layers of the same job do not overlay.
/// </summary>
public sealed class SvgWriterTests
{
    private static readonly SvgExportOptions Plain = new()
    {
        Profile = SvgProfile.LightBurn,
        Timestamp = null,
    };

    private static SvgPage Page(double minXmm, double minYmm, double maxXmm, double maxYmm) =>
        new()
        {
            Frame = new Bounds(
                Nm.FromMillimetres(minXmm), Nm.FromMillimetres(minYmm),
                Nm.FromMillimetres(maxXmm), Nm.FromMillimetres(maxYmm)),
        };

    private static Point2 Mm(double x, double y) => new(Nm.FromMillimetres(x), Nm.FromMillimetres(y));

    private static Artwork Single(ArtShape shape, ArtRole role = ArtRole.Mark) => new()
    {
        Layers =
        [
            new ArtLayer { Id = "test", Label = "Test", Role = role, Shapes = [shape] },
        ],
        ContentBounds = ArtGeometry.Measure(shape),
    };

    private static ArtShape Stroke(params ArtSegment[] segments) => new()
    {
        Subpaths = [segments],
        StrokeWidthNm = Nm.FromMillimetres(0.1),
    };

    private static string PathData(string svg) =>
        XDocument.Parse(svg).Descendants().First(e => e.Name.LocalName == "path").Attribute("d")!.Value;

    // ------------------------------------------------------------------ page and units

    [Fact]
    public void HeaderIsInRealMillimetresNotPixels()
    {
        var svg = SvgWriter.Write(
            Single(Stroke(ArtSegment.Line(Mm(10, 10), Mm(20, 10)))),
            Page(0, 0, 100, 80),
            Plain);

        var root = XDocument.Parse(svg).Root!;

        Assert.Equal("100mm", root.Attribute("width")!.Value);
        Assert.Equal("80mm", root.Attribute("height")!.Value);
        Assert.Equal("0 0 100 80", root.Attribute("viewBox")!.Value);
    }

    /// <summary>
    /// Gerber counts Y upward and SVG counts it downward. Getting this wrong flips the board, and
    /// the drawing still looks entirely plausible until a legend comes out mirrored.
    /// </summary>
    [Fact]
    public void YIsFlippedBecauseSvgCountsDownward()
    {
        var svg = SvgWriter.Write(
            Single(Stroke(ArtSegment.Line(Mm(10, 70), Mm(10, 10)))),
            Page(0, 0, 100, 80),
            Plain);

        // y=70 in a frame topping out at 80 is 10 down the page; y=10 is 70 down.
        Assert.Equal("M 10 10 L 10 70", PathData(svg));
    }

    [Fact]
    public void CoordinatesAreRelativeToThePageOrigin()
    {
        var svg = SvgWriter.Write(
            Single(Stroke(ArtSegment.Line(Mm(150, -100), Mm(155, -100)))),
            Page(145, -110, 175, -90),
            Plain);

        Assert.Equal("M 5 10 L 10 10", PathData(svg));
    }

    /// <summary>
    /// The rule from Documentation/04, section 4.3. Two artworks at different extents, exported on
    /// one page, must place a shared point at the same page coordinate — otherwise the two burns
    /// land offset by the difference between their crops.
    /// </summary>
    [Fact]
    public void OnePageKeepsSeparateExportsInRegister()
    {
        var shared = Mm(20, 20);
        var page = Page(0, 0, 100, 80);

        var small = SvgWriter.Write(Single(Stroke(ArtSegment.Line(shared, Mm(21, 20)))), page, Plain);
        var large = SvgWriter.Write(Single(Stroke(ArtSegment.Line(shared, Mm(90, 75)))), page, Plain);

        Assert.StartsWith("M 20 60 ", PathData(small), StringComparison.Ordinal);
        Assert.StartsWith("M 20 60 ", PathData(large), StringComparison.Ordinal);
    }

    [Fact]
    public void PageForContentAddsAnEqualMarginOnEverySide()
    {
        var page = SvgPage.ForContent(
            new Bounds(Nm.FromMillimetres(10), Nm.FromMillimetres(10), Nm.FromMillimetres(30), Nm.FromMillimetres(20)),
            Nm.FromMillimetres(5));

        Assert.Equal(30.0, page.WidthMm, 6);
        Assert.Equal(20.0, page.HeightMm, 6);
        Assert.Equal(Nm.FromMillimetres(5), page.Frame.MinX);
    }

    // ------------------------------------------------------------------ arcs

    /// <summary>
    /// SVG's sweep flag counts positive angles, which with Y pointing down reads as clockwise on
    /// screen. Flipping Y therefore turns a Gerber counter-clockwise arc into sweep=1.
    /// </summary>
    [Fact]
    public void CounterClockwiseArcBecomesSweepOne()
    {
        var quarter = new ArtSegment(ArtSweep.CounterClockwise, Mm(10, 0), Mm(0, 10), Mm(0, 0));
        var svg = SvgWriter.Write(Single(Stroke(quarter)), Page(-20, -20, 20, 20), Plain);

        Assert.Equal("M 30 20 A 10 10 0 0 1 20 10", PathData(svg));
    }

    [Fact]
    public void ClockwiseArcBecomesSweepZero()
    {
        var quarter = new ArtSegment(ArtSweep.Clockwise, Mm(0, 10), Mm(10, 0), Mm(0, 0));
        var svg = SvgWriter.Write(Single(Stroke(quarter)), Page(-20, -20, 20, 20), Plain);

        Assert.Equal("M 20 10 A 10 10 0 0 0 30 20", PathData(svg));
    }

    [Fact]
    public void ArcsOverHalfATurnSetTheLargeArcFlag()
    {
        // Three quarters counter-clockwise: from +X round to -Y.
        var threeQuarters = new ArtSegment(ArtSweep.CounterClockwise, Mm(10, 0), Mm(0, -10), Mm(0, 0));
        var svg = SvgWriter.Write(Single(Stroke(threeQuarters)), Page(-20, -20, 20, 20), Plain);

        Assert.Equal("M 30 20 A 10 10 0 1 1 20 30", PathData(svg));
    }

    /// <summary>
    /// A Gerber full circle has coincident endpoints. SVG cannot express that in one arc command —
    /// it degenerates to nothing — so it must be split at the antipode. KiCad draws circular silk
    /// outlines this way, so a circle that silently vanishes is a real outcome.
    /// </summary>
    [Fact]
    public void FullCircleIsSplitIntoTwoArcs()
    {
        var start = Mm(10, 0);
        var circle = new ArtSegment(ArtSweep.CounterClockwise, start, start, Mm(0, 0));

        var svg = SvgWriter.Write(Single(Stroke(circle)), Page(-20, -20, 20, 20), Plain);
        var d = PathData(svg);

        Assert.Equal(2, d.Split(" A ").Length - 1);
        Assert.Equal("M 30 20 A 10 10 0 0 1 10 20 A 10 10 0 0 1 30 20 Z", d);
    }

    // ------------------------------------------------------------------ mirroring

    [Fact]
    public void MirroringReflectsAboutThePageAndNotTheGeometry()
    {
        var svg = SvgWriter.Write(
            Single(Stroke(ArtSegment.Line(Mm(10, 40), Mm(30, 40)))),
            Page(0, 0, 100, 80),
            Plain with { Mirror = true });

        Assert.Equal("M 90 40 L 70 40", PathData(svg));
    }

    /// <summary>
    /// A mirror reverses handedness, so an arc that swept one way now sweeps the other. Forgetting
    /// this draws the complementary arc — a bulge where there should be a hollow — which is exactly
    /// the class of error that only shows up on the bottom side of a board.
    /// </summary>
    [Fact]
    public void MirroringFlipsArcDirection()
    {
        var quarter = new ArtSegment(ArtSweep.CounterClockwise, Mm(10, 0), Mm(0, 10), Mm(0, 0));

        var normal = PathData(SvgWriter.Write(Single(Stroke(quarter)), Page(-20, -20, 20, 20), Plain));
        var mirrored = PathData(SvgWriter.Write(
            Single(Stroke(quarter)), Page(-20, -20, 20, 20), Plain with { Mirror = true }));

        Assert.Contains(" 0 1 ", normal, StringComparison.Ordinal);
        Assert.Contains(" 0 0 ", mirrored, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ fills and holes

    [Fact]
    public void ClosedSubpathsEndWithZ()
    {
        var square = new ArtShape
        {
            Subpaths = [ApertureSquare(Mm(0, 0), 10)],
            Filled = true,
        };

        Assert.EndsWith("Z", PathData(SvgWriter.Write(Single(square), Page(-20, -20, 20, 20), Plain)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void OpenSubpathsDoNotGetZ()
    {
        var svg = SvgWriter.Write(
            Single(Stroke(ArtSegment.Line(Mm(0, 0), Mm(10, 0)))),
            Page(-20, -20, 20, 20),
            Plain);

        Assert.DoesNotContain("Z", PathData(svg), StringComparison.Ordinal);
    }

    /// <summary>
    /// A hole has to be a subpath of the same path element with an even-odd rule. Emitted as its
    /// own shape it becomes a second solid disc, and the pad burns closed.
    /// </summary>
    [Fact]
    public void HolesAreSubpathsOfTheSamePathWithEvenOdd()
    {
        var withHole = new ArtShape
        {
            Subpaths = [ApertureSquare(Mm(0, 0), 10), ApertureSquare(Mm(0, 0), 4)],
            Filled = true,
        };

        var svg = SvgWriter.Write(Single(withHole, ArtRole.Fill), Page(-20, -20, 20, 20), Plain);
        var path = XDocument.Parse(svg).Descendants().Single(e => e.Name.LocalName == "path");

        Assert.Equal("evenodd", path.Attribute("fill-rule")!.Value);
        Assert.Equal(2, path.Attribute("d")!.Value.Split('M').Length - 1);
    }

    [Fact]
    public void DisconnectedSegmentsStartNewSubpathsInsteadOfJoiningUp()
    {
        var shape = new ArtShape
        {
            Subpaths =
            [
                new[]
                {
                    ArtSegment.Line(Mm(0, 0), Mm(1, 0)),
                    ArtSegment.Line(Mm(5, 5), Mm(6, 5)),
                },
            ],
            StrokeWidthNm = Nm.FromMillimetres(0.1),
        };

        Assert.Equal("M 20 20 L 21 20 M 25 15 L 26 15",
            PathData(SvgWriter.Write(Single(shape), Page(-20, -20, 20, 20), Plain)));
    }

    // ------------------------------------------------------------------ profiles

    /// <summary>
    /// LightBurn assigns an imported object to a cut layer by its stroke colour, so the colour is a
    /// wire format rather than styling and must be on the element itself.
    /// </summary>
    [Fact]
    public void LightBurnProfilePutsThePaletteColourOnEveryElement()
    {
        var svg = SvgWriter.Write(
            Single(Stroke(ArtSegment.Line(Mm(0, 0), Mm(1, 0))), ArtRole.Mark),
            Page(-20, -20, 20, 20),
            Plain);

        var path = XDocument.Parse(svg).Descendants().Single(e => e.Name.LocalName == "path");

        Assert.Equal("#00E000", path.Attribute("stroke")!.Value);
        Assert.DoesNotContain("<style>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void InkscapeProfileStylesByClassSoThePaletteIsEditableInOnePlace()
    {
        var svg = SvgWriter.Write(
            Single(Stroke(ArtSegment.Line(Mm(0, 0), Mm(1, 0))), ArtRole.Mark),
            Page(-20, -20, 20, 20),
            Plain with { Profile = SvgProfile.Inkscape });

        var path = XDocument.Parse(svg).Descendants().Single(e => e.Name.LocalName == "path");

        Assert.Contains("<style>", svg, StringComparison.Ordinal);
        Assert.Null(path.Attribute("stroke"));
        Assert.Contains(".mark { stroke: #2E7D32", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void LayersCarryBothAnIdAndAnInkscapeLabel()
    {
        var svg = SvgWriter.Write(
            Single(Stroke(ArtSegment.Line(Mm(0, 0), Mm(1, 0)))),
            Page(-20, -20, 20, 20),
            Plain);

        var g = XDocument.Parse(svg).Descendants().Single(e => e.Name.LocalName == "g");

        Assert.Equal("test", g.Attribute("id")!.Value);
        Assert.Equal("layer", g.Attributes().Single(a => a.Name.LocalName == "groupmode").Value);
        Assert.Equal("Test", g.Attributes().Single(a => a.Name.LocalName == "label").Value);
    }

    // ------------------------------------------------------------------ determinism and safety

    [Fact]
    public void TwoRunsAreByteIdentical()
    {
        var artwork = Single(Stroke(
            ArtSegment.Line(Mm(0, 0), Mm(1.23456789, 0)),
            new ArtSegment(ArtSweep.Clockwise, Mm(1.23456789, 0), Mm(2, 1), Mm(2, 0))));

        var a = SvgWriter.Write(artwork, Page(-20, -20, 20, 20), Plain);
        var b = SvgWriter.Write(artwork, Page(-20, -20, 20, 20), Plain);

        Assert.Equal(a, b);
    }

    /// <summary>
    /// A timestamp is the one thing that would stop two runs being identical, so it is opt-in.
    /// </summary>
    [Fact]
    public void TimestampIsOmittedUnlessSupplied()
    {
        var artwork = Single(Stroke(ArtSegment.Line(Mm(0, 0), Mm(1, 0))));

        Assert.DoesNotContain("Generated:", SvgWriter.Write(artwork, Page(-20, -20, 20, 20), Plain),
            StringComparison.Ordinal);

        var stamped = SvgWriter.Write(
            artwork,
            Page(-20, -20, 20, 20),
            Plain with { Timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero) });

        Assert.Contains("Generated: 2026-01-02 03:04:05Z", stamped, StringComparison.Ordinal);
    }

    /// <summary>
    /// Notes carry the applied compensations and anything that could not be realised. They go in
    /// the file so it can still answer "why is this narrow?" once the project is gone.
    /// </summary>
    [Fact]
    public void NotesAreEmbeddedInTheDescription()
    {
        var artwork = Single(Stroke(ArtSegment.Line(Mm(0, 0), Mm(1, 0)))) with
        {
            Notes = ["outlines shrunk 0.055 mm = 0.045 kerf + 0.010 etch bias"],
        };

        var svg = SvgWriter.Write(artwork, Page(-20, -20, 20, 20), Plain);

        Assert.Contains("Note: outlines shrunk 0.055 mm", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkupInLabelsIsEscaped()
    {
        var artwork = new Artwork
        {
            Layers =
            [
                new ArtLayer
                {
                    Id = "x",
                    Label = "R1 <2 & \"3\"",
                    Role = ArtRole.Mark,
                    Shapes = [Stroke(ArtSegment.Line(Mm(0, 0), Mm(1, 0)))],
                },
            ],
            ContentBounds = new Bounds(0, 0, Nm.FromMillimetres(1), 0),
        };

        var svg = SvgWriter.Write(artwork, Page(-20, -20, 20, 20), Plain);

        Assert.Contains("R1 &lt;2 &amp; &quot;3&quot;", svg, StringComparison.Ordinal);

        // And it still parses, which is the point of escaping it.
        Assert.NotNull(XDocument.Parse(svg).Root);
    }

    /// <summary>
    /// A German or French machine formats 1.5 as "1,5". An SVG carrying that is not merely wrong,
    /// it is unparseable — so every number goes out invariant regardless of the ambient culture.
    ///
    /// The build already sets <c>InvariantGlobalization</c>, which is why a named culture cannot
    /// even be constructed here; the belt-and-braces defence is that the writer formats
    /// explicitly rather than relying on that setting staying switched on.
    /// </summary>
    [Fact]
    public void OutputIsInvariantOfTheAmbientCulture()
    {
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NumberGroupSeparator = ".";

        var artwork = Single(Stroke(ArtSegment.Line(Mm(1.5, 0), Mm(2.5, 0))));
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = comma;

            // Sanity: the substituted culture really would produce a comma.
            Assert.Equal("1,5", 1.5.ToString("0.####", CultureInfo.CurrentCulture));

            Assert.Equal("M 21.5 20 L 22.5 20", PathData(SvgWriter.Write(artwork, Page(-20, -20, 20, 20), Plain)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static ArtSegment[] ApertureSquare(Point2 centre, double sizeMm)
    {
        var h = Nm.FromMillimetres(sizeMm) / 2;
        var a = new Point2(centre.X - h, centre.Y - h);
        var b = new Point2(centre.X + h, centre.Y - h);
        var c = new Point2(centre.X + h, centre.Y + h);
        var d = new Point2(centre.X - h, centre.Y + h);

        return [ArtSegment.Line(a, b), ArtSegment.Line(b, c), ArtSegment.Line(c, d), ArtSegment.Line(d, a)];
    }
}
