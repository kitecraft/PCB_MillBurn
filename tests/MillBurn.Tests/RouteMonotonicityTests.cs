using MillBurn.Core;
using MillBurn.Optimize;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Sprint 1 story 3, the first half: the local search must never hand back a route worse than the
/// one it was given, and more search must never produce a worse answer than less.
///
/// The optimizer reports both numbers itself — `InitialTravelMm` is what nearest-neighbour
/// constructed, `TravelMm` is what came out — and they are measured the same way, over the same
/// steps, both including the trip home. So the invariant is checkable from outside without opening
/// the solver, which is why it is expressed over the whole search rather than move by move.
///
/// **Two invariants, and the second is the one with teeth.** A search that only applies strictly
/// improving moves cannot end worse than it started, and cannot end worse for having been allowed
/// more steps. The first is weak on its own: a search that cycles between three equally bad orders
/// satisfies it every time, because each of the three is still better than what it started from.
/// The second catches that, because the cycle lands wherever the budget happens to stop it.
///
/// Every case here asserts that the search actually did something. A route the constructor already
/// solved exercises nothing, and an assertion over a search that applied no moves is an assertion
/// about nothing — which is how the first draft of this file passed while testing nothing at all.
/// </summary>
public sealed class RouteMonotonicityTests(ITestOutputHelper output)
{
    private static Point2 At(double xMm, double yMm) =>
        new(Nm.FromMillimetres(xMm), Nm.FromMillimetres(yMm));

    /// <summary>Below a micron of travel over a whole route, a difference is arithmetic noise.</summary>
    private const double Tolerance = 0.001;

    /// <summary>
    /// A deliberately poor starting order: the cells of a grid visited by a stride coprime with the
    /// count, so every cell appears exactly once and consecutive nodes are far apart. A tidy order
    /// would leave the search nothing to do, which is the one thing these tests cannot afford.
    /// </summary>
    private static Point2 Cell(int i, int count, int columns)
    {
        var scrambled = i * 13 % count;
        return At(scrambled % columns * 10.0, scrambled / columns * 10.0);
    }

    private RoutePlan Solve(IReadOnlyList<RouteNode> nodes, RouteEffort effort)
    {
        var plan = RouteOptimizer.Solve(nodes, At(-20, -20), machine: null, effort, At(-20, -20));

        output.WriteLine(
            $"{effort,-9} {plan.InitialTravelMm,9:F3} mm -> {plan.TravelMm,9:F3} mm "
            + $"over {plan.Improvements,7} improvement(s)");

        return plan;
    }

    /// <summary>
    /// The route's length, measured here from the steps rather than taken from the plan.
    ///
    /// Everything else in this file is a number the optimizer reports about its own work, and a
    /// plan that measured both its figures from the same final ordering would satisfy every one of
    /// them while having done nothing. This is the one independent measurement, and the plan's own
    /// `TravelMm` is checked against it.
    /// </summary>
    private static double TravelOf(RoutePlan plan, Point2 from, Point2 returnTo)
    {
        var total = 0.0;
        var at = from;

        foreach (var step in plan.Steps)
        {
            total += at.DistanceTo(step.Entry) / Nm.PerMillimetre;
            at = step.Exit;
        }

        return total + (at.DistanceTo(returnTo) / Nm.PerMillimetre);
    }

    /// <summary>
    /// That the plan is an answer to the question asked: every node visited exactly once, in a
    /// configuration that node actually offers, with the entry and exit that configuration means.
    ///
    /// Without this, a search that quietly dropped nodes would report a shorter route and satisfy
    /// every other assertion here — the shortest route being the one that cuts nothing.
    /// </summary>
    private static void AssertPlanIsLegal(IReadOnlyList<RouteNode> nodes, RoutePlan plan)
    {
        Assert.Equal(nodes.Count, plan.Steps.Count);
        Assert.Equal(
            nodes.Select(n => n.Reference).Order(),
            plan.Steps.Select(s => s.Reference).Order());

        foreach (var step in plan.Steps)
        {
            var node = nodes.Single(n => n.Reference == step.Reference);

            Assert.InRange(step.Option, 0, node.OptionCount - 1);
            Assert.Equal(node.EntryFor(step.Option), step.Entry);
            Assert.Equal(node.ExitFor(step.Option), step.Exit);
        }
    }

