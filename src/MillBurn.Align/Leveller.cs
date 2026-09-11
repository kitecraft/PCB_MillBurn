using System.Globalization;
using System.Text;
using MillBurn.Core;
using MillBurn.Gcode;

namespace MillBurn.Align;

/// <summary>How closely to follow the measured surface.</summary>
public sealed record LevelOptions
{
    /// <summary>
    /// The longest a cutting move may be before it is broken up, in millimetres.
    ///
    /// One millimetre. The correction is applied at the ends of a move, so a move longer than this
    /// rides a straight line across ground the map says is curved. On a board bowed 0.15 mm over
    /// 100 mm, a 10 mm move cuts about 1.5 µm off its intended depth in the middle and a 1 mm move
    /// about 15 nm — well past the point where anything else in the machine is the limit.
    /// </summary>
    public double SegmentMm { get; init; } = 1;

    /// <summary>
    /// Moves whose deepest point is at or below this height get broken up; higher ones only have
    /// their endpoints corrected.
    ///
    /// Subdividing a rapid held 5 mm in the air to follow a surface 0.1 mm out of flat would
    /// multiply the file for no reason at all.
    /// </summary>
    public double SubdivideBelowMm { get; init; } = 0.5;

    /// <summary>
    /// How far outside the probed area the toolpath may stray before the whole thing is refused,
    /// in millimetres.
    ///
    /// Outside the measured area the map holds its edge value, which is a good answer a millimetre
    /// out and a guess a centimetre out. Past this, levelling would be pretending to knowledge
    /// nobody has — and the failure is a cut at the wrong depth across a whole region, which is
    /// exactly what levelling was supposed to prevent.
    /// </summary>
    public double MaxOutsideMm { get; init; } = 3;
}

/// <summary>What levelling did, and the evidence it did it sanely.</summary>
public sealed record LevelReport
{
    public required int MovesLevelled { get; init; }

    public required int SegmentsAdded { get; init; }

    public required int ArcsExpanded { get; init; }

    /// <summary>The largest correction applied, up and down. The stock's bow, as the cutter sees it.</summary>
    public required double MaxRiseMm { get; init; }

    public required double MaxFallMm { get; init; }

    /// <summary>The furthest any point of the path lay outside the probed area.</summary>
    public required double FurthestOutsideMm { get; init; }

