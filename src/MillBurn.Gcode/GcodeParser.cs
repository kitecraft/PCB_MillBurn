using System.Globalization;
using MillBurn.Core;

namespace MillBurn.Gcode;

/// <summary>How a move gets from one point to the next.</summary>
public enum MoveKind
{
    Rapid,
    Feed,
    ArcClockwise,
    ArcCounterClockwise,
}

/// <summary>One motion, with everything needed to draw it and to cost it.</summary>
public readonly record struct GcodeMove(
    MoveKind Kind,
    Point2 From,
    Point2 To,
    long FromZNm,
    long ToZNm,
    Point2 Centre,
    double FeedMmPerMin,
    int Line)
{
    public bool IsArc => Kind is MoveKind.ArcClockwise or MoveKind.ArcCounterClockwise;

    public bool IsRapid => Kind is MoveKind.Rapid;

    /// <summary>True when the tool moves in XY at all.</summary>
    public bool MovesInPlane => From != To;

    /// <summary>True when only Z changes: a plunge or a retract.</summary>
    public bool IsVertical => From == To && FromZNm != ToZNm;

    /// <summary>Deepest point of the move — what decides whether it is cutting.</summary>
    public long DeepestZNm => Math.Min(FromZNm, ToZNm);

    public double LengthNm
    {
        get
        {
            if (IsArc)
            {
                var segment = new ArtSegment(
                    Kind == MoveKind.ArcClockwise ? ArtSweep.Clockwise : ArtSweep.CounterClockwise,
                    From,
                    To,
                    Centre);
                return segment.RadiusNm * segment.SweptAngle();
            }

            var dz = (double)(ToZNm - FromZNm);
            var dxy = From.DistanceTo(To);
            return Math.Sqrt((dxy * dxy) + (dz * dz));
        }
    }
}

/// <summary>Something the parser could not make sense of.</summary>
public readonly record struct GcodeDiagnostic(string Message, int Line, bool IsError)
{
    public override string ToString() => $"{(IsError ? "error" : "warning")} (line {Line}): {Message}";
}

/// <summary>A parsed program.</summary>
public sealed class GcodeProgram
{
    public required IReadOnlyList<GcodeMove> Moves { get; init; }

    public required IReadOnlyList<GcodeDiagnostic> Diagnostics { get; init; }

    public required int LineCount { get; init; }

    public Bounds Bounds
    {
        get
        {
            var bounds = Core.Bounds.Empty;
            foreach (var move in Moves)
            {
                bounds = bounds.Include(move.From).Include(move.To);
            }

            return bounds;
        }
    }
}

/// <summary>
/// A modal-state G-code interpreter.
///
/// It exists so the viewer can draw **the file that will actually run**, rather than a second
/// rendering of the intent that produced it (Documentation/05, section 2.1). Those two are the same
/// picture right up until the emitter has a bug, and then only one of them is the truth.
///
/// Modal state is the whole difficulty: a line saying only <c>X10</c> inherits its motion mode,
/// its feed, its units, its plane and every axis it does not mention. Getting that wrong produces
/// a plausible-looking backplot of a program that does something else.
/// </summary>
public static class GcodeParser
{
    public static GcodeProgram Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var moves = new List<GcodeMove>();
        var diagnostics = new List<GcodeDiagnostic>();

        var motion = MoveKind.Rapid;
        var absolute = true;
        var metric = true;
        var feed = 0.0;

        var x = 0L;
        var y = 0L;
        var z = 0L;

        var lineNumber = 0;

        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = StripComments(raw);
            if (line.Length == 0)
            {
                continue;
            }

            double? nx = null, ny = null, nz = null, ni = null, nj = null;
            var moved = false;
            var motionThisLine = false;

