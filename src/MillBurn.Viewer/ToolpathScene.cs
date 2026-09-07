using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// Visual role of a run of toolpath. Maps 1:1 onto the Path* colour tokens in Themes/Tokens.axaml
/// so the viewport, the legend and the SVG export cannot drift apart.
/// </summary>
public enum SegmentStyle
{
    Isolation,
    Pocket,
    Drill,
    Outline,
    Laser,
    Fiducial,
    Travel,
    RapidLong,
}

/// <summary>A run of connected points sharing one style. Coordinates are world mm.</summary>
public sealed class Polyline(SegmentStyle style, float[] points)
{
    public SegmentStyle Style { get; } = style;

    /// <summary>Interleaved x,y pairs. Length is always even.</summary>
    public float[] Points { get; } = points;

    public int SegmentCount => Math.Max(0, (Points.Length / 2) - 1);
}

/// <summary>
/// Render-ready form of a toolpath.
///
/// Two techniques together are what make a 500k-segment board interactive:
///
///   Level of detail — four progressively simplified copies, so a zoomed-out view draws far
///   fewer segments than it has. Chosen so the simplification error stays under half a pixel.
///
///   Spatial tiling — each tier is bucketed into a grid, with one prebuilt SKPath per
///   (tile, style). A frame draws only the tiles that intersect the viewport. Without it,
///   zooming in is the *worst* case: the finest tier is selected and every segment is
///   transformed even though almost none are on screen (measured at 2 fps).
///
/// The culling is only as good as the per-tile bounds, which is worth remembering: an early bug
/// that let those bounds stretch back to the origin cost 3.2 seconds a frame at 40x zoom while
/// still looking correct on screen. Frame cost is in the viewport overlay for exactly this
/// reason.
///
/// See Documentation/05-Viewer-and-Export.md section 2.2.
/// </summary>
public sealed class ToolpathScene : IDisposable
{
    /// <summary>Simplification tolerance of tier 0, in world mm. Tier n doubles it.</summary>
    private const float BaseToleranceMm = 0.005f;

    private const int TierCount = 4;

    /// <summary>
    /// Grid resolution per tier. Coarse tiers are used when zoomed out, where everything is
    /// visible anyway and tiling would only add draw calls; fine tiers are used when zoomed in,
    /// where culling does all the work.
    /// </summary>
    /// Coarse on purpose. Skia rejects off-screen contours by bounds very cheaply on its own, so
    /// the grid only has to discard most of the board; splitting further just multiplies draw
    /// calls without saving rasterisation. Measured with --probe.
    private static readonly int[] GridSize = [8, 4, 2, 1];

    private static readonly int StyleCount = Enum.GetValues<SegmentStyle>().Length;

    private readonly Tier[] _tiers = new Tier[TierCount];

    public SKRect Bounds { get; private set; }

    /// <summary>Total segments at full detail, before any simplification.</summary>
    public int SourceSegmentCount { get; private set; }

    private ToolpathScene()
    {
    }

    public static ToolpathScene Build(IReadOnlyList<Polyline> polylines)
    {
        ArgumentNullException.ThrowIfNull(polylines);

        var scene = new ToolpathScene();

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var pl in polylines)
        {
            var pts = pl.Points;
            scene.SourceSegmentCount += pl.SegmentCount;
            for (var i = 0; i < pts.Length; i += 2)
            {
                if (pts[i] < minX) { minX = pts[i]; }
                if (pts[i] > maxX) { maxX = pts[i]; }
                if (pts[i + 1] < minY) { minY = pts[i + 1]; }
                if (pts[i + 1] > maxY) { maxY = pts[i + 1]; }
            }
        }

        scene.Bounds = scene.SourceSegmentCount == 0
            ? SKRect.Create(0, 0, 1, 1)
            : new SKRect(minX, minY, maxX, maxY);

