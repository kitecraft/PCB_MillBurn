using System.Globalization;

namespace MillBurn.Core;

/// <summary>
/// A controller's own answer to <c>$$</c>, read back into the numbers this app estimates with.
///
/// Every figure the app says about how long a job takes rests on four numbers that describe a
/// physical machine, and until now three of them were defaults nobody could change. They were also
/// wrong, by a lot, on the first machine they met: acceleration assumed at 200 mm/s² against a real
/// 20, and the Z traverse at 600 mm/min against a real 100. The estimate for a dry run came out at
/// 30 to 63 seconds and the machine took 2 min 21.
///
/// The controller knows all four and will say so for the asking. This reads what it said — the same
/// trick as pulling the firmware's name out of a <c>$I</c> reply in a probe log, and for the same
/// reason: the answer is already in the operator's terminal, and typing it in again by hand is a
/// step at which numbers go missing.
/// </summary>
public sealed record GrblSettings
{
    /// <summary>What was found, by setting number. Only the ones this app has a use for.</summary>
    public required IReadOnlyDictionary<int, double> Values { get; init; }

    /// <summary>Non-blank lines that were looked at, so "nothing found" can say how hard it looked.</summary>
    public required int LinesRead { get; init; }

    /// <summary>Why this is not a <c>$$</c> dump, or null when it is one.</summary>
    public string? Rejection { get; init; }

    public double? this[int setting] => Values.TryGetValue(setting, out var value) ? value : null;

    /// <summary>
    /// Reads a pasted dump. Never throws: an unreadable line is skipped, not fatal.
    /// </summary>
    /// <remarks>
    /// Deliberately tolerant about what surrounds the numbers. A dump arrives pasted out of a
    /// sender's console, so it carries timestamps, <c>&gt;&gt;&gt;</c> markers, the echoed
    /// <c>$$</c> itself, the trailing <c>ok</c>, and — depending on the sender — the bracketed
    /// description after the value. All of that is noise around <c>$&lt;n&gt; = &lt;number&gt;</c>,
    /// which is the only shape that matters.
    /// </remarks>
    public static GrblSettings Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var values = new Dictionary<int, double>();
        var considered = 0;

        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim().TrimStart('>', '<', '*', ' ', '\t');

            if (line.Length == 0)
            {
                continue;
            }

            considered++;

            // "$120 = 20.000  (X-axis acceleration, mm/sec^2)" — and every variation on the
            // spacing around the equals sign that a sender might print.
            if (!line.StartsWith('$') || line.IndexOf('=', StringComparison.Ordinal) is var at && at < 2)
            {
                continue;
            }

            if (!int.TryParse(
                    line[1..at].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }

            var after = line[(at + 1)..].Trim();
            var end = 0;

            while (end < after.Length && (char.IsDigit(after[end]) || after[end] is '.' or '-' or '+'))
            {
                end++;
            }

            if (end > 0
                && double.TryParse(
                    after[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                values[number] = value;
            }
        }

        return new GrblSettings
        {
            Values = values,
            LinesRead = considered,
            Rejection = Unusable(values, considered),
        };
    }

    /// <summary>
    /// Whether this is a dump at all, and if not, why not.
    ///
    /// The four settings below are the point of reading one. A file with <c>$</c> lines in it but
    /// none of them is a different report — a <c>$I</c> reply, a <c>$G</c> state, half a probe log
    /// — and accepting it would leave the machine profile untouched while saying it had been read.
    /// </summary>
    private static string? Unusable(Dictionary<int, double> values, int considered)
    {
        if (values.Count == 0)
        {
            return $"Nothing in this looks like a $$ dump: {considered} line(s) read, none of them "
                + "a setting. Send $$ to the controller and paste back everything it replies.";
        }

        var wanted = new[] { 11, 110, 112, 120 };

        return wanted.Any(values.ContainsKey)
            ? null
            : $"{values.Count} setting(s) read, but none of the ones that matter here — $11, $110, "
                + "$112 or $120. This looks like a different report rather than a $$ dump.";
    }

    /// <summary>
    /// The machine settings with whatever the dump actually carried applied, and a line per change.
    ///
    /// Settings the dump does not mention are left alone rather than reset to a default. A partial
    /// paste is a common thing to do and must not quietly undo the rest.
    /// </summary>
    public (MachineSettings Machine, IReadOnlyList<string> Changes) ApplyTo(MachineSettings machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        var changes = new List<string>();
        var updated = machine;

        void Take(int setting, string name, string unit, double current, Func<double, MachineSettings> set)
        {
            if (this[setting] is not { } value || value <= 0 || Math.Abs(value - current) < 1e-9)
            {
                return;
            }

            updated = set(value);

            changes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"${setting} {name}: {current:G6} → {value:G6} {unit}"));
        }

        Take(110, "traverse", "mm/min", updated.RapidMmPerMin,
            v => updated with { RapidMmPerMin = v });

        Take(112, "Z traverse", "mm/min", updated.ZRapidMmPerMin,
            v => updated with { ZRapidMmPerMin = v });

        Take(120, "acceleration", "mm/s²", updated.AccelerationMmPerSecondSquared,
            v => updated with { AccelerationMmPerSecondSquared = v });

        Take(11, "junction deviation", "mm", updated.JunctionDeviationMm,
            v => updated with { JunctionDeviationMm = v });

        return (updated, changes);
    }

    /// <summary>
    /// Things in the dump that are not about speed but are worth saying out loud once they are in
    /// front of somebody.
    /// </summary>
    public IReadOnlyList<string> Notes()
    {
        var notes = new List<string>();

        // $32 is laser mode. In it GRBL does not stop at corners to let the spindle keep up, and
        // every S word moves the laser rather than the spindle — a milling job run this way cuts at
        // the wrong feeds and may never start the spindle at all.
        if (this[32] is 1)
        {
            notes.Add("$32 = 1: this controller is in laser mode. Milling programs expect it off.");
        }

        if (this[30] is { } maxRpm && maxRpm > 0)
        {
            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"$30 says the spindle tops out at {maxRpm:G6} RPM. An S word above that is clamped, so a tool asking for more will not get it."));
        }

        if (this[20] is 0 && this[130] is > 0)
        {
            notes.Add("$20 = 0: soft limits are off, so nothing stops a program that runs past the "
                + "table except the hard limit switches.");
        }

        return notes;
    }
}
