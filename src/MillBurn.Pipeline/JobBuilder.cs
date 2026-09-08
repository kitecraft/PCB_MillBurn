using System.Globalization;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Geometry;
using MillBurn.Optimize;

namespace MillBurn.Pipeline;

/// <summary>Everything needed to turn a board into a mill job.</summary>
public sealed record MillOptions
{
    public IsolationOptions Isolation { get; init; } = new();

    public DrillOptions Drill { get; init; } = new();

    public OutlineOptions Outline { get; init; } = new();

    /// <summary>Which side to cut. The bottom side has to be mirrored, which is Phase 5's job.</summary>
    public BoardSide Side { get; init; } = BoardSide.Top;

    /// <summary>
    /// Move the board so its lower-left corner is the origin.
    ///
    /// On by default, and it matters more than it sounds. Gerber coordinates come from wherever the
    /// board happened to sit on the EDA canvas — this board lands at X150 Y-90 — so emitting them
    /// raw means the operator has to set work zero at a point 150 mm off the corner of their stock,
    /// which they cannot see and cannot measure. Referencing the job to the board's own corner makes
    /// work zero a place you can touch off on.
    /// </summary>
    public bool OriginAtBoardCorner { get; init; } = true;

    public bool IncludeIsolation { get; init; } = true;

    public bool IncludeDrill { get; init; } = true;

    public bool IncludeOutline { get; init; } = true;
}

/// <summary>
/// Board plus settings to an ordered job.
///
/// The order is fixed and is not a preference: **isolate, then drill, then cut out.** Isolation
/// first because the board must still be flat and fully supported for a cut measured in hundredths
/// of a millimetre. Drilling before the outline because a drill pushes down, and a board that is
/// already free of its stock lifts. The outline last because after it, there is nothing holding the
/// work.
/// </summary>
public static class JobBuilder
{
    public static Job Build(Board board, MillOptions options)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(options);

        var toolpaths = new List<Toolpath>();
        var notes = new List<string>();

        // Ordering chains from where the previous operation finished. Restarting each one at the
        // origin would both order them badly and report a travel figure dominated by one long move
        // in from wherever the machine happened to be.
        var at = Point2.Origin;

        Toolpath Sequence(Toolpath toolpath)
        {
            var ordered = toolpath with
            {
                Passes = NearestNeighbour.Order(toolpath.Passes, at),
                Drills = NearestNeighbour.Order(toolpath.Drills, at),
            };

            at = ordered.Drills.Count > 0
                ? ordered.Drills[^1].At
                : ordered.Passes.Count > 0 ? ordered.Passes[^1].End : at;

            return ordered;
        }

        var copperRole = options.Side == BoardSide.Bottom ? LayerRole.BottomCopper : LayerRole.TopCopper;
        var copper = board.Layers.FirstOrDefault(l => l.Role == copperRole);

        if (options.IncludeIsolation)
        {
            if (copper is null)
            {
                notes.Add($"No {LayerRoleInfo.Label(copperRole).ToLowerInvariant()} layer; nothing to isolate.");
            }
            else
            {
                var isolation = IsolationOperation.Build(
                    copper.Area, options.Isolation, $"Isolation — {copper.Label}");

                var unreachable = IsolationOperation.UnreachableGaps(copper.Area, options.Isolation);
                if (unreachable > 0)
                {
                    // Not a warning to bury in a log. A gap the tool cannot enter leaves the two
                    // sides connected, and the toolpath shows nothing at all there — the picture
                    // looks fine and the board is shorted.
                    var width = Nm.ToMillimetreString(options.Isolation.EffectiveWidthNm, 3);
                    notes.Add(Invariant(
                        $"{unreachable} gap(s) are narrower than the {width} mm cut: those copper regions stay connected."));
                }

                toolpaths.Add(Sequence(isolation));
            }
        }

        if (options.IncludeDrill)
        {
            foreach (var layer in board.Layers.Where(l => LayerRoleInfo.IsDrill(l.Role) && l.Drill is not null))
            {
                foreach (var path in DrillOperation.Build(layer.Drill!, options.Drill))
                {
                    toolpaths.Add(Sequence(path));
                }
            }
        }

        if (options.IncludeOutline)
        {
            var outline = board.Layers.FirstOrDefault(l => l.Role == LayerRole.Outline);
            if (outline is null)
            {
                notes.Add("No board outline; nothing to cut out.");
            }
            else
            {
                // The profile is a *stroked* line, so its realised area is a ring. The board is what
                // the ring encloses, so the outer boundary is what the cutter must go around.
                var boundary = Polygons.From(LargestRing(outline.Area));

                toolpaths.Add(Sequence(OutlineOperation.Build(boundary, options.Outline)));
            }
        }

        var originShift = Point2.Origin;

        if (options.OriginAtBoardCorner && !board.Bounds.IsEmpty)
        {
            var shift = new Point2(-board.Bounds.MinX, -board.Bounds.MinY);
            toolpaths = [.. toolpaths.Select(t => Translate(t, shift))];
            originShift = shift;

            var wasX = Nm.ToMillimetreString(board.Bounds.MinX, 3);
            var wasY = Nm.ToMillimetreString(board.Bounds.MinY, 3);
            notes.Add(Invariant(
                $"Referenced to the board's lower-left corner; the Gerber origin was at {wasX}, {wasY} mm."));
        }

        return new Job
        {
            Name = board.Source,
            Toolpaths = toolpaths,
            Notes = notes,
            OriginShift = originShift,
        };
    }

    private static Toolpath Translate(Toolpath toolpath, Point2 by) => toolpath with
    {
        Passes = [.. toolpath.Passes.Select(p => p with { Path = [.. p.Path.Select(s => Translate(s, by))] })],
        Drills = [.. toolpath.Drills.Select(d => d with { At = d.At + by })],
    };

    private static ArtSegment Translate(ArtSegment s, Point2 by) =>
        new(s.Sweep, s.From + by, s.To + by, s.Centre + by);

    /// <summary>
    /// The outer boundary of a stroked profile.
    ///
    /// Edge_Cuts is a line dragged around the board, so realising it gives a ring: an outer contour
    /// and an inner one. The board is what the ring encloses, so the cutter has to follow the
    /// outer. Taking the inner would cut a board a pen-width small on every edge, which is small
    /// enough that nobody notices until the enclosure does not fit.
    /// </summary>
    private static Clipper2Lib.Path64 LargestRing(Clipper2Lib.Paths64 area)
    {
        Clipper2Lib.Path64? largest = null;
        var largestArea = 0.0;

        foreach (var ring in area)
        {
            var size = Math.Abs(Clipper2Lib.Clipper.Area(ring));
            if (size > largestArea)
            {
                largestArea = size;
                largest = ring;
            }
        }

        return largest ?? [];
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
