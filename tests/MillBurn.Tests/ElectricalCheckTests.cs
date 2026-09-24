using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Whether the isolation pass really separates the nets the Gerbers declare.
///
/// **A short and an open are both invisible on screen and expensive in copper.** The picture shows
/// a toolpath drawn where the tool can go, and where the tool cannot fit it simply draws nothing —
/// which looks identical to a gap that did not need cutting. The board is the first place anybody
/// finds out, and by then the copper is etched.
///
/// The geometry is <see cref="IsolationOperation.UnreachableGaps"/>'s: grow every piece of copper
/// by half of what the tool cuts, and two pieces whose grown outlines meet have no room between
/// them for the tool. That method returns a count. This one puts the parsed <c>TO.N</c> net names
/// to the same geometry and says which nets.
///
/// **The synthetic cases are here because the real boards cannot fail in a controlled way.** A gap
/// swept from 1.00 mm down to 0.05 mm against a known 0.127 mm cut pins the threshold from both
/// sides: it is not enough that a board reports nothing, because so would a check that always
/// reports nothing.
/// </summary>
public sealed class ElectricalCheckTests(ITestOutputHelper output)
{
    /// <summary>A 30° V-bit at 0.05 mm deep cuts 0.1268 mm. Not assumed; asserted below.</summary>
    private static IsolationOptions VBit(double depthMm = 0.05) => new()
    {
        Tool = Tool.DefaultVBit,
        DepthNm = Nm.FromMillimetres(depthMm),
    };

    private static Path64 Square(double xMm, double yMm, double sideMm)
    {
        long x = Nm.FromMillimetres(xMm), y = Nm.FromMillimetres(yMm), s = Nm.FromMillimetres(sideMm);
        return [new(x, y), new(x + s, y), new(x + s, y + s), new(x, y + s)];
    }

    private static Point2 Centre(double xMm, double yMm, double sideMm) =>
        new(Nm.FromMillimetres(xMm + (sideMm / 2)), Nm.FromMillimetres(yMm + (sideMm / 2)));

    /// <summary>Two 1 mm pads, a named net in each, a gap between them that the caller chooses.</summary>
    private static (Paths64 Copper, List<NetPoint> Nets) TwoPads(double gapMm)
    {
        var copper = new Paths64 { Square(0, 0, 1), Square(1 + gapMm, 0, 1) };

        var nets = new List<NetPoint>
        {
            new("SDA", Centre(0, 0, 1)),
            new("SCL", Centre(1 + gapMm, 0, 1)),
        };

        return (copper, nets);
    }

    /// <summary>
    /// The number the rest of this file depends on. If the V-bit's geometry changes, every
    /// threshold below moves with it, and the failure should say so here rather than somewhere
    /// that looks like a check of the check.
    ///
    /// 0.1 mm tip plus twice 0.05 mm of depth times tan 15°, which is 126,795 nm and not the
    /// 0.127 mm the documentation rounds it to. Written out because the first version of this test
    /// asserted the rounded figure and failed, which is the test doing its job on its first run.
    /// </summary>
    [Fact]
    public void TheDefaultVBitCutsA127MicronHairlineAt50MicronsDeep()
    {
        // 0.1 mm of tip, plus twice 0.05 mm of depth times tan 15°, is 126,795 nm. The arithmetic is
        // written here rather than asserted: computing the expected value with the same formula the
        // tool uses would agree with it however wrong either became, which is not a test of
        // anything. The literal is the check.
        Assert.Equal(126_795, VBit().EffectiveWidthNm);
    }

