using System.Globalization;

namespace MillBurn.Gerber.Apertures;

/// <summary>
/// An arithmetic expression inside an aperture macro, over the macro's <c>$n</c> parameters.
///
/// Real files rely on this. KiCad's own RoundRect macro contains <c>$1+$1</c>, and thermal and
/// rounded-rectangle pads are common enough that a parser which only accepts bare numbers fails
/// on ordinary boards — silently producing the wrong copper, which is the worst failure mode we
/// have.
///
/// Gerber's operator set is small but has one trap: multiplication is written <c>x</c> or
/// <c>X</c>, the same character that separates aperture parameters. Splitting parameters on 'X'
/// before parsing expressions therefore corrupts them, so the two are lexed together.
/// </summary>
public abstract record MacroExpression
{
    public abstract double Evaluate(IReadOnlyList<double> parameters);

    public static MacroExpression Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parser = new Parser(text);
        var expr = parser.ParseExpression();
        parser.ExpectEnd();
        return expr;
    }

    public sealed record Constant(double Value) : MacroExpression
    {
        public override double Evaluate(IReadOnlyList<double> parameters) => Value;
    }

    /// <summary>A <c>$n</c> reference. Index is 1-based in the file, 0-based here.</summary>
    public sealed record Variable(int Index) : MacroExpression
    {
        public override double Evaluate(IReadOnlyList<double> parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);

            // Unset parameters are zero rather than an error: files legitimately define macros
            // with more placeholders than any one aperture supplies.
            return Index >= 0 && Index < parameters.Count ? parameters[Index] : 0.0;
        }
    }

    public sealed record Unary(char Op, MacroExpression Operand) : MacroExpression
    {
        public override double Evaluate(IReadOnlyList<double> parameters)
        {
            ArgumentNullException.ThrowIfNull(Operand);
            var v = Operand.Evaluate(parameters);
            return Op == '-' ? -v : v;
        }
    }

    public sealed record Binary(char Op, MacroExpression Left, MacroExpression Right) : MacroExpression
    {
        public override double Evaluate(IReadOnlyList<double> parameters)
        {
            ArgumentNullException.ThrowIfNull(Left);
            ArgumentNullException.ThrowIfNull(Right);

            var l = Left.Evaluate(parameters);
            var r = Right.Evaluate(parameters);

            return Op switch
            {
                '+' => l + r,
                '-' => l - r,
                'x' or 'X' => l * r,
                '/' => r == 0 ? 0 : l / r,
                _ => throw new GerberParseException($"Unknown macro operator '{Op}'."),
            };
        }
    }

    /// <summary>Recursive descent over the tiny macro grammar.</summary>
    private sealed class Parser(string text)
    {
        private int _pos;

        public MacroExpression ParseExpression()
        {
            var left = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (_pos >= text.Length)
                {
                    return left;
                }

                var c = text[_pos];
                if (c is not ('+' or '-'))
                {
                    return left;
                }

                _pos++;
                left = new Binary(c, left, ParseTerm());
            }
        }

        private MacroExpression ParseTerm()
        {
            var left = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (_pos >= text.Length)
                {
                    return left;
                }

                var c = text[_pos];
                if (c is not ('x' or 'X' or '/'))
                {
                    return left;
                }

                _pos++;
                left = new Binary(c, left, ParseFactor());
            }
        }

        private MacroExpression ParseFactor()
        {
            SkipWhitespace();
            if (_pos >= text.Length)
            {
                throw new GerberParseException($"Unexpected end of macro expression in '{text}'.");
            }

            var c = text[_pos];

            if (c is '+' or '-')
            {
                _pos++;
                return new Unary(c, ParseFactor());
            }

            if (c == '(')
            {
                _pos++;
                var inner = ParseExpression();
                SkipWhitespace();
                if (_pos >= text.Length || text[_pos] != ')')
                {
                    throw new GerberParseException($"Unbalanced parenthesis in macro expression '{text}'.");
                }

                _pos++;
                return inner;
            }

            if (c == '$')
            {
                _pos++;
                var start = _pos;
                while (_pos < text.Length && char.IsAsciiDigit(text[_pos]))
                {
                    _pos++;
                }

                if (start == _pos)
                {
                    throw new GerberParseException($"'$' with no index in macro expression '{text}'.");
                }

                var index = int.Parse(text.AsSpan(start, _pos - start), CultureInfo.InvariantCulture);
                return new Variable(index - 1);
            }

            if (char.IsAsciiDigit(c) || c == '.')
            {
                var start = _pos;
                while (_pos < text.Length && (char.IsAsciiDigit(text[_pos]) || text[_pos] == '.'))
                {
                    _pos++;
                }

                var span = text.AsSpan(start, _pos - start);
                if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    throw new GerberParseException($"Malformed number '{new string(span)}' in macro expression.");
                }

                return new Constant(value);
            }

            throw new GerberParseException($"Unexpected character '{c}' in macro expression '{text}'.");
        }

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (_pos < text.Length)
            {
                throw new GerberParseException(
                    $"Trailing '{text[_pos..]}' after macro expression '{text}'.");
            }
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
            {
                _pos++;
            }
        }
    }
}
