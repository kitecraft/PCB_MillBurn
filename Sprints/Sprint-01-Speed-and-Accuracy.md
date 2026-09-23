# Sprint 1 — Speed and accuracy

**Agreed** 2026-09-20, after v0.1.6. **Release branch:** `release/0.2.0` — the middle digit moves
because a sprint moves it, and 0.2.0 also marks the turn from adding features to making what is
there faster and provably right.

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

## How a story closes

A story is not finished because its branch merged. It is finished when somebody has written down
that it is, with the evidence attached — so **every story here ends with a `Closed` block**, and a
story without one is still open whatever its branch says.

A `Closed` block carries four things:

1. **The date, and a plain verdict** against the *Done when* clause — met, met in part, or met under
   a scope that was narrowed, saying which.
2. **The manual test.** What was actually done at the bench: which board, what was changed, what to
   watch for. A summary, not a transcript — but enough that somebody else could run a similar test
   without asking how.
3. **What was observed**, including the sentence from the bench. The numbers say whether it is
   faster; the sentence is usually what says whether it is better.
4. **What is left open**, named and pointed at, so that nothing is quietly closed along with it.

The boards are the ones in `tests/boards`, which any reader has, except where a test needed
something bigger than anything committed here — and then the closure says so, and names the nearest
committed stand-in.

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
cancelled rather than completed, and no pipeline work remains on the UI thread **on the preview
path**.

**Scope narrowed, 2026-09-21, deliberately.** The original clause said "no pipeline work remains on
the UI thread" without qualification, and that is not met: eight planning callers still run
synchronously. Reading them turned up why, and it is not what the clause assumed.

All of them are one dialog. `AlignmentWindow`'s constructor calls `DrillFiles`, `StockProgram`,
`WasteHoles` and `FrameWidthNm`, and `BuildMoves` calls `MovablePrograms` — and every one of those
runs a full `ExportPlanner.Plan`. Opening drill alignment plans the whole board **five times**.

So the defect there is not that the work is synchronous; it is that it is done five times over.
Moving five redundant plans onto the thread pool would hide that rather than fix it, and would
rewrite the same call sites story 2 is about to rewrite. It goes to story 2 with this count as its
evidence, and story 1 claims only the path it actually measured and fixed.

**And one more, found by the review rather than by reading.** `Apply` ends with `Rebuild`, which
calls `ProjectFile.ToBoard` — re-parsing every Gerber source and rebuilding every scene, on the UI
thread, at the end of every preview. So even the preview path is only half off the thread: the
planning and the backplot moved, the re-realisation did not, and on a large board it is the
dominant cost. It is left here for the same reason as the five plans: re-realising a board that has
not changed is precisely what story 2 removes, and hoisting it now would be building story 2's
answer in story 1's shape. **Story 1's clause should be read as the planning and the backplot, not
the realisation.**

### Closed — 2026-09-21

**Met, under the scope narrowed above:** the planning and the backplot, not the realisation. The
window stays live with progress while a board is realised, and a superseded edit no longer has its
result published over a newer one.

**The manual test.** Open a board big enough that a preview takes seconds — this was done on a
six-layer i.MX8M dev board, which is larger than anything committed here; `Arduino_Mega_2560` is the
nearest stand-in in `tests/boards`. Then, *while the preview is running*: drag the splitter between
the tree and the viewport, open and close layer rows, scroll the board. Then start a preview and
immediately make another edit, and watch which picture ends up on screen. What to watch for is the
window refusing input, the busy state sticking after a run ends, and a slow run's picture landing on
top of a quicker later one.

**Observed:** *"No freezing. The pane can be moved, layers open and closed, nothing seems blocked."*
The status bar reads "Working…" throughout and then how long it took — 1.46 s on the test board,
which deliberately counts the rebuild that is still on the UI thread rather than reporting the
0.93 s an earlier version did. A superseded run's result is discarded rather than published.

**Left open, recorded rather than fixed:** the eight synchronous planning callers, all of them
`AlignmentWindow`, which plans the whole board five times to open one dialog — handed to story 2
with that count as its evidence. And `Apply` → `Rebuild` → `ProjectFile.ToBoard`, which re-realises
every layer on the UI thread at the end of every preview.

