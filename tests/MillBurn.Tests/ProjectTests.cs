using System.Text;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Projects: saving, opening, and refreshing from a re-exported source folder.
///
/// The refresh is the part with teeth. Everything about it turns on telling "the file changed"
/// apart from "the board changed", and an EDA tool makes those two answers differ on every single
/// export.
/// </summary>
public sealed class ProjectTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "millburn-project-" + Guid.NewGuid().ToString("N"));

    public ProjectTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

    private string Scratch(string name) => Path.Combine(_scratch, name);

    /// <summary>A copy of a real board in a scratch folder, so a test can edit its files.</summary>
    private string CopyBoard(string name = "export")
    {
        var target = Scratch(name);
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(RealBoards.Directory(RealBoards.PogoTest1)))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        return target;
    }

    private static MillBurnProject Load(string folder) =>
        MillBurnProject.FromSources(ProjectFile.ImportFolder(folder), folder);

    /// <summary>
    /// Edits a Gerber so its geometry genuinely differs: an extra aperture and a flash, inserted
    /// before the end-of-file. Works on any layer, which matters — a test that edits an aperture
    /// only some layers happen to define ends up asserting against a file it never touched.
    /// </summary>
    private static void EditGeometry(string file) =>
        File.WriteAllText(
            file,
            File.ReadAllText(file).Replace(
                "M02*",
                "%ADD99C,0.500000*%\nD99*\nX151000000Y-90000000D03*\nM02*",
                StringComparison.Ordinal));

    // ------------------------------------------------------------------ round trip

    /// <summary>
    /// The board's thickness is the project's. It used to be the app's, so the last board worked on
    /// set the depth of the next one, and a 1.6 mm board opened after a 0.8 mm one cut 0.8 mm short.
    /// </summary>
    [Fact]
    public void TheBoardThicknessIsSavedWithTheProject()
    {
        var project = Load(CopyBoard());
        project.Settings = project.Settings with { BoardThicknessMm = 0.8 };
        var path = Scratch("thin" + ProjectFile.Extension);

        ProjectFile.Save(project, path);

        Assert.Equal(0.8, ProjectFile.Open(path).Settings.BoardThicknessMm);
    }

    /// <summary>
    /// A project that never recorded a thickness reopens with none — not a made-up 1.6 — so the app
    /// can say so and fall back to the last one set. Null is not written, so this is also exactly
    /// what a project saved before thickness was kept reads back as.
    /// </summary>
    [Fact]
    public void AProjectThatNeverRecordedAThicknessReopensWithNone()
    {
        var project = Load(CopyBoard());
        var path = Scratch("unrecorded" + ProjectFile.Extension);

        ProjectFile.Save(project, path);

        Assert.Null(ProjectFile.Open(path).Settings.BoardThicknessMm);
    }

    /// <summary>
    /// The drill alignment found at the machine is kept with the board it was found on, turn and all,
    /// so the next session starts from it instead of from the operator's memory of it.
    /// </summary>
    [Fact]
    public void TheDrillAlignmentIsSavedWithTheProject()
    {
        var project = Load(CopyBoard());
        var found = DateTimeOffset.Now;

        var alignment = new AlignmentRecord
        {
            XMm = 0.12,
            YMm = -0.05,
            RotationDegrees = 0.42,
            PivotXMm = 12.5,
            PivotYMm = 9,
            Outline = true,
            Moved = ["Board-B_Cu.gbr|Isolation"],
            Flipped = true,
            Found = found,
        };

        project.Settings = project.Settings with { Alignment = alignment };
        var path = Scratch("aligned" + ProjectFile.Extension);

        ProjectFile.Save(project, path);

        var saved = ProjectFile.Open(path).Settings.Alignment;

        Assert.NotNull(saved);
        Assert.Equal(0.42, saved.RotationDegrees);
        Assert.Equal(found.ToUnixTimeSeconds(), saved.Found?.ToUnixTimeSeconds());

        // Which programs it moved, and which way up the board was — both decide what the numbers mean.
        Assert.Equal(["Board-B_Cu.gbr|Isolation"], saved.Moved);
        Assert.True(saved.Flipped);
        Assert.Equal(saved.Moved, saved.ToAlignment().Moved);

        // And it comes back as the same correction, to the nanometre.
        var correction = saved.ToAlignment();

        Assert.Equal(Nm.FromMillimetres(0.12), correction.XNm);
        Assert.Equal(Nm.FromMillimetres(-0.05), correction.YNm);
        Assert.Equal(new Point2(Nm.FromMillimetres(12.5), Nm.FromMillimetres(9)), correction.PivotNm);
        Assert.True(correction.Outline);
    }

    /// <summary>A board that has never been aligned reopens with no correction, rather than a zero one.</summary>
    [Fact]
    public void AProjectThatWasNeverAlignedReopensWithNoCorrection()
    {
        var path = Scratch("unaligned" + ProjectFile.Extension);

        ProjectFile.Save(Load(CopyBoard()), path);

        Assert.Null(ProjectFile.Open(path).Settings.Alignment);
    }

    [Fact]
    public void AProjectSurvivesASaveAndReopen()
    {
        var folder = CopyBoard();
        var project = Load(folder);
        var path = Scratch("board" + ProjectFile.Extension);

        ProjectFile.Save(project, path);
        var reopened = ProjectFile.Open(path);

        Assert.Equal(project.Sources.Length, reopened.Sources.Length);
        Assert.Equal(folder, reopened.OriginFolder);
        Assert.Equal(MillBurnProject.CurrentSchemaVersion, reopened.SchemaVersion);
        Assert.False(reopened.IsDirty);

        foreach (var (before, after) in project.Sources.Zip(reopened.Sources))
        {
            Assert.Equal(before.FileName, after.FileName);
            Assert.Equal(before.ContentHash, after.ContentHash);
            Assert.Equal(before.GeometryHash, after.GeometryHash);
            Assert.Equal(before.Role, after.Role);
            // ImmutableArray<T>.Equals is reference equality on the underlying array, so two
            // arrays with identical bytes are not Equal. Compare the spans.
            Assert.True(
                before.Content.AsSpan().SequenceEqual(after.Content.AsSpan()),
                $"{before.FileName}: embedded bytes differ after a round trip");
        }
    }

    /// <summary>
    /// The project carries its own copies, so it still opens once the export folder is gone. That
    /// is the whole reason to embed rather than reference a path.
    /// </summary>
    [Fact]
    public void AProjectOpensAfterItsSourceFolderHasGone()
    {
        var folder = CopyBoard();
        var path = Scratch("board" + ProjectFile.Extension);
        ProjectFile.Save(Load(folder), path);

        Directory.Delete(folder, recursive: true);

        var reopened = ProjectFile.Open(path);
        var board = ProjectFile.ToBoard(reopened);

        Assert.Equal(9, board.Layers.Count);
        Assert.Contains(board.Layers, l => l.Role == LayerRole.TopCopper && l.AreaMm2 > 0);
    }

    [Fact]
    public void SavingClearsTheDirtyFlagAndRecordsThePath()
    {
        var project = Load(CopyBoard());
        var path = Scratch("board" + ProjectFile.Extension);

        project.Touch();
        Assert.True(project.IsDirty);

        ProjectFile.Save(project, path);

        Assert.False(project.IsDirty);
        Assert.Equal(path, project.FilePath);
    }

    /// <summary>
    /// A freshly dropped folder is not a modified document. Nothing has been configured, so
    /// replacing it costs the user nothing and must not interrupt them.
    /// </summary>
    [Fact]
    public void AFreshlyLoadedFolderIsNotDirty() =>
        Assert.False(Load(CopyBoard()).IsDirty);

    /// <summary>
    /// View state is persisted but must never mark the document dirty: if peeking under a layer
    /// prompts a save, people learn to hit Discard without reading.
    /// </summary>
    [Fact]
    public void ChangingViewStateDoesNotDirtyTheDocument()
    {
        var folder = CopyBoard();
        var project = Load(folder);

        project.ViewState = new ProjectViewState { HiddenLayers = ["PogoTest1-F_Mask.gbr"] };
        Assert.False(project.IsDirty);

        var path = Scratch("board" + ProjectFile.Extension);
        ProjectFile.Save(project, path);

        Assert.Equal(["PogoTest1-F_Mask.gbr"], ProjectFile.Open(path).ViewState.HiddenLayers.ToArray());
    }

    [Fact]
    public void AFileThatIsNotAProjectFailsClearly()
    {
        var path = Scratch("not-a-project" + ProjectFile.Extension);
        File.WriteAllText(path, "this is not a zip");

        Assert.ThrowsAny<Exception>(() => ProjectFile.Open(path));
    }

    [Fact]
    public void AProjectFromANewerBuildIsRefusedRatherThanMisread()
    {
        var path = Scratch("board" + ProjectFile.Extension);
        ProjectFile.Save(Load(CopyBoard()), path);

        // Rewrite the manifest's schema version to something this build cannot know about.
        Rewrite(path, json => json.Replace(
            $"\"SchemaVersion\": {MillBurnProject.CurrentSchemaVersion}",
            $"\"SchemaVersion\": {MillBurnProject.CurrentSchemaVersion + 99}",
            StringComparison.Ordinal));

        var ex = Assert.Throws<InvalidDataException>(() => ProjectFile.Open(path));
        Assert.Contains("newer version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ refresh

    [Fact]
    public void AnUntouchedFolderReportsNoChanges()
    {
        var folder = CopyBoard();
        var plan = ProjectRefresh.Inspect(Load(folder), folder);

        Assert.False(plan.HasChanges);
        Assert.All(plan.Changes, c => Assert.Equal(SourceChangeKind.Unchanged, c.Kind));
    }

    /// <summary>
    /// **The one that matters.** KiCad stamps a creation date into every file, so re-exporting an
    /// unedited board changes every byte of every layer. A refresh that compared file contents
    /// would announce that all nine layers changed, every time, and a warning that is always wrong
    /// is a warning nobody reads. The geometry fingerprint is what tells the truth.
    /// </summary>
    [Fact]
    public void ARexportWithNoEditsIsNotReportedAsAChangeToTheBoard()
    {
        var folder = CopyBoard();
        var project = Load(folder);

        // Exactly what a re-export does to an unedited board: a new timestamp, same geometry.
        foreach (var file in Directory.EnumerateFiles(folder, "*.gbr"))
        {
            var text = File.ReadAllText(file);
            File.WriteAllText(file, text.Replace(
                "%TF.CreationDate,2026-09-07T14:35:50-06:00*%",
                "%TF.CreationDate,2026-11-30T09:15:00-06:00*%",
                StringComparison.Ordinal));
        }

        var plan = ProjectRefresh.Inspect(project, folder);

        // Every Gerber's bytes differ...
        Assert.Contains(plan.Changes, c => c.Kind == SourceChangeKind.Reexported);

        // ...and not one of them is reported as affecting the board.
        Assert.False(plan.AffectsBoard);
        Assert.DoesNotContain(plan.Changes, c => c.Kind == SourceChangeKind.Changed);
        Assert.Contains("no change to the board", plan.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEditedLayerIsReportedWithWhatChanged()
    {
        var folder = CopyBoard();
        var project = Load(folder);

        // A real edit to one layer, leaving the rest alone.
        EditGeometry(Path.Combine(folder, "PogoTest1-F_Silkscreen.gbr"));

        var plan = ProjectRefresh.Inspect(project, folder);
        var change = plan.Changes.Single(c => c.FileName == "PogoTest1-F_Silkscreen.gbr");

        Assert.Equal(SourceChangeKind.Changed, change.Kind);
        Assert.True(plan.AffectsBoard);
        Assert.Contains(change.Details, d => d.Contains("Area", StringComparison.Ordinal));

        // And nothing else was disturbed.
        Assert.All(
            plan.Changes.Where(c => c.FileName != "PogoTest1-F_Silkscreen.gbr"),
            c => Assert.Equal(SourceChangeKind.Unchanged, c.Kind));
    }

    [Fact]
    public void AddedAndRemovedFilesAreReported()
    {
        var folder = CopyBoard();
        var project = Load(folder);

        File.Delete(Path.Combine(folder, "PogoTest1-B_Silkscreen.gbr"));
        File.Copy(
            Path.Combine(folder, "PogoTest1-F_Cu.gbr"),
            Path.Combine(folder, "PogoTest1-In1_Cu.gbr"));

        var plan = ProjectRefresh.Inspect(project, folder);

        Assert.Equal(SourceChangeKind.Removed, plan.Changes.Single(c => c.FileName == "PogoTest1-B_Silkscreen.gbr").Kind);
        Assert.Equal(SourceChangeKind.Added, plan.Changes.Single(c => c.FileName == "PogoTest1-In1_Cu.gbr").Kind);
    }

    /// <summary>
    /// The whole point: settings survive a refresh, because they are settings rather than geometry.
    /// </summary>
    [Fact]
    public void RefreshingKeepsTheSettings()
    {
        var folder = CopyBoard();
        var project = Load(folder);
        project.Settings = project.Settings with
        {
            Exclusions = [SelectionRef.ForNet("GND"), SelectionRef.ForPad("U3", "2")],
            Notes = "0.05 deep, 30 degree bit",
        };

        EditGeometry(Path.Combine(folder, "PogoTest1-F_Silkscreen.gbr"));

        var plan = ProjectRefresh.Inspect(project, folder);
        ProjectRefresh.Apply(project, plan, plan.Actionable.Select(c => c.FileName));

        Assert.Equal(2, project.Settings.Exclusions.Length);
        Assert.Equal("net GND", project.Settings.Exclusions[0].ToString());
        Assert.Equal("U3 pin 2", project.Settings.Exclusions[1].ToString());
        Assert.Equal("0.05 deep, 30 degree bit", project.Settings.Notes);
    }

    /// <summary>
    /// Partial refresh: fixing a typo in the legend must not disturb anything attached to copper.
    /// </summary>
    [Fact]
    public void OnlyTheNamedFilesAreTaken()
    {
        var folder = CopyBoard();
        var project = Load(folder);
        var copperBefore = project.Sources.Single(s => s.FileName == "PogoTest1-F_Cu.gbr");

        EditGeometry(Path.Combine(folder, "PogoTest1-F_Silkscreen.gbr"));
        EditGeometry(Path.Combine(folder, "PogoTest1-F_Cu.gbr"));

        var plan = ProjectRefresh.Inspect(project, folder);
        ProjectRefresh.Apply(project, plan, ["PogoTest1-F_Silkscreen.gbr"]);

        Assert.NotEqual(
            copperBefore.ContentHash,
            plan.Changes.Single(c => c.FileName == "PogoTest1-F_Cu.gbr").Incoming!.ContentHash);

        // Copper untouched, silk taken.
        Assert.Equal(copperBefore, project.Sources.Single(s => s.FileName == "PogoTest1-F_Cu.gbr"));
        Assert.Equal(
            plan.Changes.Single(c => c.FileName == "PogoTest1-F_Silkscreen.gbr").Incoming!.ContentHash,
            project.Sources.Single(s => s.FileName == "PogoTest1-F_Silkscreen.gbr").ContentHash);
    }

    /// <summary>
    /// A role the user corrected by hand outlives the file it was corrected on. Re-detecting it on
    /// every refresh would undo the correction silently.
    /// </summary>
    [Fact]
    public void AHandCorrectedRoleSurvivesARefresh()
    {
        var folder = CopyBoard();
        var project = Load(folder);

        var index = project.Sources.IndexOf(project.Sources.Single(s => s.FileName == "PogoTest1-F_Silkscreen.gbr"));
        project.Sources = project.Sources.SetItem(
            index,
            project.Sources[index] with { Role = LayerRole.TopPaste, RoleOverridden = true });

        EditGeometry(Path.Combine(folder, "PogoTest1-F_Silkscreen.gbr"));

        var plan = ProjectRefresh.Inspect(project, folder);
        ProjectRefresh.Apply(project, plan, plan.Actionable.Select(c => c.FileName));

        var refreshed = project.Sources.Single(s => s.FileName == "PogoTest1-F_Silkscreen.gbr");
        Assert.Equal(LayerRole.TopPaste, refreshed.Role);
        Assert.True(refreshed.RoleOverridden);
    }

    /// <summary>
    /// A refresh is a document edit, so closing without saving undoes it. That is the whole of the
    /// undo story for now, and it is honest.
    /// </summary>
    [Fact]
    public void ApplyingARefreshMarksTheProjectDirty()
    {
        var folder = CopyBoard();
        var project = Load(folder);

        EditGeometry(Path.Combine(folder, "PogoTest1-F_Silkscreen.gbr"));

        var plan = ProjectRefresh.Inspect(project, folder);
        Assert.False(project.IsDirty);

        ProjectRefresh.Apply(project, plan, plan.Actionable.Select(c => c.FileName));
        Assert.True(project.IsDirty);
    }

    [Fact]
    public void InspectingDoesNotChangeAnything()
    {
        var folder = CopyBoard();
        var project = Load(folder);
        var before = project.Sources;

        EditGeometry(Path.Combine(folder, "PogoTest1-F_Silkscreen.gbr"));

        ProjectRefresh.Inspect(project, folder);

        Assert.Equal(before, project.Sources);
        Assert.False(project.IsDirty);
    }

    // ------------------------------------------------------------------ selection identity

    /// <summary>
    /// Selections are stored by the designer's own names, which is what lets them re-attach after
    /// an edit. Coordinates and indices both detach the moment anything moves.
    /// </summary>
    [Fact]
    public void SelectionsRoundTripThroughAProjectFile()
    {
        var project = Load(CopyBoard());
        project.Settings = project.Settings with
        {
            Exclusions =
            [
                SelectionRef.ForNet("GND"),
                SelectionRef.ForComponent("U3"),
                SelectionRef.ForPad("J1", "1"),
                SelectionRef.ForPoint(new Point2(1_000_000, -2_000_000)),
            ],
        };

        var path = Scratch("board" + ProjectFile.Extension);
        ProjectFile.Save(project, path);

        var reopened = ProjectFile.Open(path).Settings.Exclusions;

        Assert.Equal(4, reopened.Length);
        Assert.Equal(SelectionKind.Net, reopened[0].Kind);
        Assert.Equal("GND", reopened[0].Net);
        Assert.Equal("U3", reopened[1].Component);
        Assert.Equal("J1", reopened[2].Component);
        Assert.Equal("1", reopened[2].Pin);
        Assert.Equal(new Point2(1_000_000, -2_000_000), reopened[3].At);
    }

    private static void Rewrite(string projectPath, Func<string, string> edit)
    {
        var extracted = Path.Combine(Path.GetDirectoryName(projectPath)!, "unpacked");
        System.IO.Compression.ZipFile.ExtractToDirectory(projectPath, extracted);

        var manifest = Path.Combine(extracted, "project.json");
        File.WriteAllText(manifest, edit(File.ReadAllText(manifest)), new UTF8Encoding(false));

        File.Delete(projectPath);
        System.IO.Compression.ZipFile.CreateFromDirectory(extracted, projectPath);
    }
}
