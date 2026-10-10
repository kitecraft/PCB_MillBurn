using MillBurn.Core;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Which tools the library window shows, and in what order.
///
/// **From the workshop, as three requests — filter by tool type, sorting, and copy a tool.** All
/// three are one symptom, recorded in [06 §6.6](../../Documentation/06-Roadmap-and-Risks.md): the
/// list was designed for the six tools it shipped with, and a library grows. It is insertion-ordered,
/// which is not an order.
///
/// The rule lives in Core rather than in the window so that a library of thirty can be arranged
/// here. What the window adds on top is four controls and a copy button.
/// </summary>
public sealed class ToolViewTests(ITestOutputHelper output)
{
    private static Tool Bit(string name, ToolKind kind, double sizeMm) => new()
    {
        Name = name,
        Kind = kind,
        DiameterNm = kind == ToolKind.VBit ? Nm.FromMillimetres(3.175) : Nm.FromMillimetres(sizeMm),
        TipNm = kind == ToolKind.VBit ? Nm.FromMillimetres(sizeMm) : 0,
        IncludedAngleDegrees = kind == ToolKind.VBit ? 30 : 0,
    };

    /// <summary>
    /// Thirty tools, built the way a library grows: added in no order, in three kinds.
    ///
    /// Shank diameters are all 3.175 mm for the V-bits, which is the point of
    /// <see cref="ToolView.SizeOf"/> — sorting on the diameter field would pile every V-bit together
    /// at a number that says nothing about what they cut.
    /// </summary>
    private static List<Tool> Thirty()
    {
        var tools = new List<Tool>();

        for (var i = 0; i < 30; i++)
        {
            var kind = (i % 5) switch
            {
                0 => ToolKind.VBit,
                4 => ToolKind.Drill,
                _ => ToolKind.EndMill,
            };

            // Sizes that do not follow the insertion order, so "sorted" and "as added" cannot be
            // confused. Seven and thirty are coprime, so this walks every tenth of a millimetre from
            // 0.1 to 3.0 exactly once — **all thirty are distinct**, which is what lets the sort be
            // asserted as strictly increasing rather than merely non-decreasing. (An earlier comment
            // here said the sizes repeat. They do not, and a `/describe-test` pass did the
            // arithmetic.)
            var sizeMm = Math.Round(0.1 + (((i * 7) % 30) * 0.1), 3);

            tools.Add(Bit($"Tool {i:00} of {sizeMm:0.0} mm", kind, sizeMm));
        }

        return tools;
    }

    /// <summary>
    /// Thirty tools sorted by size, with a V-bit's tip standing in for its diameter.
    ///
    /// **The Done-when of this story**, and the reason `SizeOf` exists. A V-bit's `DiameterNm` is
    /// its shank — 3.175 mm on every one of them here — so an order taken from that field would put
    /// the six V-bits in a block at 3.175 and tell the operator nothing about what any of them cuts.
    /// </summary>
    [Fact]
    public void ThirtyToolsSortByTheSizeTheyAreKnownBy()
    {
        var shown = ToolView.Arrange(Thirty(), sort: ToolSort.Size);

        foreach (var tool in shown)
        {
            output.WriteLine($"{tool.Kind,-7} {Nm.ToMillimetreString(ToolView.SizeOf(tool), 2)}  {tool.Name}");
        }

        Assert.Equal(30, shown.Count);

        var sizes = shown.Select(ToolView.SizeOf).ToList();

        // **Strictly** increasing, which the fixture's thirty distinct sizes allow: merely
        // non-decreasing is also what a `SizeOf` returning a constant would produce, and a stable
        // sort would then leave the insertion order — V-bits at 0, 5, 10 — and pass the spread check
        // below as well. A `/describe-test` pass found that pair of holes lined up.
        Assert.Equal(sizes.Distinct().Count(), sizes.Count);
        Assert.Equal(sizes.Order(), sizes);

        // And the V-bits are spread through it by their tips rather than gathered at their shank,
        // which is what the sort would do if it read the diameter field.
        var vbits = shown.Select((t, i) => (t, i)).Where(x => x.t.Kind == ToolKind.VBit).Select(x => x.i).ToList();

        output.WriteLine($"V-bits at positions {string.Join(", ", vbits)}");

        Assert.True(vbits.Count > 1, "the fixture has no V-bits to scatter");
        Assert.True(vbits[^1] - vbits[0] > vbits.Count, "the V-bits are bunched, so the sort read the shank");
    }

