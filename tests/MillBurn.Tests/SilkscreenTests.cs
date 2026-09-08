using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Gerber;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Silkscreen is the first thing this project can usefully ship, because it needs no geometry
/// realisation: the stroke width the legend is drawn at already matches the laser spot, so the
/// Gerber's own segments are the output (Documentation/04, section 2.5).
///
/// What these tests pin down is the decision that makes that true — which strokes are narrow
/// enough to trace at 1x — and the honesty rules around everything that is *not* yet realised.
/// </summary>
public sealed class SilkscreenTests
{
    private static string Silk(string body) =>
        "%FSLAX46Y46*%\n%MOMM*%\n" + body + "M02*\n";

    private static Artwork Build(string gerber, SilkscreenOptions? options = null) =>
        SilkscreenOperation.Build(GerberParser.Parse(gerber), options ?? new SilkscreenOptions());

    private static ArtLayer Layer(Artwork art, string id) =>
        art.Layers.Single(l => l.Id == id);

    [Fact]
    public void StrokesNoWiderThanTheBeamAreTracedAtOneTimes()
    {
        var art = Build(Silk(
            """
            %ADD10C,0.10*%
            D10*
            X0Y0D02*
            X1000000Y0D01*
            """));

        var layer = Layer(art, "silk-mark");

        Assert.Equal(ArtRole.Mark, layer.Role);
        Assert.Equal(1, layer.SubpathCount);
        Assert.Equal(Nm.FromMillimetres(0.10), layer.Shapes[0].StrokeWidthNm);
        Assert.False(layer.Shapes[0].Filled);
    }

    /// <summary>
    /// KiCad's 0.15 mm silk aperture against a 0.10 mm spot is the common case, and it must land on
    /// the centreline path — demanding an exact match would push ordinary boards onto the fill
    /// route for no visible gain.
    /// </summary>
    [Fact]
    public void TheDefaultToleranceCoversKicadsUsualSilkWidth()
    {
        var art = Build(Silk(
            """
            %ADD10C,0.15*%
            D10*
            X0Y0D02*
            X1000000Y0D01*
            """));

        Assert.Single(art.Layers);
        Assert.Equal("silk-mark", art.Layers[0].Id);
    }