        for (var t = 0; t < TierCount; t++)
        {
            var tier = new Tier(GridSize[t], scene.Bounds);
            var tolerance = BaseToleranceMm * (1 << t);

            foreach (var pl in polylines)
            {
                var pts = t == 0 ? pl.Points : Simplify(pl.Points, tolerance);
                tier.Add(pl.Style, pts);
            }

            scene._tiers[t] = tier;
        }

        return scene;
    }

    /// <summary>
    /// Picks the coarsest tier whose simplification error stays under half a screen pixel at the
    /// given scale, so level-of-detail is never visible.
    /// </summary>
    public static int TierForScale(float pixelsPerMm)
    {
        for (var tier = TierCount - 1; tier >= 0; tier--)
        {
            if (BaseToleranceMm * (1 << tier) * pixelsPerMm <= 0.5f)
            {
                return tier;
            }
        }

        return 0;
    }

    /// <summary>
    /// Enumerates the prebuilt paths of one style that intersect <paramref name="visibleWorld"/>.
    /// </summary>
    public IEnumerable<SKPath> VisiblePaths(int tier, SegmentStyle style, SKRect visibleWorld) =>
        _tiers[tier].Visible(style, visibleWorld);

    public int SegmentsInView(int tier, SKRect visibleWorld) =>
        _tiers[tier].SegmentsInView(visibleWorld);

    public int SegmentsAtTier(int tier) => _tiers[tier].TotalSegments;

    /// <summary>
    /// Douglas-Peucker, iterative so a long polyline cannot blow the stack. This is the same
    /// simplification the export path uses (Documentation/03-Toolpath-Optimization.md section 7).
    /// </summary>
    private static float[] Simplify(float[] pts, float tolerance)
    {
        var n = pts.Length / 2;
        if (n < 3)
        {
            return pts;
        }

        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;

        var stack = new Stack<(int Lo, int Hi)>();
        stack.Push((0, n - 1));

        while (stack.Count > 0)
        {
            var (lo, hi) = stack.Pop();
            if (hi <= lo + 1)
            {
                continue;
            }

            float ax = pts[lo * 2], ay = pts[(lo * 2) + 1];
            float bx = pts[hi * 2], by = pts[(hi * 2) + 1];
            float dx = bx - ax, dy = by - ay;
            var lenSq = (dx * dx) + (dy * dy);
            var len = MathF.Sqrt(lenSq);

            var worst = -1f;
            var worstIdx = -1;

            for (var i = lo + 1; i < hi; i++)
            {
                float px = pts[i * 2], py = pts[(i * 2) + 1];
                float d;
                if (lenSq <= float.Epsilon)
                {
                    var ex = px - ax;
                    var ey = py - ay;
                    d = MathF.Sqrt((ex * ex) + (ey * ey));
                }
                else
                {
                    d = MathF.Abs(((px - ax) * dy) - ((py - ay) * dx)) / len;
                }

                if (d > worst)
                {
                    worst = d;
                    worstIdx = i;
                }
            }

            if (worst > tolerance && worstIdx > 0)
            {
                keep[worstIdx] = true;
                stack.Push((lo, worstIdx));
                stack.Push((worstIdx, hi));
            }
        }

        var kept = 0;
        for (var i = 0; i < n; i++)
        {
            if (keep[i]) { kept++; }
        }

        var result = new float[kept * 2];
        var w = 0;
        for (var i = 0; i < n; i++)
        {
            if (!keep[i])
            {
                continue;
            }

            result[w++] = pts[i * 2];
            result[w++] = pts[(i * 2) + 1];
        }

        return result;
    }

    public void Dispose()
    {
        foreach (var t in _tiers)
        {
            t?.Dispose();
        }

        Array.Clear(_tiers);
    }

    /// <summary>One level of detail, bucketed into a uniform spatial grid.</summary>
    private sealed class Tier : IDisposable
    {
        private readonly int _n;
        private readonly float _originX;
        private readonly float _originY;
        private readonly float _cellW;
        private readonly float _cellH;

        private readonly SKPath?[] _paths;      // [tile * StyleCount + style]
        private readonly SKRect[] _tileBounds;  // accumulated, so culling is exact
        private readonly int[] _tileSegments;
        private readonly bool[] _tileUsed;

        public int TotalSegments { get; private set; }

        public Tier(int n, SKRect worldBounds)
        {
            _n = n;
            _originX = worldBounds.Left;
            _originY = worldBounds.Top;
            _cellW = MathF.Max(worldBounds.Width, 0.001f) / n;
            _cellH = MathF.Max(worldBounds.Height, 0.001f) / n;

            _paths = new SKPath?[n * n * StyleCount];
            _tileBounds = new SKRect[n * n];
            _tileSegments = new int[n * n];
            _tileUsed = new bool[n * n];
        }

        public void Add(SegmentStyle style, float[] pts)
        {
            if (pts.Length < 4)
            {
                return;
            }

            var styleIdx = (int)style;
            var currentTile = -1;
            SKPath? path = null;

            for (var i = 0; i + 3 < pts.Length; i += 2)
            {
                float x0 = pts[i], y0 = pts[i + 1];
                float x1 = pts[i + 2], y1 = pts[i + 3];

                // Bucket by the segment midpoint. A segment may poke outside its tile, which is
                // why tiles track their real accumulated bounds instead of their grid cell.
                var tile = TileOf((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);

                if (tile != currentTile)
                {
                    currentTile = tile;
                    var slot = (tile * StyleCount) + styleIdx;
                    path = _paths[slot] ??= new SKPath();
                    path.MoveTo(x0, y0);
                    Grow(tile, x0, y0);
                }

                path!.LineTo(x1, y1);
                Grow(tile, x1, y1);
                _tileSegments[tile]++;
                TotalSegments++;
            }
        }

        public IEnumerable<SKPath> Visible(SegmentStyle style, SKRect visibleWorld)
        {
            var styleIdx = (int)style;
            for (var tile = 0; tile < _tileUsed.Length; tile++)
            {
                if (!_tileUsed[tile] || !_tileBounds[tile].IntersectsWith(visibleWorld))
                {
                    continue;
                }

                var path = _paths[(tile * StyleCount) + styleIdx];
                if (path is not null)
                {
                    yield return path;
                }
            }
        }

        public int SegmentsInView(SKRect visibleWorld)
        {
            var total = 0;
            for (var tile = 0; tile < _tileUsed.Length; tile++)
            {
                if (_tileUsed[tile] && _tileBounds[tile].IntersectsWith(visibleWorld))
                {
                    total += _tileSegments[tile];
                }
            }

            return total;
        }

        private int TileOf(float x, float y)
        {
            var cx = Math.Clamp((int)((x - _originX) / _cellW), 0, _n - 1);
            var cy = Math.Clamp((int)((y - _originY) / _cellH), 0, _n - 1);
            return (cy * _n) + cx;
        }

        private void Grow(int tile, float x, float y)
        {
            ref var r = ref _tileBounds[tile];

            // _tileUsed doubles as the "bounds initialised" flag. Testing SKRect.IsEmpty here
            // instead was a bug: a one-point rect reads as empty, so the accumulator reset and
            // tiles ended up with bounds stretching back to the origin.
            if (!_tileUsed[tile])
            {
                _tileUsed[tile] = true;
                r = new SKRect(x, y, x, y);
                return;
            }

            if (x < r.Left) { r.Left = x; }
            if (x > r.Right) { r.Right = x; }
            if (y < r.Top) { r.Top = y; }
            if (y > r.Bottom) { r.Bottom = y; }
        }

        public void Dispose()
        {
            foreach (var p in _paths)
            {
                p?.Dispose();
            }

            Array.Clear(_paths);
        }
    }
}
