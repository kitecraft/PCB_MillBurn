using MillBurn.Gerber;
using MillBurn.Gerber.Apertures;
using MillBurn.Gerber.Excellon;
using MillBurn.Gerber.Model;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// End-to-end checks against real KiCad 10 exports committed under <c>MyGerbers/</c> and
/// <c>MyGerbers2/</c>.
///
/// Hand-written fixtures prove the parser handles the specification; these prove it handles what
/// an actual EDA tool actually writes, which is a different question and the one that decides
/// whether a board is cut correctly.
/// </summary>
public sealed class RealBoardTests
{
    private static string BoardDir(string name)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, name);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new DirectoryNotFoundException($"Could not locate the '{name}' board directory.");
    }

    private static GerberImage Gerber(string board, string file) =>
        GerberParser.ParseFile(Path.Combine(BoardDir(board), file));

    private static void AssertNoErrors(GerberImage image, string what) =>
        Assert.True(
            image.Diagnostics.All(d => !d.IsError),
            $"{what}: " + string.Join("; ", image.Diagnostics.Where(d => d.IsError).Take(5)));

    [Theory]
    [InlineData("MyGerbers")]
    [InlineData("MyGerbers2")]
    public void EveryLayerParsesWithoutErrors(string board)
    {
        var dir = BoardDir(board);
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
    [InlineData("MyGerbers")]
    [InlineData("MyGerbers2")]
    public void EveryDrillFileParsesWithoutErrors(string board)
    {
        var dir = BoardDir(board);
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
        var copper = Gerber("MyGerbers", "GridStripConnector-F_Cu.gbr");
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
        var copper = Gerber("MyGerbers", "GridStripConnector-F_Cu.gbr");
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
        var outline = Gerber("MyGerbers", "GridStripConnector-Edge_Cuts.gbr");
        AssertNoErrors(outline, "Edge_Cuts");

        var segments = outline.Objects.OfType<DrawObject>().SelectMany(d => d.Segments).ToList();
        Assert.Equal(8, segments.Count);
        Assert.Equal(4, segments.Count(s => s.IsArc));
    }

    [Fact]
    public void FileFunctionIdentifiesEachLayer()
    {
        Assert.Equal("Copper,L1,Top", Gerber("MyGerbers2", "PogoTest1-F_Cu.gbr").FileFunction);
        Assert.Equal("Copper,L2,Bot", Gerber("MyGerbers2", "PogoTest1-B_Cu.gbr").FileFunction);
        Assert.Equal("Profile,NP", Gerber("MyGerbers2", "PogoTest1-Edge_Cuts.gbr").FileFunction);
        Assert.Equal("Legend,Top", Gerber("MyGerbers2", "PogoTest1-F_Silkscreen.gbr").FileFunction);
        Assert.Equal("Soldermask,Bot", Gerber("MyGerbers2", "PogoTest1-B_Mask.gbr").FileFunction);
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
            Path.Combine(BoardDir("MyGerbers2"), "PogoTest1-PTH.drl"));
        var copper = Gerber("MyGerbers2", "PogoTest1-B_Cu.gbr");

        var pads = copper.Objects.OfType<FlashObject>()
            .Count(f => f.Aperture.Function == "ComponentPad");

        Assert.Equal(16, drill.Hits.Count);
        Assert.Equal(pads, drill.Hits.Count);
    }

    [Fact]
    public void CopperPoursParseAsRegions()
    {
        var copper = Gerber("MyGerbers2", "PogoTest1-B_Cu.gbr");
        AssertNoErrors(copper, "B_Cu");

        var regions = copper.Objects.OfType<RegionObject>().ToList();
        Assert.NotEmpty(regions);
        Assert.All(regions, r => Assert.NotEmpty(r.Contours));
    }

    /// <summary>
    /// Silkscreen is pure stroked line art with one narrow aperture, which makes it the easiest
    /// thing on the board to put on a laser: vector marking, no raster fill needed. See
    /// Documentation/04, section 2.7.
    /// </summary>
    [Fact]
    public void SilkscreenIsStrokedLineArt()
    {
        var silk = Gerber("MyGerbers2", "PogoTest1-F_Silkscreen.gbr");
        AssertNoErrors(silk, "F_Silkscreen");

        Assert.Empty(silk.Objects.OfType<FlashObject>());
        Assert.NotEmpty(silk.Objects.OfType<DrawObject>());

        var widths = silk.Apertures.Values.Select(a => a.NominalWidthNm).Distinct().ToList();
        Assert.All(widths, w => Assert.InRange(w, 50_000, 400_000));
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
