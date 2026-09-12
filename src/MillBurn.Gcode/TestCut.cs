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
/// <param name="PassCount">How many overlapping passes make up this line.</param>
/// <param name="StepoverMm">How far apart those passes step.</param>
/// <param name="IsLadder">
/// A rung of the width ladder rather than a line of the depth series: one depth, and a stepover
/// that climbs past the width the bit is believed to cut.
/// </param>
public readonly record struct TestCutLine(
    int Number,
    double DepthMm,
    double FeedMmPerMin,
    double YMm,
    double PredictedWidthMm,
    double ChipLoadNm,
    bool IsRepeat,
    int PassCount = 1,
    double StepoverMm = 0,
    bool IsLadder = false)
{
    /// <summary>
    /// How much copper a rung of the ladder will leave standing between its passes, if the bit
    /// cuts exactly what the library claims.
    ///
    /// Negative means the passes overlap and the rung comes out solid. The rung where this crosses
    /// zero is the answer the ladder exists to give.
    /// </summary>
    public double PredictedRibMm => StepoverMm - PredictedWidthMm;

    /// <summary>
    /// What a caliper will read across the whole band: the cut itself plus the ground the passes
    /// stepped over.
    ///
    /// This is the number the operator measures, and it is bigger than the cut by a constant that
    /// is known exactly, because the stepover is commanded rather than observed. Subtract it and
    /// what is left is <see cref="PredictedWidthMm"/>'s real counterpart.
    /// </summary>
    public double BandWidthMm => PredictedWidthMm + ((PassCount - 1) * StepoverMm);

    /// <summary>The part of the band that is not the cut. Exact, and the same on every line.</summary>
    public double SteppedMm => (PassCount - 1) * StepoverMm;
}

/// <summary>What to cut, and how.</summary>
public sealed record TestCutOptions
{
    /// <summary>The bit being dialled in. Its feed, plunge rate and spindle speed are the starting point.</summary>
    public required Tool Tool { get; init; }

    public TestCutKind Kind { get; init; } = TestCutKind.Depth;

    /// <summary>
    /// How many lines of the depth series to cut, before the repeat. Zero leaves the series out.
    ///
    /// The series and the <see cref="LadderRungs">ladder</see> are two different experiments that
    /// happen to share a coupon: one asks how the cut width <em>changes</em> with depth, the other
    /// asks what it <em>is</em> at one depth. Either is worth running on its own, and the ladder in
    /// particular answers the question most people actually have.
    /// </summary>
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

    /// <summary>
    /// How much deeper each line goes, for a depth series.
    ///
    /// Wide steps on purpose. The width of a V-cut is a straight line in depth, so what fixes the
    /// angle is the lever arm, and 0.02 mm steps gave a six-line series spanning 0.116 mm of width
    /// — a slope fitted through six points that all sit inside the caliper's own error. Stepping
    /// 0.05 spans nearly three tenths of a millimetre over the same six lines, and still starts
    /// shallow enough to include the depth people actually isolate at.
    /// </summary>
    public double DepthStepMm { get; init; } = 0.05;

    /// <summary>
    /// The one depth every line runs at, for a feed series — and the depth the width ladder is cut
    /// at for a depth series.
    ///
    /// One depth for the ladder, because it asks a different question from the series above it: not
    /// how width changes with depth, but exactly what the width is at the depth you actually
    /// isolate at.
    /// </summary>
    public double DepthMm { get; init; } = 0.05;

    /// <summary>How much the feed changes between lines, for a feed series.</summary>
    public double FeedStepMmPerMin { get; init; } = 50;

    /// <summary>
    /// How many overlapping passes make up one line of a depth series.
    ///
    /// **This is what makes the test measurable.** One pass of a 60° V-bit at 0.02 mm is 0.150 mm
    /// wide and at 0.12 mm it is 0.266 — the whole six-line spread is 0.116 mm, which is five
    /// divisions on a caliper that is honestly good to two of them, on features too narrow to get a
    /// jaw onto at all. Twenty passes stepped over a tenth of a millimetre put the same information
    /// on a band about two millimetres across, which a caliper can actually sit against.
    ///
    /// The stepover is commanded, so it adds a constant that is known exactly and identical on
    /// every line: the differences between lines are still exactly the differences in cut width,
    /// and a caliper's error is mostly systematic, so it cancels between them.
    ///
    /// One pass for a feed test, always. There the question is what the edge looks like, and a band
    /// of overlapping passes hides every edge but the outer two.
    /// </summary>
    public int PassesPerLine { get; init; } = 20;

