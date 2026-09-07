namespace MillBurn.Gerber;

/// <summary>Whether a command came from a <c>%...%</c> block or a bare word sequence.</summary>
public enum GerberCommandKind
{
    /// <summary>A word command terminated by <c>*</c>, e.g. <c>X100Y200D01*</c>.</summary>
    Word,

    /// <summary>An extended command inside <c>%...%</c>, e.g. <c>%FSLAX36Y36*%</c>.</summary>
    Extended,
}

/// <summary>One lexed command, with the source line it started on for diagnostics.</summary>
public readonly record struct GerberCommand(GerberCommandKind Kind, string Body, int Line)
{
    public override string ToString() => Kind == GerberCommandKind.Extended ? $"%{Body}%" : $"{Body}*";
}

/// <summary>
/// Splits a Gerber stream into commands.
///
/// The format is deceptively simple — <c>*</c> terminates a word command, <c>%</c> delimits an
/// extended block — but two details matter and are easy to miss:
///
///   An extended block may contain several <c>*</c>-terminated statements before its closing
///   <c>%</c>. Aperture macros rely on this, so the block is kept whole and split later.
///
///   Whitespace and newlines are insignificant *between* words but must not be dropped inside a
///   macro body, where line structure separates primitives.
/// </summary>
public static class GerberLexer
{
    public static List<GerberCommand> Tokenize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var commands = new List<GerberCommand>();
        var buffer = new System.Text.StringBuilder(128);
        var line = 1;
        var startLine = 1;
        var inExtended = false;
        var started = false;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];

            if (c == '\n')
            {
                line++;
            }

            if (c == '%')
            {
                if (inExtended)
                {
                    commands.Add(new GerberCommand(
                        GerberCommandKind.Extended, CloseExtended(buffer.ToString()), startLine));
                    buffer.Clear();
                    inExtended = false;
                    started = false;
                }
                else
                {
                    // A stray word before '%' is malformed but recoverable; keep it.
                    if (buffer.Length > 0 && buffer.ToString().Trim().Length > 0)
                    {
                        commands.Add(new GerberCommand(GerberCommandKind.Word, buffer.ToString().Trim(), startLine));
                    }

                    buffer.Clear();
                    inExtended = true;
                    started = false;
                    startLine = line;
                }

                continue;
            }

            if (c == '*' && !inExtended)
            {
                var body = buffer.ToString().Trim();
                if (body.Length > 0)
                {
                    commands.Add(new GerberCommand(GerberCommandKind.Word, body, startLine));
                }

                buffer.Clear();
                started = false;
                continue;
            }

            if (!started)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                started = true;
                startLine = line;
            }

            // Inside an extended block newlines separate macro primitives, so keep them.
            if (inExtended)
            {
                buffer.Append(c is '\r' ? ' ' : c);
            }
            else if (!char.IsWhiteSpace(c))
            {
                buffer.Append(c);
            }
        }

        var tail = inExtended ? CloseExtended(buffer.ToString()) : buffer.ToString().Trim();
        if (tail.Length > 0)
        {
            commands.Add(new GerberCommand(
                inExtended ? GerberCommandKind.Extended : GerberCommandKind.Word, tail, startLine));
        }

        return commands;
    }

    /// <summary>
    /// Trims the statement terminator that precedes the closing '%'.
    ///
    /// Every extended command ends '...*%', and leaving that '*' attached turns the last
    /// parameter of every aperture definition into a non-number — which is exactly what it did.
    /// Only one is removed: an aperture macro's internal '*' separators must survive, since they
    /// are what divides its primitives.
    /// </summary>
    private static string CloseExtended(string body)
    {
        var trimmed = body.Trim();
        return trimmed.EndsWith('*') ? trimmed[..^1].TrimEnd() : trimmed;
    }
}
