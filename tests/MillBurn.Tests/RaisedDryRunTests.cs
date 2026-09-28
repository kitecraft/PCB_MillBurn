using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Sprint 2 story 8 — V31 and 6.19: the dry run is the real program, lifted.
///
/// From the workshop: *"The dry-run could be much better representative of a real run. The lack of
/// z-moves lowers the usefulness of the current dry-run too much. What if the dry-run was just a
/// copy of the real run, then, ensure the spindle is OFF, then, raise the entire thing by X mm
/// (default 3)."*
///
/// **What the flat one leaves out is most of the program.** Holding every Z at one height shows the
/// path in plan and nothing else: no plunge, no lift, no ramp, no helix, and none of the Z travel
/// that is a quarter of a real program's time on the workshop's machine. So a flat run's time is
/// not the real run's time, and the question *"how long am I committing to"* goes unanswered by the
/// one thing that exists to answer it.
///
/// **This is a safety feature, so the tests are the feature.** A raised run that is wrong is worse
/// than none, because it is the thing somebody trusts before committing a board — and unlike the
/// flat one it cannot be safe by construction. A flat run physically cannot plunge; a raised one
/// only clears the work if the arithmetic is right and the coordinate system holds still. Each way
/// it can fail to hold still is refused, and each refusal has a test here.
/// </summary>
public sealed class RaisedDryRunTests(ITestOutputHelper output)
{
    private static readonly DryRunOptions Raised = new();