    /// <summary>
    /// How far apart those passes step. Zero works one out from the shallowest line.
    ///
    /// Deliberately below the narrowest cut in the series, so the passes always overlap and the
    /// band is solid. When they do not — when ribs of copper survive between them — that is not a
    /// failure of the test but a reading from it: the bit is cutting narrower than the stepover,
    /// which is a bound on the answer got with a loupe rather than a caliper, and a far more
    /// sensitive one.
    /// </summary>
    public double StepoverMm { get; init; }

    /// <summary>
    /// How many rungs of the width ladder to cut after the depth series. Zero leaves it off.
    ///
    /// **The ladder is the part that tells you whether you are reading the rest correctly.** Every
    /// rung is the same depth and the same number of passes; what climbs is the stepover, from
    /// comfortably under the width the library claims to comfortably over it. Under, and the passes
    /// overlap and the rung is solid copper-free. Over, and they leave hairlines of copper standing
    /// between them.
    ///
    /// So the rung where the hairlines first appear <em>is</em> the width this bit actually cuts —
    /// read with a loupe, against no instrument at all, at the resolution of one rung. The bottom
    /// rung is always solid and the top rung is always ribbed, which is the deliberately-wrong
    /// example: you are not asked to judge whether something looks right in isolation, only to find
    /// where a row of samples changes.
    /// </summary>
    public int LadderRungs { get; init; } = 5;

    /// <summary>Passes in each rung. Enough to leave several ribs, which are easier to see than one.</summary>
    public int LadderPasses { get; init; } = 6;

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

        // Neither half asked for. The report is still a report — it says what is wrong rather than
        // throwing, because the dialog recalculates on every keystroke and one of those keystrokes
        // is a zero on the way to a number.
        if (lines.Count == 0)
        {
            return (string.Empty, new TestCutReport
            {
                Kind = options.Kind,
                Lines = [],
                StockWidthMm = 0,
                StockHeightMm = 0,
                EstimatedSeconds = 0,
                Warnings = ["Nothing to cut: this test has no depth series and no width ladder."],
            });
        }

        var report = new TestCutReport
        {
            Kind = options.Kind,
            Lines = lines,
            StockWidthMm = options.LineLengthMm + (2 * options.MarginMm),
            StockHeightMm = lines[^1].YMm + lines[^1].SteppedMm + options.MarginMm,
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
        var count = options.Kind == TestCutKind.Feed
            ? Math.Clamp(options.LineCount, 1, 40)
            : Math.Clamp(options.LineCount, 0, 40);

        if (count != options.LineCount)
        {
            notes.Add(Invariant($"Line count clamped to {count}."));
        }

        var passes = Passes(options);
        var stepover = Stepover(options, passes);

        var lines = new List<TestCutLine>(count + 1);
        var y = options.MarginMm;

        // A band is as tall as the ground its passes stepped over, so the spacing between lines is
        // a gap between bands rather than a distance between centres. Constant, because the
        // stepover and the pass count are: what varies from line to line is the width of the cut
        // itself, which is the whole point and is a fifth of a millimetre at most.
        var pitch = ((passes - 1) * stepover) + options.LineSpacingMm;

        for (var i = 0; i < count; i++)
        {
            lines.Add(LineAt(options, i, count, y, passes, stepover));
            y += pitch;
        }

        // The ladder goes between the series and the repeat, so the repeat stays at the far end of
        // the coupon — that is the whole of what it measures, and putting anything past it would
        // shorten the span it is testing.
        y = Ladder(options, lines, y, passes);

        // A repeat of whatever came first, which is a series line when there is a series and the
        // bottom rung when there is not. Either answers the same question: did the stock move
        // between one end of the coupon and the other. On a ladder-only coupon a tilt shifts the
        // effective depth, which shifts every width, which moves the transition — so the repeat is
        // worth as much there as it is above.
        if (options.RepeatFirstLine && lines.Count > 1)
        {
            var first = lines[0];

            lines.Add(first with
            {
                Number = lines.Count + 1,
                YMm = y,
                IsRepeat = true,
            });
        }

        Check(options, lines, notes, warnings);

        return lines;
    }

