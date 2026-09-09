using System.Globalization;
using MillBurn.Core;

namespace MillBurn.Align;

/// <summary>What shape the log turned out to be in.</summary>
public enum ProbeLogFormat
{
    /// <summary>Nothing recognisable.</summary>
    Unknown,

    /// <summary>GRBL probe reports: <c>[PRB:12.000,8.000,-0.140:1]</c>.</summary>
    GrblProbeReports,

    /// <summary>Three numbers a line, separated by commas, tabs, semicolons or spaces.</summary>
    Triples,
}

/// <summary>What came out of a log, and everything questionable about it.</summary>
public sealed record ProbeLogResult
{
    public required ProbeLogFormat Format { get; init; }

    public required IReadOnlyList<ProbeSample> Samples { get; init; }

    /// <summary>Probes that reported a failure to trigger, and were therefore thrown away.</summary>
    public required int FailedProbes { get; init; }

    /// <summary>Lines that looked like they were meant to be data but were not.</summary>
    public required int UnreadableLines { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    public bool IsEmpty => Samples.Count == 0;
}

/// <summary>
/// Reads the results of a probing run back out of whatever the sender wrote down.
///
/// This is the import half of "generate and import" ([04 §5](../../Documentation/04-Machines-Laser-and-Mixed-Workflows.md#5-height-mapping-autolevelling)):
/// we emit a probing program, the operator runs it in their own sender, and the sender's log comes
/// back here. Because the log is the interface, a map probed months ago, or by a tool that has
/// never heard of this one, imports exactly as well as one from a routine we wrote.
/// </summary>
public static class ProbeLog
{
    private static readonly char[] Separators = [',', '\t', ';', ' '];

    /// <summary>
    /// Reads a log. Never throws on rubbish — an unreadable line is counted, not fatal.
    /// </summary>
    /// <remarks>
    /// Detected by shape rather than by file extension or a claimed sender name. A log is a few
    /// numbers a line; insisting on knowing which program produced it would reject the one someone
    /// typed by hand, which is a perfectly good way to record eight probe points.
    /// </remarks>
    public static ProbeLogResult Parse(string text, bool inches = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var scale = inches ? Nm.PerMillimetre * 25.4 : Nm.PerMillimetre;

        var samples = new List<ProbeSample>();
        var notes = new List<string>();
        var failed = 0;
        var unreadable = 0;
        var sawProbeReport = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('(') || line.StartsWith(';'))
            {
                continue;
            }

            if (FindProbeReport(line) is { } report)
            {
                sawProbeReport = true;

                // ":0" means the probe never touched anything — it ran to the end of its travel
                // and gave up. The position it reports is where it stopped, which is not a surface
                // height. Keeping it would put a hole in the map exactly where the measurement
                // failed, which is the worst possible place to invent a number.
                if (!report.Triggered)
                {
                    failed++;
                    continue;
                }

                samples.Add(ToSample(report.Values, scale));
                continue;
            }

            // A line with a probe report in it settles the format; anything else in such a file is
            // the sender's chatter — "ok", "Grbl 1.1f", status reports — and not data at all.
            if (sawProbeReport)
            {
                continue;
            }

            if (Numbers(line) is { Count: >= 3 } values)
            {
                samples.Add(ToSample([values[0], values[1], values[2]], scale));
                continue;
            }

            unreadable++;
        }

        if (failed > 0)
        {
            notes.Add($"{failed} probe(s) never touched the surface and were discarded.");
        }

        // A header row is one unreadable line at the top, and saying so would be noise. Several
        // mean the file is probably not what the user thought it was.
        if (unreadable > 1)
        {
            notes.Add($"{unreadable} line(s) could not be read as coordinates and were skipped.");
        }

        var format = sawProbeReport
            ? ProbeLogFormat.GrblProbeReports
            : samples.Count > 0 ? ProbeLogFormat.Triples : ProbeLogFormat.Unknown;

        if (format == ProbeLogFormat.GrblProbeReports)
        {
            // Worth saying because it changes what the numbers mean rather than merely how they
            // are punctuated: GRBL reports [PRB:] in machine coordinates, so the Z values are
            // nowhere near zero and the map has to be shifted before it means anything.
            notes.Add("GRBL probe reports are in machine coordinates; the map will be shifted to put "
                + "zero at the board's origin corner.");
        }

        return new ProbeLogResult
        {
            Format = format,
            Samples = samples,
            FailedProbes = failed,
            UnreadableLines = unreadable,
            Notes = notes,
        };
    }

    /// <summary>Reads a log straight into a map. The convenience the callers actually want.</summary>
    public static (HeightMap? Map, ProbeLogResult Log) Read(
        string text, HeightMapOptions? options = null, bool inches = false)
    {
        var log = Parse(text, inches);

        return log.IsEmpty
            ? (null, log)
            : (HeightMap.Build(log.Samples, options), log);
    }

    private static ProbeSample ToSample(double[] values, double scale) => new(
        new Point2(
            (long)Math.Round(values[0] * scale),
            (long)Math.Round(values[1] * scale)),
        (long)Math.Round(values[2] * scale));

    /// <summary>
    /// Pulls <c>[PRB:x,y,z:1]</c> out of a line.
    ///
    /// Anywhere in the line, because senders prefix their logs with timestamps and direction
    /// markers, and every one of them does it differently.
    /// </summary>
    private static (double[] Values, bool Triggered)? FindProbeReport(string line)
    {
        var start = line.IndexOf("[PRB:", StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return null;
        }

        var end = line.IndexOf(']', start);
        var body = end > start ? line[(start + 5)..end] : line[(start + 5)..];

        // The trailing ":1" or ":0" is the success flag. Its absence is not a failure — some
        // senders drop it, and an older GRBL never sent it — so it defaults to trusted.
        var triggered = true;
        var colon = body.LastIndexOf(':');

        if (colon >= 0)
        {
            triggered = body[(colon + 1)..].Trim() != "0";
            body = body[..colon];
        }

        var values = Numbers(body);

        return values is { Count: >= 3 }
            ? ([values[0], values[1], values[2]], triggered)
            : null;
    }

    /// <summary>Every number on a line, or null if any field is not one.</summary>
    private static List<double>? Numbers(string line)
    {
        var fields = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (fields.Length < 3)
        {
            return null;
        }

        var values = new List<double>(fields.Length);

        foreach (var field in fields)
        {
            if (!double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || double.IsNaN(value)
                || double.IsInfinity(value))
            {
                return null;
            }

            values.Add(value);
        }

        return values;
    }
}
