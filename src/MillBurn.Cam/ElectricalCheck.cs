using System.Globalization;
using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;

namespace MillBurn.Cam;

/// <summary>Copper the isolation pass cannot divide, and the nets left sharing it.</summary>
public sealed record NetJoin
{
    /// <summary>The nets sharing this copper, in name order. Always two or more.</summary>
    public required IReadOnlyList<string> Nets { get; init; }

    /// <summary>
    /// One of these nets' own points, inside the copper they share.
    ///
    /// **Not the gap that joins them**, and the name would flatter it if read that way. It is the
    /// first net point that landed in this piece of copper — a pad centre or a trace midpoint — so
    /// on a ground pour it can be tens of millimetres from the narrow place the tool could not
    /// reach. It is enough to select the right piece of copper and not enough to point at the
    /// fault, and a viewer built on it should say "this copper" rather than "here".
    ///
    /// Finding the gap itself means intersecting the two nets' grown outlines and taking a point in
    /// the overlap, which is a second offset per join and buys nothing the message needs: the
    /// message names the nets, because a net name is something the operator looks up in the
    /// schematic and a coordinate is something they go and hunt for.
    /// </summary>
    public required Point2 Near { get; init; }

    /// <summary>
    /// The nets, capped. A group is one piece of copper holding two nets or twenty-three, and
    /// spelling out twenty-three of them makes a six-hundred-character sentence in a list the
    /// operator is meant to read at a glance.
    /// </summary>
    public string Describe(int most = 6) =>
        Nets.Count <= most
            ? string.Join(" and ", Nets)
            : string.Join(" and ", Nets.Take(most))
                + Invariant($" and {Nets.Count - most} more");

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// One place where two pieces of copper stay joined, and the nets either side of it.
///
/// **This is the answer <see cref="NetJoin.Near"/> deliberately does not give.** `Near` is one of a
/// net's own points inside the shared copper — a pad centre or a trace midpoint — which on a ground
/// pour can be tens of millimetres from the narrow place the tool could not reach. It is enough to
/// select the right piece of copper and not enough to point at the fault.
///
/// This is the fault. It is found by growing each piece of artwork on its own and intersecting the
/// results: where two separately-grown pieces overlap is exactly where a cut of this width cannot
/// fit between them. That is an offset per piece and a boolean per candidate pair, which is why it
/// is not on the path that runs during every export — <see cref="ElectricalCheck.Isolation"/> stays
/// cheap and says *which copper*, and this is asked for only when somebody opens the findings view
/// and has therefore asked for the expensive answer.
/// </summary>
public sealed record NetGap
{
    /// <summary>
    /// The nets either side, in name order.
    ///
    /// **Can be empty, and can hold one name.** Copper carrying no net attribute bridges just as
    /// physically and has no name to report it under; two pieces of the same net give one name and
    /// are electrically nothing, which is story 3's split arriving here as a fact about a place
    /// rather than as a tally. The view says which kind each one is, so a reader can see the
    /// difference instead of being told a number.
    /// </summary>
    public required IReadOnlyList<string> Between { get; init; }

    /// <summary>Where. A point inside the copper that bridges the gap.</summary>
    public required Point2 At { get; init; }

    /// <summary>
    /// Every net named anywhere in the merged copper this bridge is part of, in name order.
    /// </summary>
    /// <remarks>
    /// **This is what ties a place to the group that named it**, and it is a set of names rather
    /// than an index on purpose. The obvious way to match the two — have both methods number their
    /// regions and compare the numbers — couples them through the order
    /// <see cref="Polygons.Separate"/> happens to return, computed twice from the same input in two
    /// different methods. It works and it is a trap: anything that changes one traversal silently
    /// mismatches every place against the wrong group.
    ///
    /// Matching on the names is derived from the board on both sides, and it is the comparison the
    /// reader would make anyway. It matters because <see cref="Between"/> alone is not enough: a
    /// ground pour reaches many regions, so a GND–USHIELD bridge whose own two pieces are named
    /// that could be filed under every group holding both names — which is exactly what the first
    /// version of the findings view did, listing one bridge under two separate groups and making
    /// the board look worse than it is.
    /// </remarks>
    public required IReadOnlyList<string> RegionNets { get; init; }

