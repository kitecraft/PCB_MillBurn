using MillBurn.Core;

namespace MillBurn.Tests;

/// <summary>
/// One Escape, one line (6.54; the race sprint 6 story 3's review left, closed in story 6).
///
/// Escape stops the board check and the findings search together, and each says it was stopped on
/// its own way out. The line the operator is left with must name both, in the same order, however
/// the two finishes interleave — which is a timing nobody can catch at the bench, so it is pinned
/// here instead.
/// </summary>
public sealed class StopAnnouncementTests
{
    private const string Check = "Check stopped; the board was not fully checked.";
    private const string Findings = "Stopped before the search found anything to show.";

    private static StopAnnouncement Line() => new([Check, Findings]);

    /// <summary>A run stopped on its own says its own sentence and nothing else.</summary>
    [Fact]
    public void ALoneStopSaysItsOwnSentence()
    {
        var line = Line();

        Assert.Equal(Findings, line.Say(Findings));
        Assert.Equal(Check, line.Say(Check));
    }

    /// <summary>
    /// Both stopped by one Escape: whichever finishes first, the line ends naming both, the check
    /// first.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothStoppedEndOnOneLineInAFixedOrder(bool checkFirst)
    {
        var line = Line();
        line.StoppedBoth();

        var first = line.Say(checkFirst ? Check : Findings);
        var last = line.Say(checkFirst ? Findings : Check);

        Assert.Equal(checkFirst ? Check : Findings, first);
        Assert.Equal($"{Check} {Findings}", last);
    }

    /// <summary>Once both have spoken, the next stop stands alone again.</summary>
    [Fact]
    public void AJointLineEndsWhenBothHaveSpoken()
    {
        var line = Line();
        line.StoppedBoth();
        line.Say(Check);
        line.Say(Findings);

        Assert.Equal(Findings, line.Say(Findings));
    }

    /// <summary>
    /// A new run starting ends whatever an earlier Escape was gathering: a stop of that new run is
    /// its own news, not the second half of an old line.
    /// </summary>
    [Fact]
    public void ANewRunEndsAnUnfinishedJointLine()
    {
        var line = Line();
        line.StoppedBoth();
        line.Say(Check);

        line.RunStarted();

        Assert.Equal(Findings, line.Say(Findings));
    }

    /// <summary>A sentence the order does not list goes after the ones it does.</summary>
    [Fact]
    public void AnUnlistedSentenceGoesLast()
    {
        var line = Line();
        line.StoppedBoth();
        line.Say("Something else stopped.");

        Assert.Equal($"{Check} Something else stopped.", line.Say(Check));
    }
}