    /// <summary>
    /// A gap wider than the cut is separated; a gap narrower than it is not, and the nets in it are
    /// named. The pair either side of 0.127 mm is the whole contract.
    /// </summary>
    [Theory]
    [InlineData(1.00, false)]
    [InlineData(0.50, false)]
    [InlineData(0.20, false)]
    [InlineData(0.13, false)]
    [InlineData(0.12, true)]
    [InlineData(0.10, true)]
    [InlineData(0.05, true)]
    public void AGapNarrowerThanTheCutLeavesTheNetsConnected(double gapMm, bool expectJoined)
    {
        var (copper, nets) = TwoPads(gapMm);

        var check = ElectricalCheck.Isolation(copper, nets, VBit());

        output.WriteLine($"gap {gapMm:F2} mm against a 0.127 mm cut: {check.Joins.Count} join(s)");

        Assert.True(check.Ran);
        Assert.Equal(2, check.NetsSeen);

        if (!expectJoined)
        {
            Assert.Empty(check.Joins);
            return;
        }

        var join = Assert.Single(check.Joins);
        Assert.Equal(["SCL", "SDA"], join.Nets);
        Assert.Equal("SCL and SDA", join.Describe());
    }

    /// <summary>
    /// A wider cut joins what a narrower one separates. The same 0.20 mm gap that passes above is
    /// reported here, because the tool was taken deeper — which is the operator's most common way
    /// of causing this, and is a setting rather than a board fault.
    /// </summary>
    [Fact]
    public void TakingTheSameBitDeeperJoinsAGapThatWasClear()
    {
        var (copper, nets) = TwoPads(0.20);

        // Ran, then empty. Without the first, "reported nothing" and "declined to look" are the
        // same result here, and only one of them means the gap was wide enough.
        var shallow = ElectricalCheck.Isolation(copper, nets, VBit(0.05));
        Assert.True(shallow.Ran, shallow.Silent);
        Assert.Empty(shallow.Joins);

        var deeper = VBit(0.30);
        output.WriteLine($"0.30 mm deep cuts {Nm.ToMillimetreString(deeper.EffectiveWidthNm, 3)} mm");

        var join = Assert.Single(ElectricalCheck.Isolation(copper, nets, deeper).Joins);
        Assert.Equal("SCL and SDA", join.Describe());
    }

    /// <summary>
    /// Three pads in a row, all too close: reported as one group rather than three pairs.
    ///
    /// They are one piece of copper after milling, so "A and B", "B and C", "A and C" would be
    /// three descriptions of one fault, and the operator fixing it fixes all three at once.
    /// </summary>
    [Fact]
    public void ThreeNetsOnOnePieceAreReportedOnce()
    {
        var copper = new Paths64 { Square(0, 0, 1), Square(1.05, 0, 1), Square(2.10, 0, 1) };

        var nets = new List<NetPoint>
        {
            new("GND", Centre(0, 0, 1)),
            new("VCC", Centre(1.05, 0, 1)),
            new("SDA", Centre(2.10, 0, 1)),
        };

        var check = ElectricalCheck.Isolation(copper, nets, VBit());
        var join = Assert.Single(check.Joins);

        Assert.Equal(["GND", "SDA", "VCC"], join.Nets);
        Assert.Equal("GND and SDA and VCC", join.Describe());

        // Three pieces became one, so two merges happened — and the one group reported accounts for
        // both. This is the case that catches a residual computed as "merges minus groups": it
        // would leave one merge charged to nobody and announce it as copper with no name on it.
        Assert.Equal(2, check.Merged);
        Assert.Equal(0, check.Unnamed);
    }

    /// <summary>
    /// The point reported belongs to one of the nets in the group, and to this group rather than
    /// some other piece of copper on the layer.
    ///
    /// The obvious assertion — that it lies inside the copper — cannot fail: the point is one of
    /// the <see cref="NetPoint"/>s handed in, and those are inside the copper by construction. It
    /// would be testing the input. What is worth pinning is that the check hands back a point from
    /// the *right* group, which is the thing a viewer would act on and the thing that would break
    /// if the grouping and the reporting ever disagreed about which region is which.
    /// </summary>
    [Fact]
    public void TheReportedPointBelongsToTheGroupItIsReportedWith()
    {
        // Two shorted pads at the origin, and a third net far away that must not be picked.
        var copper = new Paths64 { Square(0, 0, 1), Square(1.05, 0, 1), Square(20, 20, 1) };

        var nets = new List<NetPoint>
        {
            new("SDA", Centre(0, 0, 1)),
            new("SCL", Centre(1.05, 0, 1)),
            new("FARAWAY", Centre(20, 20, 1)),
        };

        var join = Assert.Single(ElectricalCheck.Isolation(copper, nets, VBit()).Joins);

        Assert.Equal(["SCL", "SDA"], join.Nets);

        // It is one of the points that were handed in — an exact match, so a recomputed centroid or
        // a default would throw here — and it belongs to one of this group's own nets. That rules
        // out the distant island's point, which is the mistake a wrong region index would make.
        var owner = nets.Single(n => n.At == join.Near);
        Assert.Contains(owner.Net, join.Nets);
    }

