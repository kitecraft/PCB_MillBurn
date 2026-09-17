using Clipper2Lib;
using MillBurn.Cam;
using MillBurn.Core;
using MillBurn.Gcode;
using MillBurn.Geometry;
using MillBurn.Optimize;
using MillBurn.Pipeline;
using Xunit;

namespace MillBurn.Tests;

/// <summary>
/// Milling: what the tool actually cuts, where the paths go, and what comes out of the emitter.
/// </summary>
public sealed class MillingTests
{
    private static long Mm(double mm) => Nm.FromMillimetres(mm);

    private static Paths64 Square(double cxMm, double cyMm, double sizeMm)
    {
        var h = Mm(sizeMm) / 2;
        var cx = Mm(cxMm);
        var cy = Mm(cyMm);
        return
        [
            [
                new Point64(cx - h, cy - h),
                new Point64(cx + h, cy - h),
                new Point64(cx + h, cy + h),
                new Point64(cx - h, cy + h),
            ],
        ];
    }

    // ------------------------------------------------------------------ the V-bit model

    /// <summary>
    /// <c>width = tip + 2 * depth * tan(included / 2)</c>. A 30-degree bit with a 0.1 mm tip at
    /// 0.05 mm deep cuts 0.1268 mm.
    ///
    /// The included angle is the full angle across the V, not the half angle. Reading it the other
    /// way puts the cut width out by roughly a factor of two, and both conventions are in
    /// circulation, so it is worth a test rather than a comment.
    /// </summary>
    [Theory]
    [InlineData(30, 0.1, 0.00, 0.1)]
    [InlineData(30, 0.1, 0.05, 0.126795)]
    [InlineData(30, 0.1, 0.10, 0.153590)]
    [InlineData(60, 0.1, 0.05, 0.157735)]
    [InlineData(90, 0.1, 0.05, 0.2)]
    public void VBitWidthFollowsTheConeAngle(double angle, double tipMm, double depthMm, double expectedMm)
    {
        var tool = Tool.DefaultVBit with { IncludedAngleDegrees = angle, TipNm = Mm(tipMm) };

        Assert.Equal(expectedMm, Nm.ToMillimetres(tool.WidthAtDepth(Mm(depthMm))), 5);
    }

    [Fact]
    public void AStraightToolCutsItsDiameterAtAnyDepth()
    {
        var mill = Tool.DefaultOutlineMill;

        Assert.Equal(mill.DiameterNm, mill.WidthAtDepth(Mm(0.1)));
        Assert.Equal(mill.DiameterNm, mill.WidthAtDepth(Mm(2.0)));
        Assert.Equal(0, mill.WidthPerDepth);
    }

    [Fact]
    public void DepthForWidthInvertsWidthAtDepth()
    {
        var tool = Tool.DefaultVBit;
        var depth = tool.DepthForWidth(Mm(0.2));

        Assert.Equal(0.2, Nm.ToMillimetres(tool.WidthAtDepth(depth)), 4);
    }

    /// <summary>
    /// An end mill has one width and no depth changes it, so asking for a narrower cut is a request
    /// that has to be refused rather than quietly rounded to something the machine will then cut
    /// too wide.
    /// </summary>
    [Fact]
    public void AStraightToolRefusesAWidthItCannotCut()
    {
        Assert.Equal(-1, Tool.DefaultOutlineMill.DepthForWidth(Mm(0.2)));
        Assert.Equal(0, Tool.DefaultOutlineMill.DepthForWidth(Mm(1.5)));
    }

    /// <summary>
    /// A shallower V is *more* sensitive to depth error, not less, which is the opposite of most
    /// people's intuition and the reason the number is surfaced in the UI.
    /// </summary>
    [Fact]
    public void AShallowerVIsLessSensitiveToDepthError()
    {
        var narrow = Tool.DefaultVBit with { IncludedAngleDegrees = 30 };
        var wide = Tool.DefaultVBit with { IncludedAngleDegrees = 90 };

        Assert.True(wide.WidthPerDepth > narrow.WidthPerDepth);
        Assert.Equal(0.536, narrow.WidthPerDepth, 3);
    }

    // ------------------------------------------------------------------ isolation

