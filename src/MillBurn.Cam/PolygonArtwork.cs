using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;

namespace MillBurn.Cam;

/// <summary>
/// Filled area to drawable artwork — the bridge from the boolean stage to every exporter.
///
/// One ring becomes one subpath and the whole set becomes a single shape, so an even-odd fill
/// turns the clockwise rings back into the holes they represent. Emitting each ring as its own
/// shape would fill every hole solid, which on a mask export means burning the clearance closed.
/// </summary>
public static class PolygonArtwork
{
    public static ArtShape ToShape(Paths64 area)
    {
        ArgumentNullException.ThrowIfNull(area);

        var subpaths = new List<IReadOnlyList<ArtSegment>>(area.Count);
        foreach (var ring in area)
        {
            if (ring.Count < 3)
            {
                continue;
            }

            var segments = new ArtSegment[ring.Count];
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                segments[i] = ArtSegment.Line(new Point2(a.X, a.Y), new Point2(b.X, b.Y));
            }

            subpaths.Add(segments);
        }

        return new ArtShape { Subpaths = subpaths, Filled = true };
    }

    public static Artwork ToArtwork(
        RealisedLayer layer, string id, string label, ArtRole role = ArtRole.Fill, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(layer);

        var shape = ToShape(layer.Area);

        return new Artwork
        {
            Layers = shape.Subpaths.Count == 0
                ? []
                : [new ArtLayer { Id = id, Label = label, Role = role, Shapes = [shape] }],
            ContentBounds = layer.Bounds,
            Notes = layer.Notes,
            Source = source,
        };
    }
}
