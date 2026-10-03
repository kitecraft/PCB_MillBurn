using System.Globalization;
using System.Text;
using MillBurn.Core;

namespace MillBurn.Gcode;

/// <summary>One thing the program does, drawn on the picture beside its row.</summary>
public sealed record GuideMark
{
    /// <summary>Where it is. For a slot, the middle of its run.</summary>
    public required Point2 At { get; init; }

    /// <summary>
    /// The run itself, for something with a length and a direction. Null for a hole.
    ///
    /// The direction is not decoration: it is what tells somebody watching the machine that it is
    /// doing what the page says.
    /// </summary>
    public IReadOnlyList<Point2>? Path { get; init; }

    /// <summary>How wide the feature is, which is what a refusal is labelled with.</summary>
    public long WidthNm { get; init; }

    /// <summary>
    /// A feature this program will not make.
    ///
    /// Drawn greyed and hatched, and labelled with its width rather than a number: it has no place
    /// in the running order because it is not in the program. It is the one thing on the page that
    /// cannot be checked against the machine, which is exactly why it is on the page.
    /// </summary>
    public bool Refused { get; init; }
}

/// <summary>
/// The picture beside a bit's row: the board, and what this bit does to it, numbered in run order.
///
/// **Requested from the workshop:** *"in the companion html for drills and routing, include images
/// of the layer with the holes/slots numbered in the order of drilling/routing."* Both pages were a
/// table of numbers standing in for a board — *six holes with the 1.00 mm bit*, leaving the operator
/// to work out which six at the two moments the page exists for: deciding a bit is about to go in
/// the wrong place, and finding where a run stopped.
///
/// **Inline SVG, because the page is one file by rule** — no network, nothing beside it. SVG rather
/// than a raster so the numbers stay crisp when somebody zooms into a cluster of vias on a phone in
/// a workshop, so it prints properly next to a machine, and because it is a few kilobytes where a
/// legible raster of a 100 mm board is hundreds.
///
/// **The marks come from the program and the backdrop comes from the board**, which is the split the
/// rest of this code makes. Where the two disagree the marks are the truth: a picture drawn from the
/// toolpaths would show the run somebody meant.
///
/// Recorded in [06 §6.5](../../Documentation/06-Roadmap-and-Risks.md).
/// </summary>
public static class GuideFigure
{
    /// <summary>
    /// Above this many marks, numbering stops being readable and the drawing says something else.
    ///
    /// The Arduino Mega drills 258 holes in six sizes; numbering 44 of them on a 100 mm board is
    /// already crowded and numbering 258 is spaghetti. So the drawing degrades honestly rather than
    /// producing something unreadable and calling it a feature — dots, a marked start and the travel
    /// between them, which answers *where does it begin and roughly where does it go* without
    /// pretending to answer *which one is number 173*.
    /// </summary>
    public const int MostToNumber = 40;

    /// <summary>
    /// The figure, as an inline SVG element, or an empty string when there is nothing to draw.
    /// </summary>
    /// <param name="outline">The board's own profile, as polylines, for the backdrop.</param>
    /// <param name="marks">What this bit does, in the order it does it.</param>
    /// <param name="bounds">The board's extent, which fixes the frame.</param>
    /// <param name="title">Read out by a screen reader, and shown if the SVG cannot be.</param>
    public static string Draw(
        IReadOnlyList<IReadOnlyList<Point2>> outline,
        IReadOnlyList<GuideMark> marks,
        Bounds bounds,
        string title)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(marks);

        if (bounds.IsEmpty || marks.Count == 0)
        {
            return string.Empty;
        }

        // A millimetre of margin, so a hole on the very edge of the board is not half outside the
        // frame — and holes do sit on the edge: a castellation is one.
        var margin = Nm.FromMillimetres(1);
        var width = Nm.ToMillimetres(bounds.Width + (2 * margin));
        var height = Nm.ToMillimetres(bounds.Height + (2 * margin));

        // Everything is sized from the board rather than from the page, so a 20 mm coupon and a
        // 200 mm panel both come out legible at whatever width the page gives the figure.
        var span = Math.Max(width, height);
        var stroke = span / 400;
        var font = Math.Max(span / 36, 1.2);
        var dot = Math.Max(span / 150, 0.35);

