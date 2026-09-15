using Xunit.Sdk;

namespace MillBurn.GoldenTests;

/// <summary>
/// Compares generated text against a committed baseline.
///
/// The property tests elsewhere are good at "does this claim still hold" and blind to "did the
/// whole output quietly change into something else". That blindness cost a real bug: splitting
/// drilling per bit rewrote every drill program in the corpus from the first line to the last, and
/// five hundred tests noticed nothing, because not one of them pinned what a drill program looks
/// like.
///
/// Hand-written rather than taken from a snapshot library, for two reasons. A library that launches
/// a diff tool on failure is a liability in a headless run, and the failure message here can be
/// about G-code — which line diverged and what it said — rather than about files.
/// </summary>
internal static class Snapshot
{
    /// <summary>Set <c>MILLBURN_UPDATE_SNAPSHOTS=1</c> to rewrite every baseline.</summary>
    private static bool Rewriting =>
        Environment.GetEnvironmentVariable("MILLBURN_UPDATE_SNAPSHOTS") is "1" or "true";

    /// <summary>
    /// Asserts that <paramref name="actual"/> matches the baseline named <paramref name="name"/>.
    ///
    /// A missing baseline is written and then <em>fails</em>. Accepting a new baseline silently
    /// would make the first run of a broken generator create its own proof of correctness.
    /// </summary>
    public static void Match(string name, string actual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(actual);

        var path = Path.Combine(Directory, name + ".txt");
        var normalised = Normalise(actual);

        System.IO.Directory.CreateDirectory(Directory);

        if (!File.Exists(path))
        {
            File.WriteAllText(path, normalised);

            throw new XunitException(
                $"No baseline for '{name}'. One has been written to {path} — read it, satisfy "
                + "yourself that it describes the program you meant to generate, commit it, and "
                + "run again.");
        }

        var expected = Normalise(File.ReadAllText(path));

        if (expected == normalised)
        {
            Cleanup(name);
            return;
        }

        if (Rewriting)
        {
            File.WriteAllText(path, normalised);
            return;
        }

        var actualPath = Path.Combine(Directory, name + ".actual.txt");
        File.WriteAllText(actualPath, normalised);

        throw new XunitException(
            $"'{name}' no longer matches its baseline.\n\n{FirstDifference(expected, normalised)}\n\n"
            + $"What was generated is in {actualPath}. If the change is intended, replace the "
            + "baseline with it (or re-run with MILLBURN_UPDATE_SNAPSHOTS=1) and commit the diff, "
            + "so the review sees exactly what moved.");
    }

    /// <summary>
    /// The first line that differs, with the two versions and a little context.
    ///
    /// The whole point of a baseline over a hash is that a failure tells you what changed, so the
    /// message has to do that work rather than saying "they are not equal".
    /// </summary>
    private static string FirstDifference(string expected, string actual)
    {
        var a = expected.Split('\n');
        var b = actual.Split('\n');

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var left = i < a.Length ? a[i] : "(end of file)";
            var right = i < b.Length ? b[i] : "(end of file)";

            if (left == right)
            {
                continue;
            }

            var context = i > 0 ? $"  line {i,4}: {a[i - 1]}\n" : string.Empty;

            return $"{context}  line {i + 1,4} was: {left}\n  line {i + 1,4} now: {right}";
        }

        return $"  the lines all match; the file lengths differ ({a.Length} vs {b.Length})";
    }

    /// <summary>An old failure's output must not linger and look like a current one.</summary>
    private static void Cleanup(string name)
    {
        var stale = Path.Combine(Directory, name + ".actual.txt");

        if (File.Exists(stale))
        {
            File.Delete(stale);
        }
    }

    /// <summary>Line endings only. Everything else is the generator's business and must not move.</summary>
    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";

    /// <summary>
    /// Beside the test source, not beside the built assembly.
    ///
    /// A baseline is a reviewed artefact that lives in the repository; writing it into bin/ would
    /// put a new one a clean rebuild away from existing, which is the opposite of the point.
    ///
    /// Found by walking up from the test binaries to the solution, **not** from
    /// <c>[CallerFilePath]</c>. A CI build sets <c>ContinuousIntegrationBuild</c>, which maps source
    /// paths to <c>/_/</c> for deterministic output — so on the first GitHub run every baseline was
    /// looked for under <c>\_\tests\…</c>, none was found, and all five program snapshots failed with
    /// "No baseline" while passing on every developer machine.
    /// </summary>
    private static string Directory => Path.Combine(SolutionDirectory(), "tests", "MillBurn.GoldenTests", "Snapshots");

    private static string SolutionDirectory()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "PCB_MillBurn.slnx")))
            {
                return at.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "PCB_MillBurn.slnx was not found above " + AppContext.BaseDirectory + ", so the snapshot baselines cannot be located.");
    }
}