    /// <summary>
    /// The cut must clear the copper without leaving bare board between the two, so the centreline
    /// of the first pass sits exactly half a cut-width outside the copper boundary.
    /// </summary>
    [Fact]
    public void TheFirstPassIsHalfACutWidthClearOfTheCopper()
    {
        var options = new IsolationOptions { DepthNm = Mm(0.05) };
        var path = IsolationOperation.Build(Square(0, 0, 10), options);

        var pass = Assert.Single(path.Passes);
        var bounds = ArtGeometry.Measure(pass.Path);

        // A 10 mm square grown by half of 0.1268 mm on each side.
        var expected = 10 + Nm.ToMillimetres(options.EffectiveWidthNm);
        Assert.Equal(expected, Nm.ToMillimetres(bounds.Width), 3);
    }

    [Fact]
    public void EachExtraPassStepsFurtherOut()
    {
        var options = new IsolationOptions { Passes = 3 };
        var path = IsolationOperation.Build(Square(0, 0, 10), options);

        Assert.Equal(3, path.PassCount);

        var widths = path.Passes.Select(p => ArtGeometry.Measure(p.Path).Width).ToList();
        Assert.True(widths[1] > widths[0], "pass 2 should be outside pass 1");
        Assert.True(widths[2] > widths[1], "pass 3 should be outside pass 2");
    }

    [Fact]
    public void DeeperCutsWiderAndSoStepsFurtherOut()
    {
        var shallow = IsolationOperation.Build(Square(0, 0, 10), new IsolationOptions { DepthNm = Mm(0.02) });
        var deep = IsolationOperation.Build(Square(0, 0, 10), new IsolationOptions { DepthNm = Mm(0.20) });

        Assert.True(
            ArtGeometry.Measure(deep.Passes[0].Path).Width > ArtGeometry.Measure(shallow.Passes[0].Path).Width);
    }

    /// <summary>
    /// **The check that matters.** Two islands closer together than the tool is wide have no path
    /// between them, so after this job they are still connected — and the toolpath shows nothing at
    /// all in the gap, so the picture looks perfectly fine while the board is shorted.
    /// </summary>
    [Fact]
    public void GapsTooNarrowForTheToolAreCounted()
    {
        // Two 2 mm squares, 0.08 mm apart: narrower than the 0.127 mm the default tool cuts.
        var tight = new Paths64();
        tight.AddRange(Square(0, 0, 2));
        tight.AddRange(Square(2.08, 0, 2));

        var roomy = new Paths64();
        roomy.AddRange(Square(0, 0, 2));
        roomy.AddRange(Square(2.5, 0, 2));

        var options = new IsolationOptions();

        Assert.Equal(1, IsolationOperation.UnreachableGaps(tight, options));
        Assert.Equal(0, IsolationOperation.UnreachableGaps(roomy, options));
    }

    // ------------------------------------------------------------------ outline and drilling

    /// <summary>
    /// Cutting outside the profile is what makes the finished board its nominal size. Cutting on
    /// the line would take a cutter-radius off every edge — 0.5 mm on a 1 mm mill, which nobody
    /// notices until the enclosure does not fit.
    /// </summary>
    [Fact]
    public void TheOutlineIsCutOutsideTheProfile()
    {
        var options = new OutlineOptions { TabCount = 0 };
        var path = OutlineOperation.Build(Square(0, 0, 20), options);

        var bounds = ArtGeometry.Measure(path.Passes[0].Path);
        Assert.Equal(20 + Nm.ToMillimetres(options.Tool.DiameterNm), Nm.ToMillimetres(bounds.Width), 3);
    }

    [Fact]
    public void TheOutlineStepsDownInDepth()
    {
        var options = new OutlineOptions { TabCount = 0, DepthPerPassNm = Mm(0.4) };
        var path = OutlineOperation.Build(Square(0, 0, 20), options);

        var depths = path.Passes.Select(p => p.DepthNm).Distinct().Order().ToList();

        Assert.Equal(5, depths.Count);
        Assert.Equal(options.TotalDepthNm, depths[^1]);
        Assert.True(depths.All(d => d <= options.TotalDepthNm));
    }

