using System.Globalization;
using MillBurn.Cam;
using MillBurn.Core;

namespace MillBurn.Pipeline;

/// <summary>
/// What an isolation cut will leave connected, said once for every path that asks.
///
/// **There were two planning paths and they answered differently about the same board.**
/// `ExportPlanner` called <see cref="ElectricalCheck.Isolation"/> and named the nets;
/// `JobBuilder`, behind the CLI's `mill` command, called
/// <see cref="IsolationOperation.UnreachableGaps"/> and printed a count. Measured on the Arduino
/// Mega at a matched 0.154 mm cut, on the same day this was written: `mill` said *"19 gap(s) are
/// narrower than the 0.154 mm cut"*, while `export` named eleven groups — +5V and GND among them —
/// and said three more were unnamed. Nineteen *gaps* against fourteen *groups*: both true, in
/// different units, with nothing on either screen to reconcile them.
///
/// **Calling the same check from two places would not have been enough.** Two callers that each
/// ask the checker and then word the answer themselves are two wordings waiting to drift, which is
/// how this happened the first time — the count in `JobBuilder` was right when it was written and
/// was simply never revisited when the named check arrived. So the findings are built here, once,
/// and both paths take what comes back.
///
/// Recorded in [06 §6.42](../../Documentation/06-Roadmap-and-Risks.md), with the measurement.
/// </summary>
public static class ElectricalFindings
{
    /// <summary>
    /// How many shorted groups to name before falling back to a count.
    ///
    /// A board that cannot be isolated at this width usually cannot be isolated in many places at
    /// once, and a hundred lines of them buries every other warning in the list. The Arduino Mega
    /// at 0.05 mm deep is the case that set this: six pairs on the top copper, which is a list
    /// worth reading, against a hundred and ten before the net attribution was fixed, which was
    /// not.
    ///
    /// The other end of the same problem is a single group being enormous: one piece of copper can
    /// hold twenty-three nets — the test board does, at 0.75 mm deep — which is what the cap inside
    /// <see cref="NetJoin.Describe"/> is for. This one caps how many groups; that one caps how many
    /// names within a group.
    /// </summary>
    public const int MaxNamedJoins = 8;

    /// <summary>What the check found: the lines to show, and the one-line verdict for a summary.</summary>
    /// <param name="Checks">Every finding, in the order an operator should meet them.</param>
    /// <param name="Verdict">The summary line — never "all separated" unless it is true.</param>
    /// <param name="Unnamed">
    /// Merges no pair of names could be put to. Exposed because the verdict needs it and because a
    /// caller reporting its own tally must use the same number this used.
    /// </param>
    public sealed record Result(IReadOnlyList<Check> Checks, string Verdict, int Unnamed);

    /// <summary>
    /// Check one copper layer against the cut that is about to be made on it.
    /// </summary>
    /// <param name="layer">The copper, with whatever net attributes it carried.</param>
    /// <param name="options">The isolation as it will actually be cut.</param>
    /// <param name="source">What the resulting checks are about, for the panel to group and colour.</param>
    public static Result For(BoardLayer layer, IsolationOptions options, CheckSource source) =>
        For(layer, options, source, already: null);

    /// <summary>
    /// The same findings, from a check the caller has already run.
    ///
    /// **For the one caller that wants the <see cref="NetCheck"/> itself as well as the
    /// sentences.** `BoardFindings` reads the joins and the net count off the check to build its
    /// view, then asked for the findings too — which ran the whole thing again on the same layer
    /// with the same options. That is a full offset of the copper per layer, on top of the one
    /// <see cref="ElectricalCheck.Gaps"/> does, for an answer already in hand.
    /// </summary>
    /// <param name="layer">The copper, with whatever net attributes it carried.</param>
    /// <param name="options">The isolation as it will actually be cut.</param>
    /// <param name="source">What the resulting checks are about, for the panel to group and colour.</param>
    /// <param name="already">The check's result, or null to run it here.</param>
    public static Result For(
        BoardLayer layer, IsolationOptions options, CheckSource source, NetCheck? already)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(source);

