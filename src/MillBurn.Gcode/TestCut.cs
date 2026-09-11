using System.Globalization;
using MillBurn.Core;
using static System.FormattableString;

namespace MillBurn.Gcode;

/// <summary>Which question a test cut is asking.</summary>
public enum TestCutKind
{
    /// <summary>
    /// One line per depth, increasing. For a V-bit this is the width test: the whole point of a
    /// cone is that how deep it goes decides how wide it cuts, and that relationship is worth
    /// measuring rather than trusting.
    /// </summary>
    Depth,

    /// <summary>
    /// One line per feed, at a fixed depth. Nothing about the geometry changes; what changes is the
    /// quality of the edge, and that is a thing you can only see.
    /// </summary>
    Feed,
}

/// <summary>One line of a test cut, and what it is meant to tell the operator.</summary>
/// <param name="Number">1-based, in the order they are cut.</param>
/// <param name="DepthMm">How deep this line runs.</param>
/// <param name="FeedMmPerMin">How fast the tool crosses at that depth.</param>
/// <param name="YMm">Where it sits, so a line can be found again on the coupon.</param>
/// <param name="PredictedWidthMm">
/// What the tool model says this line will measure. The number the test exists to check.
/// </param>
/// <param name="ChipLoadNm">Advance per tooth per revolution at this feed.</param>
/// <param name="IsRepeat">
/// A duplicate of the first line, cut last. Its width should match line 1's; when it does not, the
/// stock is tilted or Z moved, and every other number on the coupon is suspect.
/// </param>
public readonly record struct TestCutLine(
    int Number,
    double DepthMm,
    double FeedMmPerMin,
    double YMm,
    double PredictedWidthMm,
    double ChipLoadNm,
    bool IsRepeat);

/// <summary>What to cut, and how.</summary>
public sealed record TestCutOptions
{
    /// <summary>The bit being dialled in. Its feed, plunge rate and spindle speed are the starting point.</summary>
    public required Tool Tool { get; init; }

    public TestCutKind Kind { get; init; } = TestCutKind.Depth;

    /// <summary>How many lines to cut, before the repeat.</summary>
    public int LineCount { get; init; } = 6;

    /// <summary>
    /// How long each line is.
    ///
    /// Fifteen millimetres is enough to measure in the middle, away from the plunge at one end and
    /// whatever the tool does as it leaves at the other. Both ends of a cut lie.
    /// </summary>
    public double LineLengthMm { get; init; } = 15;

    /// <summary>
    /// How far apart the lines sit.
    ///
    /// Far enough that a caliper jaw or a loupe can see one line without the next one in frame, and
    /// far enough that the widest cut cannot run into its neighbour.
    /// </summary>
    public double LineSpacingMm { get; init; } = 2;

    /// <summary>Depth of the first line, for a depth series.</summary>
    public double StartDepthMm { get; init; } = 0.02;

    /// <summary>How much deeper each line goes, for a depth series.</summary>
    public double DepthStepMm { get; init; } = 0.02;

    /// <summary>The one depth every line runs at, for a feed series.</summary>
    public double DepthMm { get; init; } = 0.05;

    /// <summary>How much the feed changes between lines, for a feed series.</summary>
    public double FeedStepMmPerMin { get; init; } = 50;

    /// <summary>How far in from the corner of the stock to start.</summary>
    public double MarginMm { get; init; } = 2;

    public double SafeZMm { get; init; } = 2;

    /// <summary>Where the rapid descent stops and the plunge feed takes over.</summary>
    public double ApproachZMm { get; init; } = 0.5;

    public int Decimals { get; init; } = 3;

    /// <summary>
    /// Cut the first line again at the end, at the far side of the coupon.
    ///
    /// The cheapest check there is, and the one that says whether the rest of the coupon can be
    /// believed: two identical cuts at opposite ends of the stock should measure the same. When
    /// they do not, the stock is tilted or Z moved during the run, and every width on the coupon is
    /// off by an unknown and varying amount.
    /// </summary>
    public bool RepeatFirstLine { get; init; } = true;
}

/// <summary>What the test will do, in numbers worth seeing before it runs.</summary>
public sealed record TestCutReport
{
    public required TestCutKind Kind { get; init; }

    public required IReadOnlyList<TestCutLine> Lines { get; init; }

    /// <summary>How much bare copper the test needs, including its margins.</summary>
    public required double StockWidthMm { get; init; }

    public required double StockHeightMm { get; init; }

    public required double EstimatedSeconds { get; init; }

    /// <summary>Things worth knowing. Never fatal.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Things that would spoil the test, or the tool.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Standalone programs for dialling a bit in on scrap.
///
/// Every number this app computes about a cut comes from the tool library, and every one of them is
/// a claim about a physical object: this bit, in this spindle, in this material. A V-bit's cut width
/// is derived from a tip diameter somebody typed in — and a tip diameter read off a product listing
/// has been wrong by a factor of twenty-five at least once, in units rather than in digits, with
/// nothing anywhere on screen to show it.
///
/// So: cut a few lines on a piece of scrap and measure them. The test is deliberately small, quick
/// and dull. What it produces is a number to put back into the tool library, after which every
/// isolation job on this machine is right for a reason rather than by assumption.
/// </summary>
public static class TestCut
{
    /// <summary>Builds the program and the report describing it.</summary>
    public static (string Text, TestCutReport Report) Generate(TestCutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Tool);

