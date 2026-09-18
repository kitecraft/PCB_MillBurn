using System.Globalization;
using MillBurn.Core;

namespace MillBurn.Cam;

/// <summary>How to cut the blank out of a larger sheet.</summary>
public sealed record BlankOutlineOptions
{
    public Tool Tool { get; init; } = Tool.DefaultOutlineMill;

    public long BoardThicknessNm { get; init; } = Nm.FromMillimetres(1.6);

    public long BreakThroughNm { get; init; } = Nm.FromMillimetres(0.3);

    public long DepthPerPassNm { get; init; } = Nm.FromMillimetres(0.4);

    /// <summary>Width of each holding tab, on the two non-datum edges.</summary>
    public long TabWidthNm { get; init; } = Nm.FromMillimetres(4);

    /// <summary>Material left under a tab, for snapping the blank out.</summary>
    public long TabHeightNm { get; init; } = Nm.FromMillimetres(0.5);

    /// <summary>Tabs per non-datum edge. Zero cuts the blank free.</summary>
    public int TabsPerEdge { get; init; } = 2;

    /// <summary>
    /// How much of the lower-left corner to chamfer off, as a key.
    ///
    /// A rectangle seats identically whichever way round it went in, so a blank flipped the wrong
    /// way for a double-sided job seats perfectly and cuts a mirror image. The chamfer is the only
    /// thing on the piece that makes a wrong orientation visible before the cut rather than after,
    /// which is why it is cut here rather than offered as a nicety. Zero leaves the corner square.
    /// </summary>
    public long KeyNm { get; init; } = Nm.FromMillimetres(3);

    /// <summary>
    /// Centres of the alignment holes to cut in the waste, in the same coordinates as the stock.
    /// Worked out by <c>Blanks.Resolve</c>, which knows where the board sits inside the stock.
    /// </summary>
    public IReadOnlyList<Point2> AlignmentHoles { get; init; } = [];

    /// <summary>The diameter of those holes.</summary>
    public long HoleDiameterNm { get; init; }

    public long TotalDepthNm => BoardThicknessNm + BreakThroughNm;
}

