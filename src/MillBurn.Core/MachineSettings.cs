using System.Text.Json.Serialization;

namespace MillBurn.Core;

/// <summary>
/// How this machine moves, and how its files are written.
///
/// Facts about the machine rather than about the board, so they live with the settings and not with
/// the project: a taller clamp does not become a shorter one because a different board was opened.
/// </summary>
public sealed record MachineSettings
{
    /// <summary>
    /// Height for rapid moves between cuts, above work zero.
    ///
    /// Two millimetres clears a bare board and nothing else. It is the number to raise if anything
    /// stands proud of the stock — clamps, hold-downs, tape, a probe clip — because **every travel
    /// move in every program crosses the board at exactly this height**, and a clamp taller than it
    /// is struck at rapid.
    /// </summary>
    public double SafeZMm { get; init; } = 2.0;

    /// <summary>
    /// Height to drop to at rapid before switching to the plunge feed.
    ///
    /// The gap between this and the surface is travelled slowly, so lowering it saves time and
    /// leaves less room for a surface that is higher than expected. It must stay below the safe
    /// height, or the "rapid down, then feed" sequence has nothing to descend through.
    /// </summary>
    public double ApproachZMm { get; init; } = 0.5;

    /// <summary>
    /// How fast the machine traverses, for the time estimates and for the dry run when programmed
    /// feeds are turned off. Not emitted: <c>G0</c> carries no feed word.
    ///
    /// GRBL's <c>$110</c>/<c>$111</c>.
    /// </summary>
    public double RapidMmPerMin { get; init; } = 2000;

    /// <summary>
    /// How fast Z traverses. GRBL's <c>$112</c>.
    ///
    /// Separate because it is usually far slower than X and Y — a leadscrew against gravity rather
    /// than a belt — and because a PCB job is mostly plunging and retracting. On a real machine
    /// measured at <c>$112 = 100</c> against <c>$110 = 2000</c>, the 79 Z moves of one isolation
    /// program came to 108 mm and **one minute sixteen**, a quarter of its running time, all of it
    /// invisible while the estimate costed them at the traverse rate.
    /// </summary>
    public double ZRapidMmPerMin { get; init; } = 600;

    /// <summary>
    /// Acceleration in mm/s². GRBL's <c>$120</c>.
    ///
    /// The number that decides how long a program takes, far more than the feed rates do. Nothing
    /// in a PCB job is long enough to reach full speed: at 20 mm/s² a move must run 56 mm before it
    /// ever touches 2000 mm/min, and isolation moves are a millimetre. Get this wrong and every
    /// estimate is wrong — a dry run of 267 moves took 2 min 21 against a predicted 30 to 63
    /// seconds, and computing it again with the machine's real 20 mm/s² instead of an assumed 200
    /// gave 2 min 29.
    ///
    /// It also decides what the travel optimizer believes. Its whole premise is that short moves
    /// cost more than their length, and an acceleration set ten times too high under-weights the
    /// exact effect it exists to exploit.
    /// </summary>
    public double AccelerationMmPerSecondSquared { get; init; } = 200;

    /// <summary>
    /// How far the controller may cut a corner to carry speed through it, in mm. GRBL's <c>$11</c>.
    ///
    /// This is what puts a real machine somewhere between "stops at every vertex" and "never slows
    /// down", and it is why an estimate can be a bracket rather than a guess.
    /// </summary>
    public double JunctionDeviationMm { get; init; } = 0.01;

    /// <summary>Decimals on coordinates. Three is one micron, past every machine this targets.</summary>
    public int Decimals { get; init; } = 3;

    /// <summary>
    /// How the motion planner, the optimizer and the time estimate all read these.
    ///
    /// One profile rather than one per consumer. There were two, and the second — the one the
    /// estimates used — had no Z rate at all.
    /// </summary>
    public MachineProfile Profile => new()
    {
        RapidMmPerMin = RapidMmPerMin,
        ZRapidMmPerMin = ZRapidMmPerMin,
        AccelerationMmPerSecondSquared = AccelerationMmPerSecondSquared,
        JunctionDeviationMm = JunctionDeviationMm,
    };

