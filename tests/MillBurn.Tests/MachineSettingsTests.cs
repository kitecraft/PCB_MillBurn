using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;
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
            new DryRunSettings { HeightMm = 1 },
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
                new DryRunSettings { HeightMm = 1 },
                new ProbeSettings { MaxPoints = 2 },
                new LevelSettings { Smoothing = 2 },
                new MillingDefaults { IsolationWidthMm = -1 }));
    }

    /// <summary>
    /// A dry run held lower than the job's own travel height proves less than the job does, which
    /// is backwards for the thing you run to reassure yourself.
    /// </summary>
    [Fact]
    public void ADryRunBelowTheSafeHeightIsRefused()
    {
        var problems = SettingsCheck.Problems(
            new MachineSettings { SafeZMm = 8 },
            new DryRunSettings { HeightMm = 5 },
            new ProbeSettings(),
            new LevelSettings());

        Assert.Contains(problems, p => p.Contains("Dry-run height", StringComparison.Ordinal));
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
}
