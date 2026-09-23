using MillBurn.Gerber;
using MillBurn.Gerber.Model;

namespace MillBurn.Tests;

/// <summary>
/// Which net a stroke belongs to, when the net changes while a stroke is open.
///
/// **An object attribute applies to the objects that follow it.** That is the whole of the rule in
/// the specification, and the parser broke it in one specific way: strokes are batched, so several
/// consecutive <c>D01</c> runs become one <see cref="DrawObject"/>, and the object was not emitted
/// until something else forced it out. The attributes were read at that moment rather than at the
/// moment the stroke was drawn, so a trace drawn under one net was filed under the next.
///
/// **KiCad writes exactly the shape that triggers it**: a net, some strokes, the next net, more
/// strokes, with nothing in between to flush. On the author's own test board that put a
/// <c>Net-(J3-Pin_1)</c> trace under <c>Net-(J3-Pin_2)</c>, and on the Arduino Mega it misfiled
/// enough traces to make the electrical check report a hundred and ten shorts on a board that has
/// none. The check was written first and disbelieved its own output, which is how this was found;
/// the geometry was right all along and the names were wrong.
///
/// <c>%TD*%</c> is the same bug with a worse ending: it clears the attributes, so a stroke still
/// open at that point was emitted with no net at all and simply vanished from the netlist. That is
/// why the test board's net-point count rises by one when this is fixed rather than staying put.
///
/// The precedent for the fix was already in the file: <c>%LP</c> flushes before changing polarity,
/// for the identical reason that a polarity cannot apply retroactively to copper already laid.
/// </summary>
public sealed class NetAttributionTests
{
    private const string Header = "%FSLAX46Y46*%\n%MOMM*%\n%LPD*%\nG01*\n%ADD10C,0.2*%\nD10*\n";

    private static GerberImage Parse(string body) => GerberParser.Parse(Header + body + "M02*\n");

    private static string[] NetsOfDraws(GerberImage image) =>
        [.. image.Objects.OfType<DrawObject>().Select(d => d.Net ?? "<none>")];

    /// <summary>
    /// Two strokes under one net, then a second net. Both strokes belong to the first.
    ///
    /// This is the test board's own layout, reduced: <c>Net-(J3-Pin_1)</c> owns a diagonal and a
    /// horizontal run, and <c>Net-(J3-Pin_2)</c> starts afterwards. Before the fix the horizontal
    /// run came back as Pin_2.
    /// </summary>
    [Fact]
    public void AStrokeKeepsTheNetItWasDrawnUnder()
    {
        var image = Parse(
            "%TO.N,Net-(J3-Pin_1)*%\n"
            + "X1610920Y-826080D02*\n"
            + "X1599000Y-838000D01*\n"
            + "X1679000Y-826080D02*\n"
            + "X1610920Y-826080D01*\n"
            + "%TO.N,Net-(J3-Pin_2)*%\n"
            + "X1610520Y-851480D02*\n"
            + "X1599000Y-863000D01*\n");

        Assert.DoesNotContain(image.Diagnostics, d => d.IsError);

        Assert.Equal(
            ["Net-(J3-Pin_1)", "Net-(J3-Pin_1)", "Net-(J3-Pin_2)"],
            NetsOfDraws(image));
    }

    /// <summary>
    /// A stroke still open when <c>%TD*%</c> arrives keeps its net instead of losing it.
    ///
    /// Before the fix this draw came back with no net at all: the attributes were deleted, and the
    /// stroke was emitted afterwards against the empty set. A trace with no net is invisible to
    /// anything that reasons about nets, which is worse than a wrong name because nothing looks
    /// out of place.
    /// </summary>
    [Fact]
    public void DeletingTheAttributesDoesNotTakeTheOpenStrokesNetWithIt()
    {
        var image = Parse(
            "%TO.N,SDA*%\n"
            + "X1000000Y1000000D02*\n"
            + "X2000000Y1000000D01*\n"
            + "%TD*%\n");

        Assert.DoesNotContain(image.Diagnostics, d => d.IsError);
        Assert.Equal(["SDA"], NetsOfDraws(image));
    }

