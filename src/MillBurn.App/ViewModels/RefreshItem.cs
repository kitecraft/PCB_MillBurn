using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Pipeline;

namespace MillBurn.App.ViewModels;

/// <summary>
/// One row in the refresh review: what changed about a file, and whether to take it.
///
/// Ticked by default only when the board actually changed. A re-export with no edits is the common
/// case — an EDA tool rewrites every file every time — and pre-ticking those would train people to
/// hit Apply without reading, which defeats the review.
/// </summary>
public sealed partial class RefreshItem : ObservableObject
{
    public RefreshItem(SourceChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        Change = change;
        Selected = change.AffectsBoard;
    }

    public SourceChange Change { get; }

    public string FileName => Change.FileName;

    public string Label => Change.Kind switch
    {
        SourceChangeKind.Changed => "Changed",
        SourceChangeKind.Added => "Added",
        SourceChangeKind.Removed => "Removed",
        SourceChangeKind.Reexported => "Re-exported",
        _ => "Unchanged",
    };

    public string Detail => Change.Details.Count == 0
        ? Change.FileName
        : $"{Change.FileName} — {string.Join(" ", Change.Details)}";

    [ObservableProperty]
    public partial bool Selected { get; set; }
}
