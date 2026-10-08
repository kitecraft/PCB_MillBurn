using MillBurn.Core;
using MillBurn.Viewer;
using SkiaSharp;

namespace MillBurn.Tests;

/// <summary>
/// The two decisions behind going to a finding: how far in to zoom, and what the mark is called.
///
/// What the mark looks like is checked by looking at it, which is how both of this feature's faults
/// were found — a label box sized from one line while containing another, and a pink ring on orange
/// copper that could barely be seen. What is checked here is the arithmetic a screenshot cannot
/// settle: that the zoom actually shows the span it promises, and that the ring and the line that
/// was clicked cannot print the same place differently.
/// </summary>
public sealed class MarkerTests
{
    /// <summary>
    /// The scale puts the promised span of board across the viewport.
    ///
    /// Stated as the round trip rather than as a formula: what matters is that fourteen millimetres
    /// of board is what you get, not that a particular division was performed.
    /// </summary>
    [Fact]
    public void TheZoomShowsTheSpanItPromises()
    {
        var scale = Marker.ScaleFor(1400f, 900f);

        Assert.Equal(Marker.SpanMm, 900f / scale, 3);

        // **And the span is fourteen millimetres**, written out rather than taken from the constant.
        // The round trip above holds for any span at all — both sides of it move together — so on
        // its own it says the arithmetic is consistent and nothing about the number. Fourteen is a
        // decision, read off the mockup the product owner chose on 2026-09-30:
        // `WorkingFolder/mockups/rulers/E-zoomed-to-finding.png` spans 37.0 to 51.0. Changing it
        // should take an edit here and a fresh look at a board, not pass unnoticed.
        Assert.Equal(14f, Marker.SpanMm);
        Assert.Equal(900f / 14f, scale, 3);
    }

    /// <summary>
    /// The span is measured across the shorter side, so it is what you get in both directions.
    ///
    /// Taking it from the width would make the promise true of the wide axis and quietly false of
    /// the other: on a 1400 x 400 viewport, fourteen millimetres across would be four down, and the
    /// bridge the operator was sent to would be off the top or the bottom of a view that claimed to
    /// be showing it.
    /// </summary>
    [Fact]
    public void TheSpanIsTakenFromTheShorterSide()
    {
        Assert.Equal(Marker.ScaleFor(1400f, 400f), Marker.ScaleFor(400f, 1400f), 3);
        Assert.Equal(400f / Marker.SpanMm, Marker.ScaleFor(1400f, 400f), 3);
    }

    /// <summary>
    /// A viewport with no size yet asks for no zoom, rather than for an infinite one.
    ///
    /// Reachable: the screenshot path goes to a finding during `OnOpened`, before any guaranteed
    /// layout pass. The caller treats zero as "there was nothing to go to" and says so.
    ///
    /// **The span case is the one doing the work**, and it is worth being plain about that: a zero
    /// side divided by fourteen is zero anyway, so those two would pass with the guard deleted. They
    /// are here because they would not pass if a zero side were clamped to a pixel, which is the
    /// other plausible way to handle it and gives a scale of 1/14 that reads as a working zoom.
    /// Only the zero span divides by zero and returns infinity unguarded.
    /// </summary>
    [Fact]
    public void AViewportWithNoSizeAsksForNoZoom()
    {
        Assert.Equal(0f, Marker.ScaleFor(0f, 900f));
        Assert.Equal(0f, Marker.ScaleFor(1400f, 0f));
        Assert.Equal(0f, Marker.ScaleFor(1400f, 900f, spanMm: 0f));
    }

    /// <summary>
    /// The mark prints a place exactly as the findings view prints it.
    ///
    /// **Both are two decimals, and that is the point.** The ring and the line that was clicked name
    /// the same piece of copper, so a reader comparing them must not be left deciding whether a
    /// difference is real. The literal is the coordinate the roadmap quotes for this very feature —
    /// *"44.05, 30.31 mm"*.
    /// </summary>
    [Fact]
    public void TheMarkPrintsAPlaceTheWayTheFindingsViewDoes()
    {
        Assert.Equal("44.05, 30.31 mm", Marker.CoordinateOf(44.05f, 30.31f));
        Assert.Equal("9.77, 39.67 mm", Marker.CoordinateOf(9.77f, 39.67f));

        // **Against the findings view's own formatter, not only against a literal that looks like
        // it.** The line above pins the format; this pins the agreement, which is the thing the
        // name claims. `FindingsWindow` builds each place as
        // `$"{Nm.ToMillimetreString(At.X, 2)}, {Nm.ToMillimetreString(At.Y, 2)} mm"` from the gap's
        // nanometres, so the same nanometres are put through both routes here. Either side changing
        // its decimals, its separator or its suffix alone fails this.
        var x = Nm.FromMillimetres(44.05);
        var y = Nm.FromMillimetres(30.31);

        Assert.Equal(
            $"{Nm.ToMillimetreString(x, 2)}, {Nm.ToMillimetreString(y, 2)} mm",
            Marker.CoordinateOf((float)Nm.ToMillimetres(x), (float)Nm.ToMillimetres(y)));
    }

    /// <summary>
    /// A whole millimetre keeps both its decimal places, and a third is rounded away.
    ///
    /// A ragged column is the fault this guards: the findings view lists places one under another in
    /// a monospaced font, and "10, 42.07" beside "9.75, 39.66" does not line up. Separately, a
    /// coordinate printed to more places than the check measured to would claim a precision the
    /// geometry does not have.
    /// </summary>
    [Fact]
    public void EveryPlaceIsPrintedToTwoDecimals()
    {
        Assert.Equal("10.00, 42.00 mm", Marker.CoordinateOf(10f, 42f));
        Assert.Equal("1.23, 4.57 mm", Marker.CoordinateOf(1.234f, 4.567f));
    }

    /// <summary>
    /// The marker's two strings do not get swapped.
    ///
    /// **A narrow claim, deliberately.** Most of what could be asserted about a positional record is
    /// that a record stores its parameters, which is the compiler's business and not a test's. The
    /// one real hazard is that `Coordinate` and `Nets` are both strings in adjacent positions, so
    /// transposing them compiles, draws a ring labelled with a net name over a line of coordinates,
    /// and nothing else would notice.
    ///
    /// They are distinguished here by shape rather than by value: whichever one ends with "mm" is
    /// the coordinate, which stays true if the literals change.
    /// </summary>
    [Fact]
    public void TheCoordinateAndTheNetsDoNotGetSwapped()
    {
        var at = new SKPoint(44.05f, 30.31f);
        var marker = new BoardMarker(at, Marker.CoordinateOf(at.X, at.Y), "+5V and GND");

        Assert.EndsWith(" mm", marker.Coordinate, StringComparison.Ordinal);
        Assert.DoesNotContain("mm", marker.Nets, StringComparison.Ordinal);
        Assert.Equal(at, marker.At);
    }
}
