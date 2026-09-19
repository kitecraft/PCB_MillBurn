using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Gerber.Excellon;
using MillBurn.Gerber.Model;
using MillBurn.Geometry;

namespace MillBurn.Pipeline;

/// <summary>One loaded and realised layer.</summary>
public sealed record BoardLayer
{
    public required string FileName { get; init; }

    public required LayerRole Role { get; init; }

    /// <summary>True when the role came from the filename because the file declared none.</summary>
    public required bool RoleGuessed { get; init; }

    public required Paths64 Area { get; init; }

    public required Bounds Bounds { get; init; }

    public required int ObjectCount { get; init; }

    public bool DeclaredNegative { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    public IReadOnlyList<GerberDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>The drill file behind this layer, when it is one.</summary>
    public ExcellonFile? Drill { get; init; }

    public string Label => LayerRoleInfo.Label(Role, FileName);

    public double AreaMm2 => Polygons.AreaMm2(Area);

    public int RingCount => Area.Count;

    public bool HasErrors => Diagnostics.Any(d => d.IsError);

    /// <summary>
    /// The area as plain point rings, so a renderer can consume it without taking a dependency on
    /// the geometry stack. The copy is trivial next to keeping that boundary clean.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Point2>> Rings()
    {
        var rings = new List<IReadOnlyList<Point2>>(Area.Count);
        foreach (var path in Area)
        {
            var ring = new Point2[path.Count];
            for (var i = 0; i < path.Count; i++)
            {
                ring[i] = new Point2(path[i].X, path[i].Y);
            }

            rings.Add(ring);
        }

        return rings;
    }
}

/// <summary>Everything loaded from one export folder.</summary>
public sealed record Board
{
    public required string Source { get; init; }

    public required IReadOnlyList<BoardLayer> Layers { get; init; }

    /// <summary>Files that were found but could not be read at all.</summary>
    public IReadOnlyList<string> Failures { get; init; } = [];

    /// <summary>
    /// The board's extent, taken from the outline when there is one and from everything drawn when
    /// there is not. The outline is the better answer: copper is routinely pulled back from the
    /// edge, so a union of the copper understates the board and a view fitted to it is wrong.
    /// </summary>
    public Bounds Bounds
    {
        get
        {
            var outline = Layers.FirstOrDefault(l => l.Role == LayerRole.Outline);
            if (outline is not null && !outline.Bounds.IsEmpty)
            {
                return outline.Bounds;
            }

            var all = Core.Bounds.Empty;
            foreach (var layer in Layers)
            {
                all = all.Union(layer.Bounds);
            }

            return all;
        }
    }

    public IEnumerable<BoardLayer> InDrawOrder() =>
        Layers.OrderBy(l => LayerRoleInfo.DrawOrder(l.Role)).ThenBy(l => l.FileName, StringComparer.Ordinal);

    public int TotalObjects => Layers.Sum(l => l.ObjectCount);

    public int TotalRings => Layers.Sum(l => l.RingCount);

    public bool HasErrors => Layers.Any(l => l.HasErrors) || Failures.Count > 0;
}
