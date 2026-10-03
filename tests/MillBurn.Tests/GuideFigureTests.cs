using System.Text.RegularExpressions;
using MillBurn.Core;
using MillBurn.Gcode;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// The picture on a companion page: the board, and what one bit does to it.
///
/// **Requested from the workshop:** *"in the companion html for drills and routing, include images of
/// the layer with the holes/slots numbered in the order of drilling/routing."* Both pages were a
/// table of numbers standing in for a board — *six holes with the 1.00 mm bit* — at the two moments
/// the page exists for: deciding a bit is about to go in the wrong place, and finding where a run
/// stopped.
///
/// What is asserted here is what the drawing *says*, read back out of the markup: how many marks,
/// which number is beside which hole, that a run has an arrow at its far end, and that above forty
/// marks the drawing stops pretending to number them. The look of it is a separate question and was
/// answered by rendering the figures and looking at them — twice, which is how the numbers came to
/// sit beside their holes rather than above them.
///
/// Recorded in [06 §6.5](../../Documentation/06-Roadmap-and-Risks.md).
/// </summary>
public sealed class GuideFigureTests(ITestOutputHelper output)
{
    private static long Mm(double mm) => Nm.FromMillimetres(mm);

    /// <summary>A 40 x 30 mm board, as the one ring a figure draws behind its marks.</summary>
    private static IReadOnlyList<IReadOnlyList<Point2>> Board() =>
    [
        [
            new(0, 0),
            new(Mm(40), 0),
            new(Mm(40), Mm(30)),
            new(0, Mm(30)),
        ],
    ];

    private static Bounds Extent() => new(0, 0, Mm(40), Mm(30));

    /// <summary>Every number on the drawing, in the order it was written.</summary>
    private static List<string> Labels(string svg) =>
        [.. Regex.Matches(svg, "<text[^>]*>([^<]*)</text>").Select(m => m.Groups[1].Value)];

