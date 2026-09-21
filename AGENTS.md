# AGENTS.md

Briefing for any AI assistant working in this repository — Claude, Codex, Cursor, or whatever comes
next. Humans want [CONTRIBUTING.md](CONTRIBUTING.md), which covers the same process in full.

**What this is.** PCB_MillBurn turns Gerber files into G-code for a mill and SVG for a laser. It
writes files; it does not drive machines. A wrong number here is somebody's ruined board, which is
why several rules below are firmer than they would be in a web app.

---

## Environment

- **.NET 10 SDK, the `10.0.4xx` band** — no vcpkg, no CMake, no MSYS2. `global.json` pins it with
  `rollForward: latestPatch`, so a `10.0.1xx`–`3xx` SDK is refused by name before any project loads,
  and a future band cannot quietly bring new analysers to a build that treats warnings as errors.
  Moving to a new band is a one-line change to `global.json`, made deliberately.
- **A bash shell** for `build/publish.sh`. On Windows use Git Bash: plain `bash` in PowerShell may
  resolve to the WSL launcher, which is a different operating system with no SDK in it.
- Windows or Linux desktop. macOS is untested.
- Settings and the tool library live in `%AppData%\PCB_MillBurn\` (`SpecialFolder.ApplicationData`),
  not in the repository.

## Commands

```sh
dotnet build PCB_MillBurn.slnx
dotnet test  PCB_MillBurn.slnx
dotnet run --project src/MillBurn.App -- <gerber-folder>        # the window
dotnet run --project src/MillBurn.Cli -- export <folder> --write -o out/
bash build/publish.sh win-x64 out/windows                        # what a release ships
```

**Lint is the build.** `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` and
`AnalysisLevel=latest-recommended` are set in `Directory.Build.props`, and `.editorconfig` raises
`IDE0005`, `IDE0055` and `IDE1006` to warnings, so unused usings, formatting and naming fail the
build like anything else. `dotnet format` will fix most of what they catch; there is no separate
format step in CI because there is no need for one. An analyser complaint is a compile error, and the fix is the code rather than a suppression
unless the suppression says why.

**Central package management.** Versions live in `Directory.Packages.props` only — never a
`Version=` in a `.csproj`. SkiaSharp is pinned to what Avalonia needs, so the two move together.

## Verify by running, not by tests passing

Every serious bug in this project was found by looking at output. A green suite is necessary and not
sufficient.

- `dotnet run --project src/MillBurn.App -- <board> --shot out.png` renders the real window and
  exits. Useful flags: `--size WxH`, `--theme`, `--preview`, `--settings [--settings-tab X]`,
  `--about`, `--machine-check [squareness]`, `--align`, `--only-toolpath <layer>`, `--map <log>`,
  `--recent`, `--framing`, `--colour`. `--bench` and `--probe` are handled before the UI starts.
- `millburn-cli export <board> --write -o <dir>` prints travel, line counts and time estimates —
  **the numbers to quote when a change claims to make something faster or tighter.**
- Publishing: the script **empties the output folder first**, so a wrong second argument deletes
  that folder's contents. Single-file builds are compressed, so searching the `.exe` for a string
  proves nothing — run the published binary. Publish only when the app is closed; the author leaves
  it open and the publish then fails on locked files.

## Layout

Thirteen projects, all plain `net10.0` — including the GUI, deliberately not `net10.0-windows`.

| Project | For |
|---|---|
| `Core` | units (`Nm`), the project model, settings, tool library |
| `Geometry` | Clipper2 and NTS: offsets, booleans, tessellation |
| `Gerber` | RS-274X / X2 and Excellon parsers |
| `Cam` | isolation, drilling, slots, outline, stock, mask, silkscreen |
| `Optimize` | travel optimizer, motion model, simplification |
| `Gcode` | emitter, parser, backplot, dry run, probing, test cuts, machine checks |
| `Export` | the SVG writer — the whole laser output path |
| `Align` | height maps, levelling, rigid fits |
| `Viewer` | scene, LOD, culling, Skia rendering |
| `Pipeline` | export planner and companion pages; ties the rest together |
| `App`, `Cli` | entry points holding no logic, so a job is identical either way |

**Dependency direction is strictly downward, and nothing references `App`.** Every algorithm stays
UI-free. `Post` and `Mcp` appear in the architecture document for the shape of the graph; `Post`
holds no source files yet and `Mcp` does not exist.

## Tests

- **xUnit only.** No mocking library, no property-test framework — "property tests" are hand-rolled
  loops. `Verify` is pinned and deliberately unused; the golden snapshotter is hand-written
  (`tests/MillBurn.GoldenTests/Snapshot.cs`), because a library that launches a diff tool is a
  liability in a headless run.
- **A test name is a claim** — `TheOutlineCutsTabsToTheDepthItPromises` — and the class `<summary>`
  says what would break in the real world if it failed. Assert on the emitted artefact rather than
  an intermediate object where you can: the file is what the machine runs.
- **Fixtures:** hand-written Gerber snippets inline; seven real KiCad exports committed in
  `tests/boards/` (`RealBoards.cs` finds them and *throws* if they are missing — absence is a broken
  checkout, not a skip); and an optional pcb2gcode corpus that is never committed, whose tests skip
  cleanly when it is absent.
- **Golden files:** a missing baseline is written *and then fails*, so a broken generator cannot mint
  its own proof. `MILLBURN_UPDATE_SNAPSHOTS=1` rewrites them — and a golden file that changes is a
  decision, so read the diff.
- **Determinism is a hard requirement.** No unseeded RNG, no hash-order iteration, no
  nondeterministic parallel float reduction. It is what makes the golden tests and "did my change
  help?" possible.
- Environment overrides: `MILLBURN_BOARDS`, `MILLBURN_GERBER_CORPUS`, `MILLBURN_UPDATE_SNAPSHOTS`.

## The rules that shape the code

Full version with reasons: [Documentation/10](Documentation/10-Style-and-Voice.md).

- **Refuse rather than guess** in anything that writes a file or reports a number somebody acts on.
- **A value decided elsewhere gets its source named**, not a second control that can disagree.
- **A rule that encodes a workflow becomes a list of choices**, with the old behaviour as default.
- **Nanometres (`long`) inside, millimetres at the edges**, and every length carries its unit in its
  name: `BreakThroughNm`, `FeedMmPerMin`.
- **Names are words, not abbreviations.** **British spelling** in prose and identifiers — colour,
  behaviour, optimise — while .NET's own APIs keep theirs.
- **Comments say why**: the decision, where a number came from, the defect this shape prevents.
  Deleting one that records a fixed bug is a regression.
- **Never a bare `catch`**; no `#region`; `ArgumentNullException.ThrowIfNull` at public boundaries.
- **Emitted text names its source** and stays neutral about what happens away from the machine: name
  the step, never the chemistry.

