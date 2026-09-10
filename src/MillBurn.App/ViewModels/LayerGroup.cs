using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Core;

namespace MillBurn.App.ViewModels;

/// <summary>
/// A run of layers that belong to the same part of the board: the top side, the inner copper, the
/// bottom side, and the holes and outline that belong to neither side.
///
/// The list used to be in paint order — bottom silk first, outline last — which is the right order
/// for a renderer and the wrong one for a person. Paint order interleaves the two sides at the
/// point where you are least able to tell them apart (a soldermask row looks like a soldermask row),
/// and it buries the side you are actually working on in the middle. Grouping puts the top side
/// first, because that is the side most single-sided work happens on, and keeps the two sides from
/// ever being adjacent.
/// </summary>
public sealed partial class LayerGroup : ObservableObject
{
    private LayerGroup(string title, IReadOnlyList<LayerRow> rows)
    {
        Title = title;
        Rows = rows;

        foreach (var row in rows)
        {
            row.PropertyChanged += OnRowChanged;
        }
    }

    public string Title { get; }

    public IReadOnlyList<LayerRow> Rows { get; }

    /// <summary>
    /// What this group is contributing, on the header — so a collapsed-everything panel still says
    /// which parts of the board are going to be cut.
    /// </summary>
    public string Summary
    {
        get
        {
            var exporting = Rows.Count(r => r.HasExportBadge);

            return exporting == 0
                ? "none exported"
                : string.Create(CultureInfo.InvariantCulture, $"{exporting} of {Rows.Count} exported");
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LayerRow.HasExportBadge))
        {
            OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>Drops every row's subscription, so a rebuilt list does not keep the old one alive.</summary>
    public void Detach()
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }
    }

    // ------------------------------------------------------------------ building

    /// <summary>
    /// Groups the rows, keeping only the groups that have anything in them — an empty "Inner
    /// layers" heading on a two-layer board is a heading that says nothing.
    /// </summary>
    public static IReadOnlyList<LayerGroup> Build(IEnumerable<LayerRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return
        [
            .. rows
                .GroupBy(r => LayerRoleInfo.PanelGroup(r.Role ?? LayerRole.Unknown))
                .OrderBy(g => g.Key.Order)
                .Select(g => new LayerGroup(
                    g.Key.Title,
                    [.. g
                        .OrderBy(r => LayerRoleInfo.PanelOrder(r.Role ?? LayerRole.Unknown))
                        .ThenBy(r => r.Label, StringComparer.Ordinal)])),
        ];
    }
}
