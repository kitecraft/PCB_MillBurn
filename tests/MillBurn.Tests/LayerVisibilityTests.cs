using MillBurn.Pipeline;
using MillBurn.Viewer;

namespace MillBurn.Tests;

/// <summary>
/// 6.27: which layers a rebuilt scene hides, and where that answer comes from.
///
/// From the bench: *"Open the 'Arduino Mega 2560' project from the recent list. Then, file -> open
/// recent -> Millburn_Test_Board Workflow one. When this project opens, notice that all of the
/// layers are checked visible. Click preview. Now, most of the checked layers have unchecked
/// themselves."*
///
/// Visibility is how the operator checks alignment before cutting anything, so a tick that changes
/// itself when an unrelated button is pressed costs the application the one thing it is for. The
/// bug was an empty set of hidden rows being read as "nobody has an opinion yet", which is a
/// different statement from "every row is showing" — and the saved view state was consulted for the
/// second when only the first should have reached it.
///
/// **The saved names here are layers that start *visible*, and that is deliberate.** A scene is not
/// built all-visible: `LayerRoleInfo.VisibleByDefault` starts both masks, the bottom silkscreen,
/// the pastes and the drawings switched off. The first draft of these tests saved `F_Mask` and
/// `B_Silkscreen` and then asserted they were hidden — which the scene builder had already done on
/// its own, so the assertion held whether or not the code under test ran at all. Copper and the
/// outline start on, so hiding them is a change only this rule can have made.
/// </summary>
public sealed class LayerVisibilityTests
{
    private static readonly string[] Saved = ["PogoTest1-F_Cu.gbr", "PogoTest1-Edge_Cuts.gbr"];

