namespace MillBurn.Core;

/// <summary>
/// Choices that belong to the board rather than to the machine or to one layer.
///
/// The third place a setting can live, and the one that was missing. The machine's numbers are in
/// <see cref="MachineSettings"/> because they describe the machine; what a layer becomes is in
/// <c>LayerOutputSettings</c> because it describes that file. Between them sits a small set of
/// decisions about *this job*: how it is allowed to make a feature, rather than which feature or on
/// what machine.
///
/// One record rather than loose properties, because the set will grow and because a project should
/// carry these as a unit — a job reopened next year should cut the way it cut, and that is only
/// true if the whole group travels together.
/// </summary>
public sealed record JobOptions
{
    /// <summary>
    /// Spiral out a hole no drill can make, instead of refusing it.
    ///
    /// **Off by default, deliberately.** Turning it on changes a hole from drilled to milled, which
    /// is a different tool, a different motion and a different finish — and it would do so silently
    /// on a file the operator thought they understood. A board that asks for a 3.2 mm mounting hole
    /// on a machine whose largest drill is 2 mm has no correct answer, and *saying so* is a better
    /// default than quietly picking one.
    /// </summary>
    public bool MillLargeHoles { get; init; }

    /// <summary>
    /// Which end mill to spiral with, or null to take the widest in the library that fits.
    ///
    /// Named rather than always automatic because the automatic answer optimises for time — the
    /// widest cutter that leaves a helix — and the operator may be optimising for the finish on the
    /// hole wall, or for the one cutter they trust at depth.
    /// </summary>
    public Guid? MillDrillToolId { get; init; }

    /// <summary>
    /// The size at and above which a hole is milled rather than drilled, or zero to derive it from
    /// the library.
    ///
    /// Zero means "anything bigger than the largest drill you have listed", which is the rule that
    /// needs no new number: the library is already a claim about what is in the drawer. A threshold
    /// typed here overrides that, for the case where the library is aspirational.
    /// </summary>
    public double MillAboveMm { get; init; }

    /// <summary>
    /// The piece of stock this job is built on, and the datum for every machine after it.
    ///
    /// Project-level rather than machine-level for the same reason the rest of this record is: the
    /// sheet a board is laid out to fit is a fact about that board. It is not a layer — no file
    /// produces it, and every setting a layer row offers is meaningless for it.
    /// </summary>
    public BlankOptions Blank { get; init; } = new();

    public static JobOptions Default { get; } = new();
}
