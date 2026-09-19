using System.Globalization;
using System.Text;
using MillBurn.Core;
using MillBurn.Gcode;

namespace MillBurn.Pipeline;

/// <summary>What the project page needs to know that the files cannot say for themselves.</summary>
public sealed record ProjectPageContext
{
    public required string BoardName { get; init; }

    public required Bounds Board { get; init; }

    public long BoardThicknessNm { get; init; }

    public BlankPlan Blank { get; init; } = BlankPlan.None;

    /// <summary>
    /// The Board outline layer's bit, by name — which also cuts the blank. Null says nothing.
    /// </summary>
    public string? OutlineCutter { get; init; }

    /// <summary>Anything the export list flagged, so the page carries it too.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];
}

/// <summary>
/// One page that says what the whole export is, in the order somebody needs it.
///
/// Requested from the workshop, and the reason is worth keeping: the pieces of this are all said
/// somewhere already — in the export window, in a program's comments, on the drilling page — and
/// each of them is a different place, none of which is open when the folder is opened next week.
///
/// **Short lines.** Every sentence here is read standing up, by somebody deciding what to run next.
/// No paragraphs, no jargon, and no explaining of things the file itself explains — the drilling
/// page says which bit goes in when, and this one says the drilling page exists.
/// </summary>
public static class ProjectPage
{
    /// <summary>Builds the page for a finished plan.</summary>
    public static string Build(ExportPlan plan, ProjectPageContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        var page = new StringBuilder(8192);

        page.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        page.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        page.Append("<title>").Append(Escape(context.BoardName)).Append(" — this export</title>\n");
        page.Append("<style>\n").Append(DrillGuide.Style).Append("</style>\n</head>\n<body>\n");

        page.Append("<h1>").Append(Escape(context.BoardName)).Append("</h1>\n");
        page.Append("<p class=\"sub\">Everything in this folder, and what to do with it.</p>\n");

        Facts(page, plan, context);
        Order(page, plan, context);
        Files(page, plan);
        Zero(page, context);
        Laser(page, plan, context);
        Watch(page, plan, context);

        page.Append(GuideFooter.For(
            "It describes the files beside it; if you export again, read it again.",
            "project-page"));
        page.Append("</body>\n</html>\n");

        return page.ToString();
    }

    private static void Facts(StringBuilder page, ExportPlan plan, ProjectPageContext context)
    {
        page.Append("<div class=\"facts\">\n");
        Fact(page, "Board", Size(context.Board));

        if (context.Blank.Resolved)
        {
            Fact(page, "Stock", Size(context.Blank.Bounds));
        }

        if (context.BoardThicknessNm > 0)
        {
            Fact(page, "Thickness", Nm.ToMillimetreString(context.BoardThicknessNm, 2) + " mm");
        }

        Fact(page, "Files", plan.Count.ToString(CultureInfo.InvariantCulture));
        page.Append("</div>\n");
    }

