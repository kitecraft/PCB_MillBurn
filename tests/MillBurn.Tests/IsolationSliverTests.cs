using System.Globalization;
using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Pipeline;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The loops that go around nothing.
///
/// **The fault this exists for.** From the bench, on the Arduino Mega: *"There also seems to be a
/// misplaced hole. IF top copper isolation >= 0.45 then the misplaced hole appears. But, if the
/// isolation is &lt;0.45 then the misplaced hole is NOT present. The hole, while being blue, seems to
/// be connected to the top copper layer."* It is not a hole. It is a four-point loop 175 µm long
/// and 28 µm wide at (15.47, 31.47) mm from the board's lower-left corner, enclosing 4,971 µm²,
/// emitted as a plunge, three moves and a retract. At any usable zoom that draws as a dot in the
/// plunged-hole style, which is exactly what was reported.
///
/// **Why the width mattered.** That project cuts its top copper at 0.045 mm deep, where the V-bit
/// takes 0.124 mm. Four passes clear 0.441 mm and five clear 0.546, so 0.45 is the first width that
/// asks for a fifth — and the fifth pass's offsets closed on each other and left the splinter. At
/// the default 0.05 mm deep the same board takes four passes at both 0.40 and 0.45 and the fault
/// cannot be reproduced at all, which is why the bench could not find it again.
///
/// **The two directions this has to hold in.** Refusing too little leaves the dot; refusing too
/// much is far worse, because a contour dropped from the first passes is copper left standing in
/// the moat. The synthetic cases below are the second direction: the smallest island a board can
/// carry, and a channel so narrow the pass down it is a hair.
/// </summary>
public sealed class IsolationSliverTests(ITestOutputHelper output)
{
    private static readonly long Deep = Nm.FromMillimetres(0.045);

    private static IsolationOptions Options(double widthMm, long? depthNm = null) => new()
    {
        Tool = Tool.DefaultVBit,
        DepthNm = depthNm ?? Deep,
        WidthNm = Nm.FromMillimetres(widthMm),
    };

    // Every helper here walks `From` alone, which is the whole ring: an isolation pass is closed and
    // emitted vertex by vertex, so the last segment's `To` is the first segment's `From`. They are
    // straight lines too — `Area` would be reading chords if isolation ever emitted an arc.
    private static (long X, long Y) Centre(ToolpathPass pass)
    {
        long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;

        foreach (var s in pass.Path)
        {
            minX = Math.Min(minX, s.From.X);
            maxX = Math.Max(maxX, s.From.X);
            minY = Math.Min(minY, s.From.Y);
            maxY = Math.Max(maxY, s.From.Y);
        }

        return ((minX + maxX) / 2, (minY + maxY) / 2);
    }

    private static long Span(ToolpathPass pass)
    {
        long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;

        foreach (var s in pass.Path)
        {
            minX = Math.Min(minX, s.From.X);
            maxX = Math.Max(maxX, s.From.X);
            minY = Math.Min(minY, s.From.Y);
            maxY = Math.Max(maxY, s.From.Y);
        }

        return Math.Max(maxX - minX, maxY - minY);
    }

    private static double Area(ToolpathPass pass)
    {
        var ring = new Path64();
        foreach (var s in pass.Path)
        {
            ring.Add(new Point64(s.From.X, s.From.Y));
        }

        return Math.Abs(Clipper.Area(ring));
    }

    private static double LengthMm(Toolpath path) =>
        path.Passes.SelectMany(p => p.Path).Sum(s => s.From.DistanceTo(s.To)) / 1e6;

    // ------------------------------------------------------------------ the board it was found on

