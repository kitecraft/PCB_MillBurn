using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;

namespace MillBurn.Optimize;

/// <summary>
/// How many lifts were removed, and how far the tool travels instead.
/// </summary>
/// <param name="Linked">Passes reached across cleared ground rather than by lifting.</param>
/// <param name="Considered">Passes close enough to the one before for that to be asked.</param>
/// <param name="LinkedLengthMm">How far those links travel at depth.</param>
/// <param name="Continued">
/// Laps that carry straight on from the one before — the next depth of the same hole, slot or
/// profile — with no lift and no move in X or Y. Counted apart from <paramref name="Linked"/>:
/// nothing was crossed, and a summary adding them together would describe routing as isolation.
///
/// Not quite "no move at all", which is what this said before 6.24 widened it. A lap that ramps
/// needs none; a lap that simply starts deeper is reached by feeding straight down where the tool
/// stands, which is a plunge and is counted as one.
/// </param>
public readonly record struct LinkResult(int Linked, int Considered, double LinkedLengthMm, int Continued = 0)
{
    public static LinkResult Nothing => default;

    public static LinkResult operator +(LinkResult a, LinkResult b) => new(
        a.Linked + b.Linked,
        a.Considered + b.Considered,
        a.LinkedLengthMm + b.LinkedLengthMm,
        a.Continued + b.Continued);
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

        if (toolpath.Passes.Count < 2)
        {
            return (toolpath, LinkResult.Nothing);
        }

        // An outline cut goes through the stock and has nothing cleared beside it; a drill is a
        // plunge, not a contour. Only the operations that clear an area can link *across* one — but
        // any of them can carry straight on into its own next lap.
        var acrossArea = toolpath.Kind is ToolpathKind.Isolation or ToolpathKind.Pocket;

        var passes = new List<ToolpathPass>(toolpath.Passes.Count)
        {
            toolpath.Passes[0] with { LinkedFromPrevious = false },
        };

        var linked = 0;
        var considered = 0;
        var length = 0.0;
        var continued = 0;

        for (var i = 1; i < toolpath.Passes.Count; i++)
        {
            var previous = toolpath.Passes[i - 1];
            var next = toolpath.Passes[i];

            if (Continues(previous, next))
            {
                passes.Add(next with { LinkedFromPrevious = true });
                continued++;
                continue;
            }

            if (!acrossArea)
            {
                passes.Add(next with { LinkedFromPrevious = false });
                continue;
            }

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

        return (toolpath with { Passes = passes }, new LinkResult(linked, considered, length, continued));
    }

    /// <summary>
    /// Whether the next pass is the same feature's next lap, carrying straight on: the same stack,
    /// starting exactly where the last one stopped.
    ///
    /// Found at the machine. A 2.2 mm hole on a 0.8 mm board came out as four laps of one helix with
    /// a lift between each — retract to safe height, rapid back to the point the tool was already
    /// over, drop to the approach height, and feed at the plunge rate down through the lap just cut
    /// — because every lap is deeper than the last and starts where it ended, and the area rule
    /// above only ever asks about passes at one depth with a gap between them.
    ///
    /// Nothing is crossed, so no material is in question: the tool is already there.
    ///
    /// Two ways it gets to the new depth, and they are the only difference between them.
    ///
    /// A slot or a milled hole **ramps**: its next pass starts at the depth the last one reached
    /// and descends along its length, so there is no plunge at all.
    ///
    /// An outline's next lap **drops**: it begins at the same point, and the tool goes straight down
    /// to the new depth at the plunge feed before cutting. That is 6.24. The lap that just finished
    /// ended exactly where this one starts, so lifting to the safe height, rapiding to the place the
    /// tool is already standing, and dropping back past where it started is three moves to achieve
    /// nothing — on the axis that runs at a twentieth of the others. It is not the same as linking
    /// *across* cleared ground, which is what <see cref="Clears"/> decides and which an outline can
    /// never do, because a profile cut has nothing cleared beside it.
    ///
    /// Both require the passes to meet at a point. A pass that starts anywhere else is a journey,
    /// however short, and a journey at depth through uncut material is the gouge this whole file
    /// exists to prevent.
    /// </summary>
    private static bool Continues(ToolpathPass previous, ToolpathPass next) =>
        previous.Stack >= 0
        && previous.Stack == next.Stack
        && previous.Path.Count > 0
        && next.Path.Count > 0
        && previous.End == next.Start
        && ((next.RampFromNm ?? next.DepthNm) == previous.DepthNm

            // Dropping: no ramp, and the new depth is below the old one. Deeper only — a pass that
            // wanted to come back *up* would be describing something this does not model.
            || (next.RampFromNm is null && next.DepthNm > previous.DepthNm));

    /// <summary>
    /// Whether dragging the tool from one pass's end to the next pass's start cuts only material
    /// that is already gone or is going in a moment.
    /// </summary>
    private static bool Clears(ToolpathPass previous, ToolpathPass next, long width, long sagittaNm)
    {
        var half = width / 2;

        var before = Ribbon(previous, half, sagittaNm);
        var after = Ribbon(next, half, sagittaNm);

        Work.Boolean(Polygons.VertexCount(before) + Polygons.VertexCount(after));
        var allowed = Clipper.Union(before, after, FillRule.NonZero);

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

        Work.Boolean(Polygons.VertexCount(swept) + Polygons.VertexCount(allowed));
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
