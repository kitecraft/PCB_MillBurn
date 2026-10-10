using System.Globalization;
using System.Text.RegularExpressions;
using MillBurn.Core;
using MillBurn.Gcode;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The test cut that finds where the copper stops.
///
/// **From the product owner, 2026-09-27:** *"A new test for copper thickness. This test will cut a
/// series of ladders… The first rung is very shallow and each rung gets very small increment deeper.
/// This test will help users determine the optimal cut depth for their piece. For this particular
/// test, I think we should increase the granularity of the probe job."* Recorded in
/// [06 §6.52](../../Documentation/06-Roadmap-and-Risks.md).
///
/// **The geometry already existed; the resolution did not.** The depth series cuts exactly this
/// shape, and its steps are 0.050 mm on purpose — a narrow spread of widths sits inside a caliper's
/// own error. One-ounce foil is about 0.035 mm thick, so one of those steps crosses the whole of it
/// and no rung lands where the answer is. What these tests pin is the resolution, the absence of the
/// width ladder, the probing the test asks for, and that nothing on the coupon tells the operator to
/// reach for a caliper.
/// </summary>
public sealed class CopperTestCutTests(ITestOutputHelper output)
{
    private static TestCutOptions Copper => TestCutOptions.For(TestCutKind.Copper, Tool.DefaultVBit);

    /// <summary>Every Z the program actually commands, in the order it commands them.</summary>
    private static List<double> CutDepths(string program) =>
    [
        .. Regex.Matches(program, @"^G1 Z-([\d.]+)", RegexOptions.Multiline)
            .Select(m => Math.Round(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 4)),
    ];

    /// <summary>
    /// Twelve rungs from 0.010 mm, a hundredth apart, and the program cuts at exactly those depths.
    ///
    /// **The Done-when of this story**, and asserted against the emitted G-code rather than against
    /// the plan: the plan is what the app believes and the program is what the machine does, and the
    /// rung an operator reads off a coupon is the second one. Every commanded Z is matched to the
    /// rung it belongs to, including the repeat, which is the first rung cut again at the far end.
    /// </summary>
    [Fact]
    public void TwelveRungsAHundredthApartAndTheProgramCutsThem()
    {
        var (program, report) = TestCut.Generate(Copper);

        var planned = report.Lines.Where(l => !l.IsRepeat).Select(l => Math.Round(l.DepthMm, 4)).ToList();

        output.WriteLine(string.Join(", ", planned));

        Assert.Equal(
            [0.010, 0.020, 0.030, 0.040, 0.050, 0.060, 0.070, 0.080, 0.090, 0.100, 0.110, 0.120],
            planned);

        // Each rung is one plunge and then its passes at that Z, so the commanded depths are the
        // rungs in order with the repeat's 0.010 last.
        var cut = CutDepths(program).Distinct().ToList();

        output.WriteLine(string.Join(", ", CutDepths(program)));

        Assert.Equal(planned, cut);
        Assert.Equal(0.010, CutDepths(program)[^1]);

        // And every rung really is cut, not merely planned: thirteen plunges for twelve rungs and
        // the repeat.
        Assert.Equal(13, CutDepths(program).Count);

        // The repeat said as a plan as well as a plunge, which a `/describe-test` pass found nothing
        // doing: a coupon that lost the flag but kept the thirteenth cut would have passed
        // everything above, and the page's "repeat of 1" tag comes off the flag rather than off the
        // depth.
        Assert.Equal(13, report.Lines.Count);
        Assert.Single(report.Lines, l => l.IsRepeat);
    }

    /// <summary>
    /// A copper coupon has no width ladder, even when one is asked for.
    ///
    /// Decided with the product owner: one ladder, shallow to deep, plus the repeat. A width ladder
    /// asks what the cut *is* at one depth, which is the depth test's question — and on a coupon
    /// read by eye it would sit under twelve rungs of a different experiment with nothing to say
    /// which is which.
    /// </summary>
    [Fact]
    public void ACopperCouponHasNoWidthLadder()
    {
        Assert.Equal(0, Copper.LadderRungs);

        // Asked for explicitly, and still refused: the kind decides this, not the number.
        var (_, report) = TestCut.Generate(Copper with { LadderRungs = 5 });

        Assert.DoesNotContain(report.Lines, l => l.IsLadder);

        // And the depth test still has one, so this is about the kind rather than about the ladder
        // having stopped working.
        var (_, depth) = TestCut.Generate(TestCutOptions.For(TestCutKind.Depth, Tool.DefaultVBit));

        Assert.Contains(depth.Lines, l => l.IsLadder);
    }

