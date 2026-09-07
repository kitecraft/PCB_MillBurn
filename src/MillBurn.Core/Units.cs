using System.Globalization;

namespace MillBurn.Core;

/// <summary>Unit a source file expresses its coordinates in.</summary>
public enum LengthUnit
{
    Millimetres,
    Inches,
}

/// <summary>
/// Everything internal is integer nanometres.
///
/// Clipper2 is exact on integers, so working in a fixed integer grid removes a whole class of
/// robustness bugs that pcb2gcode fights with epsilon comparisons and a dedicated
/// merge_near_points pass. One nanometre is four orders of magnitude below what any hobby machine
/// resolves, and a long still covers +/- 9.2e9 mm of range.
/// </summary>
public static class Nm
{
    public const long PerMillimetre = 1_000_000L;
    public const long PerInch = 25_400_000L;

    public static long FromMillimetres(double mm) => (long)Math.Round(mm * PerMillimetre, MidpointRounding.AwayFromZero);

    public static long FromInches(double inches) => (long)Math.Round(inches * PerInch, MidpointRounding.AwayFromZero);

    public static long From(double value, LengthUnit unit) => unit switch
    {
        LengthUnit.Millimetres => FromMillimetres(value),
        LengthUnit.Inches => FromInches(value),
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    public static double ToMillimetres(long nm) => nm / (double)PerMillimetre;

    public static double ToInches(long nm) => nm / (double)PerInch;

    public static double To(long nm, LengthUnit unit) => unit switch
    {
        LengthUnit.Millimetres => ToMillimetres(nm),
        LengthUnit.Inches => ToInches(nm),
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    /// <summary>Formats as millimetres with the given decimals, always invariant.</summary>
    public static string ToMillimetreString(long nm, int decimals = 4) =>
        ToMillimetres(nm).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
