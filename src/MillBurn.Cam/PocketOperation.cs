using System.Globalization;
using Clipper2Lib;
using MillBurn.Core;
using MillBurn.Geometry;

namespace MillBurn.Cam;

/// <summary>How to clear an area rather than trace round it.</summary>
public sealed record PocketOptions
{
    public Tool Tool { get; init; } = Tool.DefaultVBit;

    /// <summary>
    /// How deep to go. For soldermask relief this is the mask thickness and very little else —
    /// cured mask is typically 20–40 µm.
    /// </summary>
    public long DepthNm { get; init; } = Nm.FromMillimetres(0.04);

    /// <summary>
    /// How far the tool steps in for each successive ring, as a fraction of the cut width.
    ///
    /// Well under one, because at exactly one the rings only just meet and any error at all leaves
    /// a ridge of uncleared material between them.
    /// </summary>
    public double Stepover { get; init; } = 0.6;

    public long SagittaNm { get; init; } = Tessellate.DefaultSagittaNm;

    public long EffectiveWidthNm => Tool.WidthAtDepth(DepthNm);
}

/// <summary>
/// Clears the material inside a region, ring by ring, working inwards from its boundary.
///
/// What this is for: milling cured **soldermask off the pads** so they can be soldered, using the
/// paste layer's apertures as the areas to clear. The paste layer is exactly the right geometry for
/// it — those apertures are where solder is meant to go, which is where the mask must not be.
///
/// **This operation is far more sensitive to a board being flat than anything else here.** Mask is
/// tens of microns thick. An isolation cut that is 50 µm deep instead of 40 µm still isolates; a
/// mask relief that is 50 µm deep instead of 40 µm is into the copper, and one that is 30 µm deep
/// leaves mask on the pad. Board flatness across even a small board is routinely worse than the
/// whole depth of this cut, which is why it usually needs height mapping to work at all.
/// </summary>
public static class PocketOperation
{
    /// <summary>
    /// Contour-parallel clearing: offset the boundary inwards by half a cut width to get the first
    /// tool centreline, then keep stepping inwards until nothing is left.
    ///
    /// Outside-in rather than inside-out, so the pass that defines the edge of the cleared area is
    /// cut first, in undisturbed material, and every later pass takes a partial width.
    /// </summary>
    public static Toolpath Build(Paths64 regions, PocketOptions options, string label = "Mask relief")
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(options);

        var width = options.EffectiveWidthNm;
        var notes = new List<string>();
        var passes = new List<ToolpathPass>();

        if (width <= 0)
        {
            notes.Add("This tool cuts nothing at this depth.");
            return Empty(options, label, notes);
        }

        var area = Polygons.UnionSelf(regions);
        var step = Math.Max((long)(width * Math.Clamp(options.Stepover, 0.05, 1.0)), 1);

        // Each region is cleared as its own stack, so the tool finishes one pad before moving to
        // the next rather than crossing the board once per ring.
        var stack = 0;
        var unreachable = 0;

        foreach (var region in Polygons.Separate(area))
        {
            var rings = new List<Path64>();
            var inset = width / 2;

            while (true)
            {
                var offset = Clipper.InflatePaths(
                    region, -inset, JoinType.Round, EndType.Polygon, arcTolerance: options.SagittaNm);

                if (offset.Count == 0)
                {
                    break;
                }

                rings.AddRange(offset.Where(r => r.Count >= 3));
                inset += step;
            }

            if (rings.Count == 0)
            {
                // Smaller than the tool: no centreline fits inside it at all. Reported rather than
                // approximated, because a pad the tool cannot enter is one that will still have
                // mask on it, and nothing about the picture would say so.
                unreachable++;
                continue;
            }

            foreach (var ring in rings)
            {
                passes.Add(new ToolpathPass
                {
                    Path = IsolationOperation.ToSegments(ring),
                    DepthNm = options.DepthNm,
                    Closed = true,
                    Stack = stack,
                });
            }

            stack++;
        }

        var depthMm = Nm.ToMillimetreString(options.DepthNm, 3);
        var widthMm = Nm.ToMillimetreString(width, 3);
        notes.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{options.Tool.Name} at {depthMm} mm deep clears {widthMm} mm per pass."));

        if (unreachable > 0)
        {
            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{unreachable} opening(s) are smaller than the tool and were not cleared."));
        }

        return new Toolpath
        {
            Kind = ToolpathKind.Pocket,
            Label = label,
            Tool = options.Tool,
            Passes = passes,
            Notes = notes,
        };
    }

    /// <summary>How many openings the tool cannot get into at all.</summary>
    public static int UnreachableOpenings(Paths64 regions, PocketOptions options)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(options);

        var width = options.EffectiveWidthNm;
        if (width <= 0)
        {
            return 0;
        }

        var count = 0;
        foreach (var region in Polygons.Separate(Polygons.UnionSelf(regions)))
        {
            var fits = Clipper.InflatePaths(
                region, -width / 2, JoinType.Round, EndType.Polygon, arcTolerance: options.SagittaNm);

            if (fits.Count == 0)
            {
                count++;
            }
        }

        return count;
    }

    private static Toolpath Empty(PocketOptions options, string label, List<string> notes) => new()
    {
        Kind = ToolpathKind.Pocket,
        Label = label,
        Tool = options.Tool,
        Notes = notes,
    };
}
