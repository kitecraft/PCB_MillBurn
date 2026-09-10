using MillBurn.Core;
using MillBurn.Gerber.Apertures;
using MillBurn.Gerber.Excellon;
using MillBurn.Gerber.Model;

namespace MillBurn.Pipeline;

/// <summary>
/// Reads holes out of a drill file written as Gerber rather than Excellon.
///
/// KiCad's <em>Generate Drill Files</em> offers "Gerber X2" for the drill output, and it is a
/// perfectly reasonable thing to pick: the result carries the same <c>%TF.FileFunction%</c>
/// attributes as every other layer, so a tool that reads them knows exactly what it has. What it is
/// not is Excellon, and a pipeline that only understands <c>.drl</c> sees a Gerber full of circles,
/// draws them, and drills nothing — a board that comes off the machine with no holes in it and no
/// warning that anything was missed.
///
/// The conversion is exact rather than inferred. Every hole is a <c>D03</c> flash of a circular
/// aperture, so the position is the flash and the diameter is the aperture's own parameter — not a
/// measurement taken back off the polygon it was drawn as.
/// </summary>
public static class GerberDrills
{
    /// <summary>
    /// Turns a parsed drill-function Gerber into the same shape an Excellon file produces.
    ///
    /// Returns null when the image holds nothing that looks like a hole, so a caller can tell
    /// "this was not really a drill file" from "this drill file is empty".
    /// </summary>
    public static ExcellonFile? From(GerberImage image, HolePlating plating)
    {
        ArgumentNullException.ThrowIfNull(image);

        var tools = new Dictionary<int, DrillTool>();
        var byDiameter = new Dictionary<long, int>();
        var hits = new List<DrillHit>();
        var slots = new List<DrillSlot>();

        // Numbered by size in first-seen order, which is what an Excellon file would have done;
        // the numbers are only ever used to group hits and slots by tool.
        int ToolFor(Aperture aperture, long diameter)
        {
            if (!byDiameter.TryGetValue(diameter, out var number))
            {
                number = byDiameter.Count + 1;
                byDiameter[diameter] = number;
                tools[number] = new DrillTool(number, diameter, aperture.Function);
            }

            return number;
        }

        foreach (var flash in image.Objects.OfType<FlashObject>())
        {
            // Only circles. A drill file should contain nothing else, and a non-circular aperture
            // here means the file is not what it claims — better to leave it out of the hole list
            // than to drill a rectangle's worth of something.
            if (flash.Aperture.Kind != ApertureKind.Circle)
            {
                continue;
            }

            var diameter = flash.Aperture.NominalWidthNm;
            if (diameter <= 0)
            {
                continue;
            }

            hits.Add(new DrillHit(ToolFor(flash.Aperture, diameter), flash.At));
        }

        // A slot is a stroke, not a flash: the drill dragged from one point to the other with a
        // round aperture the width of the hole. KiCad writes every oval and routed hole this way,
        // so a file read for flashes alone loses them — and it loses them *quietly*, because the
        // same file realises into a perfectly good picture of slots that nothing then makes.
        foreach (var draw in image.Objects.OfType<DrawObject>())
        {
            if (draw.Aperture.Kind != ApertureKind.Circle)
            {
                continue;
            }

            var width = draw.Aperture.NominalWidthNm;
            if (width <= 0)
            {
                continue;
            }

            foreach (var segment in draw.Segments)
            {
                // An arc-shaped slot is a real thing and this is not it. Recording it as the chord
                // between its ends would put a straight cut where a curved one belongs, which is
                // worse than the file saying it could not be handled.
                if (segment.IsArc)
                {
                    continue;
                }

                slots.Add(new DrillSlot(ToolFor(draw.Aperture, width), segment.From, segment.To));
            }
        }

        if (hits.Count == 0 && slots.Count == 0)
        {
            return null;
        }

        var bounds = Bounds.Empty;

        void Grow(Point2 at, long radius) => bounds = bounds
            .Include(new Point2(at.X - radius, at.Y - radius))
            .Include(new Point2(at.X + radius, at.Y + radius));

        foreach (var hit in hits)
        {
            Grow(hit.At, tools[hit.Tool].DiameterNm / 2);
        }

        foreach (var slot in slots)
        {
            var radius = tools[slot.Tool].DiameterNm / 2;
            Grow(slot.From, radius);
            Grow(slot.To, radius);
        }

        return new ExcellonFile
        {
            Tools = tools,
            Hits = hits,
            Slots = slots,

            // The coordinates are already in nanometres by the time they leave the Gerber parser,
            // so the unit is only carried for anyone re-reading the file's own numbers.
            Unit = LengthUnit.Millimetres,
            Plating = plating,
            Bounds = bounds,
            Diagnostics = image.Diagnostics,
        };
    }

    /// <summary>
    /// Whether a file function describes drilling rather than a layer of the board.
    ///
    /// <c>Plated,1,2,PTH,Drill</c> and <c>NonPlated,1,2,NPTH,Drill</c> are holes.
    /// <c>Drillmap</c> is emphatically not: it is a human-readable chart of symbols and text
    /// showing where the holes go, and treating it as holes would drill several hundred of them
    /// through the legend.
    /// </summary>
    public static bool IsDrillFunction(string? fileFunction)
    {
        if (string.IsNullOrWhiteSpace(fileFunction))
        {
            return false;
        }

        var fields = fileFunction.Split(',', StringSplitOptions.TrimEntries);

        return fields.Contains("Drill", StringComparer.OrdinalIgnoreCase)
            && !fields[0].Equals("Drillmap", StringComparison.OrdinalIgnoreCase);
    }
}
