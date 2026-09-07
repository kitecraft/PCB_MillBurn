using System.Globalization;
using MillBurn.Core;
using MillBurn.Gerber.Model;

namespace MillBurn.Gerber.Excellon;

/// <summary>
/// Parses Excellon drill files.
///
/// Excellon has no single authority behind it, so the dialects differ in the one place that
/// matters most: how a coordinate becomes a number. KiCad writes explicit decimals
/// (<c>X155.465</c>); Altium and Eagle write implicit-decimal integers with either leading or
/// trailing zeros suppressed, which cannot be interpreted without the header. Guessing wrong
/// scales the drill pattern by a power of ten while the file still parses, so the header is read
/// carefully and anything ambiguous is reported.
/// </summary>
public sealed class ExcellonParser
{
    private readonly Dictionary<int, DrillTool> _tools = [];
    private readonly List<DrillHit> _hits = [];
    private readonly List<DrillSlot> _slots = [];
    private readonly List<GerberDiagnostic> _diagnostics = [];

    private LengthUnit _unit = LengthUnit.Millimetres;
    private bool _unitSeen;

    // Implicit-decimal format. KiCad's non-decimal default is 3.3 metric; inch files are 2.4.
    private int _integerDigits = 3;
    private int _decimalDigits = 3;
    private bool _formatSeen;
    private bool _trailingZeroSuppression;

    private bool _inHeader = true;
    private int _currentTool;
    private Point2 _current;
    private Bounds _bounds = Bounds.Empty;
    private HolePlating _plating = HolePlating.Unknown;
    private string? _fileFunction;
    private string? _pendingToolFunction;

    private bool _routing;
    private Point2? _routeFrom;

