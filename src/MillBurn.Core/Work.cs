namespace MillBurn.Core;

/// <summary>A tally of the expensive things one piece of work did.</summary>
/// <param name="Booleans">Clipper boolean operations — union, difference, intersect.</param>
/// <param name="PointTests">Point-in-polygon questions.</param>
/// <param name="Vertices">
/// Vertices handed to those operations, summed. The count of calls says how often; this says how
/// big, and the two together are what a change in cost actually looks like.
/// </param>
public readonly record struct WorkCount(long Booleans, long PointTests, long Vertices)
{
    public static WorkCount operator -(WorkCount a, WorkCount b) => new(
        a.Booleans - b.Booleans,
        a.PointTests - b.PointTests,
        a.Vertices - b.Vertices);

    public override string ToString() =>
        $"{Booleans} boolean(s), {PointTests} point test(s), {Vertices} vertex/vertices";
}

/// <summary>
/// How much geometry work the application has done, counted rather than timed.
///
/// **Why counted.** A wall clock is not a fact about the program: it is a fact about the machine it
/// ran on that afternoon. A test that asserts against one either flaps or gets its threshold raised
/// until it guards nothing, which is the failure
/// <c>OptimizerBenchmarkTests</c> already names — "a gate that pins the number fails on every
/// legitimate improvement, which teaches whoever sees it to update the number without looking".
/// The optimizer answered that for itself years ago by budgeting in moves examined rather than in
/// milliseconds. This is the same answer for everything else.
///
/// **What it is for.** The same input does the same work on every machine, so the count can be
/// written down and compared — a change in it is a diff somebody has to explain in a commit
/// message, exactly as a change to an emitted program is. That catches the slow creep that no
/// threshold catches: a hundred small regressions, none of them large enough to trip a limit.
///
/// It is not a profiler and does not try to be. It says how many expensive operations were asked
/// for and how much geometry went into them, which is the part that scales with the board. Where
/// the time actually goes is a question for a profiler, on the day somebody has one open.
///
/// **Offsets are not counted, and their absence is deliberate rather than forgotten.** Every
/// toolpath is built by one, but the fourteen call sites go straight to Clipper rather than through
/// <c>Polygons</c>, so there is nowhere to count them from. A field that could only ever read zero
/// would be worse than none — somebody would trust it. Routing them through a wrapper is a
/// worthwhile follow-up and a change to fourteen files, which is its own piece of work.
///
/// **The cost of counting.** Four interlocked increments per Clipper call, on operations that take
/// microseconds to milliseconds each. Measured at the noise floor, and worth it: the first version
/// of the net-point carrier cost 77 % on top of realising a board and nothing would have said so.
///
/// **Threads.** The pipeline runs on the thread pool and two previews can overlap, so the counters
/// are interlocked and global. That makes them a total, not a per-run figure — a caller that wants
/// one run's cost takes <see cref="Taken"/> either side and subtracts, which is what
/// <see cref="Since"/> does. Overlapping runs therefore pollute each other's readings, which is
/// fine for a benchmark that runs alone and is why this is not offered as a per-export statistic.
/// </summary>
public static class Work
{
    private static long _booleans;
    private static long _pointTests;
    private static long _vertices;

    /// <summary>The tally so far, across every thread.</summary>
    public static WorkCount Taken => new(
        Interlocked.Read(ref _booleans),
        Interlocked.Read(ref _pointTests),
        Interlocked.Read(ref _vertices));

    /// <summary>What has been done since a reading was taken.</summary>
    public static WorkCount Since(WorkCount mark) => Taken - mark;

    /// <summary>One Clipper boolean over this many vertices.</summary>
    public static void Boolean(int vertices)
    {
        Interlocked.Increment(ref _booleans);
        Interlocked.Add(ref _vertices, vertices);
    }

    /// <summary>One point-in-polygon question. Counted without its size: a ring is walked until it answers.</summary>
    public static void PointTest() => Interlocked.Increment(ref _pointTests);
}
