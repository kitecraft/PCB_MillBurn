using System.Globalization;
using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;
using MillBurn.Gerber;
using MillBurn.Gerber.Apertures;
using MillBurn.Gerber.Model;

namespace MillBurn.Cam;

/// <summary>Settings for turning a parsed Gerber into filled area.</summary>
public sealed record RealisationOptions
{
    /// <summary>Greatest deviation of a flattened curve from the true one. Default 1 µm.</summary>
    public long SagittaNm { get; init; } = Tessellate.DefaultSagittaNm;

    /// <summary>
    /// Put the output in canonical ring order. Costs a sort; makes a golden hash mean "the geometry
    /// changed" rather than "Clipper visited the edges in a different order".
    /// </summary>
    public bool Canonicalise { get; init; }
}

/// <summary>
/// One object's net, and a point on the copper that object contributed.
///
/// A point rather than the object's own geometry, because the question every reader of this has is
/// "which piece of copper is this net in" — and once the copper is unioned, a point inside it is a
/// complete answer to that. One per object rather than one per net, so that a later question about
/// a particular pad or trace still has something to stand on.
///
/// <see cref="At"/> is guaranteed to be strictly inside the realised area, which is not free for a
/// region: its vertices sit on its own boundary, and a boundary point tests neither in nor out. See
/// the realiser for how one is found.
/// </summary>
public readonly record struct NetPoint(string Net, Point2 At);

/// <summary>The filled result of one Gerber layer, plus what had to be said about producing it.</summary>
public sealed record RealisedLayer
{
    public required Paths64 Area { get; init; }

    /// <summary>
    /// Where each net-bearing object left copper, for the checks that need to know which net a
    /// piece of copper belongs to. Empty when the file declares no nets, which is a fact about the
    /// exporter rather than about the board — a Protel export carries none at all.
    /// </summary>
    public IReadOnlyList<NetPoint> Nets { get; init; } = [];

    public required Bounds Bounds { get; init; }

    public required int ObjectCount { get; init; }

    /// <summary>How many times polarity flipped. Each flip forces a separate boolean pass.</summary>
    public required int PolarityRuns { get; init; }

    /// <summary>
    /// The file declared <c>%TF.FilePolarity,Negative%</c>, so <see cref="Area"/> is where the
    /// layer's material is absent rather than present. Not acted on here — see the realiser.
    /// </summary>
    public required bool DeclaredNegative { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    public double AreaMm2 => Polygons.AreaMm2(Area);

    public int RingCount => Area.Count;

    public int VertexCount => Polygons.VertexCount(Area);
}

/// <summary>
/// Turns a parsed <see cref="GerberImage"/> into the filled copper (or mask, or silk) it describes.
///
/// The single rule that matters: **a Gerber is a sequence, not a set.** Objects are painted in
/// file order, and a clear-polarity object erases only what precedes it. Collecting all the darks,
/// collecting all the clears, and subtracting once is the intuitive implementation and it is
/// wrong — a pad flashed *after* a clearance would be erased by it, and the failure is a missing
/// pad on a board that otherwise looks perfect.
///
/// So compositing walks the objects in order. The only concession to speed is that consecutive
/// objects of the same polarity are unioned as a batch before being applied, which turns a
/// thousand boolean operations into one per polarity change. Real layers change polarity rarely or
/// never, so this is usually two passes over the whole file.
/// </summary>
public static class GerberRealiser
{
    public static RealisedLayer Realise(GerberImage image, RealisationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= new RealisationOptions();

        var notes = new List<string>();
        var shapes = new Dictionary<int, Paths64?>();
        var unrealised = new Dictionary<string, int>(StringComparer.Ordinal);

        var result = Polygons.Empty();
        var pending = Polygons.Empty();
        var pendingPolarity = Polarity.Dark;
        var runs = 0;
        var objects = 0;

        void Flush()
        {
            if (pending.Count == 0)
            {
                return;
            }

            runs++;
            result = pendingPolarity == Polarity.Dark
                ? Polygons.Union(result, Polygons.UnionSelf(pending))
                : Polygons.Difference(result, Polygons.UnionSelf(pending));

            pending = Polygons.Empty();
        }

        foreach (var obj in image.Objects)
        {
            var paths = Build(obj, shapes, unrealised, options.SagittaNm);
            if (paths is null || paths.Count == 0)
            {
                continue;
            }

            objects++;

            if (obj.Polarity != pendingPolarity)
            {
                Flush();
                pendingPolarity = obj.Polarity;
            }

            pending.AddRange(paths);
        }

        Flush();

        // A negative file — KiCad writes every soldermask layer this way — states its subject as the
        // *absence* of the drawn area. It is tempting to invert here and be done, and that is wrong
        // twice over. Inverting needs a frame, and the only one available at this point is the
        // drawn extent, which is arbitrary: the real boundary is the board outline, and it lives in
        // a different file. Worse, the drawn area is exactly what CAM wants from a mask layer
        // anyway — the openings are what gets lasered to uncover the pads.
        //
        // So the polarity is reported and not acted on. Polygons.Invert does the job when the
        // caller has a real outline to invert against.
        if (image.IsNegative)
        {
            notes.Add(
                "File declares negative polarity: this area is where the layer's material is " +
                "ABSENT (for a soldermask, the openings). Invert against the board outline to get " +
                "material.");
        }

        foreach (var (name, count) in unrealised.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            notes.Add(Invariant($"{count} objects using aperture '{name}' could not be realised and were skipped."));
        }

        if (options.Canonicalise)
        {
            result = Polygons.Canonicalise(result);
        }

        return new RealisedLayer
        {
            Area = result,
            Bounds = Polygons.BoundsOf(result),
            ObjectCount = objects,
            PolarityRuns = runs,
            DeclaredNegative = image.IsNegative,
            Notes = notes,
            Nets = NetPointsOf(image, result),
        };
    }

