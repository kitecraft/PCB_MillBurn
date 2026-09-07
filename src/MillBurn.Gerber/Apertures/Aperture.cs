using System.Globalization;
using MillBurn.Core;

namespace MillBurn.Gerber.Apertures;

/// <summary>Standard aperture templates, plus the macro case.</summary>
public enum ApertureKind
{
    Circle,
    Rectangle,
    Obround,
    Polygon,
    Macro,
}

/// <summary>
/// A <c>%ADD..%</c> aperture definition.
///
/// The D-code identity is deliberately preserved all the way through the pipeline. pcb2gcode
/// renders the board through libgerbv and only ever sees "a blob of copper"; keeping the aperture
/// (and its X2 attributes) is what later lets us say "burn the mask over SMD pads only, excluding
/// vias" exactly rather than by morphological guesswork. See Documentation/02, section 1.
/// </summary>
public sealed class Aperture
{
    public required int Code { get; init; }

    public required ApertureKind Kind { get; init; }

    /// <summary>Parameters as written, in file units, before conversion.</summary>
    public required IReadOnlyList<double> Parameters { get; init; }

    /// <summary>Set when <see cref="Kind"/> is <see cref="ApertureKind.Macro"/>.</summary>
    public ApertureMacro? Macro { get; init; }

    /// <summary>Unit the parameters are expressed in.</summary>
    public required LengthUnit Unit { get; init; }

    /// <summary>Aperture attributes (<c>%TA%</c>) in force when this aperture was defined.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The <c>.AperFunction</c> attribute, if present — "SMDPad", "ViaPad", "ComponentPad",
    /// "Conductor" and so on. This is the field that makes pad selection exact.
    /// </summary>
    public string? Function =>
        Attributes.TryGetValue(".AperFunction", out var v) ? v.Split(',')[0] : null;

    public bool IsPad => Function is "SMDPad" or "ViaPad" or "ComponentPad" or "BGAPad"
        or "ConnectorPad" or "TestPad" or "HeatsinkPad" or "CastellatedPad";

    public bool IsVia => Function is "ViaPad";

    private double P(int i, double fallback = 0.0) => i < Parameters.Count ? Parameters[i] : fallback;

    /// <summary>
    /// Nominal width across the aperture, in nanometres. Used for quick bounds and for stroking
    /// a draw. Macro apertures report their bounding size, which is approximate by nature.
    /// </summary>
    public long NominalWidthNm => Kind switch
    {
        ApertureKind.Circle => Nm.From(P(0), Unit),
        ApertureKind.Rectangle => Nm.From(P(0), Unit),
        ApertureKind.Obround => Nm.From(P(0), Unit),
        ApertureKind.Polygon => Nm.From(P(0), Unit),
        _ => 0,
    };

    public long NominalHeightNm => Kind switch
    {
        ApertureKind.Circle => Nm.From(P(0), Unit),
        ApertureKind.Rectangle => Nm.From(P(1), Unit),
        ApertureKind.Obround => Nm.From(P(1), Unit),
        ApertureKind.Polygon => Nm.From(P(0), Unit),
        _ => 0,
    };

    /// <summary>The optional drilled hole in the aperture, 0 when absent.</summary>
    public long HoleDiameterNm => Kind switch
    {
        ApertureKind.Circle => Nm.From(P(1), Unit),
        ApertureKind.Rectangle or ApertureKind.Obround => Nm.From(P(2), Unit),
        ApertureKind.Polygon => Nm.From(P(3), Unit),
        _ => 0,
    };

    /// <summary>
    /// Parses the text after <c>ADD</c>, e.g. <c>10C,0.05X0.02</c> or <c>11RoundRect,0.25X-0.5</c>.
    /// </summary>
    public static Aperture Parse(
        string text,
        LengthUnit unit,
        IReadOnlyDictionary<string, ApertureMacro> macros,
        IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(macros);

        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
        {
            digits++;
        }

        if (digits == 0)
        {
            throw new GerberParseException($"Aperture definition '{text}' has no D-code.");
        }

        var code = int.Parse(text.AsSpan(0, digits), CultureInfo.InvariantCulture);
        if (code < 10)
        {
            throw new GerberParseException($"Aperture D-code {code} is reserved; must be 10 or above.");
        }

        var rest = text[digits..];
        var comma = rest.IndexOf(',', StringComparison.Ordinal);
        var template = (comma < 0 ? rest : rest[..comma]).Trim();
        var paramText = comma < 0 ? string.Empty : rest[(comma + 1)..];

        var parameters = new List<double>();
        foreach (var field in paramText.Split('X', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = field.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new GerberParseException($"Aperture {code}: parameter '{trimmed}' is not a number.");
            }

            parameters.Add(value);
        }

        var kind = template switch
        {
            "C" => ApertureKind.Circle,
            "R" => ApertureKind.Rectangle,
            "O" => ApertureKind.Obround,
            "P" => ApertureKind.Polygon,
            _ => ApertureKind.Macro,
        };

        ApertureMacro? macro = null;
        if (kind == ApertureKind.Macro)
        {
            if (!macros.TryGetValue(template, out macro))
            {
                throw new GerberParseException(
                    $"Aperture {code} references undefined macro '{template}'.");
            }
        }

        return new Aperture
        {
            Code = code,
            Kind = kind,
            Parameters = parameters,
            Macro = macro,
            Unit = unit,
            Attributes = new Dictionary<string, string>(attributes, StringComparer.Ordinal),
        };
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture,
            $"D{Code} {Kind}{(Macro is null ? "" : $"({Macro.Name})")} [{string.Join(", ", Parameters)}]" +
            $"{(Function is null ? "" : $" {Function}")}");
}