**And one the closure should not claim**, found during story 2 and written up as
**[6.33](../Documentation/06-Roadmap-and-Risks.md)**: "cancelled" here means the token is read
between programs in `PreviewBuild` and the stale result is thrown away. `ExportPlanner.Plan` takes
no token at all, so a superseded run's *planning* still goes to the end. The window behaves as this
story promised; the work underneath it does not stop. Story 1 is closed on the behaviour it tested
for, and the rest is 6.33's.

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

### Closed — 2026-09-21

**Met, on the board the clause names.** Changing one layer re-runs only what depends on it, a second
preview of the 66-up panel costs 0.2 ms of planning against 916 ms, and identical input still gives
byte-identical output — the story changed no golden snapshot, which is the check that would have
caught it if it had not.

`tests/boards/GridStripConnector_Panelized` *is* the 66-up panel: one board stepped out by KiKit
into eleven rows of six, 164.70 × 106.90 mm. It was briefly written off here as "nothing like a
66-up panel" on the grounds that it holds three layers and plans to two programs — which confuses
how many *layers* a board has with how many *boards* are on a panel. Sixty-six copies of a
three-layer board is a three-layer board.

| Board | First | Again |
|---|---|---|
| Arduino Mega 2560, in the application, end to end | 2.98 s | **0.06 s** |
| Arduino Mega, after changing one setting | 2.97 s → | **2.14 s** |
| `GridStripConnector_Panelized` — the 66-up panel — planning alone | 916 ms | **0.2 ms** |
| PogoTest1, planning alone | 207 ms | **0.2 ms** |

The second row is the part that is easy to miss: a *changed* setting costs 2.14 s where the first
preview cost 2.97 s, and the 0.83 s difference is the layer memo declining to re-realise Gerbers
that did not change. The 2.1 s that remains is planning, which this story cannot remove — only stop
repeating. Stories 3 and 5 are what reduce it.

So *"a second preview of the 66-up panel returns in well under half a second"* is satisfied with
three orders of magnitude to spare: 0.2 ms of planning, and the layers not realised again either.

**The manual test**, across three bench sessions on the author's own boards. Open a board that takes
seconds to preview — this was the six-layer i.MX8M dev board, which is bigger than anything
committed here; `Arduino_Mega_2560` in `tests/boards` is the nearest stand-in — and then read the
status bar, which now says which of the two things happened:

1. Preview once and note *"built in …"*. Preview again without touching anything: *"remembered in …"*.
2. Change one layer's output and preview. The cost should fall without vanishing: the layers that
   did not change are not realised again, but the planning still runs.
3. Go back to a setting used several changes ago, and check it is still remembered rather than
   rebuilt.
4. Race it. Start a preview of a change you know has to be built, then put the setting back and
   press preview again immediately.

**Observed:** the table above, and on the whole thing, from the bench: *"Overall, it does feel
speedy to use."*

**Steps 3 and 4 are where the session earned its keep.** Step 3 rebuilt: the plan cache kept four
plans whatever their size, sized for the six-layer board in 6.30 whose programs come to 7.2 MB while
the Mega's come to 0.6 MB, so returning to a setting used six changes ago planned it again from
scratch. It is bounded by the text it holds now rather than by a count, and that is fixed here.

Step 4 answered *"remembered in 0.11 s"* — correct, and for the wrong reason. The superseded run was
not interrupted: it kept planning to the end on its own thread, and the new preview was quick
because it found the memo. That is
**[6.33](../Documentation/06-Roadmap-and-Risks.md)**, recorded and not fixed, and it is the honest
limit of this story.

**What story 1 handed over** is bounded rather than removed. Drill alignment still makes five
planning calls to open one dialog, but `ExportPlanner.Plan` now goes through the plan memo, so four
of them come from memory instead of planning the board again. The dialog itself has not been timed
at the bench, so read that from the code rather than as a measurement.

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

**Measured before starting, 2026-09-22, and it changes what this story is about.**