    /// <summary>
    /// Emit <c>G81</c>/<c>G83</c> canned cycles for drilling.
    ///
    /// Off, because **GRBL does not implement them** and silently ignores what it cannot parse —
    /// which on a drill file means the spindle travels the whole pattern without ever going down,
    /// and the board comes out with no holes and no error. Turn it on only for LinuxCNC or Mach3.
    /// </summary>
    public bool CannedCycles { get; init; }

    /// <summary>
    /// What a routed hole or slot does with a depth that is not a whole number of laps — and the
    /// stock's alignment holes with their pecks. See <see cref="Core.ShortLastLap"/>.
    /// </summary>
    public ShortLastLap ShortLastLap { get; init; } = ShortLastLap.OwnLap;

    /// <summary>
    /// Always finish a routed hole or slot with a flat lap at full depth, even on a through cut.
    ///
    /// Off, as it always was: a flat lap flattens the floor the last ramp left sloping, and when the
    /// last ramp already starts below the underside there is no floor — only spoilboard. On for
    /// anybody who wants the wall cleaned up by one more lap regardless.
    /// </summary>
    public bool FinishingLapOnThroughCuts { get; init; }

    /// <summary>
    /// What every SVG carries, as layers of its own, for placing it rather than burning.
    /// See <see cref="Core.SvgPlacingLayers"/>.
    ///
    /// On by default, unlike most new choices here, because the thing it prevents is silent: laser
    /// software that imports by content crops each file to its own drawing, and a mask cropped to
    /// its outermost openings lands millimetres out with nothing to say so. Tried in Falcon and
    /// LightBurn first: each colour arrives as its own layer and switching it off moves nothing.
    /// </summary>
    public SvgPlacingLayers SvgPlacingLayers { get; init; } = SvgPlacingLayers.OutlineAndStock;
}

/// <summary>
/// Which placing layers go into every SVG. A list, because which is right depends on what the
/// operator puts against the laser's origin — and the box the layers make is what a content-importing
/// laser program will centre.
/// </summary>
public enum SvgPlacingLayers
{
    /// <summary>
    /// The board outline, and the stock with its alignment holes when there is stock. Every file
    /// imports at the stock's size: for placing the stock, before the board is cut out.
    /// </summary>
    OutlineAndStock,

    /// <summary>
    /// The board outline alone. Every file imports at the board's size: for placing a board that has
    /// already been cut out, against a jig at the laser's origin — the way the workshop does it.
    /// </summary>
    OutlineOnly,

    /// <summary>None: each file is its drawing alone, for software that places by the page.</summary>
    None,
}

/// <summary>What a dry run does, when one is asked for.</summary>
public sealed record DryRunSettings
{
    /// <summary>
    /// How far above work zero to hold the tool.
    ///
    /// High enough to see daylight under it from across a workshop, which is the whole point: a
    /// clearance you have to crouch to confirm is not one you will check.
    /// </summary>
    public double HeightMm { get; init; } = 5;

    /// <summary>
    /// Keep the programmed feeds, so the run takes as long as the real one.
    ///
    /// On, because half of what a dry run answers is "how long am I committing to". Turning it off
    /// runs everything at the rapid rate: quicker to watch, and no longer tells you the time.
    /// </summary>
    public bool KeepFeeds { get; init; } = true;
}

/// <summary>The probing grid, and how it is touched off.</summary>
public sealed record ProbeSettings
{
    /// <summary>
    /// How far apart to space the touches.
    ///
    /// The bow of a clamped board is a long smooth shape, so the surface between two points 10 mm
    /// apart is very well predicted by the two points. Halving this quadruples the probing time.
    /// </summary>
    public double SpacingMm { get; init; } = 10;

    /// <summary>Probing feed. Slow: the accuracy of the whole map rests on it.</summary>
    public double FeedMmPerMin { get; init; } = 30;

    /// <summary>How far below work zero a touch may search before giving up.</summary>
    public double MaxDepthMm { get; init; } = 2;

    /// <summary>How far inside the board to keep the touches. A probe half over the edge reads the table.</summary>
    public double MarginMm { get; init; } = 1;