        var notes = new List<string>();
        var warnings = new List<string>();
        var lines = Plan(options, notes, warnings);

        var report = new TestCutReport
        {
            Kind = options.Kind,
            Lines = lines,
            StockWidthMm = options.LineLengthMm + (2 * options.MarginMm),
            StockHeightMm = lines[^1].YMm + options.MarginMm,
            EstimatedSeconds = Seconds(options, lines),
            Notes = notes,
            Warnings = warnings,
        };

        return (Emit(options, report), report);
    }

    // ------------------------------------------------------------------ what to cut

    private static List<TestCutLine> Plan(
        TestCutOptions options, List<string> notes, List<string> warnings)
    {
        var count = Math.Clamp(options.LineCount, 1, 40);

        if (count != options.LineCount)
        {
            notes.Add(Invariant($"Line count clamped to {count}."));
        }

        var lines = new List<TestCutLine>(count + 1);
        var y = options.MarginMm;

        for (var i = 0; i < count; i++)
        {
            lines.Add(LineAt(options, i, count, y));
            y += options.LineSpacingMm;
        }

        // The repeat goes last and furthest away, because the question it answers is whether
        // anything changed between one end of the coupon and the other.
        if (options.RepeatFirstLine && count > 1)
        {
            var first = lines[0];

            lines.Add(first with { Number = count + 1, YMm = y, IsRepeat = true });
        }

        Check(options, lines, notes, warnings);

        return lines;
    }

    private static TestCutLine LineAt(TestCutOptions options, int index, int count, double y)
    {
        var tool = options.Tool;

        var depth = options.Kind == TestCutKind.Depth
            ? options.StartDepthMm + (index * options.DepthStepMm)
            : options.DepthMm;

        // Centred on the tool's own feed, so the middle line is what the library already believes
        // and the operator is comparing against a baseline rather than against nothing.
        var feed = options.Kind == TestCutKind.Feed
            ? tool.FeedMmPerMin + ((index - ((count - 1) / 2.0)) * options.FeedStepMmPerMin)
            : tool.FeedMmPerMin;

        feed = Math.Max(1, feed);

        var perTooth = tool.SpindleRpm > 0 && tool.Flutes > 0
            ? feed * Nm.PerMillimetre / (tool.SpindleRpm * (double)tool.Flutes)
            : 0;

        return new TestCutLine(
            index + 1,
            depth,
            feed,
            y,
            Nm.ToMillimetres(tool.WidthAtDepth(Nm.FromMillimetres(depth))),
            perTooth,
            IsRepeat: false);
    }

    /// <summary>Everything about this test that is worth saying before it is run.</summary>
    private static void Check(
        TestCutOptions options, List<TestCutLine> lines, List<string> notes, List<string> warnings)
    {
        var tool = options.Tool;
        var deepest = lines.Max(l => l.DepthMm);

        if (deepest <= 0)
        {
            warnings.Add("Every line is at or above the surface, so this test cuts nothing.");
        }

        if (tool.MaxDepthNm > 0 && Nm.FromMillimetres(deepest) > tool.MaxDepthNm)
        {
            warnings.Add(Invariant(
                $"The deepest line is {deepest:F3} mm and this tool's cone ends at {Nm.ToMillimetres(tool.MaxDepthNm):F3} mm. Past that it is a straight shank, not a V, and the width stops following the model."));
        }

        // A surface test on copper-clad. Anything approaching the substrate is a different
        // experiment and a good way to lose a fine tip.
        if (deepest > 0.3)
        {
            notes.Add(Invariant(
                $"The deepest line is {deepest:F3} mm. Copper foil is about 0.035 mm, so anything past that is cutting fibreglass — which is what you want for a depth series and hard on a fine tip."));
        }

        if (options.Kind == TestCutKind.Feed)
        {
            var slowest = lines.Min(l => l.ChipLoadNm);
            var fastest = lines.Max(l => l.ChipLoadNm);

            if (slowest > 0 && slowest < ToolAdvice.RubbingBelowNm)
            {
                notes.Add(Invariant(
                    $"The slowest line is {slowest / 1000:F1} µm per tooth, which is rubbing rather than cutting. That is deliberate here — it is the end of the range you are looking for."));
            }

            if (tool.Kind != ToolKind.VBit && fastest > ToolAdvice.SnappingAboveNm(tool.DiameterNm))
            {
                warnings.Add(Invariant(
                    $"The fastest line is {fastest / 1000:F1} µm per tooth, which is above what a {Nm.ToMillimetres(tool.DiameterNm):F2} mm cutter should be asked for. Drop the feed step, or expect to lose the bit."));
            }
        }

        if (tool.Kind != ToolKind.VBit && options.Kind == TestCutKind.Depth)
        {
            notes.Add("This tool cuts one width at any depth, so a depth series will not change the width. What it does show is where the finish falls apart and where the bit starts to complain.");
        }
    }

