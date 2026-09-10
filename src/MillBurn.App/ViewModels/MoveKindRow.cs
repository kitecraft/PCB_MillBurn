using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Viewer;

namespace MillBurn.App.ViewModels;

/// <summary>
/// One kind of move, across every program: cutting, travel, long rapids, a gouge — and the
/// substrate, which is not a move but belongs beside them for the same reason.
///
/// These used to sit in the layer list as though they were layers. They are not: no file produces
/// one, they cannot be exported, and the settings a layer row offers are meaningless for them. What
/// they actually are is a filter across the whole drawing, which is what this is.
/// </summary>
public sealed partial class MoveKindRow : ObservableObject
{
    private readonly IReadOnlyList<BoardSceneLayer> _layers;
    private readonly Action _changed;

    public MoveKindRow(
        string id,
        string label,
        IReadOnlyList<BoardSceneLayer> layers,
        IBrush swatch,
        bool visible,
        Action changed)
    {
        ArgumentNullException.ThrowIfNull(layers);

        Id = id;
        Label = label;
        Swatch = swatch;
        _layers = layers;
        _changed = changed;

        IsVisible = visible;
    }

    /// <summary>The colour key — <c>gcode-cut</c> and friends — which is also how colours are saved.</summary>
    public string Id { get; }

    public string Label { get; }

    public IBrush Swatch { get; }

    /// <summary>How many runs this kind accounts for, for the tooltip.</summary>
    public int Runs => _layers.Sum(l => l.RingCount);

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    partial void OnIsVisibleChanged(bool value)
    {
        _ = value;
        _changed?.Invoke();
    }

    /// <summary>The scene layers this chip governs, so the caller can combine the two axes.</summary>
    public IReadOnlyList<BoardSceneLayer> Layers => _layers;
}
