using MillBurn.Core;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The half of "check for updates" that decides anything: what GitHub's answer says, and what it
/// means for this build. No network here, which is the point of the split — the request is four
/// lines in the app, and everything that could be wrong is in this file.
/// </summary>
public sealed class ReleaseCheckTests(ITestOutputHelper output)
{
    /// <summary>Trimmed to the two fields that are read, in GitHub's own shape.</summary>
    private const string Answer = """
        {
          "tag_name": "v0.1.6",
          "name": "v0.1.6",
          "html_url": "https://github.com/kitecraft/PCB_MillBurn/releases/tag/v0.1.6",
          "body": "notes"
        }
        """;

    [Fact]
    public void ItReadsTheTagAndTheLink()
    {
        var release = ReleaseCheck.Parse(Answer);

        Assert.NotNull(release);
        output.WriteLine($"{release.Version} — {release.Url}");

        Assert.Equal("0.1.6", release.Version);
        Assert.EndsWith("/releases/tag/v0.1.6", release.Url, StringComparison.Ordinal);
    }

    /// <summary>
    /// A page of HTML from a captive portal, a rate-limit message, an empty body: all of them are
    /// "no answer", never an answer.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>sign in to the wifi</html>")]
    [InlineData("{\"message\":\"API rate limit exceeded\"}")]
    [InlineData("{\"tag_name\":\"\"}")]
    [InlineData("[]")]
    public void NonsenseIsNotARelease(string json) => Assert.Null(ReleaseCheck.Parse(json));

    /// <summary>A release with no link still counts: the tag is what the comparison needs.</summary>
    [Fact]
    public void AMissingLinkFallsBackToTheReleasesPage()
    {
        var release = ReleaseCheck.Parse("{\"tag_name\":\"v0.2.0\"}");

        Assert.NotNull(release);
        Assert.Equal("0.2.0", release.Version);
        Assert.Equal("https://github.com/kitecraft/PCB_MillBurn/releases/latest", release.Url);
    }

    [Theory]
    [InlineData("0.1.5", "v0.1.5", UpdateState.Current)]
    [InlineData("0.1.5", "v0.1.6", UpdateState.Available)]
    [InlineData("0.1.5", "v0.2.0", UpdateState.Available)]
    [InlineData("0.2.0", "v0.1.9", UpdateState.Ahead)]
    // A local build of main carries the version in the props, which is the one just released.
    [InlineData("0.1.6", "v0.1.5", UpdateState.Ahead)]
    // The informational version can carry the commit; the tag never does.
    [InlineData("0.1.5+3a1f2c9", "v0.1.5", UpdateState.Current)]
    // Said to three parts on one side and four on the other, but the same release.
    [InlineData("0.1.5", "v0.1.5.0", UpdateState.Current)]
    [InlineData("0.1.5.0", "v0.1.5", UpdateState.Current)]
    public void TheComparisonSaysWhatThisBuildIs(string build, string latest, UpdateState expected)
    {
        var state = ReleaseCheck.Compare(build, latest);

        output.WriteLine($"{build} against {latest}: {state}");
        Assert.Equal(expected, state);
    }

    /// <summary>
    /// Refuses rather than guesses, the rule every emitted file follows and this follows too: a
    /// version nobody can read is not quietly "up to date", because that is the answer that stops
    /// someone looking.
    /// </summary>
    [Theory]
    [InlineData(null, "v0.1.5")]
    [InlineData("0.1.5", null)]
    [InlineData("unknown", "v0.1.5")]
    [InlineData("0.1.5", "nightly")]
    [InlineData("", "")]
    public void AnUnreadableVersionIsUnknown(string? build, string? latest) =>
        Assert.Equal(UpdateState.Unknown, ReleaseCheck.Compare(build, latest));
}
