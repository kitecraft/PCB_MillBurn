using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// One tab as the viewport shows it: where it is, how wide a gap it leaves, where that gap starts
/// and ends on the loop, and whether a hand chose the position.
/// </summary>
/// <param name="At">The tab's centre, in board millimetres from the corner the ruler reads from —
/// the same space <see cref="BoardMarker.At"/> uses, so the two kinds of furniture cannot disagree
/// about where a place is.</param>
/// <param name="WidthMm">The gap the tab leaves in the cut, in millimetres.</param>
/// <param name="ByHand">True when the position was set by hand rather than by the planner (6.4).</param>
/// <param name="GapFrom">Where the cut stops: the gap's near end, walked along the loop half a
/// width back from the centre. Board millimetres, like <paramref name="At"/>.</param>
/// <param name="GapTo">Where the cut resumes: the gap's far end, half a width on.</param>
/// <param name="Letter">The tab's name — A, B … AA — drawn beside the handle, and the name the tabs
/// window and the width popup use for it.</param>
/// <param name="Outward">A unit direction in board millimetres pointing off the board at this tab:
/// the side of the cut path the board is not on, which is where the letter goes so it never sits
/// on the copper. Zero when the loop gave no direction; the letter then sits above the handle.</param>
public readonly record struct TabHandle(
    SKPoint At, float WidthMm, bool ByHand, SKPoint GapFrom, SKPoint GapTo,
    string Letter = "", SKPoint Outward = default);

/// <summary>
/// The tabs and the cut path they live on, as the viewport draws and picks them (6.4).
///
/// The profile is the *offset* contour — the path the cutter follows, not the board's edge — which
/// is the only line a tab can exist on. The view hit-tests against this; the geometry of "where
/// along the loop" stays in the planner's own types.
/// </summary>
public sealed record TabOverlay
{
    /// <summary>The cut path, in board millimetres, closed implicitly from last point to first.</summary>
    public required IReadOnlyList<SKPoint> Profile { get; init; }

    public required IReadOnlyList<TabHandle> Tabs { get; init; }

    /// <summary>The tab under the pointer, or -1. A handle has to say it is grabbable.</summary>
    public int Hot { get; init; } = -1;

    /// <summary>The tab being dragged, or -1. Drawn like <see cref="Hot"/>, kept apart so a drag
    /// stays lit when the pointer runs ahead of the projected handle.</summary>
    public int Dragging { get; init; } = -1;
}

/// <summary>
/// Draws the tab handles and the cut path they slide along.
///
/// **Handles, not marks.** The marker says "look here"; a handle says "take hold". It is drawn at a
/// constant screen size because it is a target for a pointer, not a feature of the board — and the
/// faint dashed loop under the handles is the answer to "slide it where?", the one line a tab can
/// be on.
/// </summary>
public static class TabHandles
{
    /// <summary>A handle's radius on screen.</summary>
    public const float Radius = 6f;

    /// <summary>
    /// How close a pointer has to be to take hold of a handle or the path, in pixels. Larger than
    /// the drawn radius, because a target the size of the picture is a target people miss.
    /// </summary>
    public const float HitRadius = 10f;

    /// <summary>
    /// The handles' colour. Amber, not the marker's pink: the two kinds of furniture appear
    /// together and mean different things — one marks a fault, the other offers a grip.
    /// </summary>
    private static readonly SKColor Ink = new(0xFF, 0xB1, 0x3B);

    /// <summary>
    /// The gap ticks' colour. Cyan, asked for from the bench — *"A contrasting colour might be
    /// nice"* — and chosen as amber's opposite: the ticks say where the cut stops and resumes,
    /// which is a fact about the gap rather than about the grip, and nothing else in the viewport
    /// wears this colour.
    /// </summary>
    private static readonly SKColor GapInk = new(0x3B, 0xD9, 0xFF);

    private static readonly SKColor Halo = new(0x10, 0x10, 0x12, 0xAA);

    /// <summary>The letters' face: bold and small, a name beside a grip rather than a caption.</summary>
    private static readonly SKFont LetterFont =
        new(SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default, 11f);

    /// <summary>
    /// How far the letter's centre sits from the handle's, on screen: clear of the lit handle's
    /// ring and of the gap ticks across the path, close enough to read as the handle's own.
    /// </summary>
    private const float LetterOffset = Radius + 11f;

