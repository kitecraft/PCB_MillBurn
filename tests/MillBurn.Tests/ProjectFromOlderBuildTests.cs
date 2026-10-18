using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Pipeline;

namespace MillBurn.Tests;

/// <summary>
/// Projects saved by v0.6.0 itself, opened by this build.
///
/// **The test that used to stand in for this opened a file the current build had just written**,
/// which proves nothing about an older one — `/describe-test` said so in sprint 6 story 5. These
/// open two real files: <c>MadeBy060.millburn</c>, saved by the v0.6.0 command line from the test
/// board with its outline and top copper set to G-code at 0.8 mm; and
/// <c>MadeBy060WithTabEdit.millburn</c>, the same file with one per-tab width added the way
/// v0.6.0's tabs window wrote one. This release adds fields to that file; an older one has none of
/// them and must still mean what it meant.
/// </summary>
public sealed class ProjectFromOlderBuildTests
{
    private static string Fixture(string name) =>
        Path.GetFullPath(Path.Combine(RealBoards.Root, "..", "MillBurn.Tests", "Fixtures", name));

    private static Dictionary<string, LayerOutputSettings> Outputs(MillBurnProject project) =>
        project.Settings.LayerOutputs.ToDictionary(o => o.FileName, o => o, StringComparer.Ordinal);

    /// <summary>The programs a project plans to, by name, in name order.</summary>
    private static string Programs(MillBurnProject project, IReadOnlyDictionary<string, LayerOutputSettings> outputs)
    {
        var plan = ExportPlanner.Plan(
            ProjectFile.ToBoard(project), outputs, ToolLibrary.Default, Nm.FromMillimetres(project.Settings.BoardThicknessMm ?? 0.8));

        return string.Join("\n", plan.Items
            .OrderBy(i => i.TargetName, StringComparer.Ordinal)
            .Select(i => i.TargetName + "\n" + i.Content));
    }

    /// <summary>
    /// The v0.6.0 file opens, with what it said: two layers to G-code, four tabs, no hand-set
    /// changes and no fingerprint (it has never heard of one).
    /// </summary>
    [Fact]
    public void AProjectSavedBy060OpensWithItsSettings()
    {
        var project = ProjectFile.Open(Fixture("MadeBy060.millburn"));
        var outputs = Outputs(project);

        Assert.Equal(15, project.Sources.Length);
        Assert.Equal(OutputKind.Gcode, outputs["Millburn_Test_Board-Edge_Cuts.gbr"].Output);
        Assert.Equal(OutputKind.Gcode, outputs["Millburn_Test_Board-F_Cu.gbr"].Output);

        var outline = outputs["Millburn_Test_Board-Edge_Cuts.gbr"];
        Assert.Equal(4, outline.TabCount);
        Assert.True(outline.TabEdits.IsDefaultOrEmpty);
        Assert.True(outline.TabMoves.IsDefaultOrEmpty);
        Assert.Null(outline.TabPlanKey);
    }

    /// <summary>
    /// **Its embedded sources make the same board the folder does.** Both sides are planned with
    /// the older file's own layer settings, so what this compares is the board — the Gerbers the
    /// v0.6.0 file carries, read back by this build, against the same Gerbers imported fresh — and
    /// any project-level setting the planner reads. The layer settings themselves are checked
    /// field by field above.
    /// </summary>
    [Fact]
    public void ItsSourcesMakeTheSameBoardAsTheFolder()
    {
        var old = ProjectFile.Open(Fixture("MadeBy060.millburn"));

        var fresh = MillBurnProject.FromSources(
            ProjectFile.ImportFolder(RealBoards.Directory(RealBoards.MillburnTestBoard)),
            originFolder: null);

        Assert.Equal(Programs(old, Outputs(old)), Programs(fresh, Outputs(old)), StringComparer.Ordinal);
    }

    /// <summary>
    /// **A per-tab size from v0.6.0 is refused, not guessed at.** It names "tab 1" of v0.6.0's plan,
    /// and this release spreads tabs differently, so tab 1 may now be another tab. Without a
    /// fingerprint nothing says which plan it meant, so the program cuts the planner's own tabs and
    /// its header says why — the edge-case this release's fingerprint exists for.
    /// </summary>
    [Fact]
    public void A060TabEditIsRefusedAndTheProgramSaysWhy()
    {
        var project = ProjectFile.Open(Fixture("MadeBy060WithTabEdit.millburn"));
        var outputs = Outputs(project);
        var outline = outputs["Millburn_Test_Board-Edge_Cuts.gbr"];

        var edit = Assert.Single(outline.TabEdits);
        Assert.Equal(1, edit.Index);
        Assert.Equal(Nm.FromMillimetres(4), edit.WidthNm);

        var programs = Programs(project, outputs);

        // Said once, and in the outline's own program — the one whose tabs it is about.
        Assert.Single(
            programs.Split('\n'),
            l => l.Contains("made against a different layout of these tabs", StringComparison.Ordinal));

        var plan = ExportPlanner.Plan(ProjectFile.ToBoard(project), outputs, ToolLibrary.Default, Nm.FromMillimetres(0.8));
        var outlineProgram = Assert.Single(plan.Items, ExportPlanner.IsBoardOutline);
        Assert.Contains("made against a different layout of these tabs", outlineProgram.Content, StringComparison.Ordinal);

        // And the plan is the planner's: the same programs, apart from that one line of header,
        // as the file without the edit.
        var plain = ProjectFile.Open(Fixture("MadeBy060.millburn"));
        var withoutEdit = Programs(plain, Outputs(plain));

        Assert.Equal(
            withoutEdit,
            string.Join("\n", programs.Split('\n').Where(l => !l.Contains("made against a different layout", StringComparison.Ordinal))),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The fingerprint a change is stamped with, and the change, are saved and read back as they
    /// were. (Whether a stamped change then applies is the planner's to say, and is tested there.)
    /// </summary>
    [Fact]
    public void TheStampAndTheChangeAreSavedAndReadBack()
    {
        var project = ProjectFile.Open(Fixture("MadeBy060.millburn"));
        var outline = project.Settings.LayerOutputs.First(o => o.FileName.EndsWith("Edge_Cuts.gbr", StringComparison.Ordinal));

        project.Settings = project.Settings.WithOutput(outline with
        {
            TabMoves = [new TabMove { Index = 0 }],
            TabPlanKey = "0123456789ab",
        });

        var path = Path.Combine(Path.GetTempPath(), $"millburn-stamp-{Guid.NewGuid().ToString("N")[..8]}{ProjectFile.Extension}");

        try
        {
            ProjectFile.Save(project, path);
            var reopened = ProjectFile.Open(path).Settings.LayerOutputs
                .First(o => o.FileName.EndsWith("Edge_Cuts.gbr", StringComparison.Ordinal));

            Assert.Equal("0123456789ab", reopened.TabPlanKey);

            var move = Assert.Single(reopened.TabMoves);
            Assert.True(move.IsRemoval);
            Assert.Equal(0, move.Index);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
