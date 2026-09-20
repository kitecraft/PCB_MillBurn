# Working on PCB_MillBurn

The app turns Gerbers into files that drive a mill and a laser. A wrong number is somebody's ruined
board, so the standards below are practical rather than ceremonial.

Full detail: [CONTRIBUTING.md](CONTRIBUTING.md) for process,
[Documentation/10](Documentation/10-Style-and-Voice.md) for style,
[Sprints/](Sprints) for what the current sprint is about.

## Before merging anything

**Run `/code-review` at medium effort before every merge into a release branch or into `main`**,
with an eye on anything that could affect performance: the UI's responsiveness, the geometry
pipeline, the optimizer, or the size and shape of the emitted G-code and SVG. **If it finds
anything, run it again at maximum effort** before fixing — the second pass is where the expensive
problems surface.

**Run `/describe-test` on tests that were just written or changed.** A context-free subagent reads
the test back in plain language; check that account against the test and against the code. It is
there to catch a test and a bug that agree with each other.

## Branches and commits

- `main` holds released code. Never commit to it directly.
- `release/X.Y.Z` is the sprint's integration branch; story branches are `NNN_ShortName`, taken from
  it and **squashed** back into it, one commit per story. The sprint in progress is `release/0.2.0`.
- **Versions:** a sprint moves the middle digit, an urgent mid-sprint fix moves the last one, and a
  change to a CLI workflow or to a saved file's shape is labelled **Breaking** at the top of the
  notes — pre-1.0 there is no separate major to carry it. Saved artefacts each hold a
  `SchemaVersion`: read every older format, refuse a newer one by name.
- The release branch merges into `main` with a merge commit, then a `vX.Y.Z` tag is pushed, which is
  what builds the release.
- Ask before committing, merging, pushing or publishing. The workshop's own machine runs what this
  produces.

## Verify by running, not by tests passing

Every serious bug in this project was found by looking at output. A green suite is necessary and not
sufficient.

- `dotnet test PCB_MillBurn.slnx` — zero warnings; the build treats them as errors.
- `dotnet run --project src/MillBurn.App -- <board> --shot out.png` renders headlessly; flags
  include `--preview`, `--settings`, `--settings-tab`, `--about`, `--machine-check`, `--align`,
  `--only-toolpath`, `--size WxH`, `--theme`.
- `millburn-cli export <board> --write -o <dir>` prints travel, line counts and time estimates —
  the numbers to quote when a change claims to make something faster or tighter.
- `bash build/publish.sh win-x64 out/windows` is what a release ships. Publish only when the app is
  closed, and check by **running the published binary**: single-file builds are compressed, so
  searching the exe for a string proves nothing.

## The rules that shape the code

- **Refuse rather than guess** in anything that writes a file or reports a number somebody will act
  on.
- **A value decided elsewhere gets its source named**, not a second control that can disagree with
  the first.
- **A rule that encodes a workflow becomes a list of choices**, with the old behaviour as the
  default.
- **Nanometres inside, millimetres at the edges.**
- **Comments say why.** Record the defect a shape prevents, so the next tidy-up does not reinvent it.
- **New features need tests**; a bug fix needs a test that fails without it.

## Things that have bitten before

- Quoted heredocs still eat backslashes — use the Edit tool for text with escapes.
- `Invariant($"…" + "…")` does not compile: concatenation makes it a `string`.
- The workshop leaves the app open; a publish then fails on locked files.
- Headless runs that open a project add it to the real recent-projects list.
