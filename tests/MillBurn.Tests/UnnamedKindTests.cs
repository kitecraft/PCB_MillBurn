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
}
