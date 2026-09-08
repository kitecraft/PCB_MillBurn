using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MillBurn.Core;
using MillBurn.Pipeline;
using MillBurn.Viewer;

namespace MillBurn.App.ViewModels;

/// <summary>
/// One entry in a layer's output dropdown.
///
/// A named pair rather than the bare enum so the list reads in the operator's language — "G-code
/// (mill)" rather than "Gcode" — without a converter standing between the view and the model.
/// </summary>
public sealed record OutputChoice(OutputKind Kind, string Label)
{
    public override string ToString() => Label;

    public static OutputChoice For(OutputKind kind) => new(kind, kind switch
    {
        OutputKind.Svg => "SVG (laser)",
        OutputKind.Gcode => "G-code (mill)",
        _ => "Not exported",
    });
}

/// <summary>
/// One row in the layer drawer: whether it is shown, what colour, and what it is turned into.
///
/// **Visibility and export are deliberately separate.** Conflating them reads well in a sentence and
/// fails in use: you constantly want the soldermask on screen to check a pad against while cutting
/// only the copper, and you want the bottom silk hidden while still exporting it for the laser.
/// The checkbox controls the eye; the dropdown controls the machine.
///
/// The same row type also carries the scene's synthetic layers — the substrate, and the backplot of
/// an emitted program. Those have no file and produce no output, so they show the eye and the swatch
/// and nothing else.
/// </summary>
public sealed partial class LayerRow : ObservableObject
{
    private readonly BoardSceneLayer? _scene;
    private readonly string _detail;

    // Wired only after the constructor has set the initial values, so building a row does not read
    // as the user having changed something.
    private Action? _visibilityChanged;
    private Action? _outputChanged;