    /// <summary>
    /// Whether anything is actually shorted here.
    ///
    /// Two distinct names is a short. One name is the same conductor meeting itself, which the cut
    /// equally cannot divide and which shorts nothing. No name at all is the honest middle: this is
    /// a bridge nobody can speak for.
    /// </summary>
    public bool IsShort => Between.Count >= 2;

    /// <summary>The same-net case, which is safe by construction and still worth seeing.</summary>
    public bool IsSameNet => Between.Count == 1;
}

/// <summary>What an electrical check of one copper layer found — or why it could not look.</summary>
public sealed record NetCheck
{
    /// <summary>Nets the isolation leaves connected. Empty when it separates every one of them.</summary>
    public required IReadOnlyList<NetJoin> Joins { get; init; }

    /// <summary>How many distinct nets were placed on this layer.</summary>
    public required int NetsSeen { get; init; }

    /// <summary>
    /// How many separate pieces of copper the cut fails to divide — the same count
    /// <see cref="IsolationOperation.UnreachableGaps"/> returns, computed here from geometry this
    /// already had rather than by offsetting the whole layer a second time.
    ///
    /// **It is not the same number as <see cref="Joins"/>, and the difference is the point.** A
    /// join needs two named nets in one piece of copper. Copper carrying no `.N` attribute — a
    /// fill, a fiducial, an unnamed pour, anything a Protel export wrote — merges with its
    /// neighbours just as physically and cannot be named. Reporting only the joins would leave the
    /// operator told about six shorts on a board where forty gaps could not be cut.
    /// </summary>
    public int Merged { get; init; }

    /// <summary>
    /// Merges this check could not put a pair of names to, which is what is left for a count.
    ///
    /// **Two quite different things land here**, and the wording of anything built on it has to
    /// allow for both. One is copper carrying no net attribute at all, which is a gap the tool
    /// cannot cut and a short nobody can name. The other is two pieces of the *same* net being
    /// joined — a gap the tool equally cannot cut, and electrically nothing at all, because they
    /// were the same conductor already.
    ///
    /// What it is **not** is "merges minus groups reported". One group can absorb any number of
    /// merges — as many as its region has pieces — so subtracting group counts leaves the rest
    /// charged to nobody. Measured on the author's test board at 0.75 mm deep: one named group of
    /// 23 nets over 63 merges, of which that subtraction called 62 unnamed, on a board with no
    /// unnamed copper at all. Each region's merges are attributed to it instead.
    /// </summary>
    public int Unnamed { get; init; }

    /// <summary>
    /// Merges where one piece of copper met another piece of the same net.
    ///
    /// **Electrically nothing**, and worth saying so plainly: the two pieces were one conductor
    /// before the cut and are one conductor after it. The tool could not fit between them, which
    /// is a fact about the geometry and not about the circuit, and an operator who has been told
    /// twenty-five gaps could not be cut deserves to know how many of them are this.
    ///
    /// It is still not *nothing at all*. A trace the cutter could not separate from its own pad is
    /// copper left where the design wanted none, which matters for solderability and for anyone
    /// probing the board — so it is reported, and reported as what it is.
    /// </summary>
    public int SameNet { get; init; }

    /// <summary>
    /// Merges where the copper carried no net attribute at all, so nothing could be named.
    ///
    /// **This is the half that might be a short.** A fill, a fiducial, an unnamed pour, anything a
    /// Protel export wrote: it fuses with its neighbour exactly as a named net would and the check
    /// has no name to report it under. Whether it matters cannot be decided here, which is the
    /// reason it is counted separately rather than folded in with <see cref="SameNet"/> — one of
    /// the two is safe by construction and the other is unknown, and averaging them into a single
    /// number told the operator neither.
    /// </summary>
    public int Nameless { get; init; }

