using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The test board exported a second way: Protel extensions, and X2 switched off in KiCad's dialog.
///
/// Switching X2 off does not remove the attributes. KiCad writes them as `G04 #@! TF.FileFunction`
/// comments instead, and `GerberParser` reads that form — so these layers are still named by what
/// they declare, not by what they are called. No other board in the corpus is written that way,
/// which is the whole reason this one is committed: the comment form was supported and untested.
///
/// See <see cref="LayerNamingTests"/> for the opposite case, a board with no attributes at all.
/// </summary>
public sealed class ProtelExportTests(ITestOutputHelper output)
{
    private const string Protel = "Millburn_Test_Board_Protel";

    private static Board Load(string board) => BoardLoader.LoadFolder(RealBoards.Directory(board));

    /// <summary>
    /// Every Gerber here declares its own function, in comment form, and is read from it.
    ///
    /// `RoleGuessed` is the assertion that matters: false means the file said what it was. If the
    /// comment form ever stopped being parsed, these layers would still come out with the right
    /// roles — their filenames are KiCad's and would be matched — and only this flag would change.
    /// A test on the roles alone would not notice the feature had gone.
    /// </summary>
    [Fact]
    public void TheGerbersAreNamedByWhatTheyDeclareRatherThanByTheirFilenames()
    {
        var board = Load(Protel);

        foreach (var layer in board.Layers)
        {
            output.WriteLine($"{layer.FileName,-42} {layer.Role,-16} guessed: {layer.RoleGuessed}");
        }

        var gerbers = board.Layers
            .Where(l => !l.FileName.EndsWith(".drl", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(gerbers);
        Assert.All(gerbers, l => Assert.False(
            l.RoleGuessed, $"{l.FileName} should have been read from its attributes"));
    }

    /// <summary>
    /// The Protel extensions are opened at all.
    ///
    /// `.gtl` and the rest are a different list in `BoardLoader` from `.gbr`, and a file that is
    /// never opened is not a layer however well its name reads — which is exactly how `.xnc` went
    /// missing.
    /// </summary>
    [Fact]
    public void TheProtelExtensionsAreLoadedAndNotJustRecognised()
    {
        var board = Load(Protel);
        var loaded = board.Layers.Select(l => Path.GetExtension(l.FileName).ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        output.WriteLine(string.Join(", ", loaded.Order(StringComparer.Ordinal)));

        foreach (var extension in (string[])[".gtl", ".gbl", ".gts", ".gbs", ".gto", ".gbo", ".gm1"])
        {
            Assert.Contains(extension, loaded, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Protel merges the drills into one file, and that file carries no attributes at all — so its
    /// role comes from its extension, and every hole in it is read as plated.
    ///
    /// That is a default, not a reading: the file never says which holes are plated, and the board
    /// it came from splits them into two. Nothing is lost geometrically, and for milling the
    /// distinction changes nothing that reaches metal, but it is a guess and this records it as
    /// one. If it ever stops being an acceptable guess, this test is where the decision is written
    /// down.
    /// </summary>
    [Fact]
    public void TheMergedDrillFileIsTheOneLayerReadFromItsName()
    {
        var board = Load(Protel);

        var drill = Assert.Single(board.Layers, l => l.Drill is not null);

        Assert.EndsWith(".drl", drill.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.True(drill.RoleGuessed, "the drill file declares nothing, so its name is all there is");
        Assert.Equal(LayerRole.PlatedDrill, drill.Role);

        // And there is no second drill layer, because the export put them in one file.
        Assert.DoesNotContain(board.Layers, l => l.Role == LayerRole.NonPlatedDrill);

        // Nothing is lost in the merge, which is the part that matters: every hole the original
        // splits across two files is present in this one. Only the label for six of them is gone.
        var original = Load(RealBoards.MillburnTestBoard);

        var split = original.Layers
            .Where(l => l.Role is LayerRole.PlatedDrill or LayerRole.NonPlatedDrill)
            .Sum(l => l.Drill?.Hits.Count ?? 0);

        output.WriteLine($"{drill.FileName}: {drill.Drill!.Hits.Count} hits, against {split} split across two files");

        Assert.Equal(split, drill.Drill.Hits.Count);
        Assert.Equal(2, original.Layers.Count(l => l.Drill is not null));
    }

    /// <summary>
    /// The same design, exported twice with different settings, is the same board.
    ///
    /// This is what makes the fixture worth its bytes rather than merely being another folder: any
    /// difference between these two is the export options and nothing else, so the comparison holds
    /// the reader to the geometry rather than to the container.
    /// </summary>
    [Fact]
    public void TheSameDesignExportedTwiceReadsTheSame()
    {
        var protel = Load(Protel);
        var original = Load(RealBoards.MillburnTestBoard);

        Assert.Equal(original.Bounds, protel.Bounds);

        // Every role the Protel export carries, compared where both boards have exactly one of it.
        // The drills are left to TheMergedDrillFileIsTheOneLayerReadFromItsName: this export puts
        // plated and non-plated in one file, so its drill layer is deliberately not the same shape
        // as the original's and comparing them would be comparing two different questions.
        var compared = 0;

        foreach (var role in protel.Layers.Select(l => l.Role).Distinct())
        {
            if (role is LayerRole.PlatedDrill or LayerRole.NonPlatedDrill)
            {
                continue;
            }

            var here = protel.Layers.Where(l => l.Role == role).ToList();
            var there = original.Layers.Where(l => l.Role == role).ToList();

            if (here.Count != 1 || there.Count != 1)
            {
                continue;
            }

            output.WriteLine(
                $"{role,-16} {here[0].ObjectCount,6} objects vs {there[0].ObjectCount,6}");

            // To the nanometre, and the same number of objects. Not approximately: the two files
            // describe the same design in different containers, and every coordinate survives the
            // trip through both. Anything looser here would let a real difference hide.
            Assert.Equal(there[0].Bounds, here[0].Bounds);
            Assert.Equal(there[0].ObjectCount, here[0].ObjectCount);
            compared++;
        }

        // The loop above skips a role the two boards do not both have exactly one of. That is the
        // right behaviour and a dangerous silence: if roles ever stopped lining up, every iteration
        // would skip, and this test would pass on the board-bounds assertion alone while comparing
        // nothing. Both coppers, both masks, both silks, both pastes and the outline.
        Assert.Equal(9, compared);
    }
}
