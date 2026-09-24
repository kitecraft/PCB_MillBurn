using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;

namespace MillBurn.Cam;

/// <summary>
/// One pass down the middle of a channel, instead of a lap around a sliver.
///
/// Cutting a void means keeping the cutter inside it, which is done by offsetting the void inward
/// by the cutter's radius and running round what is left. That is correct at any width, and it is
/// wasteful at one: when the channel is about as wide as the cutter, what is left is a ribbon a
/// fraction of a millimetre across, and running round it sends the cutter out along one side and
/// back along the other a hair away. The return pass is air. On a 66-up panel with fifty 1 mm
/// channels and a 1 mm cutter, that was most of an hour.
///
/// What the cutter actually wants is the channel's centreline — and getting there is not a matter
/// of halving the loop, because a channel lattice branches. The loop around a branching ribbon is
/// a depth-first walk of it: out and back along every arm. Collapsing the ribbon's two sides onto
/// each other recovers the branching structure as a graph, and a walk of that graph covers every
/// arm exactly once except for the backtracking a tree unavoidably needs. For a plus-shaped cell,
/// 70.8 mm becomes 44.2 mm.
///
/// It also cuts truer. The profile is the outside of the pen the outline was drawn with, so it
/// overhangs the real channel by half a pen width; the loop obediently cuts that overhang out of
/// the boards either side, while one pass down the middle takes exactly the channel.
///
/// **Nothing here is trusted on its own.** The centreline is worked out, then checked against what
/// the loop would have cut, and anything it would leave behind thicker than a tenth of the cutter
/// sends the whole thing back to the loop. A channel not fully cut is a panel that does not come
/// apart, discovered at the machine.
/// </summary>
public static class VoidCentreline
{
    /// <summary>
    /// The centrelines to cut instead of <paramref name="loop"/>, or null to keep the loop.
    /// </summary>
    /// <param name="loop">The void offset inward by the cutter's radius: what would be cut.</param>
    /// <param name="diameterNm">The cutter.</param>
    /// <param name="sagittaNm">Arc tolerance, so the checks tessellate like everything else.</param>
    public static Paths64? For(Paths64 loop, long diameterNm, long sagittaNm)
    {
        ArgumentNullException.ThrowIfNull(loop);

        if (loop.Count == 0 || diameterNm <= 0)
        {
            return null;
        }

        var axis = new Paths64();

        foreach (var ribbon in loop)
        {
            if (Collapse(ribbon, diameterNm) is not { } walk)
            {
                return null;
            }

            axis.Add(walk);
        }

        var radius = diameterNm / 2;

        // The check: sweep the cutter along the centreline and see whether any of the ribbon is
        // left over. The ribbon is the void offset inward by the cutter's radius, which is to say
        // exactly the set of places the cutter was meant to visit, so a gap here is a gap in the
        // cut — a channel not severed, a panel that does not come apart.
        //
        // Deliberately *not* "does it remove everything the lap would have". The lap hugs the
        // void's boundary, so it also takes the corner out of every junction and the half pen width
        // the profile overhangs the real channel by. Measured against that, a centreline is
        // rejected for doing less damage. Whether the channel is cut through is the question, and
        // this is that question.
        var byLine = Clipper.InflatePaths(
            axis, radius, JoinType.Round, EndType.Round, arcTolerance: sagittaNm);

        Work.Boolean(Polygons.VertexCount(loop) + Polygons.VertexCount(byLine));
        var missed = Clipper.Difference(loop, byLine, FillRule.NonZero);

        // A hundredth of a square millimetre of slack, for the slivers an offset leaves along an
        // edge it has just reproduced. A real gap is thousands of times that.
        return Math.Abs(Clipper.Area(missed)) > 0.01 * Nm.PerMillimetre * Nm.PerMillimetre
            ? null
            : axis;
    }

