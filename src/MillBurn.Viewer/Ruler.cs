using System.Globalization;
using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// The spacings the grid and the ruler both step through.
///
/// **One ladder, in one place.** There were two of these — the same nine numbers declared in
/// `BoardRenderer` and again in `ToolpathRenderer`. They had already drifted: one took the first
/// spacing wider than twelve screen pixels and the other the first wider than sixty, so the two
/// viewports drew grids of different densities at the same zoom and nothing said so.
/// </summary>
public static class GridLadder
{
    /// <summary>Millimetres, each about the next one you would reach for.</summary>
    public static readonly float[] Steps = [0.1f, 0.5f, 1f, 5f, 10f, 25f, 50f, 100f, 250f];

    /// <summary>
    /// The first spacing whose on-screen size clears <paramref name="leastPixels"/>, or the coarsest
    /// there is.
    ///
    /// Twelve pixels by default: closer than that and the lines read as a wash rather than as a
    /// grid.
    /// </summary>
    public static float StepFor(float scale, float leastPixels = 12f)
    {
        foreach (var candidate in Steps)
        {
            if (candidate * scale >= leastPixels)
            {
                return candidate;
            }
        }

        return Steps[^1];
    }
}

/// <summary>
/// Where the board's own numbers land on the screen.
///
/// **Board y counts up and screen y counts down, always** — so this says where board (0, 0) is and
/// the two conversions follow. The two viewports hold their world coordinates the opposite way up
/// from each other (`BoardRenderer` negates y when it builds the scene, `ToolpathRenderer` negates
/// it when it draws), and expressing the ruler in board millimetres rather than in either world
/// means neither convention reaches it.
/// </summary>
/// <param name="Scale">Screen pixels per millimetre.</param>
/// <param name="ScreenXAtZero">Where board x = 0 falls, in screen pixels.</param>
/// <param name="ScreenYAtZero">Where board y = 0 falls, in screen pixels.</param>
public readonly record struct RulerFrame(float Scale, float ScreenXAtZero, float ScreenYAtZero)
{
    public float ScreenX(float boardX) => (boardX * Scale) + ScreenXAtZero;

    public float ScreenY(float boardY) => ScreenYAtZero - (boardY * Scale);

    public float BoardX(float screenX) => (screenX - ScreenXAtZero) / Scale;

    public float BoardY(float screenY) => (ScreenYAtZero - screenY) / Scale;
}

/// <summary>
/// Where the pointer is, and what to call the corner it is measured from.
/// </summary>
/// <param name="At">The position in board millimetres.</param>
/// <param name="Origin">
/// What zero is, in the operator's words — "from the board's corner", or the stock's. Named rather
/// than assumed, because the answer changes with the job and a coordinate whose origin is a guess is
/// worse than no coordinate.
/// </param>
public readonly record struct RulerReadout(SKPoint At, string Origin);

/// <summary>
/// Two points and the distance between them, from the measuring drag.
/// </summary>
/// <param name="From">Where the drag started, in board millimetres.</param>
/// <param name="To">Where it is now.</param>
public readonly record struct RulerMeasure(SKPoint From, SKPoint To)
{
    public float DeltaX => To.X - From.X;

    public float DeltaY => To.Y - From.Y;

    public float Diagonal => MathF.Sqrt((DeltaX * DeltaX) + (DeltaY * DeltaY));
}

/// <summary>
/// A scale along the top and down the left of the viewport, in the board's own millimetres.
///
/// **Asked for in the workshop twice, a week apart**, and chosen from five mockups rendered over a
/// real Mega at true scale: a translucent strip floating over the viewport rather than a gutter
/// insetting it. The board is the thing being looked at, and a gutter takes a strip out of it on two
/// sides.
///
/// **Zero is work zero** — the lower-left corner of the frame every emitted file is referenced to,
/// which is the stock's corner on a job built on stock and the board's otherwise. A ruler measuring
/// from the window's edge would be a second coordinate system on the screen, and it would eventually
/// be believed. Y counts upward, not down the screen.
///
/// **In the viewer beside the grid**, because a ruler is a function of the transform and the
/// viewport rectangle and nothing else. Both viewports call it, and the headless renderers get it
/// for free — which is what makes it checkable in a screenshot like everything else here.
/// </summary>
public static class Ruler
{
    /// <summary>How deep the top strip is. Enough for a number and a tick under it.</summary>
    public const float Thickness = 19f;

    /// <summary>
    /// How wide the left strip is, which is not the same thing.
    ///
    /// **Found by looking at it.** The left strip was the top strip's depth and its numbers were
    /// right-aligned inside that, so "70" lost its 7 and "100" would have lost rather more: a
    /// ten-pixel font needs about seventeen pixels for three digits and there were nine. The top
    /// strip has the whole width of the viewport for a number and the left strip has only itself.
    /// </summary>
    public const float LeftWidth = 30f;

