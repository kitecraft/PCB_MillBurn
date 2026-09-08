using System.Collections.Immutable;
using System.Globalization;
using MillBurn.Cam;
using MillBurn.Core;

namespace MillBurn.Pipeline;

/// <summary>What happened to one file between the project and the folder on disk.</summary>
public enum SourceChangeKind
{
    /// <summary>Byte-for-byte identical.</summary>
    Unchanged,

    /// <summary>
    /// The bytes differ but the geometry does not — a re-export with no edits. This is the common
    /// case and the reason the refresh is worth having at all: an EDA tool stamps a creation date
    /// into every file, so a plain content comparison flags every layer of every export.
    /// </summary>
    Reexported,

    /// <summary>The board actually changed.</summary>
    Changed,

    /// <summary>In the folder, not in the project.</summary>
    Added,

    /// <summary>In the project, not in the folder.</summary>
    Removed,
}

/// <summary>One file's entry in a refresh, with enough detail to decide whether to take it.</summary>
public sealed record SourceChange
{
    public required string FileName { get; init; }

    public required SourceChangeKind Kind { get; init; }

    public required LayerRole Role { get; init; }

    /// <summary>The incoming file, or null when it was removed.</summary>
    public ProjectSource? Incoming { get; init; }

    /// <summary>Plain-language summary of what differs, for the review list.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>True for anything the user might want to take.</summary>
    public bool IsActionable => Kind is not SourceChangeKind.Unchanged;

    /// <summary>True when the geometry differs, which is what actually affects a job.</summary>
    public bool AffectsBoard => Kind is SourceChangeKind.Changed or SourceChangeKind.Added or SourceChangeKind.Removed;
}

/// <summary>The result of comparing a project against a folder.</summary>
public sealed record RefreshPlan
{
    public required string Folder { get; init; }

    public required ImmutableArray<SourceChange> Changes { get; init; }

    public IEnumerable<SourceChange> Actionable => Changes.Where(c => c.IsActionable);

    public bool HasChanges => Changes.Any(c => c.IsActionable);

    public bool AffectsBoard => Changes.Any(c => c.AffectsBoard);

    public string Summary()
    {
        var changed = Changes.Count(c => c.Kind == SourceChangeKind.Changed);
        var added = Changes.Count(c => c.Kind == SourceChangeKind.Added);
        var removed = Changes.Count(c => c.Kind == SourceChangeKind.Removed);
        var reexported = Changes.Count(c => c.Kind == SourceChangeKind.Reexported);

        if (!HasChanges)
        {
            return "The source folder matches this project.";
        }

        var parts = new List<string>();
        if (changed > 0)
        {
            parts.Add(Invariant($"{changed} changed"));
        }

        if (added > 0)
        {
            parts.Add(Invariant($"{added} added"));
        }

        if (removed > 0)
        {
            parts.Add(Invariant($"{removed} removed"));
        }

        if (reexported > 0)
        {
            parts.Add(Invariant($"{reexported} re-exported with no change to the board"));
        }

        return string.Join(", ", parts) + ".";
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Re-importing a project's sources after the board has been edited and re-exported.
///
/// The point is to keep the work: tools, feeds, depths and selections survive, because they are
/// settings rather than geometry. The two rules that make it safe are that nothing is applied
/// until the user has seen what differs, and that a refresh is a document edit — it marks the
/// project dirty, so closing without saving undoes it.
/// </summary>
public static class ProjectRefresh
{
    /// <summary>Compares a project's embedded sources against a folder. Reads, never writes.</summary>
    public static RefreshPlan Inspect(
        MillBurnProject project, string folder, RealisationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(folder);

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"'{folder}' is not a directory.");
        }

        var incoming = ProjectFile.ImportFolder(folder, options)
            .ToDictionary(s => s.FileName, StringComparer.OrdinalIgnoreCase);

        var existing = project.Sources.ToDictionary(s => s.FileName, StringComparer.OrdinalIgnoreCase);
        var changes = ImmutableArray.CreateBuilder<SourceChange>();

        foreach (var current in project.Sources)
        {
            if (!incoming.TryGetValue(current.FileName, out var fresh))
            {
                changes.Add(new SourceChange
                {
                    FileName = current.FileName,
                    Kind = SourceChangeKind.Removed,
                    Role = current.Role,
                    Details = ["No longer in the export folder."],
                });
                continue;
            }

            if (current.ContentHash == fresh.ContentHash)
            {
                changes.Add(new SourceChange
                {
                    FileName = current.FileName,
                    Kind = SourceChangeKind.Unchanged,
                    Role = current.Role,
                    Incoming = fresh,
                });
                continue;
            }

            // Bytes differ. Only the geometry fingerprint says whether that matters.
            var geometryChanged = current.GeometryHash != fresh.GeometryHash;

            changes.Add(new SourceChange
            {
                FileName = current.FileName,
                Kind = geometryChanged ? SourceChangeKind.Changed : SourceChangeKind.Reexported,
                Role = current.Role,
                Incoming = fresh,
                Details = geometryChanged
                    ? Describe(current, fresh, options)
                    : ["Re-exported; the board is unchanged."],
            });
        }

        foreach (var fresh in incoming.Values.Where(s => !existing.ContainsKey(s.FileName)))
        {
            changes.Add(new SourceChange
            {
                FileName = fresh.FileName,
                Kind = SourceChangeKind.Added,
                Role = fresh.Role,
                Incoming = fresh,
                Details = [$"New file, detected as {LayerRoleInfo.Label(fresh.Role)}."],
            });
        }

        return new RefreshPlan
        {
            Folder = folder,
            Changes = [.. changes.OrderBy(c => LayerRoleInfo.DrawOrder(c.Role)).ThenBy(c => c.FileName, StringComparer.Ordinal)],
        };
    }