    /// <summary>
    /// Without tabs the board comes free somewhere in the last pass, lifts on the cutter and is
    /// thrown. Tabs turn the final closed contour into open runs with gaps.
    /// </summary>
    [Fact]
    public void TabsBreakTheLastPassIntoOpenRuns()
    {
        var withTabs = OutlineOperation.Build(Square(0, 0, 20), new OutlineOptions { TabCount = 4 });
        var without = OutlineOperation.Build(Square(0, 0, 20), new OutlineOptions { TabCount = 0 });

        Assert.All(without.Passes, p => Assert.True(p.Closed));

        var deepest = withTabs.Passes.Max(p => p.DepthNm);
        var last = withTabs.Passes.Where(p => p.DepthNm == deepest).ToList();

        Assert.Equal(4, last.Count);
        Assert.All(last, p => Assert.False(p.Closed));
    }

    /// <summary>Biggest bit first: every size change is a manual tool change.</summary>
    [Fact]
    public void DrillsAreGroupedByToolLargestFirst()
    {
        var drill = MillBurn.Gerber.Excellon.ExcellonParser.Parse(
            """
            M48
            METRIC
            T1C0.8
            T2C3.2
            %
            T1
            X1.0Y1.0
            X2.0Y1.0
            T2
            X3.0Y1.0
            M30
            """);

        var paths = DrillOperation.Build(drill, new DrillOptions());

        Assert.Equal(2, paths.Count);
        Assert.Contains("3.20", paths[0].Label, StringComparison.Ordinal);
        Assert.Single(paths[0].Drills);
        Assert.Equal(2, paths[1].Drills.Count);
    }

    // ------------------------------------------------------------------ ordering

    /// <summary>
    /// The baseline already looks at *both* ends of a candidate, and reverses the path when the far
    /// end is nearer. pcb2gcode's greedy pass measures only the front endpoint, so a path lying
    /// right next to the tool but pointing away is scored by how far its other end is — a bug
    /// rather than a simplification (Documentation/03, section 2).
    /// </summary>
    [Fact]
    public void OrderingEntersAPathFromWhicheverEndIsNearer()
    {
        // A run whose end is next to the origin and whose start is far away.
        var pass = new ToolpathPass
        {
            Path = [ArtSegment.Line(new Point2(Mm(100), 0), new Point2(Mm(1), 0))],
            DepthNm = Mm(0.05),
        };

        var ordered = NearestNeighbour.Order([pass], Point2.Origin);

        Assert.Equal(Mm(1), ordered[0].Start.X);
        Assert.Equal(Mm(100), ordered[0].End.X);
    }

    [Fact]
    public void OrderingCutsTravelAgainstTheOrderTheyArrivedIn()
    {
        var passes = new List<ToolpathPass>();
        foreach (var x in new[] { 50, 1, 40, 2, 30, 3 })
        {
            passes.Add(new ToolpathPass
            {
                Path = [ArtSegment.Line(new Point2(Mm(x), 0), new Point2(Mm(x) + Mm(0.5), 0))],
                DepthNm = Mm(0.05),
            });
        }

        var before = NearestNeighbour.TravelMm(passes, Point2.Origin);
        var after = NearestNeighbour.TravelMm(NearestNeighbour.Order(passes, Point2.Origin), Point2.Origin);

        Assert.True(after < before, $"expected ordering to help; {after:F1} mm vs {before:F1} mm");
    }

    // ------------------------------------------------------------------ the emitter