    /// <summary>
    /// The copper test asks for finer probing than a coupon its size would otherwise get, and the
    /// other kinds are unchanged.
    ///
    /// **The rule lives in one place now**, which it did not: the expression was written out in the
    /// CLI and again in the window, so nothing kept the two in step and the rule that decides
    /// whether this test means anything belonged to neither.
    ///
    /// Asserted both ways. The relation between the two rules is what matters and holds if either
    /// is retuned; the millimetre figure is pinned as a literal as well, because a `/describe-test`
    /// pass pointed out that comparing the answer with the constant it comes from is a comparison
    /// that cannot fail, and three millimetres is the number this story decided on.
    /// </summary>
    [Fact]
    public void TheCopperCouponIsProbedFinerThanTheCouponRuleGives()
    {
        var (_, copper) = TestCut.Generate(Copper);
        var (_, depth) = TestCut.Generate(TestCutOptions.For(TestCutKind.Depth, Tool.DefaultVBit));

        var asked = TestCut.ProbeSpacingMmFor(copper);
        var rule = TestCut.CouponSpacingMmFor(copper);

        output.WriteLine($"copper coupon {copper.StockWidthMm:F1} x {copper.StockHeightMm:F1} mm: "
            + $"asks for {asked:F2} mm against the rule's {rule:F2} mm");

        Assert.True(asked < rule, $"{asked:F2} mm is not finer than {rule:F2} mm");
        Assert.Equal(3, asked);
        Assert.Equal(TestCut.FineSpacingMm, asked);

        // Every other kind takes the coupon rule untouched: the global probing settings and the
        // other two tests pay nothing for this.
        Assert.Equal(TestCut.CouponSpacingMmFor(depth), TestCut.ProbeSpacingMmFor(depth));
    }

