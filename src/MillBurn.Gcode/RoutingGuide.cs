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

    /// <summary>Whether work zero is a blank's corner rather than the board's.</summary>
    public bool OnBlank { get; init; }
}

/// <summary>One cutter's worth of routing.</summary>
public sealed record RoutingGuideStep
{
    public required int Order { get; init; }

    /// <summary>The end mill to fit, as the program named it.</summary>
    public required string Cutter { get; init; }

    /// <summary>
    /// What it makes, one entry per feature the program cuts with it — "3 slots 1.00 mm wide",
    /// "1 hole 2.20 mm across", "1 hole 2.50 mm across".
    /// </summary>
    public required IReadOnlyList<string> Makes { get; init; }

    public required int Line { get; init; }

    public required double Seconds { get; init; }

    /// <summary>The file this cutter's work is in.</summary>
    public string Program { get; init; } = string.Empty;
}

/// <summary>What a routing program does, in the order it does it.</summary>
public sealed record RoutingGuideReport
{
    public required IReadOnlyList<RoutingGuideStep> Steps { get; init; }

    public int Changes => Math.Max(0, Steps.Count - 1);

    /// <summary>How many files the cutters are spread across: one per cutter, or one for all.</summary>
    public int Files => Steps.Select(s => s.Program).Distinct(StringComparer.Ordinal).Count();

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
    /// <summary>Builds the page for one program, and the facts it is built from.</summary>
    public static (string Html, RoutingGuideReport Report) Build(string program, RoutingGuideContext context)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(context);

