namespace MillBurn.Tests;

/// <summary>
/// Locates the committed test boards under <c>tests/boards/</c>.
///
/// These are real KiCad 10 exports and they are **project content, not scratch**: they are
/// committed, so every test that uses them runs everywhere, on every clone, in CI. Hand-written
/// fixtures prove the parser handles the specification; these prove it handles what an EDA tool
/// actually writes, and that is where the bugs have actually been. See
/// <c>tests/boards/README.md</c>.
///
/// <c>MILLBURN_BOARDS</c> repoints the whole set at a directory elsewhere, which is how a private
/// board — a client's, say, that must not be committed — gets run through the same suite without
/// ever entering the repository.
/// </summary>
public static class RealBoards
{
    public const string GridStripConnector = "GridStripConnector";
    public const string PogoTest1 = "PogoTest1";

    /// <summary>
    /// A panelised board, present only when the corpus has been repointed at one that has it.
    ///
    /// Panels are the case the optimizer is most worth measuring on — dozens of separate cutouts is
    /// exactly the "edge cuts all over the place" complaint — but a panel is also a large file and
    /// somebody's actual design, so the suite asks for one rather than carrying one.
    /// </summary>
    public const string Panel = "Panel";

    private static readonly Lazy<string> RootValue = new(Locate);

    /// <summary>Directory holding the board folders.</summary>
    public static string Root => RootValue.Value;

    public static string Directory(string board) => Path.Combine(Root, board);

    /// <summary>Whether a board is in the corpus at all. Optional boards are skipped, not failed.</summary>
    public static bool Has(string board) =>
        System.IO.Directory.Exists(Directory(board))
        && System.IO.Directory.EnumerateFiles(Directory(board), "*.gbr").Any();

    public static string File(string board, string name) => Path.Combine(Directory(board), name);

    public static IEnumerable<string> Gerbers(string board) =>
        System.IO.Directory.EnumerateFiles(Directory(board), "*.gbr").Order(StringComparer.Ordinal);

    public static IEnumerable<string> DrillFiles(string board) =>
        System.IO.Directory.EnumerateFiles(Directory(board), "*.drl").Order(StringComparer.Ordinal);

    private static string Locate()
    {
        var overridePath = Environment.GetEnvironmentVariable("MILLBURN_BOARDS");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return System.IO.Directory.Exists(overridePath)
                ? overridePath
                : throw new DirectoryNotFoundException(
                    $"MILLBURN_BOARDS points at '{overridePath}', which does not exist.");
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "tests", "boards");
            if (System.IO.Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        // Committed content, so absence is a broken checkout rather than an optional extra —
        // fail loudly instead of skipping and reporting green.
        throw new DirectoryNotFoundException(
            "Could not find tests/boards above the test assembly. It is committed content; " +
            "check the working tree, or set MILLBURN_BOARDS.");
    }
}