    private void AssertNotWorse(IReadOnlyList<RouteNode> nodes)
    {
        var balanced = Solve(nodes, RouteEffort.Balanced);
        var thorough = Solve(nodes, RouteEffort.Thorough);

        Assert.True(
            balanced.Improvements > 0,
            "this case gave the search nothing to do, so it asserts nothing; fix the case, not the assertion");

        foreach (var plan in (RoutePlan[])[balanced, thorough])
        {
            AssertPlanIsLegal(nodes, plan);

            Assert.Equal(TravelOf(plan, At(-20, -20), At(-20, -20)), plan.TravelMm, 3);
        }

        Assert.True(
            balanced.TravelMm <= balanced.InitialTravelMm + Tolerance,
            $"Balanced made the route worse — {balanced.InitialTravelMm:F3} mm in, "
            + $"{balanced.TravelMm:F3} mm out, over {balanced.Improvements} applied 'improvement(s)'.");

        Assert.True(
            thorough.TravelMm <= thorough.InitialTravelMm + Tolerance,
            $"Thorough made the route worse — {thorough.InitialTravelMm:F3} mm in, "
            + $"{thorough.TravelMm:F3} mm out, over {thorough.Improvements} applied 'improvement(s)'.");

        // And a converged search gives the *same* answer whatever budget it is given, because it
        // stopped when nothing improved rather than when it ran out of steps. Equality is the honest
        // assertion here, not "no worse": a search that cycles satisfies "no worse" whenever the
        // cycle happens to stop on a good rung, and that is a coin toss rather than a guarantee.
        Assert.True(
            Math.Abs(thorough.TravelMm - balanced.TravelMm) <= Tolerance,
            $"the search did not converge — Balanced settled at {balanced.TravelMm:F3} mm over "
            + $"{balanced.Improvements} improvement(s), Thorough at {thorough.TravelMm:F3} mm over "
            + $"{thorough.Improvements}. A local optimum does not move when the budget grows.");
    }

    /// <summary>
    /// Closed contours, which is what an isolation program is made of: every node is a loop and any
    /// vertex of it can be the way in.
    /// </summary>
    [Fact]
    public void ClosedContoursAreNeverMadeWorse()
    {
        var nodes = new List<RouteNode>();

        for (var i = 0; i < 25; i++)
        {
            var at = Cell(i, 25, 5);
            nodes.Add(RouteNode.ForClosed(
                i,
                [at, At(Nm.ToMillimetres(at.X) + 3, Nm.ToMillimetres(at.Y)),
                 At(Nm.ToMillimetres(at.X) + 3, Nm.ToMillimetres(at.Y) + 3),
                 At(Nm.ToMillimetres(at.X), Nm.ToMillimetres(at.Y) + 3)]));
        }

        AssertNotWorse(nodes);
    }

    /// <summary>
    /// Open runs, which is what a channel centreline is: two ways in, and the far end is where the
    /// tool comes out.
    /// </summary>
    [Fact]
    public void OpenRunsAreNeverMadeWorse()
    {
        var nodes = new List<RouteNode>();

        for (var i = 0; i < 25; i++)
        {
            var at = Cell(i, 25, 5);
            nodes.Add(RouteNode.ForOpen(
                i, at, At(Nm.ToMillimetres(at.X) + 6, Nm.ToMillimetres(at.Y) + 4)));
        }

        AssertNotWorse(nodes);
    }

    /// <summary>
    /// Stacks whose direction is fixed, which is what a tabbed profile and an alternating channel
    /// become: one way in, one way out, and no reversing.
    ///
    /// This is the case the panel's outline is made entirely of — fifty-one of fifty-one — so if the
    /// search can go wrong on these it can go wrong on the program that cuts the board free.
    /// </summary>
    [Fact]
    public void FixedDirectionStacksAreNeverMadeWorse()
    {
        var nodes = new List<RouteNode>();

        for (var i = 0; i < 25; i++)
        {
            var at = Cell(i, 25, 5);

            // The two ends well apart and pointing the same way, because a fixed node whose ends
            // coincide is a point and cannot show a reversal being mis-costed.
            nodes.Add(RouteNode.ForFixed(
                i, at, At(Nm.ToMillimetres(at.X) + 6, Nm.ToMillimetres(at.Y) + 4)));
        }

        AssertNotWorse(nodes);
    }

