using MillBurn.Core;
using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>A layer as the scene builder wants it: identity, role, and rings in nanometres.</summary>
public readonly record struct BoardLayerSource(
    string Id,
    string Label,
    LayerRole Role,
    IReadOnlyList<IReadOnlyList<Point2>> Rings);

/// <summary>
/// Builds a scene from role-tagged layers, applying the default palette, paint order and default
/// visibility.
///
/// Separated from <see cref="BoardScene"/> so the scene stays a dumb bag of paths that knows
/// nothing about what a soldermask is, and so the app and the headless renderer cannot disagree
/// about draw order — which they would, eventually, if each sorted the list itself.
/// </summary>
public static class BoardSceneBuilder
{
    public static BoardScene Build(
        IEnumerable<BoardLayerSource> layers,
        Bounds? extent = null,
        Func<LayerRole, BoardLayerStyle>? palette = null,
        IReadOnlyList<BackplotLayer>? backplot = null,
        BoardLayerStyle? substrateStyle = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        palette ??= BoardPalette.For;

        var ordered = layers
            .OrderBy(l => LayerRoleInfo.DrawOrder(l.Role))
            .ThenBy(l => l.Id, StringComparer.Ordinal)
            .ToList();

        var painted = new List<(string Id, string Label, BoardLayerStyle Style, IReadOnlyList<IReadOnlyList<Point2>> Rings)>();

        var substrate = SubstrateFrom(ordered);
        if (substrate is not null)
        {
            painted.Add((SubstrateId, "Substrate", substrateStyle ?? BoardPalette.Substrate, substrate));
        }

        painted.AddRange(ordered.Select(l => (l.Id, l.Label, palette(l.Role), l.Rings)));

        // The program goes on last, over the copper it was made from. A backplot beside the board
        // answers a different and much less useful question.
        if (backplot is not null)
        {
            painted.AddRange(backplot.Select(b => (b.Id, b.Label, b.Style, b.Runs)));
        }

        var scene = BoardScene.Build(painted, extent);

        foreach (var source in ordered)
        {
            var layer = scene.Layer(source.Id);
            if (layer is not null)
            {
                layer.Visible = LayerRoleInfo.VisibleByDefault(source.Role);
            }
        }

        foreach (var source in backplot ?? [])
        {
            var layer = scene.Layer(source.Id);
            if (layer is not null)
            {
                layer.Visible = source.VisibleByDefault;
            }
        }

        return scene;
    }

    /// <summary>The synthetic layer's id, so callers can tell it from a real file.</summary>
    public const string SubstrateId = "(substrate)";

    /// <summary>
    /// The board's own outline, filled, to sit under everything else.
    ///
    /// The outline layer is a *stroked* profile, so it realises as a pair of rings — the outside
    /// and the inside of the pen. The largest by area is the outer boundary, and filling it gives
    /// the board plus half a pen width, which is near enough for something nobody cuts. A board
    /// with an interior cutout would have its cutout covered; that is a cosmetic limit on a visual
    /// aid, not a geometry error, and the real outline still draws on top.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<Point2>>? SubstrateFrom(IEnumerable<BoardLayerSource> layers)
    {
        var outline = layers.FirstOrDefault(l => l.Role == LayerRole.Outline);
        if (outline.Rings is null || outline.Rings.Count == 0)
        {
            return null;
        }

        IReadOnlyList<Point2>? largest = null;
        var largestArea = 0.0;

        foreach (var ring in outline.Rings)
        {
            var area = Math.Abs(SignedArea(ring));
            if (area > largestArea)
            {
                largestArea = area;
                largest = ring;
            }
        }

        return largest is null ? null : [largest];
    }

    private static double SignedArea(IReadOnlyList<Point2> ring)
    {
        var sum = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += ((double)a.X * b.Y) - ((double)b.X * a.Y);
        }

        return sum / 2.0;
    }

    /// <summary>
    /// A view transform that fits the scene into a viewport with a margin, in the same convention
    /// the controls use: screen = (world * scale) + offset.
    /// </summary>
    public static ViewTransform FitTo(SKRect bounds, SKRect viewport, float marginFraction = 0.06f)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
        {
            return new ViewTransform(1f, viewport.MidX, viewport.MidY);
        }

        var margin = 1f - (2f * marginFraction);
        var scale = Math.Min(viewport.Width * margin / bounds.Width, viewport.Height * margin / bounds.Height);

        return new ViewTransform(
            scale,
            viewport.MidX - (bounds.MidX * scale),
            viewport.MidY - (bounds.MidY * scale));
    }
}
