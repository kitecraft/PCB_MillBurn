using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
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

    private static Toolpath TabbedOutline(int boards, double stepdownMm)
    {
        Paths64 outline = [];
        for (var i = 0; i < boards; i++)
        {
            outline.AddRange(Board(i * -70.0));
        }

        return OutlineOperation.Build(outline, new OutlineOptions
        {
            Tool = Tool.DefaultOutlineMill with { StepdownNm = Nm.FromMillimetres(stepdownMm) },
            BoardThicknessNm = Nm.FromMillimetres(1.6),
            DepthPerPassNm = Nm.FromMillimetres(stepdownMm),
            TabCount = 4,
            Keep = [],
        });
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
