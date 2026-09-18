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
    /// Where a program's coordinates land on the board.
    ///
    /// A program is written in work coordinates, so putting it back on the board is a translation —
    /// except for a mirrored one. A bottom-side job is flipped left-to-right before it is written,
    /// because the stock gets turned over before it is cut; its coordinates describe the flipped
    /// board. Translating those straight onto the unflipped board draws the cuts as a mirror image
    /// of the copper they belong to, which looks exactly like a bug in a file that is correct.
    ///
    /// Undoing the flip here puts the path where it will actually land on the board — which is the
    /// question a backplot over the artwork is being asked. It is still the emitted file being
    /// drawn; only the frame it is drawn in has been chosen to match the picture.
    /// </summary>
    /// <param name="Offset">Added to every point, to undo the shift to the board's corner.</param>
    /// <param name="MirrorSumXNm">
    /// <c>MinX + MaxX</c> of the board for a mirrored program, or zero for one that is not.
    /// </param>
    public readonly record struct Placement(Point2 Offset, long MirrorSumXNm = 0)
    {
        public Point2 Apply(Point2 point) => MirrorSumXNm == 0
            ? point + Offset
            : new Point2(MirrorSumXNm - Offset.X - point.X, point.Y + Offset.Y);
    }

    /// <summary>
    /// Groups classified moves into drawable runs.
    ///
    /// <paramref name="offset"/> is added to every point, to put a job that was referenced to the
    /// board's corner back into the board's own coordinates — pass the negation of
    /// <see cref="Job.OriginShift"/>. Forgetting it draws a perfectly correct backplot 150 mm off
    /// screen, which looks exactly like one that was never generated.
    /// </summary>
    public static IReadOnlyList<BackplotLayer> Build(
        IReadOnlyList<BackplotMove> moves, Point2 offset = default, long sagittaNm = 0,
        bool splitByDepth = true) =>
        Build(moves, new Placement(offset), sagittaNm, splitByDepth);

    /// <inheritdoc cref="Build(IReadOnlyList{BackplotMove}, Point2, long, bool)"/>
    /// <param name="moves">The classified moves, in program order.</param>
    /// <param name="placement">Where they land on the board.</param>
    /// <param name="sagittaNm">How finely arcs are flattened; zero for the default.</param>
    /// <param name="splitByDepth">
    /// Whether to draw the passes that reach this program's full depth apart from the shallower
    /// ones. True for a program that does one job, which is what an export writes and what makes the
    /// split mean something. False for a merged program — the single file the Mill button writes,
    /// say — where isolation at 0.05 mm and an outline at 0.9 mm sit side by side and "does not
    /// reach full depth" is true of the isolation without being useful.
    /// </param>
    public static IReadOnlyList<BackplotLayer> Build(
        IReadOnlyList<BackplotMove> moves, Placement placement, long sagittaNm = 0,
        bool splitByDepth = true)
    {
        ArgumentNullException.ThrowIfNull(moves);

        var runs = new Dictionary<BackplotRole, List<IReadOnlyList<Point2>>>();
        var current = new List<Point2>();
        var currentRole = (BackplotRole?)null;

        // How deep this run cuts, so the ones that go all the way through can be told from the ones
        // that do not. See Deepest below for why the picture needs that.
        var currentDepth = 0L;
        var cuts = new List<(long DepthNm, IReadOnlyList<Point2> Path)>();

        void Flush()
        {
            if (currentRole is { } role && current.Count >= 2)
            {
                if (role == BackplotRole.Cut)
                {
                    cuts.Add((currentDepth, current));
                }
                else
                {
                    if (!runs.TryGetValue(role, out var list))
                    {
                        list = [];
                        runs[role] = list;
                    }

                    list.Add(current);
                }
            }

            current = [];
            currentDepth = 0;
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
            var from = placement.Apply(move.From);

            if (currentRole != role || current.Count == 0 || current[^1] != from)
            {
                Flush();
                currentRole = role;
                current.Add(from);
            }

            currentDepth = Math.Min(currentDepth, Math.Min(move.FromZNm, move.ToZNm));

            if (move.IsArc)
            {
                AppendArc(current, move, placement, sagittaNm);
            }
            else
            {
                current.Add(placement.Apply(move.To));
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

        // The cuts, split by how deep they go.
        //
        // Every depth pass of one profile follows the same line, so drawing them all in one colour
        // draws the same line several times and the picture says only "the cutter went here". What
        // it needs to say is "the cutter went all the way through here" — because where it did not
        // is a tab, and a tab is the difference between a board that comes out and a board that has
        // to be sawn out. Overlaid, the shallow pass that crosses the tab paints over the gap the
        // deep ones leave, and the tabs disappear.
        //
        // So the deepest passes are drawn in the cut colour and the shallower ones dimmed underneath.
        // Nothing is hidden — this also draws a file from somebody else's CAM, where a shallow pass
        // may be the only pass over some of the work — but a tab now reads as a gap in the bright
        // line, with the dim line still crossing it.
        var deepest = cuts.Count > 0 ? cuts.Min(c => c.DepthNm) : 0;

        bool Through(long depthNm) => !splitByDepth || depthNm <= deepest + SameDepthNm;

        var through = cuts.Where(c => Through(c.DepthNm)).Select(c => c.Path).ToList();
        var partial = cuts.Where(c => !Through(c.DepthNm)).Select(c => c.Path).ToList();

        if (partial.Count > 0)
        {
            // Off until asked for. A ramped program — a helix into every hole, a perimeter that
            // spirals down — is nearly all part-depth runs, and drawing them all lays a yellow wash
            // over the whole board. The bright line answers the question the picture is for: where
            // does the cutter go all the way through, and so where does the board stay attached.
            layers.Add(new BackplotLayer(
                "gcode-cut-partial", "Part-depth passes", BackplotPalette.PartialCut, partial, false));
        }

        if (through.Count > 0)
        {
            layers.Add(new BackplotLayer("gcode-cut", "Cutting moves", BackplotPalette.Cut, through, true));
        }

        Add(BackplotRole.Gouge, "gcode-gouge", "RAPID AT DEPTH", BackplotPalette.Gouge, true);

        return layers;
    }

    /// <summary>The role layers' ids, in the order <see cref="Build(IReadOnlyList{BackplotMove}, Placement, long, bool)"/> adds them.</summary>
    private static readonly string[] RoleOrder =
        ["gcode-travel", "gcode-long-travel", "gcode-cut-partial", "gcode-cut", "gcode-gouge"];

    /// <summary>
    /// How close two depths have to be to count as the same pass.
    ///
    /// A micron: the programs are written to three decimal places of a millimetre, so passes that
    /// are meant to be at one depth are at one depth, and anything further apart than this was meant
    /// to be.
    /// </summary>
    private const long SameDepthNm = 1_000;

    /// <summary>One program's moves, and which layer they came from.</summary>
    /// <param name="Source">The layer's file name — the id the panel knows it by.</param>
    /// <param name="Label">The layer's own name, for the scene.</param>
    /// <param name="Moves">Its classified moves.</param>
    /// <param name="Mirrored">
    /// Whether this program was flipped left-to-right on the way out, so the drawing can flip it
    /// back and put it where it lands on the board.
    /// </param>
    public readonly record struct Program(
        string Source, string Label, IReadOnlyList<BackplotMove> Moves, bool Mirrored = false);

    /// <summary>
    /// Builds a backplot that can be filtered by which program a move came from.
    ///
    /// Merging every program into one set of role layers means the viewer can only ever show all
    /// the cuts at once — and looking at one layer's toolpath is the reason anybody opens a
    /// backplot. Each program gets its own layers, with ids that carry the source and colour keys
    /// that carry the role, so the two axes stay independent: *which layer* and *what kind of move*.
    /// </summary>
    /// <param name="programs">The classified programs. A source layer may have more than one.</param>
    /// <param name="offset">Added to every point, to undo the shift to the board's corner.</param>
    /// <param name="sagittaNm">Flattening tolerance for arcs, or zero for the default.</param>
    /// <param name="mirrorSumXNm">
    /// <c>MinX + MaxX</c> of the board, used to unflip the programs that were mirrored. Zero draws
    /// every program straight, which is right only when there is no board to place them on.
    /// </param>
    /// <remarks>
    /// **One set of layers per source layer, not per program.** A drilling layer writes two
    /// programs — the drilling and the routing — and both used to be given the same ids, because the
    /// id carries the source. The scene finds a layer by id and takes the first, so the routing
    /// program's layers were reached by no control at all: its long rapids stayed on with the Long
    /// rapids chip off, and stayed on with the layer's own row off. Merging a source's programs
    /// keeps every id unique by construction. The runs stay separate, so nothing is drawn joining
    /// the end of one program to the start of the next.
    /// </remarks>
    public static IReadOnlyList<BackplotLayer> BuildPerProgram(
        IReadOnlyList<Program> programs,
        Point2 offset = default,
        long sagittaNm = 0,
        long mirrorSumXNm = 0)
    {
        ArgumentNullException.ThrowIfNull(programs);

        var layers = new List<BackplotLayer>();

        foreach (var source in programs.GroupBy(p => p.Source, StringComparer.Ordinal))
        {
            var byRole = new Dictionary<string, BackplotLayer>(StringComparer.Ordinal);

            foreach (var program in source)
            {
                var placement = new Placement(offset, program.Mirrored ? mirrorSumXNm : 0);

                foreach (var layer in Build(program.Moves, placement, sagittaNm))
                {
                    byRole[layer.Id] = byRole.TryGetValue(layer.Id, out var so)
                        ? so with { Runs = [.. so.Runs, .. layer.Runs] }
                        : layer;
                }
            }

            var label = source.First().Label;

            // In the order Build draws them, so a kind first seen in a later program still paints
            // under the cuts rather than over them.
            foreach (var id in RoleOrder.Where(byRole.ContainsKey))
            {
                var layer = byRole[id];

                layers.Add(layer with
                {
                    Id = id + ":" + source.Key,
                    Label = label + " · " + layer.Label,
                    ColourKey = id,
                    Source = source.Key,
                });
            }
        }

        return layers;
    }

    private static void AppendArc(List<Point2> into, GcodeMove move, Placement placement, long sagittaNm)
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
            into.Add(placement.Apply(new Point2(
                move.Centre.X + (long)Math.Round(radius * Math.Cos(angle), MidpointRounding.AwayFromZero),
                move.Centre.Y + (long)Math.Round(radius * Math.Sin(angle), MidpointRounding.AwayFromZero))));
        }

        into.Add(placement.Apply(move.To));
    }
}