    /// <summary>
    /// The order things happen in, which is the one thing a folder of files cannot show.
    ///
    /// **Suggested, and labelled as such.** Most of it is physical rather than preference — a blank
    /// cut second is not a datum, a slot cut after the board is free moves the board — but not all
    /// of it is, and a board can have a reason to differ that the app cannot see. Somebody meeting
    /// this for the first time should not be able to read a suggestion as an instruction.
    ///
    /// **The laser steps belong here too.** They were missed at first, which made the page read as
    /// though a mixed job were a milling job with some files left over — and a mixed job is the one
    /// where the order is hardest to guess and most expensive to get wrong.
    /// </summary>
    private static void Order(StringBuilder page, ExportPlan plan, ProjectPageContext context)
    {
        bool Any(Func<ExportItem, bool> match) => plan.Items.Any(match);

        var etch = Any(i => i.Output == OutputKind.Svg
            && i.Role is LayerRole.TopCopper or LayerRole.BottomCopper);

        var mask = Any(i => i.Output == OutputKind.Svg
            && i.Role is LayerRole.TopMask or LayerRole.BottomMask);

        var silk = Any(i => i.Output == OutputKind.Svg
            && i.Role is LayerRole.TopSilk or LayerRole.BottomSilk);

        page.Append("<h2>Suggested running order</h2>\n");
        page.Append("<p class=\"sub\">A suggestion, not an instruction. Most of it is physics — the "
            + "stock has to be cut before anything is measured from it, and the outline has to be "
            + "last because after it the board is loose — but your board may have a reason to "
            + "differ.</p>\n<ol>\n");

        var cutsBlank = context.Blank is { Resolved: true, Cut: true };

        if (context.Blank is { Resolved: true, HolesOnly: true })
        {
            page.Append("<li><strong>Put the stock in the corner stop and drill its alignment holes</strong>");

            if (context.OutlineCutter is { } holeBit)
            {
                page.Append(" with the <strong>").Append(Escape(holeBit)).Append("</strong> — the "
                    + "Board outline layer's bit");
            }

            page.Append(". The stock's edges are left as they are; its lower-left corner is work zero "
                + "for every file here.</li>\n");
        }
        else if (cutsBlank)
        {
            page.Append("<li><strong>Cut the stock to size</strong>");

            // Named, and said where it comes from: nothing next to the blank's settings picks a bit,
            // so without this the page leaves somebody standing at the machine guessing.
            if (context.OutlineCutter is { } cutter)
            {
                page.Append(" with the <strong>").Append(Escape(cutter)).Append("</strong> — the "
                    + "Board outline layer's bit");
            }

            page.Append(". Everything else is measured from its lower-left corner.</li>\n");
        }
        else if (context.Blank.Resolved)
        {
            page.Append("<li><strong>Put the stock in the corner stop.</strong> Its lower-left "
                + "corner is work zero for every file here.</li>\n");
        }

        // The laser's copper step replaces the mill's, so it comes before everything the mill does
        // to that side rather than alongside it.
        if (etch)
        {
            page.Append("<li><strong>Laser: burn the copper layer.</strong> This is the resist, not "
                + "the copper. <em>Moves to the laser.</em></li>\n");
            page.Append("<li><strong>Etch, then strip the resist.</strong> By hand; nothing here "
                + "does this part.</li>\n");
        }

        if (Any(i => i.Operation == OperationKind.Isolation))
        {
            page.Append("<li><strong>Isolate the copper.</strong>")
                .Append(etch ? " <em>Back to the mill.</em>" : string.Empty)
                .Append("</li>\n");
        }

        if (Any(i => i.Operation == OperationKind.Drilling))
        {
            page.Append("<li><strong>Drill.</strong> One file per bit, in the order the drilling page "
                + "lists them. Fit the bit and touch off Z before each file.</li>\n");
        }

        if (Any(i => i.TargetName.Contains(".slots.", StringComparison.Ordinal)))
        {
            page.Append("<li><strong>Route the slots.</strong> End mill, not a drill. One file per "
                + "cutter; the routing page lists them.</li>\n");
        }

        if (Any(i => i.Operation == OperationKind.Pocket))
        {
            page.Append("<li><strong>Mill the soldermask off the pads.</strong> After the mask is "
                + "on and cured.</li>\n");
        }

        if (mask)
        {
            page.Append("<li><strong>Laser: the soldermask openings.</strong> After the mask is on "
                + "and cured. <em>Moves to the laser.</em></li>\n");
        }

        if (silk)
        {
            page.Append("<li><strong>Laser: the silkscreen.</strong> <em>On the laser.</em></li>\n");
        }

        if (mask || silk)
        {
            page.Append("<li><strong>Back to the mill</strong>, into the same corner stop.</li>\n");
        }

        if (Any(i => i.Operation == OperationKind.Outline
            && !i.TargetName.Contains(".stock.", StringComparison.Ordinal)))
        {
            page.Append("<li><strong>Cut the board out</strong>");

            if (context.OutlineCutter is { } cutter)
            {
                page.Append(cutsBlank
                    ? " with the same bit as the stock"
                    : " with the <strong>" + Escape(cutter) + "</strong>");
            }

            page.Append(". Last: after this it is loose, so anything needing it held has to have "
                + "happened already.</li>\n");
        }

        page.Append("</ol>\n");

        if (etch || mask || silk)
        {
            page.Append("<div class=\"note\"><p>The work moves between machines here. Each time it "
                + "goes back, it goes into the same corner stop the same way round — that is what "
                + "keeps the two machines agreeing.</p></div>\n");
        }
    }

