using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace MillBurn.App.Converters;

/// <summary>
/// A brush, or nothing at all — so that the target property keeps whatever it inherits.
///
/// **This exists because binding a null brush is not the same as not binding one.** A check's label
/// is printed in its layer's colour, and a check about the whole board or the whole export has no
/// layer and so no colour: it should read in the panel's ordinary text colour. Binding the null
/// straight to `Foreground` does not do that. It sets the property *to null*, and the label
/// disappears — which is exactly what happened, and what a screenshot of a board with no outline
/// showed: two checks rendering as "— No board outline: …" with the word "Board" simply absent.
///
/// Returning <see cref="AvaloniaProperty.UnsetValue"/> instead leaves the property unset, so the
/// value falls back to the inherited one. That matters more than it sounds: the inherited value
/// comes from a style using a theme resource, so an uncoloured label follows a light/dark toggle,
/// which a brush resolved once and handed to the row could not do.
/// </summary>
public sealed class BrushOrInherited : IValueConverter
{
    public static BrushOrInherited Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value ?? AvaloniaProperty.UnsetValue;

    /// <summary>Display only; nothing edits a check's colour.</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A check's label colour is not editable.");
}