    /// <summary>A real board layer: eye, colour, output, tool.</summary>
    public LayerRow(
        BoardLayer layer,
        BoardSceneLayer? scene,
        LayerOutputSettings settings,
        IReadOnlyList<Tool> tools,
        Action visibilityChanged,
        Action outputChanged)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tools);

        Layer = layer;
        Id = layer.FileName;
        Label = layer.Label;
        Role = layer.Role;
        _scene = scene;
        _detail = string.Empty;

        Outputs = [.. LayerOperations.Available(layer.Role).Select(OutputChoice.For)];
        AllTools = tools;

        SelectedOutput = Outputs.FirstOrDefault(o => o.Kind == settings.Output) ?? Outputs[0];
        BreakThroughMm = Nm.ToMillimetres(settings.BreakThroughNm);
        DepthMm = Nm.ToMillimetres(settings.DepthNm);
        TabCount = settings.TabCount;
        Passes = settings.Passes;
        Mirrored = settings.MirrorFor(layer.Role);

        Tool = tools.FirstOrDefault(t => t.Id == settings.ToolId) ?? DefaultTool();
        Swatch = ToBrush(scene?.Style.Fill ?? SkiaSharp.SKColors.Gray);
        IsVisible = scene?.Visible ?? true;

        _visibilityChanged = visibilityChanged;
        _outputChanged = outputChanged;
    }

    /// <summary>A layer the scene invented: the substrate, or a backplot. Eye and colour only.</summary>
    public LayerRow(BoardSceneLayer scene, string detail, Action visibilityChanged)
    {
        ArgumentNullException.ThrowIfNull(scene);

        _scene = scene;
        Id = scene.Id;
        Label = scene.Label;
        _detail = detail;

        Outputs = [OutputChoice.For(OutputKind.None)];
        AllTools = [];

        SelectedOutput = Outputs[0];
        Tool = Tool.DefaultVBit;
        Swatch = ToBrush(scene.Style.Fill);
        IsVisible = scene.Visible;

        _visibilityChanged = visibilityChanged;
    }

    public BoardLayer? Layer { get; }

    public LayerRole? Role { get; }

    public string Id { get; }

    public string Label { get; }

    public string FileName => Layer?.FileName ?? Id;

    public IReadOnlyList<OutputChoice> Outputs { get; }

    public IReadOnlyList<Tool> AllTools { get; }

    /// <summary>True when this layer can produce a file at all — the dropdown is hidden otherwise.</summary>
    public bool CanExport => Outputs.Count > 1;

    /// <summary>The substrate is recolourable but is not a role, so it is named rather than mapped.</summary>
    public bool IsSubstrate => Id == BoardSceneBuilder.SubstrateId;

    public bool CanRecolour => Role is not null || IsSubstrate;

    // ------------------------------------------------------------------ view

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial IBrush Swatch { get; set; }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_scene is not null)
        {
            _scene.Visible = value;
        }

        _visibilityChanged?.Invoke();
    }

    // ------------------------------------------------------------------ output

    [ObservableProperty]
    public partial OutputChoice SelectedOutput { get; set; }

    public OutputKind Output => SelectedOutput.Kind;

    [ObservableProperty]
    public partial Tool Tool { get; set; }

    /// <summary>How far past the back face to go. Only means anything when the cut goes through.</summary>
    [ObservableProperty]
    public partial double BreakThroughMm { get; set; }

    [ObservableProperty]
    public partial double DepthMm { get; set; }

    [ObservableProperty]
    public partial int TabCount { get; set; }

    /// <summary>Isolation only: how many offsets out from the copper.</summary>
    [ObservableProperty]
    public partial int Passes { get; set; } = 1;

    /// <summary>
    /// Reflect this layer for work done on a flipped board.
    ///
    /// Ticked by default for bottom-side layers and not for top-side ones, but it is a workflow
    /// question rather than a fact about the file — burning a mask onto a transparency that will be
    /// laid face-down wants the opposite of engraving the same layer directly — so it is a choice
    /// and not a rule.
    /// </summary>
    [ObservableProperty]
    public partial bool Mirrored { get; set; }

    public OperationKind Operation =>
        Role is null ? OperationKind.None : LayerOperations.For(Role.Value, Output);

    public bool IsGcode => Output == OutputKind.Gcode;

    public bool IsSvg => Output == OutputKind.Svg;

    public bool NeedsBreakThrough => IsGcode && LayerOperations.GoesThrough(Operation);

    public bool NeedsDepth => IsGcode && Operation is OperationKind.Isolation or OperationKind.Engrave;

    public bool NeedsTabs => IsGcode && Operation == OperationKind.Outline;

    public bool NeedsPasses => IsGcode && Operation == OperationKind.Isolation;

    /// <summary>Anything that produces a file can be mirrored; drills included.</summary>
    public bool NeedsMirror => Output != OutputKind.None;

    /// <summary>Tools that suit this operation, so the list is not a catalogue of everything.</summary>
    public IReadOnlyList<Tool> Tools
    {
        get
        {
            var wanted = LayerOperations.ToolKindFor(Operation);
            return wanted is null ? AllTools : [.. AllTools.Where(t => t.Kind == wanted.Value)];
        }
    }

    partial void OnSelectedOutputChanged(OutputChoice value)
    {
        _ = value;

        if (Tools.Count > 0 && !Tools.Contains(Tool))
        {
            Tool = Tools[0];
        }

        RaiseDerived();
        _outputChanged?.Invoke();
    }

    partial void OnToolChanged(Tool value)
    {
        _ = value;
        OnPropertyChanged(nameof(Detail));
        _outputChanged?.Invoke();
    }

    partial void OnBreakThroughMmChanged(double value)
    {
        _ = value;
        OnPropertyChanged(nameof(Detail));
        _outputChanged?.Invoke();
    }

    partial void OnDepthMmChanged(double value)
    {
        _ = value;
        OnPropertyChanged(nameof(Detail));
        _outputChanged?.Invoke();
    }

    partial void OnTabCountChanged(int value)
    {
        _ = value;
        OnPropertyChanged(nameof(Detail));
        _outputChanged?.Invoke();
    }

    partial void OnPassesChanged(int value)
    {
        _ = value;
        OnPropertyChanged(nameof(Detail));
        _outputChanged?.Invoke();
    }

    partial void OnMirroredChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(Detail));
        _outputChanged?.Invoke();
    }

    private void RaiseDerived()
    {
        foreach (var name in new[]
        {
            nameof(Output), nameof(Operation), nameof(IsGcode), nameof(IsSvg), nameof(NeedsBreakThrough),
            nameof(NeedsDepth), nameof(NeedsTabs), nameof(NeedsPasses), nameof(NeedsMirror), nameof(Tools), nameof(Detail), nameof(TargetName),
        })
        {
            OnPropertyChanged(name);
        }
    }

    public string TargetName => Output == OutputKind.None
        ? string.Empty
        : ExportPlanner.TargetNameFor(FileName, Operation, Output);

    /// <summary>
    /// What this layer will produce, in the terms the operation is actually about.
    ///
    /// A V-bit's line is its cut width, because that is a consequence of the depth rather than
    /// something typed in. A drill's is the hole count and how far through the back it goes. An
    /// SVG's is the size of the drawing. One shared "info" string would say the wrong thing for two
    /// of the three.
    /// </summary>
    public string Detail
    {
        get
        {
            if (Layer is not { } layer)
            {
                return _detail;
            }

            var flip = Mirrored && Output != OutputKind.None ? " · mirrored" : string.Empty;

            var counts = string.Create(
                CultureInfo.InvariantCulture,
                $"{layer.ObjectCount:N0} objects · {layer.AreaMm2:F2} mm²");

            var size = string.Create(
                CultureInfo.InvariantCulture,
                $"{Nm.ToMillimetreString(layer.Bounds.Width, 2)} × {Nm.ToMillimetreString(layer.Bounds.Height, 2)} mm");

            var cutWidth = Nm.ToMillimetreString(Tool.WidthAtDepth(Nm.FromMillimetres(DepthMm)), 3);
            var holes = layer.Drill?.Hits.Count ?? 0;
            var sizes = layer.Drill?.Tools.Count ?? 0;
            var cutter = Nm.ToMillimetreString(Tool.DiameterNm, 2);
            var passes = Passes == 1
                ? "1 pass"
                : string.Create(CultureInfo.InvariantCulture, $"{Passes} passes");

            return Operation switch
            {
                OperationKind.None => $"{size} · {counts}",

                OperationKind.Isolation => string.Create(
                    CultureInfo.InvariantCulture,
                    $"Cuts {cutWidth} mm wide at {DepthMm:F3} mm deep · {passes}{flip}"),

                OperationKind.Engrave when IsGcode => $"Traces the legend {cutWidth} mm wide{flip}",

                OperationKind.Drilling => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{holes} holes, {sizes} sizes · {BreakThroughMm:F2} mm through the back{flip}"),

                OperationKind.Outline => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{cutter} mm cutter outside the profile · {TabCount} tabs · {BreakThroughMm:F2} mm through{flip}"),

                _ => $"{size} · {counts}{flip}",
            };
        }
    }

    public LayerOutputSettings ToSettings() => new()
    {
        FileName = FileName,
        Output = Output,
        ToolId = Tool.Id,
        BreakThroughNm = Nm.FromMillimetres(BreakThroughMm),
        DepthNm = Nm.FromMillimetres(DepthMm),
        TabCount = TabCount,
        Passes = Passes,
        Mirrored = Mirrored,
    };

    private Tool DefaultTool()
    {
        var candidates = Tools;
        return candidates.Count > 0 ? candidates[0] : Tool.DefaultVBit;
    }

    private static SolidColorBrush ToBrush(SkiaSharp.SKColor c) =>
        new SolidColorBrush(Color.FromArgb(0xFF, c.Red, c.Green, c.Blue));
}
