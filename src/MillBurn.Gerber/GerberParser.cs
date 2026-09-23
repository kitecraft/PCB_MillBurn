using System.Globalization;
using MillBurn.Core;
using MillBurn.Gerber.Apertures;
using MillBurn.Gerber.Model;

namespace MillBurn.Gerber;

/// <summary>
/// Parses RS-274X (Gerber) including the X2 attribute extensions.
///
/// Written from the Ucamco specification rather than ported from gerbv or pcb2gcode, both of
/// which are GPL-3.0 (Documentation/01, section 9).
///
/// The parser deliberately keeps everything the format tells it: aperture identity, polarity
/// ordering, arcs as arcs, and X2 attributes. Discarding those is what forces every other tool
/// into guesswork later.
/// </summary>
public sealed class GerberParser
{
    private readonly List<GraphicObject> _objects = [];
    private readonly Dictionary<int, Aperture> _apertures = [];
    private readonly Dictionary<string, ApertureMacro> _macros = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fileAttributes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _apertureAttributes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _objectAttributes = new(StringComparer.Ordinal);
    private readonly List<GerberDiagnostic> _diagnostics = [];

    private CoordinateFormat _format;
    private bool _formatSeen;
    private LengthUnit _unit = LengthUnit.Millimetres;
    private bool _unitSeen;

    private Point2 _current;
    private Aperture? _aperture;
    private Polarity _polarity = Polarity.Dark;
    private SegmentKind _interpolation = SegmentKind.Linear;
    private bool _multiQuadrant;

    private bool _inRegion;

    /// <summary>
    /// The object attributes in force when <c>G36</c> opened the region.
    ///
    /// A region is created at <c>G37</c> and used to read its attributes there, which is the same
    /// shape of bug the strokes had: a <c>%TD*%</c> between the two strips the region's net, and
    /// the copper is already drawn by then. Held from the start instead, and anything set *during*
    /// the region still applies on top — so a writer that names the net inside the G36/G37 pair is
    /// honoured, and one that clears attributes inside it does not silently lose the net.
    /// </summary>
    private Dictionary<string, string>? _regionAttributes;

    private List<List<GerberSegment>>? _regionContours;
    private List<GerberSegment>? _regionContour;

    private List<GerberSegment>? _openStroke;

    // Step and repeat: %SRX3Y2I5.0J4.0*% duplicates everything until the next %SR*%.
    private int _srX = 1;
    private int _srY = 1;
    private long _srIStep;
    private long _srJStep;
    private int _srStartIndex;
    private bool _srActive;

    private Bounds _bounds = Bounds.Empty;

    public static GerberImage Parse(string source)
    {
        var parser = new GerberParser();
        return parser.Run(source);
    }

    public static GerberImage ParseFile(string path) => Parse(File.ReadAllText(path));

    private GerberImage Run(string source)
    {
        var commands = GerberLexer.Tokenize(source);

        foreach (var command in commands)
        {
            try
            {
                if (command.Kind == GerberCommandKind.Extended)
                {
                    ExecuteExtended(command);
                }
                else
                {
                    ExecuteWord(command);
                }
            }
            catch (GerberParseException ex)
            {
                // One malformed command must not lose the rest of the board. Record and continue;
                // the parse report surfaces these so a wrong result is never silent.
                _diagnostics.Add(new GerberDiagnostic(ex.Message, command.Line, IsError: true));
            }
        }

        FlushStroke(0);

        if (!_formatSeen)
        {
            _diagnostics.Add(new GerberDiagnostic(
                "No %FS% format specification; coordinates may be scaled wrongly.", 0, IsError: true));
        }

        if (!_unitSeen)
        {
            _diagnostics.Add(new GerberDiagnostic(
                "No %MO% unit specification; assumed millimetres.", 0, IsError: false));
        }

        return new GerberImage
        {
            Objects = _objects,
            Apertures = _apertures,
            FileAttributes = _fileAttributes,
            Unit = _unit,
            Format = _formatSeen ? _format : CoordinateFormat.Default,
            Bounds = _bounds,
            Diagnostics = _diagnostics,
        };
    }

    // ---------------------------------------------------------------- extended commands

