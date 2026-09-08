using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Settings that belong to the person, not to the board.
///
/// The separation is the point being tested: a layer colour must not travel inside a project or
/// change when one is opened, because which colours read well is a fact about the operator's
/// monitor. The rest is making sure a bad settings file cannot stop the app starting.
/// </summary>
public sealed class AppSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "millburn-settings-" + Guid.NewGuid().ToString("N")[..8]);

    private string Path_ => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void SettingsSurviveARoundTrip()
    {
        var settings = new AppSettings { BoardThicknessMm = 0.8, LastExportFolder = @"C:\jobs" }
            .WithColour(LayerRole.TopSilk, "#FF00AA")
            .WithSubstrateColour("#204020")
            .WithRecent(@"C:\jobs\a.millburn");

        settings.Save(Path_);
        var read = AppSettings.LoadOrDefault(Path_);

        Assert.Equal(0.8, read.BoardThicknessMm);
        Assert.Equal(@"C:\jobs", read.LastExportFolder);
        Assert.Equal("#FF00AA", read.LayerColours[LayerRole.TopSilk]);
        Assert.Equal("#204020", read.SubstrateColour);
        Assert.Equal(@"C:\jobs\a.millburn", read.RecentProjects[0]);
    }

    /// <summary>
    /// Losing preferences is an inconvenience; failing to start is not. The file is also left alone
    /// rather than overwritten, so a hand-editing mistake stays recoverable.
    /// </summary>
    [Fact]
    public void AnUnreadableSettingsFileIsIgnoredRatherThanFatal()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, "{ this is not json");

        var read = AppSettings.LoadOrDefault(Path_);

        Assert.Equal(1.6, read.BoardThicknessMm);
        Assert.Empty(read.LayerColours);
        Assert.Equal("{ this is not json", File.ReadAllText(Path_));
    }

    [Fact]
    public void MissingSettingsAreTheDefaults()
    {
        var read = AppSettings.LoadOrDefault(Path_);

        Assert.Equal(1.6, read.BoardThicknessMm);
        Assert.Null(read.SubstrateColour);
        Assert.Empty(read.RecentProjects);
    }

    /// <summary>Opening the same project twice should not put it in the list twice.</summary>
    [Fact]
    public void TheRecentListIsNewestFirstAndHasNoDuplicates()
    {
        var settings = new AppSettings()
            .WithRecent("a.millburn")
            .WithRecent("b.millburn")
            .WithRecent("a.millburn");

        // Compared as an array: ImmutableArray<T>.Equals is reference equality, so Assert.Equal
        // on two of them tests identity rather than contents.
        Assert.Equal(["a.millburn", "b.millburn"], settings.RecentProjects.ToArray());
    }

    [Fact]
    public void TheRecentListIsBounded()
    {
        var settings = new AppSettings();
        for (var i = 0; i < 30; i++)
        {
            settings = settings.WithRecent($"p{i}.millburn");
        }

        Assert.Equal(10, settings.RecentProjects.Length);
        Assert.Equal("p29.millburn", settings.RecentProjects[0]);
    }

    /// <summary>
    /// The substrate is not a role. Keying it to <see cref="LayerRole.Unknown"/> would recolour
    /// every unrecognised file along with it.
    /// </summary>
    [Fact]
    public void TheSubstrateColourIsSeparateFromEveryRole()
    {
        var settings = new AppSettings().WithSubstrateColour("#123456");

        Assert.Empty(settings.LayerColours);
        Assert.False(settings.LayerColours.ContainsKey(LayerRole.Unknown));
    }

    [Fact]
    public void TheWindowPlacementSurvivesARoundTrip()
    {
        var settings = new AppSettings
        {
            Window = new WindowPlacement
            {
                X = -1200, Y = 40, Width = 1440, Height = 900, Maximised = true,
            },
        };

        settings.Save(Path_);
        var read = AppSettings.LoadOrDefault(Path_).Window;

        Assert.NotNull(read);
        Assert.Equal(-1200, read.X);
        Assert.Equal(40, read.Y);
        Assert.Equal(1440, read.Width);
        Assert.Equal(900, read.Height);
        Assert.True(read.Maximised);
    }

    /// <summary>
    /// A negative origin is a second monitor to the left, not corrupt data. Rejecting it would send
    /// everyone with that setup back to the primary screen on every launch.
    /// </summary>
    [Fact]
    public void ANegativeOriginIsAValidPlacement()
    {
        var settings = new AppSettings
        {
            Window = new WindowPlacement { X = -2560, Y = -120, Width = 1280, Height = 800 },
        };

        settings.Save(Path_);

        Assert.Equal(-2560, AppSettings.LoadOrDefault(Path_).Window!.X);
    }

    /// <summary>A settings file written before this field existed must still load.</summary>
    [Fact]
    public void SettingsWrittenBeforeWindowPlacementExistedStillLoad()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, "{ \"SchemaVersion\": 1, \"BoardThicknessMm\": 0.8 }");

        var read = AppSettings.LoadOrDefault(Path_);

        Assert.Equal(0.8, read.BoardThicknessMm);
        Assert.Null(read.Window);
    }

    [Fact]
    public void AColourCanBePutBack()
    {
        var settings = new AppSettings()
            .WithColour(LayerRole.TopCopper, "#FFFFFF")
            .WithoutColour(LayerRole.TopCopper);

        Assert.Empty(settings.LayerColours);
    }
}