    public static ExcellonFile Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parser = new ExcellonParser();
        return parser.Run(source);
    }

    public static ExcellonFile ParseFile(string path) => Parse(File.ReadAllText(path));

    private ExcellonFile Run(string source)
    {
        var lineNumber = 0;

        foreach (var raw in source.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim().TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == ';')
            {
                ReadCommentAttribute(line);
                continue;
            }

            try
            {
                if (_inHeader)
                {
                    ReadHeaderLine(line, lineNumber);
                }
                else
                {
                    ReadBodyLine(line, lineNumber);
                }
            }
            catch (GerberParseException ex)
            {
                _diagnostics.Add(new GerberDiagnostic(ex.Message, lineNumber, IsError: true));
            }
        }

        if (!_unitSeen)
        {
            _diagnostics.Add(new GerberDiagnostic(
                "No METRIC or INCH declaration; assumed millimetres.", 0, IsError: true));
        }

        foreach (var tool in _hits.Select(h => h.Tool).Distinct().Where(t => !_tools.ContainsKey(t)))
        {
            _diagnostics.Add(new GerberDiagnostic(
                $"Tool T{tool} is used but never given a diameter.", 0, IsError: true));
        }

        return new ExcellonFile
        {
            Tools = _tools,
            Hits = _hits,
            Slots = _slots,
            Unit = _unit,
            Plating = _plating,
            Bounds = _bounds,
            Diagnostics = _diagnostics,
            FileFunction = _fileFunction,
        };
    }

    /// <summary>
    /// KiCad carries X2 attributes through as <c>; #@! TF...</c> comments, which is how a drill
    /// file declares whether its holes are plated and what each tool is for.
    /// </summary>
    private void ReadCommentAttribute(string line)
    {
        var marker = line.IndexOf("#@!", StringComparison.Ordinal);
        if (marker < 0)
        {
            return;
        }

        var text = line[(marker + 3)..].Trim();

        if (text.StartsWith("TF.FileFunction,", StringComparison.Ordinal))
        {
            _fileFunction = text["TF.FileFunction,".Length..].Trim();
            if (_fileFunction.StartsWith("Plated", StringComparison.OrdinalIgnoreCase))
            {
                _plating = HolePlating.Plated;
            }
            else if (_fileFunction.StartsWith("NonPlated", StringComparison.OrdinalIgnoreCase))
            {
                _plating = HolePlating.NonPlated;
            }

            return;
        }

        if (text.StartsWith("TA.AperFunction,", StringComparison.Ordinal))
        {
            // Applies to the next tool definition, mirroring how %TA% works in Gerber.
            _pendingToolFunction = text["TA.AperFunction,".Length..].Trim();
        }
    }

    private void ReadHeaderLine(string line, int lineNumber)
    {
        if (line is "%" or "M95")
        {
            _inHeader = false;
            return;
        }

        if (line.StartsWith("METRIC", StringComparison.OrdinalIgnoreCase))
        {
            _unit = LengthUnit.Millimetres;
            _unitSeen = true;
            ApplyUnitDefaults();
            ReadZeroSuppression(line);
            return;
        }

        if (line.StartsWith("INCH", StringComparison.OrdinalIgnoreCase))
        {
            _unit = LengthUnit.Inches;
            _unitSeen = true;
            ApplyUnitDefaults();
            ReadZeroSuppression(line);
            return;
        }

        if (line[0] == 'T')
        {
            ReadToolDefinition(line, lineNumber);
            return;
        }

        // M48, FMAT, VER, ICI, detour/feed settings: header noise we do not need.
    }

    private void ApplyUnitDefaults()
    {
        if (_formatSeen)
        {
            return;
        }

        // Conventional implicit-decimal formats. Only used when coordinates lack a decimal point.
        _integerDigits = _unit == LengthUnit.Inches ? 2 : 3;
        _decimalDigits = _unit == LengthUnit.Inches ? 4 : 3;
    }

    private void ReadZeroSuppression(string line)
    {
        // "METRIC,TZ" keeps trailing zeros (so leading are suppressed) and vice versa. The naming
        // is famously back to front: TZ means "trailing zeros are present".
        if (line.Contains("TZ", StringComparison.OrdinalIgnoreCase))
        {
            _trailingZeroSuppression = false;
        }
        else if (line.Contains("LZ", StringComparison.OrdinalIgnoreCase))
        {
            _trailingZeroSuppression = true;
        }

        // An explicit format such as "METRIC,000.000" pins the digit counts.
        var dot = line.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            return;
        }

        var before = 0;
        for (var i = dot - 1; i >= 0 && line[i] == '0'; i--)
        {
            before++;
        }

        var after = 0;
        for (var i = dot + 1; i < line.Length && line[i] == '0'; i++)
        {
            after++;
        }

        if (before <= 0 || after <= 0)
        {
            return;
        }

        _integerDigits = before;
        _decimalDigits = after;
        _formatSeen = true;
    }

    private void ReadToolDefinition(string line, int lineNumber)
    {
        var i = 1;
        while (i < line.Length && char.IsAsciiDigit(line[i]))
        {
            i++;
        }

        if (i == 1)
        {
            throw new GerberParseException($"Tool definition '{line}' has no number.", lineNumber);
        }

        var number = int.Parse(line.AsSpan(1, i - 1), CultureInfo.InvariantCulture);

        var c = line.IndexOf('C', i);
        if (c < 0)
        {
            // A bare "Tn" in the header selects rather than defines; harmless.
            return;
        }

        var start = c + 1;
        var end = start;
        while (end < line.Length && (char.IsAsciiDigit(line[end]) || line[end] == '.'))
        {
            end++;
        }

        if (!double.TryParse(line.AsSpan(start, end - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var diameter))
        {
            throw new GerberParseException($"Tool T{number} has an unreadable diameter.", lineNumber);
        }

        _tools[number] = new DrillTool(number, Nm.From(diameter, _unit), _pendingToolFunction);
        _pendingToolFunction = null;
    }

    private void ReadBodyLine(string line, int lineNumber)
    {
        if (line.StartsWith("M30", StringComparison.Ordinal) || line.StartsWith("M00", StringComparison.Ordinal))
        {
            return;
        }

        // Tool select, possibly with a diameter attached in tool-less files.
        if (line[0] == 'T')
        {
            var i = 1;
            while (i < line.Length && char.IsAsciiDigit(line[i]))
            {
                i++;
            }

            if (i > 1)
            {
                _currentTool = int.Parse(line.AsSpan(1, i - 1), CultureInfo.InvariantCulture);
            }

            if (line.IndexOf('C', i) >= 0)
            {
                ReadToolDefinition(line, lineNumber);
            }

            return;
        }

        if (line.StartsWith("M15", StringComparison.Ordinal))
        {
            _routing = true;
            _routeFrom = _current;
            return;
        }

        if (line.StartsWith("M16", StringComparison.Ordinal) || line.StartsWith("M17", StringComparison.Ordinal))
        {
            _routing = false;
            _routeFrom = null;
            return;
        }

        if (line.StartsWith("G91", StringComparison.Ordinal))
        {
            _diagnostics.Add(new GerberDiagnostic(
                "Incremental coordinates (G91) are not supported.", lineNumber, IsError: true));
            return;
        }

        // A G85 slot is written "X..Y..G85X..Y..": drill from the first point to the second.
        var g85 = line.IndexOf("G85", StringComparison.Ordinal);
        if (g85 >= 0)
        {
            var from = ReadCoordinates(line[..g85], _current);
            var to = ReadCoordinates(line[(g85 + 3)..], from);
            _slots.Add(new DrillSlot(_currentTool, from, to));
            Track(from);
            Track(to);
            _current = to;
            return;
        }

        if (line[0] is 'G' or 'M' or 'F' or 'S')
        {
            // Routing moves carry coordinates on a G00/G01 line; everything else is a mode word.
            if (!line.StartsWith("G0", StringComparison.Ordinal) || line.IndexOf('X', StringComparison.Ordinal) < 0)
            {
                return;
            }
        }

        if (line.IndexOf('X', StringComparison.Ordinal) < 0 && line.IndexOf('Y', StringComparison.Ordinal) < 0)
        {
            return;
        }

        var at = ReadCoordinates(line, _current);

        if (_routing)
        {
            if (_routeFrom is { } start)
            {
                _slots.Add(new DrillSlot(_currentTool, start, at));
            }

            _routeFrom = at;
        }
        else
        {
            _hits.Add(new DrillHit(_currentTool, at));
        }

        Track(at);
        _current = at;
    }

    private Point2 ReadCoordinates(string text, Point2 fallback)
    {
        var x = ReadAxis(text, 'X');
        var y = ReadAxis(text, 'Y');
        return new Point2(x ?? fallback.X, y ?? fallback.Y);
    }

    private long? ReadAxis(string text, char axis)
    {
        var i = text.IndexOf(axis, StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        var start = i + 1;
        var end = start;
        if (end < text.Length && (text[end] is '-' or '+'))
        {
            end++;
        }

        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] == '.'))
        {
            end++;
        }

        if (end == start)
        {
            return null;
        }

        return DecodeCoordinate(text.AsSpan(start, end - start));
    }

    /// <summary>
    /// An explicit decimal point means the value is literal; otherwise the implicit format and
    /// zero-suppression convention from the header decide where the point belongs.
    /// </summary>
    private long DecodeCoordinate(ReadOnlySpan<char> field)
    {
        if (field.Contains('.'))
        {
            return double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var literal)
                ? Nm.From(literal, _unit)
                : throw new GerberParseException($"Unreadable coordinate '{new string(field)}'.");
        }

        var negative = false;
        if (field.Length > 0 && (field[0] is '-' or '+'))
        {
            negative = field[0] == '-';
            field = field[1..];
        }

        var digits = new string(field);

        // With leading zeros suppressed the value is right-aligned on the decimal digits; with
        // trailing zeros suppressed it is left-aligned on the integer digits.
        var padded = _trailingZeroSuppression
            ? digits.PadRight(_integerDigits + _decimalDigits, '0')
            : digits.PadLeft(_integerDigits + _decimalDigits, '0');

        if (!long.TryParse(padded, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
        {
            throw new GerberParseException($"Unreadable coordinate '{digits}'.");
        }

        var scale = _unit == LengthUnit.Millimetres ? (double)Nm.PerMillimetre : Nm.PerInch;
        for (var i = 0; i < _decimalDigits; i++)
        {
            scale /= 10.0;
        }

        var nm = (long)Math.Round(raw * scale, MidpointRounding.AwayFromZero);
        return negative ? -nm : nm;
    }

    private void Track(Point2 p)
    {
        var radius = _tools.TryGetValue(_currentTool, out var tool) ? tool.DiameterNm / 2 : 0;
        _bounds = _bounds
            .Include(new Point2(p.X - radius, p.Y - radius))
            .Include(new Point2(p.X + radius, p.Y + radius));
    }
}