    /// <summary>
    /// Net points that could not be placed in any piece of grown copper, which should be none.
    ///
    /// A point inside the copper is inside the copper grown outwards, so this is a should-not
    /// rather than a cannot — a point landing exactly on an inflated ring is refused by the same
    /// strictness that keeps a net off a boundary. Counted because a net that failed to place
    /// cannot appear in any join, while <see cref="NetsSeen"/> still counts it: without this the
    /// layer would read as checked when part of it was not.
    /// </summary>
    public int Unplaced { get; init; }

    /// <summary>
    /// Pieces of artwork that could not be placed in any piece of grown copper, which should be
    /// none, and whose absence makes <see cref="Merged"/> too small rather than too large.
    ///
    /// Same reasoning as <see cref="Unplaced"/> and the opposite direction of harm: a net that
    /// fails to place cannot be accused, but a piece of copper that fails to place cannot be
    /// counted, and a merge tally that reads low is one that lets a board through.
    /// </summary>
    public int UnplacedCopper { get; init; }

    /// <summary>
    /// Why the check could not run, or empty when it did.
    ///
    /// The distinction matters more than it looks. "No shorts found" and "I had nothing to look for"
    /// read the same in a list of green ticks, and only one of them is a reason to trust the board.
    /// A Protel export carries no net attributes at all, and a caller that cannot tell the two apart
    /// will quietly promise the operator something it never checked.
    /// </summary>
    public string Silent { get; init; } = string.Empty;

    public bool Ran => Silent.Length == 0;

    public static NetCheck CouldNot(string why, int netsSeen = 0) =>
        new() { Joins = [], NetsSeen = netsSeen, Silent = why };
}

/// <summary>
/// Whether an isolation pass really separates the nets the Gerbers declare.
///
/// **The geometry is the one <see cref="IsolationOperation.UnreachableGaps"/> already uses**, which
/// is where the idea is proved: grow every piece of copper by half of what the tool cuts, and any
/// two pieces whose grown outlines meet have no room between them for the tool to pass. After the
/// job runs they are still one piece of copper. What that method returns is a count — "two islands
/// could not be reached" — which tells an operator that something is wrong and nothing about what.
///
/// This names them. The X2 net attributes have been parsed since M4 and, until the realiser started
/// carrying them through, thrown away; a net point is a net name and a point known to lie strictly
/// inside the copper that object left behind. Put the two together and the gap that could not be
/// cut stops being a number and becomes "SDA and SCL are still connected".
///
/// **Shorts only, and deliberately.** Isolation cuts outside the copper edge — the first pass runs
/// half a cut width clear of it and every later pass steps further out — so it does not remove
/// copper from inside a net and cannot sever one. A severed-net check here would be a check that
/// can never fire, and this repository has already been bitten once by a test that could only pass.
/// Copper does come off inside a net when a pocket is cleared or an outline is cut through a trace,
/// and that is a different operation to check, with a different piece of geometry behind it.
/// </summary>
public static class ElectricalCheck
{
    public static NetCheck Isolation(
        Paths64 copper, IReadOnlyList<NetPoint> nets, IsolationOptions options)
    {
        ArgumentNullException.ThrowIfNull(copper);
        ArgumentNullException.ThrowIfNull(nets);
        ArgumentNullException.ThrowIfNull(options);

        // `%TO.N,*%` is how KiCad says this copper belongs to no net, and it arrives here as an
        // empty name. It is not a net that can be shorted to anything and it is not a name the
        // operator can look up, so it takes no part: the copper it marks still joins whatever it
        // touches, and the real nets either side of it are named on their own account.
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var point in nets)
        {
            if (!string.IsNullOrWhiteSpace(point.Net))
            {
                named.Add(point.Net);
            }
        }

        if (named.Count == 0)
        {
            return NetCheck.CouldNot("this layer declares no nets, so there is nothing to check it against");
        }

        if (named.Count == 1)
        {
            // One net cannot be shorted to anything. Said plainly rather than reported as a clean
            // board: a layer that is all ground pour passes this check by having no question in it.
            return NetCheck.CouldNot("only one net is named on this layer, which nothing can be shorted to", named.Count);
        }

