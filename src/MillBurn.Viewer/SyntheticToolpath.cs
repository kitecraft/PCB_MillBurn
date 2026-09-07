namespace MillBurn.Viewer;

/// <summary>
/// Generates a PCB-shaped toolpath of a requested size, purely to exercise the viewport in the
/// Phase 0 spike. Deterministic, so frame-rate measurements are comparable between runs.
///
/// Delete this once MillBurn.Cam produces real toolpaths.
/// </summary>
public static class SyntheticToolpath
{
    public static List<Polyline> Generate(int targetSegments, int seed = 20260907)
    {
        var rng = new Random(seed);
        var result = new List<Polyline>();
        var produced = 0;

        const float boardW = 160f;
        const float boardH = 100f;

        // Board outline: closed loop, drawn last so it reads on top.
        result.Add(new Polyline(SegmentStyle.Outline,
            [0, 0, boardW, 0, boardW, boardH, 0, boardH, 0, 0]));
        produced += 4;

        // Fiducials: three crosses in the waste frame.
        foreach (var (fx, fy) in new[] { (-8f, -8f), (boardW + 8f, -8f), (-8f, boardH + 8f) })
        {
            result.Add(new Polyline(SegmentStyle.Fiducial, [fx - 2, fy, fx + 2, fy]));
            result.Add(new Polyline(SegmentStyle.Fiducial, [fx, fy - 2, fx, fy + 2]));
            produced += 2;
        }

        // Isolation traces: many short segments, which is what makes a real file large.
        var lastEndX = 0f;
        var lastEndY = 0f;

        while (produced < targetSegments)
        {
            var startX = (float)(rng.NextDouble() * boardW);
            var startY = (float)(rng.NextDouble() * boardH);
            var pointCount = rng.Next(20, 240);

            var pts = new float[pointCount * 2];
            var x = startX;
            var y = startY;
            var heading = (float)(rng.NextDouble() * Math.Tau);

            for (var i = 0; i < pointCount; i++)
            {
                pts[i * 2] = x;
                pts[(i * 2) + 1] = y;

                // Small heading jitter produces gently curving traces rather than noise,
                // which is both more realistic and a fairer test of the simplifier.
                heading += (float)((rng.NextDouble() - 0.5) * 0.45);
                x = Math.Clamp(x + (MathF.Cos(heading) * 0.18f), 0, boardW);
                y = Math.Clamp(y + (MathF.Sin(heading) * 0.18f), 0, boardH);
            }

            // The travel move that got us here — the thing the optimizer exists to shrink.
            var dx = startX - lastEndX;
            var dy = startY - lastEndY;
            var travelStyle = MathF.Sqrt((dx * dx) + (dy * dy)) > 40f
                ? SegmentStyle.RapidLong
                : SegmentStyle.Travel;
            result.Add(new Polyline(travelStyle, [lastEndX, lastEndY, startX, startY]));
            produced++;

            result.Add(new Polyline(SegmentStyle.Isolation, pts));
            produced += pointCount - 1;

            lastEndX = x;
            lastEndY = y;
        }

        // Drill hits: short plunge crosses on a grid.
        for (var i = 0; i < 200; i++)
        {
            var dxp = 6f + ((i % 20) * 7.4f);
            var dyp = 6f + ((i / 20) * 9.1f);
            result.Add(new Polyline(SegmentStyle.Drill,
                [dxp - 0.6f, dyp, dxp + 0.6f, dyp]));
        }

        return result;
    }
}
