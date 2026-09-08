using System.Globalization;
using MillBurn.Core;

namespace MillBurn.Gcode;

/// <summary>What a move is doing, which is what decides how it is drawn.</summary>
public enum BackplotRole
{
    /// <summary>Cutting: in the material, moving in XY.</summary>
    Cut,

    /// <summary>Going down into the material.</summary>
    Plunge,

    /// <summary>Coming back out.</summary>
    Retract,

    /// <summary>Moving above the work.</summary>
    Travel,

    /// <summary>A travel longer than the threshold — what wasted time looks like.</summary>
    LongTravel,

    /// <summary>Rapid *in* the material. Always a bug, always shown.</summary>
    Gouge,
}

/// <summary>A move, classified, ready to draw.</summary>
public readonly record struct BackplotMove(BackplotRole Role, GcodeMove Move);

/// <summary>What the program costs.</summary>
public sealed record BackplotStats
{
    public required double CutMm { get; init; }

    public required double TravelMm { get; init; }

    public required double PlungeMm { get; init; }

    public required int PlungeCount { get; init; }

    public required int LongTravelCount { get; init; }

    public required int GougeCount { get; init; }

    /// <summary>
    /// Time if the machine could reach its feed instantly. A true lower bound, never achievable.
    /// </summary>
    public required TimeSpan OptimisticTime { get; init; }

    /// <summary>
    /// Time if the machine came to a full stop at every corner. A true upper bound, and on a
    /// contour made of thousands of short segments it is a long way above reality, because a real
    /// controller carries velocity through a shallow direction change.
    /// </summary>
    public required TimeSpan PessimisticTime { get; init; }

    /// <summary>
    /// The honest form of the estimate: a range, not a number.
    ///
    /// A single figure here would be wrong in a way nobody could see. Junction handling is what
    /// decides where in this range a machine actually lands, and modelling it properly is Phase 3's
    /// job — so until then the range is reported and neither end is dressed up as the answer.
    /// </summary>
    public string TimeRange() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Format(OptimisticTime)} – {Format(PessimisticTime)}");

    private static string Format(TimeSpan t) => t.TotalHours >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}h {t.Minutes}m")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes}m {t.Seconds}s");
}

/// <summary>Machine limits, for costing a program.</summary>
public sealed record MotionLimits
{
    public double RapidMmPerMin { get; init; } = 2000;

    /// <summary>Acceleration in mm/s². 200 is a typical small GRBL router.</summary>
    public double AccelerationMmPerSecondSquared { get; init; } = 200;
}

/// <summary>
/// Classifies a parsed program for drawing, and costs it.
///
/// The classification is where the verification lives. **A rapid below Z0 is a gouge**: the tool
/// crossing the board at cutting depth, which is the single worst thing a generator can emit.
/// Checking it here rather than in the emitter is the point — this reads the file that will run,
/// so it catches the emitter being wrong rather than agreeing with it.
/// </summary>
public static class GcodeBackplot
{
    /// <summary>A travel longer than this is worth pointing at. 10 mm on a PCB is a long way.</summary>
    public static long DefaultLongTravelNm { get; } = Nm.FromMillimetres(10);

    public static IReadOnlyList<BackplotMove> Classify(
        GcodeProgram program, long longTravelNm = 0)
    {
        ArgumentNullException.ThrowIfNull(program);

        var threshold = longTravelNm > 0 ? longTravelNm : DefaultLongTravelNm;
        var classified = new List<BackplotMove>(program.Moves.Count);

        foreach (var move in program.Moves)
        {
            classified.Add(new BackplotMove(RoleOf(move, threshold), move));
        }

        return classified;
    }

    private static BackplotRole RoleOf(GcodeMove move, long threshold)
    {
        var inMaterial = move.DeepestZNm < 0;

        if (move.IsRapid)
        {
            if (inMaterial && move.MovesInPlane)
            {
                return BackplotRole.Gouge;
            }

            if (!move.MovesInPlane)
            {
                return move.ToZNm > move.FromZNm ? BackplotRole.Retract : BackplotRole.Plunge;
            }

            return move.From.DistanceTo(move.To) >= threshold
                ? BackplotRole.LongTravel
                : BackplotRole.Travel;
        }

        if (move.IsVertical)
        {
            return move.ToZNm < move.FromZNm ? BackplotRole.Plunge : BackplotRole.Retract;
        }

        return inMaterial ? BackplotRole.Cut : BackplotRole.Travel;
    }

    public static BackplotStats Measure(
        IReadOnlyList<BackplotMove> moves, MotionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(moves);
        limits ??= new MotionLimits();

        var cut = 0.0;
        var travel = 0.0;
        var plunge = 0.0;
        var plunges = 0;
        var longTravels = 0;
        var gouges = 0;

        var optimistic = 0.0;
        var pessimistic = 0.0;

        foreach (var (role, move) in moves)
        {
            var lengthMm = move.LengthNm / Nm.PerMillimetre;
            if (lengthMm <= 0)
            {
                continue;
            }

            var feed = role is BackplotRole.Travel or BackplotRole.LongTravel or BackplotRole.Gouge
                ? limits.RapidMmPerMin
                : move.FeedMmPerMin > 0 ? move.FeedMmPerMin : limits.RapidMmPerMin;

            switch (role)
            {
                case BackplotRole.Cut: cut += lengthMm; break;
                case BackplotRole.Plunge: plunge += lengthMm; plunges++; break;
                case BackplotRole.Retract: plunge += lengthMm; break;
                case BackplotRole.LongTravel: travel += lengthMm; longTravels++; break;
                case BackplotRole.Gouge: travel += lengthMm; gouges++; break;
                default: travel += lengthMm; break;
            }

            optimistic += lengthMm / feed * 60;
            pessimistic += TrapezoidSeconds(lengthMm, feed / 60.0, limits.AccelerationMmPerSecondSquared);
        }

        return new BackplotStats
        {
            CutMm = cut,
            TravelMm = travel,
            PlungeMm = plunge,
            PlungeCount = plunges,
            LongTravelCount = longTravels,
            GougeCount = gouges,
            OptimisticTime = TimeSpan.FromSeconds(optimistic),
            PessimisticTime = TimeSpan.FromSeconds(pessimistic),
        };
    }

    /// <summary>
    /// Time for one move that starts and ends at rest.
    ///
    /// Accelerate, cruise, decelerate — unless the move is too short to reach the feed, in which
    /// case it is a triangle and the top speed never arrives. That second case is most of a PCB
    /// job: a contour of 0.1 mm segments never gets anywhere near its programmed feed, which is why
    /// distance divided by feed is such a poor estimate of how long a board takes.
    /// </summary>
    private static double TrapezoidSeconds(double lengthMm, double feedMmPerSecond, double accel)
    {
        if (feedMmPerSecond <= 0 || accel <= 0)
        {
            return 0;
        }

        var rampDistance = feedMmPerSecond * feedMmPerSecond / accel;

        return lengthMm >= rampDistance
            ? (lengthMm / feedMmPerSecond) + (feedMmPerSecond / accel)
            : 2 * Math.Sqrt(lengthMm / accel);
    }
}
