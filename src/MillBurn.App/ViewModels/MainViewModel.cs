using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Core;
using MillBurn.Pipeline;
using MillBurn.Viewer;

namespace MillBurn.App.ViewModels;

/// <summary>
/// The shell's state: one loaded board, its layers, and how the viewport is coping.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    /// <summary>The Phase 0 acceptance target from Documentation/06-Roadmap-and-Risks.md.</summary>
    public const int TargetSegments = 500_000;

    [ObservableProperty]
    public partial BoardScene? Scene { get; set; }

    [ObservableProperty]
    public partial ToolpathScene? ToolpathScene { get; set; }

    [ObservableProperty]
    public partial string BoardSummary { get; set; } = "No board loaded";

    [ObservableProperty]
    public partial string BoardTitle { get; set; } = "Drop a Gerber folder here";

    [ObservableProperty]
    public partial string FrameSummary { get; set; } = "—";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Drag an export folder onto the window, or use Open folder.";

    [ObservableProperty]
    public partial bool HasBoard { get; set; }

    public ObservableCollection<LayerToggle> Layers { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>Raised when a layer is toggled, so the view can repaint without a scene swap.</summary>
    public event EventHandler? RedrawRequested;

    public void LoadFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        Board board;
        var sw = Stopwatch.StartNew();
        try
        {
            board = BoardLoader.LoadFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not read '{folder}': {ex.Message}";
            return;
        }

        var elapsed = sw.Elapsed;

        // Everything below builds the view from that board, and this is a drop target: people will
        // drop half-written exports, folders of something else entirely, and files a parser has
        // never seen. A bad load has to end as a message in the status bar, never as a window that
        // vanishes while the user is looking at it.
        try
        {
            Present(board, folder, elapsed);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            StatusMessage = $"Loaded '{Path.GetFileName(folder)}' but could not display it: {ex.Message}";
        }
    }

    private void Present(Board board, string folder, TimeSpan elapsed)
    {

        if (board.Layers.Count == 0)
        {
            StatusMessage = $"No Gerber or drill files in '{Path.GetFileName(folder)}'.";
            return;
        }

        var previous = Scene;

        var scene = BoardSceneBuilder.Build(
            board.Layers.Select(l => new BoardLayerSource(l.FileName, l.Label, l.Role, l.Rings())),
            board.Bounds);

        // Look the file up rather than demanding one: the scene also carries a synthetic substrate
        // layer that belongs to no file. Insisting on a match here is what took the window down
        // the first time a board with an outline was loaded.
        var byName = board.Layers.ToDictionary(l => l.FileName, StringComparer.Ordinal);

        Layers.Clear();
        foreach (var layer in scene.Layers)
        {
            var detail = byName.TryGetValue(layer.Id, out var source)
                ? DetailFor(source)
                : "The board material, drawn under everything";

            Layers.Add(new LayerToggle(layer, detail, () => RedrawRequested?.Invoke(this, EventArgs.Empty)));
        }

        Scene = scene;
        HasBoard = true;

        // Swapping the scene first and disposing after means the old SKPaths are never released
        // while a frame in flight is still drawing them.
        previous?.Dispose();

        BoardTitle = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        BoardSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{board.Layers.Count} layers · {board.TotalObjects:N0} objects · {scene.TotalVertices:N0} vertices · " +
            $"{Nm.ToMillimetreString(board.Bounds.Width, 2)} x {Nm.ToMillimetreString(board.Bounds.Height, 2)} mm · " +
            $"loaded in {elapsed.TotalMilliseconds:F0} ms");

        RefreshWarnings(board);
        StatusMessage = Warnings.Count == 0
            ? $"Loaded {BoardTitle}."
            : $"Loaded {BoardTitle} with {Warnings.Count} thing(s) worth checking.";
    }

    /// <summary>
    /// Anything the operator should look at before trusting what is on screen. These are surfaced
    /// rather than logged because a board that is quietly missing a layer still looks like a board.
    /// </summary>
    private void RefreshWarnings(Board board)
    {
        Warnings.Clear();

        foreach (var failure in board.Failures)
        {
            Warnings.Add($"Could not read {failure}");
        }

        foreach (var layer in board.Layers.Where(l => l.HasErrors))
        {
            var first = layer.Diagnostics.First(d => d.IsError);
            Warnings.Add($"{layer.FileName}: {first.Message}");
        }

        foreach (var layer in board.Layers.Where(l => l.RoleGuessed))
        {
            Warnings.Add($"{layer.FileName} declares no file function; role guessed as {layer.Label}.");
        }

        if (!board.Layers.Any(l => l.Role == LayerRole.Outline))
        {
            Warnings.Add("No board outline: extents are taken from the drawn geometry.");
        }

        foreach (var group in board.Layers
            .Where(l => l.Role != LayerRole.Unknown)
            .GroupBy(l => l.Role)
            .Where(g => g.Count() > 1))
        {
            Warnings.Add(
                $"{group.Count()} files claim to be {LayerRoleInfo.Label(group.Key)}: " +
                string.Join(", ", group.Select(l => l.FileName)));
        }
    }

    private static string DetailFor(BoardLayer layer)
    {
        var detail = string.Create(
            CultureInfo.InvariantCulture,
            $"{layer.FileName} · {layer.ObjectCount:N0} objects · {layer.AreaMm2:F2} mm²");

        return layer.DeclaredNegative ? detail + " · negative" : detail;
    }

    public void ReportFrame(double elapsedMs, int layers, int vertices)
    {
        var fps = elapsedMs <= 0 ? 0 : 1000.0 / elapsedMs;
        FrameSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{elapsedMs:F2} ms/frame · {fps:F0} fps · {layers} layers · {vertices:N0} vertices");
    }

    public void ReportFrame(double averageMs, FrameStats stats)
    {
        var fps = averageMs <= 0 ? 0 : 1000.0 / averageMs;
        FrameSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{averageMs:F2} ms/frame · {fps:F0} fps · LOD tier {stats.Tier} · " +
            $"{stats.SegmentsInView:N0} segments in view · {stats.DrawCalls} draw calls");
    }

    /// <summary>
    /// Builds the synthetic 500k-segment toolpath for <c>--fpstest</c>.
    ///
    /// Lazy on purpose. It costs about a second, and now that the app opens a real board there is
    /// no reason to pay that on every launch for a measurement harness almost nobody runs.
    /// </summary>
    public void LoadSyntheticToolpath()
    {
        var sw = Stopwatch.StartNew();
        var polylines = SyntheticToolpath.Generate(TargetSegments);
        var generated = sw.Elapsed;

        sw.Restart();
        var scene = MillBurn.Viewer.ToolpathScene.Build(polylines);

        ToolpathScene = scene;
        BoardSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{scene.SourceSegmentCount:N0} synthetic segments · generated in {generated.TotalMilliseconds:F0} ms · " +
            $"4 LOD tiers built in {sw.Elapsed.TotalMilliseconds:F0} ms");
        BoardTitle = "Viewport benchmark";
    }
}