    /// <summary>
    /// Nothing on a copper coupon tells the operator to measure a band and subtract from it.
    ///
    /// The depth series says exactly that, on every line, because it is read with a caliper and the
    /// stepped-over ground has to come off the reading. This one is two transitions found with a
    /// loupe: the same sentence here would send somebody looking for a width that is not the answer
    /// to anything they are being asked.
    /// </summary>
    [Fact]
    public void TheProgramNeverAsksForACaliper()
    {
        var (program, _) = TestCut.Generate(Copper);

        output.WriteLine(string.Join("\n", program.Split('\n').Where(l => l.StartsWith('(')).Take(30)));

        Assert.DoesNotContain("subtract", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("band predicted", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("caliper", program, StringComparison.OrdinalIgnoreCase);

        // What it asks for instead, against each rung and in the header.
        Assert.Contains("copper all gone?", program, StringComparison.Ordinal);
        Assert.Contains("loupe", program, StringComparison.Ordinal);

        // And the depth series still says it, so this is not the instruction having been lost.
        var (series, _) = TestCut.Generate(TestCutOptions.For(TestCutKind.Depth, Tool.DefaultVBit));

        Assert.Contains("subtract", series, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The program's own header says the step it is actually cutting, and asks for a look rather
    /// than a measurement.
    ///
    /// **Both found by `/code-review`.** The header said "a hundredth at a time" whatever `--step`
    /// was given, so a coupon cut at 0.020 mm told the operator 0.010 — in the file that is read at
    /// the machine, where the note that would have contradicted it does not appear. And the repeat
    /// line said "if they do not *measure* the same" on a coupon this kind went out of its way to
    /// stop calling measurable: the per-line comment had been changed and the header had not.
    /// </summary>
    [Theory]
    [InlineData(0.010)]
    [InlineData(0.020)]
    public void TheHeaderSaysTheStepItIsCutting(double stepMm)
    {
        var (program, _) = TestCut.Generate(Copper with { DepthStepMm = stepMm });

        var header = string.Join(
            "\n",
            program.Split('\n').TakeWhile(l => l.StartsWith('(')));

        output.WriteLine(header);

        Assert.Contains(
            FormattableString.Invariant($"{stepMm:F3} mm at a time"),
            header,
            StringComparison.Ordinal);

        Assert.DoesNotContain("a hundredth at a time", header, StringComparison.Ordinal);

        // And nothing in the header asks for a caliper, which is what the rest of this coupon
        // already promises.
        Assert.Contains("look the same", header, StringComparison.Ordinal);
        Assert.DoesNotContain("measure the same", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// A step as coarse as the copper is thick is refused in words, because it is the whole reason
    /// this kind exists.
    ///
    /// Somebody who opens this test and types the depth series' own step back into it has turned it
    /// back into the depth series without changing anything else on screen. The number is in the
    /// warning so it can be compared with the one in the box.
    /// </summary>
    [Theory]
    [InlineData(0.05, true)]
    [InlineData(0.035, true)]
    [InlineData(0.010, false)]
    public void AStepThatCrossesTheWholeFoilIsFlagged(double stepMm, bool flagged)
    {
        var (_, report) = TestCut.Generate(Copper with { DepthStepMm = stepMm });

        foreach (var warning in report.Warnings)
        {
            output.WriteLine(warning);
        }

        Assert.Equal(
            flagged,
            report.Warnings.Any(w => w.Contains("crosses the whole of it", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A ladder that never reaches the copper's underside is refused, and so is one that starts past
    /// it.
    ///
    /// Both ends, because they fail differently and neither is visible in the coupon: too shallow
    /// and nothing ever breaks through, so there is no second transition; too deep and the first one
    /// happened before rung 1.
    /// </summary>
    [Fact]
    public void ALadderThatMissesTheFoilEntirelyIsFlagged()
    {
        var (_, shallow) = TestCut.Generate(Copper with { LineCount = 2 });
        var (_, deep) = TestCut.Generate(Copper with { StartDepthMm = 0.08 });

        output.WriteLine(string.Join("\n", shallow.Warnings.Concat(deep.Warnings)));

        Assert.Contains(shallow.Warnings, w => w.Contains("never cut through", StringComparison.Ordinal));
        Assert.Contains(deep.Warnings, w => w.Contains("behind the start of the ladder", StringComparison.Ordinal));

        // And the defaults are clear of both, which is the case that matters: a test that warns
        // about itself out of the box teaches people to ignore its warnings.
        var (_, fine) = TestCut.Generate(Copper);

        Assert.Empty(fine.Warnings);
    }

    /// <summary>
    /// The guide separates ribbing from thickness, which is the one way this coupon lies.
    ///
    /// The stepover is derived from the width the tool library *predicts* at the shallowest rung,
    /// and the premise of the test is that the library is not trusted. A blunter tip leaves rows of
    /// copper standing between the passes, which looks exactly like copper that has not been cut
    /// through — the same appearance, opposite answers. Named in the roadmap entry as a risk before
    /// this was built, so the page is checked for it rather than assumed to carry it.
    /// </summary>
    [Fact]
    public void TheGuideTellsRibbingFromThickness()
    {
        var options = Copper;
        var (_, report) = TestCut.Generate(options);
        var page = TestCutGuide.Build(options, report, "copper.nc");

        Assert.Contains("Where the copper stops", page, StringComparison.Ordinal);

        // The two transitions, and the gap between them as a reading in its own right.
        Assert.Contains("no copper left anywhere in its band", page, StringComparison.Ordinal);
        Assert.Contains("into the fibreglass", page, StringComparison.Ordinal);
        Assert.Contains("Adjacent rungs mean you have nothing in hand", page, StringComparison.Ordinal);

        // The lie, and how to tell which it is.
        Assert.Contains("Rows of copper are not the same as copper left", page, StringComparison.Ordinal);
        Assert.Contains("evenly spaced", page, StringComparison.Ordinal);
        Assert.Contains("the stepover was too wide for this depth", page, StringComparison.Ordinal);

        // And what the probing will be, which is the operator's only warning that this test costs
        // more minutes than the others.
        Assert.Contains(
            FormattableString.Invariant($"a touch every {TestCut.ProbeSpacingMmFor(report):F1} mm"),
            page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The page is a checklist, because the coupon is read rather than measured.
    ///
    /// Two columns to tick and one row per rung, and no predicted widths anywhere — a width in
    /// millimetres on this page is an invitation to reach for the wrong instrument.
    /// </summary>
    [Fact]
    public void TheGuideIsATickListWithARowPerRung()
    {
        var options = Copper;
        var (_, report) = TestCut.Generate(options);
        var page = TestCutGuide.Build(options, report, "copper.nc");

        Assert.Contains("Copper all gone?", page, StringComparison.Ordinal);
        Assert.Contains("Into the fibreglass?", page, StringComparison.Ordinal);

        // Case-insensitively absent: "Measured band" is the same column under a different shift key.
        Assert.DoesNotContain("predicted band", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("measured band", page, StringComparison.OrdinalIgnoreCase);

        // One row per rung and the repeat, each carrying its own depth: the table is what gets
        // printed and written on, so a rung missing from it is a rung nobody records.
        foreach (var line in report.Lines)
        {
            Assert.Contains(
                FormattableString.Invariant($"<td>{line.DepthMm:F3} mm</td>"),
                page,
                StringComparison.Ordinal);
        }

        // **Counted, not just present**, which a `/describe-test` pass is the reason for: the repeat
        // shares its depth with rung 1, so a page that printed one row per *distinct* depth would
        // have satisfied the loop above with a row missing. And the blank cells are counted exactly
        // rather than halved, where 26 and 27 both used to pass.
        Assert.Equal(report.Lines.Count, Regex.Count(page, @"<td>\d\.\d{3} mm</td>"));
        Assert.Equal(report.Lines.Count * 2, Regex.Count(page, "class=\"blank\""));
    }
}
