using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Locates the real KiCad board exports, if the developer has them beside the repository.
///
/// These are **not committed**: they are the repository owner's own designs, kept in the workspace
/// as working material rather than as project content — the same arrangement as the GPL-3.0
/// reference checkouts (Documentation/01, section 9). Tests that need them skip cleanly when they
/// are absent, so a fresh clone and CI both stay green while a local workspace gets the much
/// stronger coverage that real EDA output provides.
///
/// Hand-written fixtures prove the code handles the specification, and they run everywhere. These
/// prove it handles what an actual tool actually writes, which is a different question and the one
/// that decides whether a board is cut correctly. Losing them in CI is a real reduction in
/// coverage, so it is stated rather than hidden: the skip message names what is missing.
/// </summary>
public static class RealBoards
{
    /// <summary>Boards the suite knows about. All must be present for board tests to run.</summary>
    private static readonly string[] Required = ["MyGerbers", "MyGerbers2"];

    private static readonly Lazy<string?> RootValue = new(Locate);

    /// <summary>Directory holding the board folders, or null when they are not available.</summary>
    public static string? Root => RootValue.Value;

    public static bool Available => Root is not null;

    public const string SkipReason =
        "Real board exports (MyGerbers, MyGerbers2) are not in the repository and were not found " +
        "beside it; set MILLBURN_BOARDS to the directory that contains them.";

    public static string Directory(string board) => Path.Combine(
        Root ?? throw new InvalidOperationException(SkipReason),
        board);

    public static string File(string board, string name) => Path.Combine(Directory(board), name);

    private static string? Locate()
    {
        var overridePath = Environment.GetEnvironmentVariable("MILLBURN_BOARDS");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            // An explicit override is used exclusively, never as a first guess. That is what makes
            // the absent case testable: point it at an empty directory and the skip path runs.
            return HasAllBoards(overridePath) ? overridePath : null;
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (HasAllBoards(dir))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    private static bool HasAllBoards(string root) =>
        Required.All(b => System.IO.Directory.Exists(Path.Combine(root, b)));
}

/// <summary>Marks a fact that needs the real board exports.</summary>
public sealed class BoardFactAttribute : FactAttribute
{
    public BoardFactAttribute()
    {
        if (!RealBoards.Available)
        {
            Skip = RealBoards.SkipReason;
        }
    }
}

/// <summary>Marks a theory that needs the real board exports.</summary>
public sealed class BoardTheoryAttribute : TheoryAttribute
{
    public BoardTheoryAttribute()
    {
        if (!RealBoards.Available)
        {
            Skip = RealBoards.SkipReason;
        }
    }
}
