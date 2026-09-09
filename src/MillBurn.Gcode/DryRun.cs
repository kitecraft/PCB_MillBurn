using System.Globalization;
using MillBurn.Core;

namespace MillBurn.Gcode;

/// <summary>How high to hold the tool, and how honest to be about the time.</summary>
public sealed record DryRunOptions
{
    /// <summary>
    /// How far above work zero to hold the tool, in millimetres.
    ///
    /// Work zero is the top of the stock, so anything above it is clear. Five millimetres is high
    /// enough to see daylight under the tool from across a workshop, which is the whole point:
    /// a clearance you have to crouch to confirm is not a clearance you will check.
    /// </summary>
    public double HeightMm { get; init; } = 5;

    /// <summary>
    /// Keep the programmed feeds, so the run takes as long as the real one.
    ///
    /// On by default because the question a dry run answers is not only "does it go where I
    /// expect" but "how long am I committing to". Turning it off replaces cutting feeds with the
    /// rapid rate, which is quicker to watch and no longer tells you anything about the time.
    /// </summary>
    public bool KeepFeeds { get; init; } = true;

    /// <summary>Rapid rate used for cutting moves when <see cref="KeepFeeds"/> is off.</summary>
    public double RapidMmPerMin { get; init; } = 2000;
}

/// <summary>What the rewrite did, and the evidence that it is safe to run.</summary>
public sealed record DryRunReport
{
    public required int LinesRewritten { get; init; }

    public required int SpindleCommandsRemoved { get; init; }

    /// <summary>
    /// The lowest Z the rewritten program ever asks for, read back out of it.
    ///
    /// Commanded positions, not the machine's starting one: where the tool is before the first
    /// line runs is the operator's business, and a generator that claimed otherwise would be
    /// claiming something it cannot know.
    /// </summary>
    public required double LowestZMm { get; init; }

    /// <summary>
    /// True when nothing that travels in X or Y does so below the requested height.
    ///
    /// This is the property that matters and it is stronger than "no Z below the height": a
    /// program can sit at a safe height throughout and still drag the tool across the stock if it
    /// moves sideways before it has risen.
    /// </summary>
    public required bool StaysClear { get; init; }

    /// <summary>Why the rewrite was refused, or null if it succeeded.</summary>
    public string? Refusal { get; init; }
}

/// <summary>
/// Turns a program into one that traces the same path in the air.
///
/// The point is to be able to watch a file before it touches anything: are the extents right, is
/// the work zero where you think, does the order make sense, does it reach past the clamps. Until
/// this existed the first run of any program was into copper, and the only way to answer "which
/// side of that line is the cutter on" was to cut it and see.
///
/// **It rewrites the emitted file rather than re-generating from the toolpath.** Those two agree
/// right up until the emitter has a bug, and only one of them is what the machine will run — the
/// same reason the backplot parses the file (Documentation/05 §2.1). A dry run generated from the
/// toolpath would faithfully prove the safety of a program nobody is about to run.
/// </summary>
public static class DryRun
{
    /// <summary>
    /// Rewrites a program to run clear of the stock.
    ///
    /// Refuses rather than guesses. Incremental distance mode makes a Z word a *change* rather than
    /// a position, so substituting an absolute height would send the tool somewhere nobody asked
    /// for — and a dry run that is wrong is worse than none, because it is trusted.
    /// </summary>
    public static (string Text, DryRunReport Report) Rewrite(string program, DryRunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        options ??= new DryRunOptions();

        var lines = program.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        if (UsesIncrementalMode(lines) is { } refusal)
        {
            return (program, new DryRunReport
            {
                LinesRewritten = 0,
                SpindleCommandsRemoved = 0,
                LowestZMm = 0,
                StaysClear = false,
                Refusal = refusal,
            });
        }

        // A program in inches means its Z words are inches, so the substituted height has to be
        // too. Converting is better than refusing: the file is perfectly rewritable, and refusing
        // would push someone towards running the real one to see what it does.
        var inches = UsesInches(lines);
        var heightMm = options.HeightMm;
        var height = (inches ? heightMm / 25.4 : heightMm)
            .ToString(inches ? "0.0000" : "0.000", CultureInfo.InvariantCulture);

        var rewritten = 0;
        var spindle = 0;

        var output = new List<string>(lines.Length + 12) { Header(options, inches, height) };

        foreach (var raw in lines)
        {
            var line = raw;
            var code = StripComment(line);

            if (code.Length == 0)
            {
                output.Add(line);
                continue;
            }

            // The spindle stays off. A dry run with a tool spinning inches above the work is a dry
            // run that can still take a finger off, and it removes the one advantage of doing it.
            if (HasWord(code, 'M', 3) || HasWord(code, 'M', 4))
            {
                output.Add("( dry run: spindle start removed )");
                spindle++;
                continue;
            }

            if (HasZ(code))
            {
                line = ReplaceZ(line, height);
                rewritten++;
            }

            if (!options.KeepFeeds)
            {
                line = ReplaceFeed(line, options.RapidMmPerMin);
            }

            output.Add(line);
        }

        var text = string.Join("\n", output);

        // Read it back and measure, rather than trusting the rewrite. This is the check that makes
        // the file trustworthy: the claim "it never goes below 5 mm" is verified against the
        // program that will actually run, by the same parser the backplot uses.
        var parsed = GcodeParser.Parse(text);

        var lowest = parsed.Moves.Count == 0
            ? heightMm
            : parsed.Moves.Min(m => m.ToZNm) / (double)Nm.PerMillimetre;

        // The check is on moves that travel in the plane. A vertical move down to the safe height
        // from wherever the machine happens to be sitting is exactly what the first line should do;
        // a sideways move below that height is the thing that scrapes the stock.
        var floor = Nm.FromMillimetres(heightMm) - 1;
        var clear = parsed.Moves
            .Where(m => m.MovesInPlane)
            .All(m => m.FromZNm >= floor && m.ToZNm >= floor);

        return (text, new DryRunReport
        {
            LinesRewritten = rewritten,
            SpindleCommandsRemoved = spindle,
            LowestZMm = lowest,
            StaysClear = clear,
        });
    }

