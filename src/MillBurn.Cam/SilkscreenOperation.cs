using System.Globalization;
using MillBurn.Core;
using MillBurn.Gerber.Model;

namespace MillBurn.Cam;

/// <summary>Settings for turning a silk layer into laser artwork.</summary>
public sealed record SilkscreenOptions
{
    /// <summary>
    /// The beam's spot size. Everything in this operation turns on it: a stroke no wider than the
    /// beam needs no geometry at all, because tracing its centreline lays down exactly the right
    /// width.
    /// </summary>
    public long SpotSizeNm { get; init; } = Nm.FromMillimetres(0.10);

    /// <summary>
    /// How much wider than the spot a stroke may be and still be traced at 1x. A little over 1.0
    /// because the two numbers are close by construction — KiCad draws silk at 0.10-0.15 mm and a
    /// diode spot is 0.06-0.15 mm — and demanding an exact match would push ordinary boards onto
    /// the fill path for no visible benefit.
    /// </summary>
    public double CentrelineRatio { get; init; } = 1.5;

    public long CentrelineLimitNm =>
        (long)Math.Round(SpotSizeNm * CentrelineRatio, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Silkscreen to laser artwork.
///
/// This is the one board operation that needs **no geometry realisation**: no aperture rendering,
/// no polarity compositing, no offsetting, no DRC. A real KiCad silk layer is stroked line art
/// (<c>PogoTest1-F_Silkscreen.gbr</c> is 217 draws, two apertures, zero flashes) and the beam is
/// already the width the strokes are drawn at, so the Gerber's own segments *are* the output. See
/// Documentation/04, section 2.5.
///
/// That is why it ships before the copper path rather than after it.
/// </summary>
public static class SilkscreenOperation
{
    public static Artwork Build(GerberImage silk, SilkscreenOptions options, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(silk);
        ArgumentNullException.ThrowIfNull(options);

        // Strokes are collected per width rather than per source object. KiCad writes silk as one
        // move-plus-draw per segment, so a legend arrives as hundreds of single-segment objects;
        // emitting a separate path element for each is correct but wasteful. Every stroke of one
        // width shares a style, so they merge into a single element with many subpaths.
        var marks = new SortedDictionary<long, List<IReadOnlyList<ArtSegment>>>();
        var wide = new SortedDictionary<long, List<IReadOnlyList<ArtSegment>>>();
        var fills = new List<ArtShape>();
        var notes = new List<string>();

        var clearCount = 0;
        var widestStrokeNm = 0L;
        var unrealised = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var obj in silk.Objects)
        {
            // Clear polarity subtracts from what came before, which needs the boolean stage. On a
            // silk layer it is vanishingly rare; count it and say so rather than draw it as if it
            // were dark, which would put ink where the board has none.
            if (obj.Polarity == Polarity.Clear)
            {
                clearCount++;
                continue;
            }

            switch (obj)
            {
                case DrawObject draw:
                    {
                        var width = draw.Aperture.NominalWidthNm;
                        widestStrokeNm = Math.Max(widestStrokeNm, width);

                        var bucket = width <= options.CentrelineLimitNm ? marks : wide;
                        if (!bucket.TryGetValue(width, out var subpaths))
                        {
                            subpaths = [];
                            bucket[width] = subpaths;
                        }

                        subpaths.AddRange(SplitIntoSubpaths(draw.Segments));
                        break;
                    }

                case FlashObject flash:
                    {
                        var subpaths = ApertureOutline.TryBuild(flash.Aperture, flash.At);
                        if (subpaths is null)
                        {
                            var name = flash.Aperture.Macro?.Name ?? flash.Aperture.Kind.ToString();
                            unrealised[name] = unrealised.GetValueOrDefault(name) + 1;
                            break;
                        }

                        fills.Add(new ArtShape { Subpaths = subpaths, Filled = true });
                        break;
                    }

                case RegionObject region:
                    {
                        fills.Add(new ArtShape
                        {
                            Subpaths = [.. region.Contours.Select(c => (IReadOnlyList<ArtSegment>)[.. c.Select(ToArt)])],
                            Filled = true,
                        });
                        break;
                    }

                default:
                    break;
            }
        }

        var layers = new List<ArtLayer>();

        if (marks.Count > 0)
        {
            layers.Add(new ArtLayer
            {
                Id = "silk-mark",
                Label = "Silkscreen (trace at 1x)",
                Role = ArtRole.Mark,
                Shapes = ToShapes(marks),
            });
        }

        if (wide.Count > 0)
        {
            layers.Add(new ArtLayer
            {
                Id = "silk-wide",
                Label = "Silkscreen (wider than the beam)",
                Role = ArtRole.Boundary,
                Shapes = ToShapes(wide),
            });
        }

        if (fills.Count > 0)
        {
            layers.Add(new ArtLayer
            {
                Id = "silk-fill",
                Label = "Silkscreen (filled shapes)",
                Role = ArtRole.Fill,
                Shapes = fills,
            });
        }

        var spotMm = Nm.ToMillimetreString(options.SpotSizeNm, 3);
        var limitMm = Nm.ToMillimetreString(options.CentrelineLimitNm, 3);
        notes.Add(Invariant($"Beam spot {spotMm} mm; strokes up to {limitMm} mm traced at 1x."));

        if (widestStrokeNm > 0)
        {
            notes.Add(Invariant($"Widest stroke on this layer: {Nm.ToMillimetreString(widestStrokeNm, 3)} mm."));
        }

        if (wide.Count > 0)
        {
            var wideCount = wide.Values.Sum(v => v.Count);
            notes.Add(Invariant(
                $"{wideCount} stroked shapes are wider than the beam. They are emitted as centrelines on the 'silk-wide' layer and will come out too thin; outline-and-fill realisation arrives with the geometry stage."));
        }

        if (clearCount > 0)
        {
            notes.Add(Invariant(
                $"{clearCount} clear-polarity objects were skipped; silk compositing is not implemented."));
        }

        foreach (var (name, count) in unrealised.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            notes.Add(Invariant($"{count} flashes of aperture '{name}' were skipped: macro primitives are not realised yet."));
        }

        return new Artwork
        {
            Layers = layers,
            ContentBounds = ArtGeometry.Measure(layers),
            Notes = notes,
            Source = source,
        };
    }

    /// <summary>One shape per stroke width, in ascending width order so the output is stable.</summary>
    private static List<ArtShape> ToShapes(SortedDictionary<long, List<IReadOnlyList<ArtSegment>>> byWidth) =>
        [.. byWidth.Select(kv => new ArtShape
        {
            Subpaths = kv.Value,
            StrokeWidthNm = kv.Key,
            Filled = false,
        })];

    /// <summary>
    /// Breaks a stroke into runs of connected segments. The parser batches consecutive D01s into
    /// one object for compactness, so a single object can hold more than one run; joining them
    /// blindly would draw a line across the board between two unrelated glyphs.
    /// </summary>
    private static List<IReadOnlyList<ArtSegment>> SplitIntoSubpaths(
        IReadOnlyList<GerberSegment> segments)
    {
        var subpaths = new List<IReadOnlyList<ArtSegment>>();
        var current = new List<ArtSegment>();

        foreach (var segment in segments)
        {
            if (current.Count > 0 && current[^1].To != segment.From)
            {
                subpaths.Add(current);
                current = [];
            }

            current.Add(ToArt(segment));
        }

        if (current.Count > 0)
        {
            subpaths.Add(current);
        }

        return subpaths;
    }

    private static ArtSegment ToArt(GerberSegment s) => new(
        s.Kind switch
        {
            SegmentKind.ClockwiseArc => ArtSweep.Clockwise,
            SegmentKind.CounterClockwiseArc => ArtSweep.CounterClockwise,
            _ => ArtSweep.Linear,
        },
        s.From,
        s.To,
        s.Centre);

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
