using System.Text;
using System.Text.RegularExpressions;
using Xunit.Sdk;

namespace MillBurn.GoldenTests;

/// <summary>
/// `Documentation/11-Backlog.md` is generated from the roadmap, and this is what generates it.
///
/// **The roadmap is sixty thousand words.** It earns most of them — an entry carries the
/// measurement that justified it and the reasoning that shaped it, which is why this repository can
/// answer "why is it like this" a year later. What it cannot do is answer "what is open", because
/// there was no way in: forty-six entries of prose with the state buried in each heading.
///
/// **So the backlog is derived, never written.** A hand-maintained list of the same items would
/// drift from them inside a sprint — which is the argument this project used for *not* keeping a
/// separate bug tracker, and it applies with equal force to a summary of its own backlog. The
/// generator lives in a test so that a roadmap edit which forgets the backlog fails the build
/// rather than quietly publishing a stale one. Same contract as the program snapshots, same escape
/// hatch: <c>MILLBURN_UPDATE_SNAPSHOTS=1</c> rewrites it, and what changes is a diff somebody reads.
/// </summary>
public sealed partial class BacklogTests
{
    [Fact]
    public void TheBacklogMatchesTheRoadmap()
    {
        var roadmap = File.ReadAllText(Path.Combine(Docs, "06-Roadmap-and-Risks.md"));
        var entries = Entries(roadmap);

        // A guard against the regex quietly matching nothing after a formatting change: the file has
        // had forty-six entries since the statuses were normalised and only grows.
        Assert.True(
            entries.Count >= 46,
            $"only {entries.Count} roadmap entries were recognised, which means the heading format "
            + "changed and this generator is now describing a document that no longer exists.");

        Vouch(entries);

        var built = Render(entries);
        var path = Path.Combine(Docs, "11-Backlog.md");

        if (Rewriting || !File.Exists(path))
        {
            File.WriteAllText(path, built);

            if (!Rewriting)
            {
                throw new XunitException(
                    $"No backlog existed, so one was written to {path}. Read it before committing.");
            }

            return;
        }

        var current = Normalise(File.ReadAllText(path));

        if (current == Normalise(built))
        {
            return;
        }

        var a = Normalise(built).Split('\n');
        var b = current.Split('\n');
        var at = 0;
        while (at < a.Length && at < b.Length && a[at] == b[at])
        {
            at++;
        }

        throw new XunitException(
            "Documentation/11-Backlog.md no longer matches the roadmap it is generated from.\n"
            + $"  line {at + 1} is:       {(at < b.Length ? b[at] : "<end of file>")}\n"
            + $"  line {at + 1} should be: {(at < a.Length ? a[at] : "<end of file>")}\n"
            + "Re-run with MILLBURN_UPDATE_SNAPSHOTS=1 and commit the diff.");
    }

    private sealed record Entry(string Number, string Title, string Kind, string State, string Note, string Heading);

    /// <summary>The whole vocabulary. A state outside it belongs to no section of the backlog.</summary>
    private static readonly string[] Kinds = ["defect", "enhancement"];

    private static readonly string[] States =
        ["open", "partial", "parked", "accepted", "fixed", "built", "superseded"];

    /// <summary>
    /// Every entry must be classifiable, or the backlog quietly loses one.
    ///
    /// **This is the check the generator cannot make on its own**, and it took a cold reading to
    /// see it: the sections filter by literal kind and state, so an entry tagged with anything else
    /// — a typo, a new word, a status with no `·` in it — falls through all four tables while still
    /// being counted in the "N open" total at the foot. The document would be short by one open
    /// item and every test would pass, because the expected value is the generator's own output and
    /// the generator would have dropped it on both sides.
    ///
    /// A backlog that silently omits work is worse than no backlog: it is trusted. So an entry that
    /// cannot be placed fails the build and names itself.
    /// </summary>
    private static void Vouch(List<Entry> entries)
    {
        var duplicates = entries.GroupBy(e => e.Number, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            $"the roadmap has more than one entry numbered {string.Join(", ", duplicates)}, so a "
            + "backlog row would point at whichever heading GitHub's anchor happened to reach.");

        foreach (var e in entries)
        {
            Assert.True(
                !string.Equals(e.Kind, e.State, StringComparison.Ordinal),
                $"{e.Number}'s status has no '·' separating kind from state — it reads "
                + $"'{e.Kind}'. Every status is 'kind · state'; see the legend in 06.");

            Assert.Contains(e.Kind, Kinds, StringComparer.Ordinal);

            Assert.True(
                States.Contains(e.State, StringComparer.Ordinal)
                    || e.State.StartsWith("folded into ", StringComparison.Ordinal),
                $"{e.Number} is in state '{e.State}', which no section of the backlog collects, so "
                + $"it would appear in none of them. Either use one of [{string.Join(", ", States)}] "
                + "or 'folded into 6.NN', or teach this generator the new word deliberately.");
        }
    }

