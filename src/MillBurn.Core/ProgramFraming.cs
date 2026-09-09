using System.Globalization;
using System.Text.Json.Serialization;

namespace MillBurn.Core;

/// <summary>Where a problem with custom G-code is, and how bad it is.</summary>
/// <param name="Line">1-based line within the block the operator typed.</param>
/// <param name="Message">What is wrong, in the terms they typed it in.</param>
/// <param name="IsError">True to refuse the export; false to show it and carry on.</param>
public readonly record struct FramingIssue(int Line, string Message, bool IsError)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"line {Line}: {Message}");
}

/// <summary>
/// The operator's own lines, run at the start and end of every program.
///
/// Every machine has a ritual: home first, select a work offset, switch on a vacuum, run a
/// tool-length probe, dwell while the spindle comes up. Without somewhere to put it, anyone whose
/// ritual differs from our preamble edits every exported file by hand, every time.
///
/// <para>
/// <b>Where these land is a safety decision, not a formatting one.</b> The start block goes in
/// before <c>G21 G90 G94</c> / <c>G17</c>, so whatever it leaves behind, the program that follows
/// re-establishes millimetres, absolute distance and the XY plane for itself. That makes the worst
/// class of mistake — a header that quietly switches the machine to inches or to incremental, after
/// which every coordinate in the file means something else — impossible rather than merely
/// warned about. The end block goes in after the spindle stops and before <c>M30</c>, because
/// anything after the program end is never read.
/// </para>
/// </summary>
public sealed record ProgramFraming
{
    public static ProgramFraming None { get; } = new();

    /// <summary>
    /// Lines run before anything else, or null to inherit.
    ///
    /// Null and empty mean different things: null is "use the machine default", empty is "this
    /// project deliberately has none". A project that has been told to add nothing should not start
    /// adding something because the machine default changed.
    /// </summary>
    public string? Start { get; init; }

    /// <summary>Lines run after the spindle stops and before the program end, or null to inherit.</summary>
    public string? End { get; init; }

    /// <summary>Derived, so it stays out of the settings file: see the note on Tool's own.</summary>
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Start) && string.IsNullOrWhiteSpace(End);

    /// <summary>
    /// This one's values where it has them, falling back to <paramref name="fallback"/> where it
    /// does not.
    /// </summary>
    public ProgramFraming Over(ProgramFraming? fallback) => fallback is null
        ? this
        : new ProgramFraming
        {
            Start = Start ?? fallback.Start,
            End = End ?? fallback.End,
        };

    /// <summary>
    /// Everything wrong with a block, worst first.
    /// </summary>
    /// <remarks>
    /// Checked before a file is written rather than after it is loaded, because the alternative is
    /// finding out from the machine. The rules are narrow on purpose: this is somebody's own G-code
    /// for their own machine, and refusing things merely because we would not have written them is
    /// how a feature like this stops being useful.
    /// </remarks>
    public static IReadOnlyList<FramingIssue> Check(string? block, bool isEnd = false)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return [];
        }

        var issues = new List<FramingIssue>();
        var lines = block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var number = i + 1;

            CheckComment(line, number, issues);

            var code = Strip(line);

            if (code.Length == 0)
            {
                continue;
            }

            // Ends the program where it stands. In a start block nothing runs at all; in an end
            // block the tool never parks and the file's own M30 is unreachable.
            if (Word(code, 'M', 30) || Word(code, 'M', 2))
            {
                issues.Add(new FramingIssue(
                    number,
                    "ends the program. Everything after this line is never read — including the "
                    + "job itself, if this is the start block.",
                    IsError: true));
            }

            // Shifts the coordinate system. Every coordinate in the program after it means
            // somewhere else, and nothing in the file looks wrong.
            if (Word(code, 'G', 92))
            {
                issues.Add(new FramingIssue(
                    number,
                    "G92 sets a coordinate offset. Every coordinate in the program after it is "
                    + "measured from somewhere else, and the file gives no sign of it. Use a work "
                    + "offset (G54-G59) if you can.",
                    IsError: false));
            }

            // Only matters in the start block, and only before the program's own lift.
            if (!isEnd && MovesInPlane(code))
            {
                issues.Add(new FramingIssue(
                    number,
                    "moves in X or Y before the program has lifted the tool. If the tool is down "
                    + "when this runs, it is dragged across whatever is under it.",
                    IsError: false));
            }
        }

        // Said once for the block rather than per line: they are reassurances, not complaints, and
        // repeating them would bury the things that are.
        Reassure(lines, isEnd, issues);

        return [.. issues.OrderByDescending(i => i.IsError).ThenBy(i => i.Line)];
    }

    /// <summary>
    /// Notes the cases that look alarming and are not, because the program overrides them anyway.
    ///
    /// Worth saying rather than staying quiet: somebody who put <c>G20</c> in their header meant
    /// something by it, and should know it will not survive.
    /// </summary>
    private static void Reassure(string[] lines, bool isEnd, List<FramingIssue> issues)
    {
        if (isEnd)
        {
            return;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var code = Strip(lines[i]);

            if (Word(code, 'G', 20) || Word(code, 'G', 91) || Word(code, 'G', 18) || Word(code, 'G', 19))
            {
                issues.Add(new FramingIssue(
                    i + 1,
                    "this sets a mode the program sets again for itself on the next line "
                    + "(G21 G90 G94 / G17), so it will not affect the job.",
                    IsError: false));
            }
        }
    }

    /// <summary>
    /// A comment must open once, close once, and close before the line ends.
    ///
    /// GRBL reads to the first closing bracket and LinuxCNC refuses a nested opening one, so a
    /// stray bracket is not cosmetic: it either truncates the comment and feeds the remainder to
    /// the parser as code, or it stops the file loading at all.
    /// </summary>
    private static void CheckComment(string line, int number, List<FramingIssue> issues)
    {
        var open = line.IndexOf('(', StringComparison.Ordinal);

        if (open < 0)
        {
            if (line.Contains(')', StringComparison.Ordinal))
            {
                issues.Add(new FramingIssue(number, "closes a comment that was never opened.", true));
            }

            return;
        }

        var close = line.IndexOf(')', open);

        if (close < 0)
        {
            issues.Add(new FramingIssue(number, "opens a comment and never closes it.", true));
            return;
        }

        if (line[(open + 1)..close].Contains('(', StringComparison.Ordinal))
        {
            issues.Add(new FramingIssue(
                number,
                "has a bracket inside a comment. A comment runs to the first closing bracket, so "
                + "this ends early and the rest of the line is read as code.",
                IsError: true));
        }
    }

    private static bool MovesInPlane(string code) =>
        (code.Contains('X', StringComparison.OrdinalIgnoreCase)
            || code.Contains('Y', StringComparison.OrdinalIgnoreCase))
        && (Word(code, 'G', 0) || Word(code, 'G', 1) || Word(code, 'G', 2) || Word(code, 'G', 3));

    private static string Strip(string line)
    {
        var at = line.IndexOf('(', StringComparison.Ordinal);
        var code = at >= 0 ? line[..at] : line;

        at = code.IndexOf(';', StringComparison.Ordinal);
        return (at >= 0 ? code[..at] : code).Trim();
    }

    /// <summary>Whether a word appears with exactly this number, so M3 never matches M30.</summary>
    private static bool Word(string code, char letter, int number)
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
}
