namespace MillBurn.Core;

/// <summary>What an operation is for. Decides colour, order, and how the emitter treats it.</summary>
public enum ToolpathKind
{
    Isolation,
    Pocket,
    Drill,
    Outline,
    Tab,
    Fiducial,
    Mark,
}

/// <summary>
/// One contour cut at one depth.
///
/// The path is <see cref="ArtSegment"/>, the same type the drawings use, so an arc can travel from
/// the Gerber all the way to a <c>G2</c>/<c>G3</c> without being flattened and re-fitted. Nothing
/// produces arcs yet — Clipper offsets come back as polylines — but the pipe is the right shape,
/// which is what stops the emitter being rewritten when arc fitting arrives in Phase 3.
/// </summary>
public sealed record ToolpathPass
{
    public required IReadOnlyList<ArtSegment> Path { get; init; }

    /// <summary>Depth below the work surface. Positive; the emitter negates it.</summary>
    public required long DepthNm { get; init; }

    public bool Closed { get; init; }

    /// <summary>
    /// A precedence tier. Every pass in a lower group is cut before any pass in a higher one.
    ///
    /// Physics, not preference (Documentation/03, section 5): the pieces inside a panel are cut out
    /// before the frame around them, because cutting the frame first leaves everything still
    /// attached to it loose while the cutter is still working.
    /// </summary>
    public int Group { get; init; }

    /// <summary>
    /// Passes that must run consecutively, in the order given. Negative means independent.
    ///
    /// This is how "deeper after shallower **on the same contour**" is expressed. It is a chain per
    /// contour rather than a global ordering, and the difference is worth five times the rapid on a
    /// panel: forcing every contour to finish one depth before any starts the next means traversing
    /// the whole panel once per depth step, when the tool is already standing over the contour it
    /// is about to cut deeper.
    /// </summary>
    public int Stack { get; init; } = -1;

    /// <summary>
    /// Reach this pass from the end of the previous one without lifting: no retract, no rapid, no
    /// plunge, just a cutting move across to where it starts.
    ///
    /// Only ever set by <c>PassLinker</c>, which proves that the link stays inside material the
    /// previous pass already cleared or this one is about to. It is on the pass rather than decided
    /// in the emitter because the emitter has no geometry — it writes what it is given, and what
    /// gets written here is the difference between a 2.7-second lift and a 0.13 mm move.
    /// </summary>
    public bool LinkedFromPrevious { get; init; }

    public Point2 Start => Path.Count == 0 ? Point2.Origin : Path[0].From;

    public Point2 End => Path.Count == 0 ? Point2.Origin : Path[^1].To;

    /// <summary>Cut length in nanometres, arcs measured along the arc.</summary>
    public double LengthNm
    {
        get
        {
            var total = 0.0;
            foreach (var s in Path)
            {
                total += s.IsArc ? s.RadiusNm * s.SweptAngle() : s.From.DistanceTo(s.To);
            }

            return total;
        }
    }
}

/// <summary>
/// One hole: where, how deep, and how much to take per peck.
///
/// The peck depth travels with the hole rather than living in the emitter's settings, because it is
/// a property of the bit and the material — the operation knows both, and the emitter should not
/// have to guess.
/// </summary>
public readonly record struct DrillTarget(Point2 At, long DepthNm, long PeckNm = 0);

/// <summary>
/// One operation's worth of cutting: a tool, and everything it does before the next tool change.
/// </summary>
public sealed record Toolpath
{
    public required ToolpathKind Kind { get; init; }

    public required string Label { get; init; }

    public required Tool Tool { get; init; }

    public IReadOnlyList<ToolpathPass> Passes { get; init; } = [];

    /// <summary>Holes, for a drilling operation. Kept apart from passes because a hole is a
    /// plunge rather than a contour, and the emitter turns it into a canned cycle.</summary>
    public IReadOnlyList<DrillTarget> Drills { get; init; } = [];

    /// <summary>Anything the operator should know before running this. Carried into the G-code.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public int PassCount => Passes.Count;

    public double CutLengthMm
    {
        get
        {
            var total = 0.0;
            foreach (var pass in Passes)
            {
                total += pass.LengthNm;
            }

            return total / Nm.PerMillimetre;
        }
    }

    public Bounds Bounds
    {
        get
        {
            var bounds = Core.Bounds.Empty;
            foreach (var pass in Passes)
            {
                bounds = bounds.Union(ArtGeometry.Measure(pass.Path));
            }

            foreach (var drill in Drills)
            {
                bounds = bounds.Include(drill.At);
            }

            return bounds;
        }
    }
}

/// <summary>Everything to be cut for one board, in the order it will run.</summary>
public sealed record Job
{
    public required string Name { get; init; }

    public required IReadOnlyList<Toolpath> Toolpaths { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>
    /// How far the job was moved from the source coordinates, so a viewer can put it back.
    ///
    /// The job is referenced to the board's own corner — Gerber coordinates come from wherever the
    /// board sat on the EDA canvas, and work zero has to be somewhere the operator can touch off
    /// on. But the board is still drawn in source coordinates, so overlaying the program on it
    /// needs this undone. Leaving the caller to remember that is how a backplot ends up drawn 150
    /// mm off screen, looking for all the world like it was never generated.
    /// </summary>
    public Point2 OriginShift { get; init; }

    public double CutLengthMm => Toolpaths.Sum(t => t.CutLengthMm);

    public int DrillCount => Toolpaths.Sum(t => t.Drills.Count);
}