The figure to beat, on `GridStripConnector_Panelized`:

| Program | Nodes | Groups | Node kinds | Cutting | Travel | What the optimizer gained |
|---|---|---|---|---|---|---|
| `F_Cu` isolation | 594 | 1 | all `Closed` | 34,425 mm | 959 mm | 1033 → 1011 mm rapid, **2 %** |
| `Edge_Cuts` outline | 51 | 2, of [50, 1] | all `Fixed` | 5,863 mm | 785 mm | 775 → 775 mm, **exactly none** |

The outline's ordering line is not printed in the summary because the gain is under the half a
percent that makes it worth showing. It is not under it. It is zero.

**The grouping is right and is not the problem.** Fifty boards in one group and the frame alone in
the second is precedence doing its job: the frame has to come last or the pieces are loose while it
is cut.

**The problem is that every one of the fifty-one profiles is a `Fixed` node**, and a `Fixed` node
has `OptionCount == 1`. So `TryFlip` — which its own comment calls "the move that matters most and
it is nearly free" — returns false fifty-one times out of fifty-one. On the isolation, where every
node is `Closed` and carries its entry vertices, that same move is doing most of the work. The
optimizer is not failing on the outline; it is being handed a problem with no freedom in it and
correctly reporting that the order it was given is the best one available.

**Why they are `Fixed`, which is the part worth knowing.** The summary says it: *"50 of them enclose
nothing and are cut from the inside — slots, windows, or the channels between the boards of a
panel"*. A channel is cut as a centreline, so `closed` is false, so each depth alternates direction
— forward, then backward — which is deliberate and right, because an open run taken the same way
round twice costs its whole length in travel before the second pass can start. But alternating makes
the stack's passes neither all closed nor all the same contour, so `ToolpathRouter.NodeFor` falls
through to `ForFixed`.

**And that stack is reversible, for free.** Nothing stops it beginning with the backward pass: it
would enter from the other end and alternate identically, ending where the forward-first version
began. It has two entries and is being modelled as having one. Restoring that is not a new
optimisation — it is giving fifty of fifty-one profiles back a choice they always had.

**So the second half of this story is not mainly the merging.** Merging paths that share endpoints
is still worth doing and O6 still stands, but the outline's 785 mm is first of all an entry-choice
problem, and that is cheaper to fix and measurable on the same board.

**And the first half got more urgent.** `Flip` is the identity for a `Fixed` node while `EntryFor`
and `ExitFor` always answer `Start` and `End`, so `TryTwoOpt` reversing a span that contains one
produces an order the tool cannot physically take, costed as though it could — its own comment
claims "the interior edges survive reversal at exactly the same cost", which holds for `Open` and
`Closed` nodes and not for these. It applied no moves on this board, so nothing here is mis-cut
today; that is luck rather than safety, and it is a correctness fault rather than a missed saving.

**Done when, restated by the measurement.** The clause above stands, and gains two: an alternating
open stack offers both of its ends, and the outline's travel on the panel falls against the 775 mm
recorded here.

### Result, 2026-09-22

**Both halves landed, and the second one is where the number is.**

*The cycling was two-opt mis-costing a reversal.* Reversing a span leaves the interior unevaluated
on the grounds that reversal is cost-neutral, which holds for a closed contour (entry and exit
coincide) and for an open run (its flip swaps its ends) and is false for a fixed stack, whose flip
is the identity while its entry and exit are different points: `d(End(k), Start(k+1))` silently
becomes `d(End(k+1), Start(k))`. Reproduced on 25 fixed stacks in `RouteMonotonicityTests` —
**16,057 applied "improvements" at Balanced and 321,413 at Thorough, ending on two different
routes.** With the move refused where it cannot be evaluated: **15, and both efforts agree.**
`TryOrOpt` reverses runs of two and three on the same assumption and now carries the same guard;
reversing a run of one is only a flip and every kind evaluates that correctly.

