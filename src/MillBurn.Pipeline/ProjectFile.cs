using System.Collections.Immutable;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text.Json;
using System.Text.Json.Serialization;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Geometry;
using MillBurn.Gerber;
using MillBurn.Gerber.Excellon;

namespace MillBurn.Pipeline;

/// <summary>Reading and writing <c>.millburn</c> project files.</summary>
public static class ProjectFile
{
    public const string Extension = ".millburn";

    private const string ManifestEntry = "project.json";
    private const string SourcePrefix = "sources/";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ------------------------------------------------------------------ importing

    /// <summary>
    /// Reads a folder into project sources, computing both hashes and detecting each role.
    /// </summary>
    public static ImmutableArray<ProjectSource> ImportFolder(string folder, RealisationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"'{folder}' is not a directory.");
        }

        var sources = ImmutableArray.CreateBuilder<ProjectSource>();
        foreach (var file in Directory.EnumerateFiles(folder).Where(BoardLoader.IsBoardFile).Order(StringComparer.Ordinal))
        {
            sources.Add(Import(Path.GetFileName(file), File.ReadAllBytes(file), options));
        }

        return sources.ToImmutable();
    }

    /// <summary>Turns raw file bytes into a source, with its role and both hashes.</summary>
    public static ProjectSource Import(string fileName, byte[] content, RealisationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);

        var (role, _) = DetectRole(fileName, content);

        return new ProjectSource
        {
            FileName = fileName,
            Content = [.. content],
            ContentHash = HashBytes(content),
            GeometryHash = GeometryFingerprint(fileName, content, options),
            Role = role,
        };
    }

    public static string HashBytes(ReadOnlySpan<byte> content)
    {
        var hash = new XxHash128();
        hash.Append(content);
        return Convert.ToHexString(hash.GetCurrentHash());
    }

    /// <summary>
    /// The fingerprint of what the file actually draws.
    ///
    /// A file that cannot be parsed gets a fingerprint of its bytes instead, so a broken file still
    /// compares equal to itself and a refresh does not report it as endlessly changing.
    /// </summary>
    public static string GeometryFingerprint(string fileName, byte[] content, RealisationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            if (IsDrill(fileName))
            {
                var drill = ExcellonParser.Parse(Text(content));
                return DrillFingerprint(drill);
            }

            var image = GerberParser.Parse(Text(content));
            return Polygons.Fingerprint(GerberRealiser.Realise(image, options).Area);
        }
        catch (GerberParseException)
        {
            return "unparsed:" + HashBytes(content);
        }
    }

    private static string DrillFingerprint(ExcellonFile drill)
    {
        var hash = new XxHash128();
        var buffer = new byte[sizeof(long)];

        void Append(long value)
        {
            BitConverter.TryWriteBytes(buffer, value);
            hash.Append(buffer);
        }

        // Ordered, so a tool renumbering that leaves the holes where they are does not read as a
        // change to the board.
        foreach (var hit in drill.Hits
            .Select(h => (h.At.X, h.At.Y, Diameter: drill.Tools.TryGetValue(h.Tool, out var t) ? t.DiameterNm : 0))
            .OrderBy(h => h.X).ThenBy(h => h.Y).ThenBy(h => h.Diameter))
        {
            Append(hit.X);
            Append(hit.Y);
            Append(hit.Diameter);
        }

        foreach (var slot in drill.Slots
            .Select(s => (s.From.X, s.From.Y, s.To.X, s.To.Y, Diameter: drill.Tools.TryGetValue(s.Tool, out var t) ? t.DiameterNm : 0))
            .OrderBy(s => s.Item1).ThenBy(s => s.Item2))
        {
            Append(slot.Item1);
            Append(slot.Item2);
            Append(slot.Item3);
            Append(slot.Item4);
            Append(slot.Diameter);
        }

        return Convert.ToHexString(hash.GetCurrentHash());
    }

    public static (LayerRole Role, bool Guessed) DetectRole(string fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);

        if (IsDrill(fileName))
        {
            try
            {
                var drill = ExcellonParser.Parse(Text(content));
                return drill.Plating switch
                {
                    HolePlating.Plated => (LayerRole.PlatedDrill, false),
                    HolePlating.NonPlated => (LayerRole.NonPlatedDrill, false),
                    _ => (LayerRoles.FromFileName(fileName), true),
                };
            }
            catch (GerberParseException)
            {
                return (LayerRoles.FromFileName(fileName), true);
            }
        }

        try
        {
            return LayerRoles.Detect(GerberParser.Parse(Text(content)), fileName);
        }
        catch (GerberParseException)
        {
            return (LayerRoles.FromFileName(fileName), true);
        }
    }

    // ------------------------------------------------------------------ persistence

    public static void Save(MillBurnProject project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(path);

        var manifest = new ProjectManifest
        {
            SchemaVersion = MillBurnProject.CurrentSchemaVersion,
            OriginFolder = project.OriginFolder,
            Settings = project.Settings,
            ViewState = project.ViewState,
            Sources = [.. project.Sources.Select(s => new SourceDescriptor
            {
                FileName = s.FileName,
                ContentHash = s.ContentHash,
                GeometryHash = s.GeometryHash,
                Role = s.Role,
                RoleOverridden = s.RoleOverridden,
            })],
        };

        // Written to a temporary file and moved into place, so an interrupted save cannot leave a
        // half-written project where a working one used to be.
        var temporary = path + ".saving";

        using (var stream = File.Create(temporary))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            using (var entry = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal).Open())
            {
                JsonSerializer.Serialize(entry, manifest, Json);
            }

            foreach (var source in project.Sources)
            {
                using var entry = archive.CreateEntry(SourcePrefix + source.FileName, CompressionLevel.Optimal).Open();
                entry.Write(source.Content.AsSpan());
            }
        }

        File.Move(temporary, path, overwrite: true);

        project.FilePath = path;
        project.SchemaVersion = MillBurnProject.CurrentSchemaVersion;
        project.MarkSaved();
    }

    public static MillBurnProject Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var manifestEntry = archive.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a PCB_MillBurn project: no {ManifestEntry}.");

        ProjectManifest manifest;
        using (var entry = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize<ProjectManifest>(entry, Json)
                ?? throw new InvalidDataException($"'{Path.GetFileName(path)}' has an empty manifest.");
        }

        if (manifest.SchemaVersion > MillBurnProject.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(path)}' was written by a newer version of PCB_MillBurn " +
                $"(format {manifest.SchemaVersion}; this build reads {MillBurnProject.CurrentSchemaVersion}).");
        }

        var sources = ImmutableArray.CreateBuilder<ProjectSource>();
        foreach (var descriptor in manifest.Sources)
        {
            var entry = archive.GetEntry(SourcePrefix + descriptor.FileName);
            if (entry is null)
            {
                // The manifest and the archive disagree. Skipping quietly would show a board that
                // is missing a layer and look entirely normal.
                throw new InvalidDataException(
                    $"'{Path.GetFileName(path)}' lists {descriptor.FileName} but does not contain it.");
            }

            using var content = entry.Open();
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);

            sources.Add(new ProjectSource
            {
                FileName = descriptor.FileName,
                Content = [.. buffer.ToArray()],
                ContentHash = descriptor.ContentHash,
                GeometryHash = descriptor.GeometryHash,
                Role = descriptor.Role,
                RoleOverridden = descriptor.RoleOverridden,
            });
        }

        return new MillBurnProject
        {
            Sources = sources.ToImmutable(),
            Settings = manifest.Settings ?? new ProjectSettings(),
            ViewState = manifest.ViewState ?? new ProjectViewState(),
            OriginFolder = manifest.OriginFolder,
            FilePath = path,
            SchemaVersion = manifest.SchemaVersion,
        };
    }

    /// <summary>Realises a project's sources into a board, without touching the disk they came from.</summary>
    public static Board ToBoard(MillBurnProject project, RealisationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        return BoardLoader.LoadSources(
            project.FilePath ?? project.OriginFolder ?? "project",
            project.Sources.Select(s => (s.FileName, s.Content.AsSpan().ToArray(), s.Role)),
            options);
    }

    private static bool IsDrill(string fileName) =>
        Path.GetExtension(fileName).ToUpperInvariant() is ".DRL" or ".XLN";

    private static string Text(byte[] content) => System.Text.Encoding.UTF8.GetString(content);

    // ------------------------------------------------------------------ on-disk shape

    private sealed record ProjectManifest
    {
        public int SchemaVersion { get; init; } = MillBurnProject.CurrentSchemaVersion;

        public string? OriginFolder { get; init; }

        public ImmutableArray<SourceDescriptor> Sources { get; init; } = [];

        public ProjectSettings? Settings { get; init; }

        public ProjectViewState? ViewState { get; init; }
    }

    private sealed record SourceDescriptor
    {
        public required string FileName { get; init; }

        public required string ContentHash { get; init; }

        public required string GeometryHash { get; init; }

        public required LayerRole Role { get; init; }

        public bool RoleOverridden { get; init; }
    }
}
