using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;

namespace MillBurn.App.Rendering;

/// <summary>
/// Reads a theme token as a Skia colour.
///
/// The tokens are declared as brushes inside <c>ThemeDictionaries</c>, which is what makes them
/// switch with the window's variant (see Themes/Tokens.axaml). Render code needs an
/// <see cref="SKColor"/> rather than an <see cref="IBrush"/>, so this unwraps one — keeping a
/// single definition per token instead of a Color and a Brush that can quietly drift apart.
///
/// The variant is passed explicitly rather than left to the ambient scope, because that is what
/// makes a repaint after a theme toggle pick up the new value.
/// </summary>
internal static class ThemeTokens
{
    public static SKColor Resolve(Control control, string token, SKColor fallback)
    {
        if (!control.TryFindResource(token, control.ActualThemeVariant, out var value))
        {
            return fallback;
        }

        return value switch
        {
            ISolidColorBrush brush => ToSkia(brush.Color),
            Color color => ToSkia(color),
            _ => fallback,
        };
    }

    private static SKColor ToSkia(Color c) => new(c.R, c.G, c.B, c.A);
}
