using MillBurn.Viewer;
using SkiaSharp;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Covers the level-of-detail and spatial-culling logic behind the viewport.
///
/// These exist because a bug in the per-tile bounds accumulator cost 3.2 seconds per frame at
/// high zoom while still rendering a picture that looked entirely correct. Culling defects are
/// invisible by inspection, so they need assertions.
/// </summary>
public sealed class ToolpathSceneTests
{
    private static Polyline Line(SegmentStyle style, params float[] pts) => new(style, pts);

    [Fact]
    public void TileBoundsDoNotStretchToTheOrigin()
    {
        // A polyline far from (0,0). If the bounds accumulator starts from a default rect
        // instead of the first point, its tile will claim everything back to the origin.
        var scene = ToolpathScene.Build(
        [
            Line(SegmentStyle.Isolation, 100, 100, 101, 100, 102, 101, 103, 101),
        ]);

        var nearOrigin = new SKRect(-1, -1, 1, 1);

        for (var tier = 0; tier < 4; tier++)
        {
            Assert.Empty(scene.VisiblePaths(tier, SegmentStyle.Isolation, nearOrigin));
            Assert.Equal(0, scene.SegmentsInView(tier, nearOrigin));
        }
    }

    [Fact]
    public void CullingKeepsGeometryThatIsActuallyVisible()
    {
        var scene = ToolpathScene.Build(
        [
            Line(SegmentStyle.Isolation, 0, 0, 10, 0, 20, 0),
            Line(SegmentStyle.Isolation, 0, 100, 10, 100, 20, 100),
        ]);

        var bottomBand = new SKRect(-5, -5, 25, 5);

        var visible = scene.SegmentsInView(0, bottomBand);
        Assert.InRange(visible, 1, 2);
        Assert.NotEmpty(scene.VisiblePaths(0, SegmentStyle.Isolation, bottomBand));
    }

    [Fact]
    public void EverythingIsVisibleWhenTheViewCoversTheScene()
    {
        var scene = ToolpathScene.Build(
        [
            Line(SegmentStyle.Isolation, 0, 0, 10, 0, 20, 0),
            Line(SegmentStyle.Outline, 0, 100, 10, 100, 20, 100),
        ]);

        var everything = new SKRect(-1000, -1000, 1000, 1000);

        Assert.Equal(scene.SegmentsAtTier(0), scene.SegmentsInView(0, everything));
    }

    [Fact]
    public void CoarserTiersNeverHaveMoreSegmentsThanFinerOnes()
    {
        var scene = ToolpathScene.Build(SyntheticToolpath.Generate(20_000));

        for (var tier = 1; tier < 4; tier++)
        {
            Assert.True(
                scene.SegmentsAtTier(tier) <= scene.SegmentsAtTier(tier - 1),
                $"tier {tier} ({scene.SegmentsAtTier(tier)}) should not exceed " +
                $"tier {tier - 1} ({scene.SegmentsAtTier(tier - 1)})");
        }
    }

    [Theory]
    [InlineData(1f, 3)]      // zoomed way out: coarsest tier is within half a pixel
    [InlineData(1000f, 0)]   // zoomed way in: only the finest tier is accurate enough
    public void TierSelectionRespectsTheHalfPixelErrorBudget(float pixelsPerMm, int expected)
    {
        Assert.Equal(expected, ToolpathScene.TierForScale(pixelsPerMm));
    }

    [Fact]
    public void VisibleWorldRectRoundTripsThroughTheViewTransform()
    {
        var viewport = SKRect.Create(1600, 900);
        var view = new ViewTransform(Scale: 12.5f, OffsetX: 340f, OffsetY: 610f);

        var world = ToolpathRenderer.VisibleWorldRect(viewport, view);

        // The world rect must map back onto the viewport corners under screen = world*s + offset,
        // with Y flipped. Getting this wrong silently culls the wrong half of the board.
        Assert.Equal(viewport.Left, (world.Left * view.Scale) + view.OffsetX, 3);
        Assert.Equal(viewport.Right, (world.Right * view.Scale) + view.OffsetX, 3);
        Assert.Equal(viewport.Bottom, (-world.Top * view.Scale) + view.OffsetY, 3);
        Assert.Equal(viewport.Top, (-world.Bottom * view.Scale) + view.OffsetY, 3);
    }

    [Fact]
    public void SimplificationPreservesEndpointsAndReducesDetail()
    {
        // A gently curving trace: simplification should drop interior points but must never
        // move the ends, or paths stop meeting where the optimizer joined them.
        var pts = new float[200];
        for (var i = 0; i < 100; i++)
        {
            pts[i * 2] = i * 0.1f;
            pts[(i * 2) + 1] = MathF.Sin(i * 0.05f) * 0.02f;
        }

        var scene = ToolpathScene.Build([new Polyline(SegmentStyle.Isolation, pts)]);

        Assert.Equal(99, scene.SegmentsAtTier(0));
        Assert.True(scene.SegmentsAtTier(3) < scene.SegmentsAtTier(0));

        var bounds = scene.Bounds;
        Assert.Equal(0f, bounds.Left, 4);
        Assert.Equal(9.9f, bounds.Right, 4);
    }
}
