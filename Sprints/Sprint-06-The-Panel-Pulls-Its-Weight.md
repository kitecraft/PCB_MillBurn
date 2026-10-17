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
four of them ([6.72](../Documentation/06-Roadmap-and-Risks.md#672-on-a-panel-every-tab-goes-on-one-edge--defect--open--found-in-the-workshop-2026-10-11-for-sprint-6)).
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
| [6.72](../Documentation/06-Roadmap-and-Risks.md#672-on-a-panel-every-tab-goes-on-one-edge--defect--open--found-in-the-workshop-2026-10-11-for-sprint-6) | On a panel, every tab goes on one edge | **In — story 2**, folded with 6.4's manual placement at the product owner's suggestion |

**Then the other places the rule names.**
[08](../Documentation/08-Requirements-Matrix.md)'s Partial rows: M16 and M17 were set aside by the
product owner in sprint 5 before anything was cut, and nothing has changed at the bench since;
G24 — *tabs placed where a hand would put them... and movable one at a time* — is exactly story 2,
and its Partial note already reads *"Dragging a tab on the board is not built"*. M3, V13, V16 and
V34 are each missing a part nobody has asked for since the row was written.
[09](../Documentation/09-Machine-Accuracy-Investigations.md)'s open item is still the laser
gantry's squareness, which is a frame to be shimmed and not a line of code.

**And the fourth story comes from its own entry's standard.**
[6.50](../Documentation/06-Roadmap-and-Risks.md#650-nothing-can-test-the-view-model--enhancement--open--three-times-in-one-sprint)
was dropped from sprint 5 with one row left — *when a setting is persisted* — and the closing
argument *"the same shape, which is the argument for doing it next rather than for doing it now"*.
Next is now, and the sprint carrying a renderer story and an interaction story has room for the
careful one.

### And the four

| # | story | where it came from |
|---|---|---|
| [6.71](../Documentation/06-Roadmap-and-Risks.md#671-the-66-up-panel-draws-at-about-14-fps-where-the-readmes-hero-showed-37--enhancement--fixed--found-retaking-the-readmes-screenshots-2026-10-11-fixed-in-sprint-6) | The panel previews at a rate the README can stand beside | Retaking the hero, 2026-10-11; measured 2026-10-15; named first by the product owner |
| [6.72](../Documentation/06-Roadmap-and-Risks.md#672-on-a-panel-every-tab-goes-on-one-edge--defect--open--found-in-the-workshop-2026-10-11-for-sprint-6) + [6.4](../Documentation/06-Roadmap-and-Risks.md#64-tabs-where-how-many-how-big--enhancement--partial--placed-and-sized-from-the-board-in-sprint-4-moving-one-by-hand-is-not-built) | Tabs spread round the panel, and a hand can move one | The workshop, 2026-10-11, on the same panel; folded at the picking, 2026-10-16 |
| [6.54](../Documentation/06-Roadmap-and-Risks.md#654-the-checks-panel-cannot-show-a-cancelled-run--enhancement--open--found-by-code-review-2026-09-29) | A stopped check says so | `/code-review`, 2026-09-29; the other half of sprint 5's review debt |
| [6.50](../Documentation/06-Roadmap-and-Risks.md#650-nothing-can-test-the-view-model--enhancement--open--three-times-in-one-sprint) | When a setting is persisted, said once | Three faults in one sprint; the settings row is the one sprint 5 left |

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
