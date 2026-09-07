using System.IO.Hashing;
using System.Text;
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
