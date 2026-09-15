using System.Globalization;
using System.Text;
using MillBurn.Core;

namespace MillBurn.Gcode;

/// <summary>
/// One program a guide page describes, by the name it is written under.
///
/// A list of these rather than one program, because a layer that needs several bits is written as one
/// file per bit, and the page is what tells somebody which file goes with which bit and in what order.
/// </summary>
public sealed record GuideProgram(string Name, string Text);

/// <summary>Facts about the job that are not in the program itself.</summary>
public sealed record DrillGuideContext
{
    public required string BoardName { get; init; }

    public required string LayerLabel { get; init; }

    /// <summary>The program this page explains, by name.</summary>
    public required string ProgramName { get; init; }

    public long BoardThicknessNm { get; init; }

    public long BreakThroughNm { get; init; }

    /// <summary>Positions the drill file listed more than once for one bit, and were drilled once.</summary>
    public int RepeatedPositions { get; init; }

    /// <summary>Anything the export wanted the operator to know.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Whether work zero is a blank's corner rather than the board's.</summary>
    public bool OnBlank { get; init; }
}

/// <summary>One bit, and the run it does.</summary>
public sealed record DrillGuideStep
{
    public required int Order { get; init; }

    public required string Bit { get; init; }

    public required int Holes { get; init; }

    /// <summary>Line in the program where this section begins, for anyone following along.</summary>
    public required int Line { get; init; }

    public required double Seconds { get; init; }

    /// <summary>The file this bit's holes are in.</summary>
    public string Program { get; init; } = string.Empty;
}

/// <summary>What the page says, available without rendering it.</summary>
public sealed record DrillGuideReport
{
    public required IReadOnlyList<DrillGuideStep> Steps { get; init; }

    public int Changes => Math.Max(0, Steps.Count - 1);

    /// <summary>How many files the bits are spread across: one per bit, or one for all of them.</summary>
    public int Files => Steps.Select(s => s.Program).Distinct(StringComparer.Ordinal).Count();

    public int Holes => Steps.Sum(s => s.Holes);

    public double Seconds => Steps.Sum(s => s.Seconds);
}

/// <summary>
/// A page explaining how to run a drilling program.
///
/// The hard part of drilling a board is not the toolpath, it is the choreography: which bit goes in
/// first, when the program stops, what goes in next, and how long you are committed to. All of that
/// is knowable at export time, and a comment buried in a <c>.nc</c> is the wrong place to read it
/// from — the person who needs it is standing at the machine with a sender open, not a text editor.
///
/// Written as **self-contained HTML**, because it has to open on any machine with no network, no
/// stylesheet beside it and nothing installed. It lands next to the program it describes.
///
/// Like the backplot, the dry run and the leveller, it is derived from **the emitted program**
/// rather than from the toolpaths that made it. A guide generated from the toolpaths would describe
/// the run somebody meant rather than the one about to happen.
/// </summary>
public static class DrillGuide
{
    /// <summary>Builds the page for one program, and the facts it is built from.</summary>
    public static (string Html, DrillGuideReport Report) Build(string program, DrillGuideContext context)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(context);

