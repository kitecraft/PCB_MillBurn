using System.IO.Hashing;
using System.Text;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Export;
using MillBurn.Gerber;
using MillBurn.Viewer;
using Xunit;

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
    [Fact]
    public void SilkscreenSvgIsByteIdenticalAcrossRuns()
    {
        var file = BoardFile("MyGerbers2", "PogoTest1-F_Silkscreen.gbr");
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
    [Fact]
    public void OnlyTheTimestampVariesBetweenRuns()
    {
        var file = BoardFile("MyGerbers2", "PogoTest1-F_Silkscreen.gbr");

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

    private static string Render(string gerberFile, SvgExportOptions options)
    {
        var artwork = SilkscreenOperation.Build(
            GerberParser.ParseFile(gerberFile), new SilkscreenOptions(), Path.GetFileName(gerberFile));

        return SvgWriter.Write(
            artwork,
            SvgPage.ForContent(artwork.ContentBounds, Nm.FromMillimetres(5)),
            options);
    }

    private static string BoardFile(string board, string name)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, board, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new FileNotFoundException($"Could not locate {board}/{name}.");
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