    /// <summary>
    /// A thin closed ribbon, folded onto its own middle and walked.
    ///
    /// The fold pairs each point on the boundary with the point opposite it, and takes the midpoint
    /// of the two. Finding the opposite needs no cleverness once the ribbon is resampled at three
    /// times its own width: across the ribbon is then the shortest hop there is, three times shorter
    /// than the hop to a neighbour along it, so the nearest point that is not an immediate
    /// neighbour *is* the one across. At a tip the pair straddles the cap, which is also right.
    ///
    /// Pairing this way rather than merging whatever happens to be close is what makes the result
    /// exact. Two points that pair with each other produce the *same* midpoint, to the nanometre, so
    /// the two sides land on one another instead of somewhere near. Being off-centre is precisely
    /// what leaves material uncut.
    /// </summary>
    private static Path64? Collapse(Path64 ribbon, long diameterNm)
    {
        if (ribbon.Count < 3)
        {
            return null;
        }

        var area = Math.Abs(Clipper.Area(ribbon));
        var perimeter = Perimeter(ribbon);

        if (perimeter <= 0 || area <= 0)
        {
            return null;
        }

        // Mean width of a long thin shape: twice its area over its perimeter.
        var width = 2 * area / perimeter;

        // Only a ribbon narrow enough that its two sides are unmistakably one cut. Anything fatter
        // is a channel the cutter does not span, and both sides of it have to be cut.
        if (width > diameterNm / 5.0)
        {
            return null;
        }

        var spacing = Math.Max(width * 3, diameterNm / 20.0);
        var points = Resample(ribbon, spacing);

        if (points.Count < 4)
        {
            return null;
        }

        var nodes = Fold(Middle(points), spacing / 2);

        return Walk(points.Count, nodes, ribbon);
    }

    /// <summary>
    /// Each boundary point moved to the middle of the ribbon, by pairing it with the point opposite.
    ///
    /// The opposite is the nearest point that is not an immediate neighbour along the boundary. That
    /// works because of the resampling: a step along the ribbon is three times the width of the
    /// ribbon, so nothing is nearer than the far side.
    /// </summary>
    private static List<Point64> Middle(List<Point64> points)
    {
        var middle = new List<Point64>(points.Count);

        for (var i = 0; i < points.Count; i++)
        {
            var best = -1;
            var nearest = double.PositiveInfinity;

            for (var j = 0; j < points.Count; j++)
            {
                var gap = Math.Abs(i - j);
                gap = Math.Min(gap, points.Count - gap);

                if (gap < 2)
                {
                    continue;
                }

                var apart = Distance(points[i], points[j]);

                if (apart < nearest)
                {
                    nearest = apart;
                    best = j;
                }
            }

            middle.Add(best < 0
                ? points[i]
                : new Point64(
                    (points[i].X + points[best].X) / 2,
                    (points[i].Y + points[best].Y) / 2));
        }

        return middle;
    }

    /// <summary>Merges points closer together than <paramref name="tolerance"/>, and averages them.</summary>
    private static (int[] Of, List<Point64> At) Fold(List<Point64> points, double tolerance)
    {
        var parent = new int[points.Count];
        for (var i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        int Root(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        // A grid of one tolerance, so only nine cells ever have to be looked at.
        var cell = Math.Max(1, (long)tolerance);
        var grid = new Dictionary<(long, long), List<int>>();

        for (var i = 0; i < points.Count; i++)
        {
            var key = (points[i].X / cell, points[i].Y / cell);

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (!grid.TryGetValue((key.Item1 + dx, key.Item2 + dy), out var bucket))
                    {
                        continue;
                    }

                    foreach (var j in bucket)
                    {
                        if (Distance(points[i], points[j]) <= tolerance)
                        {
                            parent[Root(i)] = Root(j);
                        }
                    }
                }
            }

            if (!grid.TryGetValue(key, out var own))
            {
                own = [];
                grid[key] = own;
            }

            own.Add(i);
        }

        var index = new Dictionary<int, int>();
        var sums = new List<(double X, double Y, int Count)>();
        var of = new int[points.Count];

        for (var i = 0; i < points.Count; i++)
        {
            var root = Root(i);

            if (!index.TryGetValue(root, out var n))
            {
                n = sums.Count;
                index[root] = n;
                sums.Add((0, 0, 0));
            }

            of[i] = n;
            sums[n] = (sums[n].X + points[i].X, sums[n].Y + points[i].Y, sums[n].Count + 1);
        }

        var at = sums
            .Select(s => new Point64((long)Math.Round(s.X / s.Count), (long)Math.Round(s.Y / s.Count)))
            .ToList();

        return (of, at);
    }

