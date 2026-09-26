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

**Done when** all eight instances on the Mega come out straight, a test fails if any returns, and the
closure says what the curve really was. The test that would have caught it is
`ArcFittingTests.TheMegaHasNoArcSweepingAcrossStraightCopper`, which asserts the bench's own
criterion — no arc of radius over 5 mm in a copper program — on both copper layers.

### What it turned out to be

**An arc replaces segments, not points — and the fitter only checked its own vertices.**

An offset emits a rounded corner as three vertices about 18 µm apart, and then one straight run of
twelve millimetres with nothing in between. Six such points — three at each end — sit within 0.8 µm
of a 39 mm circle, because anything nearly collinear fits a huge circle. The arc through them was
therefore accepted, and it bowed **464 µm through the empty middle**, where no vertex contradicted
it, and cut into copper that was supposed to stay.

**The 17.6° was a red herring, and so was the reasoning that followed it.** 06 §6.25 argued that a
constant angular extent means something is quantising. It does not: `Simplify.FitArcs` refuses any
arc sweeping less than `MinimumSweepRadians = 0.3` rad — **17.19°** — so every gentle curve it emits
comes out just over that. The constant angle was the acceptance threshold, not the fault.

**A wrong turn worth recording**, because it cost half a day and would have cost more. An early
measurement appeared to show the simplifier moving the path by at most 5 µm, and that was written up
here as "the simplifier is innocent". It was wrong: the measurement compared each original vertex
against the *nearest point anywhere* on the simplified path, and on a 9,482-point contour that folds
back near itself, a bowed vertex sits close to some unrelated part of the path. The mistake was
found by asking a better-posed question — what did each suspect arc replace, by position in the path
rather than by proximity — which gave six vertices, two clusters, and a twelve-millimetre gap.

**The fix** checks the middle of every segment an arc replaces, not only its ends. The threshold is
measured rather than chosen: across the 4,246 arcs fitted to the Mega's top copper, the honest ones
stray 1.1 µm at the median and 4.6 µm at the 99th percentile, and the eight bad ones stray 74 to
464 µm. Nothing lies between 5 and 74 µm, so the check allows 10 µm — twice the worst honest arc,
a seventh of the mildest bad one.

**That calibration is the whole difference between a fix and a trade.** Two earlier attempts were
stricter and rejected honest arcs with the invented ones, costing 29 % to 57 % more moves — exactly
the failure the test plan below warns about. The calibrated check costs **two extra moves across the
whole board**.

### Test plan — the bench

**Board:** `tests/boards/Arduino_Mega_2560`, which is committed, so this needs nothing from the
workshop. **Settings:** the defaults — 30° V-bit, 0.1 mm deep, 0.4 mm isolation width — and 1.6 mm
thickness. Any settings will do as long as both runs use the same ones.

**What to look for.** Eight places where a straight run of copper is cut as a shallow curve. All
coordinates are in the exported program's frame, which is the board's lower-left corner:

| | Program | From | To | Bow |
|---|---|---|---|---|
| 1 | `F_Cu` | 64.849, 9.541 | 52.782, 9.541 | **464 µm** |
| 2 | `F_Cu` | 82.022, 24.830 | 70.292, 24.830 | **451 µm** |
| 3 | `F_Cu` | 35.731, 20.688 | 44.067, 20.684 | 320 µm |
| 4 | `F_Cu` | 65.655, 48.200 | 67.496, 50.042 | 101 µm |
| 5 | `B_Cu` | 72.466, 17.048 | 72.466, 25.791 | 337 µm |
| 6 | `B_Cu` | 8.980, 48.305 | 13.443, 43.842 | 243 µm |
| 7 | `B_Cu` | 81.443, 30.853 | 85.327, 30.853 | 150 µm |
| 8 | `B_Cu` | 56.845, 3.828 | 56.845, 5.725 | 74 µm |

Numbers 1, 2, 5 and 7 have endpoints sharing an X or a Y, so the run between them is provably
straight and the bow is unambiguous. Numbers 1 and 2 are the two the product owner saw.

