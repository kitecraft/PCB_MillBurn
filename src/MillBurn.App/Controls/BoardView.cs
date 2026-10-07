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

    /// <summary>
    /// Whether the scale strips are drawn along the top and the left.
    ///
    /// A preference rather than a mode: it is remembered beside the theme, and the board underneath
    /// is unaffected either way.
    /// </summary>
    public static readonly StyledProperty<bool> ShowRulerProperty =
        AvaloniaProperty.Register<BoardView, bool>(nameof(ShowRuler), defaultValue: true);

    public bool ShowRuler
    {
        get => GetValue(ShowRulerProperty);
        set => SetValue(ShowRulerProperty, value);
    }

    /// <summary>
    /// Where the pointer is, in the board's own millimetres, or null when it is not over the canvas.
    ///
    /// **The reason 6.48 was asked for the day it would have saved an afternoon**: a misplaced hole
    /// was reported with no position, because there was no way to read one off the screen, and
    /// finding it again took a screenshot measured against the outline in a script.
    /// </summary>
    public static readonly StyledProperty<Point?> PointerAtProperty =
        AvaloniaProperty.Register<BoardView, Point?>(nameof(PointerAt));

    public Point? PointerAt
    {
        get => GetValue(PointerAtProperty);
        set => SetValue(PointerAtProperty, value);
    }

    /// <summary>
    /// What the readout calls zero: the stock's corner on a job built on stock, the board's
    /// otherwise.
    ///
    /// Supplied rather than worked out, because this control knows the scene's rectangle and not
    /// what made it.
    /// </summary>
    public static readonly StyledProperty<string> OriginNameProperty =
        AvaloniaProperty.Register<BoardView, string>(
            nameof(OriginName), defaultValue: "from the board's corner");

    public string OriginName
    {
        get => GetValue(OriginNameProperty);
        set => SetValue(OriginNameProperty, value);
    }

    /// <summary>Where a measuring drag started, in board millimetres, or null when none is running.</summary>
    private Point? _measureFrom;

    /// <summary>
    /// Puts a measuring drag on screen without one being dragged, for a screenshot.
    ///
    /// The drag is the one thing in this control that cannot be reached headlessly: it needs a
    /// button held and a pointer moved. This is the same two points the drag would have set.
    /// </summary>
    internal void MeasureFrom(Point from, Point to)
    {
        _measureFrom = from;
        PointerAt = to;
        InvalidateVisual();
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
        // **`PointerAt` is deliberately not in here.** Every pointer move sets it, and
        // `BoardDrawOperation.Equals` always returns false, so listing it would re-execute the whole
        // draw — every layer path and the grid — once per mouse sample, including with the rulers
        // switched off, where nothing on screen could change. The two handlers invalidate for
        // themselves when there is actually something to redraw.
        //
        // `OriginName` is in, because the line under the readout is drawn from it: on a job built on
        // stock it changes without the scene's own reference changing.
        AffectsRender<BoardView>(SceneProperty, ShowRulerProperty, OriginNameProperty);
        FocusableProperty.OverrideDefaultValue<BoardView>(true);
    }

    public BoardView()
    {
        ClipToBounds = true;

        // A crosshair over the canvas and an arrow everywhere else, which is the other half of what
        // the workshop asked for: *"if possible turn the mouse into a crosshair"*. On a control
        // whose whole job is to be pointed at, an arrow's tip is a few pixels from where it points.
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    /// <summary>
    /// Where a screen point lands in the board's own millimetres, or null when there is no scene to
    /// measure against.
    ///
    /// The same frame the ruler draws: work zero at the lower-left corner of the frame every emitted
    /// file is referenced to, y counting up.
    /// </summary>
    public Point? BoardPointAt(Point screen)
    {
        if (Scene is not { } scene || scene.Bounds.IsEmpty || _scale <= 0)
        {
            return null;
        }

        var frame = new RulerFrame(
            _scale,
            (scene.Bounds.Left * _scale) + _offsetX,
            (scene.Bounds.Bottom * _scale) + _offsetY);

        return new Point(frame.BoardX((float)screen.X), frame.BoardY((float)screen.Y));
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
    /// Puts a board point in the middle of the viewport at a given scale.
    ///
    /// Added for the screenshots: the ladder is supposed to change with the zoom — 5 mm majors over
    /// a whole board, 0.5 mm over a single bridge — and there was no way to reach the second one
    /// headlessly, so the claim could not be checked the way everything else here is.
    /// </summary>
    /// <param name="scale">
    /// Pixels per millimetre, clamped to the range the wheel can reach. Unclamped it could be put
    /// into a state no gesture can produce and none can get out of.
    /// </param>
    /// <param name="centre">The board point to put in the middle, in millimetres.</param>
    /// <returns>
    /// False when there was nothing to zoom — no scene, or no layout yet.
    ///
    /// **Said rather than swallowed.** This is called from the screenshot path during `OnOpened`,
    /// before any guaranteed layout pass, so the early return is reachable; and a shot that quietly
    /// came out at the whole-board fit instead would read as *the ladder gives 5 mm majors at bridge
    /// zoom*, which is a false claim in a picture. The caller says so on the line it already prints.
    /// </returns>
    public bool ZoomTo(float scale, Point centre)
    {
        if (Scene is not { } scene || scene.Bounds.IsEmpty || scale <= 0
            || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return false;
        }

        scale = Math.Clamp(scale, 0.05f, 8000f);

        // Solving the ruler's own mapping for the offsets, so the two cannot disagree. Screen is
        // (world * scale) + offset; world x counts with board x from `Bounds.Left`, and world y
        // counts *against* board y from `Bounds.Bottom`, which is the renderer's flip and the one
        // thing here easy to get backwards. Got it backwards once: the first bridge shot asked for
        // y 30 and drew y 293, a board-height's worth out and twice.
        _scale = scale;
        _offsetX = (float)(Bounds.Width / 2) - ((scene.Bounds.Left + (float)centre.X) * scale);
        _offsetY = (float)(Bounds.Height / 2) - ((scene.Bounds.Bottom - (float)centre.Y) * scale);
        _hasFitted = true;
        _fittedFor = scene;
        InvalidateVisual();
        return true;
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

        // **Shift-drag measures instead of panning.** A tape measure, not a drawing tool: it is not
        // saved, nothing else reads it, and letting go ends it. Shift because the plain drag is pan
        // and pan is the thing the hand does without thinking.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && BoardPointAt(e.GetPosition(this)) is { } start)
        {
            _measureFrom = start;
            PointerAt = start;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
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
        var p = e.GetPosition(this);

        // Tracked whether or not anything is being dragged: the readout is continuous, which is what
        // makes it a thing you glance at rather than a thing you operate.
        PointerAt = BoardPointAt(p);

        if (_dragOrigin is not { } origin)
        {
            // Only when the move changes something drawn. A measuring drag always does; a bare
            // hover does when the rulers are on, because the readout is part of the frame; with
            // them off nothing moves and a sweep across a dense board should cost nothing, which is
            // what it cost before this feature existed.
            if (_measureFrom is not null || ShowRuler)
            {
                InvalidateVisual();
                e.Handled = true;
            }

            return;
        }

        _offsetX = _dragOffsetX + (float)(p.X - origin.X);
        _offsetY = _dragOffsetY + (float)(p.Y - origin.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        End();
        e.Pointer.Capture(null);
    }

    /// <summary>
    /// The capture went away without a release, so whatever was being dragged is over.
    ///
    /// **"Letting go ends it" was only true of letting go.** Alt-tab mid-measure, or open a dialog,
    /// and no release arrives — so the dashed line and its dx/dy went on tracking the pointer on
    /// every hover afterwards, forever, until somebody pressed and released again. A measure that
    /// outlives the gesture that started it is a reading of two points nobody chose.
    /// </summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        End();
        base.OnPointerCaptureLost(e);
    }

    private void End()
    {
        _dragOrigin = null;
        _measureFrom = null;
        InvalidateVisual();
    }

    /// <summary>
    /// The pointer has left the canvas, so there is no position to report.
    ///
    /// Said explicitly rather than left at the last value: a readout frozen at wherever the pointer
    /// was when it left the viewport is a number that looks live and is not.
    /// </summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        PointerAt = null;
        base.OnPointerExited(e);
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
            ThemeTokens.Resolve(this, "ViewportBackground", BoardPalette.Background),
            ThemeTokens.Resolve(this, "ViewportGrid", BoardPalette.Grid),
            ShowRuler,
            ShowRuler && PointerAt is { } at
                ? new RulerReadout(new SKPoint((float)at.X, (float)at.Y), OriginName)
                : null,
            _measureFrom is { } first && PointerAt is { } now
                ? new RulerMeasure(
                    new SKPoint((float)first.X, (float)first.Y),
                    new SKPoint((float)now.X, (float)now.Y))
                : null,
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

}