    /// <summary>
    /// Two separate shorts on one layer, both starting with the same net, come back in a fixed
    /// order and each carrying a point of its own.
    ///
    /// A ground pour shorted to something in two different places is the commonest shape of this
    /// fault, and it is the case that breaks a comparator keying on the first net alone: both
    /// groups begin "GND", `List.Sort` is not stable, and the order they were tied from is
    /// dictionary enumeration order, which is not promised. On a board with more groups than can be
    /// named, *which* ones get named would then change between runs of the same file — and any
    /// snapshot carrying those warnings flaps.
    ///
    /// Every other case in this file produces a single group, so nothing else here can see it.
    /// </summary>
    [Fact]
    public void TwoGroupsSharingANetComeBackInAStableOrder()
    {
        // Two clusters 10 mm apart. Within each, a 0.05 mm gap the cut cannot make.
        var copper = new Paths64
        {
            Square(0, 0, 1),
            Square(1.05, 0, 1),
            Square(10, 0, 1),
            Square(11.05, 0, 1),
        };

        var nets = new List<NetPoint>
        {
            new("GND", Centre(0, 0, 1)),
            new("SDA", Centre(1.05, 0, 1)),
            new("GND", Centre(10, 0, 1)),
            new("SCL", Centre(11.05, 0, 1)),
        };

        var check = ElectricalCheck.Isolation(copper, nets, VBit());

        Assert.True(check.Ran, check.Silent);
        Assert.Equal(2, check.Joins.Count);

        // Ordered through the whole sequence, so the tie on "GND" is broken by the second name.
        Assert.Equal(["GND", "SCL"], check.Joins[0].Nets);
        Assert.Equal(["GND", "SDA"], check.Joins[1].Nets);

        // Each group's point belongs to that group's own cluster, not the other's. Swapping them
        // would satisfy every assertion above and none of these.
        Assert.Equal(Nm.FromMillimetres(10.5), check.Joins[0].Near.X);
        Assert.Equal(Nm.FromMillimetres(0.5), check.Joins[1].Near.X);

        // And the same input gives the same answer, which is what a snapshot depends on.
        var again = ElectricalCheck.Isolation(copper, nets, VBit());
        Assert.Equal(
            check.Joins.Select(j => j.Describe()),
            again.Joins.Select(j => j.Describe()));
    }

    /// <summary>
    /// An end mill cuts one width whatever the depth, and the check reads that width rather than
    /// assuming the depth-dependent geometry a V-bit has.
    ///
    /// Every other case here uses a V-bit, where width comes from depth; a check that reached for
    /// the depth instead of asking the tool would pass all of them and report nothing at all for
    /// the tool an operator reaches for when a V-bit will not do.
    /// </summary>
    [Fact]
    public void AnEndMillIsMeasuredByItsDiameterNotItsDepth()
    {
        var mill = new IsolationOptions
        {
            Tool = Tool.DefaultOutlineMill,
            DepthNm = Nm.FromMillimetres(0.05),
        };

        // 1 mm wide at any depth, so a 0.5 mm gap it cannot enter.
        Assert.Equal(Nm.FromMillimetres(1.0), mill.EffectiveWidthNm);

        var (copper, nets) = TwoPads(0.5);

        // Ran, then empty — and this one carries weight beyond the usual. A V-bit leaves
        // `DiameterNm` at zero, so an implementation reaching for the diameter instead of the cut
        // width would find nothing to cut with, report no joins, and look exactly like success on
        // this half. Asserting it ran refuses that reading: a tool that cuts nothing says so.
        var vbit = ElectricalCheck.Isolation(copper, nets, VBit());
        Assert.True(vbit.Ran, vbit.Silent);
        Assert.Empty(vbit.Joins);

        var join = Assert.Single(ElectricalCheck.Isolation(copper, nets, mill).Joins);
        Assert.Equal("SCL and SDA", join.Describe());
    }