    /// <summary>
    /// The preamble: spindle off, units and distance mode stated, then up to the safe height
    /// before anything else happens.
    ///
    /// The rise has to be here rather than trusted to the program. Every file this app emits does
    /// lift first, but a dry run is exactly the thing you point at a file you are unsure of, and
    /// "it is safe as long as the file was already sensible" is not a guarantee worth making.
    /// </summary>
    private static string Header(DryRunOptions options, bool inches, string height) => string.Create(
        CultureInfo.InvariantCulture,
        $"""
        ( ******************************************************** )
        ( DRY RUN. This program cuts nothing.                       )
        ( Every Z is held {options.HeightMm:F3} mm above work zero and the spindle )
        ( is never started. Set work zero exactly as you would for  )
        ( the real program, then watch the path.                    )
        ( ******************************************************** )
        M5
        {(inches ? "G20" : "G21")} G90
        G0 Z{height}
        """);

    /// <summary>Whether the program selects inches. Our own files never do; other people's might.</summary>
    private static bool UsesInches(string[] lines) =>
        lines.Any(l => HasWord(StripComment(l), 'G', 20));

    /// <summary>
    /// Whether the program ever selects incremental distance mode.
    ///
    /// Checked across the whole file rather than tracked as state, because a single <c>G91</c>
    /// anywhere makes every later Z word a relative one — and this has to be a question with a
    /// yes-or-no answer, not a judgement about which lines it reached.
    /// </summary>
    private static string? UsesIncrementalMode(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var code = StripComment(lines[i]);

            // G91.1 sets arc-centre mode and is unrelated to distance mode.
            if (HasWord(code, 'G', 91) && !code.Contains("91.1", StringComparison.Ordinal))
            {
                return $"line {i + 1} selects incremental mode (G91); "
                    + "a dry run cannot rewrite relative Z moves safely.";
            }
        }

        return null;
    }

    private static string StripComment(string line)
    {
        var at = line.IndexOf('(', StringComparison.Ordinal);
        var code = at >= 0 ? line[..at] : line;

        at = code.IndexOf(';', StringComparison.Ordinal);
        return (at >= 0 ? code[..at] : code).Trim();
    }

    /// <summary>Whether a word appears with exactly this number, so M3 never matches M30.</summary>
    private static bool HasWord(string code, char letter, int number)
    {
        for (var i = 0; i < code.Length; i++)
        {
            if (char.ToUpperInvariant(code[i]) != letter)
            {
                continue;
            }

            var j = i + 1;
            while (j < code.Length && (char.IsDigit(code[j]) || code[j] == '.'))
            {
                j++;
            }

            if (j > i + 1
                && int.TryParse(code.AsSpan(i + 1, j - i - 1), CultureInfo.InvariantCulture, out var value)
                && value == number
                && (j >= code.Length || code[j] != '.'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasZ(string code)
    {
        for (var i = 0; i < code.Length; i++)
        {
            if (char.ToUpperInvariant(code[i]) == 'Z' && i + 1 < code.Length
                && (char.IsDigit(code[i + 1]) || code[i + 1] is '-' or '+' or '.'))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReplaceZ(string line, string height) => Replace(line, 'Z', height);

    private static string ReplaceFeed(string line, double rapid) =>
        Replace(line, 'F', rapid.ToString("0.###", CultureInfo.InvariantCulture));

    /// <summary>
    /// Replaces a word's value, leaving everything else — spacing, case, comments — untouched.
    ///
    /// Only outside comments: a Z in a comment is prose, and rewriting it would corrupt the very
    /// notes that say what the program does.
    /// </summary>
    private static string Replace(string line, char letter, string value)
    {
        var limit = line.IndexOf('(', StringComparison.Ordinal);
        if (limit < 0)
        {
            limit = line.IndexOf(';', StringComparison.Ordinal);
        }

        if (limit < 0)
        {
            limit = line.Length;
        }

        var result = new System.Text.StringBuilder(line.Length + value.Length);
        var i = 0;

        while (i < line.Length)
        {
            if (i >= limit || char.ToUpperInvariant(line[i]) != letter)
            {
                result.Append(line[i]);
                i++;
                continue;
            }

            var j = i + 1;
            if (j < line.Length && line[j] is '-' or '+')
            {
                j++;
            }

            var digits = j;
            while (j < line.Length && (char.IsDigit(line[j]) || line[j] == '.'))
            {
                j++;
            }

            if (j == digits)
            {
                result.Append(line[i]);
                i++;
                continue;
            }

            result.Append(letter).Append(value);
            i = j;
        }

        return result.ToString();
    }
}
