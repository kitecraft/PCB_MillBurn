using System.IO.Hashing;
using System.Text;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Export;
using MillBurn.Gerber;
using MillBurn.Gerber.Model;
using MillBurn.Viewer;
using Xunit;

using MillBurn.Tests;

namespace MillBurn.GoldenTests;

/// <summary>
/// Guards the "output determinism" acceptance criterion from
/// Documentation/06-Roadmap-and-Risks.md: the same inputs must produce byte-identical output,
/// every run, on every machine.
///
/// Without this, golden-file testing is impossible and "did my change improve the toolpath?"
/// has no answer. pcb2gcode needs a dedicated consistent_rand.cpp to paper over the same problem;
/// the fix is to have no nondeterminism in the first place.
/// </summary>
public sealed class DeterminismTests
{
    private const char NewLine = '\n';

    [Fact]
    public void SameSeedProducesIdenticalGeometry()
    {
        var a = Fingerprint(SyntheticToolpath.Generate(20_000, seed: 1234));
        var b = Fingerprint(SyntheticToolpath.Generate(20_000, seed: 1234));

        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentGeometry()
    {
        var a = Fingerprint(SyntheticToolpath.Generate(20_000, seed: 1234));
        var b = Fingerprint(SyntheticToolpath.Generate(20_000, seed: 5678));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SceneBuildIsDeterministic()
    {
        var polylines = SyntheticToolpath.Generate(20_000, seed: 99);

        using var first = ToolpathScene.Build(polylines);
        using var second = ToolpathScene.Build(polylines);

        Assert.Equal(first.SourceSegmentCount, second.SourceSegmentCount);
        Assert.Equal(first.Bounds, second.Bounds);

        for (var tier = 0; tier < 4; tier++)
        {
            Assert.Equal(first.SegmentsAtTier(tier), second.SegmentsAtTier(tier));
        }
    }

    /// <summary>
    /// The SVG is a deliverable, not a preview, so it is held to the same rule: same input, same
    /// bytes. That is what lets a golden file catch a regression in the writer instead of someone
    /// having to open two drawings side by side.
    ///
    /// pcb2gcode needs a whole <c>consistent_rand.cpp</c> to paper over this in its own SVG
    /// output. The cheaper fix is to have no randomness to make consistent.
    /// </summary>
    [BoardFact]
    public void SilkscreenSvgIsByteIdenticalAcrossRuns()
    {
        var file = RealBoards.File("MyGerbers2", "PogoTest1-F_Silkscreen.gbr");
        var options = new SvgExportOptions { Timestamp = null };

        var first = Render(file, options);
        var second = Render(file, options);

        Assert.Equal(first, second);
        Assert.Contains("<path", first, StringComparison.Ordinal);
    }

    /// <summary>
    /// A timestamp is the one input that legitimately varies, so it stays opt-in; this proves it
    /// is the only thing that would break the guarantee above.
    /// </summary>
    [BoardFact]
    public void OnlyTheTimestampVariesBetweenRuns()
    {
        var file = RealBoards.File("MyGerbers2", "PogoTest1-F_Silkscreen.gbr");

        var plain = Render(file, new SvgExportOptions { Timestamp = null });
        var stamped = Render(file, new SvgExportOptions
        {
            Timestamp = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
        });

        Assert.NotEqual(plain, stamped);

        // Strip the one line the timestamp adds and the two are identical again.
        var stripped = string.Join(
            NewLine,
            stamped.Split(NewLine).Where(l => !l.StartsWith("Generated:", StringComparison.Ordinal)));

        Assert.Equal(plain, stripped);
    }

    /// <summary>
    /// The determinism guarantee has to hold without the optional board exports, or a clean clone
    /// stops checking the thing this file exists for. Same assertion, hand-written input: arcs,
    /// two stroke widths, a flash and a coordinate that does not land on a round number.
    /// </summary>
    [Fact]
    public void SvgOutputIsDeterministicWithoutTheOptionalBoards()
    {
        var first = RenderSource(SyntheticSilk);
        var second = RenderSource(SyntheticSilk);

        Assert.Equal(first, second);
        Assert.Contains(" A ", first, StringComparison.Ordinal);
        Assert.Contains("<path", first, StringComparison.Ordinal);
    }

    private const string SyntheticSilk =
        """
        %FSLAX46Y46*%
        %MOMM*%
        %ADD10C,0.10*%
        %ADD11C,0.15*%
        %ADD12C,1.0*%
        D10*
        X1234567Y0D02*
        X7654321Y1234567D01*
        D11*
        X0Y2000000D02*
        G03*
        X0Y2000000I1000000J0D01*
        D12*
        X3000000Y3000000D03*
        M02*
        """;

    private static string RenderSource(string gerber) =>
        Render(GerberParser.Parse(gerber), "synthetic.gbr", new SvgExportOptions { Timestamp = null });

    private static string Render(string gerberFile, SvgExportOptions options) =>
        Render(GerberParser.ParseFile(gerberFile), Path.GetFileName(gerberFile), options);

    private static string Render(GerberImage image, string source, SvgExportOptions options)
    {
        var artwork = SilkscreenOperation.Build(image, new SilkscreenOptions(), source);

        return SvgWriter.Write(
            artwork,
            SvgPage.ForContent(artwork.ContentBounds, Nm.FromMillimetres(5)),
            options);
    }

    private static string Fingerprint(IReadOnlyList<Polyline> polylines)
    {
        var hash = new XxHash128();
        var buffer = new byte[sizeof(float)];

        foreach (var pl in polylines)
        {
            hash.Append(BitConverter.GetBytes((int)pl.Style));
            foreach (var v in pl.Points)
            {
                BitConverter.TryWriteBytes(buffer, v);
                hash.Append(buffer);
            }
        }

        return Convert.ToHexString(hash.GetCurrentHash());
    }
}
