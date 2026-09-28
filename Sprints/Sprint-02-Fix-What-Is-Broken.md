# Sprint 2 — Fix what is broken

**Agreed** 2026-09-24, after v0.2.0. **Release branch:** `release/0.3.0`, branched from `main`
rather than from `release/0.2.0` — the front page gained the optimizer comparison after the release, and
branching from the older release branch would have dropped it again at the next one.

**The product owner's direction:** fix all the known bugs, and take A6, M12 and V31 with them.

So: six of the seven open defects and three requirements — nine stories in the end, because the
three requirements are three pieces of work rather than one grouped item, and because a tenth item
found inside story 6 became story 9. They were written as a single
story to begin with, on the grounds that none was a sprint's worth on its own; that is an argument
about size and not about what they are. A6 is about the preview, M12 about a fixture and V31 about
the dry run, and closing them together would have meant one closure trying to answer for three
unrelated things. Corrected by the product owner before any of the three was started.

For the first time the sprint is defined by the defect list rather than by a theme. That is only possible because the list exists —
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

### Closed — 2026-09-26 · accepted, not fixed

**The story's own first question answered itself.** *"Whether that is worth the complication is the
first thing to decide"* — and once the ratio was turned into time, it decided. The 395 mm of extra
rapid at the product owner's 0.500 mm step-down is **about twelve seconds** at the profile's
2,000 mm/min, on a job the program estimates at 40m 49s – 42m 39s. Under half a per cent.
[6.34](../Documentation/06-Roadmap-and-Risks.md) is now `defect · accepted`, carrying the number so
the 2× figure cannot re-open it on its own.

**Everything in the entry reproduced**: 337, 721, 326, 326 and 710 mm across the five step-downs,
on `GridStripConnector_Panelized`. Nothing was wrong with the observation; what was missing was the
denominator.

**What the measuring added.** It is not extra lifting — both cases emit 59 rapids and 59 plunges,
the same hops with the even ones longer. It is not the local search — `Thorough` matches `Balanced`
exactly, the even case's *constructed* route is 732 mm before any improvement, and the odd case's
construction lands near 326 and improves by under half a per cent, which is why its program has no
`Ordering:` line at all. So the entry's reading was right: a fix would have to change what
construction is handed, not how it searches. And the obvious levers are bad trades — forcing an odd
count by shrinking the step-down adds about a quarter to the cutting to save hundreds of
millimetres of rapid; letting an even stack traverse pays one run-length inside the stack to save
one outside.

**A wrong turn, recorded rather than tidied away.** Partway through I reported that the optimizer's
objective disagreed with the emitted program — 1,050 mm planned against 337 mm emitted — and said
that had to be understood before any fix. It was my own measurement: I routed the whole outline in
one call from a start point of my choosing, which is not what `ExportPlanner` does. The planner's
own figures track the file to within three per cent — 701 mm predicted, 721 mm emitted. The product
owner spent a turn on that before it was corrected.

**What is left open.** Nothing in this story, and nothing 6.34 asked for — no line was changed on
its account. The entry names what would re-open it: a board where travel is a large share of the
job, or a machine slow enough in rapid that 395 mm is minutes.

**The branch is not empty, though**, because the `/code-review` run this story ended with read the
whole sprint and found six things in stories 1 to 3. They are fixed here rather than left for a
sprint nobody has planned:

- **Two more paths where a preview outlives what it describes**, which is 6.33 in siblings that
  were not looked at. `ResetLayerSettings` would have published a preview over the settings it had
  just reset; `ApplyRefresh` swaps the board's source bytes, so the preview finishing after it
  describes copper that no longer exists — and that one was not clearing the backplot either.
- **A comment that claimed a guarantee no test holds**: `PlansInFlight` said `CancellableWorkTests`
  pinned it. Nothing does, and the one test that reads the peak says in its own doc that it does
  not assert it. Now marked diagnostic, which is what it is.
