using MillBurn.Pipeline;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// When the app is allowed to interrupt someone about unsaved work.
///
/// The rule is narrow on purpose. A prompt that appears when there is nothing to lose is not a
/// harmless extra safeguard — it is the thing that teaches people to dismiss the prompt without
/// reading it, which costs them the one time it matters.
/// </summary>
public sealed class UnsavedWorkTests
{
    [Fact]
    public void AFreshProjectHasNothingToSave()
    {
        var project = MillBurnProject.Empty();

        Assert.False(project.IsDirty);
        Assert.False(project.NeedsSaving);
    }

    /// <summary>
    /// The case that actually bit: an empty project can still be marked dirty by something that is
    /// not the user editing it, and then the first click of the session asks whether to save
    /// nothing.
    /// </summary>
    [Fact]
    public void AnEmptyProjectIsNotWorthSavingEvenWhenItIsDirty()
    {
        var project = MillBurnProject.Empty();
        project.Touch();

        Assert.True(project.IsDirty);
        Assert.False(project.NeedsSaving);
    }

    /// <summary>Dropping a folder configures nothing, so replacing it costs nothing.</summary>
    [Fact]
    public void AJustImportedBoardIsNotWorthSavingEither()
    {
        var project = MillBurnProject.FromSources(
            ProjectFile.ImportFolder(RealBoards.Directory(RealBoards.PogoTest1)),
            RealBoards.Directory(RealBoards.PogoTest1));

        Assert.NotEmpty(project.Sources);
        Assert.False(project.NeedsSaving);
    }

    [Fact]
    public void AChangedBoardIsWorthSaving()
    {
        var project = MillBurnProject.FromSources(
            ProjectFile.ImportFolder(RealBoards.Directory(RealBoards.PogoTest1)),
            RealBoards.Directory(RealBoards.PogoTest1));

        project.Touch();

        Assert.True(project.NeedsSaving);
    }

    [Fact]
    public void SavingClearsIt()
    {
        var project = MillBurnProject.FromSources(
            ProjectFile.ImportFolder(RealBoards.Directory(RealBoards.PogoTest1)), null);

        project.Touch();
        project.MarkSaved();

        Assert.False(project.NeedsSaving);
    }
}
