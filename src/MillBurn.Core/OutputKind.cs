namespace MillBurn.Core;

/// <summary>
/// What a layer is turned into, if anything.
///
/// Vocabulary rather than pipeline machinery, and it lives beside <see cref="LayerRole"/> for the
/// same reason that does: the settings need to name one without the settings knowing anything about
/// how a layer is realised.
/// </summary>
public enum OutputKind
{
    /// <summary>Shown, but not cut or exported.</summary>
    None,

    /// <summary>Vectors for a laser program.</summary>
    Svg,

    /// <summary>A program for the mill.</summary>
    Gcode,
}