    private static void Files(StringBuilder page, ExportPlan plan)
    {
        page.Append("<h2>The files</h2>\n<table>\n<thead><tr><th>File</th><th>What it is</th>"
            + "<th>Machine</th></tr></thead>\n<tbody>\n");

        foreach (var item in plan.Items)
        {
            page.Append("<tr><td><code>").Append(Escape(item.TargetName)).Append("</code></td>")
                .Append("<td>").Append(Escape(What(item))).Append("</td>")
                .Append("<td>").Append(item.Output == OutputKind.Svg ? "Laser" : "Mill").Append("</td></tr>\n");

            if (item.Companion is { } companion)
            {
                page.Append("<tr><td><code>").Append(Escape(companion.TargetName)).Append("</code></td>")
                    .Append(item.Bit is null
                        ? "<td>Read this before running the file above.</td>"
                        : "<td>Read this first: it lists this layer's files, in order.</td>")
                    .Append("<td>—</td></tr>\n");
            }
        }

        page.Append("</tbody>\n</table>\n");
    }

    private static void Zero(StringBuilder page, ProjectPageContext context)
    {
        page.Append("<h2>Work zero</h2>\n<ul>\n");

        page.Append(context.Blank.Resolved
            ? "<li>The <strong>lower-left corner of the stock</strong>, not of the board.</li>\n"
            : "<li>The <strong>lower-left corner of the board</strong>.</li>\n");

        if (context.Blank.Resolved)
        {
            page.Append("<li>The board sits <strong>")
                .Append(Nm.ToMillimetreString(context.Board.MinX - context.Blank.Bounds.MinX, 2))
                .Append(" mm right</strong> and <strong>")
                .Append(Nm.ToMillimetreString(context.Board.MinY - context.Blank.Bounds.MinY, 2))
                .Append(" mm up</strong> from it.</li>\n");
        }

        page.Append("<li>Every file here uses the same zero.</li>\n");
        page.Append("<li><strong>Set Z again after every tool change</strong>, on the same spot each "
            + "time.</li>\n");
        page.Append("</ul>\n");
    }

