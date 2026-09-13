using System.Globalization;
using MillBurn.Core;

namespace MillBurn.Cam;

/// <summary>A cutter, or the reason there is not one. Never both, and never neither.</summary>
public readonly record struct ToolChoice(Tool? Tool, string? Refusal)
{
    public static ToolChoice Refused(string why) => new(null, why);

    public static ToolChoice Chosen(Tool tool) => new(tool, null);

    public bool Found => Tool is not null;
}

/// <summary>
/// Picks a cutter out of the library, or refuses.
///
/// The drilling path has never looked at the tool library at all: <c>Tool.DrillOf</c> synthesises a
/// drill of exactly the diameter the file asked for. That is right for drilling and only for
/// drilling — a hole is made by a bit its own size, so the file's diameter *is* the answer, and the
/// companion page tells the operator which bits to fit.
///
/// Routing cannot work that way. There is no cutter of exactly the right size to make a 2 mm slot;
/// there is a set of end mills you own, of which some are narrow enough. So this reads the library
/// and **synthesises nothing** — the moment a program depends on a tool you might not own,
/// inventing one is how a file gets written for a machine that cannot run it.
///
/// **Largest that fits, not smallest.** A wider cutter clears the same slot in fewer passes and
/// deflects less. The constraint is the slot's width, so the best tool is the one closest to it
/// from below.
/// </summary>
public static class ToolChooser
{
    /// <summary>
    /// Rounding slack when comparing a cutter against a width, in nanometres.
    ///
    /// A 1.0 mm slot and a 1.0 mm end mill are the same size, and a file that stores the width as
    /// 0.999998 mm — which Excellon in inches routinely does — must not turn that into a refusal.
    /// One micron is far below anything the machine resolves and far above the rounding.
    /// </summary>
    public const long SlackNm = 1_000;

    /// <summary>
    /// The widest end mill that fits inside <paramref name="widthNm"/> and reaches
    /// <paramref name="depthNm"/>.
    /// </summary>
    /// <remarks>
    /// <c>MaxDepthNm</c> and <c>FluteLengthNm</c> are already on <see cref="Tool"/> and were read by
    /// nobody in this path. Zero means the operator has not said, which is not the same as zero
    /// depth — the stock library sets neither on its end mills — so an unstated limit does not
    /// disqualify a cutter. A stated one does.
    /// </remarks>
    public static ToolChoice ForWidth(ToolLibrary library, long widthNm, long depthNm)
    {
        ArgumentNullException.ThrowIfNull(library);

        var mills = library.OfKind(ToolKind.EndMill).Where(t => t.DiameterNm > 0).ToList();

        if (mills.Count == 0)
        {
            return ToolChoice.Refused("there are no end mills in the tool library");
        }

        var narrow = mills.Where(t => t.DiameterNm <= widthNm + SlackNm).ToList();

        if (narrow.Count == 0)
        {
            var smallest = mills.MinBy(t => t.DiameterNm)!;

            return ToolChoice.Refused(Invariant(
                $"no end mill is narrow enough — the smallest in the library is {Mm(smallest.DiameterNm)} mm ({smallest.Name})"));
        }

        // Depth second, so that "too narrow" and "cannot reach" are told apart. They want different
        // answers from the operator: a different cutter, or a longer one.
        var reaching = narrow.Where(t => Reaches(t, depthNm)).ToList();

        if (reaching.Count == 0)
        {
            var best = narrow.MaxBy(t => Math.Max(t.MaxDepthNm, t.FluteLengthNm))!;

            return ToolChoice.Refused(Invariant(
                $"no end mill that narrow reaches {Mm(depthNm)} mm — the deepest is {best.Name} at {Mm(Limit(best))} mm"));
        }

        return ToolChoice.Chosen(reaching.MaxBy(t => t.DiameterNm)!);
    }

    /// <summary>
    /// The widest end mill that can spiral out a hole of <paramref name="diameterNm"/>.
    ///
    /// A cutter needs room to spiral. One the same size as the hole is a drill being asked to be a
    /// mill: it would descend on its own axis with every flute buried, which is the plunge this
    /// exists to avoid. So the cutter has to leave a helix of real radius, and a hole that cannot
    /// give it one is refused rather than turned into a helix of zero radius.
    /// </summary>
    public static ToolChoice ForHole(ToolLibrary library, long diameterNm, long depthNm)
    {
        ArgumentNullException.ThrowIfNull(library);

        var chosen = ForWidth(library, diameterNm, depthNm);

        if (!chosen.Found)
        {
            return chosen;
        }

        // Widest first, then narrower, because the widest that *fits* may still not leave a helix.
        var candidates = library.OfKind(ToolKind.EndMill)
            .Where(t => t.DiameterNm > 0 && t.DiameterNm <= diameterNm + SlackNm && Reaches(t, depthNm))
            .OrderByDescending(t => t.DiameterNm);

        foreach (var tool in candidates)
        {
            if (diameterNm - tool.DiameterNm >= MinimumHelixNm * 2)
            {
                return ToolChoice.Chosen(tool);
            }
        }

        var narrowest = library.OfKind(ToolKind.EndMill)
            .Where(t => t.DiameterNm > 0 && Reaches(t, depthNm))
            .MinBy(t => t.DiameterNm);

        return narrowest is null
            ? ToolChoice.Refused(Invariant($"no end mill reaches {Mm(depthNm)} mm"))
            : ToolChoice.Refused(Invariant(
                $"{Mm(diameterNm)} mm is too big to drill and too small to mill — the narrowest cutter that reaches is {narrowest.Name}, which would leave a helix of {Mm((diameterNm - narrowest.DiameterNm) / 2)} mm"));
    }

    /// <summary>
    /// Half the smallest helix worth cutting: 0.1 mm of radius.
    ///
    /// Below this the cutter is orbiting inside its own kerf and the "hole" is a plunge with extra
    /// steps.
    /// </summary>
    public static long MinimumHelixNm { get; } = Nm.FromMillimetres(0.1);

    /// <summary>
    /// Drill sizes the board asks for that the library has never heard of.
    ///
    /// Not a refusal — you may well own the bit and not have entered it — but the export window is
    /// the right place to find out that the run stops for a 0.30 mm bit you do not have.
    /// </summary>
    public static IReadOnlyList<long> DrillsNotInLibrary(ToolLibrary library, IEnumerable<long> wantedNm)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(wantedNm);

        var owned = library.OfKind(ToolKind.Drill).Where(t => t.DiameterNm > 0).Select(t => t.DiameterNm).ToList();

        // A library with no drills in it at all says nothing about which sizes are missing — it is
        // the state every library starts in, and a list of every size on the board would be noise
        // rather than a warning.
        return owned.Count == 0
            ? []
            : [.. wantedNm.Distinct().Where(w => !owned.Any(o => Math.Abs(o - w) <= SlackNm)).Order()];
    }

    /// <summary>The biggest drill the operator says they own, or zero when they have said nothing.</summary>
    public static long LargestDrill(ToolLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        return library.OfKind(ToolKind.Drill).Select(t => t.DiameterNm).DefaultIfEmpty(0).Max();
    }

    private static bool Reaches(Tool tool, long depthNm) =>
        (tool.MaxDepthNm <= 0 || tool.MaxDepthNm >= depthNm)
        && (tool.FluteLengthNm <= 0 || tool.FluteLengthNm >= depthNm);

    private static long Limit(Tool tool) => new[] { tool.MaxDepthNm, tool.FluteLengthNm }
        .Where(v => v > 0)
        .DefaultIfEmpty(0)
        .Min();

    private static string Mm(long nm) => Nm.ToMillimetreString(nm, 2);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
