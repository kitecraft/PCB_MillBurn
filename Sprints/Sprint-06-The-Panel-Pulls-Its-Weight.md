# Sprint 6 — The panel pulls its weight

**Agreed** 2026-10-16. **Release branch:** `release/0.7.0`, branched from `main` with v0.6.0
tagged — and carrying the two record-keeping commits made since the release and deliberately not
pushed, cf47dd4 (6.71's measurement) and d8c9c52 (6.73, the pick-and-place research), so that they
ride out with the sprint.

**The product owner's direction, in the order it arrived.** First, the day before the picking,
while 6.71's measurement was being committed: *"It'll be the first story for the next sprint."* So
story 1 was named before the slate existed. Then, at the picking, two set-backs that shaped it:
6.18 is *"still more 'wish list' than actual enhancement request"*, and 6.73 has the same standing
— both entries carry the dated paragraph. And one fold, the owner's own suggestion: *"6.4 - tab
drag -- tell me more about this. It may fold the bug into it."* It does. 6.72 and 6.4's manual
placement are one story here.

**The theme is the panel.** Two of the four stories exist because the 66-up GridStripConnector
panel — the README's own hero — is the first board big enough to break rules tuned on single
boards. Its preview draws at 14 fps on an application whose README claims 98 through a zoom sweep,
because nothing before it stroked a 108,504-vertex path every frame ([6.71](../Documentation/06-Roadmap-and-Risks.md#671-the-66-up-panel-draws-at-about-14-fps-where-the-readmes-hero-showed-37--enhancement--fixed--found-retaking-the-readmes-screenshots-2026-10-11-fixed-in-sprint-6)).
Its tabs all land on one edge, because *longest edge first* was written for a rectangle that has
four of them ([6.72](../Documentation/06-Roadmap-and-Risks.md#672-on-a-panel-every-tab-goes-on-one-edge--defect--fixed--found-in-the-workshop-2026-10-11-fixed-in-sprint-6)).
Both were found on 2026-10-11, one retaking the hero and one in the workshop looking at it. The
third story is the half of the tab fix the panel will still need when the planner is right —
the hand — and the fourth is review debt the sprint has room for, said plainly the way sprint 5
said it of 6.55.

---

## How this sprint was picked

**The defect list first**, as the standing rule requires:

```
grep -n 'defect · open' Documentation/06-Roadmap-and-Risks.md
```

**Three came back, and one is in — the first defect to enter a slate since sprint 4.**

| # | what | what happened to it |
|---|---|---|
| [6.45](../Documentation/06-Roadmap-and-Risks.md#645-block-apertures-and-the-aperture-transforms--defect--open--no-fixture-exists-yet) | Block apertures and the aperture transforms | **Out**, as it has been every sprint since 2: no fixture exists, and the unblocker is a Gerber set rather than a line of code |
| [6.47](../Documentation/06-Roadmap-and-Risks.md#647-isolation-cuts-inside-a-hole-that-is-about-to-be-drilled--defect--open--found-in-the-workshop-low-priority-a-later-sprint) | Isolation cuts inside a hole that is about to be drilled | **Out**: built in sprint 3 and withdrawn by the product owner; the patch is kept in `WorkingFolder/held/` and the shape is still wrong |
| [6.72](../Documentation/06-Roadmap-and-Risks.md#672-on-a-panel-every-tab-goes-on-one-edge--defect--fixed--found-in-the-workshop-2026-10-11-fixed-in-sprint-6) | On a panel, every tab goes on one edge | **In — story 2**, folded with 6.4's manual placement at the product owner's suggestion |

**Then the other places the rule names.**
[08](../Documentation/08-Requirements-Matrix.md)'s Partial rows: M16 and M17 were set aside by the
product owner in sprint 5 before anything was cut, and nothing has changed at the bench since;
G24 — *tabs placed where a hand would put them... and movable one at a time* — is exactly story 2,
and its Partial note already reads *"Dragging a tab on the board is not built"*. M3, V13, V16 and
V34 are each missing a part nobody has asked for since the row was written.
[09](../Documentation/09-Machine-Accuracy-Investigations.md)'s open item is still the laser
gantry's squareness, which is a frame to be shimmed and not a line of code.

**And the fourth story comes from its own entry's standard.**
[6.50](../Documentation/06-Roadmap-and-Risks.md#650-nothing-can-test-the-view-model--enhancement--fixed--three-times-in-one-sprint)
was dropped from sprint 5 with one row left — *when a setting is persisted* — and the closing
argument *"the same shape, which is the argument for doing it next rather than for doing it now"*.
Next is now, and the sprint carrying a renderer story and an interaction story has room for the
careful one.

### And the four

| # | story | where it came from |
|---|---|---|
| [6.71](../Documentation/06-Roadmap-and-Risks.md#671-the-66-up-panel-draws-at-about-14-fps-where-the-readmes-hero-showed-37--enhancement--fixed--found-retaking-the-readmes-screenshots-2026-10-11-fixed-in-sprint-6) | The panel previews at a rate the README can stand beside | Retaking the hero, 2026-10-11; measured 2026-10-15; named first by the product owner |
| [6.72](../Documentation/06-Roadmap-and-Risks.md#672-on-a-panel-every-tab-goes-on-one-edge--defect--fixed--found-in-the-workshop-2026-10-11-fixed-in-sprint-6) + [6.4](../Documentation/06-Roadmap-and-Risks.md#64-tabs-where-how-many-how-big--enhancement--partial--placed-and-sized-in-sprint-4-moved-by-hand-in-sprint-6-keep-outs-are-not-built) | Tabs spread round the panel, and a hand can move one | The workshop, 2026-10-11, on the same panel; folded at the picking, 2026-10-16 |
| [6.54](../Documentation/06-Roadmap-and-Risks.md#654-the-checks-panel-cannot-show-a-cancelled-run--enhancement--fixed--found-by-code-review-2026-09-29-fixed-in-sprint-6) | A stopped check says so | `/code-review`, 2026-09-29; the other half of sprint 5's review debt |
| [6.50](../Documentation/06-Roadmap-and-Risks.md#650-nothing-can-test-the-view-model--enhancement--fixed--three-times-in-one-sprint) | When a setting is persisted, said once | Three faults in one sprint; the settings row is the one sprint 5 left |
| [6.4](../Documentation/06-Roadmap-and-Risks.md#64-tabs-where-how-many-how-big--enhancement--partial--placed-and-sized-in-sprint-4-moved-by-hand-in-sprint-6-keep-outs-are-not-built) | Tabs by letter, and one tab's width at a click | **Added mid-sprint** by the product owner, 2026-10-18, after story 2 reached the bench |
| [6.74](../Documentation/06-Roadmap-and-Risks.md#674-hand-set-tab-changes-could-outlive-the-plan-they-numbered--defect--fixed--found-by-sprint-6s-xhigh-review-2026-10-18-fixed-in-sprint-6-story-6) + the debt | The sprint closes clean | **Added at the close** by the product owner, 2026-10-18: the sprint-wide `xhigh` review's findings, with the stories' own left-overs folded in — *"I really don't like tech debt"* |

**The order is the owner's first, then by risk.** 6.71 was named before the picking opened. Story
2 is second because it holds the sprint's only defect, and the defect is its first half. 6.54 is
small and third. 6.50 is last because it is a refactor of the largest file in the repository, its
own entry warns that doing it badly would be worse than the gap, and last is where a story like
that can be dropped whole.

### What was dropped, and why

| # | what | why it is not here |
|---|---|---|
| [6.18](../Documentation/06-Roadmap-and-Risks.md#618-a-paste-stencil-to-3d-print--enhancement--open) | The 3D-printed paste stencil | **Set back by the product owner at the picking**: *"still more 'wish list' than actual enhancement request."* The workshop ranking that put it ahead of the paste extruder stands; what changed is the reading of the ask. Back in when it comes back harder |
| [6.73](../Documentation/06-Roadmap-and-Risks.md#673-placement-data-for-a-pick-and-place--enhancement--open--asked-for-by-the-product-owner-2026-10-16) | Placement data for a pick-and-place | **The same standing, the same day.** The research is a day old, no machine is on the bench, and the entry's own *"big IF"* has not been answered. The entry exists so the thinking is not re-done when it is |
| M16 + M17 | Fiducials and the three-or-more fit | Set aside by the owner in sprint 5 before anything was cut — the two waste holes already survive the workshop's workflow — and both can only close at the bench. Nothing has changed at the bench since |

---

## Size, and what gets dropped first

Four stories, but five halves: story 2 is a defect fix and a new interaction, ordered inside the
story so the fix can ship alone. The fix half is an algorithm change in `TabPlan` with the test
rig already around it; the drag half is the sprint's genuinely new machinery — the viewport has
never hit-tested anything. 6.71 is a renderer change with a proven template to copy. 6.54 is the
smallest. 6.50 is a careful refactor whose size is set by where the seam stops paying.

**What never drops: 6.71 and the fix half of story 2.** The first was named by the owner before
the sprint existed; the second is the defect, and a slate that drops its only defect was picked
wrong.

**The drag half of story 2 goes first** if something has to go. The fixed planner is useful
without it — that is what *fix first* inside the story means — and the drag is the half with the
unknown in it. **Then 6.50, whole**: review debt with its own warning label, and dropping half a
refactor of the largest file is not a thing. **Then 6.54.**

---

## How a story closes

As in sprints 1 to 5: a `Closed` block with the date, a plain verdict against *Done when*, the
manual test in enough detail that somebody else could run a similar one, what was observed
including the sentence from the bench, and what is left open. A story without one is still open
whatever its branch says. A defect closes with the test that would have caught it. A story that
changes what a surface looks like closes with a picture, and a performance claim closes with the
numbers quoted, measured the way the claim was.

**And two notes for this sprint.** 6.71 closes on the status bar in the same shot the README
uses — the hero recipe, frame time read off the PNG — against the measured 69 ms, with `--bench`
and `--fpstest` re-run to show the fix did not spend what they prove. And story 2's drag cannot
close headlessly: a slide along the border is a feel as much as a function, and the headless flag
photographs the result, not the grab. Plan for a hand on a mouse before calling it done.

---

## Story 1 — The panel previews at a rate the README can stand beside (6.71)

**Problem.** The hero — the GridStripConnector panel previewed at 1600 × 1000 — draws at about
**69 ms a frame, 14 fps**, where the September shot of the *same code* read 27 ms. Measured on
2026-10-15, and every suspect cleared: not any sprint (v0.2.0 through `main` are indistinguishable
today), not the rulers, not the CPU raster (`--bench` still 5.35 ms mean), not the GPU loop
(`--fpstest` still 98.2 fps). The environmental trigger was not found; what it exposed is real
either way: **93 % of the frame is two anti-aliased monolithic stroked `SKPath`s** — the previewed
isolation program, 108,504 vertices, 48.8 ms, and the panel's outline, 42,532 vertices, 14.2 ms.
Fills are ~8× cheaper a vertex; the cost does not move with resolution. Turning AA off was
measured and is not the fix: 43 ms, 1.6×.

**What.** Draw the preview layers the way `ToolpathRenderer` already draws toolpaths — LOD tiers,
spatial culling, the stroke configuration the Phase 0 probe chose — which is the application's own
proof the cost is unnecessary: 500,247 segments at 98 fps. The scope is bounded by the
measurement: only a large panel *with its programs previewed* is slow (panel without preview
17 ms, single board with preview 1.9 ms), and exports, the CLI and every emitted file are
untouched by construction — this is the viewer, nothing downstream of it.

**Done when** the hero recipe — the panel previewed, 1600 × 1000, `--shot` — reads a frame time
that belongs in the same README as the 98 fps claim, with the figure quoted against the 69 ms it
replaces; the picture is unchanged to the eye at working zooms; `--bench` and `--fpstest` still
read their documented numbers; and the single board and the un-previewed panel are no slower than
their measured 1.9 and 17 ms.

### Closed — 2026-10-17

**Met, with the figures quoted as ratios** — because the machine spent the day in the slow state
6.71's measurement first noticed, drawing the *old* code at 122–154 ms where the 15th measured 69.
Absolutes from this machine are weather; the before/after on the same day, built from adjacent
commits and shot alternately, is the claim:

- **Fit zoom: 122.3 / 154.0 ms → 38.1 / 42.1 ms** (6–8 fps → 24–26). **40×: 58.7 / 61.7 → 19.4 /
  20.5 ms** (16–17 fps → 49–52). Three to four times faster, both zooms, both rounds.
- **The picture is the same picture.** Native-resolution crops at fit and at 40×, before against
  after: indistinguishable — anti-aliased, full stroke widths, dashes intact
  (`WorkingFolder/V0.7.0/Screenshots/story-1-crop-*.png`, `story-1-zoom-*.png`).
- **`--bench` 5.19 ms mean / 19.45 p95** against documented 5.5 / 20; **`--fpstest` 94.0 fps,
  PASS** — and its sweep now reaches the new fifth tier at far zoom, 6 draw calls on the last
  frame.
- **The single board: 2.79 ms old, 2.80 new**, same session — the untiled path untouched. **The
  un-previewed panel: 23.3 → 10.3 ms**, *faster*, because the 42k-vertex outline is over the
  budget too.

**What was built.** `BoardScene` gives any stroked layer at or past 20,000 vertices
(`TiledStrokeBudget`) a `ToolpathScene` beside its monolithic path — the exact machinery the
toolpath view proved on 500k segments — and `BoardRenderer` draws it under the same rules: tier
for the zoom, tiles culled to the view, anti-aliasing and dashes while the in-view count holds
under the shared 25k budget, hairline past it. Two pieces were new. A **fifth LOD tier** (0.08 mm,
still under half a pixel at fit zoom), because the panel's fit view sat just past tier four's
reach and would otherwise have crossed the budget at the hero's own zoom — the one zoom the story
is named after. And a **budget-aware tier choice** (`BoardRenderer.TiledTier`), found by re-shooting
after the review: the fit zoom sat *exactly* on the tier boundary, and a shot whose side panel came
up a few pixels narrower flipped the hero to the degraded look. A frame whose half-pixel tier busts
the budget now relaxes to at most one pixel of simplification error — nearly invisible — before
giving up anti-aliasing and stroke widths, which is not; proved by re-shooting the window state
that degraded, and by `TheTierRelaxesByAtMostOnePixelBeforeGoingHairline`. Fills and under-budget
strokes keep the exact code path they had.

**The manual test.** The hero recipe on both builds, alternated twice, plus `--zoom 40,82,53`;
status bars read off the PNGs (`story-1-measurements.png`, `story-1-scope.png`); crops compared at
native resolution. The status bar's vertex count now reports what the frame actually drew — 77,633
at fit against 205,620 before — which is the honest number for the next hero to quote.

**Not done, and said rather than implied.** The README's hero image itself is not retaken here —
that is release work, when the whole README is reshot against whatever state the machine is in
that day. And the environmental trigger that doubles this machine's frame times some days remains
unidentified and now matters less: it was only ever expensive because the preview stroked 108k
vertices through one path, and it no longer does.

### What `/describe-test` found — 2026-10-17, five runs

One per file, and again each time a file gained something, as the rule requires:
`BoardStrokeTileTests` three times (it gained the bounds assertion, then the ladder test),
`ToolpathSceneTests` and `DeterminismTests` once each. Two catches, both fixed before the review
ran: the segment-parity test compared only *counts*, so tiles mirrored by a dropped Y flip would
have passed — it now pins the tile extent against the monolithic path's bounds; and the fifth
tier existed while every content test still looped tiers 0–3, so the new tier had no
bounds-to-origin, monotonicity or determinism coverage — the loops now run over all of them. The
last read confirmed the ladder test pins the one-pixel allowance from both sides (0.6 px must be
allowed, 1.2 px must not be) and noted honestly what the file leans on: the renderer's reported
counters, tied to real tile contents by the parity test, not pixels.

### What the code review found — 2026-10-17, medium

Eight findings; seven acted on, one left with a reason.

- **The benchmark lied about what it measured.** `--bench` printed *"LOD build 4 tiers"* and
  looped four, and the `--fpstest` window said *"4 LOD tiers built"*, while five were built and
  timed. All three sites — and the three test loops with the same literal — now read
  `ToolpathScene.TierCount`, which went public for exactly this: the sixth tier cannot go
  unprinted or untested.
- **The culling bookkeeping was paid twice.** Each tiled layer's `SegmentsInView` ran once for
  the detail budget and again for the drawn count, per frame, on the path this story exists to
  cheapen. The pre-pass now keeps the per-layer counts and the draw loop reads them; an ordinary
  board allocates nothing.
- **`TilePaths` threw on the layers that are the common case.** The null-forgiving dereference
  meant any caller walking every layer's geometry — which is what story 2's hit-testing is about
  to do — would hit a bare NRE on every under-budget layer. It returns empty now, and says so.
- **Document 05 kept a four-tier figure under a five-tier heading.** The 94k fit-zoom count was
  tier 3's; the sentence now says which measurement produced which number.
- **The unrecorded `/describe-test` runs** were a bookkeeping gap, not a process one — the runs
  had happened and their catches were already fixed; the section above is the record.
- **Left, with a reason — dash phase restarts at tile seams.** True, and it is the behaviour the
  toolpath view has always had for its dashed travel styles, drawn from these same tiles. No
  stroked board layer in the corpus is both dashed and over the 20k budget; the renderer carries
  a comment saying what will happen when one arrives, so the decision gets made then, on a real
  layer, instead of now on a hypothetical one.
- **Left half-way — the status bar's drawn count mixes meanings**, segments-in-view for tiled
  layers against whole-path vertices for the rest. Both are honest counts of what was *submitted*
  to the canvas, which the comment now states; an untiled layer's whole path is submitted
  whatever the zoom, so its full count is not a lie, just a different question answered on the
  same line.

---

## Story 2 — Tabs spread round the panel, and a hand can move one (6.72 + 6.4)

**Problem, in the workshop's words.** On the panel: *"all the tabs end up along the bottom edge of
the 'board'. Up to 6 tabs. Then, 7 - 12 along the top."* Reproduced: four tabs by default, every
one on a 24.6 mm edge at y = −0.40 mm — all on the bottom. And from the request that opened 6.4:
*"They are large tabs and may not be in the best locations."* Sprint 4 answered the sizes and the
rectangle; a panel's outline is not a rectangle — it is many short straight stretches between the
boards, the longest of which happen to lie along one side, and *longest edge first* is blind to
where an edge is.

**The fix half, first.** `TabPlan`'s edge selection gains *where* as the first key — the quarter
of the perimeter an edge falls in, or its bearing round the board's centre — and length as the
second, so the first four tabs land on four different sides of anything that has four sides. The
constraint the entry sets: **the 21 × 37 mm board 6.4 was tuned on must not move**, proved by its
golden programs, and the blank keeps placing its own tabs on the top and right only. The defect
closes with the test that would have caught it: one that fails if the panel's default tabs
collapse back onto one edge.

### The fix half — landed 2026-10-17

The key is which **side of the profile an edge faces**: the edge's own direction decides
top-and-bottom against left-and-right, its position against the centre decides which of the two,
and `TabPlan.Allocate` fills the emptiest side first, a bare edge within it second, length per tab
last. Two findings on the way, both caught by running against the real panel frame before any of
it was called done:

- **A bearing round the centre is not a side.** The first cut quantised each edge's midpoint
  bearing at the bounding box's diagonals — and the panel's frame is wider than tall, so a top
  stretch near a corner lies past the diagonal and classified as "right". Four tabs went one to
  each *bearing* and three of them sat on the top side: the defect back again in a hat.
- **The panel's right answer is two-and-two, not one-per-side.** The frame's left and right sides
  are 6.8 mm stretches between the boards' channels; a 5 mm tab — the width a 107 mm board
  derives — needs 15 mm of edge for its clearance either side, and the `Fits` rule refusing those
  stretches is correct. So the default four hold the frame in opposite pairs on distinct bottom
  and top stretches, and the regression test asserts exactly that.

`TabPlacementTests.OnThePanelTheDefaultTabsGoRoundTheFrame` loads the real panel, red on the old
rule with all four tabs on one 24.6 mm edge. The 21 × 37 mm board's programs are byte-identical —
only the panel's golden moved, its cutting unchanged to the millimetre and its rapid travel down
from 521 to 382 mm because the lap entry points move with the first tab on each profile. The
blank's top-and-right-only tabs are `BlankOperation`'s own and untouched. 1,383 unit + 28 golden
tests pass, zero warnings. The shot of the spread tabs lands with the drag half, which is what
makes tabs visible in the viewport at all.

**The drag half, second — the part of 6.4 that answers the workshop completely.** However good
the spread rule gets, the next odd outline will put one tab over a slot or against a connector,
and today the only recourse is fighting the planner. So:

- **The viewport learns to hit-test the offset profile** — the cut path, not the board edge. This
  is the viewport's first pick of any kind; 6.56's marker points, it does not grab.
- **A drag is a constrained slide along the profile's arc**, the pointer projected onto the
  nearest profile point — a tab exists only on the outline, and `SplitForTabs` already works
  purely on arc length, so a position *is* "how far along the loop". Around a corner is allowed;
  off the outline is not possible.
- **Click an empty stretch of outline to add a tab there; a gesture removes one.**
- **The blank's datum edges refuse a drop** — bottom and left must be cut clean, and a tab stub
  there stops the piece seating, invisibly. Refused and said, not snapped somewhere else.
- **Hand-set positions live beside the hand-set sizes and clear with them.** The six faults
  `/code-review` found in the size edits are the map: a changed count, board-wide width or outline
  cutter re-plans the tabs, so hand positions clear with the plan they belonged to, reversibly —
  never landing on whichever tab then sorts into that slot.
- **The window and the viewport agree because both ask the planner** — `PlanTabs`,
  `OutlineOptionsFor`, positions through `Placed` — and a headless flag in the `--set-role`
  family moves a tab by name and arc so a shot can photograph the result and the
  *unsaved changes* line.

**And a direction to record, not build**: the owner, on seeing what the pick needs — *"Adding this
interaction with the user and the viewew opens up other possibilites I think."* The hit-testing
gets built general for exactly that reason; what it opens (click a trace to ask what it is, a
finding picked on the copper, item 4's keep-outs drawn by hand, a component picked in 6.73's
assembly tab) queues in the roadmap, not in this story.

**Done when** the panel's default tabs land spread round the outline, with the shot to prove it
and the test that fails if they regress; the 21 × 37 mm board's programs are unchanged; a tab in
the viewport can be grabbed and slid along the border with the gap, the preview and the sizes
window following; a drop on the blank's datum edges refuses and says why; a hand-set position
survives save and reopen, clears when its plan does, and the program's header tells the truth
throughout. G24 moves from Partial when the whole of this is met.

### Closed — 2026-10-17

Both halves landed; the fix half's own record is above. The drag half, as built:

- **The pick and the slide.** The cut path is drawn as a faint dashed amber loop with a handle on
  every tab — hollow for the planner's, solid for a hand's — and the gap's two ends ticked across
  the path in cyan once the zoom makes the gap wider than the handle. A handle under the pointer lights and the cursor offers
  a hand; a grabbed tab slides to the nearest point of the loop whatever the pointer does, which
  carries it round corners and makes "off the outline" impossible rather than refused. Escape or a
  lost capture drops it back — a position the hand never released is a place nobody chose. Click
  an empty stretch to add a tab at the board's own sizes; right-click a handle to remove it — the
  last one refuses, with the reason in the status line: the last tab is what holds the board, and
  cutting it free belongs to the count.
- **The stored form** is `TabMove` — one record, three shapes (move / remove / add), named by the
  tab's place in the pristine plan exactly as `TabEdit` names sizes, applied after them in
  `TabPlan.For`, and never re-deriving ordinals from the list, so an override cannot land on
  whichever tab later sorts into a slot. It lives in `LayerOutputSettings` beside the sizes, with
  no format bump, and clears through every door the sizes clear through — count, board-wide
  width, outline cutter — **plus the CLI's `--tabs` and `--tab-width`, which had never cleared
  what they re-plan, for the sizes either**. `ProfilePath` is the planner's walk made public:
  `Project` (pointer → arc length) and `PointAt` (arc length → point), one copy of the
  arithmetic.
- **The header, the window, the flag.** The placement line counts what each rule chose ("3
  centred on …; 1 placed by hand"); a shortfall with hand changes says so instead of blaming
  clearance; every tab removed says that, not "no profile takes tabs". The tabs window's rows
  read *placed by hand*, key their size edits by ordinal (row position stopped being the name the
  day a tab could be removed), and disable the boxes on an added tab rather than silently
  dropping a number. `--move-tab <n>=<mm>` moves a tab by name and arc headlessly and prints what
  it did or why not; the shots below are taken with it.
- **A defect found when a moved tab vanished across a preview, older than this story.**
  `LayerRow`'s restoring constructor assigns the cutter after the stored overrides, and the
  cutter's clearing door fires during construction — so every rebuild of the rows (every preview,
  every reopened project) wiped the operator's per-tab *sizes* while the file still held them.
  Proven with a console probe (settings carried the move; the rebuilt row did not), fixed with a
  built flag on the clearing doors, re-proven the same way. Unreachable from the unit suite —
  `LayerRow` is Avalonia-side, which is story 4's 6.50 complaint in one sentence.
- **The datum-edge clause landed as a property, not a refusal.** A board tab exists only on the
  board's cut loop, and `Blanks.Resolve` refuses any border under cutter + 2 mm, so no point of
  the loop can lie on the blank's bottom or left edge — the projection cannot produce the place
  the rule forbids, and there is nothing to refuse or say. The blank's own tabs stay
  `BlankOperation`'s, top and right only, not draggable. **This diverges from the story's bullet
  as written** ("refused and said"), which imagined a drop that cannot occur; flagged for the
  owner at review rather than building an unreachable refusal — story 3 exists because this
  codebase does not keep those.

**Measured and shot.** `story-2-panel-tabs-spread.png`: the panel's four default tabs holding
opposite sides on distinct stretches, handles on the dashed loop.
`story-2-tab-moved-by-hand.png`: `--move-tab 1=200` — the tab solid on the frame's right edge, a
place the planner's clearance rule would never choose and a hand may, `unsaved changes: yes`
below. `story-2-tab-moved-previewed.png`: the same move previewed, the handle riding the cut path
at 10 px/mm. The panel goldens moved once, for the fix half (travel 521 → 382 mm, cutting to the
millimetre); the drag half moved none. 1,406 unit + 28 golden tests, zero warnings.

**From the bench at review, 2026-10-17, acted on before the merge.** The owner, on the first
look: *"I like it. On any non-horizontal edge, it's great, but on a horizontal edge, it'd
difficult to notice, and because the bar takes up the full gap width, it's also difficult to tell
the gap"* — and, on the fix: *"A contrasting colour might be nice."* The first cut drew the gap's
width as a bar through the handle, always horizontal because nothing told it which way the edge
ran: on a vertical edge it happened to cross the path and read, on a horizontal one it lay along
the cut and painted over the gap it named. Replaced with two cyan ticks across the path where the
cut stops and resumes — orientation-independent, the gap between them left unpainted so what shows
is the board's own cut, and in a colour nothing else in the viewport wears. The ends are walked
along the loop (corners included) by the same arithmetic the splitter cuts with, and they hide
during a drag, when the planner has no answer yet.
`story-2-gap-ticks-horizontal-edge.png` is the horizontal-edge case at 25 px/mm. The owner, on
the reworked picture: *"That is the best tabs locator I've ever seen."* The story merged on that.

**Not done, and said:** resizing an added tab (it takes the board's own sizes; the window's boxes
are disabled for it, and the day it earns naming one, the add entry is where the size goes);
dragging the blank's own tabs; item 4's keep-outs. The pointer gestures themselves — press, slide,
release — are exercised through the same `ApplyTabMove` path the headless flag writes, but no test
drives Avalonia's pointer events; the owner's hand is the test of the feel.

### What `/describe-test` found — 2026-10-17, six runs

- **`TabPlacementTests`, first run** (after the fix half): the panel test's name promised "round
  the frame" while the body proves opposite-pair holding — renamed
  `OnThePanelTheDefaultTabsHoldOppositeSidesOfTheFrame`; and an orphaned doc comment above
  `PointAlong` belonged to `EitherSide` — moved. **Second run** (after the drag half): clean; its
  account of test 18 matches the intent exactly, including that the two-and-two answer is this
  fixture's own.
- **`TabMoveTests`**: the added tab's sizes were asserted only against the derived-size functions
  — the system agreeing with itself if the formula moved. The literal figures (2.0 mm, 0.25 mm)
  now sit beside the function calls.
- **`ProfilePathTests`**: clean — the describer re-derived every hand-worked coordinate
  independently and confirmed them, and correctly placed the round-trip test as consistency, with
  absolutes pinned by the landing and projection tests.
- **`LayerOutputPersistenceTests`**: clean — the `TabMoves` round-trip is a real disk round trip,
  each of the three shapes intact, the removal's null read back as null.
- **`TabMoveTests`, second run** after it gained the cross-profile leak test below: clean.

### What the code review found — 2026-10-17, medium

Seven findings; five acted on, one left with its reason, one already met.

1. **A stationary middle-click added a tab** — the click-to-add check never asked which button
   the press was. Guarded on the left button; the middle button pans, everywhere. *Fixed.*
2. **Hand removals that empty the first profile's plan leaked the same removals onto the next
   profile** — the `editsSpent` latch watched the post-removal list, so a file removing every one
   of profile one's ordinals removed the second board's tabs by the same numbers, and both pieces
   come free on the last pass. The latch now re-plans pristine in exactly that corner and spends
   the overrides where they belong. *Fixed, with the regression test
   `RemovingEveryTabOnOneProfileLeavesTheNextProfilesAlone`, proven red against the unfixed
   latch.*
3. **The CLI's `--tabs`/`--tab-width` cleared overrides even when the value was unchanged** — a
   script re-running with the same flag threw hand-set positions away for a no-op. Both compare
   before clearing now, as the window does. *Fixed.*
4. **`RefreshTabHandles` re-shaped the whole board on every settings change of any layer, on the
   UI thread** — a drill-guide checkbox re-offsetting the panel's 102 profiles. A signature of
   what the tabs actually depend on (board, outline settings record, thickness, frame) now answers
   "nothing moved" without planning. *Fixed.*
5. **`BoardView.NearestOnProfile` is a second projection beside `ProfilePath.Project`** — true,
   and kept: it answers in screen pixels over the overlay's own polyline, which is all the control
   is allowed to know (the Viewer's overlay type cannot carry a Cam type), and every *committed*
   position still goes through the planner's walk. The reason is now written at the method.
   *Left.*
6. **The hot handle and hand cursor survived the pointer leaving the viewport** — a lit handle
   under a pointer that is over the settings panel. Cleared in `OnPointerExited`. *Fixed.*
7. **Four test files owed `/describe-test` runs** — they had run before the review; the section
   above records all six. *Already met.*

---

## Story 3 — A stopped check says so (6.54)

**Problem.** `BoardCheck.Result` and `BoardFindings.Result` both carry a `Cancelled` flag,
`MainViewModel` has the status line — *"Check stopped; the board was not fully checked."* — and
`FindingsWindow` has a banner saying what is below is part of the answer. **None of the three can
be reached**: `LatestRun.Finish(token)` returns false for any cancelled run, both callers return
at that point before reading `Cancelled`, and nothing calls `Cancel` anyway — the only
cancellation is supersession by a later press, where returning early is *correct*. A check that
can never fire is a check nobody can trust the absence of.

**What.** Give the operator a way to stop a run — the findings window closing mid-check is the
natural one, a control if one earns its place — and route it so a stop is distinguishable from
supersession: the newer run still owns the status line, and only a *stopped* run announces itself.
Sprint 5's 6.55 work made the token actually get read (119 ms worst-case response), so the stop is
real the moment it is wired.

**Done when** — the entry's own words — a run an operator stops is shown as stopped rather than as
clean, with a test that fails if the banner stops appearing.

### Closed — 2026-10-17

- **The verb was the missing piece, not the wiring.** `LatestRun.Stop()` cancels the current run
  *without disowning it*: a stopped run is still the latest, so its own `Finish` returns true and
  everything written on 2026-09-29 — the two status lines and the banner — becomes its to
  publish. `Cancel`'s contract is untouched (a superseded or abandoned run still publishes
  nothing), and a successor still wins: stop-then-begin refuses the stopped run's finish exactly
  as before. Three new tests in `CancellableWorkTests` pin the triangle — a stopped run may
  publish, stopping nothing says so, a successor still beats a stopped run — beside the `Cancel`
  tests whose deliberate opposite the first one is.
- **Escape is the control.** The story's sketch — *"the findings window closing mid-check is the
  natural one"* — turned out impossible: the window is only constructed after the search
  completes, so there is no window to close mid-run. While a board check or findings search runs,
  Escape in the main window stops it instead: wired in the bubbling shortcut handler and handled
  only when `StopChecks()` actually stopped something, so the key keeps its existing meanings (a
  dragged tab, the marker) and an idle Escape is never swallowed. `StopChecks` stops both runs,
  not whichever seems active, and writes no status — the stopped run announces itself on its way
  out, which also keeps a stop that raced a natural finish from mislabelling a complete answer.
- **Both halves of "stopped", each with its own sentence.** A stop that lands mid-search gets the
  polite partial: `BoardFindings.For` returns what it has with `Cancelled: true`, `Finish` says
  the run is still the latest, and the findings window opens over the partial list with the
  banner. A stop that lands before anything was found surfaces as the `OperationCanceledException`
  path, where `Finish` now distinguishes stop from supersession: stopped says *"Stopped before
  the search found anything to show."* / *"Check stopped; the board was not fully checked."*,
  superseded stays silent as it always has.
- **The banner's words moved onto the result** — `BoardFindings.Result.StoppedBanner`, the 6.51
  shape: condition and sentence reachable with no Avalonia in sight, the window showing what the
  result says of itself. The entry's *"test that fails if the banner stops appearing"* exists
  twice: asserted on a genuinely stopped run of the Mega (the existing part-way test now checks
  the banner rides the partial result) and on the record's own two branches. The window-side
  wiring is one line, photographed.
- **`--stop-findings`** starts the search and stops it 150 ms in, the way Escape does — the only
  headless route to the stopped window. `story-3-stopped-findings-banner.png`: the Mega's
  findings stopped at one layer of two, 0.79 s in, banner over the partial answer, the bottom
  copper's real findings beneath it. AGENTS.md's flag list gains it, and `--move-tab`, which
  story 2 owed it.

**Not done, and said:** no stop control was added for the *board check* beyond Escape — the check
is sub-second on every corpus board (0.50 s for the Mega's two layers, measured today), and a
button for it would be chrome nobody presses; Escape covers the long board that may yet arrive. The check-side stop line shares every moving part with
the findings path (same `Stop`, same `Finish` gate) but its literal sentence was not photographed:
reaching it needs Escape pressed inside a sub-second window, which is exactly the bench test the
owner can do with a key and cannot be scripted headlessly without a delay hook the code does not
deserve. 1,410 unit + 28 golden tests, zero warnings.

### What `/describe-test` found — 2026-10-17, two runs

- **`CancellableWorkTests`**: clean. The cold read names the one assertion that is the feature —
  a stopped run's `Finish` returning true, "the opposite of the cancel-without-successor test" —
  and confirms the triangle (may publish, nothing-to-stop says so, successor still wins) has no
  assertion the tests computed for themselves.
- **`BoardFindingsTests`**: clean. The read confirms the banner is asserted on a *genuinely*
  stopped run of the Mega as well as on the record's own two branches, and states the honest
  limit in its own words — that the window showing this exact string is outside any test's reach,
  which is what the `--stop-findings` shot is for.

### What the code review found — 2026-10-17, medium

Eight findings; seven acted on, one left with its reason.

1. **A stop before the first layer finished opened a window whose empty-state lied** — "no layer
   is set to be isolated" over a board whose layers are set exactly that way. The cancelled-empty
   result now stays out of the window entirely (same status line as the exceptional route — the
   same event, reached politely), and the window's empty-state guards on `!Cancelled` for any
   other caller. *Fixed.*
2. **`--stop-findings` could photograph a complete window under a flag promising a stopped one**
   on a machine fast enough to finish inside the 150 ms, and claimed "already finished" when
   nothing had run. The window now opens only for a genuinely cancelled result, and each other
   case prints what actually happened. *Fixed.*
3. **The summary line sat above the banner**, so a stopped run's first-read line was a verdict —
   the exact partial-read-as-complete the banner exists to prevent. Banner first now, verdict
   under the caveat. *Fixed, and the evidence shot retaken.*
4. **A stop whose cancellation surfaced as a non-`OperationCanceledException` read as "Check
   failed"** — the broad catches' `Finish` now also succeeds for stopped runs, so both ask the
   token before wording the line: the operator's own Escape is never reported as a failure.
   *Fixed, both paths.*
5. **`IsRunning`'s doc no longer described the implementation** — a stopped run is still the
   latest but reads false. The doc now says exactly that, and why a property cannot answer "has
   it said its piece". *Fixed.*
6. **One Escape over two concurrent runs races their two status lines** — whichever continuation
   lands last owns the bar, and the operator reads one "stopped" instead of two. *Left*: both
   sentences mean the same deliberate stop the operator just made, no false cleanliness survives
   (the check panel and window still tell the truth), and ordering two announcements would buy a
   rare second sentence at the cost of the one-run-one-voice rule everything else leans on.
7. **The stopped-window route was a third copy of the window-opening sequence** — now
   `ShowFindings`, used by the click handler, `--findings` and `--stop-findings` alike. *Fixed.*
8. **"Check stopped; the board was not fully checked." existed twice** — one const now, like the
   banner's own move onto the result. *Fixed.*

---

## Story 4 — When a setting is persisted, said once (6.50, the settings row)

**Problem.** The one row of 6.50's table still standing: *when a setting is persisted* — about
twenty settings writes across nineteen handlers, each deciding alone. The most recent fault there
was the assistant's own (6.62: a session-only override made the stored value unreachable, because
the setter compared against the file rather than against what the getter returns), and sprint 5
named the hour at the middle of it: **`RememberSettings` means two incompatible things** — for
board thickness, surviving a restart via the close-write is the feature; for the `--no-ruler`
override it was the hazard that forced the value out of `AppSettings` entirely. One method, two
contracts, and the second is a pattern invented under review pressure that will be reinvented
badly.

**What.** The shape 6.51 already proved: the decision pulled into something with no Avalonia in
it — `StaleBecause` and `Staleness.Of` are the template — leaving the view model the assignments.
Start with the two contracts getting two names; the story stops where the seam stops paying,
because its own entry warns that a bad refactor of the largest file in the repository is worse
than the gap.

**Done when** the decision of when a setting is written is reachable from a unit test with no
window in it, 6.49 and 6.62 re-introduced deliberately each fail a test by name — the standard
6.51 set — and `RememberSettings`' two meanings are two names no reviewer can conflate.

### Closed — 2026-10-18

**`SettingsKeeper`, in `MillBurn.Core`, shaped like 6.51.** `SettingChange` has one member per
handler that writes a setting (twelve), and `SettingsKeeper.PersistenceOf` is the table that says
when each is written — eleven `WrittenNow`, and the board thickness `WrittenWithTheNextWrite`. The
view model's nineteen `SaveSettings(Settings with { … })` calls are now
`_settings.Change(SettingChange.X, s => s with { … })`: the edit is handed the *stored* record, so no
handler can build the next one from a copy a session override has been folded into. `Settings` is a
read-only view of `Stored`; what is left in the view model is `WriteSettings`, the disk write and its
status line.

**The two names.** *Written with the next write* is `Persistence.WrittenWithTheNextWrite`; *never
written* is `OverrideRulerForThisSession` (renamed from `RememberRuler` in the window too), held in a
field the record never sees. 6.62's rule is `ChooseRuler`, which returns whether the view changed,
so the `ShowRuler` setter is four lines.

**Done-when, line by line.** Reachable without a window: `SettingsKeeperTests`, ten tests, no
Avalonia. Reintroduced deliberately with the keeper restored after, each failing by name — 6.49
(thickness `WrittenNow`): `AThicknessSliderDragWritesNothing`, `TheThicknessIsWrittenWithTheNextWrite`,
`EveryOtherSettingIsWrittenAsItHappens`; 6.62 (compared against the stored value):
`AfterNoRulerTheMenuCanTurnThemBackOn`, `UsingTheMenuDropsTheOverride`,
`ChoosingWhatIsShowingWritesNothing`; 6.62's second half (override not dropped): the first two of
those; the override sent the old `RememberSettings` way: `ASessionOverrideIsNeverWritten`.

**What no test sees**, and is said in 6.50's entry: a handler naming the wrong `SettingChange`. It is
6.51's residue in the same shape, and the answer is the same — the name is in the call, where a
reviewer reads it.

**By running.** A `--no-ruler` shot and a plain one of the test board: the first draws no strips,
and the settings file's MD5 is `a84a53ec…` before and after both. 6.50 is closed as fixed — all
three rows of its table are behind seams. 1,419 unit + 28 golden tests, zero warnings.

### What `/describe-test` found — 2026-10-18, one run

- **`SettingsKeeperTests`**: two tightened, nothing wrong. The cold read noticed that
  `ASettingWithNoRuleChangesNothing` could not tell "refused before the edit runs" from "ran and
  discarded" — the edit now records that it ran, and the test asserts it did not; and that
  `ChoosingWhatIsShowingWritesNothing` never looked at `Stored`, so a choice that changed the record
  without writing it would pass — it now asserts the stored ruler is untouched. Its other two notes
  were read and left: `EverySettingSaysWhenItIsWritten` would pass a catch-all arm returning a valid
  value, but the table's default arm throws and the next test pins that; and in the 6.62 test the
  written `ShowRuler` being true is also the default, but the fault it guards writes nothing at all,
  which the single-write assertion catches.

### What the code review found — 2026-10-18, medium

No correctness fault: every handler kept its timing, `Stored` is still assigned before the write,
and the override is dropped by the menu exactly as before. Seven findings; three acted on.

1. **`SettingChange.Ruler` passed straight to `Change` skipped the override drop** — a later handler
   writing the ruler that way would store a value the viewport never showed: 6.62 through the seam
   built against it. `Change` now ends the override for any stored ruler choice, and
   `AnyStoredRulerChoiceEndsTheOverride` says so (1,420 unit tests). *Fixed.*
2. **Nothing ties a handler to the label it passes.** True, and the residue the Closed block and
   6.50's entry already name. *Left.*
3. **The close-handler comment was left unwrapped at 133 columns.** *Fixed.*
4. **`ShowRuler`'s doc still said the tick is the stored value**, untrue under `--no-ruler`.
   *Fixed.*
5. **Skip the write when the edit changed nothing.** *Left, and it is a trap as offered:* the
   thickness rides on the close-time placement write, so comparing the placement edit against a
   `Stored` that already holds the new thickness would find "no change" and skip the one write the
   thickness was waiting for — 6.49's survive-a-restart half, broken. Doing it right needs a
   pending flag; the saving is a rare second write of a few hundred bytes. Not this story.
6. **Put persistence on the enum member instead of a switch.** The throwing default is the forcing
   function, the same one `Staleness.Of` uses and its sweep test catches before any handler can.
   *Left.*
7. **`/describe-test` not shown as run.** It had run; recorded above. *No change.*

---

## Story 5 — Tabs by letter, and one tab's width at a click (6.4, added 2026-10-18)

**Problem.** From the product owner, the morning after the drag landed: *"I'd also like to be able
to single click on a tab (just the circle) and have it pop up a hover dialog for 'width'. The width
would be applied to only that specified tab. And, would it be possible to have a label near the tab
handle (the circle)? A - Z (then AA - ZZ, ...). And in the tab dialog, use those letters instead of
the numbered list. Displaying the label would always have to try to be on the outside of the
board."* Spreadsheet-style lettering past Z, confirmed the same morning. A story to remove the tabs
window was proposed first and withdrawn by the owner — *"the dialog is actually kind of handy still
so let's keep it for now."*

**What.** A letter per tab, stable as the ordinal is; a click on a handle that is not a drag; a
popup that writes the same edit the window does; the window's rows named by letter. And the gap
that made the last part impossible: an added tab had no name a size edit could use.

**Done when** every handle carries its letter off the board side, the window's rows use the same
letters, a click sets one tab's width — added tabs included — and nothing else, and a letter or a
size stays on its tab through drags and removals around it.

### Closed — 2026-10-18

**The owner, on seeing it:** *"That is awesome! Tabs are so easy now."*

**Letters.** `PlacedTab.Letter`, given out with the ordinal in `TabPlan.For`: planned tabs in the
pristine plan's order, adds after them, `TabPlan.LetterOf` running A … Z, AA … ZZ, AAA. Drawn by
`TabHandles.DrawLetter` 17 px off the handle along the cut path's outward normal — outward from the
loop's winding, computed in the view model (`TabLoopTurnsLeft`) and carried through the frame.

**Added tabs carry their own sizes**, on their add entry: `TabMove.WidthNm` and `HeightNm`,
read only by an add, guarded against non-positive numbers as an edit is. Removing an add takes its
size with it; the window sizes added rows and hands the moves back with the sizes written on
(`TabPlan.WithAddSize`); the export advice and the CLI's "sized one at a time" count include them.
The first build did this differently — see the code review below.

**The click.** `BoardView` holds a press on a handle as a click until it wanders 3 px; letting go
inside that raises `TabClicked` instead of `TabMoved`, which also ends a small fault: a stationary
click used to write the tab as hand-placed where it already was. `TabWidthFlyout` opens at the
pointer and calls `SetTabWidth`, which writes `TabPlan.WithWidth` — the window's own "equal to the
board's is no edit" rule, the tab's own height kept — and refuses, saying so, if the plan it was
opened on has been replaced.

**By running.** `--tab-width B=4` on the test board: B's ticks visibly wider, the others unmoved,
status *"Tab B is 4.00 mm wide now. The other tabs are unchanged."*; `--tabs` shows rows A–D with B
at 4.00; `--tab-popup B` captures the popup itself (its own top level, which a window capture does
not reach); the 66-up panel's four letters sit outside the frame. Settings file unchanged by every
shot. Eight faults reintroduced deliberately in the planner after the rework — adds lettered from
zero, an add's own size ignored, a zero size honoured, a click on an add written as an edit,
`WithWidth` dropping a planned tab's height, `WithAddSize` counting an entry that adds nothing (and
the first round's five against the first design) — each failed `TabLetterTests` by name; the last
needed a test case added to be caught. 1,441 unit + 28 golden tests, zero warnings; no golden
snapshot moved, so no existing program changed.

### What `/describe-test` found — 2026-10-18, three runs

- **`TabLetterTests`**: two tightened. The cold read noticed that
  `AnAddedTabsSizeSurvivesAnEarlierAddBeingRemoved` asserted only the board's 2 mm on the
  survivor — which a planner that never applied an added-tab edit would also produce. It now gives
  the survivor its own 3 mm and asserts that, and its letter F. And
  `ALetterStaysWithItsTabThroughMovesAndRemovals` never checked where B went; it now checks B's
  arc position against the move. The rest read as written: the gap measured off the real toolpath,
  the lettering table hand-written rather than computed, and both index spaces crossed in both
  directions.
- **`TabLetterTests` again, after the review's rework** rewrote half of it: four tightened. The
  test then called `RemovingAnAddTakesItsSizeWithIt` removed nothing — the removal is the view
  model's, out of reach — so it is renamed for what it shows, the surviving add's own size and
  freed letter. The program-gap test would not have noticed planned A failing to go, since A's
  2 mm gap sits inside the 4 mm one; it now asserts A is gone. `WithWidth`'s "an edit with nothing
  left goes" was claimed and not exercised; it is now. And the click-to-plan test said nothing
  moved without checking; it now compares every tab's position before and after.
- **`LayerOutputPersistenceTests`** (one assertion pair added, for an add's own width and height):
  the new fields round-trip with non-default values, so a dropped field fails by name. The read
  also found two weaknesses older than this story — its null checks cannot tell "written as null"
  from "left out", and "a project from before this existed" opens a file the current build just
  wrote. Recorded, not this story's to fix.

### What the code review found — 2026-10-18, medium

Ten findings. The first six were one design fault seen from six sides, and the design was changed
rather than patched.

1. **–6. Added-tab sizes in a `TabEdit` with an `Added` flag, and removed adds kept as empty
   entries.** The empty entries made `TabMoves` non-empty after an add-and-remove, so the header
   said *"after the hand-set changes"* for a shortfall the edges made; a removed add's size
   outlived it, still counted by the CLI and checked by the export advice; the CLI's
   `--tab-width` and every other door that clears the moves had to remember to clear those edits;
   and a build from before the flag reads `{Index:0, Added:true}` as planned tab 0 and cuts that
   gap at the add's width — the author runs old builds. *Fixed by moving the sizes onto the add
   entry* (`TabMove.WidthNm`/`HeightNm`): removal takes them, nothing else holds an index to an
   add, the empty entries are gone, and an older build ignores the fields. The cost, said in the
   entry: a later add's letter moves up when an earlier add is removed.
7. **A letter looked up after a re-plan can be another tab.** The popup is light-dismissed, so
   this needs a change arriving from elsewhere, but the answer is cheap: the popup carries the plan
   it was opened on (`TabWidthAsk`), and an apply against a replaced plan is refused with a status
   line. *Fixed.*
8. **A hand-rolled shoelace** where Clipper's `IsPositive` says the same. *Fixed.*
9. **Two paints allocated per letter per frame.** Hoisted to statics. *Fixed.*
10. **The letter lookup written three times.** One `TabHandleOf`, used by the shot flag, the popup
    flag and the apply path. *Fixed.*

---

## Story 6 — The sprint closes clean (the `xhigh` review, and the debt)

**Problem.** The owner asked for a sprint-wide `/code-review` at `xhigh` before the release —
*"These changes are deep and could have bad consequences"* — and it confirmed the headline: no
unedited board's programs move, the 21 x 37 mm board's included. What it found was in projects
that carry hand-set tabs, and on the UI thread: thirteen findings. Beside them, six things the
stories had left open on purpose. The owner, on those: *"I really don't like tech debt. I prefer
to complete the solution upon delivery with exceptions being weighed heavily."* And: *"fold the
debt into the review findings."*

**What.** All of it, as one story — the eighteen items below, except the README hero, which is a
release task.

**Done when** every finding is fixed or its exception argued; every program from every committed
board is byte-identical to `main` apart from the panel outline's intended spread; and every new
behaviour has a test that fails without it.

### Closed — 2026-10-18

**Hand-set tabs and the plan they numbered (6.74).** `TabPlan.KeyOf` fingerprints the pristine plan;
`OutlineOptions`/`LayerOutputSettings.TabPlanKey` carry it beside the changes; every write stamps
it. `OutlineOperation.AssignTabs` is now the one rule for *which board owns the changes* and *do
they still fit it*, used by the program and the window alike — so the window can no longer show
removals on one board while the program cuts another. Stale changes are refused in the program
(header says so) and named in the check panel while they are; they are **kept**, apply again if the
layout comes back, and the first gesture on the new layout starts afresh (`TabChangesToBuildOn`). A
0.6.0 project's per-tab sizes are refused this way: tested on a real file the v0.6.0 command line
saved, `Tests/Fixtures/MadeBy060*.millburn`.

**Overlapping tabs.** `TabPlan.Gaps` merges overlapping tabs into one gap, their union; `SplitAt`
cuts between gaps, so a narrow tab inside a wide one no longer starts a run inside the wide one's
gap. The header names overlaps (*"Tabs B and E overlap, so they are cut as one gap 8.0 mm long"*),
and the gestures refuse to make one (*"Tab A there would run into tab B … It stays where it was"*,
seen by running `--move-tab`). Lone tabs come out exactly as before.

**The header's account.** Clearance and the hand are said apart: *"… the straight edges hold N
with clearance either side of each, and M after the hand-set changes."* "Every tab was removed by
hand" only when removals were applied. **The project page** says the rebuild commands put hand-set
tabs back.

**The UI thread.** The tab overlay's cache keys on the geometry the realised-layer cache already
shares, and on only the settings that move a tab — a preview landing no longer drops a drag, and
the thickness slider no longer re-plans the panel per tick. **And the planner itself was the
cost:** `ProfilePath.At` scanned every segment, three times per edge — 0.3 s per plan on the
panel's frame. A binary search answering exactly what the scan answered (tested against the scan
as a reference) took placement to 8 ms; the outline's shaping (0.13 s) is now cached and a
gesture re-plans in under 10 ms. **Tiles** are shared by every scene drawn from the same geometry,
reference-counted so the scene being replaced cannot free its successor's; board layers hand back
the same ring list each time so a rebuild finds them. A planner exception is now a status line,
not a crash in a property setter. Handles go with the board on Close project. A pan's press point
no longer survives a lost capture into a phantom click-to-add.

**One copy each.** `ProfilePath` is the one walk along the loop (the outline's private walk is
gone, the planner's own copy too, with arithmetic kept identical). Add entries are found in one
place (`TabPlan.Adds`) and changed through `WithAdd`/`WithAddMoved`/`WithoutAdd`, with
`TabMove.IsAdd`/`IsRemoval` naming the two kinds; the view model's copies are gone.

**The debt.**
1. *Two stop lines racing* — `StopAnnouncement` gathers one Escape's two sentences into one line,
   check first, however they finish.
2. *A mislabelled setting* — `SettingsKeeper` has one method per setting (`SetBoardThickness`,
   `SetTheme`, …), so a handler cannot name the wrong one; `Change` is private.
3. *Writes that change nothing* — skipped, unless a held value is waiting, which then rides.
4. *An added tab re-lettered by a removal* — `TabMove.Number`, given once; F stays F.
5. *The persistence tests* — a real v0.6.0 file now stands in for "a project from before"; the
   null-versus-absent weakness is recorded below as left.
6. *The README hero* — a release task, by the owner's word.

**Two doc comments** misplaced by earlier stories are back on their members.

**By running.** All ten committed boards exported with `main`'s CLI and this branch's: every
`.nc` and `.svg` byte-identical except `GridStripConnector_Panelized-Edge_Cuts.nc` (6.72's spread);
the logs identical but for paths — run again after the review's fixes, the same. The v0.6.0
per-tab-size file opens unchanged (*"unsaved changes: no"*), tab B at the board's width, and the
check panel saying *"Tab changes on Board outline were made for a different tab layout … so they
are not applied … They apply again if the layout comes back; moving or sizing a tab starts them
afresh."* An overlapping `--move-tab` is refused by name. The settings
file was restored after the runs that open a project (opening one adds it to the recent list —
the existing behaviour, not this branch's). 1,476 unit + 28 golden tests, zero warnings.

**Faults reintroduced deliberately**, each caught by name: stale changes applied anyway, overlaps
not merged, heights in the fingerprint, the shortfall blamed on any change, survivors' numbers not
pinned on removal, unchanged settings written, a held value forgotten by the skip, the joint stop
line in arrival order, shared tiles freed by their first holder. One more was not: `>` for `>=` in
the binary search changes nothing observable — at an exact segment end the next segment starts at
the same point — so there is nothing for a test to catch.

### What `/describe-test` found — 2026-10-18, nine runs

Every file this story created or edited, one cold read each.

- **`StopAnnouncementTests`, `CliScriptTests`**: clean.
- **`SharedTilesTests`**: the second scene is now disposed even when an assertion fails, and the
  rebuilt-tiles test checks it drew anything at all.
- **`ProjectFromOlderBuildTests`**: the folder comparison only ever compared boards — both sides
  used the old file's layer settings — and is renamed for that; the refusal is now required once and
  in the outline's own program; the round-trip test pins index 0 and is renamed for what it shows
  (the stamp and the change come back), since planning with a stamp is the planner's test.
- **`SettingsKeeperTests`**: the undoing setters' writes were never counted — now they are, from a
  record with something to undo; the machine case checks a second of its six values.
- **`TabLetterTests`**: the removal test still built the post-removal state by hand; it now removes
  through the planner's own `WithoutAdd`, two sized adds in, and checks the survivor keeps its size
  and its letter F.
- **`TabMoveTests`**: the program-gap test never checked where the moved tab went; it does now. Its
  always-true `Spaced` assertion on an added tab predates the sprint and is left.
- **`TabPlacementTests`**: the widened-tabs test now asserts the tabs really overlap, or its
  accounting could pass without the case it is named for. The seam-curve test can pass with both
  tabs on straight edges — older than the sprint, left.
- **`TabSafetyTests`**: the two-boards test could not tell whose "N tabs" line it read — the boards
  are now different sizes and it checks the cut itself; the fingerprint test varies a tab height
  directly too; and the move test's comment no longer claims a letter it does not check.

**Left, and why:** `LayerOutputPersistenceTests`' null checks cannot tell a null written from a
field left out (story 5's read) — it is a property of the serialiser's defaults the project relies
on deliberately, and a test of the JSON text would pin formatting rather than meaning.

### What the code review found — 2026-10-18, medium

Ten findings, nine fixed and one argued.

1. **–2. Clearing stale tab changes was the wrong design.** After a preview, the clearing status
   line was overwritten at once by *"Output changed…"*, so the changes vanished with no word; and
   clearing was permanent where the cause often was not — a role toggled and toggled back, a
   cutter's diameter undone. *Redesigned:* the changes are kept and refused, named in the check
   panel while they are, and apply again when the layout comes back; the first gesture on the new
   layout builds on nothing rather than reviving them (`ShowTabStaleness`, `TabChangesToBuildOn`).
   The tabs window gets the same empty base.
3. **`--tab-width` keeps the per-tab sizes and they are refused.** True, and now the intended
   behaviour: they name tabs of the old layout and come back if the width does, as in the window.
   The CLI's comment said otherwise and is corrected, and the header no longer reads as blaming an
   old file — it names width, count, cutter or outline as the reasons a layout changes.
4. **The thin- and narrow-tab advice counted refused sizes.** It now asks the plan whether they
   apply — only when there are hand-set sizes, so an ordinary export pays nothing. *Fixed.*
5. **A board whose tabs came to nothing, or whose planning threw, was shaped again on every output
   change.** The answer is now remembered against its key, and the shape kept. *Fixed.*
6. **The no-change skip lost a failed write.** The file locked, Save refused, the file freed, Save
   pressed again with the same values — skipped, and the numbers lost at exit. The write now says
   whether it reached the disk, and a refused one leaves the record unwritten so the next write
   goes (`ARefusedWriteIsTriedAgain`). *Fixed.*
7. **A 0.6.0 project's per-tab sizes are refused even where today's plan is the same.** *Argued,
   and kept:* nothing in a 0.6.0 file says which plan its numbers meant, and on a non-rectangular
   board they will have moved. Refusing is the only reading that cannot cut a thin web somewhere
   nobody chose — and with finding 1's redesign it costs nothing permanent: the sizes stay in the
   file, the panel says why they are not applied, and they are set again in a click each.
8. **A double dispose could free shared tiles another scene was drawing.** A layer now releases
   once, and a release past zero changes nothing. *Fixed.*
9. **The tabs window shaped the outline from scratch** while the overlay held it shaped. It takes
   the cached shape when the geometry and cutter match. *Fixed.*
10. **Two doc comments stranded by this story's own edits** — `OutlineOptions`' and `BuildTiles'`.
    *Fixed.*

One more `/describe-test` run followed, on `SettingsKeeperTests`, which gained a test. It read
sound; two preconditions were added — the undo cases now assert their starting record holds what
each takes away, and the colour test that a setter really added its entry — or a "lacks" check
proves nothing.

---

## Deliberately out

**[6.45](../Documentation/06-Roadmap-and-Risks.md#645-block-apertures-and-the-aperture-transforms--defect--open--no-fixture-exists-yet)
and
[6.47](../Documentation/06-Roadmap-and-Risks.md#647-isolation-cuts-inside-a-hole-that-is-about-to-be-drilled--defect--open--found-in-the-workshop-low-priority-a-later-sprint)**
— the two standing defects, out on the same grounds as every sprint: no fixture, and a withdrawn
shape.

**[6.18](../Documentation/06-Roadmap-and-Risks.md#618-a-paste-stencil-to-3d-print--enhancement--open)
and
[6.73](../Documentation/06-Roadmap-and-Risks.md#673-placement-data-for-a-pick-and-place--enhancement--open--asked-for-by-the-product-owner-2026-10-16)**
— set back by the product owner at the picking as wish-list items; both entries carry the dated
paragraph so the standing is recorded where the work is.

**M16 and M17** — set aside in sprint 5 by the owner before anything was cut, and they close only
at the bench. The slice worth keeping is still the slice sprint 5 named: a third hole off the line
of the other two, the day a flipped piece needs catching.

**6.4's remaining remainder** — item 4's keep-outs and the blank sharing the sizing stay open in
the entry even when story 2 lands; the fold takes the drag, not the whole of 6.4.
