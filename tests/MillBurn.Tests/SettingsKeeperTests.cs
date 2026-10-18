using MillBurn.Core;

namespace MillBurn.Tests;

/// <summary>
/// When a changed setting reaches the settings file (6.50's last row).
///
/// **Both faults in this area were found by reading, not by a test**, because the decision lived in
/// a view model nothing could reach: 6.49, a slider writing the file on every tick, and 6.62, a
/// session-only override that made the stored ruler setting unreachable. Each is a test below, by
/// name, against the keeper the view model now calls — through the same one-per-setting methods it
/// calls, so a setting cannot be described here one way and wired there another.
/// </summary>
public sealed class SettingsKeeperTests
{
    private static readonly WindowPlacement Placement = new() { X = 10, Y = 20, Width = 900, Height = 700 };

    private static readonly AppSettings Defaults = new();

    /// <summary>Every setting there is, read off the enum so a new one joins each sweep unasked.</summary>
    private static IReadOnlyList<SettingChange> Changes
    {
        get
        {
            var all = Enum.GetValues<SettingChange>();
            Assert.NotEmpty(all);
            return all;
        }
    }

    /// <summary>A keeper over a fresh default record, and every record it writes, in order.</summary>
    private static (SettingsKeeper Keeper, List<AppSettings> Writes) Keeper()
    {
        var writes = new List<AppSettings>();
        return (new SettingsKeeper(new AppSettings(), s => { writes.Add(s); return true; }), writes);
    }

    /// <summary>
    /// Every setting has a rule, including one added tomorrow: a new member fails here until
    /// somebody has decided when it is written.
    /// </summary>
    [Fact]
    public void EverySettingSaysWhenItIsWritten()
    {
        foreach (var change in Changes)
        {
            Assert.True(Enum.IsDefined(SettingsKeeper.PersistenceOf(change)), $"{change} has no rule.");
        }
    }

