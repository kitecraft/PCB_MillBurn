using System.Globalization;
using MillBurn.Core;

namespace MillBurn.Gcode;

/// <summary>How high to hold the tool, and how honest to be about the time.</summary>
public sealed record DryRunOptions
{
    /// <summary>Raised by default: it is the one that answers more. See <see cref="DryRunStyle"/>.</summary>
    public DryRunStyle Style { get; init; } = DryRunStyle.Raised;

    /// <summary>
    /// How far to lift the whole program, in millimetres, when it is <see cref="DryRunStyle.Raised"/>.
    ///
    /// Three by default, which is what the workshop asked for and what a 1.6 mm board with
    /// 0.3 mm of break-through can take: the deepest cut ends 1.1 mm above the stock. A deeper
    /// program needs a bigger number, and it is refused rather than quietly raised further —
    /// somebody who asked for 3 mm should be told it was not enough, not handed 5.
    /// </summary>
    public double RiseMm { get; init; } = 3;

    /// <summary>
    /// How far above work zero the lowest point of a raised run must still be.
    ///
    /// Half a millimetre. Work zero is the top of the stock, so zero would be touching it and a
    /// tenth would be within the run-out of a tired spindle. It is not the five millimetres a flat
    /// run holds, because a raised run cannot have that and still be a raised run — the whole point
    /// is that the deepest move is only just clear.
    /// </summary>
    public double ClearanceMm { get; init; } = 0.5;

    /// <summary>
    /// How far above work zero to hold the tool, in millimetres.
    ///
    /// Work zero is the top of the stock, so anything above it is clear. Five millimetres is high
    /// enough to see daylight under the tool from across a workshop, which is the whole point:
    /// a clearance you have to crouch to confirm is not a clearance you will check.
    /// </summary>
    public double HeightMm { get; init; } = 5;

    /// <summary>
    /// Keep the programmed feeds, so the run takes as long as the real one.
    ///
    /// On by default because the question a dry run answers is not only "does it go where I
    /// expect" but "how long am I committing to". Turning it off replaces cutting feeds with the
    /// rapid rate, which is quicker to watch and no longer tells you anything about the time.
    /// </summary>
    public bool KeepFeeds { get; init; } = true;

    /// <summary>Rapid rate used for cutting moves when <see cref="KeepFeeds"/> is off.</summary>
    public double RapidMmPerMin { get; init; } = 2000;
}

/// <summary>What the rewrite did, and the evidence that it is safe to run.</summary>
public sealed record DryRunReport
{
    public required int LinesRewritten { get; init; }

    public required int SpindleCommandsRemoved { get; init; }

    /// <summary>
    /// The lowest Z the rewritten program ever asks for, read back out of it.
    ///
    /// Commanded positions, not the machine's starting one: where the tool is before the first
    /// line runs is the operator's business, and a generator that claimed otherwise would be
    /// claiming something it cannot know.
    /// </summary>
    public required double LowestZMm { get; init; }

    /// <summary>
    /// True when nothing that travels in X or Y does so below the requested height.
    ///
    /// This is the property that matters and it is stronger than "no Z below the height": a
    /// program can sit at a safe height throughout and still drag the tool across the stock if it
    /// moves sideways before it has risen.
    ///
    /// **Measured from the program's first commanded Z**, like <see cref="LowestZMm"/> and for the
    /// same reason: before that, the height is whatever the machine was left at, and a raised run
    /// travels there at exactly the height the real program would. A flat run has no such window —
    /// its preamble commands a height on its first line.
    /// </summary>
    public required bool StaysClear { get; init; }

    /// <summary>Why the rewrite was refused, or null if it succeeded.</summary>
    public string? Refusal { get; init; }
}

