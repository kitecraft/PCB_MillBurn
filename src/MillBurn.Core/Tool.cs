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
    /// <summary>
    /// Stable identity, so renaming a tool does not orphan the projects that used it.
    ///
    /// A project *embeds* the tool it was cut with rather than pointing at one by name — the same
    /// reasoning as embedding the Gerbers. If it referenced the library, editing a tip width to
    /// suit a new bit would silently change the toolpaths of every project that ever used it. The
    /// id is how the app can still say "this project's tool differs from the one in your library".
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

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
    /// Depth at which the cone stops widening, because it has reached the shank. Zero means no
    /// limit is known.
    ///
    /// Real engraving bits are a cone ground onto a straight shank, so past that point the cut
    /// width simply stops growing. Modelling it matters because the inverse — "how deep for this
    /// width" — otherwise happily returns a depth the bit cannot reach, and the operator finds out
    /// by plunging a 3.175 mm shank into the board.
    /// </summary>
    public long MaxDepthNm { get; init; }

    /// <summary>
    /// The most this tool should take in one pass. Used by the outline operation; zero falls back
    /// to the operation's own default.
    /// </summary>
    public long StepdownNm { get; init; }

    public string? Notes { get; init; }

    /// <summary>True when the tool is at the end of its cone and cannot cut any wider.</summary>
    public bool IsAtFullWidth(long depthNm) => MaxDepthNm > 0 && depthNm >= MaxDepthNm;

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
        if (MaxDepthNm > 0)
        {
            depth = Math.Min(depth, MaxDepthNm);
        }

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
        if (tan <= 0)
        {
            return -1;
        }

        var depth = (long)Math.Round((widthNm - TipNm) / (2 * tan), MidpointRounding.AwayFromZero);

        // Past the shank the cone stops widening, so a width beyond that is unreachable. Returning
        // a depth the bit physically cannot go to is worse than refusing.
        return MaxDepthNm > 0 && depth > MaxDepthNm ? -1 : depth;
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
        Id = new Guid("00000000-0000-0000-0000-0000000030b1"),
        Name = "30° V-bit, 0.1 mm tip",
        Kind = ToolKind.VBit,
        TipNm = Nm.FromMillimetres(0.1),
        IncludedAngleDegrees = 30,
        MaxDepthNm = Nm.FromMillimetres(1.0),
        FeedMmPerMin = 200,
        PlungeMmPerMin = 60,
        SpindleRpm = 12_000,
    };

    /// <summary>A 1 mm end mill for cutting the board out.</summary>
    public static Tool DefaultOutlineMill { get; } = new()
    {
        Id = new Guid("00000000-0000-0000-0000-000000001000"),
        Name = "1.0 mm end mill",
        Kind = ToolKind.EndMill,
        DiameterNm = Nm.FromMillimetres(1.0),
        StepdownNm = Nm.FromMillimetres(0.4),
        FeedMmPerMin = 300,
        PlungeMmPerMin = 60,
        SpindleRpm = 12_000,
    };

    /// <summary>The template a drill file's sizes are instantiated from.</summary>
    public static Tool DefaultDrill { get; } = new()
    {
        Id = new Guid("00000000-0000-0000-0000-00000000d011"),
        Name = "Drill",
        Kind = ToolKind.Drill,
        DiameterNm = Nm.FromMillimetres(1.0),
        FeedMmPerMin = 100,
        PlungeMmPerMin = 100,
        SpindleRpm = 12_000,
    };

    /// <summary>
    /// A drill of a given size, taking its feeds from a template.
    ///
    /// The diameters come from the drill file and are not the operator's to choose; the feeds and
    /// speed are, and they are what a drill profile is actually for.
    /// </summary>
    public static Tool DrillOf(long diameterNm, Tool? template = null)
    {
        template ??= DefaultDrill;

        return template with
        {
            Id = Guid.NewGuid(),
            Name = Nm.ToMillimetreString(diameterNm, 2) + " mm drill",
            Kind = ToolKind.Drill,
            DiameterNm = diameterNm,
        };
    }

    public override string ToString() => Kind switch
    {
        ToolKind.VBit => string.Create(
            CultureInfo.InvariantCulture,
            $"{Name} ({IncludedAngleDegrees:F0}° included, {Nm.ToMillimetreString(TipNm, 3)} mm tip)"),

        // A drill is fully described by its diameter, and DrillOf names it from exactly that, so
        // the parenthetical only ever said the same thing twice: "1.00 mm drill (1.000 mm)". That
        // is the text an operator reads off the screen when the program stops for a bit change.
        ToolKind.Drill => Name,

        _ => $"{Name} ({Nm.ToMillimetreString(DiameterNm, 3)} mm)",
    };
}