    /// <summary>
    /// Copper the tool cannot divide, on pieces carrying no net name, is still counted.
    ///
    /// This is the half the names cannot reach. A fill, a fiducial, an unnamed pour — anything the
    /// exporter wrote without a `.N` — merges with its neighbours just as physically and has no
    /// name to report it under. Reporting only the named joins would tell an operator about the
    /// shorts it could name and stay silent about the rest, which reads as a clean bill of health
    /// for gaps nobody checked.
    /// </summary>
    [Fact]
    public void CopperWithNoNameIsCountedEvenThoughItCannotBeNamed()
    {
        // Two named pads, comfortably apart. Two unnamed pads, too close to divide.
        var copper = new Paths64
        {
            Square(0, 0, 1),
            Square(5, 0, 1),
            Square(0, 10, 1),
            Square(1.05, 10, 1),
        };

        var nets = new List<NetPoint>
        {
            new("SDA", Centre(0, 0, 1)),
            new("SCL", Centre(5, 0, 1)),
        };

        var check = ElectricalCheck.Isolation(copper, nets, VBit());

        output.WriteLine($"{check.Joins.Count} join(s), {check.Merged} merged, {check.Unnamed} unnamed");

        Assert.True(check.Ran);

        // Nothing to name: the two named pads are 4 mm apart.
        Assert.Empty(check.Joins);

        // But the two unnamed ones became one piece, and that is still worth saying.
        Assert.Equal(1, check.Merged);
        Assert.Equal(1, check.Unnamed);
    }

    /// <summary>
    /// When every merge has a name on it, there is no residual to report on top — otherwise the
    /// same fault is counted twice, once in words and once in a number.
    /// </summary>
    [Fact]
    public void AMergeThatWasNamedIsNotAlsoCountedAsUnnamed()
    {
        var (copper, nets) = TwoPads(0.05);

        var check = ElectricalCheck.Isolation(copper, nets, VBit());

        Assert.Single(check.Joins);
        Assert.Equal(1, check.Merged);
        Assert.Equal(0, check.Unnamed);
    }

    /// <summary>
    /// A named merge and an unnamed one in the same scene, which is what pins the arithmetic.
    ///
    /// The two tests above constrain only the extremes — everything named, or nothing named — and
    /// both are satisfied by `Unnamed` being a plain copy of `Merged` with names never consulted.
    /// Only a scene holding one of each separates them.
    ///
    /// It also fixes the third case, which is the one that made the warning's old wording false: a
    /// merge between two pieces carrying the *same* net. The tool could not cut that gap either,
    /// but the two pieces were one conductor already, so there is nothing shorted and no pair of
    /// names to report. It counts as unnamed, and the message must not call it a short.
    /// </summary>
    [Fact]
    public void ANamedMergeAndAnUnnamedOneAreCountedApart()
    {
        var copper = new Paths64
        {
            // Two different nets, too close: one named join.
            Square(0, 0, 1),
            Square(1.05, 0, 1),

            // Copper with no net at all, too close: a merge nothing can name.
            Square(0, 10, 1),
            Square(1.05, 10, 1),

            // The same net on both sides, too close: a merge, and electrically nothing.
            Square(0, 20, 1),
            Square(1.05, 20, 1),
        };

        var nets = new List<NetPoint>
        {
            new("SDA", Centre(0, 0, 1)),
            new("SCL", Centre(1.05, 0, 1)),
            new("GND", Centre(0, 20, 1)),
            new("GND", Centre(1.05, 20, 1)),
        };

        var check = ElectricalCheck.Isolation(copper, nets, VBit());

        output.WriteLine($"{check.Joins.Count} join(s), {check.Merged} merged, {check.Unnamed} unnamed");

        Assert.True(check.Ran, check.Silent);

        // Only the two-net merge is a join. The same-net one is not a short, and the anonymous one
        // has nothing to be reported under.
        var join = Assert.Single(check.Joins);
        Assert.Equal(["SCL", "SDA"], join.Nets);

        // Three pieces of copper were lost to the growing, one of which was named.
        Assert.Equal(3, check.Merged);
        Assert.Equal(2, check.Unnamed);
    }

