using System.Globalization;
using MillBurn.Core;
using static System.FormattableString;

namespace MillBurn.Gcode;

/// <summary>Which question a machine check is asking.</summary>
public enum MachineCheckKind
{
    /// <summary>
    /// How much slack is in one axis: three rows of two holes, differing only in which side each
    /// hole was approached from.
    /// </summary>
    Backlash,

    /// <summary>
    /// Whether X and Y are at ninety degrees: four holes at a square's corners, every one
    /// approached the same way so that slack cancels and only the angle is left.
    /// </summary>
    Squareness,
}

/// <summary>The axis a backlash check measures.</summary>
public enum CheckAxis
{
    X,
    Y,
}

/// <summary>
/// One plunged hole in a check, and the move that positions it.
/// </summary>
/// <param name="Number">1-based, in the order they are drilled.</param>
/// <param name="Label">What the operator calls it: "row 2, left" or "A".</param>
/// <param name="XMm">Where it goes.</param>
/// <param name="YMm">And in Y.</param>
/// <param name="FromXMm">Where the positioning move starts, which is what decides the slack.</param>
/// <param name="FromYMm">And in Y.</param>
public readonly record struct CheckHole(
    int Number, string Label, double XMm, double YMm, double FromXMm, double FromYMm);

/// <summary>A pair of holes whose spacing is a reading the operator writes down.</summary>
/// <param name="Name">"Row 1", "A–C".</param>
/// <param name="Meaning">What this pair's number means once it is measured.</param>
/// <param name="First">Index into the report's holes.</param>
/// <param name="Second">The other one.</param>
/// <param name="NominalMm">What it should read, centre to centre, if the machine were perfect.</param>
public readonly record struct CheckPair(string Name, string Meaning, int First, int Second, double NominalMm);

/// <summary>What to check, and how.</summary>
public sealed record MachineCheckOptions
{
    /// <summary>The end mill that plunges the holes. Its plunge feed and spindle speed are used.</summary>
    public required Tool Tool { get; init; }

    public MachineCheckKind Kind { get; init; } = MachineCheckKind.Backlash;

    /// <summary>Which axis a backlash check measures. Ignored by the squareness check.</summary>
    public CheckAxis Axis { get; init; } = CheckAxis.X;

    /// <summary>
    /// How far apart the two holes of a pair sit.
    ///
    /// Backlash is a fixed offset rather than a proportional one, so a short span carries the same
    /// signal as a long one and fits an offcut. Squareness is the opposite — its error grows with
    /// distance — so that check wants the longest span the stock and the machine allow.
    /// </summary>
    public double SpanMm { get; init; } = 60;

    /// <summary>Between the rows of a backlash check: far enough apart to tell them apart.</summary>
    public double RowGapMm { get; init; } = 12;

    /// <summary>From work zero to the first hole, so a caliper jaw has somewhere to sit.</summary>
    public double MarginMm { get; init; } = 8;

    /// <summary>
    /// How far past a hole the positioning move starts before coming back to it.
    ///
    /// It only has to exceed the slack, and a millimetre would do on most machines; four is cheap
    /// insurance on one that has never been measured, which is every machine running this check for
    /// the first time.
    /// </summary>
    public double OvershootMm { get; init; } = 4;

    /// <summary>How deep each hole goes. Through the coupon, so a pin can stand in it.</summary>
    public double DepthMm { get; init; } = 1.7;

    /// <summary>How many pecks that depth is taken in.</summary>
    public int Pecks { get; init; } = 4;

    public double SafeZMm { get; init; } = 2;

    public double ApproachZMm { get; init; } = 0.5;

    public int Decimals { get; init; } = 3;
}

/// <summary>What a check will cut, and what its numbers will mean.</summary>
public sealed record MachineCheckReport
{
    public required MachineCheckKind Kind { get; init; }

    public required CheckAxis Axis { get; init; }

    public required IReadOnlyList<CheckHole> Holes { get; init; }

    /// <summary>The pairs to measure, in the order the page lists them.</summary>
    public required IReadOnlyList<CheckPair> Pairs { get; init; }

    /// <summary>How much scrap this needs, including margins.</summary>
    public required double StockWidthMm { get; init; }

    public required double StockHeightMm { get; init; }

    public required double EstimatedSeconds { get; init; }