    /// <summary>
    /// And <c>%TD*%</c> still clears: what is drawn after it has no net.
    ///
    /// Without this, the test above passes just as well against a parser for which <c>%TD*%</c> is
    /// a no-op — it never clears, so of course nothing was lost. That is not the fix; the fix is
    /// that the stroke is flushed *before* the clear happens. Proving the flush without proving the
    /// clear still happens would leave the more damaging half of the change unmeasured: attributes
    /// leaking past a delete puts the wrong net on everything that follows.
    /// </summary>
    [Fact]
    public void WhatIsDrawnAfterTheDeleteHasNoNet()
    {
        var image = Parse(
            "%TO.N,SDA*%\n"
            + "X1000000Y1000000D02*\n"
            + "X2000000Y1000000D01*\n"
            + "%TD*%\n"
            + "X1000000Y2000000D02*\n"
            + "X2000000Y2000000D01*\n");

        Assert.DoesNotContain(image.Diagnostics, d => d.IsError);

        // "<none>" is the helper's stand-in for a null net, and this is the only case that reaches
        // it — everywhere else in this file, every draw is expected to carry a name.
        Assert.Equal(["SDA", "<none>"], NetsOfDraws(image));
    }

    /// <summary>
    /// The attribute genuinely applies going forward: a stroke drawn after the change gets the new
    /// net. Flushing early must not push the old net onto what comes next.
    /// </summary>
    [Fact]
    public void AStrokeDrawnAfterTheChangeGetsTheNewNet()
    {
        var image = Parse(
            "%TO.N,SDA*%\n"
            + "X1000000Y1000000D02*\n"
            + "X2000000Y1000000D01*\n"
            + "%TO.N,SCL*%\n"
            + "X1000000Y2000000D02*\n"
            + "X2000000Y2000000D01*\n"
            + "X3000000Y2000000D01*\n");

        Assert.DoesNotContain(image.Diagnostics, d => d.IsError);
        Assert.Equal(["SDA", "SCL"], NetsOfDraws(image));

        // The two D01s after the change are one object holding two segments. The array above
        // already rules out flushing on every stroke — that would give three entries, not two — so
        // what this adds is narrower and still worth having: a segment dropped, or two collinear
        // ones quietly merged, would leave the net sequence above completely unchanged.
        var last = image.Objects.OfType<DrawObject>().Last();
        Assert.Equal(2, last.Segments.Count);
    }

    /// <summary>
    /// Consecutive strokes under an unchanged net stay one object.
    ///
    /// Batching is why the bug existed, and it is worth keeping: it holds the object count near the
    /// number of traces rather than the number of segments. A fix that flushed on every D01 would
    /// make this test pass and quietly multiply the object count on a real board.
    /// </summary>
    [Fact]
    public void AnUnchangedNetStillBatchesItsStrokes()
    {
        var image = Parse(
            "%TO.N,GND*%\n"
            + "X1000000Y1000000D02*\n"
            + "X2000000Y1000000D01*\n"
            + "X3000000Y1000000D01*\n"
            + "X4000000Y1000000D01*\n");

        Assert.DoesNotContain(image.Diagnostics, d => d.IsError);

        var draw = Assert.Single(image.Objects.OfType<DrawObject>());

        Assert.Equal("GND", draw.Net);
        Assert.Equal(3, draw.Segments.Count);
    }

    /// <summary>
    /// A region keeps the net it opened under when the attributes are deleted before it closes.
    ///
    /// The same argument as the strokes, one object later: a region's copper is drawn between
    /// <c>G36</c> and <c>G37</c>, and a <c>%TD*%</c> arriving in between used to take its net with
    /// it. No exporter in <c>tests/boards</c> writes that shape, so this is a guard rather than a
    /// repair — but a pour is exactly where a short hides, and a pour that has quietly lost its net
    /// is invisible to every check that reasons about nets.
    /// </summary>
    [Fact]
    public void ARegionKeepsTheNetItOpenedUnder()
    {
        var image = Parse(
            "%TO.N,GND*%\n"
            + "G36*\n"
            + "X1000000Y1000000D02*\n"
            + "X3000000Y1000000D01*\n"
            + "X3000000Y3000000D01*\n"
            + "X1000000Y3000000D01*\n"
            + "X1000000Y1000000D01*\n"
            + "%TD*%\n"
            + "G37*\n");

        Assert.DoesNotContain(image.Diagnostics, d => d.IsError);

        var region = Assert.Single(image.Objects.OfType<RegionObject>());

        Assert.Equal("GND", region.Net);

        // The region is a region, not just an object with the right name on it. Without this the
        // test passes over a parser that produced an empty or garbled shape, and over one that
        // also emitted the four D01s as strokes instead of absorbing them into the fill.
        Assert.Empty(image.Objects.OfType<DrawObject>());
        Assert.Equal(4, Assert.Single(region.Contours).Count);
    }

