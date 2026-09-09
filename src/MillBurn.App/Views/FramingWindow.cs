using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using MillBurn.Core;

namespace MillBurn.App.Views;

/// <summary>
/// The operator's own lines, run at the start and end of every program.
///
/// Two text boxes and a running commentary. The commentary is the point: G-code typed into a box is
/// checked here, while it is being typed, rather than by the machine — and the two mistakes that
/// would spoil a file silently, a stray bracket and an early <c>M30</c>, are refused outright.
/// </summary>
public sealed class FramingWindow : Window
{
    private readonly TextBox _start;
    private readonly TextBox _end;
    private readonly StackPanel _startIssues = new() { Spacing = 2 };
    private readonly StackPanel _endIssues = new() { Spacing = 2 };
    private readonly Button _save;

    /// <summary>What was chosen, or null if it was called off.</summary>
    public ProgramFraming? Result { get; private set; }

    public FramingWindow(ProgramFraming framing)
    {
        ArgumentNullException.ThrowIfNull(framing);

        Title = "Start and end G-code";
        AppIcon.Apply(this);
        Width = 660;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PageBackground");

        _start = Editor(framing.Start);
        _end = Editor(framing.End);
        _save = new Button { Content = "Save", IsDefault = true };

        _start.TextChanged += (_, _) => Recheck();
        _end.TextChanged += (_, _) => Recheck();

        Content = BuildBody();
        Recheck();
    }

    private static TextBox Editor(string? text) => new()
    {
        Text = text ?? string.Empty,
        AcceptsReturn = true,
        AcceptsTab = false,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Consolas,monospace"),
        MinHeight = 110,
        VerticalContentAlignment = VerticalAlignment.Top,
    };

    private Grid BuildBody()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(18),
        };

        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(new TextBlock
        {
            Text = "Start and end G-code",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
        });
        heading.Children.Add(Token(new TextBlock
        {
            Text = "Added to every program this machine writes. Homing, a work offset, a vacuum "
                + "relay, a spindle dwell — whatever yours needs.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        }, "TextSecondary"));

        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var body = new StackPanel { Spacing = 6, Margin = new Thickness(0, 14) };

        body.Children.Add(Label("At the start"));
        body.Children.Add(Token(new TextBlock
        {
            Text = "Runs before the program sets its own units and distance mode, so nothing here "
                + "can change what the job's coordinates mean.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        }, "TextSecondary"));
        body.Children.Add(_start);
        body.Children.Add(_startIssues);

        body.Children.Add(Label("At the end"));
        body.Children.Add(Token(new TextBlock
        {
            Text = "Runs after the spindle stops and before M30.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        }, "TextSecondary"));
        body.Children.Add(_end);
        body.Children.Add(_endIssues);

        var scroller = new ScrollViewer { Content = body };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var clear = new Button { Content = "Clear both" };
        clear.Click += (_, _) =>
        {
            _start.Text = string.Empty;
            _end.Text = string.Empty;
        };

        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();

        _save.Click += (_, _) =>
        {
            Result = new ProgramFraming
            {
                Start = _start.Text ?? string.Empty,
                End = _end.Text ?? string.Empty,
            };

            Close();
        };

        buttons.Children.Add(clear);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_save);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        return root;
    }

    /// <summary>
    /// Re-runs the checks and blocks saving on anything fatal.
    ///
    /// Refusing to save is the right severity for exactly two things — a comment that does not
    /// close, and a line that ends the program — because both produce a file that looks fine and
    /// does not do what it says. Everything else is shown and allowed: it is somebody's own G-code
    /// for their own machine.
    /// </summary>
    private void Recheck()
    {
        var start = ProgramFraming.Check(_start.Text);
        var end = ProgramFraming.Check(_end.Text, isEnd: true);

        Show(_startIssues, start);
        Show(_endIssues, end);

        _save.IsEnabled = !start.Concat(end).Any(i => i.IsError);
    }

    private static void Show(StackPanel into, IReadOnlyList<FramingIssue> issues)
    {
        into.Children.Clear();

        foreach (var issue in issues)
        {
            into.Children.Add(Token(new TextBlock
            {
                Text = $"line {issue.Line}: {issue.Message}",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            }, issue.IsError ? "DrcViolation" : "TextSecondary"));
        }
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 10, 0, 0),
    };

    /// <summary>
    /// Binds a colour token rather than resolving one: a dialog is built before its theme variant
    /// is applied, so resolving now would pick the light palette and keep it.
    /// </summary>
    private static T Token<T>(T control, string token)
        where T : TextBlock
    {
        control[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(token);
        return control;
    }

    /// <summary>Shows the editor and returns what was chosen, or null if it was called off.</summary>
    public static async Task<ProgramFraming?> AskAsync(Window owner, ProgramFraming framing)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var window = new FramingWindow(framing) { RequestedThemeVariant = owner.ActualThemeVariant };
        await window.ShowDialog(owner);

        return window.Result;
    }
}
