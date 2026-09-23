using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Optimize;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The plan has to describe the program it produces.
///
/// <see cref="RouteOptimizer"/> reports what it thinks the route costs, and every other test of it
/// takes that number at its word. This one does not: it walks the passes the router actually hands
/// back and measures the distance between the end of each and the start of the next, which is what
/// the machine will really travel. If those two numbers disagree, the optimizer is choosing by a
/// cost that is not the cost — and it will choose confidently and wrongly.
///
/// Built on a real outline rather than on constructed nodes, because the disagreement this catches
/// comes from the step between an ordering and the geometry that ordering stands for: a
/// configuration the solver can name and the materialiser cannot honour.
/// </summary>
public sealed class RoutedProgramTravelTests(ITestOutputHelper output)
{
    /// <summary>A rectangular board outline, <paramref name="offsetMm"/> along in X.</summary>
    private static Paths64 Board(double offsetMm) =>
    [
        [
            new(Nm.FromMillimetres(offsetMm), 0),
            new(Nm.FromMillimetres(offsetMm + 50), 0),
            new(Nm.FromMillimetres(offsetMm + 50), Nm.FromMillimetres(30)),
            new(Nm.FromMillimetres(offsetMm), Nm.FromMillimetres(30)),
        ],
    ];

    /// <summary>What the tool really travels between one pass and the next, in millimetres.</summary>
    private static double InterPassTravelMm(IReadOnlyList<ToolpathPass> passes)
    {
        var total = 0.0;
        Point2? at = null;

        foreach (var pass in passes)
        {
            if (at is { } from)
            {
                total += from.DistanceTo(pass.Start) / Nm.PerMillimetre;
            }

            at = pass.End;
        }

        return total;
    }

    private static readonly long TabWidthNm = Nm.FromMillimetres(3.0);