    /// <summary>
    /// Three anonymous pieces in a row are two merges, not one merged region.
    ///
    /// Every other scene here has its unreported merges arriving in pairs, where "merges the groups
    /// did not explain" and "regions that produced no group" give the same answer. Three in a row is
    /// the shape that separates them: two merges, one region. The count is of merges, because that
    /// is what "gaps the tool could not cut" means to the operator reading it — three pieces fused
    /// into one took two uncut gaps to do it.
    /// </summary>
    [Fact]
    public void ThreeAnonymousPiecesInARowAreTwoMerges()
    {
        var copper = new Paths64
        {
            // A named pair, far enough apart to stay separate, so the check has something to run on.
            Square(0, 0, 1),
            Square(5, 0, 1),

            // Three pieces with no net at all, each too close to the next.
            Square(0, 10, 1),
            Square(1.05, 10, 1),
            Square(2.10, 10, 1),
        };

        var nets = new List<NetPoint>
        {
            new("SDA", Centre(0, 0, 1)),
            new("SCL", Centre(5, 0, 1)),
        };

        var check = ElectricalCheck.Isolation(copper, nets, VBit());

        output.WriteLine($"{check.Joins.Count} join(s), {check.Merged} merged, {check.Unnamed} unnamed");

        Assert.True(check.Ran, check.Silent);
        Assert.Empty(check.Joins);
        Assert.Equal(2, check.Merged);
        Assert.Equal(2, check.Unnamed);
    }

    /// <summary>
    /// A named group is credited every merge inside it, including one that pulled in copper with no
    /// name of its own.
    ///
    /// Crediting a group "one merge per net beyond the first" and crediting it its region's whole
    /// piece-loss agree everywhere else in this file, because elsewhere a group's region holds
    /// exactly one piece per net. Here it holds one more. The region is reported — the operator is
    /// told SDA and SCL are connected — so every gap inside it is accounted for, and none of them
    /// should turn up again in a count of gaps nothing could name.
    /// </summary>
    [Fact]
    public void AGroupIsCreditedTheAnonymousCopperItSweptUp()
    {
        var copper = new Paths64
        {
            Square(0, 0, 1),
            Square(1.05, 0, 1),

            // No net on this one, but it is in the same blob.
            Square(2.10, 0, 1),
        };

        var nets = new List<NetPoint>
        {
            new("SDA", Centre(0, 0, 1)),
            new("SCL", Centre(1.05, 0, 1)),
        };

        var check = ElectricalCheck.Isolation(copper, nets, VBit());

        output.WriteLine($"{check.Joins.Count} join(s), {check.Merged} merged, {check.Unnamed} unnamed");

        var join = Assert.Single(check.Joins);
        Assert.Equal(["SCL", "SDA"], join.Nets);

        // Three pieces became one: two merges, both inside the group that was reported.
        Assert.Equal(2, check.Merged);
        Assert.Equal(0, check.Unnamed);
    }

    /// <summary>
    /// A layer with no net names is not a clean layer. Saying "no shorts found" about a file that
    /// never named a net promises something nobody checked, so the result says why it is silent.
    /// A Protel export carries no net attributes at all.
    /// </summary>
    [Fact]
    public void ALayerWithNoNetsSaysSoRatherThanReportingItClean()
    {
        var (copper, _) = TwoPads(0.05);

        var check = ElectricalCheck.Isolation(copper, [], VBit());

        Assert.False(check.Ran);
        Assert.Empty(check.Joins);
        Assert.Contains("no nets", check.Silent, StringComparison.Ordinal);
    }

