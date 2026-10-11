using MillBurn.Pipeline;

namespace MillBurn.Tests;

/// <summary>
/// What an edit does to the programs already on screen.
///
/// **Six methods used to answer this for themselves and every one of them was wrong at some
/// point.** None of the six faults was found by a test, because nothing could reach the view
/// model; they came from the bench and from reviews, one at a time, over two sprints. The rules
/// live here now, with no window in them, and each of those faults is a line below.
/// </summary>
public sealed class StalenessTests
{
    /// <summary>
    /// Every reason there is, read off the enum rather than listed here — so a new one joins each
    /// sweep below without being added to it, which is the whole mechanism.
    ///
    /// Non-empty is asserted on the way out, because four tests iterate this and three of them
    /// would otherwise pass having checked nothing at all.
    /// </summary>
    private static IReadOnlyList<StaleBecause> Reasons
    {
        get
        {
            var all = Enum.GetValues<StaleBecause>();
            Assert.NotEmpty(all);
            return all;
        }
    }

    /// <summary>
    /// Every reason has a rule, including one added tomorrow.
    ///
    /// **This is the test the whole entry is for.** 6.51's value was never the six paths that were
    /// already correct — it was the seventh, the one nobody had thought of yet. A new kind of edit
    /// now has to be added to the enum, and the moment it is, this fails until somebody has
    /// decided what it clears. That is the question that used to be optional.
    /// </summary>
    [Fact]
    public void EveryReasonHasARule()
    {
        foreach (var why in Reasons)
        {
            var rule = Staleness.Of(why);

            // Not merely "it returned something": a rule that does nothing at all is the shape a
            // forgotten case would take if the switch ever gained a silent default.
            Assert.True(
                rule.StopRun || rule.ClearPrograms || rule.ForgetOpenedProgram || rule.Rebuild,
                $"{why} has a rule that does nothing");
        }
    }

    /// <summary>
    /// A value that is not a reason is refused rather than quietly treated as one.
    ///
    /// The alternative to throwing is a `_ => default`, which hands back a rule that clears
    /// nothing — and an edit that invalidates nothing is the failure this type exists to prevent,
    /// arriving silently.
    /// </summary>
    [Fact]
    public void SomethingThatIsNotAReasonIsRefused()
    {
        // Said rather than assumed: the test means nothing if 999 ever becomes a real reason.
        Assert.False(Enum.IsDefined((StaleBecause)999));

        Assert.Throws<ArgumentOutOfRangeException>(() => Staleness.Of((StaleBecause)999));
    }

    /// <summary>
    /// Every reason stops a run that is still building.
    ///
    /// **The half that was missed most, and in four separate places.** A preview in flight read
    /// the settings as they were when its request was built, so left alone it finishes and
    /// publishes programs cut to numbers nobody chose any more — then writes its own status over
    /// whatever the edit was going to say. From the bench, which is where this was first caught:
    /// *"if I change a setting, click preview, then change the setting back, the preview completes
    /// instead of being interrupted on the setting change."*
    /// </summary>
    [Fact]
    public void EveryReasonStopsARunInFlight() =>
        Assert.All(Reasons, why => Assert.True(Staleness.Of(why).StopRun, $"{why} leaves a run going"));

    /// <summary>
    /// A run in flight counts as something to act on, even with nothing drawn.
    ///
    /// **This is 6.33 stated as an assertion.** The guard it came from asked whether anything was
    /// drawn and returned before it could stop a run — and a run in flight is exactly when nothing
    /// is drawn, because the edit that started it cleared the last picture. So the condition was
    /// false precisely in the case that needed the most doing. Both guarded reasons are checked,
    /// because the fault was found in one of them and then found again in the other.
    /// </summary>
    [Fact]
    public void ARunInFlightCountsEvenWithNothingDrawn()
    {
        foreach (var why in new[] { StaleBecause.OutputChanged, StaleBecause.FramingChanged })
        {
            var rule = Staleness.Of(why);

            Assert.True(rule.OnlyWhenDrawn, $"{why} is expected to be the guarded kind");
            Assert.True(rule.Applies(anythingDrawn: false, runInFlight: true), $"{why} ignored a run in flight");
            Assert.True(rule.Applies(anythingDrawn: true, runInFlight: false), $"{why} ignored drawn programs");
            Assert.True(rule.Applies(anythingDrawn: true, runInFlight: true), $"{why} ignored both at once");
            Assert.False(rule.Applies(anythingDrawn: false, runInFlight: false), $"{why} acted on nothing");
        }
    }

    /// <summary>
    /// A reason without the guard acts whatever is on screen.
    ///
    /// The guard is a performance decision and it belongs to the reason: an output change arrives
    /// on every tick of a slider, and resetting every layer is one button press. Asserted so that
    /// adding the guard to everything — which looks like a tidy-up — fails here.
    /// </summary>
    [Fact]
    public void AnUnguardedReasonActsOnNothingAsWellAsOnSomething()
    {
        var rule = Staleness.Of(StaleBecause.LayersReset);

        Assert.False(rule.OnlyWhenDrawn);
        Assert.True(rule.Applies(anythingDrawn: false, runInFlight: false));
    }

