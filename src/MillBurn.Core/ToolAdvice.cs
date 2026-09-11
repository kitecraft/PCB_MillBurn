namespace MillBurn.Core;

/// <summary>
/// What the numbers on a tool say about how it will behave.
///
/// The point of storing the flute count is not the flute count. It is that feed, speed and flutes
/// together give **chipload** — how much each cutting edge takes per revolution — and that is the
/// number which decides whether a 0.4 mm bit lasts one board or twenty. Nobody works it out by
/// hand, and nothing in the feed rate on its own gives it away.
///
/// Advice, never refusal. Every threshold here is a rule of thumb for FR4 on a hobby machine, and
/// somebody who knows their setup better than we do is entitled to ignore all of it.
/// </summary>
public static class ToolAdvice
{
    /// <summary>
    /// Below this, the edge stops cutting and starts rubbing.
    ///
    /// Rubbing is worse than it sounds: it does no work, it work-hardens the copper ahead of the
    /// cut, and it heats and blunts the edge — so a bit run too slowly wears out faster than one
    /// run properly, which is the opposite of what "being gentle with it" feels like.
    /// </summary>
    public const double RubbingBelowNm = 5_000;

    /// <summary>
    /// Above this, on a bit under a millimetre, the edge is asked for more than it can take.
    ///
    /// Scaled by diameter rather than fixed: 50 µm a tooth is unremarkable on a 3 mm cutter and
    /// destroys a 0.4 mm one, because what breaks is the shank, and its strength goes with the cube
    /// of the diameter.
    /// </summary>
    public static double SnappingAboveNm(long diameterNm) =>
        Math.Max(10_000, diameterNm * 0.06);

    /// <summary>What a drill can advance per revolution before it is being pushed rather than cutting.</summary>
    public static double PlungeTooFastAboveNm(long diameterNm) =>
        Math.Max(8_000, diameterNm * 0.05);

    /// <summary>
    /// Everything worth saying about this tool's numbers, or nothing at all.
    /// </summary>
    /// <summary>
    /// Below this, a V-bit tip is finer than anything sold for cutting copper.
    ///
    /// Not a hard limit on what the geometry can express — the arithmetic is happy with any number
    /// — but a limit on what anybody owns. Real engraving bits run from about 0.05 mm to 0.2 mm.
    /// </summary>
    public const long ImplausibleTipNm = 20_000;

    /// <summary>
    /// A gentle word when a V-bit's tip is too fine to be a real tool, or null when it is fine.
    ///
    /// The reason this is worth a sentence rather than nothing: product listings quote tip width in
    /// **inches**, usually without saying so, in a specification where every other dimension is
    /// also inches. "Tip Width: 0.005" is 0.127 mm, and typed straight into a millimetre field it
    /// is a tool twenty-five times finer than the one in your hand.
    ///
    /// Nothing about the resulting file looks wrong. The cut width is computed faithfully from the
    /// tip, the isolation passes are counted faithfully from the cut width, and the first pass is
    /// placed half a cut-width clear of the copper — so the real cutter, being wider than the app
    /// believes, takes that margin out of the trace instead of out of the gap beside it. Traces
    /// come out narrower than drawn, and a thin one can be cut through.
    ///
    /// A notice, never a refusal. Fine bits exist, and the operator is the one holding it.
    /// </summary>
    public static string? TipLooksTooFine(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (tool.Kind != ToolKind.VBit || tool.TipNm <= 0 || tool.TipNm >= ImplausibleTipNm)
        {
            return null;
        }

        var typed = Nm.ToMillimetreString(tool.TipNm, 3);
        var asInches = (long)Math.Round(tool.TipNm * 25.4);

        // Only offer the inch reading when it lands somewhere a real bit could be. Suggesting that
        // 0.0001 mm might be 0.0025 mm helps nobody.
        return asInches is > 20_000 and < 1_000_000
            ? $"A {typed} mm tip is very fine for a carbide bit — worth a second look at the units. "
                + $"Tool listings usually quote this in inches: {typed} in is "
                + $"{Nm.ToMillimetreString(asInches, 3)} mm. This field is millimetres."
            : $"A {typed} mm tip is very fine for a carbide bit — worth a second look at the units. "
                + "This field is millimetres.";
    }

    public static IReadOnlyList<string> For(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var advice = new List<string>();

        if (TipLooksTooFine(tool) is { } tip)
        {
            advice.Add(tip);
        }

        if (tool.Flutes <= 0)
        {
            advice.Add("Flute count is zero, so chipload cannot be checked. Two is the usual answer.");
        }

        if (tool.SpindleRpm <= 0)
        {
            advice.Add("Spindle speed is zero, so nothing about the feeds can be checked.");
            return advice;
        }

        var chip = tool.ChipLoadNm;

        // A V-bit's cutting diameter depends on how deep it is, so the diameter-scaled ceiling has
        // nothing to measure. The rubbing floor still applies: an edge that is not taking a chip is
        // not cutting whatever shape it is.
        var ceiling = tool.Kind == ToolKind.VBit ? double.MaxValue : SnappingAboveNm(tool.DiameterNm);

        // A drill never cuts sideways, so its lateral feed says nothing about how it is working.
        // Judging one on chipload flagged a perfectly sensible drill as "rubbing" because the
        // number it was judged on is not a number that drill will ever use. What matters for a
        // drill is how far it advances per revolution, and that is checked below.
        if (tool.Kind == ToolKind.Drill)
        {
            chip = 0;
        }

        if (chip > 0 && chip < RubbingBelowNm)
        {
            advice.Add(Microns(chip)
                + " per tooth is rubbing, not cutting — it will blunt the tool without removing"
                + " much. Raise the feed or drop the spindle speed.");
        }
        else if (chip > ceiling)
        {
            advice.Add(Microns(chip)
                + " per tooth is a lot for this diameter. Lower the feed or raise the spindle"
                + " speed before it snaps.");
        }

        if (tool.Kind == ToolKind.Drill)
        {
            var perRev = tool.PlungePerRevNm;
            var limit = PlungeTooFastAboveNm(tool.DiameterNm);

            if (perRev > limit)
            {
                advice.Add(Microns(perRev)
                    + " per revolution on the plunge is fast for this drill. This is the number"
                    + " that breaks small ones.");
            }
        }

        return advice;
    }

    /// <summary>
    /// Checks a depth against what the tool can physically reach.
    ///
    /// Separate from <see cref="For"/> because it needs to know what is being asked of the tool,
    /// not just what the tool is.
    /// </summary>
    public static string? DepthConcern(Tool tool, long depthNm)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (tool.FluteLengthNm <= 0 || depthNm <= tool.FluteLengthNm)
        {
            return null;
        }

        var wanted = Nm.ToMillimetreString(depthNm, 2);
        var flute = Nm.ToMillimetreString(tool.FluteLengthNm, 2);

        return $"{wanted} mm is deeper than this tool's {flute} mm of flute. Past the flutes there "
            + "is nothing to clear the chips, so the cut packs and the bit snaps.";
    }

    /// <summary>Nanometres are unreadable at this scale; microns are what people say out loud.</summary>
    private static string Microns(double nm) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{nm / 1000:F1} µm");
}
