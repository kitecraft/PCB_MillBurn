# Contributing

PCB_MillBurn writes files that drive machines. A wrong number here is somebody's ruined board, and
an over-confident claim is worse than a missing feature. That is the whole reason the rules below
exist; none of them is ceremony.

If you only want to report something: the [issue templates](.github/ISSUE_TEMPLATE) are the fastest
route, and a bug report with the Gerbers and the emitted file attached is worth ten without.

---

## How work is organised

**Sprints.** Each one starts from a written direction by the product owner, and its stories live in
[`Sprints/`](Sprints) — one document per sprint, each story saying what problem it solves and why,
with links into the requirement rows it moves. The current one is
[Sprint 1 — Speed and accuracy](Sprints/Sprint-01-Speed-and-Accuracy.md).

**A sprint opens by reading the defect list, not the feature list.** Every open bug and known issue
is reviewed for inclusion before the sprint is fixed: the roadmap's *not started* and *found in the
workshop* sections, the matrix's Partial rows, and the open questions in
[Documentation/09](Documentation/09-Machine-Accuracy-Investigations.md). The sprint document records
what was read and what happened to it, including the issues deliberately left out and why.

**A story closes in writing.** Merging its branch does not finish it; a `Closed` block in the sprint
document does — the date, a plain verdict against the story's *Done when* clause, a summary of the
manual test that showed it, what was observed at the bench, and what was left open. The summary is
short, and long enough that somebody else could run a similar test without asking how. A story with
no `Closed` block is still open, and the sprint does not ship with one missing.

**New requests** are written into [Documentation/06](Documentation/06-Roadmap-and-Risks.md) as they
arrive, with the asker's own words where possible, and prioritised at the next sprint boundary
rather than mid-sprint.

## Branches

| Branch | What it is |
|---|---|
| `main` | Released code. Every commit on it is a merge of a release branch, and every release is tagged `vX.Y.Z` from it. |
| `release/X.Y.Z` | The sprint's integration branch. All of a sprint's work lands here first. |
| `NNN_ShortName` | One story, one branch, taken from the release branch. Numbered so they sort. |

A story branch is merged into the release branch; the release branch is merged into `main` when the
sprint ships. Nothing is committed straight to `main`.

## Getting a change in

1. **Branch from the release branch**, named `NNN_ShortName`.
2. **Write the tests with the code.** A new feature without tests is not finished; a bug fix without
   a test that fails before it is a fix nobody can keep.
3. **Run everything:** `dotnet test PCB_MillBurn.slnx`. Zero warnings — the build treats them as
   errors, deliberately.
4. **Review before you merge** (below).
5. **Squash into the release branch, then delete the story branch.** One story, one commit, with a
   message that says what changed and why; the release branch keeps one readable entry per story.
   Deleting is part of merging, not tidying done later. A squashed commit is not a descendant of
   the branch it came from, so git cannot tell the branch was merged: `git branch -d` refuses it
   and `--merged` never lists it. A story branch left behind therefore looks unmerged forever, and
   worse, is easy to pick up again — and a branch that gets more commits after being squashed in
   fights a conflict on every later merge, because the shared history it needs is not there.
6. **The release branch merges into `main` with a merge commit**, not a squash: a release is a real
   event and its shape is worth keeping. Delete the **In progress** note at the top of `README.md`
   in the same merge — it exists so that a visitor browsing the release branch on GitHub knows what
   they are looking at, and on `main` it would be a lie.

### Review

**Every merge into a release branch or into `main` is reviewed first.** In this repository that
means `/code-review` at medium effort, with an eye on anything that could affect performance — the
UI's responsiveness, the geometry pipeline, the optimizer, or the size and shape of the emitted
G-code and SVG. Fix what it finds, or record why it was left.

**Escalating to `xhigh` is the product owner's call.** It buys a much deeper read, and it is worth
it when there is a lot to read — at the end of a sprint rather than after every story. A medium pass
finding something is the ordinary case and is not by itself a reason to go deeper.

A review that finds nothing is still worth its minute. A merge that skipped one is not.