    /// <summary>
    /// The width ladder: one depth, rising stepover, and somewhere in it the truth.
    ///
    /// The rungs run from three fifths of the width the library claims to seven fifths of it, so
    /// the bottom rung is solid whatever the bit really does and the top rung is ribbed whatever it
    /// really does. Between them is a transition, and where it falls says both how wrong the
    /// library is and — the part a single sample can never tell you — which way.
    /// </summary>
    private static double Ladder(
        TestCutOptions options, List<TestCutLine> lines, double y, int seriesPasses)
    {
        var rungs = Math.Clamp(options.LadderRungs, 0, 20);

        if (options.Kind != TestCutKind.Depth || rungs < 2 || seriesPasses < 2)
        {
            return y;
        }

        var passes = Math.Clamp(options.LadderPasses, 2, 40);
        var depth = Math.Max(0, options.DepthMm);
        var believed = Nm.ToMillimetres(options.Tool.WidthAtDepth(Nm.FromMillimetres(depth)));

        if (believed <= 0)
        {
            return y;
        }

        // Spaced by fraction of the believed width rather than by an absolute step, so one rung is
        // the same proportion of an answer whatever bit is in the spindle.
        for (var i = 0; i < rungs; i++)
        {
            var fraction = 0.6 + (i * (1.4 - 0.6) / (rungs - 1));
            var stepover = Math.Round(believed * fraction, 4);

            lines.Add(new TestCutLine(
                lines.Count + 1,
                depth,
                options.Tool.FeedMmPerMin,
                y,
                believed,
                0,
                IsRepeat: false,
                passes,
                stepover,
                IsLadder: true));

            y += ((passes - 1) * stepover) + options.LineSpacingMm;
        }

        return y;
    }

    /// <summary>One pass for a feed test, whatever was asked for. See <see cref="TestCutOptions.PassesPerLine"/>.</summary>
    private static int Passes(TestCutOptions options) => options.Kind == TestCutKind.Feed
        ? 1
        : Math.Clamp(options.PassesPerLine, 1, 200);

    /// <summary>
    /// The stepover, or one worked out from the narrowest cut in the series.
    ///
    /// Three fifths of it: enough margin that the passes still overlap when the bit turns out
    /// wider than the library claims — which is the usual direction — and little enough that ribs
    /// appearing means the bit is genuinely far narrower than believed rather than slightly.
    /// </summary>
    private static double Stepover(TestCutOptions options, int passes)
    {
        if (passes <= 1)
        {
            return 0;
        }

        if (options.StepoverMm > 0)
        {
            return options.StepoverMm;
        }

        var shallowest = options.Kind == TestCutKind.Depth ? options.StartDepthMm : options.DepthMm;
        var narrowest = Nm.ToMillimetres(
            options.Tool.WidthAtDepth(Nm.FromMillimetres(Math.Max(0, shallowest))));

        return Math.Max(0.01, Math.Round(narrowest * 0.6, 3));
    }

