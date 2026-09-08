using System.Globalization;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Export;
using MillBurn.Gerber;
using MillBurn.Gerber.Excellon;
using MillBurn.Gerber.Model;
using MillBurn.Pipeline;
using MillBurn.Gcode;
using MillBurn.Optimize;
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
            Console.WriteLine("  export <folder-or-project>     One file per layer: --only svg|gcode, --write, -o <dir>");
            Console.WriteLine("                                 --set <layer>=svg|svg-|gcode|none  (svg- inverts)");
            Console.WriteLine("                                 --set <layer>=<svg|gcode|none> overrides one layer");
            Console.WriteLine("  tools [list|add|remove|path]   Manage the saved tool library");
            Console.WriteLine("  mill <folder-or-project>       Gerber to G-code: isolate, drill, cut out");
            Console.WriteLine("                                 --isolation-tool <name> --outline-tool <name> pick from the library");
            Console.WriteLine("                                 --depth --passes --angle --tip --tool --tabs --thickness --bottom");
            Console.WriteLine("                                 --png <path> draws the emitted program over the board");
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
            "mill" when args.Length >= 2 => Mill(args),
            "export" when args.Length >= 2 => Export(args),
            "tools" => ToolsCommand(args),
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

    /// <summary>
    /// The value following an option, or null if it was not given.
    ///
    /// A blank value counts as not given. In a shell that is almost always an unexpanded variable —
    /// <c>-o "$OUT"</c> with <c>OUT</c> unset — and the alternative is an empty path reaching an IO
    /// call and coming back as a stack trace.
    /// </summary>
    private static string? Argument(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        var value = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Whether an option was written at all, whatever value it was given.</summary>
    private static bool HasOption(string[] args, params string[] names) =>
        args.Any(a => names.Contains(a, StringComparer.OrdinalIgnoreCase));

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

    /// <summary>
    /// The saved tool library: what this machine has bits for.
    ///
    /// A library rather than a set of flags because traces and edge cuts want genuinely different
    /// tools — a V-bit whose width follows depth for the one, a flat end mill for the other — and
    /// re-entering their geometry on every job is how the numbers drift apart from the bits in the
    /// drawer.
    /// </summary>
    private static int ToolsCommand(string[] args)
    {
        var verb = args.Length >= 2 ? args[1].ToLowerInvariant() : "list";
        var library = ToolLibrary.LoadOrDefault();

        switch (verb)
        {
            case "path":
                Console.WriteLine(ToolLibrary.DefaultPath);
                Console.WriteLine(File.Exists(ToolLibrary.DefaultPath)
                    ? "  (saved)"
                    : "  (not saved yet; the built-in set is in use)");
                return 0;

            case "list":
                foreach (var tool in library.Tools)
                {
                    Console.WriteLine(tool.Name);
                    Line($"  kind        {tool.Kind}");

                    if (tool.Kind == ToolKind.VBit)
                    {
                        var tip = Nm.ToMillimetreString(tool.TipNm, 3);
                        Line($"  geometry    {tool.IncludedAngleDegrees:F0}° included, {tip} mm tip");
                        Line($"  at 0.05 mm  cuts {Nm.ToMillimetreString(tool.WidthAtDepth(Nm.FromMillimetres(0.05)), 3)} mm wide");
                    }
                    else
                    {
                        Line($"  geometry    {Nm.ToMillimetreString(tool.DiameterNm, 3)} mm diameter");
                    }

                    Line($"  feeds       {tool.FeedMmPerMin} mm/min, plunge {tool.PlungeMmPerMin}, {tool.SpindleRpm} rpm");

                    if (tool.Notes is not null)
                    {
                        Line($"  note        {tool.Notes}");
                    }

                    Console.WriteLine();
                }

                return 0;

            case "add":
                return AddTool(args, library);

            case "remove" when args.Length >= 3:
                {
                    var found = library.Find(args[2]);
                    if (found is null)
                    {
                        Console.Error.WriteLine($"No tool matching '{args[2]}'.");
                        return 1;
                    }

                    library.Without(found.Id).Save();
                    Console.WriteLine($"Removed {found.Name}.");
                    return 0;
                }

            default:
                Console.Error.WriteLine($"Unknown tools command '{verb}'. Use list, add, remove or path.");
                return 1;
        }
    }

    private static int AddTool(string[] args, ToolLibrary library)
    {
        var name = Argument(args, "--name");
        var kindText = Argument(args, "--kind");

        if (name is null || kindText is null
            || !Enum.TryParse<ToolKind>(kindText, ignoreCase: true, out var kind))
        {
            Console.Error.WriteLine(
                "tools add --name <name> --kind vbit|endmill|drill " +
                "[--angle <deg> --tip <mm> --diameter <mm> --feed <mm/min> --plunge <mm/min> --rpm <n> --stepdown <mm> --maxdepth <mm>]");
            return 1;
        }

        double Number(string flag, double fallback) =>
            Argument(args, flag) is { } text
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        var tool = new Tool
        {
            Name = name,
            Kind = kind,
            DiameterNm = Nm.FromMillimetres(Number("--diameter", 1.0)),
            TipNm = Nm.FromMillimetres(Number("--tip", 0.1)),
            IncludedAngleDegrees = Number("--angle", 30),
            MaxDepthNm = Nm.FromMillimetres(Number("--maxdepth", 0)),
            StepdownNm = Nm.FromMillimetres(Number("--stepdown", 0)),
            FeedMmPerMin = (long)Number("--feed", 200),
            PlungeMmPerMin = (long)Number("--plunge", 60),
            SpindleRpm = (int)Number("--rpm", 12_000),
            Notes = Argument(args, "--notes"),
        };

        library.With(tool).Save();

        Console.WriteLine($"Added {tool.Name} to {ToolLibrary.DefaultPath}");

        if (kind == ToolKind.VBit)
        {
            Line($"  at 0.05 mm  cuts {Nm.ToMillimetreString(tool.WidthAtDepth(Nm.FromMillimetres(0.05)), 3)} mm wide");
        }

        return 0;
    }

    /// <summary>
    /// Gerber folder to G-code: isolate, drill, cut out.
    ///
    /// The report is the point as much as the file. Effective cut width, travel distance and the
    /// unreachable-gap count are the three numbers that decide whether a job is worth running, and
    /// none of them can be read off the G-code by eye.
    /// </summary>
    private static int Mill(string[] args)
    {
        var input = args[1];
        var output = Argument(args, "-o") ?? Argument(args, "--out");
        var depthMm = 0.05;
        var passes = 1;
        var angle = 30.0;
        var tipMm = 0.1;
        var toolMm = 1.0;
        var tabs = 4;
        var thicknessMm = 1.6;
        var side = BoardSide.Top;
        string? png = null;

        for (var i = 2; i < args.Length; i++)
        {
            var flag = args[i].ToLowerInvariant();
            var value = i + 1 < args.Length ? args[i + 1] : null;

            switch (flag)
            {
                case "-o" or "--out":
                    i++;
                    break;

                case "--depth" when value is not null:
                    if (!TryMm(value, out depthMm)) { return Bad(flag); }
                    i++;
                    break;

                case "--passes" when value is not null:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out passes) || passes < 1)
                    {
                        return Bad(flag);
                    }

                    i++;
                    break;

                case "--angle" when value is not null:
                    if (!TryMm(value, out angle)) { return Bad(flag); }
                    i++;
                    break;

                case "--tip" when value is not null:
                    if (!TryMm(value, out tipMm)) { return Bad(flag); }
                    i++;
                    break;

                case "--tool" when value is not null:
                    if (!TryMm(value, out toolMm)) { return Bad(flag); }
                    i++;
                    break;

                case "--tabs" when value is not null:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out tabs) || tabs < 0)
                    {
                        return Bad(flag);
                    }

                    i++;
                    break;

                case "--thickness" when value is not null:
                    if (!TryMm(value, out thicknessMm)) { return Bad(flag); }
                    i++;
                    break;

                case "--bottom":
                    side = BoardSide.Bottom;
                    break;

                // Read later by name, but still consumed here so the loop does not reject them.
                case "--isolation-tool" or "--outline-tool" or "--drill-tool" when value is not null:
                    i++;
                    break;

                case "--png" when value is not null:
                    png = value;
                    i++;
                    break;

                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}'.");
                    return 1;
            }
        }

        Board board;
        try
        {
            board = input.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)
                ? ProjectFile.ToBoard(ProjectFile.Open(input))
                : BoardLoader.LoadFolder(input);
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine($"Could not read '{input}': {ex.Message}");
            return 1;
        }

        if (board.Layers.Count == 0)
        {
            Console.Error.WriteLine($"No board files in '{input}'.");
            return 1;
        }

        // A named tool from the library wins; otherwise the geometry flags build one, which is
        // what makes the quick "try a 60 degree bit" case still work without saving anything.
        var library = ToolLibrary.LoadOrDefault();

        var isolationTool = Argument(args, "--isolation-tool") is { } isoName
            ? library.Find(isoName)
            : null;

        var outlineTool = Argument(args, "--outline-tool") is { } outName
            ? library.Find(outName)
            : null;

        foreach (var (flag, wanted) in new[]
        {
            ("--isolation-tool", isolationTool is null ? Argument(args, "--isolation-tool") : null),
            ("--outline-tool", outlineTool is null ? Argument(args, "--outline-tool") : null),
        })
        {
            if (wanted is not null)
            {
                Console.Error.WriteLine($"No tool matching '{wanted}' for {flag}. Try 'tools list'.");
                return 1;
            }
        }

        isolationTool ??= Tool.DefaultVBit with
        {
            TipNm = Nm.FromMillimetres(tipMm),
            IncludedAngleDegrees = angle,
            Name = FormattableString.Invariant($"{angle:F0}° V-bit, {tipMm:F2} mm tip"),
        };

        outlineTool ??= Tool.DefaultOutlineMill with { DiameterNm = Nm.FromMillimetres(toolMm) };

        var tool = isolationTool;

        var options = new MillOptions
        {
            Side = side,
            Tools = new ToolSelection
            {
                Isolation = isolationTool,
                Outline = outlineTool,
                Drill = library.Find("Drill") ?? Tool.DefaultDrill,
            },
            Isolation = new IsolationOptions
            {
                DepthNm = Nm.FromMillimetres(depthMm),
                Passes = passes,
            },
            Drill = new DrillOptions { BoardThicknessNm = Nm.FromMillimetres(thicknessMm) },
            Outline = new OutlineOptions
            {
                BoardThicknessNm = Nm.FromMillimetres(thicknessMm),
                TabCount = tabs,
            },
        };

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var job = JobBuilder.Build(board, options);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

        var (text, stats) = GcodeEmitter.Emit(job, new GcodeOptions());

        output ??= Path.Combine(
            Directory.Exists(input) ? input : Path.GetDirectoryName(Path.GetFullPath(input))!,
            (Directory.Exists(input)
                ? Path.GetFileName(input.TrimEnd(Path.DirectorySeparatorChar))
                : Path.GetFileNameWithoutExtension(input)) + ".nc");

        File.WriteAllText(output, text);

        var effective = isolationTool.WidthAtDepth(Nm.FromMillimetres(depthMm));

        Console.WriteLine(output);
        Line($"  isolation   {tool}");
        Line($"  outline     {outlineTool}");
        Line($"  effective   {Nm.ToMillimetreString(effective, 3)} mm wide at {depthMm:F3} mm deep");
        Line($"  built in    {elapsed.TotalMilliseconds:F0} ms");
        Console.WriteLine();

        var at = Point2.Origin;

        foreach (var toolpath in job.Toolpaths)
        {
            // Chained from the last operation's end, the same way the ordering was decided.
            var travel = toolpath.Drills.Count > 0
                ? NearestNeighbour.TravelMm(toolpath.Drills, at)
                : NearestNeighbour.TravelMm(toolpath.Passes, at);

            at = toolpath.Drills.Count > 0
                ? toolpath.Drills[^1].At
                : toolpath.Passes.Count > 0 ? toolpath.Passes[^1].End : at;

            Line($"  {toolpath.Label}");
            var summary = FormattableString.Invariant(
                $"{toolpath.PassCount} passes, {toolpath.Drills.Count} holes, {toolpath.CutLengthMm:F1} mm cut, {travel:F1} mm travel");
            Line($"  {"",-4}{summary}");

            foreach (var note in toolpath.Notes)
            {
                Line($"  {"",-4}{note}");
            }
        }

        Console.WriteLine();
        var totals = FormattableString.Invariant(
            $"{stats.Lines:N0} lines, {stats.CutLengthMm:F1} mm cutting, {stats.RapidLengthMm:F1} mm rapid, {stats.PlungeCount} plunges, {stats.ToolChanges} tool changes");
        Line($"  gcode       {totals}");

        // Read the file back and cost it. Parsing our own output rather than reporting the
        // in-memory job is the point: the two agree right up until the emitter has a bug, and only
        // one of them is what the machine will run.
        var program = GcodeParser.Parse(text);
        var backplot = GcodeBackplot.Classify(program);
        var measured = GcodeBackplot.Measure(backplot);

        Console.WriteLine();
        Line($"  backplot    {program.Moves.Count:N0} moves parsed back from the file");
        Line($"  distance    {measured.CutMm:F1} mm cutting, {measured.TravelMm:F1} mm travel, {measured.PlungeMm:F1} mm plunge");
        Line($"  time        {measured.TimeRange()} (no junction model yet; the truth is nearer the low end)");

        if (measured.LongTravelCount > 0)
        {
            Line($"  long rapids {measured.LongTravelCount} over 10 mm");
        }

        foreach (var layer in BackplotBuilder.Build(backplot, Undo(job.OriginShift)))
        {
            Line($"  layer       {layer.Id,-20} {layer.Runs.Count,5} runs, {layer.Runs.Sum(r => r.Count),7:N0} points");
        }

        var problems = job.Notes.Count;

        foreach (var note in job.Notes)
        {
            Console.Error.WriteLine($"  CHECK       {note}");
        }

        foreach (var diagnostic in program.Diagnostics.Where(d => d.IsError).Take(5))
        {
            Console.Error.WriteLine($"  CHECK       {diagnostic}");
            problems++;
        }

        if (measured.GougeCount > 0)
        {
            // A rapid below Z0 is the tool crossing the board at cutting depth. Never legitimate,
            // and caught here by reading the emitted file rather than by trusting the emitter.
            Console.Error.WriteLine($"  CHECK       {measured.GougeCount} rapid move(s) at cutting depth.");
            problems++;
        }

        if (png is not null)
        {
            RenderBackplotPng(board, BackplotBuilder.Build(backplot, Undo(job.OriginShift)), png, 1400);
            Line($"  png         {png}");
        }

        return problems > 0 ? 2 : 0;
    }

    /// <summary>
    /// The board with its program drawn over it.
    ///
    /// Over the board, not beside it: the question a backplot answers is "does this go where I
    /// meant", and that is only answerable with the copper underneath.
    /// </summary>
    private static void RenderBackplotPng(
        Board board, IReadOnlyList<BackplotLayer> backplot, string path, int width)
    {
        var sources = board.Layers
            .Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings()))
            .ToList();

        using var scene = BoardSceneBuilder.Build(sources, board.Bounds, backplot: backplot);

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

    /// <summary>Puts a corner-referenced job back into the board's own coordinates.</summary>
    private static Point2 Undo(Point2 shift) => new(-shift.X, -shift.Y);

    private static bool TryMm(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;

    private static int Bad(string flag)
    {
        Console.Error.WriteLine($"{flag} needs a positive number.");
        return 1;
    }

    /// <summary>
    /// One file per layer: isolation, drilling, cut-out and marking each go to their own program or
    /// drawing.
    ///
    /// A single file containing every operation assumes one operator watching one long run. Split
    /// per layer, a broken bit costs the drilling rather than the board, and the silkscreen can go
    /// to a laser while the outline goes to the mill — which is the whole point of the mixed
    /// workflows this exists for.
    /// </summary>
    private static int Export(string[] args)
    {
        var input = args[1];
        var outDir = Argument(args, "-o") ?? Argument(args, "--out");

        // Named an output folder and gave nothing: almost always an unexpanded shell variable.
        // Reported before any work is done, and never resolved to the folder the Gerbers came from
        // — which is the one place the output is hardest to tell apart from the design.
        if (outDir is null && HasOption(args, "-o", "--out"))
        {
            Console.Error.WriteLine("-o was given without a folder.");
            return 1;
        }

        var thicknessMm = 1.6;
        var write = args.Contains("--write", StringComparer.OrdinalIgnoreCase);
        OutputKind? only = null;

        if (Argument(args, "--only") is { } filter)
        {
            only = filter.ToLowerInvariant() switch
            {
                "svg" => OutputKind.Svg,
                "gcode" or "nc" => OutputKind.Gcode,
                _ => null,
            };

            if (only is null)
            {
                Console.Error.WriteLine("--only takes svg or gcode.");
                return 1;
            }
        }

        if (Argument(args, "--thickness") is { } thickness
            && double.TryParse(thickness, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            thicknessMm = parsed;
        }

        Board board;
        try
        {
            board = input.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)
                ? ProjectFile.ToBoard(ProjectFile.Open(input))
                : BoardLoader.LoadFolder(input);
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine($"Could not read '{input}': {ex.Message}");
            return 1;
        }

        // Defaults per role, which is what most boards want: cut the top copper, drill the holes,
        // cut the outline, and leave everything else alone.
        var settings = board.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        // Per-layer overrides, repeatable: --set F_Cu=svg --set Edge_Cuts=none. Matching on a
        // fragment because nobody wants to type "PogoTest1-F_Silkscreen.gbr" twice.
        for (var i = 2; i < args.Length - 1; i++)
        {
            if (!args[i].Equals("--set", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = args[i + 1].Split('=', 2);
            if (parts.Length != 2)
            {
                Console.Error.WriteLine($"--set wants <layer>=<svg|gcode|none>, got '{args[i + 1]}'.");
                return 1;
            }

            // "svg-" is the inverted form: everything inside the board edge except this layer,
            // which is what etching a painted board wants.
            var inverted = parts[1].EndsWith('-');
            var kind = parts[1].TrimEnd('-').ToLowerInvariant() switch
            {
                "svg" => OutputKind.Svg,
                "gcode" or "nc" => OutputKind.Gcode,
                "none" or "off" => OutputKind.None,
                _ => (OutputKind?)null,
            };

            if (kind is null)
            {
                Console.Error.WriteLine($"--set wants svg, svg-, gcode or none, got '{parts[1]}'.");
                return 1;
            }

            var matched = 0;
            foreach (var key in settings.Keys.Where(k => k.Contains(parts[0], StringComparison.OrdinalIgnoreCase)).ToList())
            {
                settings[key] = settings[key] with { Output = kind.Value, Invert = inverted };
                matched++;
            }

            if (matched == 0)
            {
                Console.Error.WriteLine($"No layer matching '{parts[0]}'.");
                return 1;
            }
        }

        var plan = ExportPlanner.Plan(
            board, settings, ToolLibrary.LoadOrDefault(), Nm.FromMillimetres(thicknessMm), only);

        Console.WriteLine(board.Source);
        Line($"  board       {Nm.ToMillimetreString(board.Bounds.Width, 2)} x {Nm.ToMillimetreString(board.Bounds.Height, 2)} mm");
        Line($"  files       {plan.Count}");
        Console.WriteLine();

        foreach (var item in plan.Items)
        {
            Line($"  {item.TargetName}");
            Line($"  {"",-4}{item.LayerLabel} · {LayerOperations.Label(item.Operation)} · {item.Bytes:N0} bytes");

            foreach (var line in item.Summary)
            {
                Line($"  {"",-4}{line}");
            }

            foreach (var warning in item.Warnings)
            {
                Console.Error.WriteLine($"  {"",-4}CHECK {warning}");
            }

            Console.WriteLine();
        }

        foreach (var skip in plan.Skipped)
        {
            Console.Error.WriteLine($"  skipped     {skip}");
        }

        if (!write)
        {
            Console.WriteLine("  Nothing written. Re-run with --write to produce these files.");
            return 0;
        }

        outDir ??= Directory.Exists(input) ? input : Path.GetDirectoryName(Path.GetFullPath(input))!;
        Directory.CreateDirectory(outDir);

        foreach (var item in plan.Items)
        {
            File.WriteAllText(Path.Combine(outDir, item.TargetName), item.Content);
        }

        Line($"  wrote       {plan.Count} file(s) to {outDir}");
        return plan.HasWarnings ? 2 : 0;
    }

    private static void Line(FormattableString text) =>
        Console.WriteLine(FormattableString.Invariant(text));
}