    /// <summary>
    /// Filtered to end mills, nothing else comes back — and the order within it is untouched.
    ///
    /// The other half of the Done-when. Asserted as *every one of them and only those*, rather than
    /// as a count, so a filter that dropped one as well would not pass.
    /// </summary>
    [Fact]
    public void FilteringToEndMillsShowsEveryEndMillAndNothingElse()
    {
        var all = Thirty();
        var shown = ToolView.Arrange(all, ToolKind.EndMill);

        output.WriteLine($"{shown.Count} of {all.Count}");

        Assert.All(shown, t => Assert.Equal(ToolKind.EndMill, t.Kind));
        Assert.Equal(all.Count(t => t.Kind == ToolKind.EndMill), shown.Count);

        // In the library's own order, because no sort was asked for: a filter is not a sort.
        Assert.Equal(all.Where(t => t.Kind == ToolKind.EndMill).Select(t => t.Id), shown.Select(t => t.Id));
    }

    /// <summary>
    /// The two together, which is how the window is actually used.
    ///
    /// **Filtering then sorting and sorting then filtering are the same list**, and this compares
    /// the two rather than asserting it — which a `/describe-test` pass pointed out the first
    /// version claimed in its own doc while making a single call. They differ only if one of the two
    /// loses something, which is exactly the fault worth catching.
    /// </summary>
    [Fact]
    public void FilteringAndSortingComposeWithoutLosingAnything()
    {
        var all = Thirty();

        var shown = ToolView.Arrange(all, ToolKind.EndMill, ToolSort.Size);
        var otherWayRound = ToolView.Arrange(ToolView.Arrange(all, sort: ToolSort.Size), ToolKind.EndMill);

        var sizes = shown.Select(ToolView.SizeOf).ToList();

        output.WriteLine(string.Join(", ", sizes.Select(s => Nm.ToMillimetreString(s, 2))));

        Assert.Equal(sizes.Order(), sizes);
        Assert.Equal(otherWayRound.Select(t => t.Id), shown.Select(t => t.Id));

        // And *which* eighteen, not merely how many: a filter that swapped one end mill for another
        // would keep the count, the kinds and the order, and nothing here would have noticed.
        Assert.Equal(
            all.Where(t => t.Kind == ToolKind.EndMill).OrderBy(ToolView.SizeOf).Select(t => t.Id),
            shown.Select(t => t.Id));
    }

    /// <summary>
    /// Arranging the view leaves the library exactly as it was.
    ///
    /// **"Sort the view, never the file"**, which is one of this story's three rules: `tools.json`
    /// keeps its order because it is diffable and it is what somebody reads when something has gone
    /// wrong. Nothing here writes, and this is the check that nothing here reorders in place either
    /// — an `OrderBy` on a list is not in place, but a `Sort` would be, and the two read almost the
    /// same.
    /// </summary>
    [Fact]
    public void ArrangingDoesNotTouchTheLibrary()
    {
        var all = Thirty();
        var before = all.Select(t => t.Id).ToList();

        foreach (var sort in Enum.GetValues<ToolSort>())
        {
            foreach (var only in new ToolKind?[] { null, ToolKind.VBit, ToolKind.EndMill, ToolKind.Drill })
            {
                ToolView.Arrange(all, only, sort);
            }
        }

        output.WriteLine($"{before.Count} tools, {Enum.GetValues<ToolSort>().Length * 4} arrangements");

        Assert.Equal(before, all.Select(t => t.Id));
    }

    /// <summary>
    /// The default is the file's own order, unchanged.
    ///
    /// The window opens on it, and a library that reordered itself the moment it was looked at would
    /// be the "change of sort becomes a change of file" this story refuses — one step before the file
    /// is even written.
    /// </summary>
    [Fact]
    public void TheDefaultIsTheLibrarysOwnOrder()
    {
        var all = Thirty();

        Assert.Equal(all.Select(t => t.Id), ToolView.Arrange(all).Select(t => t.Id));
    }

    /// <summary>
    /// A V-bit is known by its tip and everything else by its diameter.
    ///
    /// Stated directly as well as through the sort, because it is the rule the row in the window
    /// prints and the one somebody would reach for if they added a fourth kind.
    /// </summary>
    [Fact]
    public void AVBitIsKnownByItsTipAndEverythingElseByItsDiameter()
    {
        var vbit = Bit("V", ToolKind.VBit, 0.1);
        var mill = Bit("E", ToolKind.EndMill, 0.8);
        var drill = Bit("D", ToolKind.Drill, 1.2);

        Assert.Equal(Nm.FromMillimetres(0.1), ToolView.SizeOf(vbit));
        Assert.Equal(Nm.FromMillimetres(0.8), ToolView.SizeOf(mill));
        Assert.Equal(Nm.FromMillimetres(1.2), ToolView.SizeOf(drill));

        // And it is not the shank, which is the mistake this exists to prevent.
        Assert.NotEqual(vbit.DiameterNm, ToolView.SizeOf(vbit));
    }

