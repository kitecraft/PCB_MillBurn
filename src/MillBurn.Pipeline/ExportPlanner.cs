using System.Globalization;
using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Export;
using MillBurn.Gcode;
using MillBurn.Geometry;
using MillBurn.Gerber.Excellon;
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

    /// <summary>
    /// Whether the geometry was flipped left-to-right before it was written.
    ///
    /// Carried on the item because a mirrored program cannot be placed on the board without it:
    /// its coordinates describe the *flipped* stock, so drawing them straight puts a bottom-side
    /// job on the wrong half of the board — correct file, mirror-image picture.
    /// </summary>
    public bool Mirrored { get; init; }

    /// <summary>Facts worth seeing before writing it — sizes, counts, distances.</summary>
    public IReadOnlyList<string> Summary { get; init; } = [];

    /// <summary>Things that would spoil the result. Never a reason to refuse, always to show.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>A page written beside this file to explain it, or null.</summary>
    public ExportCompanion? Companion { get; init; }

    /// <summary>
    /// Whether bending this program to a probed surface means anything.
    ///
    /// False for the blank. It is cut through, with depth to spare, before the stock is probed at
    /// all — the probe is zeroed on the corner the blank makes — so a levelled copy could only be
    /// levelled against a map of a piece that did not exist yet, and its being written at all
    /// suggested the blank needed one.
    /// </summary>
    public bool Levellable { get; init; } = true;

    /// <summary>
    /// Which bit this file is for, when its layer needed more than one — "Bit 2 of 3 · 0.50 mm drill".
    /// Null for a program that runs with one bit from start to finish.
    /// </summary>
    public string? Bit { get; init; }

    public int Bytes => System.Text.Encoding.UTF8.GetByteCount(Content);
}

/// <summary>
/// Something written next to a program to explain it, rather than to be run.
///
/// Kept beside its item rather than made an item of its own, because it is not a choice the
/// operator makes about a layer — it belongs to the file it describes and it goes wherever that
/// file goes.
/// </summary>
/// <param name="TargetName">Name only, no directory.</param>
/// <param name="Content">The whole file.</param>
/// <param name="Description">One line, for the export report.</param>
public sealed record ExportCompanion(string TargetName, string Content, string Description);

/// <summary>Everything a single Export would write.</summary>
public sealed record ExportPlan
{
    public required IReadOnlyList<ExportItem> Items { get; init; }

    public IReadOnlyList<string> Skipped { get; init; } = [];

    /// <summary>The stock this job is built on, or <see cref="BlankPlan.None"/> when there is none.</summary>
    public BlankPlan Blank { get; init; } = BlankPlan.None;

    /// <summary>
    /// One page describing the whole export, or null when there is nothing to describe.
    ///
    /// Belongs to the export rather than to any file in it, which is why it is here rather than on
    /// an item: what it says — run these in this order, work zero is here, place the SVGs by the
    /// page — is true of the set and of no member of it.
    /// </summary>
    public ExportCompanion? Page { get; init; }

