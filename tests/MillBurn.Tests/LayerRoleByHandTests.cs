using System.Text;
using System.Text.RegularExpressions;
using MillBurn.Core;
using MillBurn.Pipeline;

namespace MillBurn.Tests;

/// <summary>
/// Telling a layer what it is, by hand — 06 §6.31 — and saying when its role was only a guess.
///
/// What breaks in the workshop without it: a board from an exporter that writes no file functions,
/// or uses a naming scheme the matcher does not know, gets a role read off each file's name and
/// nothing to put it right with. And a guessed role looked exactly like a declared one, because a
/// board built from a project — which is every board the window shows — reported every layer as
/// declared: the advice that a role was a guess never fired.
///
/// The fixtures are real: `TopBotNames` is PogoTest1 renamed to the Top/Bot/Ln1_Cu convention with
/// its file-function attributes stripped, so every role on it is a guess; PogoTest1 itself declares
/// every function, so none of its roles are.
/// </summary>
public sealed partial class LayerRoleByHandTests : IDisposable
{
    private const string Guessing = "TopBotNames";

    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "millburn-roles-" + Guid.NewGuid().ToString("N"));

    public LayerRoleByHandTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

    private static MillBurnProject Load(string folder) =>
        MillBurnProject.FromSources(ProjectFile.ImportFolder(folder), folder);

    private static ProjectSource Source(MillBurnProject project, string fileName) =>
        project.Sources.Single(s => s.FileName == fileName);

    private static BoardLayer Layer(Board board, string fileName) =>
        board.Layers.Single(l => l.FileName == fileName);

    /// <summary>
    /// The guess is kept from the moment the file is imported to the board the window draws.
    /// Import always knew and threw the answer away; the board from a project said false for all.
    /// </summary>
    [Fact]
    public void AGuessIsRememberedFromImportToTheBoard()
    {
        var guessing = Load(RealBoards.Directory(Guessing));
        Assert.NotEmpty(guessing.Sources);
        Assert.All(guessing.Sources, s => Assert.True(s.RoleGuessed, $"{s.FileName} was not marked as guessed"));
        var board = ProjectFile.ToBoard(guessing);
        Assert.NotEmpty(board.Layers);
        Assert.All(board.Layers, l => Assert.True(l.RoleGuessed, $"{l.FileName} lost its guess on the way to the board"));

        // And a board whose files say what they are guesses nothing.
        var declaring = Load(RealBoards.Directory(RealBoards.PogoTest1));
        Assert.False(Source(declaring, "PogoTest1-F_Cu.gbr").RoleGuessed);
        Assert.False(Layer(ProjectFile.ToBoard(declaring), "PogoTest1-F_Cu.gbr").RoleGuessed);
    }

    /// <summary>
    /// Setting a role by hand changes it, marks it as the operator's, stops calling it a guess, and
    /// forgets the layer's old settings so it takes the defaults for what it now is.
    /// </summary>
    [Fact]
    public void ARoleSetByHandIsTheOperatorsAndNoLongerAGuess()
    {
        var project = Load(RealBoards.Directory(Guessing));
        Assert.Equal(LayerRole.InnerCopper, Source(project, "Ln1_Cu.gbr").Role);

        // A setting chosen while it was inner copper, which is no choice about documentation.
        project.Settings = project.Settings.WithOutput(new LayerOutputSettings { FileName = "Ln1_Cu.gbr", Output = OutputKind.Svg });
        project.MarkSaved();

        Assert.True(project.SetRole("Ln1_Cu.gbr", LayerRole.Documentation));

        var set = Source(project, "Ln1_Cu.gbr");
        Assert.Equal(LayerRole.Documentation, set.Role);
        Assert.True(set.RoleOverridden);
        Assert.False(set.RoleGuessed);
        Assert.Null(project.Settings.OutputFor("Ln1_Cu.gbr"));
        Assert.True(project.IsDirty, "a role set by hand is a change somebody has to be asked to save");

        var layer = Layer(ProjectFile.ToBoard(project), "Ln1_Cu.gbr");
        Assert.Equal(LayerRole.Documentation, layer.Role);
        Assert.False(layer.RoleGuessed);

        // The others are untouched, and still guesses.
        Assert.True(Source(project, "Top.gbr").RoleGuessed);
        Assert.False(Source(project, "Top.gbr").RoleOverridden);

        // Nothing to do, and said so: the same role again, or a file that is not there.
        Assert.False(project.SetRole("Ln1_Cu.gbr", LayerRole.Documentation));
        Assert.False(project.SetRole("NoSuchFile.gbr", LayerRole.TopCopper));
    }

    /// <summary>
    /// Confirming a guess — choosing the role it already has — makes it the operator's. That is the
    /// two-click answer to "is this right?", and it has to stop the row calling it a guess.
    /// </summary>
    [Fact]
    public void ConfirmingAGuessMakesItTheOperatorsWithoutChangingIt()
    {
        var project = Load(RealBoards.Directory(Guessing));
        var before = Source(project, "Top.gbr");
        Assert.True(before.RoleGuessed);

        // Settings the operator chose for this layer as it is. Confirming the role must not cost them.
        var chosen = new LayerOutputSettings { FileName = "Top.gbr", Output = OutputKind.Gcode, DepthNm = Nm.FromMillimetres(0.08) };
        project.Settings = project.Settings.WithOutput(chosen);

        Assert.True(project.SetRole("Top.gbr", before.Role));
        Assert.Equal(chosen, project.Settings.OutputFor("Top.gbr"));

        var after = Source(project, "Top.gbr");
        Assert.Equal(before.Role, after.Role);
        Assert.True(after.RoleOverridden);
        Assert.False(after.RoleGuessed);
        Assert.False(Layer(ProjectFile.ToBoard(project), "Top.gbr").RoleGuessed);
    }

    /// <summary>Both the guess and the hand-set role survive a save and a reopen.</summary>
    [Fact]
    public void TheGuessAndTheHandSurviveASaveAndReopen()
    {
        var project = Load(RealBoards.Directory(Guessing));
        project.SetRole("Ln1_Cu.gbr", LayerRole.Documentation);

        var path = Path.Combine(_scratch, "roles.millburn");
        ProjectFile.Save(project, path);

        // Written into the file, so what comes back below is what was saved rather than what opening
        // would have worked out without it.
        Assert.Contains("\"RoleGuessed\": true", Manifest(path), StringComparison.Ordinal);

        var reopened = ProjectFile.Open(path);

        Assert.True(Source(reopened, "Top.gbr").RoleGuessed);
        Assert.Equal(LayerRole.Documentation, Source(reopened, "Ln1_Cu.gbr").Role);
        Assert.True(Source(reopened, "Ln1_Cu.gbr").RoleOverridden);
        Assert.False(Source(reopened, "Ln1_Cu.gbr").RoleGuessed);
    }

    /// <summary>
    /// A project saved before the flag existed works it out once, on opening, from the same bytes:
    /// a guess if the file still cannot say what it is, unless it was set by hand. **Whatever role
    /// was saved**: a role the name would not give today means the matcher changed, since nothing
    /// before this could set one by hand, and the file still declares nothing.
    /// </summary>
    [Fact]
    public void AProjectSavedBeforeTheFlagWorksItOutWhenOpened()
    {
        var project = Load(RealBoards.Directory(Guessing));
        project.SetRole("Ln1_Cu.gbr", LayerRole.Documentation);

        // Saved with a role the name does not suggest today and nobody marked as set by hand: what an
        // older build's matcher would have left.
        var top = project.Sources.IndexOf(Source(project, "Top_Silk.gbr"));
        project.Sources = project.Sources.SetItem(top, project.Sources[top] with { Role = LayerRole.TopPaste });

        var path = Path.Combine(_scratch, "old.millburn");
        ProjectFile.Save(project, path);

        // Take the field out, as a project saved by 0.5.0 would not have it.
        Rewrite(path, json => GuessedField().Replace(json, string.Empty));
        Assert.DoesNotContain("RoleGuessed", Manifest(path), StringComparison.Ordinal);

        var reopened = ProjectFile.Open(path);

        Assert.True(Source(reopened, "Top.gbr").RoleGuessed);
        Assert.False(Source(reopened, "Ln1_Cu.gbr").RoleGuessed);
        Assert.True(Source(reopened, "Top_Silk.gbr").RoleGuessed, "a file that declares nothing is a guess, whatever role was saved");
        Assert.False(Source(reopened, "Top_Silk.gbr").RoleOverridden, "the case is a role nobody set by hand");

        // And a board that declares its functions comes back guessing nothing.
        var declaring = Load(RealBoards.Directory(RealBoards.PogoTest1));
        var declaringPath = Path.Combine(_scratch, "declaring.millburn");
        ProjectFile.Save(declaring, declaringPath);
        Rewrite(declaringPath, json => GuessedField().Replace(json, string.Empty));
        Assert.DoesNotContain("RoleGuessed", Manifest(declaringPath), StringComparison.Ordinal);
        Assert.All(ProjectFile.Open(declaringPath).Sources, s => Assert.False(s.RoleGuessed, s.FileName));
    }

    /// <summary>
    /// A refresh of the folder keeps a hand-set role, and does not bring the fresh file's guess back
    /// with it: the role is the operator's, whatever the name now suggests.
    /// </summary>
    [Fact]
    public void ARefreshKeepsAHandSetRoleAndDoesNotCallItAGuessAgain()
    {
        var folder = Path.Combine(_scratch, "export");
        Directory.CreateDirectory(folder);

        foreach (var file in Directory.EnumerateFiles(RealBoards.Directory(Guessing)))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        }

        var project = Load(folder);
        project.SetRole("Ln1_Cu.gbr", LayerRole.Documentation);

        // A real change to the file, so the refresh takes a fresh copy of it.
        var ln1 = Path.Combine(folder, "Ln1_Cu.gbr");
        File.WriteAllText(ln1, File.ReadAllText(ln1).Replace(
            "M02*", "%ADD99C,0.500000*%\nD99*\nX151000000Y-90000000D03*\nM02*", StringComparison.Ordinal));

        var plan = ProjectRefresh.Inspect(project, folder);
        Assert.Contains(plan.Actionable, c => c.FileName == "Ln1_Cu.gbr");
        ProjectRefresh.Apply(project, plan, plan.Actionable.Select(c => c.FileName));

        var refreshed = Source(project, "Ln1_Cu.gbr");
        Assert.Equal(LayerRole.Documentation, refreshed.Role);
        Assert.True(refreshed.RoleOverridden);
        Assert.False(refreshed.RoleGuessed);
    }

    private static string Manifest(string projectPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(projectPath);
        using var reader = new StreamReader(archive.GetEntry("project.json")!.Open());
        return reader.ReadToEnd();
    }

    private static void Rewrite(string projectPath, Func<string, string> edit)
    {
        var extracted = Path.Combine(Path.GetDirectoryName(projectPath)!, Path.GetFileNameWithoutExtension(projectPath) + "-unpacked");
        System.IO.Compression.ZipFile.ExtractToDirectory(projectPath, extracted);

        var manifest = Path.Combine(extracted, "project.json");
        File.WriteAllText(manifest, edit(File.ReadAllText(manifest)), new UTF8Encoding(false));

        File.Delete(projectPath);
        System.IO.Compression.ZipFile.CreateFromDirectory(extracted, projectPath);
    }

    [GeneratedRegex(@",\s*""RoleGuessed""\s*:\s*(true|false)")]
    private static partial Regex GuessedField();
}
