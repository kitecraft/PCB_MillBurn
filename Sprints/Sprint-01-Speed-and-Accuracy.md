# Sprint 1 — Speed and accuracy

**Agreed** 2026-09-20, after v0.1.6. **Release branch:** `release/0.1.7`.

**The product owner's direction:** *"MillBurn had a highly featured MVP release. We've tweaked and
added some great usability and some additional functional changes. Now I'd like to prioritise on
performance, optimization, speed, and accuracy of both the app itself, and the functional nature of
the implementors (libraries)."*

So: no new features. Six stories, three about speed and three about being right.

---

## How this sprint was picked

**Measured before ranked.** Four numbers set the order:

| | |
|---|---|
| `Task.Run` in `MillBurn.App` | 0 |
| `CancellationToken` anywhere in `src/` | 0 |
| Panel preview: pipeline share of a 4.4 s run | 2.6 s, on the UI thread |
| Export: 66-up panel / Arduino Mega / test board | 2.3 s / 3.8 s / 1.5 s |

The pipeline is not slow. It is **synchronous, uncancellable and forgetful**: every preview freezes
the window for seconds, a superseded edit still runs to completion, and changing one layer redoes
all of them.

**Every open bug and known issue was read first**, which is where story 6 came from. What was read,
and what happened to it:

| Issue | Where | Outcome |
|---|---|---|
| Outline lifts to safe height between laps and over tabs | 06 §6.24, V36 | **In** — story 6 |
| Local search cycles on open runs, bounded not fixed | 03 §7.2, O10 | **In** — story 3 |
| Optimizer timing gate failed under load | `RouteOptimizerTests` | Fixed before the sprint: bound raised to 10 s, and the determinism guarantee never rested on a clock |
| Mill 0.24 mm on a diagonal | 09 §1 | Closed: three small effects, none worth chasing |
| Laser 0.25° out of square | 09 §1 | **Out** — the frame is the fix and the workshop cannot adjust it yet; compensating in the file was considered and set aside |
| Stock keeps its holes when re-measured | 06 §6.16, M30 | Stays parked: built once, too confusing |
| Datum corner unreadable on a square piece | 06 §6.20, V32 | **Out** — a feature, not a defect |

---

## Story 1 — The window stops freezing

**Problem.** Every preview and export runs on the UI thread. The window locks for 1.5–4 s, and a
2.6 s panel preview cannot be cancelled even when the next edit has already made it pointless.

**What.** Move pipeline work to the thread pool, give every run a cancellation token, and let a new
edit cancel the run it supersedes.

**Why.** Perceived speed is what people call "fast", and this is the largest single source of the
app feeling slow. It is also the prerequisite for stories 2, 3 and for the stretch items — none of
them can show progress while the thread that would draw it is busy.

**How.** One `CancellationTokenSource` per edit, cancelled when the next arrives; `Task.Run` at the
pipeline boundary rather than sprinkled through it; the UI thread left to render and to marshal
results back.

**Done when** the window stays live with progress while a board is realised, a superseded edit is
cancelled rather than completed, and no pipeline work remains on the UI thread.

**Requirements:** A5, A10 ([08](../Documentation/08-Requirements-Matrix.md)) ·
[01 §4, §7](../Documentation/01-Architecture.md)

---

## Story 2 — Changing one layer stops redoing all of them

**Problem.** Every preview realises every layer from scratch, even when one checkbox moved.

**What.** Memoise pipeline stages against a hash of their inputs, so unchanged work is reused.

**Why.** It is the difference between "the app is quick" and "the app is quick after the first
time", and it compounds with story 1: work not done needs no thread.

**How.** `XxHash128` is already in the tree for exactly this. Key each stage by a **structural**
hash of its inputs — geometry, settings, tool — never by a timestamp or an object identity, so the
same input gives the same key on any machine and determinism is preserved rather than traded away.

**Done when** changing one layer re-runs only what depends on it, a second preview of the 66-up
panel returns in well under half a second, and identical input still produces byte-identical output.

**Requirements:** A4 · [01 §4](../Documentation/01-Architecture.md)

---

## Story 3 — The optimizer stops chasing its tail

