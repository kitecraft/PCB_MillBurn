using MillBurn.Core;

namespace MillBurn.Optimize;

/// <summary>
/// A uniform grid over points, for "which nodes are near this one".
///
/// A grid rather than a tree because the query that matters is bounded-radius nearest neighbours
/// over a few thousand points that are already spread across a board — the case a grid is best at
/// and a tree's extra structure buys nothing for. It also has no dependency and no tie-breaking
/// ambiguity, which matters because the whole optimizer has to give the same answer twice.
/// </summary>
public sealed class SpatialGrid
{
    private readonly Dictionary<(int X, int Y), List<int>> _cells = [];
    private readonly Point2[] _points;
    private readonly long _cell;

    public SpatialGrid(IReadOnlyList<Point2> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        _points = [.. points];
        _cell = CellSizeFor(_points);

        for (var i = 0; i < _points.Length; i++)
        {
            var key = KeyOf(_points[i]);
            if (!_cells.TryGetValue(key, out var bucket))
            {
                bucket = [];
                _cells[key] = bucket;
            }

            bucket.Add(i);
        }
    }

    /// <summary>
    /// Sized so an average cell holds a handful of points.
    ///
    /// Too large and every query scans everything; too small and the ring search walks empty cells
    /// forever. Deriving it from the actual extent means it adapts to a 20 mm board and a 200 mm
    /// panel without a magic number in either case.
    /// </summary>
    private static long CellSizeFor(Point2[] points)
    {
        if (points.Length == 0)
        {
            return Nm.FromMillimetres(1);
        }

        long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        var span = Math.Max(Math.Max(maxX - minX, maxY - minY), 1);
        var perSide = Math.Max((int)Math.Sqrt(points.Length / 4.0), 1);

        return Math.Max(span / perSide, 1);
    }

    private (int X, int Y) KeyOf(Point2 p) =>
        ((int)Math.Floor(p.X / (double)_cell), (int)Math.Floor(p.Y / (double)_cell));

    /// <summary>
    /// The <paramref name="k"/> nearest points to <paramref name="from"/>, excluding one index.
    ///
    /// Rings are searched outwards and the search only stops once the ring being examined is
    /// further away than the worst result held — stopping at the first ring that yields k would
    /// quietly return the wrong neighbours near a cell boundary.
    /// </summary>
    public IReadOnlyList<int> Nearest(Point2 from, int k, int exclude = -1)
    {
        if (k <= 0 || _points.Length == 0)
        {
            return [];
        }

        var origin = KeyOf(from);
        var best = new List<(double Distance, int Index)>(k + 1);

        for (var ring = 0; ; ring++)
        {
            // Everything in this ring is at least (ring-1) cells away, so once that already exceeds
            // the worst kept distance there is nothing further out that can beat it.
            if (best.Count >= k && (ring - 1) * (double)_cell > best[^1].Distance)
            {
                break;
            }

            var found = false;
            foreach (var key in Ring(origin, ring))
            {
                if (!_cells.TryGetValue(key, out var bucket))
                {
                    continue;
                }

                found = true;
                foreach (var index in bucket)
                {
                    if (index == exclude)
                    {
                        continue;
                    }

                    Consider(best, k, from.DistanceTo(_points[index]), index);
                }
            }

            // Nothing anywhere further out: the grid only spans the points it was built from.
            if (!found && ring > 0 && (ring * (double)_cell) > Span())
            {
                break;
            }
        }

        return [.. best.Select(b => b.Index)];
    }

    private double Span() => _cell * (double)Math.Max(_cells.Count, 1);

    /// <summary>Keeps the list sorted and capped, with the index as a deterministic tie-break.</summary>
    private static void Consider(List<(double Distance, int Index)> best, int k, double distance, int index)
    {
        if (best.Count >= k && distance >= best[^1].Distance)
        {
            return;
        }

        var at = best.FindIndex(b => distance < b.Distance || (distance == b.Distance && index < b.Index));
        if (at < 0)
        {
            at = best.Count;
        }

        best.Insert(at, (distance, index));

        if (best.Count > k)
        {
            best.RemoveAt(best.Count - 1);
        }
    }

    private static IEnumerable<(int X, int Y)> Ring((int X, int Y) centre, int ring)
    {
        if (ring == 0)
        {
            yield return centre;
            yield break;
        }

        for (var dx = -ring; dx <= ring; dx++)
        {
            for (var dy = -ring; dy <= ring; dy++)
            {
                // Only the shell, so a cell is never visited twice across rings.
                if (Math.Abs(dx) == ring || Math.Abs(dy) == ring)
                {
                    yield return (centre.X + dx, centre.Y + dy);
                }
            }
        }
    }
}
