using MillBurn.Core;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Reading a controller's own settings back into the numbers the app estimates with.
///
/// Four numbers decide what the app says about how long a job takes, and three of them had no way
/// to be set. They sat on defaults, and on the first machine they met the defaults were wrong by
/// ten times: an acceleration of 200 mm/s² against a real 20, and a Z traverse of 600 mm/min
/// against a real 100. A dry run estimated at 30 to 63 seconds took 2 min 21.
///
/// The dump below is that machine's, verbatim, because a parser tested only against text somebody
/// invented is a parser tested against nothing.
/// </summary>
public sealed class GrblSettingsTests
{
    private const string RealDump = """
        >>> $$
        $0 = 10    (Step pulse time, microseconds)
        $1 = 25    (Step idle delay, milliseconds)
        $2 = 0    (Step pulse invert, mask)
        $3 = 6    (Step direction invert, mask)
        $10 = 115    (Status report options, mask)
        $11 = 0.010    (Junction deviation, millimeters)
        $12 = 0.002    (Arc tolerance, millimeters)
        $13 = 0    (Report in inches, boolean)
        $20 = 0    (Soft limits enable, boolean)
        $21 = 1    (Hard limits enable, boolean)
        $30 = 1000    (Maximum spindle speed, RPM)
        $32 = 0    (Laser-mode enable, boolean)
        $100 = 800.000    (X-axis travel resolution, step/mm)
        $110 = 2000.000    (X-axis maximum rate, mm/min)
        $111 = 2000.000    (Y-axis maximum rate, mm/min)
        $112 = 100.000    (Z-axis maximum rate, mm/min)
        $120 = 20.000    (X-axis acceleration, mm/sec^2)
        $121 = 20.000    (Y-axis acceleration, mm/sec^2)
        $122 = 20.000    (Z-axis acceleration, mm/sec^2)
        $130 = 500.000    (X-axis maximum travel, millimeters)
        ok
        """;

    [Fact]
    public void ARealDumpReadsBackTheNumbersThatMatter()
    {
        var dump = GrblSettings.Parse(RealDump);

        Assert.Null(dump.Rejection);
        Assert.Equal(2000, dump[110]);
        Assert.Equal(100, dump[112]);
        Assert.Equal(20, dump[120]);
        Assert.Equal(0.010, dump[11]);
    }

    /// <summary>
    /// The point of the whole exercise: a machine profile that came from the machine.
    /// </summary>
    [Fact]
    public void ItCorrectsTheProfileTheAppWasGuessing()
    {
        var (machine, changes) = GrblSettings.Parse(RealDump).ApplyTo(new MachineSettings());

        Assert.Equal(2000, machine.RapidMmPerMin);
        Assert.Equal(100, machine.ZRapidMmPerMin);
        Assert.Equal(20, machine.AccelerationMmPerSecondSquared);
        Assert.Equal(0.010, machine.JunctionDeviationMm);

        // Two of the four moved; the traverse and the junction deviation already matched, and a
        // change list that reported them would be noise over the two that matter.
        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Contains("$112", StringComparison.Ordinal));
        Assert.Contains(changes, c => c.Contains("$120", StringComparison.Ordinal));
    }

    /// <summary>
    /// A partial paste leaves the rest alone. People paste the interesting half of a dump, and
    /// resetting everything else to a default would be a silent undo.
    /// </summary>
    [Fact]
    public void WhatIsNotInThePasteIsNotTouched()
    {
        var before = new MachineSettings { ZRapidMmPerMin = 250, AccelerationMmPerSecondSquared = 33 };
        var (after, _) = GrblSettings.Parse("$120 = 20.000").ApplyTo(before);

        Assert.Equal(20, after.AccelerationMmPerSecondSquared);
        Assert.Equal(250, after.ZRapidMmPerMin);
    }

    /// <summary>
    /// Senders decorate their consoles differently, and the decoration is not the data.
    /// </summary>
    [Theory]
    [InlineData("$112=100")]
    [InlineData("  $112 = 100.000  ")]
    [InlineData("> $112 = 100.000    (Z-axis maximum rate, mm/min)")]
    [InlineData("[12:04:31] $112 =100")]
    public void TheNumberIsFoundWhateverIsAroundIt(string line)
    {
        // The timestamped one has the setting after a bracket rather than at the start, which is
        // the one shape this deliberately does not accept — it would mean hunting for "$" anywhere
        // in a line, and a comment mentioning $112 is not a setting.
        var found = GrblSettings.Parse(line)[112];

        Assert.Equal(line.StartsWith('[') ? null : 100, found);
    }

    /// <summary>A report that is not a dump is refused by name, not silently ignored.</summary>
    [Theory]
    [InlineData("Grbl 1.1f ['$' for help]")]
    [InlineData("[VER:1.1f.20230316:]\n[OPT:VMZHL,35,254]\nok")]
    [InlineData("G21 G90\nG0 X0 Y0")]
    public void SomethingElseEntirelyIsRefused(string text)
    {
        var dump = GrblSettings.Parse(text);

        Assert.NotNull(dump.Rejection);
        Assert.Empty(GrblSettings.Parse(text).ApplyTo(new MachineSettings()).Changes);
    }

    /// <summary>
    /// A dump of settings that are real but none of them ours is refused too — it is a different
    /// report, and accepting it would leave the profile untouched while saying it had been read.
    /// </summary>
    [Fact]
    public void ADumpWithoutTheOnesWeUseIsRefused() =>
        Assert.NotNull(GrblSettings.Parse("$0 = 10\n$1 = 25\n$2 = 0").Rejection);

    /// <summary>
    /// Things worth saying once somebody is looking at the dump. Laser mode is the dangerous one:
    /// in it GRBL does not stop at corners for the spindle, and S words drive a laser.
    /// </summary>
    [Fact]
    public void LaserModeIsPointedAt()
    {
        Assert.Contains(
            GrblSettings.Parse(RealDump.Replace("$32 = 0", "$32 = 1", StringComparison.Ordinal)).Notes(),
            n => n.Contains("laser mode", StringComparison.Ordinal));

        Assert.DoesNotContain(
            GrblSettings.Parse(RealDump).Notes(),
            n => n.Contains("laser mode", StringComparison.Ordinal));
    }
}