        var reach = options.EffectiveWidthNm / 2;

        if (reach <= 0)
        {
            return NetCheck.CouldNot("the tool cuts nothing at this depth, so no gap is reachable", named.Count);
        }

        // Half a cut width, which is where the first pass's centreline runs. Bias is deliberately
        // left out: it pushes the cut further from the copper and so can only make two pieces
        // harder to separate, and a check that reported a short the operator could fix by setting
        // bias back to zero would be reporting the setting rather than the board.
        var grown = Polygons.Inflate(
            copper, reach, JoinType.Round, EndType.Polygon, arcTolerance: options.SagittaNm);

        var regions = Polygons.Separate(grown).ToList();

        if (regions.Count == 0)
        {
            return NetCheck.CouldNot("the copper grows to nothing at this width", named.Count);
        }

        var boxes = BoxesOf(regions);

        // How many pieces of artwork fell into each grown region.
        //
        // **A subtraction of totals is not enough**, which is the whole reason this loop exists. A
        // region holding twelve pieces swallowed eleven merges, not one, so "merges minus reported
        // groups" charges the other ten to nobody and reports them as copper nothing could name. On
        // the author's test board at 0.75 mm deep that was one named group of 23 nets, 63 merges,
        // and 62 of them announced as unnamed on a board with no unnamed copper at all.
        //
        // A vertex of the artwork is safe to locate with: the grown copper is the artwork offset
        // outwards by a positive amount, so every point of the original — boundary included — is
        // strictly inside it.
        var artwork = Polygons.Separate(copper).ToList();
        var pieces = new int[regions.Count];
        var strays = 0;

        foreach (var piece in artwork)
        {
            if (piece.Count == 0 || piece[0].Count == 0)
            {
                continue;
            }

            var found = RegionAt(regions, boxes, new Point2(piece[0][0].X, piece[0][0].Y));

            if (found >= 0)
            {
                pieces[found]++;
            }
            else
            {
                // Should not happen — the vertex is interior to an outward offset unless the arc
                // tolerance is coarser than the offset itself. Counted rather than shrugged off,
                // because a piece that does not locate is a piece missing from the merge tally, and
                // the tally only ever errs downwards: it would report fewer uncut gaps than there
                // are, which is the direction that lets a board through.
                strays++;
            }
        }

        var merged = 0;
        foreach (var count in pieces)
        {
            if (count > 1)
            {
                merged += count - 1;
            }
        }

        // Which nets each piece of grown copper holds, and one point in it to point at.
        var inRegion = new Dictionary<int, (SortedSet<string> Nets, Point2 First)>();
        var dropped = 0;

        foreach (var point in nets)
        {
            if (string.IsNullOrWhiteSpace(point.Net))
            {
                continue;
            }

            var where = RegionAt(regions, boxes, point.At);

            if (where < 0)
            {
                // A point inside the copper is inside the copper grown outwards, so this should not
                // happen — but "should not" is not "cannot", and a point landing exactly on an
                // inflated ring is refused by the same strictness that keeps a net off a boundary.
                // It is not worth failing an export over. It is worth counting: a net that quietly
                // failed to place cannot appear in any join, and this module's whole argument is
                // that an unchecked thing must not read as a checked one.
                dropped++;
                continue;
            }

            if (inRegion.TryGetValue(where, out var found))
            {
                found.Nets.Add(point.Net);
            }
            else
            {
                inRegion[where] = (new SortedSet<string>(StringComparer.Ordinal) { point.Net }, point.At);
            }
        }

        var joins = new List<NetJoin>();

        foreach (var (index, held) in inRegion)
        {
            if (held.Nets.Count >= 2)
            {
                joins.Add(new NetJoin { Nets = [.. held.Nets], Near = held.First });
            }
        }