    /// <summary>
    /// What a label needs to itself before the ruler numbers every second, fifth or tenth line
    /// instead of every one.
    /// </summary>
    private const float LabelRoom = 44f;

    /// <summary>
    /// How far below the top strip the left strip's first number may sit.
    ///
    /// A number's baseline is three pixels above its tick, so a tick just under the strip puts its
    /// digits inside the corner the "mm" legend occupies — and the legend, drawn last, shows the
    /// number through itself. Ten pixels clears a ten-pixel font.
    /// </summary>
    private const float CornerRoom = 10f;

    /// <summary>
    /// How many grid lines a label may skip.
    ///
    /// **Multiples of the grid's own step, never a second ladder.** The step ladder is not a chain of
    /// multiples — 25 does not divide into 10 — so a ruler that picked its own rung off it would at
    /// some zoom tick every 25 mm over lines drawn every 10, and every tick but the first would fall
    /// between two lines. Numbering every *n*th line cannot do that whatever the zoom.
    /// </summary>
    private static readonly int[] Multiples = [1, 2, 5, 10, 20, 50, 100];

    private static readonly SKFont Font = new(SKTypeface.Default, 10f);

    /// <summary>For the line under the readout, which names the origin rather than stating it.</summary>
    private static readonly SKFont Small = new(SKTypeface.Default, 9f);

    /// <summary>
    /// The measuring triangle's two sides, dashed. Shared rather than per-frame: see
    /// <see cref="Measured"/> for why a `using` on the paint is not enough to release one.
    /// </summary>
    private static readonly SKPathEffect Dashes = SKPathEffect.CreateDash([3, 3], 0);

    /// <summary>
    /// Draws both strips over the viewport.
    ///
    /// Returns without drawing when the viewport is too small to carry them, which is a thumbnail
    /// rather than a view — the strips would be most of it.
    /// </summary>
    /// <param name="canvas">What to draw on.</param>
    /// <param name="viewport">The rectangle the strips sit inside.</param>
    /// <param name="frame">Where the board's own numbers land on the screen.</param>
    /// <param name="gridStep">
    /// The spacing of the grid underneath, so the ticks land on its lines. The caller passes its own,
    /// because the two viewports ask the ladder for different densities.
    /// </param>
    /// <param name="ink">The viewport's background, which the strips are a translucent wash of.</param>
    /// <param name="readout">Where the pointer is, or null when it is not over the canvas.</param>
    /// <param name="measure">A measuring drag in progress, or null when there is not one.</param>
    public static void Draw(
        SKCanvas canvas,
        SKRect viewport,
        RulerFrame frame,
        float gridStep,
        SKColor ink,
        RulerReadout? readout = null,
        RulerMeasure? measure = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (viewport.Width < LeftWidth * 5 || viewport.Height < LeftWidth * 5
            || frame.Scale <= 0 || gridStep <= 0)
        {
            return;
        }

        var every = Multiples[^1];

        foreach (var candidate in Multiples)
        {
            if (gridStep * candidate * frame.Scale >= LabelRoom)
            {
                every = candidate;
                break;
            }
        }

        var step = gridStep * every;

        using var strip = new SKPaint { Color = ink.WithAlpha(0xC8), Style = SKPaintStyle.Fill };
        using var line = new SKPaint { Color = InkOn(ink).WithAlpha(0x55), StrokeWidth = 1f, IsAntialias = false };
        using var text = new SKPaint { Color = InkOn(ink).WithAlpha(0xC8), IsAntialias = true };

        canvas.DrawRect(new SKRect(viewport.Left, viewport.Top, viewport.Right, viewport.Top + Thickness), strip);
        canvas.DrawRect(new SKRect(viewport.Left, viewport.Top, viewport.Left + LeftWidth, viewport.Bottom), strip);

        Across(canvas, viewport, frame, gridStep, step, line, text);
        Down(canvas, viewport, frame, gridStep, step, line, text);

        // The unit, once, in the corner the two strips share — out of the way of every number and
        // still the first thing read.
        canvas.DrawText("mm", viewport.Left + 3, viewport.Top + 13, SKTextAlign.Left, Font, text);

        if (measure is { } span)
        {
            Measured(canvas, viewport, frame, span, ink);
        }

        if (readout is { } where)
        {
            Readout(canvas, viewport, where, ink);
        }
    }

