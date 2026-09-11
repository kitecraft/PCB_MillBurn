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

    /// <summary>
    /// What was subtracted from every sample's X and Y to bring the log into the job's frame.
    ///
    /// GRBL answers <c>[PRB:]</c> in <em>machine</em> coordinates while the probing routine is
    /// written in work coordinates, so a log of our own routine comes back describing a region
    /// nowhere near the board. Zero when no shift was needed or none could be established.
    /// </summary>
    public Point2 FrameOffset { get; init; }

    /// <summary>
    /// The positions the log shows the machine being *sent* to before each probe, in work
    /// coordinates. Empty when the sender did not echo the commands it streamed.
    /// </summary>
    public IReadOnlyList<Point2> CommandedPoints { get; init; } = [];

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
        var commanded = new List<Point2>();
        var failed = 0;
        var unreadable = 0;
        var sawProbeReport = false;

        // Where the machine was last told to go. A sender that echoes what it streams is telling
        // us the probe grid in *work* coordinates, which is the one thing the [PRB:] reports
        // themselves cannot say.
        long? lastX = null;
        long? lastY = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('(') || line.StartsWith(';'))
            {
                continue;
            }

            var (movedX, movedY) = CommandedMove(line, scale);
            lastX = movedX ?? lastX;
            lastY = movedY ?? lastY;

            // A sender's log is mostly its own conversation: the commands it streamed and an "ok"
            // for each. None of it is data and none of it is a problem, so none of it is counted
            // as a line that could not be read — 39 complaints about a log that parsed perfectly
            // is how a useful warning gets ignored.
            var chatter = movedX is not null
                || movedY is not null
                || IsProbeCommand(line)
                || IsSenderChatter(line);

            // One commanded point per probe, in the order the probes were sent. Recorded even when
            // the probe that followed failed, so a discarded sample does not shift the rest.
            if (IsProbeCommand(line) && lastX is { } cx && lastY is { } cy)
            {
                commanded.Add(new Point2(cx, cy));
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

            if (!chatter)
            {
                unreadable++;
            }
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

        var offset = Point2.Origin;

        if (format == ProbeLogFormat.GrblProbeReports && samples.Count > 0)
        {
            // Z is dealt with downstream — HeightMapOptions.ZeroAt re-datums the surface, and a
            // machine Z of -14.7 mm is as good a reference as any as long as it is consistent.
            // X and Y were not, and nothing used to move them: the routine is written in work
            // coordinates and the answer comes back in machine coordinates, so a log of our own
            // probing run described a region 40 mm from the board and the leveller refused it.
            offset = FrameOffsetFor(samples, commanded);

            if (offset != Point2.Origin)
            {
                samples = [.. samples.Select(s => s with { At = s.At - offset })];

                notes.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"GRBL reports [PRB:] in machine coordinates. Work zero was {Nm.ToMillimetreString(offset.X, 3)}, {Nm.ToMillimetreString(offset.Y, 3)} mm from machine zero, read from the commanded positions in the log, and every point has been moved by it."));
            }
            else if (commanded.Count == 0)
            {
                notes.Add("GRBL reports [PRB:] in machine coordinates, and this log does not echo the "
                    + "moves that were streamed, so nothing in it says where work zero was. If the "
                    + "points look far from the board, that is why: log the commands as well as the "
                    + "replies, or probe with work zero at machine zero.");
            }
        }

        return new ProbeLogResult
        {
            Format = format,
            Samples = samples,
            FailedProbes = failed,
            UnreadableLines = unreadable,
            Notes = notes,
            FrameOffset = offset,
            CommandedPoints = commanded,
        };
    }

    /// <summary>
    /// How far the reported points sit from the commanded ones, when that is a single rigid shift.
    ///
    /// Taken from the lowest corner of each set rather than by pairing them off in order, because a
    /// sender streams several commands ahead of the replies — in a real log the first probe's reply
    /// arrives under the fourth probe's echo, and anything that pairs by position gets it wrong
    /// while looking entirely reasonable.
    ///
    /// Every shifted point then has to land on a commanded one, or the shift is refused. Moving a
    /// whole map by a number that merely happens to line up two corners would be exactly the kind
    /// of quiet, plausible wrongness this feature exists to avoid.
    /// </summary>
    private static Point2 FrameOffsetFor(List<ProbeSample> samples, List<Point2> commanded)
    {
        if (samples.Count == 0 || commanded.Count == 0)
        {
            return Point2.Origin;
        }

        var offset = new Point2(
            samples.Min(s => s.At.X) - commanded.Min(p => p.X),
            samples.Min(s => s.At.Y) - commanded.Min(p => p.Y));

        if (offset == Point2.Origin)
        {
            return Point2.Origin;
        }

        // The probe does not move in X or Y while it descends, so a match should be exact to the
        // sender's own rounding. A tenth is generous and still nowhere near a grid spacing.
        var tolerance = Nm.FromMillimetres(0.1);

        foreach (var sample in samples)
        {
            var at = new Point2(sample.At.X - offset.X, sample.At.Y - offset.Y);

            if (!commanded.Any(p => Math.Abs(p.X - at.X) <= tolerance && Math.Abs(p.Y - at.Y) <= tolerance))
            {
                return Point2.Origin;
            }
        }

        return offset;
    }

    /// <summary>A line that moves the machine, and the X and Y it names. Either may be absent.</summary>
    private static (long? X, long? Y) CommandedMove(string line, double scale)
    {
        // Only a rapid or a feed move. A probe line carries a Z and no X or Y, and a status report
        // carries coordinates that are a position rather than an instruction.
        if (line.Contains("[PRB", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith('<')
            || line.Contains("MPos:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("WPos:", StringComparison.OrdinalIgnoreCase)
            || !HasMotionWord(line))
        {
            return (null, null);
        }

        return (Word(line, 'X', scale), Word(line, 'Y', scale));
    }

    /// <summary>A bare G0 or G1 somewhere in the line, not the 0 of a G30 or the 1 of a G17.</summary>
    private static bool HasMotionWord(string line)
    {
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (char.ToUpperInvariant(line[i]) == 'G'
                && (i == 0 || !char.IsLetterOrDigit(line[i - 1]))
                && (line[i + 1] is '0' or '1')
                && (i + 2 >= line.Length || (!char.IsDigit(line[i + 2]) && line[i + 2] != '.')))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A line the sender wrote about itself rather than about the surface.
    ///
    /// Deliberately a short list of things senders demonstrably say, not a guess at everything they
    /// might: anything unrecognised still counts against the unreadable total, which is what makes
    /// that total worth reading.
    /// </summary>
    private static bool IsSenderChatter(string line)
    {
        var text = line.TrimStart('>', '<', '*', ' ', '	');

        return text.Length == 0
            || text.StartsWith("ok", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("error", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("ALARM", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Grbl", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Finished", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith('$')
            || text.StartsWith('[')
            || text.StartsWith('M')
            || text.StartsWith('G');
    }

    private static bool IsProbeCommand(string line) =>
        line.Contains("G38", StringComparison.OrdinalIgnoreCase)
        && !line.Contains("[PRB", StringComparison.OrdinalIgnoreCase);

    /// <summary>The number attached to a G-code word, or null when the word is not there.</summary>
    private static long? Word(string line, char word, double scale)
    {
        for (var i = 0; i < line.Length; i++)
        {
            // A digit before the word is normal and a letter is not: G-code words run together, so
            // the X in "G0X1.000Y1.000" follows the 0 of G0. Rejecting a preceding digit was why
            // nothing in a real log was read as a commanded move.
            if (char.ToUpperInvariant(line[i]) != word || (i > 0 && char.IsLetter(line[i - 1])))
            {
                continue;
            }

            var start = i + 1;
            var end = start;

            if (end < line.Length && (line[end] is '-' or '+'))
            {
                end++;
            }

            while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.'))
            {
                end++;
            }

            if (end > start
                && double.TryParse(
                    line[start..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return (long)Math.Round(value * scale);
            }
        }

        return null;
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