        var width = Nm.ToMillimetreString(options.EffectiveWidthNm, 3);
        var checks = new List<Check>();
        var electrical = already ?? ElectricalCheck.Isolation(layer.Area, layer.Nets, options);

        // Named first, because a net name is something the operator can find in the schematic and
        // "two gaps are too narrow" is something they have to go hunting for on the board.
        foreach (var join in electrical.Joins.Take(MaxNamedJoins))
        {
            checks.Add(Check.Refusal(source, CheckKind.Electrical, Invariant(
                $"{join.Describe()} are left connected: the gap between them is narrower than the {width} mm this cut is wide.")));
        }

        if (electrical.Joins.Count > MaxNamedJoins)
        {
            // Groups, not pairs. One group is a single piece of copper holding two nets or twenty,
            // so counting pairs here would understate a bad board by an order of magnitude and
            // overstate nothing — the test board at 0.75 mm deep is one group holding 23 nets.
            var rest = electrical.Joins.Skip(MaxNamedJoins).ToList();

            // Nets not already printed above, counted once each. Summing the groups would count a
            // net per group it appears in, and a net named in one of the printed groups is not a
            // "further" net at all.
            var shown = electrical.Joins
                .Take(MaxNamedJoins)
                .SelectMany(j => j.Nets)
                .ToHashSet(StringComparer.Ordinal);

            var more = rest.SelectMany(j => j.Nets).Where(n => !shown.Contains(n))
                .Distinct(StringComparer.Ordinal).Count();

            checks.Add(Check.Refusal(source, CheckKind.Electrical, Invariant(
                $"…and {rest.Count} more group(s) this cut cannot separate, naming {more} further net(s).")));
        }

        // What the named check could not speak for. Copper carrying no net attribute merges with
        // its neighbours just as physically and has no name to report it under, so the count is
        // what is left. Reported alongside the names rather than instead of them: before this the
        // count was suppressed the moment anything was named, which told an operator about six
        // shorts on a board where forty gaps could not be cut.
        var unnamed = electrical.Ran
            ? electrical.Unnamed
            : IsolationOperation.UnreachableGaps(layer.Area, options);

        // **The residual, split into the two things it was covering.**
        //
        // It used to be one sentence carefully worded to be true of both: *"the copper either side
        // carries no net, or carries the same one, in which case nothing is shorted"*. That is
        // honest and it asks the reader to do the work — an operator told twenty-five gaps could
        // not be cut had no way to know how many of them mattered. Both halves are known per
        // region, so both are said.
        //
        // "Further" is honest only because named groups were reported above these.
        if (electrical.Nameless > 0 && electrical.Ran)
        {
            // The half that might be a short: copper with no net attribute on it — a fill, a
            // fiducial, an unnamed pour, anything a Protel export wrote. Nothing here can decide
            // whether it matters, and saying so is the point.
            checks.Add(Check.Refusal(source, CheckKind.Electrical, Invariant(
                $"{electrical.Nameless} further gap(s) are narrower than the cut and stay connected, in copper carrying no net name. Nothing here can say whether those matter: look at them.")));
        }

        if (electrical.SameNet > 0 && electrical.Ran)
        {
            // The half that is safe by construction. Reported rather than dropped, because copper
            // left where the design wanted none still matters for soldering and for probing — but
            // reported as advice, because nothing is shorted that was not already joined.
            checks.Add(Check.Advice(source, CheckKind.Electrical, Invariant(
                $"{electrical.SameNet} further gap(s) are narrower than the cut, between two pieces of the same net. Nothing is shorted by those: they were one conductor before the cut.")));
        }