/// <summary>
/// Cuts the rectangle of stock the whole job is built on.
///
/// The most valuable cut in a job, and the one that used to be done by hand with a hacksaw and
/// thrown away as a step with no value: **a piece the mill made is a piece whose dimensions the app
/// knows exactly.** Everything after it — on either machine — is referenced to this rectangle's
/// lower-left corner, so the two machines never have to agree about anything except how to hold a
/// rectangle against a stop.
///
/// Deliberately its own operation rather than a reuse of <see cref="OutlineOperation"/>. That one
/// cuts a board out of a blank and puts tabs wherever the perimeter asks for them; this one cuts a
/// blank out of a sheet and has a rule that one does not: **nothing may touch the datum edges.**
/// </summary>
public static class BlankOperation
{
    /// <summary>Builds the program that cuts one blank.</summary>
    public static Toolpath Build(Bounds blank, BlankOutlineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (blank.IsEmpty)
        {
            return Empty(options);
        }

        // Outside the rectangle by half the cutter, so the piece comes out the size asked for. The
        // whole point is a known dimension; cutting on the line would make it a cutter-width small.
        var half = options.Tool.DiameterNm / 2;
        var key = options.KeyNm > 0 ? Math.Min(options.KeyNm, Math.Min(blank.Width, blank.Height) / 4) : 0;
        var edges = Edges(blank, half, key);

        var depth = options.TotalDepthNm;
        var step = options.DepthPerPassNm > 0 ? options.DepthPerPassNm : depth;
        var steps = Math.Max(1, (int)Math.Ceiling(depth / (double)step));

        // The alignment holes first, while the stock is still part of the sheet: a hole cut after the
        // edges is a hole cut in a piece held only by its tabs. Same bit, so there is no change, and a
        // helix rather than a plunge for the reason every other hole here is one.
        var passes = HolePasses(options);
        var group = passes.Count > 0 ? 1 : 0;

        var depths = new List<long>();

        for (var i = 1; i <= steps; i++)
        {
            depths.Add(Math.Min(depth, i * step));
        }

        // The depth the tabs start at is always cut, exactly as the board outline does it: the step
        // down belongs to the cutter and the tab height to us, so nothing makes them divide into each
        // other, and a tab nothing ever cut down to holds the full thickness of the sheet.
        var tabTop = depth - options.TabHeightNm - options.BreakThroughNm;
        var addedForTabs = options.TabsPerEdge > 0 && tabTop > 0 && !depths.Contains(tabTop);

        if (addedForTabs)
        {
            depths.Add(tabTop);
            depths.Sort();
        }

        foreach (var at in depths)
        {
            // Tabs only once the cut is deep enough to need them, exactly as the board outline does.
            var tabbed = options.TabsPerEdge > 0 && at > tabTop;

            var runs = Runs(edges, tabbed ? options : null);

            foreach (var run in runs)
            {
                passes.Add(new ToolpathPass
                {
                    Path = run,
                    DepthNm = at,

                    // A loop with no tab to break it is closed, whether or not tabs were asked for.
                    Closed = runs.Count == 1 && run[^1].To == run[0].From,
                    Stack = 0,
                    Group = group,
                });
            }
        }

        var notes = new List<string>
        {
            Invariant($"{Mm(blank.Width)} x {Mm(blank.Height)} mm stock, cut {Mm(half)} mm outside the line so the piece is the size asked for."),
            Invariant($"{Mm(depth)} mm deep in {Mm(step)} mm passes with the {options.Tool.Name}."),
        };

        if (options.TabsPerEdge > 0)
        {
            notes.Add(Invariant(
                $"{options.TabsPerEdge} tab(s) on the top and right edges only, {Mm(options.TabWidthNm)} mm wide. The bottom and left edges are the datum and are cut clean."));

            if (addedForTabs)
            {
                notes.Add(Invariant(
                    $"One extra pass at {Mm(tabTop)} mm, which is where the tabs start: the step down does not reach it on its own, and a tab nothing cuts down to is the full thickness of the sheet."));
            }

            if (tabTop <= 0)
            {
                notes.Add(Invariant(
                    $"The tabs are {Mm(options.TabHeightNm)} mm tall and this cut is only {Mm(depth)} mm deep, so nothing is cut away at them: they hold the full thickness of the sheet and have to be cut by hand."));
            }
        }

        if (options.AlignmentHoles.Count > 0 && options.HoleDiameterNm > 0)
        {
            notes.Add(Invariant(
                $"{options.AlignmentHoles.Count} alignment hole(s) {Mm(options.HoleDiameterNm)} mm across, cut in the waste before the edges, with this same bit."));
        }

        if (key > 0)
        {
            notes.Add(Invariant(
                $"The lower-left corner is chamfered {Mm(key)} mm, in the same pass as the edges. That corner is the datum: it goes into the stop, and it is how you tell which way up the stock was."));
        }

        return new Toolpath
        {
            Kind = ToolpathKind.Outline,
            Label = "Stock",
            Tool = options.Tool,
            Passes = passes,
            Notes = notes,
        };
    }

    /// <summary>
    /// The alignment holes, spiralled out with the stock's own cutter.
    ///
    /// Built by the same code as a milled hole in a board, so a hole the cutter cannot spiral is
    /// refused there rather than plunged here — and each one is a stack of its own, so the router
    /// keeps its laps together.
    /// </summary>
    private static List<ToolpathPass> HolePasses(BlankOutlineOptions options)
    {
        if (options.AlignmentHoles.Count == 0 || options.HoleDiameterNm <= 0)
        {
            return [];
        }

        var plan = SlotOperation.Holes(
            [.. options.AlignmentHoles.Select((at, i) => new DrillSlotTarget(i + 1, at, at, options.HoleDiameterNm))],
            new ToolLibrary { Tools = [options.Tool] },
            new SlotOptions
            {
                BoardThicknessNm = options.BoardThicknessNm,
                BreakThroughNm = options.BreakThroughNm,
                ToolId = options.Tool.Id,
            },
            "Alignment holes");

        return [.. plan.Toolpaths.SelectMany(t => t.Passes)];
    }

    /// <summary>One edge of the loop the cutter follows, and whether a tab may go on it.</summary>
    private readonly record struct Edge(Point2 From, Point2 To, bool Tabbed);

