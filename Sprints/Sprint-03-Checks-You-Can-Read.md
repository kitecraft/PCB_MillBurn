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

**And it was dropped — after being built.** Not for size, but because the fix turned out to make two
nets' separation depend on the drill finishing what the isolation started. See the withdrawal note
under story 7. The sentence above turned out to be the useful thing in this paragraph: it went last,
so taking it out stranded nothing.

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

**Thirty-eight of them, corrected while story 1 was built.** That count was taken with one grep and
it conflated two channels that never meet. The CHECK panel is fed by `MainViewModel` and
`ExportPlanner`; `MachineCheck` and `TestCut` produce warnings for a coupon, shown in their own
dialog and by the CLI beside the report they belong to, and **not one of them ever reaches the
panel** — traced through `MachineCheckWindow`, `TestCutWindow` and `Program.cs`. Converting those
six would deliver nothing to any story in this sprint and would put a second vocabulary through
three consumers for the sake of a number. They stay strings, named here so that the next person
counting gets the same answer for the same reason.

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
- **The sentence keeps its current text, exactly.** Lines that already say `Stock: …` say it
  because there was nowhere else to put it, and moving that prefix into the field is right — but it
  is only right *together with* the panel rendering the field, because the moment it does, the
  other forty lines gain a prefix they did not have. That is a change to what the operator sees and
  to the companion pages' bytes, so it is story 5's to make and not this one's. Until then a
  prefixed message carries its source twice, in the text and in the field, and the field is the one
  that is right. **Recorded here because it was got wrong first:** this bullet originally said the
  prefix moves in this story, which would have made "the output does not change" impossible to
  keep.

**Where this gets dangerous, and the rule for it.** Forty-four sites is enough that a mechanical
migration will be tempting, and the sites are not interchangeable: `TestCut.cs` and
`MachineCheck.cs` produce advice about a coupon, `ExportPlanner.cs` produces refusals about files
somebody is about to run. **Anything whose severity is not obvious from its call site gets read and
decided, not defaulted.** A refusal quietly demoted to advice by a default is exactly the kind of
fault this sprint exists to make impossible.

**Done when** a check carries its source, its kind and its severity rather than beginning with them
by convention; every site that feeds the CHECK panel produces one; `_exportWarnings` and its
string-equality removal are gone; the panel renders the same content it does today from the new
model, proved by a `--shot` against one taken before the story; and nothing in the emitted files
changed, proved by the golden baselines.

**Not in it.** Anything that changes what the panel *looks* like. This story ends with the same
list on screen and a model underneath it, and that is deliberate: a migration of this many sites
and a redesign of the surface in one story is two ways for it to go wrong at once.

### Closed — 2026-09-28

**Done, against every clause.** `Check` carries a `CheckSource`, a `CheckKind` and a
`CheckSeverity`; the thirty-eight sites that feed the panel produce one; `_exportWarnings` and its
string-equality removal are gone; and both proofs hold.

**The output did not move, and that is the whole verdict.** 1,244 tests green — 1,216 plus 28
golden — with **no golden baseline regenerated**, which is what says the emitted programs and every
companion page are byte-for-byte what they were.

**And that claim was itself checked, because it could have been true for the wrong reason.**
`Snapshot.Match` rewrites a baseline and *passes* when `MILLBURN_UPDATE_SNAPSHOTS` is set, so a
green golden suite proves nothing unless that variable is absent. It is unset, `MILLBURN_BOARDS` is
unset, `git status` on the Snapshots folder is clean and no `.actual.txt` was left behind. The
`/describe-test` pass is what put the question, by describing that escape hatch plainly.

**The panel is pixel-identical.** `--preview --shot` on the Arduino Mega at 1100 x 1100, before the
story and after it, compared pixel by pixel:

| | |
|---|---|
| Whole window differing box | **(250, 1079) to (283, 1091)** — 33 x 12 px |
| What is in it | the status bar's *"built in 2.78 s"* against *"built in 2.80 s"* |
| CHECK panel differing box | **none** |

