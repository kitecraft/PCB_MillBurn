using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Viewer;

namespace MillBurn.Pipeline;

/// <summary>
/// A parsed program to drawable layers.
///
/// Consecutive moves of the same role are joined into one run, so a contour of two thousand short
/// segments becomes one polyline rather than two thousand. That is the difference between a scene
/// the viewport draws in a fraction of a millisecond and one it does not.
///
/// Arcs are flattened here and only here. They survived from the Gerber, through the toolpath, into
/// the file as <c>G2</c>/<c>G3</c> — this is a picture of that file, and a picture is the one place
/// where turning a curve into short lines costs nothing.
/// </summary>
public static class BackplotBuilder
{
    /// <summary>
    /// Groups classified moves into drawable runs.
    ///
    /// <paramref name="offset"/> is added to every point, to put a job that was referenced to the
    /// board's corner back into the board's own coordinates — pass the negation of
    /// <see cref="Job.OriginShift"/>. Forgetting it draws a perfectly correct backplot 150 mm off
    /// screen, which looks exactly like one that was never generated.
    /// </summary>
    public static IReadOnlyList<BackplotLayer> Build(
        IReadOnlyList<BackplotMove> moves, Point2 offset = default, long sagittaNm = 0)
    {
        ArgumentNullException.ThrowIfNull(moves);

        var runs = new Dictionary<BackplotRole, List<IReadOnlyList<Point2>>>();
        var current = new List<Point2>();
        var currentRole = (BackplotRole?)null;

        void Flush()
        {
            if (currentRole is { } role && current.Count >= 2)
            {
                if (!runs.TryGetValue(role, out var list))
                {
                    list = [];
                    runs[role] = list;
                }

                list.Add(current);
            }

            current = [];
        }

        foreach (var (role, move) in moves)
        {
            // A vertical move has no extent in plan, so it would draw as a dot. Plunges are counted
            // and reported instead of drawn, which is what the operator can actually use.
            if (!move.MovesInPlane)
            {
                continue;
            }

            // Compare in the same frame the points are stored in. Comparing an offset point against
            // a raw one never matches, so every move starts its own run — 2,978 of them instead of
            // 15, which still draws correctly and makes the count meaningless.
            var from = move.From + offset;

            if (currentRole != role || current.Count == 0 || current[^1] != from)
            {
                Flush();
                currentRole = role;
                current.Add(from);
            }

            if (move.IsArc)
            {
                AppendArc(current, move, offset, sagittaNm);
            }
            else
            {
                current.Add(move.To + offset);
            }
        }

        Flush();

        var layers = new List<BackplotLayer>();

        void Add(BackplotRole role, string id, string label, BoardLayerStyle style, bool visible)
        {
            if (runs.TryGetValue(role, out var list) && list.Count > 0)
            {
                layers.Add(new BackplotLayer(id, label, style, list, visible));
            }
        }

        Add(BackplotRole.Travel, "gcode-travel", "Travel moves", BackplotPalette.Travel, false);
        Add(BackplotRole.LongTravel, "gcode-long-travel", "Long rapids", BackplotPalette.LongTravel, true);
        Add(BackplotRole.Cut, "gcode-cut", "Cutting moves", BackplotPalette.Cut, true);
        Add(BackplotRole.Gouge, "gcode-gouge", "RAPID AT DEPTH", BackplotPalette.Gouge, true);

        return layers;
    }

    private static void AppendArc(List<Point2> into, GcodeMove move, Point2 offset, long sagittaNm)
    {
        var segment = new ArtSegment(
            move.Kind == MoveKind.ArcClockwise ? ArtSweep.Clockwise : ArtSweep.CounterClockwise,
            move.From,
            move.To,
            move.Centre);

        var radius = segment.RadiusNm;
        var swept = segment.SweptAngle();
        var tolerance = sagittaNm > 0 ? sagittaNm : Geometry.Tessellate.DefaultSagittaNm;
        var steps = Geometry.Tessellate.SegmentsForArc(radius, swept, tolerance);

        var start = Math.Atan2(move.From.Y - move.Centre.Y, move.From.X - move.Centre.X);
        var direction = segment.Sweep == ArtSweep.CounterClockwise ? 1.0 : -1.0;

        for (var i = 1; i < steps; i++)
        {
            var angle = start + (direction * swept * i / steps);
            into.Add(new Point2(
                move.Centre.X + (long)Math.Round(radius * Math.Cos(angle), MidpointRounding.AwayFromZero),
                move.Centre.Y + (long)Math.Round(radius * Math.Sin(angle), MidpointRounding.AwayFromZero)) + offset);
        }

        into.Add(move.To + offset);
    }
}