- **Two overflows that fail the wrong way.** `toleranceNm * 10` wraps negative above a tenth of
  `long.MaxValue`, `Math.Max` hands back 1 nm, and arc fitting switches silently *off* — the
  opposite of what a caller asking for an enormous tolerance meant, and the doc invites them to
  pass `long.MaxValue`. Both sites now saturate.
- **An integer division that rounds in silence**: `ChordMultiple = DefaultChordMultiple /
  BudgetShare` is only the derivation it claims while the numerator is even. A constant expression
  that divides by zero when it is not makes that a compile error at the edit rather than a surprise
  later.
- **`loop(s)` reached the operator as `loop[s]`**, because parentheses delimit a G-code comment and
  the writer rewrites them. It says `1 loop` or `3 loops` now, like every other count in that file.

**O10 needed nothing from this story** — *"local search never makes travel worse than the
baseline"* was met in sprint 1 story 3, and it is listed here because it is the requirement this
ground belongs to. The measurements confirm it rather than test it: the even case improves by 4 %
and the odd case by under half a per cent, neither gets worse, and `Thorough` never differs from
`Balanced`.

---

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

### Closed — 2026-09-26

**Met, with one part of 6.40 refused and argued rather than forced.**

**6.39 — offsets, and a door that cannot be walked around.** `Polygons.Inflate` and
`Polygons.Sweep` count the offset and delegate. All fifteen offset sites go through them, and the
four booleans that were counting themselves by hand beside the call now go through the `Polygons`
wrappers that already existed — so nothing outside one file touches Clipper's counted entry points,
and `BannedApiAnalyzers` makes a new one a build error. Proved by planting a direct call back into
`IsolationOperation` and watching the build fail, rather than by trusting the wiring.

| Board | offsets realising | offsets planning | planning vertices before → after |
|---|---:|---:|---|
| Arduino Mega | 8,024 | 1,323 | 885,555 → **1,273,512** |
| Panel | 10,622 | 359 | 290,968 → 431,953 |
| Test board, wide moat | 6,237 | 1,451 | 478,595 → 739,613 |

Realising the Mega does 8,024 offsets against 1,493 booleans — five times as many operations as the
counters were watching — and a third of the geometry the planner hands to Clipper was unrecorded.
The sharpest way to put the risk: a change that swapped a boolean for an offset would have shown
booleans down and vertices down, so the snapshot whose job is to notice would have said the work
got cheaper.

**6.40 — what the counters watch.** The optimizer is counted: `Work.Search` records one search and
the steps it spent, which is what the search *cost* against `RoutePlan.Improvements`, which is
what it *achieved*. The number already existed and was being discarded on return. Counted once per
search, not once per move — the inner loop runs hundreds of thousands of times on a panel, and an
increment in there would be a cost worth measuring rather than a measurement.

SVG is measured, and getting there corrected the premise: under the milling defaults nothing
produces SVG, so the case planned an empty job and the floor assertion caught it doing no work at
all. It uses `ImportDefaults.LaserEtching` now — the workflow that actually emits SVG, and the one
the product owner runs. Its baseline earns its place through its zeroes: SVG planning does no
offsetting and no route search.

Seven boards of ten, with the choice argued in the file. And `LoadSources` — the path the app takes
when a project is reopened — costs exactly what the tested path costs, 144 booleans, 6,237 offsets
and 229,341 vertices either way, to the digit. That was an open question about whether this whole
folder measured a path nobody uses; it is an assertion now rather than an assumption.

**The levelled case was refused.** Levelling is not in `MillBurn.Pipeline` at all — it is a
post-process over emitted G-code, run from the CLI against a probe log — and `WorkSnapshotTests`
brackets loading and planning. There is no point in that file where a levelled run exists to
measure, so bolting one on would have measured something else and called it levelling. The "done
when" asked for the wrong thing; 06 §6.40 now says so, and says it wants its own harness.

**What the review found, and it was the story's own headline claim.** The first ban list named six
methods; it missed `Xor`, a `BooleanOp` overload, every double-precision overload, and the classes
the static facade is built on. `Clipper.InflatePaths` is a few lines over `ClipperOffset`, so
banning only the facade left the door beside it open — `new ClipperOffset()` inside `MillBurn.Cam`
compiled clean. The claim that a direct call was a build error was simply false for three ways in.
The list is enumerated off the type now, and three ways round planted in `IsolationOperation`
produce ten diagnostics where they produced none.