    /// <summary>
    /// A stroke much wider than the beam cannot be laid down by tracing it once. Emitting it
    /// quietly as a centreline would produce a legend that is simply too thin, so it goes on its
    /// own layer and the artwork says so.
    /// </summary>
    [Fact]
    public void StrokesWiderThanTheBeamAreSeparatedAndReported()
    {
        var art = Build(Silk(
            """
            %ADD10C,0.10*%
            %ADD11C,0.80*%
            D10*
            X0Y0D02*
            X1000000Y0D01*
            D11*
            X0Y2000000D02*
            X1000000Y2000000D01*
            """));

        Assert.Equal(1, Layer(art, "silk-mark").SubpathCount);
        Assert.Equal(1, Layer(art, "silk-wide").SubpathCount);
        Assert.Equal(ArtRole.Boundary, Layer(art, "silk-wide").Role);

        Assert.Contains(art.Notes, n => n.Contains("wider than the beam", StringComparison.Ordinal));
        Assert.Contains(art.Notes, n => n.Contains("0.800 mm", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSpotSizeMovesTheThreshold()
    {
        const string Gerber = """
            %ADD10C,0.30*%
            D10*
            X0Y0D02*
            X1000000Y0D01*
            """;

        var narrowBeam = Build(Silk(Gerber), new SilkscreenOptions { SpotSizeNm = Nm.FromMillimetres(0.06) });
        var wideBeam = Build(Silk(Gerber), new SilkscreenOptions { SpotSizeNm = Nm.FromMillimetres(0.25) });

        Assert.Equal("silk-wide", narrowBeam.Layers[0].Id);
        Assert.Equal("silk-mark", wideBeam.Layers[0].Id);
    }

    /// <summary>
    /// The parser batches consecutive D01s into one object, so one object can hold several
    /// disconnected runs. Joining them would draw a line across the board between two glyphs.
    /// </summary>
    [Fact]
    public void DisconnectedRunsBecomeSeparateSubpaths()
    {
        var art = Build(Silk(
            """
            %ADD10C,0.10*%
            D10*
            X0Y0D02*
            X1000000Y0D01*
            X5000000Y5000000D02*
            X6000000Y5000000D01*
            """));

        Assert.Equal(2, Layer(art, "silk-mark").SubpathCount);
    }

    /// <summary>
    /// Strokes of one width share a style, so they merge into one path element. KiCad writes silk
    /// as a move plus a draw per segment, so a legend arrives as hundreds of single-segment
    /// objects; one element each is correct but wasteful.
    /// </summary>
    [Fact]
    public void StrokesOfTheSameWidthMergeIntoOneElement()
    {
        var art = Build(Silk(
            """
            %ADD10C,0.10*%
            %ADD11C,0.15*%
            D10*
            X0Y0D02*
            X1000000Y0D01*
            X2000000Y0D02*
            X3000000Y0D01*
            D11*
            X0Y1000000D02*
            X1000000Y1000000D01*
            """));

        var layer = Layer(art, "silk-mark");

        Assert.Equal(3, layer.SubpathCount);
        Assert.Equal(2, layer.ShapeCount);
        Assert.Equal(
            [Nm.FromMillimetres(0.10), Nm.FromMillimetres(0.15)],
            layer.Shapes.Select(s => s.StrokeWidthNm));
    }

    [Fact]
    public void CircularFlashesBecomeClosedFilledShapes()
    {
        var art = Build(Silk(
            """
            %ADD10C,1.0*%
            D10*
            X0Y0D03*
            """));

        var shape = Layer(art, "silk-fill").Shapes[0];

        Assert.True(shape.Filled);
        Assert.Single(shape.Subpaths);
        Assert.True(shape.Subpaths[0][0].IsArc);
        Assert.Equal(2 * Math.PI, shape.Subpaths[0][0].SweptAngle(), 6);
    }

    /// <summary>
    /// The hole is a subpath of the same shape, wound the other way so a non-zero fill reads it as
    /// a hole. As its own shape it would be a second solid disc.
    /// </summary>
    [Fact]
    public void AnApertureHoleBecomesASecondSubpathOfTheSameShape()
    {
        var art = Build(Silk(
            """
            %ADD10C,1.0X0.4*%
            D10*
            X0Y0D03*
            """));

        Assert.Equal(2, Layer(art, "silk-fill").Shapes[0].Subpaths.Count);
    }

    [Fact]
    public void RectangularFlashesBecomeFourSidedClosedShapes()
    {
        var art = Build(Silk(
            """
            %ADD10R,2.0X1.0*%
            D10*
            X0Y0D03*
            """));

        var subpath = Layer(art, "silk-fill").Shapes[0].Subpaths[0];

        Assert.Equal(4, subpath.Count);
        Assert.Equal(subpath[0].From, subpath[^1].To);
        Assert.Equal(new Bounds(-1_000_000, -500_000, 1_000_000, 500_000), ArtGeometry.Measure(subpath));
    }

    /// <summary>
    /// Macro apertures compose primitives with additive and subtractive polarity, so they need the
    /// boolean stage rather than an outline. Until it exists they are counted and named, never
    /// silently dropped — a missing pad opening is not something to discover on the machine.
    /// </summary>
    [Fact]
    public void UnrealisedMacroFlashesAreCountedAndNamed()
    {
        var art = Build(Silk(
            """
            %AMROUNDRECT*
            21,1,$1,$2,0,0,0*%
            %ADD10ROUNDRECT,1.0X0.5*%
            D10*
            X0Y0D03*
            X1000000Y0D03*
            """));

        Assert.DoesNotContain(art.Layers, l => l.Id == "silk-fill");
        Assert.Contains(art.Notes, n => n.Contains("2 flashes of aperture 'ROUNDRECT'", StringComparison.Ordinal));
    }

    /// <summary>
    /// Clear polarity subtracts from what came before, which needs compositing. Drawing it as dark
    /// would put ink where the board has none, so it is skipped and declared.
    /// </summary>
    [Fact]
    public void ClearPolarityObjectsAreSkippedAndDeclared()
    {
        var art = Build(Silk(
            """
            %ADD10C,0.10*%
            D10*
            X0Y0D02*
            X1000000Y0D01*
            %LPC*%
            X0Y1000000D02*
            X1000000Y1000000D01*
            """));

        Assert.Equal(1, Layer(art, "silk-mark").SubpathCount);
        Assert.Contains(art.Notes, n => n.Contains("clear-polarity", StringComparison.Ordinal));
    }

    /// <summary>
    /// The page is sized from this, so a full circle whose endpoints coincide has to measure as a
    /// circle and not as a point.
    /// </summary>
    [Fact]
    public void ContentBoundsCoverArcsAndHalfTheStrokeWidth()
    {
        var art = Build(Silk(
            """
            %ADD10C,0.20*%
            D10*
            X1000000Y0D02*
            G03*
            X1000000Y0I-1000000J0D01*
            """));

        // A 1 mm radius circle drawn with a 0.2 mm pen reaches 1.1 mm from the centre.
        Assert.Equal(-1_100_000, art.ContentBounds.MinX);
        Assert.Equal(1_100_000, art.ContentBounds.MaxX);
        Assert.Equal(-1_100_000, art.ContentBounds.MinY);
        Assert.Equal(1_100_000, art.ContentBounds.MaxY);
    }

    [Fact]
    public void TheBeamAssumptionIsRecordedInTheArtwork()
    {
        var art = Build(Silk(
            """
            %ADD10C,0.10*%
            D10*
            X0Y0D02*
            X1000000Y0D01*
            """));

        Assert.Contains(art.Notes, n => n.Contains("Beam spot 0.100 mm", StringComparison.Ordinal));
        Assert.Contains(art.Notes, n => n.Contains("Widest stroke on this layer: 0.100 mm", StringComparison.Ordinal));
    }
}
