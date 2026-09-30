using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// That a check is about the thing it says it is about.
///
/// **Why this file exists, written down because the gap it fills was invisible.** When a check was
/// a bare string, everything a test could ask about it was in its text, and the golden baselines
/// pinned that text exactly. Moving to <see cref="Check"/> added a source, a kind and a severity at
/// every producing site at once — and none of the three appears in any emitted file, so the golden
/// baselines went on passing whatever was put in them. A check attached to the wrong layer, or a
/// refusal recorded as advice, would have been caught by nothing at all: the sentence would be
/// right, the panel would look right, and the field underneath would be wrong.
///
/// A `/describe-test` pass over the tests edited alongside that migration is what found it, by
/// saying plainly of one of them that it reads `Message` and never `Severity`, `Kind` or `Source`.
/// That was true of every test in the repository.
///
/// So these are invariants rather than pinned values. Wording is already pinned in a dozen places
/// and in the goldens; what is not pinned anywhere else is whether the new fields agree with the
/// item carrying them.
/// </summary>
public sealed class CheckSourceTests(ITestOutputHelper output)
{
    /// <summary>
    /// A cut wide enough that the copper checks have something to say.
    ///
    /// **The depth is what does it, not the moat**, which is worth stating because the first
    /// version of this file set only the moat and got nothing. A wider moat adds laps; it does not
    /// change how wide the cutter is, and what fuses two nets together is the cut width. For a
    /// V-bit that is a function of depth alone, so 0.4 mm of moat at the default 0.05 mm produced
    /// a plan whose every check was <see cref="CheckSeverity.Advice"/> — nothing refused, nothing
    /// electrical, and two assertions failing for a reason that had nothing to do with what they
    /// were testing.
    ///
    /// 0.75 mm is deliberately deeper than anybody would cut a board, and it is the depth
    /// [06 §6.42](../../Documentation/06-Roadmap-and-Risks.md) records the test board fusing at:
    /// one piece of copper holding twenty-three nets. Nothing here asserts that number — it is
    /// simply the setting known to make the electrical path produce something, which is what these
    /// tests need in order to be about anything.
    /// </summary>
    private static ExportPlan FusedByAWideCut(string board)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
                IsolationWidthNm = Nm.FromMillimetres(0.4),
                DepthNm = Nm.FromMillimetres(0.75),
            },
            StringComparer.Ordinal);

        return ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);
    }

    /// <summary>
    /// A check that names a file names the file of the item it is on.
    ///
    /// **The one that would catch a mis-wired migration.** The planner builds its checks inside
    /// nested helpers — isolation, drilling, the outline, the pocket — each handed a layer, and
    /// every one of them asks a shared helper for a source. Passing the wrong layer in any of them
    /// produces a check that reads correctly, renders correctly, and points at the wrong file; the
    /// window would then colour it as another layer's and a reader would go and look at the wrong
    /// copper.
    /// </summary>
    [Fact]
    public void ACheckThatNamesAFileNamesItsOwnItemsFile()
    {
        var plan = FusedByAWideCut(RealBoards.PogoTest1);
        var compared = 0;

        foreach (var item in plan.Items)
        {
            // The stock is planned from the blank rather than from a layer, and says so: its
            // checks carry CheckSource.Stock, which names no file. Its item's LayerFileName is the
            // "(stock)" sentinel, so comparing the two would be comparing a real file name to a
            // placeholder.
            if (item.LayerFileName == ExportPlanner.StockLayer)
            {
                continue;
            }

            foreach (var check in item.Warnings.Where(c => c.Source.FileName is not null))
            {
                Assert.Equal(item.LayerFileName, check.Source.FileName);
                Assert.Equal(item.Role, check.Source.Role);
                compared++;
            }
        }

        output.WriteLine($"{compared} file-bearing checks across {plan.Items.Count} items");

        // Without this the loop above passes on a plan whose checks all carry no file at all,
        // which is the same shape of hole this file was written to close.
        Assert.True(compared > 0, "no check carried a file name, so nothing above was compared");
    }

    /// <summary>
    /// Every check says what it is about, in a word a reader would recognise.
    ///
    /// The label is what the panel will put at the head of the line, so a blank one is a line that
    /// begins with ": ". Checked across every item rather than sampled, because a source is built
    /// per call site and one forgotten site is exactly the failure.
    /// </summary>
    [Fact]
    public void EveryCheckCarriesALabel()
    {
        var plan = FusedByAWideCut(RealBoards.PogoTest1);
        var seen = 0;

        foreach (var check in plan.Items.SelectMany(i => i.Warnings))
        {
            Assert.False(
                string.IsNullOrWhiteSpace(check.Source.Label),
                $"a {check.Kind} check has no source label: {check.Message}");
            seen++;
        }

        Assert.True(seen > 0, "this board produced no checks, so nothing was examined");
    }

    /// <summary>
    /// Every check about a tool is advice, which is <see cref="ToolAdvice"/>'s own rule and not
    /// this file's opinion.
    ///
    /// The name says *advice* rather than *never a refusal* because the body is the stronger of
    /// the two: an error would fail this as surely as a refusal would, and a name should not
    /// promise less than it checks any more than it should promise more.
    ///
    /// Its class comment states it outright — *"Advice, never refusal. Every threshold here is a
    /// rule of thumb for FR4 on a hobby machine, and somebody who knows their setup better than we
    /// do is entitled to ignore all of it."* A tool check promoted to a refusal would be the
    /// application telling an operator it had declined to do something on the strength of a rule of
    /// thumb about their own machine, which is the opposite of what that class promises.
    /// </summary>
    [Fact]
    public void EveryCheckAboutAToolIsAdvice()
    {
        var plan = FusedByAWideCut(RealBoards.PogoTest1);

        var tools = plan.Items
            .SelectMany(i => i.Warnings)
            .Where(c => c.Kind == CheckKind.Tool)
            .ToList();

        Assert.NotEmpty(tools);

        foreach (var check in tools)
        {
            Assert.Equal(CheckSeverity.Advice, check.Severity);
        }
    }

    /// <summary>
    /// Copper the cut will not separate is a refusal, and names the layer it is about.
    ///
    /// This is the severity the whole three-level scale exists for, and the one most easily got
    /// wrong in either direction. It is not an error — the file is correct and safe to run — and it
    /// is not advice, because what will not happen is precisely the thing the operator asked for.
    ///
    /// **The second half of the name used to be a weaker claim than it sounded.** This asserted
    /// only that the source carried *some* file name, which a check hung on the wrong layer
    /// satisfies perfectly. A `/describe-test` pass over this very file said so: *"the test does
    /// not verify that the file named is the right layer for that check — only that a file name is
    /// present"*. It walks the items now, so the name is the claim.
    /// </summary>
    [Fact]
    public void CopperThatWillNotSeparateIsARefusalAndNamesItsLayer()
    {
        var plan = FusedByAWideCut(RealBoards.MillburnTestBoard);
        var electrical = new List<Check>();

        foreach (var item in plan.Items)
        {
            foreach (var check in item.Warnings.Where(c => c.Kind == CheckKind.Electrical))
            {
                output.WriteLine($"[{check.Severity}] {check.Source.Label}: {check.Message}");

                // The layer it is about, not merely a layer. An electrical finding points the
                // operator at a piece of copper to go and look at, so naming another layer's file
                // sends them to the wrong side of the board.
                Assert.Equal(item.LayerFileName, check.Source.FileName);
                Assert.Equal(item.Role, check.Source.Role);

                electrical.Add(check);
            }
        }

        Assert.NotEmpty(electrical);

        // Not all of them: two say how far the check could see rather than what it found, and those
        // are advice on purpose. What must not happen is the set being uniformly one severity,
        // which is what a migration that defaulted instead of deciding would produce.
        Assert.Contains(electrical, c => c.Severity == CheckSeverity.Refusal);
    }

    /// <summary>
    /// Two layers saying the same thing are two lines, each naming its own layer — and the panel
    /// still never shows one line twice.
    ///
    /// **This test used to claim the opposite, and changing it is the point.** While a check
    /// rendered as its message alone, one sentence produced for the top copper and again for the
    /// bottom was genuinely one line, and collapsing the pair was right: showing an identical
    /// sentence twice is the duplicate-line defect a `/code-review` pass caught here on this exact
    /// board — six checks, five lines before the regression, six after it.
    ///
    /// Now that the line begins with its source the pair is *not* identical, and collapsing it
    /// would hide a finding about one side of the board. The previous version of this test said in
    /// its own summary that it was expected to change here, so it is changing deliberately rather
    /// than being discovered by a failure somebody then explained away.
    ///
    /// **The de-duplication itself has not moved and must not.** The key is what a reader can see,
    /// so the second assertion below is the half that survives unchanged: two checks that render
    /// the same are one line however much their fields differ.
    /// </summary>
    [Fact]
    public void TwoLayersSayingTheSameThingAreTwoLinesThatNameTheirLayers()
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoUno));

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

        var preview = PreviewBuild.From(
            plan, loaded.Bounds, new MachineProfile(), CancellationToken.None);

        var everyCheck = plan.Items.SelectMany(i => i.Warnings).ToList();
        var lines = preview.Warnings.Select(w => w.Line).ToList();

        output.WriteLine($"{everyCheck.Count} checks across the plan, {lines.Count} lines in the panel");
        output.WriteLine(string.Join("\n", lines));

        // The guard that makes the rest mean anything: this board really does say one thing about
        // two different layers. Stated on the **message**, because that is what used to collide —
        // asking it of `Line` would now be asking whether the feature under test is switched off.
        var repeated = everyCheck
            .GroupBy(c => c.Message, StringComparer.Ordinal)
            .Where(g => g.DistinctBy(c => c.Source.Label, StringComparer.Ordinal).Count() > 1)
            .ToList();

        Assert.True(
            repeated.Count > 0,
            "this board no longer says the same thing about two layers, so nothing here is tested");

        // A preview that produced no warnings at all would satisfy every distinctness check
        // perfectly. The guard above is on the plan's checks, not on the panel's lines, so it does
        // not cover that — pointed out by a `/describe-test` pass over this file.
        Assert.NotEmpty(lines);

        // **Both survive, and each says which layer it is about.** This is the half that inverted:
        // collapsing these would tell an operator about a short on one side of a board and stay
        // silent about the same short on the other.
        foreach (var group in repeated)
        {
            foreach (var check in group)
            {
                Assert.Contains(check.Line, lines, StringComparer.Ordinal);
            }

            // **The half that makes them findable**, and the one worth asserting: these checks share
            // a message, so if their lines did not differ the pair would be two identical rows and
            // the reader could not tell which side of the board each was about.
            //
            // A `/describe-test` pass removed what stood here before — a `StartsWith` on the
            // source's own label, which `Check.Line` is built from and which therefore held by
            // definition for every check, empty labels included.
            Assert.Equal(
                group.Count(),
                group.Select(c => c.Line).Distinct(StringComparer.Ordinal).Count());
        }

        // And the half that did not: no line appears twice, whatever the fields behind it say.
        Assert.Equal(lines.Count, lines.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The severities are actually used, rather than every check landing on one of them.
    ///
    /// A blunt guard, and the reason for it is blunt too: thirty-eight call sites were converted in
    /// one pass, and the cheapest wrong way to do that is to give them all the same severity and
    /// let the text carry the difference as it did before.
    ///
    /// **It has to be a board with something refused, and that is not a technicality.** PogoTest1
    /// at this cut produces nothing but advice, which is the correct answer for it: a clean board
    /// with nothing declined has one severity, and a test asserting otherwise would be asserting
    /// that the application complains. The test board fuses copper at this depth and so has
    /// refusals to go with its advice.
    /// </summary>
    [Fact]
    public void ABoardWithSomethingRefusedProducesMoreThanOneSeverity()
    {
        var plan = FusedByAWideCut(RealBoards.MillburnTestBoard);

        var severities = plan.Items
            .SelectMany(i => i.Warnings)
            .Select(c => c.Severity)
            .Distinct()
            .ToList();

        var named = string.Join(", ", severities.Order().Select(s => s.ToString()));
        output.WriteLine(named);

        // `named` rather than `severities.Single()`: an assertion message is built before the
        // assertion runs, so a Single() in it throws on the passing path instead of explaining the
        // failing one — which is the failure looking like the opposite of what it is.
        Assert.True(severities.Count > 1, $"every check on this board is {named}");
    }

    /// <summary>
    /// A check reads as the thing it is about, then what is wrong with it.
    ///
    /// **The request, from the bench, twice in one sitting:** *"Each item should list its source
    /// first, then the message."* Before a check had a source there was nothing to put first, and
    /// provenance was whatever the sentence happened to begin with — which for most of them was
    /// nothing at all.
    ///
    /// **Written wrongly first, and a `/describe-test` pass said so in one sentence.** The original
    /// asserted `Source.Label + " — " + Message == Line` across every check — which is the formula
    /// `Check.Line` is *defined* by, read off the same two fields of the same object. It restated
    /// the implementation rather than checking it, and could only ever fail if somebody edited that
    /// one line. It would have passed just as happily had the planner attached every check to the
    /// wrong layer, which is the thing worth knowing.
    ///
    /// So the value below is **written out in full**. It is the bottom copper's mirror warning on
    /// PogoTest1, a line also pinned in that board's golden baseline, which is what makes it safe to
    /// spell out here: if the wording moves, the golden moves with it and both are read together.
    ///
    /// The sweep after it states the property rather than the formula — every check gained a prefix
    /// and kept its message — so it stays true of a separator somebody changes deliberately, while
    /// the literal above is what pins the separator itself.
    /// </summary>
    [Fact]
    public void ALineBeginsWithTheThingItIsAbout()
    {
        var plan = FusedByAWideCut(RealBoards.PogoTest1);
        var checks = plan.Items.SelectMany(i => i.Warnings).ToList();
        var lines = checks.Select(c => c.Line).ToList();

        output.WriteLine(string.Join("\n", lines));

        Assert.Contains(
            "Bottom copper — Mirrored: the stock must be flipped left-to-right about its vertical "
                + "centreline, and re-registered. Flipping it the other way cuts a mirror image.",
            lines,
            StringComparer.Ordinal);

        // Every one of them, not just the one spelled out: the story's claim was that forty lines
        // gained a prefix they had never had. A check whose line still *is* its message is one the
        // migration missed, and it would read in the panel as though it were about nothing.
        foreach (var check in checks)
        {
            Assert.EndsWith(check.Message, check.Line, StringComparison.Ordinal);
            Assert.True(
                check.Line.Length > check.Message.Length,
                $"this check still reads as its message alone: {check.Line}");
        }

        Assert.NotEmpty(checks);
    }

    /// <summary>
    /// No check names its own source twice.
    ///
    /// **This is the rule that had to become checkable the moment the panel started printing the
    /// label.** Every surface now puts the source in front of the message, so a sentence that also
    /// begins by naming the thing it is about says it twice, and the second one is the one nobody
    /// can fix by changing a field.
    ///
    /// It found two when it was written, both in the window rather than the planner and so out of
    /// this test's reach: a role-guessed layer read *"Top copper — F_Cu.gbr declares no file
    /// function; role guessed as Top copper"*, and an unreadable file read *"F_Cu.gbr — Could not
    /// read F_Cu.gbr: unexpected token at line 12"*. Both were found by reading a screenshot, which
    /// is why this exists: a screenshot is not run by anything, and the planner is where most of
    /// the sentences are.
    ///
    /// **Swept across four boards rather than sampled from one.** The duplication is a property of
    /// one sentence meeting one source, so a board that happens not to produce that pairing proves
    /// nothing about the pairing existing.
    ///
    /// `GridStripConnector` is deliberately not among them: it produces no checks at all at this
    /// cut, so it would contribute a passing case that examined nothing. The guard at the end is
    /// what said so — it failed the moment the board was added, which is the whole reason a sweep
    /// like this needs one.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.MillburnTestBoard)]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.ArduinoUno)]
    [InlineData(RealBoards.ArduinoMega)]
    public void NoCheckNamesItsOwnSourceTwice(string board)
    {
        var plan = FusedByAWideCut(board);
        var seen = 0;

        foreach (var check in plan.Items.SelectMany(i => i.Warnings))
        {
            Assert.DoesNotContain(check.Source.Label, check.Message, StringComparison.Ordinal);
            seen++;
        }

        output.WriteLine($"{board}: {seen} check(s), none repeating its own label");

        Assert.True(seen > 0, $"{board} produced no checks, so nothing was examined");
    }

    /// <summary>
    /// A refusal and an error both count as something to look at; advice does not.
    ///
    /// **A three-way enum drives a two-way decision in the panel**, and the panel is the reason
    /// this matters: the fold leaves only a heading, and that heading says how many of the checks
    /// want looking at. Getting this predicate wrong would put a number in front of an operator
    /// that says a folded panel is safe to leave folded when it is not.
    ///
    /// Written against the severities directly rather than against a board, because what is being
    /// pinned is the mapping and not which boards happen to produce which severity.
    /// </summary>
    [Fact]
    public void ARefusalAndAnErrorAreBothWorthLookingAt()
    {
        var advice = Check.Advice(CheckSource.Board, CheckKind.Board, "a note");
        var refusal = Check.Refusal(CheckSource.Board, CheckKind.Board, "not done");
        var error = Check.Error(CheckSource.Board, CheckKind.Board, "do not run this");

        Assert.True(advice.IsAdvice);
        Assert.False(advice.IsRefusal);
        Assert.False(advice.IsError);

        Assert.False(refusal.IsAdvice);
        Assert.True(refusal.IsRefusal);
        Assert.False(refusal.IsError);

        // The one that is easy to get wrong: an error is the loudest severity, so a predicate that
        // only matched CheckSeverity.Refusal would leave the worst check in the list counted as
        // advice and the heading of a folded panel would under-report it.
        Assert.False(error.IsAdvice);
        Assert.True(error.IsRefusal);
        Assert.True(error.IsError);
    }
}
