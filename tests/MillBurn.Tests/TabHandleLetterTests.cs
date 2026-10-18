using MillBurn.Viewer;
using SkiaSharp;

namespace MillBurn.Tests;

/// <summary>
/// A tab's letter can be read on either theme.
///
/// Found retaking the README's light-theme screenshot for 0.7.0: the letters were amber with a dark
/// halo whatever the theme, and on the light theme's #FAFAFB that came out as pale ink in a grey
/// smudge. The rulers had the same fault for 0.6.0 and the same answer — the ink chosen from the
/// viewport's own colour.
/// </summary>
public sealed class TabHandleLetterTests
{
    private static readonly SKColor LightViewport = new(0xFA, 0xFA, 0xFB);
    private static readonly SKColor DarkViewport = new(0x0F, 0x12, 0x14);

    private static double Luma(SKColor c) => (0.2126 * c.Red) + (0.7152 * c.Green) + (0.0722 * c.Blue);

    private static SKColor Blend(SKColor over, SKColor under, double alpha) => new(
        (byte)Math.Round((over.Red * alpha) + (under.Red * (1 - alpha))),
        (byte)Math.Round((over.Green * alpha) + (under.Green * (1 - alpha))),
        (byte)Math.Round((over.Blue * alpha) + (under.Blue * (1 - alpha))));

    /// <summary>
    /// On the light theme the letter is a dark ink with a light halo; on the dark theme, the
    /// handles' amber with a dark halo. Measured as luminance, the way the rulers' rule is.
    /// </summary>
    [Fact]
    public void TheLetterIsInkedAgainstTheViewportsOwnColour()
    {
        var (lightInk, lightHalo) = TabHandles.LetterColours(LightViewport);
        Assert.True(Luma(lightInk) < 100, $"light theme ink luma {Luma(lightInk):F0}");
        Assert.True(Luma(lightHalo) > 200, $"light theme halo luma {Luma(lightHalo):F0}");

        var (darkInk, darkHalo) = TabHandles.LetterColours(DarkViewport);
        Assert.True(Luma(darkInk) > 140, $"dark theme ink luma {Luma(darkInk):F0}");
        Assert.True(Luma(darkHalo) < 40, $"dark theme halo luma {Luma(darkHalo):F0}");
    }

    /// <summary>
    /// **And the drawing uses it.** One handle drawn on each theme's viewport: somewhere in the
    /// letter, beside the handle on its outward side, a pixel is the theme's own ink. A draw that
    /// ignored the viewport's colour would paint the dark theme's amber on the light one, and fail
    /// here.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheDrawnLetterIsInTheThemesInk(bool light)
    {
        var background = light ? LightViewport : DarkViewport;
        var (pure, _) = TabHandles.LetterColours(background);

        // An unlit letter is drawn at 90 % opacity, so the colour a fully covered pixel shows is the
        // ink blended over the viewport — compared against that, not the pure ink, which leaves the
        // threshold its margin whatever the font's weight on the machine running this.
        var ink = Blend(pure, background, 0xE6 / 255.0);

        // With the letter, a pixel beside the handle is the theme's ink...
        var withLetter = NearestToInkBesideTheHandle(background, ink, "A");
        Assert.True(withLetter < 60, $"no pixel of the letter is near the {(light ? "light" : "dark")} theme's ink; nearest differs by {withLetter}");

        // ...and without one, nothing there is: the handle, its ring and its gap ticks stay out of
        // the window, so only the letter can have passed the line above.
        var without = NearestToInkBesideTheHandle(background, ink, "");
        Assert.True(without >= 60, $"something other than the letter is near the ink beside the handle; nearest differs by {without}");
    }

    /// <summary>
    /// One handle drawn on the viewport, and how close any pixel in the letter's window comes to
    /// the ink. Board (10, 10) mm at 8 px/mm is screen (180, 270); outward is +x, so the letter's
    /// centre sits 17 px to the right of the handle's, inside x 188–208, y 258–282.
    /// </summary>
    private static double NearestToInkBesideTheHandle(SKColor background, SKColor ink, string letter)
    {
        var overlay = new TabOverlay
        {
            Profile = [new SKPoint(10, 0), new SKPoint(10, 20)],
            Tabs = [new TabHandle(new SKPoint(10, 10), 2f, false, new SKPoint(10, 9), new SKPoint(10, 11), letter, new SKPoint(1, 0))],
        };

        using var bitmap = new SKBitmap(400, 400);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(background);
            TabHandles.Draw(
                canvas,
                new SKRect(0, 0, 400, 400),
                new RulerFrame(Scale: 8f, ScreenXAtZero: 100f, ScreenYAtZero: 350f),
                overlay,
                background);
        }

        var nearest = double.MaxValue;

        for (var x = 188; x <= 208; x++)
        {
            for (var y = 258; y <= 282; y++)
            {
                var p = bitmap.GetPixel(x, y);
                var distance = Math.Abs(p.Red - ink.Red) + Math.Abs(p.Green - ink.Green) + Math.Abs(p.Blue - ink.Blue);
                nearest = Math.Min(nearest, distance);
            }
        }

        return nearest;
    }
}