    /// <summary>
    /// What a tab's letter is drawn in against a viewport washed in <paramref name="background"/>:
    /// the ink, and the halo that separates it from whatever is behind.
    ///
    /// **Amber with a dark halo on the dark theme; a dark amber with a light halo on the light
    /// one.** The letters were amber with the dark halo whatever the theme, and on the light
    /// theme's #FAFAFB that is a pale ink in a grey smudge — legible if you look for it, muddy if
    /// you do not. Found retaking the README's light-theme screenshot for 0.7.0, the same way the
    /// rulers' white-on-white was found for 0.6.0, and decided the same way: by the background's
    /// luminance (<see cref="Ruler.InkOn"/>). The handles keep their amber in both: a grip is a
    /// colour you learn, and the letter beside it is what has to be read.
    /// </summary>
    public static (SKColor Ink, SKColor Halo) LetterColours(SKColor background) =>
        OnLight(background)
            ? (DarkAmber, new SKColor(0xFF, 0xFF, 0xFF, 0xD9))
            : (Ink, Halo);

    /// <summary>The one test of which theme the letters are on, used by the colours and the paints alike.</summary>
    private static bool OnLight(SKColor background) => Ruler.InkOn(background) != SKColors.White;

    /// <summary>The letters' ink on a light viewport: the handles' amber, darkened until it reads.</summary>
    private static readonly SKColor DarkAmber = new(0x7A, 0x43, 0x00);

    /// <summary>
    /// The letters' paints, made once per theme: they never change, and the handles redraw on
    /// every hover change and every pointer move of a drag — on the panel, that is a paint pair
    /// per tab per frame for nothing.
    /// </summary>
    private static readonly LetterPaints DarkLetters = new(LetterColours(SKColors.Black));

    private static readonly LetterPaints LightLetters = new(LetterColours(SKColors.White));

    /// <summary>The paints for this viewport, by the same test <see cref="LetterColours"/> uses.</summary>
    private static LetterPaints LettersOn(SKColor background) => OnLight(background) ? LightLetters : DarkLetters;

    private sealed class LetterPaints((SKColor Ink, SKColor Halo) colours)
    {
        public SKPaint Halo { get; } = new()
        {
            Color = colours.Halo,
            StrokeWidth = 4f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Round,
        };

        public SKPaint Ink { get; } = new()
        {
            Color = colours.Ink.WithAlpha(0xE6),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        public SKPaint InkLit { get; } = new()
        {
            Color = colours.Ink,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
    }

    /// <summary>
    /// Draws the loop and the handles over the board. Screen furniture: the canvas carries no
    /// transform, and <paramref name="frame"/> says where the board's numbers land;
    /// <paramref name="background"/> is the viewport's own colour, which the letters are inked
    /// against (<see cref="LetterColours"/>).
    /// </summary>
    public static void Draw(SKCanvas canvas, SKRect viewport, RulerFrame frame, TabOverlay tabs, SKColor background)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tabs);

        if (frame.Scale <= 0)
        {
            return;
        }

        DrawProfile(canvas, frame, tabs.Profile);

        // Chosen once per frame, not once per tab.
        var letters = LettersOn(background);

        for (var i = 0; i < tabs.Tabs.Count; i++)
        {
            var tab = tabs.Tabs[i];
            var x = frame.ScreenX(tab.At.X);
            var y = frame.ScreenY(tab.At.Y);

            if (x < viewport.Left - Radius || x > viewport.Right + Radius
                || y < viewport.Top - Radius || y > viewport.Bottom + Radius)
            {
                continue;
            }

            var lit = i == tabs.Hot || i == tabs.Dragging;

            // The gap the tab leaves, marked at its two ends with ticks *across* the path, in the
            // gap's own colour. The first cut drew the width as a bar through the handle — always
            // horizontal, because nothing told it which way the edge ran — and the bench read both
            // faults off it at once: *"on a horizontal edge, it'd difficult to notice, and because
            // the bar takes up the full gap width, it's also difficult to tell the gap."* A bar
            // lying along a horizontal edge disappears into the cut line and paints over the very
            // gap it names. Two ticks where the cut stops and resumes are visible at any
            // orientation and leave the gap itself unpainted, so what is between them is the
            // board's own picture. Skipped while this tab is being dragged: the ends belong to the
            // planner's answer, and mid-drag there is none yet.
            if (i != tabs.Dragging)
            {
                DrawGapTicks(canvas, frame, tab, lit);
            }

            using var halo = new SKPaint
            {
                Color = Halo,
                StrokeWidth = 5f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
            };

            using var fill = new SKPaint
            {
                // A hand-placed tab is solid and a planned one is hollow: the program's header
                // makes the same distinction in words, and the picture should not need the words.
                Color = tab.ByHand || lit ? Ink : new SKColor(0x18, 0x1A, 0x1D, 0xEE),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            using var ring = new SKPaint
            {
                Color = Ink,
                StrokeWidth = lit ? 2.5f : 1.5f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
            };

            var radius = lit ? Radius + 2f : Radius;

            canvas.DrawCircle(x, y, radius + 1.5f, halo);
            canvas.DrawCircle(x, y, radius, fill);
            canvas.DrawCircle(x, y, radius, ring);

            DrawLetter(canvas, frame, tab, x, y, lit, letters);
        }
    }

