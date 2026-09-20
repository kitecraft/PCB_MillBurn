using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MillBurn.Core;

namespace MillBurn.App.Views;

/// <summary>
/// About: what this build is, whether there is a newer one, and one thing worth watching.
///
/// It used to be a single line in a note dialog. The version still comes first, because that is
/// the one fact a bug report needs from here — but a person who opens About is usually asking one
/// of two questions, "what have I got" and "is there a newer one", and the second had no answer at
/// all.
/// </summary>
public sealed class AboutWindow : Window
{
    private const string LatestApi = "https://api.github.com/repos/kitecraft/PCB_MillBurn/releases/latest";
    private const string ReleasesPage = "https://github.com/kitecraft/PCB_MillBurn/releases/latest";

    private readonly string _version;
    private readonly TravelDemo _demo = new() { Height = 168 };
    private readonly TextBlock _caption = Muted(string.Empty);
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _check = new() { Content = "Check for updates" };
    private readonly Button _open = new() { Content = "Open the release page", IsVisible = false };

    /// <summary>
    /// Internal rather than private so a screenshot can pass a fixed build date: the window is
    /// captured headlessly for the docs, and a date that changes every build changes the picture.
    /// </summary>
    internal AboutWindow(string version, string built)
    {
        _version = version;

        Title = "About PCB_MillBurn";
        AppIcon.Apply(this);
        SizeToContent = SizeToContent.Height;
        Width = 560;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PageBackground");

        Content = Build(version, built);

        _check.Click += async (_, _) => await CheckAsync();
        _open.Click += (_, _) => Open(ReleasesPage);
        _demo.Solved += (_, _) => Caption();

        Caption();
    }

