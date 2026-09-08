using MillBurn.Core;
using SkiaSharp;

namespace MillBurn.Viewer;

/// <summary>
/// Default colours for a board view.
///
/// It lives here rather than in the app so the window, a headless PNG and any future export all
/// paint the same board. The app is free to override any entry from its theme tokens; what matters
/// is that there is one default and not three that drift.
///
/// The choices are conventional on purpose — copper reads as copper, silk as white, mask as green
/// — because a viewer's job is to let someone recognise their board at a glance and say "that is
/// wrong", which is harder in a palette they have to learn first.
/// </summary>
public static class BoardPalette
{
    public static BoardLayerStyle For(LayerRole role) => role switch
    {
        LayerRole.TopCopper => new BoardLayerStyle(new SKColor(0xC8, 0x71, 0x37), 0.95f),
        LayerRole.InnerCopper => new BoardLayerStyle(new SKColor(0xA0, 0x5A, 0x2C), 0.75f),
        LayerRole.BottomCopper => new BoardLayerStyle(new SKColor(0x4A, 0x8D, 0xB8), 0.80f),

        LayerRole.TopMask => new BoardLayerStyle(new SKColor(0x2E, 0x7D, 0x32), 0.45f),
        LayerRole.BottomMask => new BoardLayerStyle(new SKColor(0x1B, 0x5E, 0x20), 0.45f),

        LayerRole.TopSilk => new BoardLayerStyle(new SKColor(0xF5, 0xF5, 0xF5), 0.95f),
        LayerRole.BottomSilk => new BoardLayerStyle(new SKColor(0xBD, 0xBD, 0xBD), 0.85f),

        LayerRole.TopPaste => new BoardLayerStyle(new SKColor(0x9E, 0x9E, 0x9E), 0.60f),
        LayerRole.BottomPaste => new BoardLayerStyle(new SKColor(0x75, 0x75, 0x75), 0.60f),

        // Holes are the absence of material, so they are painted as the substrate showing through
        // rather than as another coloured layer stacked on top.
        LayerRole.PlatedDrill => new BoardLayerStyle(new SKColor(0x14, 0x1A, 0x1F), 1f),
        LayerRole.NonPlatedDrill => new BoardLayerStyle(new SKColor(0x0A, 0x0D, 0x10), 1f),

        // Outlined, never filled: the profile says where the board ends, and filling it would paint
        // a slab over everything it is meant to frame.
        LayerRole.Outline => new BoardLayerStyle(new SKColor(0xE0, 0xE0, 0xE0), 0.9f, Outlined: true),

        _ => new BoardLayerStyle(new SKColor(0xFF, 0x6D, 0x00), 0.7f),
    };

    /// <summary>
    /// The board material itself, drawn under every layer.
    ///
    /// Not decoration. Layer colours model physical reality — copper is copper, silk is white — so
    /// they cannot follow the UI theme, and white silk on a light theme background is invisible.
    /// Giving the board its own substrate means every layer keeps its contrast whatever the rest
    /// of the window is doing, and it is what the board actually looks like.
    /// </summary>
    public static BoardLayerStyle Substrate { get; } =
        new(new SKColor(0x1E, 0x2A, 0x1C), 1f);

    public static SKColor Background { get; } = new(0x0D, 0x11, 0x17);

    public static SKColor Grid { get; } = new(0x1E, 0x27, 0x31);
}