**In the window.** Open the board, set the top copper to G-code, Preview, and zoom to each `F_Cu`
row above. The toolpath should run straight between those two points. It is worth looking at 1 and 2
before any fix as well as after — *"one cut line near the middle-bottom of the board is not straight,
it's an arc"* is how it was first described, and knowing what that looks like makes the after-shot
convincing rather than merely clean.

**From the command line**, which is the quicker check and the one that cannot be argued with:

```
millburn-cli export tests/boards/Arduino_Mega_2560 --thickness 1.6 --write -o out
```

then look in `out` for a `G2` or `G3` whose `I`/`J` describe a radius over 5 mm. Every arc in a
correct program belongs to a pad or a corner, and no pad on this board is 39 mm across. **One
line of evidence: a radius greater than 5 mm in a copper program is the fault, and there should be
none.** Eight is what it is today.

**Pass.** No arc in either copper program has a radius over 5 mm, and the eight rows above are
straight lines. **Fail, and worth reporting:** any arc of large radius remains, or a run that was
straight before is now made of many short segments instead — trading a wrong curve for a bloated
program is not a fix, and the line count in the export summary will say so.

**Also check nothing else moved.** The same export should still simplify about as well as it does
now — the summary line reports it, and a large change in the arc count means legitimate arcs were
lost along with the invented ones.

**Requirements:** — · [06 §6.25](../Documentation/06-Roadmap-and-Risks.md)

### Closed — 2026-09-25

**Met.** All eight instances come out straight, the program is the same size, and the product owner
confirmed it at the bench: *"The original problem lines are fixed. I have not found any others.
Performance seems to be unaffected."*

**The manual test.** The plan above, run on `tests/boards/Arduino_Mega_2560` at the default V-bit,
0.1 mm deep and 0.4 mm isolation. The two rows the bench had originally reported — `F_Cu` at
(64.849, 9.541) → (52.782, 9.541) and (82.022, 24.830) → (70.292, 24.830) — were looked at before
and after, and the other six checked with them.

**What was observed.**

| | before | after |
|---|---|---|
| Arcs of implausible radius, `F_Cu` | 4 | **0** |
| Arcs of implausible radius, `B_Cu` | 4 | **0** |
| Worst bow | **464 µm** | none |
| Moves, `F_Cu` | 18,130 | 18,130 |
| Moves, `B_Cu` | 8,851 | 8,853 |

Two extra moves on the whole board. That number is the story: two earlier attempts at the same fix
were stricter, rejected honest arcs along with invented ones, and cost 29 % to 57 % more moves —
which the plan above had already named as the way to fail. The difference between them is a
threshold measured rather than chosen.

**The test that would have caught it** is
`ArcFittingTests.TheMegaHasNoArcSweepingAcrossStraightCopper`, run over both copper layers. It is
the only one of the three in that file that goes through `PathSimplifier`, so it is the only one
that would notice the call site quietly ceasing to pass a chord tolerance — the other two hand
`Simplify` the value themselves and would pass while the product shipped the bug.

**What is left open.**

- **The arc fitter's threshold is calibrated against one board.** 3 mm in the test and 10 µm in the
  simplifier both come from the Arduino Mega's distribution. Another board with a genuinely curved
  trace of large radius would be refused a legitimate arc — the failure would be a bigger program
  rather than a wrong cut, and the test names the number so it can be argued with.
- **A wrong turn is recorded above** rather than deleted: an early measurement appeared to clear the
  simplifier and was written into this document before it was checked properly. It was wrong for a
  specific and repeatable reason, which is why it stays.
- **[6.47](../Documentation/06-Roadmap-and-Risks.md)** was found and measured while this story was
  open — isolation cutting inside a hole about to be drilled — and deferred by the product owner the
  same day.

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

### Closed — 2026-09-25

**Met.** The board is clean at every isolation width from 0.1 mm to 1.0 mm — the smallest loop that
survives anywhere on it is 0.165 mm across, against a 0.124 mm cut — and `IsolationSliverTests`
covers the width that produced the fault along with the one that did not. Confirmed at the bench on
a published build: *"Verified fixed."*

