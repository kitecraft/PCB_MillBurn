using System.Globalization;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Export;
using MillBurn.Gerber;
using MillBurn.Gerber.Excellon;
using MillBurn.Gerber.Model;
using MillBurn.Pipeline;
using MillBurn.Viewer;
using SkiaSharp;

namespace MillBurn.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("PCB_MillBurn CLI");
            Console.WriteLine();
            Console.WriteLine("  inspect <file-or-directory>   Parse Gerber files and report what was understood");
            Console.WriteLine("  svg <silkscreen.gbr> [options] Export a silk layer as laser-ready SVG");
            Console.WriteLine("  project save <folder> [-o p]   Build a .millburn project from an export folder");
            Console.WriteLine("  project info <project>         Report what a project contains");
            Console.WriteLine("  project refresh <p> [--apply]  Compare against the source folder; --apply takes the changes");
            Console.WriteLine("  board <directory>              Load a whole export folder: detect layers, realise, report");
            Console.WriteLine("                                 --png <path> renders the board through the real viewer");
            Console.WriteLine("  render <file-or-directory>     Realise Gerber geometry and report the filled area");
            Console.WriteLine("                                 --svg <path> also writes the result as SVG");
            Console.WriteLine();
            Console.WriteLine("  svg options:");
            Console.WriteLine("    -o <path>          Output file (default: alongside the input)");
            Console.WriteLine("    --flavour <name>   lightburn (default) | inkscape");
            Console.WriteLine("    --mirror           Mirror for a bottom-side layer");
            Console.WriteLine("    --single-layer     One group, one path: for importers that make a layer per object");
            Console.WriteLine("    --spot <mm>        Laser spot size (default 0.10)");
            Console.WriteLine("    --margin <mm>      Page margin around the artwork (default 5)");
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "inspect" when args.Length >= 2 => Inspect(args[1]),
            "svg" when args.Length >= 2 => ExportSvg(args),
            "render" when args.Length >= 2 => Render(args),
            "board" when args.Length >= 2 => LoadBoard(args),
            "project" when args.Length >= 2 => ProjectCommand(args),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"Unknown command '{verb}'.");
        return 1;
    }

    /// <summary>
    /// The parse report from Documentation/06's risk table: show exactly what was understood, so a
    /// misread file is never silent. Real-world Gerbers break parsers in creative ways, and the
    /// only defence is making the parser's understanding inspectable.
    /// </summary>
    private static int Inspect(string path)
    {
        var files = Directory.Exists(path)
            ? Directory.EnumerateFiles(path)
                .Where(f => f.EndsWith(".gbr", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".ger", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".gtl", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".gbl", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".gko", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".drl", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".xln", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [path];

        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No Gerber files found in '{path}'.");
            return 1;
        }

        var anyErrors = false;

        foreach (var file in files)
        {
            Console.WriteLine();
            Console.WriteLine(Path.GetFileName(file));
            Console.WriteLine(new string('=', Path.GetFileName(file).Length));

            if (file.EndsWith(".drl", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".xln", StringComparison.OrdinalIgnoreCase))
            {
                anyErrors |= !InspectDrill(file);
                continue;
            }

            GerberImage image;
            try
            {
                image = GerberParser.ParseFile(file);
            }
            catch (Exception ex) when (ex is IOException or GerberParseException)
            {
                Console.Error.WriteLine($"  FAILED: {ex.Message}");
                anyErrors = true;
                continue;
            }

            var draws = image.Objects.OfType<DrawObject>().ToList();
            var flashes = image.Objects.OfType<FlashObject>().ToList();
            var regions = image.Objects.OfType<RegionObject>().ToList();
            var segments = draws.Sum(d => d.Segments.Count)
                         + regions.Sum(r => r.Contours.Sum(c => c.Count));
            var arcs = draws.Sum(d => d.Segments.Count(s => s.IsArc))
                     + regions.Sum(r => r.Contours.Sum(c => c.Count(s => s.IsArc)));

            Line($"  function    {image.FileFunction ?? "(not declared)"}");
            Line($"  units       {image.Unit}, format {image.Format}");
            Line($"  extents     {image.Bounds}");
            Line($"  objects     {flashes.Count} flashes, {draws.Count} draws, {regions.Count} regions");
            Line($"  segments    {segments:N0} ({arcs:N0} arcs kept as arcs)");
            Line($"  attributes  {(image.HasExtendedAttributes ? "X2 present - pad selection is exact" : "none - pads would be inferred")}");

            if (image.Apertures.Count > 0)
            {
                Console.WriteLine("  apertures");
                foreach (var aperture in image.Apertures.Values.OrderBy(a => a.Code))
                {
                    var uses = flashes.Count(f => f.Aperture.Code == aperture.Code)
                             + draws.Count(d => d.Aperture.Code == aperture.Code);
                    Line($"    {aperture}  x{uses}");
                }
            }

            var nets = image.Objects
                .Select(o => o.Net)
                .Where(n => n is not null)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (nets.Count > 0)
            {
                Line($"  nets        {nets.Count}: {string.Join(", ", nets)}");
            }

            var pads = flashes.Where(f => f.Aperture.IsPad).ToList();
            if (pads.Count > 0)
            {
                var vias = pads.Count(p => p.Aperture.IsVia);
                Line($"  pads        {pads.Count} ({vias} vias) - selectable directly for a mask-open pass");
            }

            var errors = image.Diagnostics.Where(d => d.IsError).ToList();
            var warnings = image.Diagnostics.Where(d => !d.IsError).ToList();

            if (errors.Count > 0)
            {
                anyErrors = true;
                Console.WriteLine("  ERRORS");
                foreach (var d in errors.Take(10))
                {
                    Console.WriteLine($"    {d}");
                }

                if (errors.Count > 10)
                {
                    Line($"    ... and {errors.Count - 10} more");
                }
            }

            if (warnings.Count > 0)
            {
                Console.WriteLine("  warnings");
                foreach (var group in warnings.GroupBy(w => w.Message).Take(10))
                {
                    Line($"    {group.Key} (x{group.Count()})");
                }
            }
        }

        return anyErrors ? 2 : 0;
    }

    private static bool InspectDrill(string file)
    {
        ExcellonFile drill;
        try
        {
            drill = ExcellonParser.ParseFile(file);
        }
        catch (Exception ex) when (ex is IOException or GerberParseException)
        {
            Console.Error.WriteLine($"  FAILED: {ex.Message}");
            return false;
        }

        Line($"  function    {drill.FileFunction ?? "(not declared)"}");
        Line($"  units       {drill.Unit}, plating {drill.Plating}");
        Line($"  extents     {drill.Bounds}");
        Line($"  holes       {drill.Hits.Count} in {drill.Tools.Count} tools, {drill.Slots.Count} slots");

        foreach (var (tool, count) in drill.ByTool())
        {
            Line($"    {tool}  x{count}");
        }

        var errors = drill.Diagnostics.Where(d => d.IsError).ToList();
        if (errors.Count > 0)
        {
            Console.WriteLine("  ERRORS");
            foreach (var d in errors.Take(10))
            {
                Console.WriteLine($"    {d}");
            }
        }

        return errors.Count == 0;
    }

    /// <summary>
    /// Silkscreen to laser-ready SVG. The first end-to-end output the app produces, and the
    /// cheapest: silk is stroked line art whose stroke width already matches the beam, so nothing
    /// between the parser and the writer has to realise any geometry
    /// (Documentation/04, section 2.5).
    /// </summary>
    private static int ExportSvg(string[] args)
    {
        var input = args[1];
        string? output = null;
        var profile = SvgProfile.LightBurn;
        var mirror = false;
        var singleLayer = false;
        var spotMm = 0.10;
        var marginMm = 5.0;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-o" or "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;

                case "--mirror":
                    mirror = true;
                    break;

                case "--single-layer":
                    singleLayer = true;
                    break;

                case "--flavour" or "--flavor" when i + 1 < args.Length:
                    var name = args[++i].ToLowerInvariant();
                    if (name is not ("lightburn" or "inkscape"))
                    {
                        Console.Error.WriteLine($"Unknown flavour '{name}'. Use lightburn or inkscape.");
                        return 1;
                    }

                    profile = name == "inkscape" ? SvgProfile.Inkscape : SvgProfile.LightBurn;
                    break;

                case "--spot" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out spotMm)
                        || spotMm <= 0)
                    {
                        Console.Error.WriteLine("--spot needs a positive size in millimetres.");
                        return 1;
                    }

                    break;

                case "--margin" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out marginMm)
                        || marginMm < 0)
                    {
                        Console.Error.WriteLine("--margin needs a size in millimetres.");
                        return 1;
                    }

                    break;

                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}'.");
                    return 1;
            }
        }

        GerberImage image;
        try
        {
            image = GerberParser.ParseFile(input);
        }
        catch (Exception ex) when (ex is IOException or GerberParseException)
        {
            Console.Error.WriteLine($"FAILED: {ex.Message}");
            return 1;
        }

        foreach (var d in image.Diagnostics.Where(d => d.IsError))
        {
            Console.Error.WriteLine($"  {d}");
        }

        var artwork = SilkscreenOperation.Build(
            image,
            new SilkscreenOptions { SpotSizeNm = Nm.FromMillimetres(spotMm) },
            Path.GetFileName(input));

        if (artwork.ContentBounds.IsEmpty)
        {
            Console.Error.WriteLine("Nothing to export: the layer produced no drawable geometry.");
            return 2;
        }

        var page = SvgPage.ForContent(artwork.ContentBounds, Nm.FromMillimetres(marginMm));
        output ??= Path.ChangeExtension(input, ".svg");

        SvgWriter.WriteFile(
            output,
            artwork,
            page,
            new SvgExportOptions
            {
                Profile = profile,
                Mirror = mirror,
                SingleLayer = singleLayer,
                Title = Path.GetFileNameWithoutExtension(input),
                Timestamp = DateTimeOffset.UtcNow,
            });

        Console.WriteLine(output);
        Line($"  page        {page}");
        Line($"  content     {artwork.ContentBounds}");
        Line($"  flavour     {profile.Flavour}{(mirror ? ", mirrored" : "")}{(singleLayer ? ", single layer" : "")}");
        Line($"  elements    {CountElements(output)} drawable element(s) in {CountGroups(output)} group(s)");

        foreach (var layer in artwork.Layers)
        {
            Line($"  layer       {layer.Id,-12} {profile.LayerNameFor(layer.Role),-4} {layer.SubpathCount,5} paths in {layer.ShapeCount} elements  ({layer.Label})");
        }

        foreach (var note in artwork.Notes)
        {
            Line($"  note        {note}");
        }

        return 0;
    }

    /// <summary>
    /// Realise a Gerber into filled area and report what came out. The point is the numbers: an
    /// area, a ring count and a bounding box are enough to tell at a glance whether the geometry is
    /// plausible, and they are the first thing to look at when it is not.
    /// </summary>
    private static int Render(string[] args)
    {
        var input = args[1];
        var sagittaMm = 0.001;
        var canonical = false;
        string? svgOut = null;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--tolerance" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out sagittaMm)
                        || sagittaMm <= 0)
                    {
                        Console.Error.WriteLine("--tolerance needs a positive size in millimetres.");
                        return 1;
                    }

                    break;

                case "--canonical":
                    canonical = true;
                    break;

                case "--svg" when i + 1 < args.Length:
                    svgOut = args[++i];
                    break;

                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}'.");
                    return 1;
            }
        }

        var files = Directory.Exists(input)
            ? Directory.EnumerateFiles(input, "*.gbr").Order(StringComparer.Ordinal).ToList()
            : [input];

        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No Gerber files found in '{input}'.");
            return 1;
        }

        var options = new RealisationOptions
        {
            SagittaNm = Nm.FromMillimetres(sagittaMm),
            Canonicalise = canonical,
        };

        var anyErrors = false;

        foreach (var file in files)
        {
            GerberImage image;
            try
            {
                image = GerberParser.ParseFile(file);
            }
            catch (Exception ex) when (ex is IOException or GerberParseException)
            {
                Console.Error.WriteLine($"{Path.GetFileName(file)}  FAILED: {ex.Message}");
                anyErrors = true;
                continue;
            }

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var layer = GerberRealiser.Realise(image, options);
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

            Console.WriteLine();
            Console.WriteLine(Path.GetFileName(file));
            Line($"  function    {image.FileFunction ?? "(not declared)"}");
            Line($"  input       {image.Objects.Count} objects, {image.Apertures.Count} apertures");
            var negative = layer.DeclaredNegative ? "  [negative]" : "";
            Line($"  realised    {layer.ObjectCount} objects in {layer.PolarityRuns} polarity run(s){negative}");
            Line($"  area        {layer.AreaMm2:F4} mm^2 in {layer.RingCount} rings, {layer.VertexCount:N0} vertices");
            Line($"  extents     {layer.Bounds}");
            Line($"  time        {elapsed.TotalMilliseconds:F1} ms");

            foreach (var note in layer.Notes)
            {
                Line($"  note        {note}");
            }

            if (svgOut is not null)
            {
                // Looking at the geometry is the only way to catch a whole class of error that
                // every count and area agrees with: a hole filled solid, a thermal missing a
                // quadrant, a macro primitive rotated about the wrong centre.
                var artwork = PolygonArtwork.ToArtwork(
                    layer, "copper", image.FileFunction ?? "Layer", ArtRole.Fill, Path.GetFileName(file));

                if (artwork.Layers.Count > 0)
                {
                    var target = files.Count == 1
                        ? svgOut
                        : Path.Combine(svgOut, Path.ChangeExtension(Path.GetFileName(file), ".svg"));

                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
                    SvgWriter.WriteFile(
                        target,
                        artwork,
                        SvgPage.ForContent(artwork.ContentBounds, Nm.FromMillimetres(2)),
                        new SvgExportOptions { Title = Path.GetFileNameWithoutExtension(file) });

                    Line($"  svg         {target}");
                }
            }

            if (layer.Area.Count == 0 && image.Objects.Count > 0)
            {
                Console.Error.WriteLine("  WARNING: parsed objects produced no area.");
                anyErrors = true;
            }
        }

        return anyErrors ? 2 : 0;
    }

    /// <summary>
    /// Load a whole export folder the way the window does: detect each file's role, parse it,
    /// realise its geometry, and optionally render the result to a PNG.
    ///
    /// The PNG matters more than it looks. It drives the real <c>BoardScene</c> and
    /// <c>BoardRenderer</c> — the same code the viewport uses — so the entire view stack is
    /// checkable from a terminal, in CI, without launching a window. A board that loads with
    /// perfect counts and renders as an empty rectangle is a real outcome, and no count catches it.
    /// </summary>
    private static int LoadBoard(string[] args)
    {
        var folder = args[1];
        string? png = null;
        var width = 1400;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--png" when i + 1 < args.Length:
                    png = args[++i];
                    break;

                case "--width" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
                        || width < 64 || width > 8192)
                    {
                        Console.Error.WriteLine("--width needs a pixel count between 64 and 8192.");
                        return 1;
                    }

                    break;

                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}'.");
                    return 1;
            }
        }

        Board board;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            board = BoardLoader.LoadFolder(folder);
        }
        catch (DirectoryNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

        if (board.Layers.Count == 0)
        {
            Console.Error.WriteLine($"No board files found in '{folder}'.");
            return 1;
        }

        Console.WriteLine(board.Source);
        Line($"  layers      {board.Layers.Count}, {board.TotalObjects} objects, {board.TotalRings} rings");
        Line($"  extents     {board.Bounds}");
        Line($"  load        {elapsed.TotalMilliseconds:F0} ms");
        Console.WriteLine();

        foreach (var layer in board.InDrawOrder())
        {
            var guessed = layer.RoleGuessed ? " (guessed from the filename)" : "";
            var negative = layer.DeclaredNegative ? " [negative]" : "";
            Line($"  {layer.Label,-18} {layer.FileName}{guessed}{negative}");
            Line($"  {"",-18} {layer.ObjectCount} objects, {layer.RingCount} rings, {layer.AreaMm2:F3} mm^2");

            foreach (var d in layer.Diagnostics.Where(d => d.IsError).Take(3))
            {
                Console.Error.WriteLine($"  {"",-18} ERROR {d}");
            }
        }

        if (board.Failures.Count > 0)
        {
            Console.WriteLine();
            foreach (var failure in board.Failures)
            {
                Console.Error.WriteLine($"  FAILED  {failure}");
            }
        }

        if (!board.Layers.Any(l => l.Role == LayerRole.Outline))
        {
            Console.WriteLine();
            Console.WriteLine("  note: no board outline found, so extents come from the drawn geometry.");
        }

        if (png is not null)
        {
            RenderBoardPng(board, png, width);
            Console.WriteLine();
            Line($"  png         {png}");
        }

        return board.HasErrors ? 2 : 0;
    }

    private static void RenderBoardPng(Board board, string path, int width)
    {
        using var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            board.Bounds);

        var aspect = scene.Bounds.Height <= 0 ? 1f : scene.Bounds.Height / scene.Bounds.Width;
        var height = Math.Clamp((int)Math.Round(width * aspect), 64, 8192);

        var viewport = new SKRect(0, 0, width, height);
        var view = BoardSceneBuilder.FitTo(scene.Bounds, viewport);

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        BoardRenderer.Draw(surface.Canvas, viewport, scene, view, BoardPalette.Background, BoardPalette.Grid);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var file = File.OpenWrite(path);
        data.SaveTo(file);
    }

    /// <summary>
    /// Project commands: build one from a folder, inspect it, and refresh it after a re-export.
    ///
    /// The refresh in particular belongs in the CLI as well as the window. It is the operation
    /// people will want in a script — "re-export from KiCad, pull it into the project, regenerate"
    /// — and having it here means the whole feature is checkable from a terminal.
    /// </summary>
    private static int ProjectCommand(string[] args)
    {
        var verb = args[1].ToLowerInvariant();

        return verb switch
        {
            "save" when args.Length >= 3 => ProjectSave(args),
            "info" when args.Length >= 3 => ProjectInfo(args[2]),
            "refresh" when args.Length >= 3 => ProjectRefreshCommand(args),
            _ => UnknownProjectVerb(verb),
        };
    }

    private static int UnknownProjectVerb(string verb)
    {
        Console.Error.WriteLine($"Unknown project command '{verb}'. Use save, info or refresh.");
        return 1;
    }

    private static int ProjectSave(string[] args)
    {
        var folder = args[2];
        var output = Argument(args, "-o") ?? Argument(args, "--out")
            ?? Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(folder))!,
                Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) + ProjectFile.Extension);

        MillBurnProject project;
        try
        {
            project = MillBurnProject.FromSources(ProjectFile.ImportFolder(folder), Path.GetFullPath(folder));
        }
        catch (DirectoryNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (project.Sources.Length == 0)
        {
            Console.Error.WriteLine($"No Gerber or drill files in '{folder}'.");
            return 1;
        }

        ProjectFile.Save(project, output);
        Console.WriteLine(output);
        Line($"  sources     {project.Sources.Length} files embedded");
        Line($"  origin      {project.OriginFolder}");
        return 0;
    }

    private static int ProjectInfo(string path)
    {
        MillBurnProject project;
        try
        {
            project = ProjectFile.Open(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            Console.Error.WriteLine($"Could not open '{path}': {ex.Message}");
            return 1;
        }

        var board = ProjectFile.ToBoard(project);

        Console.WriteLine(path);
        Line($"  format      {project.SchemaVersion}");
        Line($"  origin      {project.OriginFolder ?? "(none recorded)"}");
        Line($"  sources     {project.Sources.Length} files, {board.TotalObjects} objects, {board.TotalRings} rings");
        Line($"  extents     {board.Bounds}");
        Console.WriteLine();

        foreach (var source in project.Sources.OrderBy(s => LayerRoleInfo.DrawOrder(s.Role)).ThenBy(s => s.FileName, StringComparer.Ordinal))
        {
            var overridden = source.RoleOverridden ? " (role set by hand)" : "";
            Line($"  {LayerRoleInfo.Label(source.Role),-18} {source.FileName}{overridden}");
            Line($"  {"",-18} content {source.ContentHash[..12]}  geometry {source.GeometryHash[..12]}");
        }

        return 0;
    }

    private static int ProjectRefreshCommand(string[] args)
    {
        var path = args[2];
        var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);

        MillBurnProject project;
        try
        {
            project = ProjectFile.Open(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            Console.Error.WriteLine($"Could not open '{path}': {ex.Message}");
            return 1;
        }

        var folder = Argument(args, "--from") ?? project.OriginFolder;
        if (folder is null || !Directory.Exists(folder))
        {
            Console.Error.WriteLine(
                folder is null
                    ? "This project records no source folder; pass --from <folder>."
                    : $"The source folder '{folder}' is not there any more; pass --from <folder>.");
            return 1;
        }

        var plan = ProjectRefresh.Inspect(project, folder);

        Console.WriteLine(path);
        Line($"  source      {folder}");
        Line($"  result      {plan.Summary()}");

        if (plan.HasChanges)
        {
            Console.WriteLine();
            foreach (var change in plan.Actionable)
            {
                Line($"  {change.Kind,-12} {change.FileName}");
                foreach (var detail in change.Details)
                {
                    Line($"  {"",-12} {detail}");
                }
            }
        }

        if (!apply)
        {
            if (plan.HasChanges)
            {
                Console.WriteLine();
                Console.WriteLine("  Nothing applied. Re-run with --apply to take these.");
            }

            return 0;
        }

        if (!plan.HasChanges)
        {
            return 0;
        }

        ProjectRefresh.Apply(project, plan, plan.Actionable.Select(c => c.FileName));
        ProjectFile.Save(project, path);

        Console.WriteLine();
        Line($"  applied     {plan.Actionable.Count()} file(s) and saved.");
        return 0;
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>
    /// How many drawable elements the file holds.
    ///
    /// Worth printing, because it is the number that decides whether a laser program will make one
    /// cut layer or four hundred, and there is no way to tell by looking at the picture.
    /// </summary>
    private static int CountElements(string path) =>
        System.Text.RegularExpressions.Regex.Count(File.ReadAllText(path), "<path ");

    private static int CountGroups(string path) =>
        System.Text.RegularExpressions.Regex.Count(File.ReadAllText(path), "<g ");

    private static void Line(FormattableString text) =>
        Console.WriteLine(FormattableString.Invariant(text));
}