    /// <summary>
    /// The tab's letter, off the board side of the handle.
    ///
    /// **Outside, always** — asked for from the bench: the letter is a name, and a name printed
    /// over copper is a name in the way of the thing it names. The outward direction is a board
    /// direction, so it is carried through the frame rather than assumed: whatever the frame does
    /// to the axes, the letter lands on the side the board is not.
    /// </summary>
    private static void DrawLetter(
        SKCanvas canvas, RulerFrame frame, TabHandle tab, float x, float y, bool lit, LetterPaints paints)
    {
        if (string.IsNullOrEmpty(tab.Letter))
        {
            return;
        }

        var dx = frame.ScreenX(tab.At.X + tab.Outward.X) - x;
        var dy = frame.ScreenY(tab.At.Y + tab.Outward.Y) - y;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));

        if (length < 1e-3f)
        {
            dx = 0;
            dy = -1;
            length = 1;
        }

        var cx = x + (dx / length * LetterOffset);
        var cy = y + (dy / length * LetterOffset);

        // Centred on the point, vertically too: the baseline sits half a cap height below it.
        var baseline = cy + (LetterFont.Size * 0.36f);

        canvas.DrawText(tab.Letter, cx, baseline, SKTextAlign.Center, LetterFont, paints.Halo);
        canvas.DrawText(tab.Letter, cx, baseline, SKTextAlign.Center, LetterFont, lit ? paints.InkLit : paints.Ink);
    }

    /// <summary>
    /// Two ticks across the path where the tab's gap starts and ends.
    ///
    /// Perpendicular to the chord between the ends, which for a gap a few millimetres long is the
    /// path's own direction to within nothing. Drawn only once the gap is wider on screen than the
    /// handle sitting in it — nearer in, the ticks would be under the circle, saying nothing.
    /// </summary>
    private static void DrawGapTicks(SKCanvas canvas, RulerFrame frame, TabHandle tab, bool lit)
    {
        var fx = frame.ScreenX(tab.GapFrom.X);
        var fy = frame.ScreenY(tab.GapFrom.Y);
        var tx = frame.ScreenX(tab.GapTo.X);
        var ty = frame.ScreenY(tab.GapTo.Y);

        var dx = tx - fx;
        var dy = ty - fy;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));

        if (length <= (2f * Radius) + 4f)
        {
            return;
        }

        // The chord's normal, half a tick long each way.
        const float Tick = 5f;
        var nx = -dy / length * Tick;
        var ny = dx / length * Tick;

        using var halo = new SKPaint
        {
            Color = Halo,
            StrokeWidth = 5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        using var ink = new SKPaint
        {
            Color = lit ? GapInk : GapInk.WithAlpha(0xCC),
            StrokeWidth = 3f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        canvas.DrawLine(fx - nx, fy - ny, fx + nx, fy + ny, halo);
        canvas.DrawLine(tx - nx, ty - ny, tx + nx, ty + ny, halo);
        canvas.DrawLine(fx - nx, fy - ny, fx + nx, fy + ny, ink);
        canvas.DrawLine(tx - nx, ty - ny, tx + nx, ty + ny, ink);
    }

    /// <summary>
    /// The cut path, dashed and faint: present enough to say where a tab can go, quiet enough not
    /// to read as one more layer of the board.
    /// </summary>
    private static void DrawProfile(SKCanvas canvas, RulerFrame frame, IReadOnlyList<SKPoint> loop)
    {
        if (loop.Count < 2)
        {
            return;
        }

        using var path = new SKPath();

        path.MoveTo(frame.ScreenX(loop[0].X), frame.ScreenY(loop[0].Y));

        for (var i = 1; i < loop.Count; i++)
        {
            path.LineTo(frame.ScreenX(loop[i].X), frame.ScreenY(loop[i].Y));
        }

        path.Close();

        using var dash = SKPathEffect.CreateDash([4f, 4f], 0);

        using var halo = new SKPaint
        {
            Color = Halo.WithAlpha(0x66),
            StrokeWidth = 3f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = dash,
        };

        using var ink = new SKPaint
        {
            Color = Ink.WithAlpha(0x99),
            StrokeWidth = 1f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = dash,
        };

        canvas.DrawPath(path, halo);
        canvas.DrawPath(path, ink);
    }
}
