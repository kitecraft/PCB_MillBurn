# Sprint 3 — Checks you can read

**Agreed** 2026-09-28, after v0.3.0. **Release branch:** `release/0.4.0`, branched from `main`.

**The product owner's direction:** [6.44](../Documentation/06-Roadmap-and-Risks.md#644-the-checks-become-something-you-can-read--enhancement--open--agreed-by-the-product-owner-2026-09-23),
and [6.47](../Documentation/06-Roadmap-and-Risks.md#647-isolation-cuts-inside-a-hole-that-is-about-to-be-drilled--defect--open--found-in-the-workshop-low-priority-a-later-sprint)
brought in at the end. Everything else that was on the list for this sprint goes to the next one.

**This is the sprint sprint 2 said it would be.** Its closing note called 6.44 *"the obvious
candidate to be sprint 3 on its own"*, and left it out because *"putting it beside nine other items
is how a sprint ends with everything half done"*. A first draft of this document had it beside eight
other items. The product owner cut them on the day it was written.

So the item is the sprint, and **its parts are the stories.** 6.44 already absorbs four roadmap
entries — 6.36, 6.37, 6.42 and 6.43 — plus two faults with no entry of their own, and those six
things are what stories 1 to 6 below are. None of them is to be picked up separately; that is what
folding them meant.

**And 6.47 at the end**, brought in on the same day after being held twice. It is not part of 6.44
and does not pretend to be: it is the one open defect that can be started, it is small beside the
six, and a sprint with room for it should not leave it waiting a third time. It goes last because
nothing in it depends on the checks and nothing in the checks depends on it — which makes it the
one story here that can be dropped without stranding another, if the six turn out to be as large as
sprint 2 thought they were.

---

## What is actually wrong

**A check is a bare string.** `MainViewModel.Warnings` is an `ObservableCollection<string>`
(`MainViewModel.cs:130`). There is no source, no kind, no severity beyond a single `bool IsError`
buried in the parser diagnostics, and **no identity**. Provenance is whatever the sentence happens
to begin with: some lead with a source (`Stock: …`, `Not levelled: …`), most do not.

Every one of the five requests folded in here is a consequence. You cannot colour a label that does
not exist, cannot fold a list into a count of things it cannot categorise, cannot link a status
message to a line it cannot name, and cannot give a check a view of its own when the check is a
sentence.

**The clearest proof that the shape is wrong is already in the code, with a comment explaining
it.** `ReplaceExportWarnings` (`MainViewModel.cs:2350`) keeps `_exportWarnings`, a second parallel
`List<string>`, for one reason: to know which lines in the panel were its own so it can take them
out again. It does that with `Warnings.Remove(stale)` — **removal by string equality**. A whole
shadow list exists because the item has no field saying where it came from, and the removal it
performs is ambiguous the moment two checks read the same. That is not a tidiness argument; it is a
data model asking to be given one field and then not needing any of it.

**Forty-four producer sites**, across four files: `MainViewModel.cs` (12), `ExportPlanner.cs`,
`MachineCheck.cs` and `TestCut.cs`. That is the size of the migration and the reason this is a
sprint rather than a story.

### What sprint 1's story 4 did to it

Seventeen CHECK items before that story, in the workshop screenshot taken for its closure; a dense
board now adds eleven more. The warnings had to be capped at **eight groups and six names each** —
`NetJoin.Describe(int most = 6)` carries the cap and its doc comment carries the reason: one piece
of copper holding twenty-three nets became a six-hundred-character sentence.

**The format is straining, and the answer is not a longer sentence.**

### The two faults with no entry of their own

Both found by review while story 4 was being built, both recorded rather than bundled into a story
that was about something else.

- **The electrical check does not run until you preview or export.** `RefreshWarnings`
  (`MainViewModel.cs:2754`) runs on load and covers board-level things only; the isolation warnings
  arrive through `PreviewResult.Warnings`. Open a board with a short on it, look at the CHECK panel,
  and it is not there. 06 calls this *"closer to a bug than to an enhancement"*, and it is the
  strongest argument for 6.43's button.
- **Two planning paths disagree.** `ExportPlanner.cs:1788` calls `ElectricalCheck.Isolation` and
  names the nets. `JobBuilder.cs:129` — behind the CLI's `job` command — calls only
  `IsolationOperation.UnreachableGaps` and emits *"2 gap(s) are narrower than the cut"*. **The same
  board, the same settings, two commands, two different answers.** Confirmed by running both, and
  confirmed again by reading both call sites on 2026-09-28.

---

## How a story closes

As in [sprint 1](Sprint-01-Speed-and-Accuracy.md) and
[sprint 2](Sprint-02-Fix-What-Is-Broken.md): every story ends with a `Closed` block carrying the
date and a plain verdict against *Done when*, the manual test in enough detail that somebody else
could run a similar one, what was observed including the sentence from the bench, and what is left
open. A story without one is still open whatever its branch says.

**Sprint 2's addition stands:** a defect closes with the test that would have caught it, named in
the closure. Stories 2, 3 and 7 are defects.

**And one for this sprint, because of what it is about.** This sprint's entire output is a surface
somebody reads. A closure that only says a test passed has not shown the thing, so **every story
that changes what the panel looks like closes with a picture** — `--shot`, on a board with real
findings on it. The Arduino Mega is the board with the short; the test board is the one with 25
unnamed merges. Describing a layout in prose when the tool to show it takes one command is not
good enough here.

---

## Story 1 — A check stops being a string

**Problem.** Everything in *What is actually wrong*, above. This is the story the other five stand
on, and none of them can start before it lands.

**What.** A check becomes a thing with a source, a kind and a severity, and every one of the
forty-four producer sites makes one.

**Why.** Because five separate requests from the product owner and the workshop are all the same
request, and answering them one at a time on top of `ObservableCollection<string>` means five
workarounds instead of one model. 6.44 was agreed on that basis on 2026-09-23.

**How.**

- **Source, kind, severity, identity.** The source is the thing the check is about — a layer, the
  stock, the export, the tool library. The kind is what sort of check it is. The severity is at
  least *refusal* against *advice*, because telling those apart without reading to the end of the
  sentence is 6.36's own *Done when*.
- **A layer source carries the layer**, not a string naming it, because 6.36's better half is the
  colour: *"might be nice to put that label in the layer's colour"*, and the layer rows are already
  coloured. A check that carries its layer gets the colour for free; one that carries a layer's
  *name* has to look it up and can fail to.
- **`_exportWarnings` goes away.** With a source on the item, replacing this export's refusals is a
  filter rather than a shadow list and a string comparison. If it does not go away, the model is
  not carrying enough.
- **The sentence stops beginning with its source.** Lines that already say `Stock: …` say it
  because there was nowhere else to put it. Once there is, the prefix is duplication — and worse,
  duplication that can disagree with the field beside it.

**Where this gets dangerous, and the rule for it.** Forty-four sites is enough that a mechanical
migration will be tempting, and the sites are not interchangeable: `TestCut.cs` and
`MachineCheck.cs` produce advice about a coupon, `ExportPlanner.cs` produces refusals about files
somebody is about to run. **Anything whose severity is not obvious from its call site gets read and
decided, not defaulted.** A refusal quietly demoted to advice by a default is exactly the kind of
fault this sprint exists to make impossible.

**Done when** a check carries its source, its kind and its severity rather than beginning with them
by convention; all forty-four sites produce one; `_exportWarnings` and its string-equality removal
are gone; the panel renders the same content it does today from the new model, proved by a `--shot`
against one taken before the story; and nothing in the emitted files changed, proved by the golden
baselines.

**Not in it.** Anything that changes what the panel *looks* like. This story ends with the same
list on screen and a model underneath it, and that is deliberate: a migration of forty-four sites
and a redesign of the surface in one story is two ways for it to go wrong at once.

---

## Story 2 — Both planning paths answer the same

**Problem.** `ExportPlanner.cs:1788` runs `ElectricalCheck.Isolation` and names the nets.
`JobBuilder.cs:129` runs `IsolationOperation.UnreachableGaps` and emits a count. An operator
verifying a board from the CLI's `job` command gets *"2 gap(s) are narrower than the cut"* where
the export path would tell them which nets are shorted together.

**What.** One answer for one board.

**Why.** It is the half of 6.42 that is straightforwardly a defect. The application's most valuable
check — 02 §5 calls electrical verification *"the single highest-value check in the product"* —
reaches one of the two ways into the pipeline, and which one you get depends on which command you
typed.

**How.** *"Both paths run the same check, or there is one path"*, in 6.42's own words, and the
second half of that is worth taking seriously before the first. Two planning paths that must agree
are a thing to keep agreeing forever; if `JobBuilder` can call what `ExportPlanner` calls, this is
small, and if the two have diverged far enough that it cannot, **that** is the finding and it goes
in 06 before any code is written.

**Done when** the same board checked through the window and through `millburn-cli job` reports the
same nets and the same counts, on the Arduino Mega — the board with the real short — and on the
test board; a test runs both paths over one board and asserts they agree; and the closure names the
test that would have caught it.

---

## Story 3 — An unnamed gap says which kind it is

**Problem.** `NetCheck.Unnamed` counts merges no pair of names could be put to, and **two quite
different things land in it**: copper carrying no net attribute, which is a short nobody can name;
and two pieces of the *same* net being joined, which is a gap the tool equally cannot cut and
electrically nothing at all, because they were one conductor already.

The warning is worded to allow for both, which is honest and is not the same as useful. **An
operator reading "25 gaps" on the test board cannot tell how many matter.**

**What.** Split the residual into the two things it is.

**Why.** 25 is the real number on the test board, and it is worth repeating why, because a review
once asserted the board had none: the test board carries copper with no net on it — the lettering,
and the 0.5/0.8/1.0 test patterns — which fuses at a wide cut and has no name to be reported under.
The 62 the first arithmetic reported were wrong; the 25 that replaced them are real. **How many of
them are interesting is exactly what this story is about.**

**How.** Ask, per merged region, whether the pieces that fell into it carry one name between them
or none. **The per-region attribution that 6.41's fix introduced is most of the machinery already**,
so this is a question asked of data that exists rather than a new pass over geometry.

**Done when** the check reports nameless copper separately from same-net rejoins; the test board's
25 is broken down into the two and the breakdown is checked by hand against what is actually on the
board; a test pins both counts; and the closure names the test that would have caught it.

---

## Story 4 — A board can be checked without exporting it

**Problem.** `RefreshWarnings` runs on load and covers board-level things only. The isolation
warnings — the electrical ones, the valuable ones — arrive through `PreviewResult.Warnings`, which
means they arrive when the operator previews or exports. **Open a board with a short on it, look at
the CHECK panel, and it is not there.**

**What.** A way to check a board as its own job, from a button or the Job menu.

**Why, and what not to do with it.** 6.43 asked whether the check would have been better as
something the operator starts rather than as warnings produced while planning, and **the answer is
both, with the automatic half staying.** The short on the Arduino Mega was found because the app
said so without being asked. A button only protects an operator who thinks to press it, and the one
most at risk is the one who does not know there is a question. It also runs before anything is
written, which is what sprint 1's story 4 asked for: **a gate, not a report.**

So this story adds a way to ask, and removes nothing.

**How.** The check runs on demand over the board as it currently stands, without planning an
export. Worth watching what it costs on the six-layer i.MX8M board, where a preview takes seven to
eight seconds and sixteen at 1 mm isolation — if checking is most of that, the button needs the
same cancellation the preview got in sprint 2 rather than a frozen window, and sprint 2's story 3
is the pattern.

**Done when** a board with a short shows it in the CHECK panel without the operator previewing or
exporting anything; the automatic check still runs on load and on preview as it does today; the
button reports how long it took and can be superseded rather than freezing the window; and the
closure carries a `--shot` of the Mega showing its short, taken without an export.

---

## Story 5 — The list can be read at a glance

**Problem.** From the product owner, 2026-09-22, twice in one sitting: *"The CHECK section: Each
item should list its source first, then the message. ie: Stock - `<message>` or Top copper -
`<message>`. Also, might be nice to put that label in the layer's colour."* And: *"The CHECK section
should be minimizable (downwards) leaving just the title and the count visible."*

**And the case that raised the first one.** The export's status line said *"1 thing(s) refused — see
the checks"* while the check itself read *"Not levelled: Millburn_Test_Board-B_Cu.nc — …"*. The word
"refused" appeared nowhere in the list, so **there was nothing to look for**. From the bench: *"I
don't know which check it is referring to."* The status line was changed on the spot to name what
was refused, and the bench's verdict on that was that it is not enough: *"it's there, but the link
from status message to check is not really clear to the user."* Naming the thing still leaves the
reader matching one sentence against a list of sentences.

Screenshot: `WorkingFolder/V0.2.0/Story 6 - Stop Climbing Outline/Screenshots/refused.png`.

**What.** Make the panel readable: source first, in the layer's colour; refusal distinguishable
from advice; a status message that points at a check the reader can find; and the whole section
folds to its title and count.

**Why.** It is a list you consult while the mill is running.

**How.**

- **Source first, in the layer's colour**, which story 1 made possible by putting the layer on the
  check rather than its name in the sentence.
- **A refusal looks different from advice** without reading to the end of the sentence. 6.36's
  *Done when* asks for exactly this and does not say how; a mark, a colour and a word are all
  candidates and the one that survives a `--shot` at working size wins.
- **The status-to-check link.** 06 offers three shapes — the same word in both, the line marked as
  a refusal rather than as advice, or the check itself highlighted when the message is about it —
  and with identity on the item from story 1, the third is now cheap and is the only one that
  cannot be defeated by two checks reading similarly.
- **Folds downwards**, so the board keeps the room. **The count stays visible**: a job with three
  checks and a job with none must not look the same folded.
- **Two things to decide, and 06 says neither is obvious:** whether the fold is remembered, and
  whether a new check unfolds it. A panel that reopens itself gets in the way once an operator has
  read the checks and decided they are fine; a fold that hides a check which arrived afterwards is
  the opposite failure. **The count in the title is what makes leaving it folded defensible**, so
  the decision should be made with the count's legibility in front of you and recorded in the
  closure either way.

**Done when** every check line begins with the thing it is about, a layer's own checks carry that
layer's colour, a reader can tell a refusal from advice without reading to the end of the sentence,
a status message that points at the checks points at one a reader can find without hunting, the
section folds to its title and count with the count legible folded, and the closure carries
`--shot`s folded and unfolded on a board with both refusals and advice on it.

---

## Story 6 — The findings get a place of their own

**Problem.** Three ways the warning channel is straining, all visible today:

*The format caps what can be said.* Eight groups, six names each, because of the
six-hundred-character sentence.

*The findings are scattered.* Each isolation program reports its own, so a two-sided board answers
in two places in the export listing and nothing collects them.

*And it dilutes the list.* Seventeen items before sprint 1's story 4; eleven more on a dense board
now.

**What.** A view where the checks are read, and an automatic list that gets *shorter* and points at
it.

**Why.** This is where the expensive answers can live — the ones sprint 1's story 4 deliberately
declined as too much for a warning line, and which are perfectly justifiable for something the
operator asked to run:

- **Where the gap actually is.** `NetJoin.Near` is one of the group's own net points and its doc
  comment is explicit that it can be tens of millimetres from the narrow place: *"enough to select
  the right piece of copper and not enough to point at the fault"*, so a view built on it says
  *"this copper"* and not *"here"*. Finding the gap itself means intersecting the two nets' grown
  outlines and taking a point in the overlap — a second offset per join, unjustifiable for a
  one-liner and reasonable here.
- **Story 3's split**, shown rather than counted.
- **Reasoning across layers**, which per-program warnings cannot do at all.

**How.** The automatic check stays and gets shorter — **one line per layer, naming the count and
pointing at the view.** That addresses the dilution and the format at once, rather than trading one
for the other, and it is why this story comes after story 5 rather than before it: story 5 makes
the list readable, story 6 makes it short and gives it somewhere to point.

**Not in it, and this is the line to hold.** The rest of DRC — drill-to-copper clearance, an outline
cut that severs a trace, a pocket that removes part of a net. They have nowhere to live today and
this view is where they will live, which makes it tempting to add one while the view is open. They
are a sprint of their own and each needs its own argument about when it can fire.

**And the severed-net family stays out entirely** until the operation that can sever is the one
being checked. Isolation cuts outside the copper edge and **cannot** sever anything; sprint 1's
story 4 refused an opens check for that reason. **A check that can never fire is the failure this
repository has been bitten by twice**, and a view with room for more checks is exactly the
condition under which somebody adds one.

**Done when** there is a view that collects every layer's electrical findings in one place, it
names where a gap is rather than only which copper it is in, it shows story 3's split, the CHECK
list is down to one line per layer pointing at it, and the closure carries a `--shot` of the view
on the Mega and on the test board.

---

## Story 7 — No copper program cuts inside a hole it is about to drill

**Problem.** From the bench, on the Arduino Mega 2560, confirmed in v0.2.0: *"there are two
non-plated holes that get cut lines inside the hole. Left edge, just above center, a very shot
distance in from the edge."*

**Deferred twice before it was taken.** By the product owner on 2026-09-24, the day it was reported
and measured, as *"low priority, a later sprint"*; held again on 2026-09-28 when this sprint was cut
back to 6.44 alone; and brought in the same day at the end. Recorded because a defect that gets put
off repeatedly is how a known fault survives three releases, and 06 says so in as many words.

**What.** Stop the isolation cutting inside a hole the same export is going to drill.

**Why.** Not because it is dangerous — it is not, and 06 says so at length. The run order is isolate
then drill, so at isolation time the hole is solid copper-clad and the cutter is cutting material
rather than air. A workflow that drilled first would be plunging a V-bit into an open hole, which is
worth knowing if the order ever becomes a choice, and today it is not.

It is a defect for two other reasons. It is time spent removing material the next operation removes
anyway. And it is time spent **looking wrong**: an operator watching the cutter track through a hole
has no way to tell that from a fault, and the whole value of the backplot is that it can be trusted
at a glance.

**Which is why it belongs in this sprint rather than merely near it.** Six stories above are about
the application saying what it found in a form somebody can act on. This one is about the
application not doing something indefensible for a defensible reason — the same argument 6.30 was
closed on. A check panel that can be read and a backplot that can be believed are the same project.

**It is already measured**, so the story starts from arithmetic rather than a hunt. Both holes are
the only two entries in the NPTH file: 0.65 mm, at board (6.568, 41.130) and (6.568, 35.350),
exactly where the bench said. The top copper program makes **17 cutting moves inside the first and
18 inside the second**, the nearest 138 µm from a centre whose hole radius is 325 µm. Sixty-one
samples across each disc found **no copper in either hole**, so nothing is being isolated in there.

**And the isolation is arithmetically right**, which is the part to keep hold of while fixing it:

| | |
|---|---|
| Hole radius | 325 µm |
| Nearest copper to the centre | 477 µm — the copper edge stands 152 µm clear of the hole |
| Moat asked for, and achieved | 0.400 mm, 0.415 mm |

A 0.415 mm moat swept inward from copper 477 µm out reaches to 62 µm from the centre. **The passes
inside the hole are the last laps of a moat around copper that really is there.** Nothing is
miscomputed; the moat is simply wider than the gap between the copper and the hole, and the hole is
not a thing the isolation knows about.

**How.** Subtract the known hole footprints from the region the isolation is allowed to cut, so a
pass stops at the hole's edge rather than crossing it. The drill layers are already loaded and their
positions and diameters are already known — `BoardLayer.Drill` carries them — so this is a clip and
not a new measurement.

**The question to answer before writing it**, named in 06 and repeated here because it is the whole
design: **what happens to a pass that a hole would cut in two.** Two passes and an extra lift, or
one pass that dips through — and the second is what happens today. A lift is not free on a machine
doing 100 mm/min in Z against 2000 in XY, and [6.3](../Documentation/06-Roadmap-and-Risks.md)
measured what one costs. So this is a trade with a number on each side, and it gets made with the
numbers rather than on principle.

**Watch what it does to the moat that is reported.** `AchievedWidthNm` tells the operator what they
really got. If clipping against holes means a pass no longer clears what that number says in the
neighbourhood of a hole, **that has to be said rather than quietly become untrue** — the same rule
that put the achieved width there in the first place. Story 1's model is the place to say it, which
is the one thread between this story and the six before it.

**Not in it: the check that would have found it.** Drill-to-copper clearance is one of the three DRC
checks story 6 deliberately leaves out, and this story fixes the geometry rather than adding the
check. Worth saying plainly so the two are not confused later: after this, no program should cut
inside a hole, and nothing yet *verifies* that on a board nobody has looked at.

**Done when** no copper program cuts inside a hole the same export is going to drill, on the Mega
and on the test board; a test covers both Mega holes by position; the travel and line counts are
quoted before and after, from `millburn-cli export --write`; the closure says what was decided about
a pass cut in two and why; and the closure names the test that would have caught it.

---

## Held for sprint 4

**Everything else the product owner named on 2026-09-28**, cut from this sprint on the day it was
written. All seven already have full entries in 06 — the reading behind them is there and does not
need repeating here. What is recorded below is only what was worked out on the day and is not in
06: which matrix rows are which entries, the order, and where each one can honestly be cut short.

**Three of them arrived as matrix rows, and all three are 06 entries**, which is worth writing down
once so nobody works from two numbers for one thing:

| Matrix row | Roadmap entry |
|---|---|
| G20 — a numbered picture of the holes and slots on each companion page | [6.5](../Documentation/06-Roadmap-and-Risks.md#65-a-picture-on-the-companion-pages--enhancement--open) |
| G21 — tool library: filter by kind, sort, copy a tool | [6.6](../Documentation/06-Roadmap-and-Risks.md#66-the-tool-library-once-it-has-more-than-a-handful-in-it--enhancement--open) |
| G23 — the project page lists the CLI commands that would rebuild the export | [6.15](../Documentation/06-Roadmap-and-Risks.md#615-the-companion-page-names-the-commands-that-would-rebuild-it--enhancement--open) |

**G23 and 6.15 are the same item**, asked for twice in the same sentence, so the held list is seven
items and not eight. The matrix rows close when their 06 entries do.

**The order worked out on the day**, by what an emitted file pays for and then by where the work
lands:

| | # | Note |
|---|---|---|
| 1 | [6.4](../Documentation/06-Roadmap-and-Risks.md#64-tabs-where-how-many-how-big--enhancement--open) | Carries a false statement in an emitted program — see below |
| 2 | [6.5](../Documentation/06-Roadmap-and-Risks.md#65-a-picture-on-the-companion-pages--enhancement--open) · G20 | Companion page; third caller of `SvgWriter`, not a third implementation |
| 3 | [6.15](../Documentation/06-Roadmap-and-Risks.md#615-the-companion-page-names-the-commands-that-would-rebuild-it--enhancement--open) · G23 | Companion page; derived from the emitted plan, never from the settings |
| 4 | [6.52](../Documentation/06-Roadmap-and-Risks.md#652-a-test-cut-that-finds-where-the-copper-stops--enhancement--open--asked-for-by-the-product-owner) | Its guide page wants the builder items 2 and 3 will have been in; design questions already answered 2026-09-27 |
| 5 | [6.32](../Documentation/06-Roadmap-and-Risks.md#632-say-where-the-project-came-from--enhancement--open--asked-for-by-the-product-owner) | The smallest item on the list |
| 6 | [6.6](../Documentation/06-Roadmap-and-Risks.md#66-the-tool-library-once-it-has-more-than-a-handful-in-it--enhancement--open) · G21 | |
| 7 | [6.1](../Documentation/06-Roadmap-and-Risks.md#61-rulers--enhancement--open) | |

**6.4 carries something closer to a bug than an enhancement, and it should not wait unnoticed
inside a feature.** A pass counts as "tabbed" once it is deeper than
`total − TabHeightNm − BreakThroughNm`, and a tabbed pass jumps the tab entirely — nothing ever cuts
the tab region down to the tab's top. So when *every* pass is tabbed, the tab is left the full
thickness of the board. Measured on the test board: 0.8 mm board, 0.5 mm stepdown, 0.1 mm through,
threshold 0.3 mm, both passes jump the tab. **The program says `0.50 mm of material left under
each`; what is left is 0.8 mm.** Same rule in `BlankOperation` and `OutlineOperation`. That is an
emitted number somebody acts on with a knife, wrong by 60%, and it is a candidate to be pulled
forward on its own rather than to wait for the tab work around it.

**Three of the seven are lists of four sub-items, already in priority order in 06 by whoever wrote
the entry.** Each has an honest first slice, so a sprint 4 that runs long has somewhere to stop
that is not halfway through a story:

| # | Worth having on its own | Can wait |
|---|---|---|
| 6.1 | The ruler itself, both viewports, plus the cursor readout | The measuring drag |
| 6.4 | The true tab-height comment, then placement by edge | Manual placement, keep-out from features |
| 6.6 | The scannable row — 06 says *"build the row first and see what is left to want"* | Copy, sort, filter, in that order |

**6.47 is no longer here.** It was held with the rest for part of a day and then brought into this
sprint as story 7, which is the third decision made about it in five days and the one that ends the
run. The reasoning is in the story.

---

## Deliberately out

**6.45 — block apertures and the aperture transforms.** The other open defect and still the one
that cannot be started. `%AB%` and the `%LM/LR/LS%` transforms are refused outright, and **no export
in `tests/boards` uses either**, because KiCad emits neither. Confirmed again on 2026-09-28. The
product owner's position: *blocked until someone can provide additional info or an effective test
case*. Writing a fixture from the Ucamco specification remains possible and remains the trap this
project has twice been caught by — a feature proved only by a fixture its own author wrote. It waits
for a real board that needs it, which is also the only evidence that anyone does.

**G8 — Voronoi-based isolation.** Raised on 2026-09-28, surveyed and declined the same day, so the
reasoning is recorded rather than left to be re-derived. It cuts down the medial axis of the gap
between two nets instead of at a fixed offset from each, which splits the clearance evenly and
retains more copper. What it costs is the number the operator sets: a Voronoi cut has no stated
width, and `AchievedWidthNm` exists precisely because a number somebody acts on gets reported. It
would also leave a hairline separation of the kind `IsolationOptions` already warns about —
*"electrically fine, and a hairline you cannot see, cannot solder across without bridging, and can
close by handling the board"* — which is worse, not better, for the paint-resist workflow. The
capped-and-blended version in [02 §5.4](../Documentation/02-Gerber-and-Geometry-Pipeline.md) is the
one worth building if it is ever built, and the rest-machining idea in 02 §5.3 answers the same
problem without damaging copper, so neither should be started before both are measured. NTS is
already a dependency for this and nothing in `src/` calls `VoronoiDiagramBuilder`.

**6.50 — nothing can test the view model.** Out, and it is the closest call in this document.
Stories 1 and 5 work almost entirely inside `MainViewModel`, the largest file in the repository,
and story 1 alone migrates twelve producer sites in it and deletes a shadow list — which is
precisely the code 6.50 says nothing can reach. Sprint 2 paid three times for that gap and a fourth
time when a tested debounce sat behind broken wiring.

It is out because pulling a seam through the file *while* rewriting what flows through it is two
refactors interleaved, and because 6.50's own entry says it *"belongs in a sprint that has room for
it"* — and the room here is smaller than the story count suggests, since six of the seven stories
depend on each other in order and only the seventh stands alone.

**But the decision should be revisited at story 1's close, deliberately and in writing.** By then
the new model exists and the question is concrete rather than theoretical: if the checks have
become the most testable thing in the view model and nothing can reach them, that is the argument
for 6.50 next, and the closure is where to say so. [6.51](../Documentation/06-Roadmap-and-Risks.md#651-there-is-no-single-place-that-says-this-picture-is-no-longer-true--enhancement--open--six-paths-and-counting)
travels with it.

**6.2, 6.16 and 6.46**, all parked or low priority with reasons, and none of the reasons has
changed.

**Anything that is not 6.44 or 6.47.** Sprint 2 held this line once, on 2026-09-27, and it held.
This sprint is two roadmap items and the second was added deliberately, with a reason, on the day
the sprint was agreed — which is the bar. The temptation here will not be a new feature but a small
fix noticed while reading forty-four call sites. Those go into 06 as they are found, with a note
saying which story found them.

---

## Still true from sprint 2

- **1,238 tests green** on `main` at 7572238 — 1,210 plus 28 golden — before a line of this sprint
  was written. That is the baseline anything here is measured against.
- **Verify by running.** `--shot` for the window; `millburn-cli export … --write` for the travel and
  line-count numbers to quote for any performance claim. A green suite is necessary and not
  sufficient — and for this sprint in particular, a green suite proves almost nothing about whether
  a list is readable.
- **`/code-review` at medium before every merge into `release/0.4.0` or `main`**, and
  **`/describe-test` on every test file created or edited**, one file per run.
- **Ask before committing, merging, pushing or publishing.** The author's own mill and laser run
  what this produces.