    private void ExecuteExtended(GerberCommand command)
    {
        var body = command.Body;
        if (body.Length < 2)
        {
            return;
        }

        var code = body[..2];

        switch (code)
        {
            case "FS":
                ParseFormat(body, command.Line);
                return;

            case "MO":
                _unit = body.Contains("IN", StringComparison.Ordinal) ? LengthUnit.Inches : LengthUnit.Millimetres;
                _unitSeen = true;
                return;

            case "AM":
                {
                    var macro = ApertureMacro.Parse(body);
                    _macros[macro.Name] = macro;
                    return;
                }

            case "AD":
                {
                    var aperture = Aperture.Parse(
                        body[3..].TrimStart(), _unit, _macros, _apertureAttributes);
                    _apertures[aperture.Code] = aperture;
                    return;
                }

            case "LP":
                FlushStroke(command.Line);
                _polarity = body.Length > 2 && (body[2] is 'C' or 'c') ? Polarity.Clear : Polarity.Dark;
                return;

            case "SR":
                ApplyStepAndRepeat(body, command.Line);
                return;

            case "TF":
                StoreAttribute(_fileAttributes, body[2..]);
                return;

            case "TA":
                StoreAttribute(_apertureAttributes, body[2..]);
                return;

            // Flushed first, for the same reason LP above is: an attribute statement applies to the
            // objects that come after it, and a stroke already drawn is not one of them. Without
            // this the open stroke is emitted later and snapshots whatever the net has become by
            // then, so the last trace before a net change is filed under the next net. KiCad writes
            // exactly that shape — several D01 runs, then the next %TO.N — and on the test board it
            // put a J3-Pin_1 trace under J3-Pin_2, which is a short report naming the wrong net.
            case "TO":
                FlushStroke(command.Line);
                StoreAttribute(_objectAttributes, body[2..]);
                return;

            case "TD":
                FlushStroke(command.Line);
                DeleteAttribute(body[2..]);
                return;

            case "AS":
            case "IP":
            case "IR":
            case "MI":
            case "OF":
            case "SF":
            case "IN":
            case "LN":
                // Deprecated or presentation-only. Ignored deliberately: honouring OF/SF/MI would
                // silently move geometry, and no modern writer emits them.
                _diagnostics.Add(new GerberDiagnostic(
                    $"Ignoring deprecated command '{code}'.", command.Line, IsError: false));
                return;

            case "AB":
                _diagnostics.Add(new GerberDiagnostic(
                    "Block apertures (%AB%) are not implemented yet.", command.Line, IsError: true));
                return;

            case "LM":
            case "LR":
            case "LS":
                _diagnostics.Add(new GerberDiagnostic(
                    $"Aperture transform '{code}' is not implemented yet.", command.Line, IsError: true));
                return;

            default:
                _diagnostics.Add(new GerberDiagnostic(
                    $"Unknown extended command '{code}'.", command.Line, IsError: false));
                return;
        }
    }

    private void ParseFormat(string body, int line)
    {
        var x = body.IndexOf('X', StringComparison.Ordinal);
        var y = body.IndexOf('Y', StringComparison.Ordinal);
        if (x < 0 || y < 0 || x + 2 >= body.Length)
        {
            throw new GerberParseException($"Malformed format specification '%{body}%'.", line);
        }

        var integers = body[x + 1] - '0';
        var decimals = body[x + 2] - '0';

        var format = new CoordinateFormat(integers, decimals);
        if (!format.IsValid)
        {
            throw new GerberParseException($"Coordinate format {format} is out of range.", line);
        }

        // A file whose X and Y formats differ is legal but vanishingly rare, and supporting it
        // would thread a second format through every coordinate. Warn rather than mis-scale.
        if (y + 2 < body.Length)
        {
            var yi = body[y + 1] - '0';
            var yd = body[y + 2] - '0';
            if (yi != integers || yd != decimals)
            {
                _diagnostics.Add(new GerberDiagnostic(
                    $"X format {integers}.{decimals} differs from Y format {yi}.{yd}; using the X format.",
                    line, IsError: true));
            }
        }

        _format = format;
        _formatSeen = true;
    }

    private static void StoreAttribute(Dictionary<string, string> target, string text)
    {
        var comma = text.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            target[text.Trim()] = string.Empty;
            return;
        }