    /// <summary>The most touches to ask for. Roughly four seconds each, so 200 is thirteen minutes.</summary>
    public int MaxPoints { get; init; } = 200;
}

/// <summary>How closely a levelled program follows the measured surface.</summary>
/// <summary>
/// What a cutting job starts out doing, as opposed to what the machine is.
///
/// These are per-layer settings underneath — a board can isolate its two sides differently — so
/// what lives here is only the value a freshly imported layer begins with.
/// </summary>
public sealed record MillingDefaults
{
    /// <summary>
    /// How wide a moat to clear either side of every trace, in millimetres.
    ///
    /// The number that decides what an isolated board looks like, and until now it was not a number
    /// at all: isolation cut one lap and stopped, which with a 30° V-bit at 0.05 mm deep is a
    /// 0.127 mm hairline. Electrically that separates the nets. Physically it is a gap you cannot
    /// see, cannot solder across without bridging, and can close by handling the board.
    ///
    /// Zero means one pass, whatever the tool happens to cut — the old behaviour, kept reachable.
    /// </summary>
    public double IsolationWidthMm { get; init; } = 0.4;

    [JsonIgnore]
    public long IsolationWidthNm => Nm.FromMillimetres(IsolationWidthMm);
}

public sealed record LevelSettings
{
    /// <summary>The longest a cutting move may be before it is broken up to follow the surface.</summary>
    public double SegmentMm { get; init; } = 1;

    /// <summary>Moves at or below this height get broken up; higher ones only have their ends corrected.</summary>
    public double SubdivideBelowMm { get; init; } = 0.5;

    /// <summary>
    /// How far outside the probed area a job may stray before levelling is refused.
    ///
    /// Outside the measurements the map holds its edge value, which is a good answer a millimetre
    /// out and a guess a centimetre out.
    /// </summary>
    public double MaxOutsideMm { get; init; } = 3;

    /// <summary>
    /// How far to relax the fit, 0 (through every sample) to about 1 (barely more than a plane).
    ///
    /// A touch probe repeats to a few microns, and a surface forced exactly through noise that size
    /// ripples between the samples in a way the board does not.
    /// </summary>
    public double Smoothing { get; init; }
}

/// <summary>Which part of the settings a problem is in.</summary>
public enum SettingsSection
{
    Machine,
    Milling,
    DryRun,
    Probing,
    Levelling,
}

/// <summary>One thing wrong with the settings, and the section it is in.</summary>
public readonly record struct SettingsProblem(SettingsSection Section, string Text);

/// <summary>
/// Whether a set of settings is self-consistent, and what to say if not.
/// </summary>
public static class SettingsCheck
{
    /// <summary>
    /// Everything wrong with the current settings, worst first. Empty when they are fine.
    /// </summary>
    /// <remarks>
    /// Checked rather than clamped. Silently correcting somebody's number leaves them believing the
    /// machine is set up one way while it is set up another, which is the failure this whole app is
    /// arranged to avoid.
    /// </remarks>
    public static IReadOnlyList<string> Problems(
        MachineSettings machine,
        DryRunSettings dryRun,
        ProbeSettings probe,
        LevelSettings level,
        MillingDefaults? milling = null) =>
        [.. Found(machine, dryRun, probe, level, milling).Select(p => p.Text)];

    /// <summary>
    /// The same problems, each with the section it belongs to, so a window that shows the sections
    /// apart can say where each one is.
    /// </summary>
    public static IReadOnlyList<SettingsProblem> Found(
        MachineSettings machine,
        DryRunSettings dryRun,
        ProbeSettings probe,
        LevelSettings level,
        MillingDefaults? milling = null)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(dryRun);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(level);

        var problems = new List<SettingsProblem>();

        if (milling is { } mill)
        {
            if (mill.IsolationWidthMm < 0)
            {
                problems.Add(new SettingsProblem(SettingsSection.Milling, "Isolation width cannot be negative. Zero means a single pass."));
            }

            // Not a hard limit on what the geometry can do — it is a limit on what anybody means.
            // A 5 mm moat around every trace is a board with almost no copper left on it, and it
            // is much more likely to be millimetres typed where microns were meant.
            if (mill.IsolationWidthMm > 5)
            {
                problems.Add(new SettingsProblem(SettingsSection.Milling, "Isolation width above 5 mm would clear most of the copper off the board."));
            }
        }

