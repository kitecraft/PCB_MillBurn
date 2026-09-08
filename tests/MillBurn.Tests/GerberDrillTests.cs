using MillBurn.Core;
using MillBurn.Gerber;
using MillBurn.Gerber.Excellon;
using MillBurn.Gerber.Model;
using MillBurn.Pipeline;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Drill files written as Gerber X2 instead of Excellon.
///
/// KiCad's <em>Generate Drill Files</em> offers this and it is a reasonable thing to pick — the
/// output carries the same attributes as every other layer. What it is not is Excellon, and a
/// pipeline that only reads <c>.drl</c> sees a Gerber full of circles, draws them beautifully, and
/// drills none of them. The board comes off the machine solid, and nothing said so.
/// </summary>
public sealed class GerberDrillTests
{
    /// <summary>A drill file exactly as KiCad 10 writes one: two sizes, four holes.</summary>
    private const string DrillGerber = """
        %TF.FileFunction,Plated,1,2,PTH,Drill*%
        %FSLAX46Y46*%
        %MOMM*%
        %LPD*%
        G01*
        %TA.AperFunction,ComponentDrill*%
        %ADD10C,1.000000*%
        %TD*%
        %TA.AperFunction,ComponentDrill*%
        %ADD11C,1.700000*%
        %TD*%
        D10*
        X1000000Y1000000D03*
        X3000000Y1000000D03*
        D11*
        X5000000Y1000000D03*
        M02*
        """;

    private const string DrillMap = """
        %TF.FileFunction,Drillmap*%
        %FSLAX46Y46*%
        %MOMM*%
        %ADD10C,0.100000*%
        D10*
        X1000000Y1000000D03*
        M02*
        """;

    private static GerberImage Parse(string text) => GerberParser.Parse(text);

    // ------------------------------------------------------------------ the holes

    [Fact]
    public void HolesComeOutOfADrillFunctionGerber()
    {
        var drill = GerberDrills.From(Parse(DrillGerber), HolePlating.Plated);

        Assert.NotNull(drill);
        Assert.Equal(3, drill.Hits.Count);
        Assert.Equal(2, drill.Tools.Count);
        Assert.Equal(HolePlating.Plated, drill.Plating);
    }

    /// <summary>
    /// The diameter is the aperture's own parameter, not a measurement taken back off the polygon
    /// it was drawn as. A circle is realised as a many-sided polygon, so measuring the drawing
    /// would give a number that is close and never exact.
    /// </summary>
    [Fact]
    public void DiametersAreExactRatherThanMeasuredOffTheDrawing()
    {
        var drill = GerberDrills.From(Parse(DrillGerber), HolePlating.Plated)!;
        var sizes = drill.Tools.Values.Select(t => t.DiameterNm).Order().ToList();

        Assert.Equal(Nm.FromMillimetres(1.0), sizes[0]);
        Assert.Equal(Nm.FromMillimetres(1.7), sizes[1]);
    }

    [Fact]
    public void HolesLandWhereTheFlashesAre()
    {
        var drill = GerberDrills.From(Parse(DrillGerber), HolePlating.Plated)!;

        Assert.Contains(drill.Hits, h => h.At == new Point2(Nm.FromMillimetres(1), Nm.FromMillimetres(1)));
        Assert.Contains(drill.Hits, h => h.At == new Point2(Nm.FromMillimetres(5), Nm.FromMillimetres(1)));
    }

    [Fact]
    public void AGerberWithNoFlashesIsNotADrillFile()
    {
        Assert.Null(GerberDrills.From(Parse("""
            %FSLAX46Y46*%
            %MOMM*%
            %ADD10C,0.100000*%
            D10*
            X0Y0D02*
            X1000000Y0D01*
            M02*
            """), HolePlating.Plated));
    }

    // ------------------------------------------------------------------ what it is not

    /// <summary>
    /// A drill map is a chart <em>of</em> the holes: symbols and text telling an operator where
    /// they go. Reading it as holes puts several hundred of them through the legend.
    /// </summary>
    [Theory]
    [InlineData("Plated,1,2,PTH,Drill", true)]
    [InlineData("NonPlated,1,2,NPTH,Drill", true)]
    [InlineData("Drillmap", false)]
    [InlineData("Copper,L1,Top", false)]
    [InlineData(null, false)]
    public void OnlyRealDrillFunctionsCount(string? fileFunction, bool expected) =>
        Assert.Equal(expected, GerberDrills.IsDrillFunction(fileFunction));

    [Fact]
    public void ADrillMapIsNeverGuessedIntoADrillLayer()
    {
        // The filename says PTH; the file says drill map. The file wins.
        var (role, guessed) = LayerRoles.Detect(Parse(DrillMap), "Board-PTH-drl_map.gbr");

        Assert.Equal(LayerRole.Unknown, role);
        Assert.False(guessed, "a file that says what it is has not been guessed at");
    }

    [Fact]
    public void ADrillFunctionGerberIsRecognisedWithoutTheFilename()
    {
        var (plated, platedGuessed) = LayerRoles.Detect(Parse(DrillGerber), "anything.gbr");

        Assert.Equal(LayerRole.PlatedDrill, plated);
        Assert.False(platedGuessed);

        var np = DrillGerber.Replace(
            "Plated,1,2,PTH,Drill", "NonPlated,1,2,NPTH,Drill", StringComparison.Ordinal);

        Assert.Equal(LayerRole.NonPlatedDrill, LayerRoles.Detect(Parse(np), "anything.gbr").Role);
    }

    [Theory]
    [InlineData("Drillmap")]
    [InlineData("FabricationDrawing")]
    [InlineData("AssemblyDrawing")]
    public void DocumentationLayersAreKnownNotGuessed(string function) =>
        Assert.True(LayerRoles.DeclaresNonBoardFunction(function));

    [Fact]
    public void ARealLayerIsNotMistakenForDocumentation() =>
        Assert.False(LayerRoles.DeclaresNonBoardFunction("Copper,L1,Top"));
}