The preview's own elapsed time is the only thing on screen that changed, and it is not the kind of
number that can be the same twice. The panel shows the Mega's fourteen checks including its real
short — *"+5V and GND are left connected: the gap between them is narrower than the 0.154 mm this
cut is wide"* — laid out exactly as before.

**What the migration decided, rather than defaulted.** The story said anything whose severity is
not obvious from its call site gets read and decided, and the ones worth recording are:

- **The electrical findings are `Refusal`, not `Error`.** The file is correct and safe to run; what
  will not happen is the separation that was asked for. That is precisely the middle severity and
  the reason there is one.
- **`ToolAdvice` decided its own.** Its class comment already said *"Advice, never refusal"*, so all
  three call sites take their severity from the class that produces them rather than from a guess
  at the call site.
- **Start/end G-code was the one site that already knew.** `issue.IsError` was a bool being
  flattened into two different sentences; it is the severity now, and nothing else changed.
- **"No tabs: the board will be thrown by the cutter" stayed `Advice`**, and it was the closest
  call. It is the worst consequence on the list, and `TabCount == 0` is something the operator set
  on purpose — `Refusal` means *we did not do what you asked*, which would be false. Left as
  advice and written down here because story 5 may prove the list needs emphasis that severity
  alone does not give.

**`/describe-test` found the hole this story would otherwise have left, and it was in the new code
rather than in the tests.** Six files were read back cold, one per run. Of one of them the account
said plainly: *"The `Check` record carries `Severity`, `Kind` and `Source`; no test in this file
reads any of them — only `Message`."* That was true of every test in the repository, and it
generalises into the thing that matters:

| Pins | What it pins |
|---|---|
| Golden baselines | the **text**, which this story leaves unchanged by construction |
| The panel comparison | the **pixels**, which render that text |
| *Nothing* | the source, the kind and the severity |

So a check attached to the wrong layer, or a refusal recorded as advice, would have been caught by
nothing at all — the sentence right, the panel right, the field underneath wrong. Thirty-eight
sites were assigned in one pass and every one of them was unguarded.

**`tests/MillBurn.Tests/CheckSourceTests.cs`** closes it, with invariants rather than pinned values
— wording is already pinned in a dozen places and the new fields are not pinned anywhere. Five
tests: a check that names a file names its own item's file; every check carries a label; a tool
check is never a refusal (which is `ToolAdvice`'s own declared rule, not this file's opinion);
copper that will not separate is a refusal and names its layer; and a board with something refused
carries more than one severity.

**Both halves were then mutation-tested**, because a guard nobody has seen fail is a guard nobody
has seen:

| Mutation | Result |
|---|---|
| Every layer source made to name `"mutant.gbr"` | two tests fail |
| The four electrical refusals demoted to advice | two tests fail |

**Then the new file was itself put through `/describe-test`**, the rule applying to a file created
as much as to one edited — and it found a name promising more than its body delivered, in a test
an hour old. `CopperThatWillNotSeparateIsARefusalAndNamesItsLayer` asserted only that the source
carried *some* file name, which a check hung on the wrong layer satisfies perfectly. The account
said so in as many words: *"the test does not verify that the file named is the right layer for
that check — only that a file name is present."*

It walks the items now and compares each finding against the one carrying it. **The first mutation
above caught one test before that change and catches two after it**, which is the difference
measured rather than asserted. A second, smaller correction came from the same account: a test
named *"…IsNeverARefusal"* that actually required `Advice` — a name promising less than it checks,
which is the safe direction and still wrong. It is `EveryCheckAboutAToolIsAdvice`.

That is the second time in this story the pass has found the defect in the new work rather than in
the code being read, which is the argument for the rule as written: *without exception*, including
on the file you wrote to close the last finding.

**Writing those tests found two things about the fixtures**, both worth keeping:

- **Depth is what fuses copper, not the moat.** The first version set a 0.4 mm moat and got a plan
  whose every check was advice. A wider moat adds laps; the cut width — which is what decides
  whether two nets stay joined — is a function of depth alone for a V-bit. 0.75 mm is the depth
  06 §6.42 records the test board fusing at, and it is what makes the electrical path produce
  anything.