        return Build([new GuideProgram(context.ProgramName, program)], context);
    }

    /// <summary>Builds the page for a layer's routing files, in the order they run.</summary>
    public static (string Html, RoutingGuideReport Report) Build(
        IReadOnlyList<GuideProgram> programs, RoutingGuideContext context)
    {
        ArgumentNullException.ThrowIfNull(programs);
        ArgumentNullException.ThrowIfNull(context);

        var steps = new List<RoutingGuideStep>();

        foreach (var program in programs)
        {
            foreach (var step in Read(program.Text))
            {
                steps.Add(step with { Order = steps.Count + 1, Program = program.Name });
            }
        }

        var report = new RoutingGuideReport { Steps = steps };

        return (Render(report, context), report);
    }

    /// <summary>
    /// Splits the program into cutters, and each cutter into what it makes.
    ///
    /// A step ends where the program stops for a tool change — but a feature's heading is written
    /// *before* its tool change, not after it. Splitting at the <c>M0</c> alone handed each new
    /// cutter's first feature to the cutter before it, and reading one comment per step dropped
    /// every feature after the first. On a board with six holes milled by one end mill, the page
    /// said "1 hole" beside a program that mills six, and named the wrong size for the one it did
    /// list.
    ///
    /// So the program is cut into features first, at their headings, and a feature starts a new
    /// step only if it holds a stop. The feature count and the cutter come out of the program's own
    /// comments — <c>( 3 slots 1.00 mm wide, cut with the 1.0 mm end mill. )</c> — which the
    /// operation wrote for exactly this: the file is the source, not the toolpath that made it.
    /// </summary>
    private static List<RoutingGuideStep> Read(string program)
    {
        var lines = program.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var starts = new List<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (Feature(lines[i]) is null)
            {
                continue;
            }

            // The heading above it — "( Plated holes — 2.20 mm )" — is part of the same feature.
            var heading = i > 0 && lines[i - 1].TrimStart().StartsWith('(') && Feature(lines[i - 1]) is null;
            starts.Add(heading ? i - 1 : i);
        }

        if (starts.Count == 0)
        {
            return Seconds(lines) is { } whole
                ? [new RoutingGuideStep
                {
                    Order = 1,
                    Cutter = "the cutter already in the spindle",
                    Makes = ["routing"],
                    Line = 1,
                    Seconds = whole,
                }]
                : [];
        }

        // Each step is the features from one that holds a stop up to the next that does. The first
        // starts at the top of the file, so its time includes the preamble.
        var groups = new List<List<int>>();

        for (var k = 0; k < starts.Count; k++)
        {
            var end = k + 1 < starts.Count ? starts[k + 1] : lines.Length;
            var stops = lines[starts[k]..end].Any(l => Code(l) == "M0");

            if (groups.Count == 0 || stops)
            {
                groups.Add([]);
            }

            groups[^1].Add(k);
        }

        var steps = new List<RoutingGuideStep>();

        for (var g = 0; g < groups.Count; g++)
        {
            var from = g == 0 ? 0 : starts[groups[g][0]];
            var to = g + 1 < groups.Count ? starts[groups[g + 1][0]] : lines.Length;

            if (Seconds(lines[from..to]) is not { } seconds)
            {
                continue;
            }

            var features = groups[g]
                .Select(k => Feature(lines.Skip(starts[k]).First(l => Feature(l) is not null))!.Value)
                .ToList();

            steps.Add(new RoutingGuideStep
            {
                Order = steps.Count + 1,
                Cutter = features[0].Cutter,
                Makes = [.. features.Select(f => f.What)],
                Line = starts[groups[g][0]] + 1,
                Seconds = seconds,
            });
        }

        return steps;
    }

    /// <summary>
    /// "( 3 slots 1.00 mm wide, cut with the 1.0 mm end mill. )" as what it makes and what with, or
    /// null for any other line.
    /// </summary>
    private static (string What, string Cutter)? Feature(string line)
    {
        const string marker = ", cut with the ";

        var trimmed = line.Trim();
        var at = trimmed.IndexOf(marker, StringComparison.Ordinal);

        return at < 0 || !trimmed.StartsWith('(')
            ? null
            : (trimmed[1..at].Trim(), trimmed[(at + marker.Length)..].TrimEnd(')', ' ', '.').Trim());
    }

    /// <summary>How long a stretch of the program takes, or null if it cuts nothing.</summary>
    private static double? Seconds(string[] section)
    {
        var parsed = GcodeParser.Parse(string.Join("\n", section));

        if (!parsed.Moves.Any(m => !m.IsRapid && m.ToZNm < 0))
        {
            return null;
        }

        return GcodeBackplot.Measure(GcodeBackplot.Classify(parsed)).PessimisticTime.TotalSeconds;
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
        page.Append("<p class=\"sub\">").Append(Escape(context.BoardName));

        if (report.Files > 1)
        {
            page.Append(" · <strong>").Append(report.Files).Append(" files</strong>, one per cutter</p>\n");
        }
        else
        {
            page.Append(" · <code>").Append(Escape(context.ProgramName)).Append("</code></p>\n");
        }

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

        page.Append(GuideFooter.For(
            report.Files > 1
                ? "This page describes the files beside it; if you re-export, read it again."
                : "This page describes the program beside it; if you re-export, read it again.",
            "drills"));

        page.Append("</body>\n</html>\n");

        return page.ToString();
    }

    private static void Overview(StringBuilder page, RoutingGuideReport report, RoutingGuideContext context)
    {
        var depth = context.BoardThicknessNm + context.BreakThroughNm;

        page.Append("<div class=\"facts\">\n");
        Fact(page, "Cutters", report.Steps.Count.ToString(CultureInfo.InvariantCulture));

        if (report.Files > 1)
        {
            Fact(page, "Files", report.Files.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            Fact(page, "Tool changes", report.Changes.ToString(CultureInfo.InvariantCulture));
        }
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
        page.Append(report.Files > 1 ? "<h2>Suggested order</h2>\n" : "<h2>The run, in order</h2>\n");

        if (report.Steps.Count == 0)
        {
            page.Append("<p>This program cuts nothing.</p>\n");
            return;
        }

        var perFile = report.Files > 1;

        page.Append("<table>\n<thead><tr><th>#</th><th>Put in the spindle</th><th>Makes</th>"
            + "<th>About</th><th>").Append(perFile ? "File" : "From line").Append("</th></tr></thead>\n<tbody>\n");

        foreach (var step in report.Steps)
        {
            page.Append("<tr><td>").Append(step.Order).Append("</td>")
                .Append("<td><strong>").Append(Escape(step.Cutter)).Append("</strong></td>")
                .Append("<td>").Append(string.Join("<br>", step.Makes.Select(Escape))).Append("</td>")
                .Append("<td>").Append(Duration(step.Seconds)).Append("</td>")
                .Append("<td>");

            if (perFile)
            {
                page.Append("<code>").Append(Escape(step.Program)).Append("</code>");
            }
            else
            {
                page.Append(step.Line);
            }

            page.Append("</td></tr>\n");
        }

        page.Append("</tbody>\n</table>\n");

        if (perFile)
        {
            page.Append("<p><strong>One file per cutter</strong>, in the suggested order above. Before "
                + "each one, fit its cutter and touch off Z. <strong>X and Y keep their zero</strong> — "
                + "leave them alone between files.</p>\n");

            page.Append("<div class=\"warn\"><p><strong>Z is the one you have to set again</strong>, "
                + "before every file and <strong>on the same spot each time</strong>. A new cutter sits "
                + "at a different height in the collet, and the depths are measured from the surface "
                + "of the board.</p></div>\n");
        }
        else if (report.Changes > 0)
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

        page.Append(DrillGuide.WorkZero(context.OnBlank));

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