## Process

- `main` holds released code. **Never commit to it.** Story branches are `NNN_ShortName`, taken from
  the sprint's `release/X.Y.Z` branch and **squashed** back into it, one commit per story; the
  release branch merges into `main` with a merge commit, and a `vX.Y.Z` tag is what builds a release.
- **CI runs on `main`, on `release/**` and on pull requests** — but not on story branches, which
  never leave the machine they were made on. Run the tests yourself before squashing one in.
- **Versions:** a sprint moves the middle digit, an urgent mid-sprint fix the last one. A change to a
  CLI workflow or to a saved file's shape is labelled **Breaking** in the first line of the notes.
  Saved artefacts carry a `SchemaVersion`: read every older format, refuse a newer one by name.
- **Ask before committing, merging, pushing or publishing.** The author's own machine runs what this
  produces.
- The current sprint and its stories: [`Sprints/`](Sprints).

## Traps that have caught people

- `Invariant($"…" + "…")` does not compile — concatenation makes it a `string`.
- Quoted heredocs still eat backslashes; use a file edit for text with escapes.
- A headless run that opens a project **adds it to the author's real recent-projects list**.
- `.gitattributes` keeps `*.nc`, `*.gbr`, `*.drl` and `*.txt` byte-for-byte (`-text`) and forces LF
  on `*.sh`. A tool that rewrites line endings will show real diffs in golden baselines.
- `WorkingFolder/`, `out/` and `tmp/` are git-ignored in full; `design/Pictures/` is ignored because
  phone photos carry GPS. Strip metadata before any photo goes into `Help/`.
- **Licensing is load-bearing.** This project is MIT. `WorkingFolder/pcb2gcode/` and
  `WorkingFolder/Universal-G-Code-Sender/` are GPL-3.0 checkouts kept for reading only: copying,
  translating or transliterating any of it would relicense the whole application. Anything that
  looks remembered from those sources rather than derived from the spec gets flagged, not merged.
