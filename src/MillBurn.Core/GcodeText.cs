namespace MillBurn.Core;

/// <summary>
/// Reading G-code as text, for the checks that have to look at a line before anything runs it.
///
/// **One implementation, because three disagreed and two of them were wrong.** `GcodeParser` took
/// comments out correctly; `DryRun` and `ProgramFraming` each carried a six-line copy that
/// truncated the line at its first bracket instead. So a line whose code sat *behind* a comment —
/// <c>( touch off ) G1 Z-1.0 F50</c>, which is how people write headers — looked completely blank
/// to both of them while the machine, the backplot and the parser all saw a real move.
///
/// What that cost: every dry-run refusal went silent on such a line, the raise skipped it, and the
/// clearance check never measured it. A dry run would hand back a file containing a verbatim
/// plunge into the work, under a header promising every Z three millimetres higher. The framing
/// check lost its errors the same way, on the real program rather than the dry run.
/// </summary>
public static class GcodeText
{
    /// <summary>
    /// A line with its comments removed, trimmed — the code a controller would act on.
    ///
    /// Both spellings: <c>;</c> to end of line, and balanced <c>(...)</c> spans anywhere in it.
    /// An unclosed <c>(</c> takes the rest of the line, which is what a controller does with it.
    /// Removing the span rather than truncating at it is the whole point: what follows a comment
    /// is code.
    /// </summary>
    public static string WithoutComments(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var semicolon = line.IndexOf(';', StringComparison.Ordinal);
        if (semicolon >= 0)
        {
            line = line[..semicolon];
        }

        var open = line.IndexOf('(', StringComparison.Ordinal);
        while (open >= 0)
        {
            var close = line.IndexOf(')', open);
            line = close < 0 ? line[..open] : line.Remove(open, close - open + 1);
            open = line.IndexOf('(', StringComparison.Ordinal);
        }

        return line.Trim();
    }
}
