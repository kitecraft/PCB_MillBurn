using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MillBurn.Core;
using MillBurn.Optimize;

namespace MillBurn.App.Views;

/// <summary>
/// The travel optimizer, run live on a scatter of pads, drawing what it saved.
///
/// It is in the About window because it is the one thing in here a person can watch happen: the
/// grey path is the order a naive tool would drill in — nearest unvisited hole, every time — and
/// the drawn one is what <see cref="RouteOptimizer"/> makes of the same holes. The numbers under
/// it are the optimizer's own, not a recomputation, so the claim on screen is the claim the
/// exporter makes about a real board.
///
/// Every pad is fed through the same <see cref="RouteNode"/> model an export uses. Nothing here is
/// a simulation of the optimizer; it is the optimizer.
/// </summary>
public sealed class TravelDemo : Control
{
    private const int PadCount = 44;
    private const double FieldWidthMm = 120;
    private const double FieldHeightMm = 70;

    /// <summary>Long enough to follow the route, short enough not to become a wait.</summary>
    private static readonly TimeSpan Draws = TimeSpan.FromSeconds(2.2);

    private readonly DispatcherTimer _timer;
    private readonly List<Point2> _pads = [];
    private readonly List<Point2> _greedy = [];
    private readonly List<Point2> _optimised = [];

    private DateTime _started = DateTime.UtcNow;
    private double _progress;
    private int _seed = 4;

    public TravelDemo()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => Step();

        Shuffle(_seed);
    }

    /// <summary>What the optimizer made of the pads on screen, for the caption beside it.</summary>
    public RoutePlan? Plan { get; private set; }

    /// <summary>Raised when a new scatter has been solved, so the caption can be rewritten.</summary>
    public event EventHandler? Solved;

    /// <summary>A fresh scatter, solved and drawn from the start.</summary>
    public void Shuffle(int? seed = null)
    {
        _seed = seed ?? _seed + 1;

        var random = new Random(_seed);

        _pads.Clear();

        // A board rather than a cloud: two rows of header pins along the top, and the rest in
        // loose clusters, because that is the shape whose ordering the optimizer actually wins on.
        for (var i = 0; i < 12; i++)
        {
            _pads.Add(Mm(12 + (i * 8), 60));
            _pads.Add(Mm(12 + (i * 8), 54));
        }

        for (var cluster = 0; cluster < 4; cluster++)
        {
            var cx = 20 + (random.NextDouble() * (FieldWidthMm - 40));
            var cy = 10 + (random.NextDouble() * 28);

            for (var i = 0; i < (PadCount - 24) / 4; i++)
            {
                _pads.Add(Mm(
                    Math.Clamp(cx + ((random.NextDouble() - 0.5) * 26), 6, FieldWidthMm - 6),
                    Math.Clamp(cy + ((random.NextDouble() - 0.5) * 22), 6, FieldHeightMm - 6)));
            }
        }

        Solve();

        _progress = 0;
        _started = DateTime.UtcNow;
        _timer.Start();

        Solved?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private static Point2 Mm(double x, double y) => new(Nm.FromMillimetres(x), Nm.FromMillimetres(y));

    private void Solve()
    {
        var nodes = _pads.Select((pad, i) => RouteNode.ForPoint(i, pad)).ToList();
        var plan = RouteOptimizer.Solve(nodes, Point2.Origin, returnTo: Point2.Origin);

        Plan = plan;

        _optimised.Clear();
        _optimised.AddRange(plan.Steps.Select(s => s.Entry));

        // The "before" route, drawn faint: nearest unvisited pad, every time, from the same corner.
        // Its length is the optimizer's own InitialTravelMm — this only reproduces the order so it
        // can be drawn.
        _greedy.Clear();

        var remaining = new List<Point2>(_pads);
        var at = Point2.Origin;

        while (remaining.Count > 0)
        {
            var best = 0;

            for (var i = 1; i < remaining.Count; i++)
            {
                if (at.DistanceTo(remaining[i]) < at.DistanceTo(remaining[best]))
                {
                    best = i;
                }
            }

            at = remaining[best];
            _greedy.Add(at);
            remaining.RemoveAt(best);
        }
    }

    private void Step()
    {
        _progress = Math.Min(1, (DateTime.UtcNow - _started) / Draws);

        if (_progress >= 1)
        {
            _timer.Stop();
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ground = Brush("ViewportBackground", Colors.Black);

        // Two routes over the same holes have to be told apart at a glance: the one the optimizer
        // replaced is dashed and dim, the one it chose is the accent the rest of the app uses for
        // "this is the answer".
        var chosen = Brush("InteractivePrimary", Colors.CornflowerBlue);
        var replaced = Brush("TextDisabled", Colors.DimGray);
        var copper = Brush("PathDrill", Colors.Orange);

        context.FillRectangle(ground, new Rect(Bounds.Size));

        if (Bounds.Width < 8 || Bounds.Height < 8 || _pads.Count == 0)
        {
            return;
        }

        var scale = Math.Min(Bounds.Width / (FieldWidthMm + 8), Bounds.Height / (FieldHeightMm + 8));
        var left = (Bounds.Width - (FieldWidthMm * scale)) / 2;
        var top = (Bounds.Height - (FieldHeightMm * scale)) / 2;

        Point At(Point2 p) => new(
            left + (Nm.ToMillimetres(p.X) * scale),
            // Y up on the board, down on the screen.
            top + ((FieldHeightMm - Nm.ToMillimetres(p.Y)) * scale));

        Path(context, new Pen(replaced, 1, DashStyle.Dash), _greedy, 1, At);
        Path(context, new Pen(chosen, 1.8), _optimised, _progress, At);

        foreach (var pad in _pads)
        {
            context.DrawEllipse(copper, null, At(pad), 2.8, 2.8);
        }
    }

    /// <summary>Draws the first <paramref name="fraction"/> of a route, starting from the corner.</summary>
    private static void Path(DrawingContext context, Pen pen, List<Point2> route, double fraction, Func<Point2, Point> at)
    {
        if (route.Count == 0 || fraction <= 0)
        {
            return;
        }

        var drawn = (int)Math.Ceiling(route.Count * fraction);
        var from = at(Point2.Origin);

        for (var i = 0; i < drawn && i < route.Count; i++)
        {
            var to = at(route[i]);
            context.DrawLine(pen, from, to);
            from = to;
        }

        // The hop home is part of the job, and the optimizer counted it.
        if (drawn >= route.Count)
        {
            context.DrawLine(pen, from, at(Point2.Origin));
        }
    }

    private IBrush Brush(string token, Color fallback) =>
        this.TryFindResource(token, ActualThemeVariant, out var value) && value is IBrush found
            ? found
            : new SolidColorBrush(fallback);
}
