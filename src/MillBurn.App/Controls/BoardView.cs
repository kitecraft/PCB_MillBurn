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

    /// <summary>
    /// The place a finding took the view to, or null when there is none.
    ///
    /// **Not a selection.** Nothing acts on it and nothing is measured from it; it answers "which
    /// one did I click" and nothing else. It survives panning and zooming, because exploring around
    /// the place is the entire point, and it survives the findings window closing, which is why it
    /// carries its own label.
    /// </summary>
    public static readonly StyledProperty<BoardMarker?> MarkerProperty =
        AvaloniaProperty.Register<BoardView, BoardMarker?>(nameof(Marker));

    public BoardMarker? Marker
    {
        get => GetValue(MarkerProperty);
        set => SetValue(MarkerProperty, value);
    }

    /// <summary>
    /// The tabs and the cut path they slide along, or null when the board has none to offer.
    ///
    /// The control owns the gesture and nothing else: which tab is hot, which is being dragged and
    /// where the pointer has slid it live on screen. What a drop *means* — the projection onto the
    /// planner's own arc length, the settings write, the re-plan — belongs to whoever set this
    /// property and arrives back through the events below.
    /// </summary>
    public static readonly StyledProperty<TabOverlay?> TabsProperty =
        AvaloniaProperty.Register<BoardView, TabOverlay?>(nameof(Tabs));

    public TabOverlay? Tabs
    {
        get => GetValue(TabsProperty);
        set => SetValue(TabsProperty, value);
    }

    /// <summary>A tab was dropped: which one, and where the hand left it, in board millimetres.</summary>
    public event EventHandler<TabGestureEventArgs>? TabMoved;

    /// <summary>An empty stretch of the cut path was clicked: a tab belongs there.</summary>
    public event EventHandler<TabGestureEventArgs>? TabAddRequested;

    /// <summary>A handle was right-clicked: that tab should go.</summary>
    public event EventHandler<TabGestureEventArgs>? TabRemoveRequested;

    /// <summary>The handle under the pointer, the one being dragged, and where it has slid to.</summary>
    private int _hotTab = -1;
    private int _dragTab = -1;
    private Point? _dragAt;

    /// <summary>Where the last pan began on screen, for telling a click from a drag on release.</summary>
    private Point? _pressedScreen;

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
        AffectsRender<BoardView>(
            SceneProperty, ShowRulerProperty, OriginNameProperty, MarkerProperty, TabsProperty);
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
        return CurrentFrame() is { } frame
            ? new Point(frame.BoardX((float)screen.X), frame.BoardY((float)screen.Y))
            : null;
    }

    /// <summary>
    /// The mapping between the board's millimetres and the screen as things stand — the same frame
    /// the ruler, the marker and the tab handles all draw with, so a pick cannot disagree with the
    /// picture it is picking from.
    /// </summary>
    private RulerFrame? CurrentFrame()
    {
        if (Scene is not { } scene || scene.Bounds.IsEmpty || _scale <= 0)
        {
            return null;
        }

        return new RulerFrame(
            _scale,
            (scene.Bounds.Left * _scale) + _offsetX,
            (scene.Bounds.Bottom * _scale) + _offsetY);
    }

    /// <summary>
    /// The tab handle under a screen point, or -1. The nearest within reach, not the first: two
    /// handles can crowd at a corner, and picking by list order would grab the one underneath.
    /// </summary>
    private int HandleAt(Point screen)
    {
        if (Tabs is not { } tabs || CurrentFrame() is not { } frame)
        {
            return -1;
        }

        var best = -1;
        var bestDistance = (double)TabHandles.HitRadius;

        for (var i = 0; i < tabs.Tabs.Count; i++)
        {
            var dx = frame.ScreenX(tabs.Tabs[i].At.X) - screen.X;
            var dy = frame.ScreenY(tabs.Tabs[i].At.Y) - screen.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// The nearest place on the cut path to a screen point: where it is in board millimetres, and
    /// how many pixels away the pointer was.
    ///
    /// Screen pixels and not millimetres, because the slop a finger needs is a fact about the
    /// pointer rather than about the board: 10 px is 10 px at any zoom. The geometry's own
    /// arc-length answer is the view model's business, through the planner's walk; this is only
    /// "near enough to mean the path, and roughly where".
    ///
    /// **A second projection on purpose, which `/code-review` rightly questioned.** The planner's
    /// `ProfilePath` is the one copy of the *arc-length* arithmetic, and every committed position
    /// goes through it — the drop event hands the view model a point, and the view model projects.
    /// This loop answers a different question in a different space — pixels from the picture, for
    /// the live slide and the hit slop — over the overlay's own polyline, which is all this
    /// control is allowed to know: the Viewer's overlay type cannot carry a Cam type, and handing
    /// the control a projection callback would couple it to the planner for a picture-only answer
    /// the commit re-derives anyway.
    /// </summary>
    private (Point At, double DistancePx)? NearestOnProfile(Point screen)
    {
        if (Tabs is not { } tabs || tabs.Profile.Count < 2 || CurrentFrame() is not { } frame)
        {
            return null;
        }

        var bestDistance = double.MaxValue;
        var bestX = 0.0;
        var bestY = 0.0;

        for (var i = 0; i < tabs.Profile.Count; i++)
        {
            var a = tabs.Profile[i];
            var b = tabs.Profile[(i + 1) % tabs.Profile.Count];

            double ax = frame.ScreenX(a.X);
            double ay = frame.ScreenY(a.Y);
            double bx = frame.ScreenX(b.X);
            double by = frame.ScreenY(b.Y);

            double dx = bx - ax;
            double dy = by - ay;
            var lengthSquared = (dx * dx) + (dy * dy);

            var t = lengthSquared <= 0
                ? 0
                : Math.Clamp((((screen.X - ax) * dx) + ((screen.Y - ay) * dy)) / lengthSquared, 0, 1);

            var nx = ax + (t * dx);
            var ny = ay + (t * dy);
            var distance = Math.Sqrt(((screen.X - nx) * (screen.X - nx)) + ((screen.Y - ny) * (screen.Y - ny)));

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestX = frame.BoardX((float)nx);
                bestY = frame.BoardY((float)ny);
            }
        }

        return (new Point(bestX, bestY), bestDistance);
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
    /// Fits again at the next frame, rather than now.
    ///
    /// **Now is too early when the frame moved during start-up.** `FitCore` gives up when the
    /// control has no layout yet, which is exactly the state a `--shot` is in while the flags run —
    /// so a fit asked for there did nothing and said nothing. `Render` already fits lazily when it
    /// meets a scene it has not fitted; forgetting which scene that was puts the request on the
    /// same path, which happens once the control has a size and not before.
    ///
    /// Needed because the scene object does not change when only its extent does: that is the
    /// point of reframing rather than rebuilding, and it is also why the lazy check cannot notice
    /// on its own.
    /// </summary>
    public void RefitWhenDrawn()
    {
        _fittedFor = null;
        InvalidateVisual();
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
    /// Goes to a place on the board and marks it.
    ///
    /// **Zooms as well as centres**, because a bridge is a fraction of a millimetre across: at
    /// whole-board zoom it is smaller than a pixel, and centring alone would leave the operator
    /// looking at the same board with a mark they cannot see.
    /// </summary>
    /// <param name="place">Where, and what to call it.</param>
    /// <returns>False when there was nothing to go to — no scene, or no layout yet.</returns>
    public bool GoTo(BoardMarker place)
    {
        if (!ZoomTo(
            MillBurn.Viewer.Marker.ScaleFor((float)Bounds.Width, (float)Bounds.Height),
            new Point(place.At.X, place.At.Y)))
        {
            return false;
        }

        Marker = place;
        return true;
    }

    /// <summary>
    /// Takes the mark off the board.
    ///
    /// The view stays where it is. Somebody who went to a finding and then cleared the ring is
    /// looking at that copper on purpose, and moving them away again would undo the thing they
    /// asked for.
    /// </summary>
    public void ClearMarker() => Marker = null;

    /// <summary>
    /// A mark means nothing once the rectangle its coordinates are measured from has moved.
    ///
    /// **The three handlers were not enough.** Check, preview and export clear it because the
    /// findings it came from are stale, but the board can be replaced without any of them: Refresh
    /// source re-reads the Gerbers, and opening or dropping a project swaps the board entirely. Go
    /// to a finding, re-plot in KiCad, press Refresh, and the ring stayed where it was with the old
    /// coordinate and the old net names over copper from a different board — the exact failure the
    /// clearing rule was written to prevent, through the one door it did not cover.
    ///
    /// Keyed on the scene's extent rather than on the scene object, because that is the thing the
    /// coordinates are relative to. Panning, zooming and hiding a layer leave it alone, which is
    /// what the rule asks for; re-importing, opening another board, or turning stock on and moving
    /// work zero do not.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SceneProperty
            && Marker is not null
            && (change.OldValue as BoardScene)?.Bounds != (change.NewValue as BoardScene)?.Bounds)
        {
            ClearMarker();
        }

        // A new overlay is a new set of tabs: whatever was hot or in hand named an index into the
        // old list, and an index into the wrong list is the planner's own review fault — a gesture
        // landing on whichever tab now sits in that slot.
        if (change.Property == TabsProperty)
        {
            _hotTab = -1;
            _dragTab = -1;
            _dragAt = null;
        }
    }

    /// <summary>
    /// Escape takes the mark off, when the viewport has focus.
    ///
    /// Handled only when there is one, so Escape keeps whatever other meaning it has everywhere
    /// else — a key that silently swallows itself on a control that had nothing to dismiss is how a
    /// dialog stops closing.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Escape drops a grabbed tab back where it was — before it dismisses anything else,
        // because the thing in hand is the thing being escaped from.
        if (e.Key == Key.Escape && _dragTab >= 0)
        {
            _dragTab = -1;
            _dragAt = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && Marker is not null)
        {
            ClearMarker();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
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
        var position = e.GetPosition(this);

        // Right-click on a handle removes that tab. On a handle only: everywhere else the right
        // button keeps meaning nothing, so a slip of the finger over bare copper cannot unhold a
        // board.
        if (props.IsRightButtonPressed)
        {
            if (HandleAt(position) is var removed && removed >= 0)
            {
                TabRemoveRequested?.Invoke(this, new TabGestureEventArgs(removed, default));
                e.Handled = true;
            }

            return;
        }

        if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed)
        {
            return;
        }

        // **Shift-drag measures instead of panning.** A tape measure, not a drawing tool: it is not
        // saved, nothing else reads it, and letting go ends it. Shift because the plain drag is pan
        // and pan is the thing the hand does without thinking.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && BoardPointAt(position) is { } start)
        {
            _measureFrom = start;
            PointerAt = start;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        // A left press on a handle takes hold of the tab instead of the view. Left only: the
        // middle button stays a pan everywhere, so there is always a way to move the view with a
        // handle under the pointer.
        if (props.IsLeftButtonPressed && HandleAt(position) is var grabbed && grabbed >= 0)
        {
            _dragTab = grabbed;
            _dragAt = Tabs is { } tabs ? new Point(tabs.Tabs[grabbed].At.X, tabs.Tabs[grabbed].At.Y) : null;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        _dragOrigin = position;
        _pressedScreen = position;
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

        // A grabbed tab slides along the cut path, not under the pointer: the pointer can be
        // anywhere, the handle goes to the nearest place on the loop. That is what carries it round
        // a corner, and what makes "off the outline" not possible rather than merely refused.
        if (_dragTab >= 0)
        {
            if (NearestOnProfile(p) is { } nearest)
            {
                _dragAt = nearest.At;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_dragOrigin is not { } origin)
        {
            // The handle under the pointer lights up, and the cursor says "take hold". Only
            // redrawn when the answer changes: hovering is the common case and must stay free.
            var hot = HandleAt(p);

            if (hot != _hotTab)
            {
                _hotTab = hot;
                Cursor = hot >= 0
                    ? new Cursor(StandardCursorType.Hand)
                    : new Cursor(StandardCursorType.Cross);
                InvalidateVisual();
            }

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
        // Dropping a grabbed tab commits it: the view model projects the point onto the planner's
        // own arc length and writes the override. The control's own projected point is only the
        // live picture; the authoritative one comes back through the Tabs property re-planned.
        if (_dragTab >= 0)
        {
            if (_dragAt is { } at)
            {
                TabMoved?.Invoke(this, new TabGestureEventArgs(_dragTab, at));
            }

            _dragTab = -1;
            _dragAt = null;
            End();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        // A press that went nowhere is a click, and a click on an empty stretch of the cut path
        // asks for a tab there. Three pixels of slop, because a hand on a mouse never moves zero;
        // a real pan is hundreds. Left only, said explicitly — `/code-review` found a stationary
        // middle-click adding a tab, because the pan path that set `_pressedScreen` takes either
        // button and this check never asked which. The middle button pans, everywhere, full stop.
        if (_pressedScreen is { } pressed
            && Tabs is not null
            && e.InitialPressMouseButton == MouseButton.Left)
        {
            var p = e.GetPosition(this);
            var moved = Math.Abs(p.X - pressed.X) + Math.Abs(p.Y - pressed.Y);

            if (moved < 3
                && HandleAt(p) < 0
                && NearestOnProfile(p) is { } nearest
                && nearest.DistancePx <= TabHandles.HitRadius)
            {
                TabAddRequested?.Invoke(this, new TabGestureEventArgs(-1, nearest.At));
            }
        }

        _pressedScreen = null;
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
        // A drag the window lost is a drag nobody finished: the tab goes back where it was, because
        // committing a position the hand never let go of would write a place nobody chose.
        _dragTab = -1;
        _dragAt = null;
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

        // The hot handle goes out and the cursor goes back when the pointer leaves — a handle
        // drawn lit under a pointer that is over the settings panel claims a grip nobody is
        // offering. Found by `/code-review`; the hover path only ever ran while moves arrived
        // inside the control.
        if (_hotTab >= 0)
        {
            _hotTab = -1;
            Cursor = new Cursor(StandardCursorType.Cross);
            InvalidateVisual();
        }

        // **Clearing it is not enough on its own.** `PointerAt` was in `AffectsRender` when this
        // was written, so setting it repainted for free; taking it out — to stop a bare hover
        // redrawing the whole board — left this method setting a value nothing would draw. The
        // readout then stayed on screen showing the last position, which is precisely the number
        // that looks live and is not, this method's whole reason for existing.
        //
        // Only when the strips are on, for the same reason they came out of `AffectsRender`: with
        // them off there is no readout to take away.
        if (ShowRuler)
        {
            InvalidateVisual();
        }

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
            Marker,
            DisplayTabs(),
            OnFrameRendered));
    }

    /// <summary>
    /// The overlay as this frame shows it: the bound tabs, with the gesture state painted on — the
    /// hot handle, and a dragged tab drawn at the point it has slid to rather than where the plan
    /// still has it.
    /// </summary>
    private TabOverlay? DisplayTabs()
    {
        if (Tabs is not { } tabs)
        {
            return null;
        }

        if (_dragTab >= 0 && _dragTab < tabs.Tabs.Count && _dragAt is { } at)
        {
            var sliding = tabs.Tabs.ToArray();
            sliding[_dragTab] = sliding[_dragTab] with { At = new SKPoint((float)at.X, (float)at.Y) };

            return tabs with { Tabs = sliding, Hot = _hotTab, Dragging = _dragTab };
        }

        return _hotTab >= 0 || tabs.Hot >= 0 || tabs.Dragging >= 0
            ? tabs with { Hot = _hotTab, Dragging = -1 }
            : tabs;
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

/// <summary>
/// One tab gesture, as the viewport reports it: which handle, and where on the board — in the
/// frame's millimetres, the space <see cref="BoardView.BoardPointAt"/> answers in.
/// </summary>
/// <param name="index">The tab's place in the overlay's list, or -1 for a gesture that names no
/// existing tab (an add).</param>
/// <param name="at">Where, in board millimetres. Default for a removal, which is about a tab and
/// not a place.</param>
public sealed class TabGestureEventArgs(int index, Point at) : EventArgs
{
    public int Index { get; } = index;

    public Point At { get; } = at;
}
