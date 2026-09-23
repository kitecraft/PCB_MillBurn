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
        var grown = Clipper.InflatePaths(
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
        var explained = 0;

        foreach (var (index, held) in inRegion)
        {
            if (held.Nets.Count < 2)
            {
                continue;
            }

            joins.Add(new NetJoin { Nets = [.. held.Nets], Near = held.First });

            // Every merge inside this region is accounted for by the group just reported. Charging
            // it one merge instead of all of them is what made the residual nonsense.
            if (pieces[index] > 1)
            {
                explained += pieces[index] - 1;
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
            Unnamed = Math.Max(0, merged - explained),
            Unplaced = dropped,
            UnplacedCopper = strays,
        };
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