    /// <summary>
    /// Stacks that keep their order but can be taken from either end, which is what a channel
    /// centreline cut at several depths really is.
    ///
    /// An even number of alternating passes comes back to where it started, so both of this node's
    /// configurations are a there-and-back: entered at one end or at the other, finishing where it
    /// began either way. That is the shape the panel's fifty channels have, and it is why the kind
    /// exists — neither an open run nor a loop describes it.
    /// </summary>
    [Fact]
    public void ReversibleStacksAreNeverMadeWorse()
    {
        var nodes = new List<RouteNode>();

        for (var i = 0; i < 25; i++)
        {
            var at = Cell(i, 25, 5);
            var far = At(Nm.ToMillimetres(at.X) + 6, Nm.ToMillimetres(at.Y) + 4);

            // Six alternating passes: out, back, out, back, out, back. Forward it starts and ends
            // at the near end; flipped, at the far end.
            nodes.Add(RouteNode.ForStack(i, at, at, far, far));
        }

        AssertNotWorse(nodes);
    }

    /// <summary>
    /// A stack with an odd number of passes, which finishes at the end it did not start from — so
    /// its two configurations are the mirror of each other rather than both being round trips.
    /// </summary>
    [Fact]
    public void ReversibleStacksWithAnOddNumberOfPassesAreNeverMadeWorse()
    {
        var nodes = new List<RouteNode>();

        for (var i = 0; i < 25; i++)
        {
            var at = Cell(i, 25, 5);
            var far = At(Nm.ToMillimetres(at.X) + 6, Nm.ToMillimetres(at.Y) + 4);

            nodes.Add(RouteNode.ForStack(i, at, far, far, at));
        }

        AssertNotWorse(nodes);
    }

    /// <summary>
    /// Every kind together, which is what a real export is: an isolation's loops, a channel's open
    /// runs, an outline's rigid stacks, the reversible stacks this story added, and a drilled hole.
    ///
    /// It said "every kind" while holding three of them, which is how a name stops being true
    /// without anybody editing it.
    /// </summary>
    [Fact]
    public void AMixtureOfEveryKindIsNeverMadeWorse()
    {
        var nodes = new List<RouteNode>();

        for (var i = 0; i < 25; i++)
        {
            var at = Cell(i, 25, 5);
            var x = Nm.ToMillimetres(at.X);
            var y = Nm.ToMillimetres(at.Y);
            var far = At(x + 6, y + 4);

            nodes.Add((i % 5) switch
            {
                0 => RouteNode.ForClosed(
                    i, [at, At(x + 3, y), At(x + 3, y + 3), At(x, y + 3)]),
                1 => RouteNode.ForOpen(i, at, far),
                2 => RouteNode.ForFixed(i, at, far),
                3 => RouteNode.ForStack(i, at, at, far, far),
                _ => RouteNode.ForPoint(i, at),
            });
        }

        AssertNotWorse(nodes);
    }

    /// <summary>
    /// A stack really is entered from whichever of its two ends suits the route, which is the whole
    /// of what the second half of story 3 bought.
    ///
    /// Nothing else in this file would notice a solver that ignored the second configuration: every
    /// other case is satisfied by entering every stack at its first end, because the assertions are
    /// about the route's length rather than about which way in was taken. So this one is built so
    /// that the answer is not a matter of degree. Each stack's option 0 is parked a metre away in X;
    /// its option 1 sits on a tidy line the tool is already walking. Any route that takes option 0
    /// even once is hundreds of millimetres longer than one that never does.
    /// </summary>
    [Fact]
    public void AStackIsEnteredFromWhicheverEndSuitsTheRoute()
    {
        var nodes = new List<RouteNode>();

        for (var i = 0; i < 12; i++)
        {
            var near = At(i * 10.0, 0);
            var absurd = At(1000 + (i * 10.0), 0);

            // Option 0 is the absurd one, so choosing it is visible rather than arguable.
            nodes.Add(RouteNode.ForStack(i, absurd, absurd, near, near));
        }

        var plan = Solve(nodes, RouteEffort.Balanced);

        AssertPlanIsLegal(nodes, plan);
        Assert.All(plan.Steps, s => Assert.Equal(1, s.Option));

        // And the route is the tidy line: out along it and back, not a trip to the far field.
        Assert.InRange(plan.TravelMm, 0, 300);
    }
}