    /// <summary>Every number with where it was put, for asking which mark it belongs to.</summary>
    private static List<(string Text, double X, double Y)> Placed(string svg) =>
    [
        .. Regex.Matches(svg, "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>([^<]*)</text>")
            .Select(m => (
                m.Groups[3].Value,
                double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))),
    ];

    private static List<(double X, double Y, double R)> Circles(string svg) =>
    [
        .. Regex.Matches(svg, "<circle cx=\"([-\\d.]+)\" cy=\"([-\\d.]+)\" r=\"([\\d.]+)\"")
            .Select(m => (
                double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture))),
    ];

    /// <summary>
    /// Three holes, three numbers, and each number beside its own hole.
    ///
    /// **The order is the whole point of the drawing**, and "beside its own hole" is the part that
    /// cannot be checked by counting: the first version placed every number above and to the right,
    /// so on a column of holes a millimetre apart each number sat between two of them. Here each
    /// label is matched to the nearest circle and has to be the right one.
    /// </summary>
    [Fact]
    public void EachHoleIsNumberedInTheOrderItIsDrilled()
    {
        Point2[] holes = [new(Mm(5), Mm(5)), new(Mm(20), Mm(15)), new(Mm(35), Mm(25))];

        var svg = GuideFigure.Draw(
            Board(),
            [.. holes.Select(h => new GuideMark { At = h, WidthNm = Mm(1) })],
            Extent(),
            "three holes");

        output.WriteLine(svg);

        Assert.Equal(["1", "2", "3"], Labels(svg));

        var circles = Circles(svg);
        Assert.Equal(3, circles.Count);

        // **The circles are in the order the holes were given**, which a `/describe-test` pass found
        // nothing checking: the pairing below is internally consistent either way round, so a figure
        // that emitted the holes backwards and numbered them backwards would have passed it. The
        // holes here run up and to the right, and the drawing counts down the page where the board
        // counts up — so x rises and y falls across the three.
        Assert.True(
            circles[0].X < circles[1].X && circles[1].X < circles[2].X,
            "the holes were not drawn left to right, so the numbering is not in the order they were given");

        Assert.True(
            circles[0].Y > circles[1].Y && circles[1].Y > circles[2].Y,
            "the figure did not flip the board's y, or the holes were drawn out of order");

        // The drawing counts down the page where the board counts up, so a hole at y = 5 mm is near
        // the bottom of the figure. Matching each label to its nearest circle rather than asserting
        // coordinates keeps this about the pairing and not about the margin.
        foreach (var (text, x, y) in Placed(svg))
        {
            var nearest = circles
                .OrderBy(c => ((c.X - x) * (c.X - x)) + ((c.Y - y) * (c.Y - y)))
                .First();

            var expected = circles[int.Parse(text, System.Globalization.CultureInfo.InvariantCulture) - 1];

            output.WriteLine($"label {text} at {x}, {y} is nearest the circle at {nearest.X}, {nearest.Y}");

            Assert.Equal(expected, nearest);
        }
    }

    /// <summary>
    /// A hole is drawn the size it is drilled.
    ///
    /// A 3.5 mm hole and a 0.3 mm one look the same as dots, and which is which is half of what
    /// somebody checks a drawing for.
    /// </summary>
    [Fact]
    public void AHoleIsDrawnAtItsOwnDiameter()
    {
        var svg = GuideFigure.Draw(
            Board(),
            [new GuideMark { At = new Point2(Mm(20), Mm(15)), WidthNm = Mm(3.5) }],
            Extent(),
            "one big hole");

        var circle = Assert.Single(Circles(svg));

        output.WriteLine($"radius {circle.R} mm");

        Assert.Equal(1.75, circle.R, 0.01);
    }

    /// <summary>
    /// Above forty marks the drawing stops numbering and says where the run begins instead.
    ///
    /// **It degrades rather than producing something unreadable and calling it a feature.** The
    /// Arduino Mega drills 258 holes in six sizes; numbering 44 of them on a 100 mm board is already
    /// crowded. What a crowded drawing can still answer is *where does it begin and roughly where
    /// does it go*, so that is what it answers: dots, the travel between them, and a ring on the
    /// first one.
    /// </summary>
    [Fact]
    public void AboveFortyMarksItDrawsTheRunRatherThanNumbers()
    {
        var many = Enumerable.Range(0, GuideFigure.MostToNumber + 1)
            .Select(i => new GuideMark { At = new Point2(Mm(2 + (i * 0.8)), Mm(15)) })
            .ToList();

        var svg = GuideFigure.Draw(Board(), many, Extent(), "too many to number");

        output.WriteLine($"{many.Count} marks, {Labels(svg).Count} numbers, {Circles(svg).Count} circles");

        Assert.Empty(Labels(svg));

        // One dot each, and one more ring around the first: the start is the one place worth picking
        // out of a field of dots.
        Assert.Equal(many.Count + 1, Circles(svg).Count);

        // And the travel path, which is what makes the order readable at all without numbers. Two
        // paths: the board behind it, and the run.
        Assert.Equal(2, Regex.Count(svg, "<path"));
    }

    /// <summary>
    /// Exactly forty is still numbered: the limit is where the drawing gives up, not where it hesitates.
    /// </summary>
    [Fact]
    public void AtTheLimitItStillNumbers()
    {
        var marks = Enumerable.Range(0, GuideFigure.MostToNumber)
            .Select(i => new GuideMark { At = new Point2(Mm(2 + (i * 0.9)), Mm(15)) })
            .ToList();

        var svg = GuideFigure.Draw(Board(), marks, Extent(), "right at the limit");

        Assert.Equal(GuideFigure.MostToNumber, Labels(svg).Count);
    }

    /// <summary>
    /// A slot is drawn as its own shape, with an arrow at the end showing which way the cutter runs.
    ///
    /// **The direction is not decoration.** It is what tells somebody watching the machine that it is
    /// doing what the page says — and the number goes at the end the cutter starts from, so the two
    /// together read as *in at this one, out at that one*.
    /// </summary>
    [Fact]
    public void ASlotIsDrawnWithAnArrowAtTheEndOfItsRun()
    {
        Point2[] run = [new(Mm(10), Mm(15)), new(Mm(30), Mm(15))];

        var svg = GuideFigure.Draw(
            Board(),
            [new GuideMark { At = run[1], Path = run, WidthNm = Mm(1) }],
            Extent(),
            "one slot");

        output.WriteLine(svg);

        // The run itself, the arrowhead, and the board: three paths.
        Assert.Equal(3, Regex.Count(svg, "<path"));

        // The arrowhead is a filled triangle, and it is at the far end of the run rather than the
        // near one: a figure that drew it at the start would be stating the opposite of the truth.
        var head = Regex.Match(svg, "<path fill=\"#1a73e8\" d=\"M ([\\d.]+) ([\\d.]+)");

        Assert.True(head.Success, "no arrowhead was drawn");

        var tipX = double.Parse(head.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        output.WriteLine($"arrow tip at x = {tipX} mm, run from 10 to 30 mm");

        Assert.Equal(31, tipX, 0.5);

        // And the number sits at the end it starts from, clear of the run.
        var label = Assert.Single(Placed(svg));

        Assert.Equal("1", label.Text);
        Assert.True(label.X < 11, $"the number is at x = {label.X} mm, which is on the slot rather than beside its start");
    }

    /// <summary>
    /// A feature the program will not make is hatched, drawn dashed, and labelled with its width.
    ///
    /// **This is the strongest reason the drawing exists**, and it is about routing rather than
    /// drilling: a refused slot is invisible. It is not in the program, and the board renders exactly
    /// as the EDA tool drew it, so prose is the only thing that can report it — and a shape somebody
    /// recognises beats a sentence they skim. It is labelled with its width rather than a number
    /// because it has no place in the running order: it is not in the program.
    ///
    /// No board in the corpus refuses a slot with the author's own tool library, so this case is
    /// synthetic and the figure was rendered and looked at from this test's output.
    /// </summary>
    [Fact]
    public void ARefusedFeatureIsHatchedAndLabelledWithItsWidth()
    {
        Point2[] cut = [new(Mm(8), Mm(22)), new(Mm(20), Mm(22))];
        Point2[] refused = [new(Mm(8), Mm(8)), new(Mm(20), Mm(8))];

        var svg = GuideFigure.Draw(
            Board(),
            [
                new GuideMark { At = cut[1], Path = cut, WidthNm = Mm(1.2) },
                new GuideMark { At = refused[1], Path = refused, WidthNm = Mm(0.6), Refused = true },
            ],
            Extent(),
            "one cut, one refused");

        output.WriteLine(svg);

        // The hatch is declared once and *used*, and the dash is on the refused shape rather than
        // merely somewhere in the figure — both of which a `/describe-test` pass pointed out were
        // being taken on trust: the pattern could have been declared and never referenced, and the
        // dash could have been on the board outline.
        Assert.Contains("<pattern id=\"refused\"", svg, StringComparison.Ordinal);

        var hatched = Regex.Match(svg, "<path fill=\"url\\(#refused\\)\"[^>]*>");

        Assert.True(hatched.Success, "nothing in the figure is filled with the refused hatch");
        Assert.Contains("stroke-dasharray", hatched.Value, StringComparison.Ordinal);

        // Its width, not a number — and the one that is cut keeps its place in the order.
        Assert.Equal(["1", "0.60"], Labels(svg));

        // Only the run that is cut gets an arrow: a direction on something nothing will cut would be
        // a statement about a machine movement that is not going to happen.
        Assert.Single(Regex.Matches(svg, "<path fill=\"#[0-9a-f]{6}\""));
    }

    /// <summary>
    /// Without a board there is no picture, rather than a frame around nothing.
    ///
    /// A frame with marks floating in it looks like a board with no outline, which is a claim about
    /// the Gerbers that this has no business making.
    /// </summary>
    [Fact]
    public void WithoutABoardThereIsNoPicture()
    {
        Assert.Empty(GuideFigure.Draw([], [new GuideMark { At = default }], Bounds.Empty, "nothing"));

        Assert.Empty(GuideFigure.Draw(Board(), [], Extent(), "nothing to draw"));
    }

    /// <summary>
    /// The picture is small enough to travel inside a page somebody opens on a phone.
    ///
    /// The pages are self-contained by rule, so every byte in a figure is a byte somebody downloads
    /// over whatever a workshop has. A hundred holes on a board with a tessellated outline is the
    /// worst case the corpus offers; a legible raster of the same board is hundreds of kilobytes,
    /// which is the comparison that made this SVG rather than a PNG.
    /// </summary>
    [Fact]
    public void ThePictureIsAFewKilobytes()
    {
        var holes = Enumerable.Range(0, 100)
            .Select(i => new GuideMark { At = new Point2(Mm(2 + (i % 20 * 1.9)), Mm(2 + (i / 20 * 5))) })
            .ToList();

        var svg = GuideFigure.Draw(Board(), holes, Extent(), "a hundred holes");

        // Bytes, not characters: the page is a file somebody downloads, and the message said bytes
        // while the assertion counted UTF-16 units.
        var bytes = System.Text.Encoding.UTF8.GetByteCount(svg);

        output.WriteLine($"{bytes} bytes for {holes.Count} marks");

        Assert.True(bytes < 12_000, $"a figure of {bytes} bytes is not a few kilobytes");
    }
}
