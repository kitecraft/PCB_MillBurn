using System.Globalization;
using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;

namespace MillBurn.Cam;

/// <summary>How to route the slots in a drill file.</summary>
public sealed record SlotOptions
{
    public long BoardThicknessNm { get; init; } = Nm.FromMillimetres(1.6);

    /// <summary>How far past the underside to go, so the slug actually comes out.</summary>
    public long BreakThroughNm { get; init; } = Nm.FromMillimetres(0.3);

    public long SagittaNm { get; init; } = Tessellate.DefaultSagittaNm;

    public long TotalDepthNm => BoardThicknessNm + BreakThroughNm;
}

/// <summary>One slot that will not be cut, and why.</summary>
public readonly record struct SlotRefusal(long WidthNm, int Count, string Reason);

/// <summary>What the slots in a file turned into.</summary>
public sealed record SlotPlan
{
    public required IReadOnlyList<Toolpath> Toolpaths { get; init; }

    /// <summary>Slots that were cut, grouped by the cutter that will cut them.</summary>
    public required IReadOnlyList<string> Summary { get; init; }

    /// <summary>Slots that will not be cut. One entry per width, naming the width and the reason.</summary>
    public required IReadOnlyList<SlotRefusal> Refusals { get; init; }

    public int CutCount { get; init; }

    public int RefusedCount => Refusals.Sum(r => r.Count);

    public static SlotPlan Nothing { get; } = new()
    {
        Toolpaths = [],
        Summary = [],
        Refusals = [],
    };
}

/// <summary>
/// Routes the oval holes a drill cannot make.
///
/// A slot is read, drawn, reported — and until now not made. Seven per board on both Arduino
/// designs, and every KiCad board with a slotted pad has some. The failure was silent in the worst
/// way available: the picture on screen shows the slots, so there was nothing to notice until the
/// connector would not fit.
///
/// **The cut is not the centreline.** A slot is a width as well as a path, so the cutter runs a
/// racetrack around the *inside* of it, offset in by its own radius. Only when the cutter is
/// exactly the slot's width does the centreline itself become the path, in one pass.
///
/// **It ramps rather than plunging.** An end mill driven straight down into FR4 is how small
/// cutters break, and the geometry hands us the lead-in for free: the slot is already a path to
/// descend along. Each depth step ramps over one lap, and a final lap at constant depth flattens
/// the floor the ramp left sloping.
///
/// **A cutter is chosen from the library, never invented** — see <see cref="ToolChooser"/>. Which
/// means some slots cannot be cut, and that is the point of the whole exercise rather than a
/// shortcoming: cutting a 0.8 mm slot where the board asked for 0.6 mm puts a hole through the
/// adjacent pad. Those are refused by name, and the slots that *can* be cut still are — a board
/// with four impossible slots and three possible ones gets a program for the three.
/// </summary>
public static class SlotOperation
{
    /// <summary>Builds a routing program for every slot in a drill file that can be cut.</summary>
    public static SlotPlan Build(
        IReadOnlyList<DrillSlotTarget> slots, ToolLibrary library, SlotOptions options, string label = "Slots")
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(options);

        if (slots.Count == 0)
        {
            return SlotPlan.Nothing;
        }

        var depth = options.TotalDepthNm;
        var toolpaths = new List<Toolpath>();
        var summary = new List<string>();
        var refusals = new List<SlotRefusal>();
        var cut = 0;

        // Grouped by width, because the choice of cutter is a property of the width and nothing
        // else — and because a refusal that says "four 0.60 mm slots" is worth four times one that
        // names a coordinate.
        foreach (var group in slots.GroupBy(s => s.WidthNm).OrderBy(g => g.Key))
        {
            var width = group.Key;
            var choice = ToolChooser.ForWidth(library, width, depth);

            if (choice.Tool is not { } tool)
            {
                refusals.Add(new SlotRefusal(width, group.Count(), choice.Refusal!));
                continue;
            }

            var passes = new List<ToolpathPass>();

            foreach (var slot in group)
            {
                passes.AddRange(PassesFor(slot, tool, options));
            }

            if (passes.Count == 0)
            {
                continue;
            }

            cut += group.Count();
            toolpaths.Add(new Toolpath
            {
                Kind = ToolpathKind.Outline,
                Label = Invariant($"{label} — {Mm(width)} mm"),
                Tool = tool,
                Passes = passes,
                Notes =
                [
                    Invariant($"{Count(group.Count())} {Mm(width)} mm wide, cut with the {tool.Name}."),
                    Invariant($"{Mm(depth)} mm deep in {Mm(tool.StepdownNm > 0 ? tool.StepdownNm : depth)} mm steps, ramping along the slot rather than plunging."),
                ],
            });

            summary.Add(Invariant($"{Count(group.Count())} {Mm(width)} mm wide · {tool.Name}"));
        }