        // **Which kind of unnamed each merge is.** The residual used to be one number covering two
        // things that mean opposite amounts of trouble: copper carrying no net attribute, which is
        // a gap nobody can name and might be a short; and two pieces of the *same* net rejoining,
        // which the tool equally cannot cut and which is electrically nothing at all, because they
        // were one conductor before the cut and are one conductor after it.
        //
        // An operator reading "25 gaps" could not tell how many mattered, and the honest wording
        // that allowed for both — "the copper either side carries no net, or carries the same one,
        // in which case nothing is shorted" — is a sentence that asks the reader to do the work.
        //
        // The machinery was already here. Every region's merges are attributed to that region, so
        // the only question is how many names the region holds: two or more is a join and is
        // reported by name; exactly one is the same net meeting itself; none is copper that cannot
        // be spoken for.
        var sameNet = 0;
        var nameless = 0;

        for (var index = 0; index < pieces.Length; index++)
        {
            if (pieces[index] <= 1)
            {
                continue;
            }

            var extra = pieces[index] - 1;
            var names = inRegion.TryGetValue(index, out var held) ? held.Nets.Count : 0;

            if (names >= 2)
            {
                // Every merge inside this region is accounted for by the group reported above, so
                // it is not part of the residual and nothing needs to be added anywhere. It was
                // counted into an `explained` total for a while, which nothing ever read —
                // `Unnamed` is `sameNet + nameless` — and a compound assignment does not warn, so
                // it sat here looking load-bearing.
            }
            else if (names == 1)
            {
                sameNet += extra;
            }
            else
            {
                nameless += extra;
            }
        }

        // A total order, so that two runs of the same board report in the same order.
        //
        // Keying on the first net alone is not enough and the reason is the commonest shape of this
        // fault: a ground pour shorted to something in two separate places gives two joins that
        // both begin "GND", and `List.Sort` is introsort, which is not stable. They would come back
        // in whichever order the dictionary happened to enumerate — and on a board with more joins
        // than can be named, *which* ones get named would change between runs of the same file.
        // Compared through the whole sequence, then by position, so that two joins holding exactly
        // the same nets in different places still order predictably.
        joins.Sort(Order);

        return new NetCheck
        {
            Joins = joins,
            NetsSeen = named.Count,
            Merged = merged,

            // Summed from the two kinds rather than subtracted from the total. The old
            // `merged - explained` gives the same answer and says nothing about which kind, and
            // keeping the subtraction alongside the split would be two ways of computing one
            // number, waiting to disagree.
            Unnamed = sameNet + nameless,
            SameNet = sameNet,
            Nameless = nameless,
            Unplaced = dropped,
            UnplacedCopper = strays,
        };
    }

