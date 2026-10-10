using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The numbers that describe the machine rather than the board.
///
/// Two things to establish: that they reach the emitted file at all — every one of these was a
/// constant in the source until now, so "the setting exists" and "the setting does anything" are
/// genuinely separate claims — and that an inconsistent set is refused rather than quietly clamped.
/// </summary>
public sealed class MachineSettingsTests(ITestOutputHelper output)
{
    private static string Emit(MachineSettings machine)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode,
            machineSettings: machine);

        return plan.Items.First(i => i.Operation == OperationKind.Isolation).Content;
    }

    // ------------------------------------------------------------------ they reach the file

    /// <summary>
    /// The one that matters most. Every travel move in every program crosses the board at the safe
    /// height, so a clamp taller than it is struck at rapid — and until this setting existed the
    /// only way to raise it was to recompile.
    /// </summary>
    [Theory]
    [InlineData(2.0)]
    [InlineData(6.5)]
    [InlineData(15.0)]
    public void TheSafeHeightIsWhereTravelHappens(double safeMm)
    {
        var text = Emit(new MachineSettings { SafeZMm = safeMm });
        var moves = GcodeParser.Parse(text).Moves.Where(m => m.MovesInPlane).ToList();

        Assert.NotEmpty(moves);

        // Nothing travels sideways higher than the safe height, and the rapids between cuts are at
        // exactly it — which is the property a taller clamp needs.
        var highest = moves.Max(m => m.ToZNm);

        output.WriteLine($"safe {safeMm} mm -> highest in-plane move {Nm.ToMillimetreString(highest, 3)} mm");
        Assert.Equal(Nm.FromMillimetres(safeMm), highest);
    }

    [Fact]
    public void TheApproachHeightIsWhereTheRapidDescentStops()
    {
        var text = Emit(new MachineSettings { SafeZMm = 8, ApproachZMm = 1.25 });

        Assert.Contains("G0 Z1.250", text, StringComparison.Ordinal);
        Assert.Contains("G0 Z8.000", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDecimalCountReachesTheCoordinates()
    {
        var three = Emit(new MachineSettings());
        var two = Emit(new MachineSettings { Decimals = 2 });

        Assert.Contains("Z-0.050", three, StringComparison.Ordinal);
        Assert.DoesNotContain("Z-0.050", two, StringComparison.Ordinal);
        Assert.Contains("Z-0.05", two, StringComparison.Ordinal);

        // And the whole file is smaller, because every coordinate in it lost a digit.
        Assert.True(two.Length < three.Length, "two decimals should produce a smaller file");
    }

    /// <summary>Defaults must keep producing what they produced before this was configurable.</summary>
    [Fact]
    public void TheDefaultsAreWhatWasHardCoded()
    {
        var machine = new MachineSettings();

        Assert.Equal(2.0, machine.SafeZMm);
        Assert.Equal(0.5, machine.ApproachZMm);
        Assert.Equal(3, machine.Decimals);
        Assert.False(machine.CannedCycles);
        Assert.Equal(5, new DryRunSettings().HeightMm);
        Assert.True(new DryRunSettings().KeepFeeds);

        // The two V31 added. The style default is load-bearing rather than cosmetic: it decides
        // which kind of file every export writes, and several tests elsewhere now have to ask for
        // the flat run by name precisely because it changed once already.
        Assert.Equal(DryRunStyle.Raised, new DryRunSettings().Style);
        Assert.Equal(3, new DryRunSettings().RiseMm);
        Assert.Equal(10, new ProbeSettings().SpacingMm);
        Assert.Equal(1, new LevelSettings().SegmentMm);
    }

    // ------------------------------------------------------------------ what it refuses

    /// <summary>
    /// The tool rapids down to the approach height and then feeds, so an approach above the safe
    /// height has nothing to descend through.
    /// </summary>
    [Fact]
    public void AnApproachAboveTheSafeHeightIsRefused()
    {
        var problems = SettingsCheck.Problems(
            new MachineSettings { SafeZMm = 2, ApproachZMm = 3 },
            new DryRunSettings { HeightMm = 5 },
            new ProbeSettings(),
            new LevelSettings());

        output.WriteLine(string.Join("\n", problems));
        Assert.Contains(problems, p => p.Contains("must be below the safe height", StringComparison.Ordinal));
    }

    /// <summary>
    /// Each problem says which section it is in, so the settings window can mark the tab it is on.
    /// With the sections on separate tabs, a problem that did not say where it was would leave the
    /// operator opening every tab to find the number Save is waiting on.
    /// </summary>
    [Fact]
    public void EachProblemSaysWhichSectionItIsIn()
    {
        var found = SettingsCheck.Found(
            new MachineSettings { SafeZMm = 2, ApproachZMm = 3 },
            new DryRunSettings { Style = DryRunStyle.Flat, HeightMm = 1 },
            new ProbeSettings { MaxPoints = 2 },
            new LevelSettings { Smoothing = 2 },
            new MillingDefaults { IsolationWidthMm = -1 });

        output.WriteLine(string.Join("\n", found.Select(p => $"{p.Section}: {p.Text}")));

        Assert.Contains(found, p => p.Section == SettingsSection.Machine && p.Text.Contains("Approach height", StringComparison.Ordinal));
        Assert.Contains(found, p => p.Section == SettingsSection.DryRun && p.Text.StartsWith("Dry-run height", StringComparison.Ordinal));
        Assert.Contains(found, p => p.Section == SettingsSection.Probing && p.Text.Contains("four touches", StringComparison.Ordinal));
        Assert.Contains(found, p => p.Section == SettingsSection.Levelling && p.Text.StartsWith("Smoothing", StringComparison.Ordinal));
        Assert.Contains(found, p => p.Section == SettingsSection.Milling && p.Text.StartsWith("Isolation width", StringComparison.Ordinal));

        // And the plain list is the same problems, in the same order.
        Assert.Equal(
            found.Select(p => p.Text),
            SettingsCheck.Problems(
                new MachineSettings { SafeZMm = 2, ApproachZMm = 3 },
                new DryRunSettings { Style = DryRunStyle.Flat, HeightMm = 1 },
                new ProbeSettings { MaxPoints = 2 },
                new LevelSettings { Smoothing = 2 },
                new MillingDefaults { IsolationWidthMm = -1 }));
    }

    /// <summary>
    /// A dry run held lower than the job's own travel height proves less than the job does, which
    /// is backwards for the thing you run to reassure yourself.
    ///
    /// The flat style, by name. The held height is the flat run's number and only the flat run's;
    /// V31 made the raised style the default, and these problems disable Save, so flagging it
    /// while the raised style is chosen would kill the Save button over a value that changes
    /// nothing in the file — with the tab it points at showing nothing wrong.
    ///
    /// **One theory, not a pair of tests**, because the interesting half is a negative: on its own,
    /// "the raised style is not flagged" passes just as well if the check has stopped reporting
    /// anything at all. Asked as the same call with the style swapped, the flat row is what proves
    /// the rule is still alive when the raised row says it stayed quiet.
    /// </summary>
    [Theory]
    [InlineData(DryRunStyle.Flat, true)]
    [InlineData(DryRunStyle.Raised, false)]
    public void TheHeldHeightIsJudgedOnlyWhenTheRunIsHeldFlat(DryRunStyle style, bool flagged)
    {
        var problems = SettingsCheck.Problems(
            new MachineSettings { SafeZMm = 8 },
            new DryRunSettings { Style = style, HeightMm = 5 },
            new ProbeSettings(),
            new LevelSettings());

        output.WriteLine($"{style}: {(problems.Count == 0 ? "no problems" : string.Join("; ", problems))}");

        Assert.Equal(flagged, problems.Any(p => p.Contains("Dry-run height", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A rise of zero is the real program with the spindle off, which is not a dry run.
    ///
    /// Caught here rather than at export, where the only sign of it is a clearance refusal per
    /// file — one message per program, none of which names the setting that caused it.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    public void ARiseOfZeroOrLessIsRefused(double riseMm)
    {
        var problems = SettingsCheck.Problems(
            new MachineSettings { SafeZMm = 8 },
            new DryRunSettings { Style = DryRunStyle.Raised, RiseMm = riseMm },
            new ProbeSettings(),
            new LevelSettings());

        Assert.Contains(problems, p => p.Contains("Dry-run rise", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(-1.0, 0.5)]
    public void ASafeHeightAtOrBelowWorkZeroIsRefused(double safeMm, double approachMm)
    {
        var problems = SettingsCheck.Problems(
            new MachineSettings { SafeZMm = safeMm, ApproachZMm = approachMm },
            new DryRunSettings(),
            new ProbeSettings(),
            new LevelSettings());

        Assert.Contains(problems, p => p.Contains("above work zero", StringComparison.Ordinal));
    }

    [Fact]
    public void NonsenseProbingIsRefused()
    {
        var problems = SettingsCheck.Problems(
            new MachineSettings(),
            new DryRunSettings(),
            new ProbeSettings { SpacingMm = 0, FeedMmPerMin = 0, MaxDepthMm = 0, MaxPoints = 1 },
            new LevelSettings());

        output.WriteLine(string.Join("\n", problems));
        Assert.Equal(4, problems.Count);
    }

    [Fact]
    public void SmoothingOutsideItsRangeIsRefused()
    {
        Assert.Contains(
            SettingsCheck.Problems(
                new MachineSettings(), new DryRunSettings(), new ProbeSettings(),
                new LevelSettings { Smoothing = 1.5 }),
            p => p.Contains("Smoothing runs from 0", StringComparison.Ordinal));
    }

    [Fact]
    public void ASensibleSetIsAccepted() =>
        Assert.Empty(SettingsCheck.Problems(
            new MachineSettings(), new DryRunSettings(), new ProbeSettings(), new LevelSettings()));

    // ------------------------------------------------------------------ what it merely mentions

    /// <summary>
    /// GRBL ignores canned cycles silently, so a drill file travels the whole pattern without
    /// drilling anything. Not refused — LinuxCNC and Mach3 do support them — but said.
    /// </summary>
    [Fact]
    public void CannedCyclesAreNotedRatherThanRefused()
    {
        var machine = new MachineSettings { CannedCycles = true };

        Assert.Empty(SettingsCheck.Problems(
            machine, new DryRunSettings(), new ProbeSettings(), new LevelSettings()));

        Assert.Contains(
            SettingsCheck.Notes(machine, new ProbeSettings()),
            n => n.Contains("GRBL does not implement", StringComparison.Ordinal));
    }

    [Fact]
    public void AVeryLowSafeHeightIsWorthMentioning() =>
        Assert.Contains(
            SettingsCheck.Notes(new MachineSettings { SafeZMm = 1 }, new ProbeSettings()),
            n => n.Contains("clears a bare board", StringComparison.Ordinal));

    [Fact]
    public void AVeryLongProbingRunIsWorthMentioning() =>
        Assert.Contains(
            SettingsCheck.Notes(new MachineSettings(), new ProbeSettings { MaxPoints = 800 }),
            n => n.Contains("standing", StringComparison.Ordinal));

    // ------------------------------------------------------------------ persistence

    [Fact]
    public void TheySurviveSaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), "millburn-settings-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            new AppSettings
            {
                Machine = new MachineSettings { SafeZMm = 7.5, Decimals = 4, CannedCycles = true },
                DryRun = new DryRunSettings { HeightMm = 12, KeepFeeds = false },
                Probe = new ProbeSettings { SpacingMm = 6, MaxPoints = 400 },
                Level = new LevelSettings { SegmentMm = 0.5, Smoothing = 0.2 },
            }.Save(path);

            var back = AppSettings.LoadOrDefault(path);

            Assert.Equal(7.5, back.Machine.SafeZMm);
            Assert.Equal(4, back.Machine.Decimals);
            Assert.True(back.Machine.CannedCycles);
            Assert.Equal(12, back.DryRun.HeightMm);
            Assert.False(back.DryRun.KeepFeeds);
            Assert.Equal(6, back.Probe.SpacingMm);
            Assert.Equal(400, back.Probe.MaxPoints);
            Assert.Equal(0.5, back.Level.SegmentMm);
            Assert.Equal(0.2, back.Level.Smoothing);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheImportDefaultsAndPanelWidthSurviveSaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), "millburn-import-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            new AppSettings
            {
                Import = ImportDefaults.LaserEtching,
                PanelWidth = 512,
            }.Save(path);

            var back = AppSettings.LoadOrDefault(path);

            Assert.Equal(OutputKind.Svg, back.Import.Copper);
            Assert.Equal(OutputKind.Gcode, back.Import.Outline);
            Assert.Equal(512, back.PanelWidth);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A preset is recognised when it comes back, so the dialog can say which one is in force
    /// rather than only showing six dropdowns.
    /// </summary>
    [Fact]
    public void APresetIsStillItselfAfterARoundTrip()
    {
        Assert.Equal("Milling", ImportDefaults.Milling.PresetName);
        Assert.Equal("Laser etching", ImportDefaults.LaserEtching.PresetName);
        Assert.Equal("Nothing", ImportDefaults.Nothing.PresetName);
        Assert.Null((ImportDefaults.Milling with { Silk = OutputKind.Svg }).PresetName);
    }

    /// <summary>A settings file written before these existed loads with the old hard-coded numbers.</summary>
    [Fact]
    public void ASettingsFileFromBeforeTheseExistedStillLoads()
    {
        var path = Path.Combine(Path.GetTempPath(), "millburn-old-" + Guid.NewGuid().ToString("N") + ".json");

        File.WriteAllText(path, """
            { "SchemaVersion": 1, "BoardThicknessMm": 1.6, "Theme": "Dark" }
            """);

        try
        {
            var back = AppSettings.LoadOrDefault(path);

            Assert.Equal(2.0, back.Machine.SafeZMm);
            Assert.Equal(5, back.DryRun.HeightMm);
            Assert.Equal("Dark", back.Theme);
            Assert.Equal(OutputKind.Gcode, back.Import.Copper);
            Assert.Equal(380, back.PanelWidth);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The default tab sizes: zero means "from the board", and a number means that number.
    ///
    /// **Asked for from the workshop**, 2026-10-01, the day after the tabs were made to size
    /// themselves: *"add some options to the settings → new board tab for default tab width and
    /// thickness."* The derived rule is right for most boards and is nobody's decision; an operator
    /// who knows what their stock and their hands want should be able to say so once rather than on
    /// every board.
    ///
    /// Zero as "let the board decide" is the same idiom the isolation width beside it uses for "one
    /// lap, whatever the tool cuts" — and the reason it is zero rather than a nullable is that this
    /// record is a settings file somebody can open in an editor.
    /// </summary>
    [Fact]
    public void ZeroTabSizesMeanTheBoardDecides()
    {
        var derived = new MillingDefaults();

        output.WriteLine($"out of the box: width {derived.TabWidthMm} mm, left {derived.TabHeightMm} mm");

        Assert.Equal(0, derived.TabWidthMm);
        Assert.Equal(0, derived.TabHeightMm);
        Assert.Null(derived.TabWidthNm);
        Assert.Null(derived.TabHeightNm);

        var set = new MillingDefaults { TabWidthMm = 2.5, TabHeightMm = 0.35 };

        Assert.Equal(Nm.FromMillimetres(2.5), set.TabWidthNm);
        Assert.Equal(Nm.FromMillimetres(0.35), set.TabHeightNm);
    }

    /// <summary>
    /// A tab size that cannot mean anything is refused rather than clamped, and says which tab of
    /// the settings window it is on.
    ///
    /// Only the negatives: zero is the derived case, and there is no upper bound to check without a
    /// board — whether 1.5 mm of material left is absurd depends on a thickness this record does not
    /// hold, and the program already says plainly when a tab is taller than the whole cut.
    /// </summary>
    [Fact]
    public void ANegativeTabSizeIsRefused()
    {
        var found = SettingsCheck.Found(
            new MachineSettings(),
            new DryRunSettings(),
            new ProbeSettings(),
            new LevelSettings(),
            new MillingDefaults { TabWidthMm = -1, TabHeightMm = -0.5 });

        output.WriteLine(string.Join("\n", found.Select(p => $"{p.Section}: {p.Text}")));

        // **New boards, not Milling**, because that is the tab the two boxes are on. The window
        // marks the tab a problem belongs to, and marking the wrong one sends the operator looking
        // for a number that is not there — which a `/code-review` pass caught: the isolation width
        // these are modelled on lives on Milling, and the section was copied with the rule.
        Assert.Contains(found, p => p.Section == SettingsSection.NewBoards
            && p.Text.StartsWith("Tab width", StringComparison.Ordinal));

        Assert.Contains(found, p => p.Section == SettingsSection.NewBoards
            && p.Text.StartsWith("The material left under a tab", StringComparison.Ordinal));

        // And the ordinary pair is accepted, so the rule is about negatives rather than about tabs.
        Assert.Empty(SettingsCheck.Problems(
            new MachineSettings(),
            new DryRunSettings(),
            new ProbeSettings(),
            new LevelSettings(),
            new MillingDefaults { TabWidthMm = 3, TabHeightMm = 0.5 }));

        // **And so is zero, which is the pair every install ships with.** A `/describe-test` pass
        // noticed that nothing here put a zero through the validator: a rule written as "not
        // positive" rather than "negative" would have refused to save the settings of every
        // machine that had never touched a tab.
        Assert.Empty(SettingsCheck.Problems(
            new MachineSettings(),
            new DryRunSettings(),
            new ProbeSettings(),
            new LevelSettings(),
            new MillingDefaults()));
    }

    /// <summary>
    /// They survive the settings file, including a settings file written before they existed.
    ///
    /// The second half is the one that matters: every board the author has already set up was saved
    /// by a build with no tab defaults in it, and reading one of those must give "from the board"
    /// rather than a tab 0 mm wide.
    /// </summary>
    [Fact]
    public void TabDefaultsSurviveTheSettingsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "millburn-tabs-" + Guid.NewGuid().ToString("N")[..8] + ".json");

        try
        {
            new AppSettings
            {
                Milling = new MillingDefaults { TabWidthMm = 2.5, TabHeightMm = 0.35 },
            }.Save(path);

            var back = AppSettings.LoadOrDefault(path);

            output.WriteLine(File.ReadAllText(path));

            Assert.Equal(2.5, back.Milling.TabWidthMm);
            Assert.Equal(0.35, back.Milling.TabHeightMm);

            // A file from before this existed.
            // **0.25, not the 0.4 this record defaults to.** A `/describe-test` pass pointed out
            // that asserting the default back proves nothing: a loader that ignored the whole
            // nested object would have produced 0.4 just as happily.
            File.WriteAllText(path, """
                { "SchemaVersion": 1, "Milling": { "IsolationWidthMm": 0.25 } }
                """);

            var older = AppSettings.LoadOrDefault(path);

            Assert.Equal(0.25, older.Milling.IsolationWidthMm);
            Assert.Null(older.Milling.TabWidthNm);
            Assert.Null(older.Milling.TabHeightNm);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