    /// <summary>
    /// The rectangle every program in this plan is referenced to: the blank's, or the board's when
    /// there is no blank.
    ///
    /// Exposed because anything that draws these programs back over the board has to undo the same
    /// shift that was applied to them, and getting it from the plan is the only way to be sure it
    /// is the same one. Drawing a blank-referenced program against the board's corner puts it out
    /// by the border — a picture that is wrong in a way the file is not.
    /// </summary>
    public Bounds FrameFor(Bounds board) => Blank.Resolved ? Blank.Bounds : board;

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
        RouteEffort effort = RouteEffort.Balanced,
        ProgramFraming? framing = null,
        MachineSettings? machineSettings = null,
        JobOptions? job = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);

        var items = new List<ExportItem>();
        var skipped = new List<string>();

        // The machine's own numbers, unless a caller deliberately supplied a different profile.
        //
        // Both real callers passed the settings and left this null, so the optimizer and the time
        // estimate ran on built-in defaults — an acceleration of 200 mm/s² against a real machine's
        // 20, and a Z traverse of 600 against a real 100. Estimates were out by more than double,
        // and the optimizer was choosing orderings with the wrong cost function: its whole premise
        // is that short moves cost more than their length, which is an effect that scales with
        // acceleration.
        machine ??= (machineSettings ?? new MachineSettings()).Profile;

        var options = job ?? JobOptions.Default;

        // The blank, and with it the frame every file in this export is referenced to.
        //
        // Three things downstream read the frame rather than the board: work zero is its lower-left
        // corner, the shared SVG page is its bounds, and a mirrored layer flips about *its*
        // centreline. Without a blank the frame is the board's own bounding box, which is exactly
        // what those three used before this existed — so a job with no blank is unchanged.
        var blank = BlankFor(board, settings, library, options);

        var frame = blank.Resolved ? blank.Bounds : board.Bounds;

        // Every export in one run shares a page, so the layers overlay when imported. Cropping each
        // to its own extents is the mistake that puts the second burn out by the difference.
        //
        // With a blank the page *is* the blank: 04 section 4.3 has always wanted the page fixed to
        // the stock outline, and until now the app did not know what the stock was, so it fitted a
        // page to the artwork with an invented 2 mm margin. The blank's border is the margin.
        var page = frame.IsEmpty
            ? null
            : SvgPage.ForContent(frame, blank.Resolved ? 0 : Nm.FromMillimetres(2));

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

            List<ExportItem> made = setting.Output == OutputKind.Svg
                ? PlanSvg(board, frame, layer, setting, operation, page) is { } svg ? [svg] : []
                : PlanGcode(board, frame, layer, setting, operation, library, boardThicknessNm, machine,
                    effort, framing, machineSettings ?? new MachineSettings(), options);

            items.AddRange(made);

            // Slots and milled holes get their own file. A run that alternates drills and end mills
            // is a tool change the drilling companion page cannot describe honestly, and the two are
            // different operations with different feeds — so `Board-PTH.slots.nc` sits beside the
            // drilling program with its own line in this list.
            //
            // Tried even when the drilling item came out empty, because it can: a layer whose every
            // hole is too big to drill has nothing left to drill and everything left to route, and
            // that layer used to lose its routing file to a "nothing to cut" that was about the
            // wrong program.
            List<ExportItem> routed = operation == OperationKind.Drilling && setting.Output == OutputKind.Gcode
                ? PlanSlots(board, frame, layer, setting, library, boardThicknessNm, machine, effort,
                    framing, machineSettings ?? new MachineSettings(), options)
                : [];

            items.AddRange(routed);

            if (made.Count == 0 && routed.Count == 0)
            {
                skipped.Add(Invariant($"{layer.FileName}: nothing to cut."));
            }
        }

        // First in the list, because everything else is referenced to the piece it makes. There is
        // no program when the blank is declared: the stock is already that size, and the app is
        // being told what is on the table rather than asked to make it.
        if (blank is { Resolved: true, Cut: true } && items.Count > 0)
        {
            items.Insert(0, BlankProgram(
                board, blank, settings, library, machine, framing,
                machineSettings ?? new MachineSettings(), boardThicknessNm));
        }

        foreach (var refusal in blank.Refusals)
        {
            skipped.Add("Stock: " + refusal);
        }

        var plan = new ExportPlan { Items = items, Skipped = skipped, Blank = blank };

        // One page for the whole export, written last because it describes everything above it.
        //
        // Every fact on it is said somewhere already — in the export window, in a program's
        // comments, on a drilling page — and each of those is a different place, none of which is
        // open when somebody opens the folder next week.
        return items.Count == 0
            ? plan
            : plan with
            {
                Page = new ExportCompanion(
                    Path.GetFileNameWithoutExtension(board.Source ?? "board") + ".project.html",
                    ProjectPage.Build(plan, new ProjectPageContext
                    {
                        BoardName = Path.GetFileName(board.Source ?? "board"),
                        Board = board.Bounds,
                        BoardThicknessNm = boardThicknessNm,
                        Blank = blank,
                        OutlineCutter = OutlineCutter(board, settings, library).Name,
                        Skipped = skipped,
                    }),
                    Invariant($"{items.Count} file(s), a suggested running order, and where work zero is")),
            };
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
        Board board,
        Bounds frame,
        BoardLayer layer,
        LayerOutputSettings setting,
        OperationKind operation,
        SvgPage? page)
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
            ? MirrorX(layer.Area, frame.MinX + frame.MaxX)
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

        // Where the artwork sits on the page, which is the number somebody needs if their laser
        // software imports by the *content* rather than by the page.
        //
        // Plenty of it does: it drops the empty border and lands the drawing at the origin, which
        // is right for a picture and wrong for a board — the art is then out by the border, and the
        // border is the whole reason the page is bigger than the artwork. Saying the offset here
        // means nobody has to work it out from two dimensions and a memory of what they typed.
        var inset = new Point2(board.Bounds.MinX - frame.MinX, board.Bounds.MinY - frame.MinY);

        var summary = new List<string>
        {
            Invariant($"{page.WidthMm:F2} × {page.HeightMm:F2} mm page, shared by every layer in this export"),
            inset.X == 0 && inset.Y == 0
                ? "The artwork starts at the page's lower-left corner."
                : Invariant($"The artwork sits {Nm.ToMillimetreString(inset.X, 2)} mm right and {Nm.ToMillimetreString(inset.Y, 2)} mm up from the page's lower-left corner. If your laser software imports by content rather than by page, that is the offset to add."),
            // Measured from what is going into the file, not from the layer it came from. Mirroring
            // leaves both alone, but inverting replaces the geometry entirely — and reporting the
            // source layer's 18 shapes and 112 mm² for a drawing that is now the board minus those
            // shapes describes the wrong thing, in the one place someone checks before writing it.
            Invariant($"{area.Count} shapes, {Polygons.AreaMm2(area):F2} mm²"),
        };

        if (mirrored)
        {
            summary.Add("Mirrored · for work done on the flipped board");
        }

        if (setting.Invert)
        {
            // On a negative layer, inverting is not "the opposite of this layer" — it is the step
            // the realiser deliberately left undone, because the complement of a shape needs a
            // frame and the only real one is the board outline, which lives in a different file.
            // Saying "everything except this layer" there would describe the operation and not the
            // result.
            summary.Add(layer.DeclaredNegative
                ? "Inverted · the layer's material, since its shapes are the openings"
                : "Inverted · everything inside the board edge except this layer");
        }

        var warnings = new List<string>();

        // Only while it is still true. Inverting a negative layer against the board outline
        // resolves the polarity, so carrying the warning past that point contradicts the summary
        // line directly above it.
        if (layer.DeclaredNegative && !setting.Invert)
        {
            warnings.Add("This layer is negative: the shapes are its openings, not its material. "
                + "Inverting gives the material instead.");
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

    private static List<ExportItem> PlanGcode(
        Board board,
        Bounds frame,
        BoardLayer layer,
        LayerOutputSettings setting,
        OperationKind operation,
        ToolLibrary library,
        long boardThicknessNm,
        MachineProfile? machine,
        RouteEffort effort,
        ProgramFraming? framing,
        MachineSettings machineSettings,
        JobOptions job)
    {
        var tool = ResolveTool(setting, operation, library);
        var warnings = new List<string>();
        var summary = new List<string>();

        // What the tool's own numbers say about how it will behave. Shown against the operation
        // rather than only in the tool editor, because this is the moment somebody is deciding to
        // press go, and the feed that suited the last bit may not suit this one.
        warnings.AddRange(ToolAdvice.For(tool));

        // A list, because drilling is genuinely several toolpaths: one per hole size, each with its
        // own bit. Every other operation is one. Collapsing them into a single toolpath -- which is
        // what this used to do -- silently drilled every hole with whichever bit came first.
        var repeated = 0;

        IReadOnlyList<Toolpath> toolpaths = operation switch
        {
            OperationKind.Isolation => Only(BuildIsolation(layer, setting, tool, summary, warnings)),
            OperationKind.Drilling => BuildDrilling(
                layer, setting, tool, boardThicknessNm, library, job, summary, warnings, out repeated),
            OperationKind.Outline => Only(BuildOutline(board, layer, setting, tool, boardThicknessNm, summary, warnings)),
            OperationKind.Engrave => Only(BuildEngrave(layer, setting, tool, summary)),
            OperationKind.Pocket => Only(BuildPocket(layer, setting, tool, summary, warnings)),
            _ => [],
        };

        toolpaths = [.. toolpaths.Where(t => t.Passes.Count > 0 || t.Drills.Count > 0)];

        if (toolpaths.Count == 0)
        {
            return [];
        }

        var target = TargetNameFor(layer.FileName, operation, OutputKind.Gcode);

        var files = AssembleEach(
            board, frame, layer, setting, operation, toolpaths, tool, summary, warnings,
            target, LayerOperations.Label(operation),
            boardThicknessNm, machine, effort, framing, machineSettings);

        if (files.Count == 0 || operation != OperationKind.Drilling || !setting.WriteDrillGuide)
        {
            return files;
        }

        // One page for the layer, beside its first file, describing every file in the order they run.
        var guide = GuideFor(
            board, layer, setting, target, files, boardThicknessNm, files[0].Warnings, repeated, frame != board.Bounds);

        return guide is null ? files : [files[0] with { Companion = guide }, .. files.Skip(1)];
    }

    /// <summary>
    /// One program per bit.
    ///
    /// A layer that needs several bits used to be one file that stopped between them with <c>M0</c>.
    /// Found at the machine: <c>M0</c> puts GRBL in <em>Hold</em>, and a controller on hold will not
    /// jog or probe — so there was no way to lift the head, fit the next bit and set Z without stopping
    /// the program anyway, and the page's "change the bit, then resume" could not be followed. One
    /// file per bit makes that stop the plan: fit the bit, set Z on the same spot, run the file.
    ///
    /// The bits are grouped by tool, in the order each first appears, so no bit is fitted twice.
    /// A layer with one bit is exactly the one file it always was, under the same name. The layer's
    /// own summary and warnings go on the first file, which is where the export list shows this layer;
    /// each file says which bit it is and which file comes next, in its header, because the header is
    /// what a sender shows when the file is opened.
    /// </summary>
    private static List<ExportItem> AssembleEach(
        Board board,
        Bounds frame,
        BoardLayer layer,
        LayerOutputSettings setting,
        OperationKind operation,
        IReadOnlyList<Toolpath> toolpaths,
        Tool tool,
        List<string> summary,
        List<string> warnings,
        string target,
        string jobLabel,
        long boardThicknessNm,
        MachineProfile? machine,
        RouteEffort effort,
        ProgramFraming? framing,
        MachineSettings machineSettings)
    {
        var groups = toolpaths
            .GroupBy(t => t.Tool.Name, StringComparer.Ordinal)
            .Select(g => g.ToList())
            .ToList();

        if (groups.Count == 1)
        {
            return Assemble(
                board, frame, layer, setting, operation, toolpaths, tool, summary, warnings, target, jobLabel,
                boardThicknessNm, machine, effort, framing, machineSettings) is { } only
                ? [only]
                : [];
        }

        var names = groups.Select((g, i) => BitFileName(target, i + 1, groups.Count, g[0].Tool)).ToList();
        var files = new List<ExportItem>(groups.Count);

        for (var g = 0; g < groups.Count; g++)
        {
            var bit = groups[g][0].Tool;
            var label = Invariant($"Bit {g + 1} of {groups.Count} · {bit.Name}");

            List<string> fileSummary = g == 0 ? [label, .. summary] : [label];
            List<string> fileWarnings = g == 0 ? [.. warnings] : [];

            List<string> notes =
            [
                Invariant($"Bit {g + 1} of {groups.Count}: fit the {bit.Name}, touch off Z on the same spot each time, then run this file."),
                g + 1 < groups.Count ? Invariant($"Next: {names[g + 1]}") : "This is the last of them.",
            ];

            if (Assemble(
                    board, frame, layer, setting, operation, groups[g], bit, fileSummary, fileWarnings, names[g],
                    jobLabel, boardThicknessNm, machine, effort, framing, machineSettings, notes) is { } file)
            {
                files.Add(file with { Bit = label });
            }
        }

        return files;
    }

    /// <summary>"Board-PTH-drl.nc", bit 2 of 3, a 0.50 mm drill: "Board-PTH-drl.bit2-0.50mm.nc".</summary>
    /// <remarks>Numbered so the files sort in the order they run, padded once there are ten.</remarks>
    private static string BitFileName(string target, int number, int count, Tool bit)
    {
        var digits = count.ToString(CultureInfo.InvariantCulture).Length;

        return Path.GetFileNameWithoutExtension(target)
            + ".bit" + number.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0')
            + "-" + Nm.ToMillimetreString(bit.DiameterNm, 2) + "mm"
            + Path.GetExtension(target);
    }

    /// <summary>
    /// The slots in a drill file, as their own routing program.
    ///
    /// Empty when there are no slots, or when none of them can be cut with anything in the library —
    /// in which case the refusals are already on the drilling item, which is where somebody looking
    /// at this board will see them.
    /// </summary>
    private static List<ExportItem> PlanSlots(
        Board board,
        Bounds frame,
        BoardLayer layer,
        LayerOutputSettings setting,
        ToolLibrary library,
        long boardThicknessNm,
        MachineProfile? machine,
        RouteEffort effort,
        ProgramFraming? framing,
        MachineSettings machineSettings,
        JobOptions job)
    {
        if (layer.Drill is null)
        {
            return [];
        }

        var milled = MilledSizes(layer.Drill, library, job);

        if (layer.Drill.Slots.Count == 0 && milled.Count == 0)
        {
            return [];
        }

        var options = new SlotOptions
        {
            BoardThicknessNm = boardThicknessNm,
            BreakThroughNm = setting.BreakThroughNm,
            ToolId = job.MillDrillToolId,
        };

        var targets = new List<DrillSlotTarget>(layer.Drill.Slots.Count);

        for (var i = 0; i < layer.Drill.Slots.Count; i++)
        {
            var slot = layer.Drill.Slots[i];
            var width = layer.Drill.Tools.TryGetValue(slot.Tool, out var t) ? t.DiameterNm : 0;

            if (width > 0)
            {
                targets.Add(new DrillSlotTarget(i, slot.From, slot.To, width));
            }
        }

        var plan = SlotOperation.Build(targets, library, options, layer.Label);

        // Holes too big for any drill in the library, spiralled out with the same cutter machinery.
        // They join the slot file rather than getting one of their own: both are an end mill, and a
        // second routing file would be a second tool change for no reason.
        var holes = SlotOperation.Holes(
            [.. layer.Drill.Hits
                .Select(h => (h.At, D: layer.Drill.Tools.TryGetValue(h.Tool, out var t) ? t.DiameterNm : 0))
                .Where(h => milled.Contains(h.D))
                .Select((h, i) => new DrillSlotTarget(1_000_000 + i, h.At, h.At, h.D))],
            library,
            options,
            layer.Label);

        plan = new SlotPlan
        {
            Toolpaths = [.. plan.Toolpaths, .. holes.Toolpaths],
            Summary = [.. plan.Summary, .. holes.Summary],
            Refusals = [.. plan.Refusals, .. holes.Refusals],
            CutCount = plan.CutCount + holes.CutCount,
        };

        var summary = new List<string>();
        var warnings = new List<string>();

        // Said on this item and on the drilling item both, because the two are read in different
        // moods: here by somebody deciding whether to run this file, there by somebody who thinks
        // the holes are all accounted for.
        foreach (var refusal in plan.Refusals)
        {
            warnings.Add(Worded(refusal) + " Cut them yourself, or the parts that need them will not fit.");
        }

        if (plan.Toolpaths.Count == 0)
        {
            return [];
        }

        summary.AddRange(plan.Summary);

        var depth = Nm.ToMillimetreString(options.TotalDepthNm, 2);
        var through = Nm.ToMillimetreString(setting.BreakThroughNm, 2);
        summary.Add(Invariant($"{depth} mm deep ({through} mm through the back), ramped not plunged"));

        if (milled.Count > 0)
        {
            summary.Add(Invariant(
                $"{string.Join(", ", milled.Order().Select(m => Nm.ToMillimetreString(m, 2) + " mm"))} milled rather than drilled — no drill that size in the library"));
        }

        if (plan.RefusedCount > 0)
        {
            summary.Add(Invariant(
                $"{plan.CutCount} of {plan.CutCount + plan.RefusedCount} slots — the rest have no cutter"));
        }

        // The tool on the item is only for the advice line; each toolpath carries its own.
        var tool = plan.Toolpaths[0].Tool;
        warnings.AddRange(ToolAdvice.For(tool));

        // One stem, two files: `Board-PTH-drl.slots.nc` and `Board-PTH-drl.slots.html`. The page
        // shares the program's name so the two sort together and nobody has to guess which page
        // belongs to which file — `.routing.html` beside `.slots.nc` read as a page for some other
        // program.
        var stem = Path.GetFileNameWithoutExtension(layer.FileName);

        var files = AssembleEach(
            board, frame, layer, setting, OperationKind.Outline, plan.Toolpaths, tool, summary, warnings,
            stem + ".slots.nc",
            "Routed slots",
            boardThicknessNm, machine, effort, framing, machineSettings);

        if (files.Count == 0)
        {
            return [];
        }

        // The page that travels with the files. It carries the refusals, which is the one thing on
        // it that cannot be recovered from a program: a file cannot describe what is absent from it,
        // and the export window is not what anybody has open at the machine.
        var (html, report) = RoutingGuide.Build(
            [.. files.Select(f => new GuideProgram(f.TargetName, f.Content))],
            new RoutingGuideContext
            {
                BoardName = Path.GetFileName(board.Source),
                LayerLabel = layer.Label,
                ProgramName = files.Count == 1 ? files[0].TargetName : stem + ".slots",
                BoardThicknessNm = boardThicknessNm,
                BreakThroughNm = setting.BreakThroughNm,
                Refusals = [.. plan.Refusals.Select(Worded)],
                RefusedCount = plan.RefusedCount,
                Warnings = [.. files[0].Warnings.Where(w => !w.Contains("are NOT cut", StringComparison.Ordinal))],
                OnBlank = frame != board.Bounds,
            });

        var cutters = report.Steps.Count == 1 ? "1 cutter" : Invariant($"{report.Steps.Count} cutters");
        var where = files.Count == 1 ? string.Empty : Invariant($" in {files.Count} files");
        var missing = plan.RefusedCount == 0
            ? string.Empty
            : Invariant($", {plan.RefusedCount} not cut");

        return
        [
            files[0] with
            {
                Companion = new ExportCompanion(stem + ".slots.html", html, $"{cutters}{where}{missing}"),
            },
            .. files.Skip(1),
        ];
    }

    /// <summary>
    /// The stock a job with these settings is built on — the blank <see cref="Plan"/> resolves,
    /// without planning anything.
    ///
    /// Public because the export is not the only thing that has to agree with it. A probing routine
    /// must be written in the same frame as the programs it will level: a grid referenced to the
    /// board's corner, beside programs referenced to the blank's, measures a surface a border's
    /// width away from where the correction is applied — and both files look entirely reasonable
    /// on their own.
    /// </summary>
    public static BlankPlan BlankFor(
        Board board,
        IReadOnlyDictionary<string, LayerOutputSettings> settings,
        ToolLibrary library,
        JobOptions? job = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);

        var mirrorsAnything = board.Layers.Any(l =>
            settings.TryGetValue(l.FileName, out var s) && s.Output != OutputKind.None && s.MirrorFor(l.Role));

        return Blanks.Resolve(
            (job ?? JobOptions.Default).Blank,
            board.Bounds,
            OutlineCutter(board, settings, library).DiameterNm,
            mirrorsAnything);
    }

    /// <summary>
    /// The Board outline layer's bit — which cuts the board out, cuts the blank too, and sets the
    /// blank's minimum border.
    ///
    /// Looked up by the layer's role. It used to take the first G-code layer with *any* bit picked,
    /// which on a job whose copper was isolated with a chosen V-bit meant the blank was cut with
    /// the V-bit and its border floor measured against a 0.1 mm tip.
    ///
    /// Public because the window says which bit this is, and has to say the same one the export
    /// uses.
    /// </summary>
    public static Tool OutlineCutter(
        Board board, IReadOnlyDictionary<string, LayerOutputSettings> settings, ToolLibrary library)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);

        return OutlineSetting(board, settings) is { } outline
            ? ResolveTool(outline, OperationKind.Outline, library)
            : LayerOperations.DefaultToolFor(OperationKind.Outline, library.Tools);
    }

    /// <summary>The Board outline layer's settings, found by role, or null when there is none.</summary>
    private static LayerOutputSettings? OutlineSetting(
        Board board, IReadOnlyDictionary<string, LayerOutputSettings> settings) =>
        board.Layers
            .Where(l => l.Role == LayerRole.Outline)
            .Select(l => settings.GetValueOrDefault(l.FileName))
            .FirstOrDefault(s => s is not null);

    /// <summary>
    /// The program that cuts the blank, on its own — the same file <see cref="Plan"/> puts first in a
    /// full export.
    ///
    /// For cutting the blank on a different day from the rest of the job, which is the usual order
    /// rather than an odd one: the blank is cut and the stock seated before anything is probed, and
    /// planning every layer of a big panel just to write one rectangle is most of an export's time.
    ///
    /// The item is null when there is nothing to cut — no blank, a blank declared rather than cut,
    /// or one that was refused. The blank plan says which.
    /// </summary>
    public static (ExportItem? Item, BlankPlan Blank) PlanBlank(
        Board board,
        IReadOnlyDictionary<string, LayerOutputSettings> settings,
        ToolLibrary library,
        long boardThicknessNm,
        ProgramFraming? framing = null,
        MachineSettings? machineSettings = null,
        JobOptions? job = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);

        var blank = BlankFor(board, settings, library, job);

        if (blank is not { Resolved: true, Cut: true })
        {
            return (null, blank);
        }

        var machine = machineSettings ?? new MachineSettings();

        return (BlankProgram(board, blank, settings, library, machine.Profile, framing, machine, boardThicknessNm), blank);
    }

    /// <summary>The blank program's file name, which the full export and the Job menu both use.</summary>
    public static string BlankFileName(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);

        return Path.GetFileNameWithoutExtension(board.Source ?? "board") + ".stock.nc";
    }

    /// <summary>
    /// The program that cuts the blank out of a larger sheet.
    ///
    /// First in the list, because everything else in the export is referenced to the piece this
    /// makes. It is not a layer — no file produces it — so it is built here rather than coming out
    /// of the loop over layers, and it carries the layer identity of the outline it will later be
    /// cut against only so the export list has something to group it under.
    ///
    /// **Tabs go on the top and right edges only.** The datum is the lower-left corner, and a tab
    /// stub on a datum edge stops the blank seating by a few tenths — silently, because a stub that
    /// small is invisible and a rectangle that is 0.3 mm off seats perfectly well at a slight
    /// angle. That is the failure this feature exists to prevent, so it must not be the failure it
    /// introduces.
    /// </summary>
    private static ExportItem BlankProgram(
        Board board,
        BlankPlan blank,
        IReadOnlyDictionary<string, LayerOutputSettings> settings,
        ToolLibrary library,
        MachineProfile? machine,
        ProgramFraming? framing,
        MachineSettings machineSettings,
        long thicknessNm)
    {
        // The Board outline layer's bit, so the blank and the board come out with one cutter and no
        // tool change between the first program and the last. Said in the program and on the page,
        // because nothing in the window points at the outline row when the blank is being set up.
        var tool = OutlineCutter(board, settings, library);
        var summary = new List<string>();
        var warnings = new List<string>();

        // The outline's own cut, not just its bit: the bit's stepdown and the outline row's distance
        // through, so the blank and the board it frames come out of one set of numbers. The blank
        // used built-in values instead — 0.4 mm passes and 0.3 mm through — and cut an 0.8 mm board
        // in three passes where the outline beside it, with the same bit, took two.
        var options = new BlankOutlineOptions
        {
            Tool = tool,
            BoardThicknessNm = thicknessNm,
            DepthPerPassNm = tool.StepdownNm > 0 ? tool.StepdownNm : Nm.FromMillimetres(0.4),
            BreakThroughNm = OutlineSetting(board, settings)?.BreakThroughNm ?? new BlankOutlineOptions().BreakThroughNm,
        };

        var toolpath = BlankOperation.Build(blank.Bounds, options);

        var shift = new Point2(-blank.Bounds.MinX, -blank.Bounds.MinY);

        var job = new Job
        {
            Name = Path.GetFileNameWithoutExtension(board.Source ?? "board") + " — stock",
            Toolpaths = [Translate(toolpath, shift)],
            OriginShift = shift,
            Notes =
            [
                Invariant($"Cut this first. Everything else in this export is referenced to the corner it makes."),
                Invariant($"Fit the {tool.Name}: the Board outline layer's bit, which cuts the stock to size as well as cutting out the board."),
                Invariant($"Stock {Nm.ToMillimetreString(blank.Bounds.Width, 2)} x {Nm.ToMillimetreString(blank.Bounds.Height, 2)} mm. Work zero is its lower-left corner."),
                "Tabs are on the top and right edges only: a stub on a datum edge stops the stock seating.",
                "Deburr the two datum edges before first use — a fresh cut leaves a burr underneath.",
            ],
        };

        var (text, stats) = GcodeEmitter.Emit(job, new GcodeOptions
        {
            Framing = framing ?? ProgramFraming.None,
            SafeZNm = Nm.FromMillimetres(machineSettings.SafeZMm),
            ApproachZNm = Nm.FromMillimetres(machineSettings.ApproachZMm),
            Decimals = machineSettings.Decimals,
            CannedCycles = machineSettings.CannedCycles,
        });

        var measured = GcodeBackplot.Measure(GcodeBackplot.Classify(GcodeParser.Parse(text)), machine);

        summary.Add(Invariant(
            $"{Nm.ToMillimetreString(blank.Bounds.Width, 2)} x {Nm.ToMillimetreString(blank.Bounds.Height, 2)} mm from a larger sheet"));
        summary.Add(Invariant($"{tool.Name}, the Board outline's bit · {Nm.ToMillimetreString(options.TotalDepthNm, 2)} mm deep in {Nm.ToMillimetreString(options.DepthPerPassNm, 2)} mm passes"));
        summary.Add(Invariant($"{stats.CutLengthMm:F0} mm cutting, {measured.TravelMm:F0} mm travel"));
        summary.Add(Invariant($"{measured.TimeRange()} · {stats.Lines:N0} lines"));
        summary.AddRange(blank.Notes);

        warnings.Add("Cut this before anything else, and keep the piece the right way up — the lower-left corner of this rectangle is work zero for every other file in this export.");
        warnings.AddRange(ToolAdvice.For(tool));

        return new ExportItem
        {
            LayerFileName = "(stock)",
            Levellable = false,
            LayerLabel = "Stock",
            Role = LayerRole.Unknown,
            Operation = OperationKind.Outline,
            Output = OutputKind.Gcode,
            TargetName = BlankFileName(board),
            Content = text,
            Summary = summary,
            Warnings = warnings,
        };
    }

    /// <summary>One refusal, said the same way on the export list and on the page beside the file.</summary>
    private static string Worded(SlotRefusal refusal) => Invariant(
        $"{(refusal.Count == 1 ? "1 feature" : $"{refusal.Count} features")} {Nm.ToMillimetreString(refusal.WidthNm, 2)} mm across are NOT cut: {refusal.Reason}.");

    /// <summary>
    /// Hole sizes this board asks for that no drill in the library can make, when the project has
    /// said to mill those out.
    ///
    /// The threshold is the largest drill the operator has listed, because the library is already a
    /// claim about what is in the drawer and a second number would be one more thing to keep true.
    /// A library with no drills in it says nothing about what is too big, so nothing is milled —
    /// the alternative is milling every hole on the board the first time somebody opens the app.
    /// </summary>
    private static IReadOnlyCollection<long> MilledSizes(ExcellonFile drill, ToolLibrary library, JobOptions job)
    {
        if (!job.MillLargeHoles)
        {
            return [];
        }

        var threshold = job.MillAboveMm > 0
            ? Nm.FromMillimetres(job.MillAboveMm)
            : ToolChooser.LargestDrill(library);

        return threshold <= 0
            ? []
            : [.. drill.Tools.Values
                .Select(t => t.DiameterNm)
                .Where(d => d > threshold + ToolChooser.SlackNm)
                .Distinct()];
    }

    /// <summary>
    /// Everything between a set of toolpaths and a file: order them, emit, measure, describe.
    ///
    /// Extracted because slots are a second program from the same layer — same board, same work
    /// zero, same ordering and simplification and reporting — differing only in which toolpaths go
    /// in and what the file is called. Two copies of this would be two places for a fix to land in
    /// one of.
    /// </summary>
    private static ExportItem? Assemble(
        Board board,
        Bounds frame,
        BoardLayer layer,
        LayerOutputSettings setting,
        OperationKind operation,
        IReadOnlyList<Toolpath> toolpaths,
        Tool tool,
        List<string> summary,
        List<string> warnings,
        string target,
        string jobLabel,
        long boardThicknessNm,
        MachineProfile? machine,
        RouteEffort effort,
        ProgramFraming? framing,
        MachineSettings machineSettings,
        IReadOnlyList<string>? bitNotes = null)
    {
        _ = tool;

        // Each file is referenced to the board's own corner, so every one of them shares a work
        // zero the operator can actually touch off on.
        var shift = frame.IsEmpty
            ? Point2.Origin
            : new Point2(-frame.MinX, -frame.MinY);

        // A bottom-side layer is drawn as seen through the board, so cutting it as-is produces a
        // mirror image. The flip is baked in here rather than left to the operator, and the file
        // says which way the stock must be turned — a program that is silently the wrong hand
        // looks completely correct on screen and scraps the board.
        // Which bit this file is for comes first, when there is more than one: it is the line a sender
        // shows at the top of the file, and the one thing to check before pressing start.
        var notes = new List<string>();

        if (bitNotes is not null)
        {
            notes.AddRange(bitNotes);
        }

        notes.Add(OriginNote(board, frame));

        var mirrored = setting.MirrorFor(layer.Role);

        if (mirrored)
        {
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

        var prepared = new List<Toolpath>(toolpaths.Count);
        var reduced = SimplifyResult.Nothing;
        var route = RoutePlan.Nothing;
        var links = LinkResult.Nothing;
        var at = start;

        for (var i = 0; i < toolpaths.Count; i++)
        {
            // Simplify first, before the flip and before ordering.
            //
            // It keeps every path's endpoints, so ordering is unaffected by going second. Doing it
            // last instead breaks the mirror: arc fitting is greedy against a hard tolerance, so a
            // run that just fits in one orientation just misses in the other, and the two sides of
            // a board stop being exact reflections of each other for no reason anyone could see.
            var (path, step) = PathSimplifier.Apply(toolpaths[i]);
            reduced += step;

            // The flip comes next, so the route is optimised for the geometry that will actually be
            // cut rather than for its mirror image.
            if (mirrored)
            {
                // About the *blank's* centreline, not the board's. Physically the operator flips
                // the stock and pushes it back into the same corner, so that is the axis - and with
                // a blank in play the board's own centreline is simply the wrong line, in the worst
                // way available: the file looks entirely correct and the board is scrap.
                path = MirrorX(path, frame.MinX + frame.MaxX);
            }

            // Each toolpath picks up where the last one left off. A tool change lifts to safe Z and
            // stops; it does not move in X or Y, so the next bit starts over the last hole rather
            // than back at the corner.
            //
            // Only the last returns to work zero: the emitter parks there when the program ends, so
            // the tour is closed. On PogoTest1 that last hop was 35 mm of a 79 mm total — nearly
            // half the rapid in the file, and invisible to an optimizer that stops at the last cut.
            var last = i == toolpaths.Count - 1;
            var (ordered, plan) = ToolpathRouter.Order(
                path, at, machine, effort, last ? start : null);

            route += plan;
            at = last ? start : EndOf(ordered, at);

            // Last, because which pass follows which is exactly what decides whether the tool can
            // stay down between them, and nothing after this reorders anything.
            var (linkedPath, linkStep) = PassLinker.Apply(ordered);
            links += linkStep;

            prepared.Add(Translate(linkedPath, shift));
        }

        var job = new Job
        {
            Name = Path.GetFileNameWithoutExtension(layer.FileName) + " — " + jobLabel,
            Toolpaths = prepared,
            OriginShift = shift,
            Notes = notes,
        };

        var (text, stats) = GcodeEmitter.Emit(job, new GcodeOptions
        {
            Framing = framing ?? ProgramFraming.None,
            SafeZNm = Nm.FromMillimetres(machineSettings.SafeZMm),
            ApproachZNm = Nm.FromMillimetres(machineSettings.ApproachZMm),
            Decimals = machineSettings.Decimals,
            CannedCycles = machineSettings.CannedCycles,
        });

        // Checked against the program it is going into rather than only when it was typed: a block
        // that was fine in the editor is still worth refusing here if it would end the file early,
        // because this is the last point before something gets written to disk.
        foreach (var issue in Framing(framing))
        {
            warnings.Add(issue.IsError
                ? $"Start/end G-code, line {issue.Line}: {issue.Message} This file should not be run."
                : $"Start/end G-code, line {issue.Line}: {issue.Message}");
        }
        var emitted = GcodeParser.Parse(text);
        var measured = GcodeBackplot.Measure(GcodeBackplot.Classify(emitted), machine);

        // Drilling has no lateral cutting distance, so reporting "0 mm cutting" for it reads as a
        // failure rather than as the shape of the operation.
        //
        // Holes rather than plunges: a plunge count includes the rapid down to the approach plane
        // and every peck, so two holes were being reported as "8 plunges" directly under a line
        // saying "2 holes". Counted here from the emitted program rather than from the drill file,
        // so it is a check on the output and not a restatement of the input.
        summary.Add(operation == OperationKind.Drilling
            ? Invariant($"{HolesDrilled(emitted)} holes drilled, {measured.TravelMm:F0} mm travel")
            : Invariant($"{stats.CutLengthMm:F0} mm cutting, {measured.TravelMm:F0} mm travel"));
        summary.Add(Invariant($"{measured.TimeRange()} · {stats.Lines:N0} lines"));

        // Worth showing: fewer, longer moves is what lets the controller reach its programmed feed,
        // and it is invisible in the geometry.
        if (reduced.Reduction > 0.02)
        {
            var arcs = reduced.Arcs > 0
                ? Invariant($", {reduced.Arcs:N0} arcs")
                : string.Empty;

            summary.Add(
                Invariant($"Simplified: {reduced.SegmentsBefore:N0} → {reduced.SegmentsAfter:N0} moves")
                + arcs
                + Invariant($" ({reduced.Reduction:P0} fewer)"));
        }

        // Shown because a claim that the optimizer helps is worth nothing unless the size of the
        // help is visible on the job it helped (Documentation/03, section 6).
        if (route.InitialTravelMm > 0 && route.TravelSavedFraction > 0.005)
        {
            summary.Add(Invariant(
                $"Ordering: {route.InitialTravelMm:F0} mm rapid → {route.TravelMm:F0} mm ({route.TravelSavedFraction:P0} less)"));
        }

        // A lift is the most expensive thing in an isolation program that nobody counts, because Z
        // traverse is typically a twentieth of the XY rate. Say how many were not taken.
        if (links.Linked > 0)
        {
            summary.Add(Invariant(
                $"Stayed down for {links.Linked} of {links.Considered} pass links ({links.LinkedLengthMm:F1} mm), saving that many plunges"));
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
            TargetName = target,
            Content = text,
            Mirrored = mirrored,
            Summary = summary,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// The page that explains how to run a layer's drilling files.
    ///
    /// Built from the emitted text rather than from the toolpaths, like everything else here that
    /// describes a program: a guide made from the toolpaths would describe the run somebody meant
    /// rather than the one about to happen.
    /// </summary>
    private static ExportCompanion? GuideFor(
        Board board,
        BoardLayer layer,
        LayerOutputSettings setting,
        string target,
        List<ExportItem> files,
        long thicknessNm,
        IReadOnlyList<string> warnings,
        int repeats,
        bool onBlank)
    {
        var (html, report) = DrillGuide.Build(
            [.. files.Select(f => new GuideProgram(f.TargetName, f.Content))],
            new DrillGuideContext
            {
                BoardName = Path.GetFileName(board.Source),
                LayerLabel = layer.Label,
                ProgramName = files.Count == 1 ? files[0].TargetName : Path.GetFileNameWithoutExtension(target),
                BoardThicknessNm = thicknessNm,
                BreakThroughNm = setting.BreakThroughNm,
                RepeatedPositions = repeats,
                Warnings = warnings,
                OnBlank = onBlank,
            });

        if (report.Steps.Count == 0)
        {
            return null;
        }

        var bits = report.Steps.Count == 1 ? "1 bit" : Invariant($"{report.Steps.Count} bits");
        var where = files.Count == 1 ? string.Empty : Invariant($" in {files.Count} files");
        var holes = report.Holes == 1 ? "1 hole" : Invariant($"{report.Holes} holes");

        return new ExportCompanion(
            Path.GetFileNameWithoutExtension(target) + ".drilling.html",
            html,
            $"{bits}{where}, {holes}");
    }

    private static Toolpath BuildIsolation(
        BoardLayer layer, LayerOutputSettings setting, Tool tool, List<string> summary, List<string> warnings)
    {
        var options = new IsolationOptions
        {
            Tool = tool,
            DepthNm = setting.DepthFor(OperationKind.Isolation),
            Passes = setting.Passes,
            WidthNm = setting.IsolationWidthNm,
        };

        var width = Nm.ToMillimetreString(options.EffectiveWidthNm, 3);
        var depth = Nm.ToMillimetreString(options.DepthNm, 3);
        var count = options.PassCount;
        var moat = Nm.ToMillimetreString(options.AchievedWidthNm, 3);

        // The moat leads, because it is what the board ends up looking like; the cut width and the
        // pass count follow as the arithmetic that got there. On a single lap the two numbers are
        // the same one, so it is not said twice.
        summary.Add(count == 1
            ? Invariant($"{moat} mm isolated · 1 pass at {depth} mm deep")
            : Invariant($"{moat} mm isolated · {count} passes of {width} mm at {depth} mm deep"));

        if (options.WidthNm > 0 && count >= IsolationOptions.MaxPasses)
        {
            warnings.Add(Invariant(
                $"Isolation is capped at {IsolationOptions.MaxPasses} passes and reaches only {Nm.ToMillimetreString(options.AchievedWidthNm, 3)} mm of the {Nm.ToMillimetreString(options.WidthNm, 3)} mm asked for. Use a wider tool, or cut deeper."));
        }

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

    private static IReadOnlyList<Toolpath> BuildDrilling(
        BoardLayer layer,
        LayerOutputSettings setting,
        Tool tool,
        long thicknessNm,
        ToolLibrary library,
        JobOptions job,
        List<string> summary,
        List<string> warnings,
        out int repeated)
    {
        repeated = 0;

        if (layer.Drill is null)
        {
            return [];
        }

        var milled = MilledSizes(layer.Drill, library, job);

        var options = new DrillOptions
        {
            BoardThicknessNm = thicknessNm,
            BreakThroughNm = setting.BreakThroughNm,
            MilledNm = milled,
        };

        var depth = Nm.ToMillimetreString(options.DepthNm, 2);
        var through = Nm.ToMillimetreString(setting.BreakThroughNm, 2);
        // Sizes the *holes* come in, not tools in the file. A slot's width is a tool too, and
        // counting it here claimed a size that no hole is drilled at.
        var built = DrillOperation.Build(layer.Drill, options, tool);

        // Counted from the program rather than from the file, like everything else here that
        // describes a program. Counting sizes out of the drill file said "4 sizes" on a board whose
        // file carried 1.00000 and 1.00076 mm as separate apertures and whose program — rightly —
        // drills them with one bit; and with milling on it called the six holes that had gone to
        // the routing file "repeated", because they were missing from a subtraction that assumed
        // every hole in the file ends up in this program.
        var wanted = layer.Drill.Hits
            .Where(h => !milled.Contains(layer.Drill.Tools.TryGetValue(h.Tool, out var t) ? t.DiameterNm : 0))
            .ToList();

        var sizes = built.Count == 1 ? "1 size" : Invariant($"{built.Count} sizes");
        var repeats = wanted.Count - built.Sum(p => p.Drills.Count);
        repeated = repeats;

        var holes = repeats > 0
            ? Invariant($"{wanted.Count} holes in {sizes}, {repeats} of them repeated")
            : Invariant($"{wanted.Count} holes in {sizes}");

        summary.Add(Invariant($"{holes} · {depth} mm deep ({through} mm through the back)"));

        // Slots are routed, not drilled, and they get their own file — see SlotsFor. What belongs
        // here is only the pointer to it, because a drilling program that silently left the oval
        // holes out of a board that needs them was the original failure, and the picture on screen
        // shows the slots either way.
        if (layer.Drill.Slots.Count > 0)
        {
            var count = layer.Drill.Slots.Count == 1 ? "1 slot" : Invariant($"{layer.Drill.Slots.Count} slots");

            summary.Add(Invariant($"{count} — routed separately, in the .slots program beside this"));
        }

        // The library is a claim about the drawer. Not a refusal — you may well own a bit and not
        // have entered it — but the export window is where somebody would want to find out that
        // the run stops for a size they have never listed.
        var missing = ToolChooser.DrillsNotInLibrary(
            library,
            layer.Drill.Hits
                .Select(h => layer.Drill.Tools.TryGetValue(h.Tool, out var t) ? t.DiameterNm : 0)
                .Where(d => d > 0 && !milled.Contains(d)));

        if (missing.Count > 0)
        {
            warnings.Add(Invariant(
                $"The tool library has no drill of {string.Join(", ", missing.Select(m => Nm.ToMillimetreString(m, 2) + " mm"))}. The program still asks for {(missing.Count == 1 ? "it" : "them")} — check you have {(missing.Count == 1 ? "that bit" : "those bits")} before you start."));
        }

        // One toolpath per size, and one file per bit when they are written (see AssembleEach).
        // Returned as separate toolpaths because each carries its own bit: merging them into one
        // kept only the first tool, so a board with 0.8 mm and 1.0 mm holes had every one of them
        // drilled 1.0 mm and nothing in the file said so.
        return built;
    }

    /// <summary>
    /// How many holes the emitted program actually drills: distinct places it feeds below zero.
    ///
    /// Pecking makes several descents at one spot and they are one hole, so the positions are
    /// counted rather than the moves.
    /// </summary>
    private static int HolesDrilled(GcodeProgram program) => program.Moves
        .Where(m => !m.IsRapid && m.IsVertical && m.ToZNm < 0)
        .Select(m => m.From)
        .Distinct()
        .Count();

    /// <summary>Everything wrong with the operator's own lines, both blocks together.</summary>
    private static IReadOnlyList<FramingIssue> Framing(ProgramFraming? framing) => framing is null
        ? []
        : [.. ProgramFraming.Check(framing.Start), .. ProgramFraming.Check(framing.End, isEnd: true)];

    /// <summary>Where a toolpath leaves the tool, so the next one can start from there.</summary>
    private static Point2 EndOf(Toolpath toolpath, Point2 fallback)
    {
        if (toolpath.Drills.Count > 0)
        {
            return toolpath.Drills[^1].At;
        }

        for (var i = toolpath.Passes.Count - 1; i >= 0; i--)
        {
            if (toolpath.Passes[i].Path.Count > 0)
            {
                return toolpath.Passes[i].Path[^1].To;
            }
        }

        return fallback;
    }

    private static IReadOnlyList<Toolpath> Only(Toolpath? toolpath) =>
        toolpath is null ? [] : [toolpath];

    private static Toolpath BuildOutline(
        Board board,
        BoardLayer layer,
        LayerOutputSettings setting,
        Tool tool,
        long thicknessNm,
        List<string> summary,
        List<string> warnings)
    {
        // Everything on the board except the outline itself, so the cut knows a piece from a void.
        //
        // A profile with none of this inside it encloses nothing worth keeping, which is what a
        // slot, a window, or the routed channel between the boards of a panel is.
        //
        // Drawings are left out, and that is not tidiness. A KiCad drill map draws the board
        // profile as the backdrop to its symbols, so it contributes a copy of every outline —
        // including the channels — to the pile of "things worth keeping". Every channel on a panel
        // then looks occupied, is called a piece, and is cut around instead of down: two grooves
        // through the boards either side and a channel still joining them.
        var keep = new Paths64(board.Layers
            .Where(l => l.Role != LayerRole.Outline && LayerRoleInfo.IsFabricated(l.Role))
            .SelectMany(l => l.Area)
            .Where(r => Clipper.Area(r) > 0));

        var options = new OutlineOptions
        {
            Tool = tool,
            BoardThicknessNm = thicknessNm,
            BreakThroughNm = setting.BreakThroughNm,
            TabCount = setting.TabCount,
            DepthPerPassNm = tool.StepdownNm > 0 ? tool.StepdownNm : Nm.FromMillimetres(0.4),
            Keep = keep,
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
        // right for a single board and silently wrong for everything else — on a 66-up panel it
        // cut the frame and left every board attached, and it would drop an interior slot the
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
            // "Profiles", not "boards". A panel's Edge_Cuts is usually a shared lattice with tab
            // gaps rather than one closed outline per board, so the number of ring regions it
            // realises to is not the number of boards and must not be reported as though it were.
            summary.Add(Invariant(
                $"{profiles.Count} profiles · inner pieces cut before the frame around them"));
        }

        // Which side each profile is cut on, said before anybody presses go.
        //
        // This is worth a line of its own because it is the difference between a panel that comes
        // apart and one that is quietly destroyed: a routed channel cut on the outside takes a
        // cutter-radius off the boards on both sides and leaves the channel itself standing.
        var cutInside = OutlineOperation.PiecesAmong(profiles, keep, tool.DiameterNm).Count(p => !p);

        if (cutInside > 0)
        {
            summary.Add(Invariant(
                $"{cutInside} of them enclose nothing and are cut from the inside — slots, windows, or the channels between the boards of a panel"));
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

    /// <summary>
    /// Which bit this layer is cut with: the one it names, else the first suitable one in the
    /// library, else a built-in.
    ///
    /// **The library comes before the built-in, because the library is what the operator owns.**
    /// It used to fall straight through to a hard-coded tool whenever a layer named none — which is
    /// every layer of a folder the window has never opened — so the same board exported from the
    /// CLI and from the app came out cut with different bits. On one real library that was a
    /// 0.127 mm cut against a 0.032 mm one: four isolation passes against fifteen, an hour against
    /// three, and a different set of gaps reported as unreachable. Both files looked entirely
    /// correct.
    ///
    /// The built-in stays as the last resort, for a library with nothing of the right kind in it.
    /// A plan that refused to exist because nobody had entered a drill yet would be worse than one
    /// that assumes a 1 mm drill and says so.
    /// </summary>
    private static Tool ResolveTool(LayerOutputSettings setting, OperationKind operation, ToolLibrary library)
    {
        if (setting.ToolId is { } id && library.Tools.FirstOrDefault(t => t.Id == id) is { } chosen)
        {
            return chosen;
        }

        // The same rule the board pane uses when it fills in a tool nobody has picked, so the two
        // agree by construction rather than by both happening to be written the same way.
        return LayerOperations.DefaultToolFor(operation, library.Tools);
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
    /// <summary>
    /// Where work zero is, said in the file because that is where somebody at the machine reads it.
    ///
    /// With a blank it is the *blank's* corner, not the board's, and the difference is the whole
    /// point of the feature — a note that still said "the board's corner" would be telling the
    /// operator to touch off in a place no file in the export is referenced to.
    /// </summary>
    private static string OriginNote(Board board, Bounds frame)
    {
        var x = Nm.ToMillimetreString(board.Bounds.MinX, 3);
        var y = Nm.ToMillimetreString(board.Bounds.MinY, 3);

        return frame == board.Bounds
            ? Invariant($"Work zero is the board's lower-left corner; the Gerber origin was at {x}, {y} mm.")
            : Invariant($"Work zero is the STOCK's lower-left corner, not the board's. The board sits {Nm.ToMillimetreString(board.Bounds.MinX - frame.MinX, 2)} mm right and {Nm.ToMillimetreString(board.Bounds.MinY - frame.MinY, 2)} mm up from it. The Gerber origin was at {x}, {y} mm.");
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
