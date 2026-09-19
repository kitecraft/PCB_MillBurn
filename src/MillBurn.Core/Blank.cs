using System.Globalization;

namespace MillBurn.Core;

/// <summary>How the blank's size is arrived at.</summary>
public enum BlankSizing
{
    /// <summary>Four offsets from the board's bounding box.</summary>
    GrownFromBoard,

    /// <summary>A rectangle of stated size, with the board placed inside it.</summary>
    Stated,
}

/// <summary>
/// The piece of stock the job is built on.
///
/// **The datum is the lower-left corner**, always, because that is what every file this app emits
/// already references and a second convention would be one more thing to get wrong at the machine.
/// A different corner is a different jig, not a different setting.
///
/// **Off by default**, because a mill-only single-sided board gains nothing from it — nothing leaves
/// the machine, so there is no registration problem to solve — and a feature that shifts work zero
/// should not arrive uninvited.
/// </summary>
public sealed record BlankOptions
{
    public bool Enabled { get; init; }

    public BlankSizing Sizing { get; init; } = BlankSizing.GrownFromBoard;

    /// <summary>
    /// Border on each side, when the blank is grown from the board.
    ///
    /// Four numbers rather than one because waste matters: an L-shaped corner stop only needs
    /// margin on two edges, so the defaults are generous where the stop is and tight elsewhere.
    /// </summary>
    public double LeftMm { get; init; } = 10;

    public double RightMm { get; init; } = 5;

    public double BottomMm { get; init; } = 10;

    public double TopMm { get; init; } = 5;

    /// <summary>The blank's own size, when it is stated rather than grown.</summary>
    public double WidthMm { get; init; }

    public double HeightMm { get; init; }

    /// <summary>
    /// Whether the mill cuts this piece, or the operator already has it.
    ///
    /// The distinction the phase turns on. A cut blank is a piece whose dimensions the app knows,
    /// because it made it. A declared one is a claim about stock already on the table — which fixes
    /// the origin, the page and the mirror axis just as well, and guarantees none of the tolerance.
    /// </summary>
    public bool Cut { get; init; } = true;

    /// <summary>
    /// Where the board's lower-left corner sits inside a stated blank, from the blank's lower-left.
    /// Null means centred, which is what somebody laying a panel onto a sheet means.
    /// </summary>
    public double? PlaceXMm { get; init; }

    public double? PlaceYMm { get; init; }

    /// <summary>
    /// Cut two small holes in the waste border, as a reference for checking alignment later.
    ///
    /// The stock's edges are the datum, and they are good — but a jig with a little play in it puts
    /// the piece back a few hundredths out, or a few hundredths turned, and neither shows until the
    /// holes are in the board. Two holes cut in the same setup as the edges sit at known coordinates
    /// in the stock's own frame, in material that is thrown away, so every later setup can be checked
    /// against them before anything is cut into the board.
    /// </summary>
    public bool AlignmentHoles { get; init; }

    public static BlankOptions Default { get; } = new();
}

/// <summary>A blank that was worked out, or the reasons it could not be.</summary>
public sealed record BlankPlan
{
    /// <summary>The blank in board coordinates, or null when there is not one.</summary>
    public Bounds Bounds { get; init; } = Bounds.Empty;

    public bool Resolved => !Bounds.IsEmpty;

    /// <summary>True when the mill makes this piece, false when the operator already has it.</summary>
    public bool Cut { get; init; }

    /// <summary>Why there is no blank. Empty when there is one.</summary>
    public IReadOnlyList<string> Refusals { get; init; } = [];

    /// <summary>Things worth saying about the blank there is.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>
    /// Where the alignment holes go, in board coordinates: one in the bottom border, one in the left.
    /// Empty when none were asked for, or when the borders cannot hold them.
    /// </summary>
    public IReadOnlyList<Point2> AlignmentHoles { get; init; } = [];

    /// <summary>The diameter of those holes: the cutter's own, since each is a single plunge.</summary>
    public long HoleDiameterNm { get; init; }

    public static BlankPlan None { get; } = new();

    public long LeftNm(Bounds board) => board.MinX - Bounds.MinX;

    public long RightNm(Bounds board) => Bounds.MaxX - board.MaxX;

    public long BottomNm(Bounds board) => board.MinY - Bounds.MinY;

    public long TopNm(Bounds board) => Bounds.MaxY - board.MaxY;
}