    /// <summary>
    /// The reported coordinate, at the setting that produced it and the one that did not.
    ///
    /// **This is the whole bug report, in the units the bench measured it in.** The window is
    /// 0.3 mm square around the spot — wide enough that a splinter a few microns to one side still
    /// fails it, narrow enough to hold one thing: a single 0.53 mm loop, centred 40 µm away and
    /// there at both widths, which the bench never complained about. A floor of 0.2 mm cannot catch
    /// it by accident, and its presence is what proves the window is over the right copper.
    ///
    /// Both widths are asserted, not only the one that failed. At 0.40 mm the bench saw nothing
    /// wrong, so 0.40 is the control: if it ever starts failing, the fix has begun refusing
    /// contours that were always fine, and the two rows fail together instead of silently agreeing.
    /// </summary>
    [Theory]
    [InlineData(0.40)]
    [InlineData(0.45)]
    public void TheMisplacedHoleIsNotThere(double widthMm)
    {
        var layer = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoMega))
            .Layers.Single(l => l.Role == LayerRole.TopCopper);

        // The screenshot is in board coordinates; the geometry is still in the Gerber's.
        var spotX = Nm.FromMillimetres(15.47 + 97.574);
        var spotY = Nm.FromMillimetres(31.47 - 131.801);
        var window = Nm.FromMillimetres(0.15);
        var floor = Nm.FromMillimetres(0.2);

        var built = IsolationOperation.Build(layer.Area, Options(widthMm), layer.Label);

        var atTheSpot = built.Passes
            .Select(p => (Pass: p, C: Centre(p)))
            .Where(p => Math.Abs(p.C.X - spotX) < window && Math.Abs(p.C.Y - spotY) < window)
            .ToList();

        var strays = atTheSpot
            .Where(p => Span(p.Pass) < floor)
            .Select(p => $"{Span(p.Pass) / 1000.0:F1} µm across enclosing {Area(p.Pass) / 1e6:F0} µm² "
                + $"at ({(p.C.X / 1e6) - 97.574:F3}, {(p.C.Y / 1e6) + 131.801:F3})")
            .ToList();

        output.WriteLine(
            $"{widthMm} mm: {built.Passes.Count} passes, {atTheSpot.Count} at the spot, "
            + $"{strays.Count} of them stray");

        // **The window has to prove it is looking at the right place.** Its coordinate is the
        // screenshot's, shifted by a hard-coded Gerber origin; if the loader's origin handling ever
        // moved, the window would land on bare board, find nothing, and both rows would pass while
        // asserting nothing at all. There is exactly one real contour here — the 0.53 mm loop the
        // bench never complained about, at (15.455, 31.507) — and it is present at both widths.
        Assert.True(
            atTheSpot.Count > 0,
            $"nothing at all is planned within {window / 1e6:F2} mm of ({spotX / 1e6:F3}, "
            + $"{spotY / 1e6:F3}), so this window is over bare board and the absence below means "
            + "nothing. Check the board's origin before believing the fix.");

        Assert.True(
            strays.Count == 0,
            $"at {widthMm} mm of isolation the Mega still plunges for {strays.Count} loop(s) where the "
            + "bench found the misplaced hole:\n  " + string.Join("\n  ", strays));
    }

    /// <summary>
    /// And nowhere else on the board either, on both sides of it.
    ///
    /// **A window test alone would pass on a fix that only knew about that one spot.** The Mega's
    /// top copper carried 374 of these at 0.45 mm and its bottom 119 — a quarter of every plunge on
    /// the layer, each costing the best part of three seconds of Z motion to cut nothing.
    ///
    /// **The floors are measurements, not the rule written twice.** Asserting the rule's own
    /// predicate over the output of the code that applies it is a tautology: it survives both
    /// thresholds being wrong by an order of magnitude and can only fail if the guard is deleted
    /// outright. So what is asserted is the smallest thing this board actually plans: 191.9 µm
    /// across and 507.6 µm round on the top copper, 205.1 and 530.7 on the bottom, against a
    /// 124.1 µm cut. The loop the bench reported was 126 µm across and about 380 round, so both
    /// floors sit above it and below anything real, and a rule loosened enough to let it back would
    /// fail here rather than pass.
    ///
    /// **Not area, which would be the wrong measurement.** The smallest area planned is 1,888 µm²,
    /// a sixth of what a plunge removes — because a long thin hole legitimately encloses very
    /// little, and the rule only asks about area for loops that are also compact. A floor on area
    /// would either be meaningless or forbid the very thing the rule is careful to keep.
    ///
    /// These are numbers to argue with. A board carrying a genuinely smaller feature would fail
    /// this while the software behaved, and the failure would be these figures rather than the code
    /// — the same bargain `ArcFittingTests` makes with its 3 mm radius.
    /// </summary>
    [Theory]
    [InlineData(LayerRole.TopCopper, 180_000, 450_000)]
    [InlineData(LayerRole.BottomCopper, 190_000, 450_000)]
    public void TheSmallestThingPlannedIsStillWellClearOfTheCutter(
        LayerRole role, long floorNm, long floorRoundNm)
    {
        var layer = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoMega))
            .Layers.Single(l => l.Role == role);

        var options = Options(0.45);
        var cut = options.EffectiveWidthNm;
        var plunge = Math.PI * (cut / 2.0) * (cut / 2.0);

        var built = IsolationOperation.Build(layer.Area, options, layer.Label);

        var narrowest = built.Passes.Min(Span);
        var shortest = built.Passes.Min(p => p.Path.Sum(s => s.From.DistanceTo(s.To)));

        output.WriteLine(
            $"{role}: {built.Passes.Count} passes, cut {cut / 1000.0:F1} µm, plunge takes "
            + $"{plunge / 1e6:F0} µm²; narrowest {narrowest / 1000.0:F1} µm, "
            + $"shortest {shortest / 1000.0:F1} µm round");

        Assert.True(
            narrowest > floorNm,
            $"{role} plans a pass {narrowest / 1000.0:F1} µm across, under the {floorNm / 1000.0:F0} µm "
            + $"floor and close to the {cut / 1000.0:F1} µm cut — a loop that small is a dot.");

        Assert.True(
            shortest > floorRoundNm,
            $"{role} plans a pass only {shortest / 1000.0:F1} µm round, under the "
            + $"{floorRoundNm / 1000.0:F0} µm floor — less travel than the plunge reaching it is worth.");
    }

    /// <summary>
    /// The cut itself is still there. Dropping loops is only safe if what is dropped is nothing.
    ///
    /// **The floor is the direction that matters**, and it is measured rather than chosen: the top
    /// copper cuts 14,553 mm at this setting and the bottom 11,031. An over-eager rule would show up
    /// here as a shortfall long before anybody saw bare copper — the 374 loops this removes account
    /// for 57 mm between them, four tenths of one per cent, and any rule that starts taking real
    /// contours takes whole millimetres.
    ///
    /// The pass ceiling is the other direction, and it is what proves the cut length is not simply
    /// being held up by the slivers still being there: 999 and 510 today, against 1,373 and 629
    /// before this guard existed.
    /// </summary>
    [Theory]
    [InlineData(LayerRole.TopCopper, 14_500, 1_020)]
    [InlineData(LayerRole.BottomCopper, 11_000, 530)]
    public void RemovingThemCostsAlmostNoCutting(LayerRole role, double floorMm, int ceiling)
    {
        var layer = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.ArduinoMega))
            .Layers.Single(l => l.Role == role);

        var built = IsolationOperation.Build(layer.Area, Options(0.45), layer.Label);
        var length = LengthMm(built);

        output.WriteLine($"{role}: {built.Passes.Count} passes, {length:F1} mm of cutting");

        Assert.True(length > floorMm, $"{role} now cuts {length:F1} mm, under the {floorMm} mm floor.");
        Assert.True(
            built.Passes.Count < ceiling,
            $"{role} emits {built.Passes.Count} passes, over the {ceiling} ceiling — the loops are back.");
    }

    /// <summary>
    /// The program says how many it refused, and says nothing when it refused none.
    ///
    /// **A rule applied without being asked for has to be visible**, which is why the achieved width
    /// and the pass cap are reported rather than assumed. This one drops a quarter of the plunges on
    /// a dense board; an operator comparing a plan against the picture should be able to see that
    /// loops were refused rather than wonder where they went. The note lands in the emitted G-code
    /// as a comment, so it is the only part of this reaching the person at the machine.
    ///
    /// **The silent case is the half worth testing.** A note that always appears is furniture. The
    /// connector board has nothing for the rule to take — measured, across two depths and two widths
    /// — so its program must not carry the line, and a guard that started refusing real contours
    /// would announce itself here before anybody looked at a board.
    /// </summary>
    [Theory]
    [InlineData(RealBoards.ArduinoMega, true)]
    [InlineData(RealBoards.GridStripConnector, false)]
    public void TheProgramSaysHowManyLoopsItRefused(string board, bool expected)
    {
        var layer = BoardLoader.LoadFolder(RealBoards.Directory(board))
            .Layers.Single(l => l.Role == LayerRole.TopCopper);

        var built = IsolationOperation.Build(layer.Area, Options(0.45), layer.Label);
        var note = built.Notes.SingleOrDefault(n => n.Contains("too small to be a cut", StringComparison.Ordinal));

        output.WriteLine($"{board}: {built.Passes.Count} passes; note: {note ?? "(none)"}");

        if (!expected)
        {
            Assert.Null(note);
            return;
        }

        Assert.NotNull(note);

        // And it counts, rather than just warning. Read back out of the sentence the operator sees,
        // because a note naming the wrong number is worse than none — they would go looking for
        // loops that were never there.
        var refused = int.Parse(note.Split(' ')[0], CultureInfo.InvariantCulture);

        Assert.InRange(refused, 300, 450);

        // **Exactly right, not merely plausible.** A range only says the number is of the right
        // order; the operator is being told how many were refused, so the arithmetic has to hold.
        // The offsets are redone here — the same laps at the same distances — and everything
        // Clipper returned has to be either planned or accounted for. That duplicates six lines of
        // the production loop, and it is the only way to check a count against the thing counted.
        var options = Options(0.45);
        var offered = 0;

        for (var pass = 0; pass < options.PassCount; pass++)
        {
            var offset = (options.EffectiveWidthNm / 2) + options.BiasNm + (pass * options.StepNm);
            offered += Clipper.InflatePaths(
                    layer.Area, offset, JoinType.Round, EndType.Polygon,
                    arcTolerance: options.SagittaNm)
                .Count(c => c.Count >= 3);
        }

        output.WriteLine($"    {offered} contour(s) offered, {built.Passes.Count} planned, {refused} refused");

        Assert.Equal(offered, built.Passes.Count + refused);
    }

    // ------------------------------------------------------------------ what must survive

    /// <summary>
    /// The smallest thing a board can carry still gets its ring.
    ///
    /// **This is the bound the rule is built on, tested at the bound.** Offsetting a point outwards
    /// by half a cut width gives a circle exactly one cut across, enclosing exactly what the plunge
    /// removes — so a real island can come within a hair of the threshold and must still survive.
    /// A 0.1 mm pad is the smallest feature these boards use, and its ring encloses 0.05 mm², four
    /// times the threshold; the point island below is the degenerate case underneath that.
    /// </summary>
    [Theory]
    [InlineData(0.1)]
    [InlineData(0.05)]
    [InlineData(0.01)]
    public void ATinyCopperIslandIsStillIsolated(double sideMm)
    {
        var side = Nm.FromMillimetres(sideMm);
        var copper = new Paths64
        {
            new Path64
            {
                new Point64(0, 0), new Point64(side, 0), new Point64(side, side), new Point64(0, side),
            },
        };

        var options = new IsolationOptions { Tool = Tool.DefaultVBit, DepthNm = Deep, Passes = 1 };
        var built = IsolationOperation.Build(copper, options, "island");

        output.WriteLine(
            $"{sideMm} mm island: {built.Passes.Count} pass(es), "
            + (built.Passes.Count == 1
                ? $"{Span(built.Passes[0]) / 1000.0:F1} µm across enclosing "
                    + $"{Area(built.Passes[0]) / 1e6:F0} µm², against a plunge's "
                    + $"{Math.PI * (options.EffectiveWidthNm / 2.0) * (options.EffectiveWidthNm / 2.0) / 1e6:F0}"
                : "—"));

        var pass = Assert.Single(built.Passes);

        // Grown by half a cut on every side, so the ring is the island plus one whole width.
        Assert.InRange(
            Span(pass),
            side + options.EffectiveWidthNm - Nm.FromMillimetres(0.002),
            side + options.EffectiveWidthNm + Nm.FromMillimetres(0.002));
    }

    /// <summary>
    /// A slot in a pour, so narrow that the second pass down it is a hair — and this is the case the
    /// rule could most easily have broken.
    ///
    /// **A long hair encloses almost nothing either.** The slot is two microns wider than two
    /// passes take — 0.337 mm for today's bit, but measured off the tool rather than written down —
    /// so the second pass leaves a hole nearly five millimetres long and under two microns across,
    /// enclosing about 8,800 µm² against the 12,100 a plunge removes. Refusing that on area
    /// alone would leave a ridge of copper lying down the middle of the moat, which is why the rule
    /// also asks how far the loop reaches. Worth is in the run of moat a loop clears, not in what it
    /// encircles, and past two cut widths a loop has stopped being a dot.
    ///
    /// The area is asserted as well as the survival, because that is what makes this test about the
    /// span cap rather than about offsetting: if the hole ever came out fatter than a plunge's
    /// footprint it would survive on area alone and stop guarding anything.
    /// </summary>
    [Fact]
    public void AHairThinSlotInAPourIsStillCut()
    {
        var options = new IsolationOptions { Tool = Tool.DefaultVBit, DepthNm = Deep, Passes = 2 };

        // Built from the tool rather than written down: two laps reach (width/2 + step) into the
        // slot from each side, and the slot is that plus a hair. A literal 0.337 mm says the same
        // thing today and stops saying it the moment the bit, the depth or the 15 % overlap moves —
        // and it would fail as "the slot vanished", which is not what this test is about.
        var hair = Nm.FromMillimetres(0.002);
        var gap = (2 * ((options.EffectiveWidthNm / 2) + options.StepNm)) + hair;

        var slot = Nm.FromMillimetres(5);
        var x0 = Nm.FromMillimetres(2);
        var y0 = Nm.FromMillimetres(1);
        var w = Nm.FromMillimetres(8);
        var h = Nm.FromMillimetres(7);

        // A pour with a slot in it: outer ring one way round, the slot the other.
        var copper = new Paths64
        {
            new Path64 { new(0, 0), new(w, 0), new(w, h), new(0, h) },
            new Path64
            {
                new(x0, y0), new(x0, y0 + slot),
                new(x0 + gap, y0 + slot), new(x0 + gap, y0),
            },
        };

        var built = IsolationOperation.Build(copper, options, "slot");

        foreach (var p in built.Passes)
        {
            var c = Centre(p);
            output.WriteLine(
                $"pass: {Span(p) / 1e6:F3} mm across, {Area(p) / 1e12:F5} mm², "
                + $"centre ({c.X / 1e6:F3}, {c.Y / 1e6:F3})");
        }

        // Down the slot rather than round the outside of the pour: the outer rings are 8 mm across.
        var down = built.Passes
            .Where(p => Span(p) < Nm.FromMillimetres(6))
            .Where(p =>
            {
                var c = Centre(p);
                return c.X > x0 && c.X < x0 + gap;
            })
            .OrderBy(Area)
            .ToList();

        // Two laps round the pour and two down the slot. Naming the total is what stops the filter
        // below passing on a plan that lost the outside of the board.
        Assert.Equal(4, built.Passes.Count);
        Assert.Equal(2, down.Count);

        var cut = options.EffectiveWidthNm;
        var plunge = Math.PI * (cut / 2.0) * (cut / 2.0);

        Assert.True(
            Area(down[0]) < plunge,
            $"the second pass down the slot encloses {Area(down[0]) / 1e6:F0} µm², which is already "
            + $"more than the {plunge / 1e6:F0} µm² a plunge removes — so it would survive on area "
            + "alone and this test has stopped saying anything about the reach test.");
    }
}