*Then the stacks got their second entry back.* `RouteKind.Stack` keeps a stack's pass order — shallow
still before deep — while allowing every pass in it to be flipped, which enters from the far end.
Its reversed ends are stored rather than derived, because they are not its own ends swapped: with an
even number of alternating passes both configurations are a there-and-back, a shape neither `Open`
nor `Closed` can express.

**Measured at the bench by the product owner**, on `GridStripConnector_Panelized`, cutting the
outline with the 0.8 mm end mill at his own 0.500 mm step-down:

| | V0.1.5 | Now |
|---|---|---|
| `F_Cu` isolation | 959 mm | 959 mm |
| `Edge_Cuts` outline | 785 mm | **718 mm** |
| **Whole export** | **1744 mm** | **1677 mm** |

*"Cut length and plunges remain identical."* **67 mm, or 4 % of the export**, all of it in the
outline.

**The two tables below are not the same experiment, and their millimetres do not line up to the
unit.** The bench figures above come from the product owner's own machine settings and his own tool
library; the ones below are a controlled sweep with the shipped `ToolLibrary.Default`, its end mills
forced to each step-down in turn, so that only the step-down varies. At 0.50 mm the sweep reads
721 mm where the bench reads 718 mm, and that three-millimetre gap is the two libraries differing,
not a discrepancy to resolve. The sweep is what the parity claim rests on; the bench is what the
story is worth.

Within the sweep, the second entry accounts for **755 → 721 mm** — that is the one comparison
actually run, with the stack's second configuration withdrawn and everything else left alone. How
much of the remainder belongs to refusing the mis-costed moves was not measured at this step-down,
and is not claimed here.

**That is the number this story is worth, and it is the one to quote.** An earlier draft of this
block led with 82 %, which is a real measurement of a configuration nobody here cuts — and putting
it first made the story sound like something it is not.

**Where 82 % comes from is worth knowing, because it is not the step-down.** It is the *parity* of
the pass count:

| Step-down | Passes to 1.90 mm | Outline travel |
|---|---|---|
| 0.40 mm | 5 — odd | **337 mm** |
| 0.65 mm | 3 — odd | **326 mm** |
| 0.70 mm | 3 — odd | **326 mm** |
| 0.50 mm | 4 — even | 721 mm |
| 1.00 mm | 2 — even | 710 mm |

An even number of alternating passes brings the tool back to where the stack started, so both of its
configurations are a there-and-back and the choice only decides which end it waits at. An odd number
traverses end to end, so the optimizer can run one channel into the next. **The outline travels more
than twice as far for an even pass count as for an odd one**, on the same board with the same
cutter, and that is a property of how the stack is built rather than anything the operator did.

**Left open by this, and not chased here.** An even stack could in principle be given the same
freedom — what it needs is a way to finish at the far end without paying a full-length return, which
is a question about how the passes are laid out rather than about how they are ordered. Worth a
story of its own, with the table above as its starting figure. Nothing about the current behaviour
is wrong; it is a saving not taken.

**The gain is concentrated where the choice exists**, and that is worth saying plainly.
`Millburn_Test_Board`, `GridStripConnector` and `Arduino_Mega_2560` are byte-identical before and
after — 136 mm, 60 mm and 335 mm of outline travel respectively, unchanged. One outer profile and a
handful of cutouts give the optimizer almost nothing to choose between. Fifty channels do.

**O6, the Eulerian merging, was measured and closed rather than built.** It is half of what this
story named, so not doing it needs more than a shrug. Reading the emitted programs and asking which
cut runs actually meet: the outline has 106 runs over 109 distinct endpoints and **no point at all
where two different paths meet** — every shared endpoint is one channel sharing with itself at
another depth. The isolation has 198 runs and 396 endpoints, none repeated. There is nothing to
merge, and a structural reason why: separate voids' centrelines do not touch, and a closed contour's
two ends are the same point.

**What is wasting the lifts is vertical, and it already has a story.** 51 of the outline's 105 lifts
go straight back down where they left, which is the retract and plunge between a channel's two depth
passes, and the program's vertical motion is now 735.4 mm against 718 mm of horizontal travel. That
is story 6, and this measurement makes it the more valuable of the two.