**How to reproduce it, which took a round trip to establish.** Asked to try again, the bench could
not make the fault happen on either v0.1.6 or v0.2.0. The missing ingredient is in the project, not
the settings dialog: `Arduino Mega 2560.millburn` cuts its top copper at **0.045 mm deep**, where
the V-bit takes 0.124 mm. Four passes clear 0.441 mm and five clear 0.546, so 0.45 is the first
width asking for a fifth. At the 0.05 mm default the board takes four passes at both 0.40 and 0.45
and plans identically. The threshold in the bug's title is a pass count boundary, and it moves with
the depth.

**What it is.** The screenshot was measured against the board outline to get a coordinate — the
transform checked by aiming it at 6.25's known bowed line, whose midpoint the other red box in
`2_Errors_On_This_Board.png` lands on to three decimal places. The stray sits at **(15.47, 31.47) mm**
from the board's lower-left: a plunge, three moves and a retract tracing a loop 175 µm long and
28 µm wide that encloses 4,971 µm², left where the fifth pass's offsets closed on each other.

**The first fix was not enough, and only the bench's failure to reproduce caught it.** A contour
narrower than the cut cannot have come from copper, so a bounding box under the cut width was
refused. That cleared 122 slivers and left the reported one standing — it measures 126.4 µm across
a 124.1 µm cut and clears the test by two microns. Had the reproduction guide gone out unchecked,
the story would have closed on a fix for a different bug.

**What the rule is now.** What the loop encircles, against what the plunge starting it already
takes out: a disc one cut wide, `π(w/2)²`. Offsetting a point gives exactly that disc, so the bound
is exact for an island, and measurement says nothing real is near it — across the Mega, the Uno, the
test board, the connector and the pogo jig, at two depths and two widths, the smallest positive-area
contour is 360,000 µm², thirty times the threshold, and the rule refuses none of them. The cap at
two cut widths is what makes it safe: a long thin hole encloses little too, one on this board runs
2.03 mm, and dropping it would leave a ridge of copper down the middle of the moat.

**What was observed**, Mega top copper at 0.045 mm / 0.45 mm:

| | before | after |
|---|---|---|
| Plunges | 1,373 | **999** |
| Cutting | 14,612.0 mm | 14,554.7 mm |
| Travel | 1,664.4 mm | **1,433.8 mm** |

374 plunge-and-retract cycles — about three seconds each — for four tenths of one per cent of the
cutting. At the 0.05 mm / 0.40 mm default the same board loses 325 of 1,242.

**Three baselines moved.** `PogoTest1-wide-moat` drops 8 plunges and 23 mm of travel for 0.7 mm less
cutting, and gains a line: the note below. `Millburn_Test_Board-wide-moat-work` falls from 796
booleans to 724.

`Arduino_Mega_2560-work` goes the other way, and it is worth saying why rather than accepting it:
planning vertices 885,225 → 885,555, with booleans and point tests identical to the digit. That
baseline plans at one pass and 0.05 mm deep, where this board drops **two** contours on the whole
top copper — measured, not assumed. `PassLinker` calls `Clears` for each consecutive pair within
reach of each other, two counted booleans a time; the count is unchanged, so the same number of link
decisions ran, and the extra vertices are those decisions now being made between real passes rather
than between a real pass and a splinter with six points in it. A quarter of a per mille, in the
direction that means the optimizer is looking at the board instead of at litter.

**Said out loud.** The isolation program now carries a note — *"325 loop(s) too small to be a cut
were not planned: each was under a cut wide, or enclosed less than the plunge starting it would
remove"* — for the same reason the achieved width and the pass cap are reported. It is a rule
applied without being asked for, on a dense board it accounts for a quarter of the plunges, and an
operator comparing a plan against the picture should be able to see that loops were refused rather
than wonder.

**What is left open.**

- **Nothing checks the viewer**, and nothing can. The product owner's condition was *"as long as the
  UI shows correctly, we can't have random blue rings laying about"* — a picture, not an assertion.
  It was checked at the bench and passed, and the next change to this rule will need checking the
  same way.
- **[6.48](../Documentation/06-Roadmap-and-Risks.md)** came out of closing this one: a coordinate
  readout in the corner of the window, and a crosshair pointer. The bug this story fixed was
  reported without a position because there was no way to read one off the screen, and recovering it
  took a screenshot measured against the board outline in a script.
