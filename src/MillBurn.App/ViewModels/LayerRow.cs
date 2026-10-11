using System.Collections.Immutable;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MillBurn.Cam;
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

/// <summary>What a layer can be told it is, by hand (6.31).</summary>
public sealed record RoleChoice(LayerRole Role, string Label)
{
    public override string ToString() => Label;

    /// <summary>
    /// Every role, in the enum's order — which is the order a board is described in: coppers, masks,
    /// legends, paste, outline, holes, then the files that are not layers to be made.
    /// </summary>
    public static IReadOnlyList<RoleChoice> All { get; } =
        [.. Enum.GetValues<LayerRole>().Select(r => new RoleChoice(r, LayerRoleInfo.Label(r)))];
}

/// <summary>
/// One row in the board pane: whether it is shown, what colour, and what it is turned into.
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

    /// <summary>
    /// The operation whose default depth is currently showing, or null once the user has typed one.
    ///
    /// Switching a paste layer from a stencil to mask relief has to bring the depth with it: 0.05 mm
    /// suits isolation and goes clean through 0.02-0.04 mm of soldermask into the copper. Tracking
    /// what the number came from means a depth the operator chose is never quietly overwritten.
    /// </summary>
    private OperationKind? _depthDefaultedFor;

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
        Action outputChanged,
        Action<LayerRow, LayerRole>? roleChanged = null,
        bool roleSetByHand = false)
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
        DepthMm = Nm.ToMillimetres(settings.DepthFor(Operation));
        _depthDefaultedFor = Operation;
        TabCount = settings.TabCount;
        TabWidthNm = settings.TabWidthNm;
        TabHeightNm = settings.TabHeightNm;
        TabEdits = settings.TabEdits;
        DrillGuide = settings.WriteDrillGuide;
        Passes = settings.Passes;
        IsolationWidthMm = Nm.ToMillimetres(settings.IsolationWidthNm);
        Mirrored = settings.MirrorFor(layer.Role);
        Invert = settings.Invert;

        Tool = tools.FirstOrDefault(t => t.Id == settings.ToolId) ?? DefaultTool();
        Swatch = ToBrush(scene?.Style.Fill ?? SkiaSharp.SKColors.Gray);
        IsVisible = scene?.Visible ?? true;

        _visibilityChanged = visibilityChanged;
        _outputChanged = outputChanged;

        // Set before the callback is attached, so building the row does not report a change.
        _selectedRole = RoleChoice.All.First(c => c.Role == layer.Role);
        RoleSetByHand = roleSetByHand;
        _roleChanged = roleChanged;
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

    // ------------------------------------------------------------------ role (6.31)

    private readonly Action<LayerRow, LayerRole>? _roleChanged;

    private RoleChoice? _selectedRole;

    /// <summary>The roles the picker offers. Empty on a row the scene invented, which has none.</summary>
    public IReadOnlyList<RoleChoice> Roles => Layer is null ? [] : RoleChoice.All;

    /// <summary>Whether this row can be told what it is: a real layer, not the substrate or a backplot.</summary>
    public bool CanSetRole => Layer is not null;

    /// <summary>
    /// What this layer is, as the picker shows it. Choosing another reports it; the view model
    /// changes the project and rebuilds, and this row is replaced by one built for the new role.
    /// </summary>
    public RoleChoice? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (value is null || value == _selectedRole)
            {
                return;
            }

            _selectedRole = value;
            OnPropertyChanged();
            _roleChanged?.Invoke(this, value.Role);
        }
    }

    /// <summary>
    /// Puts the picker back to the role the layer really has, without reporting a change — for a
    /// choice that was refused. The row is not rebuilt for a refusal, so without this it went on
    /// showing the role that was turned down, and choosing the real one back then counted as a
    /// change and marked it as set by hand. Found by review.
    /// </summary>
    public void ShowRole(LayerRole role)
    {
        if (RoleChoice.All.FirstOrDefault(c => c.Role == role) is { } choice && choice != _selectedRole)
        {
            _selectedRole = choice;
            OnPropertyChanged(nameof(SelectedRole));
        }
    }

    /// <summary>The operator chose this role, so detection will not change it again.</summary>
    public bool RoleSetByHand { get; }

    /// <summary>
    /// The role was read off the file's name, because the file did not say — and nobody has
    /// confirmed it since. A guess has to look like a guess (`LayerRoles`), and on a project board it
    /// did not: every layer read as declared.
    /// </summary>
    public bool RoleGuessed => Layer?.RoleGuessed == true;

    /// <summary>
    /// Confirms a guessed role as it stands. The picker cannot: choosing the item a combo box already
    /// shows is not a change, so it raises nothing — and confirming a guess is the commonest answer
    /// to "is this right?". Found by review.
    /// </summary>
    [RelayCommand]
    private void ConfirmRole()
    {
        if (SelectedRole is { } role)
        {
            _roleChanged?.Invoke(this, role.Role);
        }
    }

    /// <summary>How the role was arrived at, under the picker.</summary>
    public string RoleSource => RoleSetByHand
        ? "Set by hand. Refreshing the folder keeps it."
        : RoleGuessed
            ? "Guessed from the file's name — the file does not say what it is. Check it."
            : "Read from the file.";

    public string Id { get; }

    public string Label { get; }

    public string FileName => Layer?.FileName ?? Id;

    public IReadOnlyList<OutputChoice> Outputs { get; }

    public IReadOnlyList<Tool> AllTools { get; }

    /// <summary>True when this layer can produce a file at all — the dropdown is hidden otherwise.</summary>
    public bool CanExport => Outputs.Count > 1;

    /// <summary>
    /// Every drawn layer can be recoloured, whether or not a file produced it — so there is no
    /// condition on the swatch. The backplot layers need it most: the palette picks hues the board
    /// does not use, but which hues those are depends on which layers are showing and on the
    /// monitor in front of the operator.
    /// </summary>

    // ------------------------------------------------------------------ view

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial IBrush Swatch { get; set; }

    /// <summary>
    /// Whether this row is showing its settings.
    ///
    /// On the row rather than in the view so it survives a rebuild — changing a colour or taking a
    /// refresh re-creates every row, and having them all snap shut underneath you would make the
    /// panel feel broken.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>The disclosure marker. A string beats a converter for one character.</summary>
    public string Chevron => IsExpanded ? "▾" : "▸";

    partial void OnIsExpandedChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(Chevron));
    }

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

    /// <summary>
    /// Sets the output by kind, ignoring one this layer cannot produce — a bulk action walks every
    /// row, and a drill file has no SVG to offer.
    /// </summary>
    public void SetOutput(OutputKind kind)
    {
        if (Outputs.FirstOrDefault(o => o.Kind == kind) is { } choice)
        {
            SelectedOutput = choice;
        }
    }

    [ObservableProperty]
    public partial Tool Tool { get; set; }

    /// <summary>How far past the back face to go. Only means anything when the cut goes through.</summary>
    [ObservableProperty]
    public partial double BreakThroughMm { get; set; }

    [ObservableProperty]
    public partial double DepthMm { get; set; }

    [ObservableProperty]
    public partial int TabCount { get; set; }

    /// <summary>
    /// Outline only: the gap each tab leaves, or null to size it from the board.
    ///
    /// Not an <c>ObservableProperty</c> and not bound to anything: these three are set together by
    /// the tabs window, through <see cref="SetTabs"/>, and a half-applied trio would plan a board
    /// nobody asked for.
    /// </summary>
    public long? TabWidthNm { get; private set; }

    /// <summary>Outline only: the material left under each tab, or null to take it from the thickness.</summary>
    public long? TabHeightNm { get; private set; }

    /// <summary>Outline only: the tabs whose size was set by hand, by their place in the planned order.</summary>
    public ImmutableArray<TabEdit> TabEdits { get; private set; } = [];

    /// <summary>
    /// Takes what the tabs window chose.
    ///
    /// All three at once, and one notification: the preview is rebuilt from this, and rebuilding it
    /// three times to arrive at the same board is the drag-rebuild fault sprint 2 story 6 fixed.
    /// </summary>
    public void SetTabs(long? widthNm, long? heightNm, ImmutableArray<TabEdit> edits)
    {
        TabWidthNm = widthNm;
        TabHeightNm = heightNm;
        TabEdits = edits;

        OnPropertyChanged(nameof(Detail));
        _outputChanged?.Invoke();
    }

    /// <summary>Drilling only: write the page that explains how to run the program.</summary>
    [ObservableProperty]
    public partial bool DrillGuide { get; set; } = true;

    /// <summary>
    /// Whether this layer's own toolpath is drawn.
    ///
    /// Separate from <see cref="IsVisible"/>, which is the copper. They have to be separate: the
    /// artwork sits under the toolpath and hides it, so "show me this layer's cuts" means turning
    /// one off and the other on.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowToolpath { get; set; } = true;

    /// <summary>
    /// The scene layers carrying this layer's program — one per kind of move.
    ///
    /// A row owns several, because *which layer* and *what kind of move* are independent axes and
    /// the scene has one layer per combination.
    /// </summary>
    private IReadOnlyList<BoardSceneLayer> _toolpath = [];

    /// <summary>True once a preview exists and this layer produced a program.</summary>
    public bool HasToolpath => _toolpath.Count > 0;

    /// <summary>Attaches the scene layers this row's toolpath toggle governs.</summary>
    public void AttachToolpath(IReadOnlyList<BoardSceneLayer> layers)
    {
        _toolpath = layers;
        OnPropertyChanged(nameof(HasToolpath));
    }

    /// <summary>Every scene layer this row's toolpath toggle governs.</summary>
    public IReadOnlyList<BoardSceneLayer> ToolpathLayers => _toolpath;

    partial void OnShowToolpathChanged(bool value)
    {
        _ = value;
        _visibilityChanged?.Invoke();
    }

    /// <summary>
    /// What this layer becomes, in two or three words, for the collapsed row.
    ///
    /// The row previously said nothing at all about export unless it was expanded — while the
    /// checkbox that *was* visible controlled something else entirely, and had to carry a tooltip
    /// denying it.
    /// </summary>
    public string ExportBadge => Output switch
    {
        OutputKind.Gcode => "G-code",
        OutputKind.Svg => "SVG",
        _ => string.Empty,
    };

    public bool HasExportBadge => Output != OutputKind.None;

    /// <summary>Which of the two badge styles to use, as a class name the XAML can select on.</summary>
    public string BadgeClass => Output switch
    {
        OutputKind.Gcode => "gcode",
        OutputKind.Svg => "svg",
        _ => "none",
    };

    /// <summary>
    /// Isolation only, and only where a project saved before widths existed set one. Not shown.
    /// </summary>
    public int Passes { get; private set; } = 1;

    /// <summary>
    /// Isolation only: how wide a moat to clear either side of the copper, in millimetres.
    ///
    /// The control, because it is the thing the board ends up having. How many laps that takes is
    /// arithmetic — see <see cref="IsolationPasses"/> — and typing a lap count means doing that
    /// arithmetic in your head with a number (the bit's cut width) that is itself derived.
    /// </summary>
    [ObservableProperty]
    public partial double IsolationWidthMm { get; set; }

    /// <summary>What that width costs, in passes, with this tool at this depth.</summary>
    public int IsolationPasses => IsolationWidthMm <= 0
        ? Math.Max(1, Passes)
        : IsolationOptions.PassesFor(
            Nm.FromMillimetres(IsolationWidthMm),
            Tool.WidthAtDepth(Nm.FromMillimetres(DepthMm)),
            new IsolationOptions().Overlap,
            0);

    /// <summary>What those passes really clear, which is the width rounded up to a whole lap.</summary>
    public double IsolationAchievedMm => Nm.ToMillimetres(IsolationOptions.ClearedBy(
        IsolationPasses,
        Tool.WidthAtDepth(Nm.FromMillimetres(DepthMm)),
        new IsolationOptions().Overlap,
        0));

    /// <summary>The arithmetic, said out loud under the control that drives it.</summary>
    public string IsolationExplanation => string.Create(
        CultureInfo.InvariantCulture,
        $"{IsolationPasses} pass{(IsolationPasses == 1 ? string.Empty : "es")} of "
        + $"{Nm.ToMillimetreString(Tool.WidthAtDepth(Nm.FromMillimetres(DepthMm)), 3)} mm "
        + $"clears {IsolationAchievedMm:F3} mm");

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

    /// <summary>
    /// SVG only: burn everything except this layer, out to the board edge.
    ///
    /// Two opposite jobs use the same shapes. Etching a painted board wants the resist cleared off
    /// everywhere the acid should reach — the complement of the copper. Clearing soldermask off
    /// pads wants the openings themselves. Which one is the target is a fact about the process, not
    /// about the file.
    /// </summary>
    [ObservableProperty]
    public partial bool Invert { get; set; }

    public OperationKind Operation =>
        Role is null ? OperationKind.None : LayerOperations.For(Role.Value, Output);

    public bool IsGcode => Output == OutputKind.Gcode;

    public bool IsSvg => Output == OutputKind.Svg;

    public bool NeedsBreakThrough => IsGcode && LayerOperations.GoesThrough(Operation);

    public bool NeedsDepth => IsGcode
        && Operation is OperationKind.Isolation or OperationKind.Engrave or OperationKind.Pocket;

    public bool NeedsTabs => IsGcode && Operation == OperationKind.Outline;

    /// <summary>
    /// Drilling is the only operation with tool changes in it, so it is the only one where the
    /// program alone does not tell the operator what to do.
    /// </summary>
    public bool NeedsDrillGuide => IsGcode && Operation == OperationKind.Drilling;

    public bool NeedsPasses => IsGcode && Operation == OperationKind.Isolation;

    /// <summary>
    /// Anything that produces a file can be mirrored — drills and stencils included.
    ///
    /// Only once it produces one. This used to be offered on every layer that *could* export, on the
    /// grounds that the choice should be visible while deciding rather than appear after; but a tick
    /// on a layer set to Not exported changes nothing, and <see cref="Detail"/> had already stopped
    /// saying "mirrored" in that state. Every other option in the row — tabs, depth, invert — waits
    /// for an output, so this one waiting too is what the rest of the row led the operator to expect.
    /// </summary>
    public bool NeedsMirror => IsGcode || IsSvg;

    /// <summary>Only a drawing can be inverted; a toolpath has nothing to be the complement of.</summary>
    public bool NeedsInvert => IsSvg;

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

        if (_depthDefaultedFor is { } was && was != Operation)
        {
            _depthDefaultedFor = Operation;
            DepthMm = Nm.ToMillimetres(LayerOperations.DefaultDepthNm(Operation));
        }

        RaiseDerived();
        _outputChanged?.Invoke();
    }

    partial void OnToolChanged(Tool value)
    {
        _ = value;

        // A different bit cuts a different width, so the same moat takes a different number of
        // passes. Nothing the operator typed has changed and the answer has.
        RefreshIsolation();

        // **And on an outline layer it re-plans the tabs, so the hand-set sizes go with it.** Found
        // by `/code-review`, and it is the third door onto the same hazard: the count clears them
        // just below, and the tabs window clears them when the board-wide width changes. The cutter
        // is the one nobody had thought of — the profile is offset by its radius before the tabs are
        // placed, so a wider bit lengthens every straight edge, moves `DefaultWidthFor`, and changes
        // which edges can hold how many. An edit names a tab by its place in that order.
        if (Operation == OperationKind.Outline)
        {
            TabEdits = [];
        }

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
        // Typed by hand, so it is theirs now and no operation change may replace it.
        if (_depthDefaultedFor is { } was
            && Nm.FromMillimetres(value) != LayerOperations.DefaultDepthNm(was))
        {
            _depthDefaultedFor = null;
        }

        // For a V-bit the depth *is* the cut width, so it moves the pass count too.
        RefreshIsolation();
        _outputChanged?.Invoke();
    }

    partial void OnTabCountChanged(int value)
    {
        _ = value;

        // **A change of count throws the sizes set by hand away**, and a `/code-review` pass is
        // why. An edit names a tab by its place in the planned order, and the order is a function
        // of the count: the planner gives one tab to each edge before any edge gets a second, so
        // going from four to six re-plans every position. The edit then lands on whichever tab now
        // sorts third — an unexplained wide tab somewhere nobody chose — and going the other way
        // drops it silently.
        //
        // Keeping them would need a tab to have an identity that survives re-planning, which is
        // 06 §6.4's "move a tab by hand" and is not built. Until it is, the honest answer is that
        // asking for a different number of tabs is asking for a different set of tabs.
        TabEdits = [];

        OnPropertyChanged(nameof(Detail));
        _outputChanged?.Invoke();
    }

    partial void OnDrillGuideChanged(bool value)
    {
        _ = value;
        _outputChanged?.Invoke();
    }

    partial void OnIsolationWidthMmChanged(double value)
    {
        _ = value;
        RefreshIsolation();
        _outputChanged?.Invoke();
    }

    /// <summary>
    /// Everything derived from the width, the depth and the tool. Called from all three, because
    /// changing any of them changes how many passes the same moat takes.
    /// </summary>
    private void RefreshIsolation()
    {
        OnPropertyChanged(nameof(IsolationPasses));
        OnPropertyChanged(nameof(IsolationAchievedMm));
        OnPropertyChanged(nameof(IsolationExplanation));
        OnPropertyChanged(nameof(Detail));
    }

    partial void OnMirroredChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(Detail));
        _outputChanged?.Invoke();
    }

    partial void OnInvertChanged(bool value)
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
            nameof(NeedsDepth), nameof(NeedsTabs), nameof(NeedsPasses), nameof(NeedsMirror),
            nameof(NeedsDrillGuide),
            nameof(NeedsInvert), nameof(Tools), nameof(Detail), nameof(TargetName),
            nameof(ExportBadge), nameof(HasExportBadge), nameof(BadgeClass),
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
            var negative = Invert && IsSvg ? " · inverted" : string.Empty;

            var counts = string.Create(
                CultureInfo.InvariantCulture,
                $"{layer.ObjectCount:N0} objects · {layer.AreaMm2:F2} mm²");

            var size = string.Create(
                CultureInfo.InvariantCulture,
                $"{Nm.ToMillimetreString(layer.Bounds.Width, 2)} × {Nm.ToMillimetreString(layer.Bounds.Height, 2)} mm");

            var cutWidth = Nm.ToMillimetreString(Tool.WidthAtDepth(Nm.FromMillimetres(DepthMm)), 3);
            var holes = layer.Drill?.Hits.Count ?? 0;
            var sizeCount = layer.Drill?.Tools.Count ?? 0;
            var sizes = sizeCount == 1
                ? "1 size"
                : string.Create(CultureInfo.InvariantCulture, $"{sizeCount} sizes");
            var cutter = Nm.ToMillimetreString(Tool.DiameterNm, 2);
            var passes = IsolationPasses == 1
                ? "1 pass"
                : string.Create(CultureInfo.InvariantCulture, $"{IsolationPasses} passes");

            return Operation switch
            {
                OperationKind.None => $"{size} · {counts}",

                OperationKind.Isolation => string.Create(
                    CultureInfo.InvariantCulture,
                    $"Isolates {IsolationAchievedMm:F3} mm · {passes} of {cutWidth} mm at {DepthMm:F3} mm deep{flip}"),

                OperationKind.Engrave when IsGcode => $"Traces the legend {cutWidth} mm wide{flip}",

                OperationKind.Pocket => string.Create(
                    CultureInfo.InvariantCulture,
                    $"Clears the mask off {layer.RingCount} openings, {cutWidth} mm per pass at {DepthMm:F3} mm{flip}"),

                OperationKind.Drilling => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{holes} holes, {sizes} · {BreakThroughMm:F2} mm through the back{flip}"),

                OperationKind.Outline => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{cutter} mm cutter outside the profile · {TabCount} tabs · {BreakThroughMm:F2} mm through{flip}"),

                _ => $"{size} · {counts}{flip}{negative}",
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
        TabWidthNm = TabWidthNm,
        TabHeightNm = TabHeightNm,
        TabEdits = TabEdits,
        WriteDrillGuide = DrillGuide,
        Passes = Passes,
        IsolationWidthNm = Nm.FromMillimetres(IsolationWidthMm),
        Mirrored = Mirrored,
        Invert = Invert,
    };

    /// <summary>
    /// The bit this layer starts out being cut with. Shared with the planner, so the window and the
    /// command line never pick different tools for the same untouched layer.
    /// </summary>
    private Tool DefaultTool() => LayerOperations.DefaultToolFor(Operation, AllTools);

    internal static SolidColorBrush ToBrush(SkiaSharp.SKColor c) =>
        new SolidColorBrush(Color.FromArgb(0xFF, c.Red, c.Green, c.Blue));
}
