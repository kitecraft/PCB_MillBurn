using System.Globalization;

namespace MillBurn.Gerber.Apertures;

/// <summary>The primitive codes defined by the Gerber specification.</summary>
public enum MacroPrimitiveKind
{
    Comment = 0,
    Circle = 1,

    /// <summary>Deprecated alias for <see cref="VectorLine"/>.</summary>
    VectorLineDeprecated = 2,
    Outline = 4,
    Polygon = 5,
    Moire = 6,
    Thermal = 7,
    VectorLine = 20,
    CenterLine = 21,

    /// <summary>Deprecated: a rectangle given by its lower-left corner.</summary>
    LowerLeftLine = 22,
}

/// <summary>One primitive statement in a macro body, with its arguments still unevaluated.</summary>
public sealed record MacroPrimitive(MacroPrimitiveKind Kind, IReadOnlyList<MacroExpression> Arguments)
{
    public double Arg(IReadOnlyList<double> parameters, int index, double fallback = 0.0) =>
        index >= 0 && index < Arguments.Count ? Arguments[index].Evaluate(parameters) : fallback;
}

/// <summary>An assignment statement, <c>$4=$1x0.75</c>, which mutates the parameter list.</summary>
public sealed record MacroAssignment(int Index, MacroExpression Value);

/// <summary>
/// A parsed <c>%AM...%</c> aperture macro: an ordered program of primitives and assignments that
/// is replayed with a specific parameter list each time an aperture instantiates it.
/// </summary>
public sealed class ApertureMacro(string name, IReadOnlyList<object> statements)
{
    public string Name { get; } = name;

    /// <summary>Each element is a <see cref="MacroPrimitive"/> or a <see cref="MacroAssignment"/>.</summary>
    public IReadOnlyList<object> Statements { get; } = statements;

    /// <summary>
    /// Parses a macro body. The body arrives as the inside of a <c>%...%</c> block: a name line
    /// followed by <c>*</c>-separated statements.
    /// </summary>
    public static ApertureMacro Parse(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var parts = body.Split('*', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new GerberParseException("Empty aperture macro.");
        }

        var name = parts[0].Trim();
        if (name.StartsWith("AM", StringComparison.Ordinal))
        {
            name = name[2..].Trim();
        }

        if (name.Length == 0)
        {
            throw new GerberParseException("Aperture macro has no name.");
        }

        var statements = new List<object>();

        for (var i = 1; i < parts.Length; i++)
        {
            var statement = parts[i].Trim();
            if (statement.Length == 0)
            {
                continue;
            }

            // A primitive-0 comment runs to the end of its statement and may contain anything at
            // all, including '$' and ',', so it must be recognised before any splitting.
            if (statement[0] == '0' && (statement.Length == 1 || !char.IsAsciiDigit(statement[1])))
            {
                continue;
            }

            if (statement[0] == '$')
            {
                statements.Add(ParseAssignment(statement));
                continue;
            }

            statements.Add(ParsePrimitive(statement));
        }

        return new ApertureMacro(name, statements);
    }

    private static MacroAssignment ParseAssignment(string statement)
    {
        var eq = statement.IndexOf('=', StringComparison.Ordinal);
        if (eq < 0)
        {
            throw new GerberParseException($"Malformed macro assignment '{statement}'.");
        }

        var lhs = statement[1..eq].Trim();
        if (!int.TryParse(lhs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 1)
        {
            throw new GerberParseException($"Malformed macro variable '${lhs}'.");
        }

        return new MacroAssignment(index - 1, MacroExpression.Parse(statement[(eq + 1)..]));
    }

    private static MacroPrimitive ParsePrimitive(string statement)
    {
        var fields = SplitArguments(statement);
        if (fields.Count == 0)
        {
            throw new GerberParseException($"Empty macro primitive '{statement}'.");
        }

        if (!int.TryParse(fields[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            throw new GerberParseException($"Macro primitive code '{fields[0]}' is not a number.");
        }

        if (!Enum.IsDefined(typeof(MacroPrimitiveKind), code))
        {
            throw new GerberParseException($"Unknown macro primitive code {code}.");
        }

        var args = new List<MacroExpression>(fields.Count - 1);
        for (var i = 1; i < fields.Count; i++)
        {
            args.Add(MacroExpression.Parse(fields[i]));
        }

        var kind = (MacroPrimitiveKind)code;
        if (kind == MacroPrimitiveKind.VectorLineDeprecated)
        {
            kind = MacroPrimitiveKind.VectorLine;
        }

        return new MacroPrimitive(kind, args);
    }

    /// <summary>
    /// Splits on commas only at parenthesis depth zero, so an argument may itself be a
    /// parenthesised expression.
    /// </summary>
    private static List<string> SplitArguments(string statement)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < statement.Length; i++)
        {
            var c = statement[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                result.Add(statement[start..i]);
                start = i + 1;
            }
        }

        result.Add(statement[start..]);
        return result;
    }
}