- **The two-cut-width cap is a scale, not a law.** It is what separates a dot from a path, and it is
  chosen against this board's distribution; a board whose geometry leaves compact holes of two or
  three cut widths would keep them, and they would look like the same fault.

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

### Closed — 2026-09-26

**Met.** A superseded run stops at its next check rather than at the end of the plan, a plan that
completed is still kept, and the number in flight is stated and readable. Confirmed at the bench on
the i.MX8M board this was found on: *"Verified."*

**What was done.** `ExportPlanner.Plan` takes a token and reads it between layers, between programs
and between toolpaths — the innermost of the three is where simplification and route optimisation
spend the seconds, so it is the one that decides how long a cancelled run keeps going. The token has
**no default** at `ExportRequest.Plan`, deliberately: a token with a default is a token somebody
forgets, which is the shape of the chord-tolerance trap `/code-review` found in story 1, and the one
call site that matters is in a view model no test can reach. The preview passes its run's token;
`PlanExport`, which every synchronous caller funnels through, says `CancellationToken.None` out loud.

**What was observed.**

| | |
|---|---|
| Stopped 5 layers into 15 | 12,426 vertices, against 133,908 for the whole board |
| Four overlapping previews | 1 ran to a plan |
| Returning to the setting you left | still a memo hit |
| A cancelled plan | no longer remembered |

**The half the entry said to think about was a misreading of the bench, not a trade-off.** 6.33
credited the abandoned run finishing with the *"remembered in 0.11 s"* that was seen. It should not
have: the operator changed a setting, previewed, changed it **back**, and previewed again, so the
plan remembered is the one for the setting returned *to* — cached before any of it started. The
abandoned plan was for the setting left. Cancelling early therefore costs almost nothing, and both
halves are asserted rather than argued.

**A second fault, found at the bench while testing the first.** *"If I change a setting, click
preview, then change the setting back, the preview completes instead of being interrupted on the
setting change."* `OnOutputChanged` returned early when nothing was drawn — and while a preview is
building nothing is drawn, because the edit that started it cleared the last one. Worse than waste:
the finished preview drew itself, showing programs for the setting just left. Fixed by stopping the
run above that guard, and in `ForgetProgram` too, which `Adopt` calls before swapping projects.

**The test that would have caught it** does not exist and cannot: `MillBurn.Tests` does not
reference `MillBurn.App`, so nothing reaches `OnOutputChanged` or `Adopt`.
`CancellableWorkTests.ARunCancelledWithoutASuccessorCannotPublish` pins the contract underneath —
after a cancel with no successor, `Finish` returns false, which is exactly why the caller has to
clear the busy flag itself.

**Two mistakes of mine, both caught by running rather than by thinking.**

- Four of the six new tests read global counters — `Work`, and the memo's hits and misses — while
  `MillBurn.Tests` runs its classes in parallel. They passed under `--filter` and failed the moment
  the suite ran, which is the exact shape `Parallelism.cs` was written about. The counter-reading
  ones moved to `MillBurn.GoldenTests`, which runs one test at a time, with the fixture shared by
  linking as `RealBoards.cs` is.
- A Python edit converted `ExportPlanner.cs` from CRLF to LF. Git normalises on commit so nothing
  looked wrong, and what broke was the build: IDE0055 is an error here and reported it as three
  formatting complaints inside a comment nobody had touched.

**What is left open.**

- **Nothing tests the view model**, and that is a gap this story could not close. Both faults live
  in `MainViewModel`, the fix for the second one is four lines there, and the only check either got
  is the bench.
- **The ceiling is measured, not enforced.** `PlansInFlight` and `PeakPlansInFlight` are readable
  and nothing acts on them. If a board ever appears where four abandoned runs still overlap long
  enough to matter, the answer is a bound on concurrency, and that is a different piece of work.

---

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

**6.47 — isolation cuts inside a hole that is about to be drilled.** Reported from the bench on
2026-09-24, after this sprint was agreed, and deferred by the product owner the same day. A sprint
defined by the defect list has to say what it does when the list grows underneath it: this one is
low priority and waits. The cutting is arithmetically correct — a 0.415 mm moat around copper
standing 152 µm clear of a 0.65 mm hole necessarily sweeps across it — so what it costs is time and
a backplot that reads wrong, and neither is worth reopening a sprint for.

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
