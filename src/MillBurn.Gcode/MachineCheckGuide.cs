using System.Globalization;
using System.Text;
using static System.FormattableString;

namespace MillBurn.Gcode;

/// <summary>
/// The page written beside a machine check: what to measure, and what the number means.
///
/// A coupon with six holes in it is not a result, and the arithmetic that turns it into one —
/// "(row 2 − row 3) ÷ 2" — is exactly the sort of thing that gets reconstructed wrongly at a bench
/// an hour later. So the page carries the method, a picture of the coupon drawn from the same
/// numbers the program was emitted from, and a table with the nominal already filled in and blanks
/// for the readings.
///
/// It also says what the check cannot see. Pins and calipers resolve about a tenth of a millimetre,
/// and a page that prints three decimals without saying so invites somebody to believe the third
/// one.
/// </summary>
public static class MachineCheckGuide
{
    /// <summary>Builds the page for a check that has just been planned.</summary>
    public static string Build(MachineCheckOptions options, MachineCheckReport report, string programName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);

        var page = new StringBuilder();
        var title = Title(report);

        page.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        page.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        page.Append("<title>").Append(Escape(title)).Append("</title>\n");
        page.Append("<style>\n").Append(Style).Append("</style>\n</head>\n<body>\n");

        page.Append("<h1>").Append(Escape(title)).Append("</h1>\n");
        page.Append("<p class=\"sub\">").Append(Escape(options.Tool.Name))
            .Append(" &middot; <code>").Append(Escape(programName)).Append("</code></p>\n");

        Facts(page, report);
        Warnings(page, report);
        Diagram(page, options, report);
        Before(page, options, report);
        Measure(page, report);
        Arithmetic(page, report);
        Limits(page, report);

        page.Append(GuideFooter.For(
            "This page describes the program beside it; if you change the numbers and export again, read it again.",
            "machinechecks"));

        page.Append("</body>\n</html>\n");

