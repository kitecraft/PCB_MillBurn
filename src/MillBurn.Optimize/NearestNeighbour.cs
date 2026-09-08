using MillBurn.Core;

namespace MillBurn.Optimize;

/// <summary>
/// Nearest-neighbour ordering: from where the tool is, go to whichever pass starts or ends closest,
/// and reverse it if that is the end.
///
/// **Deliberately the naive algorithm.** Phase 3 replaces it with a GTSP model over entry
/// configurations and a real motion-time cost, and the only way to say honestly whether that is an
/// improvement is to have a baseline that was not tuned to flatter it. This is that baseline.
///
/// It does already do the one thing pcb2gcode's greedy pass does not, which is to consider *both*
/// ends of a candidate. Its <c>tsp_solver.hpp</c> measures only the front endpoint, so a path whose
/// far end is next to the tool is scored by how far away its near end is
/// (Documentation/03, section 2). That is a bug rather than a simplification, and reproducing it to
/// make the baseline look worse would be dishonest.
/// </summary>
public static class NearestNeighbour
{
    /// <summary>Orders passes, reversing any that are better entered from the other end.</summary>
    public static IReadOnlyList<ToolpathPass> Order(IReadOnlyList<ToolpathPass> passes, Point2 from)
    {
        ArgumentNullException.ThrowIfNull(passes);

        var remaining = passes.ToList();
        var ordered = new List<ToolpathPass>(remaining.Count);
        var at = from;

        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestDistance = double.MaxValue;
            var bestReversed = false;

            for (var i = 0; i < remaining.Count; i++)
            {
                var pass = remaining[i];

                var toStart = at.DistanceTo(pass.Start);
                if (toStart < bestDistance)
                {
                    bestDistance = toStart;
                    bestIndex = i;
                    bestReversed = false;
                }

                // A closed contour can be entered anywhere, so reversing it is free; an open run
                // reverses too, it just changes which end is the entry.
                var toEnd = at.DistanceTo(pass.End);
                if (toEnd < bestDistance)
                {
                    bestDistance = toEnd;
                    bestIndex = i;
                    bestReversed = true;
                }
            }

            var chosen = remaining[bestIndex];
            remaining.RemoveAt(bestIndex);

            if (bestReversed)
            {
                chosen = chosen with { Path = Reverse(chosen.Path) };
            }

            ordered.Add(chosen);
            at = chosen.End;
        }

        return ordered;
    }

    /// <summary>Orders drill hits. The same algorithm, without the two-ended part.</summary>
    public static IReadOnlyList<DrillTarget> Order(IReadOnlyList<DrillTarget> drills, Point2 from)
    {
        ArgumentNullException.ThrowIfNull(drills);

        var remaining = drills.ToList();
        var ordered = new List<DrillTarget>(remaining.Count);
        var at = from;

        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestDistance = double.MaxValue;

            for (var i = 0; i < remaining.Count; i++)
            {
                var distance = at.DistanceTo(remaining[i].At);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            ordered.Add(remaining[bestIndex]);
            at = remaining[bestIndex].At;
            remaining.RemoveAt(bestIndex);
        }

        return ordered;
    }

    /// <summary>Total distance travelled between passes, which is what the optimizer must beat.</summary>
    public static double TravelMm(IReadOnlyList<ToolpathPass> passes, Point2 from)
    {
        ArgumentNullException.ThrowIfNull(passes);

        var total = 0.0;
        var at = from;

        foreach (var pass in passes)
        {
            total += at.DistanceTo(pass.Start);
            at = pass.End;
        }

        return total / Nm.PerMillimetre;
    }

    public static double TravelMm(IReadOnlyList<DrillTarget> drills, Point2 from)
    {
        ArgumentNullException.ThrowIfNull(drills);

        var total = 0.0;
        var at = from;

        foreach (var drill in drills)
        {
            total += at.DistanceTo(drill.At);
            at = drill.At;
        }

        return total / Nm.PerMillimetre;
    }

    private static ArtSegment[] Reverse(IReadOnlyList<ArtSegment> path)
    {
        var reversed = new ArtSegment[path.Count];
        for (var i = 0; i < path.Count; i++)
        {
            var s = path[path.Count - 1 - i];
            reversed[i] = new ArtSegment(
                s.Sweep switch
                {
                    ArtSweep.Clockwise => ArtSweep.CounterClockwise,
                    ArtSweep.CounterClockwise => ArtSweep.Clockwise,
                    _ => ArtSweep.Linear,
                },
                s.To,
                s.From,
                s.Centre);
        }

        return reversed;
    }
}
