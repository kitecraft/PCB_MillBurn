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

    /// <summary>
    /// A drill map is named, not shrugged at. Calling it "Unknown" makes an identified thing look
    /// like a failure to identify, and leaves the operator wondering which layer went wrong.
    /// </summary>
    [Fact]
    public void ADrillMapIsNamedRatherThanGuessedOrUnknown()
    {
        // The filename says PTH; the file says drill map. The file wins.
        var (role, guessed) = LayerRoles.Detect(Parse(DrillMap), "Board-PTH-drl_map.gbr");

        Assert.Equal(LayerRole.DrillMap, role);
        Assert.False(guessed, "a file that says what it is has not been guessed at");
        Assert.Equal("Drill map", LayerRoleInfo.Label(role));
    }

    /// <summary>A drawing is off by default and cannot be cut. It is for reading.</summary>
    [Theory]
    [InlineData(LayerRole.DrillMap)]
    [InlineData(LayerRole.Documentation)]
    public void ADrawingIsNotDrawnAndNotCut(LayerRole role)
    {
        Assert.False(LayerRoleInfo.VisibleByDefault(role));
        Assert.Equal([OutputKind.None], LayerOperations.Available(role));
        Assert.False(LayerRoleInfo.IsDrill(role));
        Assert.False(LayerRoleInfo.IsCopper(role));
    }

    [Fact]
    public void OtherDrawingsAreDocumentationRatherThanUnknown()
    {
        foreach (var function in new[] { "FabricationDrawing", "AssemblyDrawing", "ArrayDrawing" })
        {
            Assert.Equal(LayerRole.Documentation, LayerRoles.FromFileFunction(function));
        }
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

    // ------------------------------------------------------------------ which parser reads it

    private const string Excellon = """
        M48
        METRIC,TZ
        T1C1.000
        %
        G90
        T1
        X10.0Y10.0
        M30
        """;

    /// <summary>The same three holes and two sizes as <see cref="DrillGerber"/>, in Excellon.</summary>
    private const string SameHolesAsExcellon = """
        M48
        METRIC,TZ
        T1C1.000
        T2C1.700
        %
        G90
        T1
        X1.0Y1.0
        X3.0Y1.0
        T2
        X5.0Y1.0
        M30
        """;

    /// <summary>An outline, so the board's extents come from the board rather than from the holes.</summary>
    private const string Outline = """
        %TF.FileFunction,Profile,NP*%
        %FSLAX46Y46*%
        %MOMM*%
        %ADD10C,0.100000*%
        D10*
        X0Y0D02*
        X10000000Y0D01*
        X10000000Y10000000D01*
        X0Y10000000D01*
        X0Y0D01*
        M02*
        """;

    /// <summary>
    /// The two drill formats are two spellings of the same holes, so they have to produce the same
    /// program. Checked on the emitted G-code rather than on the parsed holes, because that is the
    /// artefact the machine runs and the only place a difference would actually matter.
    ///
    /// An outline is included because a real board has one. Without it the board's extents come
    /// from the holes themselves, and the two paths derive those differently — a flashed circle
    /// realises as a polygon drawn *around* the true circle, so its bounds sit about half a micron
    /// wide of the Excellon file's exact centre-plus-radius. That is far below anything a machine
    /// can act on, and it is not what this test is about.
    /// </summary>
    [Fact]
    public void TheSameHolesGiveTheSameProgramInEitherFormat()
    {
        static string Program(string fileName, string text)
        {
            var board = BoardLoader.LoadSources(
                "memory",
                [
                    ("Board-Edge_Cuts.gbr", System.Text.Encoding.UTF8.GetBytes(Outline), LayerRole.Outline),
                    (fileName, System.Text.Encoding.UTF8.GetBytes(text), LayerRole.PlatedDrill),
                ]);

            var plan = ExportPlanner.Plan(
                board,
                new Dictionary<string, LayerOutputSettings>(StringComparer.Ordinal)
                {
                    [fileName] = new() { FileName = fileName, Output = OutputKind.Gcode },
                },
                ToolLibrary.Default,
                Nm.FromMillimetres(1.6));

            // The layer's own name appears in the header, and it is the one thing that legitimately
            // differs between two files holding the same holes.
            return Assert.Single(plan.Items).Content.Replace(
                Path.GetFileNameWithoutExtension(fileName), "drill", StringComparison.Ordinal);
        }

        Assert.Equal(
            Program("Board-PTH.drl", SameHolesAsExcellon),
            Program("Board-PTH-drl.gbr", DrillGerber),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A project holds its files in memory, and that path chose the parser from the layer's
    /// <em>role</em> — so an X2 drill file, whose role is a drill role, was handed to the Excellon
    /// parser. It read no holes, warned about a units declaration the file plainly had, and the
    /// drill layers disappeared from a project that opened perfectly well from a folder.
    ///
    /// Same bytes, same role, both ways in: they have to agree.
    /// </summary>
    [Fact]
    public void AProjectReadsAnX2DrillFileTheSameWayAFolderDoes()
    {
        var board = BoardLoader.LoadSources(
            "memory",
            [("Board-PTH-drl.gbr", System.Text.Encoding.UTF8.GetBytes(DrillGerber), LayerRole.PlatedDrill)]);

        var layer = Assert.Single(board.Layers);

        Assert.NotNull(layer.Drill);
        Assert.Equal(3, layer.Drill.Hits.Count);
        Assert.DoesNotContain(layer.Diagnostics, d => d.IsError);
        Assert.Equal(3, layer.ObjectCount);
    }

    /// <summary>And a real Excellon file must still take the Excellon path.</summary>
    [Fact]
    public void AnExcellonDrillFileIsStillReadAsExcellon()
    {
        var board = BoardLoader.LoadSources(
            "memory",
            [("Board-PTH.drl", System.Text.Encoding.UTF8.GetBytes(Excellon), LayerRole.PlatedDrill)]);

        var layer = Assert.Single(board.Layers);

        Assert.NotNull(layer.Drill);
        Assert.Single(layer.Drill.Hits);
    }

    [Fact]
    public void ARealLayerIsNotMistakenForDocumentation() =>
        Assert.False(LayerRoles.DeclaresNonBoardFunction("Copper,L1,Top"));
}
