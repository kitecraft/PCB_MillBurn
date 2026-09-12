using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using MillBurn.Tests;

namespace MillBurn.GoldenTests;

/// <summary>
/// What every program in the corpus looks like, pinned.
///
/// Not the whole file: the panel's isolation is fifteen thousand lines, and a baseline nobody can
/// read during review is a baseline nobody reviews. What is pinned is a **fingerprint** — the
/// comments, the tool changes, the control words, the distinct depths, the move counts, the
/// distances, and a hash of the full text. That is a page per program, it diffs legibly, and it is
/// sensitive to the thing that actually went wrong: a change that rewrote every drill file in the
/// corpus would have shown up here as a completely different set of comments and tool changes.
///
/// The hash is the backstop. If it moves and nothing else does, the change was at coordinate level
/// — which is worth knowing about deliberately rather than discovering on a board.
/// </summary>
public sealed class ProgramSnapshotTests
{
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.GridStripConnector)]
    [InlineData(RealBoards.Panel)]
    public void TheProgramsForThisBoardAreUnchanged(string board) => Pin(board, 0, board + "-programs");

    /// <summary>
    /// The same board with a moat wide enough to need several laps, which is what anybody actually
    /// cuts.
    ///
    /// Every other case here leaves the isolation width at zero — one lap — and one lap has no lap
    /// after it. That left concentric passes, their ordering, and everything that decides whether
    /// the tool lifts between them outside the corpus entirely: <c>PassLinker</c> could have been
    /// deleted and all three snapshots above would still have matched.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.PogoTest1)]
    [InlineData(RealBoards.GridStripConnector)]
    public void TheProgramsForThisBoardWithAWideMoatAreUnchanged(string board) =>
        Pin(board, Nm.FromMillimetres(0.4), board + "-wide-moat");

    private static void Pin(string board, long isolationWidthNm, string snapshot)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(board));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
                IsolationWidthNm = isolationWidthNm,
            },
            StringComparer.Ordinal);

        var plan = ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        Assert.NotEmpty(plan.Items);

        var report = new StringBuilder();
        report.Append(board).Append('\n');
        report.Append(new string('=', board.Length)).Append("\n\n");

        foreach (var item in plan.Items.OrderBy(i => i.TargetName, StringComparer.Ordinal))
        {
            Fingerprint(report, item);
        }

        Snapshot.Match(snapshot, report.ToString());
    }

    private static void Fingerprint(StringBuilder into, ExportItem item)
    {
        var program = GcodeParser.Parse(item.Content);
        var lines = item.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        into.Append(item.TargetName).Append('\n');
        Line(into, "operation", LayerOperations.Label(item.Operation));
        Line(into, "layer", item.LayerLabel);
        Line(into, "lines", lines.Length.ToString(CultureInfo.InvariantCulture));
        Line(into, "sha256", Hash(item.Content));

        // The summary the operator is shown before writing anything. It has been wrong before —
        // it once said "2 sizes" about a program that used one bit — so it is pinned beside the
        // program it describes rather than trusted.
        foreach (var summary in item.Summary)
        {
            Line(into, "summary", summary);
        }

        foreach (var warning in item.Warnings)
        {
            Line(into, "warning", warning);
        }

        // Every comment, in order. This is where the operation, the tool changes, the depths and
        // the tab counts are written down, so it is the most information per line in the file.
        foreach (var comment in lines.Where(l => l.TrimStart().StartsWith('(')))
        {
            Line(into, "comment", comment.Trim());
        }

        // The control words in order, which is the shape of the run: spindle on, cut, stop for a
        // bit change, spindle on, cut, end.
        var control = lines
            .Select(l => l.Split('(')[0].Trim())
            .Where(l => l.StartsWith('M') || l.StartsWith('m'))
            .ToList();

        Line(into, "control", control.Count == 0 ? "(none)" : string.Join(" · ", control));

        var depths = program.Moves
            .Where(m => m.ToZNm < 0)
            .Select(m => m.ToZNm)
            .Distinct()
            .Order()
            .Select(z => Nm.ToMillimetreString(z, 3))
            .ToList();

        Line(into, "depths", depths.Count == 0 ? "(never below zero)" : string.Join(", ", depths));

        var measured = GcodeBackplot.Measure(GcodeBackplot.Classify(program));

        var rapid = program.Moves.Count(m => m.IsRapid);
        var feed = program.Moves.Count(m => !m.IsRapid && !m.IsArc);
        var arc = program.Moves.Count(m => m.IsArc);

        Line(into, "moves", Invariant($"{rapid} rapid, {feed} feed, {arc} arc"));

        Line(into, "distance", Invariant($"{measured.CutMm:F2} mm cutting, {measured.TravelMm:F2} mm travel"));
        Line(into, "plunges", measured.PlungeCount.ToString(CultureInfo.InvariantCulture));

        var bounds = program.Bounds;

        if (bounds.IsEmpty)
        {
            Line(into, "extent", "(empty)");
        }
        else
        {
            var across = Nm.ToMillimetreString(bounds.MinX, 3) + " .. " + Nm.ToMillimetreString(bounds.MaxX, 3);
            var up = Nm.ToMillimetreString(bounds.MinY, 3) + " .. " + Nm.ToMillimetreString(bounds.MaxY, 3);

            Line(into, "extent", across + " x " + up + " mm");
        }

        // Anything the parser could not make sense of in our own output is a bug in our output.
        foreach (var diagnostic in program.Diagnostics)
        {
            Line(into, "PARSE", diagnostic.ToString());
        }

        into.Append('\n');
    }

    private static void Line(StringBuilder into, string label, string value) =>
        into.Append("  ").Append(label.PadRight(10)).Append(value).Append('\n');

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            text.Replace("\r\n", "\n", StringComparison.Ordinal))))[..16].ToLowerInvariant();

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