- **PogoTest1 produces only advice, and that is correct.** A clean board with nothing declined has
  one severity, so the severity test had to move to a board that really does refuse something.
  Asserting otherwise would have been asserting that the application complains.

**Two more things found on the way, neither of them in the story.**

`PreviewBuild` deduplicated warnings with `Distinct(StringComparer.Ordinal)` — the text as the key,
because it was the only key there was. It is `Distinct()` on the record now. The set is identical
today, since two checks reading the same also agree on source, kind and severity; the point is that
when they stop agreeing, two genuinely different findings worded alike both survive.

**A trap worth the note it is getting:** rewriting a `.cs` file with a Python script that reads
text and writes with `newline=''` converts the file from CRLF to LF, and the build fails with three
`IDE0055` formatting errors pointing at code nobody touched. Half an hour went into reading a
perfectly correct `&&` chain. Read and write bytes, or restore the endings afterwards. It belongs
beside the heredoc-eats-backslashes trap in AGENTS.md.

**`/code-review` at medium found three, and the middle one is the one that matters.**

**It found a regression the pixel comparison could not have caught, and a comment of mine asserting
it was impossible.** `PreviewBuild` de-duplicated the panel's checks on the sentence; record
equality looked like the obvious upgrade, and the comment written beside it claimed the set was
unchanged *"because two checks reading the same also agree on source, kind and severity"*. That is
false **because of this very story** — every planner check now carries its own layer, so one
sentence produced for the top copper and again for the bottom is two records where it was one
line. Reproduced on the Arduino Uno: six checks, **five lines before and six after**. It
generalises to tool advice for the same bit on both copper layers, the mask-relief sentence on both
masks, and the mirror sentence on every mirrored item — so the migration underneath 6.44 was
quietly lengthening the panel that 6.44 exists to shorten.

**Why the evidence missed it, which is the lesson worth keeping.** The before/after comparison was
pixel-perfect and it was taken on the Arduino Mega, a board with no cross-layer duplicate to show.
The proof was sound for that board and was stated as though it were general. A screenshot proves a
board, not a property.

De-duplication is back on the rendered line, and `OneSentenceFromTwoLayersIsOneLineInThePanel` pins
it — with a guard that fails if the Uno ever stops repeating a sentence, so the test cannot pass by
having nothing to examine. It fails on the record-equality version. **And it is expected to change
in story 5:** once the label renders, "Top copper: …" and "Bottom copper: …" are two different
lines and both must survive. The key changes when the label does, together, with the panel in
front of you.

**The other two.** A check built for an unreadable file was handed the whole sentence — `Board.Failures`
entries are `"F_Cu.gbr: unexpected token at line 12"` — as its `FileName`, a field whose entire job
is to be matched against a layer's name and which nothing could ever equal. Reconstructing the name
by splitting the sentence is exactly what this story exists to stop, so `Board.Failures` is now
`BoardFailure(FileName, Reason)` with a `ToString` that renders the sentence it always was; every
reader is unchanged and the name is there for anything that needs it. And the deleted
`_exportWarnings` left its doc comment behind, giving `ReplaceExportWarnings` two `<summary>`
blocks — the orphan recorded a real fixed bug (two runs disagreeing about the probed side showed
each file refused for two opposite reasons at once), so it is folded into the method's own summary
rather than deleted.

**Left open, deliberately.** The source is attached everywhere and rendered nowhere: `Check.Line`
returns the message alone, so a prefixed message such as `Stock: …` carries its source twice, in
the text and in the field. Story 5 renders the label, strips the duplicates and takes the golden
diff that comes with it.

**6.50 revisited, as the story required.** The work landed almost entirely in `MainViewModel` — a
new `SourceFor`, twelve producer sites and the deletion of the shadow list — and **none of it is
reachable from a test.** What proves this story is the golden files and a pixel comparison of a
screenshot, which is real evidence and is not a unit test. The one genuinely new decision in the
view model, *"which checks in this list are the export's"*, is now a one-line predicate on a field
and would be the easiest thing in the file to test if anything could reach it. That is the argument
for 6.50 stated from the other side, and it is stronger than it was on 2026-09-28 morning. Still
not pulled into this sprint: stories 4, 5 and 6 are all in this file, and a seam pulled now would
be pulled underneath them. **Worth putting to the product owner again when story 6 closes.**

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

