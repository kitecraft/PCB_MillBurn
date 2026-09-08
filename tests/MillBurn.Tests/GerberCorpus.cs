using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Locates the pcb2gcode test-data checkout, if the developer has one beside the repo.
///
/// Those files are **not copied into this repository**: pcb2gcode is GPL-3.0 and redistributing
/// its data would drag the licence along with it (Documentation/01, section 9). Tests that use
/// the corpus skip cleanly when it is absent, so CI and a fresh clone both stay green while a
/// local checkout gets much broader coverage.
///
/// The fixtures we own are hand-written from the Ucamco specification and kept inline in the test
/// files, so a case is readable next to the assertion that explains why it matters.
/// </summary>
public static class GerberCorpus
{
    private static readonly Lazy<string?> RootValue = new(Locate);

    /// <summary>Path to the corpus directory, or null when it is not available.</summary>
    public static string? Root => RootValue.Value;

    public static bool Available => Root is not null;

    public const string SkipReason =
        "pcb2gcode test corpus not found beside the repository; set MILLBURN_GERBER_CORPUS to override.";

    public static string File(string name) =>
        Path.Combine(Root ?? throw new InvalidOperationException(SkipReason), name);

    public static IEnumerable<string> AllGerbers() =>
        Root is null
            ? []
            : Directory.EnumerateFiles(Root, "*.gbr", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal);

    private static string? Locate()
    {
        var overridePath = Environment.GetEnvironmentVariable("MILLBURN_GERBER_CORPUS");
        if (!string.IsNullOrWhiteSpace(overridePath) && Directory.Exists(overridePath))
        {
            return overridePath;
        }

        // Both layouts: the checkout beside the repository, and the one tidied into WorkingFolder
        // with everything else that is not project content. Looking in only one place means the
        // corpus quietly stops running the day it is moved, and a skipped test looks like a passing
        // one.
        string[] layouts =
        [
            Path.Combine("WorkingFolder", "pcb2gcode", "tests", "data", "gerberimporter"),
            Path.Combine("pcb2gcode", "tests", "data", "gerberimporter"),
        ];

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            foreach (var layout in layouts)
            {
                var candidate = Path.Combine(dir, layout);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }
}

/// <summary>Marks a fact that needs the optional external corpus.</summary>
public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute()
    {
        if (!GerberCorpus.Available)
        {
            Skip = GerberCorpus.SkipReason;
        }
    }
}
