using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Cutting out more than one thing.
///
/// Found on a real fifty-up panel: the exporter cut the frame and left every board inside it
/// attached. It is the worst shape a bug can have — the file runs, the machine sounds right, the
/// frame comes free, and the boards are still in it. Nothing in the output said so, because from
/// the program's point of view it did exactly what it was asked.
/// </summary>
public sealed class PanelOutlineTests
{
    private static readonly OutlineOptions Options = new()
    {
        Tool = Tool.DefaultOutlineMill,
        BoardThicknessNm = Nm.FromMillimetres(1.6),
        TabCount = 0,
    };

    /// <summary>A rectangle, wound positive.</summary>
    private static Path64 Rect(double x, double y, double w, double h) =>
    [
        new(Nm.FromMillimetres(x), Nm.FromMillimetres(y)),
        new(Nm.FromMillimetres(x + w), Nm.FromMillimetres(y)),
        new(Nm.FromMillimetres(x + w), Nm.FromMillimetres(y + h)),
        new(Nm.FromMillimetres(x), Nm.FromMillimetres(y + h)),
    ];

    /// <summary>A frame with three small boards inside it, as a panel realises.</summary>
    private static Paths64 Panel() =>
    [
        Rect(0, 0, 100, 40),
        Rect(5, 5, 20, 10),
        Rect(35, 5, 20, 10),
        Rect(65, 5, 20, 10),
    ];

    [Fact]
    public void EveryProfileIsCutNotJustTheBiggest()
    {
        var toolpath = OutlineOperation.Build(Panel(), Options);

        var steps = toolpath.Passes.Select(p => p.DepthNm).Distinct().Count();

        // Four profiles at every depth, and nothing missing.
        Assert.Equal(4 * steps, toolpath.Passes.Count);
    }

    /// <summary>
    /// The frame's own filled rectangle covers every board inside it, so offsetting the profiles as
    /// one polygon set makes the boards vanish. Cutting length is the check that survives a
    /// refactor: three 20x10 boards are a lot of extra perimeter, and if they are gone it collapses.
    /// </summary>
    [Fact]
    public void TheInnerProfilesSurviveTheOffset()
    {
        var panel = OutlineOperation.Build(Panel(), Options);
        var frameOnly = OutlineOperation.Build([Rect(0, 0, 100, 40)], Options);

        var steps = panel.Passes.Select(p => p.DepthNm).Distinct().Count();
        var extra = (panel.Passes.Sum(p => p.LengthNm) - frameOnly.Passes.Sum(p => p.LengthNm))
            / Nm.PerMillimetre;

        // Three 20x10 boards offset outward by a 0.5 mm cutter radius are about 64 mm of perimeter
        // each, at every depth. Anything much under that means profiles went missing.
        Assert.True(
            extra > 3 * 60 * steps,
            $"expected about {3 * 64 * steps} mm of extra cutting for the three boards, got {extra:F0} mm");
    }

    /// <summary>
    /// Cut the frame first and everything still attached to it is loose while the cutter is still
    /// working. So the contained pieces are in an earlier group than the frame around them.
    /// </summary>
    [Fact]
    public void InnerPiecesAreCutBeforeTheFrameAroundThem()
    {
        var toolpath = OutlineOperation.Build(Panel(), Options);

        // The frame is the longest profile.
        var frame = toolpath.Passes.OrderByDescending(p => p.LengthNm).First();

        Assert.All(
            toolpath.Passes.Where(p => p.Stack != frame.Stack),
            p => Assert.True(
                p.Group < frame.Group,
                $"a contained profile is in group {p.Group}, the frame in {frame.Group}"));
    }

    /// <summary>
    /// Deeper after shallower <em>on the same contour</em> — which is what the design doc actually
    /// requires, and is a chain per contour rather than one global ordering.
    ///
    /// The distinction is worth five times the rapid on a panel. Forcing every contour to finish
    /// one depth before any starts the next means crossing the whole panel once per depth step,
    /// when the tool is already standing over the contour it is about to cut deeper.
    /// </summary>
    [Fact]
    public void EachContourGetsDeeperAndItsPassesStayTogether()
    {
        var toolpath = OutlineOperation.Build(Panel(), Options);

        foreach (var stack in toolpath.Passes.GroupBy(p => p.Stack))
        {
            Assert.True(stack.Key >= 0, "a profile's depth passes must be stacked");

            var depths = stack.Select(p => p.DepthNm).ToList();
            for (var i = 1; i < depths.Count; i++)
            {
                Assert.True(depths[i] >= depths[i - 1], "a deeper pass came before a shallower one");
            }
        }

        // And each stack is contiguous in the emitted order.
        var seen = new HashSet<int>();
        var current = int.MinValue;

        foreach (var pass in toolpath.Passes)
        {
            if (pass.Stack == current)
            {
                continue;
            }

            Assert.True(seen.Add(pass.Stack), $"stack {pass.Stack} was left and returned to");
            current = pass.Stack;
        }
    }

    /// <summary>A single board must be unaffected: one profile, one stack, one group.</summary>
    [Fact]
    public void OneBoardIsStillOneProfile()
    {
        var toolpath = OutlineOperation.Build([Rect(0, 0, 30, 20)], Options);

        var steps = toolpath.Passes.Select(p => p.DepthNm).Distinct().Count();

        Assert.Equal(steps, toolpath.Passes.Count);
        Assert.Single(toolpath.Passes.Select(p => p.Group).Distinct());
        Assert.Single(toolpath.Passes.Select(p => p.Stack).Distinct());
    }

    [Fact]
    public void NoProfileAtAllIsNotACrash()
    {
        var toolpath = OutlineOperation.Build([], Options);

        Assert.Empty(toolpath.Passes);
        Assert.NotEmpty(toolpath.Notes);
    }
}