/// <summary>
/// The handful of numbers the panel shows about a loaded board.
///
/// They exist to answer "is this the board I meant?" at a glance, so being wrong is worse than
/// being absent: a plausible number nobody can check is how a job gets run against the wrong file.
/// </summary>
public sealed class BoardSummaryTests
{
    private static Board Board(string name) => BoardLoader.LoadFolder(RealBoards.Directory(name));

    [Fact]
    public void TheSizeMatchesTheBoardExtents()
    {
        var board = Board(RealBoards.PogoTest1);
        var facts = BoardSummary.Facts(board);

        Assert.Equal(Nm.ToMillimetres(board.Bounds.Width), facts.WidthMm, 3);
        Assert.Equal(Nm.ToMillimetres(board.Bounds.Height), facts.HeightMm, 3);
        Assert.Equal(board.Layers.Count, facts.Layers);
    }

    /// <summary>Every hit in every drill file, and the range of sizes it needs.</summary>
    [Fact]
    public void HolesAreCountedAcrossEveryDrillFile()
    {
        var board = Board(RealBoards.PogoTest1);
        var facts = BoardSummary.Facts(board);

        var expected = board.Layers.Where(l => l.Drill is not null).Sum(l => l.Drill!.Hits.Count);

        Assert.Equal(expected, facts.Holes);
        Assert.True(facts.SmallestHoleMm <= facts.LargestHoleMm);
        Assert.True(facts.HoleSizes >= 1);
    }

    /// <summary>
    /// Islands, not rings. A ring with negative area is a hole inside another island, and counting
    /// those makes a ground pour look like fifty separate pieces of copper.
    /// </summary>
    [Fact]
    public void CopperIslandsCountPiecesOfCopperNotRings()
    {
        var board = Board(RealBoards.GridStripConnector);
        var copper = board.Layers.First(l => l.Role == LayerRole.TopCopper);
        var facts = BoardSummary.Facts(board);

        // Three named nets on this board, and the outer rings outnumber the holes inside them.
        Assert.Equal(3, facts.CopperIslands);
        Assert.True(facts.CopperIslands <= copper.RingCount);
    }

    [Fact]
    public void CoverageIsCopperAreaOverBoardArea()
    {
        var facts = BoardSummary.Facts(Board(RealBoards.GridStripConnector));

        Assert.Equal(facts.CopperAreaMm2 / facts.AreaMm2, facts.CopperCoverage, 6);
        Assert.InRange(facts.CopperCoverage, 0.0, 1.0);
    }

    /// <summary>A board with no drill file is a board, not an error.</summary>
    [Fact]
    public void ABoardWithNothingToCountReportsZeroRatherThanFailing()
    {
        var facts = BoardSummary.Facts(new Board
        {
            Source = "empty",
            Layers = [],
        });

        Assert.Equal(0, facts.Holes);
        Assert.Equal(0, facts.CopperIslands);
        Assert.Equal(0, facts.CopperCoverage);
    }
}
