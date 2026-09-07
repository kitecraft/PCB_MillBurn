using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.App.Rendering;
using MillBurn.Viewer;

namespace MillBurn.App.ViewModels;

/// <summary>
/// Phase 0 spike shell. Loads a synthetic toolpath and reports how the viewport copes with it.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    /// <summary>The Phase 0 acceptance target from Documentation/06-Roadmap-and-Risks.md.</summary>
    public const int TargetSegments = 500_000;

    [ObservableProperty]
    public partial ToolpathScene? Scene { get; set; }

    [ObservableProperty]
    public partial string BuildSummary { get; set; } = "Building scene…";

    [ObservableProperty]
    public partial string FrameSummary { get; set; } = "—";

    [ObservableProperty]
    public partial string GcodePreview { get; set; } = string.Empty;

    public MainViewModel()
    {
        var sw = Stopwatch.StartNew();
        var polylines = SyntheticToolpath.Generate(TargetSegments);
        var generated = sw.Elapsed;

        sw.Restart();
        var scene = ToolpathScene.Build(polylines);
        var built = sw.Elapsed;

        Scene = scene;
        BuildSummary =
            $"{scene.SourceSegmentCount:N0} segments · generated in {generated.TotalMilliseconds:F0} ms · " +
            $"4 LOD tiers built in {built.TotalMilliseconds:F0} ms";

        GcodePreview = BuildGcodeSample(polylines);
    }

    public void ReportFrame(double averageMs, FrameStats stats)
    {
        var fps = averageMs <= 0 ? 0 : 1000.0 / averageMs;
        FrameSummary =
            $"{averageMs:F2} ms/frame  ·  {fps:F0} fps  ·  LOD tier {stats.Tier}  ·  " +
            $"{stats.SegmentsInView:N0} segments in view  ·  {stats.DrawCalls} draw calls";
    }

    /// <summary>
    /// A small slab of representative G-code for the editor pane. Real output will come from
    /// MillBurn.Post in Phase 2; this only proves AvaloniaEdit handles the pane.
    /// </summary>
    private static string BuildGcodeSample(List<Polyline> polylines)
    {
        var sb = new StringBuilder();
        sb.AppendLine("( PCB_MillBurn - Phase 0 spike )");
        sb.AppendLine("( This is placeholder output. MillBurn.Post generates the real thing. )");
        sb.AppendLine("G21 G90 G94");
        sb.AppendLine("G17");
        sb.AppendLine("M3 S12000");

        var emitted = 0;
        foreach (var pl in polylines)
        {
            if (pl.Style is SegmentStyle.Travel or SegmentStyle.RapidLong)
            {
                sb.AppendLine(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"G0 X{pl.Points[2]:F3} Y{pl.Points[3]:F3}");
                continue;
            }

            sb.AppendLine(
                System.Globalization.CultureInfo.InvariantCulture,
                $"G1 Z-0.150 F120");

            for (var i = 0; i < pl.Points.Length; i += 2)
            {
                sb.AppendLine(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"G1 X{pl.Points[i]:F3} Y{pl.Points[i + 1]:F3} F400");
            }

            sb.AppendLine("G0 Z1.000");

            if (++emitted >= 40)
            {
                break;
            }
        }

        sb.AppendLine("M5");
        sb.AppendLine("M30");
        return sb.ToString();
    }
}
