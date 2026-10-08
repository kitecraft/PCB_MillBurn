using System.Globalization;
using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// A place on the board the operator asked to be taken to, and what to call it.
/// </summary>
/// <param name="At">Where, in board millimetres, measured from the corner the ruler reads from.</param>
/// <param name="Coordinate">
/// The coordinate as the findings view printed it. Carried as text rather than formatted here, so
/// the mark on the board and the line that was clicked cannot disagree about a rounding.
/// </param>
/// <param name="Nets">
/// What is joined there, in the findings view's own words, or empty when there is nothing to say.
///
/// **The marker has to carry its own label**, because closing the findings window does not clear it:
/// a bare ring left on the board after the list is gone is a mark nobody can interpret.
/// </param>
public readonly record struct BoardMarker(SKPoint At, string Coordinate, string Nets);

/// <summary>
/// Draws the marker: a ring where the finding is, crosshairs out to the rulers, a pointer on each
/// strip, and the coordinate beside it.
///
/// **A marker, not a selection.** Nothing acts on it and nothing is measured from it — measuring is
/// the ruler's drag. Calling it a selection would invite both.
/// </summary>
public static class Marker
{
    /// <summary>
    /// How much board to show across the viewport when going to a finding, in millimetres.
    ///
    /// **It has to zoom, not only centre.** A bridge is a fraction of a millimetre across, so at
    /// whole-board zoom it is smaller than a pixel, and a view that merely centred would leave the
    /// operator looking at the same board with a marker they cannot see. Fourteen millimetres is
    /// what the chosen mockup shows — `WorkingFolder/mockups/rulers/E-zoomed-to-finding.png` reads
    /// 37.0 to 51.0 across — the bridge plus enough copper either side to see which traces it joins.
    /// </summary>
    public const float SpanMm = 14f;

    /// <summary>The ring's radius on screen. Constant in pixels, because it marks rather than measures.</summary>
    private const float Radius = 22f;

    private static readonly SKFont Font =
        new(SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default, 12f);

    private static readonly SKFont Small = new(SKTypeface.Default, 11f);

    /// <summary>
    /// The colour of every part of the mark. Not a layer colour and not a theme token: it has to
    /// stay legible over copper, over substrate and over a toolpath, and it must not read as one
    /// more thing the board is made of.
    /// </summary>
    private static readonly SKColor Ink = new(0xFF, 0x5B, 0x7A);

    /// <summary>
    /// Pixels per millimetre that puts <see cref="SpanMm"/> of board across the viewport.
    ///
    /// Taken from the shorter side, so the span is what you actually get in both directions rather
    /// than a promise about the wide one that a tall window quietly breaks.
    /// </summary>
    /// <param name="width">The viewport's width in pixels.</param>
    /// <param name="height">Its height.</param>
    /// <param name="spanMm">How much board to show across it.</param>
    public static float ScaleFor(float width, float height, float spanMm = SpanMm)
    {
        var shorter = MathF.Min(width, height);

        return shorter <= 0 || spanMm <= 0 ? 0 : shorter / spanMm;
    }

    /// <summary>
    /// Draws the mark over whatever is already there.
    /// </summary>
    /// <param name="canvas">The canvas, with no transform applied — this is screen furniture.</param>
    /// <param name="viewport">The rectangle being drawn into.</param>
    /// <param name="frame">Where the board's own numbers land on screen.</param>
    /// <param name="marker">The place and its label.</param>
    /// <param name="rulers">
    /// Whether the scale strips are drawn, which decides where the crosshairs stop and whether the
    /// strip pointers are drawn at all. With the rulers off there is no strip to point at.
    /// </param>
    public static void Draw(
        SKCanvas canvas, SKRect viewport, RulerFrame frame, BoardMarker marker, bool rulers)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (frame.Scale <= 0)
        {
            return;
        }

        var x = frame.ScreenX(marker.At.X);
        var y = frame.ScreenY(marker.At.Y);

        var left = viewport.Left + (rulers ? Ruler.LeftWidth : 0);
        var top = viewport.Top + (rulers ? Ruler.Thickness : 0);

        // Off screen entirely: nothing to point at, and a ring drawn at the clip edge would claim
        // the fault is there. The caller moved the view onto it, so this is the case where somebody
        // has since panned away — and panning away is allowed.
        if (x < left || x > viewport.Right || y < top || y > viewport.Bottom)
        {
            return;
        }