    private static double Seconds(TestCutOptions options, List<TestCutLine> lines)
    {
        var plunge = Math.Max(1, options.Tool.PlungeMmPerMin);
        var total = 0.0;

        foreach (var line in lines)
        {
            total += 60 * (options.ApproachZMm + line.DepthMm) / plunge;
            total += 60 * options.LineLengthMm / line.FeedMmPerMin;

            // Rapids and the retract, roughly. Not worth modelling properly for a 30-second job.
            total += 2;
        }

        return total;
    }

    // ------------------------------------------------------------------ the program

    private static string Emit(TestCutOptions options, TestCutReport report)
    {
        var tool = options.Tool;
        var d = Math.Clamp(options.Decimals, 2, 5);

        string Mm(double value) => value.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        var safe = Mm(options.SafeZMm);
        var approach = Mm(options.ApproachZMm);
        var x0 = Mm(options.MarginMm);
        var x1 = Mm(options.MarginMm + options.LineLengthMm);

        var text = new List<string> { Rule() };

        foreach (var line in Header(options, report))
        {
            text.Add(Boxed(line));
        }

        text.Add(Rule());
        text.Add("G21 G90 G94");
        text.Add("G17");
        text.Add(Invariant($"G0 Z{safe}"));
        text.Add(Invariant($"M3 S{tool.SpindleRpm}"));
        text.Add(string.Empty);

        foreach (var line in report.Lines)
        {
            var what = line.IsRepeat
                ? Invariant($"Line {line.Number}: a repeat of line 1. Both should measure the same.")
                : options.Kind == TestCutKind.Depth
                    ? Invariant($"Line {line.Number}: {line.DepthMm:F3} mm deep, predicted {line.PredictedWidthMm:F3} mm wide")
                    : Invariant($"Line {line.Number}: {line.FeedMmPerMin:F0} mm/min at {line.DepthMm:F3} mm deep");

            text.Add(Comment(what));
            text.Add(Invariant($"G0 X{x0} Y{Mm(line.YMm)}"));
            text.Add(Invariant($"G0 Z{approach}"));
            text.Add(Invariant($"G1 Z-{Mm(line.DepthMm)} F{tool.PlungeMmPerMin}"));
            text.Add(Invariant($"G1 X{x1} F{line.FeedMmPerMin:F0}"));
            text.Add(Invariant($"G0 Z{safe}"));
            text.Add(string.Empty);
        }

        text.Add("M5");
        text.Add("G0 X0 Y0");
        text.Add("M30");

        return string.Join("\n", text);
    }

    private static IEnumerable<string> Header(TestCutOptions options, TestCutReport report)
    {
        var kind = options.Kind == TestCutKind.Depth ? "DEPTH" : "FEED";

        yield return Invariant($"{kind} TEST CUT. Scrap copper-clad only.");
        yield return string.Empty;
        yield return Invariant($"{options.Tool.Name}");
        yield return Invariant(
            $"{report.Lines.Count} lines, {options.LineLengthMm:F1} mm long, {options.LineSpacingMm:F1} mm apart.");
        yield return Invariant(
            $"Needs {report.StockWidthMm:F1} x {report.StockHeightMm:F1} mm of bare copper.");
        yield return Invariant($"About {Math.Max(1, Math.Round(report.EstimatedSeconds))} seconds.");
        yield return string.Empty;
        yield return "Work zero is the lower-left corner of the scrap, on";
        yield return "the copper. Line 1 is nearest that corner and every";
        yield return "line runs left to right.";

        if (options.Kind == TestCutKind.Depth)
        {
            yield return string.Empty;
            yield return "Lines get deeper as they go. Measure the width of";
            yield return "each one and compare it with the predicted width in";
            yield return "the comment above it.";
        }
        else
        {
            yield return string.Empty;
            yield return "Every line is the same depth and a different feed.";
            yield return "Look at the edges, not the widths: you are choosing";
            yield return "the cleanest one.";
        }

        if (options.RepeatFirstLine && report.Lines.Count > 1)
        {
            yield return string.Empty;
            yield return "The last line repeats the first. If they do not";
            yield return "measure the same, the stock is tilted or Z moved,";
            yield return "and nothing else here can be trusted.";
        }
    }

    // ------------------------------------------------------------------ formatting

    private const int BoxWidth = 56;

    private static string Rule() => "( " + new string('*', BoxWidth) + " )";

    /// <summary>
    /// One line of the header box, with brackets made impossible.
    ///
    /// A G-code comment runs to its closing bracket, so an innocent "minute(s)" ends it early and
    /// leaves the rest of the sentence to be read as code; LinuxCNC rejects the nested opening
    /// bracket outright. Easier to make that impossible here than to remember it every time.
    /// </summary>
    private static string Boxed(string text) =>
        "( " + Safe(text).PadRight(BoxWidth) + " )";

    private static string Comment(string text) => "( " + Safe(text) + " )";

    private static string Safe(string text) =>
        text.Replace('(', '[').Replace(')', ']');
}