    /// <summary>
    /// A point inside the finished copper for every object that names a net.
    ///
    /// Done after compositing rather than during it, because the answer has to be a point in the
    /// copper that *survived*: an object erased by a later clear-polarity one has no business
    /// claiming a net, and the only way to know is to ask the result.
    ///
    /// Each kind needs a different point, and the difference is not cosmetic. A flash is its centre
    /// and a stroke's first segment has a midpoint, and both of those are inside by construction —
    /// measured across three boards, 1,495 of 1,495 of them. A region is the awkward one: its
    /// vertices lie *on* its own outline, where a point is neither inside nor outside, and the first
    /// vertex of a poured region can end up outside the union altogether once neighbouring copper
    /// has merged with it. On the Arduino Mega that was 15 of 21 regions misplaced, every one of
    /// them a pour — which is exactly where a short hides. So a region is asked for the midpoint of
    /// each of its edges in turn, and falls back to the mean of its vertices.
    /// </summary>
    private static List<NetPoint> NetPointsOf(GerberImage image, Paths64 area)
    {
        var points = new List<NetPoint>();

        // Nothing to place, so nothing to index. Silkscreen, mask, paste and outline carry no net
        // attributes at all, and neither does a copper layer from an exporter that writes none —
        // and every one of them was walking each vertex of each ring to build a table for objects
        // that do not exist. A board has more layers without nets than with.
        var hasNets = false;
        foreach (var obj in image.Objects)
        {
            if (obj.Net is not null)
            {
                hasNets = true;
                break;
            }
        }

        if (!hasNets)
        {
            return points;
        }

        // Each ring's bounding box, computed once.
        //
        // Without it this asks Clipper to walk every ring for every object — on the Arduino Mega,
        // 1,516 objects against 384 rings — and measured at 77 % on top of realising the layer at
        // all. A box rejects a ring in four comparisons where walking it costs a point-in-polygon
        // over its vertices, and a board's rings are mostly nowhere near any given point.
        var boxes = new (long MinX, long MinY, long MaxX, long MaxY)[area.Count];

        for (var i = 0; i < area.Count; i++)
        {
            long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;

            foreach (var v in area[i])
            {
                minX = Math.Min(minX, v.X);
                minY = Math.Min(minY, v.Y);
                maxX = Math.Max(maxX, v.X);
                maxY = Math.Max(maxY, v.Y);
            }

            boxes[i] = (minX, minY, maxX, maxY);
        }

        foreach (var obj in image.Objects)
        {
            if (obj.Net is not { } net)
            {
                continue;
            }

            // One candidate list, tested once. Each shape offers the places worth trying, in the
            // order worth trying them, and the first that is strictly inside wins. Asking the
            // shapes to test their own candidates and then testing the winner again here — which
            // is what this did — put every object through the point-in-polygon twice for no
            // answer it did not already have.
            foreach (var candidate in Candidates(obj))
            {
                if (Encloses(area, boxes, candidate))
                {
                    points.Add(new NetPoint(net, candidate));
                    break;
                }
            }
        }

        return points;
    }