    /// <summary>
    /// A net declared inside the region wins over the one it opened under.
    ///
    /// The two tests either side of this one rule out the extremes — reading the attributes as they
    /// stand at <c>G37</c> loses the net to a <c>%TD*%</c>, and freezing them at <c>G36</c> ignores
    /// a writer that names the net while the region is open. Both are ruled out by holding the
    /// opening set and laying anything seen since over the top, and *that* leaves one question the
    /// others do not ask: which wins when both are present. The later statement does, because it is
    /// the more specific one and because a writer that says a net inside the pair means it.
    ///
    /// This also covers the simpler case of a region opened with no net in force at all, which had
    /// a test of its own until this one subsumed it: any parser that lets a later declaration beat
    /// an earlier one necessarily honours a later declaration when there is no earlier one. A test
    /// that cannot fail unless another already has is noise in a suite this file is meant to make
    /// trustworthy.
    /// </summary>
    [Fact]
    public void ANetDeclaredInsideTheRegionWinsOverTheOneItOpenedUnder()
    {
        var image = Parse(
            "%TO.N,GND*%\n"
            + "G36*\n"
            + "X1000000Y1000000D02*\n"
            + "X3000000Y1000000D01*\n"
            + "X3000000Y3000000D01*\n"
            + "X1000000Y3000000D01*\n"
            + "X1000000Y1000000D01*\n"
            + "%TO.N,VCC*%\n"
            + "G37*\n");

        Assert.DoesNotContain(image.Diagnostics, d => d.IsError);

        var region = Assert.Single(image.Objects.OfType<RegionObject>());

        Assert.Equal("VCC", region.Net);

        // The same two checks its neighbours carry. Without them this passes against a parser that
        // emitted the square as four strokes *and* a shapeless region holding the right name.
        Assert.Empty(image.Objects.OfType<DrawObject>());
        Assert.Equal(4, Assert.Single(region.Contours).Count);
    }

    /// <summary>
    /// <c>%TO.N,*%</c> — KiCad's way of saying this copper belongs to no net — comes back as a
    /// name that is empty rather than absent. Recorded because the electrical check has to know the
    /// difference between "no net" and a net called nothing, and it reads this value to tell.
    /// </summary>
    [Fact]
    public void AnEmptyNetAttributeIsAnEmptyNameAndNotAMissingOne()
    {
        var declared = Parse(
            "%TO.N,*%\n"
            + "X1000000Y1000000D02*\n"
            + "X2000000Y1000000D01*\n");

        Assert.DoesNotContain(declared.Diagnostics, d => d.IsError);

        // Empty, not absent. `IsNullOrEmpty` would pass here whether the parser stored the empty
        // value or threw the attribute away, and those are the two cases this exists to tell apart.
        Assert.Equal(string.Empty, Assert.Single(declared.Objects.OfType<DrawObject>()).Net);

        var silent = Parse(
            "X1000000Y1000000D02*\n"
            + "X2000000Y1000000D01*\n");

        Assert.DoesNotContain(silent.Diagnostics, d => d.IsError);

        // And a stroke that never had an attribute has no net at all, which is the other case. The
        // check reads these differently: no net is copper it says nothing about, an empty net is
        // copper the file has explicitly said belongs to nothing.
        Assert.Null(Assert.Single(silent.Objects.OfType<DrawObject>()).Net);
    }
}