    /// <summary>
    /// The loop the cutter's centre follows, clockwise from the lower-right corner: bottom, the
    /// chamfer, left, then the top and right edges — the only two that take tabs.
    ///
    /// **The chamfer is an edge of this loop.** It used to be cut as passes of its own after the
    /// rectangle was finished, which at full depth frees a small triangle of board at the corner for
    /// the cutter to throw. As part of the loop the corner stays with the sheet.
    ///
    /// Every edge is offset outward by half the cutter, the chamfer included, and the corners are
    /// where the offset edges meet. The chamfer is at 45°, so its offset line crosses the offset
    /// bottom and left edges short of where an unoffset one would, by half the cutter times
    /// (√2 − 1). Leaving that out makes the chamfer on the piece a sixth of a millimetre small with
    /// an 0.8 mm cutter — not much, but this is the piece every dimension is measured from.
    /// </summary>
    private static List<Edge> Edges(Bounds blank, long half, long key)
    {
        var left = blank.MinX - half;
        var right = blank.MaxX + half;
        var bottom = blank.MinY - half;
        var top = blank.MaxY + half;

        var lowerRight = new Point2(right, bottom);
        var upperLeft = new Point2(left, top);
        var upperRight = new Point2(right, top);

        var edges = new List<Edge>(5);

        if (key > 0)
        {
            var slack = half - (long)Math.Round(half * Math.Sqrt(2));
            var onBottom = new Point2(blank.MinX + key + slack, bottom);
            var onLeft = new Point2(left, blank.MinY + key + slack);

            edges.Add(new Edge(lowerRight, onBottom, false));
            edges.Add(new Edge(onBottom, onLeft, false));
            edges.Add(new Edge(onLeft, upperLeft, false));
        }
        else
        {
            var lowerLeft = new Point2(left, bottom);

            edges.Add(new Edge(lowerRight, lowerLeft, false));
            edges.Add(new Edge(lowerLeft, upperLeft, false));
        }

        edges.Add(new Edge(upperLeft, upperRight, true));
        edges.Add(new Edge(upperRight, lowerRight, true));

        return edges;
    }

    /// <summary>
    /// The loop, broken only where a tab has to be jumped. Null options means no tabs on this pass.
    ///
    /// Built as one walk round the loop rather than a run per edge. Per edge, a run that ended at a
    /// corner and the next that began there were still two runs, and the tool lifted clear and came
    /// straight back down on the same spot — at the top-left and top-right corners on every pass.
    /// The walk also joins its last run to its first, since they meet at the corner it started from.
    /// </summary>
    private static List<List<ArtSegment>> Runs(List<Edge> edges, BlankOutlineOptions? tabs)
    {
        var runs = new List<List<ArtSegment>> { new() };

        foreach (var edge in edges)
        {
            var pieces = tabs is not null && edge.Tabbed
                ? Gapped(edge.From, edge.To, tabs)
                : [[Line(edge.From, edge.To)]];

            for (var p = 0; p < pieces.Count; p++)
            {
                // A tab lies between this piece and the one before it on the same edge.
                if (p > 0)
                {
                    runs.Add([]);
                }

                runs[^1].AddRange(pieces[p]);
            }
        }

        runs.RemoveAll(r => r.Count == 0);

        if (runs.Count > 1 && runs[^1][^1].To == runs[0][0].From)
        {
            runs[^1].AddRange(runs[0]);
            runs.RemoveAt(0);
        }

        return runs;
    }

    /// <summary>One edge, split into the runs between its tabs.</summary>
    private static List<List<ArtSegment>> Gapped(Point2 from, Point2 to, BlankOutlineOptions options)
    {
        var length = from.DistanceTo(to);
        var half = options.TabWidthNm / 2.0;
        var runs = new List<List<ArtSegment>>();

        if (length <= options.TabWidthNm * options.TabsPerEdge)
        {
            runs.Add([Line(from, to)]);
            return runs;
        }

        // Spaced evenly along the edge rather than at its ends, so a tab never lands on a corner
        // where it would be in the way of the adjacent edge's run.
        var marks = new List<double> { 0 };

        for (var k = 1; k <= options.TabsPerEdge; k++)
        {
            var centre = length * k / (options.TabsPerEdge + 1.0);
            marks.Add(centre - half);
            marks.Add(centre + half);
        }

        marks.Add(length);

        for (var i = 0; i + 1 < marks.Count; i += 2)
        {
            var a = At(from, to, marks[i] / length);
            var b = At(from, to, marks[i + 1] / length);

            if (a != b)
            {
                runs.Add([Line(a, b)]);
            }
        }

        return runs;
    }

    private static Point2 At(Point2 from, Point2 to, double t) => new(
        from.X + (long)Math.Round((to.X - from.X) * t),
        from.Y + (long)Math.Round((to.Y - from.Y) * t));

    private static ArtSegment Line(Point2 from, Point2 to) => ArtSegment.Line(from, to);

    private static Toolpath Empty(BlankOutlineOptions options) => new()
    {
        Kind = ToolpathKind.Outline,
        Label = "Stock",
        Tool = options.Tool,
        Passes = [],
        Notes = ["No stock: the board has no extent."],
    };

    private static string Mm(long nm) => Nm.ToMillimetreString(nm, 2);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
