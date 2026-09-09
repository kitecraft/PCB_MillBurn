using System.Globalization;
using MillBurn.Core;

namespace MillBurn.Gcode;

/// <summary>How to probe a piece of stock.</summary>
public sealed record ProbeRoutineOptions
{
    /// <summary>
    /// How far apart to space the touches, in millimetres.
    ///
    /// Ten millimetres is a sensible default for FR4: the bow of a clamped board is a long, smooth
    /// shape, not a rough one, so the surface between two points 10 mm apart is very well predicted
    /// by the two points. Halving this quadruples the probing time and buys very little.
    /// </summary>
    public double SpacingMm { get; init; } = 10;

    /// <summary>Height to cross the board at on the way in and out.</summary>
    public double SafeHeightMm { get; init; } = 5;

    /// <summary>
    /// Height each descent starts from, and the height the tool travels at between touches.
    ///
    /// One millimetre, not the safe height: every touch would otherwise spend four extra seconds
    /// covering ground the probe has already proved is empty, and a two-hundred-point grid would
    /// take a quarter of an hour longer for nothing. There is nothing standing on a bare piece of
    /// stock to hit.
    /// </summary>
    public double StartHeightMm { get; init; } = 1;

    /// <summary>How far below work zero a touch may search before giving up.</summary>
    public double MaxDepthMm { get; init; } = 2;

    /// <summary>Probing feed. Slow: this is what the accuracy of the whole map rests on.</summary>
    public double FeedMmPerMin { get; init; } = 30;

    /// <summary>
    /// How far inside the region to keep the touches, in millimetres.
    ///
    /// A probe tip half over the edge of the board reads the table. One millimetre in is enough to
    /// be certain of landing on copper, and costs nothing: the map is extended out to the board's
    /// corners by holding the nearest measured value, and over one millimetre of a surface this
    /// smooth that is exact to well under a micron.
    /// </summary>
    public double MarginMm { get; init; } = 1;

    /// <summary>
    /// The most touches to ask for. The spacing is opened up rather than the grid being truncated.
    ///
    /// Every point costs about four seconds of somebody standing and watching. Two hundred is
    /// already thirteen minutes.
    /// </summary>
    public int MaxPoints { get; init; } = 200;
}

/// <summary>What the routine will do, in numbers worth seeing before running it.</summary>
public sealed record ProbeRoutineReport
{
    public required int Columns { get; init; }

    public required int Rows { get; init; }

    public required double SpacingMm { get; init; }

    /// <summary>Roughly how long this will stand there doing it.</summary>
    public required double EstimatedSeconds { get; init; }

    public int PointCount => Columns * Rows;

    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// Emits a standalone program that touches off a grid over the board, so the surface can be
/// measured and the cutting depth made to follow it.
///
/// **We generate; we do not drive.** The operator runs this in their own sender, the sender logs
/// what came back, and <c>ProbeLog</c> reads the log
/// ([01 §1.1](../../Documentation/01-Architecture.md)). No serial port is opened anywhere in this
/// program, which is also why a map probed by any other tool imports just as well.
/// </summary>
public static class ProbeRoutine
{
    /// <summary>Builds the probing program for a region, in work coordinates.</summary>
    public static (string Text, ProbeRoutineReport Report) Generate(
        Bounds region, ProbeRoutineOptions? options = null)
    {
        options ??= new ProbeRoutineOptions();

        if (region.IsEmpty)
        {
            throw new ArgumentException("There is no region to probe.", nameof(region));
        }

        var notes = new List<string>();
        var margin = Nm.FromMillimetres(options.MarginMm);

        // A margin wider than half the board would turn it inside out. Give up the margin rather
        // than the probing: a board 3 mm across is a strange thing to be levelling, but refusing
        // to do it would be stranger.
        var maxMargin = Math.Min(region.Width, region.Height) / 4;

        if (margin > maxMargin)
        {
            margin = maxMargin;
            notes.Add("The board is too small for the usual 1 mm edge margin; used a narrower one.");
        }

        // Emitted relative to the region's own lower-left corner, because that is where work zero
        // is for every other file in the export (Help/faq.html, "Where is work zero?"). A probing
        // routine that used raw Gerber coordinates would measure a surface in a different frame
        // from the one the cutting files are in, and the levelling would be nonsense — while
        // looking perfectly reasonable in both files.
        var minX = margin;
        var minY = margin;
        var width = region.Width - (2 * margin);
        var height = region.Height - (2 * margin);

        var spacing = options.SpacingMm;
        var (columns, rows) = GridFor(width, height, spacing);

        // Coarsen rather than crop. A grid that stops two thirds of the way across the board leaves
        // the last third unmeasured, and the map would then hold the edge value out over ground it
        // has never seen — which is exactly the kind of quiet wrongness this is meant to prevent.
        while (columns * rows > options.MaxPoints && spacing < 1000)
        {
            spacing *= 1.25;
            (columns, rows) = GridFor(width, height, spacing);
        }

        if (Math.Abs(spacing - options.SpacingMm) > 1e-9)
        {
            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Spacing opened from {options.SpacingMm:F1} mm to {spacing:F1} mm to stay under "
                + $"{options.MaxPoints} points."));
        }

        var stepX = columns > 1 ? width / (double)(columns - 1) : 0;
        var stepY = rows > 1 ? height / (double)(rows - 1) : 0;

        var seconds = EstimateSeconds(columns * rows, options);
        var text = Emit(minX, minY, stepX, stepY, columns, rows, options, spacing, seconds);

