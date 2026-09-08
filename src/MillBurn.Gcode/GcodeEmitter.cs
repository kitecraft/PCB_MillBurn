using System.Globalization;
using System.Text;
using MillBurn.Core;

namespace MillBurn.Gcode;

/// <summary>Machine-facing settings for emitting a program.</summary>
public sealed record GcodeOptions
{
    /// <summary>Height for rapid moves between cuts.</summary>
    public long SafeZNm { get; init; } = Nm.FromMillimetres(2.0);

    /// <summary>Height to drop to at rapid before switching to the plunge feed.</summary>
    public long ApproachZNm { get; init; } = Nm.FromMillimetres(0.5);

    /// <summary>Decimals on coordinates. Three is one micron, which is past every hobby machine.</summary>
    public int Decimals { get; init; } = 3;

    /// <summary>
    /// Emit <c>G81</c>/<c>G83</c> canned cycles for drilling.
    ///
    /// Off by default because **GRBL does not implement them** and silently ignores what it cannot
    /// parse — which on a drill file means the spindle travels the pattern without ever going down,
    /// and the board comes out with no holes and no error. LinuxCNC and Mach3 do support them.
    /// </summary>
    public bool CannedCycles { get; init; }

    /// <summary>Where the tool starts and returns to.</summary>
    public Point2 Origin { get; init; } = Point2.Origin;

    public bool IncludeComments { get; init; } = true;
}

/// <summary>What a program turned out to cost.</summary>
public sealed record GcodeStats
{
    public required int Lines { get; init; }

    public required double CutLengthMm { get; init; }

    public required double RapidLengthMm { get; init; }

    public required int PlungeCount { get; init; }

    public required int ToolChanges { get; init; }

    public required int DrillCount { get; init; }
}

/// <summary>
/// Turns a <see cref="Job"/> into G-code.
///
/// Deliberately plain: absolute coordinates, millimetres, no canned cycles unless asked, no
/// controller-specific extensions. Everything that varies by machine belongs in the post-processor
/// chain rather than in here, so that this stays the part that is easy to be sure about.
///
/// The invariant that matters: **the tool is never at cutting depth while moving to somewhere it
/// was not cutting.** Every move between passes goes up to safe Z first. That is the difference
/// between a travel move and a gouge across the board, and it is the sort of thing a generator gets
/// right by construction or not at all.
/// </summary>
public static class GcodeEmitter
{
    public static (string Text, GcodeStats Stats) Emit(Job job, GcodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(options);

        var sb = new StringBuilder(16 * 1024);
        var format = "F" + options.Decimals.ToString(CultureInfo.InvariantCulture);

        var cut = 0.0;
        var rapid = 0.0;
        var plunges = 0;
        var toolChanges = 0;
        var drills = 0;

        var at = options.Origin;
        Tool? currentTool = null;

        void Comment(string text)
        {
            if (options.IncludeComments)
            {
                sb.Append("( ").Append(text.Replace('(', '[').Replace(')', ']')).Append(" )\n");
            }
        }

        string Mm(long nm) => Nm.ToMillimetres(nm).ToString(format, CultureInfo.InvariantCulture);

        Comment($"PCB_MillBurn - {job.Name}");
        foreach (var note in job.Notes)
        {
            Comment(note);
        }

        sb.Append("G21 G90 G94\n");
        sb.Append("G17\n");
        sb.Append("G0 Z").Append(Mm(options.SafeZNm)).Append('\n');

        foreach (var toolpath in job.Toolpaths)
        {
            if (toolpath.Passes.Count == 0 && toolpath.Drills.Count == 0)
            {
                continue;
            }

            sb.Append('\n');
            Comment(toolpath.Label);
            foreach (var note in toolpath.Notes)
            {
                Comment(note);
            }

            if (currentTool is null || currentTool.Name != toolpath.Tool.Name)
            {
                // A manual tool change: stop, let the operator swap it, and do not assume the
                // spindle survived. M5/M0/M3 is the sequence that is safe on a machine with no
                // changer, which is every machine this targets.
                if (currentTool is not null)
                {
                    sb.Append("G0 Z").Append(Mm(options.SafeZNm)).Append('\n');
                    sb.Append("M5\n");
                    Comment($"Change tool to {toolpath.Tool}");
                    sb.Append("M0\n");
                    toolChanges++;
                }

                sb.Append("M3 S").Append(toolpath.Tool.SpindleRpm.ToString(CultureInfo.InvariantCulture)).Append('\n');
                currentTool = toolpath.Tool;
            }

            foreach (var pass in toolpath.Passes)
            {
                if (pass.Path.Count == 0)
                {
                    continue;
                }

                rapid += at.DistanceTo(pass.Start);
                at = EmitPass(sb, pass, toolpath.Tool, options, Mm);
                cut += pass.LengthNm;
                plunges++;
            }

            foreach (var drill in toolpath.Drills)
            {
                rapid += at.DistanceTo(drill.At);
                EmitDrill(sb, drill, toolpath.Tool, options, Mm);
                at = drill.At;
                drills++;
                plunges++;
            }
        }

        sb.Append('\n');
        sb.Append("G0 Z").Append(Mm(options.SafeZNm)).Append('\n');
        sb.Append("M5\n");
        sb.Append("G0 X").Append(Mm(options.Origin.X)).Append(" Y").Append(Mm(options.Origin.Y)).Append('\n');
        sb.Append("M30\n");

        var text = sb.ToString();

        return (text, new GcodeStats
        {
            Lines = text.Count(c => c == '\n'),
            CutLengthMm = cut / Nm.PerMillimetre,
            RapidLengthMm = rapid / Nm.PerMillimetre,
            PlungeCount = plunges,
            ToolChanges = toolChanges,
            DrillCount = drills,
        });
    }