    private static string Real(string board, OperationKind operation)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        return plan.Items.First(i => i.Operation == operation).Content;
    }

    // ------------------------------------------------------------------ it is the same program

    /// <summary>
    /// Every Z is exactly three millimetres higher, and nothing else about the move changes.
    ///
    /// **This is the whole claim, checked move by move against the program it came from.** Not "the
    /// lowest point is clear" — that is the safety check further down — but that the raised run
    /// *is* the real run: same number of moves, same X and Y, same order, each Z lifted by the same
    /// amount. Anything less and it is a different program that happens to look similar, which is
    /// exactly what the flat one already was.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1, OperationKind.Isolation)]
    [InlineData(RealBoards.MillburnTestBoard, OperationKind.Isolation)]
    [InlineData(RealBoards.MillburnTestBoard, OperationKind.Drilling)]
    [InlineData(RealBoards.GridStripConnector, OperationKind.Outline)]
    public void ItIsTheRealProgramWithEveryZThreeMillimetresHigher(string board, OperationKind operation)
    {
        var real = Real(board, operation);
        var (dry, report) = DryRun.Rewrite(real, Raised);

        Assert.Null(report.Refusal);

        var before = GcodeParser.Parse(real).Moves;
        var after = GcodeParser.Parse(dry).Moves;

        output.WriteLine(
            $"{board} {operation}: {before.Count} moves, lowest {report.LowestZMm:F3} mm after a "
            + $"{Raised.RiseMm:F2} mm rise");

        Assert.Equal(before.Count, after.Count);

        var rise = Nm.FromMillimetres(Raised.RiseMm);

        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].To, after[i].To);
            Assert.Equal(before[i].Kind, after[i].Kind);
            Assert.Equal(before[i].ToZNm + rise, after[i].ToZNm);

            // The feed too. "The time this takes is the time the real program takes" is half the
            // claim, and it is the feeds that make it true — a rewrite that kept every move and
            // every depth but ran them all at the rapid rate would satisfy everything above.
            Assert.Equal(before[i].FeedMmPerMin, after[i].FeedMmPerMin);
        }
    }

    /// <summary>
    /// The Z travel is still there, which is the thing the flat run threw away.
    ///
    /// A flat run has no vertical motion at all after its opening lift: every move is at one
    /// height, so the plunges and retracts that take a quarter of a real program's time simply do
    /// not happen. Asserted as a comparison rather than a number, because what matters is that the
    /// raised run has the real program's Z motion and the flat one has none of it.
    /// </summary>
    [Fact]
    public void TheZTravelSurvives()
    {
        var real = Real(RealBoards.MillburnTestBoard, OperationKind.Drilling);

        var (raised, _) = DryRun.Rewrite(real, Raised);
        var (flat, _) = DryRun.Rewrite(real, new DryRunOptions { Style = DryRunStyle.Flat });

        // Past the opening move, because the parser starts from an assumed zero: the real program's
        // first move climbs to its safe height and the raised one climbs three millimetres higher,
        // so that first length is about where the machine is imagined to be rather than about the
        // program. Everything after it is the program's own Z motion, which is the claim.
        static double VerticalMm(string program) =>
            GcodeParser.Parse(program).Moves.Skip(1).Sum(m => Math.Abs(m.ToZNm - m.FromZNm))
            / (double)Nm.PerMillimetre;

        var realZ = VerticalMm(real);
        var raisedZ = VerticalMm(raised);
        var flatZ = VerticalMm(flat);

        output.WriteLine($"Z travel: real {realZ:F0} mm, raised {raisedZ:F0} mm, flat {flatZ:F0} mm");

        Assert.Equal(realZ, raisedZ, 3);
        Assert.True(
            flatZ < realZ / 10,
            $"the flat run moved {flatZ:F0} mm in Z against the real program's {realZ:F0} — it is "
            + "supposed to have thrown that away, and this comparison is what says the raised one "
            + "keeps it.");
    }

    /// <summary>The spindle never starts, which is the one thing both styles must promise.</summary>
    [Fact]
    public void TheSpindleNeverStarts()
    {
        var (dry, report) = DryRun.Rewrite(Real(RealBoards.PogoTest1, OperationKind.Isolation), Raised);

        Assert.True(report.SpindleCommandsRemoved > 0, "the real program should have started it");

        // Word by word, because "M3" is a substring of "M30" — which every program ends with, and
        // which is the opposite of starting the spindle.
        var words = dry.Split('\n')
            .Select(l => l.Split('(')[0])
            .SelectMany(c => c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(w => w.Trim().ToUpperInvariant())
            .ToList();

        // Both spellings of each: a controller reads M03 and M3 the same way, and the production
        // code parses the number rather than the text — so a test comparing tokens has to know
        // about the zero or it is checking less than the code does.
        Assert.DoesNotContain("M3", words);
        Assert.DoesNotContain("M03", words);
        Assert.DoesNotContain("M4", words);
        Assert.DoesNotContain("M04", words);
        Assert.True(
            words.Contains("M5") || words.Contains("M05"),
            "the dry run has to stop the spindle, and no M5 reached the file.");
    }

    // ------------------------------------------------------------------ where it refuses

    /// <summary>
    /// Too deep for the rise asked for, and the refusal says what would be enough.
    ///
    /// **Refused rather than raised further**, which is the point worth testing: a program quietly
    /// lifted to five millimetres when the operator typed three is a program that behaves
    /// differently from the one they are expecting to watch. The message carries the number so the
    /// answer is "raise it to this", not "it did not work".
    /// </summary>
    [Fact]
    public void ADeeperProgramThanTheRiseIsRefusedWithTheNumberItNeeds()
    {
        var (text, report) = DryRun.Rewrite(
            """
            G21 G90
            G0 X0 Y0
            G1 Z-4.5 F60
            G1 X10 F200
            G0 Z2
            """,
            Raised);

        output.WriteLine(report.Refusal ?? "(not refused)");

        Assert.NotNull(report.Refusal);

        // -4.5 raised by 3 is -1.5, which is 2.0 under the 0.5 mm clearance, so 5.00 mm would do.
        Assert.Contains("-1.50", report.Refusal, StringComparison.Ordinal);
        Assert.Contains("5.00", report.Refusal, StringComparison.Ordinal);

        // And the program comes back untouched, so nobody can run the half-rewritten thing.
        Assert.Equal("G21 G90\nG0 X0 Y0\nG1 Z-4.5 F60\nG1 X10 F200\nG0 Z2", text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// The three commands that move work zero out from under the rewrite, each refused by name.
    ///
    /// **A raised run adds to the numbers already in the file**, so it only means "three
    /// millimetres higher" while the coordinate system those numbers are in stays still. `G92` and
    /// `G10` move it. `G38` feels for a surface the rise has just put three millimetres further
    /// away, so it would touch off on the wrong plane or find nothing at all.
    ///
    /// The flat run does not care about any of them — it replaces every Z with the same number, so
    /// whatever that number is measured from, it is above it. Which is why these are refusals the
    /// raised style had to add rather than ones it inherited.
    /// </summary>
    [Theory]
    // Each line carries exactly one fault. The probe was `Z-5` to begin with, which is also deeper
    // than the rise clears — so the fixture had two reasons to be refused and the test only asked
    // about one of them. It would still have caught the probe check being removed, by the message
    // changing, but a fixture that is wrong in two ways cannot say which way it is being read.
    [InlineData("G92 Z0", "G92")]
    [InlineData("G10 L20 P1 Z0", "G10")]
    [InlineData("G38.2 Z-0.2 F50", "G38")]
    // The decimal variants, which do the same job as the number they vary and were not caught:
    // the word matcher read the point into the digit run and then failed to parse "92.3" as an
    // integer, so every one of these produced a raised dry run with no refusal at all. G92.3
    // restores a saved work offset, which is exactly what the G92 refusal exists to stop.
    [InlineData("G92.1", "G92")]
    [InlineData("G92.3", "G92")]
    [InlineData("G38.3 Z-0.2 F50", "G38")]
    // A tool-length offset moves the datum the rise is measured from. G43.1 is what a tool setter
    // writes into start G-code, and start G-code reaches every program.
    [InlineData("G43.1 Z-0.050", "G43")]
    [InlineData("G43 H1", "G43")]
    [InlineData("G49", "G49")]
    // Return-to-home ends at a machine position whatever its Z word says, so raising the Z moves
    // the waypoint and not the destination — the same hole as G53, and one the clearance check
    // cannot see, because it reads the file back as though every Z were in work coordinates.
    [InlineData("G28 Z5", "G28")]
    [InlineData("G28", "G28")]
    [InlineData("G30 Z5", "G30")]
    public void ItRefusesWhatMovesWorkZero(string line, string named)
    {
        var (text, report) = DryRun.Rewrite(
            $"""
            G21 G90
            G0 X0 Y0
            {line}
            G1 Z-0.5 F60
            """,
            Raised);

        output.WriteLine($"{named}: {report.Refusal}");

        Assert.NotNull(report.Refusal);
        Assert.Contains(named, report.Refusal, StringComparison.Ordinal);
        Assert.Contains("line 3", report.Refusal, StringComparison.Ordinal);
        Assert.Contains(line, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A machine-coordinate Z is refused, and the reason matters more than the rule.
    ///
    /// `G53` is measured from the machine's own zero — somebody's tool-change position — so raising
    /// it by three millimetres would be meaningless. But leaving it alone is not enough either: the
    /// safety check reads the rewritten file back, and has no way to know that one Z in it is in a
    /// different coordinate system. Either way the promise has a hole in it, so the honest answer
    /// is to say so rather than hand over a program with an unvouched-for move inside.
    /// </summary>
    [Fact]
    public void AMachineCoordinateZIsRefused()
    {
        var (text, report) = DryRun.Rewrite(
            """
            G21 G90
            G1 Z-0.5 F60
            G53 G0 Z-2
            """,
            Raised);

        output.WriteLine(report.Refusal ?? "(not refused)");

        Assert.NotNull(report.Refusal);
        Assert.Contains("G53", report.Refusal, StringComparison.Ordinal);
        Assert.Contains("line 3", report.Refusal, StringComparison.Ordinal);
        Assert.Contains("G53 G0 Z-2", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A program that travels before it commands a Z still gets a dry run.
    ///
    /// **The parser's Z starts at zero because it has to start somewhere**, and that zero was being
    /// counted as a commanded position. So a program whose start G-code says `G0 X0 Y0` — which
    /// framing only warns about, never rejects — measured a lowest point of 0.00 mm however high
    /// the rise was, and was refused with the advice to raise it further. That advice could never
    /// work: the number being refused does not depend on the rise, so every retry gave the same
    /// answer. Raised is the default, so this lost the dry run entirely for anyone with such a
    /// line, which is the person most likely to want one.
    ///
    /// The three rises are the proof the old behaviour was not merely strict: under it all three
    /// reported 0.00 and all three were refused.
    /// </summary>
    [Theory]
    [InlineData(3.0)]
    [InlineData(10.0)]
    [InlineData(25.0)]
    public void TravellingBeforeTheFirstZDoesNotSinkTheMeasurement(double riseMm)
    {
        var (text, report) = DryRun.Rewrite(
            """
            G21 G90
            G0 X0 Y0
            G0 Z2.000
            G1 Z-0.050 F60
            G1 X10.000 F200
            M30
            """,
            Raised with { RiseMm = riseMm });

        output.WriteLine($"rise {riseMm:F2}: lowest {report.LowestZMm:F3}, {report.Refusal ?? "no refusal"}");

        Assert.Null(report.Refusal);
        Assert.True(report.StaysClear);

        // The deepest *commanded* Z is -0.050, so raised it is the rise less fifty microns.
        Assert.Equal(riseMm - 0.05, report.LowestZMm, 3);

        // And the file really is the raised one, not the original handed back.
        Assert.Contains(
            FormattableString.Invariant($"G1 Z{riseMm - 0.05:F3} F60"), text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A program that never commands a Z at all is refused, not handed back as itself.
    ///
    /// **There is nothing to raise, so the file would be the real program with the spindle off.**
    /// Excluding uncommanded moves from the measurement — right in itself — left this case
    /// measuring nothing, and an empty measurement fell back to reporting the rise. So a 2D profile
    /// or a hand-written program came back byte-for-byte unchanged, under a header saying *every Z
    /// raised by 3.00 mm*, with the summary quoting a lowest point of 3.000 mm that no line in the
    /// file asks for. Set work zero as that header instructs and it cuts at Z0 for its whole length.
    ///
    /// Nothing the app emits looks like this — every program it writes opens with a lift — but
    /// `DryRun` is public, and a file somebody else wrote is exactly what a dry run is for.
    /// </summary>
    [Fact]
    public void AProgramThatCommandsNoZIsRefused()
    {
        var program = """
            G21 G90
            G0 X0 Y0
            G1 X10 Y0 F200
            G1 X10 Y10 F200
            M30
            """;

        var (text, report) = DryRun.Rewrite(program, Raised);

        output.WriteLine(report.Refusal ?? $"(not refused) lowest {report.LowestZMm:F3}");

        Assert.NotNull(report.Refusal);
        Assert.False(report.StaysClear);

        // And the body comes back untouched, so nothing half-rewritten can be run. Both sides
        // normalised: the fixture above is a raw literal, which carries this source file's CRLF.
        Assert.Equal(
            program.Replace("\r\n", "\n", StringComparison.Ordinal),
            text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// Travelling in the plane before the first commanded Z does not make the run dirty.
    ///
    /// The clearance check tests where a move starts as well as where it ends, and the first
    /// measured move inherits its start from the last move that was deliberately *not* measured —
    /// the parser's uncommanded zero. So this reported `StaysClear` false while refusing nothing,
    /// which is the worst of the two: a file written and shipped carrying its own evidence that the
    /// promise was broken, and nothing in `src/` reads the flag to notice.
    ///
    /// The boundary is exempt because that height was never commanded. Every later move starts
    /// where the one before it ended, so they are all still checked at both ends.
    /// </summary>
    [Fact]
    public void TheUncommandedStartingHeightDoesNotMakeTheRunDirty()
    {
        var (_, report) = DryRun.Rewrite(
            """
            G21 G90
            G0 X20 Y20
            G1 X5 Y5 Z-0.100 F300
            G0 Z2.000
            M30
            """,
            Raised);

        output.WriteLine($"lowest {report.LowestZMm:F3}, clear {report.StaysClear}, "
            + (report.Refusal ?? "no refusal"));

        Assert.Null(report.Refusal);
        Assert.True(report.StaysClear);
        Assert.Equal(2.9, report.LowestZMm, 3);
    }

    /// <summary>
    /// With the feeds replaced, every line gets the rapid rate — including the one that passes
    /// through untouched.
    ///
    /// `G53` is skipped by the rise because it is in machine coordinates, and it was skipping the
    /// feed rewrite with it. That left one line in the file still carrying its cutting feed, in a
    /// program whose header says every move runs at the rapid rate.
    /// </summary>
    [Fact]
    public void ThePassedThroughMachineCoordinateLineStillLosesItsFeed()
    {
        var (text, report) = DryRun.Rewrite(
            """
            G21 G90
            G1 Z-0.500 F60
            G53 G1 X0 Y0 F60
            M30
            """,
            Raised with { KeepFeeds = false, RapidMmPerMin = 2000 });

        output.WriteLine(text);

        Assert.Null(report.Refusal);
        Assert.DoesNotContain("F60", text, StringComparison.Ordinal);
        Assert.Contains("G53 G1 X0 Y0 F2000", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same command with no Z passes through untouched: it travels in the plane, and nothing
    /// about the rise concerns it.
    /// </summary>
    [Fact]
    public void AMachineCoordinateMoveInThePlaneIsLeftAlone()
    {
        var (text, report) = DryRun.Rewrite(
            """
            G21 G90
            G1 Z-0.5 F60
            G53 G0 X0 Y0
            """,
            Raised);

        Assert.Null(report.Refusal);

        // Whole lines, not substrings: "Z2.5" is also a prefix of "Z2.55", and the point of this
        // test is that one line changed by exactly the rise and the other did not change at all.
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        // Three decimals because that is what the emitter writes, and a dry run read side by side
        // with the real program should differ in the value and nowhere else. The source line here
        // says "Z-0.5" only because it is hand-written; a real one would say "Z-0.500".
        Assert.Contains("G1 Z2.500 F60", lines);
        Assert.Contains("G53 G0 X0 Y0", lines);
    }

    /// <summary>
    /// A canned cycle's retract plane rises with its depth.
    ///
    /// R and Z share a coordinate system. Lift one without the other and the cycle either drills
    /// through its own retract or the controller refuses the block — and a dry run that will not
    /// run is a dry run nobody learns anything from.
    /// </summary>
    [Fact]
    public void ACannedCyclesRetractPlaneRisesToo()
    {
        var (text, report) = DryRun.Rewrite(
            """
            G21 G90
            G81 X5 Y5 Z-1.9 R1 F100
            """,
            Raised);

        Assert.Null(report.Refusal);

        var cycle = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .First(l => l.Contains("G81", StringComparison.Ordinal));

        output.WriteLine(cycle);

        // The whole line, because "R4" is a prefix of "R40" and "Z1.1" of "Z1.15" — and because
        // what is being claimed is that the two words moved together and nothing else moved at all.
        Assert.Equal("G81 X5 Y5 Z1.100 R4.000 F100", cycle);
    }

    /// <summary>
    /// Every canned cycle's retract plane rises, not only the ones numbered 81 to 89.
    ///
    /// `G73` is the high-speed peck cycle. It carries an R for exactly the same reason and sits
    /// outside that range, so its Z was lifted and its R left behind — and a cycle whose Z is above
    /// its own retract either drills through the retract or is rejected by the controller. The
    /// decimal variants were missed the same way the refusal guards missed `G92.3`.
    /// </summary>
    [Theory]
    [InlineData("G73 X5 Y5 Z-1.900 R1.000 F100", "G73 X5 Y5 Z1.100 R4.000 F100")]
    [InlineData("G83.1 X5 Y5 Z-1.900 R1.000 F100", "G83.1 X5 Y5 Z1.100 R4.000 F100")]
    public void EveryCannedCycleRetractRises(string line, string expected)
    {
        var (text, report) = DryRun.Rewrite($"G21 G90\n{line}", Raised);

        Assert.Null(report.Refusal);

        var cycle = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .First(l => l.StartsWith("G7", StringComparison.Ordinal)
                || l.StartsWith("G8", StringComparison.Ordinal));

        output.WriteLine(cycle);

        Assert.Equal(expected, cycle);
    }

    /// <summary>Incremental mode is refused in both styles, and was before V31.</summary>
    [Fact]
    public void IncrementalModeIsStillRefused()
    {
        var (_, report) = DryRun.Rewrite("G21 G91\nG1 Z-1 F60", Raised);

        Assert.NotNull(report.Refusal);
        Assert.Contains("G91", report.Refusal, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the real boards

    /// <summary>
    /// Every program the test board produces is raiseable, and none of them ends up touching it.
    ///
    /// **The board this is checked against is the one that gets cut.** A 1.6 mm board with 0.3 mm
    /// of break-through goes 1.9 mm deep, so a 3 mm rise leaves 1.1 mm — which is the arithmetic
    /// 6.19 uses to argue the default is right, asserted here against the real programs rather than
    /// left as a sum in prose.
    /// </summary>
    [Fact]
    public void EveryProgramTheTestBoardMakesCanBeRaised()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.MillburnTestBoard));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        // Or the loop below asserts nothing at all, and the only thing standing between that and a
        // green test is the bound at the end happening to fail on double.MaxValue.
        Assert.True(plan.Count > 10, $"the test board planned {plan.Count} programs");

        var lowest = double.MaxValue;

        foreach (var item in plan.Items)
        {
            var (_, report) = DryRun.Rewrite(item.Content, Raised);

            Assert.True(
                report.Refusal is null,
                $"{item.TargetName} could not be raised: {report.Refusal}");

            // `StaysClear` is deliberately not asserted here. It used to be, and it could not fail:
            // every program this board emits opens with a pure vertical lift, so no in-plane move
            // starts or ends below `lowest`, and the refusal above already establishes that. It is
            // weaker still now — a run that is not clear is refused outright, so past that check
            // the flag is true by construction rather than by anything this board does.
            //
            // The case where the two can disagree needs a program that travels before its first Z.
            // That is `TheUncommandedStartingHeightDoesNotMakeTheRunDirty`, which this board cannot
            // produce and so cannot test.

            lowest = Math.Min(lowest, report.LowestZMm);
        }

        output.WriteLine($"{plan.Count} programs, lowest point {lowest:F2} mm above the stock");

        // The number, not a band around it. "At least the clearance" was the same rule the refusal
        // above already enforces — given no refusal it could not have failed — so half of this
        // check was decorative. 3.00 mm of rise less the deepest cut this board asks for, which is
        // 1.6 mm of board plus the 0.3 mm the outline goes through the back: 1.10 mm. That is
        // 6.19's own arithmetic, and if the rise, the stock or the break-through ever moves, this
        // says so instead of quietly still passing.
        Assert.Equal(1.10, lowest, 2);
    }
}
