namespace MillBurn.Core;

/// <summary>
/// What the machine can actually do, in the terms its motion planner works in.
///
/// These are not preferences, they are the numbers that decide how long a program takes and — for
/// the optimizer — which of two orderings is genuinely faster. A distance-based cost gets this
/// wrong in a specific and important way: two hundred short moves cost far more than their summed
/// length suggests, because none of them ever reaches full speed.
/// </summary>
public sealed record MachineProfile
{
    /// <summary>Rapid feed for X and Y, in mm/min. GRBL's <c>$110</c>/<c>$111</c>.</summary>
    public double RapidMmPerMin { get; init; } = 2000;

    /// <summary>Rapid feed for Z, which is almost always slower. GRBL's <c>$112</c>.</summary>
    public double ZRapidMmPerMin { get; init; } = 600;

    /// <summary>Acceleration in mm/s². 200 is a typical small GRBL router. GRBL's <c>$120</c>.</summary>
    public double AccelerationMmPerSecondSquared { get; init; } = 200;

    /// <summary>
    /// How far the controller lets the tool deviate from the exact corner in order to carry speed
    /// through it. GRBL's <c>$11</c>, default 0.010 mm.
    ///
    /// This one number is what decides where a real machine lands between "never slows down" and
    /// "stops at every corner", and it is why a single time estimate is possible at all.
    /// </summary>
    public double JunctionDeviationMm { get; init; } = 0.01;

    /// <summary>
    /// Speed a corner is allowed to keep no matter how sharp it is, in mm/min.
    ///
    /// Without a floor, a perfect reversal costs a full stop *and* the acceleration back up, and a
    /// path that doubles back on itself is estimated far slower than it runs. GRBL keeps the same
    /// floor for the same reason.
    /// </summary>
    public double MinimumJunctionMmPerMin { get; init; } = 60;

    public double RapidMmPerSec => RapidMmPerMin / 60.0;

    public double ZRapidMmPerSec => ZRapidMmPerMin / 60.0;

    public double MinimumJunctionMmPerSec => MinimumJunctionMmPerMin / 60.0;

    /// <summary>A machine that never accelerates and never corners. Only useful as a bound.</summary>
    public static MachineProfile Instant { get; } = new()
    {
        AccelerationMmPerSecondSquared = double.PositiveInfinity,
    };
}

/// <summary>
/// How long a move takes, modelled the way the controller actually does it.
///
/// This is deliberately the same algorithm GRBL runs: a junction speed per corner from the
/// deviation setting, then a backward and a forward pass to make every junction reachable, then a
/// trapezoid per segment. Reimplementing the planner is what makes the estimate match the machine
/// instead of merely being plausible — and the optimizer is only as good as the cost it minimises.
/// </summary>
public static class MotionPlanner
{
    /// <summary>
    /// Time for one move that starts and ends at rest.
    ///
    /// The shape that matters: below the distance needed to reach full speed the profile is a
    /// triangle and the time goes as <c>2*sqrt(d/a)</c>, so halving a rapid's length saves only
    /// about 30% of its time. That is why an optimizer scored on distance makes choices a
    /// time-scored one would not.
    /// </summary>
    public static double SecondsAtRest(double distanceMm, double maxMmPerSec, double accelMmPerSec2)
    {
        if (distanceMm <= 0 || maxMmPerSec <= 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(accelMmPerSec2))
        {
            return distanceMm / maxMmPerSec;
        }

        // Distance needed to reach cruise and come back to rest again.
        var toCruise = (maxMmPerSec * maxMmPerSec) / accelMmPerSec2;

        return distanceMm >= toCruise
            ? (distanceMm / maxMmPerSec) + (maxMmPerSec / accelMmPerSec2)
            : 2.0 * Math.Sqrt(distanceMm / accelMmPerSec2);
    }

    /// <summary>A rapid between two points, starting and ending stationary.</summary>
    public static double RapidSeconds(double distanceMm, MachineProfile machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return SecondsAtRest(
            distanceMm, machine.RapidMmPerSec, machine.AccelerationMmPerSecondSquared);
    }

    /// <summary>A Z move, which uses the slower Z rapid.</summary>
    public static double ZSeconds(double distanceMm, MachineProfile machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return SecondsAtRest(
            distanceMm, machine.ZRapidMmPerSec, machine.AccelerationMmPerSecondSquared);
    }

    /// <summary>
    /// Time for a continuous run of moves, with the controller's look-ahead applied.
    ///
    /// <paramref name="feedMmPerSec"/> is the programmed feed; the planner never exceeds it, and
    /// routinely falls well short of it on short segments, which is exactly the effect worth
    /// modelling.
    /// </summary>
    public static double PathSeconds(
        IReadOnlyList<Point2> points, double feedMmPerSec, MachineProfile machine)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(machine);

