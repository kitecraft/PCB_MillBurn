# Sprint 5 — Put it where it belongs

**Agreed** 2026-10-10, after sprint 4 and the two fixes that followed it. **Release branch:**
`release/0.6.0`, to be branched from `main` once v0.5.0 is tagged.

**The product owner's direction:** a ten-item list, narrowed to six in the picking. The ten were
6.50, 6.55, A7, G22, M6, M16, M17, V11, V32 and 6.20 — and two of those are the same requirement,
which is where the narrowing started.

**The theme is what five of the six have in common:** getting a design onto the machine in the
right place, the right way up, and on the right layer. M6 and V11 are the laser's half of it — an
SVG whose geometry lands on a layer whose mode is already set, and a preset that sets those modes
once instead of every board. V32 is the stock's half: which way up the piece goes, before anything
is cut into it. M16 and M17 are the mill's half and the hardest of the four — marks that survive
the process that comes between, and a fit from the numbers an operator reads off them.

**This is the first real cut into the block that has been stuck longest.**
[08](../Documentation/08-Requirements-Matrix.md) has said the same thing at every audit: document 04 is **15 not-started rows out of 32, nearly all
of them the alignment and Job-model half of Phase 5**, and *"that one block is still most of what
is left"*. 6.14 was the first cut — the stock's waste holes, a two-point rigid fit, a correction
kept with the project. This is the second, and it takes M16 and M17 from their smallest useful form
to what §4.2 actually specifies.

**The sixth is review debt and does not fit the theme**, which is said here rather than argued
away. 6.55 is in because it is small, because it is the last thing `/code-review` has raised and
never had answered, and because the findings view is now the front door to two other features —
6.44 reads it and 6.56 navigates from it — so the second and a half it costs is paid more often
than when it was measured.

---

## How this sprint was picked

**The defect list first**, as the standing rule requires. One command produces it:

```
grep -n 'defect · open' Documentation/06-Roadmap-and-Risks.md
```

**Two came back, and both are out — the first time the list has produced nothing.**

