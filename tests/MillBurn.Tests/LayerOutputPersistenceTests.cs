using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Pipeline;

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
                IsolationWidthNm = Nm.FromMillimetres(0.4),
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
                TabWidthNm = Nm.FromMillimetres(2.5),
                TabHeightNm = Nm.FromMillimetres(0.35),

                // One tab set by hand, sparsely: the shape the tabs window writes, and the one that
                // exercises a record with a required member through the serialiser.
                TabEdits = [new TabEdit { Index = 2, WidthNm = Nm.FromMillimetres(4.0) }],
                BreakThroughNm = Nm.FromMillimetres(0.45),

                // Off, and off is the interesting direction: a bool that failed to round-trip would
                // come back at its default, which is on.
                WriteDrillGuide = false,
            });

        ProjectFile.Save(project, Path_);
        var reopened = ProjectFile.Open(Path_);

        var copper = reopened.Settings.OutputFor("PogoTest1-F_Cu.gbr");
        Assert.NotNull(copper);
        Assert.Equal(OutputKind.Gcode, copper.Output);
        Assert.Equal(tool, copper.ToolId);
        Assert.Equal(Nm.FromMillimetres(0.08), copper.DepthNm);
        Assert.Equal(3, copper.Passes);
        Assert.Equal(Nm.FromMillimetres(0.4), copper.IsolationWidthNm);
        Assert.True(copper.Mirrored);

        var mask = reopened.Settings.OutputFor("PogoTest1-F_Mask.gbr");
        Assert.NotNull(mask);
        Assert.Equal(OutputKind.Svg, mask.Output);
        Assert.True(mask.Invert);

        var outline = reopened.Settings.OutputFor("PogoTest1-Edge_Cuts.gbr");
        Assert.NotNull(outline);
        Assert.Equal(6, outline.TabCount);
        Assert.Equal(Nm.FromMillimetres(0.45), outline.BreakThroughNm);
        Assert.False(outline.WriteDrillGuide);
        Assert.Equal(Nm.FromMillimetres(2.5), outline.TabWidthNm);
        Assert.Equal(Nm.FromMillimetres(0.35), outline.TabHeightNm);

        // The edit, whole: which tab it names, the size it sets, and the size it leaves alone. A
        // serialiser that dropped the null would be indistinguishable from one that dropped the
        // edit until a board came out with the wrong web under one tab.
        var edit = Assert.Single(outline.TabEdits);
        Assert.Equal(2, edit.Index);
        Assert.Equal(Nm.FromMillimetres(4.0), edit.WidthNm);
        Assert.Null(edit.HeightNm);
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
    /// Two projects configured the same way save the same manifest, whatever order the layers were
    /// touched in — a file that depends on click order cannot be diffed.
    ///
    /// **It used to prove that in memory and not in the file**, which a `/describe-test` pass pointed
    /// out: it compared two `ProjectSettings` built in the two orders and never saved anything, under
    /// a name and a comment that both talked about the file. Two empty collections would have
    /// satisfied it as happily. It now writes both projects and compares the manifests that come out.
    /// </summary>
    [Fact]
    public void TheOrderLayersWereTouchedInDoesNotReachTheFile()
    {
        var forwards = Imported();
        forwards.Settings = forwards.Settings
            .WithOutput(new LayerOutputSettings { FileName = "PogoTest1-F_Cu.gbr", Output = OutputKind.Gcode })
            .WithOutput(new LayerOutputSettings { FileName = "PogoTest1-F_Mask.gbr", Output = OutputKind.Svg });

        var backwards = Imported();
        backwards.Settings = backwards.Settings
            .WithOutput(new LayerOutputSettings { FileName = "PogoTest1-F_Mask.gbr", Output = OutputKind.Svg })
            .WithOutput(new LayerOutputSettings { FileName = "PogoTest1-F_Cu.gbr", Output = OutputKind.Gcode });

        var first = Path.Combine(_folder, "forwards" + ProjectFile.Extension);
        var second = Path.Combine(_folder, "backwards" + ProjectFile.Extension);

        ProjectFile.Save(forwards, first);
        ProjectFile.Save(backwards, second);

        var a = Manifest(first);
        var b = Manifest(second);

        // Both of them really carry the two layers, so the comparison is of something rather than of
        // two absences.
        Assert.Contains("PogoTest1-F_Cu.gbr", a, StringComparison.Ordinal);
        Assert.Contains("PogoTest1-F_Mask.gbr", a, StringComparison.Ordinal);

        Assert.Equal(a, b, StringComparer.Ordinal);
    }

    /// <summary>
    /// The manifest inside a saved project, as text.
    ///
    /// Found by extension rather than by name: the entry's name is the file format's business, and
    /// there is exactly one JSON entry beside the Gerbers.
    /// </summary>
    private static string Manifest(string path)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);

        var entry = archive.Entries.Single(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
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