### Closed — 2026-09-28

**Done.** *Job ▸ Check this board* (**F6**), above Preview in the menu because it is the cheaper
question and the one worth asking first. `BoardCheck` in the pipeline does the work; the window
runs it on a background thread through its own `LatestRun`, so pressing it twice supersedes rather
than queues, and it does **not** share the preview's run — checking a board while a preview builds
is a reasonable thing to do and must not throw the picture away.

**The picture is the verdict.** `--check --shot` on the Mega: the board still drawn as copper
rather than as a toolpath — nothing was planned — the CHECK panel holding eleven findings beginning
*"+5V and GND are left connected…"*, and the status line reading **"Checked 2 layers in 0.50 s: 11
thing(s) worth checking."** No export, no preview, no G-code. A screenshot taken after a preview
could not have shown this, which is why `--check` exists as a flag of its own.

**Half a second on the Mega**, so the window does not need protecting from it — but the run is
cancellable anyway, because the six-layer i.MX8M board is the one this was sized against and
nobody should discover the ceiling at the machine.

**The automatic check stays, and nothing was taken away.** Planning still checks and still refuses
to let a board through quietly. The button is for the operator who has not pressed Preview yet; the
automatic half is for the one who never thinks to press anything.

**Pressing Check after Preview does not show everything twice.** Preview alone puts 14 items in the
panel, Check alone 11, and Preview-then-Check 14 — the eleven electrical findings replaced rather
than appended. That is `ReplaceElectricalChecks`, and **it is a method that could not have been
written before story 1**: the electrical findings for a layer are the same findings whoever
produced them, so what has to be matched is the layer and the kind. Matching on the sentence — the
only key there used to be — cannot tell one layer's copy from another layer's identical copy.

**A bug caught while writing it, and worth recording because the test would not have found it.**
The first version worked out which layers to refresh from the findings themselves. A layer that was
checked and came back *clean* contributes no finding to announce itself, so its stale line would
have stayed on screen for ever — precisely when the operator most wants to see a short they have
just fixed disappear. `BoardCheck.Result` carries the layers it looked at, not only a count of
them.

**And the cancellation test was written wrongly first, in exactly the way CLAUDE.md warns about.**
The first version cancelled the token *before* the call — which a single `IsCancellationRequested`
at the top of the method satisfies, proving nothing about a running check stopping. That is the
`LatestRun` failure CLAUDE.md names as having already happened once, reproduced verbatim. A
`/describe-test` pass said so plainly: *"a run cancelled part-way through, which is the situation
the doc comment describes, is not exercised."*

It uses `CancelsAfterLayers` now, the fixture sprint 2 built for this, aimed at the first isolated
layer's own settings lookup so the run finishes one layer and stops at the next. **Measured:** the
whole run checks 2 layers, the abandoned one reaches 1 lookup and checks 1. And it is
mutation-tested — moving the token check out of the loop to the top of the method, which is what
the first version would have allowed, **fails it**.

Getting there took two wrong assertions of my own, both recorded in the test: a cut-off expressed
as "most of the way through the layer list" landed after both copper layers on a board with
thirteen layers and two isolated ones, and a guard written on the lookup count failed on a board
whose first layer is copper, where one lookup is all it takes to be half done. Lookups are how the
cut-off is aimed; layers are what "part way" means.

**Left open.** `CheckBoardAsync` and `ReplaceElectricalChecks` are in `MainViewModel`, which no
test can reach — the third time this sprint. `BoardCheck` itself is in the pipeline and is tested
four ways. What is untested is the wiring, and the wiring is what sprint 2 was bitten by. See the
note on 6.50 below.

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
  check rather than its name in the sentence. **This is where the prefix actually moves.** Story 1
  attached the source and deliberately did not render it; rendering it gives forty lines a prefix
  they have never had and takes it out of the text of the handful that carry it today. Both halves
  are one change, and the companion pages' golden baselines move with it — **a golden file that
  changes is a decision, so the diff gets read rather than regenerated.**
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