    /// <summary>Why levelling was refused, or null if it was applied.</summary>
    public string? Refusal { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// Bends a finished program to follow a measured surface.
///
/// This is the answer to the single most common way PCB milling disappoints people. Isolation runs
/// 0.05 mm deep; ordinary FR4 clamped at its corners is 0.1–0.2 mm out of flat. Without levelling
/// the same program skates over the copper at one end of the board and ploughs through into the
/// substrate at the other, and no amount of care with the toolpath changes that.
///
/// Like the dry run, it rewrites **the emitted program**, so what gets levelled is the file that
/// will actually run. It is also therefore useful on G-code from anywhere else, which makes this a
/// standalone levelling utility as much as a step in our own pipeline.
/// </summary>
public static class Leveller
{
    /// <summary>Applies a height map to a program.</summary>
    /// <remarks>
    /// Refuses rather than guesses, in the two cases where the rewrite cannot be made to mean the
    /// right thing: incremental distance mode, where a Z word is a change rather than a position,
    /// and canned drilling cycles, where the depth lives in a modal word that applies to holes on
    /// later lines. Getting either wrong drills or mills straight through the board.
    /// </remarks>
    /// <summary>
    /// Why a height map must not be applied to this program, or null when it may be.
    ///
    /// **A map describes one face of the stock as it was clamped at the time.** Bottom-side
    /// geometry is mirrored because the operator turns the board over before cutting it, so a
    /// program and a map either belong to the same side or they do not. Applied across the flip,
    /// the correction lands on the wrong point — the coordinates have been mirrored — of the wrong
    /// surface. Wrong twice, and confidently.
    ///
    /// Nothing can be inverted to rescue it. The stock is re-clamped when it is turned over, and a
    /// map belongs to a piece of stock in the position it is currently held; that is why it is never
    /// saved into a project. The only correct answer is to probe the side about to be cut.
    ///
    /// Refused rather than warned about, because one export produces both sides from one map and at
    /// most one of them can be right. A file carrying "every Z follows a measured surface" above
    /// "flip the stock left-to-right" is telling the operator two things that cannot both be true.
    /// </summary>
    /// <param name="programMirrored">Whether this program is cut on the flipped stock.</param>
    /// <param name="mapOfFlippedStock">Whether the map was probed with the stock flipped.</param>
    public static string? WhyNotLevel(bool programMirrored, bool mapOfFlippedStock = false)
    {
        if (programMirrored == mapOfFlippedStock)
        {
            return null;
        }

        var program = programMirrored ? "on the flipped stock" : "with the board top-up";
        var map = mapOfFlippedStock ? "with the stock flipped" : "with the board top-up";

        return $"This program is cut {program} and the map was probed {map}. Turning the board over "
            + "presents the other face and mirrors the coordinates as well, so the correction would "
            + "land on the wrong point of the wrong surface. Cut this one unlevelled, or probe the "
            + "side you are about to cut and level this file on its own.";
    }

    public static (string Text, LevelReport Report) Apply(
        string program, HeightMap map, LevelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(map);
        options ??= new LevelOptions();

        var lines = program.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        if (Refuse(lines) is { } refusal)
        {
            return (program, Refused(refusal));
        }

        var inches = lines.Any(l => HasWord(StripComment(l), 'G', 20));
        var parsed = GcodeParser.Parse(program);

        // Everything the path will ask of the map, before a single line is written. A path that
        // runs off the probed area has to be caught here rather than levelled region by region:
        // the answer is to probe a bigger area, and finding that out after the file is written
        // helps nobody.
        var outside = 0.0;

        foreach (var move in parsed.Moves)
        {
            outside = Math.Max(outside, Math.Max(map.OutsideByMm(move.From), map.OutsideByMm(move.To)));
        }

        if (outside > options.MaxOutsideMm)
        {
            return (program, Refused(string.Create(
                CultureInfo.InvariantCulture,
                $"the toolpath runs {outside:F1} mm outside the probed area; probe a region that "
                + $"covers the whole job, or raise the limit above {options.MaxOutsideMm:F1} mm.")));
        }

        var byLine = parsed.Moves
            .GroupBy(m => m.Line)
            .ToDictionary(g => g.Key, g => g.ToList());

        var output = new List<string>(lines.Length * 2)
        {
            Header(map, options, inches),
        };

        var levelled = 0;
        var added = 0;
        var arcs = 0;
        var rise = 0.0;
        var fall = 0.0;
        var lastFeed = double.NaN;

        for (var i = 0; i < lines.Length; i++)
        {
            if (!byLine.TryGetValue(i + 1, out var moves))
            {
                output.Add(lines[i]);
                continue;
            }

            // The comment on a motion line describes the motion, so it travels with the first of
            // the segments that replace it rather than being dropped.
            var comment = CommentOf(lines[i]);

            foreach (var move in moves)
            {
                if (move.IsArc)
                {
                    arcs++;
                }

                var before = output.Count;

                Write(output, move, map, options, inches, ref lastFeed, ref rise, ref fall);

                levelled++;
                added += output.Count - before - 1;
            }

            if (comment is not null && output.Count > 0)
            {
                output[^1] += "  " + comment;
            }
        }

        return (string.Join("\n", output), new LevelReport
        {
            MovesLevelled = levelled,
            SegmentsAdded = added,
            ArcsExpanded = arcs,
            MaxRiseMm = rise,
            MaxFallMm = fall,
            FurthestOutsideMm = outside,
            Notes = map.Notes,
        });
    }

    // ------------------------------------------------------------------ writing one move

    private static void Write(
        List<string> output,
        GcodeMove move,
        HeightMap map,
        LevelOptions options,
        bool inches,
        ref double lastFeed,
        ref double rise,
        ref double fall)
    {
        var deepest = Math.Min(move.FromZNm, move.ToZNm);
        var subdivide = deepest <= Nm.FromMillimetres(options.SubdivideBelowMm);
        var steps = 1;

        if (subdivide)
        {
            var length = move.IsArc
                ? ArcLengthNm(move)
                : move.From.DistanceTo(move.To);

            steps = (int)Math.Max(1, Math.Ceiling(length / Nm.FromMillimetres(options.SegmentMm)));
        }

        for (var s = 1; s <= steps; s++)
        {
            var t = s / (double)steps;

            // The last step lands on the destination itself rather than on a point computed from
            // the parameter. For a line the two agree; for an arc, walking round by angle and
            // rounding back to nanometres leaves the end a nanometre or two off — and an endpoint
            // that is not exactly where the next move starts is a gap in the toolpath.
            var at = s == steps
                ? move.To
                : move.IsArc ? ArcPointAt(move, t) : Lerp(move.From, move.To, t);

            var z = s == steps
                ? move.ToZNm
                : move.FromZNm + (long)Math.Round((move.ToZNm - move.FromZNm) * t);

            var correction = map.SampleNm(at);
            var mm = correction / (double)Nm.PerMillimetre;

            rise = Math.Max(rise, mm);
            fall = Math.Min(fall, mm);

            // An arc that has to be broken up becomes lines: its Z now changes along its length in
            // a way no G2 can express. An arc that does not stays an arc, helix and all.
            var word = !subdivide && move.IsArc
                ? move.Kind == MoveKind.ArcClockwise ? "G2" : "G3"
                : move.IsRapid ? "G0" : "G1";

            var line = new StringBuilder(48).Append(word);

            if (move.MovesInPlane)
            {
                line.Append(" X").Append(Coordinate(at.X, inches))
                    .Append(" Y").Append(Coordinate(at.Y, inches));
            }

            line.Append(" Z").Append(Coordinate(z + correction, inches));

            if (!subdivide && move.IsArc)
            {
                line.Append(" I").Append(Coordinate(move.Centre.X - move.From.X, inches))
                    .Append(" J").Append(Coordinate(move.Centre.Y - move.From.Y, inches));
            }

            // Modal, so it is written only when it changes — which after subdivision is once per
            // original move rather than once per segment.
            if (!move.IsRapid && move.FeedMmPerMin > 0
                && (double.IsNaN(lastFeed) || Math.Abs(lastFeed - move.FeedMmPerMin) > 1e-9))
            {
                line.Append(" F").Append(move.FeedMmPerMin.ToString("0.###", CultureInfo.InvariantCulture));
                lastFeed = move.FeedMmPerMin;
            }

            output.Add(line.ToString());
        }
    }

    private static Point2 Lerp(Point2 a, Point2 b, double t) => new(
        a.X + (long)Math.Round((b.X - a.X) * t),
        a.Y + (long)Math.Round((b.Y - a.Y) * t));

    private static double ArcLengthNm(GcodeMove move) => Segment(move).RadiusNm * Segment(move).SweptAngle();

    private static ArtSegment Segment(GcodeMove move) => new(
        move.Kind == MoveKind.ArcClockwise ? ArtSweep.Clockwise : ArtSweep.CounterClockwise,
        move.From,
        move.To,
        move.Centre);

    private static Point2 ArcPointAt(GcodeMove move, double t)
    {
        var segment = Segment(move);
        var start = Math.Atan2(move.From.Y - move.Centre.Y, move.From.X - move.Centre.X);
        var sweep = segment.SweptAngle() * (move.Kind == MoveKind.ArcClockwise ? -1 : 1);
        var angle = start + (sweep * t);
        var radius = segment.RadiusNm;

        return new Point2(
            move.Centre.X + (long)Math.Round(radius * Math.Cos(angle)),
            move.Centre.Y + (long)Math.Round(radius * Math.Sin(angle)));
    }

    private static string Coordinate(long nm, bool inches)
    {
        var text = inches
            ? (nm / (double)Nm.PerMillimetre / 25.4).ToString("0.0000", CultureInfo.InvariantCulture)
            : (nm / (double)Nm.PerMillimetre).ToString("0.000", CultureInfo.InvariantCulture);

        // A correction of a few hundred nanometres rounds to "-0.000", which is valid G-code and
        // reads like a mistake. Nobody wants to wonder what a negative zero meant.
        return text.TrimStart('-').All(c => c is '0' or '.') ? text.TrimStart('-') : text;
    }

    // ------------------------------------------------------------------ what it refuses

    private static string? Refuse(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var code = StripComment(lines[i]);

            if (HasWord(code, 'G', 91) && !code.Contains("91.1", StringComparison.Ordinal))
            {
                return $"line {i + 1} selects incremental mode (G91); "
                    + "a Z word there is a change rather than a position, so it cannot be levelled.";
            }

            foreach (var cycle in (int[])[81, 82, 83, 85])
            {
                if (HasWord(code, 'G', cycle))
                {
                    return $"line {i + 1} uses a canned drilling cycle (G{cycle}); its depth is a "
                        + "modal word shared by later holes, so each hole cannot be given its own. "
                        + "Re-export with canned cycles off.";
                }
            }
        }

        return null;
    }

    private static LevelReport Refused(string why) => new()
    {
        MovesLevelled = 0,
        SegmentsAdded = 0,
        ArcsExpanded = 0,
        MaxRiseMm = 0,
        MaxFallMm = 0,
        FurthestOutsideMm = 0,
        Refusal = why,
    };

    private static string Header(HeightMap map, LevelOptions options, bool inches)
    {
        var fit = map.Fit.ToString().ToLowerInvariant();
        var surface = Invariant($"( {map.PointCount} probe points, {fit} fit, {map.RangeMm:F3} mm out of flat. )");
        var steps = Invariant(
            $"( Broken into {options.SegmentMm:F1} mm steps below {options.SubdivideBelowMm:F1} mm. )");

        var lines = new List<string>
        {
            "( ******************************************************** )",
            "( LEVELLED. Every Z follows a measured surface.             )",
            surface,
            steps,
            "( Set work zero exactly where it was when you probed, or    )",
            "( this is worse than no levelling at all.                   )",
            "( ******************************************************** )",
            inches ? "G20 G90" : "G21 G90",
        };

        return string.Join("\n", lines);
    }

    // ------------------------------------------------------------------ small text helpers

    private static string StripComment(string line)
    {
        var at = line.IndexOf('(', StringComparison.Ordinal);
        var code = at >= 0 ? line[..at] : line;

        at = code.IndexOf(';', StringComparison.Ordinal);
        return (at >= 0 ? code[..at] : code).Trim();
    }

    private static string? CommentOf(string line)
    {
        var at = line.IndexOf('(', StringComparison.Ordinal);
        return at >= 0 ? line[at..].Trim() : null;
    }

    /// <summary>Whether a word appears with exactly this number, so G8 never matches G80.</summary>
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

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
