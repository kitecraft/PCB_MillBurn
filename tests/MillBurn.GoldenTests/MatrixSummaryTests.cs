using System.Text;
using System.Text.RegularExpressions;
using Xunit.Sdk;

namespace MillBurn.GoldenTests;

/// <summary>
/// The requirements matrix opens with a table counting its own rows. This recomputes it.
///
/// **It has drifted twice.** The document's own audit notes record the first: *"twelve rows added
/// or changed after v0.1.0 while the summary still described a document of 92"*. The second was
/// found by the product owner reading it — the summary said seven not-started rows against document
/// 01 where the table below listed three, because a sprint's worth of requirements had moved and
/// the counts had not. Two of the five section rows were also wrong in ways that predated that.
///
/// A summary that counts a table by hand is a claim nobody checks, and a wrong count is worse than
/// no count: it is the number a reader takes away without scrolling. So it is derived here, and a
/// matrix edit that forgets it fails the build. Same contract as the backlog and the program
/// snapshots, same escape hatch — <c>MILLBURN_UPDATE_SNAPSHOTS=1</c> rewrites it.
///
/// **The columns are not fixed.** The original table had five, and work since has produced statuses
/// it had no column for, which is how a Parked row vanished from the arithmetic entirely. The
/// columns are whatever the tables actually use, so a new status appears by itself rather than
/// being quietly dropped into a column that does not exist.
/// </summary>
public sealed partial class MatrixSummaryTests
{
    [Fact]
    public void TheSummaryCountsTheTableBeneathIt()
    {
        var path = Path.Combine(Root(), "Documentation", "08-Requirements-Matrix.md");
        var text = File.ReadAllText(path).Replace("\r\n", "\n");

        var sections = Count(text);

        Assert.True(
            sections.Count >= 7,
            $"only {sections.Count} sections of the matrix were recognised, so the heading or row "
            + "format has changed and this is now counting a document that no longer exists.");

        var rebuilt = Replace(text, Render(sections));

        if (Rewriting)
        {
            File.WriteAllText(path, rebuilt.Replace("\n", Environment.NewLine));
            return;
        }

        if (text == rebuilt)
        {
            return;
        }

        var a = rebuilt.Split('\n');
        var b = text.Split('\n');
        var at = 0;
        while (at < a.Length && at < b.Length && a[at] == b[at])
        {
            at++;
        }

        throw new XunitException(
            "The matrix's summary no longer counts the rows beneath it.\n"
            + $"  line {at + 1} is:        {(at < b.Length ? b[at] : "<end of file>")}\n"
            + $"  line {at + 1} should be: {(at < a.Length ? a[at] : "<end of file>")}\n"
            + "Re-run with MILLBURN_UPDATE_SNAPSHOTS=1 and read the diff — a count that moved "
            + "without a row moving means something else is wrong.");
    }

    /// <summary>Rows per status, per section, in the order the document lays them out.</summary>
    private static List<(string Section, Dictionary<string, int> Rows)> Count(string text)
    {
        var found = new List<(string, Dictionary<string, int>)>();
        Dictionary<string, int>? current = null;

        foreach (var line in text.Split('\n'))
        {
            var heading = SectionHeading().Match(line);

            if (heading.Success)
            {
                current = [];
                found.Add((heading.Groups["name"].Value.Replace(" — ", " ").Trim(), current));
                continue;
            }

            if (!Row().IsMatch(line) || current is null)
            {
                continue;
            }

            // The status is read as "the row's one bolded cell", not by position and not by a
            // greedy pattern. A greedy match takes the *last* bold on the line, so the day a note
            // says **never** the count moves to a column that is not a status — silently, because
            // the generator and the expected value are the same parse. Two bolded cells means the
            // row cannot be read, and saying so is the only honest answer.
            var bolded = line.Split('|')
                .Select(c => c.Trim())
                .Where(c => c.Length > 4 && c.StartsWith("**", StringComparison.Ordinal)
                    && c.EndsWith("**", StringComparison.Ordinal))
                .ToList();

            Assert.True(
                bolded.Count == 1,
                $"this row has {bolded.Count} bolded cells, so which one is its status is a guess:\n  {line}");

            var status = bolded[0][2..^2].Trim();
            current[status] = current.GetValueOrDefault(status) + 1;
        }

        return found.Where(s => s.Item2.Count > 0).ToList();
    }

