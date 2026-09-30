using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Pipeline;

using static System.FormattableString;

namespace MillBurn.App.Views;

/// <summary>
/// Where the electrical findings live, in full, for a board somebody has asked about.
///
/// **This is the room the CHECK list does not have.** That list is read at a glance while a mill is
/// running, so it caps what it says — eight groups, six names each — and it is fed one program at a
/// time, so a two-sided board answers in two places with nothing collecting them. A full account of
/// a board and a list read at a glance are different documents, and trying to be both is what was
/// straining.
///
/// So the list stays short and points here, and here there is room for the answers a warning line
/// cannot carry: **where a gap actually is** rather than which copper it is in, and story 3's split
/// shown as places rather than counted as a tally.
///
/// **Every number on this page is about a specific cut**, which is why the width is printed beside
/// each layer rather than assumed. The same board is sound at 0.127 mm and full of shorts at 0.4,
/// and a finding without its width is a finding an operator could act on wrongly.
/// </summary>
public sealed class FindingsWindow : Window
{
    /// <summary>
    /// How many places to list under one group before counting the rest.
    ///
    /// **Much higher than the warning line's cap, and that is the whole point of this window** —
    /// `ElectricalFindings.MaxNamedJoins` stops at eight because a panel read at a glance cannot
    /// carry more. This is not that panel. It still has a limit, because a ground pour shorted
    /// along a whole edge can bridge in hundreds of places and a list that long is not read either;
    /// what changes is where the limit sits.
    /// </summary>
    private const int MostPlaces = 40;

    public FindingsWindow(BoardFindings.Result findings, string board, Func<LayerRole, IBrush> colourOf)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(colourOf);

        Title = "Findings — " + board;
        AppIcon.Apply(this);
        Width = 760;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PageBackground");

        var body = new StackPanel { Spacing = 0, Margin = new Thickness(22, 18, 22, 18) };

        body.Children.Add(Token(new TextBlock
        {
            Text = "What the copper does at the cut this export would make",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
        }, "TextHeadline"));