    /// <summary>One net cannot be shorted to anything, and that is not the same as being checked.</summary>
    [Fact]
    public void ASingleNetLayerIsNotReportedAsChecked()
    {
        var copper = new Paths64 { Square(0, 0, 1), Square(1.05, 0, 1) };

        var nets = new List<NetPoint>
        {
            new("GND", Centre(0, 0, 1)),
            new("GND", Centre(1.05, 0, 1)),
        };

        var check = ElectricalCheck.Isolation(copper, nets, VBit());

        Assert.False(check.Ran);
        Assert.Empty(check.Joins);
    }

    /// <summary>
    /// Two pads of the *same* net touching is what a net is, not a fault.
    /// </summary>
    [Fact]
    public void OnePieceOfCopperHoldingOneNetIsNotAJoin()
    {
        var copper = new Paths64 { Square(0, 0, 1), Square(0.5, 0, 1) };

        var nets = new List<NetPoint>
        {
            new("GND", Centre(0, 0, 1)),
            new("GND", Centre(0.5, 0, 1)),
            new("VCC", Centre(5, 5, 1)),
        };

        copper.Add(Square(5, 5, 1));

        var check = ElectricalCheck.Isolation(copper, nets, VBit());

        // Ran first. Without it this passes just as well when the check declines to look at all,
        // which is the failure mode an empty-result assertion cannot tell from success. Two
        // distinct names are present, so there is a real question here and it must be asked.
        Assert.True(check.Ran, check.Silent);
        Assert.Equal(2, check.NetsSeen);
        Assert.Empty(check.Joins);

        // Copper that already touched in the artwork is one piece, not two that merged. The tool
        // did not fail to divide anything here — there was never a gap — and counting it would put
        // a number against the operator's name for something the board arrived with.
        Assert.Equal(0, check.Merged);
        Assert.Equal(0, check.Unnamed);
    }