    /// <summary>
    /// Every place this cut leaves two pieces of copper joined, with the nets either side.
    ///
    /// **The expensive answer, and it is only ever asked for deliberately.** Where
    /// <see cref="Isolation"/> says *this copper holds +5V and GND*, this says *here, at 41.6,
    /// 22.3 mm*. Getting from one to the other costs an offset per piece of artwork and a boolean
    /// per surviving pair, which is unjustifiable for a warning line and reasonable for a view
    /// somebody opened.
    ///
    /// **How it works, and why it is exact rather than approximate.** The isolation pass runs its
    /// centreline half a cut width from the copper, so two pieces can be separated exactly when
    /// their outlines grown by that half-width do not meet. Growing each piece *on its own* and
    /// intersecting two of them therefore answers the real question directly: the overlap is the
    /// set of places a cut of this width cannot get between them, and any point in it is a point
    /// the operator can go and look at.
    ///
    /// **Pairs are rejected by their boxes first**, which is what keeps this tractable. A board's
    /// copper is mostly nowhere near any given piece, and two pieces whose bounding boxes do not
    /// come within a cut width cannot possibly bridge — four comparisons each, against an offset
    /// and a boolean for the ones that survive. On the Arduino Mega that is the difference between
    /// examining every pair of 245 islands and examining the handful that touch.
    /// </summary>
    /// <param name="copper">The layer's artwork, as realised.</param>
    /// <param name="nets">The net points, used to name the copper either side of each bridge.</param>
    /// <param name="options">The isolation as it will actually be cut.</param>
    /// <param name="token">Checked per candidate pair; a cancelled run returns what it has.</param>
    public static IReadOnlyList<NetGap> Gaps(
        Paths64 copper,
        IReadOnlyList<NetPoint> nets,
        IsolationOptions options,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(copper);
        ArgumentNullException.ThrowIfNull(nets);
        ArgumentNullException.ThrowIfNull(options);

        var reach = options.EffectiveWidthNm / 2;

        if (reach <= 0)
        {
            // The same refusal `Isolation` makes: a cut of no width separates nothing, so there is
            // no gap to be narrower than it.
            return [];
        }

        var artwork = Polygons.Separate(copper).ToList();

        if (artwork.Count < 2)
        {
            // One piece of copper cannot bridge to anything, and nought pieces still less.
            return [];
        }

        var boxes = BoxesOf(artwork);

        // **Only pieces that merged into the same region can bridge**, and this is what makes the
        // whole thing tractable rather than merely possible.
        //
        // The first version paired every piece against every other and rejected the far ones by
        // their boxes, which sounds sufficient and is not: the largest piece on a real board is the
        // ground pour, its outer box is the whole board, and so no other piece is ever rejected
        // against it. Every one of them then paid for a boolean against a two-hundred-ring pour.
        // **Measured on the Arduino Mega's top copper at 0.4 mm: 1,205 ms.**
        //
        // Growing the layer as a whole first answers the question directly. Two pieces that landed
        // in different merged regions are provably further apart than this cut, because the regions
        // *are* the grown copper's connected parts — so the only pairs worth examining are the ones
        // inside a region holding more than one piece, which on a board that is mostly fine is a
        // handful out of hundreds.
        var merged = Polygons.Separate(
            Polygons.Inflate(copper, reach, JoinType.Round, EndType.Polygon, arcTolerance: options.SagittaNm))
            .ToList();

        if (merged.Count == 0)
        {
            return [];
        }

        var mergedBoxes = BoxesOf(merged);
        var together = new Dictionary<int, List<int>>();

        for (var piece = 0; piece < artwork.Count; piece++)
        {
            if (artwork[piece].Count == 0 || artwork[piece][0].Count == 0)
            {
                continue;
            }

            // A vertex of the artwork is safe to locate with: the grown copper is the artwork
            // offset outwards by a positive amount, so every point of the original — boundary
            // included — is strictly inside it. The same argument `Isolation` relies on.
            var first = artwork[piece][0][0];
            var region = RegionAt(merged, mergedBoxes, new Point2(first.X, first.Y));

            if (region < 0)
            {
                continue;
            }

            if (together.TryGetValue(region, out var list))
            {
                list.Add(piece);
            }
            else
            {
                together[region] = [piece];
            }
        }

        // Which piece each named net point sits in. Attributed per *piece* rather than per grown
        // region, which is the whole reason this can name the two sides of a bridge separately:
        // a region is what the pieces became after they merged, so asking it which nets it holds
        // gives the union and not the two halves.
        var namesOf = new SortedSet<string>[artwork.Count];

        foreach (var point in nets)
        {
            if (string.IsNullOrWhiteSpace(point.Net))
            {
                continue;
            }

            var piece = RegionAt(artwork, boxes, point.At);

            if (piece < 0)
            {
                // A net point that does not land on this layer's copper names nothing here. Counted
                // by `Isolation`, which reports it; not counted twice.
                continue;
            }

            (namesOf[piece] ??= new SortedSet<string>(StringComparer.Ordinal)).Add(point.Net);
        }

        var grown = new Paths64?[artwork.Count];
        var gaps = new List<NetGap>();

        // Regions in index order, so two runs walk the same pairs in the same sequence. A
        // dictionary's own enumeration order is not something to build a reported list on.
        foreach (var region in together.Keys.OrderBy(k => k))
        {
            var here = together[region];

            if (here.Count < 2)
            {
                // One piece that grew into a region of its own merged with nothing. This is the
                // common case on a board that is mostly fine, and it costs nothing to skip.
                continue;
            }

            // Every name in this region, which is what `Isolation` reports as a group's nets. Built
            // once for the region rather than per pair: it is a property of the copper, not of any
            // one bridge through it.
            var regionNets = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var piece in here)
            {
                if (namesOf[piece] is { } named)
                {
                    regionNets.UnionWith(named);
                }
            }

            string[] inRegionNets = [.. regionNets];

            for (var a = 0; a < here.Count; a++)
            {
                for (var b = a + 1; b < here.Count; b++)
                {
                    if (token.IsCancellationRequested)
                    {
                        // Ordered even on the way out. A partial list is still shown — the findings
                        // view says it is partial — and this method's own contract is that two runs
                        // of the same board report the same places in the same sequence.
                        gaps.Sort(OrderGaps);
                        return gaps;
                    }

                    var i = here[a];
                    var j = here[b];

                    // Still worth the box test inside a region: three pieces can merge in a chain,
                    // where the ends never touch each other. Four comparisons to find that out.
                    if (Apart(boxes[i][0], boxes[j][0], options.EffectiveWidthNm))
                    {
                        continue;
                    }

                    var left = grown[i] ??= Grow(artwork[i], reach, options);
                    var right = grown[j] ??= Grow(artwork[j], reach, options);

                    var overlap = Polygons.Intersect(left, right);

                    if (overlap.Count == 0)
                    {
                        continue;
                    }

                    var between = new SortedSet<string>(StringComparer.Ordinal);

                    if (namesOf[i] is { } one)
                    {
                        between.UnionWith(one);
                    }

                    if (namesOf[j] is { } other)
                    {
                        between.UnionWith(other);
                    }

                    gaps.Add(new NetGap
                    {
                        Between = [.. between],
                        At = Somewhere(overlap),
                        RegionNets = inRegionNets,
                    });
                }
            }
        }