    /// <summary>
    /// A path through the folded graph that covers every edge.
    ///
    /// Only trees, which is what a channel lattice is. A folded ribbon that still holds a cycle —
    /// a void shaped like a washer — is handed back as null and cut as a loop, which for a ring is
    /// the right answer anyway.
    ///
    /// The walk starts at one end of the graph's longest path and leaves the branch towards the
    /// other end until last, so the one arm that never has to be retraced is the longest one there
    /// is. That is the whole of the saving over the loop.
    /// </summary>
    private static Path64? Walk(int count, (int[] Of, List<Point64> At) nodes, Path64 ribbon)
    {
        var (of, at) = nodes;
        var near = new Dictionary<int, List<int>>();
        var component = new int[at.Count];

        for (var i = 0; i < at.Count; i++)
        {
            component[i] = i;
            near[i] = [];
        }

        int Group(int i)
        {
            while (component[i] != i)
            {
                component[i] = component[component[i]];
                i = component[i];
            }

            return i;
        }

        var links = 0;

        // A spanning tree, not every edge the fold produced.
        //
        // Folding a ribbon leaves the odd spurious link — a two-node loop at a tip, a shortcut
        // across a junction — and one is enough to stop this being a tree. Dropping any edge that
        // closes a loop cannot disconnect anything, and if a dropped edge was real structure rather
        // than an artefact, the coverage check at the end sees the gap and the whole thing falls
        // back to the loop. Guessing is allowed here only because it is checked.
        for (var i = 0; i < count; i++)
        {
            var a = of[i];
            var b = of[(i + 1) % count];

            if (a == b || Group(a) == Group(b))
            {
                continue;
            }

            component[Group(a)] = Group(b);
            near[a].Add(b);
            near[b].Add(a);
            links++;
        }

        if (at.Count < 2 || links != at.Count - 1)
        {
            return null;
        }

        Meet(at, near);
        Reach(at, near, ribbon);

        // Both ends of the longest path through the tree, and every node's distance from the far
        // one. Children are then visited nearest-to-the-far-end *last*, so the walk finishes there
        // and the longest arm is the one that never has to be retraced. That saving is the whole
        // point; the first version of this ordered by distance from the *start* instead, which is
        // very nearly the same number for every child of a node and therefore no ordering at all.
        var start = Farthest(at, near, Farthest(at, near, 0).Node).Node;
        var end = Farthest(at, near, start).Node;
        var (_, fromEnd) = Farthest(at, near, end);

        var walk = new Path64 { at[start] };
        var seen = new bool[at.Count];
        var stack = new Stack<(int Node, int From, IEnumerator<int> Left)>();

        seen[start] = true;
        stack.Push((start, -1, Children(near, start, -1, fromEnd).GetEnumerator()));

        while (stack.Count > 0)
        {
            var (node, from, left) = stack.Peek();

            if (left.MoveNext())
            {
                var child = left.Current;

                if (seen[child])
                {
                    return null;
                }

                seen[child] = true;
                walk.Add(at[child]);
                stack.Push((child, node, Children(near, child, node, fromEnd).GetEnumerator()));
                continue;
            }

            stack.Pop();

            // Back the way we came, because an arm of a tree has no other way out.
            if (from >= 0)
            {
                walk.Add(at[from]);
            }
        }

        // The walk ends where it started; everything after the last visit to the far end is the
        // retrace of the longest arm, and that is the one pass nothing forces us to make twice.
        var last = walk.LastIndexOf(at[end]);

        if (last > 0)
        {
            walk.RemoveRange(last + 1, walk.Count - last - 1);
        }

        return walk.Count >= 2 ? walk : null;
    }

