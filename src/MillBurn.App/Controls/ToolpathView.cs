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
/// The toolpath viewport: pan, zoom, level-of-detail, and a live frame-cost overlay.
///
/// Phase 0 acceptance test (Documentation/06-Roadmap-and-Risks.md): 60 fps pan/zoom on a
/// 500k-segment scene. The overlay reports the real number so the claim is checkable rather
/// than asserted.
/// </summary>
public sealed class ToolpathView : Control
{
    public static readonly StyledProperty<ToolpathScene?> SceneProperty =
        AvaloniaProperty.Register<ToolpathView, ToolpathScene?>(nameof(Scene));

    public ToolpathScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    private float _scale = 4f;
    private float _offsetX;
    private float _offsetY;
    private bool _hasFitted;

    private Point? _dragOrigin;
    private float _dragOffsetX;
    private float _dragOffsetY;

    private readonly double[] _frameTimes = new double[60];
    private int _frameIndex;
    private FrameStats _lastStats;

    static ToolpathView()
    {
        AffectsRender<ToolpathView>(SceneProperty);
        FocusableProperty.OverrideDefaultValue<ToolpathView>(true);
    }

    public ToolpathView()
    {
        ClipToBounds = true;
    }

    /// <summary>Rolling average frame cost in milliseconds, or 0 before the first frame.</summary>
    public double AverageFrameMs
    {
        get
        {
            double sum = 0;
            var n = 0;
            foreach (var t in _frameTimes)
            {
                if (t <= 0)
                {
                    continue;
                }

                sum += t;
                n++;
            }

            return n == 0 ? 0 : sum / n;
        }
    }

    public event EventHandler? StatsUpdated;

    public FrameStats LastStats => _lastStats;

    public float Scale => _scale;

    /// <summary>Frames actually rasterised. Used by --fpstest to measure the real render loop.</summary>
    public long FramesRendered { get; private set; }

    /// <summary>Overrides the zoom, keeping the current centre. Used by --fpstest.</summary>
    public void SetScale(float scale)
    {
        var cx = (float)Bounds.Width / 2;
        var cy = (float)Bounds.Height / 2;
        var clamped = Math.Clamp(scale, 0.05f, 8000f);
        _offsetX = cx - ((cx - _offsetX) * (clamped / _scale));
        _offsetY = cy - ((cy - _offsetY) * (clamped / _scale));
        _scale = clamped;
        InvalidateVisual();
    }

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

        var b = scene.Bounds;
        var w = MathF.Max(b.Width, 0.001f);
        var h = MathF.Max(b.Height, 0.001f);

        _scale = 0.92f * MathF.Min((float)Bounds.Width / w, (float)Bounds.Height / h);
        _offsetX = (float)(Bounds.Width / 2) - (((b.Left + b.Right) / 2) * _scale);
        _offsetY = (float)(Bounds.Height / 2) + (((b.Top + b.Bottom) / 2) * _scale);
        _hasFitted = true;
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

        if (!_hasFitted)
        {
            FitCore();
        }

        context.Custom(new ToolpathDrawOperation(
            new Rect(0, 0, Bounds.Width, Bounds.Height),
            scene,
            new ViewTransform(_scale, _offsetX, _offsetY),
            ResolvePalette(),
            ThemeTokens.Resolve(this, "ViewportBackground", new SKColor(0x0F, 0x12, 0x14)),
            ThemeTokens.Resolve(this, "ViewportGrid", new SKColor(0x26, 0x2B, 0x30)),
            OnFrameRendered));
    }

    private void OnFrameRendered(FrameStats stats)
    {
        FramesRendered++;
        _lastStats = stats;
        _frameTimes[_frameIndex] = stats.MillisecondsElapsed;
        _frameIndex = (_frameIndex + 1) % _frameTimes.Length;

        // Render runs off the UI thread; marshal the notification.
        Dispatcher.UIThread.Post(
            () => StatsUpdated?.Invoke(this, EventArgs.Empty),
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Pulls the palette from the theme tokens, so the viewport and the rest of the UI share one
    /// palette and switching light/dark restyles the toolpaths too.
    /// </summary>
    private Dictionary<SegmentStyle, SKColor> ResolvePalette() => new()
    {
        [SegmentStyle.Isolation] = ThemeTokens.Resolve(this, "PathIsolation", SKColors.DodgerBlue),
        [SegmentStyle.Pocket] = ThemeTokens.Resolve(this, "PathPocket", SKColors.Teal),
        [SegmentStyle.Drill] = ThemeTokens.Resolve(this, "PathDrill", SKColors.IndianRed),
        [SegmentStyle.Outline] = ThemeTokens.Resolve(this, "PathOutline", SKColors.MediumPurple),
        [SegmentStyle.Laser] = ThemeTokens.Resolve(this, "PathLaser", SKColors.OrangeRed),
        [SegmentStyle.Fiducial] = ThemeTokens.Resolve(this, "PathFiducial", SKColors.SkyBlue),
        [SegmentStyle.Travel] = ThemeTokens.Resolve(this, "PathTravel", SKColors.Gray),
        [SegmentStyle.RapidLong] = ThemeTokens.Resolve(this, "PathRapidLong", SKColors.Orange),
    };
}