### Closed — 2026-09-28

**The prefix moved, and it cost six golden lines.** `Check.Line` is now
`"{Source.Label} — {Message}"`, which reaches the panel, the companion page, the CLI and the export
window from one place. The golden diff was read rather than regenerated: exactly six lines across two
baselines, each gaining the label of the `layer` line in its own block — `Bottom copper — Mirrored:
…`, `Non-plated holes — …`, `Plated holes — …`. The two drill warnings had been indistinguishable
prose about two different layers; they now say which.

**The panel takes the halves apart, because it colours one of them.** `CheckRow` carries the label,
the message, the mark and the brush, and the brush comes from the view model's `Palette(role)` so a
label follows a colour override. A check naming "Top copper" in the palette's orange while the layer
row above it shows a chosen green is the disagreement this project's rules name first.

**A null brush is not the same as no brush, and a screenshot is what said so.** Binding a null
`Foreground` to a `Run` does not inherit — it sets the property to null and the text vanishes. A
board with no outline rendered two checks as `— No board outline: …` with the word "Board" simply
absent. `BrushOrInherited` returns `AvaloniaProperty.UnsetValue` instead, so an uncoloured label
inherits from a style and therefore follows a light/dark toggle, which a brush resolved once and
handed to the row could not. **Nothing in the test suite could have caught this**; it is the 6.50
argument again, and this time the evidence is a screenshot of a missing word.

**The fold's two open questions, answered with the count in front of us** — not remembered across
sessions, not reopened by an arriving note, reopened by an error or by an export refusal. The second
exception is a different rule from the first: the status line is about to say *go and look at these*,
and a message pointing into a folded list is an instruction nobody can follow. Recorded in 06 §6.37.

**The de-duplication key did not move, and its reason did.** Story 1 predicted the key would have to
change here. It did not: `DistinctBy(w => w.Line)` reads identically and now means something else,
because `Line` changed underneath it. The comment was rewritten rather than left looking prescient —
the rule is *the key is what the reader can see*, which is why record equality is still wrong, and
for a reason that outlasts the label: it would separate checks differing only in a field the panel
does not print.

**A test inverted on purpose.** `OneSentenceFromTwoLayersIsOneLineInThePanel` said in its own summary
that it expected to change here, and it has, to
`TwoLayersSayingTheSameThingAreTwoLinesThatNameTheirLayers`. Both halves are now pinned: the pair
survives and each names its layer, and no line still appears twice.

**Three duplicated sources removed, all found by reading screenshots**, none reachable by a test
because two of the three live in `MainViewModel`. `NoCheckNamesItsOwnSourceTwice` sweeps four boards
so the planner's half cannot regress — and its emptiness guard immediately earned itself by failing
on `GridStripConnector`, which produces no checks at this cut and would have been a passing case that
examined nothing.

**One defect exposed rather than introduced.** An export refusing a dry run for seventeen programs
named all seventeen in one check, which filled the entire panel. Capped at three and a count. It had
always been that way; what changed is that this story put a screenshot of the panel in front of
someone.

**The companion page gained a line, correctly.** Its `Distinct` collapsed the two soldermask
warnings — *"This layer is negative: the shapes are its openings…"*, identical prose about two
different layers — into one. They now read `Top soldermask — …` and `Bottom soldermask — …` and both
survive, which is the same principle as the panel arriving at a surface with no golden file on it.

**Shots.** Unfolded, Arduino Mega via `--check`: eleven findings, "Bottom copper" in blue and "Top
copper" in orange matching their layer rows, amber `!` marks aligned in their own column.
Folded, same board: `CHECK · 11 to look at` and five more layer rows visible.
Highlighted, test board via `--write-export`: the tinted `Export — No dry run: …and 14 more` check
under the status line *"17 refused — No dry run — see the checks."*