    /// <summary>
    /// The boards the author actually cuts, at the settings they are actually cut at: nothing.
    ///
    /// This is the half of the story's acceptance that cannot be faked by a check that always
    /// reports nothing, because the cases above prove it reports something when there is something.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.MillburnTestBoard)]
    [InlineData(RealBoards.Panel)]
    [InlineData(RealBoards.PogoTest1)]
    public void TheseBoardsIsolateCleanly(string board)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));

        var copper = loaded.Layers
            .Where(l => l.Role is LayerRole.TopCopper or LayerRole.BottomCopper)
            .ToList();

        Assert.NotEmpty(copper);

        foreach (var layer in copper)
        {
            var check = ElectricalCheck.Isolation(layer.Area, layer.Nets, VBit());

            output.WriteLine($"{board} / {layer.Label}: {check.NetsSeen} nets, {check.Joins.Count} join(s)");

            Assert.True(check.Ran, $"{layer.Label}: {check.Silent}");
            Assert.Empty(check.Joins);
        }
    }

    /// <summary>
    /// The other half of the story's acceptance: a board deliberately under-isolated names what it
    /// has joined.
    ///
    /// The board is not altered — the tool is taken deeper, which widens what it cuts, and is
    /// exactly the mistake this check exists to catch: one an operator makes by typing a number,
    /// not by drawing anything wrong.
    ///
    /// The test board turns out to have no middle ground. It is clean to a 0.368 mm cut and fuses
    /// into a single piece at 0.502 mm, which says its clearances are all of a size — unsurprising
    /// for a board laid out to test 0.5, 0.8 and 1.0 mm features. So the thing worth asserting is
    /// the cliff itself: clean on one side, named on the other.
    /// </summary>
    [Theory]
    [InlineData(0.10, false)]
    [InlineData(0.30, false)]
    [InlineData(0.50, false)]
    [InlineData(0.75, true)]
    public void TheTestBoardIsCleanUntilTheCutIsWiderThanItsClearances(double depthMm, bool expectJoined)
    {
        var layer = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard))
            .Layers.Single(l => l.Role == LayerRole.TopCopper);

        var options = VBit(depthMm);
        var check = ElectricalCheck.Isolation(layer.Area, layer.Nets, options);

        output.WriteLine(
            $"{depthMm:F2} mm deep cuts {Nm.ToMillimetreString(options.EffectiveWidthNm, 3)} mm: "
            + $"{check.Joins.Count} group(s)");

        Assert.True(check.Ran);

        if (!expectJoined)
        {
            Assert.Empty(check.Joins);
            return;
        }

        var join = Assert.Single(check.Joins);

        // Named, not counted, and every name is one the operator can look up.
        Assert.True(join.Nets.Count >= 20);
        Assert.Contains("Net-(J1-Pin_1)", join.Nets);
        Assert.All(join.Nets, n => Assert.False(string.IsNullOrWhiteSpace(n)));

        // The merges the named group accounts for are not left over as unnamed.
        //
        // Each net in the group arrived on its own piece of copper — two nets on one piece would be
        // a short in the artwork, not something the tool did — so a group naming N nets swallowed
        // at least N-1 merges, and those must not also be counted as copper nothing could name.
        // "Merges minus groups" charges all but one of them to nobody: on this board it reported 62
        // unnamed where 25 are real, the rest being the group's own.
        //
        // The remainder is genuine and is left unpinned. This board carries copper with no net at
        // all — the lettering, and the 0.5/0.8/1.0 test patterns — which merges at this width and
        // has no name to be reported under. That count is a property of the artwork, not of the
        // arithmetic, so asserting it would pin the board rather than the check.
        output.WriteLine($"    {check.Merged} merged, {check.Unnamed} unnamed");

        Assert.True(
            check.Merged - check.Unnamed >= join.Nets.Count - 1,
            $"a group naming {join.Nets.Count} nets must account for at least {join.Nets.Count - 1} "
            + $"of the {check.Merged} merges, but only {check.Merged - check.Unnamed} were attributed.");

        output.WriteLine($"    {join.Describe()}");
    }

    /// <summary>
    /// A wider cut can only join more, never less.
    ///
    /// This is the property that cannot be satisfied by accident, and it is here because the
    /// alternative — asserting that one board reports one particular number of shorts — pins a
    /// magic constant that says nothing about whether the check is reasoning correctly. The Arduino
    /// Mega is the board for it: a commercial six-mil layout that a 30° V-bit cannot fully isolate
    /// at any depth, so the counts are non-zero across the whole range and actually move.
    /// </summary>
    [Fact]
    public void AWiderCutNeverSeparatesMoreThanANarrowerOne()
    {
        var layer = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoMega))
            .Layers.Single(l => l.Role == LayerRole.TopCopper);

        var joined = new List<(double Depth, int Nets)>();

        foreach (var mm in new[] { 0.05, 0.10, 0.15, 0.20, 0.30 })
        {
            var check = ElectricalCheck.Isolation(layer.Area, layer.Nets, VBit(mm));

            // Distinct nets caught up in any group.
            //
            // Summing each group's count instead would count a net once per group it appears in,
            // and that quantity *falls* when two groups sharing a net merge: {GND,A} and {GND,B}
            // total four, and the single {GND,A,B} they become totals three. The assertion below
            // would then fail on a wider cut that behaved perfectly. Not hypothetical — on this
            // board at 0.154 mm, +5V is in three groups, GND in three and USHIELD in two, so the
            // merge that breaks it is one step away from the sweep this test walks.
            var caught = check.Joins
                .SelectMany(j => j.Nets)
                .Distinct(StringComparer.Ordinal)
                .Count();

            output.WriteLine(
                $"  {mm:F2} mm deep cuts {Nm.ToMillimetreString(VBit(mm).EffectiveWidthNm, 3)} mm: "
                + $"{check.Joins.Count} group(s) holding {caught} nets");

            joined.Add((mm, caught));
        }

        Assert.All(joined, entry => Assert.True(entry.Nets > 0));

        for (var i = 1; i < joined.Count; i++)
        {
            Assert.True(
                joined[i].Nets >= joined[i - 1].Nets,
                $"cutting {joined[i].Depth:F2} mm deep caught {joined[i].Nets} nets, fewer than the "
                + $"{joined[i - 1].Nets} caught at {joined[i - 1].Depth:F2} mm. A wider cut cannot separate more.");
        }

        // And it must actually move, or the ordering above holds trivially.
        Assert.True(joined[^1].Nets > joined[0].Nets);
    }
}
