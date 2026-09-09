using Avalonia.Controls;
using Avalonia.Platform;

namespace MillBurn.App.Views;

/// <summary>
/// The window icon, decoded once and shared by every window.
///
/// Set in code rather than in each window's XAML because three of the five windows are built in
/// code, and an icon that appears on some windows and not others looks like a bug rather than a
/// decision. The <c>.ico</c> beside it is a separate thing: that one is burned into the executable
/// by MSBuild so the file has a face in Explorer before it is ever run.
/// </summary>
internal static class AppIcon
{
    private static readonly Lazy<WindowIcon?> Loaded = new(Load, isThreadSafe: true);

    /// <summary>Null only if the asset could not be decoded, which no window should die over.</summary>
    public static WindowIcon? Value => Loaded.Value;

    /// <summary>Gives a window the app icon. Safe to call from a constructor.</summary>
    public static void Apply(Window window)
    {
        if (Value is { } icon)
        {
            window.Icon = icon;
        }
    }

    private static WindowIcon? Load()
    {
        try
        {
            using var stream = AssetLoader.Open(
                new Uri("avares://MillBurn.App/Assets/millburn.png"));

            return new WindowIcon(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or InvalidOperationException)
        {
            // A missing or unreadable icon is a cosmetic problem. Taking the whole app down over
            // one would be a considerably larger one.
            return null;
        }
    }
}