**1,261 tests green** (1,233 + 28 golden), two baselines moved by three lines each and read line by
line.

**And 06's own status lines were left alone, on the repository's say-so.** Marking 6.36 and 6.37
`· Fixed` in their headings failed `BacklogTests.TheBacklogMatchesTheRoadmap`, which generates
[11](../Documentation/11-Backlog.md) from those headings. The convention it enforces is the one 6.42
and 6.43 already follow after stories 2, 3 and 4: a folded entry keeps `folded into 6.44` and records
its outcome in its body, and 6.44 itself is what gets marked when the last of the six lands. The
headings went back; the bodies say what was built.

**Left for story 6.** The companion page still flattens severity — it is read away from the app, so a
refusal and a note look identical there with no panel to compare against. Not fixed here because the
fix is a section of its own rather than a mark on a list item, which is exactly what story 6 is.

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

### Closed — 2026-09-28

**Where a gap is, computed rather than approximated.** `ElectricalCheck.Gaps` grows each piece of
artwork on its own and intersects the results: the overlap is exactly the set of places a cut of that
width cannot fit between two pieces. That is the answer `NetJoin.Near` says in its own doc comment
that it does not give — *"enough to select the right piece of copper and not enough to point at the
fault"*.

**It is off the export path on purpose, and the measurement is why.** The first version paired every
piece against every other and rejected the far ones by their boxes, which is useless against a ground
pour whose box is the whole board: every other piece survived the rejection and paid for a boolean
against a two-hundred-ring pour. **1,205 ms** on the Mega's top copper. Restricting pairs to pieces
that merged into the *same grown region* — different regions are provably further apart than the cut
— gave the same eight gaps in **266 ms**. A whole board is 1.59 s on the Mega, 0.26 s on the test
board, off the UI thread on the `LatestRun` pattern.

**Two faults the first screenshot of the view found, neither reachable by any test that existed:**

- **The coordinates were in the Gerber frame.** Shorts reported at "107.35, -92.13 mm" on a board
  whose own coordinates run from zero. Work zero is the board's lower-left corner in every emitted
  program, and a negative coordinate on a board with no negative corner is worse than none, because
  somebody will try to go there. Shifted in `BoardFindings`, which knows about boards — not in
  `ElectricalCheck`, which knows about copper and would be guessing at a convention it does not own.
- **One bridge appeared under two groups.** Matching a place to a group by the two nets either side
  looks right and is not: a ground pour reaches many separate regions, so a GND–USHIELD bridge was
  filed under every group holding both names and the board looked worse than it is. It now matches on
  the whole region's net names — **carried as names rather than as a region index**, because
  numbering the regions in two methods couples them through a traversal order computed twice from the
  same input, which works and is a trap.

**The CHECK list is down to one line a layer.** The Mega's eleven electrical lines became two, each
coloured, each a refusal, each naming its count and pointing at F7; three more layer rows fit above
it. Collapsed in the view model and **not** in `ElectricalFindings`, because the companion page and
the CLI have no window to point at — a line ending "see the findings view" would be a dead end on a
page read away from the app, so they keep the full set. The status line gained the same pointer, so
"11 thing(s) worth checking" over a panel headed "· 2" is explained rather than contradictory.

**Story 3's split is places now, not a tally.** Same-net and nameless bridges each get their own
section with coordinates and a sentence saying which of them can be ignored and which cannot.

**The line held.** No other DRC check was added while the view was open — no drill-to-copper
clearance, no outline severing a trace — and nothing from the severed-net family, which stays out
until the operation that can sever is the one being checked.

**Shots.** Mega: 2 layers, 23 places, 13 groups, 1.59 s, every group with real coordinates inside the
board's 101.85 × 53.59 mm. Test board: *"nothing shorted"*, both layers green, 0.26 s — the clean
case, which is the one a view like this must get right or it will be ignored.

**`/describe-test` found three defects in the new tests, and one of them was a trap this repository
had already written down.**

