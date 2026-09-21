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
    /// The project's own feature-coverage board, designed by its author to exercise what users run
    /// into: plated and non-plated holes from 0.3 to 3.5 mm, plated slots, and edge cuts.
    ///
    /// The board that found the routing page reading one hole per cutter: six holes milled by one
    /// end mill, and a page that said "1 hole". Its KiCad sources are under <c>design/</c>.
    /// </summary>
    public const string MillburnTestBoard = "Millburn_Test_Board";

    /// <summary>
    /// Sixty-six boards and a frame — six by eleven — stepped out by KiKit as one 165 x 107 mm panel.
    ///
    /// Not fifty, which this said for a long time. Fifty-one is the number of ring regions its
    /// shared Edge_Cuts lattice realises to, and tests/boards/README.md warns in as many words that
    /// a count of profiles is not a count of boards. Whoever wrote the number here had counted the
    /// wrong thing, and it has since been read back as fact.
    ///
    /// The case the optimizer is most worth measuring on, and the one that found the outline bug:
    /// taking only the largest ring cut the frame and left every board attached to it. Nothing
    /// smaller reproduces that, because on a single board the largest ring is the right answer.
    /// </summary>
    public const string Panel = "GridStripConnector_Panelized";

    /// <summary>
    /// The same design as <see cref="PogoTest1"/>, exported with every layer KiCad offers.
    ///
    /// Kept alongside rather than replacing it, because the two cover different things: this one is
    /// the only board in the corpus with **paste** layers or a **user** layer, and the shorter
    /// export is the one that proves a board with neither still works.
    /// </summary>
    public const string PogoTest1AllLayers = "PogoTest1-AllLayers";

    /// <summary>
    /// An Arduino Uno R3, recreated in KiCad 8 by somebody else and published under the WTFPL.
    ///
    /// The board that found the slot bug: seven plated slots written as strokes rather than
    /// flashes, discarded by the drill reader while the viewer drew them perfectly. It is also the
    /// only board here whose drill files are Gerbers rather than Excellon, the only one with more
    /// than two drill sizes in a program, and the only one with spaces in its filenames.
    /// </summary>
    public const string ArduinoUno = "Arduino_Uno";

    /// <summary>
    /// The same, at twice the size: 10,007 objects and 63 % copper coverage, six drill sizes, seven
    /// outline profiles. The densest board in the corpus, kept as the stress case.
    /// </summary>
    public const string ArduinoMega = "Arduino_Mega_2560";

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
