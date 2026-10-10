using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Where the Gerbers are, as the Project info panel says it.
///
/// **From the product owner:** *"We should display the gerber folder somewhere so the user knows
/// where the project is from."* Recorded in
/// [06 §6.32](../../Documentation/06-Roadmap-and-Risks.md). The window title is the folder's last
/// segment alone, and this machine has two boards called `Millburn_Test_Board` in different folders:
/// a project opened from the wrong one looks exactly like a project opened from the right one, right
/// up until its programs are cut.
///
/// **The panel shows the path whole**, so there is very little rule left to test — only that what it
/// shows is a path somebody could walk to. A first version elided the middle and had the question
/// wrong: *"what I would like to know is where the source folder is"* is not answered by a path with
/// its middle taken out, however well that tells two boards apart.
/// </summary>
public sealed class ProjectOriginTests(ITestOutputHelper output)
{
    /// <summary>A path in this platform's separators, so the tests read the same on either.</summary>
    private static string Path(params string[] segments) =>
        string.Join(System.IO.Path.DirectorySeparatorChar, segments);

    /// <summary>
    /// A relative path is made absolute, in this platform's separators.
    ///
    /// **Found by looking at the panel.** A board opened as `tests/boards/Millburn_Test_Board` showed
    /// exactly that: forward slashes in a window of backslashes, and a path relative to a working
    /// directory the operator never sees — which answers *where is it* with "it depends where you
    /// were standing", on the one line added to answer it properly.
    /// </summary>
    [Fact]
    public void ARelativePathIsMadeAbsolute()
    {
        var full = ProjectOrigin.Full("tests/boards/Millburn_Test_Board");

        output.WriteLine(full);

        Assert.True(System.IO.Path.IsPathRooted(full), full);
        Assert.EndsWith("Millburn_Test_Board", full, StringComparison.Ordinal);

        // Rooted *here*, which is not the same as rooted: a `/describe-test` pass pointed out that
        // resolving against some other base would have satisfied the line above, and a path that is
        // absolute and wrong is the one outcome worse than a path that is relative.
        Assert.StartsWith(Environment.CurrentDirectory, full, StringComparison.OrdinalIgnoreCase);

        if (System.IO.Path.DirectorySeparatorChar == '\\')
        {
            Assert.DoesNotContain('/', full);
        }
    }

    /// <summary>
    /// An absolute path comes back as it went in.
    ///
    /// The common case by far — a folder picked from a dialog is already absolute — and the one
    /// where incidental rewriting would show: a trailing separator added, a drive letter re-cased, a
    /// segment normalised away.
    /// </summary>
    [Fact]
    public void AnAbsolutePathIsLeftAlone()
    {
        var path = Path("E:", "Workspace", "PCB_MillBurn", "tests", "boards", "Millburn_Test_Board");

        output.WriteLine(ProjectOrigin.Full(path));

        Assert.Equal(path, ProjectOrigin.Full(path));
    }

    /// <summary>
    /// The whole path survives, however deep it is.
    ///
    /// **This is the point of the story, and it is a negative.** The first version shortened the
    /// middle out of a long path to keep the panel narrow; what the workshop asked for is where the
    /// folder is, and the middle is most of that. So: no ellipsis, nothing dropped, every segment
    /// still there — the view wraps it rather than hiding any of it.
    /// </summary>
    [Fact]
    public void ADeepPathIsNotShortened()
    {
        string[] segments =
        [
            "E:", "Workspace", "PCB_MillBurn", "WorkingFolder", "V0.5.0",
            "Deep Path Check", "A Rather Long Folder Name", "Millburn_Test_Board",
        ];

        var full = ProjectOrigin.Full(Path(segments));

        output.WriteLine($"{full.Length} characters: {full}");

        Assert.DoesNotContain("…", full, StringComparison.Ordinal);
        Assert.DoesNotContain("...", full, StringComparison.Ordinal);

        foreach (var segment in segments)
        {
            Assert.Contains(segment, full, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A path that cannot be resolved comes back as it was, rather than throwing.
    ///
    /// This runs while a panel is drawing, off a string a project file supplied — which is to say off
    /// something this build did not write and cannot vouch for. A folder name that is merely odd is a
    /// better outcome than an exception thrown during layout.
    /// </summary>
    [Fact]
    public void APathThatCannotBeResolvedIsReturnedAsItIs()
    {
        var nonsense = "\0not a path at all";

        output.WriteLine(ProjectOrigin.Full(nonsense).Replace("\0", "<nul>", StringComparison.Ordinal));

        Assert.Equal(nonsense, ProjectOrigin.Full(nonsense));
    }
}
