using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The two planning paths say the same thing about the same board.
///
/// **They did not.** `ExportPlanner` ran <see cref="ElectricalCheck"/> and named the nets;
/// `JobBuilder`, behind the CLI's `mill` command, ran
/// <see cref="IsolationOperation.UnreachableGaps"/> and printed a bare count. Measured on the
/// Arduino Mega at a matched 0.154 mm cut, before this was fixed:
///
/// <code>
/// mill    19 gap(s) are narrower than the 0.154 mm cut: those copper regions stay connected.
/// export  +5V and M8RXD are left connected: the gap between them is narrower than 0.154 mm …
///         …and 3 more group(s) this cut cannot separate, naming 7 further net(s).
/// </code>
///
/// Nineteen *gaps* against fourteen *groups* — both true, in different units, with nothing on
/// either screen to reconcile them. An operator checking a board one way and then the other had no
/// way to tell whether the two commands were even describing the same copper.
///
/// **This is the test that would have caught it**, and it is written as a comparison rather than
/// as pinned text on purpose: pinning the sentences would have passed just as happily with the two
/// paths saying different pinned things. What it asserts is that they *agree*, which is the
/// property that was broken.
///
/// Recorded in [06 §6.42](../../Documentation/06-Roadmap-and-Risks.md).
/// </summary>
public sealed class PlanningPathsAgreeTests(ITestOutputHelper output)
{
    /// <summary>
    /// The Mega, at the depth that makes the disagreement visible.
    ///
    /// 0.1 mm rather than the 0.05 mm default, because it is what the export path resolves to
    /// through the app's settings and so is the depth at which the two commands were compared by
    /// hand. It is also deep enough that the check has plenty to say: at the default the Mega
    /// reports eight gaps, at this depth nineteen.
    /// </summary>
    private static readonly long DepthNm = Nm.FromMillimetres(0.1);

