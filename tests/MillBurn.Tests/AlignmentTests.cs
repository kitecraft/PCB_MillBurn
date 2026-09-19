using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace MillBurn.Tests;

/// <summary>
/// Drill alignment: hover a bit over a real hole, find the offset by eye, and write the drilling and
/// routing files again with it.
///
/// A small hole in a small pad leaves a few tenths either side, so a drilling origin slightly out puts
/// holes on the edge of their pads. The fix is an origin shift, and — when a second hole says the board
/// is not square to the machine — a turn with it. Every one of these tests is about that correction
/// being exactly what was measured, on exactly the files it is meant for.
/// </summary>
public sealed class AlignmentTests(ITestOutputHelper output)
{
    private static ExportPlan Plan(DrillAlignment? alignment = null, JobOptions? job = null)
    {
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var settings = loaded.Layers.ToDictionary(
            l => l.FileName,
            l => new LayerOutputSettings { FileName = l.FileName, Output = LayerOperations.DefaultFor(l.Role) },
            StringComparer.Ordinal);

        return ExportPlanner.Plan(
            loaded, settings, ToolLibrary.Default, Nm.FromMillimetres(1.6), OutputKind.Gcode,
            job: job, alignment: alignment);
    }

    // ------------------------------------------------------------------ the holes to hover over

    /// <summary>Every hole a drilling file makes, in the order it makes them, and nothing else.</summary>
    [Fact]
    public void EveryHoleADrillingFileMakesIsATarget()
    {
        var drilling = Plan().Items.Where(i => i.Operation == OperationKind.Drilling).ToList();

        Assert.NotEmpty(drilling);

        foreach (var file in drilling)
        {
            var holes = GcodeParser.Parse(file.Content).Moves
                .Where(m => !m.IsRapid && m.IsVertical && m.ToZNm < 0)
                .Select(m => m.From)
                .Distinct()
                .ToList();

            var targets = AlignmentTest.Targets(file.Content);

            output.WriteLine($"{file.TargetName}: {holes.Count} holes, {targets.Count} targets");

            Assert.Equal(holes, targets.Select(t => t.At));
            Assert.All(targets, t => Assert.Equal("hole", t.Kind));
            Assert.Equal(Enumerable.Range(1, targets.Count), targets.Select(t => t.Number));
        }
    }

