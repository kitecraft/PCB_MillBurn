using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Roadmap 6.25's neighbour, 6.28: a board whose files are named the way Altium, Eagle and Olimex
/// name them — `Top`, `Bot`, `Dimension`, `Ln1_Cu` — rather than the way KiCad does.
///
/// The fixture is `tests/boards/TopBotNames`: PogoTest1's own geometry, renamed, and with every X2
/// attribute stripped out. Stripping them is the point. Left in, the file would say what it is and
/// the name would never be consulted, so the test would pass whatever these rules did.
/// </summary>
public sealed class LayerNamingTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ the names themselves

    [Theory]
    // The Top/Bot vocabulary: the copper layer is named by its side alone.
    [InlineData("Top.gbr", LayerRole.TopCopper)]
    [InlineData("Bot.gbr", LayerRole.BottomCopper)]
    [InlineData("Top_Mask.gbr", LayerRole.TopMask)]
    [InlineData("Bot_Mask.gbr", LayerRole.BottomMask)]
    [InlineData("Top_Silk.gbr", LayerRole.TopSilk)]
    [InlineData("Bot_Silk.gbr", LayerRole.BottomSilk)]
    [InlineData("Top_Paste.gbr", LayerRole.TopPaste)]
    [InlineData("Bot_Paste.gbr", LayerRole.BottomPaste)]
    [InlineData("Dimension.gbr", LayerRole.Outline)]
    [InlineData("Drill.xnc", LayerRole.PlatedDrill)]
    [InlineData("Ln1_Cu.gbr", LayerRole.InnerCopper)]
    [InlineData("Ln2_Cu.gbr", LayerRole.InnerCopper)]
    // KiCad's, which went on working.
    [InlineData("PogoTest1-F_Cu.gbr", LayerRole.TopCopper)]
    [InlineData("PogoTest1-B_Cu.gbr", LayerRole.BottomCopper)]
    [InlineData("PogoTest1-F_Mask.gbr", LayerRole.TopMask)]
    [InlineData("PogoTest1-B_Silkscreen.gbr", LayerRole.BottomSilk)]
    [InlineData("PogoTest1-Edge_Cuts.gbr", LayerRole.Outline)]
    [InlineData("PogoTest1-PTH.drl", LayerRole.PlatedDrill)]
    [InlineData("PogoTest1-NPTH.drl", LayerRole.NonPlatedDrill)]
    [InlineData("Millburn_Test_Board-In1_Cu.gbr", LayerRole.InnerCopper)]
    public void ANameIsReadTheSameWhicheverToolWroteIt(string fileName, LayerRole expected) =>
        Assert.Equal(expected, LayerRoles.FromFileName(fileName));

    /// <summary>
    /// The trap this whole change exists to avoid.
    ///
    /// `Top_Mask` contains `Top`. A substring rule taught that a bare `Top` means copper turns every
    /// mask, silk and paste layer into copper — and copper is the layer that gets cut, so that is
    /// the expensive direction to be wrong in. Whole-word matching cannot make the mistake, and
    /// this is the test that would catch it being reintroduced.
    /// </summary>
    [Theory]
    [InlineData("Top_Mask.gbr")]
    [InlineData("Top_Silk.gbr")]
    [InlineData("Top_Paste.gbr")]
    [InlineData("Bot_Mask.gbr")]
    [InlineData("Bot_Silk.gbr")]
    [InlineData("Bot_Paste.gbr")]
    public void ASideWordInsideALongerNameIsNotCopper(string fileName)
    {
        var role = LayerRoles.FromFileName(fileName);

        output.WriteLine($"{fileName} -> {role}");

        Assert.NotEqual(LayerRole.TopCopper, role);
        Assert.NotEqual(LayerRole.BottomCopper, role);
    }

    /// <summary>
    /// A name nobody can read stays unreadable. Widening what can be understood is not the same as
    /// widening what gets guessed — a file whose purpose is unstated must not be cut as something.
    /// </summary>
    [Theory]
    [InlineData("Top_Assembly.gbr")]
    [InlineData("Bot_Courtyard.gbr")]
    [InlineData("PogoTest1-User_Comments.gbr")]
    [InlineData("readme.gbr")]
    public void ANameThatSaysNothingIsStillUnknown(string fileName) =>
        Assert.Equal(LayerRole.Unknown, LayerRoles.FromFileName(fileName));

    /// <summary>
    /// The edges of the rule, every one of them found by running the matcher rather than reading it.
    ///
    /// A review built the old substring matcher alongside the new one and fed both every name it
    /// could think of. These are the names where they disagreed and the new one was wrong — kept
    /// here because each was a plausible reading that would have reached metal.
    /// </summary>
    [Theory]
    // A side word at the end of a longer name is not the copper. These were read as copper, which
    // is a drawing cut into a board.
    [InlineData("Assembly_Top.gbr", LayerRole.Unknown)]
    [InlineData("Panel_Top.gbr", LayerRole.Unknown)]
    [InlineData("MyBoard_Rev_B.gbr", LayerRole.Unknown)]
    // A panelising tool writes the outline as one word. Read as nothing, the board is never cut free.
    [InlineData("PanelOutline.gbr", LayerRole.Outline)]
    [InlineData("BoardProfile.gbr", LayerRole.Outline)]
    [InlineData("Outline.gbr", LayerRole.Outline)]
    // Eagle and some Altium templates put the side after the kind. Read as an inner layer, the top
    // copper of a two-sided board exports nothing at all and says nothing about it.
    [InlineData("Copper_Top.gbr", LayerRole.TopCopper)]
    [InlineData("Copper_Bottom.gbr", LayerRole.BottomCopper)]
    // A mask, silk or paste layer with no side stated is one we cannot place. Guessing "top" etches
    // a bottom layer onto the front of the board and looks entirely normal doing it.
    [InlineData("Soldermask.gbr", LayerRole.Unknown)]
    [InlineData("Legend.gbr", LayerRole.Unknown)]
    [InlineData("Paste.gbr", LayerRole.Unknown)]
    // A chart of the holes is not the holes.
    [InlineData("Drill_Drawing.gbr", LayerRole.DrillMap)]
    [InlineData("PogoTest1-PTH-drl_map.gbr", LayerRole.DrillMap)]
    [InlineData("NC_Drill.gbr", LayerRole.PlatedDrill)]
    // Altium numbers the stack with a bare L. Read as nothing, four files on a six-layer board have
    // no role and nobody is told why.
    [InlineData("Copper_L2.gbr", LayerRole.InnerCopper)]
    [InlineData("L3_Cu.gbr", LayerRole.InnerCopper)]
    // But copper that names neither a face nor a number stays Unknown. Reading it as inner would
    // look tidier and be worse: inner copper is never exported, so it would produce nothing while
    // appearing to have been understood.
    [InlineData("Copper.gbr", LayerRole.Unknown)]
    public void TheEdgesOfTheRuleWereFoundByRunningIt(string fileName, LayerRole expected) =>
        Assert.Equal(expected, LayerRoles.FromFileName(fileName));

    /// <summary>The side nearest the word naming the kind is the side that counts.</summary>
    [Fact]
    public void ABoardNameContainingASideWordDoesNotStealTheAnswer()
    {
        // "Rev_F" is part of the board's name; the layer is bottom copper.
        Assert.Equal(LayerRole.BottomCopper, LayerRoles.FromFileName("Rev_F-B_Cu.gbr"));
        Assert.Equal(LayerRole.TopCopper, LayerRoles.FromFileName("Rev_B-F_Cu.gbr"));
    }

    // ------------------------------------------------------------------ the whole folder

    /// <summary>
    /// The renamed board loads with every layer named, and names them the same as the board it was
    /// copied from.
    ///
    /// Comparing the two folders is the assertion worth having: it is the same copper, the same
    /// mask and the same holes, so any difference is the naming and nothing else.
    /// </summary>
    [Fact]
    public void TheRenamedBoardReadsAsTheOneItWasCopiedFrom()
    {
        var renamed = BoardLoader.LoadFolder(RealBoards.Directory("TopBotNames"));
        var original = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        foreach (var layer in renamed.Layers)
        {
            output.WriteLine($"{layer.FileName,-16} {layer.Role}");
        }

        Assert.DoesNotContain(renamed.Layers, l => l.Role == LayerRole.Unknown);

        // Every role the original has, except the non-plated drill, which the Top/Bot convention
        // writes into the one drill file and which the fixture therefore does not carry.
        foreach (var role in original.Layers
            .Select(l => l.Role)
            .Where(r => r != LayerRole.NonPlatedDrill)
            .Distinct())
        {
            Assert.Contains(renamed.Layers, l => l.Role == role);
        }

        // And the inner layer, which the original two-layer board has no equivalent of.
        Assert.Contains(renamed.Layers, l => l.Role == LayerRole.InnerCopper);
    }

    /// <summary>
    /// The fixture must keep forcing the filename path.
    ///
    /// If a file-function attribute ever creeps back into those files — a regenerated export, a
    /// helpful tidy-up — the role would be read from the file and every test above would go on
    /// passing while guarding nothing at all.
    ///
    /// Only <c>TF.FileFunction</c> was taken out, not every X2 attribute: the others say when the
    /// file was made and by what, and role detection never reads them. The count below is asserted
    /// because a loop over an empty folder passes having checked nothing, which is the same kind of
    /// silence this test exists to break.
    /// </summary>
    [Fact]
    public void TheFixtureStillHasNothingButItsNamesToGoOn()
    {
        var folder = RealBoards.Directory("TopBotNames");
        var files = 0;

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var text = File.ReadAllText(file);

            Assert.DoesNotContain("TF.FileFunction", text, StringComparison.Ordinal);
            files++;
        }

        Assert.Equal(9, files);
    }
}
