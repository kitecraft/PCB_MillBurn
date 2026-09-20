using System.Text.Json;

namespace MillBurn.Core;

/// <summary>What a check for a newer release came to.</summary>
public enum UpdateState
{
    /// <summary>This build matches the latest release.</summary>
    Current,

    /// <summary>There is a newer release than this build.</summary>
    Available,

    /// <summary>This build is newer than any release — someone's own build of main.</summary>
    Ahead,

    /// <summary>Neither version could be read. Never guessed at: see <see cref="ReleaseCheck"/>.</summary>
    Unknown,
}

/// <summary>The release a check found.</summary>
/// <param name="Version">The tag with any leading "v" removed: "0.1.5".</param>
/// <param name="Url">Where to read about it.</param>
public sealed record Release(string Version, string Url);

/// <summary>
/// Reading GitHub's answer about the latest release, and deciding what it means for this build.
///
/// Split from the request that fetches it so that the part with the decisions in it can be tested
/// without a network: everything here is a string in and an answer out. The app supplies the
/// network, asks only when a person presses the button, and sends nothing about the person — the
/// request carries a user agent and nothing else, because a tool that phones home uninvited has no
/// business in a workshop.
/// </summary>
public static class ReleaseCheck
{
    /// <summary>The release GitHub describes, or null if the answer was not what we expected.</summary>
    public static Release? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("tag_name", out var tag)
                || tag.ValueKind != JsonValueKind.String
                || tag.GetString() is not { Length: > 0 } text)
            {
                return null;
            }

            var url = root.TryGetProperty("html_url", out var link) && link.ValueKind == JsonValueKind.String
                ? link.GetString()
                : null;

            return new Release(Number(text), url ?? "https://github.com/kitecraft/PCB_MillBurn/releases/latest");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What the latest release means for the version this build carries.
    ///
    /// Unreadable on either side is <see cref="UpdateState.Unknown"/> rather than an optimistic
    /// "you are up to date": the whole value of the button is that its answer can be trusted, and
    /// "no idea" is an answer a person can act on.
    /// </summary>
    public static UpdateState Compare(string? build, string? latest)
    {
        if (!Version.TryParse(Number(build), out var mine) || !Version.TryParse(Number(latest), out var theirs))
        {
            return UpdateState.Unknown;
        }

        // Compare on the parts that were given. "0.1.5" and "0.1.5.0" are the same release, and a
        // three-part build should not read as older than a four-part tag that says the same thing.
        var parts = Math.Min(Parts(build), Parts(latest));

        return Normalise(mine, parts).CompareTo(Normalise(theirs, parts)) switch
        {
            0 => UpdateState.Current,
            < 0 => UpdateState.Available,
            _ => UpdateState.Ahead,
        };
    }

    /// <summary>"v0.1.5" or "0.1.5+3a1f2c" becomes "0.1.5". Anything else is left alone.</summary>
    private static string Number(string? tag)
    {
        var text = (tag ?? string.Empty).Trim();

        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var build = text.IndexOf('+', StringComparison.Ordinal);

        return build < 0 ? text : text[..build];
    }

    private static int Parts(string? version) => Number(version).Split('.').Length;

    private static Version Normalise(Version version, int parts) => new(
        version.Major,
        version.Minor,
        parts >= 3 ? Math.Max(version.Build, 0) : 0,
        parts >= 4 ? Math.Max(version.Revision, 0) : 0);
}
