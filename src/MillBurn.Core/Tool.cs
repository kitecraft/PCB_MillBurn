using System.Globalization;

namespace MillBurn.Core;

/// <summary>What the cutter is.</summary>
public enum ToolKind
{
    /// <summary>A conical engraving bit. Its cut width depends on how deep it goes.</summary>
    VBit,

    /// <summary>Straight-sided. Cut width is the diameter, whatever the depth.</summary>
    EndMill,

    Drill,
}

/// <summary>
/// A cutter, and the feeds to run it at.
///
/// Feeds live here rather than on the operation because in practice they belong to the tool and the
/// material together, and PCB work is nearly always the same material. Splitting them out is a
/// Phase 5 concern once machine profiles are real.
/// </summary>
public sealed record Tool
{
    public required string Name { get; init; }

    public required ToolKind Kind { get; init; }

    /// <summary>Cutting diameter of an end mill or drill. Not meaningful for a V-bit.</summary>
    public long DiameterNm { get; init; }

    /// <summary>
    /// The flat at the very point of a V-bit. Never zero on a real bit — a true point would have no
    /// strength — and it is what stops the cut width going to zero at zero depth.
    /// </summary>
    public long TipNm { get; init; }

    /// <summary>
    /// The **included** angle of the cone: the full angle across the V, not the half angle.
    ///
    /// Worth being explicit about, because both conventions are in circulation. A bit sold as "30
    /// degree" is almost always 30 degrees included, and reading it as a half angle would put the
    /// cut width out by a factor of two.
    /// </summary>
    public double IncludedAngleDegrees { get; init; }

    public long FeedMmPerMin { get; init; } = 200;

    public long PlungeMmPerMin { get; init; } = 60;

    public int SpindleRpm { get; init; } = 12_000;

    /// <summary>
    /// How wide a groove this tool cuts at a given depth below the surface.
    ///
    /// For a V-bit this is the whole game:
    /// <code>width = tip + 2 * depth * tan(included / 2)</code>
    /// Cut width is therefore a *function of depth*, which is why isolation milling with a V-bit is
    /// unforgiving: the width is set by how accurately the machine holds Z over a board that is
    /// never flat. At 30 degrees included, <c>d(width)/d(depth)</c> is about 0.54 — so being
    /// 0.05 mm deep in one corner widens the cut there by 27 um.
    ///
    /// That is the argument for height mapping, and the reason the effective width is shown as a
    /// computed number in the UI rather than typed in as a setting.
    /// </summary>
    public long WidthAtDepth(long depthNm)
    {
        if (Kind is not ToolKind.VBit)
        {
            return DiameterNm;
        }

        var depth = Math.Max(0, depthNm);
        var halfAngle = IncludedAngleDegrees * Math.PI / 360.0;
        return TipNm + (long)Math.Round(2 * depth * Math.Tan(halfAngle), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// How deep to go for a given cut width — the inverse of <see cref="WidthAtDepth"/>.
    ///
    /// Returns zero when the width is already covered by the tip, and −1 when a straight tool
    /// simply cannot make a cut that narrow: an end mill has one width and no amount of depth
    /// changes it, so asking for less is a request that has to be refused rather than rounded.
    /// </summary>
    public long DepthForWidth(long widthNm)
    {
        if (Kind is not ToolKind.VBit)
        {
            return widthNm >= DiameterNm ? 0 : -1;
        }

        if (widthNm <= TipNm)
        {
            return 0;
        }

        var halfAngle = IncludedAngleDegrees * Math.PI / 360.0;
        var tan = Math.Tan(halfAngle);
        return tan <= 0 ? -1 : (long)Math.Round((widthNm - TipNm) / (2 * tan), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// How much the cut widens per unit of depth error. Zero for a straight tool.
    ///
    /// Surfaced because it is the number that decides whether a board needs height mapping, and it
    /// is not obvious: a shallower V is *more* sensitive, not less.
    /// </summary>
    public double WidthPerDepth => Kind is ToolKind.VBit
        ? 2 * Math.Tan(IncludedAngleDegrees * Math.PI / 360.0)
        : 0;

    // ------------------------------------------------------------------ defaults

    /// <summary>
    /// A 30-degree, 0.1 mm-tip engraving bit: the usual choice for isolation milling, and narrow
    /// enough to separate 0.2 mm traces without the depth becoming unmanageable.
    /// </summary>
    public static Tool DefaultVBit { get; } = new()
    {
        Name = "30° V-bit, 0.1 mm tip",
        Kind = ToolKind.VBit,
        TipNm = Nm.FromMillimetres(0.1),
        IncludedAngleDegrees = 30,
        FeedMmPerMin = 200,
        PlungeMmPerMin = 60,
        SpindleRpm = 12_000,
    };

    /// <summary>A 1 mm end mill for cutting the board out.</summary>
    public static Tool DefaultOutlineMill { get; } = new()
    {
        Name = "1.0 mm end mill",
        Kind = ToolKind.EndMill,
        DiameterNm = Nm.FromMillimetres(1.0),
        FeedMmPerMin = 300,
        PlungeMmPerMin = 60,
        SpindleRpm = 12_000,
    };

    public static Tool DrillOf(long diameterNm) => new()
    {
        Name = Nm.ToMillimetreString(diameterNm, 2) + " mm drill",
        Kind = ToolKind.Drill,
        DiameterNm = diameterNm,
        FeedMmPerMin = 100,
        PlungeMmPerMin = 100,
        SpindleRpm = 12_000,
    };

    public override string ToString() => Kind switch
    {
        ToolKind.VBit => string.Create(
            CultureInfo.InvariantCulture,
            $"{Name} ({IncludedAngleDegrees:F0}° included, {Nm.ToMillimetreString(TipNm, 3)} mm tip)"),
        _ => $"{Name} ({Nm.ToMillimetreString(DiameterNm, 3)} mm)",
    };
}
