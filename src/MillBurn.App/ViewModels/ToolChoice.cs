using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Core;

namespace MillBurn.App.ViewModels;

/// <summary>
/// One operation's tool choice: the list to pick from, the one picked, and what it will actually do.
///
/// The "what it will do" line is the point. For a V-bit the cut width is a *consequence* of the
/// depth, not a setting, so a dropdown showing only names would hide the number that decides
/// whether the board works.
/// </summary>
public sealed partial class ToolChoice : ObservableObject
{
    private readonly Func<Tool, string> _describe;

    public ToolChoice(string label, IEnumerable<Tool> tools, Tool selected, Func<Tool, string> describe)
    {
        ArgumentNullException.ThrowIfNull(tools);

        Label = label;
        _describe = describe;
        Tools = [.. tools];
        Selected = Tools.FirstOrDefault(t => t.Id == selected.Id) ?? selected;
    }

    public string Label { get; }

    public IReadOnlyList<Tool> Tools { get; }

    [ObservableProperty]
    public partial Tool Selected { get; set; }

    public string Effect => _describe(Selected);

    partial void OnSelectedChanged(Tool value)
    {
        _ = value;
        OnPropertyChanged(nameof(Effect));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    /// <summary>Re-reads the effect line, for when something outside the choice changed it.</summary>
    public void Refresh() => OnPropertyChanged(nameof(Effect));

    /// <summary>What this tool does at a given depth, in the units the operator thinks in.</summary>
    public static string DescribeIsolation(Tool tool, long depthNm)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (tool.Kind != ToolKind.VBit)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Cuts {Nm.ToMillimetreString(tool.DiameterNm, 3)} mm wide at any depth");
        }

        var width = Nm.ToMillimetreString(tool.WidthAtDepth(depthNm), 3);
        var depth = Nm.ToMillimetreString(depthNm, 3);
        var sensitivity = Nm.ToMillimetreString((long)(tool.WidthPerDepth * Nm.FromMillimetres(0.01)), 4);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Cuts {width} mm wide at {depth} mm deep · 0.01 mm deeper adds {sensitivity} mm");
    }

    public static string DescribeOutline(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (tool.Kind == ToolKind.VBit)
        {
            return "A V-bit at full depth is enormously wide. Use a flat end mill.";
        }

        var diameter = Nm.ToMillimetreString(tool.DiameterNm, 3);
        var stepdown = tool.StepdownNm > 0
            ? Nm.ToMillimetreString(tool.StepdownNm, 2) + " mm per pass"
            : "default stepdown";

        return string.Create(CultureInfo.InvariantCulture, $"{diameter} mm · {stepdown}");
    }

    public static string DescribeDrill(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Sizes come from the drill file · plunge {tool.PlungeMmPerMin} mm/min, {tool.SpindleRpm} rpm");
    }
}
