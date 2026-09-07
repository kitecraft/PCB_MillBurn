using System.Globalization;
using MillBurn.Core;

namespace MillBurn.Gerber;

/// <summary>
/// The <c>%FSLAX36Y36*%</c> format specification: how many integer and decimal digits a
/// coordinate carries, and whether leading zeros are omitted.
///
/// Getting this wrong scales the entire board by a power of ten and the file still parses
/// cleanly, so the parser refuses to guess: a file without an FS command is an error, not a
/// default.
/// </summary>
public readonly record struct CoordinateFormat(int IntegerDigits, int DecimalDigits)
{
    /// <summary>What KiCad emits, and a sane assumption only where a file is already invalid.</summary>
    public static CoordinateFormat Default => new(4, 6);

    public bool IsValid => IntegerDigits is >= 1 and <= 6 && DecimalDigits is >= 1 and <= 7;

    /// <summary>
    /// Decodes one coordinate field into nanometres.
    ///
    /// Gerber coordinates are integers with an implied decimal point: with format 4.6, "1500000"
    /// means 1.5 units. Modern files omit leading zeros (the "L" in FSLA), which is irrelevant
    /// when the decimal count is fixed and the value is right-aligned — the trailing-zero-omitted
    /// variant died with RS-274-D and is not supported.
    /// </summary>
    public long Decode(ReadOnlySpan<char> field, LengthUnit unit)
    {
        if (field.IsEmpty)
        {
            throw new GerberParseException("Empty coordinate field.");
        }

        var negative = false;
        if (field[0] is '+' or '-')
        {
            negative = field[0] == '-';
            field = field[1..];
        }

        if (field.IsEmpty)
        {
            throw new GerberParseException("Coordinate field has a sign but no digits.");
        }

        // Some writers emit an explicit decimal point even though the spec forbids it. Accept it
        // rather than mangling the board: a wrong-by-1000x coordinate is far worse than leniency.
        var dot = field.IndexOf('.');
        if (dot >= 0)
        {
            var text = new string(field);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var explicitValue))
            {
                throw new GerberParseException($"Malformed coordinate '{text}'.");
            }

            var nmExplicit = Nm.From(explicitValue, unit);
            return negative ? -nmExplicit : nmExplicit;
        }

        long raw = 0;
        foreach (var c in field)
        {
            if (!char.IsAsciiDigit(c))
            {
                throw new GerberParseException($"Non-digit '{c}' in coordinate '{new string(field)}'.");
            }

            raw = (raw * 10) + (c - '0');
        }

        // raw is in units of 10^-DecimalDigits of the file unit.
        var scale = unit == LengthUnit.Millimetres ? (double)Nm.PerMillimetre : Nm.PerInch;
        for (var i = 0; i < DecimalDigits; i++)
        {
            scale /= 10.0;
        }

        var nm = (long)Math.Round(raw * scale, MidpointRounding.AwayFromZero);
        return negative ? -nm : nm;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{IntegerDigits}.{DecimalDigits}");
}

/// <summary>Raised when a Gerber or Excellon file cannot be understood.</summary>
public sealed class GerberParseException : Exception
{
    public GerberParseException()
    {
    }

    public GerberParseException(string message)
        : base(message)
    {
    }

    public GerberParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public GerberParseException(string message, int lineNumber)
        : base($"Line {lineNumber}: {message}") => LineNumber = lineNumber;

    public int LineNumber { get; }
}