        var sb = new StringBuilder(2048);

        // **No `xmlns`**, which the page's own test caught: it declares a `http://` URL, and these
        // pages carry no URLs at all so that nothing in them can look like something to fetch. An
        // inline SVG in HTML needs no namespace — the parser puts it in the right one — and the
        // attribute is only required by a standalone `.svg` file, which this is not.
        sb.Append(Invariant($"<svg class=\"figure\" viewBox=\"0 0 {Num(width)} {Num(height)}\" "))
            .Append("role=\"img\" aria-label=\"")
            .Append(Escape(title))
            .Append("\">\n");

        // The hatch a refused feature is filled with. Declared only when something uses it: an
        // unused definition in every figure on a page with twenty of them is twenty times nothing.
        if (marks.Any(m => m.Refused))
        {
            sb.Append("<defs><pattern id=\"refused\" width=\"1.4\" height=\"1.4\" ")
                .Append("patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\">")
                .Append(Invariant($"<line x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1.4\" stroke=\"#9aa0a6\" stroke-width=\"{Num(stroke * 2)}\" />"))
                .Append("</pattern></defs>\n");
        }

        Point2 Origin(Point2 at) => new(at.X - bounds.MinX + margin, at.Y - bounds.MinY + margin);

        // SVG counts down the page and a board counts up, so every y is flipped here rather than by
        // a transform: a transform would mirror the numbers with the geometry.
        double X(Point2 at) => Nm.ToMillimetres(Origin(at).X);
        double Y(Point2 at) => height - Nm.ToMillimetres(Origin(at).Y);

        Backdrop(sb, outline, X, Y, stroke);

        if (marks.Count > MostToNumber)
        {
            Dots(sb, marks, X, Y, stroke, dot);
        }
        else
        {
            Numbered(sb, marks, X, Y, stroke, font, dot);
        }

        sb.Append("</svg>");