        // **Everything is drawn twice, dark underneath.** The mark has one colour and the board has
        // two extremes — bare substrate and bright copper — and a single stroke that reads on one
        // disappears into the other. The first shot of this put a pink ring on orange copper that
        // could barely be found, which is the fault the measuring drag's guide lines had a story
        // ago; a dark halo is cheaper than choosing a colour per background and cannot be wrong.
        using var halo = new SKPaint
        {
            Color = new SKColor(0x10, 0x10, 0x12, 0xAA),
            StrokeWidth = 4f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        using var hairHalo = new SKPaint
        {
            Color = new SKColor(0x10, 0x10, 0x12, 0x88),
            StrokeWidth = 3f,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
        };

        using var hair = new SKPaint
        {
            Color = Ink.WithAlpha(0xCC),
            StrokeWidth = 1f,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
        };

        using var ring = new SKPaint
        {
            Color = Ink,
            StrokeWidth = 2f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        // Out to the rulers rather than across the whole viewport, and broken either side of the
        // ring: the copper inside the ring is the thing being looked at, and a line through it is a
        // line over the answer.
        foreach (var paint in new[] { hairHalo, hair })
        {
            canvas.DrawLine(left, y, x - Radius, y, paint);
            canvas.DrawLine(x + Radius, y, viewport.Right, y, paint);
            canvas.DrawLine(x, top, x, y - Radius, paint);
            canvas.DrawLine(x, y + Radius, x, viewport.Bottom, paint);
        }

        canvas.DrawCircle(x, y, Radius, halo);
        canvas.DrawCircle(x, y, Radius, ring);

        if (rulers)
        {
            Pointer(canvas, x, top, down: true);
            Pointer(canvas, left, y, down: false);
        }

        Label(canvas, viewport, x, y, marker, left, top);
    }

    /// <summary>
    /// A filled triangle on a ruler strip, so the finding's own number can be read off the scale.
    /// </summary>
    private static void Pointer(SKCanvas canvas, float x, float y, bool down)
    {
        using var fill = new SKPaint { Color = Ink, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var path = new SKPath();

        if (down)
        {
            path.MoveTo(x, y);
            path.LineTo(x - 5, y - 7);
            path.LineTo(x + 5, y - 7);
        }
        else
        {
            path.MoveTo(x, y);
            path.LineTo(x - 7, y - 5);
            path.LineTo(x - 7, y + 5);
        }

        path.Close();
        canvas.DrawPath(path, fill);
    }

    /// <summary>
    /// The coordinate, and under it what is joined there.
    ///
    /// Placed up and to the right of the ring, and flipped to the other side when that would run it
    /// off the viewport — a label half outside the window names nothing.
    /// </summary>
    private static void Label(
        SKCanvas canvas, SKRect viewport, float x, float y, BoardMarker marker,
        float left, float top)
    {
        var hasNets = marker.Nets.Length > 0;

        // **Both lines go inside one box, and the box is as wide as the wider of them.** They were
        // split at first — the coordinate boxed, the nets line loose underneath in the mark's own
        // colour — and that is unreadable: pink on orange copper, over most of a board. It is the
        // same lesson the measuring drag's guide lines taught one story ago, and the same fix.
        //
        // It also sized the box from the nets line while drawing only the coordinate in it, so a
        // long net name produced a box three times the width of the text it contained.
        var width = MathF.Max(
            Font.MeasureText(marker.Coordinate),
            hasNets ? Small.MeasureText(marker.Nets) : 0) + 16;

        var height = hasNets ? 36f : 20f;

        // **Clamped after the flip, not instead of it.** Flipping tested the right edge only and
        // then used the left-hand position unchecked, so a long net name on a marker in the left
        // half of a narrow viewport put most of the box outside the window — the very outcome the
        // flip exists to prevent, reached by the flip itself.
        var boxLeft = x + Radius + 8 + width < viewport.Right
            ? x + Radius + 8
            : x - Radius - 8 - width;

        boxLeft = Math.Clamp(boxLeft, left + 2, MathF.Max(left + 2, viewport.Right - width - 2));

        // Below the top strip rather than below the viewport, which is not the same line: clamped
        // to the viewport, a marker near the top of the board laid its label over the very scale
        // its own pointer is pointing at.
        var boxTop = MathF.Max(y - Radius - height + 10, top + 2);

        using var panel = new SKPaint
        {
            Color = new SKColor(0x18, 0x1A, 0x1D, 0xEE),
            Style = SKPaintStyle.Fill,
        };

        using var edge = new SKPaint
        {
            Color = Ink,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = true,
        };

        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var nets = new SKPaint { Color = Ink, IsAntialias = true };

        var box = new SKRect(boxLeft, boxTop, boxLeft + width, boxTop + height);

        canvas.DrawRoundRect(box, 3, 3, panel);

        // Outlined in the mark's own colour, so the box reads as belonging to the ring rather than
        // as one more panel the window happens to be showing.
        canvas.DrawRoundRect(box, 3, 3, edge);

        canvas.DrawText(marker.Coordinate, boxLeft + 8, boxTop + 14, SKTextAlign.Left, Font, text);

        if (hasNets)
        {
            canvas.DrawText(marker.Nets, boxLeft + 8, boxTop + 30, SKTextAlign.Left, Small, nets);
        }
    }

    /// <summary>Formats a place the way the findings view prints it, so the two always agree.</summary>
    public static string CoordinateOf(float xMm, float yMm) =>
        string.Create(CultureInfo.InvariantCulture, $"{xMm:0.00}, {yMm:0.00} mm");
}