**Reopening it needs a board, not an argument.** If somebody asks for merging and brings the
Gerbers, measure them first: the giveaway is a distinct endpoint count well below twice the run
count, with the sharing between different paths rather than within a stack. [03
§7.2](../Documentation/03-Toolpath-Optimization.md) carries the numbers and the method.

**Requirements:** O10 met · O6 closed as not needed, reopenable on evidence ·
[03 §7.2, §8](../Documentation/03-Toolpath-Optimization.md)

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

### Closed — 2026-09-22

**Met on both halves, and the clause's own numbers had to be restated first.**

6.24 recorded 39.40 mm of rapid up, 16.50 down and 20.90 of plunging — about 59 s — on four laps of
the test board's outline. That does not reproduce: the depth structure has changed since it was
written, and the same board today cuts in three laps. So the baseline was measured again before
anything was touched.

| Test board outline | Before | After |
|---|---|---|
| Rapid up | 23.70 mm | 14.70 mm |
| Rapid down | 9.00 mm | 3.00 mm |
| Plunging | 12.70 mm | 9.70 mm |
| **Vertical, total** | **45.40 mm · 34.9 s** | **27.40 mm · 22.3 s** |

at the workshop's rates — 100 mm/min in Z, 50 plunging. **A 36 % cut in vertical distance and
12.6 s off a two-minute program.** The clause asked for under fifteen seconds against fifty-nine;
against a baseline of 34.9 s that is not the same target, and 22.3 s is what the two changes are
worth. Claiming the old number would be claiming a saving against a program this application no
longer emits.

**The panel is where the size of it shows**, because it has fifty-one profiles rather than one:

| `GridStripConnector_Panelized` outline | Before | After |
|---|---|---|
| Rapid moves | 797 | **182** |
| Plunges | 530 | **319** |
| Feed moves | 4,725 | 4,725 |
| Arc moves | 378 | 378 |
| Estimated run | 57m 44s – 1h 0m | **49m 29s – 51m 44s** |

**About eight minutes, and the cut is identical to the move** — the feed and arc counts do not
change, because nothing about what is cut has changed. Only the getting there.

**Between laps.** `PassLinker.Continues` now allows a pass that begins where the last one ended and
goes deeper, with no ramp: the emitter drops straight down at the plunge feed. The condition that
matters is unchanged and is the whole of the safety here — the two passes must **meet at a point**.
Anything else is a journey, and a journey at depth through uncut material is the gouge the lift
exists to prevent.

**Over a tab, the hop goes to the surface rather than to the tab's own top**, which is the product
owner's call and a better trade than the one this started with. Clearing the tab's top would save
another six tenths of a millimetre of Z, and it puts a rapid in-plane move below zero — which
`GcodeBackplot.RoleOf` calls a gouge without qualification, and the window answers with "do not run
this". That check earns its keep on every other program here, and an exception to it that no reader
of the file could tell from the real thing is not worth 0.6 mm. Coming up to zero keeps the crossing
a rapid, costs nothing in cut length, and clears any tab there can be.

**One test asserted the opposite of this story**, and its reasoning said why:
*"a linked pass is written without a plunge"*. That was a true statement about the emitter, and it
is the statement 6.24 changes. `ALapStartingDeeperThanTheLastEndedDropsStraightToIt` now asserts the
drop, and reads the emitted program to confirm there is no retract between the two laps — the flag
is a request, and the text is what the machine is given.

**One limitation accepted, not fixed.** The hop clears the surface by half a millimetre, and
levelling adds the probed correction to every move including rapids — so a board that falls more than
that below the datum along a tab gap gets a hop written below zero, which the gouge check then
condemns. It is a class of failure this story created: before it, the only move at approach height
was vertical, and a vertical move is never a gouge. Accepted on the product owner's reading of the
number — half a millimetre of fall is a whole millimetre of range once the rise is counted, which is
a workholding problem before it is a levelling one — and on the fact that the file is refused rather
than quietly run. Recorded with its three possible fixes as
[6.38](../Documentation/06-Roadmap-and-Risks.md).

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