    private static List<Entry> Entries(string roadmap)
    {
        var found = new List<Entry>();

        foreach (Match m in Heading().Matches(roadmap))
        {
            var status = m.Groups["status"].Value;
            var parts = status.Split('·', 2);

            found.Add(new Entry(
                m.Groups["num"].Value,
                m.Groups["title"].Value.Trim(),
                parts[0].Trim(),
                parts.Length > 1 ? parts[1].Trim() : status.Trim(),
                m.Groups["note"].Success ? m.Groups["note"].Value.Trim() : string.Empty,
                m.Value.Trim()[5..]));
        }

        return found;
    }

    private static string Render(List<Entry> all)
    {
        var s = new StringBuilder();

        s.Append("# 11. Backlog\n\n");
        s.Append("**Generated from [06](06-Roadmap-and-Risks.md). Do not edit by hand** — `BacklogTests`\n");
        s.Append("rewrites it and fails the build when it drifts. Every row links to the entry that carries the\n");
        s.Append("measurement and the reasoning; this page carries only what is open.\n\n");

        Section(s, all, "Defects", "defect", "open",
            "Something is wrong. Nothing here is fixed, parked or accepted — those stay in 06.");

        Section(s, all, "Enhancements", "enhancement", "open",
            "Something new or better, which is not the same as something broken.");

        Section(s, all, "Part-built", null, "partial",
            "Begun, and the entry says which half. Open work, but not from nothing.");

        s.Append("## Not being worked\n\n");
        s.Append("Here so that nothing looks forgotten. Each says why in 06.\n\n");
        s.Append("| # | what | state |\n|---|---|---|\n");

        foreach (var e in all.Where(e =>
            e.State is "parked" or "accepted" || e.State.StartsWith("folded", StringComparison.Ordinal)))
        {
            s.Append($"| [{e.Number}]({Anchor(e)}) | {e.Title} | `{e.Kind} · {e.State}` |\n");
        }

        var open = all.Count(e => e.State is "open" or "partial");
        s.Append($"\n---\n\n**{open} open**, of {all.Count} entries in the roadmap.\n");

        return s.ToString();
    }

    private static void Section(StringBuilder s, List<Entry> all, string title, string? kind, string state, string blurb)
    {
        var rows = all.Where(e => e.State == state && (kind is null || e.Kind == kind)).ToList();

        s.Append($"## {title} ({rows.Count})\n\n{blurb}\n\n");
        s.Append("| # | what | note |\n|---|---|---|\n");

        foreach (var e in rows)
        {
            var note = e.Note.Length > 0 ? e.Note : "—";
            var what = kind is null ? $"{e.Title} *({e.Kind})*" : e.Title;
            s.Append($"| [{e.Number}]({Anchor(e)}) | {what} | {note} |\n");
        }

        s.Append('\n');
    }

    /// <summary>
    /// GitHub's anchor for a heading: lowercased, letters and digits kept, spaces and hyphens to
    /// hyphens, everything else dropped.
    ///
    /// **Slugged from the heading as it actually reads**, not rebuilt from the parts. The first
    /// version reassembled it as number, title, kind and state — and silently omitted the italic
    /// note that twenty of the forty-six headings carry, so twenty links pointed at anchors that do
    /// not exist. A link that does not resolve is worse than no link: the page looks navigable and
    /// drops you at the top of a sixty-thousand-word document. Taking the real text also means a
    /// heading can gain punctuation or be reworded without this having to learn about it.
    /// </summary>
    private static string Anchor(Entry e)
    {
        var slug = new StringBuilder("06-Roadmap-and-Risks.md#");

        foreach (var c in e.Heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (c is ' ' or '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString();
    }

    private static string Normalise(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).TrimEnd() + "\n";

    private static bool Rewriting =>
        Environment.GetEnvironmentVariable("MILLBURN_UPDATE_SNAPSHOTS") is "1" or "true";

    private static string Docs => Path.Combine(Root(), "Documentation");

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

    [GeneratedRegex(@"^#### (?<num>6\.\d+) (?<title>.*?) — \*\*(?<status>[^*]+)\*\*(?: — \*(?<note>.*?)\*)?\s*$", RegexOptions.Multiline)]
    private static partial Regex Heading();
}