    /// <summary>The diameter of the pins the operator should stand in the holes: the cutter's own.</summary>
    public double PinMm { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Programs that measure the machine, the way the test cuts measure a bit.
///
/// The app assumes things about a machine that nobody has checked: that a commanded millimetre is a
/// millimetre, that X and Y are at ninety degrees, that a position reached from the left is the
/// position reached from the right. On a desktop mill none of those is given, and the first one to
/// be wrong is discovered as a board that does not fit together.
///
/// Both checks here are built out of plunged holes rather than milled features, because a plunge's
/// position is decided by the move that arrived at it and by nothing else — a milled pocket's edges
/// are cut by further moves, each with its own reversal to confuse the reading. And both are
/// measured with pins and calipers over a span, because that is what a workshop owns.
///
/// What the numbers are for: backlash sets the approach overshoot for drilled holes; squareness is
/// reported rather than applied, because it belongs in the machine's frame. Neither is ever used to
/// quietly correct a program the operator did not ask to have corrected.
/// </summary>
public static class MachineCheck
{
    /// <summary>Builds the program and the report describing it.</summary>
    public static (string Text, MachineCheckReport Report) Generate(MachineCheckOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Tool);

        var notes = new List<string>();
        var warnings = new List<string>();

        var span = options.SpanMm;

        if (span < 20)
        {
            span = 20;
            notes.Add("Span raised to 20 mm: a caliper cannot straddle a shorter pair usefully.");
        }

        if (options.Kind == MachineCheckKind.Squareness && span < 50)
        {
            warnings.Add(Invariant($"A squareness error grows with distance, and this square is only {span:F0} mm across.")
                + " With calipers reading to about 0.05 mm, a skew under 0.1 degrees will not show."
                + " Use the longest span the stock and the machine allow.");
        }

        if (options.Tool.Kind != ToolKind.EndMill)
        {
            warnings.Add(Invariant($"{options.Tool.Name} is a {options.Tool.Kind.ToString().ToLowerInvariant()}, not an end mill.")
                + " A twist drill wanders as it enters, by about as much as this check is trying to measure."
                + " Use an end mill and plunge it.");
        }

        var (holes, pairs) = options.Kind == MachineCheckKind.Backlash
            ? Backlash(options, span)
            : Squareness(options, span);

        var extent = options.MarginMm + span + options.MarginMm;

        var report = new MachineCheckReport
        {
            Kind = options.Kind,
            Axis = options.Axis,
            Holes = holes,
            Pairs = pairs,
            StockWidthMm = options.Kind == MachineCheckKind.Backlash && options.Axis == CheckAxis.Y
                ? options.MarginMm + (2 * options.RowGapMm) + options.MarginMm
                : extent,
            StockHeightMm = options.Kind == MachineCheckKind.Backlash && options.Axis == CheckAxis.X
                ? options.MarginMm + (2 * options.RowGapMm) + options.MarginMm
                : extent,
            EstimatedSeconds = Seconds(options, holes.Count),
            PinMm = Nm.ToMillimetres(options.Tool.DiameterNm),
            Notes = notes,
            Warnings = warnings,
        };

        return (Emit(options, report, span), report);
    }

    // ------------------------------------------------------------------ where the holes go

    /// <summary>
    /// Three rows of two. Row 1 takes the slack up the same way at both holes, so its spacing is the
    /// machine's true one; rows 2 and 3 take it up on opposite sides, one each way, so the
    /// difference between them is twice the backlash.
    /// </summary>
    private static (List<CheckHole> Holes, List<CheckPair> Pairs) Backlash(MachineCheckOptions options, double span)
    {
        // -1 approaches a hole from below (arriving in +), +1 from above (arriving in -).
        (string Name, string Meaning, int First, int Second)[] rows =
        [
            ("Row 1", "the control: both holes take the slack up the same way, so this is the true spacing", -1, -1),
            ("Row 2", "reads long by the backlash", +1, -1),
            ("Row 3", "reads short by the backlash", -1, +1),
        ];

        var holes = new List<CheckHole>();
        var pairs = new List<CheckPair>();

        for (var row = 0; row < rows.Length; row++)
        {
            var across = options.MarginMm + (row * options.RowGapMm);

            foreach (var (hole, side) in new[] { (0, rows[row].First), (1, rows[row].Second) })
            {
                var along = options.MarginMm + (hole * span);

                var (x, y) = options.Axis == CheckAxis.X ? (along, across) : (across, along);
                var (fromX, fromY) = options.Axis == CheckAxis.X
                    ? (along + (side * options.OvershootMm), y)
                    : (x, along + (side * options.OvershootMm));

                holes.Add(new CheckHole(
                    holes.Count + 1,
                    Invariant($"{rows[row].Name}, {(hole == 0 ? "first" : "second")}"),
                    x, y, fromX, fromY));
            }

            pairs.Add(new CheckPair(rows[row].Name, rows[row].Meaning, holes.Count - 2, holes.Count - 1, span));
        }

        return (holes, pairs);
    }

    /// <summary>
    /// Four corners of a square, every one approached from below-left so backlash is taken up
    /// identically and cancels out of both diagonals. What is left in the difference is the angle
    /// between the axes.
    /// </summary>
    private static (List<CheckHole> Holes, List<CheckPair> Pairs) Squareness(MachineCheckOptions options, double span)
    {
        var low = options.MarginMm;
        var high = options.MarginMm + span;
        var over = options.OvershootMm;

        (string Label, double X, double Y)[] corners =
        [
            ("A, lower-left", low, low),
            ("B, lower-right", high, low),
            ("C, upper-right", high, high),
            ("D, upper-left", low, high),
        ];

        var holes = corners
            .Select((c, i) => new CheckHole(i + 1, c.Label, c.X, c.Y, c.X - over, c.Y - over))
            .ToList();

        var diagonal = Math.Sqrt(2) * span;

        return (holes,
        [
            new CheckPair("A–C", "one diagonal", 0, 2, diagonal),
            new CheckPair("B–D", "the other diagonal", 1, 3, diagonal),
        ]);
    }

    private static double Seconds(MachineCheckOptions options, int holes)
    {
        // Rough, and honest about it: the plunges dominate and the travel between six holes on a
        // coupon does not.
        var plunge = options.DepthMm / Math.Max(1, options.Tool.PlungeMmPerMin) * 60;
        var retract = options.Pecks * 0.6;

        return holes * ((plunge * 1.4) + retract + 2);
    }

    // ------------------------------------------------------------------ the program

    private static string Emit(MachineCheckOptions options, MachineCheckReport report, double span)
    {
        var tool = options.Tool;
        var d = Math.Clamp(options.Decimals, 2, 5);

        string Mm(double value) => value.ToString(
            "F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        var text = new List<string> { Rule() };

        foreach (var line in Header(options, report, span))
        {
            text.Add(Boxed(line));
        }

        text.Add(Rule());
        text.Add("G21 G90 G94");
        text.Add("G17");
        text.Add(Invariant($"G0 Z{Mm(options.SafeZMm)}"));
        text.Add(Invariant($"M3 S{tool.SpindleRpm}"));

        foreach (var hole in report.Holes)
        {
            text.Add(string.Empty);
            text.Add(Comment(Invariant($"{hole.Label} — approached from X{Mm(hole.FromXMm)} Y{Mm(hole.FromYMm)}")));
            text.Add(Invariant($"G0 Z{Mm(options.SafeZMm)}"));
            text.Add(Invariant($"G0 X{Mm(hole.FromXMm)} Y{Mm(hole.FromYMm)}"));
            text.Add(Invariant($"G0 X{Mm(hole.XMm)} Y{Mm(hole.YMm)}"));
            text.Add(Invariant($"G0 Z{Mm(options.ApproachZMm)}"));

            for (var peck = 1; peck <= Math.Max(1, options.Pecks); peck++)
            {
                text.Add(Invariant($"G1 Z-{Mm(options.DepthMm * peck / Math.Max(1, options.Pecks))} F{tool.PlungeMmPerMin}"));

                if (peck < options.Pecks)
                {
                    text.Add(Invariant($"G0 Z{Mm(options.ApproachZMm)}"));
                }
            }
        }

        text.Add(string.Empty);
        text.Add(Invariant($"G0 Z{Mm(options.SafeZMm)}"));
        text.Add("M5");
        text.Add("G0 X0.000 Y0.000");
        text.Add("M30");
        text.Add(string.Empty);

        return string.Join('\n', text);
    }

    private static List<string> Header(MachineCheckOptions options, MachineCheckReport report, double span)
    {
        var lines = new List<string>
        {
            options.Kind == MachineCheckKind.Backlash
                ? Invariant($"PCB_MillBurn — backlash check, {options.Axis} axis")
                : "PCB_MillBurn — squareness check",
            string.Empty,
            Invariant($"Fit the {options.Tool.Name}. Any flat offcut at least {report.StockWidthMm:F0} x {report.StockHeightMm:F0} mm."),
            Invariant($"{report.Holes.Count} holes, {options.DepthMm:F2} mm deep, so a {report.PinMm:F2} mm pin can stand in each."),
            string.Empty,
        };

        if (options.Kind == MachineCheckKind.Backlash)
        {
            lines.Add(Invariant(
                $"Every hole is approached along {options.Axis} only, {options.OvershootMm:F0} mm past it and back,"));
            lines.Add("from the side its row calls for. The rows differ in nothing else.");
            lines.Add(string.Empty);
            lines.Add("Measure OVER THE OUTSIDES of each row's pair, with the same pins in every row:");
            lines.Add("the pin diameter is common to all three, so it cancels out of the comparison.");
            lines.Add(string.Empty);
            lines.Add("    backlash = ( row 2 - row 3 ) / 2");
            lines.Add(string.Empty);
            lines.Add(Invariant($"Row 1 is the control: {span:F3} mm between centres, plus one pin."));
        }
        else
        {
            lines.Add("Every hole is approached from below-left, so backlash is taken up the same way");
            lines.Add("at all four and cancels out of both diagonals. What is left is squareness.");
            lines.Add(string.Empty);
            lines.Add("Measure each diagonal with pins: over the outsides, then between the insides,");
            lines.Add("and average the two — that is the centre distance, with the pin cancelling.");
            lines.Add(string.Empty);
            lines.Add(Invariant($"Both diagonals should read {Math.Sqrt(2) * span:F3} mm."));
        }

        lines.Add(string.Empty);
        lines.Add("A difference under 0.1 mm is not a measurement: that is what pins and calipers");
        lines.Add("resolve. Take each reading three times.");

        return lines;
    }

    private static string Rule() => "( " + new string('-', 72) + " )";

    private static string Boxed(string text) => text.Length == 0 ? "(  )" : "( " + text + " )";

    private static string Comment(string text) => "( " + text + " )";
}