    /// <summary>A value with no rule is refused rather than given one by default.</summary>
    [Fact]
    public void ASettingWithNoRuleIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SettingsKeeper.PersistenceOf((SettingChange)999));
    }

    /// <summary>
    /// The thickness is the only setting held back; the table says so, and the sweep below shows
    /// the setters agree with it.
    /// </summary>
    [Fact]
    public void OnlyTheThicknessIsHeldBack()
    {
        Assert.Equal(
            [SettingChange.BoardThickness],
            Changes.Where(c => SettingsKeeper.PersistenceOf(c) == Persistence.WrittenWithTheNextWrite));
    }

    /// <summary>
    /// 6.49: a full-range drag of the thickness slider — 0.4 to 3.2 mm in 0.1 mm ticks, twenty-nine
    /// calls — writes the file zero times, and the last value is what is kept.
    /// </summary>
    [Fact]
    public void AThicknessSliderDragWritesNothing()
    {
        var (keeper, writes) = Keeper();

        for (var tenths = 4; tenths <= 32; tenths++)
        {
            keeper.SetBoardThickness(tenths / 10.0);
        }

        Assert.Empty(writes);
        Assert.Equal(3.2, keeper.Stored.BoardThicknessMm, 9);
    }

    /// <summary>
    /// The other half of 6.49's bargain: the thickness still survives a restart, because the next
    /// write — the window placement, as it closes — carries the whole record.
    /// </summary>
    [Fact]
    public void TheThicknessIsWrittenWithTheNextWrite()
    {
        var (keeper, writes) = Keeper();

        keeper.SetBoardThickness(0.8);
        keeper.SetWindowPlacement(Placement);

        var written = Assert.Single(writes);
        Assert.Equal(0.8, written.BoardThicknessMm, 9);
        Assert.Equal(Placement, written.Window);
    }

    /// <summary>
    /// **And it rides even on a write that changes nothing else.** The close saves the placement
    /// every time, usually the same placement as last time — which the keeper would otherwise skip
    /// as no change, losing the thickness held since. Once written, the same placement again is
    /// skipped.
    /// </summary>
    [Fact]
    public void AHeldThicknessRidesOnAnOtherwiseEmptyWrite()
    {
        var (keeper, writes) = Keeper();

        keeper.SetWindowPlacement(Placement);
        keeper.SetBoardThickness(0.8);
        keeper.SetWindowPlacement(Placement);
        keeper.SetWindowPlacement(Placement);

        Assert.Equal(2, writes.Count);
        Assert.Equal(1.6, writes[0].BoardThicknessMm, 9);
        Assert.Equal(0.8, writes[1].BoardThicknessMm, 9);
    }

    /// <summary>
    /// Every setter but the thickness writes once, as it happens, with its own change in it — read
    /// back off the written record, so a setter wired to the wrong field fails here by name.
    /// </summary>
    [Fact]
    public void EveryOtherSetterWritesItsOwnChangeOnce()
    {
        var machine = Defaults.Machine with { SafeZMm = 7.5 };
        var dryRun = Defaults.DryRun with { HeightMm = Defaults.DryRun.HeightMm + 1 };
        var framing = Defaults.Framing with { Start = "G21 ; test" };

        var cases = new (string Name, Action<SettingsKeeper> Act, Func<AppSettings, bool> Holds)[]
        {
            ("placement", k => k.SetWindowPlacement(Placement), s => s.Window == Placement),
            ("panel width", k => k.SetPanelWidth(412), s => s.PanelWidth == 412),
            ("theme", k => k.SetTheme("Dark"), s => s.Theme == "Dark"),
            ("machine", k => k.SetMachine(machine, dryRun, Defaults.Probe, Defaults.Level, Defaults.Import, Defaults.Milling),
                s => s.Machine.SafeZMm == 7.5 && s.DryRun.HeightMm == dryRun.HeightMm),
            ("framing", k => k.SetFraming(framing), s => s.Framing.Start == "G21 ; test"),
            ("recent", k => k.RememberRecent(@"C:\boards\a.millburn"), s => s.RecentProjects.Contains(@"C:\boards\a.millburn")),
            ("align hover", k => k.SetAlignHover(4.25), s => s.Align.HoverMm == 4.25),
            ("align folder", k => k.SetAlignFolder(@"C:\align"), s => s.Align.LastFolder == @"C:\align"),
            ("export", k => k.SetExport(@"C:\out", dryRun: true), s => s.LastExportFolder == @"C:\out" && s.WriteDryRun),
            ("layer colour", k => k.SetLayerColour(LayerRole.TopCopper, "#112233"), s => s.LayerColours[LayerRole.TopCopper] == "#112233"),
            ("scene colour", k => k.SetSceneColour("substrate", "#445566"), s => s.SceneColours["substrate"] == "#445566"),
        };

        foreach (var (name, act, holds) in cases)
        {
            var (keeper, writes) = Keeper();

            act(keeper);

            Assert.True(writes.Count == 1, $"{name} wrote {writes.Count} times.");
            Assert.True(holds(writes[0]), $"{name}'s change is not in what was written.");
        }
    }

    /// <summary>
    /// A setter that changes nothing writes nothing: picking the theme already set, saving the
    /// placement already saved. Found by sprint 6's review — every discrete setting used to rewrite
    /// the file whether or not it had changed.
    /// </summary>
    [Fact]
    public void ASetterThatChangesNothingWritesNothing()
    {
        var (keeper, writes) = Keeper();

        keeper.SetTheme(null);
        keeper.SetTheme("Dark");
        keeper.SetTheme("Dark");

        Assert.Equal(["Dark"], writes.Select(w => w.Theme));
    }

    /// <summary>
    /// The undoing setters write once too — from a record that has something to undo, so each is
    /// a change: a layer colour reset, a scene colour reset, all colours reset, a recent project
    /// forgotten.
    /// </summary>
    [Fact]
    public void TheUndoingSettersWriteTheirChangeOnce()
    {
        var start = Defaults
            .WithColour(LayerRole.TopCopper, "#112233")
            .WithSceneColour("substrate", "#445566")
            .WithRecent(@"C:\boards\a.millburn");

        var cases = new (string Name, Action<SettingsKeeper> Act, Func<AppSettings, bool> Holds)[]
        {
            ("layer colour reset", k => k.ResetLayerColour(LayerRole.TopCopper), s => !s.LayerColours.ContainsKey(LayerRole.TopCopper)),
            ("scene colour reset", k => k.ResetSceneColour("substrate"), s => !s.SceneColours.ContainsKey("substrate")),
            ("all colours reset", k => k.ResetColours(), s => s.LayerColours.IsEmpty && s.SceneColours.IsEmpty),
            ("recent forgotten", k => k.ForgetRecent(@"C:\boards\a.millburn"), s => !s.RecentProjects.Contains(@"C:\boards\a.millburn")),
        };

        // The start really holds what each case takes away, or a "lacks" check proves nothing.
        Assert.True(start.LayerColours.ContainsKey(LayerRole.TopCopper));
        Assert.True(start.SceneColours.ContainsKey("substrate"));
        Assert.Contains(@"C:\boards\a.millburn", start.RecentProjects);

        foreach (var (name, act, holds) in cases)
        {
            var writes = new List<AppSettings>();
            var keeper = new SettingsKeeper(start, s => { writes.Add(s); return true; });

            act(keeper);

            Assert.True(writes.Count == 1, $"{name} wrote {writes.Count} times.");
            Assert.True(holds(writes[0]), $"{name}'s change is not in what was written.");
        }
    }

    /// <summary>
    /// **A write the disk refused is tried again**, even for the same values — found by the review
    /// of the no-change skip: the file locked, Save pressed, "Could not save settings" shown, the
    /// file freed, Save pressed again with nothing changed — and skipped, so the numbers were lost
    /// at exit. The refused write leaves the record unwritten, and the next one goes.
    /// </summary>
    [Fact]
    public void ARefusedWriteIsTriedAgain()
    {
        var attempts = new List<AppSettings>();
        var diskWorks = false;
        var keeper = new SettingsKeeper(new AppSettings(), s =>
        {
            attempts.Add(s);
            return diskWorks;
        });

        keeper.SetTheme("Dark");
        diskWorks = true;
        keeper.SetTheme("Dark");
        keeper.SetTheme("Dark");

        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, a => Assert.Equal("Dark", a.Theme));
    }

    /// <summary>The colour resets take back exactly what their setters set.</summary>
    [Fact]
    public void TheColourResetsUndoTheirSetters()
    {
        var (keeper, _) = Keeper();

        keeper.SetLayerColour(LayerRole.TopCopper, "#112233");
        keeper.SetSceneColour("substrate", "#445566");
        Assert.Equal("#112233", keeper.Stored.LayerColours[LayerRole.TopCopper]);

        keeper.ResetLayerColour(LayerRole.TopCopper);
        Assert.False(keeper.Stored.LayerColours.ContainsKey(LayerRole.TopCopper));
        Assert.True(keeper.Stored.SceneColours.ContainsKey("substrate"));

        keeper.ResetSceneColour("substrate");
        Assert.False(keeper.Stored.SceneColours.ContainsKey("substrate"));

        keeper.SetLayerColour(LayerRole.BottomCopper, "#778899");
        keeper.ResetColours();
        Assert.Empty(keeper.Stored.LayerColours);
        Assert.Empty(keeper.Stored.SceneColours);
    }

    /// <summary>A recent project forgotten is gone from the list it was remembered on.</summary>
    [Fact]
    public void AForgottenRecentProjectIsGone()
    {
        var (keeper, _) = Keeper();

        keeper.RememberRecent(@"C:\boards\a.millburn");
        Assert.Contains(@"C:\boards\a.millburn", keeper.Stored.RecentProjects);

        keeper.ForgetRecent(@"C:\boards\a.millburn");
        Assert.DoesNotContain(@"C:\boards\a.millburn", keeper.Stored.RecentProjects);
    }

    /// <summary>
    /// 6.62: a session started with <c>--no-ruler</c> can turn the rulers back on from the menu.
    /// Compared against the stored value — on — the choice of <c>true</c> looked like no change,
    /// and the tick snapped back. The file already says on, so nothing needs writing.
    /// </summary>
    [Fact]
    public void AfterNoRulerTheMenuCanTurnThemBackOn()
    {
        var (keeper, writes) = Keeper();
        keeper.OverrideRulerForThisSession(false);
        Assert.False(keeper.ShowRuler);

        Assert.True(keeper.ChooseRuler(true));

        Assert.True(keeper.ShowRuler);
        Assert.Empty(writes);
    }

    /// <summary>
    /// The override is gone once the menu is used: a choice made afterwards is the stored one, and
    /// is written.
    /// </summary>
    [Fact]
    public void UsingTheMenuDropsTheOverride()
    {
        var (keeper, writes) = Keeper();
        keeper.OverrideRulerForThisSession(false);

        Assert.True(keeper.ChooseRuler(true));
        Assert.True(keeper.ChooseRuler(false));

        Assert.False(keeper.ShowRuler);
        Assert.False(keeper.Stored.ShowRuler);
        Assert.False(Assert.Single(writes).ShowRuler);
    }

    /// <summary>
    /// A session-only override is never written by anything — the hazard that made it a separate
    /// name: before 6.62 it rode with the next write, and closing the window after a screenshot
    /// would have turned the operator's rulers off for good.
    /// </summary>
    [Fact]
    public void ASessionOverrideIsNeverWritten()
    {
        var (keeper, writes) = Keeper();
        keeper.OverrideRulerForThisSession(false);

        keeper.SetTheme("Dark");
        keeper.SetWindowPlacement(Placement);

        Assert.Equal(2, writes.Count);
        Assert.All(writes, w => Assert.True(w.ShowRuler));
        Assert.True(keeper.Stored.ShowRuler);
        Assert.False(keeper.ShowRuler);
    }

    /// <summary>Choosing what is already showing writes nothing and says nothing changed.</summary>
    [Fact]
    public void ChoosingWhatIsShowingWritesNothing()
    {
        var (keeper, writes) = Keeper();

        Assert.False(keeper.ChooseRuler(true));

        keeper.OverrideRulerForThisSession(false);
        Assert.False(keeper.ChooseRuler(false));

        Assert.Empty(writes);
        Assert.False(keeper.ShowRuler);
        Assert.True(keeper.Stored.ShowRuler);
    }
}
