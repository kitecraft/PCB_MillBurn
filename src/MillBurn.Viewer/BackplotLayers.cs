using MillBurn.Core;
using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>One backplot layer, ready to hand to the scene builder alongside the board.</summary>
/// <param name="Id">Unique in the scene. Carries the source program, so one layer's cuts can be
/// shown without the others'.</param>
/// <param name="ColourKey">
/// What this is coloured by, which is the *role* rather than the source: every program's cutting
/// moves are one colour. Kept apart from <paramref name="Id"/> so that splitting the backplot per
/// program did not silently orphan every colour anybody had already chosen.
/// </param>
/// <param name="Label">What the panel and the scene call it.</param>
/// <param name="Style">Stroke colour and width.</param>
/// <param name="Runs">Polylines, already flattened and in board coordinates.</param>
/// <param name="VisibleByDefault">Whether it is drawn before anybody touches anything.</param>
/// <param name="Source">The layer file this came from, or empty when it came from everything.</param>
public readonly record struct BackplotLayer(
    string Id,
    string Label,
    BoardLayerStyle Style,
    IReadOnlyList<IReadOnlyList<Point2>> Runs,
    bool VisibleByDefault,
    string ColourKey = "",
    string Source = "")
{
    /// <summary>The colour key if one was given, else the id — which is what it used to be.</summary>
    public string Palette => ColourKey.Length > 0 ? ColourKey : Id;
}

/// <summary>
/// Colours and defaults for drawing a program over the board it was made from.
///
/// Drawn as an overlay rather than in its own view, because the question a backplot answers is
/// "does this go where I meant" — and that is only answerable with the copper underneath it.
/// </summary>
public static class BackplotPalette
{
    // The board already owns orange (top copper), blue (bottom copper), dark green (substrate),
    // white (silk) and grey (outline). A backplot drawn in any of those is invisible exactly where
    // it matters most — the first attempt used blue for cuts and vanished into the ground pour it
    // was drawn on top of. So these are deliberately chosen from the hues the board does not use.

    /// <summary>Yellow: nothing on a board is yellow.</summary>
    public static BoardLayerStyle Cut { get; } =
        new(new SKColor(0xFF, 0xD7, 0x40), 1f, Outlined: true, StrokePixels: 1.8f);

    public static BoardLayerStyle Plunge { get; } =
        new(new SKColor(0xFF, 0x17, 0x44), 1f, Outlined: true, StrokePixels: 2.5f);

    /// <summary>Dashed and dim: present so the optimizer's work is visible, not to be read.</summary>
    public static BoardLayerStyle Travel { get; } =
        new(new SKColor(0xB0, 0xBE, 0xC5), 0.55f, Outlined: true, StrokePixels: 0.9f, DashPixels: 3f);

    /// <summary>
    /// The long ones, in magenta. This is the layer that makes "edge cuts all over the place"
    /// visible at a glance — the complaint this whole project started from — so it must not be
    /// confusable with the copper underneath it.
    /// </summary>
    public static BoardLayerStyle LongTravel { get; } =
        new(new SKColor(0xFF, 0x4F, 0xD8), 0.95f, Outlined: true, StrokePixels: 1.6f, DashPixels: 4f);

    /// <summary>A rapid at cutting depth. Never legitimate, so it is drawn loud.</summary>
    public static BoardLayerStyle Gouge { get; } =
        new(new SKColor(0xFF, 0x00, 0x2B), 1f, Outlined: true, StrokePixels: 4f);
}
