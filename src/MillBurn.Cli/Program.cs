using System.Globalization;
using MillBurn.Core;
using MillBurn.Gerber;
using MillBurn.Gerber.Excellon;
using MillBurn.Gerber.Model;

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
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "inspect" when args.Length >= 2 => Inspect(args[1]),
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

    private static void Line(FormattableString text) =>
        Console.WriteLine(FormattableString.Invariant(text));
}
