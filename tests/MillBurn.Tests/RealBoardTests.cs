using System.Globalization;
using System.Xml.Linq;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Export;
using MillBurn.Gerber;
using MillBurn.Gerber.Apertures;
using MillBurn.Gerber.Excellon;
using MillBurn.Gerber.Model;

namespace MillBurn.Tests;

/// <summary>
/// End-to-end checks against real KiCad 10 exports.
///
/// Hand-written fixtures prove the parser handles the specification; these prove it handles what
/// an actual EDA tool actually writes, which is a different question and the one that decides
/// whether a board is cut correctly.
///
/// The boards are committed under <c>tests/boards/</c>, so these run everywhere rather than being
/// an optional extra that quietly stops covering anything.
/// </summary>
public sealed class RealBoardTests
{
    private static GerberImage Gerber(string board, string file) =>
        GerberParser.ParseFile(RealBoards.File(board, file));

    private static void AssertNoErrors(GerberImage image, string what) =>
        Assert.True(
            image.Diagnostics.All(d => !d.IsError),
            $"{what}: " + string.Join("; ", image.Diagnostics.Where(d => d.IsError).Take(5)));

    [Theory]
    [InlineData(RealBoards.GridStripConnector)]
    [InlineData(RealBoards.PogoTest1)]
    public void EveryLayerParsesWithoutErrors(string board)
    {
        var dir = RealBoards.Directory(board);
        var files = Directory.GetFiles(dir, "*.gbr");
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var image = GerberParser.ParseFile(file);
            AssertNoErrors(image, Path.GetFileName(file));
            Assert.NotEmpty(image.Objects);
            Assert.True(image.HasExtendedAttributes, $"{Path.GetFileName(file)} should carry X2 attributes");
        }
    }

    [Theory]
    [InlineData(RealBoards.GridStripConnector)]
    [InlineData(RealBoards.PogoTest1)]
    public void EveryDrillFileParsesWithoutErrors(string board)
    {
        var dir = RealBoards.Directory(board);
        var files = Directory.GetFiles(dir, "*.drl");
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var drill = ExcellonParser.ParseFile(file);
            Assert.True(
                drill.Diagnostics.All(d => !d.IsError),
                $"{Path.GetFileName(file)}: " + string.Join("; ", drill.Diagnostics.Where(d => d.IsError)));
            Assert.NotEqual(HolePlating.Unknown, drill.Plating);
        }
    }

    /// <summary>
    /// The whole argument for keeping X2 attributes: "open the mask over the pads" becomes a
    /// query rather than a morphological guess (Documentation/02, section 1).
    /// </summary>
    [Fact]
    public void PadsAreSelectableDirectlyFromAttributes()
    {
        var copper = Gerber(RealBoards.GridStripConnector, "GridStripConnector-F_Cu.gbr");
        AssertNoErrors(copper, "F_Cu");

        var pads = copper.Objects.OfType<FlashObject>().Where(f => f.Aperture.IsPad).ToList();
        Assert.Equal(6, pads.Count);
        Assert.All(pads, p => Assert.Equal("SMDPad", p.Aperture.Function));

        // And every pad knows its net, so an electrical check has something to check against.
        Assert.All(pads, p => Assert.NotNull(p.Net));
        Assert.Equal(3, pads.Select(p => p.Net).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void RoundRectMacroPadsResolveTheirParameters()
    {
        var copper = Gerber(RealBoards.GridStripConnector, "GridStripConnector-F_Cu.gbr");
        var aperture = copper.Apertures[10];

        Assert.Equal(ApertureKind.Macro, aperture.Kind);
        Assert.Equal("RoundRect", aperture.Macro!.Name);
        Assert.Equal(10, aperture.Parameters.Count);
        Assert.Equal(0.3, aperture.Parameters[0], 6);
    }

    [Fact]
    public void OutlineArcsSurviveAsArcs()
    {
        // The board outline has rounded corners. Flattening them at parse time is what stops
        // pcb2gcode ever re-fitting G2/G3 on output.
        var outline = Gerber(RealBoards.GridStripConnector, "GridStripConnector-Edge_Cuts.gbr");
        AssertNoErrors(outline, "Edge_Cuts");

        var segments = outline.Objects.OfType<DrawObject>().SelectMany(d => d.Segments).ToList();
        Assert.Equal(8, segments.Count);
        Assert.Equal(4, segments.Count(s => s.IsArc));
    }

    [Fact]
    public void FileFunctionIdentifiesEachLayer()
    {
        Assert.Equal("Copper,L1,Top", Gerber(RealBoards.PogoTest1, "PogoTest1-F_Cu.gbr").FileFunction);
        Assert.Equal("Copper,L2,Bot", Gerber(RealBoards.PogoTest1, "PogoTest1-B_Cu.gbr").FileFunction);
        Assert.Equal("Profile,NP", Gerber(RealBoards.PogoTest1, "PogoTest1-Edge_Cuts.gbr").FileFunction);
        Assert.Equal("Legend,Top", Gerber(RealBoards.PogoTest1, "PogoTest1-F_Silkscreen.gbr").FileFunction);
        Assert.Equal("Soldermask,Bot", Gerber(RealBoards.PogoTest1, "PogoTest1-B_Mask.gbr").FileFunction);
    }

    /// <summary>
    /// A cross-check between two independently parsed files: every plated hole should land on a
    /// through-hole pad. This is the shape of the electrical verification described in
    /// Documentation/02, section 5, and it catches unit and format errors that a single-file test
    /// cannot.
    /// </summary>
    [Fact]
    public void PlatedHoleCountMatchesThroughHolePadCount()
    {
        var drill = ExcellonParser.ParseFile(
            Path.Combine(RealBoards.Directory(RealBoards.PogoTest1), "PogoTest1-PTH.drl"));
        var copper = Gerber(RealBoards.PogoTest1, "PogoTest1-B_Cu.gbr");

        var pads = copper.Objects.OfType<FlashObject>()
            .Count(f => f.Aperture.Function == "ComponentPad");

        Assert.Equal(16, drill.Hits.Count);
        Assert.Equal(pads, drill.Hits.Count);
    }

    [Fact]
    public void CopperPoursParseAsRegions()
    {
        var copper = Gerber(RealBoards.PogoTest1, "PogoTest1-B_Cu.gbr");
        AssertNoErrors(copper, "B_Cu");

        var regions = copper.Objects.OfType<RegionObject>().ToList();
        Assert.NotEmpty(regions);
        Assert.All(regions, r => Assert.NotEmpty(r.Contours));
    }

    /// <summary>
    /// Silkscreen is pure stroked line art with one narrow aperture, which makes it the easiest
    /// thing on the board to put on a laser: trace the centrelines, because the beam is already
    /// the right width. See Documentation/04, section 2.5.
    /// </summary>
    [Fact]
    public void SilkscreenIsStrokedLineArt()
    {
        var silk = Gerber(RealBoards.PogoTest1, "PogoTest1-F_Silkscreen.gbr");
        AssertNoErrors(silk, "F_Silkscreen");

        Assert.Empty(silk.Objects.OfType<FlashObject>());
        Assert.NotEmpty(silk.Objects.OfType<DrawObject>());

        var widths = silk.Apertures.Values.Select(a => a.NominalWidthNm).Distinct().ToList();
        Assert.All(widths, w => Assert.InRange(w, 50_000, 400_000));
    }

    // ------------------------------------------------------------------ silkscreen to SVG

    /// <summary>
    /// The whole laser path end to end on a real board: parse, build artwork, write SVG.
    ///
    /// The assertions are all about *scale and placement*, because that is where this fails
    /// silently. A file with the wrong units opens perfectly in every viewer and burns a legend
    /// 4% small; nothing about it looks wrong until it is on the board.
    /// </summary>
    [Fact]
    public void RealSilkscreenExportsAsMillimetreAccurateSvg()
    {
        var silk = Gerber(RealBoards.PogoTest1, "PogoTest1-F_Silkscreen.gbr");
        var artwork = SilkscreenOperation.Build(silk, new SilkscreenOptions(), "PogoTest1-F_Silkscreen.gbr");

        // Every stroke on this layer is within the beam width, so nothing needs realising.
        var marks = Assert.Single(artwork.Layers);
        Assert.Equal("silk-mark", marks.Id);
        Assert.Equal(217, marks.SubpathCount);

        var margin = Nm.FromMillimetres(5);
        var page = SvgPage.ForContent(artwork.ContentBounds, margin);
        var svg = SvgWriter.Write(artwork, page, new SvgExportOptions { Timestamp = null });

        var root = XDocument.Parse(svg).Root!;
        Assert.Equal($"{page.WidthMm.ToString("0.####", CultureInfo.InvariantCulture)}mm",
            root.Attribute("width")!.Value);

        // The page is the content plus a 5 mm margin on each side, in real millimetres.
        Assert.Equal(Nm.ToMillimetres(artwork.ContentBounds.Width) + 10, page.WidthMm, 6);
        Assert.Equal(Nm.ToMillimetres(artwork.ContentBounds.Height) + 10, page.HeightMm, 6);

        // And every drawn coordinate is inside it. A point outside the viewBox is geometry the
        // operator will never see.
        var (minX, minY, maxX, maxY) = Extent(root);
        Assert.InRange(minX, 0, page.WidthMm);
        Assert.InRange(maxX, 0, page.WidthMm);
        Assert.InRange(minY, 0, page.HeightMm);
        Assert.InRange(maxY, 0, page.HeightMm);

        // The strokes sit exactly a margin plus half a stroke width in from the page edge.
        Assert.Equal(Nm.ToMillimetres(margin + (marks.Shapes.Min(s => s.StrokeWidthNm) / 2)), minX, 4);
    }

    /// <summary>
    /// Bottom silk must mirror, and the mirror must be about the shared page rather than about the
    /// geometry's own extents — otherwise the front and back exports do not line up on the bed.
    /// </summary>
    [Fact]
    public void MirroringBottomSilkReflectsAboutTheSharedPage()
    {
        var artwork = SilkscreenOperation.Build(
            Gerber(RealBoards.PogoTest1, "PogoTest1-B_Silkscreen.gbr"), new SilkscreenOptions());

        var page = SvgPage.ForContent(artwork.ContentBounds, Nm.FromMillimetres(5));
        var options = new SvgExportOptions { Timestamp = null };

        var front = Extent(XDocument.Parse(SvgWriter.Write(artwork, page, options)).Root!);
        var back = Extent(XDocument.Parse(SvgWriter.Write(artwork, page, options with { Mirror = true })).Root!);

        Assert.Equal(page.WidthMm - front.MaxX, back.MinX, 4);
        Assert.Equal(page.WidthMm - front.MinX, back.MaxX, 4);
        Assert.Equal(front.MinY, back.MinY, 4);
        Assert.Equal(front.MaxY, back.MaxY, 4);
    }

    /// <summary>Extent of every drawn coordinate in the document, in page millimetres.</summary>
    private static (double MinX, double MinY, double MaxX, double MaxY) Extent(XElement root)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var path in root.Descendants().Where(e => e.Name.LocalName == "path"))
        {
            var tokens = path.Attribute("d")!.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < tokens.Length; i++)
            {
                // Every command ends with an x/y pair, so the two numbers before the next command
                // (or the end) are a point.
                if (tokens[i] is not ("M" or "L" or "A"))
                {
                    continue;
                }

                var span = tokens[i] == "A" ? 7 : 2;
                var x = double.Parse(tokens[i + span - 1], CultureInfo.InvariantCulture);
                var y = double.Parse(tokens[i + span], CultureInfo.InvariantCulture);

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                i += span;
            }
        }

        return (minX, minY, maxX, maxY);
    }

    // ------------------------------------------------------------------ geometry realisation

    /// <summary>
    /// Every layer of both boards must realise into actual area. A layer that parses cleanly and
    /// then produces nothing is the exact shape of the two parser bugs found so far, and it is
    /// invisible unless something asserts that copper exists.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.GridStripConnector)]
    [InlineData(RealBoards.PogoTest1)]
    public void EveryLayerRealisesIntoArea(string board)
    {
        foreach (var file in RealBoards.Gerbers(board))
        {
            var image = GerberParser.ParseFile(file);
            var layer = GerberRealiser.Realise(image);
            var name = Path.GetFileName(file);

            Assert.True(layer.AreaMm2 > 0, $"{name}: realised to no area from {image.Objects.Count} objects");
            Assert.Equal(image.Objects.Count, layer.ObjectCount);
            Assert.DoesNotContain(layer.Notes, n => n.Contains("could not be realised", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A board outline is a stroked rectangle, so its realised area is its perimeter times the pen
    /// width. That makes it the one layer whose area can be predicted from first principles, and
    /// therefore the check that the whole stroke path is dimensionally right rather than merely
    /// plausible.
    /// </summary>
    [Fact]
    public void TheOutlineAreaIsItsPerimeterTimesThePenWidth()
    {
        var image = Gerber(RealBoards.PogoTest1, "PogoTest1-Edge_Cuts.gbr");
        var layer = GerberRealiser.Realise(image);

        var pen = Nm.ToMillimetres(image.Apertures.Values.Single().NominalWidthNm);
        var centreline = layer.Bounds.Inflate(-Nm.FromMillimetres(pen / 2));
        var perimeter = 2 * (Nm.ToMillimetres(centreline.Width) + Nm.ToMillimetres(centreline.Height));

        // Rounded corners shave a little off a true rectangle's perimeter, so allow 2%.
        var expected = perimeter * pen;
        Assert.InRange(layer.AreaMm2, expected * 0.95, expected * 1.02);

        // Outer ring and inner ring: a closed stroke, not a filled slab.
        Assert.Equal(2, layer.RingCount);
    }

    /// <summary>
    /// The copper on this board carries three named nets, and the pads on each are connected. So
    /// the realised copper must come out as exactly three islands — a count derived from the
    /// electrical model agreeing with one derived from the geometry, which is the shape of the
    /// verification described in Documentation/02, section 5.
    /// </summary>
    [Fact]
    public void CopperIslandsMatchTheNetCount()
    {
        var image = Gerber(RealBoards.GridStripConnector, "GridStripConnector-F_Cu.gbr");
        var layer = GerberRealiser.Realise(image);

        var nets = image.Objects
            .Select(o => o.Net)
            .Where(n => n is not null)
            .Distinct(StringComparer.Ordinal)
            .Count();

        Assert.Equal(3, nets);
        Assert.Equal(nets, layer.RingCount);
    }

    /// <summary>
    /// A soldermask layer is declared negative, and the realiser reports that rather than acting on
    /// it: the drawn area is the openings, which is what a mask-open laser pass needs. One ring per
    /// flash also proves no two openings were merged.
    /// </summary>
    [Fact]
    public void SoldermaskRealisesAsItsOpenings()
    {
        var image = Gerber(RealBoards.PogoTest1, "PogoTest1-F_Mask.gbr");
        var layer = GerberRealiser.Realise(image);

        Assert.True(layer.DeclaredNegative);
        Assert.Equal(image.Objects.Count, layer.RingCount);
        Assert.InRange(layer.AreaMm2, 50, 200);
    }

    /// <summary>
    /// The ground pour keeps its clearances: a plane realised as one solid slab would short every
    /// pad on the board, and the area alone would not show it.
    /// </summary>
    [Fact]
    public void ThePourKeepsItsClearances()
    {
        var layer = GerberRealiser.Realise(Gerber(RealBoards.PogoTest1, "PogoTest1-B_Cu.gbr"));

        Assert.True(layer.RingCount > 10, $"expected many rings for a pour with clearances; got {layer.RingCount}");

        // Comfortably filled, and comfortably not the whole board.
        var boardArea = Nm.ToMillimetres(layer.Bounds.Width) * Nm.ToMillimetres(layer.Bounds.Height);
        Assert.InRange(layer.AreaMm2 / boardArea, 0.4, 0.95);
    }

    [Fact]
    public void RealisationIsDeterministicOnARealBoard()
    {
        var image = Gerber(RealBoards.PogoTest1, "PogoTest1-F_Cu.gbr");
        var options = new RealisationOptions { Canonicalise = true };

        Assert.Equal(
            GerberRealiser.Realise(image, options).Area,
            GerberRealiser.Realise(image, options).Area);
    }

    /// <summary>
    /// The corpus is where the exotic macro primitives live — thermal, moire, outline, polygon, and
    /// the deprecated line codes — plus polarity levels and step-and-repeat. Nothing there may be
    /// silently skipped.
    /// </summary>
    [CorpusFact]
    public void ExternalCorpusRealisesWithoutSkippingAnything()
    {
        var failures = new List<string>();

        foreach (var file in GerberCorpus.AllGerbers())
        {
            var image = GerberParser.ParseFile(file);
            var layer = GerberRealiser.Realise(image);
            var name = Path.GetFileName(file);

            if (image.Objects.Count > 0 && layer.AreaMm2 <= 0)
            {
                failures.Add($"{name}: {image.Objects.Count} objects realised to no area");
            }

            foreach (var note in layer.Notes.Where(n => n.Contains("could not be realised", StringComparison.Ordinal)))
            {
                failures.Add($"{name}: {note}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // ------------------------------------------------------------------ external corpus

    /// <summary>
    /// The optional pcb2gcode corpus covers aperture-macro primitives, polarity levels, step and
    /// repeat, and single-quadrant arcs far more thoroughly than any board of ours does. It is not
    /// redistributed here (GPL-3.0), so this skips when the checkout is absent.
    /// </summary>
    [CorpusFact]
    public void ExternalCorpusParsesWithoutErrors()
    {
        var failures = new List<string>();
        var parsed = 0;

        foreach (var file in GerberCorpus.AllGerbers())
        {
            var image = GerberParser.ParseFile(file);
            parsed++;

            var errors = image.Diagnostics.Where(d => d.IsError).ToList();
            if (errors.Count > 0)
            {
                failures.Add($"{Path.GetFileName(file)}: {errors[0].Message}");
            }
        }

        Assert.True(parsed > 0, "corpus was located but contained no .gbr files");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
