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

            if (!byDiameter.TryGetValue(diameter, out var number))
            {
                // Numbered by size in first-seen order, which is what an Excellon file would have
                // done; the numbers are only ever used to group hits by tool.
                number = byDiameter.Count + 1;
                byDiameter[diameter] = number;
                tools[number] = new DrillTool(number, diameter, flash.Aperture.Function);
            }

            hits.Add(new DrillHit(number, flash.At));
        }

        if (hits.Count == 0)
        {
            return null;
        }

        var bounds = Bounds.Empty;
        foreach (var hit in hits)
        {
            var radius = tools[hit.Tool].DiameterNm / 2;
            bounds = bounds
                .Include(new Point2(hit.At.X - radius, hit.At.Y - radius))
                .Include(new Point2(hit.At.X + radius, hit.At.Y + radius));
        }

        return new ExcellonFile
        {
            Tools = tools,
            Hits = hits,
            Slots = [],

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