**And every test that is written or changed gets read back by somebody who did not write it**,
before the work merges. Here that is the `/describe-test` skill, which hands one test file to an
assistant with no knowledge of why it exists and asks it to say plainly what the test does, what it
feeds in, what it checks, and what would have to break for it to fail. Then that account is checked
against the test *and* against the code under it.

It is not ceremony. It has caught a cancellation test that only proved the very first line of a
method ran, and a test named for a guarantee whose body checked a default instead — which had been
passing over a real defect since the day it was written. Both were missed the same way: read by the
person who already knew what they were supposed to mean.

### If you are not the author

Outside contributions are welcome, and the process is the same shape with one extra step:

1. **Fork**, and branch from the release branch the work belongs to — ask in the issue if it is not
   obvious which.
2. **Open a pull request against the release branch**, never against `main`.
3. **Say what you measured.** For anything touching geometry, the optimizer or emitted files, a
   before-and-after number is worth more than a description. The CLI makes them easy to get:
   `millburn-cli export <board> --write` prints travel, line counts and time estimates.
4. A maintainer reviews it as above and squashes it in. Expect questions about *why*, not only
   *what* — this codebase explains itself in comments, and a change that cannot be explained is not
   ready.

**What gets a change rejected:** a number that cannot be traced to something measured or specified;
an emitted file that guesses where it should refuse; a feature with no test; and copied code from
either of the GPL projects in the workspace, which would relicense the whole application.

## Versions

Three numbers, and a leading `0` until the project has earned otherwise.

**Every change that a user would notice earns a line in [CHANGELOG.md](CHANGELOG.md)** under
`Unreleased`, written when the change lands rather than remembered at release time. The releases
page is the original; that file is the copy that travels with a clone.

| Change | Version moves | And |
|---|---|---|
| A sprint | the middle digit: `0.2.0` → `0.3.0` | |
| An urgent fix shipped mid-sprint | the last digit: `0.2.1` | |
| A change to a CLI workflow | the middle digit | **Breaking** at the top of the release notes, with the old invocation and the new one |
| A change to the project file's shape | the middle digit | the format number inside the file goes up too |

**Pre-1.0 there is no room for a separate "major", so the label does that work.** A release that
breaks a scripted pipeline says so in its first line, not in a paragraph somebody has to reach.

**Compatibility is a property of the code, not a promise in a release note.** Every saved artefact
carries its own `SchemaVersion` — projects, settings, the tool library, a saved test cut. The rule
is that a build reads every format it has ever written, and refuses a newer one by name rather than
guessing at it:

> `'Board.millburn' was written by a newer version of PCB_MillBurn (format 2; this build reads 1).`

Raising a format number is therefore a deliberate act with a reader to write, not a side effect of
adding a field. A field with a sensible default does not need one.

**1.0** is not a date. It is when the mixed workflow has been run end to end by somebody who is not
the author, and nothing a board depends on is still Partial in
[the matrix](Documentation/08-Requirements-Matrix.md).

## Style

Written down in [Documentation/10](Documentation/10-Style-and-Voice.md), because a codebase with a
consistent voice is easier to read than one where every file argues its own case. The short version:

- **Comments say why, not what.** The code already says what.
- **Names are words**, not abbreviations. `BreakThroughNm`, not `btNm`.
- **Nanometres in, nanometres out.** Millimetres are for people, and conversion happens at the edges.
- **Refuse rather than guess** in anything that writes a file.
- **Tests are sentences.** `TheOutlineCutsTabsToTheDepthItPromises`, and a summary saying what it
  guards.
- **British spelling** in prose and identifiers: colour, behaviour, optimise. The .NET APIs keep
  theirs.

## Running it

```sh
dotnet build PCB_MillBurn.slnx
dotnet test  PCB_MillBurn.slnx
dotnet run --project src/MillBurn.App -- <gerber-folder>
bash build/publish.sh win-x64 out/windows      # what a release ships
```

The app renders headlessly for screenshots and for checking a change actually landed —
`--shot <png>`, with `--preview`, `--settings`, `--about`, `--machine-check` and others. Use it: in
this project, every serious bug was found by looking at output rather than by a green test run.
