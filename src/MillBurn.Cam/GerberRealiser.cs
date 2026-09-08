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

/// <summary>The filled result of one Gerber layer, plus what had to be said about producing it.</summary>
public sealed record RealisedLayer
{
    public required Paths64 Area { get; init; }

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
        };
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