**Problem.** The local search can cycle on open runs — 299,044 "improvements" on 50 nodes — which
the step budget bounds rather than fixes. And Eulerian merging across the containment tree, the last
open item of the optimizer's own phase, is still not built.

**What.** Make every applied move strictly improving, and merge paths that share endpoints so the
tool stops lifting between them.

**Why.** Wasted search is wasted time on every export, and a search that can cycle is a correctness
smell in the place the project makes its loudest claim. Merging cuts travel directly, which is
machine time on every board.

**How.** Prove the improvement condition in a test rather than by inspection. Merge across the
containment tree the geometry already builds.

**Done when** no applied move can make the route worse, shown by a test, and measured travel on the
panel falls against today's figure.

**Requirements:** O10, O6 · [03 §7.2, §8](../Documentation/03-Toolpath-Optimization.md)

---

## Story 4 — The app checks its own isolation electrically

**Problem.** Nothing verifies that an isolation pass actually separates the nets it should, or that
it has not severed one. The first test is the etched board.

**What.** After isolation, compare the connected components of the remaining copper against the
netlist the Gerbers already declare, and report what does not match.

**Why.** This is the accuracy item with the most teeth: a short and an open are both invisible on
screen and expensive in copper. The app is one step away from it — X2 attributes are parsed today
and then unused.

**How.** Connected components over the realised copper; nets from the X2 attributes; report before
anything is written, naming the two nets involved rather than a coordinate.

**Done when** the test board and the panel report zero violations, and a deliberately
under-isolated board names the nets it has joined.

**Requirements:** A11, and it puts M4's parsed attributes to work ·
[01 §8](../Documentation/01-Architecture.md)

---

## Story 5 — Stop ignoring what the export already tells us

**Problem.** `.gbrjob` ships in every KiCad export and is never read, after which the operator is
asked for a thickness and layer roles it already states. Block apertures (`%AB%`) and the transform
commands are reported as errors, which is honest and still refuses boards other tools produce.

**What.** Read the job file for board-level metadata, and realise block apertures and transforms.

**Why.** Accuracy of the input, and less typing: a number read from the file cannot be typed wrongly.
Both also remove a class of "it refused my board" with no workaround.

**How.** Job file first, the operator's own entry still winning, with the source named wherever the
number is shown — the rule this project already follows for inherited settings.

**Done when** thickness and layer roles come from the job file when it is there, their source is
named, and a board using block apertures realises correctly under test.

**Requirements:** G5, G3 · [02 §2](../Documentation/02-Gerber-and-Geometry-Pipeline.md)

---

## Story 6 — The outline stops climbing to the sky

**Problem.** Found at the bench. The cut-out lifts to the safe height at the end of every lap — to
the same X and Y it is about to cut from — and clears a 0.5 mm tab by climbing 2 mm above the
surface. Measured on the test board's outline: 39.40 mm of rapid up, 16.50 down and 20.90 of
plunging, about **59 s of vertical motion** on a two-minute program.

**What.** Drop straight to the next lap when X and Y have not moved, and hop a tab at the tab's own
height.

**Why.** It is the only story here that speeds up the machine rather than the app, it costs about
45 s a board on the one it was measured on, and Z is the slowest axis on a desktop mill.

**How.** Routing already does the first half — `SlotOperation` carries one continuous descent per
feature — so this is the outline learning what its sibling knows. The tab clearance is computed from
the tab's own height and named in the program's comments, never typed.

**Done when** the test board's outline spends under fifteen seconds moving vertically rather than
fifty-nine, cuts the same shape, and still lifts to the safe height for genuine travel.

**Requirements:** V36 · [06 §6.24](../Documentation/06-Roadmap-and-Risks.md)

---

## Stretch, if the six land early

- **A6** — debounce and coalesce slider drags. Only meaningful after story 1.
- **A7** — progressive reveal: draw paths before the optimizer has finished.

## First reserve

- **M17, M19** — the three-point fit, and refusing an export above a residual threshold, if
  alignment accuracy turns out to matter more than parser accuracy.

## Deliberately out

V30 (paste stencil), V31 (the raised dry run), V28 (stock dialog), V33 (one-sided hole approach).
All are features, and this sprint is not about features.