    /// <summary>
    /// Applies the named changes. Everything not named is left exactly as it was, so refreshing
    /// only the silkscreen does not disturb anything attached to the copper.
    /// </summary>
    public static void Apply(MillBurnProject project, RefreshPlan plan, IEnumerable<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fileNames);

        var taking = fileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = project.Sources.ToList();

        foreach (var change in plan.Changes.Where(c => taking.Contains(c.FileName)))
        {
            var index = sources.FindIndex(s => string.Equals(s.FileName, change.FileName, StringComparison.OrdinalIgnoreCase));

            switch (change.Kind)
            {
                case SourceChangeKind.Removed when index >= 0:
                    sources.RemoveAt(index);
                    break;

                case SourceChangeKind.Added when change.Incoming is { } added:
                    sources.Add(added);
                    break;

                case SourceChangeKind.Changed or SourceChangeKind.Reexported when index >= 0 && change.Incoming is { } fresh:
                    // A role the user corrected by hand outlives the file it was corrected on.
                    // Re-detecting it here would undo that silently on every refresh.
                    sources[index] = sources[index].RoleOverridden
                        ? fresh with { Role = sources[index].Role, RoleOverridden = true }
                        : fresh;
                    break;

                default:
                    break;
            }
        }

        project.Sources = [.. sources];
        project.OriginFolder = plan.Folder;
        project.Touch();
    }

    /// <summary>
    /// What changed, in numbers a person can judge. Realising both versions costs a few
    /// milliseconds and is the difference between "F_Cu changed" and "F_Cu changed: 122.77 to
    /// 118.40 mm², net +VBUS added".
    /// </summary>
    private static List<string> Describe(
        ProjectSource before, ProjectSource after, RealisationOptions? options)
    {
        var details = new List<string>();

        var oldBoard = BoardLoader.LoadSources("before", [(before.FileName, before.Content.AsSpan().ToArray(), before.Role)], options);
        var newBoard = BoardLoader.LoadSources("after", [(after.FileName, after.Content.AsSpan().ToArray(), after.Role)], options);

        var oldLayer = oldBoard.Layers.Count > 0 ? oldBoard.Layers[0] : null;
        var newLayer = newBoard.Layers.Count > 0 ? newBoard.Layers[0] : null;

        if (oldLayer is null || newLayer is null)
        {
            details.Add("The board changed.");
            return details;
        }

        if (Math.Abs(oldLayer.AreaMm2 - newLayer.AreaMm2) > 0.0005)
        {
            details.Add(Invariant($"Area {oldLayer.AreaMm2:F3} to {newLayer.AreaMm2:F3} mm²."));
        }

        if (oldLayer.RingCount != newLayer.RingCount)
        {
            details.Add(Invariant($"{oldLayer.RingCount} regions to {newLayer.RingCount}."));
        }

        if (oldLayer.ObjectCount != newLayer.ObjectCount)
        {
            details.Add(Invariant($"{oldLayer.ObjectCount} objects to {newLayer.ObjectCount}."));
        }

        if (oldLayer.Drill is not null && newLayer.Drill is not null
            && oldLayer.Drill.Hits.Count != newLayer.Drill.Hits.Count)
        {
            details.Add(Invariant($"{oldLayer.Drill.Hits.Count} holes to {newLayer.Drill.Hits.Count}."));
        }

        if (details.Count == 0)
        {
            // The fingerprint differs but nothing summarised above does — a trace moved without
            // changing its length, say. Worth saying plainly rather than showing an empty row.
            details.Add("Geometry moved; totals are unchanged.");
        }

        return details;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
