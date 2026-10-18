using MillBurn.Core;

namespace MillBurn.Tests;

/// <summary>
/// When a changed setting reaches the settings file (6.50's last row).
///
/// **Both faults in this area were found by reading, not by a test**, because the decision lived in
/// a view model nothing could reach: 6.49, a slider writing the file on every tick, and 6.62, a
/// session-only override that made the stored ruler setting unreachable. Each is a test below, by
/// name, against the keeper the view model now calls.
/// </summary>
public sealed class SettingsKeeperTests
{
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

    private static readonly WindowPlacement Placement = new() { X = 10, Y = 20, Width = 900, Height = 700 };

    /// <summary>A keeper over a fresh default record, and every record it writes, in order.</summary>
    private static (SettingsKeeper Keeper, List<AppSettings> Writes) Keeper()
    {
        var writes = new List<AppSettings>();
        return (new SettingsKeeper(new AppSettings(), writes.Add), writes);
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

    /// <summary>
    /// A value with no rule is refused before the edit runs, so the record is not left changed and
    /// unwritten by a setting nobody decided about.
    /// </summary>
    [Fact]
    public void ASettingWithNoRuleChangesNothing()
    {
        var (keeper, writes) = Keeper();
        var editRan = false;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            keeper.Change((SettingChange)999, s =>
            {
                editRan = true;
                return s with { Theme = "Dark" };
            }));

        Assert.False(editRan);
        Assert.Null(keeper.Stored.Theme);
        Assert.Empty(writes);
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
            var mm = tenths / 10.0;
            keeper.Change(SettingChange.BoardThickness, s => s with { BoardThicknessMm = mm });
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

        keeper.Change(SettingChange.BoardThickness, s => s with { BoardThicknessMm = 0.8 });
        keeper.Change(SettingChange.WindowPlacement, s => s with { Window = Placement });

        var written = Assert.Single(writes);
        Assert.Equal(0.8, written.BoardThicknessMm, 9);
        Assert.Equal(900, written.Window!.Width, 9);
    }

    /// <summary>
    /// The thickness is the only setting held back. Everything else is a discrete event and is
    /// written as part of its change — once, with the edit in it.
    /// </summary>
    [Fact]
    public void EveryOtherSettingIsWrittenAsItHappens()
    {
        Assert.Equal(
            [SettingChange.BoardThickness],
            Changes.Where(c => SettingsKeeper.PersistenceOf(c) == Persistence.WrittenWithTheNextWrite));

        foreach (var change in Changes.Where(c => c != SettingChange.BoardThickness))
        {
            var (keeper, writes) = Keeper();

            keeper.Change(change, s => s with { LastExportFolder = change.ToString() });

            var written = Assert.Single(writes);
            Assert.Equal(change.ToString(), written.LastExportFolder);
        }
    }

    /// <summary>
    /// 6.62: a session started with <c>--no-ruler</c> can turn the rulers back on from the menu.
    /// Compared against the stored value — on — the choice of <c>true</c> looked like no change,
    /// and the tick snapped back.
    /// </summary>
    [Fact]
    public void AfterNoRulerTheMenuCanTurnThemBackOn()
    {
        var (keeper, writes) = Keeper();
        keeper.OverrideRulerForThisSession(false);
        Assert.False(keeper.ShowRuler);

        Assert.True(keeper.ChooseRuler(true));

        Assert.True(keeper.ShowRuler);
        Assert.True(Assert.Single(writes).ShowRuler);
    }

    /// <summary>
    /// The override is gone once the menu is used: a choice made afterwards is the stored one, and
    /// turning them off and on again behaves as it would in a session that never had a flag.
    /// </summary>
    [Fact]
    public void UsingTheMenuDropsTheOverride()
    {
        var (keeper, writes) = Keeper();
        keeper.OverrideRulerForThisSession(false);

        keeper.ChooseRuler(true);
        keeper.ChooseRuler(false);

        Assert.False(keeper.ShowRuler);
        Assert.False(keeper.Stored.ShowRuler);
        Assert.Equal([true, false], writes.Select(w => w.ShowRuler));
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

        keeper.Change(SettingChange.Theme, s => s with { Theme = "Dark" });
        keeper.Change(SettingChange.WindowPlacement, s => s with { Window = Placement });

        Assert.Equal(2, writes.Count);
        Assert.All(writes, w => Assert.True(w.ShowRuler));
        Assert.True(keeper.Stored.ShowRuler);
        Assert.False(keeper.ShowRuler);
    }

    /// <summary>
    /// A stored ruler choice made by any route ends the override — not only one made through
    /// <see cref="SettingsKeeper.ChooseRuler"/>. Otherwise a handler writing the ruler directly
    /// would store a value the viewport never showed, which is 6.62 by a side door.
    /// </summary>
    [Fact]
    public void AnyStoredRulerChoiceEndsTheOverride()
    {
        var (keeper, writes) = Keeper();
        keeper.OverrideRulerForThisSession(false);

        keeper.Change(SettingChange.Ruler, s => s with { ShowRuler = true });

        Assert.True(keeper.ShowRuler);
        Assert.True(Assert.Single(writes).ShowRuler);
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