    private static Toolpath TabbedOutline(int boards, double stepdownMm, double thicknessMm = 1.6)
    {
        Paths64 outline = [];
        for (var i = 0; i < boards; i++)
        {
            outline.AddRange(Board(i * -70.0));
        }

        return OutlineOperation.Build(outline, new OutlineOptions
        {
            Tool = Tool.DefaultOutlineMill with { StepdownNm = Nm.FromMillimetres(stepdownMm) },
            BoardThicknessNm = Nm.FromMillimetres(thicknessMm),
            DepthPerPassNm = Nm.FromMillimetres(stepdownMm),
            TabCount = 4,
            TabWidthNm = TabWidthNm,
            Keep = [],
        });
    }
    /// <summary>
    /// Every rapid that moves in X or Y, with the Z it is at while it does.
    ///
    /// Written carefully, because every assertion below rests on it and the first version was
    /// wrong in three ways that a green test would never have shown:
    ///
    /// **Arcs were skipped.** It read only lines beginning `G0` or `G1`, and this emitter writes
    /// `G2`/`G3` — fifty-nine of them in the test board's outline alone. A skipped move leaves the
    /// tracked position where it was, so every distance measured after the first arc was measured
    /// from the wrong point.
    ///
    /// **Comments were read as coordinates.** The regex was unanchored and comments are on by
    /// default, so `( hop over tab at X37.5 )` set X to 37.5 and invented a rapid that is not in the
    /// program.
    ///
    /// **A combined move was credited with the height it finished at.** A rapid that rises while it
    /// traverses would be recorded at the safe height it reached, not the depth it started from,
    /// which is the half of the move that matters.
    ///
    /// So: comments are stripped first, position is tracked on any line carrying axis words
    /// whatever its motion word, the motion word is matched exactly rather than by prefix (`G1` must
    /// not match `G10` or `G17`), and a move is credited with the *lower* of the heights it spans.
    /// </summary>
    private static List<(double Z, double Distance)> InPlaneRapids(string program)
    {
        var rapids = new List<(double Z, double Distance)>();
        double x = 0, y = 0, z = 0;
        var modal = -1;

        foreach (var raw in program.Split('\n'))
        {
            // Comments first, both forms. An X inside one is a letter, not an axis. The emitter
            // writes only the parenthesised kind today; the semicolon is stripped as well so that
            // the day it writes one, this reads the program rather than the remark.
            var line = System.Text.RegularExpressions.Regex
                .Replace(raw, @"\([^)]*\)", " ")
                .Split(';')[0]
                .Trim();

            if (line.Length == 0)
            {
                continue;
            }

            // Exactly G0/G00/G1/G01/G2/G02/G3/G03 — not G10, G17, G90.
            var word = System.Text.RegularExpressions.Regex.Match(line, @"(?<![0-9])G0*([0-3])(?![0-9])");
            if (word.Success)
            {
                modal = int.Parse(word.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }

            static double? Axis(string l, char a)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    l, a + @"\s*([-+]?(?:\d+\.?\d*|\.\d+))");

                return m.Success
                    ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                    : null;
            }

            var (px, py, pz) = (x, y, z);
            x = Axis(line, 'X') ?? x;
            y = Axis(line, 'Y') ?? y;
            z = Axis(line, 'Z') ?? z;

            var moved = Math.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py)));

            if (modal == 0 && moved > 0)
            {
                // The lower of the two: a move that rises as it goes is at the depth it left from
                // for the first half of it.
                rapids.Add((Math.Min(pz, z), moved));
            }
        }

        return rapids;
    }

    private static string ProgramFor(int boards, double stepdownMm, double thicknessMm)
    {
        var toolpath = TabbedOutline(boards, stepdownMm, thicknessMm);

        var (ordered, _) = ToolpathRouter.Order(
            toolpath, Point2.Origin, machine: null, RouteEffort.Balanced, Point2.Origin);

        var (linked, _) = PassLinker.Apply(ordered);
        var (text, _) = GcodeEmitter.Emit(
            new Job { Name = "outline", Toolpaths = [linked] }, new GcodeOptions());

        return text;
    }

    /// <summary>
    /// No rapid ever crosses the board below the surface, and a rapid that crosses it below the
    /// safe height goes no further than a tab is wide.
    ///
    /// 6.24 lets the tool hop a tab instead of climbing to the safe height, and the whole risk of it
    /// lives in this one sentence: *which* gap is a tab. The operation that lays the runs out knows
    /// only that they are adjacent when it builds them, and everything downstream is free to
    /// reorder them — `ToolpathRouter` reverses a stack of open runs outright when that shortens the
    /// route. So the pass carries a distance and the emitter checks the move actually in front of
    /// it, and this asserts the property that condition exists to protect rather than the condition.
    ///
    /// The heights are checked against zero rather than against the hop height, because a hop is
    /// levelled like everything else: `Leveller` adds the probed correction to every move, rapids
    /// included, so a hop written at the surface comes out *below* it on any board that dips. Above
    /// the surface by the approach distance is out of that reach.
    /// </summary>
    [Theory]
    [InlineData(3, 0.4, 1.6, true)]
    [InlineData(3, 1.0, 1.6, true)]

    // Thin stock, where the tabs would hold the full thickness of the board: there are no shallow
    // laps, so the stack is open all the way down and the router will reverse it. The gap between
    // two runs is then a traverse of the whole profile rather than a tab — which is exactly the
    // move that must not be taken at a hop height, and it is why the distance is checked.
    [InlineData(3, 0.4, 0.4, false)]
    [InlineData(1, 0.4, 0.4, false)]
    public void NoRapidCrossesTheBoardBelowTheSurface(
        int boards, double stepdownMm, double thicknessMm, bool expectHops)
    {
        var program = ProgramFor(boards, stepdownMm, thicknessMm);

        // InPlaneRapids reads a narrow dialect: absolute, millimetres, no coordinate-system games.
        // Those hold for what this emitter writes, and asserting it beats assuming it — a program
        // that started setting offsets or switching to incremental would not be mis-parsed quietly,
        // it would say so here.
        foreach (var unread in (string[])["G91", "G20", "G92", "G10", "G53", "G28", "G30"])
        {
            Assert.DoesNotContain(unread, program, StringComparison.Ordinal);
        }

        var rapids = InPlaneRapids(program);

        // A program this shape cannot have fewer rapids than it has profiles: each is reached from
        // somewhere. Without a floor, a parser that found one move in two hundred would satisfy
        // everything below.
        Assert.True(
            rapids.Count >= boards,
            $"only {rapids.Count} in-plane rapid(s) were read out of a {boards}-profile program, "
            + "which is fewer than there are places to go");

        var safe = Nm.ToMillimetres(new GcodeOptions().SafeZNm);
        var hop = Nm.ToMillimetres(new GcodeOptions().ApproachZNm);

        foreach (var (z, distance) in rapids)
        {
            Assert.True(
                z >= 0,
                $"a rapid moved {distance:F3} mm in plane at Z{z:F3} — below the surface, which is "
                + "a gouge however short it is.");

            // A tab's width and nothing more, which is what the outline asks for. This said
            // "tab width plus the cutter's diameter" while production had stopped adding the
            // cutter, so the test would not have noticed production widening back to it.
            Assert.True(
                z >= safe || distance <= Nm.ToMillimetres(TabWidthNm) + 0.001,
                $"a rapid moved {distance:F3} mm in plane at Z{z:F3}, below the safe height of "
                + $"{safe:F3} mm — that is a journey, not a hop over a tab.");

            Assert.True(z >= hop - 0.001 || z >= safe, $"unexpected rapid height Z{z:F3}");
        }

        // And the hop is actually happening where it should. Every assertion above is satisfied by
        // a program that never hops at all and climbs to the safe height every time, so without
        // this the whole of 6.24 could be switched off and this test would stay green.
        //
        // Only asserted where tabs exist. On stock too thin for them the runs are reordered freely,
        // and hops still happen wherever two of them land next to each other — which is safe, short
        // and above the surface, and was the first thing this assertion got wrong by forbidding.
        // What matters on thin stock is that no *long* move is taken low, and that is the assertion
        // in the loop above.
        if (expectHops)
        {
            // One per profile at least. "At least one anywhere in the program" was too weak: any
            // in-plane movement at the approach height satisfies it — a lead-in would do — so the
            // whole of 6.24 could be switched off and this stay green on an unrelated move.
            var hops = rapids.Count(r => r.Z < safe);

            Assert.True(
                hops >= boards,
                $"{hops} hop(s) below the safe height across {boards} profile(s), each of which has "
                + "four tabs to cross — the hop is not happening");
        }
    }


    /// <summary>
    /// Three tabbed profiles — enough that the local search runs at all, since it returns early
    /// below three nodes.
    ///
    /// A tabbed profile's stack is closed shallow passes followed by the open runs between its
    /// tabs. Reversing that stack is not a reversal: the materialiser leaves a closed pass alone
    /// and flips each tab run where it stands, so every tab gap becomes a traverse of the whole
    /// run. The ordering still looks free, because the node's stated entry and exit are unchanged.
    /// </summary>
    [Fact]
    public void ATabbedOutlineTravelsWhatThePlanSaysItWill()
    {
        var toolpath = TabbedOutline(boards: 3, stepdownMm: 0.4);

        // An outline that produced nothing would make every number below zero, and zero agrees with
        // zero.
        Assert.True(toolpath.Passes.Count > 3, $"expected a real outline, got {toolpath.Passes.Count} pass(es)");

        var (ordered, plan) = ToolpathRouter.Order(
            toolpath, Point2.Origin, machine: null, RouteEffort.Balanced, Point2.Origin);

        var real = InterPassTravelMm(ordered.Passes);

        output.WriteLine($"plan says {plan.TravelMm:F2} mm; the passes travel {real:F2} mm");
        output.WriteLine($"options chosen: {string.Join(", ", plan.Steps.Select(s => s.Option))}");

        // Generous on purpose: the plan measures hops between whole stacks and this measures every
        // pass boundary, so the real figure is legitimately the larger. An order of magnitude apart
        // is not that.
        Assert.True(
            real <= (plan.TravelMm * 2) + 50,
            $"the plan promised {plan.TravelMm:F2} mm of travel and the passes it produced travel "
            + $"{real:F2} mm — the optimizer is choosing by a cost that is not the cost.");
    }

    /// <summary>
    /// And the ordering must not have made the real travel worse than the order it started from.
    /// </summary>
    [Fact]
    public void OrderingATabbedOutlineDoesNotLengthenTheRealTravel()
    {
        var toolpath = TabbedOutline(boards: 3, stepdownMm: 0.4);

        var before = InterPassTravelMm(toolpath.Passes);

        var (ordered, _) = ToolpathRouter.Order(
            toolpath, Point2.Origin, machine: null, RouteEffort.Balanced, Point2.Origin);

        var after = InterPassTravelMm(ordered.Passes);

        output.WriteLine($"inter-pass travel {before:F2} mm -> {after:F2} mm");

        Assert.True(
            after <= before + 0.001,
            $"ordering made the real travel worse: {before:F2} mm -> {after:F2} mm.");
    }
}