    /// <summary>
    /// A milled hole is a helix, and its target is the centre of the circle it cuts — which is the hole
    /// in the drill file, in work coordinates. Its pecks and depth passes are one target, not several.
    /// </summary>
    [Fact]
    public void AMilledHoleIsTargetedAtItsCentre()
    {
        // The stock library's only drill is 1.0 mm, so PogoTest1's larger holes are milled when asked.
        var plan = Plan(job: new JobOptions { MillLargeHoles = true });
        var loaded = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));

        var centres = loaded.Layers
            .Where(l => l.Drill is not null)
            .SelectMany(l => l.Drill!.Hits)
            .Select(h => new Point2(h.At.X - loaded.Bounds.MinX, h.At.Y - loaded.Bounds.MinY))
            .ToList();

        var routed = plan.Items.Where(i => i.TargetName.Contains(".slots.", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(routed);

        foreach (var file in routed)
        {
            var targets = AlignmentTest.Targets(file.Content);

            output.WriteLine($"{file.TargetName}: {string.Join(", ", targets.Select(t => $"{t.Kind} X{Nm.ToMillimetreString(t.At.X, 3)} Y{Nm.ToMillimetreString(t.At.Y, 3)}"))}");

            Assert.NotEmpty(targets);
            Assert.All(targets, t => Assert.Contains(
                centres,
                c => Math.Abs(c.X - t.At.X) <= Nm.FromMillimetres(0.01) && Math.Abs(c.Y - t.At.Y) <= Nm.FromMillimetres(0.01)));
        }
    }

    // ------------------------------------------------------------------ the test program

    /// <summary>
    /// Over the hole, moved by the offset, down to the hover height and no lower, spindle off — and it
    /// stays there, because the point is to look at the tip.
    /// </summary>
    [Fact]
    public void TheTestHoversOverTheHoleAndNeverGoesLower()
    {
        var target = new AlignmentTarget(3, new Point2(Nm.FromMillimetres(23.62), Nm.FromMillimetres(47.878)), "hole");
        var offset = new Point2(Nm.FromMillimetres(0.12), Nm.FromMillimetres(-0.05));

        var text = AlignmentTest.Generate(
            target, offset, "Board-PTH-drl.bit1-1.00mm.nc", new AlignmentTestOptions { HoverMm = 0.1, SafeZMm = 5 });

        output.WriteLine(text);

        var moves = GcodeParser.Parse(text).Moves;
        var lines = text.Split('\n').Select(l => l.Trim()).ToList();

        Assert.Equal(Nm.FromMillimetres(0.1), moves.Min(m => m.ToZNm));
        Assert.Equal(new Point2(Nm.FromMillimetres(23.74), Nm.FromMillimetres(47.828)), moves[^1].To);
        Assert.Equal(Nm.FromMillimetres(0.1), moves[^1].ToZNm);

        Assert.Contains("M5", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("M3", StringComparison.Ordinal));
        Assert.Contains("Offset X+0.120 Y-0.050 mm", text, StringComparison.Ordinal);
    }

    /// <summary>A hover height of zero or less is a scratch through the pad being checked, and is refused.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    public void TheBitIsNeverSentToTheSurfaceOrBelow(double hover) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AlignmentTest.Generate(
            new AlignmentTarget(1, Point2.Origin, "hole"), Point2.Origin, "a.nc", new AlignmentTestOptions { HoverMm = hover }));

    // ------------------------------------------------------------------ the aligned files

    /// <summary>
    /// Every drilling and routing file, written again under its aligned name, with every move shifted by
    /// exactly the offset; and every other file exactly as it was.
    /// </summary>
    [Fact]
    public void AlignedFilesAreThePlainFilesMovedByTheOffset()
    {
        var offset = new DrillAlignment(Nm.FromMillimetres(0.12), Nm.FromMillimetres(-0.05));

        var plain = Plan();
        var aligned = Plan(offset);

        var drill = plain.Items.Where(ExportPlanner.IsDrillOrRouting).ToList();

        Assert.NotEmpty(drill);

        foreach (var original in drill)
        {
            var name = Path.GetFileNameWithoutExtension(original.TargetName) + ".aligned.nc";
            var moved = Assert.Single(aligned.Items, i => i.TargetName == name);

            // The park home at the end goes to work zero in both, and is not part of the work.
            var before = GcodeParser.Parse(original.Content).Moves.Where(m => m.MovesInPlane && m.To != Point2.Origin).ToList();
            var after = GcodeParser.Parse(moved.Content).Moves.Where(m => m.MovesInPlane && m.To != Point2.Origin).ToList();

            output.WriteLine($"{original.TargetName} -> {moved.TargetName}: {before.Count} moves");

            Assert.Equal(before.Count, after.Count);

            for (var i = 0; i < before.Count; i++)
            {
                Assert.Equal(new Point2(before[i].To.X + offset.XNm, before[i].To.Y + offset.YNm), after[i].To);
            }

            Assert.Contains("shifted X+0.120 Y-0.050 mm", moved.Content, StringComparison.Ordinal);
        }

        foreach (var other in plain.Items.Where(i => !ExportPlanner.IsDrillOrRouting(i)))
        {
            Assert.Equal(other.Content, Assert.Single(aligned.Items, i => i.TargetName == other.TargetName).Content);
        }
    }

    /// <summary>
    /// The board outline moves when asked, so it cuts round the copper the holes were lined up with; and
    /// stays exactly as it was when not. The copper programs never move: they are what was measured against.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOutlineMovesOnlyWhenAsked(bool outline)
    {
        var offset = new DrillAlignment(Nm.FromMillimetres(-0.2), Nm.FromMillimetres(0.08), outline);

        var plain = Plan();
        var aligned = Plan(offset);

        var original = Assert.Single(plain.Items, i => i.Operation == OperationKind.Outline);
        var name = Path.GetFileNameWithoutExtension(original.TargetName) + ".aligned.nc";

        Assert.Equal(outline, offset.Moves(original));

        if (!outline)
        {
            Assert.Equal(original.Content, Assert.Single(aligned.Items, i => i.TargetName == original.TargetName).Content);
            Assert.DoesNotContain(aligned.Items, i => i.TargetName == name);
            return;
        }

        var moved = Assert.Single(aligned.Items, i => i.TargetName == name);

        var before = GcodeParser.Parse(original.Content).Moves.Where(m => m.MovesInPlane && m.To != Point2.Origin).ToList();
        var after = GcodeParser.Parse(moved.Content).Moves.Where(m => m.MovesInPlane && m.To != Point2.Origin).ToList();

        output.WriteLine($"{original.TargetName} -> {moved.TargetName}: {before.Count} moves");

        Assert.Equal(before.Count, after.Count);
        Assert.All(before.Zip(after), p => Assert.Equal(new Point2(p.First.To.X + offset.XNm, p.First.To.Y + offset.YNm), p.Second.To));

        foreach (var copper in plain.Items.Where(i => i.Operation == OperationKind.Isolation))
        {
            Assert.False(offset.Moves(copper));
            Assert.Equal(copper.Content, Assert.Single(aligned.Items, i => i.TargetName == copper.TargetName).Content);
        }
    }

    /// <summary>
    /// The stock is cut as an outline but is not the board's outline: it is cut before there is any
    /// copper to line up with, and its corner is work zero. It never moves and is never written aligned.
    /// </summary>
    [Fact]
    public void TheStockNeverMoves()
    {
        var job = new JobOptions { Blank = new BlankOptions { Enabled = true } };
        var offset = new DrillAlignment(Nm.FromMillimetres(0.3), Nm.FromMillimetres(0.3), Outline: true);

        var plain = Plan(job: job);
        var aligned = Plan(offset, job);

        var stock = Assert.Single(plain.Items, i => i.TargetName.EndsWith(".stock.nc", StringComparison.Ordinal));

        Assert.False(offset.Moves(stock));
        Assert.False(ExportPlanner.IsBoardOutline(stock));
        Assert.Equal(stock.Content, Assert.Single(aligned.Items, i => i.TargetName == stock.TargetName).Content);
        Assert.DoesNotContain(aligned.Items, i => i.TargetName.EndsWith(".stock.aligned.nc", StringComparison.Ordinal));

        // The board's own outline, on the same stock, still does.
        Assert.Contains(aligned.Items, i => ExportPlanner.IsBoardOutline(i) && i.TargetName.EndsWith(".aligned.nc", StringComparison.Ordinal));
    }

    /// <summary>
    /// A layer's page is written again too, under the aligned name, and lists the aligned files — the ones
    /// to run — rather than the originals beside them.
    /// </summary>
    [Fact]
    public void TheAlignedPagesListTheAlignedFiles()
    {
        var aligned = Plan(new DrillAlignment(Nm.FromMillimetres(0.12), 0));

        var layers = aligned.Items
            .Where(ExportPlanner.IsDrillOrRouting)
            .GroupBy(i => i.LayerFileName)
            .ToList();

        Assert.Contains(layers, l => l.First().Companion is not null);

        foreach (var layer in layers)
        {
            var files = layer.ToList();

            if (files[0].Companion is not { } page)
            {
                continue;
            }

            output.WriteLine($"{page.TargetName}: {string.Join(", ", files.Select(f => f.TargetName))}");

            Assert.EndsWith(".aligned.html", page.TargetName, StringComparison.Ordinal);
            Assert.All(files, f => Assert.EndsWith(".aligned.nc", f.TargetName, StringComparison.Ordinal));

            if (files.Count > 1)
            {
                Assert.All(files, f => Assert.Contains(f.TargetName, page.Content, StringComparison.Ordinal));
            }
        }
    }

    // ------------------------------------------------------------------ what moves

    /// <summary>
    /// Named programs move and nothing else does — including the copper, which the default rule never
    /// moves and which a flipped board's workflow needs moved.
    /// </summary>
    [Fact]
    public void OnlyTheProgramsNamedAreMoved()
    {
        var plain = Plan();
        var movable = ExportPlanner.Movable(plain);

        Assert.NotEmpty(movable);

        // The copper: exactly what the default rule refuses to move.
        var copper = movable.First(m => m.What.StartsWith("Isolation", StringComparison.Ordinal));
        var alignment = new DrillAlignment(Nm.FromMillimetres(0.2), 0) { Moved = [copper.Key] };

        var aligned = Plan(alignment);
        var moved = aligned.Items.Where(alignment.Moves).ToList();

        output.WriteLine($"moved: {string.Join(", ", moved.Select(m => m.TargetName))}");

        Assert.All(moved, m => Assert.Equal(copper.Key, DrillAlignment.KeyFor(m)));
        Assert.All(moved, m => Assert.EndsWith(".aligned.nc", m.TargetName, StringComparison.Ordinal));

        // And the drilling, which the default rule always moves, is untouched here.
        foreach (var drill in plain.Items.Where(ExportPlanner.IsDrillOrRouting))
        {
            Assert.Equal(
                drill.Content,
                Assert.Single(aligned.Items, i => i.TargetName == drill.TargetName).Content);
        }
    }

    /// <summary>
    /// The stock is never moved, however it is named. It is cut before there is anything on the board
    /// to line up with, and it is the work zero the correction itself is measured from.
    /// </summary>
    [Fact]
    public void TheStockIsNeverMovedEvenWhenNamed()
    {
        var job = new JobOptions { Blank = new BlankOptions { Enabled = true } };
        var plain = Plan(job: job);

        var stock = Assert.Single(plain.Items, i => i.LayerFileName == ExportPlanner.StockLayer);

        var alignment = new DrillAlignment(Nm.FromMillimetres(0.3), 0)
        {
            Moved = [DrillAlignment.KeyFor(stock)],
        };

        Assert.False(alignment.Moves(stock));
        Assert.DoesNotContain(ExportPlanner.Movable(plain), m => m.Key == DrillAlignment.KeyFor(stock));

        var aligned = Plan(alignment, job);

        Assert.Equal(
            stock.Content,
            Assert.Single(aligned.Items, i => i.LayerFileName == ExportPlanner.StockLayer).Content);
    }

    /// <summary>
    /// The listing tells the dialog what it needs to draw a row: what each group is, whose layer it
    /// belongs to, how many files it is, and which side of the board it is written for.
    /// </summary>
    [Fact]
    public void TheMovableListDescribesEveryGcodeProgram()
    {
        var plan = Plan();
        var movable = ExportPlanner.Movable(plan);

        foreach (var program in movable)
        {
            output.WriteLine($"{program.Key}  {program.What} · {program.LayerLabel}  {program.Files} file(s)");
        }

        // Every G-code file except the stock is in exactly one group.
        var files = plan.Items.Where(i => i.Output == OutputKind.Gcode && i.LayerFileName != ExportPlanner.StockLayer).ToList();

        Assert.Equal(files.Count, movable.Sum(m => m.Files));
        Assert.Equal(movable.Select(m => m.Key).Distinct().Count(), movable.Count);
        Assert.Contains(movable, m => m.What == "Drilling" && m.MovedByDefault);
        Assert.Contains(movable, m => m.What == "Isolation routing" && !m.MovedByDefault);
        Assert.All(movable, m => Assert.False(string.IsNullOrWhiteSpace(m.LayerLabel)));
    }

    /// <summary>
    /// Routed slots are written as an outline operation on a drill layer, which is not the program
    /// that cuts the board out — and the two must not be confused, because one moves by default and
    /// the other only when asked.
    /// </summary>
    [Fact]
    public void SlotsAreNotTheBoardOutline()
    {
        var plan = Plan(job: new JobOptions { MillLargeHoles = true });
        var slots = plan.Items.Where(i => i.TargetName.Contains(".slots.", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.False(ExportPlanner.IsBoardOutline(s)));
        Assert.All(slots, s => Assert.True(ExportPlanner.IsDrillOrRouting(s)));
    }

    // ------------------------------------------------------------------ a board that is also turned

    /// <summary>
    /// A correction with a turn in it moves every hole to where the turn puts it — not just the one it
    /// was measured at.
    ///
    /// This is the whole point of measuring twice. A shift alone lands the hole it was measured at and
    /// misses the far end of the board by the length of the arc, which on a 70 mm board at half a degree
    /// is most of a pad.
    /// </summary>
    [Fact]
    public void ATurnMovesEveryHoleAlongWithIt()
    {
        var turned = new DrillAlignment(Nm.FromMillimetres(0.12), Nm.FromMillimetres(-0.05))
        {
            RotationDegrees = 0.6,
            PivotNm = new Point2(Nm.FromMillimetres(12), Nm.FromMillimetres(9)),
        };

        var plain = Plan();
        var aligned = Plan(turned);

        var drill = plain.Items.Where(ExportPlanner.IsDrillOrRouting).ToList();

        Assert.NotEmpty(drill);

        var checkedHoles = 0;

        foreach (var original in drill)
        {
            var name = Path.GetFileNameWithoutExtension(original.TargetName) + ".aligned.nc";
            var moved = Assert.Single(aligned.Items, i => i.TargetName == name);

            var before = AlignmentTest.Targets(original.Content);
            var after = AlignmentTest.Targets(moved.Content);

            output.WriteLine($"{original.TargetName}: {before.Count} holes turned");

            Assert.NotEmpty(before);
            Assert.Equal(before.Count, after.Count);

            for (var i = 0; i < before.Count; i++)
            {
                // A micron of slack: the coordinates are written to the machine's decimals, and a
                // milled hole's centre is read back from the arcs that cut it.
                Assert.True(
                    turned.Apply(before[i].At).DistanceTo(after[i].At) <= Nm.FromMillimetres(0.001),
                    $"{original.TargetName} hole {before[i].Number}: {turned.Apply(before[i].At)} vs {after[i].At}");

                checkedHoles++;
            }

            // The note says both halves of what was done, and where the turn was taken about.
            Assert.Contains("turned 0.6 degrees about X12.000 Y9.000 mm", moved.Content, StringComparison.Ordinal);
            Assert.Contains("then shifted X+0.120 Y-0.050 mm", moved.Content, StringComparison.Ordinal);
        }

        output.WriteLine($"{checkedHoles} holes checked");
    }

    /// <summary>
    /// A turned arc is the same arc, turned: same radius, same sweep.
    ///
    /// The risk is the centre. An arc is written as a point and an offset to its centre, and a rotation
    /// applied to the endpoints but not the centre leaves a command the machine will not accept — or
    /// worse, one it will, cutting an arc of the wrong radius through the board.
    /// </summary>
    [Fact]
    public void ATurnKeepsEveryArcTheSameSize()
    {
        var turned = new DrillAlignment(Nm.FromMillimetres(0.2), Nm.FromMillimetres(0.1))
        {
            RotationDegrees = -1.25,
            PivotNm = new Point2(Nm.FromMillimetres(20), Nm.FromMillimetres(15)),
        };

        var job = new JobOptions { MillLargeHoles = true };
        var plain = Plan(job: job);
        var aligned = Plan(turned, job);

        var arcs = 0;

        foreach (var original in plain.Items.Where(ExportPlanner.IsDrillOrRouting))
        {
            var name = Path.GetFileNameWithoutExtension(original.TargetName) + ".aligned.nc";
            var moved = Assert.Single(aligned.Items, i => i.TargetName == name);

            var before = GcodeParser.Parse(original.Content).Moves.Where(m => m.IsArc).ToList();
            var after = GcodeParser.Parse(moved.Content).Moves.Where(m => m.IsArc).ToList();

            Assert.Equal(before.Count, after.Count);

            for (var i = 0; i < before.Count; i++)
            {
                var was = before[i].From.DistanceTo(before[i].Centre);
                var now = after[i].From.DistanceTo(after[i].Centre);

                Assert.True(
                    Math.Abs(was - now) <= Nm.FromMillimetres(0.001),
                    $"{original.TargetName} arc {i}: radius {was} became {now}");

                Assert.Equal(before[i].Kind, after[i].Kind);
                arcs++;
            }
        }

        output.WriteLine($"{arcs} arcs checked");
        Assert.True(arcs > 0, "this board should have milled holes to check");
    }

    /// <summary>
    /// The copper is what the measurement was made against, so it never moves — turn or no turn. The
    /// same rule as the shift, and worth its own test: a rotation is applied in a different place.
    /// </summary>
    [Fact]
    public void ATurnLeavesTheCopperWhereItIs()
    {
        var turned = new DrillAlignment(Nm.FromMillimetres(0.12), 0)
        {
            RotationDegrees = 0.9,
            PivotNm = new Point2(Nm.FromMillimetres(10), Nm.FromMillimetres(10)),
        };

        var plain = Plan();
        var aligned = Plan(turned);

        foreach (var other in plain.Items.Where(i => !ExportPlanner.IsDrillOrRouting(i)))
        {
            Assert.Equal(other.Content, Assert.Single(aligned.Items, i => i.TargetName == other.TargetName).Content);
        }
    }
}