/// <summary>
/// Turns a program into one that traces the same path in the air.
///
/// The point is to be able to watch a file before it touches anything: are the extents right, is
/// the work zero where you think, does the order make sense, does it reach past the clamps. Until
/// this existed the first run of any program was into copper, and the only way to answer "which
/// side of that line is the cutter on" was to cut it and see.
///
/// **It rewrites the emitted file rather than re-generating from the toolpath.** Those two agree
/// right up until the emitter has a bug, and only one of them is what the machine will run — the
/// same reason the backplot parses the file (Documentation/05 §2.1). A dry run generated from the
/// toolpath would faithfully prove the safety of a program nobody is about to run.
/// </summary>
public static class DryRun
{
    /// <summary>
    /// Rewrites a program to run clear of the stock.
    ///
    /// Refuses rather than guesses. Incremental distance mode makes a Z word a *change* rather than
    /// a position, so substituting an absolute height would send the tool somewhere nobody asked
    /// for — and a dry run that is wrong is worse than none, because it is trusted.
    /// </summary>
    public static (string Text, DryRunReport Report) Rewrite(string program, DryRunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        options ??= new DryRunOptions();

        var lines = program.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var refusal = UsesIncrementalMode(lines)
            ?? (options.Style == DryRunStyle.Raised ? RedefinesTheCoordinateSystem(lines) : null);

        if (refusal is not null)
        {
            return (program, new DryRunReport
            {
                LinesRewritten = 0,
                SpindleCommandsRemoved = 0,
                LowestZMm = 0,
                StaysClear = false,
                Refusal = refusal,
            });
        }

        // A program in inches means its Z words are inches, so the substituted height has to be
        // too. Converting is better than refusing: the file is perfectly rewritable, and refusing
        // would push someone towards running the real one to see what it does.
        var inches = UsesInches(lines);
        var raised = options.Style == DryRunStyle.Raised;
        var heightMm = raised ? options.RiseMm : options.HeightMm;
        var height = (inches ? heightMm / 25.4 : heightMm)
            .ToString(inches ? "0.0000" : "0.000", CultureInfo.InvariantCulture);

        // In the file's own units, so a program in inches is lifted by three millimetres rather
        // than by three inches.
        var rise = inches ? options.RiseMm / 25.4 : options.RiseMm;
        var places = inches ? 4 : 3;

        var rewritten = 0;
        var spindle = 0;

        var output = new List<string>(lines.Length + 12) { Header(options, inches, height) };

        foreach (var raw in lines)
        {
            var line = raw;
            var code = StripComment(line);

            if (code.Length == 0)
            {
                output.Add(line);
                continue;
            }

            // The spindle stays off. A dry run with a tool spinning inches above the work is a dry
            // run that can still take a finger off, and it removes the one advantage of doing it.
            if (HasWord(code, 'M', 3) || HasWord(code, 'M', 4))
            {
                output.Add("( dry run: spindle start removed )");
                spindle++;
                continue;
            }

            if (raised)
            {
                // G53 is a move in machine coordinates — a tool-change position in somebody's end
                // G-code — and has nothing to do with work zero. Lifting it would send the tool
                // 3 mm from wherever the machine's own limit is, which is not what anyone meant.
                // One carrying a Z was refused above; this is the rest, which move in the plane.
                //
                // Its feed still gets rewritten when feeds are off. "Every move runs at the rapid
                // rate" must not have one line in it that does not — and that line would be the
                // only one in the file still carrying a cutting feed.
                if (HasFamily(code, 'G', 53))
                {
                    output.Add(options.KeepFeeds ? line : ReplaceFeed(line, options.RapidMmPerMin));
                    continue;
                }

                if (HasZ(code))
                {
                    line = Offset(line, 'Z', rise, places);
                    rewritten++;
                }

                // A canned cycle's R is the plane it retracts to, in the same coordinate system as
                // its Z. Lift one without the other and the cycle either drills through the retract
                // or refuses to run.
                if (IsCannedCycle(code) && HasValue(code, 'R'))
                {
                    line = Offset(line, 'R', rise, places);
                }
            }
            else if (HasZ(code))
            {
                line = ReplaceZ(line, height);
                rewritten++;
            }

            if (!options.KeepFeeds)
            {
                line = ReplaceFeed(line, options.RapidMmPerMin);
            }

            output.Add(line);
        }

        var text = string.Join("\n", output);

        // Read it back and measure, rather than trusting the rewrite. This is the check that makes
        // the file trustworthy: the claim "it never goes below 5 mm" is verified against the
        // program that will actually run, by the same parser the backplot uses.
        var parsed = GcodeParser.Parse(text);

        // **Commanded positions only.** The parser's Z starts at zero because it has to start
        // somewhere, and that zero is where the machine happens to be sitting rather than anything
        // the program asked for. `DryRunReport.LowestZMm` says as much in as many words.
        var firstZ = output.FindIndex(l => HasZ(StripComment(l)));

        // **A program that commands no Z at all raises nothing.** Every move runs at the modal
        // height, so the file handed back would be the real program with the spindle taken out —
        // under a header promising every Z raised by the rise, and a summary quoting a lowest point
        // no line in the file asks for. That header is what an operator sets work zero against.
        if (raised && firstZ < 0)
        {
            return (program, new DryRunReport
            {
                LinesRewritten = 0,
                SpindleCommandsRemoved = 0,
                LowestZMm = 0,
                StaysClear = false,
                Refusal = "this program never commands a Z, so there is no height to raise and the "
                    + "dry run would be the program itself. Use the flat dry run, which holds every "
                    + "move at one height whatever the program says.",
            });
        }

        // `firstZ` cannot be 0: line 0 is the header's first comment. Written as `<= 0` rather than
        // `< 0` so that a header which one day opens with a Z means "measure everything" instead of
        // parsing an empty prefix.
        var skip = firstZ <= 0
            ? 0
            : GcodeParser.Parse(string.Join("\n", output.Take(firstZ))).Moves.Count;

        var measured = parsed.Moves.Skip(skip).ToList();

        var lowest = measured.Count == 0
            ? heightMm
            : measured.Min(m => m.ToZNm) / (double)Nm.PerMillimetre;

        // The check is on moves that travel in the plane. A vertical move down to the safe height
        // from wherever the machine happens to be sitting is exactly what the first line should do;
        // a sideways move below that height is the thing that scrapes the stock.
        //
        // A raised run is judged against the clearance rather than the rise: the whole point is
        // that its deepest move is only just clear, so holding it to the rise would refuse every
        // program that actually cuts anything.
        var floorMm = raised ? options.ClearanceMm : heightMm;
        var floor = Nm.FromMillimetres(floorMm) - 1;
        var clear = true;

        for (var i = 0; i < measured.Count && clear; i++)
        {
            if (!measured[i].MovesInPlane)
            {
                continue;
            }

            // **The very first measured move is exempt at its starting end only.** It inherits that
            // height from the last move that was deliberately not measured — the uncommanded zero
            // above — so testing it condemned any program that travels before its first Z word, and
            // condemned it silently: `StaysClear` came back false while nothing was refused, so the
            // file shipped carrying its own evidence that the promise was broken.
            //
            // Every later move starts exactly where the one before it ended, so they are still
            // tested at both ends and nothing else is weakened.
            var startsClear = i == 0 || measured[i].FromZNm >= floor;

            clear = startsClear && measured[i].ToZNm >= floor;
        }

        // **Refused rather than raised further.** The operator asked for a rise; if it is not
        // enough, the answer is the number that would be, not a program silently lifted to
        // something they did not choose and will not be expecting at the machine.
        //
        // Keyed on `lowest` because that is what yields a number to advise. With the boundary
        // exempted above, `lowest >= floorMm` should now imply `clear`: every measured move starts
        // where the one before it ended. That is reasoning, not a proof, and the last time it was
        // written down as an invariant it was false — so `clear` is checked on its own straight
        // after, rather than trusted.
        if (raised && lowest < floorMm)
        {
            var needed = options.RiseMm + (floorMm - lowest);

            return (program, new DryRunReport
            {
                LinesRewritten = 0,
                SpindleCommandsRemoved = 0,
                LowestZMm = lowest,
                StaysClear = false,
                Refusal = string.Create(
                    CultureInfo.InvariantCulture,
                    $"raised by {options.RiseMm:F2} mm the lowest move is {lowest:F2} mm, which is "
                    + $"under the {floorMm:F2} mm clearance. Raise by at least {needed:F2} mm."),
            });
        }

        // The clearance check, asked rather than inferred. Nothing in `src/` reads `StaysClear`, so
        // if this is ever false and the file is still written, the fact that the promise was broken
        // is recorded in a field nobody looks at and the operator is told nothing.
        if (raised && !clear)
        {
            return (program, new DryRunReport
            {
                LinesRewritten = 0,
                SpindleCommandsRemoved = 0,
                LowestZMm = lowest,
                StaysClear = false,
                Refusal = string.Create(
                    CultureInfo.InvariantCulture,
                    $"raised by {options.RiseMm:F2} mm the program still travels in X or Y below the {floorMm:F2} mm clearance, so this dry run cannot promise to stay off the work."),
            });
        }

        return (text, new DryRunReport
        {
            LinesRewritten = rewritten,
            SpindleCommandsRemoved = spindle,
            LowestZMm = lowest,
            StaysClear = clear,
        });
    }