        return sb.ToString();
    }

    /// <summary>
    /// The board, drawn thin and pale.
    ///
    /// The outline and the features and nothing else — not the copper. A board's copper as SVG runs
    /// to hundreds of kilobytes on the panel and adds nothing to *where does this bit go*, and it
    /// would keep the page from opening on a phone.
    /// </summary>
    private static void Backdrop(
        StringBuilder sb,
        IReadOnlyList<IReadOnlyList<Point2>> outline,
        Func<Point2, double> x,
        Func<Point2, double> y,
        double stroke)
    {
        if (outline.Count == 0)
        {
            return;
        }

        sb.Append(Invariant($"<path class=\"board\" fill=\"none\" stroke=\"#9aa0a6\" stroke-width=\"{Num(stroke)}\" d=\""));

        foreach (var ring in outline)
        {
            if (ring.Count < 2)
            {
                continue;
            }

            sb.Append(Invariant($"M {Num(x(ring[0]))} {Num(y(ring[0]))}"));

            for (var i = 1; i < ring.Count; i++)
            {
                sb.Append(Invariant($" L {Num(x(ring[i]))} {Num(y(ring[i]))}"));
            }

            sb.Append(" Z");
        }

        sb.Append("\" />\n");
    }

    /// <summary>
    /// Up to forty marks, each with its number.
    ///
    /// A label that would land on one already placed is pushed out and given a leader line, which is
    /// what a drawing does: the alternative is two numbers on top of each other, which is worse than
    /// either of them alone.
    /// </summary>
    private static void Numbered(
        StringBuilder sb,
        IReadOnlyList<GuideMark> marks,
        Func<Point2, double> x,
        Func<Point2, double> y,
        double stroke,
        double font,
        double dot)
    {
        var placed = new List<(double X, double Y)>();
        var order = 0;

        foreach (var mark in marks)
        {
            var at = (X: x(mark.At), Y: y(mark.At));

            if (mark.Path is { Count: > 1 } run)
            {
                Slot(sb, run, x, y, stroke, mark.Refused, Nm.ToMillimetres(mark.WidthNm));
            }
            else if (mark.Refused)
            {
                sb.Append(
                    $"<circle cx=\"{Num(at.X)}\" cy=\"{Num(at.Y)}\" r=\"{Num(Math.Max(Nm.ToMillimetres(mark.WidthNm) / 2, dot))}\" "
                    + $"fill=\"url(#refused)\" stroke=\"#9aa0a6\" stroke-width=\"{Num(stroke)}\" />\n");
            }
            else
            {
                sb.Append(
                    $"<circle cx=\"{Num(at.X)}\" cy=\"{Num(at.Y)}\" r=\"{Num(Math.Max(Nm.ToMillimetres(mark.WidthNm) / 2, dot))}\" "
                    + $"fill=\"none\" stroke=\"#1a73e8\" stroke-width=\"{Num(stroke * 2)}\" />\n");
            }

            // A refusal is labelled with its width, not a number: it has no place in the running
            // order, because it is not in the program.
            var label = mark.Refused
                ? Invariant($"{Nm.ToMillimetreString(mark.WidthNm, 2)}")
                : (++order).ToString(CultureInfo.InvariantCulture);

            // **A run is numbered where it starts**, not in its middle, which is where the middle put
            // the number: on top of the line it was labelling. The arrow is already at the far end,
            // so the two together read as "in at this one, out at that one".
            var anchor = mark.Path is { Count: > 0 } run2
                ? (X: x(run2[0]), Y: y(run2[0]))
                : at;

            // And on the side the run is not: a left-to-right slot labelled on its right has its
            // number sitting on its own arrowhead, which is what the first attempt produced.
            var away = mark.Path is { Count: > 1 } run3 && x(run3[^1]) > x(run3[0]) ? -1 : 1;

            Label(sb, anchor, label, placed, font, stroke, mark.Refused, away);
        }
    }

    /// <summary>Where a number goes: beside its mark, or pushed out with a leader if that is taken.</summary>
    private static void Label(
        StringBuilder sb,
        (double X, double Y) at,
        string text,
        List<(double X, double Y)> placed,
        double font,
        double stroke,
        bool refused,
        int away = 1)
    {
        // Beside the mark, on the line of its own centre, which is how a drawing numbers a hole.
        //
        // **The first version put the number above and to the right**, and looking at the figure is
        // the only reason that was caught: on a column of holes a millimetre or two apart, every
        // number sat between two of them and belonged to neither. Nothing about the markup said so.
        (double X, double Y)[] tries =
        [
            (at.X + (font * 0.95 * away), at.Y + (font * 0.36)),
            (at.X - (font * 0.95 * away), at.Y + (font * 0.36)),
            (at.X + (font * 0.95 * away), at.Y - (font * 0.75)),
            (at.X - (font * 0.95 * away), at.Y - (font * 0.75)),
            (at.X + (font * 0.95 * away), at.Y + (font * 1.45)),
            (at.X - (font * 0.95 * away), at.Y + (font * 1.45)),
            (at.X + (font * 2.1 * away), at.Y + (font * 0.36)),
            (at.X - (font * 2.1 * away), at.Y + (font * 0.36)),
        ];

        var spot = tries[0];
        var clear = false;

        foreach (var candidate in tries)
        {
            if (placed.All(p => Math.Abs(p.X - candidate.X) > font || Math.Abs(p.Y - candidate.Y) > font))
            {
                spot = candidate;
                clear = true;
                break;
            }
        }

        placed.Add(spot);

        // A leader only when the label had to move out of its first choice: a line from every number
        // to its own hole would be a thicket.
        // **Either way it moved**, which a `/code-review` pass caught: testing the horizontal
        // displacement alone meant a number pushed a full line-height below its hole — which is two
        // of the eight places it can go — was drawn with no leader at all, on the dense column of
        // holes this whole arrangement exists for.
        if (clear && (Math.Abs(spot.X - at.X) > font * 1.5 || Math.Abs(spot.Y - at.Y) > font))
        {
            sb.Append(
                $"<line x1=\"{Num(at.X)}\" y1=\"{Num(at.Y)}\" x2=\"{Num(spot.X)}\" y2=\"{Num(spot.Y - (font * 0.3))}\" "
                + $"stroke=\"#9aa0a6\" stroke-width=\"{Num(stroke)}\" />\n");
        }

        sb.Append(Invariant($"<text x=\"{Num(spot.X)}\" y=\"{Num(spot.Y)}\" font-size=\"{Num(font)}\" "))
            .Append(Invariant($"text-anchor=\"middle\" fill=\"{(refused ? "#5f6368" : "#1a73e8")}\">"))
            .Append(Escape(text))
            .Append("</text>\n");
    }

    /// <summary>A slot: its own shape, with an arrow at the end showing which way the cutter runs.</summary>
    private static void Slot(
        StringBuilder sb,
        IReadOnlyList<Point2> run,
        Func<Point2, double> x,
        Func<Point2, double> y,
        double stroke,
        bool refused,
        double widthMm = 0)
    {
        // **A refused feature is drawn as the shape it would have been**, hatched, and not as a line
        // through where it would have been. A line says *the cutter would have gone this way*; the
        // shape says *this much of your board is not going to be there*, which is the thing nobody
        // can check against the machine afterwards because it is absent from the program.
        //
        // Only for a straight centreline, which is what a refused slot has: anything with corners in
        // it falls through to the dashed line below rather than being offset badly.
        if (refused && widthMm > 0 && run.Count == 2)
        {
            Band(sb, x(run[0]), y(run[0]), x(run[1]), y(run[1]), widthMm, stroke);
            return;
        }

        sb.Append(Invariant($"<path fill=\"none\" stroke=\"{(refused ? "#9aa0a6" : "#1a73e8")}\" stroke-width=\"{Num(stroke * 2)}\" "))
            .Append(refused ? Invariant($"stroke-dasharray=\"{Num(stroke * 6)} {Num(stroke * 4)}\" ") : string.Empty)
            .Append(Invariant($"d=\"M {Num(x(run[0]))} {Num(y(run[0]))}"));

        for (var i = 1; i < run.Count; i++)
        {
            sb.Append(Invariant($" L {Num(x(run[i]))} {Num(y(run[i]))}"));
        }

        sb.Append("\" />\n");

        if (refused)
        {
            return;
        }

        // The arrowhead, built from the last segment that has any length to it.
        var tip = (X: x(run[^1]), Y: y(run[^1]));
        var from = tip;

        for (var i = run.Count - 2; i >= 0; i--)
        {
            from = (x(run[i]), y(run[i]));

            if (Math.Abs(from.X - tip.X) > 1e-6 || Math.Abs(from.Y - tip.Y) > 1e-6)
            {
                break;
            }
        }

        var dx = tip.X - from.X;
        var dy = tip.Y - from.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        if (length <= 0)
        {
            return;
        }

        var head = stroke * 8;
        var ux = dx / length;
        var uy = dy / length;

        sb.Append(
            $"<path fill=\"#1a73e8\" d=\"M {Num(tip.X)} {Num(tip.Y)} "
            + $"L {Num(tip.X - (head * ux) + (head * 0.4 * uy))} {Num(tip.Y - (head * uy) - (head * 0.4 * ux))} "
            + $"L {Num(tip.X - (head * ux) - (head * 0.4 * uy))} {Num(tip.Y - (head * uy) + (head * 0.4 * ux))} Z\" />\n");
    }

    /// <summary>
    /// The outline of a slot that width, as a stadium: a rectangle with a half-round at each end,
    /// which is the shape a round cutter leaves and the shape an EDA tool drew.
    /// </summary>
    private static void Band(
        StringBuilder sb,
        double x0,
        double y0,
        double x1,
        double y1,
        double width,
        double stroke)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        var r = width / 2;

        // A refused *hole* is a centreline of no length — a milled hole refused for being too small
        // is a target whose two ends are the same point. It is a circle, and drawing nothing at all
        // while the caller still writes its width label leaves a number floating on the board with no
        // mark under it. Found by `/code-review`.
        if (length <= 0)
        {
            sb.Append(Invariant($"<circle cx=\"{Num(x0)}\" cy=\"{Num(y0)}\" r=\"{Num(Math.Max(r, stroke * 3))}\" "))
                .Append(Invariant($"fill=\"url(#refused)\" stroke=\"#9aa0a6\" stroke-width=\"{Num(stroke)}\" />\n"));

            return;
        }

        var ux = dx / length;
        var uy = dy / length;
        var nx = -uy * r;
        var ny = ux * r;

        // Down one side, round the far end, back the other side, round the near end. The arcs are
        // written as SVG arc commands rather than opened out: two numbers each, and a circle drawn
        // by a renderer is rounder than one drawn by me.
        //
        // **Sweep flag zero, and a `/code-review` pass is why.** The figure counts down the page, so
        // the traversal above runs counter-clockwise on screen, and a cap has to turn the same way to
        // bulge outwards. With the flag set the far cap goes from six o'clock to twelve the short way
        // round — through the inside of the rectangle — and both ends come out as bites taken out of
        // the shape rather than as the round ends a cutter leaves.
        sb.Append(Invariant($"<path fill=\"url(#refused)\" stroke=\"#9aa0a6\" stroke-width=\"{Num(stroke)}\" "))
            .Append(Invariant($"stroke-dasharray=\"{Num(stroke * 6)} {Num(stroke * 4)}\" "))
            .Append(Invariant($"d=\"M {Num(x0 + nx)} {Num(y0 + ny)} L {Num(x1 + nx)} {Num(y1 + ny)} "))
            .Append(Invariant($"A {Num(r)} {Num(r)} 0 0 0 {Num(x1 - nx)} {Num(y1 - ny)} "))
            .Append(Invariant($"L {Num(x0 - nx)} {Num(y0 - ny)} "))
            .Append(Invariant($"A {Num(r)} {Num(r)} 0 0 0 {Num(x0 + nx)} {Num(y0 + ny)} Z\" />\n"));
    }

    /// <summary>
    /// More than can be numbered: dots, a marked start, and the travel between them.
    ///
    /// This answers the question a crowded drawing cannot — where does it begin, and roughly where
    /// does it go — and refuses the one it would only pretend to answer. The count stays in the
    /// page's text, which is the authority either way.
    /// </summary>
    private static void Dots(
        StringBuilder sb,
        IReadOnlyList<GuideMark> marks,
        Func<Point2, double> x,
        Func<Point2, double> y,
        double stroke,
        double dot)
    {
        // **A refusal is still a refusal on a crowded drawing**, which a `/code-review` pass caught:
        // this branch drew every mark as an ordinary dot and threaded the travel line through them,
        // so once a figure passed forty marks the features nothing will cut became indistinguishable
        // from the ones it will, the travel detoured through places the cutter never goes, and the
        // caption still promised they were hatched. They are drawn as themselves, and left out of the
        // run, because they are not part of it.
        foreach (var mark in marks.Where(m => m.Refused))
        {
            if (mark.Path is { Count: > 1 } run)
            {
                Slot(sb, run, x, y, stroke, refused: true, Nm.ToMillimetres(mark.WidthNm));
                continue;
            }

            sb.Append(Invariant($"<circle cx=\"{Num(x(mark.At))}\" cy=\"{Num(y(mark.At))}\" r=\"{Num(Math.Max(Nm.ToMillimetres(mark.WidthNm) / 2, dot))}\" "))
                .Append(Invariant($"fill=\"url(#refused)\" stroke=\"#9aa0a6\" stroke-width=\"{Num(stroke)}\" />\n"));
        }

        var cut = marks.Where(m => !m.Refused).ToList();

        if (cut.Count == 0)
        {
            return;
        }

        sb.Append(Invariant($"<path fill=\"none\" stroke=\"#c6d4f9\" stroke-width=\"{Num(stroke)}\" d=\"M {Num(x(cut[0].At))} {Num(y(cut[0].At))}"));

        for (var i = 1; i < cut.Count; i++)
        {
            sb.Append(Invariant($" L {Num(x(cut[i].At))} {Num(y(cut[i].At))}"));
        }

        sb.Append("\" />\n");

        foreach (var mark in cut)
        {
            sb.Append(
                $"<circle cx=\"{Num(x(mark.At))}\" cy=\"{Num(y(mark.At))}\" r=\"{Num(dot)}\" fill=\"#1a73e8\" />\n");
        }

        // Where it starts, which is the one place worth picking out of a field of dots.
        sb.Append(
            $"<circle cx=\"{Num(x(cut[0].At))}\" cy=\"{Num(y(cut[0].At))}\" r=\"{Num(dot * 2.6)}\" "
            + $"fill=\"none\" stroke=\"#1a73e8\" stroke-width=\"{Num(stroke * 2)}\" />\n");
    }

    private static string Num(double value) =>
        Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
