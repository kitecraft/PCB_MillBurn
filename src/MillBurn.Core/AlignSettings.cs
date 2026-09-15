namespace MillBurn.Core;

/// <summary>How the drill alignment test brings the bit down over a hole.</summary>
public sealed record AlignSettings
{
    /// <summary>
    /// How far above the surface the bit stops.
    ///
    /// A tenth of a millimetre: close enough to judge a pad's centre by eye with the tip almost on
    /// it, far enough that a Z zero a few hundredths out does not put the tip into the copper.
    /// </summary>
    public double HoverMm { get; init; } = 0.1;

    /// <summary>
    /// The feed for the last millimetre down. Slow, so it can be stopped by hand if the tip is
    /// heading for the copper rather than for the air above it.
    /// </summary>
    public double FeedMmPerMin { get; init; } = 100;
}