Two more, both on the new search counter: a group of fewer than three nodes returned before it was
recorded, so splitting routing into many tiny groups would have made the counters fall while the
optimizer did more work. And it was called `SearchMoves` and claimed to be "the only honest measure
of what the search costs" — a step is one dequeue, including ones the don't-look bits discard and
excluding the candidates weighed within a step. It is `SearchSteps` now and the doc says what it
misses.

**What is left open.**

- **One hole in the ban, deliberately.** `Polygons.cs` suppresses the rule for the whole file
  rather than per call site, because a pragma repeated fourteen times stops being read. A new
  method *inside that file* that forgets to count is not caught — one file to review rather than a
  source tree, and it is written at the top of it.
- **Levelling is still unmeasured**, and now has a reason and a shape rather than a line in a list.
- **Nothing here makes the program faster.** It is an instrument. What it buys is that the next
  regression is visible, which is exactly what the first baselines committed — wrong by a factor of
  two hundred on planning booleans — showed was not true before.

---

## Story 6 — A slider drag rebuilds once

**The premise this story was written on was wrong, and the correction is the useful part.** It
said a drag asks for a plan at every value it passes through, and that cancellation made that
survivable rather than free. **Nothing plans on a drag.** Planning happens when Preview is clicked
and at no other time; the only property change that previews as a side effect is the Settings
dialog's Save, which is already on-commit. That reasoning was invented when this story was drafted,
not taken from A6, which says only *"debounce/coalesce window on slider drags"*.

**Problem, measured.** What a tick does cost, on the Arduino Mega:

| Per tick | |
|---|---|
| `ProjectFile.ToBoard` | **5.4 ms**, and zero geometry — the realisation memo absorbs all of it |
| The same on the test board | 0.4 ms |
| A full-range thickness drag | 28 ticks, so ≈ 152 ms of UI thread |
| And when a preview is on screen | a whole `Rebuild`: scene build, and every `LayerRow` destroyed and re-created |

**What.** Debounce the invalidate-and-rebuild, so a drag rebuilds once at the value it settled on.

**Why.** 152 ms of copying every Gerber's bytes to reach a memo hit is the cheap half; the
expensive half is the `Rebuild`, which happens whenever the operator has a preview up — which is
exactly when they are most likely to be adjusting something.

**How.** There is no debounce anywhere in the app today and no drag-completed signal on either
slider — no `Thumb.DragCompleted`, no `LostFocus` binding, no `UpdateSourceTrigger`. So the delay
goes beside `LatestRun` in the view model, which is already where "the newest edit wins" lives.

**The trap, written down before it is fallen into.** `Rebuild` destroys and re-creates every
`LayerRow`, re-seeding from `_project.Settings` — including the row whose control the operator is
holding. A debounce that parks a pending value outside the project loses it to any `Rebuild` fired
from another path.

**Two more corrections to the original text.** There are exactly two sliders in the application,
board thickness and blank border; the isolation width named in the first draft is a `NumericUpDown`.
Both sliders already snap to ticks, which is the only rate limiting that exists today.

**Done when** a drag of any length rebuilds once, at the value the operator let go on, and a test
proves the intermediate values never reached the rebuild rather than reaching it and being
discarded.

**Requirements:** A6

### Closed — 2026-09-26 · already satisfied, and no code written

**A6 holds, and it held before this story started.** `OnOutputChanged` already coalesces a drag,
by accident of the guard it uses to decide whether there is anything to invalidate:

```
var stopped = StopPreview();
if (_backplot.Count == 0 && !stopped) { return; }
_backplot = [];
...
Rebuild(TimeSpan.Zero);
```

The first tick of a drag finds a picture on screen, clears it and rebuilds. **Every tick after that
finds `_backplot` empty** — the first one emptied it — and no preview running, so it returns before
reaching `Rebuild`. A drag of twenty-eight ticks rebuilds once, and a drag with nothing on screen
rebuilds not at all. That is what A6 asked for.

