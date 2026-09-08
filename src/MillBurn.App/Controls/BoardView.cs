using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MillBurn.App.Rendering;
using MillBurn.Viewer;
using SkiaSharp;

namespace MillBurn.App.Controls;

/// <summary>
/// The board viewport: pan, zoom, fit, and a live frame cost.
///
/// Deliberately a sibling of <see cref="ToolpathView"/> rather than a shared base class. They look
/// alike today and are about to diverge — the toolpath view grows level-of-detail, tier selection
/// and travel-move filtering, none of which a board needs — and a premature base class would end
/// up carrying both sets of concerns for neither's benefit.
/// </summary>
public sealed class BoardView : Control
{
    public static readonly StyledProperty<BoardScene?> SceneProperty =
        AvaloniaProperty.Register<BoardView, BoardScene?>(nameof(Scene));

    public BoardScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    private float _scale = 4f;
    private float _offsetX;
    private float _offsetY;
    private bool _hasFitted;
    private object? _fittedFor;

    private Point? _dragOrigin;
    private float _dragOffsetX;
    private float _dragOffsetY;

    static BoardView()
    {
        AffectsRender<BoardView>(SceneProperty);
        FocusableProperty.OverrideDefaultValue<BoardView>(true);
    }

    public BoardView()
    {
        ClipToBounds = true;
    }

    public event EventHandler? StatsUpdated;

    public double LastFrameMs { get; private set; }

    public int LastVerticesDrawn { get; private set; }

    public int LastLayersDrawn { get; private set; }

    public float Scale => _scale;

    /// <summary>Millimetres per pixel — the number that tells you what you are actually looking at.</summary>
    public double MillimetresPerPixel => _scale <= 0 ? 0 : 1.0 / _scale;

    public void FitToContent()
    {
        if (FitCore())
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Recomputes the view transform. Separate from <see cref="FitToContent"/> because Render may
    /// need to fit on the first frame, and invalidating from inside a render pass throws.
    /// </summary>
    private bool FitCore()
    {
        var scene = Scene;
        if (scene is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return false;
        }

        var view = BoardSceneBuilder.FitTo(
            scene.Bounds,
            new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));

        _scale = view.Scale;
        _offsetX = view.OffsetX;
        _offsetY = view.OffsetY;
        _hasFitted = true;
        _fittedFor = scene;
        return true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var factor = MathF.Pow(1.15f, (float)e.Delta.Y);
        var newScale = Math.Clamp(_scale * factor, 0.05f, 8000f);

        // Zoom about the cursor: the world point under the pointer must not move.
        _offsetX = (float)p.X - (((float)p.X - _offsetX) * (newScale / _scale));
        _offsetY = (float)p.Y - (((float)p.Y - _offsetY) * (newScale / _scale));
        _scale = newScale;

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed)
        {
            return;
        }

        _dragOrigin = e.GetPosition(this);
        _dragOffsetX = _offsetX;
        _dragOffsetY = _offsetY;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragOrigin is not { } origin)
        {
            return;
        }

        var p = e.GetPosition(this);
        _offsetX = _dragOffsetX + (float)(p.X - origin.X);
        _offsetY = _dragOffsetY + (float)(p.Y - origin.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _dragOrigin = null;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext context)
    {
        var scene = Scene;
        if (scene is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        // A new board gets a fresh fit; a toggled layer does not, because re-fitting under someone
        // who has zoomed in to look at a pad is worse than useless.
        if (!_hasFitted || !ReferenceEquals(_fittedFor, scene))
        {
            FitCore();
        }

        context.Custom(new BoardDrawOperation(
            new Rect(0, 0, Bounds.Width, Bounds.Height),
            scene,
            new ViewTransform(_scale, _offsetX, _offsetY),
            ResolveColor("ViewportBackgroundColor", BoardPalette.Background),
            ResolveColor("ViewportGridColor", BoardPalette.Grid),
            OnFrameRendered));
    }

    private void OnFrameRendered(double elapsedMs, BoardRenderer.DrawResult result)
    {
        LastFrameMs = elapsedMs;
        LastLayersDrawn = result.LayersDrawn;
        LastVerticesDrawn = result.VerticesDrawn;

        // Render runs off the UI thread; marshal the notification.
        Dispatcher.UIThread.Post(
            () => StatsUpdated?.Invoke(this, EventArgs.Empty),
            DispatcherPriority.Background);
    }

    private SKColor ResolveColor(string token, SKColor fallback)
    {
        if (this.TryFindResource(token, ActualThemeVariant, out var value) && value is Color c)
        {
            return new SKColor(c.R, c.G, c.B, c.A);
        }

        return fallback;
    }
}
