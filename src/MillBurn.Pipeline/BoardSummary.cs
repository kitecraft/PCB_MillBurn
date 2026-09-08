using MillBurn.Core;
using MillBurn.Geometry;

namespace MillBurn.Pipeline;

/// <summary>Cheap facts about a loaded board, for the panel.</summary>
public static class BoardSummary
{
    /// <summary>
    /// Everything here comes from geometry that has already been realised, so it costs nothing to
    /// compute and cannot disagree with what is on screen.
    /// </summary>
    public static BoardFacts Facts(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var copper = board.Layers.FirstOrDefault(l => l.Role == LayerRole.TopCopper)
            ?? board.Layers.FirstOrDefault(l => LayerRoleInfo.IsCopper(l.Role));

        var drills = board.Layers.Where(l => l.Drill is not null).Select(l => l.Drill!).ToList();

        var diameters = drills
            .SelectMany(d => d.Hits.Select(h => d.Tools.TryGetValue(h.Tool, out var t) ? t.DiameterNm : 0))
            .Where(d => d > 0)
            .ToList();

        return new BoardFacts
        {
            WidthMm = Nm.ToMillimetres(board.Bounds.Width),
            HeightMm = Nm.ToMillimetres(board.Bounds.Height),
            Layers = board.Layers.Count,
            Holes = drills.Sum(d => d.Hits.Count),
            HoleSizes = diameters.Distinct().Count(),
            SmallestHoleMm = diameters.Count == 0 ? 0 : Nm.ToMillimetres(diameters.Min()),
            LargestHoleMm = diameters.Count == 0 ? 0 : Nm.ToMillimetres(diameters.Max()),

            // Islands, not rings: a ring with negative area is a hole in another island, and
            // counting those would make a ground pour look like fifty separate pieces of copper.
            CopperIslands = copper is null
                ? 0
                : copper.Area.Count(r => Clipper2Lib.Clipper.Area(r) > 0),
            CopperAreaMm2 = copper is null ? 0 : Polygons.AreaMm2(copper.Area),
        };
    }
}
