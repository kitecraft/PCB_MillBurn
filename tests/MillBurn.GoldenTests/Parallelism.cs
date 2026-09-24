// These tests run one at a time, and the reason is WorkSnapshotTests.
//
// The work counters are global by necessity — the pipeline spreads itself across the thread pool,
// so a per-thread tally would only ever see a fraction of the job. That makes them accurate for one
// piece of work at a time and meaningless for two, and xUnit runs test classes in parallel by
// default: ProgramSnapshotTests plans four boards' worth of geometry while WorkSnapshotTests is
// trying to measure one, and the counts come out wherever the two happened to interleave.
//
// Found by running the suite rather than by thinking about it: the work snapshots passed on their
// own and failed the moment the whole assembly ran, which is exactly the shape of that bug.
//
// The cost is small — this assembly is seventeen tests of a few seconds — and the alternative is a
// measurement that cannot be trusted, which is worse than no measurement at all.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
