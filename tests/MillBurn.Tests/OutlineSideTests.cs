using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Which side of the line the cutter runs on, profile by profile.
///
/// Found on a real panelised board. KiCad draws the routed channels between the boards as closed
/// loops in Edge_Cuts, and every profile was being offset the same way — outward. The result runs,
/// sounds right, and is a disaster: the cutter takes a groove out of the board on each side of a
/// channel and leaves the channel itself standing, so the panel never comes apart and every board
/// is a cutter-radius undersize on that edge. Measured on the emitted program, the passes were at
/// Y96.500 and Y98.600 for a channel running from Y97.05 to Y98.05.
///
/// The rule is that the cutter goes on the waste side: outside a piece, inside a void. Telling
/// those apart is the interesting part, and two obvious tests for it are wrong — see
/// <see cref="APourRunningToTheBoardEdgeDoesNotMakeAChannelAPiece"/>.
/// </summary>
public sealed class OutlineSideTests
{
    private static readonly Tool Cutter = Tool.DefaultOutlineMill;

    private static Path64 Rect(double x, double y, double w, double h) =>
    [
        new(Nm.FromMillimetres(x), Nm.FromMillimetres(y)),
        new(Nm.FromMillimetres(x + w), Nm.FromMillimetres(y)),
        new(Nm.FromMillimetres(x + w), Nm.FromMillimetres(y + h)),
        new(Nm.FromMillimetres(x), Nm.FromMillimetres(y + h)),
    ];

    /// <summary>
    /// A panel: one frame, and a 1 mm channel across the middle separating two boards.
    ///
    /// The channel is drawn 1.1 mm tall rather than 1.0, because a stroked Edge_Cuts realises to the
    /// *outside* of the pen it was drawn with and that overhangs the true edge by half a pen width.
    /// Reproducing the overhang matters: it is what defeated the first two attempts at this.
    /// </summary>
    private static Paths64 Panel() => [Rect(0, 0, 100, 40), Rect(10, 19.45, 80, 1.1)];

    /// <summary>Copper on both boards, running right up to the edge of the channel.</summary>
    private static Paths64 Copper() => [Rect(12, 3, 76, 16.5), Rect(12, 20.5, 76, 16.5)];

    [Fact]
    public void AChannelBetweenBoardsIsCutFromTheInside()
    {
        var pieces = OutlineOperation.PiecesAmong(Panel(), Copper(), Cutter.DiameterNm);

        Assert.True(pieces[0], "the frame is a piece and is cut on the outside");
        Assert.False(pieces[1], "the channel encloses nothing and must be cut from the inside");
    }

    /// <summary>
    /// The failure that made this hard, in the shape it actually arrived in.
    ///
    /// A ground pour runs to the board edge, so the copper beside a channel begins exactly at the
    /// channel's boundary — and the profile overhangs that boundary by half a pen width, so the
    /// pour lies *inside* the profile. Asking "is any artwork inside this profile" answered yes for
    /// every channel on the board; on the real panel it was nine copper rings and 0.49 mm² per
    /// channel, which beat a 1 %-of-area threshold as well.
    ///
    /// What works is asking where a cut would go: inside a profile the cutter sweeps a band one
    /// diameter wide in from the boundary, and a 1.1 mm channel has nothing left beyond that.
    /// </summary>
    [Fact]
    public void APourRunningToTheBoardEdgeDoesNotMakeAChannelAPiece()
    {
        var channel = Rect(10, 19.45, 80, 1.1);

        // Copper that touches the channel's own edge, and then some: 0.05 mm of pour inside the
        // profile along its whole length, which is the overhang a 0.1 mm pen leaves.
        Paths64 pour = [Rect(12, 3, 76, 16.5), Rect(12, 20.5, 76, 16.5)];

        var overlap = Math.Abs(Clipper.Area(
            Clipper.Intersect(pour, [channel], FillRule.NonZero)));

        Assert.True(overlap > 0, "the fixture must actually reproduce the overhang");

        Assert.False(
            OutlineOperation.PiecesAmong([Rect(0, 0, 100, 40), channel], pour, Cutter.DiameterNm)[1],
            "the channel is still a void");
    }

    /// <summary>
    /// The counter-case, and the reason nesting alone cannot decide this.
    ///
    /// A hand-panelised file draws the stock as one rectangle and each board as another rectangle
    /// inside it. Those nest exactly as a channel does, and they are pieces: cut them on the inside
    /// and every board comes out a cutter-diameter small.
    /// </summary>
    [Fact]
    public void BoardsInsideAHandCutFrameAreStillPieces()
    {
        Paths64 panel = [Rect(0, 0, 100, 40), Rect(5, 5, 20, 10), Rect(35, 5, 20, 10)];
        Paths64 copper = [Rect(7, 7, 16, 6), Rect(37, 7, 16, 6)];

        Assert.All(OutlineOperation.PiecesAmong(panel, copper, Cutter.DiameterNm), Assert.True);
    }

    /// <summary>An empty window in a board is a void, whatever its size.</summary>
    [Fact]
    public void AnEmptyWindowInABoardIsCutFromTheInside()
    {
        Paths64 board = [Rect(0, 0, 60, 40), Rect(20, 15, 20, 10)];
        Paths64 copper = [Rect(2, 2, 15, 36)];

        var pieces = OutlineOperation.PiecesAmong(board, copper, Cutter.DiameterNm);

        Assert.True(pieces[0]);
        Assert.False(pieces[1]);
    }

    /// <summary>
    /// With no artwork there is no evidence, and the cut stays where it has always been.
    ///
    /// Deliberately not "nesting decides": a channel and a board inside a frame nest identically,
    /// so guessing between them destroys a panel in one direction or the other. Silence means the
    /// old answer, which is at least the one every existing file was cut with.
    /// </summary>
    [Fact]
    public void WithoutArtworkNothingIsFlipped()
    {
        Assert.All(OutlineOperation.PiecesAmong(Panel(), null, Cutter.DiameterNm), Assert.True);
        Assert.All(OutlineOperation.PiecesAmong(Panel(), [], Cutter.DiameterNm), Assert.True);
    }

    /// <summary>
    /// And the point of all of it: the toolpath ends up inside the channel rather than either side
    /// of it.
    /// </summary>
    [Fact]
    public void TheCutterRunsInsideTheChannel()
    {
        var toolpath = OutlineOperation.Build(
            Panel(),
            new OutlineOptions
            {
                Tool = Cutter,
                BoardThicknessNm = Nm.FromMillimetres(1.6),
                TabCount = 0,
                Keep = Copper(),
            });

        // Every pass that is not the frame — the frame's own passes run outside 0..40.
        var inner = toolpath.Passes
            .SelectMany(p => p.Path)
            .Select(s => s.From.Y)
            .Where(y => y > Nm.FromMillimetres(5) && y < Nm.FromMillimetres(35))
            .ToList();

        Assert.NotEmpty(inner);

        Assert.All(inner, y => Assert.InRange(
            y, Nm.FromMillimetres(19.45), Nm.FromMillimetres(20.55)));
    }
}