        return (text, new ProbeRoutineReport
        {
            Columns = columns,
            Rows = rows,
            SpacingMm = spacing,
            EstimatedSeconds = seconds,
            Notes = notes,
        });
    }

    private static (int Columns, int Rows) GridFor(long width, long height, double spacingMm)
    {
        var step = Nm.FromMillimetres(spacingMm);

        return (
            (int)Math.Max(2, (width / step) + 1),
            (int)Math.Max(2, (height / step) + 1));
    }

    /// <summary>
    /// Four seconds a point, near enough: down from the start height at the probing feed, back up,
    /// and across to the next one.
    ///
    /// Approximate on purpose — this is here so nobody starts a twenty-minute routine thinking it
    /// is a two-minute one, and for that a number that is right to the nearest minute is plenty.
    /// </summary>
    private static double EstimateSeconds(int points, ProbeRoutineOptions options)
    {
        var descent = (options.StartHeightMm + 0.2) / Math.Max(options.FeedMmPerMin, 1) * 60;
        return points * (descent + 1.6);
    }

    private static string Emit(
        long minX,
        long minY,
        double stepX,
        double stepY,
        int columns,
        int rows,
        ProbeRoutineOptions options,
        double spacingMm,
        double seconds)
    {
        var lines = new List<string>((columns * rows * 3) + 24);

        var minutes = seconds / 60;
        var safe = Millimetres(Nm.FromMillimetres(options.SafeHeightMm));
        var start = Millimetres(Nm.FromMillimetres(options.StartHeightMm));
        var depth = Millimetres(-Nm.FromMillimetres(options.MaxDepthMm));
        var feed = options.FeedMmPerMin.ToString("0.###", CultureInfo.InvariantCulture);

        var grid = Invariant(
            $"{columns} x {rows} = {columns * rows} touches at {spacingMm:F1} mm spacing.");
        var howLong = Invariant($"About {minutes:F0} minutes of standing and watching.");

        lines.Add(Rule());
        lines.Add(Boxed("PROBE ROUTINE. This program cuts nothing."));
        lines.Add(Boxed(string.Empty));
        lines.Add(Boxed(grid));
        lines.Add(Boxed(howLong));
        lines.Add(Boxed("Work zero is the board's lower-left corner, the same"));
        lines.Add(Boxed("as every other file in this export."));
        lines.Add(Boxed(string.Empty));
        lines.Add(Boxed("Put a probe on the tool and a clip on the copper, zero"));
        lines.Add(Boxed("Z on the surface, run this, and save your sender's log."));
        lines.Add(Boxed(string.Empty));

        // Said here rather than only in the help, because this is what the operator is looking at
        // the moment it happens. Plenty of G-code viewers have no G38.2 in their vocabulary, and a
        // preview that appears to show the tool dragging through the board is exactly the sort of
        // thing that stops someone pressing start — rightly, if they had no way to know better.
        lines.Add(Boxed("YOUR SENDER'S PREVIEW MAY LOOK WRONG. Many G-code"));
        lines.Add(Boxed("viewers do not understand G38.2 - probe toward the"));
        lines.Add(Boxed("work - so they draw no Z movement at all, or show the"));
        lines.Add(Boxed("tool dragging across the board at depth. The file is"));
        lines.Add(Boxed(Invariant($"right: every touch is a move at Z{start}, a probe")));
        lines.Add(Boxed(Invariant($"down, and a retract to Z{start}. Nothing here cuts.")));
        lines.Add(Rule());
        lines.Add("M5");
        lines.Add("G21 G90");
        lines.Add(Invariant($"G0 Z{safe}"));
        lines.Add(string.Empty);

        for (var row = 0; row < rows; row++)
        {
            var y = minY + (long)Math.Round(row * stepY);

            // Serpentine: every other row runs backwards, so the tool never crosses the whole board
            // to start the next one. On a 15 x 12 grid that is 165 pointless traverses saved.
            for (var i = 0; i < columns; i++)
            {
                var column = row % 2 == 0 ? i : columns - 1 - i;
                var x = minX + (long)Math.Round(column * stepX);

                lines.Add(Invariant($"G0 X{Millimetres(x)} Y{Millimetres(y)}"));
                lines.Add(Invariant($"G38.2 Z{depth} F{feed}"));
                lines.Add(Invariant($"G0 Z{start}"));
            }
        }

        lines.Add(string.Empty);
        lines.Add(Invariant($"G0 Z{safe}"));
        lines.Add("G0 X0 Y0");
        lines.Add("M30");

        return string.Join("\n", lines);
    }

    /// <summary>The width of the comment box, so the header lines up however long the numbers are.</summary>
    private const int BoxWidth = 56;

    private static string Rule() => "( " + new string('*', BoxWidth) + " )";

    /// <summary>
    /// One line of the header box.
    ///
    /// Brackets are stripped rather than trusted. A G-code comment runs to its closing bracket, so
    /// an innocent "minute(s)" ends the comment early and leaves the rest of the sentence to be
    /// read as code; LinuxCNC rejects the nested opening bracket outright. Easier to make that
    /// impossible here than to remember it in every line of prose.
    /// </summary>
    private static string Boxed(string text) =>
        "( " + text.Replace('(', '[').Replace(')', ']').PadRight(BoxWidth) + " )";

    private static string Millimetres(long nm) =>
        (nm / (double)Nm.PerMillimetre).ToString("0.000", CultureInfo.InvariantCulture);

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