        if (unnamed > 0 && electrical.Ran && electrical.Nameless == 0 && electrical.SameNet == 0)
        {
            // Belt and braces. Nameless + SameNet is Unnamed by construction, so this cannot fire
            // — and if the two ever stop adding up, a silently vanishing count is the worst way to
            // find out. A gap reported as nothing at all is a board that goes to the machine.
            checks.Add(Check.Refusal(source, CheckKind.Electrical, Invariant(
                $"{unnamed} further gap(s) are narrower than the cut and stay connected, and could not be put into either kind. This is a fault in the check itself; treat the board as unverified.")));
        }
        else if (unnamed > 0 && !electrical.Ran)
        {
            // **`!Ran` is load-bearing and was missing.** Without it this fires whenever the check
            // *did* run and found nameless or same-net merges — which is most boards — because the
            // branch above also requires both of those to be zero. The same gaps were then reported
            // twice, the second time with a sentence that is simply false: the test board at
            // 0.75 mm deep said *"20 gap(s) … This layer names no nets"* about a layer naming
            // twenty-three of them, directly under the line that had already counted them. An
            // operator tallying the list by hand doubles the number.
            //
            // The check never ran, so nothing was named, nothing was compared, and there is no
            // "further" to be further than. The reassuring half of the sentence above — that it may
            // be the same net and so harmless — is a conclusion drawn from a comparison that did
            // not happen, and offering it here invites the operator to dismiss gaps nobody checked.
            checks.Add(Check.Refusal(source, CheckKind.Electrical, Invariant(
                $"{unnamed} gap(s) are narrower than the cut: those copper regions stay connected. This layer names no nets, so which of them matters was not determined.")));
        }

        if (electrical.Unplaced > 0)
        {
            checks.Add(Check.Advice(source, CheckKind.Electrical, Invariant(
                $"{electrical.Unplaced} net point(s) could not be placed on this layer's copper, so those nets were not checked. The rest of the layer was.")));
        }

        if (electrical.UnplacedCopper > 0)
        {
            checks.Add(Check.Advice(source, CheckKind.Electrical, Invariant(
                $"{electrical.UnplacedCopper} piece(s) of copper could not be placed, so the count of gaps above may be low.")));
        }

        var verdict = electrical.Ran
            ? Invariant($"{electrical.NetsSeen} nets · {Verdict(electrical, unnamed)}")
            : Invariant($"not checked electrically · {electrical.Silent}");

        return new Result(checks, verdict, unnamed);
    }

    /// <summary>
    /// The one-line verdict beside an isolation program.
    ///
    /// "All separated" has to mean it. Saying it whenever no *named* join was found puts a clean
    /// summary directly above a warning that copper stays connected — the operator reads the line
    /// that agrees with them, and the contradiction is exactly what <see cref="NetCheck.Silent"/>
    /// exists elsewhere to prevent.
    /// </summary>
    private static string Verdict(NetCheck check, int unnamed)
    {
        var parts = new List<string>();

        if (check.Joins.Count > 0)
        {
            // Distinct nets, not the sum of each group's count: a ground pour shorted in three
            // places appears in three groups and would be counted three times, inflating the
            // headline number an operator reads first.
            var caught = check.Joins
                .SelectMany(j => j.Nets)
                .Distinct(StringComparer.Ordinal)
                .Count();

            parts.Add(Invariant($"{caught} left connected in {check.Joins.Count} group(s)"));
        }

        // Carried whether or not something was named. Dropping it as soon as a join exists is the
        // same suppression that was fixed in the warning list, re-done on the line that is read
        // first — and a summary that stops short of the warnings beneath it is worse than no
        // summary, because it is the one the operator takes away.
        if (unnamed > 0)
        {
            parts.Add(Invariant($"{unnamed} gap(s) uncut"));
        }

        // Nets the check could not place are nets it did not look at, and "all separated" must
        // never be said over them.
        if (check.Unplaced > 0)
        {
            parts.Add(Invariant($"{check.Unplaced} net point(s) not placed"));
        }

        return parts.Count == 0 ? "all separated" : string.Join(" · ", parts);
    }

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