        return Build([new GuideProgram(context.ProgramName, program)], context);
    }

    /// <summary>Builds the page for a layer's files, in the order they run.</summary>
    public static (string Html, DrillGuideReport Report) Build(
        IReadOnlyList<GuideProgram> programs, DrillGuideContext context)
    {
        ArgumentNullException.ThrowIfNull(programs);
        ArgumentNullException.ThrowIfNull(context);

        var steps = new List<DrillGuideStep>();

        foreach (var program in programs)
        {
            foreach (var step in Read(program.Text))
            {
                steps.Add(step with { Order = steps.Count + 1, Program = program.Name });
            }
        }

        var report = new DrillGuideReport { Steps = steps };

        return (Render(report, context), report);
    }

    /// <summary>
    /// Splits the program at its bit labels and measures each section.
    ///
    /// **Split on the labels, not on <c>M0</c>.** It used to split on the tool stop and then zip the
    /// labels onto the sections by index, on the reasoning that the emitter writes a toolpath's
    /// label before the tool-change sequence that precedes it — so the two lists line up. They line
    /// up exactly as long as there is one stop per label, and a real board proved there need not
    /// be: a KiCad export carried 1.00000 and 1.00076 mm as separate sizes, they became separate
    /// toolpaths with the same printed name, the emitter saw no change of tool between them and
    /// emitted no stop. Four labels, three sections, and every entry after the collision named the
    /// wrong bit — the page told the operator to fit a 1.00 mm drill for fifteen 0.40 mm holes.
    ///
    /// Splitting on the labels cannot come apart that way: a section *is* whatever follows the
    /// comment that names it. Adjacent sections naming the same bit are then merged, which is what
    /// the program does anyway by not stopping between them.
    /// </summary>
    private static List<DrillGuideStep> Read(string program)
    {
        var lines = program.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var labels = new List<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().StartsWith("( Drill ", StringComparison.Ordinal))
            {
                labels.Add(i);
            }
        }

        var steps = new List<DrillGuideStep>();

        for (var k = 0; k < labels.Count; k++)
        {
            var from = labels[k];
            var to = k + 1 < labels.Count ? labels[k + 1] : lines.Length;

            if (Describe(lines[from..to], from) is not { } step)
            {
                continue;
            }

            var bit = Name(lines[from].Trim());

            // The same bit twice running is one trip to the spindle, however many toolpaths the
            // geometry took. Merging here keeps the page's count of tool changes equal to the
            // number of times the program actually stops.
            if (steps.Count > 0 && string.Equals(steps[^1].Bit, bit, StringComparison.Ordinal))
            {
                steps[^1] = steps[^1] with
                {
                    Holes = steps[^1].Holes + step.Holes,
                    Seconds = steps[^1].Seconds + step.Seconds,
                };

                continue;
            }

            steps.Add(step with { Order = steps.Count + 1, Bit = bit });
        }

        return steps;
    }

    /// <summary>"( Drill 1.70 mm [8 holes] )" becomes "Drill 1.70 mm".</summary>
    private static string Name(string comment)
    {
        var bracket = comment.IndexOf('[', StringComparison.Ordinal);
        var body = bracket > 0 ? comment[..bracket] : comment.TrimEnd(')', ' ');

        return body[1..].Trim();
    }

    private static DrillGuideStep? Describe(string[] section, int offset)
    {
        var text = string.Join("\n", section);
        var parsed = GcodeParser.Parse(text);

        // A hole is a place the program feeds below zero, however many pecks that takes.
        var holes = parsed.Moves
            .Where(m => !m.IsRapid && m.IsVertical && m.ToZNm < 0)
            .Select(m => m.From)
            .Distinct()
            .Count();

        if (holes == 0)
        {
            return null;
        }

        var measured = GcodeBackplot.Measure(GcodeBackplot.Classify(parsed));

        return new DrillGuideStep
        {
            Order = 0,

            // Replaced by the caller from the program's own labels, taken in file order. Only
            // survives when there are fewer labels than sections, and naming the wrong bit would
            // be considerably worse than declining to name one.
            Bit = "the bit already in the spindle",
            Holes = holes,
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

    private static string Render(DrillGuideReport report, DrillGuideContext context)
    {
        var page = new StringBuilder(4096);

        page.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        page.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        page.Append("<title>").Append(Escape(context.ProgramName)).Append(" — drilling</title>\n");
        page.Append("<style>\n").Append(Style).Append("</style>\n</head>\n<body>\n");

        page.Append("<h1>").Append(Escape(context.LayerLabel)).Append("</h1>\n");
        page.Append("<p class=\"sub\">").Append(Escape(context.BoardName));

        if (report.Files > 1)
        {
            page.Append(" · <strong>").Append(report.Files).Append(" files</strong>, one per bit</p>\n");
        }
        else
        {
            page.Append(" · <code>").Append(Escape(context.ProgramName)).Append("</code></p>\n");
        }

        Overview(page, report, context);
        Sequence(page, report);
        Before(page, context);

        if (context.Warnings.Count > 0 || context.RepeatedPositions > 0)
        {
            Concerns(page, context);
        }

        page.Append(GuideFooter.For(
            report.Files > 1
                ? "This page describes the files beside it; if you re-export, read it again."
                : "This page describes the program beside it; if you re-export, read it again.",
            "drilling"));

        page.Append("</body>\n</html>\n");

        return page.ToString();
    }

    private static void Overview(StringBuilder page, DrillGuideReport report, DrillGuideContext context)
    {
        var depth = context.BoardThicknessNm + context.BreakThroughNm;

        page.Append("<div class=\"facts\">\n");
        Fact(page, "Holes", report.Holes.ToString(CultureInfo.InvariantCulture));
        Fact(page, "Bits", report.Steps.Count.ToString(CultureInfo.InvariantCulture));

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

        page.Append("</div>\n");
    }

    private static void Sequence(StringBuilder page, DrillGuideReport report)
    {
        // "Suggested", as on the project page: most of the order is physics, but a board can have a
        // reason to differ, and somebody new to this must not read a suggestion as an instruction.
        page.Append(report.Files > 1 ? "<h2>Suggested order</h2>\n" : "<h2>The run, in order</h2>\n");

        if (report.Steps.Count == 0)
        {
            page.Append("<p>This program drills nothing.</p>\n");
            return;
        }

        // One file per bit: the last column names the file, which is what somebody opens in the
        // sender. A single file keeps the line each bit starts at.
        var perFile = report.Files > 1;

        page.Append("<table>\n<thead><tr><th>#</th><th>Put in the spindle</th><th>Holes</th>"
            + "<th>About</th><th>").Append(perFile ? "File" : "From line").Append("</th></tr></thead>\n<tbody>\n");

        foreach (var step in report.Steps)
        {
            page.Append("<tr><td>").Append(step.Order).Append("</td>")
                .Append("<td><strong>").Append(Escape(step.Bit)).Append("</strong></td>")
                .Append("<td>").Append(step.Holes).Append("</td>")
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
            page.Append("<p><strong>One file per bit</strong>, in the suggested order above. Before "
                + "each one, fit its bit and touch off Z. <strong>X and Y keep their zero</strong> — "
                + "leave them alone between files.</p>\n");

            page.Append("<div class=\"warn\"><p><strong>Z is the one you have to set again</strong>, "
                + "before every file and <strong>on the same spot each time</strong>. A new bit sits at "
                + "a different height in the collet, and the depths are measured from the surface of "
                + "the board rather than from the spindle.</p></div>\n");
        }
        else if (report.Changes > 0)
        {
            // Scoped to X and Y, and only to X and Y. An earlier version said the tool change
            // "does not need re-zeroing" — meaning the two axes that had not moved — immediately
            // above a warning to re-zero Z. On a page somebody reads standing at the machine, an
            // apparent contradiction is worse than saying nothing: they have to pick one.
            page.Append("<p>The program <strong>stops on its own</strong> between each of these, "
                + "with the spindle off and the tool lifted clear. Change the bit, then resume in "
                + "your sender. <strong>X and Y keep their zero</strong> — the machine has not "
                + "moved sideways and the holes stay where they are.</p>\n");

            page.Append("<div class=\"warn\"><p><strong>Z is the one you have to set again.</strong> "
                + "A new bit sits at a different height in the collet, and the program's depths are "
                + "measured from the surface of the board rather than from the spindle. Touch off Z "
                + "on the board after every bit change. It is the one part of this that cannot be "
                + "done for you.</p></div>\n");
        }
    }

    /// <summary>
    /// Where work zero is, as a list item — shared with the routing page so the two cannot drift.
    ///
    /// Both pages said "the lower-left corner of the board" unconditionally, and on a job built on a
    /// blank every program beside them is referenced to the blank's corner instead. Zeroing on the
    /// board's corner as the page said puts every hole a border's width out.
    /// </summary>
    public static string WorkZero(bool onBlank) => onBlank
        ? "<li>Work zero is the <strong>lower-left corner of the stock</strong>, not of the board "
            + "— the same as every other file in this export.</li>\n"
        : "<li>Work zero is the <strong>lower-left corner of the board</strong>, the same as every "
            + "other file in this export.</li>\n";

    private static void Before(StringBuilder page, DrillGuideContext context)
    {
        page.Append("<h2>Before you start</h2>\n<ul>\n");

        page.Append(WorkZero(context.OnBlank));

        if (context.BreakThroughNm > 0)
        {
            var through = Nm.ToMillimetreString(context.BreakThroughNm, 2);

            page.Append("<li>Every hole goes <strong>").Append(through)
                .Append(" mm past the underside</strong>, so a hole that stops in the last skin of "
                + "fibreglass does not tear on the way out. <strong>Put a sacrificial board "
                + "underneath</strong> — this will cut into whatever is there.</li>\n");
        }

        page.Append("<li>Biggest bit first. A small drill wanders when it starts on a surface a "
            + "larger one has already broken, so the usual small-to-large reasoning is backwards "
            + "here — and the delicate bits spend the least time in the spindle this way.</li>\n");

        page.Append("</ul>\n");
    }

    private static void Concerns(StringBuilder page, DrillGuideContext context)
    {
        page.Append("<h2>Worth knowing</h2>\n<ul>\n");

        if (context.RepeatedPositions > 0)
        {
            var many = context.RepeatedPositions != 1;

            page.Append("<li>The drill file listed <strong>").Append(context.RepeatedPositions)
                .Append(many ? " positions" : " position")
                .Append(" more than once</strong> for the same bit. ")
                .Append(many ? "They are" : "It is")
                .Append(" drilled once. Drilling a hole that already exists cuts nothing, and is "
                + "the way drills get grabbed and snapped.</li>\n");
        }

        foreach (var warning in context.Warnings)
        {
            page.Append("<li>").Append(Escape(warning)).Append("</li>\n");
        }

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

    /// <summary>
    /// Inlined, and deliberately plain. Public because every page this app writes shares it — one
    /// look, learned once.
    ///
    /// The page is written into whatever folder the operator picked, so there is nothing beside it
    /// to link to and no network to fetch from — it has to be one file that works on a laptop in a
    /// workshop. It follows the system's light or dark setting rather than picking one.
    /// </summary>
    public const string Style = """
        :root { color-scheme: light dark; --rule: #d9dce1; --muted: #5b6470; --warn: #b3261e; }
        @media (prefers-color-scheme: dark) {
          :root { --rule: #333941; --muted: #9aa4b2; --warn: #ff6b63; }
        }
        body {
          font: 15px/1.55 system-ui, -apple-system, "Segoe UI", sans-serif;
          max-width: 46rem; margin: 2rem auto; padding: 0 1.25rem;
        }
        h1 { font-size: 1.5rem; margin: 0 0 0.15rem; }
        h2 { font-size: 1.05rem; margin: 2rem 0 0.5rem; }
        .sub { color: var(--muted); margin: 0 0 1.5rem; }
        code { font-family: ui-monospace, Consolas, monospace; }
        .facts { display: flex; flex-wrap: wrap; gap: 1.75rem; padding: 0.9rem 0; border-block: 1px solid var(--rule); }
        .facts div { display: flex; flex-direction: column; }
        .facts span { font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.04em; color: var(--muted); }
        .facts strong { font-size: 1.2rem; }
        table { border-collapse: collapse; width: 100%; }
        th, td { text-align: left; padding: 0.5rem 0.75rem 0.5rem 0; border-bottom: 1px solid var(--rule); }
        th { font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.04em; color: var(--muted); font-weight: 600; }
        td:nth-child(3), td:nth-child(5), th:nth-child(3), th:nth-child(5) { text-align: right; }
        ul { padding-left: 1.1rem; }
        li { margin: 0.4rem 0; }
        .warn { border-left: 3px solid var(--warn); padding: 0.1rem 0 0.1rem 0.9rem; margin: 1rem 0; }
        .foot { color: var(--muted); font-size: 0.8rem; margin-top: 2.5rem; }
        """;
}
