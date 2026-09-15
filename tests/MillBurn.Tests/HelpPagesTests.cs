using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The shipped user help, checked for the one kind of rot nothing else catches.
///
/// Documentation/06 Phase 7 said "a test walks the HTML and fails on a broken internal link or a
/// missing image" for some time before one existed. The pages were fine — but a link that goes
/// nowhere is invisible until a user in a workshop clicks it, which is the worst possible moment,
/// and these pages are edited by hand every time a feature lands.
///
/// Deliberately not a parser. The rules being enforced are about references between files, and a
/// regex that finds every href and every id is enough for that and cannot itself be wrong in an
/// interesting way. Anything that needs real HTML semantics does not belong here.
/// </summary>
public sealed class HelpPagesTests(ITestOutputHelper output)
{
    /// <summary>
    /// `Help/` sits beside the solution, and the tests run from `bin/Release/net10.0`. Walk up
    /// until the folder appears rather than counting directory levels, which breaks the first time
    /// a target framework or configuration changes.
    /// </summary>
    private static string Directory
    {
        get
        {
            var at = new DirectoryInfo(AppContext.BaseDirectory);

            while (at is not null)
            {
                var help = Path.Combine(at.FullName, "Help");
                if (System.IO.Directory.Exists(help))
                {
                    return help;
                }

                at = at.Parent;
            }

            throw new DirectoryNotFoundException("Help/ was not found above " + AppContext.BaseDirectory);
        }
    }

    /// <summary>
    /// Plain patterns rather than raw string literals: the thing being matched is full of quote
    /// characters, which is exactly the case raw strings make harder to read rather than easier.
    /// </summary>
    private const string IdPattern = "id=\"([^\"]+)\"";

    private const string HrefPattern = "href=\"([^\"]+)\"";

    private const string SrcPattern = "src=\"([^\"]+)\"";

    /// <summary>
    /// Every page, keyed by its path under `Help/` with forward slashes: <c>faq.html</c>,
    /// <c>guides/drill-alignment.html</c>. Guides live a folder down, and link back up with <c>../</c>.
    /// </summary>
    private static Dictionary<string, string> Pages() =>
        System.IO.Directory.GetFiles(Directory, "*.html", SearchOption.AllDirectories)
            .ToDictionary(Relative, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    private static string Relative(string path) =>
        Path.GetRelativePath(Directory, path).Replace('\\', '/');

    /// <summary>
    /// Where a reference from a page lands, as a path under `Help/` — resolved from the page's own
    /// folder, the way a browser does. Null when it climbs out of `Help/`, which cannot ship.
    /// </summary>
    private static string? Resolve(string page, string reference)
    {
        var folder = Path.GetDirectoryName(Path.Combine(Directory, page))!;
        var full = Path.GetFullPath(Path.Combine(folder, reference));
        var relative = Relative(full);

        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : relative;
    }

    private static List<string> Anchors(string html) =>
        [.. Regex.Matches(html, IdPattern).Select(m => m.Groups[1].Value)];

    [Fact]
    public void ThereAreHelpPagesToCheck()
    {
        var pages = Pages();

        output.WriteLine(string.Join(", ", pages.Keys.Order(StringComparer.Ordinal)));

        Assert.Contains("index.html", pages.Keys);
        Assert.Contains("faq.html", pages.Keys);
    }

    /// <summary>
    /// Every internal link lands on a file that exists and, where it names one, an anchor that
    /// exists.
    ///
    /// The FAQ is one long page reached almost entirely by fragment from the contents list and from
    /// "More ›" links elsewhere, so a renamed section silently sends every one of them to the top
    /// of the page — which looks like the help failing to answer the question.
    /// </summary>
    [Fact]
    public void EveryInternalLinkResolves()
    {
        var pages = Pages();
        var anchors = pages.ToDictionary(p => p.Key, p => Anchors(p.Value), StringComparer.OrdinalIgnoreCase);
        var broken = new List<string>();
        var checkedLinks = 0;

        foreach (var (name, html) in pages)
        {
            foreach (Match match in Regex.Matches(html, HrefPattern))
            {
                var href = match.Groups[1].Value;
                if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                checkedLinks++;

                var hash = href.IndexOf('#', StringComparison.Ordinal);
                var file = hash < 0 ? href : href[..hash];
                var fragment = hash < 0 ? string.Empty : href[(hash + 1)..];
                var target = file.Length == 0 ? name : Resolve(name, file);

                if (target is null || (!pages.ContainsKey(target) && !File.Exists(Path.Combine(Directory, target))))
                {
                    broken.Add($"{name} → {href} (no such file)");
                    continue;
                }

                if (fragment.Length > 0 && anchors.TryGetValue(target, out var ids) && !ids.Contains(fragment))
                {
                    broken.Add($"{name} → {href} (no such anchor)");
                }
            }
        }

        output.WriteLine($"{checkedLinks} internal links across {pages.Count} pages");
        Assert.True(broken.Count == 0, string.Join("\n", broken));
    }

    /// <summary>Every image, stylesheet and script the pages pull in is beside them.</summary>
    [Fact]
    public void EveryReferencedFileIsShipped()
    {
        var missing = new List<string>();

        foreach (var (name, html) in Pages())
        {
            foreach (Match match in Regex.Matches(html, SrcPattern))
            {
                var src = match.Groups[1].Value;
                if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Resolve(name, src) is not { } shipped || !File.Exists(Path.Combine(Directory, shipped)))
                {
                    missing.Add($"{name} → {src}");
                }
            }
        }

        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    /// <summary>
    /// A guide nobody can find is not help. Every page under `guides/` is listed in the help contents
    /// and has an entry in the app's Help menu.
    /// </summary>
    [Fact]
    public void EveryGuideIsListedInTheContentsAndTheMenu()
    {
        var pages = Pages();
        var guides = pages.Keys.Where(k => k.StartsWith("guides/", StringComparison.OrdinalIgnoreCase)).ToList();

        var menu = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(Directory)!, "src", "MillBurn.App", "Views", "MainWindow.axaml.cs"));