    private static Point2 EmitPass(
        StringBuilder sb, ToolpathPass pass, Tool tool, GcodeOptions options, Func<long, string> mm)
    {
        var start = pass.Start;

        // Up, across, down. Never across at depth.
        sb.Append("G0 Z").Append(mm(options.SafeZNm)).Append('\n');
        sb.Append("G0 X").Append(mm(start.X)).Append(" Y").Append(mm(start.Y)).Append('\n');
        sb.Append("G0 Z").Append(mm(options.ApproachZNm)).Append('\n');
        sb.Append("G1 Z").Append(mm(-pass.DepthNm))
          .Append(" F").Append(tool.PlungeMmPerMin.ToString(CultureInfo.InvariantCulture)).Append('\n');

        var feed = tool.FeedMmPerMin.ToString(CultureInfo.InvariantCulture);
        var first = true;

        foreach (var segment in pass.Path)
        {
            if (segment.IsArc)
            {
                // Arcs survive from the Gerber to here, so they can be emitted as arcs rather than
                // as a thousand short lines. I and J are relative to the start of the arc.
                var i = segment.Centre.X - segment.From.X;
                var j = segment.Centre.Y - segment.From.Y;

                sb.Append(segment.Sweep == ArtSweep.Clockwise ? "G2" : "G3")
                  .Append(" X").Append(mm(segment.To.X))
                  .Append(" Y").Append(mm(segment.To.Y))
                  .Append(" I").Append(mm(i))
                  .Append(" J").Append(mm(j));
            }
            else
            {
                sb.Append("G1 X").Append(mm(segment.To.X)).Append(" Y").Append(mm(segment.To.Y));
            }

            if (first)
            {
                sb.Append(" F").Append(feed);
                first = false;
            }

            sb.Append('\n');
        }

        sb.Append("G0 Z").Append(mm(options.SafeZNm)).Append('\n');
        return pass.End;
    }

    private static void EmitDrill(
        StringBuilder sb, DrillTarget drill, Tool tool, GcodeOptions options, Func<long, string> mm)
    {
        sb.Append("G0 X").Append(mm(drill.At.X)).Append(" Y").Append(mm(drill.At.Y)).Append('\n');

        if (options.CannedCycles)
        {
            sb.Append("G81 Z").Append(mm(-drill.DepthNm))
              .Append(" R").Append(mm(options.ApproachZNm))
              .Append(" F").Append(tool.PlungeMmPerMin.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("G80\n");
            return;
        }

        // Emulated pecking, because GRBL has no canned cycles. Each peck retracts to the approach
        // height to clear the flutes; a 0.5 mm bit that packs up snaps, and a snapped bit in a
        // plated hole ends the board.
        var plunge = tool.PlungeMmPerMin.ToString(CultureInfo.InvariantCulture);
        sb.Append("G0 Z").Append(mm(options.ApproachZNm)).Append('\n');

        var peck = drill.PeckNm > 0 ? drill.PeckNm : drill.DepthNm;
        for (var depth = peck; ; depth += peck)
        {
            var reached = Math.Min(depth, drill.DepthNm);
            sb.Append("G1 Z").Append(mm(-reached)).Append(" F").Append(plunge).Append('\n');

            if (reached >= drill.DepthNm)
            {
                break;
            }

            // Back to the approach height, not just off the bottom: the point is to clear the
            // flutes, and a short retract inside the hole leaves the swarf where it was.
            sb.Append("G0 Z").Append(mm(options.ApproachZNm)).Append('\n');
        }

        sb.Append("G0 Z").Append(mm(options.SafeZNm)).Append('\n');
    }
}