        if (machine.SafeZMm <= 0)
        {
            problems.Add(new SettingsProblem(SettingsSection.Machine, "Safe height must be above work zero, or travel moves cross the board at "
                + "or below the surface."));
        }

        if (machine.ApproachZMm <= 0)
        {
            problems.Add(new SettingsProblem(SettingsSection.Machine, "Approach height must be above work zero."));
        }

        if (machine.ApproachZMm >= machine.SafeZMm)
        {
            var approach = Invariant($"{machine.ApproachZMm:F2} mm");
            var safe = Invariant($"{machine.SafeZMm:F2} mm");

            problems.Add(new SettingsProblem(SettingsSection.Machine, $"Approach height ({approach}) must be below the safe height ({safe}) — "
                + "the tool rapids down to it before feeding."));
        }

        // The dry run is meant to be visibly clear of everything the real job clears. Held lower
        // than the job's own travel height it proves less than the job does, which is backwards.
        if (dryRun.HeightMm < machine.SafeZMm)
        {
            var held = Invariant($"{dryRun.HeightMm:F2} mm");
            var safe = Invariant($"{machine.SafeZMm:F2} mm");

            problems.Add(new SettingsProblem(SettingsSection.DryRun, $"Dry-run height ({held}) is below the safe height ({safe}). A dry run "
                + "should clear at least as much as the real job does."));
        }

        if (probe.MaxDepthMm <= 0)
        {
            problems.Add(new SettingsProblem(SettingsSection.Probing, "Probe search depth must be greater than zero, or the probe never descends."));
        }

        if (probe.FeedMmPerMin <= 0)
        {
            problems.Add(new SettingsProblem(SettingsSection.Probing, "Probe feed must be greater than zero."));
        }

        if (probe.SpacingMm <= 0)
        {
            problems.Add(new SettingsProblem(SettingsSection.Probing, "Probe spacing must be greater than zero."));
        }

        if (probe.MaxPoints < 4)
        {
            problems.Add(new SettingsProblem(SettingsSection.Probing, "A probing grid needs at least four touches to describe a surface."));
        }

        if (level.SegmentMm <= 0)
        {
            problems.Add(new SettingsProblem(SettingsSection.Levelling, "Levelling segment length must be greater than zero."));
        }

        if (level.Smoothing is < 0 or > 1)
        {
            problems.Add(new SettingsProblem(SettingsSection.Levelling, "Smoothing runs from 0 (through every probe point) to 1 (nearly a plane)."));
        }

        if (machine.Decimals is < 2 or > 5)
        {
            problems.Add(new SettingsProblem(SettingsSection.Machine, "Coordinate decimals should be between 2 and 5. Three is one micron."));
        }

        return problems;
    }

    /// <summary>Things worth saying that are not wrong, only unusual.</summary>
    public static IReadOnlyList<string> Notes(MachineSettings machine, ProbeSettings probe)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(probe);

        var notes = new List<string>();

        if (machine.CannedCycles)
        {
            notes.Add("Canned cycles are on. GRBL does not implement them and ignores what it "
                + "cannot parse, so a drill file would travel the whole pattern without drilling "
                + "anything. Only turn this on for LinuxCNC or Mach3.");
        }

        if (machine.SafeZMm < 2)
        {
            var safe = Invariant($"{machine.SafeZMm:F2} mm");

            notes.Add($"A {safe} safe height clears a bare board and very little else. Every "
                + "travel move crosses the stock at that height.");
        }

        // Four seconds a touch, near enough.
        var minutes = probe.MaxPoints * 4 / 60.0;

        if (minutes > 20)
        {
            var about = Invariant($"{minutes:F0}");

            notes.Add($"{probe.MaxPoints} probe points is up to about {about} minutes of standing "
                + "and watching.");
        }

        return notes;
    }

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