        return page.ToString();
    }

    private static string Title(MachineCheckReport report) => report.Kind == MachineCheckKind.Backlash
        ? Invariant($"Backlash check — {report.Axis} axis")
        : "Squareness check";

    private static void Facts(StringBuilder page, MachineCheckReport report)
    {
        var minutes = report.EstimatedSeconds / 60;

        page.Append("<div class=\"facts\">\n");
        Fact(page, "Holes", report.Holes.Count.ToString(CultureInfo.InvariantCulture));
        Fact(page, "Scrap needed", Invariant($"{report.StockWidthMm:F0} × {report.StockHeightMm:F0} mm"));
        Fact(page, "Pins", Invariant($"{report.PinMm:F2} mm"));
        Fact(page, "Run time", minutes < 1
            ? Invariant($"{report.EstimatedSeconds:F0} s")
            : Invariant($"{minutes:F0} min"));
        page.Append("</div>\n");
    }

    private static void Fact(StringBuilder page, string label, string value) =>
        page.Append("<div><span>").Append(Escape(label)).Append("</span><strong>")
            .Append(Escape(value)).Append("</strong></div>\n");

    private static void Warnings(StringBuilder page, MachineCheckReport report)
    {
        foreach (var warning in report.Warnings)
        {
            page.Append("<p class=\"warn\">").Append(Escape(warning)).Append("</p>\n");
        }

        foreach (var note in report.Notes)
        {
            page.Append("<p class=\"note\">").Append(Escape(note)).Append("</p>\n");
        }
    }

    /// <summary>
    /// The coupon, drawn from the same hole positions the program was emitted from — so the picture
    /// cannot describe a different experiment from the one about to be run.
    /// </summary>
    private static void Diagram(StringBuilder page, MachineCheckOptions options, MachineCheckReport report)
    {
        const double Pad = 12;
        const double Scale = 3.2;

        var width = (report.StockWidthMm * Scale) + (2 * Pad);
        var height = (report.StockHeightMm * Scale) + (2 * Pad);

        string X(double mm) => Num(Pad + (mm * Scale));

        // Y up on the coupon, down on the screen.
        string Y(double mm) => Num(height - Pad - (mm * Scale));

        page.Append("<figure class=\"shot\">\n");
        page.Append(Invariant($"<svg viewBox=\"0 0 {Num(width)} {Num(height)}\" width=\"{Num(width)}\" height=\"{Num(height)}\" role=\"img\" aria-label=\""))
            .Append(Escape(Description(report)))
            .Append("\">\n");

        page.Append(Invariant(
            $"<rect x=\"{Num(Pad)}\" y=\"{Num(Pad)}\" width=\"{Num(report.StockWidthMm * Scale)}\" height=\"{Num(report.StockHeightMm * Scale)}\" class=\"stock\"/>\n"));

        foreach (var pair in report.Pairs)
        {
            var a = report.Holes[pair.First];
            var b = report.Holes[pair.Second];

            page.Append(Invariant(
                $"<line x1=\"{X(a.XMm)}\" y1=\"{Y(a.YMm)}\" x2=\"{X(b.XMm)}\" y2=\"{Y(b.YMm)}\" class=\"span\"/>\n"));
        }

        foreach (var hole in report.Holes)
        {
            // The approach, drawn as the arrow it is: this is the whole point of the check.
            page.Append(Invariant(
                $"<line x1=\"{X(hole.FromXMm)}\" y1=\"{Y(hole.FromYMm)}\" x2=\"{X(hole.XMm)}\" y2=\"{Y(hole.YMm)}\" class=\"approach\" marker-end=\"url(#tip)\"/>\n"));

            page.Append(Invariant($"<circle cx=\"{X(hole.XMm)}\" cy=\"{Y(hole.YMm)}\" r=\"3.4\" class=\"hole\"/>\n"));
            page.Append(Invariant(
                $"<text x=\"{X(hole.XMm)}\" y=\"{Num(double.Parse(Y(hole.YMm), CultureInfo.InvariantCulture) - 7)}\" class=\"label\">{hole.Number}</text>\n"));
        }

        page.Append("<defs><marker id=\"tip\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerWidth=\"5\" markerHeight=\"5\" orient=\"auto-start-reverse\">");
        page.Append("<path d=\"M 0 1 L 9 5 L 0 9 z\" class=\"tipfill\"/></marker></defs>\n");
        page.Append("</svg>\n");

        page.Append("<figcaption>").Append(Escape(Description(report)))
            .Append(Invariant($" Work zero is the coupon's lower-left corner; the first hole sits {options.MarginMm:F0} mm in from it."))
            .Append("</figcaption>\n</figure>\n");
    }

    private static string Description(MachineCheckReport report) => report.Kind == MachineCheckKind.Backlash
        ? Invariant($"Three rows of two holes. The arrows are the approach: each hole is reached from {report.Axis} on the side its row calls for, which is the only difference between the rows.")
        : "Four holes at the corners of a square, every one approached from below-left so that backlash is taken up identically and cancels out of both diagonals.";

    private static void Before(StringBuilder page, MachineCheckOptions options, MachineCheckReport report)
    {
        page.Append("<h2>Before you run it</h2>\n<ul>\n");
        page.Append("<li>Fit the <strong>").Append(Escape(options.Tool.Name))
            .Append("</strong> &mdash; an end mill, plunged, rather than a twist drill, which wanders as it enters by about as much as this check is measuring. Use the same bit for every hole and do not touch the collet in between: a bit that moves takes the experiment with it.</li>\n");
        page.Append(Invariant(
            $"<li>Clamp a flat offcut at least <strong>{report.StockWidthMm:F0} × {report.StockHeightMm:F0} mm</strong>, and hold it down all over — a coupon that lifts mid-run reads as slack that is not there.</li>\n"));
        page.Append("<li>Zero X and Y anywhere on it, and Z on its surface. Nothing here is measured from the machine's own coordinates.</li>\n");
        page.Append(Invariant(
            $"<li>Afterwards, deburr both faces. A burr round a hole is exactly where a pin sits, and it is worth more than the difference being measured.</li>\n"));
        page.Append(Invariant(
            $"<li>Have two identical <strong>{report.PinMm:F2} mm</strong> pins ready — the shanks of two drills of that size do nicely.</li>\n"));
        page.Append("</ul>\n");
    }

    private static void Measure(StringBuilder page, MachineCheckReport report)
    {
        page.Append("<h2>Measure</h2>\n");

        page.Append(report.Kind == MachineCheckKind.Backlash
            ? "<p>Stand a pin in each hole of a row and read <strong>over the outsides</strong> of the pair. One reading per row is enough: the same pins are in every row, so their diameter is common to all three and cancels out of the comparison.</p>\n"
            : "<p>Stand a pin in each hole of a diagonal, then read <strong>over the outsides</strong> and <strong>between the insides</strong>. The average of those two is the centre-to-centre distance, with the pin diameter cancelling — which is the only way a caliper gives you a distance between two hole centres.</p>\n");

        page.Append("<p>Take each reading three times. If the three disagree by more than a few hundredths, the coupon is moving, a burr is in the way, or the pins are not seated.</p>\n");

        page.Append("<table>\n<tr><th>Pair</th><th>Should read</th><th>1st</th><th>2nd</th><th>3rd</th><th>What it means</th></tr>\n");

        foreach (var pair in report.Pairs)
        {
            var nominal = report.Kind == MachineCheckKind.Backlash
                ? Invariant($"{pair.NominalMm:F3} + one pin")
                : Invariant($"{pair.NominalMm:F3} mm");

            page.Append("<tr><td><strong>").Append(Escape(pair.Name)).Append("</strong></td><td>")
                .Append(Escape(nominal))
                .Append("</td><td class=\"blank\"></td><td class=\"blank\"></td><td class=\"blank\"></td><td class=\"tag\">")
                .Append(Escape(pair.Meaning)).Append("</td></tr>\n");
        }

        page.Append("</table>\n");
    }

    private static void Arithmetic(StringBuilder page, MachineCheckReport report)
    {
        page.Append("<h2>What the numbers come to</h2>\n");

        if (report.Kind == MachineCheckKind.Backlash)
        {
            page.Append("<p class=\"big\">backlash = ( row 2 − row 3 ) ÷ 2</p>\n");
            page.Append("<p>Rows 2 and 3 are displaced in opposite directions, so the difference between them is <em>twice</em> the slack — which is the point of measuring it this way rather than trying to catch a single hole in the wrong place.</p>\n");
            page.Append("<p>Row 1 is the control. Subtract one pin diameter from it and you have the machine's true spacing over that span; more than a few hundredths away from the nominal and the axis's steps-per-millimetre is worth a look before anything else here means much.</p>\n");
            page.Append("<h2>What to do with it</h2>\n<ul>\n");
            page.Append("<li><strong>Under 0.05 mm.</strong> Nothing. That is a machine in good order, and the reading is at the edge of what pins and calipers resolve anyway.</li>\n");
            page.Append("<li><strong>0.05 to 0.2 mm.</strong> Ordinary for a desktop mill with anti-backlash nuts. Worth knowing, and worth approaching every drilled hole from the same side so it stops mattering.</li>\n");
            page.Append("<li><strong>Above 0.2 mm.</strong> The nut wants adjusting or replacing. Two holes approached from opposite sides then land a fifth of a millimetre closer than the program asked, which is a via missing its pad.</li>\n");
            page.Append("</ul>\n");
        }
        else
        {
            page.Append("<p class=\"big\">skew = arcsin( ( one diagonal − the other ) ÷ the diagonal )</p>\n");
            page.Append(Invariant($"<p>Both diagonals should read {report.Pairs[0].NominalMm:F3} mm. A difference of 0.10 mm over that span is about 0.07°; 0.50 mm is about 0.34°, which throws the far corner of a 100 mm square out by 0.6 mm.</p>\n"));
            page.Append("<p>Which diagonal is longer says which way the error leans, and that is what you correct against.</p>\n");
            page.Append("<h2>What to do with it</h2>\n<ul>\n");
            page.Append("<li>Squareness lives in the frame, so it is <strong>fixed there, not in a file</strong>: the app reports it and never quietly bends a program to match. Nothing about alignment, levelling or approach direction touches it.</li>\n");
            page.Append("<li>It matters most on long diagonals and on work that moves between two machines. A board cut and drilled entirely on one out-of-square mill is out of square with the world and square with itself.</li>\n");
            page.Append("</ul>\n");
        }
    }

    private static void Limits(StringBuilder page, MachineCheckReport report)
    {
        page.Append("<h2>What this cannot see</h2>\n<ul>\n");
        page.Append("<li><strong>A difference under 0.1 mm is not a measurement</strong> with pins and calipers. Read it, write it down, and treat it as a ceiling rather than a figure.</li>\n");

        page.Append(report.Kind == MachineCheckKind.Backlash
            ? "<li>Backlash does not grow with distance, so a longer coupon would not help. If the rows agree, the slack is smaller than this method can see — which is the useful answer it was run for.</li>\n"
            : "<li>A skew <em>does</em> grow with distance, so a longer span sees a smaller angle. If the diagonals agree on a short square, run it again as large as the stock and the machine allow before concluding anything.</li>\n");

        page.Append("<li>Neither check says anything about Z, about tram, or about what the spindle does under load. They measure where the machine puts things, not how well it cuts.</li>\n");
        page.Append("</ul>\n");
    }

    private static string Num(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    private const string Style = """
        :root { color-scheme: light dark; --rule: #d9dce1; --muted: #5b6470; --warn: #b3261e; --tint: #eef1f5; --ink: #1c1f24; }
        @media (prefers-color-scheme: dark) {
          :root { --rule: #333941; --muted: #9aa4b2; --warn: #ff6b63; --tint: #1c2127; --ink: #e6e9ee; }
        }
        body {
          font: 15px/1.55 system-ui, -apple-system, "Segoe UI", sans-serif;
          max-width: 46rem; margin: 2rem auto; padding: 0 1.25rem;
        }
        h1 { font-size: 1.5rem; margin: 0 0 0.15rem; }
        h2 { font-size: 1.05rem; margin: 2rem 0 0.5rem; }
        .sub { color: var(--muted); margin: 0 0 1.5rem; }
        code { font-family: ui-monospace, Consolas, monospace; background: var(--tint); padding: 0.05rem 0.3rem; border-radius: 3px; }
        .facts { display: flex; flex-wrap: wrap; gap: 1.75rem; padding: 0.9rem 0; border-block: 1px solid var(--rule); }
        .facts div { display: flex; flex-direction: column; }
        .facts span { font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.04em; color: var(--muted); }
        .facts strong { font-size: 1.2rem; }
        table { border-collapse: collapse; width: 100%; }
        th, td { text-align: left; padding: 0.5rem 0.75rem 0.5rem 0; border-bottom: 1px solid var(--rule); }
        th { font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.04em; color: var(--muted); font-weight: 600; }
        .blank { border-bottom: 1px solid var(--muted); min-width: 5rem; }
        .tag { font-size: 0.7rem; color: var(--muted); }
        ul { padding-left: 1.1rem; }
        li { margin: 0.5rem 0; }
        .warn { border-left: 3px solid var(--warn); padding: 0.1rem 0 0.1rem 0.9rem; margin: 1rem 0; }
        .note { color: var(--muted); }
        .big { font-size: 1.1rem; font-weight: 600; margin: 1.2rem 0; }
        figure.shot { margin: 1.5rem 0; }
        figure.shot svg { display: block; max-width: 100%; height: auto; border: 1px solid var(--rule); background: var(--tint); }
        figcaption { font-size: 0.78rem; color: var(--muted); margin-top: 0.35rem; }
        .stock { fill: none; stroke: var(--rule); stroke-width: 1; }
        .hole { fill: var(--ink); }
        .span { stroke: var(--muted); stroke-width: 1; stroke-dasharray: 4 3; }
        .approach { stroke: var(--warn); stroke-width: 1.4; }
        .tipfill { fill: var(--warn); }
        .label { fill: var(--muted); font: 9px ui-monospace, Consolas, monospace; text-anchor: middle; }
        """;
}
