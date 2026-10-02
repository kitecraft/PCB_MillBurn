using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// An unnamed gap says which kind of unnamed it is.
///
/// **One number covered two things that mean opposite amounts of trouble.** `NetCheck.Unnamed`
/// counted every merge no pair of names could be put to, and two quite different cases land there:
/// copper carrying no net attribute, which nobody can name and which might be a short; and two
/// pieces of the *same* net rejoining, which the tool equally cannot cut and which is electrically
/// nothing at all, because they were one conductor before the cut and are one after it.
///
/// The warning built on it was worded to allow for both — *"the copper either side carries no net,
/// or carries the same one, in which case nothing is shorted"* — which is honest and is not the
/// same as useful. An operator reading twenty-five gaps on the test board could not tell how many
/// of them mattered.
///
/// Recorded in [06 §6.42](../../Documentation/06-Roadmap-and-Risks.md).
/// </summary>
public sealed class UnnamedKindTests(ITestOutputHelper output)
{
    /// <summary>
    /// The depth the test board fuses at, and the one 06 §6.42's measurement was taken at.
    ///
    /// Deeper than anybody cuts a board — for a V-bit, depth is cut width, and this is what makes
    /// the lettering and the test patterns meet their neighbours.
    /// </summary>
    private static readonly long DepthNm = Nm.FromMillimetres(0.75);

    /// <summary>
    /// The default isolation depth, which is what an operator gets without touching anything.
    ///
    /// Used where the point is that a case turns up on an ordinary cut rather than a contrived one.
    /// </summary>
    private static readonly long DefaultDepthNm = Nm.FromMillimetres(0.05);

