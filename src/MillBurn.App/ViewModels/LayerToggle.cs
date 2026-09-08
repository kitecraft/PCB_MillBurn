using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Viewer;

namespace MillBurn.App.ViewModels;

/// <summary>
/// One row in the layer panel: a checkbox, a colour swatch, and what the layer contains.
///
/// It writes straight through to the scene layer rather than raising an event the view model has
/// to translate. Toggling visibility is not a state change worth modelling twice — the scene is
/// the state, and a second copy of it in the view model is a bug waiting for someone to update one
/// and not the other.
/// </summary>
public sealed partial class LayerToggle : ObservableObject
{
    private readonly BoardSceneLayer _layer;
    private readonly Action _changed;

    public LayerToggle(BoardSceneLayer layer, string detail, Action changed)
    {
        ArgumentNullException.ThrowIfNull(layer);

        _layer = layer;
        _changed = changed;
        Detail = detail;

        var fill = layer.Style.Fill;
        Swatch = new SolidColorBrush(Color.FromArgb(0xFF, fill.Red, fill.Green, fill.Blue));
    }

    public string Label => _layer.Label;

    public string Id => _layer.Id;

    public string Detail { get; }

    public IBrush Swatch { get; }

    public bool IsVisible
    {
        get => _layer.Visible;
        set
        {
            if (_layer.Visible == value)
            {
                return;
            }

            _layer.Visible = value;
            OnPropertyChanged();
            _changed();
        }
    }
}
