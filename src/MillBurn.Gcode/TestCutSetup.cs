using System.Text.Json;
using System.Text.Json.Serialization;
using MillBurn.Core;

namespace MillBurn.Gcode;

/// <summary>
/// What a test cut was set up to do, written beside the files it produced.
///
/// A coupon that is probed first takes two visits: write the probing routine, run it, come back
/// with the log. Between those two the dialog is closed, and everything chosen in it — which test,
/// which bit, how deep the first line goes, how far apart the lines sit — is gone. Retyping it from
/// memory is both annoying and quietly wrong, because a test whose second half does not match its
/// first half measures nothing.
///
/// So the settings go to disk next to the program, and the dialog can read them back. It is also
/// useful on its own: a test worth running once is usually worth running again with the same
/// numbers and a different bit.
///
/// Machine settings are deliberately **not** in here. Safe height, approach and decimals belong to
/// the machine as it is configured now, not to a test that was set up last week, and restoring a
/// stale safe height is the one thing in this file that could break something.
/// </summary>
public sealed record TestCutSetup
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>The suffix these are written with, so the file says what it is.</summary>
    public const string Extension = ".testcut.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public TestCutKind Kind { get; init; }

    /// <summary>
    /// The bit, by id and by name.
    ///
    /// Both, because they fail differently: an id survives a rename and a name survives a library
    /// rebuilt from scratch. If neither matches, the dialog says so rather than quietly cutting the
    /// test with whatever bit happened to be selected.
    /// </summary>
    public Guid ToolId { get; init; }

    public string? ToolName { get; init; }

    public int LineCount { get; init; } = 6;

    public double LineLengthMm { get; init; } = 15;

    public double LineSpacingMm { get; init; } = 2;

    public double StartDepthMm { get; init; } = 0.02;

    public double DepthStepMm { get; init; } = 0.02;

    public double DepthMm { get; init; } = 0.05;

    public double FeedStepMmPerMin { get; init; } = 50;

    public bool RepeatFirstLine { get; init; } = true;

    public int PassesPerLine { get; init; } = 20;

    public int LadderRungs { get; init; } = 5;

    /// <summary>Zero means "work one out from the shallowest line", as it does in the options.</summary>
    public double StepoverMm { get; init; }

    /// <summary>
    /// Whether this was the probe-only half of a two-visit coupon.
    ///
    /// Recorded so that reopening it can put the dialog straight into "level to a probe log", which
    /// is the whole reason the operator is back.
    /// </summary>
    public bool ProbeWritten { get; init; }

    /// <summary>The program written alongside, for the note at the top of the file.</summary>
    public string? Program { get; init; }

    public DateTimeOffset Written { get; init; } = DateTimeOffset.UtcNow;

    public static TestCutSetup From(TestCutOptions options, bool probeWritten, string? program = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new TestCutSetup
        {
            Kind = options.Kind,
            ToolId = options.Tool.Id,
            ToolName = options.Tool.Name,
            LineCount = options.LineCount,
            LineLengthMm = options.LineLengthMm,
            LineSpacingMm = options.LineSpacingMm,
            StartDepthMm = options.StartDepthMm,
            DepthStepMm = options.DepthStepMm,
            DepthMm = options.DepthMm,
            FeedStepMmPerMin = options.FeedStepMmPerMin,
            RepeatFirstLine = options.RepeatFirstLine,
            PassesPerLine = options.PassesPerLine,
            StepoverMm = options.StepoverMm,
            LadderRungs = options.LadderRungs,
            ProbeWritten = probeWritten,
            Program = program,
        };
    }

    /// <summary>The name to write beside a program: <c>depth-test.nc</c> gets <c>depth-test.testcut.json</c>.</summary>
    public static string PathBeside(string programPath) =>
        Path.ChangeExtension(programPath, null) + Extension;

    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var folder = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Reads a saved setup, or null if the file is not one.</summary>
    public static TestCutSetup? Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            var loaded = JsonSerializer.Deserialize<TestCutSetup>(File.ReadAllText(path), Json);

            // A file with none of these is not a test cut setup, whatever it is called. Zero lines
            // or zero length would produce a program that cuts nothing and says nothing about why.
            return loaded is { LineCount: > 0, LineLengthMm: > 0 } ? loaded : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The bit this was set up with, or null when the library no longer has it.</summary>
    public Tool? ToolIn(ToolLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        if (library.Tools.FirstOrDefault(t => t.Id == ToolId) is { } byId)
        {
            return byId;
        }

        return ToolName is { } name
            ? library.Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            : null;
    }
}