**So this story wrote a debounce, wired it in, tested it seven ways, took it to the bench, and then
took it out again.** The class and its tests are gone. What is left is the trace above and this
account of it.

**The premise was wrong twice, and the second one is mine.** The first draft said a drag asked for a
plan at every value it passed through; nothing plans on a drag, and that was corrected before any
code was written. The rewrite then said a drag cost twenty-eight rebuilds at 5.4 ms each — 152 ms
of UI thread on the Arduino Mega. That figure came from timing `ProjectFile.ToBoard` in a loop and
multiplying by the tick count, which is arithmetic rather than measurement: `ToBoard` runs *inside*
`Rebuild`, and `Rebuild` runs once. The 5.4 ms is real; the twenty-eight was invented.

**What a tick actually costs**, which is the useful residue: `RecordOutputs` over every row,
`_project.Touch()`, `DescribeBlank`, `StopPreview` — microseconds — and, on the thickness slider
only, **a settings file written to disk**. That last one is [6.49](../Documentation/06-Roadmap-and-Risks.md),
split out at the product owner's direction before any of this was known, and it is now the whole of
what a drag costs that is worth anything.

**The bench passed all four checks against a broken implementation.** That is the part worth
keeping. The wiring put `Settle.Request()` *below* the guard, so ticks two to twenty-eight never
reached it: the leading run happened, the trailing run never did, and the scene was left showing the
first tick's value rather than the one the operator stopped on. All four manual checks passed
anyway, because with the guard already coalescing, a broken debounce and no debounce look identical
from the outside. The checks were mine to design and they could not have caught it.

**What found it** was `/code-review`, tracing the guard against the leading run's first statement.
Not the tests — eight of them, all passing, none able to reach `MainViewModel`. The header of that
test file said as much while the file was being written.

**What is left open.**

- **The guard is load-bearing and nothing says so.** A6 now depends on `_backplot.Count == 0` being
  the coalescing mechanism, which is not what that line was written for and not what its comment
  describes. Anything that makes the invalidation path rebuild unconditionally reintroduces the
  per-tick cost with no test to notice. Worth an entry if it is ever touched.
- **`Debounce` was good code for a problem that did not exist.** It is in the history if a real one
  turns up — a genuine per-keystroke path, or A7's progressive reveal.

---

## Story 7 — A corner-stop fixture, as the recommended default

**Problem.** A board cut today and a board cut next week do not register against anything, so the
second one has to be aligned by eye.

**What.** Generate the fixture: cut it once, and every board after that registers against the same
two edges.

**Why.** It is what makes a double-sided board repeatable, and it is the thing the author's own
workflow does by hand with an L-shaped jig — [06](../Documentation/06-Roadmap-and-Risks.md) records
the laser side of that. Recommending a default matters more than the generator: the value is in
everyone using the *same* two edges.

**How.** No code. The product owner's decision, 2026-09-27: *"I don't think I want this is code. I
think a help guide that users can follow to create their own corner stop guide is fine. I don't
think we need to code anything or produce anything for them at this point."*

**Requirements:** M12

### Closed — 2026-09-27 · met by a guide

**Met, and the requirement is met without a generator.** `Help/guides/corner-stops.html` ships in
the Help menu and the contents: [04 §4.1.1](../Documentation/04-Machines-Laser-and-Mixed-Workflows.md)'s
three-pad 3-2-1 corner, the relief, the pad height worked out from the operator's own stock, the
error budget, the squaring operation that collapses it, and the mirrored second corner — with six
line drawings rather than photographs, which also means no workshop photo's GPS tag to strip.

**The argument for not building it is better than the one for building it.** Two things came out of
writing the guide:

- **The datum belongs in the controller, not in the app.** The half that looked like the valuable
  one — remembering where the fixture is so later jobs use it — is a work offset. `G55` does this,
  every sender exposes it, and it survives the app being uninstalled. Storing it here would have
  been a second, worse copy of a thing the machine already owns, and one more place for the two to
  disagree.