    /// <summary>
    /// A point strictly inside a stroke, tried segment by segment.
    ///
    /// Consecutive draws are batched into one object, so a trace is usually several segments, and
    /// taking only the first gave the whole trace one chance. It fails in two ways that matter: an
    /// arc's chord midpoint is not on the arc and can land off the copper entirely, and a first
    /// segment erased by a later clear-polarity object is gone while the rest of the trace is still
    /// there. Either dropped the trace out of the netlist — silently, because a net with no point
    /// looks exactly like a net the file never mentioned.
    ///
    /// A region already worked this way, trying every edge before falling back. This is a stroke
    /// learning what its sibling knows.
    /// </summary>
    private static IEnumerable<Point2> Candidates(GraphicObject obj)
    {
        switch (obj)
        {
            case FlashObject f:
                // A pad's centre is inside it by construction, unless a later clear-polarity object
                // has taken that bit away — in which case nothing else about this flash is better.
                yield return f.At;
                break;

            case DrawObject d:
                // Every segment, not just the first. Consecutive draws are batched into one object,
                // so a trace is usually several; an arc's chord midpoint is not on the arc, and a
                // first segment erased by a later clear object is gone while the rest of the trace
                // survives. Trying only the first dropped the whole trace out of the netlist, and a
                // net with no point looks exactly like a net the file never mentioned.
                foreach (var segment in d.Segments)
                {
                    yield return Midpoint(segment);
                }

                break;

            case RegionObject r when r.Contours.Count > 0 && r.Contours[0].Count > 0:
                {
                    var outer = r.Contours[0];

                    // A region's vertices lie on its own outline, where a point is neither in nor
                    // out, so the edges are asked instead — and the first vertex of a pour can
                    // finish outside the union once neighbouring copper has merged into it. On the
                    // Arduino Mega the obvious choice misplaced 15 of 21 regions, every one a pour,
                    // which is exactly where a short hides.
                    foreach (var segment in outer)
                    {
                        yield return Midpoint(segment);
                    }

                    long x = 0, y = 0;
                    foreach (var segment in outer)
                    {
                        x += segment.From.X;
                        y += segment.From.Y;
                    }

                    yield return new Point2(x / outer.Count, y / outer.Count);
                }

                break;

            default:
                break;
        }
    }

    private static Point2 Midpoint(GerberSegment s) =>
        new((s.From.X + s.To.X) / 2, (s.From.Y + s.To.Y) / 2);

    /// <summary>
    /// Whether a point is strictly inside the filled area, counting a ring inside a ring as a hole.
    ///
    /// Clipper answers per ring, and a layer's area is rings within rings — a pad inside a pour's
    /// clearance is inside two of them and inside neither in the sense that matters. Odd crossings
    /// is the even-odd rule the realiser already fills by. A point on any boundary is refused: it
    /// belongs to no piece in particular, and a net placed on an edge would be claimed by whichever
    /// side the arithmetic fell towards.
    /// </summary>
    private static bool Encloses(
        Paths64 area, (long MinX, long MinY, long MaxX, long MaxY)[] boxes, Point2 p)
    {
        var point = new Point64(p.X, p.Y);
        var inside = 0;

        for (var i = 0; i < area.Count; i++)
        {
            // Outside the box is outside the ring, and costs four comparisons to find out.
            var box = boxes[i];
            if (p.X < box.MinX || p.X > box.MaxX || p.Y < box.MinY || p.Y > box.MaxY)
            {
                continue;
            }

            var where = Polygons.PointIn(point, area[i]);

            if (where == PointInPolygonResult.IsOn)
            {
                return false;
            }

            if (where == PointInPolygonResult.IsInside)
            {
                inside++;
            }
        }

        return inside % 2 == 1;
    }

    private static Paths64? Build(
        GraphicObject obj,
        Dictionary<int, Paths64?> shapes,
        Dictionary<string, int> unrealised,
        long sagittaNm) => obj switch
        {
            FlashObject flash => Flash(flash, shapes, unrealised, sagittaNm),
            DrawObject draw => Stroke(draw, shapes, unrealised, sagittaNm),
            RegionObject region => Region(region, sagittaNm),
            _ => null,
        };

    private static Paths64? Flash(
        FlashObject flash,
        Dictionary<int, Paths64?> shapes,
        Dictionary<string, int> unrealised,
        long sagittaNm)
    {
        var shape = ShapeFor(flash.Aperture, shapes, unrealised, sagittaNm);
        return shape is null ? null : ApertureShapes.Translate(shape, flash.At);
    }

