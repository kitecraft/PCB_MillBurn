# Sprint 2 — Fix what is broken

**Agreed** 2026-09-24, after v0.2.0. **Release branch:** `release/0.3.0`, branched from `main`
rather than from `release/0.2.0` — the front page gained the optimizer comparison after the release, and
branching from the older release branch would have dropped it again at the next one.

**The product owner's direction:** fix all the known bugs, and take A6, M12 and V31 with them.

So: six of the seven open defects and three requirements, and for the first time the sprint is
defined by the defect list rather than by a theme. That is only possible because the list exists —
before 0.2.0 there were twenty-four different status strings across the roadmap and no way to ask
what was broken. There is now [a backlog](../Documentation/11-Backlog.md), and this sprint is what
it is for.

---

## How this sprint was picked

**The defect list is the sprint.** One command produces it:

```
grep -n 'defect · open' Documentation/06-Roadmap-and-Risks.md
```

Seven came back. **Six are in**; the seventh cannot start and is held over — see *Deliberately
out*. The three requirements the product owner named are added to the six.

| # | What | Where it came from |
|---|---|---|
| [6.25](../Documentation/06-Roadmap-and-Risks.md) | An isolation path bows into an arc where the copper is straight | The bench, twice, on the Arduino Mega |
| [6.26](../Documentation/06-Roadmap-and-Risks.md) | A hole that is not a hole, at isolation widths of 0.45 mm and over | The bench, same board |
| [6.33](../Documentation/06-Roadmap-and-Risks.md) | A superseded preview stops being watched, not stopped | The bench, testing sprint 1 story 2 |
| [6.34](../Documentation/06-Roadmap-and-Risks.md) | An even number of outline passes costs twice the travel of an odd one | Measured during sprint 1 story 3 |
| [6.39](../Documentation/06-Roadmap-and-Risks.md) | Offsets are not counted | A review, during sprint 1 |
| [6.40](../Documentation/06-Roadmap-and-Risks.md) | What the work counters do not watch | Written down when the counters landed |
| A6 | Debounce and coalesce slider drags | Requirements matrix |
| M12 | Corner-stop fixture generator — the recommended default | Requirements matrix |
| V31 | A dry run that is the real program raised | 06 §6.19 |

**Ordered by what a board pays for.** 6.25 is first because it is the only one that spoils copper: a
pass bows into the region it is supposed to leave alone, on a board somebody actually cut. 6.26 is
second because it is the same kind of fault — an offset producing geometry nobody asked for — on the
same board, and the two may share a cause. Everything after that costs time or trust rather than
copper.

**One item is deliberately not here.** [6.44](../Documentation/06-Roadmap-and-Risks.md) — the checks
becoming something you can read — absorbed four entries, two of them defects, so a sprint billed as
"fix all the known bugs" arguably owes it. It is out because it is a data-model change touching a
dozen producer sites, the CLI and two UI surfaces, and putting it beside nine other items is how a
sprint ends with everything half done. It is the obvious candidate to be sprint 3 on its own.

---

## How a story closes

As in [sprint 1](Sprint-01-Speed-and-Accuracy.md): every story ends with a `Closed` block carrying
the date and a plain verdict against *Done when*, the manual test in enough detail that somebody
else could run a similar one, what was observed including the sentence from the bench, and what is
left open. A story without one is still open whatever its branch says.

**And for this sprint, one addition.** A defect closes with the test that would have caught it, named
in the closure. Sprint 1's parser bug was green in a suite of 1,100 tests from the day it was
written; "fixed" without "and here is what now fails if it comes back" is half a job.

---

## Story 1 — The isolation path stops bowing

**Problem.** From the bench, on the Arduino Mega: *"One cut line near the middle-bottom of the board
is not straight. It's an arc."* A single isolation pass runs roughly horizontally and bows upward
into a shallow curve while the passes parallel to it stay straight. The copper it isolates is
straight, so **the path is wrong rather than ugly**: at the centre of the bow it cuts into the region
it was supposed to leave alone, and at the ends it leaves copper it was supposed to take. A second
instance was found the same way round, in the header pads along the top edge.

**What.** Find why one offset contour in a set comes back curved, and make it straight.

**Why.** It is the only open defect that spoils a board. Everything else on this list costs time, a
misleading number, or a wasted preview.

**How.** It was measured and not fixed, so the measurement is the starting point: eight bowed arcs,
all of them 17.6°, recorded in 06 §6.25. A consistent angle across instances is a strong hint —
that is not noise, it is something quantising. The arc tolerance, the sagitta and the round-join
approximation are all places a straight run could acquire a curve.

**Done when** the two instances on the Mega come out straight, a test fails if either returns, and
the closure says what the 17.6° was.

**Requirements:** — · [06 §6.25](../Documentation/06-Roadmap-and-Risks.md)

---

## Story 2 — The hole that is not a hole

**Problem.** From the bench, same board: *"IF top copper isolation >= 0.45 then the misplaced hole
appears. But, if the isolation is <0.45 then the misplaced hole is NOT present. The hole, while
being blue, seems to be connected to the top copper layer. When I hide the top copper layer, the
hole also hides."*

**What.** Find what the isolation program emits that the backplot draws as a plunge, and stop
emitting it.

**Why.** An operator cannot tell a real hole from this one by looking, and the board has holes. It
also appears and disappears with a setting, which is the kind of behaviour that makes somebody stop
trusting the picture.

**How.** The layer it belongs to is the whole clue: it is drawn in the plunged-hole style but hides
with the top copper, so it is not a drill. A width-dependent appearance points at an offset closing a
small feature into a loop, or two offsets driving into each other, leaving a short circular path
around nothing.