    private static NetCheck Check(string board, LayerRole role, long? depthNm = null)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));
        var copper = loaded.Layers.Single(l => l.Role == role);

        return ElectricalCheck.Isolation(
            copper.Area,
            copper.Nets,
            new IsolationOptions { Tool = Tool.DefaultVBit, DepthNm = depthNm ?? DepthNm });
    }

    /// <summary>
    /// The two kinds add up to the number they replaced.
    ///
    /// The split is computed per region now; the old residual was `merged − explained`. Those are
    /// two routes to one number and this is the one assertion that keeps them honest — if they ever
    /// disagree, one of the two is wrong and it matters which, because the subtraction is the one
    /// that has already been wrong once (62 unnamed on a board with none).
    /// </summary>
    [Fact]
    public void TheTwoKindsAccountForEveryUnnamedMerge()
    {
        var check = Check(RealBoards.MillburnTestBoard, LayerRole.TopCopper);

        output.WriteLine(
            $"merged {check.Merged} · unnamed {check.Unnamed} "
            + $"= same-net {check.SameNet} + nameless {check.Nameless} · "
            + $"{check.Joins.Count} named group(s)");

        Assert.True(check.Ran, check.Silent);
        Assert.Equal(check.Unnamed, check.SameNet + check.Nameless);

        // **What this actually checks is that no merge was charged to the residual twice**, which
        // is the direction the old subtraction got wrong: `merged − explained` once reported 62
        // unnamed gaps on a board with none. It is a bound, not a reconciliation.
        //
        // **What it does not check**, said plainly because the comment here used to claim it did:
        // that the remainder really is accounted for by the named groups. Nothing compares
        // `explained` against `check.Joins`, and it cannot be done from a `NetCheck` alone — a group
        // is a region and a merge is a pair of pieces within one, so the two are not the same unit
        // and the check does not expose the per-region tally that would relate them.
        var explained = check.Merged - check.Unnamed;
        Assert.True(explained >= 0, "more merges were called unnamed than happened");
    }

    /// <summary>
    /// The test board's unnamed merges are nameless copper, not the same net meeting itself.
    ///
    /// **The number that makes the split worth having.** 06 §6.42 says why this board has unnamed
    /// copper at all: it carries the lettering and the 0.5/0.8/1.0 test patterns, none of which is
    /// on a net, and at a wide enough cut they fuse with what is beside them. So the expectation is
    /// that its residual is overwhelmingly *nameless* — and that is the half that might be a short,
    /// which is exactly what an operator wants distinguished from the half that cannot be.
    ///
    /// Asserted as a relation rather than a pinned count: the totals move with the cutter and the
    /// board, and what this file is about is the two kinds being told apart, not how many there
    /// happen to be today. The counts are written to the test output so a reader can see them.
    ///
    /// **What it pins is "more nameless than same-net", not "overwhelmingly"** — a `/describe-test`
    /// pass pointed out that the prose claimed more than the line below does. The stronger claim
    /// is not worth inventing a threshold for: this board reports 25 and 0, and a day when it
    /// reports 13 and 12 is a day to come and read it rather than one a multiplier should decide.
    /// </summary>
    [Fact]
    public void OnTheTestBoardTheUnnamedMergesAreNamelessCopper()
    {
        var check = Check(RealBoards.MillburnTestBoard, LayerRole.TopCopper);

        output.WriteLine($"same-net {check.SameNet}, nameless {check.Nameless}");

        Assert.True(check.Unnamed > 0, "this board no longer has unnamed merges to classify");
        Assert.True(
            check.Nameless > check.SameNet,
            $"expected mostly nameless copper on this board, got {check.Nameless} nameless "
            + $"against {check.SameNet} same-net");
    }

    /// <summary>
    /// The same net meeting itself is counted as that, and it happens on a real board at the
    /// default cut.
    ///
    /// **Without this, <see cref="NetCheck.SameNet"/> would be a field that never fires**, which is
    /// the failure this repository has been bitten by twice and the reason the boards were swept
    /// before the split was called done. The test board and the Mega both report zero same-net
    /// merges at every depth tried, because on a dense board a region that fuses at all pulls in a
    /// second net almost immediately and becomes a named join instead.
    ///
    /// The Arduino Uno does it, on both copper layers, at **0.02, 0.05, 0.1 and 0.2 mm** — and
    /// 0.05 mm is the default isolation depth, so this is not a contrived cut. One merge, no
    /// nameless copper: two pieces of one net that the cutter cannot get between. Exactly the case
    /// the old single number could not distinguish from a short.
    /// </summary>
    [Fact]
    public void TwoPiecesOfOneNetAreCountedAsTheSameNet()
    {
        var check = Check(RealBoards.ArduinoUno, LayerRole.TopCopper, DefaultDepthNm);

        output.WriteLine(
            $"merged {check.Merged} · same-net {check.SameNet} · nameless {check.Nameless} · "
            + $"{check.Joins.Count} named group(s)");

        Assert.True(check.Ran, check.Silent);
        Assert.True(check.SameNet > 0, "no same-net merge on this board, so the field never fires");
    }

    /// <summary>
    /// A board whose copper is all on named nets reports no nameless merges.
    ///
    /// The other side of the same claim, and the one that stops the split being a relabelling. If
    /// everything landed in <see cref="NetCheck.Nameless"/> regardless, the test above would pass
    /// just as happily and mean nothing.
    ///
    /// The Arduino Mega is a KiCad export whose copper carries net attributes throughout, and at
    /// this depth it has plenty of merges to classify.
    /// </summary>
    [Fact]
    public void ABoardWhoseCopperIsAllNamedReportsNoNamelessMerges()
    {
        var check = Check(RealBoards.ArduinoMega, LayerRole.TopCopper);

        output.WriteLine(
            $"merged {check.Merged} · same-net {check.SameNet} · nameless {check.Nameless} · "
            + $"{check.Joins.Count} named group(s)");

        Assert.True(check.Ran, check.Silent);
        Assert.True(check.Merged > 0, "nothing merged on this board, so nothing was classified");
        Assert.Equal(0, check.Nameless);

        // **And nothing in the other bucket either**, which a `/describe-test` pass noticed was
        // printed and never asserted: a board whose copper is all named has no residual of either
        // kind, and a change that misfiled named copper as same-net would have passed the line
        // above without anybody noticing. Every merge here belongs to a reported group.
        Assert.Equal(0, check.SameNet);
    }

    /// <summary>
    /// A layer that names its nets is never told it names none.
    ///
    /// **A `/code-review` pass found the same gaps reported twice, the second time falsely.**
    /// `ElectricalFindings` has two tail branches: one for a residual that could not be put into
    /// either kind, guarded on the check having run *and* both kinds being zero, and an `else if`
    /// for a check that never ran at all. The second was guarded only on the count, so whenever the
    /// check *did* run and found nameless or same-net merges — which is most boards — both fired.
    ///
    /// The test board at 0.75 mm deep said *"20 further gap(s) … in copper carrying no net name"*
    /// and then, directly beneath it, *"20 gap(s) … This layer names no nets, so which of them
    /// matters was not determined"* — about a layer that names twenty-three. An operator tallying
    /// the list by hand doubles the number, and the second sentence tells them the check was blind
    /// when it was not.
    ///
    /// Asserted on the wording rather than on a count, because the count was right in both lines;
    /// what was wrong was a sentence claiming the layer had no names.
    /// </summary>
    [Fact]
    public void ALayerThatNamesItsNetsIsNeverToldItNamesNone()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));
        var copper = loaded.Layers.Single(l => l.Role == LayerRole.TopCopper);

        var options = new IsolationOptions { Tool = Tool.DefaultVBit, DepthNm = DepthNm };
        var check = ElectricalCheck.Isolation(copper.Area, copper.Nets, options);

        // The guards: the check ran, it named nets, and it found residual gaps — which is the exact
        // state in which the wrong branch used to fire.
        Assert.True(check.Ran, "the check did not run, so the branch under test is the correct one");
        Assert.True(check.NetsSeen > 0, "this layer names no nets, so the sentence would be true");
        Assert.True(check.Nameless + check.SameNet > 0, "no residual gaps, so no tail branch fires");

        var findings = ElectricalFindings.For(
            copper, options, CheckSource.Layer(copper.Role, copper.Label, copper.FileName));

        var lines = findings.Checks.Select(c => c.Message).ToList();

        output.WriteLine($"{check.NetsSeen} nets named; {lines.Count} finding(s)");
        output.WriteLine(string.Join(Environment.NewLine, lines));

        Assert.DoesNotContain(lines, l => l.Contains("names no nets", StringComparison.Ordinal));

        // **Each kind of residual gets one line, and that is the assertion with teeth.** A
        // `/describe-test` pass pointed out that the phrase above is all the check the line before
        // it makes: the same false claim reworded, or the duplicate returning with different
        // wording, or either count being wrong, would all slip past it.
        //
        // This is the property the defect actually broke — the same gaps counted twice — and it is
        // stated against the check's own numbers rather than against a literal, so it holds on a
        // board with same-net merges, nameless ones, both, or neither.
        var residual = lines.Count(
            l => l.Contains("gap(s) are narrower than the cut", StringComparison.Ordinal));

        var expected = (check.Nameless > 0 ? 1 : 0) + (check.SameNet > 0 ? 1 : 0);

        Assert.Equal(expected, residual);
    }

    /// <summary>
    /// A square of copper, in millimetres.
    ///
    /// Synthetic rather than a board, because the shape this is about — one named net and an unnamed
    /// neighbour merging — is a few square millimetres of a real layer and cannot be pointed at from
    /// the outside. Three squares say it in nine lines.
    /// </summary>
    private static Path64 Square(double xMm, double yMm, double sideMm) =>
    [
        new(Nm.FromMillimetres(xMm), Nm.FromMillimetres(yMm)),
        new(Nm.FromMillimetres(xMm + sideMm), Nm.FromMillimetres(yMm)),
        new(Nm.FromMillimetres(xMm + sideMm), Nm.FromMillimetres(yMm + sideMm)),
        new(Nm.FromMillimetres(xMm), Nm.FromMillimetres(yMm + sideMm)),
    ];

    private static Point2 Middle(double xMm, double yMm, double sideMm) => new(
        Nm.FromMillimetres(xMm + (sideMm / 2)),
        Nm.FromMillimetres(yMm + (sideMm / 2)));

    /// <summary>
    /// Copper with no net name beside copper that has one is nameless, not same-net.
    ///
    /// **06 §6.53, found by `/code-review`, and wrong in the direction that reassures.** The split
    /// charged a region's merges by counting the *named* nets it held: two or more was a reported
    /// group, exactly one was the same net meeting itself, none was nameless. A region holding one
    /// named net **and unnamed copper beside it** has exactly one name — so every merge in it was
    /// filed under same-net and reported as advice: *"Nothing is shorted by those: they were one
    /// conductor before the cut."* One side of such a merge carries no net at all. That is precisely
    /// the case the split exists to isolate, reported as safe by construction.
    ///
    /// The fixture is the smallest board that can hold the shape: a named square, an unnamed square
    /// a tenth of a millimetre from it, and a second named square far enough away to be its own
    /// region — which is there because a layer naming only one net is refused outright, and rightly.
    /// </summary>
    [Fact]
    public void CopperWithNoNameBesideANamedNetIsNameless()
    {
        Paths64 copper = [Square(0, 0, 2), Square(2.1, 0, 2), Square(20, 0, 2)];

        // Only two of the three carry a name, and the unnamed one is the near neighbour.
        NetPoint[] nets =
        [
            new("NET_A", Middle(0, 0, 2)),
            new("NET_B", Middle(20, 0, 2)),
        ];

        var check = ElectricalCheck.Isolation(copper, nets, Wide);

        output.WriteLine(
            $"merged {check.Merged} · same-net {check.SameNet} · nameless {check.Nameless} · "
            + $"{check.Joins.Count} group(s)");

        Assert.True(check.Ran, check.Silent);

        // One merge, and it is the dangerous kind: nothing here can say whether it matters.
        Assert.Equal(1, check.Merged);
        Assert.Equal(1, check.Nameless);
        Assert.Equal(0, check.SameNet);
    }

    /// <summary>
    /// And the same shape with both squares named the same is still the same net meeting itself.
    ///
    /// The other side of the claim, and what stops the fix being a relabelling: if every merge were
    /// now called nameless the test above would pass just as happily and mean nothing. This is the
    /// case the advice was written for — two pieces of one conductor the cut cannot divide — and it
    /// has to keep saying so.
    /// </summary>
    [Fact]
    public void TwoPiecesOfOneNamedNetAreStillTheSameNet()
    {
        Paths64 copper = [Square(0, 0, 2), Square(2.1, 0, 2), Square(20, 0, 2)];

        NetPoint[] nets =
        [
            new("NET_A", Middle(0, 0, 2)),
            new("NET_A", Middle(2.1, 0, 2)),
            new("NET_B", Middle(20, 0, 2)),
        ];

        var check = ElectricalCheck.Isolation(copper, nets, Wide);

        output.WriteLine($"same-net {check.SameNet} · nameless {check.Nameless}");

        Assert.Equal(1, check.Merged);
        Assert.Equal(1, check.SameNet);
        Assert.Equal(0, check.Nameless);
    }

    /// <summary>
    /// And a bridge to unnamed copper is not filed under "one net meeting itself" in the findings
    /// view either.
    ///
    /// **The same fault by its own route.** `NetGap.Between` is the union of both pieces' names, so a
    /// bridge between unnamed copper and one named net holds exactly one name — and `IsSameNet` was
    /// `Between.Count == 1`. The window an operator opens to decide a board is sound printed
    /// *"Nothing is shorted by these"* over its coordinates.
    /// </summary>
    [Fact]
    public void ABridgeToUnnamedCopperIsNotFiledAsOneNetMeetingItself()
    {
        Paths64 copper = [Square(0, 0, 2), Square(2.1, 0, 2), Square(20, 0, 2)];

        NetPoint[] nets =
        [
            new("NET_A", Middle(0, 0, 2)),
            new("NET_B", Middle(20, 0, 2)),
        ];

        var gaps = ElectricalCheck.Gaps(copper, nets, Wide, CancellationToken.None);

        foreach (var gap in gaps)
        {
            output.WriteLine(
                $"[{string.Join(", ", gap.Between)}] at {gap.At} · short {gap.IsShort} · "
                + $"same-net {gap.IsSameNet}");
        }

        var bridge = Assert.Single(gaps);

        Assert.False(bridge.IsSameNet, "a bridge to copper carrying no name was called one net meeting itself");
        Assert.False(bridge.IsShort, "nothing here is shorted: only one of the two pieces is named");

        // **One named side, said as the number rather than as the flag.** A `/describe-test` pass
        // pointed out that every other assertion here reduces to `Between.Count == 1`, which is
        // exactly what could not tell this case from two pieces of one net: the only thing that
        // distinguishes them is how many of the two pieces carried a name at all.
        Assert.Equal(1, bridge.NamedSides);

        // Which leaves it in the bucket the view calls "copper with no net name", where somebody has
        // to look at it.
        Assert.Single(bridge.Between);
    }

    /// <summary>The cut this file's synthetic cases use: wide enough to close a tenth of a millimetre.</summary>
    private static IsolationOptions Wide => new()
    {
        Tool = Tool.DefaultVBit,
        DepthNm = DepthNm,
    };
}
