using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace MillBurn.App.Views;

/// <summary>
/// Picks the colour a layer is drawn in.
///
/// Worth a dialog of its own because "I can't see that layer" is a complete blocker rather than a
/// preference: the default palette is conventional, and mid-tones that read fine on one monitor
/// vanish on another. The presets are there because most of the time the answer is "something
/// brighter", not a specific colour.
/// </summary>
public sealed class ColourWindow : Window
{
    private readonly ColorView _view;

    public Color? Result { get; private set; }

    public ColourWindow(string layerLabel, Color current)
    {
        Title = "Colour — " + layerLabel;
        AppIcon.Apply(this);
        Width = 420;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PageBackground");

        _view = new ColorView
        {
            Color = current,
            IsAlphaEnabled = false,
            IsColorPaletteVisible = true,
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(16),
        };

        var heading = new TextBlock
        {
            Text = layerLabel,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        Grid.SetRow(_view, 1);
        root.Children.Add(_view);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var reset = new Button { Content = "Use default" };
        ToolTip.SetTip(reset, "Put this layer back to the built-in palette colour");
        reset.Click += (_, _) =>
        {
            Result = null;
            Reset = true;
            Close();
        };

        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();

        var apply = new Button { Content = "Apply", IsDefault = true };
        apply.Click += (_, _) =>
        {
            Result = _view.Color;
            Close();
        };

        buttons.Children.Add(reset);
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }

    /// <summary>True when the user asked for the built-in colour back rather than a new one.</summary>
    public bool Reset { get; private set; }

    /// <summary>
    /// Returns the chosen colour, or null. The second value distinguishes "cancelled" from "put it
    /// back to the default", which are the same null and mean opposite things.
    /// </summary>
    public static async Task<(Color? Colour, bool Reset)> AskAsync(
        Window owner, string layerLabel, Color current)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var window = new ColourWindow(layerLabel, current)
        {
            RequestedThemeVariant = owner.ActualThemeVariant,
        };

        await window.ShowDialog(owner);
        return (window.Result, window.Reset);
    }
}
