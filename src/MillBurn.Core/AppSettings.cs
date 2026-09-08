using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MillBurn.Core;

/// <summary>
/// Settings that belong to the person rather than to a board: colours, stock, defaults.
///
/// Separate from the project on purpose. Which colour the bottom copper is drawn in is a fact about
/// this operator's eyes and this monitor, not about the board — so it must not travel with a project
/// or change when one is opened.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Layer colours, as <c>#RRGGBB</c>, keyed by role.
    ///
    /// Overridable because the defaults are conventional rather than universal: greys and mid-tones
    /// that read fine on one monitor disappear on another, and "I can't see that layer" is a
    /// complete blocker rather than a preference.
    /// </summary>
    public ImmutableDictionary<LayerRole, string> LayerColours { get; init; } =
        ImmutableDictionary<LayerRole, string>.Empty;

    /// <summary>
    /// The board material drawn under everything, as <c>#RRGGBB</c>.
    ///
    /// Kept out of <see cref="LayerColours"/> because it is not a role: no file produces it, and
    /// keying it to <see cref="LayerRole.Unknown"/> would recolour every unrecognised file with it.
    /// </summary>
    public string? SubstrateColour { get; init; }

    public double BoardThicknessMm { get; init; } = 1.6;

    public string? LastExportFolder { get; init; }

    /// <summary>Where the window was when it was last closed, or null if it never has been.</summary>
    public WindowPlacement? Window { get; init; }

    /// <summary>Most recently opened projects, newest first.</summary>
    public ImmutableArray<string> RecentProjects { get; init; } = [];

    public AppSettings WithColour(LayerRole role, string hex) =>
        this with { LayerColours = LayerColours.SetItem(role, hex) };

    public AppSettings WithSubstrateColour(string hex) => this with { SubstrateColour = hex };

    public AppSettings WithoutColour(LayerRole role) =>
        this with { LayerColours = LayerColours.Remove(role) };

    public AppSettings WithRecent(string path)
    {
        var trimmed = RecentProjects.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        return this with { RecentProjects = [path, .. trimmed.Take(9)] };
    }

    // ------------------------------------------------------------------ persistence

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PCB_MillBurn",
        "settings.json");

    public static AppSettings LoadOrDefault(string? path = null)
    {
        path ??= DefaultPath;

        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Losing preferences is an inconvenience; failing to start is not. The file is left
            // alone rather than overwritten, so a hand-editable mistake stays recoverable.
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var temporary = path + ".saving";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Json));
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>
/// Where the window was, so it comes back there.
///
/// The maximised flag is kept separately from the size because they answer different questions: a
/// maximised window's own bounds are the whole screen, so storing those and restoring them
/// un-maximised would give a window that fills the display and cannot be told apart from a
/// maximised one. What gets stored is the size it had when it was last a normal window.
/// </summary>
public sealed record WindowPlacement
{
    public required int X { get; init; }

    public required int Y { get; init; }

    public required double Width { get; init; }

    public required double Height { get; init; }

    public bool Maximised { get; init; }
}

/// <summary>
/// Numbers about a loaded board that are worth glancing at.
///
/// All cheap to compute and all answerable from what is already realised — the point is a handful
/// of facts that confirm the right board loaded, not a report nobody reads.
/// </summary>
public sealed record BoardFacts
{
    public required double WidthMm { get; init; }

    public required double HeightMm { get; init; }

    public required int Layers { get; init; }

    public required int Holes { get; init; }

    public required int HoleSizes { get; init; }

    public required double SmallestHoleMm { get; init; }

    public required double LargestHoleMm { get; init; }

    public required int CopperIslands { get; init; }

    public required double CopperAreaMm2 { get; init; }

    public double AreaMm2 => WidthMm * HeightMm;

    /// <summary>How much of the board is copper — a quick sanity check that a pour realised.</summary>
    public double CopperCoverage => AreaMm2 <= 0 ? 0 : CopperAreaMm2 / AreaMm2;
}
