namespace MillBurn.Core;

/// <summary>A tally of the expensive things one piece of work did.</summary>
/// <param name="Booleans">Clipper boolean operations — union, difference, intersect.</param>
/// <param name="Offsets">
/// Clipper offsets — every toolpath is built by one, and until 6.39 none of them were counted.
/// Kept apart from <paramref name="Booleans"/> rather than added to it because the two answer
/// different questions: compositing a board and building a toolpath from it are separate stages
/// that regress separately, and a single total would let one hide inside the other.
/// </param>
/// <param name="Searches">
/// Local searches the route optimizer ran — one per group of nodes it orders.
/// </param>
/// <param name="SearchSteps">
/// Steps those searches took — the unit the optimizer's budget is denominated in, which is one
/// dequeue from its work queue. `RoutePlan.Improvements` counts the moves a search *kept*; this
/// counts what it spent getting there, and the gap between them is where a search gets dearer
/// without getting better.
///
/// **It is a proxy, and worth knowing the shape of.** A step includes iterations the don't-look
/// bits discard without examining anything, and excludes the individual candidates weighed inside
/// a single step by the flip, two-opt and or-opt trials — so work that made each step consider
/// twice as many candidates would not move this number at all. What it measures exactly is budget
/// spend, which is the thing that was being thrown away.
/// </param>
/// <param name="PointTests">Point-in-polygon questions.</param>
/// <param name="Vertices">
/// Vertices handed to those operations, summed. The count of calls says how often; this says how
/// big, and the two together are what a change in cost actually looks like.
/// </param>
public readonly record struct WorkCount(
    long Booleans, long Offsets, long Searches, long SearchSteps, long PointTests, long Vertices)
{
    public static WorkCount operator -(WorkCount a, WorkCount b) => new(
        a.Booleans - b.Booleans,
        a.Offsets - b.Offsets,
        a.Searches - b.Searches,
        a.SearchSteps - b.SearchSteps,
        a.PointTests - b.PointTests,
        a.Vertices - b.Vertices);

    public override string ToString() =>
        $"{Booleans} boolean(s), {Offsets} offset(s), {Searches} search(es) over {SearchSteps} "
        + $"step(s), {PointTests} point test(s), {Vertices} vertex/vertices";
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
/// **Offsets are counted now**, which they were not until 6.39. Every toolpath is built by one, and
/// the fifteen call sites went straight to Clipper rather than through <c>Polygons</c>, so there
/// was nowhere to count them from; a field that could only ever read zero was left out rather than
/// shipped, because somebody would have trusted it. They go through <c>Polygons.Inflate</c>, and
/// an analyser makes calling Clipper's own offsetting or boolean entry points from anywhere else a
/// build error — so the tally is a fact about the program rather than about who remembered.
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
    private static long _offsets;
    private static long _searches;
    private static long _searchSteps;
    private static long _pointTests;
    private static long _vertices;

    /// <summary>The tally so far, across every thread.</summary>
    public static WorkCount Taken => new(
        Interlocked.Read(ref _booleans),
        Interlocked.Read(ref _offsets),
        Interlocked.Read(ref _searches),
        Interlocked.Read(ref _searchSteps),
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

    /// <summary>
    /// One Clipper offset over this many vertices.
    ///
    /// The vertices go into the same total the booleans use, because that total is "geometry handed
    /// to an expensive operation" and an offset is one. What the separate count adds is *which*
    /// operation: before 6.39 a change in offsetting moved the vertex line and nothing said why.
    /// </summary>
    public static void Offset(int vertices)
    {
        Interlocked.Increment(ref _offsets);
        Interlocked.Add(ref _vertices, vertices);
    }

    /// <summary>
    /// One local search, and the steps it spent.
    ///
    /// **Added once per search rather than once per move**, and that is the whole reason this is
    /// affordable. The inner loop runs hundreds of thousands of times on a panel; an interlocked
    /// increment inside it would be a cost worth measuring rather than a measurement. The solver
    /// already counts its own steps against the budget, so the number is free — it was simply
    /// being thrown away when the search returned.
    /// </summary>
    public static void Search(long steps)
    {
        Interlocked.Increment(ref _searches);
        Interlocked.Add(ref _searchSteps, steps);
    }

    /// <summary>One point-in-polygon question. Counted without its size: a ring is walked until it answers.</summary>
    public static void PointTest() => Interlocked.Increment(ref _pointTests);
}
