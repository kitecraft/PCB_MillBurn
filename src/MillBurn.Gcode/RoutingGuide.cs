using System.Globalization;
using System.Text;
using MillBurn.Core;

namespace MillBurn.Gcode;

/// <summary>What the routing page needs that the program itself cannot say.</summary>
public sealed record RoutingGuideContext
{
    public required string BoardName { get; init; }

    public required string LayerLabel { get; init; }

    /// <summary>File name of the program this page is about.</summary>
    public required string ProgramName { get; init; }

    public long BoardThicknessNm { get; init; }

    public long BreakThroughNm { get; init; }

    /// <summary>
    /// Features this program will **not** make, already worded.
    ///
    /// The most important thing on the page, and the only thing on it that cannot be recovered from
    /// the program — a file cannot describe what is absent from it. The export window says this
    /// too, but the export window is not what anybody has open while standing at the machine.
    /// </summary>
    public IReadOnlyList<string> Refusals { get; init; } = [];

    /// <summary>
    /// How many individual features those refusals cover.
    ///
    /// Separate from the list's length because the refusals are grouped by width: "4 slots 0.60 mm
    /// wide" is one line and four missing features, and the headline number has to be the one the
    /// board is short of.
    /// </summary>
    public int RefusedCount { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>One cutter's worth of routing.</summary>
public sealed record RoutingGuideStep
{
    public required int Order { get; init; }

    /// <summary>The end mill to fit, as the program named it.</summary>
    public required string Cutter { get; init; }

    /// <summary>What it makes — "3 slots 1.00 mm wide", "8 holes 1.70 mm across".</summary>
    public required string What { get; init; }

    public required int Line { get; init; }

    public required double Seconds { get; init; }
}

/// <summary>What a routing program does, in the order it does it.</summary>
public sealed record RoutingGuideReport
{
    public required IReadOnlyList<RoutingGuideStep> Steps { get; init; }

    public int Changes => Math.Max(0, Steps.Count - 1);

    public double Seconds => Steps.Sum(s => s.Seconds);
}

/// <summary>
/// A page explaining how to run a routing program — the slots, and the holes too big to drill.
///
/// The drilling program has had one of these since Phase 5; the routing program had nothing, which
/// is the worse gap of the two. Drilling is familiar: fit a bit, touch off, go. Routing is the file
/// that looks wrong if you have not been told what it is doing — the tool descends while it is
/// moving instead of plunging, it goes round a slot rather than down the middle of it, and some of
/// the features on the board are deliberately missing from it.
///
/// That last one is the reason this exists. A program cannot describe what is absent from it, so
/// the four slots that had no cutter narrow enough are invisible in the <c>.nc</c> and invisible on
/// the machine — right up until the connector will not fit. They are named here, on the page that
/// travels with the file.
///
/// Like the drilling guide, it is built from **the emitted program**: a page made from the
/// toolpaths would describe the run somebody meant rather than the one about to happen.
/// </summary>
public static class RoutingGuide
{
    /// <summary>Builds the page, and the facts it is built from.</summary>
    public static (string Html, RoutingGuideReport Report) Build(string program, RoutingGuideContext context)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(context);

        var report = new RoutingGuideReport { Steps = Read(program) };

        return (Render(report, context), report);
    }

    /// <summary>
    /// Splits the program at its tool changes and measures each section.
    ///
    /// The same <c>M0</c> boundary the drilling guide uses, for the same reason: an honest time per
    /// cutter is the number that decides whether this is something to start now.
    /// </summary>
    private static List<RoutingGuideStep> Read(string program)
    {
        var lines = program.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var steps = new List<RoutingGuideStep>();
        var start = 0;

        for (var i = 0; i <= lines.Length; i++)
        {
            if (i != lines.Length && Code(lines[i]) != "M0")
            {
                continue;
            }

            var section = lines[start..Math.Min(i + 1, lines.Length)];

            if (Describe(section, start) is { } step)
            {
                steps.Add(step with { Order = steps.Count + 1 });
            }

            start = i + 1;
        }

        return steps;
    }

    /// <summary>
    /// What one section makes, and how long it takes.
    ///
    /// The cutter's name and the feature count come out of the program's own comments, because the
    /// operation put them there for exactly this purpose: <c>( 3 slots 1.00 mm wide, cut with the
    /// 1.0 mm end mill. )</c>. Reading them back is the same discipline as reading the bits out of
    /// a drilling program — the file is the source, not the toolpath that made it.
    /// </summary>
    private static RoutingGuideStep? Describe(string[] section, int offset)
    {
        var text = string.Join("\n", section);
        var parsed = GcodeParser.Parse(text);

        if (!parsed.Moves.Any(m => !m.IsRapid && m.ToZNm < 0))
        {
            return null;
        }

        var cutter = "the cutter already in the spindle";
        var what = "routing";

        foreach (var line in section.Select(l => l.Trim()))
        {
            const string marker = ", cut with the ";
            var at = line.IndexOf(marker, StringComparison.Ordinal);

            if (at < 0 || !line.StartsWith('('))
            {
                continue;
            }

            what = line[1..at].Trim();
            cutter = line[(at + marker.Length)..].TrimEnd(')', ' ', '.').Trim();
            break;
        }

        var measured = GcodeBackplot.Measure(GcodeBackplot.Classify(parsed));

        return new RoutingGuideStep
        {
            Order = 0,
            Cutter = cutter,
            What = what,
            Line = offset + 1,
            Seconds = measured.PessimisticTime.TotalSeconds,
        };
    }

    private static string Code(string line)
    {
        var at = line.IndexOf('(', StringComparison.Ordinal);
        return (at >= 0 ? line[..at] : line).Trim();
    }

    // ------------------------------------------------------------------ the page

    private static string Render(RoutingGuideReport report, RoutingGuideContext context)
    {
        var page = new StringBuilder(4096);

        page.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        page.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        page.Append("<title>").Append(Escape(context.ProgramName)).Append(" — routing</title>\n");
        page.Append("<style>\n").Append(DrillGuide.Style).Append("</style>\n</head>\n<body>\n");

        page.Append("<h1>").Append(Escape(context.LayerLabel)).Append(" — routed features</h1>\n");
        page.Append("<p class=\"sub\">")
            .Append(Escape(context.BoardName))
            .Append(" · <code>").Append(Escape(context.ProgramName)).Append("</code></p>\n");

        Overview(page, report, context);

        // Before the sequence, because it changes whether you run the file at all.
        if (context.Refusals.Count > 0)
        {
            NotCut(page, context);
        }

        Sequence(page, report);
        Before(page, context);

        if (context.Warnings.Count > 0)
        {
            page.Append("<h2>Worth knowing</h2>\n<ul>\n");

            foreach (var warning in context.Warnings)
            {
                page.Append("<li>").Append(Escape(warning)).Append("</li>\n");
            }

            page.Append("</ul>\n");
        }

        page.Append("<p class=\"foot\">Generated by PCB_MillBurn. This page describes the program "
            + "beside it; if you re-export, read it again.</p>\n");
        page.Append("</body>\n</html>\n");

        return page.ToString();
    }

    private static void Overview(StringBuilder page, RoutingGuideReport report, RoutingGuideContext context)
    {
        var depth = context.BoardThicknessNm + context.BreakThroughNm;

        page.Append("<div class=\"facts\">\n");
        Fact(page, "Cutters", report.Steps.Count.ToString(CultureInfo.InvariantCulture));
        Fact(page, "Tool changes", report.Changes.ToString(CultureInfo.InvariantCulture));
        Fact(page, "Time", Duration(report.Seconds));

        if (depth > 0)
        {
            Fact(page, "Depth", Nm.ToMillimetreString(depth, 2) + " mm");
        }

        if (context.Refusals.Count > 0)
        {
            Fact(page, "Not cut", Math.Max(context.RefusedCount, context.Refusals.Count)
                .ToString(CultureInfo.InvariantCulture));
        }

        page.Append("</div>\n");
    }

    /// <summary>
    /// The features this program leaves out, and why.
    ///
    /// Near the top, because it is the one thing here that cannot be discovered by running the file
    /// and looking at the board — the board will simply be missing something, and the first symptom
    /// is a part that does not fit.
    /// </summary>
    private static void NotCut(StringBuilder page, RoutingGuideContext context)
    {
        page.Append("<div class=\"warn\">\n<p><strong>Some features are not in this program.</strong> "
            + "Nothing in your tool library can make them, and cutting them with the wrong cutter "
            + "would be worse than not cutting them at all — an 0.8 mm cutter in a 0.6 mm slot puts "
            + "a hole through whatever is next to it.</p>\n<ul>\n");

        foreach (var refusal in context.Refusals)
        {
            page.Append("<li>").Append(Escape(refusal)).Append("</li>\n");
        }

        page.Append("</ul>\n<p>Add a cutter that fits to <em>Edit &rsaquo; Tool library</em> and "
            + "export again, or make these by hand. They are drawn on the board either way, so the "
            + "picture will not tell you they are missing.</p>\n</div>\n");
    }

    private static void Sequence(StringBuilder page, RoutingGuideReport report)
    {
        page.Append("<h2>The run, in order</h2>\n");

        if (report.Steps.Count == 0)
        {
            page.Append("<p>This program cuts nothing.</p>\n");
            return;
        }

        page.Append("<table>\n<thead><tr><th>#</th><th>Put in the spindle</th><th>Makes</th>"
            + "<th>About</th><th>From line</th></tr></thead>\n<tbody>\n");

        foreach (var step in report.Steps)
        {
            page.Append("<tr><td>").Append(step.Order).Append("</td>")
                .Append("<td><strong>").Append(Escape(step.Cutter)).Append("</strong></td>")
                .Append("<td>").Append(Escape(step.What)).Append("</td>")
                .Append("<td>").Append(Duration(step.Seconds)).Append("</td>")
                .Append("<td>").Append(step.Line).Append("</td></tr>\n");
        }

        page.Append("</tbody>\n</table>\n");

        if (report.Changes > 0)
        {
            page.Append("<p>The program <strong>stops on its own</strong> between each of these, "
                + "with the spindle off and the tool lifted clear. Change the cutter, then resume in "
                + "your sender. <strong>X and Y keep their zero</strong> — the machine has not moved "
                + "sideways.</p>\n");

            page.Append("<div class=\"warn\"><p><strong>Z is the one you have to set again.</strong> "
                + "A new cutter sits at a different height in the collet, and the depths in this "
                + "program are measured from the surface of the board. Touch off Z on the board "
                + "after every change, <strong>on the same spot you used before</strong>.</p></div>\n");
        }
    }

    private static void Before(StringBuilder page, RoutingGuideContext context)
    {
        page.Append("<h2>Before you start</h2>\n<ul>\n");

        page.Append("<li><strong>This is an end mill, not a drill.</strong> These features cannot be "
            + "made by a drill — a twist drill cuts on its point and will not travel sideways — so "
            + "they are routed in their own file rather than folded into the drilling program.</li>\n");

        page.Append("<li><strong>It descends while it is moving, not straight down.</strong> Each "
            + "depth step ramps along the cut, because an end mill driven vertically into FR4 at "
            + "full depth is how small cutters break. In a preview this looks like the tool sinking "
            + "gradually rather than plunging; that is correct.</li>\n");

        page.Append("<li>Work zero is the <strong>lower-left corner of the board</strong>, the same "
            + "as every other file in this export.</li>\n");

        page.Append("<li><strong>Run this after drilling and before the outline.</strong> The board "
            + "is still held to the stock at that point, and a slot cut after the board is free "
            + "moves the board instead of cutting it.</li>\n");

        if (context.BreakThroughNm > 0)
        {
            var through = Nm.ToMillimetreString(context.BreakThroughNm, 2);

            page.Append("<li>Everything here goes <strong>").Append(through)
                .Append(" mm past the underside</strong>, so the slug actually comes out. "
                + "<strong>Put a sacrificial board underneath</strong> — this will cut into "
                + "whatever is there.</li>\n");
        }

        page.Append("<li>Clear the chips. A slot is a closed pocket until it breaks through, and a "
            + "cutter re-cutting its own swarf is the other way small end mills die.</li>\n");

        page.Append("</ul>\n");
    }

    private static void Fact(StringBuilder page, string label, string value) =>
        page.Append("<div><span>").Append(Escape(label)).Append("</span><strong>")
            .Append(Escape(value)).Append("</strong></div>\n");

    private static string Duration(double seconds) => seconds switch
    {
        < 90 => Invariant($"{seconds:F0} sec"),
        < 3600 => Invariant($"{seconds / 60:F0} min"),
        _ => Invariant($"{seconds / 3600:F0} h {seconds % 3600 / 60:F0} min"),
    };

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