**Done when** the board is clean at every isolation width from 0.1 mm to 1.0 mm, and a test covers
the width that produced it.

**Requirements:** — · [06 §6.26](../Documentation/06-Roadmap-and-Risks.md)

---

## Story 3 — A superseded preview actually stops

**Problem.** From the bench, on a six-layer i.MX8M board where a preview takes seven to eight
seconds: a superseded preview *feels* interrupted and is not. `ExportPlanner.Plan` takes no
cancellation token; the token is read inside `PreviewBuild`, between programs, which is reached only
after planning has finished. The superseded run plans to the end on its pool thread, and the new
preview is instant because it found the memo, not because anything stopped.

**What.** Carry cancellation into planning, so that a superseded run stops where it is.

**Why.** Sprint 1 sold cancellation, and half of it is not there. On a board big enough to notice —
which is exactly where it matters — a thread keeps working on an answer nobody will read.

**How.** The token has to reach `ExportPlanner.Plan` and be checked between programs and between
layers. Keeping the abandoned plan on the way out is a feature and should survive: going back to a
setting you just left is instant because the work was kept.

**Done when** a superseded preview's thread stops within one program, the memo still catches the
return trip, and a test proves the abandoned run ended early rather than finished.

**Requirements:** A5 · [06 §6.33](../Documentation/06-Roadmap-and-Risks.md)

---

## Story 4 — The outline's even-pass penalty

**Problem.** Measured on `GridStripConnector_Panelized`: a channel cut at several depths alternates
direction as it goes deeper, so an **even** number of passes returns the tool to where the stack
began and an **odd** number leaves it at the far end. The difference is roughly twice the travel —
odd ≈ 330 mm against even ≈ 715 mm — and it is a property of the geometry, not of the ordering. The
optimizer is already doing the best it can with what it is handed.

**What.** Stop the pass count's parity deciding the travel.

**Why.** It is the largest remaining travel win on a panel, and it is invisible: nobody would guess
that changing a step-down by a tenth of a millimetre doubles the getting-about.

**How.** The choice is upstream of the optimizer — either the stack is handed over in an order that
does not alternate, or the step-down that produces an even count is nudged, or the last pass is
allowed to repeat a direction when that is cheaper than returning. Which of those is right is the
story's first question, and it should be answered with numbers.

**Done when** the panel's outline travel does not depend on the parity of its pass count, and the
work snapshots record the new figure.

**Requirements:** O10 · [06 §6.34](../Documentation/06-Roadmap-and-Risks.md)

---

## Story 5 — The counters count everything

**Problem.** Two known gaps, both written down when the counters landed. **6.39:** offsets are not
counted, and every toolpath this application makes is built by one, so the tally describes the
compositing well and the toolpath building badly. Nothing stops a fifth bypass either — four call
sites went around `Polygons` and were found by a review reading for them. **6.40:** the optimizer is
not measured at all, only `Gcode` at 1.6 mm is exercised, four of ten boards are covered, and the
path the app actually takes when a project is reopened is not the path the benchmark walks.

**What.** Count the offsets, make going around the counter a build error, and close the coverage
gaps worth closing.

**Why.** Stories 1, 2 and 4 are all offset work. Doing them against a tally that cannot see offsets
means doing them blind, and this sprint has three of them.

**How.** A counting wrapper for `InflatePaths`, a banned-API list or analyser rule so that calling
Clipper directly does not compile, and the baselines regenerated with the diff explained. The
coverage half is judgement: the optimizer is worth bracketing, ten boards may not be.

**Done when** offsets appear in `WorkCount`, calling Clipper outside the wrapper fails the build, and
the optimizer's cost is recorded per board.

**Requirements:** — · [06 §6.39](../Documentation/06-Roadmap-and-Risks.md),
[§6.40](../Documentation/06-Roadmap-and-Risks.md)

## Story 6 — The three the product owner asked for

Grouped because none is a sprint's worth on its own, and all three are about the operator rather than
about the copper.

**A6 — slider drags stop re-planning on every pixel.** Debounce and coalesce, so dragging a width
slider plans the value you stopped on rather than every value you passed through. Only meaningful
now that the pipeline is off the UI thread and cancellable, which is why it waited.

**M12 — a corner-stop fixture generator, as the recommended default.** The fixture that makes a board
repeatable: cut it once, and every board after that registers against the same two edges.

**V31 — a dry run that is the real program raised.** Every move as written, spindle off, every Z
offset by a rise (default 3 mm), so plunges and lifts show and the time is the real time. It refuses
when the lowest point would not clear the stock, and on `G92`, `G10` and `G38`, because a program
that redefines the coordinate system cannot be safely lifted. The existing flat dry run stays as a
choice.

**Done when** all three are in, each with the test that covers it, and V31 refuses the three cases
above by name.

**Requirements:** A6, M12, V31

---

## Deliberately out

**6.45 — block apertures and the aperture transforms.** An open defect, and the only one not in a
sprint that set out to fix them all, so the reason belongs here rather than in a footnote. It cannot
be started: `%AB%` and the `%LM/LR/LS%` transforms are refused outright, and **no export in
`tests/boards` uses either**, because KiCad emits neither. There is nothing to prove a fix against.
Writing a fixture from the Ucamco specification is possible — it is how `GerberParserTests` already
works — but a feature of this shape, proved only by a fixture its own author wrote, is the trap this
project has been caught by twice. It waits for a real board that needs it, which is also the only
evidence that anyone does.

**6.44 — the checks become something you can read.** Explained above: a data-model change large
enough to be its own sprint, and it would not be finished beside nine other items.

**6.16 and 6.46**, both parked with reasons, and neither reason has changed.

**Anything that is not on the list.** A sprint defined by the defect list stops meaning that the
moment a feature is added to it.