    /// <summary>
    /// A copy carries everything except the identity.
    ///
    /// **The first of this story's three non-negotiables.** `Tool.Id` is what a project stores, so
    /// two tools sharing one are the same tool to everything downstream: a job naming the copy would
    /// cut with the original, and nothing anywhere would say so. Everything else is carried, which
    /// is the point of the button — a 0.8 and a 1.0 end mill share their feeds, their stepdown and
    /// their notes, and entering the second from scratch is retyping the first.
    ///
    /// Asserted field by field against the original rather than against remembered values, so a
    /// field added to `Tool` later is covered by the one line that compares the two records.
    /// </summary>
    [Fact]
    public void ACopyCarriesEverythingButTheIdentity()
    {
        var original = Bit("0.8 mm end mill", ToolKind.EndMill, 0.8) with
        {
            FeedMmPerMin = 450,
            PlungeMmPerMin = 90,
            SpindleRpm = 12_000,
            StepdownNm = Nm.FromMillimetres(0.35),
            Flutes = 3,
            FluteLengthNm = Nm.FromMillimetres(4),
            Notes = "The one that likes a slow plunge.",
        };

        var copy = ToolView.Copy(original);

        output.WriteLine($"{original.Id} -> {copy.Id}");
        output.WriteLine($"{original.Name} -> {copy.Name}");

        Assert.NotEqual(original.Id, copy.Id);
        Assert.NotEqual(Guid.Empty, copy.Id);
        Assert.Equal(original.Name + " (copy)", copy.Name);

        // Everything else, in one line: a record differing only in the two fields named above is
        // equal to the original once those two are put back.
        Assert.Equal(original, copy with { Id = original.Id, Name = original.Name });

        // And the feeds in particular, because "without retyping its feeds" is what was asked for.
        Assert.Equal(450, copy.FeedMmPerMin);
        Assert.Equal(90, copy.PlungeMmPerMin);
        Assert.Equal(12_000, copy.SpindleRpm);
        Assert.Equal(3, copy.Flutes);
    }

    /// <summary>
    /// A copy takes the name it is given, trimmed.
    ///
    /// **Asked for from the workshop:** *"when copying a tool, a small dialog for the user to enter
    /// the new name."* The suffixed name is the dialog's proposal rather than the answer now, so the
    /// copy arrives named instead of arriving called "(copy)" and waiting to be noticed.
    ///
    /// A name of nothing falls back to the proposal rather than making a tool with no name — the
    /// dialog will not return one, and the form refuses one, so this is the third guard on the same
    /// thing and the one a caller that is not the dialog would meet.
    /// </summary>
    [Theory]
    [InlineData("0.9 mm end mill", "0.9 mm end mill")]
    [InlineData("  padded  ", "padded")]
    [InlineData("", "1.0 mm end mill (copy)")]
    [InlineData("   ", "1.0 mm end mill (copy)")]
    [InlineData(null, "1.0 mm end mill (copy)")]
    public void ACopyTakesTheNameItIsGiven(string? given, string expected)
    {
        var original = Bit("1.0 mm end mill", ToolKind.EndMill, 1.0);
        var copy = ToolView.Copy(original, given);

        output.WriteLine($"{given ?? "<null>"} -> {copy.Name}");

        Assert.Equal(expected, copy.Name);

        // Whatever it is called, it is still a different tool carrying everything else.
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(original, copy with { Id = original.Id, Name = original.Name });
    }

    /// <summary>
    /// Copying a copy does not collide, which is what makes the button usable twice.
    ///
    /// Two copies of one tool are two tools: same name, different ids. The name is the operator's to
    /// fix — the box is focused and selected for exactly that — but the identity cannot be.
    /// </summary>
    [Fact]
    public void TwoCopiesOfOneToolAreTwoTools()
    {
        var original = Bit("1.0 mm end mill", ToolKind.EndMill, 1.0);

        var first = ToolView.Copy(original);
        var second = ToolView.Copy(original);
        var third = ToolView.Copy(first);

        Guid[] ids = [original.Id, first.Id, second.Id, third.Id];

        output.WriteLine(string.Join(" ", ids));

        Assert.Equal(4, ids.Distinct().Count());
        Assert.Equal("1.0 mm end mill (copy) (copy)", third.Name);
    }

    /// <summary>
    /// Sorting by name is case-insensitive, because the names in a real library are not consistent.
    ///
    /// This machine's own library holds `1mm Corn Endmill` and `1.2 mm end mill`, which is exactly
    /// the shape of the problem: an ordinal sort puts every capital before every lower-case letter,
    /// so the two would not land near each other however alike they are.
    /// </summary>
    [Fact]
    public void SortingByNameIgnoresCase()
    {
        List<Tool> tools =
        [
            Bit("beta", ToolKind.EndMill, 1.0),
            Bit("Alpha", ToolKind.EndMill, 1.0),
            Bit("Gamma", ToolKind.EndMill, 1.0),
            Bit("delta", ToolKind.EndMill, 1.0),
        ];

        var shown = ToolView.Arrange(tools, sort: ToolSort.Name).Select(t => t.Name).ToList();

        output.WriteLine(string.Join(", ", shown));

        Assert.Equal(["Alpha", "beta", "delta", "Gamma"], shown);
    }
}