    /// <summary>
    /// Only an edit that replaces what is underneath it forgets an opened program.
    ///
    /// A standalone program came off disk and was not made from these settings, so changing the
    /// safe height or a layer's output leaves it alone. Closing it, re-reading the board's bytes
    /// and swapping the project all replace what it is drawn over.
    ///
    /// Asserted as a partition rather than three positives, so a new reason cannot join the wrong
    /// side by default.
    /// </summary>
    [Fact]
    public void OnlyAReplacedBoardForgetsAnOpenedProgram()
    {
        StaleBecause[] replacing =
            [StaleBecause.ProgramClosed, StaleBecause.SourceRefreshed, StaleBecause.BoardReplaced];

        foreach (var why in Reasons)
        {
            Assert.Equal(replacing.Contains(why), Staleness.Of(why).ForgetOpenedProgram);
        }
    }

    /// <summary>
    /// A machine-settings change stops the run and keeps the picture.
    ///
    /// **The one reason that does not clear, and it is not an oversight.** Its caller re-previews
    /// straight away so the confirmation lands on programs made with the new numbers, and the run
    /// it starts replaces the backplot rather than leaving a hole where one was. Written down
    /// because it reads like a missing line, and the next person to "fix" it would introduce a
    /// flicker on every visit to the settings window.
    /// </summary>
    [Fact]
    public void ChangingTheMachineStopsTheRunAndKeepsThePicture()
    {
        var rule = Staleness.Of(StaleBecause.MachineChanged);

        Assert.True(rule.StopRun);
        Assert.False(rule.ClearPrograms);
        Assert.False(rule.Rebuild);
    }

    /// <summary>
    /// Replacing the board does not rebuild the scene here.
    ///
    /// Its caller is swapping the project and rebuilds afterwards with everything fresh, because
    /// the rows on screen still belong to the project being replaced and none of what was hidden
    /// in that one is a statement about this one — 6.27, which was one line and came from the
    /// bench. Rebuilding here as well would draw the old board's rows over the new board.
    /// </summary>
    [Fact]
    public void ReplacingTheBoardLeavesTheRebuildToItsCaller()
    {
        var rule = Staleness.Of(StaleBecause.BoardReplaced);

        Assert.True(rule.ClearPrograms);
        Assert.True(rule.ForgetOpenedProgram);
        Assert.False(rule.Rebuild);
    }

    /// <summary>
    /// Everything clears the drawn programs except a machine change.
    ///
    /// **A partition, like the one below it, and for the same reason.** `StopRun` and
    /// `ForgetOpenedProgram` were pinned across the whole enum from the start and these three were
    /// not, which meant a new reason could join with the wrong answer here and nothing would say
    /// so — in a file whose entire purpose is that a new reason cannot be added without the
    /// question being asked. Found by `/describe-test`.
    ///
    /// The exception is the one that re-previews immediately: see
    /// <see cref="ChangingTheMachineStopsTheRunAndKeepsThePicture"/> for why that is not a
    /// missing line.
    /// </summary>
    [Fact]
    public void EverythingClearsTheProgramsExceptAMachineChange() => Assert.All(
        Reasons,
        why => Assert.Equal(why != StaleBecause.MachineChanged, Staleness.Of(why).ClearPrograms));

    /// <summary>
    /// Only the two reasons that arrive repeatedly carry the guard.
    ///
    /// An output change comes on every tick of a slider and framing is saved from a dialog that
    /// can be saved twice; everything else is one button press, one file, one project. Pinned
    /// across the enum so that adding the guard to a reason that does not need it — which looks
    /// like a tidy-up and silently makes that reason do nothing when nothing is drawn — fails
    /// here.
    /// </summary>
    [Fact]
    public void OnlyTheRepeatedReasonsAreGuarded()
    {
        StaleBecause[] guarded = [StaleBecause.OutputChanged, StaleBecause.FramingChanged];

        Assert.All(
            Reasons,
            why => Assert.Equal(guarded.Contains(why), Staleness.Of(why).OnlyWhenDrawn));
    }

    /// <summary>
    /// Everything that clears the drawn programs puts a scene back, unless its caller is about to.
    ///
    /// **The fault this guards** is a path that clears and does not rebuild, which leaves the
    /// viewport showing toolpaths that have just been declared wrong — or nothing at all where a
    /// board was.
    ///
    /// **The three exceptions are the same exception three times.** Replacing the project,
    /// re-reading the source bytes and telling a layer what it is (6.31) all have a caller that
    /// swaps the thing a scene is built *from* and then rebuilds itself; rebuilding here as well runs the whole thing twice, the first time against
    /// geometry that is about to be thrown away. `SourceRefreshed` did exactly that for a day, and
    /// this test is what failed when it was corrected — which is the right way round.
    ///
    /// Written as a partition over the whole enum rather than as a sweep with a skip. The skip was
    /// computed from a production value, so the loop could empty itself and pass having examined
    /// nothing; and `Rebuild` was the last of the five decisions with no sweep pinning it, so a new
    /// reason could have arrived with the wrong answer and nothing would have said so.
    /// </summary>
    [Fact]
    public void ClearingThePicturePutsOneBackUnlessTheCallerWill()
    {
        StaleBecause[] rebuiltByTheirCaller =
            [StaleBecause.BoardReplaced, StaleBecause.SourceRefreshed, StaleBecause.RoleChanged];

        Assert.All(Reasons, why =>
        {
            var rule = Staleness.Of(why);
            var expected = rule.ClearPrograms && !rebuiltByTheirCaller.Contains(why);

            Assert.Equal(expected, rule.Rebuild);
        });
    }
}
