using System.Globalization;
using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Export;
using MillBurn.Gcode;
using MillBurn.Geometry;
using MillBurn.Optimize;

namespace MillBurn.Pipeline;

/// <summary>One file that will be written.</summary>
public sealed record ExportItem
{
    public required string LayerFileName { get; init; }

    public required string LayerLabel { get; init; }

    public required LayerRole Role { get; init; }

    public required OperationKind Operation { get; init; }

    public required OutputKind Output { get; init; }

    /// <summary>Name only, no directory: the user picks the folder.</summary>
    public required string TargetName { get; init; }

    public required string Content { get; init; }

    /// <summary>Facts worth seeing before writing it — sizes, counts, distances.</summary>
    public IReadOnlyList<string> Summary { get; init; } = [];

    /// <summary>Things that would spoil the result. Never a reason to refuse, always to show.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public int Bytes => System.Text.Encoding.UTF8.GetByteCount(Content);
}

/// <summary>Everything a single Export would write.</summary>
public sealed record ExportPlan
{
    public required IReadOnlyList<ExportItem> Items { get; init; }

    public IReadOnlyList<string> Skipped { get; init; } = [];

    public int Count => Items.Count;

    public bool HasWarnings => Items.Any(i => i.Warnings.Count > 0);
}

/// <summary>
/// Turns a board plus per-layer settings into the set of files to write.
///
/// Planning and writing are separate on purpose. A CAM tool that writes first and reports after
/// gives the operator nothing to check, and the check is the point: which layer became which file,
/// what tool it assumes, how deep it goes, and whether anything about the combination is wrong.
/// </summary>
public static class ExportPlanner
{
    public static ExportPlan Plan(
        Board board,
        IReadOnlyDictionary<string, LayerOutputSettings> settings,
        ToolLibrary library,
        long boardThicknessNm,
        OutputKind? only = null,
        MachineProfile? machine = null,
        RouteEffort effort = RouteEffort.Balanced)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);

        var items = new List<ExportItem>();
        var skipped = new List<string>();

        // Every export in one run shares a page, so the layers overlay when imported. Cropping each
        // to its own extents is the mistake that puts the second burn out by the difference.
        var page = board.Bounds.IsEmpty
            ? null
            : SvgPage.ForContent(board.Bounds, Nm.FromMillimetres(2));

        foreach (var layer in board.InDrawOrder())
        {
            if (!settings.TryGetValue(layer.FileName, out var setting) || setting.Output == OutputKind.None)
            {
                continue;
            }

            if (only is { } wanted && setting.Output != wanted)
            {
                continue;
            }

            var operation = LayerOperations.For(layer.Role, setting.Output);
            if (operation == OperationKind.None)
            {
                skipped.Add(Invariant(
                    $"{layer.FileName}: {LayerRoleInfo.Label(layer.Role)} cannot be exported as {setting.Output}."));
                continue;
            }

            var item = setting.Output == OutputKind.Svg
                ? PlanSvg(board, layer, setting, operation, page)
                : PlanGcode(board, layer, setting, operation, library, boardThicknessNm, machine, effort);

            if (item is null)
            {
                skipped.Add(Invariant($"{layer.FileName}: nothing to cut."));
                continue;
            }

            items.Add(item);
        }

        return new ExportPlan { Items = items, Skipped = skipped };
    }

    /// <summary>
    /// The exported name: the layer's own stem, and the extension for what it is.
    ///
    /// Keeping the stem means the output sorts next to the file it came from, and <c>.nc</c> or
    /// <c>.svg</c> already says which machine wants it. An operation tag in the middle was tried
    /// and is not worth the noise: a layer produces one output, so there is nothing for the tag to
    /// disambiguate.
    /// </summary>
    public static string TargetNameFor(string layerFileName, OperationKind operation, OutputKind output)
    {
        _ = operation;

        return Path.GetFileNameWithoutExtension(layerFileName)
            + (output == OutputKind.Svg ? ".svg" : ".nc");
    }

    // ------------------------------------------------------------------ SVG

    private static ExportItem? PlanSvg(
        Board board, BoardLayer layer, LayerOutputSettings setting, OperationKind operation, SvgPage? page)
    {
        if (page is null || layer.Area.Count == 0)
        {
            return null;
        }

        // The same axis the G-code uses, so a layer sent to both machines lands the same way round
        // in both. Reflecting about the board's own centreline also keeps the drawing on the shared
        // page, which is the whole reason the page exists.
        var mirrored = setting.MirrorFor(layer.Role);
        var area = mirrored
            ? MirrorX(layer.Area, board.Bounds.MinX + board.Bounds.MaxX)
            : layer.Area;

        // Inverted: everything inside the board except this layer.
        //
        // The board outline has to be in the drawing for this to mean anything — the complement of
        // a shape is unbounded until something bounds it, and what bounds it here is the edge of
        // the material. Without that the laser would be asked to clear an infinite plane.
        if (setting.Invert)
        {
            area = Polygons.Difference(BoardRegion(board, mirrored), area);
        }

        var artwork = PolygonArtwork.ToArtwork(
            new RealisedLayer
            {
                Area = area,
                Bounds = layer.Bounds,
                ObjectCount = layer.ObjectCount,
                PolarityRuns = 1,
                DeclaredNegative = layer.DeclaredNegative,
                Notes = layer.Notes,
            },
            layer.FileName,
            layer.Label,
            ArtRole.Fill,
            layer.FileName);

        // Single layer by default: some laser software makes one of its own cut layers per imported
        // object, which turns a board into hundreds of them.
        var svg = SvgWriter.Write(artwork, page, new SvgExportOptions { SingleLayer = true });

        var summary = new List<string>
        {
            Invariant($"{page.WidthMm:F2} × {page.HeightMm:F2} mm page, shared by every layer in this export"),
            Invariant($"{layer.RingCount} shapes, {layer.AreaMm2:F2} mm²"),
        };

        if (mirrored)
        {
            summary.Add("Mirrored · for work done on the flipped board");
        }

        if (setting.Invert)
        {
            summary.Add("Inverted · everything inside the board edge except this layer");
        }

        var warnings = new List<string>();
        if (layer.DeclaredNegative)
        {
            warnings.Add("This layer is negative: the shapes are the openings, not the material.");
        }

        warnings.AddRange(MirrorWarnings(layer.Role, setting));

        return new ExportItem
        {
            LayerFileName = layer.FileName,
            LayerLabel = layer.Label,
            Role = layer.Role,
            Operation = operation,
            Output = OutputKind.Svg,
            TargetName = TargetNameFor(layer.FileName, operation, OutputKind.Svg),
            Content = svg,
            Summary = summary,
            Warnings = warnings,
        };
    }

    // ------------------------------------------------------------------ G-code

    private static ExportItem? PlanGcode(
        Board board,
        BoardLayer layer,
        LayerOutputSettings setting,
        OperationKind operation,
        ToolLibrary library,
        long boardThicknessNm,
        MachineProfile? machine,
        RouteEffort effort)
    {
        var tool = ResolveTool(setting, operation, library);
        var warnings = new List<string>();
        var summary = new List<string>();

        Toolpath? toolpath = operation switch
        {
            OperationKind.Isolation => BuildIsolation(layer, setting, tool, summary, warnings),
            OperationKind.Drilling => BuildDrilling(layer, setting, tool, boardThicknessNm, summary),
            OperationKind.Outline => BuildOutline(layer, setting, tool, boardThicknessNm, summary, warnings),
            OperationKind.Engrave => BuildEngrave(layer, setting, tool, summary),
            OperationKind.Pocket => BuildPocket(layer, setting, tool, summary, warnings),
            _ => null,
        };

        if (toolpath is null || (toolpath.Passes.Count == 0 && toolpath.Drills.Count == 0))
        {
            return null;
        }

        // Each file is referenced to the board's own corner, so every one of them shares a work
        // zero the operator can actually touch off on.
        var shift = board.Bounds.IsEmpty
            ? Point2.Origin
            : new Point2(-board.Bounds.MinX, -board.Bounds.MinY);

        // A bottom-side layer is drawn as seen through the board, so cutting it as-is produces a
        // mirror image. The flip is baked in here rather than left to the operator, and the file
        // says which way the stock must be turned — a program that is silently the wrong hand
        // looks completely correct on screen and scraps the board.
        var notes = new List<string> { OriginNote(board) };

        // The flip comes first, so the route is optimised for the geometry that will actually be
        // cut rather than for its mirror image.
        if (setting.MirrorFor(layer.Role))
        {
            toolpath = MirrorX(toolpath, board.Bounds.MinX + board.Bounds.MaxX);
            notes.Add(FlipNote());
            summary.Add("Mirrored · flip the stock left-to-right");
            warnings.Add(
                "Mirrored: the stock must be flipped left-to-right about its vertical centreline, "
                + "and re-registered. Flipping it the other way cuts a mirror image.");
        }

        warnings.AddRange(MirrorWarnings(layer.Role, setting));

        // Ordering runs in board coordinates, before the shift to the corner: a translation cannot
        // change which order is shortest.
        //
        // It starts from the board's own lower-left corner, because that is where the tool is when
        // the program begins. Starting from the coordinate origin instead adds a lead-in from
        // wherever the board happened to sit on the EDA canvas — 175 mm of it for PogoTest1 — which
        // both skews the first choice and swamps the reported saving with a move that is not real.
        var start = board.Bounds.IsEmpty
            ? Point2.Origin
            : new Point2(board.Bounds.MinX, board.Bounds.MinY);

        // The emitter parks back at work zero when it finishes, so the route is a closed tour.
        // On PogoTest1 that last hop was 35 mm of a 79 mm total — nearly half the rapid in the
        // file, and entirely invisible to an optimizer that stops at the last cut.
        var (ordered, route) = ToolpathRouter.Order(toolpath, start, machine, effort, start);

        var job = new Job
        {
            Name = Path.GetFileNameWithoutExtension(layer.FileName) + " — " + LayerOperations.Label(operation),
            Toolpaths = [Translate(ordered, shift)],
            OriginShift = shift,
            Notes = notes,
        };

        var (text, stats) = GcodeEmitter.Emit(job, new GcodeOptions());
        var measured = GcodeBackplot.Measure(GcodeBackplot.Classify(GcodeParser.Parse(text)));

        // Drilling has no lateral cutting distance, so reporting "0 mm cutting" for it reads as a
        // failure rather than as the shape of the operation.
        summary.Add(operation == OperationKind.Drilling
            ? Invariant($"{measured.PlungeCount} plunges, {measured.TravelMm:F0} mm travel")
            : Invariant($"{stats.CutLengthMm:F0} mm cutting, {measured.TravelMm:F0} mm travel"));
        summary.Add(Invariant($"{measured.TimeRange()} · {stats.Lines:N0} lines"));

        // Shown because a claim that the optimizer helps is worth nothing unless the size of the
        // help is visible on the job it helped (Documentation/03, section 6).
        if (route.InitialTravelMm > 0 && route.TravelSavedFraction > 0.005)
        {
            summary.Add(Invariant(
                $"Ordering: {route.InitialTravelMm:F0} mm rapid → {route.TravelMm:F0} mm ({route.TravelSavedFraction:P0} less)"));
        }

        if (measured.GougeCount > 0)
        {
            warnings.Add(Invariant($"{measured.GougeCount} rapid move(s) at cutting depth. Do not run this."));
        }

        return new ExportItem
        {
            LayerFileName = layer.FileName,
            LayerLabel = layer.Label,
            Role = layer.Role,
            Operation = operation,
            Output = OutputKind.Gcode,
            TargetName = TargetNameFor(layer.FileName, operation, OutputKind.Gcode),
            Content = text,
            Summary = summary,
            Warnings = warnings,
        };
    }

    private static Toolpath BuildIsolation(
        BoardLayer layer, LayerOutputSettings setting, Tool tool, List<string> summary, List<string> warnings)
    {
        var options = new IsolationOptions
        {
            Tool = tool,
            DepthNm = setting.DepthFor(OperationKind.Isolation),
            Passes = setting.Passes,
        };

        var width = Nm.ToMillimetreString(options.EffectiveWidthNm, 3);
        var depth = Nm.ToMillimetreString(options.DepthNm, 3);
        var passLabel = setting.Passes == 1 ? "1 pass" : Invariant($"{setting.Passes} passes");
        summary.Add(Invariant($"{width} mm wide at {depth} mm deep · {passLabel}"));

        var unreachable = IsolationOperation.UnreachableGaps(layer.Area, options);
        if (unreachable > 0)
        {
            warnings.Add(Invariant(
                $"{unreachable} gap(s) are narrower than the cut: those copper regions stay connected."));
        }

        if (tool.Kind == ToolKind.EndMill)
        {
            warnings.Add("An end mill cuts one width everywhere and cannot separate anything closer than itself.");
        }

        return IsolationOperation.Build(layer.Area, options, layer.Label);
    }

    private static Toolpath? BuildDrilling(
        BoardLayer layer, LayerOutputSettings setting, Tool tool, long thicknessNm, List<string> summary)
    {
        if (layer.Drill is null)
        {
            return null;
        }

        var options = new DrillOptions
        {
            BoardThicknessNm = thicknessNm,
            BreakThroughNm = setting.BreakThroughNm,
        };

        var depth = Nm.ToMillimetreString(options.DepthNm, 2);
        var through = Nm.ToMillimetreString(setting.BreakThroughNm, 2);
        var sizes = layer.Drill.Tools.Count == 1 ? "1 size" : Invariant($"{layer.Drill.Tools.Count} sizes");
        summary.Add(Invariant(
            $"{layer.Drill.Hits.Count} holes in {sizes} · {depth} mm deep ({through} mm through the back)"));

        var paths = DrillOperation.Build(layer.Drill, options, tool);

        // One file per layer, so the sizes inside it become tool changes rather than more files.
        return paths.Count == 0
            ? null
            : paths.Aggregate((a, b) => a with
            {
                Label = layer.Label,
                Drills = [.. a.Drills, .. b.Drills],
            });
    }

    private static Toolpath BuildOutline(
        BoardLayer layer,
        LayerOutputSettings setting,
        Tool tool,
        long thicknessNm,
        List<string> summary,
        List<string> warnings)
    {
        var options = new OutlineOptions
        {
            Tool = tool,
            BoardThicknessNm = thicknessNm,
            BreakThroughNm = setting.BreakThroughNm,
            TabCount = setting.TabCount,
            DepthPerPassNm = tool.StepdownNm > 0 ? tool.StepdownNm : Nm.FromMillimetres(0.4),
        };

        var cutter = Nm.ToMillimetreString(tool.DiameterNm, 2);
        var total = Nm.ToMillimetreString(options.TotalDepthNm, 2);
        var perPass = Nm.ToMillimetreString(options.DepthPerPassNm, 2);
        summary.Add(Invariant(
            $"{cutter} mm cutter outside the profile · {total} mm deep in {perPass} mm passes"));

        if (setting.TabCount == 0)
        {
            warnings.Add("No tabs: the board comes free on the last pass and will be thrown by the cutter.");
        }

        if (tool.Kind == ToolKind.VBit)
        {
            warnings.Add("A V-bit at full depth is enormously wide at the surface. Use a flat end mill.");
        }

        // Every profile, not just the biggest one.
        //
        // A stroked Edge_Cuts realises as an annulus per outline: a positive ring around the
        // outside of the pen and a negative one inside it. Taking only the largest positive ring is
        // right for a single board and silently wrong for everything else — on a fifty-up panel it
        // cut the frame and left all fifty boards attached, and it would drop an interior slot the
        // same way. The negative rings are the inside of the pen stroke rather than real cutouts,
        // so they are not profiles and are left alone.
        var profiles = new Paths64(layer.Area.Where(r => Clipper.Area(r) > 0));

        if (profiles.Count == 0)
        {
            warnings.Add("The outline layer has no closed profile to cut.");
            return OutlineOperation.Build(profiles, options, layer.Label);
        }

        if (profiles.Count > 1)
        {
            summary.Add(Invariant(
                $"{profiles.Count} profiles · inner pieces cut before the frame around them"));
        }

        return OutlineOperation.Build(profiles, options, layer.Label);
    }

    /// <summary>
    /// Milling the applied soldermask off the pads, using the paste apertures as the areas to clear.
    ///
    /// The warnings are the point of this one. Cured mask is 20–40 µm, which is less than the
    /// flatness of a typical piece of copper-clad over even a small board — so the same program
    /// that leaves mask on one pad cuts into the copper of another, and neither shows up until the
    /// board is under a light.
    /// </summary>
    private static Toolpath BuildPocket(
        BoardLayer layer,
        LayerOutputSettings setting,
        Tool tool,
        List<string> summary,
        List<string> warnings)
    {
        var options = new PocketOptions { Tool = tool, DepthNm = setting.DepthFor(OperationKind.Pocket) };

        var depth = Nm.ToMillimetreString(options.DepthNm, 3);
        var width = Nm.ToMillimetreString(options.EffectiveWidthNm, 3);
        summary.Add(Invariant($"{layer.RingCount} openings cleared {width} mm per pass at {depth} mm deep"));

        warnings.Add(
            Invariant($"Mask relief cuts {depth} mm deep. ")
            + "Cured soldermask is about 0.02-0.04 mm, so the board's own flatness is the whole "
            + "depth of this cut: level the stock and probe a height map, or expect bare copper in "
            + "one place and mask left in another.");

        var unreachable = PocketOperation.UnreachableOpenings(layer.Area, options);
        if (unreachable > 0)
        {
            warnings.Add(Invariant(
                $"{unreachable} opening(s) are smaller than the tool cuts and will keep their mask."));
        }

        return PocketOperation.Build(layer.Area, options, layer.Label);
    }

    /// <summary>
    /// Engraving silk on the mill: trace the stroke centrelines, exactly as the laser path does.
    ///
    /// The same observation makes both work — silk is drawn at about the width the tool cuts — so
    /// the geometry is the Gerber's own segments either way.
    /// </summary>
    private static Toolpath BuildEngrave(
        BoardLayer layer, LayerOutputSettings setting, Tool tool, List<string> summary)
    {
        var contours = Clipper.InflatePaths(
            layer.Area, -tool.WidthAtDepth(setting.DepthFor(OperationKind.Engrave)) / 2, JoinType.Round, EndType.Polygon);

        var passes = new List<ToolpathPass>();
        foreach (var contour in contours.Where(c => c.Count >= 3))
        {
            passes.Add(new ToolpathPass
            {
                Path = IsolationOperation.ToSegments(contour),
                DepthNm = setting.DepthFor(OperationKind.Engrave),
                Closed = true,
            });
        }

        var engraveDepth = Nm.ToMillimetreString(setting.DepthFor(OperationKind.Engrave), 3);
        summary.Add(Invariant($"{passes.Count} strokes at {engraveDepth} mm deep"));

        return new Toolpath
        {
            Kind = ToolpathKind.Mark,
            Label = layer.Label,
            Tool = tool,
            Passes = passes,
        };
    }

    // ------------------------------------------------------------------ helpers

    private static Tool ResolveTool(LayerOutputSettings setting, OperationKind operation, ToolLibrary library)
    {
        if (setting.ToolId is { } id && library.Tools.FirstOrDefault(t => t.Id == id) is { } chosen)
        {
            return chosen;
        }

        return operation switch
        {
            OperationKind.Outline => Tool.DefaultOutlineMill,
            OperationKind.Drilling => Tool.DefaultDrill,
            _ => Tool.DefaultVBit,
        };
    }


    /// <summary>
    /// Says something only when the mirror setting is not the one the layer's side implies.
    ///
    /// Both overrides are legitimate and both are dangerous, and neither is visible in the
    /// resulting file — so the warning appears exactly when someone has departed from the default,
    /// and stays silent the rest of the time.
    /// </summary>
    private static IEnumerable<string> MirrorWarnings(LayerRole role, LayerOutputSettings setting)
    {
        var chosen = setting.MirrorFor(role);
        if (chosen == LayerOperations.MirrorByDefault(role))
        {
            yield break;
        }

        yield return chosen
            ? $"{LayerRoleInfo.Label(role)} is a top-side layer but is set to mirror. It will only "
                + "fit if the stock is flipped."
            : $"{LayerRoleInfo.Label(role)} is a bottom-side layer but is set not to mirror. It "
                + "will come out reversed unless you are working from the other face.";
    }

    /// <summary>
    /// The board itself, as a filled region, for bounding an inverted layer.
    ///
    /// The outline layer when there is one, because a board is rarely a rectangle and burning the
    /// bounding box would clear resist off the stock outside the board. Falls back to the extents
    /// when there is no outline, which is the same fallback the viewer makes and is stated as a
    /// warning rather than assumed.
    /// </summary>
    private static Paths64 BoardRegion(Board board, bool mirrored)
    {
        var outline = board.Layers.FirstOrDefault(l => l.Role == LayerRole.Outline);

        var region = outline is null || outline.Area.Count == 0
            ? new Paths64 { Polygons.Rectangle(board.Bounds) }
            : new Paths64(outline.Area.Where(r => Clipper.Area(r) > 0));

        // Filling the outline's own strokes closes the annulus, so the region is the whole board
        // rather than a ring around its edge.
        region = Polygons.UnionSelf(region);

        return mirrored ? MirrorX(region, board.Bounds.MinX + board.Bounds.MaxX) : region;
    }

    /// <summary>Reflects realised geometry in the same vertical line the toolpaths use.</summary>
    private static Paths64 MirrorX(Paths64 area, long sumX)
    {
        var mirrored = new Paths64(area.Count);

        foreach (var ring in area)
        {
            var flipped = new Path64(ring.Count);
            foreach (var point in ring)
            {
                flipped.Add(new Clipper2Lib.Point64(sumX - point.X, point.Y));
            }

            // Point order is left alone. A reflection reverses every ring's winding, so outers and
            // holes keep their relative orientation and the non-zero fill still resolves.
            mirrored.Add(flipped);
        }

        return mirrored;
    }

    /// <summary>
    /// Reflects a toolpath in the vertical line <c>x = sumX / 2</c>.
    ///
    /// Taking the sum of the board's own extents as the axis keeps the result in exactly the same
    /// bounding box, so work zero stays the board's lower-left corner in both setups — which is the
    /// corner the operator can still see and touch off on after the stock is turned over.
    ///
    /// Arc sweeps have to be flipped with the points. A reflection reverses handedness, so a
    /// clockwise arc becomes counter-clockwise; leaving the sweep alone would emit a G2 that takes
    /// the long way round the circle, which is a real cut through the middle of the board.
    /// </summary>
    private static Toolpath MirrorX(Toolpath toolpath, long sumX)
    {
        Point2 Flip(Point2 p) => new(sumX - p.X, p.Y);

        ArtSweep Reverse(ArtSweep sweep) => sweep switch
        {
            ArtSweep.Clockwise => ArtSweep.CounterClockwise,
            ArtSweep.CounterClockwise => ArtSweep.Clockwise,
            _ => ArtSweep.Linear,
        };

        return toolpath with
        {
            Passes = [.. toolpath.Passes.Select(p => p with
            {
                Path = [.. p.Path.Select(s =>
                    new ArtSegment(Reverse(s.Sweep), Flip(s.From), Flip(s.To), Flip(s.Centre)))],
            })],
            Drills = [.. toolpath.Drills.Select(d => d with { At = Flip(d.At) })],
        };
    }

    private static Toolpath Translate(Toolpath toolpath, Point2 by) => toolpath with
    {
        Passes = [.. toolpath.Passes.Select(p => p with
        {
            Path = [.. p.Path.Select(s => new ArtSegment(s.Sweep, s.From + by, s.To + by, s.Centre + by))],
        })],
        Drills = [.. toolpath.Drills.Select(d => d with { At = d.At + by })],
    };

    private static Path64 LargestRing(Paths64 area)
    {
        Path64? largest = null;
        var largestArea = 0.0;

        foreach (var ring in area)
        {
            var size = Math.Abs(Clipper.Area(ring));
            if (size > largestArea)
            {
                largestArea = size;
                largest = ring;
            }
        }

        return largest ?? [];
    }

    /// <summary>
    /// What the operator has to do to the stock before running a bottom-side file.
    ///
    /// In the file, because these are handed to the machine one at a time and the flip is the one
    /// step that cannot be recovered from once the cut has started.
    /// </summary>
    private static string FlipNote() =>
        "BOTTOM SIDE. Flip the stock left-to-right about its vertical centreline, then re-register. "
        + "Coordinates below are already mirrored for that flip.";

    /// <summary>
    /// Where work zero is, in every file this export writes.
    ///
    /// Repeated into each one because they are handed to the machine separately, and a file that
    /// does not say what its origin means is a file someone will run against the wrong zero.
    /// </summary>
    private static string OriginNote(Board board)
    {
        var x = Nm.ToMillimetreString(board.Bounds.MinX, 3);
        var y = Nm.ToMillimetreString(board.Bounds.MinY, 3);
        return Invariant($"Work zero is the board's lower-left corner; the Gerber origin was at {x}, {y} mm.");
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
