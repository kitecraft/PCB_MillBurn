using System.Globalization;
using MillBurn.Core;
using static System.FormattableString;

namespace MillBurn.Gcode;

/// <summary>A hole or slot a program cuts, numbered in the order the program first reaches it.</summary>
/// <param name="Number">One-based, in program order.</param>
/// <param name="At">Its centre, in the program's own work coordinates.</param>
/// <param name="Kind"><c>"hole"</c> or <c>"slot"</c>.</param>
public sealed record AlignmentTarget(int Number, Point2 At, string Kind);

/// <summary>How the alignment test moves.</summary>
public sealed record AlignmentTestOptions
{
    /// <summary>Where the bit stops, above the surface. Must be above it.</summary>
    public double HoverMm { get; init; } = 0.1;

    /// <summary>The feed for the last millimetre down.</summary>
    public double FeedMmPerMin { get; init; } = 100;

    public double SafeZMm { get; init; } = 5;

    public int Decimals { get; init; } = 3;
}

/// <summary>
/// Hovers a bit over one hole, so the operator can see whether the holes will land in their pads.
///
/// A small hole in a small pad leaves a few tenths of a millimetre either side, so a drilling origin
/// that is slightly out puts holes on the edge of the pad or off it. The fix is an origin shift, found
/// by looking: bring the tip down just above a real hole with the spindle off, move the origin until
/// it sits dead centre, and write the drilling and routing files again with that shift. The board is
/// assumed square to the machine, so one hole right means every hole right.
///
/// The holes are read from **the emitted program**, not from the drill file, for the same reason every
/// other description here is: the program is what the machine will run, mirrored and shifted and
/// split exactly as it will run it.
/// </summary>
public static class AlignmentTest
{
    /// <summary>
    /// Every hole and slot a program cuts, in the order it first reaches them.
    ///
    /// A feature is a stretch of the program below the surface, between going down and coming back
    /// up; its target is the centre of the ground that stretch covers. A drilled hole covers a point.
    /// A routed hole is a helix, and its arcs cover exactly the circle, so the centre of that is the
    /// hole's centre. A slot's racetrack covers the slot, likewise. Pecks and depth passes come back
    /// to the same place and are counted once.
    ///
    /// Canned cycles (<c>G81</c>/<c>G83</c>) are not interpreted by the reader, so a drilling program
    /// written with them has no targets here.
    /// </summary>
    public static IReadOnlyList<AlignmentTarget> Targets(string program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var found = new List<(Point2 At, string Kind)>();
        var same = Nm.FromMillimetres(0.05);
        var round = Nm.FromMillimetres(0.1);

        var inside = false;
        long minX = 0, minY = 0, maxX = 0, maxY = 0;

        void Include(long x, long y)
        {
            if (!inside)
            {
                (minX, maxX, minY, maxY, inside) = (x, x, y, y, true);
                return;
            }

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        void Close()
        {
            if (!inside)
            {
                return;
            }

            inside = false;

            var at = new Point2((minX + maxX) / 2, (minY + maxY) / 2);

            if (found.Any(f => Math.Abs(f.At.X - at.X) <= same && Math.Abs(f.At.Y - at.Y) <= same))
            {
                return;
            }

            found.Add((at, Math.Abs((maxX - minX) - (maxY - minY)) <= round ? "hole" : "slot"));
        }

        foreach (var move in GcodeParser.Parse(program).Moves)
        {
            if (Math.Min(move.FromZNm, move.ToZNm) >= 0)
            {
                Close();
                continue;
            }

            Include(move.From.X, move.From.Y);
            Include(move.To.X, move.To.Y);

            if (move.IsArc)
            {
                var radius = (long)Math.Round(move.From.DistanceTo(move.Centre));

                Include(move.Centre.X - radius, move.Centre.Y - radius);
                Include(move.Centre.X + radius, move.Centre.Y + radius);
            }

            if (move.ToZNm >= 0)
            {
                Close();
            }
        }

        Close();

        return [.. found.Select((f, i) => new AlignmentTarget(i + 1, f.At, f.Kind))];
    }

    /// <summary>
    /// The test program: spindle off, up to safe height, over the hole with the offset applied, and
    /// down to the hover height — slowly for the last millimetre — where it stays.
    ///
    /// It ends there on purpose. The point is to look at the tip, so nothing lifts it away when the
    /// program finishes; the next run starts by going up to safe height.
    /// </summary>
    /// <param name="target">The hole to hover over.</param>
    /// <param name="offsetNm">How far to move from where the program puts it.</param>
    /// <param name="programName">The file the hole is in, for the header.</param>
    /// <param name="options">Heights and feed.</param>
    public static string Generate(
        AlignmentTarget target, Point2 offsetNm, string programName, AlignmentTestOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(programName);

        options ??= new AlignmentTestOptions();

        // Never at or below the surface. This program is run with the tip a tenth of a millimetre from
        // the copper and nobody's hand on the stop; a zero here is a scratch through the pad it is meant
        // to be checking.
        if (options.HoverMm <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "The bit has to stop above the surface: the hover height must be more than zero.");
        }

        var at = new Point2(target.At.X + offsetNm.X, target.At.Y + offsetNm.Y);
        var format = Invariant($"F{options.Decimals}");
        var name = programName.Replace('(', '[').Replace(')', ']');
        var kind = target.Kind == "slot" ? "Slot" : "Hole";

        string Coordinate(long nm) => (nm / (double)Nm.PerMillimetre).ToString(format, CultureInfo.InvariantCulture);

        string[] lines =
        [
            "( PCB_MillBurn - drill alignment test. This program cuts nothing. )",
            Invariant($"( {kind} {target.Number} of {name}, at X{Coordinate(target.At.X)} Y{Coordinate(target.At.Y)}. )"),
            Invariant($"( Offset X{FormatOffset(offsetNm.X)} Y{FormatOffset(offsetNm.Y)} mm: the bit goes to X{Coordinate(at.X)} Y{Coordinate(at.Y)}. )"),
            Invariant($"( The spindle stays off. The bit stops {Height(options.HoverMm)} mm above the surface and stays there. )"),
            "( Fit the bit that file uses, and zero Z on your usual spot, before running this. )",
            "( If the bit touches the copper, stop: Z zero is below the surface. )",
            "G21 G90 G94",
            "G17",
            "M5",
            Invariant($"G0 Z{Height(options.SafeZMm)}"),
            Invariant($"G0 X{Coordinate(at.X)} Y{Coordinate(at.Y)}"),
            Invariant($"G0 Z{Height(options.HoverMm + 1)}"),
            Invariant($"G1 Z{Height(options.HoverMm)} F{options.FeedMmPerMin.ToString("0.###", CultureInfo.InvariantCulture)}"),
            "M2",
        ];

        return string.Join("\n", lines) + "\n";
    }

    /// <summary>"+0.120" or "-0.050".</summary>
    public static string FormatOffset(long nm) => (nm < 0 ? "-" : "+") + Nm.ToMillimetreString(Math.Abs(nm), 3);

    private static string Height(double mm) => mm.ToString("0.000", CultureInfo.InvariantCulture);
}