    /// <summary>
    /// What to draw the ruler's marks in, against a strip washed in <paramref name="background"/>:
    /// near-black on a light viewport, white on a dark one.
    ///
    /// **It was white whatever the theme**, which on the light theme's #FAFAFB is white on white —
    /// the numbers, the ticks, the pointer's readout and the measuring line all there and none of
    /// them visible. Found when the README's light-theme screenshot was retaken in sprint 5; nothing
    /// had looked at the rulers in the light theme since they shipped.
    /// </summary>
    internal static SKColor InkOn(SKColor background)
    {
        // Relative luminance, Rec. 709 weights on the 0-255 channels. Mid-grey is the line: below it
        // white reads, above it a near-black does.
        var luma = (0.2126 * background.Red) + (0.7152 * background.Green) + (0.0722 * background.Blue);
        return luma > 140 ? new SKColor(0x1B, 0x1F, 0x24) : SKColors.White;
    }

    /// <summary>
    /// The pointer's position, in a box in the top right.
    ///
    /// **Asked for in those words:** *"Put a small rectangular display in the top right corner of the
    /// UI and have it display the coordinates."* Two decimals, because that is the resolution the
    /// findings view prints and a reader comparing the two should not have to decide whether a
    /// difference is real.
    ///
    /// The origin is named under the numbers. A pair of coordinates with no stated origin is the
    /// defect this is meant to fix, one level up.
    /// </summary>
    private static void Readout(SKCanvas canvas, SKRect viewport, RulerReadout readout, SKColor ink)
    {
        var position = string.Create(
            CultureInfo.InvariantCulture,
            $"X {readout.At.X:0.00}   Y {readout.At.Y:0.00}");

        using var panel = new SKPaint { Color = ink.WithAlpha(0xE0), Style = SKPaintStyle.Fill };
        using var edge = new SKPaint { Color = InkOn(ink).WithAlpha(0x33), Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var bold = new SKPaint { Color = InkOn(ink).WithAlpha(0xE6), IsAntialias = true };
        using var faint = new SKPaint { Color = InkOn(ink).WithAlpha(0x99), IsAntialias = true };

        var width = MathF.Max(Font.MeasureText(position), Small.MeasureText(readout.Origin)) + 20;
        var box = new SKRect(viewport.Right - width - 10, viewport.Top + Thickness + 10, viewport.Right - 10, viewport.Top + Thickness + 50);

        canvas.DrawRoundRect(box, 4, 4, panel);
        canvas.DrawRoundRect(box, 4, 4, edge);

        canvas.DrawText(position, box.MidX, box.Top + 18, SKTextAlign.Center, Font, bold);
        canvas.DrawText(readout.Origin, box.MidX, box.Top + 32, SKTextAlign.Center, Small, faint);
    }

    /// <summary>
    /// The measuring drag: a line between two points, and what it spans.
    ///
    /// **A tape measure, not a drawing tool.** It is not saved, nothing else reads it, and letting go
    /// ends it — so it answers "how far is that from that" without becoming a feature with state.
    /// </summary>
    private static void Measured(
        SKCanvas canvas, SKRect viewport, RulerFrame frame, RulerMeasure measure, SKColor ink)
    {
        var a = new SKPoint(frame.ScreenX(measure.From.X), frame.ScreenY(measure.From.Y));
        var b = new SKPoint(frame.ScreenX(measure.To.X), frame.ScreenY(measure.To.Y));

        // White in both themes, unlike the strip and the boxes: this line is drawn over the board, and
        // the board's substrate is dark whatever the theme. Following the viewport's colour made it
        // near-black over near-black in the light theme — found by review of 6.70's own fix.
        using var span = new SKPaint { Color = SKColors.White.WithAlpha(0xDD), StrokeWidth = 1.5f, IsAntialias = true };
        // Brighter than it first was: at 0x55 the two sides were invisible over copper, which is
        // most of a board, so the only shot that showed them was one taken over bare substrate.
        //
        // Built once and kept, because `SKPaint.Dispose` does *not* dispose an attached path
        // effect — `BoardRenderer` disposes its own by hand for the same reason. This runs once per
        // rendered frame for as long as a drag lasts, so a `using` here stranded one native dash
        // per frame until a finaliser caught up.
        using var guide = new SKPaint
        {
            Color = SKColors.White.WithAlpha(0xAA),
            StrokeWidth = 1f,
            IsAntialias = false,
            PathEffect = Dashes,
        };

        // The two sides as well as the hypotenuse, because dx and dy are what somebody moving a
        // machine actually types, and reading them off a diagonal is arithmetic.
        canvas.DrawLine(a.X, a.Y, b.X, a.Y, guide);
        canvas.DrawLine(b.X, a.Y, b.X, b.Y, guide);
        canvas.DrawLine(a.X, a.Y, b.X, b.Y, span);

        var label = string.Create(
            CultureInfo.InvariantCulture,
            $"dx {measure.DeltaX:0.00}   dy {measure.DeltaY:0.00}   {measure.Diagonal:0.00} mm");

        using var panel = new SKPaint { Color = ink.WithAlpha(0xE6), Style = SKPaintStyle.Fill };
        using var text = new SKPaint { Color = InkOn(ink).WithAlpha(0xE6), IsAntialias = true };

        var width = Font.MeasureText(label) + 16;
        var cx = MathF.Min(MathF.Max((a.X + b.X) / 2, viewport.Left + LeftWidth + (width / 2)), viewport.Right - (width / 2));
        var cy = MathF.Max(MathF.Min(a.Y, b.Y) - 14, viewport.Top + Thickness + 12);

        canvas.DrawRoundRect(new SKRect(cx - (width / 2), cy - 11, cx + (width / 2), cy + 5), 3, 3, panel);
        canvas.DrawText(label, cx, cy, SKTextAlign.Center, Font, text);
    }

    /// <summary>The top strip, walked in board millimetres from the first grid line in view.</summary>
    private static void Across(
        SKCanvas canvas,
        SKRect viewport,
        RulerFrame frame,
        float gridStep,
        float step,
        SKPaint line,
        SKPaint text)
    {
        var from = MathF.Floor(frame.BoardX(viewport.Left) / gridStep) * gridStep;
        var to = frame.BoardX(viewport.Right);

        for (var at = from; at <= to; at += gridStep)
        {
            var sx = frame.ScreenX(at);

            if (sx < viewport.Left + LeftWidth || sx > viewport.Right)
            {
                continue;
            }

            // A labelled tick is twice the height of an unlabelled one, which is how the eye finds
            // the numbered ones without reading them.
            var major = OnStep(at, step);

            canvas.DrawLine(sx, viewport.Top + Thickness - (major ? 8 : 4), sx, viewport.Top + Thickness, line);

            // Centred over its own tick, as a ruler's numbers are — beside it reads as belonging to
            // the gap rather than to the line.
            if (major)
            {
                canvas.DrawText(Label(at, step), sx, viewport.Top + 10, SKTextAlign.Center, Font, text);
            }
        }
    }

    /// <summary>The left strip. Board y counts upward, so the walk starts at the bottom.</summary>
    private static void Down(
        SKCanvas canvas,
        SKRect viewport,
        RulerFrame frame,
        float gridStep,
        float step,
        SKPaint line,
        SKPaint text)
    {
        var from = MathF.Floor(frame.BoardY(viewport.Bottom) / gridStep) * gridStep;
        var to = frame.BoardY(viewport.Top);

        for (var at = from; at <= to; at += gridStep)
        {
            var sy = frame.ScreenY(at);

            // Clear of the corner as well as of the strip. A number's baseline sits 3 px above its
            // tick, so one just under the top strip was drawn through the "mm" legend that shares
            // that corner — the legend wins, being drawn last, and the number showed through it.
            if (sy < viewport.Top + Thickness + CornerRoom || sy > viewport.Bottom)
            {
                continue;
            }

            var major = OnStep(at, step);

            canvas.DrawLine(viewport.Left + LeftWidth - (major ? 8 : 4), sy, viewport.Left + LeftWidth, sy, line);

            // Above its tick and against the outer edge, where there is room for three digits and a
            // sign. Right-aligned to the tick would be tidier and is what clipped them.
            if (major)
            {
                canvas.DrawText(Label(at, step), viewport.Left + 3, sy - 3, SKTextAlign.Left, Font, text);
            }
        }
    }

    /// <summary>
    /// Whether a millimetre mark is one of the labelled ones.
    ///
    /// Against a fraction of the step rather than against zero, because the walk accumulates: adding
    /// 0.1 mm six hundred times does not land on 60 exactly, and an equality test would drop labels
    /// at the far end of a long board.
    /// </summary>
    private static bool OnStep(float millimetres, float step) =>
        MathF.Abs(millimetres - (MathF.Round(millimetres / step) * step)) < step / 100;

    /// <summary>
    /// A tick's number, with only the decimals its own spacing needs.
    ///
    /// At a 0.1 mm step "12.5" and "12.6" have to be told apart; at 25 mm "25" is the whole of it and
    /// "25.0" is noise on a strip this narrow. Negative zero is spelled "0", because a board whose
    /// corner is a rounding below the origin should not say "-0".
    /// </summary>
    private static string Label(float millimetres, float step)
    {
        var rounded = MathF.Round(millimetres / step) * step;

        if (MathF.Abs(rounded) < step / 100)
        {
            return "0";
        }

        return step >= 1
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