/// <summary>
/// Works out the rectangle of stock a job is built on.
///
/// The piece of this phase that everything else depends on: once the blank is known, work zero is
/// its datum corner, the shared SVG page is its bounds, and a mirrored layer flips about *its*
/// centreline rather than the board's. Each of those is one line elsewhere and all three are wrong
/// without this.
///
/// **The two sizing modes are the same four numbers read from opposite ends.** Grown from the
/// board, the offsets give the size; stated outright, the size and the board's placement inside it
/// give the offsets. Everything downstream works off the resolved rectangle and never learns which
/// mode produced it.
/// </summary>
public static class Blanks
{
    /// <summary>
    /// How much border a cutter needs before the blank stops being useful, over the cutter's own
    /// diameter.
    ///
    /// Two millimetres. The outline cutter has to run outside the board, and the blank needs
    /// somewhere to be held that is not the board — and a 2 mm sliver of FR4 flexes and can break
    /// away while the board is being released, which is the argument that does not depend on the
    /// jig.
    /// </summary>
    public static long HoldDownNm { get; } = Nm.FromMillimetres(2);

    /// <summary>The narrowest border worth cutting, for a given outline cutter.</summary>
    public static long MinimumBorderNm(long cutterDiameterNm) => cutterDiameterNm + HoldDownNm;