    private static TestCutLine LineAt(
        TestCutOptions options, int index, int count, double y, int passes, double stepover)
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
            IsRepeat: false,
            passes,
            stepover);
    }

    /// <summary>Everything about this test that is worth saying before it is run.</summary>
    private static void Check(
        TestCutOptions options, List<TestCutLine> lines, List<string> notes, List<string> warnings)
    {
        var tool = options.Tool;

        // Nothing asked for. Generate says so; there is nothing here to check about it, and every
        // line below assumes there is at least one line.
        if (lines.Count == 0)
        {
            return;
        }

        // The series, not the ladder. A ladder rung sits at its own depth, so a series that cuts
        // nothing would otherwise look fine because the rungs beneath it do — and a coupon that is
        // deliberately ladder-only must not be told it cuts nothing.
        var series = lines.Where(l => !l.IsLadder).ToList();
        var deepest = lines.Max(l => l.DepthMm);

        if (deepest <= 0)
        {
            warnings.Add("Every line is at or above the surface, so this test cuts nothing.");
        }
        else if (series.Count > 0 && series.Max(l => l.DepthMm) <= 0)
        {
            warnings.Add("Every line of the depth series is at or above the surface, so it cuts nothing. The width ladder below it still does.");
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

            // The passes are cut back and forth without lifting, so a line costs its length once
            // per pass plus the stepover hops between them.
            total += 60 * options.LineLengthMm * line.PassCount / line.FeedMmPerMin;
            total += 60 * line.SteppedMm / line.FeedMmPerMin;

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
            var what = line.IsLadder
                ? Invariant($"Line {line.Number}: LADDER RUNG, stepping {line.StepoverMm:F3} mm at {line.DepthMm:F3} mm deep")
                : line.IsRepeat
                    ? Invariant($"Line {line.Number}: a repeat of line 1. Both should measure the same.")
                    : options.Kind == TestCutKind.Depth
                        ? Invariant($"Line {line.Number}: {line.DepthMm:F3} mm deep, {line.PassCount} pass(es), band predicted {line.BandWidthMm:F3} mm")
                        : Invariant($"Line {line.Number}: {line.FeedMmPerMin:F0} mm/min at {line.DepthMm:F3} mm deep");

            text.Add(Comment(what));

            if (line.IsLadder)
            {
                text.Add(Comment(line.PredictedRibMm > 0
                    ? Invariant($"  should leave {line.PredictedRibMm:F3} mm ribs if the library is right")
                    : Invariant($"  should come out solid; the passes overlap by {-line.PredictedRibMm:F3} mm")));
            }
            else if (line.PassCount > 1)
            {
                text.Add(Comment(Invariant(
                    $"  measure the band, then subtract {line.SteppedMm:F3} mm for the cut width")));
            }

            text.Add(Invariant($"G0 X{x0} Y{Mm(line.YMm)}"));
            text.Add(Invariant($"G0 Z{approach}"));
            text.Add(Invariant($"G1 Z-{Mm(line.DepthMm)} F{tool.PlungeMmPerMin}"));
            text.Add(Invariant($"G1 X{x1} F{line.FeedMmPerMin:F0}"));

            // Back and forth without lifting. Stepping over at depth is what a cleared band is, and
            // retracting between passes would cost a plunge apiece for nothing.
            for (var pass = 1; pass < line.PassCount; pass++)
            {
                text.Add(Invariant($"G1 Y{Mm(line.YMm + (pass * line.StepoverMm))}"));
                text.Add(Invariant($"G1 X{(pass % 2 == 0 ? x1 : x0)}"));
            }

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
        var series = report.Lines.Where(l => !l.IsLadder).ToList();

        yield return Invariant($"{options.Tool.Name}");
        yield return Invariant(
            $"{report.Lines.Count} lines, {options.LineLengthMm:F1} mm long, {options.LineSpacingMm:F1} mm apart.");

        if (series.Count > 0 && series[0].PassCount > 1)
        {
            yield return Invariant(
                $"Each of the {series.Count} series lines is {series[0].PassCount} passes stepping {series[0].StepoverMm:F3} mm.");
        }
        yield return Invariant(
            $"Needs {report.StockWidthMm:F1} x {report.StockHeightMm:F1} mm of bare copper.");
        yield return Invariant($"About {Math.Max(1, Math.Round(report.EstimatedSeconds))} seconds.");
        yield return string.Empty;
        yield return "Work zero is the lower-left corner of the scrap, on";
        yield return "the copper. Line 1 is nearest that corner and every";
        yield return "line runs left to right.";

        if (options.Kind == TestCutKind.Depth)
        {
            if (series.Count > 0)
            {
                yield return string.Empty;
                yield return "Lines get deeper as they go. Measure each band";
                yield return "across, subtract the stepped-over ground named in";
                yield return "the comment above it, and what is left is the width";
                yield return "that one pass of this bit cuts at that depth.";
            }

            var rungs = report.Lines.Where(l => l.IsLadder && !l.IsRepeat).ToList();

            if (rungs.Count > 1)
            {
                yield return string.Empty;
                var lead = series.Count > 0 ? "Then " : "";

                yield return Invariant(
                    $"{lead}{rungs.Count} LADDER RUNGS, all {rungs[0].DepthMm:F3} mm deep,");
                yield return Invariant(
                    $"stepping {rungs[0].StepoverMm:F3} up to {rungs[^1].StepoverMm:F3} mm.");
                yield return "The bottom one should be solid and the top one";
                yield return "should have copper left standing in it. Find the";
                yield return "first rung with copper in it: its stepover is what";
                yield return "this bit really cuts.";
            }
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
