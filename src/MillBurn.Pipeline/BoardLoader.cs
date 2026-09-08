using System.Globalization;
using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using System.Text;
using MillBurn.Gerber;
using MillBurn.Gerber.Model;
using MillBurn.Gerber.Excellon;
using MillBurn.Geometry;

namespace MillBurn.Pipeline;

/// <summary>
/// Loads a whole export folder: identify each file, parse it, realise its geometry.
///
/// The unit is the folder rather than the file because that is what an EDA tool produces and what
/// a user drags onto the window. It also means the loader can say useful things no single file
/// knows — that there is no outline, or that two files both claim to be the top copper.
/// </summary>
public static class BoardLoader
{
    private static readonly string[] GerberExtensions =
        [".gbr", ".ger", ".gtl", ".gbl", ".gts", ".gbs", ".gto", ".gbo", ".gtp", ".gbp", ".gko", ".gm1"];

    private static readonly string[] DrillExtensions = [".drl", ".xln"];

    public static Board LoadFolder(string folder, RealisationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"'{folder}' is not a directory.");
        }

        var files = Directory.EnumerateFiles(folder)
            .Where(IsBoardFile)
            .Order(StringComparer.Ordinal)
            .ToList();

        return Load(folder, files, options);
    }

    public static Board Load(string source, IEnumerable<string> files, RealisationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        options ??= new RealisationOptions();

        var layers = new List<BoardLayer>();
        var failures = new List<string>();

        foreach (var file in files)
        {
            try
            {
                layers.Add(IsDrill(file) ? LoadDrill(file, options) : LoadGerber(file, options));
            }
            catch (Exception ex) when (ex is IOException or GerberParseException or UnauthorizedAccessException)
            {
                // One unreadable file must not cost the whole board. The name and the reason go in
                // the result so the UI can say which file, rather than showing a board that is
                // quietly missing a layer.
                failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return new Board
        {
            Source = source,
            Layers = layers,
            Failures = failures,
        };
    }

    /// <summary>
    /// Realises sources already held in memory, with roles the caller has already decided.
    ///
    /// This is the path a saved project takes: its files live inside the project container, not in
    /// a folder, and its roles may have been corrected by hand — so re-detecting them here would
    /// quietly undo the correction every time the project was opened.
    /// </summary>
    public static Board LoadSources(
        string source,
        IEnumerable<(string FileName, byte[] Content, LayerRole Role)> sources,
        RealisationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        options ??= new RealisationOptions();

        var layers = new List<BoardLayer>();
        var failures = new List<string>();

        foreach (var (fileName, content, role) in sources)
        {
            try
            {
                var text = System.Text.Encoding.UTF8.GetString(content);
                layers.Add(LayerRoleInfo.IsDrill(role)
                    ? RealiseDrill(fileName, ExcellonParser.Parse(text), role, roleGuessed: false, options)
                    : RealiseGerber(fileName, GerberParser.Parse(text), role, roleGuessed: false, options));
            }
            catch (Exception ex) when (ex is GerberParseException or DecoderFallbackException)
            {
                failures.Add($"{fileName}: {ex.Message}");
            }
        }

        return new Board { Source = source, Layers = layers, Failures = failures };
    }

    public static bool IsBoardFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return GerberExtensions.Contains(extension) || DrillExtensions.Contains(extension);
    }

    private static bool IsDrill(string path) =>
        DrillExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static BoardLayer LoadGerber(string file, RealisationOptions options)
    {
        var image = GerberParser.ParseFile(file);
        var (role, guessed) = LayerRoles.Detect(image, Path.GetFileName(file));
        return RealiseGerber(Path.GetFileName(file), image, role, guessed, options);
    }

    private static BoardLayer RealiseGerber(
        string fileName, GerberImage image, LayerRole role, bool roleGuessed, RealisationOptions options)
    {
        var layer = GerberRealiser.Realise(image, options);

        return new BoardLayer
        {
            FileName = fileName,
            Role = role,
            RoleGuessed = roleGuessed,
            Area = layer.Area,
            Bounds = layer.Bounds,
            ObjectCount = layer.ObjectCount,
            DeclaredNegative = layer.DeclaredNegative,
            Notes = layer.Notes,
            Diagnostics = image.Diagnostics,
        };
    }

    /// <summary>
    /// A drill file becomes area too — a disc per hole, a swept slot per slot — so the viewer and
    /// every later stage treat it exactly like any other layer instead of special-casing it.
    /// </summary>
    private static BoardLayer LoadDrill(string file, RealisationOptions options)
    {
        var drill = ExcellonParser.ParseFile(file);

        var role = drill.Plating switch
        {
            HolePlating.NonPlated => LayerRole.NonPlatedDrill,
            HolePlating.Plated => LayerRole.PlatedDrill,
            _ => LayerRoles.FromFileName(Path.GetFileName(file)),
        };

        return RealiseDrill(
            Path.GetFileName(file), drill, role, drill.Plating == HolePlating.Unknown, options);
    }

    private static BoardLayer RealiseDrill(
        string fileName, ExcellonFile drill, LayerRole role, bool roleGuessed, RealisationOptions options)
    {
        var notes = new List<string>();
        var holes = Polygons.Empty();

        foreach (var hit in drill.Hits)
        {
            var radius = RadiusOf(drill, hit.Tool);
            if (radius > 0)
            {
                holes.Add(Tessellate.Circle(hit.At, radius, options.SagittaNm));
            }
        }

        // A slot is the drill dragged from one point to the other: the same swept-disc the stroke
        // realiser does, and the same reason it must not be drawn as two separate holes.
        var slotPaths = Polygons.Empty();
        foreach (var slot in drill.Slots)
        {
            var radius = RadiusOf(drill, slot.Tool);
            if (radius <= 0)
            {
                continue;
            }

            var line = new Path64
            {
                new Point64(slot.From.X, slot.From.Y),
                new Point64(slot.To.X, slot.To.Y),
            };

            slotPaths.AddRange(Clipper.InflatePaths(
                Polygons.From(line), radius, JoinType.Round, EndType.Round, arcTolerance: options.SagittaNm));
        }

        var area = Polygons.Union(Polygons.UnionSelf(holes), Polygons.UnionSelf(slotPaths));

        if (drill.Hits.Count > 0 || drill.Slots.Count > 0)
        {
            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{drill.Hits.Count} holes in {drill.Tools.Count} tools, {drill.Slots.Count} slots."));
        }

        return new BoardLayer
        {
            FileName = fileName,
            Role = role,
            RoleGuessed = roleGuessed,
            Area = area,
            Bounds = Polygons.BoundsOf(area),
            ObjectCount = drill.Hits.Count + drill.Slots.Count,
            Notes = notes,
            Diagnostics = drill.Diagnostics,
            Drill = drill,
        };
    }

    private static long RadiusOf(ExcellonFile drill, int tool) =>
        drill.Tools.TryGetValue(tool, out var t) ? t.DiameterNm / 2 : 0;
}
