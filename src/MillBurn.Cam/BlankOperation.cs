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
        var out_ = options.Tool.DiameterNm / 2;
        var path = new Bounds(
            blank.MinX - out_, blank.MinY - out_, blank.MaxX + out_, blank.MaxY + out_);

        var depth = options.TotalDepthNm;
        var step = options.DepthPerPassNm > 0 ? options.DepthPerPassNm : depth;
        var steps = Math.Max(1, (int)Math.Ceiling(depth / (double)step));
        var passes = new List<ToolpathPass>();

        for (var i = 1; i <= steps; i++)
        {
            var at = Math.Min(depth, i * step);

            // Tabs only once the cut is deep enough to need them, exactly as the board outline does.
            var tabbed = options.TabsPerEdge > 0
                && at > depth - options.TabHeightNm - options.BreakThroughNm;

            foreach (var run in tabbed ? Tabbed(path, options) : [Whole(path)])
            {
                passes.Add(new ToolpathPass
                {
                    Path = run,
                    DepthNm = at,
                    Closed = !tabbed,
                    Stack = 0,
                });
            }
        }

        if (options.KeyNm > 0)
        {
            passes.AddRange(Key(path, blank, options, depth, step, steps));
        }

        var notes = new List<string>
        {
            Invariant($"{Mm(blank.Width)} x {Mm(blank.Height)} mm blank, cut {Mm(out_)} mm outside the line so the piece is the size asked for."),
            Invariant($"{Mm(depth)} mm deep in {Mm(step)} mm passes with the {options.Tool.Name}."),
        };

        if (options.TabsPerEdge > 0)
        {
            notes.Add(Invariant(
                $"{options.TabsPerEdge} tab(s) on the top and right edges only, {Mm(options.TabWidthNm)} mm wide. The bottom and left edges are the datum and are cut clean."));
        }

        if (options.KeyNm > 0)
        {
            notes.Add(Invariant(
                $"The lower-left corner is chamfered {Mm(options.KeyNm)} mm. That corner is the datum: it goes into the stop, and it is how you tell which way up the blank was."));
        }

        return new Toolpath
        {
            Kind = ToolpathKind.Outline,
            Label = "Blank",
            Tool = options.Tool,
            Passes = passes,
            Notes = notes,
        };
    }

    /// <summary>The whole rectangle, anticlockwise from the lower left.</summary>
    private static List<ArtSegment> Whole(Bounds r)
    {
        var corners = Corners(r);
        var path = new List<ArtSegment>(4);

        for (var i = 0; i < 4; i++)
        {
            path.Add(Line(corners[i], corners[(i + 1) % 4]));
        }

        return path;
    }

    /// <summary>
    /// The rectangle broken into runs, with gaps on the top and right edges only.
    ///
    /// The bottom and left edges are cut in one piece each, every pass, because they are the datum.
    /// A tab stub there is a few tenths of an obstruction that stops the blank seating, and it is
    /// invisible — the piece sits at a slight angle and everything after it is wrong.
    /// </summary>
    private static List<List<ArtSegment>> Tabbed(Bounds r, BlankOutlineOptions options)
    {
        var c = Corners(r);

        // Bottom then left, uninterrupted: from the lower-right round to the upper-left.
        var datum = new List<ArtSegment> { Line(c[1], c[0]), Line(c[0], c[3]) };
        var runs = new List<List<ArtSegment>> { datum };

        runs.AddRange(Gapped(c[3], c[2], options));  // top, left to right
        runs.AddRange(Gapped(c[2], c[1], options));  // right, top to bottom

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

    /// <summary>
    /// The chamfer across the datum corner, cut to full depth after the outline.
    ///
    /// After, because until the outline is through there is nothing for it to chamfer; and at the
    /// corner the two datum edges meet, so it removes material from both and takes no tab with it.
    /// </summary>
    private static IEnumerable<ToolpathPass> Key(
        Bounds path, Bounds blank, BlankOutlineOptions options, long depth, long step, int steps)
    {
        var key = Math.Min(options.KeyNm, Math.Min(blank.Width, blank.Height) / 4);

        if (key <= 0)
        {
            yield break;
        }

        var half = options.Tool.DiameterNm / 2;

        // Across the corner of the blank itself, offset outward by half the cutter so the chamfer
        // is the size asked for, like the rest of the cut.
        var a = new Point2(blank.MinX + key, blank.MinY - half);
        var b = new Point2(blank.MinX - half, blank.MinY + key);

        for (var i = 1; i <= steps; i++)
        {
            yield return new ToolpathPass
            {
                Path = [Line(a, b)],
                DepthNm = Math.Min(depth, i * step),
                Closed = false,
                Stack = 1,
            };
        }

        _ = path;
    }

    private static Point2[] Corners(Bounds r) =>
    [
        new(r.MinX, r.MinY),
        new(r.MaxX, r.MinY),
        new(r.MaxX, r.MaxY),
        new(r.MinX, r.MaxY),
    ];

    private static Point2 At(Point2 from, Point2 to, double t) => new(
        from.X + (long)Math.Round((to.X - from.X) * t),
        from.Y + (long)Math.Round((to.Y - from.Y) * t));

    private static ArtSegment Line(Point2 from, Point2 to) =>
        new(ArtSweep.Linear, from, to, Point2.Origin);

    private static Toolpath Empty(BlankOutlineOptions options) => new()
    {
        Kind = ToolpathKind.Outline,
        Label = "Blank",
        Tool = options.Tool,
        Passes = [],
        Notes = ["No blank: the board has no extent."],
    };

    private static string Mm(long nm) => Nm.ToMillimetreString(nm, 2);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