    /// <summary>
    /// Brings a junction that came out as two nodes together onto one point.
    ///
    /// Where arms cross, the void is wider than anywhere else — a disc of the cutter's radius fits
    /// diagonally as well as along — so the ribbon swells into a small diamond there, and folding a
    /// diamond gives two forks a step apart rather than one crossing. The cutter then rounds the
    /// corner between two arms instead of passing through the middle, and leaves a wedge of the
    /// diamond standing: 0.29 mm² per junction, right where four boards meet.
    ///
    /// The two are moved onto their midpoint rather than merged away, which costs a zero-length
    /// step in the walk and no bookkeeping at all.
    /// </summary>
    private static void Meet(List<Point64> at, Dictionary<int, List<int>> near)
    {
        foreach (var (node, links) in near)
        {
            if (links.Count < 3)
            {
                continue;
            }

            foreach (var other in links)
            {
                if (near[other].Count < 3)
                {
                    continue;
                }

                // Where the arms actually cross, which is the average of where they leave.
                //
                // Not the midpoint of the two forks: both of them sit off to one side of the
                // diamond, so their midpoint is off to that side as well and the wedge stays. The
                // first node out along each arm is on that arm's own centreline, one resampling
                // step from the crossing, so the four of them average to the crossing itself.
                var arms = links
                    .Concat(near[other])
                    .Where(n => n != node && n != other)
                    .Distinct()
                    .ToList();

                if (arms.Count < 3)
                {
                    continue;
                }

                var middle = new Point64(
                    (long)Math.Round(arms.Average(n => (double)at[n].X)),
                    (long)Math.Round(arms.Average(n => (double)at[n].Y)));

                at[node] = middle;
                at[other] = middle;
            }
        }
    }

    /// <summary>
    /// Pushes every loose end of the axis out to the end of the ribbon.
    ///
    /// Folding stops short at a tip: a cap is narrower than the step the boundary was resampled at,
    /// so the last pair of points that face each other sit back from it. Nearly half a millimetre
    /// of arm then went uncut, four times per panel cell — the kind of thing that is invisible in a
    /// preview and obvious on the stock.
    /// </summary>
    private static void Reach(List<Point64> at, Dictionary<int, List<int>> near, Path64 ribbon)
    {
        foreach (var (node, links) in near)
        {
            if (links.Count != 1)
            {
                continue;
            }

            var from = at[links[0]];
            var tip = at[node];
            var run = Distance(from, tip);

            if (run <= 0)
            {
                continue;
            }

            var dx = (tip.X - from.X) / run;
            var dy = (tip.Y - from.Y) / run;
            var step = Math.Max(1, run / 16);

            for (var reach = step; reach <= run * 4; reach += step)
            {
                var probe = new Point64(
                    (long)Math.Round(tip.X + (dx * reach)),
                    (long)Math.Round(tip.Y + (dy * reach)));

                if (Polygons.PointIn(probe, ribbon) != PointInPolygonResult.IsInside)
                {
                    break;
                }

                at[node] = probe;
            }
        }
    }

    private static IEnumerable<int> Children(
        Dictionary<int, List<int>> near, int node, int from, double[] fromEnd) =>
        near[node]
            .Where(n => n != from)
            .OrderByDescending(n => fromEnd[n]);

    /// <summary>The node furthest from <paramref name="source"/> by path length, and every distance.</summary>
    private static (int Node, double[] To) Farthest(
        List<Point64> at, Dictionary<int, List<int>> near, int source)
    {
        var to = new double[at.Count];
        Array.Fill(to, double.PositiveInfinity);
        to[source] = 0;

        var queue = new Queue<int>();
        queue.Enqueue(source);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();

            foreach (var next in near[node])
            {
                var through = to[node] + Distance(at[node], at[next]);

                if (through < to[next])
                {
                    to[next] = through;
                    queue.Enqueue(next);
                }
            }
        }

        var best = source;
        for (var i = 0; i < to.Length; i++)
        {
            if (!double.IsPositiveInfinity(to[i]) && to[i] > to[best])
            {
                best = i;
            }
        }

        return (best, to);
    }

    private static List<Point64> Resample(Path64 contour, double spacing)
    {
        var points = new List<Point64>();
        var carried = 0.0;

        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            var length = Distance(a, b);

            if (length <= 0)
            {
                continue;
            }

            for (var at = spacing - carried; at < length; at += spacing)
            {
                var t = at / length;
                points.Add(new Point64(
                    (long)Math.Round(a.X + ((b.X - a.X) * t)),
                    (long)Math.Round(a.Y + ((b.Y - a.Y) * t))));
            }

            carried = (carried + length) % spacing;
        }

        return points;
    }

    private static double Perimeter(Path64 contour)
    {
        var total = 0.0;

        for (var i = 0; i < contour.Count; i++)
        {
            total += Distance(contour[i], contour[(i + 1) % contour.Count]);
        }

        return total;
    }

    private static double Distance(Point64 a, Point64 b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

}