    /// <summary>
    /// The one that caused this page to be asked for.
    ///
    /// The SVGs share a page, and the page is bigger than the artwork — so software that imports by
    /// the drawing's own bounds drops the border and lands everything at the origin, out by exactly
    /// that border. The numbers are stated rather than left to be worked out — and per file, because
    /// each file's drawing is a different box: copper that reaches the board edge is the board, a
    /// mask is only its outermost openings.
    /// </summary>
    private static void Laser(StringBuilder page, ExportPlan plan, ProjectPageContext context)
    {
        var svgs = plan.Items.Where(i => i.Output == OutputKind.Svg && i.Drawing is not null).ToList();

        if (svgs.Count == 0)
        {
            return;
        }

        // The page is the stock when there is one, the board when not: the same frame the planner drew in.
        var frame = context.Blank.Resolved ? context.Blank.Bounds : context.Board;
        var boardX = context.Board.MinX - frame.MinX;
        var boardY = context.Board.MinY - frame.MinY;

        page.Append("<h2>Placing the SVGs</h2>\n<ul>\n");
        page.Append("<li>They all share one page, so they line up with each other.</li>\n");
        page.Append("<li>Put the <strong>page's</strong> lower-left corner on work zero, and nothing "
            + "below needs doing.</li>\n");
        page.Append("</ul>\n");

        var placing = svgs[0].PlacingLayers;
        var stock = context.Blank.Resolved;

        // Which box the placing layers make: the stock's if its layer is in the file, the board's if
        // only the outline is — chosen in Settings › Laser by what goes against the laser's origin.
        var withStock = placing.Any(p => p.StartsWith("Stock", StringComparison.Ordinal));

        if (placing.Count > 0)
        {
            // The layers make every file's box the same, so the advice is one placement, not a lookup.
            var holes = placing.Contains("Stock and holes");

            page.Append("<div class=\"note\"><p><strong>Every SVG also carries the board outline")
                .Append(withStock ? (holes ? ", and the stock with its holes" : ", and the stock") : string.Empty)
                .Append(withStock
                    ? (holes
                        ? "</strong>, as layers of their own: the board outline in red, the stock and its holes in blue"
                        : "</strong>, as layers of their own: the board outline in red, the stock in blue")
                    : "</strong>, as a layer of its own, in red")
                .Append(". They are for placing the file, not for burning, so switch them off in the "
                    + "laser software before you burn. Because every file has them, software that "
                    + "imports the drawing rather than the page imports every file at the same size — ")
                .Append(withStock ? "the stock's" : "the board's")
                .Append(" — and centring each at half its width and height puts ")
                .Append(withStock ? "the stock's" : "the board's")
                .Append(" corner on the laser's origin")
                .Append(!withStock && stock ? ", for a board already cut out and put against a jig there" : string.Empty)
                .Append(".</p></div>\n");
        }
        else
        {
            page.Append("<div class=\"warn\"><p><strong>If your laser software imports the drawing "
                + "rather than the page</strong>, it throws the empty border away and keeps only the "
                + "drawing's own box, which is different for every file. The size it shows on import says "
                + "which it did. Then place each file by its own row: its lower-left corner at the first "
                + "pair of numbers when ")
                .Append(stock ? "the stock's corner" : "the board's corner")
                .Append(" is on the laser's origin, or its centre at the second when the ")
                .Append(stock ? "cut-out board's" : "board's")
                .Append(" corner is. One file's numbers are wrong for another — Settings › Laser can "
                    + "draw the board outline into every file to make them the same.</p></div>\n");
        }

        // The centre is measured from the corner of whatever the file imports as, because that is
        // what the operator puts on the origin: the stock's corner when the stock layer makes the
        // box, the board's otherwise. Measured from the board's corner while the box was the
        // stock's, the workshop had to work the right number out themselves.
        var from = withStock ? "the stock's" : "the board's";
        var originX = withStock ? 0 : boardX;
        var originY = withStock ? 0 : boardY;

        page.Append("<table>\n<thead><tr><th>File</th><th>Imports as</th>"
            + "<th>Lower-left, from the page's corner</th><th>Centre, from ")
            .Append(from)
            .Append(" corner</th></tr></thead>\n<tbody>\n");

        foreach (var item in svgs)
        {
            var d = item.Drawing!.Value;

            page.Append("<tr><td><code>").Append(Escape(item.TargetName)).Append("</code></td><td>")
                .Append(Nm.ToMillimetreString(d.Width, 2)).Append(" × ")
                .Append(Nm.ToMillimetreString(d.Height, 2)).Append(" mm</td><td>")
                .Append(Pair(d.MinX, d.MinY)).Append("</td><td>")
                .Append(Pair(d.Centre.X - originX, d.Centre.Y - originY)).Append("</td></tr>\n");
        }

        page.Append("</tbody>\n</table>\n");

        static string Pair(long x, long y) =>
            Nm.ToMillimetreString(x, 2) + " right, " + Nm.ToMillimetreString(y, 2) + " up";
    }

    private static void Watch(StringBuilder page, ExportPlan plan, ProjectPageContext context)
    {
        var warnings = plan.Items
            .SelectMany(i => i.Warnings)
            .Concat(context.Skipped)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (warnings.Count == 0)
        {
            return;
        }

        page.Append("<h2>Worth checking</h2>\n<ul>\n");

        foreach (var warning in warnings)
        {
            page.Append("<li>").Append(Escape(warning)).Append("</li>\n");
        }

        page.Append("</ul>\n");
    }

    private static string What(ExportItem item) => item.Bit is { } bit ? Kind(item) + " · " + bit : Kind(item);

    private static string Kind(ExportItem item) => item.TargetName switch
    {
        var n when n.Contains(".stock.", StringComparison.Ordinal) => item.Doing is null
            ? "Cuts the stock to size"
            : "Drills the stock's alignment holes",
        var n when n.Contains(".slots.", StringComparison.Ordinal) => "Slots and milled holes",
        var n when n.Contains(".dryrun.", StringComparison.Ordinal) => "The same path, in the air",
        var n when n.Contains(".levelled.", StringComparison.Ordinal) => "Bent to the probed surface",
        _ => item.LayerLabel + " · " + LayerOperations.Label(item.Operation),
    };

    private static string Size(Bounds b) =>
        Nm.ToMillimetreString(b.Width, 2) + " × " + Nm.ToMillimetreString(b.Height, 2) + " mm";

    private static void Fact(StringBuilder page, string label, string value) =>
        page.Append("<div><span>").Append(Escape(label)).Append("</span><strong>")
            .Append(Escape(value)).Append("</strong></div>\n");

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