        output.WriteLine(string.Join(", ", guides));

        Assert.NotEmpty(guides);

        foreach (var guide in guides)
        {
            Assert.Contains($"href=\"{guide}\"", pages["index.html"], StringComparison.Ordinal);
            Assert.Contains($"\"{Path.GetFileName(guide)}\"", menu, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An anchor is only useful if something reaches it, and a section nothing links to is usually
    /// a contents entry somebody forgot rather than a deliberate choice.
    ///
    /// A warning rather than a failure: `index.html` is a landing page whose own headings carry ids
    /// for deep-linking from outside, so an unreferenced id there is normal.
    /// </summary>
    [Fact]
    public void EveryFaqSectionIsReachableFromSomewhere()
    {
        var pages = Pages();
        var all = string.Concat(pages.Values);
        var orphans = Anchors(pages["faq.html"])
            .Where(id => !all.Contains($"href=\"faq.html#{id}\"", StringComparison.Ordinal)
                && !all.Contains($"href=\"#{id}\"", StringComparison.Ordinal))
            .ToList();

        output.WriteLine(orphans.Count == 0
            ? "every FAQ section is linked"
            : "unreferenced: " + string.Join(", ", orphans));

        Assert.True(orphans.Count == 0, "FAQ sections nothing links to: " + string.Join(", ", orphans));
    }

    /// <summary>
    /// The pages ship beside the executable rather than as embedded resources, so they have to be
    /// copied by the build. A help menu that opens a file that is not there is worse than no help
    /// menu, and it only shows up in a published build.
    /// </summary>
    [Fact]
    public void TheAppIsBuiltToShipThem()
    {
        var project = File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(Directory)!, "src", "MillBurn.App", "MillBurn.App.csproj"));

        Assert.Contains("Help", project, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every anchor a companion page links into actually exists in the FAQ.
    ///
    /// The pages written beside a program deep-link into the help, and those links leave the repo:
    /// they end up in an export folder on somebody's machine, where a broken one is a dead end with
    /// no way back. Asserted here because the two halves live in different projects and nothing
    /// else would notice them drifting apart.
    /// </summary>
    [Fact]
    public void EveryAnchorTheCompanionPagesLinkToExists()
    {
        var faq = Anchors(Pages()["faq.html"]);

        // The sections GuideFooter points at, by the names the pages use.
        string[] linked = ["drilling", "drills", "project-page", "testcuts", "levelling", "staydown"];

        output.WriteLine(string.Join(", ", faq.Order(StringComparer.Ordinal)));

        foreach (var anchor in linked)
        {
            Assert.Contains(anchor, faq);
        }
    }
}