    /// <summary>
    /// Resolves the blank, or says why it cannot be.
    /// </summary>
    /// <param name="options">What the project asks for.</param>
    /// <param name="board">The board's bounding box, in board coordinates.</param>
    /// <param name="cutterNm">Diameter of the cutter that will run the outline.</param>
    /// <param name="mirrored">Whether any layer in this job is mirrored, which constrains the sides.</param>
    public static BlankPlan Resolve(
        BlankOptions options, Bounds board, long cutterNm, bool mirrored = false)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled || board.IsEmpty)
        {
            return BlankPlan.None;
        }

        var refusals = new List<string>();
        var notes = new List<string>();
        var floor = MinimumBorderNm(cutterNm);

        var bounds = options.Sizing == BlankSizing.Stated
            ? Stated(options, board, floor, refusals, notes)
            : Grown(options, board);

        if (refusals.Count > 0 || bounds.IsEmpty)
        {
            return new BlankPlan { Refusals = refusals, Notes = notes };
        }

        // Checked for both modes, because a generous-looking offset is still too small for a wide
        // cutter, and the operator is better told now than with the stock clamped.
        Border(refusals, "left", board.MinX - bounds.MinX, floor, cutterNm);
        Border(refusals, "right", bounds.MaxX - board.MaxX, floor, cutterNm);
        Border(refusals, "bottom", board.MinY - bounds.MinY, floor, cutterNm);
        Border(refusals, "top", bounds.MaxY - board.MaxY, floor, cutterNm);

        if (refusals.Count > 0)
        {
            return new BlankPlan { Refusals = refusals, Notes = notes };
        }

        // The flip is about the blank's centreline, so asymmetric sides survive it only if the
        // arithmetic is right — and a rectangle seats identically whichever way it went in, so a
        // wrong flip is invisible until the board is scrap. Equal sides make the question moot.
        var left = board.MinX - bounds.MinX;
        var right = bounds.MaxX - board.MaxX;

        if (mirrored && Math.Abs(left - right) > Nm.FromMillimetres(0.01))
        {
            notes.Add(Invariant(
                $"The left and right borders differ ({Mm(left)} and {Mm(right)} mm) and this job has a mirrored layer. The flip is about the stock's centreline, so the design lands {Mm(Math.Abs(left - right))} mm from where equal borders would put it. Equalise them unless you have checked the arithmetic."));
        }

        // Deliberately not a "Blank W x H" note: the caller leads with that, and a note that
        // repeats the headline pushes the ones that say something new further down the page.
        if (!options.Cut)
        {
            notes.Add("Pre-cut stock is a claim rather than a measurement: it fixes work zero, the shared page and the mirror axis, and guarantees nothing about the stock's size or squareness. Enter measured dimensions, and mark the datum corner yourself — nothing cuts a key into a piece it did not make.");
        }

        notes.Add(Invariant(
            $"The board uses {Fill(board, bounds):P0} of it."));

        var (holes, diameter) = AlignmentHolesFor(options, board, bounds, cutterNm, notes);

        return new BlankPlan
        {
            Bounds = bounds,
            Cut = options.Cut,
            Notes = notes,
            AlignmentHoles = holes,
            HoleDiameterNm = diameter,
        };
    }

    /// <summary>
    /// Clearance around an alignment hole: enough that it never runs into the stock's own cut, the
    /// board's outline cut, or the corner where two borders meet. One millimetre, over the hole's
    /// radius and the cutter's.
    /// </summary>
    public static long HoleClearanceNm { get; } = Nm.FromMillimetres(1);

    /// <summary>
    /// Where the two alignment holes go, and how big they are.
    ///
    /// **As far apart as the stock allows**, because the angle two holes can resolve is the error in
    /// reading each one divided by the distance between them: the bottom hole goes at the right-hand
    /// end of the bottom border and the left hole at the top of the left one, which is very nearly
    /// the stock's diagonal.
    /// </summary>
    private static (IReadOnlyList<Point2> Holes, long DiameterNm) AlignmentHolesFor(
        BlankOptions options, Bounds board, Bounds bounds, long cutterNm, List<string> notes)
    {
        if (!options.AlignmentHoles)
        {
            return ([], 0);
        }

        if (!options.Cut)
        {
            notes.Add("No alignment holes: nothing cuts holes into a piece of stock it did not make. Ask for them on stock the mill cuts, or mark your own and use them the same way.");
            return ([], 0);
        }

        // Drilled straight down with the stock's own cutter, so a hole is exactly that cutter's width.
        // It used to be spiralled out half as wide again, which took a helix and a lap of linking for
        // nothing the check needs: the check needs a centre, and a plunge has only one.
        var diameter = cutterNm;

        // What each hole needs from the edge it sits beside: its own radius, the cutter's, and the
        // clearance. Twice that is the narrowest border one fits in.
        var reach = (diameter / 2) + (cutterNm / 2) + HoleClearanceNm;
        var bottom = board.MinY - bounds.MinY;
        var left = board.MinX - bounds.MinX;

        if (bottom < reach * 2 || left < reach * 2)
        {
            notes.Add(Invariant(
                $"No alignment holes: a {Mm(diameter)} mm hole needs {Mm(reach * 2)} mm of border, and the bottom and left borders are {Mm(bottom)} and {Mm(left)} mm. Widen them."));

            return ([], 0);
        }

        notes.Add(Invariant(
            $"Two {Mm(diameter)} mm alignment holes in the waste — one in the bottom border, one in the left — drilled before the edges with the same bit. Check a later setup against them with Job > Drill alignment before anything is cut into the board."));

        return (
            [
                new Point2(bounds.MaxX - reach, bounds.MinY + (bottom / 2)),
                new Point2(bounds.MinX + (left / 2), bounds.MaxY - reach),
            ],
            diameter);
    }

    private static Bounds Grown(BlankOptions options, Bounds board) => new(
        board.MinX - Nm.FromMillimetres(options.LeftMm),
        board.MinY - Nm.FromMillimetres(options.BottomMm),
        board.MaxX + Nm.FromMillimetres(options.RightMm),
        board.MaxY + Nm.FromMillimetres(options.TopMm));

    /// <summary>
    /// A blank of stated size, with the board placed inside it.
    ///
    /// This is the mode for pre-cut stock, which is what hobby copper-clad is: a board gets laid out
    /// to suit the sheet at least as often as a sheet is cut to suit the board, and working out
    /// which four offsets turn an 11 x 6 panel into 183 x 122 is arithmetic nobody should be doing
    /// by hand — least of all again every time the panel changes.
    /// </summary>
    private static Bounds Stated(
        BlankOptions options, Bounds board, long floor, List<string> refusals, List<string> notes)
    {
        var width = Nm.FromMillimetres(options.WidthMm);
        var height = Nm.FromMillimetres(options.HeightMm);

        if (width <= 0 || height <= 0)
        {
            refusals.Add("The stock's size has not been given.");
            return Bounds.Empty;
        }

        // Named before anything else, because "1.4 mm too wide" is the answer somebody needs and
        // "the left border is too small" is a symptom of it.
        Fits(refusals, "wide", board.Width, width, floor);
        Fits(refusals, "tall", board.Height, height, floor);

        if (refusals.Count > 0)
        {
            return Bounds.Empty;
        }

        var x = options.PlaceXMm is { } px
            ? Nm.FromMillimetres(px)
            : (width - board.Width) / 2;

        var y = options.PlaceYMm is { } py
            ? Nm.FromMillimetres(py)
            : (height - board.Height) / 2;

        if (options.PlaceXMm is null && options.PlaceYMm is null)
        {
            notes.Add("The board is centred on the stock.");
        }

        return new Bounds(board.MinX - x, board.MinY - y, board.MinX - x + width, board.MinY - y + height);
    }

    private static void Fits(List<string> refusals, string way, long board, long blank, long floor)
    {
        var needed = board + (floor * 2);

        if (needed > blank)
        {
            refusals.Add(Invariant(
                $"The board is {Mm(needed - blank)} mm too {way} for a {Mm(blank)} mm stock — it needs {Mm(floor)} mm of border on each side and there is only {Mm((blank - board) / 2)} mm."));
        }
    }

    private static void Border(List<string> refusals, string side, long have, long floor, long cutterNm)
    {
        if (have < floor)
        {
            refusals.Add(Invariant(
                $"The {side} border is {Mm(have)} mm; {Mm(floor)} mm is the floor for a {Mm(cutterNm)} mm cutter (its own width, plus 2 mm to hold the stock down and to keep a sliver from breaking away)."));
        }
    }

    private static double Fill(Bounds board, Bounds blank) =>
        blank.Width <= 0 || blank.Height <= 0
            ? 0
            : (double)board.Width * board.Height / ((double)blank.Width * blank.Height);

    private static string Mm(long nm) => Nm.ToMillimetreString(nm, 2);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
