namespace MillBurn.Core;

/// <summary>How the tool list is ordered. The file's own order is one of the choices, and the default.</summary>
public enum ToolSort
{
    /// <summary>As <c>tools.json</c> holds them, which is the order they were added in.</summary>
    Library,

    Name,

    Kind,

    /// <summary>Tip width for a V-bit, diameter for anything else — see <see cref="ToolView.SizeOf"/>.</summary>
    Size,
}

/// <summary>
/// Which tools the library window shows, and in what order.
///
/// **Requested from the workshop as three things — filter by kind, sorting, and copy a tool — which
/// are one symptom.** The list was designed for the six tools it shipped with, and a library grows:
/// it is insertion-ordered, which is not an order, so the 0.5 mm end mill bought last Tuesday sits
/// between a V-bit and a drill and the only way to find anything is to read every row.
///
/// **The view, never the file.** `tools.json` keeps the order it has. It is diffable, it is what
/// somebody reads when something has gone wrong, and rewriting it because a window was looked at
/// would turn a change of sort into a change of file. Nothing about the library depends on its order
/// — every reference to a tool is by id — so the sort is not saved either, and nothing here mutates
/// what it is given.
///
/// **Here rather than in the window** so that a library of thirty can be arranged in a test. The
/// window is the four controls and this is the rule behind them.
/// </summary>
public static class ToolView
{
    /// <summary>
    /// The size a tool is known by: a V-bit's tip width, anything else's diameter.
    ///
    /// **The tip stands in for a V-bit's diameter**, which is what makes sorting by size mean
    /// anything: a V-bit's `DiameterNm` is its shank, which has nothing to do with what it cuts, so
    /// ordering on that would scatter the V-bits through the end mills by a number nobody chose them
    /// for — or land them all at zero, where the field is left unset.
    /// </summary>
    public static long SizeOf(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return tool.Kind == ToolKind.VBit ? tool.TipNm : tool.DiameterNm;
    }

    /// <summary>
    /// A copy of a tool, with its own identity.
    ///
    /// **The id is the whole risk, and it is why this is a function rather than two lines in a
    /// click handler.** `Tool.Id` is what a project stores, so two tools sharing one are the same
    /// tool to everything downstream: a job naming the copy would cut with the original, and nothing
    /// anywhere would say so. Everything else is carried, which is the point — tools come in
    /// families, and a 0.8 and a 1.0 end mill share their feeds, their stepdown and their notes.
    /// </summary>
    /// <param name="tool">The tool to copy.</param>
    /// <param name="name">
    /// What to call it, or null for the suffixed proposal. The window asks the operator for this
    /// and passes what they typed; the proposal is what the dialog starts with.
    /// </param>
    public static Tool Copy(Tool tool, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return tool with
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(name) ? tool.Name + " (copy)" : name.Trim(),
        };
    }

    /// <summary>
    /// The tools to show, filtered to one kind and put in order.
    ///
    /// Returns a new list and leaves its argument alone, which is the whole of "sort the view, never
    /// the file": the caller's library is the same library afterwards, in the same order, and
    /// nothing has been written.
    /// </summary>
    /// <param name="tools">The library, in its own order.</param>
    /// <param name="only">The one kind to show, or null for all of them.</param>
    /// <param name="sort">How to order what is left.</param>
    public static IReadOnlyList<Tool> Arrange(
        IEnumerable<Tool> tools, ToolKind? only = null, ToolSort sort = ToolSort.Library)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var shown = tools.Where(t => only is not { } kind || t.Kind == kind).ToList();

        return sort switch
        {
            // Culture-aware and case-insensitive, because this is a list somebody reads: "1.2 mm end
            // mill" and "1mm Corn Endmill" are both in this machine's own library, and an ordinal
            // sort would put every capital letter before every lower-case one.
            ToolSort.Name => [.. shown.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)],

            // Within a kind, by size: a drawer of end mills is thought about in millimetres, and
            // grouping without ordering inside the group only moves the reading problem.
            ToolSort.Kind => [.. shown.OrderBy(t => t.Kind).ThenBy(SizeOf).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)],

            ToolSort.Size => [.. shown.OrderBy(SizeOf).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)],
            _ => shown,
        };
    }
}
