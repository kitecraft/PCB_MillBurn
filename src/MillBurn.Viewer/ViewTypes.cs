namespace MillBurn.Viewer;

/// <summary>Pan/zoom state. Screen = (world * Scale) + Offset, with Y flipped.</summary>
public readonly record struct ViewTransform(float Scale, float OffsetX, float OffsetY);

/// <summary>What one rendered frame cost, surfaced in the viewport overlay.</summary>
public readonly record struct FrameStats(
    double MillisecondsElapsed,
    int Tier,
    int SegmentsInView,
    int DrawCalls);
