using MillBurn.Core;

namespace MillBurn.Gerber.Excellon;

/// <summary>Whether the holes in a file are plated through.</summary>
public enum HolePlating
{
    Unknown,
    Plated,
    NonPlated,
}

/// <summary>A drill tool: a number, a diameter, and whatever the writer said about it.</summary>
public sealed record DrillTool(int Number, long DiameterNm, string? Function = null)
{
    /// <summary>From <c>.AperFunction</c>, e.g. "ComponentDrill", "ViaDrill".</summary>
    public bool IsVia => Function?.Contains("ViaDrill", StringComparison.OrdinalIgnoreCase) == true;

    public override string ToString() =>
        $"T{Number} {Nm.ToMillimetreString(DiameterNm, 3)}mm{(Function is null ? "" : $" {Function}")}";
}

/// <summary>A single drilled hole.</summary>
public readonly record struct DrillHit(int Tool, Point2 At);

/// <summary>
/// A routed slot: the tool travels from one point to another at depth. Produced by <c>G85</c> and
/// by <c>G01</c> routing between <c>M15</c>/<c>M16</c>.
/// </summary>
public readonly record struct DrillSlot(int Tool, Point2 From, Point2 To);

/// <summary>The parsed contents of one Excellon drill file.</summary>
public sealed class ExcellonFile
{
    public required IReadOnlyDictionary<int, DrillTool> Tools { get; init; }

    public required IReadOnlyList<DrillHit> Hits { get; init; }

    public required IReadOnlyList<DrillSlot> Slots { get; init; }

    public required LengthUnit Unit { get; init; }

    public required HolePlating Plating { get; init; }

    public required Bounds Bounds { get; init; }

    public required IReadOnlyList<Model.GerberDiagnostic> Diagnostics { get; init; }

    public string? FileFunction { get; init; }

    /// <summary>Hole count grouped by tool, largest tool first — the order a job runs in.</summary>
    public IEnumerable<(DrillTool Tool, int Count)> ByTool() =>
        Hits.GroupBy(h => h.Tool)
            .Select(g => (Tool: Tools.TryGetValue(g.Key, out var t)
                    ? t
                    : new DrillTool(g.Key, 0),
                Count: g.Count()))
            .OrderByDescending(x => x.Tool.DiameterNm);
}