- **There is no hard geometry in it.** The fixture is a pocket with three islands, or — for CAM that
  will not do islands — four rectangles. Anyone who can run this app can draw that once. A generator
  would have had to learn stock size, margin, cutter diameter and thickness in order to emit what a
  person draws in ten minutes and cuts once, ever.

**What the guide has to carry instead**, because no code is checking it: that a fixture is cut for
one stock thickness and running thinner stock puts the pads above the work, and that the outline
must be checked to clear the pads. Both are stated as warnings at the point they bite, and the
suggested label engraves the thickness onto the plate so the first one is answered by looking at it.

**Nothing in `src/` changed** beyond the Help menu item and its handler.

---

## Story 8 — A dry run that is the real program raised

**Problem.** The dry run available today is flat: it traces the path at one height. It does not show
plunges and lifts, and its time is not the job's time.

**What.** Every move as written, spindle off, every Z offset by a rise — 3 mm by default — so the
Z motion is visible and the estimate is real.

**Why.** A dry run exists to be believed before a cut. One that leaves out the Z motion leaves out
the part that takes the time and the part that crashes.

**How.** It refuses rather than guesses when it cannot be safe: when the lowest point would not
clear the stock, and on `G92`, `G10` and `G38`, because a program that redefines the coordinate
system cannot be lifted by adding to Z. The flat dry run stays as a choice — it is the right tool
for checking XY alone.

**Done when** the raised run emits the real program with Z offset, the estimate matches the job's,
each of the three refusals is named in its own message, and a test covers each refusal by name.

**Requirements:** V31

### Closed — 2026-09-27

**Met.** Confirmed at the bench on a published build: the Z motion is there, the run time lands
where the real program's does, and nothing touched the board.

**The claim is checked move by move, which is stronger than the entry asked for.** 6.19's done-when
is about the machine; the test is that the raised program has the same number of moves as the real
one, the same X and Y, the same kinds, and every Z exactly the rise higher — across four programs
on three boards. Checking only that the lowest point clears the stock would pass for a great many
programs that are not the real one lifted.

| | |
|---|---|
| Z travel, real vs raised | 15 mm vs 15 mm |
| Z travel, flat | **0 mm** — what the old dry run threw away |
| Lowest point, test board | 1.10 mm above the stock, over 19 programs |

**Where it refuses**, each with a test: too deep for the rise (and the message names the rise that
would do — 5.00 mm for a 4.5 mm cut), `G92`, `G10`, `G38`, and a machine-coordinate Z. Incremental
mode was already refused and still is.

**Two decisions that differ from the entry.** No opening lift, because an extra move makes the run
no longer a copy and kills the move-by-move check; the clearance check on the way out is what keeps
it safe. And `G53` carrying a Z is refused rather than left alone — the safety check reads the
rewritten file back and cannot know that one Z is in another coordinate system, so leaving it means
the promise has a hole. Both are recorded in 6.19 as departures rather than presented as what was
asked for.

**The choice is wired in three places**: Settings, the CLI's two flags, and the confirmation line —
which used to say "dry run held at 5.00 mm" unconditionally and would now be wrong half the time.
Asking the CLI for both a rise and a height is refused, because they are different promises.

**The existing tests now name the flat style.** They predate there being a choice, and V31 changed
the default; leaning on it would have quietly turned them into tests of the raised run, and most
would still have passed. Same lesson as story 1's chord tolerance.

### What the code review found, and what was done

Ten findings, all in this story's own work. Every one is fixed; each has a test, and the two that
mattered were mutation-checked against the code before the fix.

**The one that would have reached the bench.** With the raised style — now the default — any
program that travelled in X or Y before commanding a Z was refused outright, and told to raise
further. That advice could never work: the number being refused was the parser's implicit start at
Z0, which no rise changes, so every retry gave the same answer. `--start-gcode "G0 X0 Y0"` is
enough to trigger it, and framing only *warns* about such a line, never rejects it. The dry run was
lost entirely for the person most likely to want one.