        target[text[..comma].Trim()] = text[(comma + 1)..].Trim();
    }

    private void DeleteAttribute(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            _objectAttributes.Clear();
            _apertureAttributes.Clear();
            return;
        }

        _objectAttributes.Remove(name);
        _apertureAttributes.Remove(name);
    }

    private void ApplyStepAndRepeat(string body, int line)
    {
        if (_srActive)
        {
            ExpandStepAndRepeat();
        }

        var x = ReadIntField(body, 'X', 1);
        var y = ReadIntField(body, 'Y', 1);
        var i = ReadDoubleField(body, 'I', 0);
        var j = ReadDoubleField(body, 'J', 0);

        if (x <= 1 && y <= 1)
        {
            _srActive = false;
            return;
        }

        _srX = x;
        _srY = y;
        _srIStep = Nm.From(i, _unit);
        _srJStep = Nm.From(j, _unit);
        _srStartIndex = _objects.Count;
        _srActive = true;
        _ = line;
    }

    /// <summary>Duplicates everything emitted since %SR% began across the repeat grid.</summary>
    private void ExpandStepAndRepeat()
    {
        var template = _objects.GetRange(_srStartIndex, _objects.Count - _srStartIndex);
        _srActive = false;

        if (template.Count == 0)
        {
            return;
        }

        for (var iy = 0; iy < _srY; iy++)
        {
            for (var ix = 0; ix < _srX; ix++)
            {
                if (ix == 0 && iy == 0)
                {
                    continue;
                }

                var offset = new Point2(_srIStep * ix, _srJStep * iy);
                foreach (var obj in template)
                {
                    _objects.Add(Translate(obj, offset));
                }
            }
        }
    }

    private GraphicObject Translate(GraphicObject obj, Point2 offset)
    {
        switch (obj)
        {
            case FlashObject f:
                {
                    var at = f.At + offset;
                    Track(at);
                    return f with { At = at };
                }

            case DrawObject d:
                {
                    var segments = new List<GerberSegment>(d.Segments.Count);
                    foreach (var s in d.Segments)
                    {
                        segments.Add(TranslateSegment(s, offset));
                    }

                    return d with { Segments = segments };
                }

            case RegionObject r:
                {
                    var contours = new List<IReadOnlyList<GerberSegment>>(r.Contours.Count);
                    foreach (var contour in r.Contours)
                    {
                        var moved = new List<GerberSegment>(contour.Count);
                        foreach (var s in contour)
                        {
                            moved.Add(TranslateSegment(s, offset));
                        }

                        contours.Add(moved);
                    }

                    return r with { Contours = contours };
                }

            default:
                return obj;
        }
    }

    private GerberSegment TranslateSegment(GerberSegment s, Point2 offset)
    {
        var from = s.From + offset;
        var to = s.To + offset;
        Track(from);
        Track(to);
        return s with { From = from, To = to, Centre = s.IsArc ? s.Centre + offset : s.Centre };
    }

    private static int ReadIntField(string body, char key, int fallback)
    {
        var value = ReadField(body, key);
        return value is null || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? fallback
            : n;
    }

    private static double ReadDoubleField(string body, char key, double fallback)
    {
        var value = ReadField(body, key);
        return value is null || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? fallback
            : d;
    }

    private static string? ReadField(string body, char key)
    {
        var i = body.IndexOf(key, 2);
        if (i < 0)
        {
            return null;
        }

        var start = i + 1;
        var end = start;
        while (end < body.Length && (char.IsAsciiDigit(body[end]) || body[end] is '.' or '-' or '+'))
        {
            end++;
        }

        return end > start ? body[start..end] : null;
    }

    // ---------------------------------------------------------------- word commands

    private void ExecuteWord(GerberCommand command)
    {
        var body = command.Body;

        if (body.StartsWith("G04", StringComparison.Ordinal) || body.StartsWith("G4", StringComparison.Ordinal))
        {
            ParseCommentAttribute(body, command.Line);
            return;
        }

        if (body.StartsWith("M0", StringComparison.Ordinal))
        {
            FlushStroke(command.Line);
            if (_srActive)
            {
                ExpandStepAndRepeat();
            }

            return;
        }

        var index = 0;
        long? x = null, y = null, i = null, j = null;
        int? dCode = null;

        while (index < body.Length)
        {
            var letter = body[index];
            index++;

            var start = index;
            while (index < body.Length && !char.IsAsciiLetter(body[index]))
            {
                index++;
            }

            var field = body.AsSpan(start, index - start);

            switch (letter)
            {
                case 'G':
                    ApplyGCode(ParseInt(field), command.Line);
                    break;

                case 'X':
                    x = _format.Decode(field, _unit);
                    break;

                case 'Y':
                    y = _format.Decode(field, _unit);
                    break;

                case 'I':
                    i = _format.Decode(field, _unit);
                    break;

                case 'J':
                    j = _format.Decode(field, _unit);
                    break;

                case 'D':
                    {
                        var d = ParseInt(field);
                        if (d >= 10)
                        {
                            // Aperture selection must take effect where it appears, not after the
                            // whole block is read. Files in the wild write a bare "D10" with no '*'
                            // terminator, which merges with the following line into
                            // "D10X0Y0D03" - so deferring would let the D03 overwrite the D10 and
                            // the flash would find no aperture selected.
                            SelectAperture(d, command.Line);
                        }
                        else
                        {
                            dCode = d;
                        }

                        break;
                    }

                case 'M':
                    break;

                default:
                    _diagnostics.Add(new GerberDiagnostic(
                        $"Ignoring unknown word '{letter}{new string(field)}'.", command.Line, IsError: false));
                    break;
            }
        }

        if (x is null && y is null && dCode is null)
        {
            return;
        }

        // Coordinates are modal: an omitted X or Y keeps its previous value.
        var target = new Point2(x ?? _current.X, y ?? _current.Y);

        switch (dCode)
        {
            case 1:
                Interpolate(target, i, j, command.Line);
                break;

            case 2:
                FlushStroke(command.Line);
                if (_inRegion)
                {
                    StartRegionContour();
                }

                _current = target;
                Track(_current);
                break;

            case 3:
                Flash(target, command.Line);
                break;

            case null:
                // A bare coordinate repeats the previous operation. Modal D-codes are legal and
                // real files use them, so treat it as another interpolation.
                Interpolate(target, i, j, command.Line);
                break;

            default:
                _diagnostics.Add(new GerberDiagnostic(
                    $"Unsupported D-code D{dCode:00}.", command.Line, IsError: true));
                break;
        }
    }

    /// <summary>
    /// KiCad and several other writers emit X2 attributes as <c>G04 #@! TF.FileFunction,...*</c>
    /// comments rather than <c>%TF...*%</c> blocks. Missing this means missing every attribute on
    /// files produced by the most common EDA tool there is.
    /// </summary>
    private void ParseCommentAttribute(string body, int line)
    {
        var marker = body.IndexOf("#@!", StringComparison.Ordinal);
        if (marker < 0)
        {
            return;
        }

        var text = body[(marker + 3)..].Trim();
        if (text.Length < 3)
        {
            return;
        }

        var code = text[..2];
        var payload = text[2..];

        switch (code)
        {
            case "TF":
                StoreAttribute(_fileAttributes, payload);
                break;
            case "TA":
                StoreAttribute(_apertureAttributes, payload);
                break;
            // As above: the stroke in hand was drawn under the old net and keeps it.
            case "TO":
                FlushStroke(line);
                StoreAttribute(_objectAttributes, payload);
                break;
            case "TD":
                FlushStroke(line);
                DeleteAttribute(payload);
                break;
            default:
                _ = line;
                break;
        }
    }

    private void ApplyGCode(int code, int line)
    {
        switch (code)
        {
            case 1:
                FlushStroke(line);
                _interpolation = SegmentKind.Linear;
                break;

            case 2:
                FlushStroke(line);
                _interpolation = SegmentKind.ClockwiseArc;
                break;

            case 3:
                FlushStroke(line);
                _interpolation = SegmentKind.CounterClockwiseArc;
                break;

            case 36:
                FlushStroke(line);
                _inRegion = true;
                _regionAttributes = SnapshotObjectAttributes();
                _regionContours = [];
                _regionContour = null;
                break;

            case 37:
                EndRegion(line);
                break;

            case 74:
                _multiQuadrant = false;
                break;

            case 75:
                _multiQuadrant = true;
                break;

            case 54:
            case 55:
                // Deprecated tool-select prefixes; the D-code in the same block does the work.
                break;

            case 70:
                _unit = LengthUnit.Inches;
                _unitSeen = true;
                break;

            case 71:
                _unit = LengthUnit.Millimetres;
                _unitSeen = true;
                break;

            case 90:
            case 91:
                // Absolute / incremental. Incremental notation is deprecated and unsupported;
                // warn rather than silently misplacing every coordinate.
                if (code == 91)
                {
                    _diagnostics.Add(new GerberDiagnostic(
                        "Incremental coordinates (G91) are not supported.", line, IsError: true));
                }

                break;

            default:
                _diagnostics.Add(new GerberDiagnostic(
                    $"Ignoring unknown G-code G{code:00}.", line, IsError: false));
                break;
        }
    }

    private void SelectAperture(int code, int line)
    {
        FlushStroke(line);

        if (_apertures.TryGetValue(code, out var aperture))
        {
            _aperture = aperture;
            return;
        }

        _diagnostics.Add(new GerberDiagnostic($"Aperture D{code} used before definition.", line, IsError: true));
        _aperture = null;
    }

    private void Interpolate(Point2 target, long? i, long? j, int line)
    {
        var segment = BuildSegment(_current, target, i, j, line);

        if (_inRegion)
        {
            _regionContour ??= [];
            _regionContour.Add(segment);
        }
        else
        {
            _openStroke ??= [];
            _openStroke.Add(segment);
        }

        _current = target;
        Track(_current);
        TrackArc(segment);
    }

    private GerberSegment BuildSegment(Point2 from, Point2 to, long? i, long? j, int line)
    {
        if (_interpolation == SegmentKind.Linear)
        {
            return GerberSegment.Line(from, to);
        }

        if (i is null && j is null)
        {
            // Common and recoverable: a file leaves G02/G03 in force from an earlier block and
            // then draws plain segments. Every renderer treats these as lines, so do the same and
            // report it as a warning rather than failing the layer.
            _diagnostics.Add(new GerberDiagnostic(
                "Arc mode active but no I/J offset given; treated as a straight line.",
                line, IsError: false));
            return GerberSegment.Line(from, to);
        }

        var di = i ?? 0;
        var dj = j ?? 0;

        if (_multiQuadrant)
        {
            return new GerberSegment(_interpolation, from, to, new Point2(from.X + di, from.Y + dj));
        }

        // Single-quadrant mode: I and J are unsigned magnitudes and the correct signs must be
        // recovered by testing all four combinations. Deprecated, but old files still use it.
        var best = new Point2(from.X + di, from.Y + dj);
        var bestError = double.MaxValue;

        foreach (var sx in (ReadOnlySpan<int>)[1, -1])
        {
            foreach (var sy in (ReadOnlySpan<int>)[1, -1])
            {
                var candidate = new Point2(from.X + (di * sx), from.Y + (dj * sy));
                var r0 = candidate.DistanceTo(from);
                var r1 = candidate.DistanceTo(to);
                var error = Math.Abs(r0 - r1);

                if (error >= bestError || !IsWithinOneQuadrant(candidate, from, to, _interpolation))
                {
                    continue;
                }

                bestError = error;
                best = candidate;
            }
        }

        return new GerberSegment(_interpolation, from, to, best);
    }

    private static bool IsWithinOneQuadrant(Point2 centre, Point2 from, Point2 to, SegmentKind kind)
    {
        var a0 = Math.Atan2(from.Y - centre.Y, from.X - centre.X);
        var a1 = Math.Atan2(to.Y - centre.Y, to.X - centre.X);
        var sweep = a1 - a0;

        if (kind == SegmentKind.CounterClockwiseArc)
        {
            while (sweep <= 0)
            {
                sweep += Math.Tau;
            }
        }
        else
        {
            while (sweep >= 0)
            {
                sweep -= Math.Tau;
            }

            sweep = -sweep;
        }

        return sweep <= (Math.PI / 2) + 1e-6;
    }

    private void Flash(Point2 at, int line)
    {
        FlushStroke(line);

        if (_aperture is null)
        {
            _diagnostics.Add(new GerberDiagnostic("Flash with no aperture selected.", line, IsError: true));
            _current = at;
            return;
        }

        if (_inRegion)
        {
            _diagnostics.Add(new GerberDiagnostic("Flash inside a region is not allowed.", line, IsError: true));
            _current = at;
            return;
        }

        _objects.Add(new FlashObject
        {
            Aperture = _aperture,
            At = at,
            Polarity = _polarity,
            SourceLine = line,
            Attributes = SnapshotObjectAttributes(),
        });

        _current = at;
        Track(at);
        TrackAperture(at, _aperture);
    }

    private void StartRegionContour()
    {
        if (_regionContour is { Count: > 0 })
        {
            _regionContours!.Add(_regionContour);
        }

        _regionContour = null;
    }

    private void EndRegion(int line)
    {
        if (!_inRegion)
        {
            return;
        }

        StartRegionContour();
        _inRegion = false;

        var contours = _regionContours;
        var opened = _regionAttributes;
        _regionContours = null;
        _regionContour = null;
        _regionAttributes = null;

        if (contours is null || contours.Count == 0)
        {
            return;
        }

        _objects.Add(new RegionObject
        {
            Contours = contours.ConvertAll(c => (IReadOnlyList<GerberSegment>)c),
            Polarity = _polarity,
            SourceLine = line,
            Attributes = AttributesForRegion(opened),
        });
    }

    /// <summary>
    /// Emits the accumulated stroke as one object. Batching consecutive D01s into a single
    /// DrawObject keeps the object count near the number of traces rather than the number of
    /// segments, which matters on boards with hundreds of thousands of moves.
    /// </summary>
    private void FlushStroke(int line)
    {
        if (_openStroke is not { Count: > 0 })
        {
            _openStroke = null;
            return;
        }

        if (_aperture is null)
        {
            _diagnostics.Add(new GerberDiagnostic("Draw with no aperture selected.", line, IsError: true));
            _openStroke = null;
            return;
        }

        _objects.Add(new DrawObject
        {
            Aperture = _aperture,
            Segments = _openStroke,
            Polarity = _polarity,
            SourceLine = line,
            Attributes = SnapshotObjectAttributes(),
        });

        foreach (var s in _openStroke)
        {
            TrackAperture(s.From, _aperture);
            TrackAperture(s.To, _aperture);
        }

        _openStroke = null;
    }

    /// <summary>
    /// What was in force when the region opened, with anything set since laid over it. Neither
    /// alone is right: the opening set alone would ignore a writer that names the net inside the
    /// pair, and the closing set alone loses it to a <c>%TD*%</c> that arrives after the copper is
    /// drawn.
    /// </summary>
    private Dictionary<string, string> AttributesForRegion(Dictionary<string, string>? opened)
    {
        if (opened is null || opened.Count == 0)
        {
            return SnapshotObjectAttributes();
        }

        if (_objectAttributes.Count == 0)
        {
            return opened;
        }

        var merged = new Dictionary<string, string>(opened, StringComparer.Ordinal);

        foreach (var (key, value) in _objectAttributes)
        {
            merged[key] = value;
        }

        return merged;
    }

    private Dictionary<string, string> SnapshotObjectAttributes() =>
        _objectAttributes.Count == 0
            ? EmptyAttributes
            : new Dictionary<string, string>(_objectAttributes, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> EmptyAttributes = new(StringComparer.Ordinal);

    private void Track(Point2 p) => _bounds = _bounds.Include(p);

    private void TrackAperture(Point2 p, Aperture aperture)
    {
        var halfW = Math.Max(aperture.NominalWidthNm, 0) / 2;
        var halfH = Math.Max(aperture.NominalHeightNm, 0) / 2;
        _bounds = _bounds
            .Include(new Point2(p.X - halfW, p.Y - halfH))
            .Include(new Point2(p.X + halfW, p.Y + halfH));
    }

    private void TrackArc(GerberSegment segment)
    {
        if (!segment.IsArc)
        {
            return;
        }

        // Cheap and safe: the arc never leaves the circle through its endpoints.
        var radius = (long)Math.Round(segment.Centre.DistanceTo(segment.From));
        _bounds = _bounds
            .Include(new Point2(segment.Centre.X - radius, segment.Centre.Y - radius))
            .Include(new Point2(segment.Centre.X + radius, segment.Centre.Y + radius));
    }

    private static int ParseInt(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return 0;
        }

        return int.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}
