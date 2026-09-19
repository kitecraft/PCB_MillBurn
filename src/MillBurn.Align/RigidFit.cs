using System.Globalization;
using MillBurn.Core;

namespace MillBurn.Align;

/// <summary>
/// What two measured holes say about where the board really is.
/// </summary>
/// <param name="RotationDegrees">How far it is turned, anticlockwise, about the first hole.</param>
/// <param name="OffsetNm">Where the first hole has to move to, once the turn is taken out.</param>
/// <param name="PivotNm">The first hole, as the program has it: what the rotation turns about.</param>
/// <param name="SeparationErrorNm">
/// Measured distance between the holes minus the known one. Zero for a board that has only moved and
/// turned; anything else means a hole was misread, or the piece is not the size it is supposed to be.
/// </param>
/// <param name="Refusal">Why there is no fit, or null when there is one.</param>
public readonly record struct RigidFitResult(
    double RotationDegrees,
    Point2 OffsetNm,
    Point2 PivotNm,
    long SeparationErrorNm,
    string? Refusal)
{
    public bool Found => Refusal is null;
}

/// <summary>
/// Fits a board's position from two holes measured on the machine.
///
/// One hole gives a shift, and a shift assumes the board is square to the table. It often is not: a
/// jig with a little play in it can put the piece back a few hundredths turned, and at one hole that
/// looks exactly like a shift — it is the holes at the other end of the board that come out wrong, by
/// the length of the arc. Two holes separate the two: the line between them is turned by the same
/// angle the board is.
///
/// Rigid only — a turn and a move, never a stretch. The distance between two holes in a piece of
/// copper-clad does not change, so a measured distance that disagrees with the known one is a
/// misreading rather than a scale to fit out, and it is reported as an error rather than absorbed.
/// This is the two-point case of the fiducial fit in Documentation/04 §4.2, which is where the general
/// version (three or more points, Kabsch/SVD) belongs when it is built.
/// </summary>
public static class RigidFit
{
    /// <summary>
    /// The smallest sensible baseline: two holes closer than this cannot say anything useful about
    /// rotation, because the error in reading each one is a large part of the distance between them.
    /// </summary>
    public static long ShortestBaselineNm { get; } = Nm.FromMillimetres(10);

    /// <summary>
    /// How far the measured distance between the holes may differ from the known one before there is
    /// no fit at all.
    ///
    /// Copper-clad does not stretch by half a millimetre, so a disagreement this large is a hole read
    /// wrongly — the wrong hole picked in the list, or a jog logged from the wrong axis. Fitting it
    /// anyway would turn one bad reading into every hole on the board being out.
    /// </summary>
    public static long LargestSeparationErrorNm { get; } = Nm.FromMillimetres(0.5);

    /// <summary>
    /// Solves for the turn and the move.
    /// </summary>
    /// <param name="firstKnown">The first hole, where the program puts it.</param>
    /// <param name="firstFound">Where that hole actually is.</param>
    /// <param name="secondKnown">The second hole, where the program puts it.</param>
    /// <param name="secondFound">Where that one actually is.</param>
    public static RigidFitResult Solve(
        Point2 firstKnown, Point2 firstFound, Point2 secondKnown, Point2 secondFound)
    {
        var known = firstKnown.DistanceTo(secondKnown);
        var found = firstFound.DistanceTo(secondFound);

        if (known < ShortestBaselineNm)
        {
            var apart = Nm.ToMillimetreString((long)Math.Round(known), 2);
            var shortest = Nm.ToMillimetreString(ShortestBaselineNm, 0);

            return new RigidFitResult(0, default, firstKnown, 0, string.Create(
                CultureInfo.InvariantCulture,
                $"Those two holes are {apart} mm apart. Closer than {shortest} mm, a hole read a hundredth out swings the angle further than the angle itself, so the fit would be guesswork. Pick two holes at opposite ends of the board."));
        }

        var separationError = (long)Math.Round(found - known);

        if (Math.Abs(separationError) > LargestSeparationErrorNm)
        {
            var apartKnown = Nm.ToMillimetreString((long)Math.Round(known), 2);
            var apartFound = Nm.ToMillimetreString((long)Math.Round(found), 2);
            var difference = Nm.ToMillimetreString(Math.Abs(separationError), 2);

            return new RigidFitResult(0, default, firstKnown, separationError, string.Create(
                CultureInfo.InvariantCulture,
                $"The program has those two holes {apartKnown} mm apart, but as measured they are {apartFound} mm apart — {difference} mm out. The board cannot have stretched, so one of the two readings is of a different hole than the one it was meant to be. Check both before writing files."));
        }

        var angle = Math.Atan2(secondFound.Y - firstFound.Y, secondFound.X - firstFound.X)
            - Math.Atan2(secondKnown.Y - firstKnown.Y, secondKnown.X - firstKnown.X);

        // Wrapped into ±180°, so a hair anticlockwise never reads as 359.99° clockwise.
        angle = Math.Atan2(Math.Sin(angle), Math.Cos(angle));

        return new RigidFitResult(
            angle * 180 / Math.PI,
            new Point2(firstFound.X - firstKnown.X, firstFound.Y - firstKnown.Y),
            firstKnown,
            separationError,
            null);
    }
}
