namespace MillBurn.Core;

/// <summary>
/// What to do when a depth is not a whole number of steps.
///
/// 1.10 mm in 0.50 mm steps is 0.50 + 0.50 + 0.10: a last lap that takes a fifth of a step and a
/// whole lap's time, and on a through cut is usually cutting air. Which answer is right depends on
/// the operator and their cutters rather than on the board, so it is a machine-wide choice, and the
/// old behaviour stays the default.
/// </summary>
public enum ShortLastLap
{
    /// <summary>Cut it as a lap of its own: every lap but the last is exactly one step. The original rule.</summary>
    OwnLap,

    /// <summary>
    /// The same number of laps, all the same depth: 1.10 mm is three of 0.367. None is deeper than
    /// a step, so nothing asks more of the cutter than its stepdown says it can take.
    /// </summary>
    SpreadEvenly,

    /// <summary>
    /// A last lap under a quarter of a step goes into the one before: 1.10 mm is 0.50 + 0.60. One lap
    /// fewer, and that lap takes up to a quarter more than the stepdown.
    /// </summary>
    FoldIn,
}

/// <summary>How a cut's depth is divided into laps.</summary>
public static class Laps
{
    /// <summary>
    /// The depth each lap reaches, shallowest first, ending exactly at <paramref name="depthNm"/>.
    /// </summary>
    /// <param name="depthNm">How deep the cut goes in all.</param>
    /// <param name="stepNm">The cutter's stepdown. Zero or less takes the whole depth in one lap.</param>
    /// <param name="policy">What to do with a short last lap.</param>
    public static IReadOnlyList<long> Depths(long depthNm, long stepNm, ShortLastLap policy = ShortLastLap.OwnLap)
    {
        if (depthNm <= 0)
        {
            return [];
        }

        var step = stepNm > 0 ? stepNm : depthNm;
        var count = Math.Max(1, (int)Math.Ceiling(depthNm / (double)step));
        var remainder = depthNm - ((count - 1) * step);

        switch (policy)
        {
            case ShortLastLap.SpreadEvenly:
                return [.. Enumerable.Range(1, count).Select(i => (long)Math.Round(depthNm * (double)i / count))];

            case ShortLastLap.FoldIn when count > 1 && remainder * 4 < step:
                return [.. Enumerable.Range(1, count - 1).Select(i => i == count - 1 ? depthNm : i * step)];

            default:
                return [.. Enumerable.Range(1, count).Select(i => Math.Min(depthNm, i * step))];
        }
    }

    /// <summary>The laps as a sum a person can check: "0.50 + 0.50 + 0.10 mm".</summary>
    public static string Describe(IReadOnlyList<long> depths)
    {
        ArgumentNullException.ThrowIfNull(depths);

        var previous = 0L;
        var parts = new List<string>(depths.Count);

        foreach (var depth in depths)
        {
            parts.Add(Nm.ToMillimetreString(depth - previous, 2));
            previous = depth;
        }

        return string.Join(" + ", parts) + " mm";
    }

    /// <summary>What the choice is called in the settings, for saying where a lap came from.</summary>
    public static string Name(ShortLastLap policy) => policy switch
    {
        ShortLastLap.SpreadEvenly => "spread evenly",
        ShortLastLap.FoldIn => "folded into the lap before",
        _ => "kept as its own lap",
    };

    /// <summary>
    /// One peck depth that covers the laps in as many equal pecks — for a drilled hole, where the
    /// emitter pecks by a fixed amount. Zero when one plunge does it.
    /// </summary>
    public static long EvenPeck(long depthNm, long stepNm, ShortLastLap policy)
    {
        var depths = Depths(depthNm, stepNm, policy);

        if (depths.Count <= 1)
        {
            return 0;
        }

        return policy == ShortLastLap.OwnLap
            ? stepNm
            : (long)Math.Ceiling(depthNm / (double)depths.Count);
    }
}