        body.Children.Add(Token(new TextBlock
        {
            Text = Summary(findings),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 14),
            TextWrapping = TextWrapping.Wrap,
        }, "TextSecondary"));

        // **A run that stopped early must never read as a clean board.** The same distinction
        // sprint 2's story 3 was about, and it matters more here than anywhere else in the
        // application: this is the window somebody opens to satisfy themselves a board is sound.
        if (findings.Cancelled)
        {
            body.Children.Add(Banner(
                "This run was stopped before it finished. What is below is part of the answer, not "
                + "all of it — nothing here says the rest of the board is clean."));
        }

        if (findings.Layers.Count == 0)
        {
            body.Children.Add(Token(new TextBlock
            {
                Text = "No layer on this board is set to be isolated, so there is no cut to check "
                    + "copper against. Set a copper layer to G-code and ask again.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            }, "TextSecondary"));
        }

        foreach (var layer in findings.Layers)
        {
            body.Children.Add(ForLayer(layer, colourOf));
        }

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = body,
        };
    }

    private static string Summary(BoardFindings.Result findings)
    {
        var shorts = findings.Layers.Sum(l => l.Shorts.Count);
        var groups = findings.Layers.Sum(l => l.Joins.Count);
        var layers = findings.Layers.Count == 1 ? "1 layer" : Invariant($"{findings.Layers.Count} layers");
        var took = Invariant($"{findings.Elapsed.TotalSeconds:F2} s");

        // **"Nothing shorted" has to mean it**, so the groups are consulted as well as the places.
        // A join whose bridge could not be located contributes no place, and a headline counting
        // only places would have said "nothing shorted" over a board with a group on it — the same
        // fault as the per-layer line below, one level up and read first.
        if (groups == 0 && shorts == 0)
        {
            return Invariant($"{layers} · nothing shorted · {took}");
        }

        // Places when there are any, groups when the bridges could not be found. Counting both into
        // one number would add two different units together.
        return shorts > 0
            ? Invariant($"{layers} · {shorts} place(s) where copper stays joined · {took}")
            : Invariant($"{layers} · {groups} group(s) left connected · {took}");
    }

    private static StackPanel ForLayer(BoardFindings.Layer layer, Func<LayerRole, IBrush> colourOf)
    {
        var panel = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 0, 18) };

        // **The layer's own colour from the same place the CHECK panel gets it**, which is the
        // view model — where the operator's override is applied and where a colour too dark or too
        // pale to read as text is brought into range. Asking `BoardPalette` directly, as this did,
        // skips both: an operator who had set top copper to green saw green in the layer list, green
        // in the panel, and the built-in orange in the window those two point at.
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        heading.Children.Add(new TextBlock
        {
            Text = layer.Label,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = colourOf(layer.Role),
        });

        heading.Children.Add(Token(new TextBlock
        {
            Text = Invariant($"cut {Nm.ToMillimetreString(layer.WidthNm, 3)} mm wide"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        }, "TextDisabled"));

        panel.Children.Add(heading);

        panel.Children.Add(Token(new TextBlock
        {
            Text = layer.Verdict,
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        }, "TextSecondary"));

        if (!layer.Ran)
        {
            // Why it could not look, rather than silence. A layer that was not checked reading as a
            // layer that came back clean is the failure `NetCheck.Silent` exists to prevent.
            panel.Children.Add(Banner("Not checked: " + layer.Silent));
            return panel;
        }

        // **The groups decide whether a layer is clear, not the places.** Tested on the located
        // gaps alone, this said "everything comes apart" in green on a layer the check had just
        // reported shorted — two lines under a verdict reading "N left connected in M group(s)".
        //
        // They can disagree, and the case is ordinary rather than exotic: `Gaps` skips a region
        // holding a single piece of artwork, because a bridge is something between two pieces. Two
        // nets attributed to *one* island — a net tie, a zero-ohm link, a pour the exporter
        // flattened — is a join with no bridge to find, so `Shorts` is empty while `Joins` is not.
        // This is the one window whose job is to let somebody satisfy themselves a board is sound.
        if (layer.Joins.Count == 0
            && layer.Shorts.Count == 0
            && layer.SameNet.Count == 0
            && layer.Nameless.Count == 0)
        {
            panel.Children.Add(Token(new TextBlock
            {
                Text = Invariant($"Every one of the {layer.NetsSeen} nets on this layer comes apart at this width."),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            }, "StatusSuccess"));

            return panel;
        }

        // **Shorts first, uncapped in groups.** The warning line stops at eight because a panel read
        // at a glance cannot carry more; this is the page that can.
        if (layer.Joins.Count > 0)
        {
            panel.Children.Add(Section(
                Invariant($"Left connected — {layer.Joins.Count} group(s)"),
                "DrcViolation",
                "Two or more nets sharing one piece of copper that this cut cannot divide. The "
                + "places below are where the copper actually bridges."));

            foreach (var join in layer.Joins)
            {
                panel.Children.Add(Group(join, layer.Shorts));
            }
        }

        // **Story 3's split, shown rather than counted**, which is the other half of why this window
        // exists. These two used to be one number covering two things that mean opposite amounts of
        // trouble.
        if (layer.Nameless.Count > 0)
        {
            panel.Children.Add(Section(
                Invariant($"Copper with no net name — {layer.Nameless.Count} place(s)"),
                "StatusWarning",
                "The cut cannot get between these either, and nothing here can say whether it "
                + "matters: the copper carries no net attribute to check against. Look at them."));

            panel.Children.Add(Places(layer.Nameless));
        }

        if (layer.SameNet.Count > 0)
        {
            panel.Children.Add(Section(
                Invariant($"One net meeting itself — {layer.SameNet.Count} place(s)"),
                "TextSecondary",
                "Nothing is shorted by these: the copper either side carries the same net, so it "
                + "was one conductor before the cut and is one after it. Listed because copper left "
                + "where the design wanted none still matters for soldering and for probing."));

            panel.Children.Add(Places(layer.SameNet));
        }

        return panel;
    }

    /// <summary>One group of nets, and the places the copper holding them bridges.</summary>
    private static StackPanel Group(NetJoin join, IReadOnlyList<NetGap> shorts)
    {
        var panel = new StackPanel { Spacing = 1, Margin = new Thickness(12, 4, 0, 6) };

        // Every name, uncapped. `NetJoin.Describe` caps at six for the warning line; one piece of
        // copper can hold twenty-three nets and this is the page where all of them fit.
        panel.Children.Add(Token(new TextBlock
        {
            Text = string.Join(" · ", join.Nets),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        }, "TextPrimary"));

        // **The bridges in this group's own copper**, matched on the whole region's net names
        // rather than on the two either side of the bridge.
        //
        // Matching on `Between` is the obvious thing and it is wrong: a ground pour reaches many
        // separate regions, so a GND–USHIELD bridge gets filed under every group that happens to
        // hold both names. The first version of this window did that and listed one bridge under
        // two groups — a board made to look worse than it is, by a view whose entire job is to be
        // the trustworthy account.
        var here = shorts
            .Where(g => g.RegionNets.SequenceEqual(join.Nets, StringComparer.Ordinal))
            .ToList();

        if (here.Count == 0)
        {
            // **Says so rather than showing nothing.** The groups come from merging the whole layer
            // at once and the places from growing pieces one at a time; they agree on every board
            // tested, and a group whose bridge could not be located is a gap in this window's
            // account of the board, not an absence of one on the board.
            panel.Children.Add(Token(new TextBlock
            {
                Text = "The bridge between these could not be located — they share one piece of "
                    + "copper, but the narrow place was not found. Treat this group as unverified.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0),
            }, "StatusWarning"));

            return panel;
        }

        // The names beside each place only when the group holds more than the two they would
        // repeat: on "+5V · GND" every bridge is +5V and GND and saying so under each one is noise,
        // while on a group of twenty-three nets "which two meet here" is the question a bare
        // coordinate raises.
        panel.Children.Add(Places(here, withNames: join.Nets.Count > 2));

        return panel;
    }

    /// <summary>A list of coordinates, capped and counted.</summary>
    private static StackPanel Places(IReadOnlyList<NetGap> gaps, bool withNames = false)
    {
        var panel = new StackPanel { Spacing = 0, Margin = new Thickness(12, 1, 0, 2) };

        foreach (var gap in gaps.Take(MostPlaces))
        {
            var where = Invariant(
                $"{Nm.ToMillimetreString(gap.At.X, 2)}, {Nm.ToMillimetreString(gap.At.Y, 2)} mm");

            var text = withNames && gap.Between.Count >= 2
                ? Invariant($"{where}  ·  {string.Join(" and ", gap.Between)}")
                : where;

            panel.Children.Add(Token(new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                TextWrapping = TextWrapping.Wrap,
            }, "TextSecondary"));
        }

        if (gaps.Count > MostPlaces)
        {
            panel.Children.Add(Token(new TextBlock
            {
                Text = Invariant($"…and {gaps.Count - MostPlaces} more"),
                FontSize = 11,
                Margin = new Thickness(0, 1, 0, 0),
            }, "TextDisabled"));
        }

        return panel;
    }

    private static StackPanel Section(string title, string token, string blurb)
    {
        var panel = new StackPanel { Spacing = 1, Margin = new Thickness(0, 8, 0, 2) };

        panel.Children.Add(Token(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
        }, token));

        panel.Children.Add(Token(new TextBlock
        {
            Text = blurb,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        }, "TextSecondary"));

        return panel;
    }

    private static Border Banner(string text)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(10, 6, 8, 7),
            Margin = new Thickness(0, 2, 0, 10),
        };

        border[!Border.BorderBrushProperty] = new DynamicResourceExtension("StatusWarning");
        border[!Border.BackgroundProperty] = new DynamicResourceExtension("CheckPointedFill");

        border.Child = Token(new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        }, "TextPrimary");

        return border;
    }

    private static T Token<T>(T control, string token)
        where T : TextBlock
    {
        control[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(token);
        return control;
    }
}
