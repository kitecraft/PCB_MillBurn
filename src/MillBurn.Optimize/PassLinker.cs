using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;

namespace MillBurn.Optimize;

/// <summary>How many lifts were removed, and how far the tool travels instead.</summary>
public readonly record struct LinkResult(int Linked, int Considered, double LinkedLengthMm)
{
    public static LinkResult Nothing => default;

    public static LinkResult operator +(LinkResult a, LinkResult b) => new(
        a.Linked + b.Linked,
        a.Considered + b.Considered,
        a.LinkedLengthMm + b.LinkedLengthMm);
}

/// <summary>
/// Keeps the tool down between passes that touch.
///
/// Isolating a pad with three laps used to be three lifts: cut the ring, retract to safe Z, rapid a
/// tenth of a millimetre, drop back, plunge, cut the next ring. On a machine whose Z traverse is
/// 100 mm/min — an ordinary GRBL router — one of those cycles is a 2.04 mm retract, a 1.50 mm rapid
/// back down and a 0.54 mm feed to depth: <b>about 2.7 seconds</b>, spent moving the tool away from
/// the exact place it is about to work.
///
/// The gaps in a real program separate cleanly. Sorting the twenty-five links in a levelled
/// isolation file by distance gives thirteen at 0.126 mm — exactly the isolation stepover — then
/// 0.150, 0.154, two at 0.161, and then nothing until 0.629 and on up to 10 mm. The short ones are
/// lap-to-lap on the same island; the long ones are journeys to somewhere else.
///
/// **The rule is about material, not distance.** A 0.126 mm hop between concentric laps is safe
/// because the lap that just finished cleared that band; the same hop between two unrelated runs
/// that happen to end up near each other is a gouge through copper that was meant to stay. So this
/// asks the only question that matters: would the tool, dragged from here to there at depth, cut
/// anything that is not already gone or about to be?
///
/// It is answered with the same Clipper offsets that produced the paths. The region a pass clears
/// is its centreline swept by the tool — <see cref="EndType.Joined"/> for a closed contour, which
/// gives the ribbon rather than filling the island inside it. A link is allowed when its own swept
/// ribbon lies inside the previous pass's ribbon plus the next one's. The next one counts because it
/// is cut immediately afterwards: material the link takes out of it was leaving anyway.
///
/// Deliberately local — the previous pass and the next, not the whole accumulated history of the
/// board. That is the conservative direction (a link over ground cleared five passes ago is
/// refused), it keeps the cost per link to two small offsets and a difference, and it covers every
/// one of the thirteen above.
/// </summary>
public static class PassLinker
{
    /// <summary>
    /// Beyond this multiple of the cut width, do not even ask.
    ///
    /// Purely to keep the geometry off the long journeys: a link has to stay inside a ribbon one
    /// cut wide, so anything much longer than a few widths cannot pass the real test anyway, and
    /// running it is work for a foregone answer.
    /// </summary>
    private const int ReachInWidths = 10;

    /// <summary>
    /// Slack for the fact that a round join is a polygon.
    ///
    /// Clipper approximates the cutter's round end with line segments, so the computed ribbon can
    /// fall up to one sagitta inside the true one and leave a sliver of the link apparently
    /// uncovered. One micron of allowance removes that without meaning anything physically: the
    /// machine this targets resolves 1.25 µm per step.
    /// </summary>
    private const long SlackNm = Tessellate.DefaultSagittaNm;

    /// <summary>
    /// Marks every pass that can be reached from the one before it without lifting.
    ///
    /// Runs after ordering, because which pass follows which is exactly what decides this, and
    /// before the emitter, which has no geometry to decide it with.
    /// </summary>
    public static (Toolpath Path, LinkResult Result) Apply(
        Toolpath toolpath, long sagittaNm = Tessellate.DefaultSagittaNm)
    {
        ArgumentNullException.ThrowIfNull(toolpath);

        // An outline cut goes through the stock and has nothing cleared beside it; a drill is a
        // plunge, not a contour. Only the operations that clear an area can link across one.
        if (toolpath.Kind is not (ToolpathKind.Isolation or ToolpathKind.Pocket)
            || toolpath.Passes.Count < 2)
        {
            return (toolpath, LinkResult.Nothing);
        }

        var passes = new List<ToolpathPass>(toolpath.Passes.Count)
        {
            toolpath.Passes[0] with { LinkedFromPrevious = false },
        };

        var linked = 0;
        var considered = 0;
        var length = 0.0;

        for (var i = 1; i < toolpath.Passes.Count; i++)
        {
            var previous = toolpath.Passes[i - 1];
            var next = toolpath.Passes[i];
            var gap = previous.End.DistanceTo(next.Start);
            var width = toolpath.Tool.WidthAtDepth(next.DepthNm);

            // A different depth is a plunge by definition, and a pass with nothing in it has no end
            // to leave from.
            var possible = previous.DepthNm == next.DepthNm
                && width > 0
                && previous.Path.Count > 0
                && next.Path.Count > 0
                && gap > 0
                && gap <= (double)width * ReachInWidths;

            if (possible)
            {
                considered++;
            }

            if (possible && Clears(previous, next, width, sagittaNm))
            {
                passes.Add(next with { LinkedFromPrevious = true });
                linked++;
                length += gap / Nm.PerMillimetre;
            }
            else
            {
                passes.Add(next with { LinkedFromPrevious = false });
            }
        }

        return (toolpath with { Passes = passes }, new LinkResult(linked, considered, length));
    }

    /// <summary>
    /// Whether dragging the tool from one pass's end to the next pass's start cuts only material
    /// that is already gone or is going in a moment.
    /// </summary>
    private static bool Clears(ToolpathPass previous, ToolpathPass next, long width, long sagittaNm)
    {
        var half = width / 2;

        var allowed = Clipper.Union(
            Ribbon(previous, half, sagittaNm),
            Ribbon(next, half, sagittaNm),
            FillRule.NonZero);

        // The slack goes on the region rather than off the link: shrinking the link would let a
        // genuinely short overhang through, and growing the region by a micron cannot.
        allowed = Clipper.InflatePaths(
            allowed, SlackNm, JoinType.Round, EndType.Polygon, arcTolerance: sagittaNm);

        var link = new Paths64
        {
            new Path64
            {
                new Point64(previous.End.X, previous.End.Y),
                new Point64(next.Start.X, next.Start.Y),
            },
        };

        var swept = Clipper.InflatePaths(
            link, half, JoinType.Round, EndType.Round, arcTolerance: sagittaNm);

        var outside = Clipper.Difference(swept, allowed, FillRule.NonZero);

        // Clipper leaves degenerate slivers where two boundaries touch. A square a micron on a side
        // is not a gouge in anybody's copper.
        return Math.Abs(Clipper.Area(outside)) < (double)SlackNm * SlackNm;
    }

    /// <summary>
    /// The area a pass clears: its centreline swept by the tool.
    ///
    /// <see cref="EndType.Joined"/> for a closed contour is the point of this method.
    /// <see cref="EndType.Polygon"/> would inflate the contour into a filled region, claiming the
    /// island *inside* an isolation ring as cleared ground — which is the copper the ring exists to
    /// protect, and the one mistake here that would cut a trace in half.
    /// </summary>
    private static Paths64 Ribbon(ToolpathPass pass, long half, long sagittaNm) => Clipper.InflatePaths(
        new Paths64 { Tessellate.Flatten(pass.Path, sagittaNm) },
        half,
        JoinType.Round,
        pass.Closed ? EndType.Joined : EndType.Round,
        arcTolerance: sagittaNm);
}