    private static string Render(List<(string Section, Dictionary<string, int> Rows)> sections)
    {
        // Requirement sections are the ones the totals row adds up; the last two count different
        // things — criteria that are met and capabilities that are proven — and are said in prose
        // underneath rather than folded into a total that would mean nothing.
        var requirements = sections.Take(sections.Count - 2).ToList();
        var rest = sections.Skip(sections.Count - 2).ToList();

        // Named, not counted from the end. Taking "the last two" is positional, so adding a section
        // at the bottom of the document would quietly fold the acceptance criteria into a total of
        // requirements and treat the newcomer as prose — arithmetic that still adds up and means
        // something else.
        Assert.Equal("06 §2 Cross-cutting acceptance criteria", rest[0].Section);
        Assert.Equal("07 Physically verified on a machine", rest[1].Section);

        var columns = requirements
            .SelectMany(s => s.Rows.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var s = new StringBuilder();

        s.Append("| Section | Rows | ").Append(string.Join(" | ", columns)).Append(" |\n");
        s.Append("|---|---|").Append(string.Concat(columns.Select(_ => "---|"))).Append('\n');

        foreach (var (name, rows) in requirements)
        {
            s.Append($"| {name} | {rows.Values.Sum()} | ");
            s.Append(string.Join(" | ", columns.Select(c => rows.GetValueOrDefault(c)))).Append(" |\n");
        }

        var totals = columns.ToDictionary(
            c => c,
            c => requirements.Sum(r => r.Rows.GetValueOrDefault(c)),
            StringComparer.Ordinal);

        s.Append($"| **Requirements** | **{requirements.Sum(r => r.Rows.Values.Sum())}** | ");
        s.Append(string.Join(" | ", columns.Select(c => $"**{totals[c]}**"))).Append(" |\n");
        s.Append('\n');

        // The last two sections count different things, so they are stated rather than totalled —
        // and stated one per line with their own names, because folding "9 06 §2 cross-cutting
        // acceptance criteria" into a sentence reads like a typo.
        foreach (var (name, rows) in rest)
        {
            s.Append($"**{name}** — {rows.Values.Sum()} rows: ");
            s.Append(string.Join(", ", rows.OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(k => $"{k.Value} {k.Key.ToLowerInvariant()}")));
            s.Append(".\n\n");
        }

        s.Length -= 1;

        return s.ToString();
    }

    /// <summary>
    /// Swaps the generated block in, between the summary table's header and the paragraph that
    /// follows the last per-section line. Anchored on both ends so a failure to find either is loud.
    /// </summary>
    private static string Replace(string text, string block)
    {
        var lines = text.Split('\n');
        var from = Array.FindIndex(lines, l => l.StartsWith("| Section | Rows |", StringComparison.Ordinal));

        Assert.True(from >= 0, "the matrix's summary table header was not found.");

        // Walk forward while the lines still look like the block this generates — table rows, the
        // bold per-section lines, and the blanks between them — and stop at the first line that
        // does not.
        //
        // **Bounded deliberately.** The obvious way to find the end is to search for the last
        // per-section line, and that searches the whole document: the day somebody opens a
        // paragraph with "**05 Viewer & export is the fastest-growing…", everything between the
        // table and that sentence is replaced by this block and deleted. The test would then pass,
        // because the file and the generator would agree about a document with its middle removed.
        // A rewriter of source documents must not be able to reach further than it can see.
        var to = from;
        while (to < lines.Length)
        {
            var line = lines[to];

            // `**` followed by a digit, not `**0` — a tenth section would render "**10 …" and be
            // read as the end of the block, leaving the line below a freshly written copy of
            // itself.
            var belongs = line.Length == 0
                || line.StartsWith('|')
                || (line.StartsWith("**", StringComparison.Ordinal)
                    && line.Length > 2 && char.IsAsciiDigit(line[2]));

            if (!belongs)
            {
                break;
            }

            to++;
        }

        // Trailing blanks belong to whatever comes next, not to this block.
        while (to > from && lines[to - 1].Length == 0)
        {
            to--;
        }

        Assert.True(
            to > from,
            "the summary table header was found but nothing followed it, so there is no block to "
            + "replace.");

        return string.Join('\n', lines[..from]) + "\n" + block + string.Join('\n', lines[to..]);
    }

    private static bool Rewriting =>
        Environment.GetEnvironmentVariable("MILLBURN_UPDATE_SNAPSHOTS") is "1" or "true";

    private static string Root()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "PCB_MillBurn.slnx")))
            {
                return at.FullName;
            }
        }

        throw new DirectoryNotFoundException("PCB_MillBurn.slnx was not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"^## (?<name>\d.*)$", RegexOptions.Multiline)]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"^\| [A-Z]+\d+ \|.*\| \*\*(?<status>[^*]+)\*\* \|")]
    private static partial Regex Row();
}