    /// <summary>Shows it, and returns when it is closed.</summary>
    public static Task ShowAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return new AboutWindow(Version(), Built()).ShowDialog(owner);
    }

    /// <summary>"0.1.5", without the "+commit" a build may append.</summary>
    public static string Version()
    {
        var informational = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AboutWindow).Assembly)
            ?.InformationalVersion;

        return informational?.Split('+')[0]
            ?? typeof(AboutWindow).Assembly.GetName().Version?.ToString(3)
            ?? "unknown";
    }

    /// <summary>
    /// When this build was made, from the program's own file.
    ///
    /// The executable's timestamp rather than an attribute, because a single-file build has no
    /// assembly on disk to ask — and because the date somebody needs when comparing two copies of
    /// the app is the date of the file they are running.
    /// </summary>
    private static string Built()
    {
        try
        {
            if (Environment.ProcessPath is { } path && File.Exists(path))
            {
                return File.GetLastWriteTime(path).ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing here is worth failing the window for.
        }

        return "unknown";
    }

    private ScrollViewer Build(string version, string built)
    {
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(20, 18) };

        body.Children.Add(Header(version, built));

        body.Children.Add(new Border
        {
            BorderThickness = new Thickness(1),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSubtle"),
            CornerRadius = new CornerRadius(4),
            Child = _demo,
        });

        body.Children.Add(_caption);

        var shuffle = new Button { Content = "Again, with new holes", FontSize = 11, Padding = new Thickness(8, 3) };
        shuffle.Click += (_, _) => _demo.Shuffle();
        body.Children.Add(shuffle);

        body.Children.Add(new Border { Height = 2 });

        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _check, _open },
        });

        body.Children.Add(Muted(
            "Asks GitHub for the newest release tag when you press it, and never on its own. "
            + "Nothing about you or your boards is sent, and nothing is downloaded."));

        body.Children.Add(_result);

        body.Children.Add(new Border { Height = 2 });
        body.Children.Add(Links());

        return new ScrollViewer { Content = body };
    }

    private static StackPanel Header(string version, string built)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

        text.Children.Add(new TextBlock { Text = "PCB_MillBurn", FontSize = 20, FontWeight = FontWeight.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = $"Version {version} · built {built}",
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 12,
        });
        text.Children.Add(Muted("Gerber to G-code for the mill, SVG for the laser. MIT licensed."));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };

        if (Mark() is { } mark)
        {
            row.Children.Add(new Image { Source = mark, Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Top });
        }

        row.Children.Add(text);

        return row;
    }

    private static Bitmap? Mark()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://MillBurn.App/Assets/millburn.png"));
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private Grid Links()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        row.Children.Add(Link("Release notes", () => Open(ReleasesPage)));
        row.Children.Add(Link("Questions and answers", () => Beside("Help", "faq.html")));
        row.Children.Add(Link("Third-party notices", () => Beside(null, "THIRD-PARTY-NOTICES.md")));

        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        Grid.SetColumn(close, 1);
        grid.Children.Add(row);
        grid.Children.Add(close);

        return grid;
    }

    private static Button Link(string text, Action click)
    {
        var button = new Button { Content = text, FontSize = 11, Padding = new Thickness(8, 3) };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>What the optimizer just did to the holes on screen.</summary>
    private void Caption()
    {
        if (_demo.Plan is not { } plan || plan.InitialTravelMm <= 0)
        {
            _caption.Text = string.Empty;
            return;
        }

        var saved = (plan.InitialTravelMm - plan.TravelMm) / plan.InitialTravelMm;

        _caption.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"The travel optimizer, live: {plan.InitialTravelMm:F0} mm of rapids taken nearest-first (dashed), {plan.TravelMm:F0} mm as it ordered them — {saved:P0} less air. Every export runs this.");
    }

    /// <summary>
    /// Asks GitHub, once, on the button.
    ///
    /// Refuses rather than guesses, like everything else that reports a fact here: an answer that
    /// cannot be read is said to be unreadable, never rounded down to "you are up to date", which
    /// is the one wrong answer that stops a person looking further.
    /// </summary>
    private async Task CheckAsync()
    {
        _check.IsEnabled = false;
        _open.IsVisible = false;
        Say("Asking GitHub…");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

            // GitHub refuses a request with no user agent. It names the app and its version, which
            // is what a user agent is for, and nothing else.
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"PCB_MillBurn/{_version}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var release = ReleaseCheck.Parse(await http.GetStringAsync(new Uri(LatestApi)));

            Say(release is null
                ? $"GitHub answered, but not with a release this could read. Have a look at {ReleasesPage}"
                : Describe(release));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            // Offline is the normal case in a workshop, so it reads as a fact rather than a failure.
            Say($"Could not reach GitHub — {ex.Message.TrimEnd('.')}. The releases page is {ReleasesPage}");
        }
        finally
        {
            _check.IsEnabled = true;
        }
    }

    private string Describe(Release release)
    {
        var state = ReleaseCheck.Compare(_version, release.Version);

        _open.IsVisible = state is UpdateState.Available or UpdateState.Unknown;

        return state switch
        {
            UpdateState.Current => $"You have the latest release, {release.Version}.",
            UpdateState.Available => $"Version {release.Version} is out. You have {_version}.",
            UpdateState.Ahead => $"This build is {_version}; the latest release is {release.Version}. You are ahead of it.",
            _ => $"The latest release calls itself \"{release.Version}\", which this could not compare with {_version}.",
        };
    }

    private void Say(string text)
    {
        _result.Text = text;
        _result.IsVisible = true;
    }

    /// <summary>Opens a file that ships beside the executable, or says where it should have been.</summary>
    private void Beside(string? folder, string file)
    {
        var path = folder is null
            ? Path.Combine(AppContext.BaseDirectory, file)
            : Path.Combine(AppContext.BaseDirectory, folder, file);

        if (!File.Exists(path))
        {
            Say($"{file} is not installed: {path} is missing.");
            return;
        }

        Open(path);
    }

    private void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or IOException or PlatformNotSupportedException)
        {
            Say($"Could not open {target}: {ex.Message}");
        }
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        block[!ForegroundProperty] = new DynamicResourceExtension("TextSecondary");
        return block;
    }
}
