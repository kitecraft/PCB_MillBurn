using MillBurn.Viewer;
using SkiaSharp;

namespace MillBurn.Tests;

/// <summary>
/// The arithmetic behind the scale strips: which spacing the ruler steps in, and where a
/// millimetre lands on the screen.
///
/// Drawing is checked by looking at a screenshot, which is how both of this feature's real faults
/// were found. What is checked here is the part a screenshot cannot settle — that the ruler and the
/// grid cannot pick different spacings, and that the mapping an operator reads a coordinate off is
/// the inverse of the one the board was drawn with.
/// </summary>
public sealed class RulerTests
{
    /// <summary>
    /// The first rung clearing the threshold, not the nearest one.
    ///
    /// At 90 px/mm — the zoom a single bridge is inspected at — 0.1 mm is 9 px and 0.5 mm is 45 px,
    /// so the answer is 0.5 and the screenshot at that zoom reads 39.5, 40, 40.5.
    /// </summary>
    [Fact]
    public void TheStepIsTheFirstOnTheLadderThatClearsTheThreshold()
    {
        Assert.Equal(0.5f, GridLadder.StepFor(90f));
        Assert.Equal(0.1f, GridLadder.StepFor(120f));
        Assert.Equal(1f, GridLadder.StepFor(20f));
    }

    /// <summary>
    /// Zoomed out past the coarsest rung, the coarsest rung is still the answer.
    ///
    /// The alternative is a division by a spacing of zero, or an unbounded one, drawn once per
    /// pixel — a hang rather than a wrong number.
    ///
    /// 250 is written out rather than read from `Steps[^1]`, so that this states an answer instead
    /// of agreeing with whatever the production code currently holds. Adding a coarser rung would
    /// fail it, which is the right moment to look at this again. 0.04 is the case that matters: at
    /// 0.04 px/mm even 250 mm spans ten pixels, so it is under the threshold and the clamp is what
    /// is being exercised rather than merely a large answer.
    /// </summary>
    [Fact]
    public void ZoomedRightOutTheLadderStopsAtItsCoarsestRung()
    {
        Assert.Equal(250f, GridLadder.StepFor(0.0001f));
        Assert.Equal(250f, GridLadder.StepFor(0.04f));
    }

    /// <summary>
    /// Whatever the zoom and whichever threshold is asked for, the answer is *chosen from* the
    /// ladder and never computed.
    ///
    /// That is the whole of what this guards, and it is narrower than the roadmap's argument it
    /// comes from. The grid and the ruler agree because both are handed the same returned value —
    /// that is a fact about `BoardRenderer` and `ToolpathRenderer` calling this once each per frame,
    /// not something a test of this method can observe. What *is* observable is that nothing here
    /// invents a spacing: a `Pow(10, Floor(log))` or a rounded multiple would land off the ladder
    /// somewhere in the sweep, and a rung that is 0, infinite or NaN fails membership.
    ///
    /// Both thresholds are swept because the two viewports pass different ones — 12 px and 60 — and
    /// a second code path for the explicit overload would be caught here and nowhere else.
    /// </summary>
    [Fact]
    public void EveryScaleAndEitherThresholdLandsOnTheSameLadder()
    {
        for (var scale = 0.01f; scale < 4000f; scale *= 1.07f)
        {
            Assert.Contains(GridLadder.StepFor(scale), GridLadder.Steps);
            Assert.Contains(GridLadder.StepFor(scale, 60f), GridLadder.Steps);
        }
    }

    /// <summary>
    /// Board y counts up; screen y counts down.
    ///
    /// Stated as two separate facts rather than one round trip, because a mapping that negated y in
    /// both directions would round-trip perfectly and still draw the board upside down. The viewport
    /// has in fact had that fault this sprint, in <c>BoardView.ZoomTo</c>: a bridge at y 30 was
    /// centred at y 293, and nothing but a screenshot said so.
    /// </summary>
    [Fact]
    public void BoardYCountsUpTheScreen()
    {
        var frame = new RulerFrame(Scale: 8f, ScreenXAtZero: 100f, ScreenYAtZero: 700f);

        Assert.Equal(700f, frame.ScreenY(0f));
        Assert.Equal(620f, frame.ScreenY(10f));
        Assert.Equal(780f, frame.ScreenY(-10f));

        Assert.Equal(100f, frame.ScreenX(0f));
        Assert.Equal(180f, frame.ScreenX(10f));
    }

    /// <summary>
    /// Reading a coordinate off the screen undoes drawing one onto it.
    ///
    /// The readout and the drawing have to be each other's inverse or the number under the pointer
    /// is not the number the pointer is on — which is the whole of what 6.48 was asked for.
    /// </summary>
    [Fact]
    public void ScreenAndBoardAreEachOthersInverse()
    {
        var frame = new RulerFrame(Scale: 7.73f, ScreenXAtZero: 548.2f, ScreenYAtZero: 657.4f);

        Assert.Equal(44.05f, frame.BoardX(frame.ScreenX(44.05f)), 3);
        Assert.Equal(30.31f, frame.BoardY(frame.ScreenY(30.31f)), 3);
        Assert.Equal(-12.5f, frame.BoardY(frame.ScreenY(-12.5f)), 3);
    }

    /// <summary>
    /// The measuring drag reports both sides and the hypotenuse.
    ///
    /// The literal numbers are the ones in the screenshot taken to check this story: a drag from
    /// 43.2, 29.8 to 45.1, 31.4 across a bridge on the Mega, which the viewport labelled
    /// "dx 1.90   dy 1.60   2.48 mm".
    /// </summary>
    [Fact]
    public void TheMeasureGivesBothSidesAndTheDiagonal()
    {
        var span = new RulerMeasure(new SKPoint(43.2f, 29.8f), new SKPoint(45.1f, 31.4f));

        Assert.Equal(1.9f, span.DeltaX, 3);
        Assert.Equal(1.6f, span.DeltaY, 3);
        Assert.Equal(2.4839f, span.Diagonal, 3);
    }

    /// <summary>
    /// Dragging the other way keeps the sign on each side and loses it on the diagonal.
    ///
    /// dx and dy are what somebody moving a machine types, and a machine cares which way. The
    /// distance does not have a direction, so reporting it as negative would be wrong rather than
    /// merely unhelpful.
    /// </summary>
    [Fact]
    public void MeasuringBackwardsKeepsTheSignOnEachSideAndNotOnTheDistance()
    {
        var span = new RulerMeasure(new SKPoint(45.1f, 31.4f), new SKPoint(43.2f, 29.8f));

        Assert.Equal(-1.9f, span.DeltaX, 3);
        Assert.Equal(-1.6f, span.DeltaY, 3);
        Assert.Equal(2.4839f, span.Diagonal, 3);
    }
}