        // A total order, for the same reason the joins have one: two runs of the same board must
        // report the same places in the same sequence, or a view of them is not a fact about the
        // board. Names first, because that is what a reader scans.
        gaps.Sort(OrderGaps);

        return gaps;
    }

    private static Paths64 Grow(Paths64 piece, long reach, IsolationOptions options) =>
        Polygons.Inflate(piece, reach, JoinType.Round, EndType.Polygon, arcTolerance: options.SagittaNm);

    /// <summary>Whether two boxes are further apart than <paramref name="by"/> in either axis.</summary>
    private static bool Apart(
        (long MinX, long MinY, long MaxX, long MaxY) a,
        (long MinX, long MinY, long MaxX, long MaxY) b,
        long by) =>
        a.MinX - b.MaxX > by || b.MinX - a.MaxX > by ||
        a.MinY - b.MaxY > by || b.MinY - a.MaxY > by;

    /// <summary>
    /// A point inside the overlap, which is the coordinate the operator is given.
    ///
    /// The centroid of the largest ring, verified rather than assumed. The overlap of two
    /// round-grown outlines is a lens and its centroid is inside it, but "usually convex" is not
    /// "always convex" — two pieces that touch in several places at once give an overlap in several
    /// parts, and the centroid of the largest part is what is wanted there too. When the test fails
    /// the first vertex is used: it sits on the boundary of the overlap rather than within it,
    /// which is a fraction of a cut width away from the answer and still points at the right place
    /// on the board. Refusing to name a location at all would be worse than naming one a hair off.
    /// </summary>
    private static Point2 Somewhere(Paths64 overlap)
    {
        var best = overlap[0];
        var most = Math.Abs(Clipper.Area(best));

        foreach (var ring in overlap)
        {
            var area = Math.Abs(Clipper.Area(ring));

            if (area > most)
            {
                most = area;
                best = ring;
            }
        }

        double x = 0, y = 0;

        foreach (var v in best)
        {
            x += v.X;
            y += v.Y;
        }

        var middle = new Point64((long)(x / best.Count), (long)(y / best.Count));

        return Polygons.PointIn(middle, best) == PointInPolygonResult.IsInside
            ? new Point2(middle.X, middle.Y)
            : new Point2(best[0].X, best[0].Y);
    }

    private static int OrderGaps(NetGap a, NetGap b)
    {
        for (var i = 0; i < Math.Min(a.Between.Count, b.Between.Count); i++)
        {
            var by = string.CompareOrdinal(a.Between[i], b.Between[i]);
            if (by != 0)
            {
                return by;
            }
        }

        var byCount = a.Between.Count.CompareTo(b.Between.Count);
        if (byCount != 0)
        {
            return byCount;
        }

        var byX = a.At.X.CompareTo(b.At.X);
        return byX != 0 ? byX : a.At.Y.CompareTo(b.At.Y);
    }

    private static int Order(NetJoin a, NetJoin b)
    {
        for (var i = 0; i < Math.Min(a.Nets.Count, b.Nets.Count); i++)
        {
            var by = string.CompareOrdinal(a.Nets[i], b.Nets[i]);
            if (by != 0)
            {
                return by;
            }
        }

        var byCount = a.Nets.Count.CompareTo(b.Nets.Count);
        if (byCount != 0)
        {
            return byCount;
        }

        var byX = a.Near.X.CompareTo(b.Near.X);
        return byX != 0 ? byX : a.Near.Y.CompareTo(b.Near.Y);
    }

    /// <summary>
    /// Every ring of every region as a box. The same trick, and for the same reason, as the one the
    /// realiser uses to place net points: a board's copper is mostly nowhere near any given point,
    /// and four comparisons reject a ring where a point-in-polygon walks its vertices.
    ///
    /// **Per ring, not per region**, which is the part worth saying. A region's outer box is the
    /// obvious thing to index on and it is nearly useless here: the largest region on a real board
    /// is the ground pour, its outer ring is the whole board, and so every point falls inside its
    /// box and then pays for a test against each of its two hundred–odd clearance holes. Boxing the
    /// holes as well took the Arduino Mega from 209,752 point tests to 12,008 — measured, because
    /// the work counters make it a number rather than an opinion. That is the whole argument for
    /// having built them: the first version of this was written, shipped a snapshot, and only then
    /// showed what it cost.
    /// </summary>
    private static (long MinX, long MinY, long MaxX, long MaxY)[][] BoxesOf(List<Paths64> regions)
    {
        var boxes = new (long MinX, long MinY, long MaxX, long MaxY)[regions.Count][];

        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            var forRegion = new (long MinX, long MinY, long MaxX, long MaxY)[region.Count];

            for (var j = 0; j < region.Count; j++)
            {
                long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;

                foreach (var v in region[j])
                {
                    minX = Math.Min(minX, v.X);
                    minY = Math.Min(minY, v.Y);
                    maxX = Math.Max(maxX, v.X);
                    maxY = Math.Max(maxY, v.Y);
                }

                forRegion[j] = (minX, minY, maxX, maxY);
            }

            boxes[i] = forRegion;
        }

        return boxes;
    }

    /// <summary>
    /// Which region holds this point, or -1. A region is its outer ring and the holes in it, so
    /// crossing an odd number of its rings is inside: in the outer and in one of its holes is out
    /// again, which is what puts a point in a pour's cutout into the island standing there instead.
    /// </summary>
    private static int RegionAt(
        List<Paths64> regions, (long MinX, long MinY, long MaxX, long MaxY)[][] boxes, Point2 at)
    {
        var point = new Point64(at.X, at.Y);

        for (var i = 0; i < regions.Count; i++)
        {
            var forRegion = boxes[i];

            // Ring 0 is the outer, so its box contains every hole's: outside it is outside the
            // region, and the whole region costs four comparisons to dismiss.
            if (Outside(forRegion[0], at))
            {
                continue;
            }

            var region = regions[i];
            var inside = 0;

            for (var j = 0; j < region.Count; j++)
            {
                if (Outside(forRegion[j], at))
                {
                    continue;
                }

                if (Polygons.PointIn(point, region[j]) == PointInPolygonResult.IsInside)
                {
                    inside++;
                }
            }

            if (inside % 2 == 1)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Outside((long MinX, long MinY, long MaxX, long MaxY) box, Point2 at) =>
        at.X < box.MinX || at.X > box.MaxX || at.Y < box.MinY || at.Y > box.MaxY;
}
