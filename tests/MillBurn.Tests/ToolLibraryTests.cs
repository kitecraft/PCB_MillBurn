using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Pipeline;

namespace MillBurn.Tests;

/// <summary>
/// The saved tool library, and choosing a tool per operation.
///
/// Traces and edge cuts want genuinely different tools — that is physics, not preference — so the
/// selection is per operation, and the interesting tests are the ones that catch a tool being asked
/// to do something it cannot.
/// </summary>
public sealed class ToolLibraryTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "millburn-tools-" + Guid.NewGuid().ToString("N"));

    public ToolLibraryTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

    private string Scratch(string name) => Path.Combine(_scratch, name);

    private static long Mm(double mm) => Nm.FromMillimetres(mm);

    // ------------------------------------------------------------------ the library

    [Fact]
    public void TheBuiltInSetCoversWhatABoardNeeds()
    {
        var library = ToolLibrary.Default;

        Assert.Contains(library.OfKind(ToolKind.VBit), t => t.IncludedAngleDegrees == 30);
        Assert.Contains(library.OfKind(ToolKind.EndMill), t => t.DiameterNm == Mm(1.0));
        Assert.NotEmpty(library.OfKind(ToolKind.Drill));
    }

    [Fact]
    public void ALibrarySurvivesASaveAndReload()
    {
        var path = Scratch("tools.json");
        var custom = new Tool
        {
            Name = "20° fine",
            Kind = ToolKind.VBit,
            TipNm = Mm(0.05),
            IncludedAngleDegrees = 20,
            MaxDepthNm = Mm(1.0),
            Notes = "For 0.15 mm traces",
        };

        ToolLibrary.Default.With(custom).Save(path);

        var reloaded = ToolLibrary.LoadOrDefault(path).Find("20° fine");

        Assert.NotNull(reloaded);
        Assert.Equal(custom.Id, reloaded!.Id);
        Assert.Equal(Mm(0.05), reloaded.TipNm);
        Assert.Equal(20, reloaded.IncludedAngleDegrees);
        Assert.Equal("For 0.15 mm traces", reloaded.Notes);
    }

    /// <summary>Editing keeps the id, so a project cut with the tool can still be matched to it.</summary>
    [Fact]
    public void EditingReplacesRatherThanAdds()
    {
        var edited = Tool.DefaultVBit with { TipNm = Mm(0.15) };
        var library = ToolLibrary.Default.With(edited);

        Assert.Equal(ToolLibrary.Default.Tools.Length, library.Tools.Length);
        Assert.Equal(Mm(0.15), library.Find(Tool.DefaultVBit.Id.ToString())!.TipNm);
    }

    /// <summary>A corrupt library must not stop the app opening, and must not be overwritten.</summary>
    [Fact]
    public void ACorruptLibraryFallsBackToTheBuiltInsWithoutDestroyingIt()
    {
        var path = Scratch("broken.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.NotEmpty(ToolLibrary.LoadOrDefault(path).Tools);
        Assert.Equal("{ this is not json", File.ReadAllText(path));
    }

    [Fact]
    public void ToolsAreFoundByIdOrByName()
    {
        var library = ToolLibrary.Default;

        Assert.Equal(Tool.DefaultVBit.Id, library.Find(Tool.DefaultVBit.Id.ToString())!.Id);
        Assert.Equal(Tool.DefaultVBit.Id, library.Find("30° V-bit, 0.1 mm tip")!.Id);

        // Partial names are what people actually type.
        Assert.Equal(Tool.DefaultOutlineMill.Id, library.Find("1.0 mm")!.Id);
        Assert.Null(library.Find("no such bit"));
    }

    // ------------------------------------------------------------------ the shank limit

    /// <summary>
    /// An engraving bit is a cone ground onto a straight shank, so past that point the cut simply
    /// stops widening. Without the limit the model happily reports widths the bit cannot reach.
    /// </summary>
    [Fact]
    public void TheConeStopsWideningAtTheShank()
    {
        var tool = Tool.DefaultVBit with { MaxDepthNm = Mm(0.5) };

        var atLimit = tool.WidthAtDepth(Mm(0.5));

        Assert.Equal(atLimit, tool.WidthAtDepth(Mm(1.0)));
        Assert.Equal(atLimit, tool.WidthAtDepth(Mm(5.0)));
        Assert.True(tool.IsAtFullWidth(Mm(0.5)));
    }

    /// <summary>
    /// Returning a depth the bit physically cannot go to is worse than refusing: the operator finds
    /// out by plunging a 3 mm shank into the board.
    /// </summary>
    [Fact]
    public void AWidthBeyondTheConeIsRefused()
    {
        var tool = Tool.DefaultVBit with { MaxDepthNm = Mm(0.5) };

        Assert.True(tool.DepthForWidth(Mm(0.2)) > 0);
        Assert.Equal(-1, tool.DepthForWidth(Mm(2.0)));
    }

    // ------------------------------------------------------------------ per-operation selection

    [Fact]
    public void EachOperationUsesItsOwnTool()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var outlineMill = ToolLibrary.Default.Find("2.0 mm")!;

        var job = JobBuilder.Build(board, new MillOptions
        {
            Tools = new ToolSelection { Isolation = Tool.DefaultVBit, Outline = outlineMill },
        });

        var isolation = job.Toolpaths.Single(t => t.Kind == ToolpathKind.Isolation);
        var outline = job.Toolpaths.Single(t => t.Kind == ToolpathKind.Outline);

        Assert.Equal(ToolKind.VBit, isolation.Tool.Kind);
        Assert.Equal(outlineMill.Id, outline.Tool.Id);
    }

    /// <summary>The stepdown belongs to the tool, so a fragile mill takes lighter passes.</summary>
    [Fact]
    public void TheOutlineToolsStepdownDecidesThePassCount()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        int Passes(string toolName) => JobBuilder
            .Build(board, new MillOptions { Tools = new ToolSelection { Outline = ToolLibrary.Default.Find(toolName)! } })
            .Toolpaths.Single(t => t.Kind == ToolpathKind.Outline)
            .Passes.Select(p => p.DepthNm).Distinct().Count();

        // 0.3 mm per pass against 0.8 mm per pass, over the same total depth.
        Assert.True(Passes("0.8 mm") > Passes("2.0 mm"));
    }

    [Fact]
    public void DrillsInheritFeedsFromTheirTemplate()
    {
        var template = Tool.DefaultDrill with { PlungeMmPerMin = 42, SpindleRpm = 9_000 };
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var job = JobBuilder.Build(board, new MillOptions { Tools = new ToolSelection { Drill = template } });
        var drill = job.Toolpaths.First(t => t.Kind == ToolpathKind.Drill);

        Assert.Equal(42, drill.Tool.PlungeMmPerMin);
        Assert.Equal(9_000, drill.Tool.SpindleRpm);

        // The diameter still comes from the file, not the template.
        Assert.NotEqual(template.DiameterNm, drill.Tool.DiameterNm);
    }

    // ------------------------------------------------------------------ validation

    /// <summary>
    /// Each of these produces a program that looks perfectly reasonable and a board that is ruined.
    /// </summary>
    [Fact]
    public void CuttingTheOutlineWithAVBitIsCalledOut()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var job = JobBuilder.Build(board, new MillOptions
        {
            Tools = new ToolSelection { Outline = Tool.DefaultVBit },
        });

        Assert.Contains(job.Notes, n => n.Contains("flat end mill", StringComparison.Ordinal));
    }

    [Fact]
    public void IsolatingWithAnEndMillIsCalledOut()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var job = JobBuilder.Build(board, new MillOptions
        {
            Tools = new ToolSelection { Isolation = Tool.DefaultOutlineMill },
        });

        Assert.Contains(job.Notes, n => n.Contains("cannot separate", StringComparison.Ordinal));
    }

    [Fact]
    public void GoingDeeperThanTheConeIsCalledOut()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var job = JobBuilder.Build(board, new MillOptions
        {
            Isolation = new IsolationOptions { DepthNm = Mm(2.0) },
        });

        Assert.Contains(job.Notes, n => n.Contains("stops widening", StringComparison.Ordinal));
    }

    [Fact]
    public void SensibleDefaultsRaiseNothing()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var job = JobBuilder.Build(board, new MillOptions());

        // Only the origin note, which is information rather than a warning.
        Assert.All(job.Notes, n => Assert.Contains("lower-left corner", n, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ projects embed their tools

    /// <summary>
    /// A project keeps a copy of what it was cut with, not a pointer into the library. Otherwise
    /// adjusting a tip width to suit a newly bought bit silently changes the toolpaths of every
    /// project that ever used it.
    /// </summary>
    [Fact]
    public void AProjectKeepsTheToolItWasCutWith()
    {
        var folder = RealBoards.Directory(RealBoards.PogoTest1);
        var project = MillBurnProject.FromSources(ProjectFile.ImportFolder(folder), folder);

        var used = Tool.DefaultVBit with { TipNm = Mm(0.08) };
        project.Settings = project.Settings with
        {
            Tools = [used],
            IsolationToolId = used.Id,
        };

        var path = Scratch("board" + ProjectFile.Extension);
        ProjectFile.Save(project, path);

        // The library moves on; the project does not.
        ToolLibrary.Default.With(Tool.DefaultVBit with { TipNm = Mm(0.20) }).Save(Scratch("tools.json"));

        var reopened = ProjectFile.Open(path).Settings;

        Assert.Equal(Mm(0.08), reopened.ToolFor(reopened.IsolationToolId, Tool.DefaultVBit).TipNm);
    }

    [Fact]
    public void AProjectWithNoToolsFallsBackToTheDefaults()
    {
        var settings = new ProjectSettings();

        Assert.Equal(Tool.DefaultVBit.Id, settings.ToolFor(settings.IsolationToolId, Tool.DefaultVBit).Id);
    }
}