    private static Board Mega() => BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoMega));

    /// <summary>
    /// Both paths, over one board, at one cut, name the same copper.
    ///
    /// The top copper only, because `mill` plans one side per run and `export` plans both — so the
    /// comparison is against the side `mill` chose, not against everything `export` found.
    /// </summary>
    [Fact]
    public void TheMillPathAndTheExportPathNameTheSameShortedCopper()
    {
        var board = Mega();
        var top = board.Layers.Single(l => l.Role == LayerRole.TopCopper);

        // The same isolation, expressed the way each path expresses it. If these two ever describe
        // different cuts the test is meaningless — so the cut width each arrives at is compared
        // below, before anything else is.
        var settings = board.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
                DepthNm = DepthNm,
            },
            StringComparer.Ordinal);

        var millOptions = new MillOptions
        {
            Isolation = new IsolationOptions { Tool = Tool.DefaultVBit, DepthNm = DepthNm },
            IncludeDrill = false,
            IncludeOutline = false,
        };

        // **The comparison that has to come first**, and it was missing: a `/describe-test` pass
        // over this file pointed out that the comment above promised the cut widths were asserted
        // equal and the body never did it. Two paths agreeing about a board they are cutting
        // differently would be a coincidence, not the property this test is named for.
        var exportCut = LayerOperations
            .DefaultToolFor(OperationKind.Isolation, ToolLibrary.Default.Tools)
            .WidthAtDepth(DepthNm);

        var millCut = millOptions.Tools.Isolation.WidthAtDepth(DepthNm);

        output.WriteLine(
            $"cut widths — export {Nm.ToMillimetreString(exportCut, 3)} mm, "
            + $"mill {Nm.ToMillimetreString(millCut, 3)} mm");

        Assert.Equal(exportCut, millCut);

        var exported = ExportPlanner.Plan(
            board, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        var job = JobBuilder.Build(board, millOptions);

        var fromExport = exported.Items
            .Where(i => i.LayerFileName == top.FileName)
            .SelectMany(i => i.Warnings)
            .Where(c => c.Kind == CheckKind.Electrical)
            .Select(c => c.Line)
            .ToList();

        var fromMill = job.Notes
            .Where(n => n.Contains("left connected", StringComparison.Ordinal)
                || n.Contains("stay connected", StringComparison.Ordinal)
                || n.Contains("cannot separate", StringComparison.Ordinal)
                || n.Contains("not checked", StringComparison.Ordinal))
            .ToList();

        output.WriteLine($"export: {fromExport.Count} line(s)");
        output.WriteLine(string.Join("\n", fromExport));
        output.WriteLine($"mill: {fromMill.Count} line(s)");
        output.WriteLine(string.Join("\n", fromMill));

        // The guard. Both sides being empty would satisfy an equality check perfectly, and this
        // board is here precisely because it has a real short on it.
        Assert.NotEmpty(fromExport);

        Assert.Equal(fromExport, fromMill);
    }

    /// <summary>
    /// A bit the operator has edited is the bit every path plans with.
    ///
    /// **The rule the `mill` command now depends on, and the case that exposed it.** The author's
    /// saved library holds the built-in 30° V-bit with its tip corrected to the 0.127 mm they
    /// actually own. `export` honoured that edit; `mill` synthesised a bit from its own 30°/0.1 mm
    /// defaults and ignored it, so the two commands planned 0.154 mm and 0.127 mm cuts at the same
    /// depth and disagreed about the board.
    ///
    /// `mill` is in `MillBurn.Cli`, which the tests cannot reach — the same boundary as the view
    /// model in 6.50 — so what the CLI chooses is verified by running it and recorded with the
    /// measurement. What is testable, and what makes the fix hold, is that the rule all three
    /// callers share returns the operator's tool rather than the built-in it was made from.
    /// </summary>
    [Fact]
    public void AnEditedBuiltInToolIsWhatTheSharedRuleReturns()
    {
        // Same id as the built-in, different tip: the shape of an operator correcting the shipped
        // entry to the bit in their hand, rather than adding a new one beside it.
        var owned = Tool.DefaultVBit with
        {
            TipNm = Nm.FromMillimetres(0.127),
            Name = "30° V-bit, the one I actually own",
        };

        var library = new[] { owned };

        var chosen = LayerOperations.DefaultToolFor(OperationKind.Isolation, library);

        Assert.Equal(owned.TipNm, chosen.TipNm);
        Assert.NotEqual(Tool.DefaultVBit.TipNm, chosen.TipNm);

        // And the consequence that was actually visible at the machine: a different cut width at
        // the same depth, which is what made the two commands disagree.
        var depth = Nm.FromMillimetres(0.05);

        Assert.NotEqual(
            Tool.DefaultVBit.WidthAtDepth(depth),
            chosen.WidthAtDepth(depth));

        output.WriteLine(
            $"built-in cuts {Nm.ToMillimetreString(Tool.DefaultVBit.WidthAtDepth(depth), 3)} mm, "
            + $"the owned bit cuts {Nm.ToMillimetreString(chosen.WidthAtDepth(depth), 3)} mm");
    }

    /// <summary>
    /// And they agree because they ask the same question, not because two wordings happen to match.
    ///
    /// The sentences agreeing is what an operator sees; it is not quite the property that matters,
    /// because two independent implementations can be made to print the same thing today and drift
    /// tomorrow — which is how this broke the first time. What stops that is both paths taking
    /// their findings from one place, so this pins that <see cref="ElectricalFindings"/> produces
    /// exactly what the export path shows for the layer.
    /// </summary>
    [Fact]
    public void TheExportPathShowsWhatTheSharedCheckProduced()
    {
        var board = Mega();
        var top = board.Layers.Single(l => l.Role == LayerRole.TopCopper);

        var settings = board.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings
            {
                FileName = l.FileName,
                Output = LayerOperations.DefaultFor(l.Role),
                DepthNm = DepthNm,
            },
            StringComparer.Ordinal);

        var exported = ExportPlanner.Plan(
            board, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode);

        var shared = ElectricalFindings.For(
            top,
            new IsolationOptions { Tool = Tool.DefaultVBit, DepthNm = DepthNm },
            CheckSource.Layer(top.Role, top.Label, top.FileName));

        var fromExport = exported.Items
            .Where(i => i.LayerFileName == top.FileName)
            .SelectMany(i => i.Warnings)
            .Where(c => c.Kind == CheckKind.Electrical)
            .ToList();

        Assert.NotEmpty(shared.Checks);
        Assert.Equal(shared.Checks.Select(c => c.Line), fromExport.Select(c => c.Line));

        // Every one of them is about the layer it was found on, not merely about some layer.
        foreach (var check in fromExport)
        {
            Assert.Equal(top.FileName, check.Source.FileName);
        }
    }
}