    /// <summary>
    /// The preamble.
    ///
    /// **The raised one does not add a lift, and the flat one does.** A flat run replaces every Z,
    /// so an opening `G0` to the safe height costs nothing and covers a file that might plunge
    /// first. A raised run is supposed to be the real program with a constant added — add a move at
    /// the top and it is no longer that, and the claim the whole feature rests on stops being
    /// checkable move by move. What keeps it safe instead is the clearance check on the way out,
    /// which reads the finished program back and refuses it if anything travels too low.
    /// </summary>
    private static string Header(DryRunOptions options, bool inches, string height) =>
        options.Style == DryRunStyle.Raised
            // **The timing sentence is conditional, because with feeds off it is not true.**
            // `KeepFeeds = false` replaces every F with the rapid rate, which is the whole point of
            // that option and is exactly what its own help text says. Printing "the time this takes
            // is the time the real program takes" anyway would put the one false sentence in the
            // file on the one page the operator reads at the machine.
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"""
                ( ******************************************************** )
                ( DRY RUN. This program cuts nothing.                       )
                ( It is the real program with every Z raised by {options.RiseMm:F2} mm,     )
                ( and the spindle never started. Every plunge and lift      )
                ( happens where it really happens, in the air.              )
                {(options.KeepFeeds
                    ? "( The feeds are the real ones, so the time this takes is    )\n"
                        + "( the time the real program takes.                          )"
                    : "( The feeds have been replaced with the rapid rate, so this )\n"
                        + "( runs faster than the real program and its time means      )\n"
                        + "( nothing.                                                  )")}
                ( Set work zero exactly as you would for the real program.  )
                ( ******************************************************** )
                M5
                {(inches ? "G20" : "G21")} G90
                """)
            : string.Create(
                CultureInfo.InvariantCulture,
                $"""
                ( ******************************************************** )
                ( DRY RUN. This program cuts nothing.                       )
                ( Every Z is held {options.HeightMm:F3} mm above work zero and the spindle )
                ( is never started. Set work zero exactly as you would for  )
                ( the real program, then watch the path.                    )
                ( ******************************************************** )
                M5
                {(inches ? "G20" : "G21")} G90
                G0 Z{height}
                """);

    /// <summary>
    /// Whether the program moves work zero out from under the rewrite.
    ///
    /// **Only a raised run has to ask.** A flat one replaces every Z with the same number, so it
    /// does not matter what that number is measured from — it is above whatever the machine thinks
    /// the top of the stock is, which is the property that keeps the tool out of the work. A raised
    /// one adds to the value already there, and that only means "3 mm higher" while the coordinate
    /// system those values are in stays still.
    ///
    /// `G92` and `G10` move it. `G38.x` feels for a surface that a raised run has just put 3 mm
    /// further away, so it would either travel the extra distance and touch off on the wrong plane
    /// or fail to find anything at all. Refused, all three, rather than rewritten cleverly.
    /// </summary>
    private static string? RedefinesTheCoordinateSystem(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var code = StripComment(lines[i]);

            // Matched as families, so the decimal variants come with the number they vary. `G92.3`
            // restores a saved work offset, which is precisely what the `G92` refusal exists to
            // catch, and it is the spelling a hand-written start G-code is most likely to use.
            if (HasFamily(code, 'G', 92))
            {
                return $"line {i + 1} sets a coordinate offset (G92); "
                    + "raising a program whose work zero moves under it would not be a copy of it.";
            }

            if (HasFamily(code, 'G', 10))
            {
                return $"line {i + 1} writes a coordinate system (G10); "
                    + "raising a program whose work zero moves under it would not be a copy of it.";
            }

            if (HasFamily(code, 'G', 38))
            {
                return $"line {i + 1} probes (G38); "
                    + "a raised run would feel for a surface 3 mm further away than the program expects.";
            }

            // **A tool-length offset moves the datum the rise is measured from.** `G43` applies one
            // and `G49` cancels one, and either changes what a Z word means without changing the Z
            // word. A tool-setter workflow puts `G43.1` in start G-code, which reaches every
            // program, so this is not an exotic case. Caught here rather than left to the clearance
            // check, which sees only numbers and cannot know the datum moved under them.
            if (HasFamily(code, 'G', 43) || HasFamily(code, 'G', 49))
            {
                return $"line {i + 1} changes the tool-length offset (G43/G49); "
                    + "a raised run adds to Z, and this moves what Z is measured from.";
            }

            // **A machine-coordinate Z cannot be vouched for, so it is refused rather than left.**
            // G53 is measured from the machine's own zero, so raising it would be meaningless — but
            // leaving it alone is not enough either, because the safety check reads the rewritten
            // file back and has no way to know that one Z in it is in a different coordinate system.
            // A dry run whose promise has a hole in it is worse than one that says so. G53 without
            // a Z is a move in the plane and passes through untouched.
            if (HasFamily(code, 'G', 53) && HasZ(code))
            {
                return $"line {i + 1} moves Z in machine coordinates (G53); "
                    + "a raised run cannot say how far that is from the stock.";
            }

            // **Return-to-home ends at a machine position, whatever its Z word says.** `G28` and
            // `G30` take an optional intermediate point in work coordinates and then go to a stored
            // machine one, so raising the Z moves the waypoint and not the destination — and the
            // clearance check reads the file back without any way to know the difference. The same
            // hole as `G53`, refused the same way; unlike `G53` there is no version of it that
            // stays in the plane, because the whole point of it is to move Z.
            if (HasFamily(code, 'G', 28) || HasFamily(code, 'G', 30))
            {
                return $"line {i + 1} returns to a machine position (G28/G30); "
                    + "a raised run cannot say how far that is from the stock.";
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a word appears with this number, counting its decimal variants as the same word:
    /// <c>G92</c> and <c>G92.3</c> both match 92, and <c>G38.2</c> matches 38.
    ///
    /// The variants are why this exists. They do the same job as the number they vary — `G92.3`
    /// restores a saved work offset, `G43.1` sets a tool length — so a guard that knows only the
    /// bare number has a hole in it exactly where a hand-written start G-code lives.
    /// <c>HasWord</c> cannot see them: it reads the decimal into the digit run and then fails to
    /// parse "92.3" as an integer.
    /// </summary>
    private static bool HasFamily(string code, char letter, int number)
    {
        for (var i = 0; i < code.Length; i++)
        {
            if (char.ToUpperInvariant(code[i]) != letter)
            {
                continue;
            }

            // Stops at the decimal point rather than swallowing it, which is the whole difference.
            var j = i + 1;
            while (j < code.Length && char.IsDigit(code[j]))
            {
                j++;
            }

            if (j > i + 1
                && int.TryParse(code.AsSpan(i + 1, j - i - 1), CultureInfo.InvariantCulture, out var value)
                && value == number)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A drilling cycle, whose R word shares the coordinate system of its Z and must rise with it.
    /// </summary>
    private static bool IsCannedCycle(string code)
    {
        // G73 is the high-speed peck cycle. It carries an R exactly like G81–G89 and sits outside
        // their range, so it was lifted by its Z and left behind by its R — the cycle then drills
        // through its own retract, which is the failure this whole check exists to prevent.
        if (HasFamily(code, 'G', 73))
        {
            return true;
        }

        // By family, so G83.1 and G84.2 count as the cycles they vary. `HasWord` saw neither.
        for (var n = 81; n <= 89; n++)
        {
            if (HasFamily(code, 'G', n))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the program selects inches. Our own files never do; other people's might.</summary>
    private static bool UsesInches(string[] lines) =>
        lines.Any(l => HasWord(StripComment(l), 'G', 20));

    /// <summary>
    /// Whether the program ever selects incremental distance mode.
    ///
    /// Checked across the whole file rather than tracked as state, because a single <c>G91</c>
    /// anywhere makes every later Z word a relative one — and this has to be a question with a
    /// yes-or-no answer, not a judgement about which lines it reached.
    /// </summary>
    private static string? UsesIncrementalMode(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var code = StripComment(lines[i]);

            // G91.1 sets arc-centre mode and is unrelated to distance mode.
            if (HasWord(code, 'G', 91) && !code.Contains("91.1", StringComparison.Ordinal))
            {
                return $"line {i + 1} selects incremental mode (G91); "
                    + "a dry run cannot rewrite relative Z moves safely.";
            }
        }

        return null;
    }

    private static string StripComment(string line)
    {
        var at = line.IndexOf('(', StringComparison.Ordinal);
        var code = at >= 0 ? line[..at] : line;

        at = code.IndexOf(';', StringComparison.Ordinal);
        return (at >= 0 ? code[..at] : code).Trim();
    }

    /// <summary>Whether a word appears with exactly this number, so M3 never matches M30.</summary>
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

    private static bool HasZ(string code) => HasValue(code, 'Z');

    private static string ReplaceZ(string line, string height) => Replace(line, 'Z', height);

    /// <summary>Whether a word appears with a number after it, whatever that number is.</summary>
    private static bool HasValue(string code, char letter)
    {
        for (var i = 0; i < code.Length; i++)
        {
            if (char.ToUpperInvariant(code[i]) == letter && i + 1 < code.Length
                && (char.IsDigit(code[i + 1]) || code[i + 1] is '-' or '+' or '.'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds to a word's value wherever it appears outside a comment, leaving the rest of the line
    /// exactly as written.
    ///
    /// The value is re-formatted rather than patched, because "-0.045" plus three is "2.955" and
    /// there is no way to reach that by editing characters. Everything else on the line — the
    /// spacing, the case, the comment, the order of the words — survives, which matters because the
    /// point of rewriting the emitted file is that a person can read the dry run against the real
    /// program and see that they are the same moves.
    /// </summary>
    private static string Offset(string line, char letter, double rise, int places)
    {
        var limit = line.IndexOf('(', StringComparison.Ordinal);
        if (limit < 0)
        {
            limit = line.IndexOf(';', StringComparison.Ordinal);
        }

        if (limit < 0)
        {
            limit = line.Length;
        }

        var result = new System.Text.StringBuilder(line.Length + 8);
        var i = 0;

        while (i < line.Length)
        {
            if (i >= limit || char.ToUpperInvariant(line[i]) != letter)
            {
                result.Append(line[i]);
                i++;
                continue;
            }

            var j = i + 1;
            if (j < line.Length && line[j] is '-' or '+')
            {
                j++;
            }

            var digits = j;
            while (j < line.Length && (char.IsDigit(line[j]) || line[j] == '.'))
            {
                j++;
            }

            if (j == digits
                || !double.TryParse(
                    line.AsSpan(i + 1, j - i - 1), CultureInfo.InvariantCulture, out var value))
            {
                result.Append(line[i]);
                i++;
                continue;
            }

            // Fixed decimals, not significant ones. `0.###` drops trailing zeros, so `Z2.000`
            // came back as `Z5` and every Z line in the dry run differed in shape from the real
            // one — which defeats the reason this method preserves everything else on the line.
            result.Append(letter)
                .Append((value + rise).ToString($"F{places}", CultureInfo.InvariantCulture));
            i = j;
        }

        return result.ToString();
    }

    private static string ReplaceFeed(string line, double rapid) =>
        Replace(line, 'F', rapid.ToString("0.###", CultureInfo.InvariantCulture));

    /// <summary>
    /// Replaces a word's value, leaving everything else — spacing, case, comments — untouched.
    ///
    /// Only outside comments: a Z in a comment is prose, and rewriting it would corrupt the very
    /// notes that say what the program does.
    /// </summary>
    private static string Replace(string line, char letter, string value)
    {
        var limit = line.IndexOf('(', StringComparison.Ordinal);
        if (limit < 0)
        {
            limit = line.IndexOf(';', StringComparison.Ordinal);
        }

        if (limit < 0)
        {
            limit = line.Length;
        }

        var result = new System.Text.StringBuilder(line.Length + value.Length);
        var i = 0;

        while (i < line.Length)
        {
            if (i >= limit || char.ToUpperInvariant(line[i]) != letter)
            {
                result.Append(line[i]);
                i++;
                continue;
            }

            var j = i + 1;
            if (j < line.Length && line[j] is '-' or '+')
            {
                j++;
            }

            var digits = j;
            while (j < line.Length && (char.IsDigit(line[j]) || line[j] == '.'))
            {
                j++;
            }

            if (j == digits)
            {
                result.Append(line[i]);
                i++;
                continue;
            }

            result.Append(letter).Append(value);
            i = j;
        }

        return result.ToString();
    }
}
