using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MillBurn.Core;

/// <summary>
/// The tools this machine has, saved between sessions.
///
/// **The library is where you pick a tool from; it is not where a job's tool lives.** A project
/// embeds a copy of what it was cut with, for the same reason it embeds its Gerbers: a project that
/// pointed at the library by name would have its toolpaths silently change the day someone adjusted
/// a tip width to suit a newly bought bit. Old jobs must keep cutting what they cut.
///
/// That does mean a project and the library can drift apart, and that is the useful state to be
/// able to see rather than one to prevent — hence stable ids, so the app can say "this project used
/// a 0.10 mm tip; your library now says 0.12".
/// </summary>
public sealed class ToolLibrary
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public required ImmutableArray<Tool> Tools { get; init; }

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IEnumerable<Tool> OfKind(ToolKind kind) => Tools.Where(t => t.Kind == kind);

    /// <summary>Finds by id first, then by name — ids are stable, names are what people type.</summary>
    public Tool? Find(string idOrName)
    {
        ArgumentNullException.ThrowIfNull(idOrName);

        if (Guid.TryParse(idOrName, out var id))
        {
            var byId = Tools.FirstOrDefault(t => t.Id == id);
            if (byId is not null)
            {
                return byId;
            }
        }

        return Tools.FirstOrDefault(t => t.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase))
            ?? Tools.FirstOrDefault(t => t.Name.Contains(idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public ToolLibrary With(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var index = Tools.IndexOf(Tools.FirstOrDefault(t => t.Id == tool.Id)!);
        return new ToolLibrary
        {
            Tools = index >= 0 ? Tools.SetItem(index, tool) : Tools.Add(tool),
        };
    }

    public ToolLibrary Without(Guid id) =>
        new() { Tools = [.. Tools.Where(t => t.Id != id)] };

    // ------------------------------------------------------------------ persistence

    /// <summary>Where the library lives when the caller does not say.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PCB_MillBurn",
        "tools.json");

    /// <summary>Loads the library, falling back to the built-ins when there is not one yet.</summary>
    public static ToolLibrary LoadOrDefault(string? path = null)
    {
        path ??= DefaultPath;

        if (!File.Exists(path))
        {
            return Default;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<ToolLibrary>(File.ReadAllText(path), Json);
            return loaded is { Tools.IsDefaultOrEmpty: false } ? loaded : Default;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // A corrupt library must not stop the app opening. The built-ins are a working set, and
            // the file is left alone rather than overwritten, so nothing is lost that could be
            // recovered by hand.
            return Default;
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

    // ------------------------------------------------------------------ built-ins

    /// <summary>
    /// A working set for someone who has just installed the app: the bits a PCB actually needs.
    ///
    /// Traces want a V-bit, because cut width follows depth and a narrow enough end mill does not
    /// exist at a sane price. The outline wants a flat end mill, because a V cutting 1.9 mm deep
    /// would be enormously wide at the top and would take the board with it. They are different
    /// operations with genuinely different tools, which is the whole reason the selection is
    /// per-operation rather than per-job.
    /// </summary>
    public static ToolLibrary Default { get; } = new()
    {
        Tools =
        [
            Tool.DefaultVBit,
            new Tool
            {
                Id = new Guid("00000000-0000-0000-0000-0000000060b2"),
                Name = "60° V-bit, 0.2 mm tip",
                Kind = ToolKind.VBit,
                TipNm = Nm.FromMillimetres(0.2),
                IncludedAngleDegrees = 60,
                MaxDepthNm = Nm.FromMillimetres(1.0),
                FeedMmPerMin = 250,
                PlungeMmPerMin = 60,
                SpindleRpm = 10_000,
                Notes = "Stronger than a 30°, but widens more than twice as fast with depth.",
            },
            new Tool
            {
                Id = new Guid("00000000-0000-0000-0000-000000000800"),
                Name = "0.8 mm end mill",
                Kind = ToolKind.EndMill,
                DiameterNm = Nm.FromMillimetres(0.8),
                StepdownNm = Nm.FromMillimetres(0.3),
                FeedMmPerMin = 200,
                PlungeMmPerMin = 50,
                SpindleRpm = 10_000,
                Notes = "For tight internal cut-outs. Fragile; keep the stepdown small.",
            },
            Tool.DefaultOutlineMill,
            new Tool
            {
                Id = new Guid("00000000-0000-0000-0000-000000002000"),
                Name = "2.0 mm end mill",
                Kind = ToolKind.EndMill,
                DiameterNm = Nm.FromMillimetres(2.0),
                StepdownNm = Nm.FromMillimetres(0.8),
                FeedMmPerMin = 400,
                PlungeMmPerMin = 80,
                SpindleRpm = 10_000,
                Notes = "Fast and stiff for a plain rectangular outline.",
            },
            Tool.DefaultDrill,
        ],
    };
}
