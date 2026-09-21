using MillBurn.Core;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// How the layer panel arranges itself.
///
/// This is a list a person reads, and it used to be in paint order, which is a list a renderer
/// reads. The two disagree in ways that matter: paint order opens with the bottom silkscreen, sets
/// the two sides' soldermasks four rows apart, and puts the side you are working on in the middle.
/// </summary>
public sealed class PanelOrderTests(ITestOutputHelper output)
{
    private static IReadOnlyList<LayerRole> Everything =>
        [.. Enum.GetValues<LayerRole>()];

    /// <summary>Sorted the way the panel sorts, so a test can read the whole list at once.</summary>
    private static List<LayerRole> Sorted(IEnumerable<LayerRole> roles) =>
        [.. roles
            .OrderBy(r => LayerRoleInfo.PanelGroup(r).Order)
            .ThenBy(r => LayerRoleInfo.PanelOrder(r))
            .ThenBy(r => LayerRoleInfo.Label(r), StringComparer.Ordinal)];

    [Fact]
    public void TheTopSideComesFirstAndTheTwoSidesAreNeverInterleaved()
    {
        var order = Sorted(Everything);

        output.WriteLine(string.Join(
            "\n", order.Select(r => $"{LayerRoleInfo.PanelGroup(r).Title,-18} {LayerRoleInfo.Label(r)}")));

        var titles = order.Select(r => LayerRoleInfo.PanelGroup(r).Title).ToList();

        // Each group is one contiguous run: a title never comes back after another has started.
        Assert.Equal(titles.Distinct(StringComparer.Ordinal).Count(), CountRuns(titles));

        Assert.Equal(
            ["Top side", "Inner layers", "Bottom side", "Holes and outline", "Other files"],
            titles.Distinct(StringComparer.Ordinal));
    }

    private static int CountRuns(List<string> titles) =>
        titles.Count == 0
            ? 0
            : 1 + titles.Zip(titles.Skip(1)).Count(p => !string.Equals(p.First, p.Second, StringComparison.Ordinal));

    /// <summary>
    /// The copper is what gets cut, so it heads its side. On the old ordering the top copper sat
    /// under the bottom side's four layers.
    /// </summary>
    [Theory]
    [InlineData(LayerRole.TopCopper, LayerRole.TopMask)]
    [InlineData(LayerRole.TopCopper, LayerRole.TopSilk)]
    [InlineData(LayerRole.BottomCopper, LayerRole.BottomMask)]
    [InlineData(LayerRole.PlatedDrill, LayerRole.Outline)]
    public void TheCopperHeadsItsGroup(LayerRole first, LayerRole second)
    {
        Assert.Equal(LayerRoleInfo.PanelGroup(first).Title, LayerRoleInfo.PanelGroup(second).Title);
        Assert.True(LayerRoleInfo.PanelOrder(first) < LayerRoleInfo.PanelOrder(second));
    }

    /// <summary>
    /// The middle layers get their own heading rather than being filed with one of the two sides,
    /// which is what "the layer between them" means.
    /// </summary>
    [Fact]
    public void InnerCopperIsItsOwnGroup()
    {
        Assert.Equal("Inner layers", LayerRoleInfo.PanelGroup(LayerRole.InnerCopper).Title);

        Assert.True(
            LayerRoleInfo.PanelGroup(LayerRole.TopCopper).Order
            < LayerRoleInfo.PanelGroup(LayerRole.InnerCopper).Order);

        Assert.True(
            LayerRoleInfo.PanelGroup(LayerRole.InnerCopper).Order
            < LayerRoleInfo.PanelGroup(LayerRole.BottomCopper).Order);
    }

    /// <summary>Documentation is not a side of the board and must not be filed as one.</summary>
    [Theory]
    [InlineData(LayerRole.DrillMap)]
    [InlineData(LayerRole.Documentation)]
    [InlineData(LayerRole.Unknown)]
    public void DrawingsAreFiledLast(LayerRole role) =>
        Assert.Equal((4, "Other files"), LayerRoleInfo.PanelGroup(role));

    /// <summary>
    /// The panel order is a different question from the paint order, and both still have to be
    /// answered — the renderer needs copper drawn under silk whatever the list looks like.
    /// </summary>
    [Fact]
    public void PaintOrderIsUntouched()
    {
        Assert.True(
            LayerRoleInfo.DrawOrder(LayerRole.BottomCopper) < LayerRoleInfo.DrawOrder(LayerRole.TopCopper));

        Assert.True(
            LayerRoleInfo.DrawOrder(LayerRole.TopCopper) < LayerRoleInfo.DrawOrder(LayerRole.TopSilk));
    }
}