        if (points.Count < 2 || feedMmPerSec <= 0)
        {
            return 0;
        }

        var accel = machine.AccelerationMmPerSecondSquared;
        var lengths = new double[points.Count - 1];
        var units = new (double X, double Y)[points.Count - 1];
        var count = 0;

        for (var i = 0; i < points.Count - 1; i++)
        {
            var dx = (points[i + 1].X - points[i].X) / (double)Nm.PerMillimetre;
            var dy = (points[i + 1].Y - points[i].Y) / (double)Nm.PerMillimetre;
            var length = Math.Sqrt((dx * dx) + (dy * dy));

            // A zero-length move has no direction, so it cannot define a corner either.
            if (length <= 0)
            {
                continue;
            }

            lengths[count] = length;
            units[count] = (dx / length, dy / length);
            count++;
        }

        if (count == 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(accel))
        {
            var total = 0.0;
            for (var i = 0; i < count; i++)
            {
                total += lengths[i] / feedMmPerSec;
            }

            return total;
        }

        // Junction speeds: entry[i] is the speed carried into segment i. The ends are at rest.
        var entry = new double[count + 1];

        for (var i = 1; i < count; i++)
        {
            entry[i] = Math.Min(JunctionSpeed(units[i - 1], units[i], machine), feedMmPerSec);
        }

        // Backward pass: no junction may be faster than what can still be shed before the next one.
        for (var i = count - 1; i >= 1; i--)
        {
            var reachable = Math.Sqrt((entry[i + 1] * entry[i + 1]) + (2 * accel * lengths[i]));
            entry[i] = Math.Min(entry[i], reachable);
        }

        // Forward pass: nor faster than what can be built up since the previous one.
        var seconds = 0.0;
        for (var i = 0; i < count; i++)
        {
            var reachable = Math.Sqrt((entry[i] * entry[i]) + (2 * accel * lengths[i]));
            entry[i + 1] = Math.Min(entry[i + 1], reachable);

            seconds += SegmentSeconds(lengths[i], entry[i], entry[i + 1], feedMmPerSec, accel);
        }

        return seconds;
    }

    /// <summary>
    /// How fast the tool may go round a corner, from the machine's junction deviation.
    ///
    /// Straight through is unrestricted; a full reversal drops to the floor. Everything between
    /// follows from how far the controller is willing to cut the corner.
    /// </summary>
    private static double JunctionSpeed(
        (double X, double Y) before, (double X, double Y) after, MachineProfile machine)
    {
        // Negated, so that straight-on is -1 and a reversal is +1 — the convention the deviation
        // formula is written in.
        var cosTheta = -((before.X * after.X) + (before.Y * after.Y));

        if (cosTheta >= 0.999)
        {
            return machine.MinimumJunctionMmPerSec;
        }

        var sinHalf = Math.Sqrt(0.5 * (1.0 - Math.Max(cosTheta, -0.999)));
        if (sinHalf >= 1.0)
        {
            return double.PositiveInfinity;
        }

        var squared = machine.AccelerationMmPerSecondSquared
            * machine.JunctionDeviationMm * sinHalf / (1.0 - sinHalf);

        return Math.Max(Math.Sqrt(squared), machine.MinimumJunctionMmPerSec);
    }

    /// <summary>One segment: accelerate, maybe cruise, decelerate.</summary>
    private static double SegmentSeconds(
        double lengthMm, double entryMmPerSec, double exitMmPerSec, double feedMmPerSec, double accel)
    {
        var toCruise = ((feedMmPerSec * feedMmPerSec) - (entryMmPerSec * entryMmPerSec)) / (2 * accel);
        var fromCruise = ((feedMmPerSec * feedMmPerSec) - (exitMmPerSec * exitMmPerSec)) / (2 * accel);

        if (toCruise + fromCruise <= lengthMm)
        {
            var cruise = lengthMm - toCruise - fromCruise;
            return ((feedMmPerSec - entryMmPerSec) / accel)
                + (cruise / feedMmPerSec)
                + ((feedMmPerSec - exitMmPerSec) / accel);
        }

        // Never reaches the programmed feed: it peaks partway and comes straight back down.
        var peakSquared =
            ((2 * accel * lengthMm) + (entryMmPerSec * entryMmPerSec) + (exitMmPerSec * exitMmPerSec)) / 2.0;
        var peak = Math.Sqrt(Math.Max(peakSquared, 0));

        return ((peak - entryMmPerSec) / accel) + ((peak - exitMmPerSec) / accel);
    }
}