- **`NetGapTests` set `WidthNm` and named its cases after it** — "at 0.4 mm" — while the check reads
  `EffectiveWidthNm`, which is the bit's width at the depth it cuts and never looks at `WidthNm`.
  Every case ran at the default 0.05 mm depth whatever was passed, so two tests framed as a wide cut
  against a narrow one were the same cut twice, and the figures printed to the test log were false.
  `CheckSourceTests` records this exact trap — *"The depth is what does it, not the moat"* — which
  makes this the second time it has been walked into. The helper now takes a depth and every test
  prints the width it actually used. **A new test pins the relationship** that would have caught it:
  a 0.127 mm cut finds 8 places, a 0.234 mm cut finds 161.
- **`BoardFindingsTests`' split test could not fail.** `Assert.Single(gap.Between)` and "no nameless
  gap has names" restate the partition itself — `IsSameNet` *is* `Between.Count == 1` — and the test
  named for showing places never touched a coordinate. It now asserts the places are on the board,
  are distinct from one another, and name nets the board actually declares.
- **Two guards were missing**: the determinism test compared two lists with nothing saying they were
  not both empty, and the timing test asserted only a ceiling, which is satisfied perfectly by
  returning nothing instantly.

**1,273 tests green** (1,245 + 28 golden), no golden baseline regenerated.

**6.44 is closed.** All six of its *Done when* clauses are met across stories 1–6, and it leaves the
backlog: 23 open entries become 22. The backlog is generated from 06, so that page was regenerated
rather than edited — with `MILLBURN_UPDATE_SNAPSHOTS=1` scoped to that one test, because setting it
across the suite would silently accept any golden that had drifted.

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

### Withdrawn — 2026-09-29

**Built, and taken back out on the product owner's judgement:** *"I worry this is actually creating a
problem and the copper is not getting separated. Let's leave this one out for now."*

The objection is the right one and it is not about the implementation. Clipping the moat at a hole
makes two nets' separation depend on the **drill** completing what the isolation started — a
different operation, run afterwards, with its own registration. A hole slightly out of position, an
undersized bit, or an operator who stops after isolation to look at the board leaves copper joined
and a program that reads as complete. The behaviour being fixed costs a fraction of a second and
looks wrong; the fix risks a short. **Refuse rather than guess** decides that, and a cut completed by
a later operation is a guess about that operation.

**Everything measured is recorded in [06 §6.47](../Documentation/06-Roadmap-and-Risks.md)** — the 44
crossings, the 43 seconds of extra plunges against 0.2 seconds of cutting saved, the keepout band,
and the shape a replacement would need: stop the moat short of the hole while still closing the loop,
so the isolation separates the copper on its own. The code is kept as a patch in
`WorkingFolder/held/` rather than deleted.

**Four of the five faults found while building it were mistakes in measuring it**, not in the code,
and the product owner found the one that mattered by looking at the board. Endpoints hid a chord
across a hole; chords invented crossings on arcs; a wrong radius produced a defect that did not
exist and was reported as a finding; a mirrored program was compared against an unmirrored drill
file. Plotting the toolpath from the emitted G-code was the only measurement that held up.

**The sprint ships six stories, not seven**, and 6.47 stays open with more written under it than it
had before.

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

**6.47 is back here**, and it is the fourth decision about it in six days. It was held twice, brought
into this sprint as story 7, built, and withdrawn once the fix was seen to make the copper's
separation depend on the drill. It goes back to the held list better understood than it left:
everything measured is under the entry, and so is the shape a replacement has to take.

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

**One thing came in under that rule, and it is recorded rather than absorbed.** Story 2's
measurement turned up a third disagreement between the two paths that had nothing to do with the
check: `mill` synthesised its cutter from its own 30°/0.1 mm defaults while `export` resolved one
from the operator's saved library, so the two commands planned different cuts on the same board
and `mill` ignored a tip the author had measured and corrected. It was put to the product owner
with the measurement rather than fixed in passing, because defaulting `mill` to the library is a
**Breaking** CLI change; they took it the same day. It belongs to G16 rather than to 6.42, it is
written up in [06 §6.42](../Documentation/06-Roadmap-and-Risks.md) where it was found, and the
matrix row says what was actually true before it.

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
