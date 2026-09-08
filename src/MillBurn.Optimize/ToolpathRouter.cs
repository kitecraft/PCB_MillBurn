using MillBurn.Core;

namespace MillBurn.Optimize;

/// <summary>
/// Turns a toolpath into an ordering problem and the answer back into a toolpath.
///
/// Kept apart from <see cref="RouteOptimizer"/> so the solver never sees a segment or an arc: the
/// combinatorics can then be tested on coordinates, and the geometry — rotating a closed contour to
/// start somewhere else, reversing an arc's sweep — is tested on geometry.
/// </summary>
public static class ToolpathRouter
{
    /// <summary>
    /// Orders one toolpath's passes and drills, starting from <paramref name="from"/>.
    ///
    /// Drills come before passes in the same toolpath, but that ordering is the caller's business:
    /// within a single operation everything is one group, because a drilling operation contains
    /// only holes and an isolation operation only contours.
    /// </summary>
    public static (Toolpath Ordered, RoutePlan Plan) Order(
        Toolpath toolpath,
        Point2 from,
        MachineProfile? machine = null,
        RouteEffort effort = RouteEffort.Balanced,
        Point2? returnTo = null)
    {
        ArgumentNullException.ThrowIfNull(toolpath);

        var nodes = new List<RouteNode>(toolpath.Passes.Count + toolpath.Drills.Count);

        // References are indices into a single combined list, so one solve covers both and the
        // answer says which is which by where the index falls.
        for (var i = 0; i < toolpath.Passes.Count; i++)
        {
            nodes.Add(NodeFor(i, toolpath.Passes[i]));
        }

        for (var i = 0; i < toolpath.Drills.Count; i++)
        {
            nodes.Add(RouteNode.ForPoint(toolpath.Passes.Count + i, toolpath.Drills[i].At));
        }

        var plan = RouteOptimizer.Solve(nodes, from, machine, effort, returnTo);

        var passes = new List<ToolpathPass>(toolpath.Passes.Count);
        var drills = new List<DrillTarget>(toolpath.Drills.Count);

        foreach (var step in plan.Steps)
        {
            if (step.Reference < toolpath.Passes.Count)
            {
                passes.Add(Materialise(toolpath.Passes[step.Reference], step));
            }
            else
            {
                drills.Add(toolpath.Drills[step.Reference - toolpath.Passes.Count]);
            }
        }

        return (toolpath with { Passes = passes, Drills = drills }, plan);
    }

    private static RouteNode NodeFor(int reference, ToolpathPass pass)
    {
        if (pass.Path.Count == 0)
        {
            return RouteNode.ForPoint(reference, Point2.Origin);
        }

        return pass.Closed
            ? RouteNode.ForClosed(reference, [.. pass.Path.Select(s => s.From)], pass.Group)
            : RouteNode.ForOpen(reference, pass.Start, pass.End, pass.Group);
    }

    /// <summary>Applies the chosen configuration to the geometry.</summary>
    private static ToolpathPass Materialise(ToolpathPass pass, RouteStep step)
    {
        if (pass.Path.Count == 0)
        {
            return pass;
        }

        if (!pass.Closed)
        {
            return step.Option == 0 ? pass : pass with { Path = Reverse(pass.Path) };
        }

        // The solver worked from a sample of the contour's vertices; the exact nearest one is
        // recovered here in a single pass, so the sample costs nothing in the final answer.
        var start = NearestVertex(pass.Path, step.Entry);
        return start == 0 ? pass : pass with { Path = RotateTo(pass.Path, start) };
    }

    private static int NearestVertex(IReadOnlyList<ArtSegment> path, Point2 to)
    {
        var best = 0;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < path.Count; i++)
        {
            var distance = path[i].From.DistanceTo(to);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Re-cuts a closed contour so it begins at a different vertex.
    ///
    /// The segments are a cycle, so this is a rotation and nothing else: no geometry is added,
    /// removed or moved, and the tool covers exactly the same material in the same direction.
    /// </summary>
    private static IReadOnlyList<ArtSegment> RotateTo(IReadOnlyList<ArtSegment> path, int start) =>
        [.. path.Skip(start), .. path.Take(start)];

    /// <summary>
    /// Reverses an open run.
    ///
    /// An arc has to have its sweep flipped as well as its endpoints swapped. Reversing the points
    /// and leaving the direction alone turns a short arc into the long way round the same circle,
    /// which is a real cut straight through whatever the arc was going around.
    /// </summary>
    public static IReadOnlyList<ArtSegment> Reverse(IReadOnlyList<ArtSegment> path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var reversed = new List<ArtSegment>(path.Count);

        for (var i = path.Count - 1; i >= 0; i--)
        {
            var s = path[i];
            reversed.Add(new ArtSegment(
                s.Sweep switch
                {
                    ArtSweep.Clockwise => ArtSweep.CounterClockwise,
                    ArtSweep.CounterClockwise => ArtSweep.Clockwise,
                    _ => ArtSweep.Linear,
                },
                s.To,
                s.From,
                s.Centre));
        }

        return reversed;
    }
}