The fix was already written down. `DryRunReport.LowestZMm` says it reports *"commanded positions,
not the machine's starting one — where the tool is before the first line runs is the operator's
business, and a generator that claimed otherwise would be claiming something it cannot know."*
Counting the implicit zero broke that sentence. Moves before the first commanded Z are now excluded
from both the measurement and the clearance check, and the flat run is untouched because its header
commands a Z on the first line.

**Two holes in the refusal guards**, both of which produced a raised dry run with no refusal at all:

| Missed | What it does | Why it slipped |
|---|---|---|
| `G92.1`, `G92.2`, `G92.3` | Restore a saved work offset — the thing the `G92` refusal exists to stop | The word matcher read the decimal point into the digit run, then failed to parse `"92.3"` as an integer |
| `G43`, `G43.1`, `G49` | Move the Z datum the rise is measured from | Never considered. `G43.1` is what a tool setter writes into start G-code, which reaches every program |

A negative `G43.1` was caught only by accident — the parser mistook it for a Z move — and the
message it produced was nonsense: `raise by at least 50.50 mm` for a 3 mm rise.

**Four smaller ones, all true.**

- The raised header said *"the time this takes is the time the real program takes"* unconditionally,
  while `KeepFeeds = false` replaces every feed with the rapid rate. It was the one false sentence
  in the file, on the one page read standing at the machine. The sentence is now conditional, and
  says plainly that the time means nothing when the feeds are off.
- `G53` passed through the rise *and* the feed rewrite, so with feeds off it was the only line in
  the file still carrying a cutting feed.
- `Offset` re-formatted with `0.###`, which drops trailing zeros: `Z2.000` came out as `Z5`. Every
  Z line in the dry run differed in *shape* from the real program — defeating the reason that method
  preserves everything else on the line. Verified on a real export: the dry run's Z words are now
  `Z2.950 Z3.500 Z5.000` against the real `Z-0.050 Z0.500 Z2.000`.
- The settings check blocked **Save** on the flat height whatever style was chosen, so picking the
  raised run could kill the Save button over a number with no effect on the file — with the tab it
  pointed at showing nothing wrong. Each style is now judged on its own number, and a rise of zero
  or less is caught in Settings rather than as one clearance refusal per program at export time.

### The second review found two more, both introduced by the first round's fixes

Worth recording because of where they came from: the first review's top finding was that excluding
the parser's uncommanded Z0 was necessary. Doing it introduced two new holes, and neither was
visible from the change itself.

**A program that commands no Z at all was handed back as itself, reported as raised.** With every
move excluded from the measurement, the measurement was empty, and an empty measurement fell back
to reporting the rise. So the file came back byte-for-byte unchanged apart from the spindle, under
a header saying *every Z raised by 3.00 mm* and a summary quoting a lowest point of 3.000 mm that
no line in it asks for. Set work zero as that header instructs and it cuts at Z0 for its length.
Refused now. Nothing the app emits looks like this — every program it writes opens with a lift —
but `DryRun` is public and a file somebody else wrote is what a dry run is for.

**And the clearance flag could come back false with nothing refused.** `StaysClear` tests where a
move starts as well as where it ends, and the first measured move inherits its start from the last
move that was deliberately *not* measured. So `G1 X20 Y20 F300` in start G-code produced a file
that shipped carrying its own evidence that the promise was broken — and nothing in `src/` reads
the flag, so nobody would ever have seen it. The boundary is exempt at its starting end only now,
every later move is still checked at both, and a run that is not clear is refused outright rather
than written with a false flag.

**The comment that said this could not happen was the tell.** It asserted as an invariant that
`lowest >= floor` implies `clear`, which was false for exactly the reason above. It now says the
relationship is reasoning rather than proof, and the code checks `clear` separately instead of
trusting it.

**Two gaps of the same family, found by asking what else the guards miss.** `G73` is a canned cycle
outside the 81–89 range the retract check knew about, so its Z rose and its R did not — the cycle
drills through its own retract, which is the failure that check exists to prevent. And `G28`/`G30`
end at a machine position whatever their Z word says, so raising one moves the waypoint and not the
destination; refused now, like `G53`.