    private static Job OneSquare() => new()
    {
        Name = "test",
        Toolpaths =
        [
            new Toolpath
            {
                Kind = ToolpathKind.Isolation,
                Label = "Isolation",
                Tool = Tool.DefaultVBit,
                Passes =
                [
                    new ToolpathPass
                    {
                        Path = IsolationOperation.Build(Square(5, 5, 4), new IsolationOptions()).Passes[0].Path,
                        DepthNm = Mm(0.05),
                        Closed = true,
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// The tool lifts once between features, not twice.
    ///
    /// A pass that ends by retracting used to be followed by a pass that began by retracting, so every
    /// hole and slot in a routing file carried a second <c>G0 Z</c> to the height it was already at. It
    /// cost nothing to run and made the file harder to read, which is most of what these files are for.
    /// </summary>
    [Fact]
    public void TheToolNeverLiftsTwiceInARow()
    {
        var job = new Job
        {
            Name = "two squares",
            Toolpaths =
            [
                OneSquare().Toolpaths[0] with
                {
                    Passes =
                    [
                        .. IsolationOperation.Build(Square(0, 0, 4), new IsolationOptions()).Passes,
                        .. IsolationOperation.Build(Square(30, 30, 4), new IsolationOptions()).Passes,
                    ],
                },
            ],
        };

        var options = new GcodeOptions();
        var (text, _) = GcodeEmitter.Emit(job, options);
        var safeZ = $"G0 Z{Nm.ToMillimetreString(options.SafeZNm, options.Decimals)}";

        var lines = text.Split('\n').Select(l => l.Split('(')[0].Trim()).Where(l => l.Length > 0).ToList();

        for (var i = 1; i < lines.Count; i++)
        {
            Assert.False(
                lines[i] == safeZ && lines[i - 1] == safeZ,
                $"lifted twice in a row at line {i + 1}:\n  "
                    + string.Join("\n  ", lines.Skip(Math.Max(0, i - 3)).Take(7)));
        }

        // And it does still lift: the squares are 30 mm apart, so the tool cannot cross at depth.
        Assert.Contains(safeZ, lines);
    }

    [Fact]
    public void TheProgramSetsMillimetresAndAbsoluteMode()
    {
        var (text, _) = GcodeEmitter.Emit(OneSquare(), new GcodeOptions());

        Assert.Contains("G21 G90 G94", text, StringComparison.Ordinal);
        Assert.EndsWith("M30\n", text, StringComparison.Ordinal);
        Assert.Contains("M5", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// **The invariant that matters.** The tool is never at cutting depth while moving somewhere it
    /// was not cutting. Every move between passes goes up to safe Z first — the difference between
    /// a travel move and a gouge straight across the board.
    /// </summary>
    [Fact]
    public void TheToolIsNeverAtDepthDuringARapid()
    {
        var job = OneSquare() with
        {
            Toolpaths =
            [
                OneSquare().Toolpaths[0] with
                {
                    Passes =
                    [
                        .. IsolationOperation.Build(Square(0, 0, 4), new IsolationOptions()).Passes,
                        .. IsolationOperation.Build(Square(30, 30, 4), new IsolationOptions()).Passes,
                    ],
                },
            ],
        };

        var (text, _) = GcodeEmitter.Emit(job, new GcodeOptions());

        var z = 0.0;
        var line = 0;
        foreach (var raw in text.Split('\n'))
        {
            line++;
            var code = raw.Split('(')[0].Trim();
            if (code.Length == 0)
            {
                continue;
            }

            var zMatch = System.Text.RegularExpressions.Regex.Match(code, @"Z(-?[\d.]+)");
            var moved = System.Text.RegularExpressions.Regex.IsMatch(code, @"[XY]-?[\d.]+");

            if (zMatch.Success)
            {
                z = double.Parse(zMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (code.StartsWith("G0", StringComparison.Ordinal) && moved)
            {
                Assert.True(z >= 0, $"line {line}: rapid across the board at Z{z}");
            }
        }
    }

    [Fact]
    public void ArcsAreEmittedAsArcsRatherThanFlattened()
    {
        var job = OneSquare() with
        {
            Toolpaths =
            [
                OneSquare().Toolpaths[0] with
                {
                    Passes =
                    [
                        new ToolpathPass
                        {
                            Path =
                            [
                                new ArtSegment(
                                    ArtSweep.CounterClockwise,
                                    new Point2(Mm(10), 0),
                                    new Point2(0, Mm(10)),
                                    Point2.Origin),
                            ],
                            DepthNm = Mm(0.05),
                        },
                    ],
                },
            ],
        };

        var (text, _) = GcodeEmitter.Emit(job, new GcodeOptions());

        // Counter-clockwise, so G3; I and J are the centre offset *from the arc start*, which is
        // the convention every controller uses and the one that is easy to get backwards.
        Assert.Contains("G3 X0.000 Y10.000 I-10.000 J0.000", text, StringComparison.Ordinal);
        Assert.DoesNotContain("G2 ", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A machine in a de-DE locale must not be sent "X1,5". The build sets InvariantGlobalization,
    /// but the emitter formats explicitly rather than relying on that staying switched on.
    /// </summary>
    [Fact]
    public void CoordinatesAreInvariantOfTheAmbientCulture()
    {
        var comma = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";

        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = comma;
            var (text, _) = GcodeEmitter.Emit(OneSquare(), new GcodeOptions());

            Assert.DoesNotContain(",", text.Replace("( ", "", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.Contains("Z2.000", text, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// GRBL has no canned cycles and ignores what it cannot parse, so a G81 drill file would travel
    /// the pattern without ever going down — a board with no holes and no error. Pecking is
    /// emulated with plain moves by default.
    /// </summary>
    [Fact]
    public void DrillingPecksWithPlainMovesByDefault()
    {
        var job = new Job
        {
            Name = "drill",
            Toolpaths =
            [
                new Toolpath
                {
                    Kind = ToolpathKind.Drill,
                    Label = "Drill",
                    Tool = Tool.DrillOf(Mm(1.0)),
                    Drills = [new DrillTarget(new Point2(Mm(5), Mm(5)), Mm(1.9), Mm(0.8))],
                },
            ],
        };

        var (text, stats) = GcodeEmitter.Emit(job, new GcodeOptions());

        Assert.DoesNotContain("G81", text, StringComparison.Ordinal);
        Assert.Contains("G1 Z-0.800", text, StringComparison.Ordinal);
        Assert.Contains("G1 Z-1.600", text, StringComparison.Ordinal);
        Assert.Contains("G1 Z-1.900", text, StringComparison.Ordinal);
        Assert.Equal(1, stats.DrillCount);
    }

    // ------------------------------------------------------------------ end to end

    /// <summary>
    /// Gerber coordinates come from wherever the board sat on the EDA canvas — this one lands at
    /// X150 Y-90 — so a job emitted raw would need work zero set at a point the operator cannot
    /// see or measure.
    /// </summary>
    [Fact]
    public void ARealBoardIsReferencedToItsOwnCorner()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var job = JobBuilder.Build(board, new MillOptions());

        var bounds = Core.Bounds.Empty;
        foreach (var toolpath in job.Toolpaths)
        {
            bounds = bounds.Union(toolpath.Bounds);
        }

        // The outline is cut half a cutter outside the board, so the extents start just below zero.
        Assert.InRange(Nm.ToMillimetres(bounds.MinX), -0.6, -0.4);
        Assert.InRange(Nm.ToMillimetres(bounds.MinY), -0.6, -0.4);
        Assert.Contains(job.Notes, n => n.Contains("lower-left corner", StringComparison.Ordinal));
    }

    /// <summary>
    /// Isolate, then drill, then cut out. Isolation needs the board flat and supported; a drill
    /// pushes down and a loose board lifts; after the outline nothing holds the work at all.
    /// </summary>
    [Fact]
    public void OperationsRunInAnOrderThatKeepsTheBoardHeldDown()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var kinds = JobBuilder.Build(board, new MillOptions()).Toolpaths.Select(t => t.Kind).ToList();

        Assert.Equal(ToolpathKind.Isolation, kinds[0]);
        Assert.Equal(ToolpathKind.Outline, kinds[^1]);
        Assert.All(kinds[1..^1], k => Assert.Equal(ToolpathKind.Drill, k));
    }

    [Fact]
    public void ARealBoardProducesRunnableGcode()
    {
        var board = BoardLoader.LoadFolder(RealBoards.Directory(RealBoards.PogoTest1));
        var job = JobBuilder.Build(board, new MillOptions());
        var (text, stats) = GcodeEmitter.Emit(job, new GcodeOptions());

        Assert.True(stats.Lines > 1000, $"expected a real program; got {stats.Lines} lines");
        Assert.Equal(18, stats.DrillCount);
        Assert.Equal(4, stats.ToolChanges);
        Assert.True(stats.CutLengthMm > 500);

        // Every tool change stops the spindle and waits for the operator.
        Assert.Equal(stats.ToolChanges, text.Split("\nM0\n").Length - 1);
    }
}