    private static BoardScene Scene()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        return BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            board.Bounds);
    }

    private static BoardSceneLayer Layer(BoardScene scene, string id) =>
        scene.Layers.Single(l => string.Equals(l.Id, id, StringComparison.Ordinal));

    /// <summary>The scene builder's own defaults, which every test below has to reckon with.</summary>
    [Fact]
    public void ASceneIsNotBuiltWithEveryLayerVisible()
    {
        using var scene = Scene();

        Assert.True(Layer(scene, "PogoTest1-F_Cu.gbr").Visible);
        Assert.True(Layer(scene, "PogoTest1-Edge_Cuts.gbr").Visible);

        // Off from the start, by role. An assertion that one of these is hidden says nothing about
        // whether anything hid it.
        Assert.False(Layer(scene, "PogoTest1-F_Mask.gbr").Visible);
        Assert.False(Layer(scene, "PogoTest1-B_Silkscreen.gbr").Visible);
    }

    /// <summary>What the operator has hidden in the window is the live answer.</summary>
    [Fact]
    public void WhatIsHiddenOnScreenWins()
    {
        var onScreen = new HashSet<string>(["PogoTest1-B_Cu.gbr"], StringComparer.Ordinal);

        var hidden = LayerVisibility.Hidden(onScreen, Saved);

        Assert.NotNull(hidden);
        Assert.Equal(["PogoTest1-B_Cu.gbr"], hidden.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Nothing hidden on screen is an answer, and it is "show everything".
    ///
    /// This is the half of 6.27 that bit inside a single project: reveal every layer, change any
    /// setting, and the rebuild fell back to the saved state and hid them again.
    /// </summary>
    [Fact]
    public void NothingHiddenOnScreenIsAnAnswerRatherThanNoAnswer()
    {
        var hidden = LayerVisibility.Hidden(new HashSet<string>(StringComparer.Ordinal), Saved);

        Assert.NotNull(hidden);
        Assert.Empty(hidden);
    }

    /// <summary>
    /// With nobody to ask — the first build of a project, or the build after a different project
    /// replaced this one — the saved state is what applies.
    /// </summary>
    [Fact]
    public void WithNobodyToAskTheSavedStateApplies()
    {
        var hidden = LayerVisibility.Hidden(onScreen: null, Saved);

        Assert.NotNull(hidden);
        Assert.Equal(Saved.Order(StringComparer.Ordinal), hidden.Order(StringComparer.Ordinal));
    }

    /// <summary>Nobody to ask and nothing saved leaves the scene exactly as it was built.</summary>
    [Fact]
    public void WithNobodyToAskAndNothingSavedTheSceneIsLeftAlone() =>
        Assert.Null(LayerVisibility.Hidden(onScreen: null, []));

    /// <summary>
    /// The two halves together, on a real scene, in the order the bench hit them.
    ///
    /// A project is opened over another one. The rows on screen belong to the project being
    /// replaced, so there is nobody to ask about this one and its saved state applies — which is
    /// what the old code got wrong in the other direction, showing every layer. Then Preview
    /// rebuilds with the rows this project really has, and nothing may move.
    /// </summary>
    [Fact]
    public void OpeningAProjectOverAnotherAndThenPreviewingChangesNoTicks()
    {
        using var opened = Scene();

        // The open: the window's rows are the previous project's, so there is nobody to ask.
        LayerVisibility.Apply(opened, LayerVisibility.Hidden(onScreen: null, Saved));

        // Both of these were on until this ran, so the rule really did something.
        Assert.False(Layer(opened, "PogoTest1-F_Cu.gbr").Visible);
        Assert.False(Layer(opened, "PogoTest1-Edge_Cuts.gbr").Visible);
        Assert.True(Layer(opened, "PogoTest1-B_Cu.gbr").Visible);

        // Now the operator hides one more and reveals one of the saved two. The on-screen set has
        // to differ from the saved set, or the assertions at the end cannot tell which of the two
        // the rule consulted — both would name the same layers.
        Layer(opened, "PogoTest1-B_Cu.gbr").Visible = false;
        Layer(opened, "PogoTest1-Edge_Cuts.gbr").Visible = true;

        var afterOpening = opened.Layers.ToDictionary(l => l.Id, l => l.Visible, StringComparer.Ordinal);

        // Preview: same project, and now the rows on screen are its own.
        using var previewed = Scene();
        Assert.Equal(opened.Layers.Count, previewed.Layers.Count);

        var onScreen = afterOpening.Where(e => !e.Value).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        LayerVisibility.Apply(previewed, LayerVisibility.Hidden(onScreen, Saved));

        foreach (var layer in previewed.Layers)
        {
            Assert.Equal(afterOpening[layer.Id], layer.Visible);
        }

        // Said plainly, because the loop above is only a comparison against a snapshot: the layer
        // the file remembers as hidden is showing, and one it says nothing about is hidden.
        Assert.True(Layer(previewed, "PogoTest1-Edge_Cuts.gbr").Visible);
        Assert.False(Layer(previewed, "PogoTest1-B_Cu.gbr").Visible);
    }

    /// <summary>
    /// And the same again after the operator reveals everything: the next rebuild leaves it
    /// revealed.
    ///
    /// This is the assertion that fails against the old rule, which counted the hidden rows, found
    /// none, and re-applied the ones the file remembered.
    /// </summary>
    [Fact]
    public void RevealingEveryLayerSurvivesTheNextRebuild()
    {
        using var scene = Scene();
        Assert.NotEmpty(scene.Layers);

        // Hidden first, and checked on a layer that was on until now — otherwise the reveal at the
        // end is satisfied by a rule that returns null and an Apply that does nothing.
        LayerVisibility.Apply(scene, LayerVisibility.Hidden(onScreen: null, Saved));
        Assert.False(Layer(scene, "PogoTest1-F_Cu.gbr").Visible);

        // Then everything on, which is what the operator just did by hand. This asks for more than
        // undoing the line above: the masks and the bottom silkscreen were never on to begin with.
        LayerVisibility.Apply(scene, LayerVisibility.Hidden(new HashSet<string>(StringComparer.Ordinal), Saved));

        Assert.All(scene.Layers, l => Assert.True(l.Visible));
    }
}