        return new SlotPlan
        {
            Toolpaths = toolpaths,
            Summary = summary,
            Refusals = refusals,
            CutCount = cut,
        };
    }

    /// <summary>
    /// The depth passes for one slot: a lap per step, ramping, then one flat lap.
    ///
    /// The flat lap is not optional. A ramp leaves the floor sloping by exactly one step over the
    /// length of the lap, so without it the deepest point and the shallowest differ by a stepdown —
    /// which on a slot that has to clear a connector's leg is the difference between fitting and
    /// nearly fitting.
    /// </summary>
    private static IEnumerable<ToolpathPass> PassesFor(DrillSlotTarget slot, Tool tool, SlotOptions options)
    {
        var path = PathFor(slot, tool, options);

        if (path.Count == 0)
        {
            yield break;
        }

        // A racetrack closes on itself; a cutter the slot's own width draws one line down the
        // middle and does not. The emitter and the linker both care, so it is derived from the
        // path rather than asserted by the caller.
        var closed = path.Count > 2 && path[^1].To == path[0].From;

        var depth = options.TotalDepthNm;
        var step = tool.StepdownNm > 0 ? tool.StepdownNm : depth;
        var steps = Math.Max(1, (int)Math.Ceiling(depth / (double)step));
        var previous = 0L;

        // An open slot is cut out and then back, rather than lifting and returning to the same end
        // each time: the tool finishes every pass exactly where the next one starts. A closed
        // racetrack is left alone, because reversing it reverses the cutting hand — which is a
        // choice that belongs to the operator once [6.2] exists, not a side effect of pass numbering.
        var alternates = !closed;

        for (var i = 1; i <= steps; i++)
        {
            var to = Math.Min(depth, i * step);

            yield return new ToolpathPass
            {
                Path = alternates && i % 2 == 0 ? Reversed(path) : path,
                DepthNm = to,
                RampFromNm = previous,
                Closed = closed,
                Stack = slot.Index,
                Group = 0,
            };

            previous = to;
        }

        yield return new ToolpathPass
        {
            Path = alternates && (steps + 1) % 2 == 0 ? Reversed(path) : path,
            DepthNm = depth,
            Closed = closed,
            Stack = slot.Index,
            Group = 0,
        };
    }

    /// <summary>The same path walked the other way: segments in reverse order, each one flipped.</summary>
    private static List<ArtSegment> Reversed(List<ArtSegment> path)
    {
        var back = new List<ArtSegment>(path.Count);

        for (var i = path.Count - 1; i >= 0; i--)
        {
            var s = path[i];

            back.Add(s.IsArc
                ? new ArtSegment(
                    s.Sweep == ArtSweep.Clockwise ? ArtSweep.CounterClockwise : ArtSweep.Clockwise,
                    s.To,
                    s.From,
                    s.Centre)
                : new ArtSegment(ArtSweep.Linear, s.To, s.From, Point2.Origin));
        }

        return back;
    }

    /// <summary>
    /// Where the cutter's centre goes.
    ///
    /// A cutter the slot's own width has nowhere to go but down the middle. A narrower one runs the
    /// racetrack the slot's centreline makes when inflated by the difference — which Clipper gives
    /// directly, and which is a stadium: two straights and two half-circles, arcs kept as arcs.
    /// </summary>
    private static List<ArtSegment> PathFor(DrillSlotTarget slot, Tool tool, SlotOptions options)
    {
        var clearance = (slot.WidthNm - tool.DiameterNm) / 2;

        if (clearance <= ToolChooser.SlackNm)
        {
            // Down the middle. A degenerate slot — both ends at one point — is a hole this cutter
            // fills exactly, and there is no motion to make.
            return slot.From == slot.To
                ? []
                : [new ArtSegment(ArtSweep.Linear, slot.From, slot.To, Point2.Origin)];
        }

        var inflated = Clipper.InflatePaths(
            new Paths64 { new Path64 { new Point64(slot.From.X, slot.From.Y), new Point64(slot.To.X, slot.To.Y) } },
            clearance,
            JoinType.Round,
            EndType.Round,
            arcTolerance: options.SagittaNm);

        if (inflated.Count == 0 || inflated[0].Count < 3)
        {
            return [];
        }

        var ring = inflated[0];
        var segments = new List<ArtSegment>(ring.Count);

        for (var i = 0; i < ring.Count; i++)
        {
            var from = new Point2(ring[i].X, ring[i].Y);
            var to = new Point2(ring[(i + 1) % ring.Count].X, ring[(i + 1) % ring.Count].Y);

            if (from != to)
            {
                segments.Add(new ArtSegment(ArtSweep.Linear, from, to, Point2.Origin));
            }
        }

        return segments;
    }

    private static string Count(int n) => n == 1 ? "1 slot" : Invariant($"{n} slots");

    private static string Mm(long nm) => Nm.ToMillimetreString(nm, 2);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// One slot to route: where it runs and how wide it is.
///
/// Separate from the parser's <c>DrillSlot</c>, which carries a tool *number* into a dictionary.
/// By the time CAM sees it the width is the fact that matters, and the index is what keeps the
/// depth passes of one slot together when the optimizer reorders everything.
/// </summary>
public readonly record struct DrillSlotTarget(int Index, Point2 From, Point2 To, long WidthNm);
