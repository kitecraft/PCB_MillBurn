using MillBurn.Core;

namespace MillBurn.Optimize;

/// <summary>How a node may be entered and left.</summary>
public enum RouteKind
{
    /// <summary>A hole. One configuration: it is a point.</summary>
    Point,

    /// <summary>An open run. Two configurations: enter at either end.</summary>
    Open,

    /// <summary>
    /// A closed contour. Enter at any vertex, and leave from the same one.
    ///
    /// That entry and exit coincide is the fact that makes closed loops cheap to optimise: the
    /// best entry for a loop is simply its nearest vertex to wherever the tool already is, which is
    /// a closed-form answer rather than a search. pcb2gcode instead enters at whichever vertex
    /// happened to be first in the data structure, and on a board with eight cutouts that throws
    /// away most of the available saving before its solver even starts
    /// (Documentation/03, section 1).
    /// </summary>
    Closed,
}

/// <summary>
/// One thing to visit, and the ways it can be visited.
///
/// Deliberately free of geometry beyond the entry and exit points: the optimizer solves ordering,
/// and the caller turns the answer back into toolpaths. Keeping the two apart is what lets the
/// solver be tested on numbers rather than on Gerbers.
/// </summary>
public sealed class RouteNode
{
    public required RouteKind Kind { get; init; }

    /// <summary>The caller's own index for this item, handed back in the result untouched.</summary>
    public required int Reference { get; init; }

    /// <summary>Where an unreversed traversal starts. For a point, the point itself.</summary>
    public required Point2 Start { get; init; }

    /// <summary>Where an unreversed traversal finishes. For a point or a loop, equals Start.</summary>
    public required Point2 End { get; init; }

    /// <summary>
    /// Candidate entry vertices for a closed contour, in path order.
    ///
    /// A sample rather than every vertex: a 2,000-point contour does not need 2,000 configurations
    /// to be entered near-optimally, and the exact nearest vertex is recovered at the end anyway.
    /// </summary>
    public IReadOnlyList<Point2> Entries { get; init; } = [];

    /// <summary>
    /// A hard partition. Every node in group 0 is visited before any node in group 1.
    ///
    /// Precedence here is physics, not preference: the board has to still be held down when it is
    /// drilled, so the outline cannot come first (Documentation/03, section 5). Expressing it as a
    /// partition rather than as a penalty means it cannot be traded away for a shorter route.
    /// </summary>
    public int Group { get; init; }

    /// <summary>How many ways this node can be entered.</summary>
    public int OptionCount => Kind switch
    {
        RouteKind.Open => 2,
        RouteKind.Closed => Math.Max(Entries.Count, 1),
        _ => 1,
    };

    public Point2 EntryFor(int option) => Kind switch
    {
        RouteKind.Open => option == 0 ? Start : End,
        RouteKind.Closed => Entries.Count == 0 ? Start : Entries[Math.Clamp(option, 0, Entries.Count - 1)],
        _ => Start,
    };

    /// <summary>Where the tool ends up. For a loop that is the entry again; for a point, the point.</summary>
    public Point2 ExitFor(int option) => Kind switch
    {
        RouteKind.Open => option == 0 ? End : Start,
        RouteKind.Closed => EntryFor(option),
        _ => Start,
    };

    /// <summary>The configuration that reverses this one. Loops and points have none.</summary>
    public int Flip(int option) => Kind == RouteKind.Open ? 1 - option : option;

    public static RouteNode ForPoint(int reference, Point2 at, int group = 0) => new()
    {
        Kind = RouteKind.Point,
        Reference = reference,
        Start = at,
        End = at,
        Group = group,
    };

    public static RouteNode ForOpen(int reference, Point2 start, Point2 end, int group = 0) => new()
    {
        Kind = RouteKind.Open,
        Reference = reference,
        Start = start,
        End = end,
        Group = group,
    };

    /// <summary>
    /// A closed contour, sampling entry vertices when there are more than the solver needs.
    ///
    /// The sample is evenly spaced by index rather than chosen cleverly, because the exact best
    /// vertex is found afterwards in one pass over the real vertices — spending effort here would
    /// buy accuracy that the final snap provides for nothing.
    /// </summary>
    public static RouteNode ForClosed(
        int reference, IReadOnlyList<Point2> vertices, int group = 0, int maxEntries = 32)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        if (vertices.Count == 0)
        {
            return ForPoint(reference, Point2.Origin, group);
        }

        var entries = new List<Point2>(Math.Min(vertices.Count, maxEntries));

        if (vertices.Count <= maxEntries)
        {
            entries.AddRange(vertices);
        }
        else
        {
            for (var i = 0; i < maxEntries; i++)
            {
                entries.Add(vertices[(int)((long)i * vertices.Count / maxEntries)]);
            }
        }

        return new RouteNode
        {
            Kind = RouteKind.Closed,
            Reference = reference,
            Start = vertices[0],
            End = vertices[0],
            Entries = entries,
            Group = group,
        };
    }
}

/// <summary>One visit in the answer: which node, entered which way.</summary>
public readonly record struct RouteStep(int Reference, int Option, Point2 Entry, Point2 Exit);

/// <summary>
/// The ordering the optimizer settled on, and what it cost.
///
/// The before-and-after numbers are part of the result rather than something the caller has to
/// recompute, because a claim that the optimizer helps is only worth anything if the size of the
/// help is visible (Documentation/03, section 6).
/// </summary>
public sealed record RoutePlan
{
    public required IReadOnlyList<RouteStep> Steps { get; init; }

    /// <summary>Rapid distance in millimetres before optimisation.</summary>
    public required double InitialTravelMm { get; init; }

    public required double TravelMm { get; init; }

    /// <summary>Rapid time in seconds, on the machine's own motion profile.</summary>
    public required double InitialSeconds { get; init; }

    public required double Seconds { get; init; }

    /// <summary>How many improving moves the local search actually applied.</summary>
    public int Improvements { get; init; }

    public TimeSpan Elapsed { get; init; }

    public double TravelSavedFraction =>
        InitialTravelMm <= 0 ? 0 : 1.0 - (TravelMm / InitialTravelMm);

    public double TimeSavedFraction =>
        InitialSeconds <= 0 ? 0 : 1.0 - (Seconds / InitialSeconds);
}

/// <summary>How hard to try. Same budget, same seed, same answer, every time.</summary>
public enum RouteEffort
{
    /// <summary>Construction only, no local search. For a live preview.</summary>
    Fast,

    /// <summary>The default. Runs on every settled edit.</summary>
    Balanced,

    /// <summary>One click before exporting the file that will actually be cut.</summary>
    Thorough,
}