**And a test that had stopped testing what it named.** `CommentSyntaxTests.TheDryRunHeaderIsWellFormed`
called `Rewrite` with no options, so when V31 changed the default it silently switched from checking
the flat header to the raised one — without failing. It now names all four combinations, and a
second test pins the timing sentence to the feeds.

**What is left open.**

- **The flat run still rewrites a `G53` Z**, which was true before this story and is wrong for the
  same reason the raised one refuses it: it turns a machine-coordinate move into a work-coordinate
  one. Out of scope here — it is pre-existing behaviour and changing it is its own decision — but
  it should not go unrecorded.
- **The rise is one number for the whole program.** A job whose deepest pass is much deeper than
  the rest is refused on account of that pass, when a smaller rise would have been fine for all the
  others. Refusing is the right default; a per-program rise would be a feature, not a fix.

---

---

## Story 9 — The settings file stops being written mid-drag

**Problem.** `OnBoardThicknessMmChanged` writes the app settings to disk, so a full-range drag of
the thickness slider writes the JSON **twenty-eight times** — and what it is saving is not the
project's thickness but the app-wide default, the value that decides where the *next* new board
starts. Twenty-seven of the twenty-eight are superseded immediately.

**What.** Write it once, or later, or somewhere that is not a property setter.

**Why.** Disk I/O in a drag loop runs on the UI thread, is the one cost in that loop that scales
with the file system rather than with the board, and is fine on a warm SSD and horrible on a
network share.

**How.** Unsettled. The narrow fix is to move the write off the setter; the broader question is
whether an app-wide default should be saved on change at all rather than on close.

**Why it is not part of story 6.** Split out at the product owner's direction, and the reason is
worth keeping: a debounce would *hide* this — twenty-eight writes become one — without the write
ever having been examined. It is a thing that should not be happening at that moment whether or not
anything is debounced.

**Added to the sprint after it was agreed**, which the sprint's own rule says has to be declared:
it is a defect, found while working the list, and it is small. Take it out if you would rather it
waited, as 6.45 and 6.47 did.

**Requirements:** — · [06 §6.49](../Documentation/06-Roadmap-and-Risks.md)

### Closed — 2026-09-26

**Met, and with less code than the story expected.** A full-range drag writes the settings file
**zero** times, down from twenty-eight; the value still survives a restart; and where the write
happens is stated at the place it happens. Confirmed at the bench: *"verified."*

**The fix is a deletion.** `Closing` already saves the window placement, and that goes through
`SaveSettings`, which writes the whole object — so the thickness default was going to be persisted
on close whatever the setter did. Holding it in memory with `RememberSettings` and letting the
close path write it is the entire change.

**What it costs**, said rather than glossed: a session that ends in a crash loses the thickness
default. It is read only when the next board is imported.

**Worth noting against story 6.** This was split out of A6 on the grounds that a debounce would
have *hidden* it — twenty-eight writes becoming one — without the write being examined. That turned
out to matter more than it looked: story 6's debounce was removed entirely because the problem it
addressed did not exist, and had the write not been split out first it would have gone with it,
still happening twenty-eight times a drag and still unexamined.

**What is left open.**

- **No test, and none reachable.** Four lines in a view model `MillBurn.Tests` does not reference.
  Third time this sprint; the pattern is now worth an entry of its own rather than a line in each
  closure.

---

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

**6.52 — a test cut that finds where the copper stops.** Asked for by the product owner on
2026-09-27 and written up as a tenth story the same day, then held for the next sprint before any
of it was built. It is the only item here that was *in* and came out again, so it is worth saying
why plainly: it is an enhancement, this sprint is the defect list, and the rule below was written
to stop exactly this. The work of writing the story was not wasted — the design questions were put
and answered, and all three answers are recorded in
[6.52](../Documentation/06-Roadmap-and-Risks.md) rather than in a sprint document that no longer
carries it. Whoever starts it next sprint starts from decided ground.

**Anything that is not on the list.** A sprint defined by the defect list stops meaning that the
moment a feature is added to it. Tested once, on 2026-09-27, and it held.
