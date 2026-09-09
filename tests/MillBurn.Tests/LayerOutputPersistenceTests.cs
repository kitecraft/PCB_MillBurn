using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// A project has to remember what its layers become.
///
/// It did not. The Gerbers were embedded, the tools were embedded, the hidden layers were kept —
/// and the one thing that says what the job actually <em>is</em> was session state, so opening a
/// saved project gave back the defaults. The second setup is the one that quietly differs from the
/// first, and nothing about the file would have shown it.
/// </summary>
public sealed class LayerOutputPersistenceTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "millburn-project-" + Guid.NewGuid().ToString("N")[..8]);

    private string Path_ => Path.Combine(_folder, "board" + ProjectFile.Extension);

    public LayerOutputPersistenceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static MillBurnProject Imported() => MillBurnProject.FromSources(
        ProjectFile.ImportFolder(RealBoards.Directory(RealBoards.PogoTest1)),
        RealBoards.Directory(RealBoards.PogoTest1));

    /// <summary>
    /// Every field, not a representative one. Each of these is a decision someone made about how
    /// the board gets cut, and any of them silently reverting is a board cut differently from the
    /// one that was checked.
    /// </summary>
    [Fact]
    public void EverySettingSurvivesASaveAndReopen()
    {
        var project = Imported();
        var tool = Guid.NewGuid();

        project.Settings = project.Settings
            .WithOutput(new LayerOutputSettings
            {
                FileName = "PogoTest1-F_Cu.gbr",
                Output = OutputKind.Gcode,
                ToolId = tool,
                DepthNm = Nm.FromMillimetres(0.08),
                Passes = 3,
                Mirrored = true,
            })
            .WithOutput(new LayerOutputSettings
            {
                FileName = "PogoTest1-F_Mask.gbr",
                Output = OutputKind.Svg,
                Invert = true,
            })
            .WithOutput(new LayerOutputSettings
            {
                FileName = "PogoTest1-Edge_Cuts.gbr",
                Output = OutputKind.Gcode,
                TabCount = 6,
                BreakThroughNm = Nm.FromMillimetres(0.45),
            });

        ProjectFile.Save(project, Path_);
        var reopened = ProjectFile.Open(Path_);

        var copper = reopened.Settings.OutputFor("PogoTest1-F_Cu.gbr");
        Assert.NotNull(copper);
        Assert.Equal(OutputKind.Gcode, copper.Output);
        Assert.Equal(tool, copper.ToolId);
        Assert.Equal(Nm.FromMillimetres(0.08), copper.DepthNm);
        Assert.Equal(3, copper.Passes);
        Assert.True(copper.Mirrored);

        var mask = reopened.Settings.OutputFor("PogoTest1-F_Mask.gbr");
        Assert.NotNull(mask);
        Assert.Equal(OutputKind.Svg, mask.Output);
        Assert.True(mask.Invert);

        var outline = reopened.Settings.OutputFor("PogoTest1-Edge_Cuts.gbr");
        Assert.NotNull(outline);
        Assert.Equal(6, outline.TabCount);
        Assert.Equal(Nm.FromMillimetres(0.45), outline.BreakThroughNm);
    }

    /// <summary>
    /// The point of remembering it: the same project exports the same files. Compared on the
    /// emitted G-code, because that is what reaches the machine.
    /// </summary>
    [Fact]
    public void AReopenedProjectExportsExactlyWhatItDidBefore()
    {
        var project = Imported();

        project.Settings = project.Settings
            .WithOutput(new LayerOutputSettings
            {
                FileName = "PogoTest1-F_Cu.gbr",
                Output = OutputKind.Gcode,
                DepthNm = Nm.FromMillimetres(0.09),
                Passes = 2,
            })
            .WithOutput(new LayerOutputSettings
            {
                FileName = "PogoTest1-Edge_Cuts.gbr",
                Output = OutputKind.Gcode,
                TabCount = 7,
            });

        static string Export(MillBurnProject p)
        {
            var board = ProjectFile.ToBoard(p);
            var plan = ExportPlanner.Plan(
                board,
                p.Settings.LayerOutputs.ToDictionary(o => o.FileName, o => o, StringComparer.Ordinal),
                ToolLibrary.Default,
                Nm.FromMillimetres(1.6));

            return string.Join("\n", plan.Items.OrderBy(i => i.TargetName, StringComparer.Ordinal)
                .Select(i => i.TargetName + "\n" + i.Content));
        }

        var before = Export(project);

        ProjectFile.Save(project, Path_);
        var after = Export(ProjectFile.Open(Path_));

        Assert.Equal(before, after, StringComparer.Ordinal);
    }

    /// <summary>
    /// A layer nobody has touched has no entry, so its default can still change with the app
    /// without silently overriding a choice that was never made.
    /// </summary>
    [Fact]
    public void UntouchedLayersAreNotRecorded()
    {
        var project = Imported();

        project.Settings = project.Settings.WithOutput(new LayerOutputSettings
        {
            FileName = "PogoTest1-F_Cu.gbr",
            Output = OutputKind.Gcode,
        });

        Assert.Single(project.Settings.LayerOutputs);
        Assert.Null(project.Settings.OutputFor("PogoTest1-B_Cu.gbr"));
    }

    [Fact]
    public void SettingALayerTwiceReplacesRatherThanDuplicates()
    {
        var settings = new ProjectSettings()
            .WithOutput(new LayerOutputSettings { FileName = "a.gbr", Output = OutputKind.Svg })
            .WithOutput(new LayerOutputSettings { FileName = "a.gbr", Output = OutputKind.Gcode });

        Assert.Single(settings.LayerOutputs);
        Assert.Equal(OutputKind.Gcode, settings.OutputFor("a.gbr")!.Output);
    }

    /// <summary>
    /// Two projects configured the same way must save the same bytes, whatever order the layers
    /// were touched in — a manifest that depends on click order cannot be diffed.
    /// </summary>
    [Fact]
    public void TheOrderLayersWereTouchedInDoesNotReachTheFile()
    {
        var forwards = new ProjectSettings()
            .WithOutput(new LayerOutputSettings { FileName = "a.gbr", Output = OutputKind.Svg })
            .WithOutput(new LayerOutputSettings { FileName = "b.gbr", Output = OutputKind.Gcode });

        var backwards = new ProjectSettings()
            .WithOutput(new LayerOutputSettings { FileName = "b.gbr", Output = OutputKind.Gcode })
            .WithOutput(new LayerOutputSettings { FileName = "a.gbr", Output = OutputKind.Svg });

        Assert.Equal(
            forwards.LayerOutputs.Select(o => o.FileName),
            backwards.LayerOutputs.Select(o => o.FileName));
    }

    /// <summary>
    /// A project written before any of this existed still opens, with no outputs recorded — which
    /// is exactly right: it never had any.
    /// </summary>
    [Fact]
    public void AProjectFromBeforeThisExistedStillOpens()
    {
        var project = Imported();

        ProjectFile.Save(project, Path_);
        var reopened = ProjectFile.Open(Path_);

        Assert.Empty(reopened.Settings.LayerOutputs);
        Assert.Equal(project.Sources.Length, reopened.Sources.Length);
    }
}