    /// <summary>
    /// A stroke is the aperture dragged along the path — a Minkowski sum.
    ///
    /// The circular case is special-cased onto Clipper's offsetter, which is both faster and
    /// exactly right: sweeping a disc along a polyline *is* an offset with round joins and round
    /// caps. Everything else goes through a genuine Minkowski sum.
    /// </summary>
    private static Paths64? Stroke(
        DrawObject draw,
        Dictionary<int, Paths64?> shapes,
        Dictionary<string, int> unrealised,
        long sagittaNm)
    {
        var polyline = Tessellate.Flatten(ToArt(draw.Segments), sagittaNm);
        if (polyline.Count == 0)
        {
            return null;
        }

        // A stroke that never moves is a dot, and Clipper's offsetter has nothing to offset. Real
        // files do this — it is how some tools draw a single via stub.
        if (IsDegenerate(polyline))
        {
            var dot = ShapeFor(draw.Aperture, shapes, unrealised, sagittaNm);
            return dot is null ? null : ApertureShapes.Translate(dot, Tessellate.ToPoint2(polyline[0]));
        }

        var closed = polyline.Count > 2 && polyline[0] == polyline[^1];

        if (draw.Aperture.Kind == ApertureKind.Circle)
        {
            var radius = draw.Aperture.NominalWidthNm / 2.0;
            if (radius <= 0)
            {
                return null;
            }

            return Clipper.InflatePaths(
                Polygons.From(polyline),
                radius,
                JoinType.Round,
                closed ? EndType.Joined : EndType.Round,
                arcTolerance: sagittaNm);
        }

        var pen = ShapeFor(draw.Aperture, shapes, unrealised, sagittaNm);
        if (pen is null || pen.Count == 0)
        {
            return null;
        }

        // Minkowski takes a single closed pattern, so a pen with a hole in it contributes only its
        // outer contour. Stroking with such an aperture is undefined in the specification anyway;
        // this at least errs toward covering more copper rather than less.
        var swept = Polygons.Empty();
        foreach (var contour in pen)
        {
            swept.AddRange(Clipper.MinkowskiSum(contour, polyline, closed));
        }

        return Polygons.UnionSelf(swept);
    }

    /// <summary>
    /// A <c>G36</c>/<c>G37</c> region. Its contours nest, and even-odd is what makes an inner
    /// contour a hole instead of a second island.
    /// </summary>
    private static Paths64? Region(RegionObject region, long sagittaNm)
    {
        var contours = Polygons.Empty();
        foreach (var contour in region.Contours)
        {
            var path = Tessellate.Flatten(ToArt(contour), sagittaNm);

            // A contour is closed by definition, so a repeated final point is just a duplicate
            // vertex; Clipper tolerates it, but dropping it keeps vertex counts honest.
            if (path.Count > 2 && path[0] == path[^1])
            {
                path.RemoveAt(path.Count - 1);
            }

            if (path.Count >= 3)
            {
                contours.Add(path);
            }
        }

        return contours.Count == 0 ? null : Polygons.ResolveEvenOdd(contours);
    }

    private static Paths64? ShapeFor(
        Aperture aperture,
        Dictionary<int, Paths64?> shapes,
        Dictionary<string, int> unrealised,
        long sagittaNm)
    {
        if (shapes.TryGetValue(aperture.Code, out var cached))
        {
            if (cached is null)
            {
                Count(aperture, unrealised);
            }

            return cached;
        }

        Paths64? built;
        try
        {
            built = ApertureShapes.TryBuild(aperture, sagittaNm);
        }
        catch (GerberParseException)
        {
            // A macro whose expressions cannot be evaluated with these parameters. One bad
            // aperture should cost its own objects, not the whole layer.
            built = null;
        }

        shapes[aperture.Code] = built;
        if (built is null)
        {
            Count(aperture, unrealised);
        }

        return built;
    }

    private static void Count(Aperture aperture, Dictionary<string, int> unrealised)
    {
        var name = aperture.Macro?.Name ?? aperture.Kind.ToString();
        unrealised[name] = unrealised.GetValueOrDefault(name) + 1;
    }

    private static bool IsDegenerate(Path64 path)
    {
        for (var i = 1; i < path.Count; i++)
        {
            if (path[i] != path[0])
            {
                return false;
            }
        }

        return true;
    }

    private static ArtSegment[] ToArt(IReadOnlyList<GerberSegment> segments)
    {
        var result = new ArtSegment[segments.Count];
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            result[i] = new ArtSegment(
                s.Kind switch
                {
                    SegmentKind.ClockwiseArc => ArtSweep.Clockwise,
                    SegmentKind.CounterClockwiseArc => ArtSweep.CounterClockwise,
                    _ => ArtSweep.Linear,
                },
                s.From,
                s.To,
                s.Centre);
        }

        return result;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