| # | what | what happened to it |
|---|---|---|
| [6.45](../Documentation/06-Roadmap-and-Risks.md#645-block-apertures-and-the-aperture-transforms--defect--open--no-fixture-exists-yet) | Block apertures and the aperture transforms | **Out.** No fixture exists, so it cannot start. Held on that ground since sprint 2, and the unblocker is a Gerber set rather than a line of code |
| [6.47](../Documentation/06-Roadmap-and-Risks.md#647-isolation-cuts-inside-a-hole-that-is-about-to-be-drilled--defect--open--found-in-the-workshop-low-priority-a-later-sprint) | Isolation cuts inside a hole that is about to be drilled | **Out.** Built in sprint 3 and withdrawn by the product owner, because the fix made two nets' separation depend on the drill finishing what the isolation started. The patch is kept in `WorkingFolder/held/`; the shape is wrong and nobody yet has a better one |

Sprint 2 opened with four open defects and sprint 4 with one. **Worth recording that the number
reached nought**, and worth not reading too much into it: twelve defects were raised and closed
*during* sprint 4, eleven of them by `/code-review` and one from the bench. The list is empty
because the sprint that just ended cleaned up after itself, not because the application stopped
having faults.

**Then the other two places the rule names.** [08](../Documentation/08-Requirements-Matrix.md)'s
**Partial** rows are where four of the six stories come from — M6, M16, M17 and V11 are all Partial
and all in the block above. [09](../Documentation/09-Machine-Accuracy-Investigations.md)'s single
open item is still squaring the laser's gantry, which is a frame to be shimmed and not a line of
code.

### And the six

| # | story | where it came from |
|---|---|---|
| [M6](../Documentation/08-Requirements-Matrix.md) | The SVG says which layer, so the mode is already set | 04 §2.3; Partial — single-layer mode and inversion exist |
| [V11](../Documentation/08-Requirements-Matrix.md) | A LightBurn preset that ships | 04 §2.3, §3.3; Partial — palette mapping done, no preset file ships |
| [V32](../Documentation/08-Requirements-Matrix.md) + [6.20](../Documentation/06-Roadmap-and-Risks.md#620-which-way-up-is-this-stock--enhancement--open) | Which way up is this stock? | The workshop, cutting the 78.63 × 76.09 test stock |
| [M16](../Documentation/08-Requirements-Matrix.md) | Marks that survive what comes between | 04 §4.2; Partial — the milled case is cut and measured, the rest is not |
| [M17](../Documentation/08-Requirements-Matrix.md) | A fit from three or more marks, and what it cost | 04 §4.2; Partial — the two-point rigid case exists |
| [6.55](../Documentation/06-Roadmap-and-Risks.md#655-the-findings-view-costs-about-twice-what-it-needs-to-and-cannot-be-interrupted--enhancement--open--found-by-code-review-at-xhigh-2026-09-30) | The findings view, at half the cost and interruptible | `/code-review` at xhigh, 2026-09-30 |

**The order is cheapest first, and that is deliberate rather than conventional.** M16 and M17 are
the substantial pair and they are last, so that a sprint which runs long drops the half that was
always going to be hard rather than the four that were not. Sprint 4's own notes are the reason:
nine stories, flagged as a deliberate risk, and it ran long.

**V32 and 6.20 are one story, not two.** They were offered as separate items and they are the same
requirement — V32's Source column reads `06 §6.20`. Catching that is the only reason this sprint is
six stories rather than seven.

### What was dropped, and why

| # | what | why it is not here |
|---|---|---|
| [6.50](../Documentation/06-Roadmap-and-Risks.md#650-nothing-can-test-the-view-model--enhancement--open--three-times-in-one-sprint) | Nothing can test the view model — the settings row | **Out, on its own entry's standard.** That entry says *"the argument for it is the table above rather than a principle"*, and the table has two faults for settings persistence (6.49 and 6.62) where the invalidation row had six. Both are fixed. The decision is three booleans across nineteen handlers, and a seam there would mostly say "write it now" nineteen times. **The one thing worth doing is smaller than a story**: `RememberSettings` means two incompatible things — for board thickness, surviving a restart via the close-write is the feature; for the `--no-ruler` override it was the hazard that forced the value out of `AppSettings` entirely. One method, two contracts, and the second is a pattern invented under review pressure that will be reinvented badly. An hour, when somebody is next in that file |
| G22 | Coachmarks and a first-run walkthrough | **Out — it has said "needs research" since it was written**, and its own entry says the walkthrough wants the test board to point at. Putting an unresearched item in a slate is how a sprint ends with one story not started, which is the warning sprint 3 left and sprint 4 took the risk against anyway |
| A7 | Progressive reveal: paths drawn before the optimizer finishes | **Out — it wants a measurement first.** It is architectural: the optimizer yields partial results and the viewer accepts them mid-flight, so the pipeline, the renderer and cancellation all move together. And C7 — *preview latency, parameter change to redraw, < 200 ms* — is still **Not measured**. The 2026-10-10 audit found its stated blocker (*"depends on A4/A5, which do not exist"*) had been removed in sprint 1 and nobody revisited the row. Measure C7, then decide whether A7 is the answer to it |

---

## Size, and what gets dropped first

Six stories, and **the last two are the sprint**. M6, V11, V32 and 6.55 are each a day or
thereabouts; M16 and M17 together are half of 04 §4.2, which specifies a survivability table per
workflow, automatic placement, a solver for three or more points, and a dialog that parses what an
operator pastes into it.

**M17 goes first if something has to go**, and M16 can ship without it: marks that survive are
useful on their own, because the two-point fit already built consumes them. The reverse is not
true — a three-point solver with nothing to measure is a solver nobody can reach.

**Then 6.55**, which is the one story here nobody asked for.

**What does not get dropped is V32.** It is the smallest of the six and the only one that stops
somebody putting a piece of stock in the machine the wrong way round, which is a mistake that
wastes the piece and everything cut into it.

---

## How a story closes

As in sprints 1 to 4: a `Closed` block with the date, a plain verdict against *Done when*, the
manual test in enough detail that somebody else could run a similar one, what was observed
including the sentence from the bench, and what is left open. A story without one is still open
whatever its branch says. A defect closes with the test that would have caught it. A story that
changes what a surface looks like closes with a picture, and one whose output leaves the screen
closes with the artefact itself.

**And one for this sprint, which is new.** **M16 and M17 cannot be closed by a screenshot**, and
that is a first since sprint 1. Every story in sprint 4 closed on a `--shot`; these two close on
numbers read off a machine — jog to a mark, read the DRO, type it back, and see what the fit says
the residual was. The *generator* can be checked headlessly and must be; the *fit* cannot be
trusted until it has been fed measurements from a real piece of stock with real marks in it. Plan
for a bench session, and do not let a green suite stand in for one.

---

## Story 1 — The SVG says which layer, so the mode is already set

**Problem.** A laser program decides *how* to burn a shape from its layer settings — Line, Fill, or
Offset Fill — and not from the SVG's own `fill` attribute. So the SVG's only job is to put each
piece of geometry on the layer that has the right mode, and in LightBurn that assignment happens
**by stroke colour**. Single-layer mode and inversion exist; the per-layer contract does not.

**What.** The contract 04 §2.3 already writes down:

| Geometry | Layer | Mode the operator sets once |
|---|---|---|
| Mask-removal regions (etch resist, pad openings) | `C01` | **Fill** |
| Region boundaries, when a crisp edge is wanted | `C02` | Line — low power, runs first |
| Silkscreen centrelines | `C03` | Line |
| Registration marks | `C00` | Line, or a "tool" layer |
| Board outline, reference only | `C08` | **Output off** — never burn it; it is there to see |

**The palette lives in the `LaserProfile`, not in code.** It is already there; what is missing is
the geometry being assigned to it per layer rather than per file.

**Done when** an exported SVG puts each kind of geometry on its own stroke colour, the board
outline lands on a layer whose preset is output-off, and a `--shot` of the file opened in something
that honours stroke colour shows the five groups distinctly.

### Closed — 2026-10-10

**Met on the screen; not yet at the laser.** The colours are right by construction and by test,
and nobody has imported one of these files into LightBurn or Falcon since they changed.

**What was wrong was worse than "per file rather than per layer".** The export writes every SVG
flattened, for the Falcon, and flattening painted the drawing in the Mark colour — `#00E000`,
LightBurn's **C03**, a Line layer. So a mask's openings arrived on a layer that would trace their
edges and clear nothing inside them. The placing layers were literal colours chosen to be
different from the drawing: the board outline red, which is **C02**, the low-power Line layer that
runs first; and the stock and holes blue, which is **C01**, the layer the contract sets to Fill.
Set LightBurn up the way 04 §2.3 says and it would have filled the stock's rectangle and traced
the board.

- **Every colour now comes from the role, through the profile.** `SvgReference` carries an
  `ArtRole` instead of a colour string, and the writer asks `SvgProfile` for it like it asks for
  everything else. The board outline is `Reference` — C08, `#B4B4B4`, the layer the preset sets not
  to output. The stock and its holes are `Registration` — C00, black — because the holes are what
  Print and Cut registers on.
- **Single-layer mode keeps its count and gains a colour per role.** One filled path and one
  stroked path per width *per role*, rather than one of each in all. The test board's mask is still
  one path for the drawing; a file with fills and centrelines in it now has two, because it needs
  two modes. That is two layers in the laser software, not the hundreds the mode exists to prevent.
- **Each file names its layers.** The `<desc>` lists every role actually in the file with its layer
  name, colour and mode — *"Layer C01 (#0000FF): regions to clear; set to Fill."* — so the contract
  is in the file and not only in a document.
- **The five places that said red and blue** — the Settings window, the project page, the FAQ and
  two comments — now say grey and black.

**The manual test.** The test board exported from its project with `--thickness 0.8`, and every
SVG's strokes counted: in each of the four, one `#0000FF` path, one `#B4B4B4`, three `#000000` (the
stock and two holes), and no red or green anywhere. Then `F_Mask.svg` beside the `svg` command's
centreline output for `F_Silkscreen.gbr`, both rendered in Edge, which honours stroke colour:
`WorkingFolder/V0.6.0/Screenshots/story-1-layers-by-colour.png`. Between them the five groups are
there and distinct — openings in blue, the outline in grey, the stock and holes in black, legend
centrelines in green, and the wider strokes in red.

**Not done, and said rather than implied.**

- **Region boundaries on C02 are not generated by the export.** The contract has them *"when a
  crisp edge is wanted"*, and nothing in the application asks for one yet. The red in the picture
  is the `svg` command's wide silk strokes, which use the Boundary role for a different reason and
  were already there. A layer setting for a crisp edge is the obvious shape and it was not asked
  for.
- **The export's silkscreen is still the realised area on C01, not centrelines on C03.** 04 §2.5
  wants centrelines, the `svg` command produces them, and the export does not route through it.
  Changing that changes what the workshop's silkscreen burns look like, and *"the mask and
  silkscreen alignment is perfect"* is the last thing said about them.
- **The bench.** The placing layers the workshop has been switching off by colour are now grey and
  black instead of red and blue. They are still a layer each in both programs by construction —
  three colours, three layers — but that is the claim 6.17 was careful to check in Falcon itself
  rather than assume, and it should be checked again.

### What the code review found — 2026-10-10, medium

Eight findings; five fixed, three left with a reason.

- **The file told the operator to burn the stock.** The first wording for C00 was *"Line if the marks
  are to be burned"* — and the stock's rectangle is on that layer with the holes, while the project
  page, the FAQ and Settings all say the placing layers are not for burning. It now says *not for
  burning; set not to output*, like they do.
- **The description could name a layer that is not there**, from a role whose shapes write nothing.
  It now counts only what draws. And it is **LightBurn's only**: an Inkscape file's colours are for
  reading, so telling it to *"set to Fill"* was advice for a palette that is not the laser's.
- **Two more places still said red and blue.** M31's row in 08 is corrected; 6.17's *Built* note is
  history and gains a line saying it was recoloured, as does nothing in 09, which records what that
  burn really was.
- **Left:** the Inkscape flavour's flattened output shows no role colours — true before this branch
  and never used by the export, which writes LightBurn's. Boundary is not ordered before the fills
  in a flattened file — the export never writes Boundary, and LightBurn orders by layer rather than
  by document. And a helper that could have been a `GroupBy`, which is style.

---

## Story 2 — A LightBurn preset that ships

**Problem.** The palette mapping is built and nothing ships that applies it. So the operator sets
Fill, Line, power and order by hand, once per board, from a table in a document — which is exactly
the kind of thing the application knows and keeps to itself.

**What.** A LightBurn layer preset written next to the SVG, so the modes are applied once and
reused. 04 §2.3: *"Ship this as a LightBurn layer preset next to the SVG, so the settings are
applied once and then reused for every board."*

**Not in it.** Power/feed matrices and focus ramps, which 04 §3.3 deliberately leaves to
LightBurn's own material library — *"deliberately not generated"*. This story ships the layer
assignment and the modes, not the numbers that depend on the tube, the lens and the material.

**Done when** the preset imports into LightBurn and the five layers come up with the modes the
table names, on a board exported from the application and nothing typed by hand.

### Closed — 2026-10-10: declined, and what was kept

**Not met, deliberately.** The preset was built, failed on the bench, was shown how to work, and
was then declined by the product owner as not worth keeping. V11 is **Superseded** in 08, and the
whole account — including the file format as LightBurn wrote it — is
[06 §6.69](../Documentation/06-Roadmap-and-Risks.md), so that nobody repeats the bench work.

**What happened at the bench, in order.**

1. **The preset as first built** — a `.lbrn2` holding the five layers' settings and no shapes —
   loaded nothing: *"Neither file opened anything in LightBurn. The Cuts/Layers remain empty."*
   LightBurn keeps a layer only while a shape is on it. The tests had passed, because they checked
   the file against my own reading of an unpublished format: exactly the failure *verify by
   running* exists to catch.
2. **An SVG of this application's, imported and saved by LightBurn**, gave the real format — and
   showed the chore is real: C01, remembered as Fill in the workshop's preferences, came back as
   Line at the defaults.
3. **A hand-written probe** with a square on each layer opened with every setting as written, and
   in Preview filled the C01 square as a ring and left the output-off layers out of the job. A
   `.lbrn2` per SVG, with the geometry in it, would have worked.
4. **Declined:** *"It seems like overhead that we have to maintain for a small number of users. I
   don't use LightBurn normally. I use Falcon from Creality."* The value was always in each SVG
   putting its geometry on the right colour, which story 1 already does for any software that
   imports by colour.

**What was kept, because it is worth having without LightBurn.**

- **The project page's *Laser layers* table** — colour, with a swatch, what it is, Fill or Line,
  and whether to burn it — for the colours actually in this export's files and no others, naming
  no program. The test board on stock gets three rows: the drawing, the stock and holes, the
  outline. `WorkingFolder/V0.6.0/Screenshots/story-2-laser-layers-table.png`.
- **`LayerContract`**, the one table both that page and each SVG's own description are written
  from. Story 1 had the description's wording in a `switch` inside the writer.
- **The export window counts the project page.** It never had, so it said one file fewer than it
  wrote; found while counting the preset. *16 files will be written*, which is what the CLI writes:
  `story-2-export-window-counts-the-page.png`.

**What was removed:** the preset writer and its tests, the plan's preset field and the list of
whole-export files built for it, and the changes to "Export only this layer", the plan cache and
the CLI that existed only to carry it. The five code-review findings fixed in the preset all went
with it.

**The review of what was kept — 2026-10-10, medium.** Ten findings; six fixed, four left.

- **A stock with no holes was called "stock and holes".** The row is now named by the placing layer
  actually in the files, as the paragraph above the table already was.
- **The file's description and the page listed layers in different orders**; both follow the
  contract's run order now.
- **`ArtRole.Registration` said "usually burned"**, while the contract says not to output it. The
  only geometry on it is the stock and its holes, so its documentation now says so — and says a
  mark meant to burn wants a role of its own, which is what story 4 has to know before it puts
  fiducials anywhere.
- **Smaller:** the mode cell uses the mode's own name rather than a ternary that would call a third
  mode "Line"; the colour is escaped on the page like everything beside it.
- **Left:** an unknown role throws rather than writing "unassigned" — the enum is closed and a new
  role should fail loudly; the page reads the built-in palette rather than the SVGs' profile, and
  the test now asserts the page's colours are exactly the files'; a Fill row for an export whose
  only drawing is empty, which is one harmless row; and placing layers matched by label, a pattern
  `Laser()` already used, whose move to roles is a change of its own.
- **`/describe-test` found the gap that mattered**: the page's table was never compared with the
  SVGs. The test now collects every stroke colour in the export's files and asserts it is the set
  of colours in the table.

---

## Story 3 — Which way up is this stock?

**Problem.** From the workshop, cutting the 78.63 × 76.09 mm test stock: *"Might need a better way
to orient the board. On this mostly square board, the chamfer corner isn't quite enough for a
visual check for orientation. It's too square."*

The datum corner **is** already marked — `BlankOperation` chamfers it 3 mm in the same pass as the
edges, and the stock program says so. On an oblong piece the shape itself says which way round it
goes. On a nearly square one it does not: a 3 mm chamfer on a 78 mm edge is a detail you have to go
looking for, and every one of the four corners is a candidate until you find it.

**And the holes do not settle it.** Turned 180°, the two waste holes land very nearly where each
other were — 2.50, 74.29 and 76.83, 2.50 on this piece. That near-symmetry is not an accident: they
sit in the two borders, as far apart as the stock allows.

**Getting it wrong puts every later file on the wrong face or the wrong way round**, and the stock
is the thing everything else is measured from. That is why this is not dropped however short the
sprint runs.

**Three ideas, none chosen** — the entry lists them and the choice is the story's first task:

- **A chamfer sized for the piece** rather than a fixed 3 mm: a proportion of the shorter edge, with
  a floor and a ceiling. Costs nothing and cuts in the pass that is already running.
- **A second, smaller chamfer** on a neighbouring corner, so the pattern of corners differs from
  every angle. Unambiguous under 180° *and* under a flip — the flip being the one the datum corner
  cannot catch today.
- **A notch in a waste edge**, top or right only, never on the two datum edges.

**Done when** a near-square piece can be oriented at a glance from across the bench, the choice is
unambiguous under both a 180° turn and a flip, the stock program says what the mark means, and
there is a photograph of the real piece rather than only a render.

### Set aside — 2026-10-11, by the product owner

**Not built, and not dropped either: left until it turns out to be a bigger problem.** This was the
story the plan said would never be dropped. The product owner set it aside, and that decision
outranks the plan's line about it.

**What came out of looking at it,** so the next attempt starts from here:

- **One chamfer cannot catch a flip on a near-square piece, whatever its size.** Turned over and
  given a quarter turn, the chamfered corner goes straight back into the stop. On an oblong piece
  the quarter turn is obvious; on 78.63 × 76.09 it is 2.5 mm.
- **A second chamfer only works on a neighbouring corner** — on the diagonal, a flip about that
  diagonal reproduces the pattern — **and both neighbouring corners hold an alignment hole**,
  1.8 mm in from the edge, because the holes are as far apart as the stock allows. A second
  chamfer means moving a hole, which makes every stock drilled before it disagree with the export.
- **Sizing the chamfer to the piece was proposed and declined:** *"I'm ok with the chamfer as it is.
  There is nothing wrong with it. The problem I had can not be solved by changing the chamfer size."*
  The trouble at the bench was not that the corner was hard to see.
- And: *"I think maybe we are overthinking/overworking this."* Worth taking as the guide for when it
  comes back.

V32 stays **Not started** in 08, and 06 §6.20 stays open with this beside it.

---

## Story 4 — Marks that survive what comes between

**Problem.** M16 is Partial: the stock's two waste holes are the milled case, cut and measured.
Fiducials in copper, and the rules about what survives which workflow, are not built.

**Fiducial design matters more than the maths**, which is 04 §4.2's own heading and the reason this
is a story of its own rather than part of the fit. The mark has to survive every process step
between making it and measuring it:

| Workflow | Fiducial that survives |
|---|---|
| Laser mask → acid etch | A **copper island** — a filled donut or a cross — that the resist protects, so etching *creates* it in relief. A burned-away-mask mark would be destroyed by the etch |
| Any workflow with a mill step | **Drilled holes**, 2–3 of them, Ø 1.5–3 mm, outside the outline but in the stock. Probeable, scope-visible, survives everything |
| Mill-first | An **engraved cross**, V-bit, 0.3 mm deep — easy to centre a crosshair on, and probeable if cut as a small conical dimple |

**The default is two drilled holes and one copper cross**, all in the waste frame outside the
outline, placed as far apart as the stock allows — a long baseline is a low angular error — and
never collinear when three are used.

**Done when** the application places the default three for a board whose workflow it knows, refuses
rather than guesses when the workflow would destroy the mark it would otherwise choose, the
companion page shows where they are and says which process each survives, and the nominal
coordinates are stated for an operator to go and find.

---

## Story 5 — A fit from three or more marks, and what it cost

**Problem.** M17 is Partial: `RigidFit.Solve` handles the two-point rigid case with the separation
error as its residual. Three or more points, and the SVD behind a proper Kabsch fit, are not built.

**Measurement happens in the operator's sender, not here** —
[01 §1.1](../Documentation/01-Architecture.md). The workflow is numbers in, G-code out: the application states the nominal coordinates, the
operator jogs to each mark in whatever sender they already run and reads the DRO, types the
measured X/Y back, and the application fits the transform, reports the residual and emits
re-registered G-code.

**Make the numeric part painless, since it is the whole interface.** 04 §4.2 is specific about
what that means, and each clause is a thing to build:

- Paste-friendly input: accept `X12.345 Y67.890`, tab-separated pairs, or a pasted GRBL `?` status
  line, and parse it.
- A per-fiducial residual shown **live**, as the numbers are entered.
- A clear *"this is worse than your last alignment"* warning, which is the one that needs the
  previous fit kept with the project.

**Done when** three or more measured marks produce a fit, the per-fiducial residual appears as each
pair is typed, a worse-than-last-time result says so in those words, and the whole thing has been
run once on a real piece of stock with real marks — numbers read off the machine, not invented.

**This is the story most likely to be dropped**, and it can be: story 4 is useful without it,
because the two-point fit already built consumes the marks it places.

---

## Story 6 — The findings view, at half the cost and interruptible

**Problem.** *Job ▸ Findings* takes about **1.5 s** on a two-sided Arduino Mega. Roughly half of
that is work already done, and almost none of it can be cancelled. Found by `/code-review` at
xhigh, 2026-09-30, and never answered.

**The whole layer is offset and separated twice.** `ElectricalCheck.Isolation` computes
`Separate(copper)` and `Separate(Inflate(copper, reach))`; `Gaps`, called on the next line with the
same copper and the same options, computes both again. Byte-identical inputs, identical results,
and the round offset of a dense layer is the dominant cost. **Sprint 3 added an `already` parameter
to avoid re-running the cheaper half of this and left the expensive half duplicated**, which is the
detail worth keeping: the fix was started and stopped halfway.

**The token is read in the wrong place.** `Gaps` documents itself as *"checked per candidate pair;
a cancelled run returns what it has"*, and `BoardFindings` sells the view as cancellable. But both
whole-layer Clipper operations, both box passes and the attribution loops run before the first
token read. On a board that is mostly fine, most regions hold one piece, the pair loop is skipped
entirely and the token is **effectively never read** — so pressing F7 twice means waiting out the
first run in full.

**And the window builds itself synchronously**, adding up to `Joins × 40` TextBlocks to a plain
`StackPanel` in its constructor, before `Show`, with no virtualisation. Groups are uncapped on
purpose — that is what the window is for.

**Worth more now than when it was measured.** The findings view has since become the front door to
two other features: 6.44 reads it and 6.56 navigates from it. The second and a half is paid more
often than it was.

**Done when** the Mega's findings come back in appreciably less than 1.5 s with the figure
measured and quoted, pressing F7 twice abandons the first run rather than waiting it out, and the
window opens without a visible pause on a board with a hundred groups.

---

## Deliberately out

**[6.45](../Documentation/06-Roadmap-and-Risks.md#645-block-apertures-and-the-aperture-transforms--defect--open--no-fixture-exists-yet) — block apertures.**
Still no fixture. It cannot start, and the unblocker is a Gerber set rather than a line of code.

**[6.47](../Documentation/06-Roadmap-and-Risks.md#647-isolation-cuts-inside-a-hole-that-is-about-to-be-drilled--defect--open--found-in-the-workshop-low-priority-a-later-sprint) — isolation inside a hole.**
Built in sprint 3 and withdrawn by the product owner, because the fix made two nets' separation
depend on the drill. The patch is kept in `WorkingFolder/held/`; it should not be re-attempted in
that shape.

**[6.31](../Documentation/06-Roadmap-and-Risks.md#631-let-the-operator-say-what-a-layer-is--enhancement--open--asked-for-by-the-product-owner) — let the operator say what a layer is.**
Asked for by the product owner and **not in this sprint**, which is worth stating plainly because it
is the cheapest workshop-sourced item on the list: every part of the mechanism exists except a
picker. It is the first reserve if this sprint turns out shorter than it looks, and the first
candidate for sprint 6 if it does not.

**[6.35](../Documentation/06-Roadmap-and-Risks.md#635-a-knife-blade-as-a-tool-for-vinyl-masking--enhancement--open--asked-for-by-the-product-owner) — a knife blade as a tool.**
Also asked for, also out. It is a third answer for the soldermask layer and this sprint is already
carrying the laser's two.

**The features nobody has asked for recently.** 6.2 climb or conventional, 6.18 the 3D-printed
paste stencil, 6.21 every hole approached from the same side. None is on the held list and none came
from a board being cut.

**[6.54](../Documentation/06-Roadmap-and-Risks.md#654-the-checks-panel-cannot-show-a-cancelled-run--enhancement--open--found-by-code-review-2026-09-29) — the checks panel cannot show a cancelled run.**
The other half of the review debt, left out so that 6.55 is the only story here nobody asked for.
Small, and it pairs naturally with 6.55's cancellation work — fold it in if story 6 turns out to be
shorter than it reads.