            foreach (var (letter, value) in Words(line, lineNumber, diagnostics))
            {
                switch (letter)
                {
                    case 'G':
                        // Floor, not round, and the fraction kept: a G word can carry one. G38.2
                        // is probing and G91.1 is arc-centre mode — and rounding turned that
                        // second one into G91, so a file that merely stated how it measures arc
                        // centres was read as switching the whole program to incremental.
                        var code = (int)Math.Floor(value + 1e-6);
                        var minor = value - code > 0.05;

                        switch (minor && code != 38 && code != 91 ? -1 : code)
                        {
                            case 0: motion = MoveKind.Rapid; motionThisLine = true; break;
                            case 1: motion = MoveKind.Feed; motionThisLine = true; break;
                            case 2: motion = MoveKind.ArcClockwise; motionThisLine = true; break;
                            case 3: motion = MoveKind.ArcCounterClockwise; motionThisLine = true; break;
                            // Probing: G38.2 and .3 toward the work, .4 and .5 away from it. All
                            // four travel at the feed rate, which is what matters for drawing one
                            // and for costing it. Where it actually stops is up to the switch.
                            case 38: motion = MoveKind.Feed; motionThisLine = true; break;

                            case 20: metric = false; break;
                            case 21: metric = true; break;
                            case 90: absolute = true; break;
                            // G91.1 is arc-centre mode and says nothing about distance mode.
                            case 91 when !minor: absolute = false; break;
                            case 91: break;
                            case 17 or 40 or 49 or 54 or 61 or 64 or 80 or 94: break;
                            case 81 or 82 or 83 or 85:
                                // Canned cycles are not interpreted. Saying so matters: GRBL does
                                // not implement them either, and a program full of them drills no
                                // holes on the machine as surely as it draws none here.
                                diagnostics.Add(new GcodeDiagnostic(
                                    $"G{(int)value} canned cycle is not interpreted; those holes are not shown.",
                                    lineNumber,
                                    IsError: false));
                                break;
                            default:
                                diagnostics.Add(new GcodeDiagnostic(
                                    minor
                                        ? FormattableString.Invariant($"Unhandled G{value:0.0#}.")
                                        : $"Unhandled G{code}.",
                                    lineNumber,
                                    IsError: false));
                                break;
                        }

                        break;

                    case 'X': nx = value; moved = true; break;
                    case 'Y': ny = value; moved = true; break;
                    case 'Z': nz = value; moved = true; break;
                    case 'I': ni = value; break;
                    case 'J': nj = value; break;
                    case 'F': feed = metric ? value : value * 25.4; break;

                    case 'M' or 'S' or 'T' or 'N' or 'P' or 'R' or 'K':
                        break;

                    default:
                        diagnostics.Add(new GcodeDiagnostic(
                            $"Unhandled word '{letter}'.", lineNumber, IsError: false));
                        break;
                }
            }

            if (!moved)
            {
                continue;
            }

            long Axis(double? given, long current) => given is null
                ? current
                : absolute
                    ? ToNm(given.Value, metric)
                    : current + ToNm(given.Value, metric);

            var toX = Axis(nx, x);
            var toY = Axis(ny, y);
            var toZ = Axis(nz, z);

            var from = new Point2(x, y);
            var to = new Point2(toX, toY);

            var centre = Point2.Origin;
            var kind = motion;

            if (kind is MoveKind.ArcClockwise or MoveKind.ArcCounterClockwise)
            {
                if (ni is null && nj is null)
                {
                    // R-form arcs exist but nothing here emits them, and guessing a centre from a
                    // radius has two answers. Drawing it as a line is visibly wrong, which is
                    // better than drawing the wrong arc convincingly.
                    diagnostics.Add(new GcodeDiagnostic(
                        "Arc without I or J; drawn as a line.", lineNumber, IsError: false));
                    kind = MoveKind.Feed;
                }
                else
                {
                    // I and J are the centre offset from the *start* of the arc.
                    centre = new Point2(
                        x + ToNm(ni ?? 0, metric),
                        y + ToNm(nj ?? 0, metric));
                }
            }

            moves.Add(new GcodeMove(kind, from, to, z, toZ, centre, feed, lineNumber));

            x = toX;
            y = toY;
            z = toZ;

            _ = motionThisLine;
        }

        return new GcodeProgram
        {
            Moves = moves,
            Diagnostics = diagnostics,
            LineCount = lineNumber,
        };
    }

    private static long ToNm(double value, bool metric) =>
        metric ? Nm.FromMillimetres(value) : Nm.FromInches(value);

    /// <summary>
    /// Removes comments. G-code has two forms — parenthesised and semicolon-to-end-of-line — and
    /// both appear in files this app will be handed.
    /// </summary>
    private static string StripComments(string line)
    {
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

    private static IEnumerable<(char Letter, double Value)> Words(
        string line, int lineNumber, List<GcodeDiagnostic> diagnostics)
    {
        var i = 0;
        while (i < line.Length)
        {
            var c = char.ToUpperInvariant(line[i]);
            if (!char.IsAsciiLetter(c))
            {
                i++;
                continue;
            }

            var start = ++i;
            while (i < line.Length && (char.IsAsciiDigit(line[i]) || line[i] is '.' or '-' or '+'))
            {
                i++;
            }

            var span = line.AsSpan(start, i - start).Trim();
            if (span.Length == 0)
            {
                continue;
            }

            if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                yield return (c, value);
            }
            else
            {
                diagnostics.Add(new GcodeDiagnostic(
                    $"'{c}{span}' is not a number.", lineNumber, IsError: true));
            }
        }
    }
}
