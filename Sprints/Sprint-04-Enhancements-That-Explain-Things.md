# Sprint 4 — Enhancements that explain things

**Agreed** 2026-09-30, after v0.4.0. **Release branch:** `release/0.5.0`, branched from `main`.

**The product owner's direction:** the list held over from sprint 3, in the order it was agreed on
the day v0.4.0 was tagged — *"Next sprint is fine and Rulers as 7th is fine too."* Eight of the nine
stories are something the workshop asked for out loud, and the roadmap entry for each one quotes the
request.

**The theme is the product owner's, and it is sharper than the list it came from:** *"enhancements
that explain things."* Read down the nine and every one of them is the application saying out loud
something it already knows and currently keeps to itself. Where the tabs will be, before the board is
in the machine. Which of the gaps in the copper nobody can vouch for. Which six holes the 1.00 mm bit
is about to visit. What to type to get this export again. Where the copper stops on *this* piece of
laminate. Which folder this board came out of. What kind of bit that row is. How big the thing on
screen is, and where the pointer is in the board's own numbers. And which piece of copper a finding
is about.

None of it is new capability. All of it is the difference between an application that knows and an
operator who has to work it out — which is the same argument sprint 3 made about the checks, aimed
now at everything around them.

**One of the nine is a defect rather than an enhancement**, and it is this theme inverted: the
application explaining something confidently and wrongly, which is worse than the silence the other
eight are about.

**Sprint 3 was one item.** 6.44 was the sprint, its clauses were the stories, and the reason was
written down at the time: *"putting it beside nine other items is how a sprint ends with everything
half done."* This sprint has nine items, which is the shape sprint 2 had. That is a risk and it is
taken deliberately — see *Size, and what gets dropped first*.

---

## How this sprint was picked

**The defect list first**, as the standing rule requires. One command produces it:

```
grep -n 'defect · open' Documentation/06-Roadmap-and-Risks.md
```

Three came back. **One is in:**

| # | what | what happened to it |
|---|---|---|
| [6.45](../Documentation/06-Roadmap-and-Risks.md#645-block-apertures-and-the-aperture-transforms--defect--open--no-fixture-exists-yet) | Block apertures and the aperture transforms | **Out.** No fixture exists. It cannot start, and it has been held on that ground since sprint 2 |
| [6.47](../Documentation/06-Roadmap-and-Risks.md#647-isolation-cuts-inside-a-hole-that-is-about-to-be-drilled--defect--open--found-in-the-workshop-low-priority-a-later-sprint) | Isolation cuts inside a hole that is about to be drilled | **Out.** Built in sprint 3 and withdrawn by the product owner after it turned out to make two nets' separation depend on the drill finishing what the isolation started. The patch is kept; the shape is wrong |
| [6.53](../Documentation/06-Roadmap-and-Risks.md#653-copper-with-no-net-name-is-reported-as-safe-when-one-named-net-shares-its-region--defect--open--found-by-code-review-2026-09-29) | Copper with no net name is reported as safe when one named net shares its region | **In, as story 2** |

Then the other two places the rule names. The matrix's **Partial** rows in
[08](../Documentation/08-Requirements-Matrix.md) hold nothing this sprint moves that is not already
below; M16, M17 and V34's four unbuilt machine checks are all accuracy work, which was sprint 1's
theme and is the first reserve if this one turns out to be shorter than it looks.
[09](../Documentation/09-Machine-Accuracy-Investigations.md)'s single open item is squaring the
laser's gantry, which is a frame to be shimmed and not a line of code.

**And the nine:**

| # | story | where it came from |
|---|---|---|
| [6.4](../Documentation/06-Roadmap-and-Risks.md#64-tabs-where-how-many-how-big--enhancement--open) | Tabs: where, how many, how big | The workshop, on seeing four tabs cut: *"They are large tabs and may not be in the best locations"* |
| [6.53](../Documentation/06-Roadmap-and-Risks.md#653-copper-with-no-net-name-is-reported-as-safe-when-one-named-net-shares-its-region--defect--open--found-by-code-review-2026-09-29) | Unnamed copper is called unnamed everywhere | `/code-review`, during sprint 3 |
| [6.5](../Documentation/06-Roadmap-and-Risks.md#65-a-picture-on-the-companion-pages--enhancement--open) | A picture on the companion pages | The workshop: *"include images of the layer with the holes/slots numbered in the order of drilling/routing"* |
| [6.15](../Documentation/06-Roadmap-and-Risks.md#615-the-companion-page-names-the-commands-that-would-rebuild-it--enhancement--open) | The commands that would rebuild the export | The workshop: *"a list of cli commands that would be the fastest way to replicate the outputs"* |
| [6.52](../Documentation/06-Roadmap-and-Risks.md#652-a-test-cut-that-finds-where-the-copper-stops--enhancement--open--asked-for-by-the-product-owner) | A test cut that finds where the copper stops | The product owner, 2026-09-27: *"A new test for copper thickness"* |
| [6.32](../Documentation/06-Roadmap-and-Risks.md#632-say-where-the-project-came-from--enhancement--open--asked-for-by-the-product-owner) | Say where the project came from | The product owner: *"We should display the gerber folder somewhere"* |
| [6.6](../Documentation/06-Roadmap-and-Risks.md#66-the-tool-library-once-it-has-more-than-a-handful-in-it--enhancement--open) | A tool library you can find a bit in | The workshop: filter by tool type, sorting, and copy a tool |
| [6.1](../Documentation/06-Roadmap-and-Risks.md#61-rulers--enhancement--fixed) + [6.48](../Documentation/06-Roadmap-and-Risks.md#648-where-the-pointer-is-in-the-boards-own-numbers--enhancement--fixed--asked-for-in-the-workshop) | Rulers, and the pointer's own numbers | The workshop, twice: a scale down the edges, and *"display the coordinates"* |
| [6.56](../Documentation/06-Roadmap-and-Risks.md#656-a-finding-takes-you-to-the-copper-it-is-about--enhancement--open--asked-for-by-the-product-owner-2026-09-30) | A finding takes you to the copper it is about | The product owner, the day after the findings view shipped |

**The order is the product owner's, with one insertion.** Seven of these were ranked on 2026-09-30
and that ranking stands: tabs, the companion page picture, the CLI commands, the copper test cut, the
project's origin, the tool library, rulers last. 6.53 was found during the sprint that had just
closed and was never in that list, so it needed a place. It is **story 2, next to story 1, because
they are the same fault wearing different clothes**: a number the app states plainly, that an
operator acts on, and that is wrong in the direction which reassures. Story 1's is in the material
and story 2's is on the screen, which is the only reason story 1 is first. Moving it is one edit to
this document if the product owner would rather it went elsewhere.

**Rulers and click-to-locate are two stories, not one.** They were asked for in one breath and they
are different work — one is about reading the board, the other about moving to a place on it. Sprint
2 has the precedent and it was the product owner's own correction: three requirements written as a
single story became three, because *"closing them together would have meant one closure trying to
answer for three unrelated things."*

**6.48 comes with the ruler.** It is the cursor readout 6.1 already lists as *"worth having with it,
cheaply"*, it was asked for separately at the bench, and building the ruler without it would mean
touching the same overlay twice. The two close together.

---

## Size, and what gets dropped first

Nine stories, three of which — the picture, the CLI block and the copper test cut — are each a
substantial piece of work on their own. Sprint 3's opening note is the warning, and the mitigation is
the order rather than optimism:

**Nothing here depends on anything else here.** Not one story. So the tail can be cut without
stranding a half-built thing, which is exactly what happened to sprint 3's story 7 — *"it goes last,
so taking it out stranded nothing"* turned out to be the useful sentence in that document.

**If the sprint has to be shortened, it is shortened from the end**, and stories 8 and 9 go first
even though they are the two the product owner is most looking forward to. Better said now than
discovered in a fortnight: they are the most droppable because the findings view they serve already
works and already prints the coordinate, so what they add is reach, where stories 1 and 2 take away a
wrong number.

---

## How a story closes

As in [sprint 1](Sprint-01-Speed-and-Accuracy.md), [sprint 2](Sprint-02-Fix-What-Is-Broken.md) and
[sprint 3](Sprint-03-Checks-You-Can-Read.md): every story ends with a `Closed` block carrying the
date and a plain verdict against *Done when*, the manual test in enough detail that somebody else
could run a similar one, what was observed including the sentence from the bench, and what is left
open. A story without one is still open whatever its branch says.

**Sprint 2's addition stands:** a defect closes with the test that would have caught it, named in the
closure. Story 2 is the defect.

**Sprint 3's addition stands too**, and widens. A story that changes what a surface looks like closes
with a picture. Three of these stories do not put their output on a screen at all — a companion page
is a file, a coupon is a piece of copper, a tab is a program — so **a story whose output leaves the
screen closes with the artefact itself**: the page opened in a browser, the emitted program quoted,
the measurement taken off the coupon with the tool it is meant to be read with.

**And one for this sprint, from what sprint 3 cost.** Story 7 of sprint 3 was withdrawn after four
separate wrong readings, and every one of them was a mistake in *measuring* rather than in code: end
points counted instead of segments, arcs judged by their chords, a radius compared against the wrong
feature size. Each time the code was checked and the measurement was not.

So: **any count this sprint quotes off geometry is taken two ways before it is written down**, and
the second way has to be independent of the first — a picture, a different unit, or a hand count on a
small case. A number in a closure that came out of one script nobody checked is not a measurement.
It is a hypothesis with a decimal point.

---

## Story 1 — Tabs where you would put them by hand

**Problem.** From the workshop, after [every tab asked for became a tab that gets cut](../Documentation/06-Roadmap-and-Risks.md#four-tabs-asked-for-two-tabs-cut)
put four tabs on a 21 x 37 mm board that had been getting two: *"They are large tabs and may not be
in the best locations."* Both halves of that are right and both follow from the same thing — a tab
today is **a count and two global numbers**. `SplitForTabs` divides the perimeter by `TabCount` and
puts a tab every `spacing` from wherever the contour happens to start, each `TabWidthNm` wide and
each leaving `TabHeightNm`. Nothing looks at the board.

So a tab lands across a corner, or beside a mounting hole, or on the one edge a connector sits on;
3 mm is a lot on a 21 mm edge and little on a 100 mm one; and one height serves a tab near a corner
and a tab in the middle of a long edge equally badly.

**What.** Placement by edge rather than by arc length — one per straight edge, centred, which on a
rectangle is what anybody draws by hand — and width and height per tab rather than for all of them,
with defaults derived from the perimeter and the thickness.

**Why first.** It is the only story here whose output goes into the material. Everything else on this
list is a number on a screen or a page; a tab in the wrong place is a board that breaks out of its
stock badly, or does not come out at all.

**How.** The physics stays where it is: the split happens in `SplitForTabs`, on arc length *within* a
segment, and the lesson from the bug that produced this request is that it must never be computed on
which vertices happen to exist. What changes is what decides the positions before it is called.

**The blank keeps its rule.** `BlankOperation` puts tabs on the top and right edges only, two per
edge, because the bottom and left are the datum and must be cut clean — a stub on a datum edge stops
the piece seating, invisibly. The workshop's own words on seeing that: *"We can deal with that when
we get to the tabs upgrades."* Whatever placement this story adds has to keep it.

**Already done, and worth saying so because the roadmap said otherwise.** 6.4's *Found since* note
claimed the tab height in the program's comment could be false — that on a thin board with a big
stepdown every pass jumps the tab and the piece is left the full thickness while the header promises
`0.50 mm of material left`. **That was fixed on 2026-09-18** (`61a47b3`, shipped in v0.1.2): both
`OutlineOperation` and `BlankOperation` add a pass at exactly the tab's top whenever the stepdown
does not land there, both say in the program that they did, and both refuse to pretend when a tab is
taller than the whole cut. The note was stale and is corrected in the roadmap by this story. The
number in the comment is the number in the material today.

**Not in it.** Manual placement — click the outline in the viewport to put a tab there, drag to move
it — and keep-out from features. Both are in 6.4's own list and both need the viewport to hit-test
the profile, which nothing does. Story 9 gives the viewport a marker, not a pick, and conflating the
two is how this story becomes three.

**Done when** a rectangular board puts one centred tab on each edge by default, each tab's width and
height can be set individually, the emitted gaps land where the preview says they will, the blank
still tabs only its top and right edges, and the tab positions are pinned by a test on a board whose
edges are different lengths.

### Closed — 2026-09-30

**Met**, with one clause of 6.4's own *Done when* deliberately left for later: a tab can be resized
individually but not **moved** individually, which needs the viewport to hit-test the profile. 6.4 is
now `enhancement · partial` and says which half.

- **A rectangular board puts one centred tab on each edge.** `TabPlan` groups the offset profile's
  segments into straight edges and gives one to each, longest first, before any edge gets a second.
- **Each tab's width and height can be set individually**, in *Tabs ▸ Sizes…* beside the count, and
  an edit changes the tab it names and nothing else.
- **The emitted gaps land where the plan says.** Asserted against the toolpath rather than the
  planner, because the two walk the profile independently.
- **The blank still tabs its top and right edges only.** Untouched, and `BlankTests` still holds it.
- **The positions are pinned** on a 21 x 37 mm rectangle — the workshop's own board, whose edges are
  different lengths — against the four side midpoints rather than four remembered coordinates.

**What it looks like.** `WorkingFolder/V0.5.0/Screenshots/story-1-tabs-on-the-board.png`: the test
board's outline alone, four gaps, each in the middle of an edge and none at a corner. Compare the
same board before, where four tabs were spaced from an arbitrary seam.
`story-1-tabs-dialog.png` is the sizes window, which also shows the two things the first screenshot
caught — a coordinate in the Gerber's frame (*"183.41, -124.47 mm"*, fixed by shifting to the board's
lower-left corner as `BoardFindings` does) and a numeric box too narrow for its own second decimal.

**The sizes now come off the board**: a tenth of the narrow side, a third of the thickness. The
workshop's 21 x 37 mm board gets 2.0 mm tabs where it used to get 3.0 — *"they are large tabs"* — and
a 30 x 50 mm board still gets exactly 3.0, so the ordinary case did not move.

**And a default for them, asked for the morning after** — *"add some options to the settings → new
board tab for default tab width and thickness"* — under *Settings ▸ New boards ▸ Tabs*, applied as a
board is imported, where the isolation width beside it is applied. **Zero means the board decides**,
which is how it ships, so nobody who does not want the setting ever meets it. Negatives are refused
and say which tab of the window they are on; there is no upper bound, because whether 1.5 mm of
material left is absurd depends on a thickness the settings record does not hold, and the program
already says plainly when a tab is taller than the whole cut.

**Measured, against the previous release, over the corpus:**

| | cutting | rapid | estimate |
|---|---|---|---|
| Panel outline | 13,786 → 13,762 mm | 337 → 521 mm | 49m33s → 49m34s |
| PogoTest1 outline | 676 → 688 mm | 71 → 79 mm | 2m49s → 2m50s |
| Connector strip | 398 → 416 mm | 83 → 73 mm | 1m54s → 1m55s |

Cutting moves with tab width — narrower tabs leave less uncut — and the rapid is where the placement
shows. The panel gains 184 mm of it because the frame's runs now begin and end at edge midpoints
rather than wherever the offset started; at the machine that is about five seconds on a
forty-nine-minute program.

**And 309 mm of that was avoidable and was avoided.** The first version left the last full lap
finishing at the profile's start vertex while the first tabbed run began at the middle of an edge —
further than a tab is wide, so the emitter climbed to the safe height and traversed. Starting the lap
at the **near** side of the first tab costs one hop over a tab that has just been cut down. Ending it
at the *far* side would have been free, and was rejected: the emitter then carries straight on
without lifting, the backplot classifies a run by its deepest point, and the shallow lap would be
drawn in the full-depth colour over the gaps the tabs leave — the v0.1.2 complaint
(*"the outline's cut line stopped showing where the tabs were"*) reintroduced to save a few
millimetres. `BackplotTests.TabsShowAsGapsInTheFullDepthCutLine` is what caught it.

**Two tests changed, and both had been passing for the wrong reason:**

- `OutlineTabTests.ATabTallerThanTheCutSaysSo` looked for the words *"full thickness of the board"*,
  which now also appear in the note about the extra pass at a tab's top — so it went on passing after
  the derived height stopped making that case happen at all. It asserts the sentence only this case
  writes, and passes the height it is about.
- `BackplotTests.TabsShowAsGapsInTheFullDepthCutLine` asserted that a part-depth run is a closed loop,
  which is a fact about how the emitter happened to break up its runs rather than about the picture.
  It now checks that a dim run crosses each gap in the bright line — **against the run's segments, not
  its vertices**, which is this sprint's own measuring rule and was got wrong on the first attempt: a
  straight edge is one polyline segment, so the nearest vertex to a tab in the middle of it is ten
  millimetres away.

**Five golden files moved**, all of them the outline programs, and all four differences read the same
way: a narrower or wider tab, a new line saying which edges the tabs are centred on, and the rapid
figures above.

**`/describe-test` on all four test files, and it earned its place on every one:**

- `TabPlacementTests.OnARealBoardEveryTabSitsOnAStraightEdge` asserted `EdgeLengthNm >= 3 × WidthNm`,
  which is the planner's own placement rule restated in the planner's own numbers — it could only
  have failed if the planner contradicted itself, and it looked at no coordinates at all, in a test
  named for tabs sitting on straight edges. It now walks the contour half a tab either side of each
  centre and asserts the profile between them is straight to within a micron. That is the assertion
  the story's whole placement rule rests on, and it was not being made.
- Three assertions could pass over an empty list: the corner check, the edit comparison, and the
  plan-against-program count. All three now say how many tabs they expect.
- The round-profile test compared its gaps only with each other and never measured the closing one,
  so four tabs crowded into one arc would have passed a test named for even spacing. It measures
  against the circumference now.
- `OutlineTabTests.TheTabbedPassesAreAllDeeperThanTheOneThatCrossesThem` was an `Assert.All` over a
  sequence that would be empty if tabbing were removed entirely.
- And on the settings tests written for the default sizes: nothing passed a **zero** through the
  validator, which is the pair every install ships with — a rule written as "not positive" rather
  than "negative" would have refused to save the settings of every machine that had never touched a
  tab. The legacy-file case also asserted an isolation width equal to the record's own default, so a
  loader that ignored the whole `Milling` object would have passed it.
- And two findings in a file this story only touched in passing:
  `LayerOutputPersistenceTests.EverySettingSurvivesASaveAndReopen` says *"every field, not a
  representative one"* and was not checking two of them, and
  `TheOrderLayersWereTouchedInDoesNotReachTheFile` never saved a file — it compared two in-memory
  collections under a name and a comment that both talk about bytes. Both fixed here, because the
  second would have been satisfied by a serialiser that wrote nothing at all.

**Left open**, and recorded under 6.4: moving a tab by hand, keep-out from holes and pads, and the
blank sharing the sizing rule rather than only the placement one.

### What the code review found — 2026-10-01, medium

Nine findings, all acted on. The four that could have reached a board:

- **The tabs window planned from the layer's raw area; the export plans from its positive rings
  only.** A stroked Edge_Cuts realises as an annulus per outline, so the dialog would have shaped a
  different set of paths from the program — different nesting, a different contour order, and
  therefore a tab numbered *3* in the list that is not the third tab in the file. The filter now has
  one home, `ExportPlanner.OutlineProfiles`, and both callers use it. The same fix halved the
  dialog's cost: asking for the plan and the derived sizes separately shaped every profile twice, 102
  of them on the panel, on the UI thread, per click. One call returns both.
- **A per-tab edit survived a change of tab count.** An edit names a tab by its place in the planned
  order, and the order is a function of the count — one to each edge before any edge gets a second —
  so going from four tabs to six moved a 4.5 mm width onto whichever tab now sorted third, and going
  the other way dropped it silently. Changing the count now clears the edits: asking for a different
  number of tabs is asking for a different set of tabs, and keeping them would need a tab to have an
  identity that survives re-planning, which is 6.4's unbuilt "move a tab by hand".
- **A generously rounded corner read as a straight edge.** Straightness was tested between each
  segment and the next, which bounds one turn and not the sum of them; at the default 0.001 mm
  sagitta a curve of radius 11 mm or more turns by less than the tolerance at every step. A 15 mm
  corner radius chained into a 23 mm "edge" — room for a tab, placed on a curve, under a program
  saying *"never across a corner"*. There is a second bound now, about two degrees across the whole
  run, and `ATabIsNeverPlacedOnARoundedCorner` fails without it: a tab lands at 49.76, 0.78 mm, on
  the corner.
- **Nothing warned about a tab too thin or too narrow to do its job.** The 1.5 mm and 0.2 mm floors
  clamp the *derived* sizes; a number typed into the dialog, the settings or `--tab-width` went
  through untouched, so a 0.05 mm web — which breaks while the cutter is still working, with the
  board loose next to it — emitted a program that read as if it were fine. Advice now, not a
  refusal: thin stock and a light touch are somebody's own business.

And five smaller ones: the program's tab note described only the first tabbed profile although the
width is derived per profile (a sheet holding a 21 x 37 mm board and a 60 x 60 mm one gets 2.0 mm
tabs on one and 5.0 on the other); `TabEdits` could arrive as a default `ImmutableArray` from an
explicit `"TabEdits": null` and throw mid-export, where a refusal belongs; the tabs dialog clamped
and rounded a saved size on opening and wrote the result back as the operator's own choice, which is
the clamping-without-telling the settings window explicitly refuses; the new tab-size problems were
raised against the Milling section while their boxes are on New boards, so the window would have
marked the wrong tab; and the comment claiming the new lap start was worth 309 mm of rapid on the
panel read as though the panel came out ahead — it recovers 125 mm of the 184 mm that moving the tabs
cost, and now says so with all three numbers.

**One test failed twice under the full suite and passes on its own:**
`OptimizerBenchmarkTests.APanelIsWhereTheOrderingMattersMost`, which asserts the panel's isolation
orders in under 800 ms of wall clock. Nothing in this story touches the optimiser, and the machine
was running several background agents at the time. Left alone, and noted here because a wall-clock
budget in a test is the shape [06's own note](../Documentation/06-Roadmap-and-Risks.md) records about
the optimizer's budget: it measures the machine as much as the code.

---

## Story 2 — Unnamed copper is called unnamed everywhere

**Problem.** [6.53](../Documentation/06-Roadmap-and-Risks.md#653-copper-with-no-net-name-is-reported-as-safe-when-one-named-net-shares-its-region--defect--open--found-by-code-review-2026-09-29),
found by `/code-review` during sprint 3. Story 3 of that sprint split the residual gaps into copper
carrying **no net name**, which might be a short and which nothing can decide about, and two pieces
of the **same net** meeting, which is electrically nothing. The split is right. Its attribution is
not, in one case: `ElectricalCheck.Isolation` charges a region by counting the *named* nets it holds,
so a region holding one named net **and unnamed copper beside it** has `names == 1` and every merge
in it is filed under same-net — and reported as advice, *"Nothing is shorted by those: they were one
conductor before the cut."* One side of that merge carries no net at all.

The findings view reaches the same wrong answer by its own route: `NetGap.Between` is the union of
both pieces' names and `IsSameNet` is `Between.Count == 1`, so the bridge is filed under *"One net
meeting itself"* and the window prints *"Nothing is shorted by these"* over its coordinates.

**Why second.** It is the same fault as story 1 in a different medium: a plain statement, acted on by
an operator, wrong in the direction that reassures. It can only ever turn a possible short into a
note, never the other way round — and the window it appears in is the one somebody opens to satisfy
themselves a board is sound.

**What.** A merge with unnamed copper on either side is reported as nameless wherever it is reported:
the CHECK panel, the companion page, the CLI and the findings view.

**How, and the trade to measure first.** Both halves need to know whether each *piece* carried a
name, not how many names the region holds. `ElectricalCheck.Gaps` already computes that in its
`namesOf` array and throws it away, so the view's half is nearly free. `Isolation` is the expensive
half: it attributes net points to regions, and attributing them to pieces is another
point-in-polygon pass over the artwork, on the path that runs during every preview and every export
— which sprint 3's story 6 worked to keep cheap.

**So the cost is measured before the shape is chosen**, on the Mega and the panel, and quoted in the
closure. If the per-piece pass is not affordable there, the honest fallback is to report a region as
nameless whenever *any* piece in it is unnamed: it costs nothing extra, it errs towards saying
somebody should look, and it is no worse than today at telling the two kinds apart.

**And the third route a name goes missing**, which the `xhigh` review found: the two halves place net
points against different geometry — `Isolation` against the copper *grown by the cut*, `Gaps` against
the *raw* artwork — so a point a nanometre outside its island is named by one and silently dropped by
the other. A fix here places both halves against one set of regions rather than two.

**Done when** a region holding one named net beside unnamed copper is reported as nameless in all
four places, a test covers exactly that shape, both halves place their net points against the same
regions, and the preview and export timings are quoted before and after.

### Closed — 2026-10-02

**Met.** A merge with unnamed copper on either side is reported as nameless in all four places, the
two halves place their net points against the same geometry, and the cost is measured.

- **`Isolation` attributes a name to the piece it sits on**, and reads the region off the piece. A
  region is same-net only when every piece that merged there carries a name of its own; one named
  net beside unnamed copper is nameless.
- **`NetGap.NamedSides`** counts the two sides rather than the union of their names, and `IsSameNet`
  is now *both named, and named the same*. The union could never tell one named side from two.
- **Both halves locate in the raw artwork**, with the grown region as a fallback for a point that
  lands on a boundary — and a region holding such a point is never called wholly named, because
  nothing can say which piece the name was on.
- **The panel, the companion page and the CLI** take their words from `ElectricalFindings`, which
  reads these counts, so all three moved with them. The sentence widened with the bucket: *"with at
  least one side carrying no net name"*, where it used to say the copper carried none — true while
  the bucket held only regions with no names at all, and no longer. The findings window's heading
  says the same, and points at the ones whose other side is a net you can look up.

**What it cost**, which is what 6.53 asked to be measured before the shape was chosen. The work
snapshot counts point-in-polygon tests and they are the whole of the extra work:

| | before | after |
|---|---|---|
| Arduino Mega, point tests | 13,740 | 14,060 (+2.3 %) |
| Arduino Uno, point tests | 5,222 | 5,370 (+2.8 %) |
| Mega export, wall clock | 4.86 s | 4.94 s |
| Panel export, wall clock | 2.94 s | 2.85 s |

No second offset and no second separation — the expensive halves are untouched, which is why the
entry's worry about the preview path did not materialise and the fallback shape it suggested was not
needed.

**No board in the corpus changes**, and that is the honest result. The test board still reports 25
nameless gaps and no same-net ones at 0.75 mm; the Uno still reports its one genuine same-net merge
at the default cut, both of whose pieces are named; the Mega's 240 merges are still all explained by
a named group. The fault needs a region holding exactly one named net *and* unnamed copper, and none
of the five boards has one — which is exactly why nothing caught it and why a review reading the code
had to.

**`/describe-test` on the one test file touched, and it found three.** The bridge test asserted
`IsSameNet`, `IsShort` and `Between.Count` — and every one of those but the first reduces to
"`Between` holds one name", which is exactly what could not tell this case from two pieces of one
net. It asserts `NamedSides` now, which is the fact the fix turns on. The Mega test checked that
nothing was misfiled as nameless and not that anything was misfiled as same-net, which is the
direction this change could have broken. And the test-board test’s prose claimed "overwhelmingly
nameless" where the line below it says "more nameless than same-net"; the prose moved rather than
the assertion, because 25 against 0 does not need a threshold invented for it.

### What the second code review found — 2026-10-02, medium

**Four of the five were in story 1’s tabs**, which this review reached because it diffs the whole
branch from the sprint’s start rather than the last commit. They are fixed here rather than reopened
there, and the two serious ones are the kind only a reader finds:

- **Two tabs widened until they met cut through every other tab on the board.** The splitter built
  each run from one tab’s far side to the next tab’s near side and wrapped a whole perimeter
  whenever the next tab came out earlier — which cannot tell *this run spans the seam* from *these
  two overlap*. Measured on the 21 x 37 mm board with two 20 mm tabs: the runs covered **190.3 mm of
  a 119.1 mm profile**, every tab crossed, the piece free on a pass that is not the last one with
  the cutter still turning. Only the last run spans the seam now, by position rather than by
  comparing distances, and the test covers the gaps the way the board does: every millimetre of the
  profile is either cut or under a tab, so the runs and the tabs have to add up to it.
- **The board-wide width box was inert, and worse than inert.** `Apply` decides a tab was set by
  hand by comparing its box with the board-wide one, so typing 3.0 at the top left every row holding
  2.0 — which came out as four explicit 2.0 mm edits nobody made, applied *after* the width and
  overriding it. The number changed and every tab was cut at the old one. The top box now carries the
  rows that agreed with it, and a row the operator has already changed keeps what they set.
- **The guard against a default `ImmutableArray` was reached past twice**, in the advice this story
  added — three lines after the comment explaining why it exists.
- **The seam merge tested only the pairwise straightness**, not the overall one, so two runs each
  within two degrees could merge into an edge bending twice that.
- And **nothing warned about a tab wider than the planner would ever derive**, which is the input
  that produces the first of these. It does now.

**The process note was right too.** `TabsShowAsGapsInTheFullDepthCutLine` picks each run’s nearest
neighbour across the whole full-depth layer, so if two depths ever both counted as full depth a run
could pair with its own copy — distance zero, a "gap" that is the run’s own end, and an assertion
that passes because the dim line crosses a point on the line. The gap is now required to be a tab
wide.

**And `/describe-test` again on the two test files the fixes touched**, which is the rule about a
file that gains tests after it was last read. It found both of the new assertions claiming more than
they checked. The overlap test asserted "at least 40 mm is left uncut" — a number picked by hand,
and it passed by three tenths of a millimetre, so a legitimate change in how an overlapping pair is
clipped could have moved it either way. It now unions the tabs’ own spans and asserts the runs and
the tabs add up to the profile exactly: 78.8 mm cut, 40.3 mm under a tab, 119.1 mm of profile. And
the backplot’s new gap check pinned 2.0 mm, which couples a test about the *picture* to whatever
width the planner happens to derive for that board; it asks for four equal, non-trivial gaps
instead.

**Nothing in story 2’s own change was found wanting**, which is worth recording as plainly as the
rest: the per-piece naming, the box sets it pairs with each path set, and the way `unplacedName`
errs toward "look at this" were all checked and sound.

**So the case is three squares.** A named one, an unnamed one a tenth of a millimetre away, and a
second named one far enough off to be its own region — which is there because a layer naming one net
is refused outright, and rightly. Against the old code the first test fails with `Nameless` at 0
where it should be 1, and the third fails because the bridge is filed under *"one net meeting
itself"*. The second is the guard that stops the fix being a relabelling: two pieces of one *named*
net still report as same-net.

---

## Story 3 — A picture on the companion pages

**Problem.** From the workshop: *"in the companion html for drills and routing, include images of the
layer with the holes/slots numbered in the order of drilling/routing."* Both pages are a table of
numbers standing in for a board. They say *six holes with the 1.00 mm bit* and leave the operator to
work out which six, at the two moments the page exists for: deciding a bit is about to go in the
wrong place, and finding where a run stopped.

**The strongest case is the routing page, not the drilling one.** A refused slot is invisible — it is
not in the program, and the board renders exactly as the EDA tool drew it. The page names it in
prose, which is better than nothing and worse than a picture of the board with four slots marked *not
cut*.

**What.** One inline SVG per bit, beside that bit's row in the table, with its holes numbered from
one in run order; slots drawn as their own shape with an arrow for direction; refusals greyed and
hatched with their width labelled.

**How.** Inline SVG rather than a raster, because the pages are self-contained by rule — one file, no
network — and because numbers must stay crisp when somebody zooms into a cluster of vias on a phone
in a workshop. `SvgWriter` already exists and is already shared between the laser path and the
documentation exports, so this is a third caller and not a third implementation.

**Draw the outline and the features, never the copper**: a board's copper as SVG runs to hundreds of
kilobytes on the panel and adds nothing to *where does this bit go*. And the marks come off the
program while the backdrop comes off the board, which is the split the rest of this code already
makes — where the two disagree, the marks are the truth.

**Legibility is what decides whether this is any good.** The Mega drills 258 holes in six sizes;
numbering 44 on a 100 mm board is already crowded. So it degrades honestly: up to about 40 marks in a
step get numbers with leader lines, above that dots with a marked start and the travel path between
them, and either way the count is in the text and the text is the authority.

**Done when** a board with several bits produces one drawing per bit with its holes numbered in run
order, a routing page shows its slots with direction and its refusals hatched, the Mega's 44-hole
step degrades to dots without becoming unreadable, and every page is still one file that opens with
no network.

### Closed — 2026-10-02

**Met, all four clauses.**

- **One drawing per bit with its holes numbered in run order.** The test board's plated drilling page
  carries nine, one per bit, each captioned with the bit it belongs to.
- **The routing page shows its slots with direction and its refusals hatched.** Each run has an
  arrowhead at its far end and its number at the end the cutter enters from; a refused feature is the
  shape it would have been, hatched and dashed, labelled with its width.
- **The Mega's crowded steps degrade to dots** — 98 holes with the 1.00 mm bit, 104 with the 0.41 mm
  — drawn as dots with the travel between them and a ring on the first, with the caption saying so.
- **Every page is still one file that opens with no network**, which the pages' own test enforces and
  which caught the one URL this story tried to add.

**The artefacts, to open rather than read about**: `WorkingFolder/V0.5.0/` holds the test board's
drilling and routing pages as they ship, and `Screenshots/story-3-*.png` are the three figures that
matter — the numbered holes, the Mega degrading to dots, and the slots with their arrows.

**`SlotRefusal` had to learn where it was.** It carried a width, a count and a reason and no
geometry, so a refused slot could only ever be prose — which is the one thing on these pages that
cannot be checked against the machine, because the feature is absent from the program and the board
renders exactly as the EDA tool drew it. It now carries the centrelines it refused.

**Two faults were found by rendering the figures and looking at them**, and neither was visible in
the markup:

- the numbers were placed above and to the right of their holes, so on a column of holes a
  millimetre apart each number sat between two of them and belonged to neither;
- a slot running left to right was labelled on its right, which put its number on its own arrowhead.

Both are one line of arithmetic. Neither would have been caught by any assertion I would have thought
to write, which is this repository's standing claim about pictures arriving as a story about
pictures.

**`/describe-test` on all three test files, and it found the page tests claiming what the figure
tests check.** `ThePageDrawsEveryBitsHoles` counted figures and captions against each other and never
opened a figure or asked the program how many holes a bit drills — it compares the marks in each
drawing against the hole count now, allowing for the ring a crowded drawing puts on its first.
`ABitWithTooManyHolesDrawsTheRunInstead` took the caption's own phrase as evidence that the drawing
had changed; it now asserts a crowded figure carries no numbers at all and an uncrowded one carries
one per hole. The routing tests could both pass with zero steps, and the refusal fixture said four
refused and drew one — a fixture disagreeing with itself in exactly the way the page must not. And at
the figure level: nothing tied the number *1* to the first hole given, the hatch pattern was asserted
declared but never referenced, and a byte count was counting UTF-16 characters.

**No board in the corpus refuses a feature** with the shipped tool library — a refusal needs a slot
narrower than the narrowest cutter and none of the five has one — so the hatching is covered by a
synthetic case and said to be synthetic in the test that covers it.

### What the code review found — 2026-10-03, medium

Seven, and four of them were about shapes nobody had looked at in the one state that produces them.

- **A mirrored layer drew its holes on an unmirrored board.** The marks come out of the emitted
  program, which is mirrored about the frame's centreline when a layer is flipped; the backdrop was
  only translated. On an asymmetric board the holes land off the profile; on a symmetric one they
  look perfectly plausible and are the mirror image of the truth, which is why it is worth a test
  rather than a glance. The backdrop now takes the same three steps the emitter takes — mirror,
  shift, then the drill alignment's turn — and `AMirroredLayerMirrorsThePictureToo` fails without it
  with the two drawings byte-identical.
- **The refused slot's round ends pointed the wrong way.** Both arc caps carried sweep flag 1, which
  in a figure that counts down the page turns them *into* the rectangle: a stadium drawn as a
  rectangle with a bite out of each end. Nothing asserted the flags, so the suite passed on the wrong
  shape — of the one shape this story calls the strongest reason the drawing exists.
- **Above forty marks a refusal became an ordinary cut.** The crowded branch never read `Refused`: it
  drew every mark as a blue dot and threaded the travel line through them, so the features nothing
  will cut were indistinguishable from the ones it will, the travel detoured through places the
  cutter never goes, and the caption still promised they were hatched.
- **A refused *hole* drew nothing at all.** A milled hole refused for being too small is a centreline
  whose two ends are the same point; the band drew nothing and returned, while the caller still wrote
  its width — a number floating on the board with no mark under it. It falls through to a hatched
  circle now.

And three smaller: a number pushed only *downwards* got no leader line, which is two of the eight
places it can go and the dense column is what leaders are for; `Middle` returned the far end for a
two-point run, because element `Count / 2` of two points is the second one; and two doc comments had
been orphaned onto the wrong members by the insertions, leaving one method with two summaries and
another with none.

**What it costs the page.** A figure is about four kilobytes: the backdrop is the outline alone,
thinned to a tenth of a millimetre, which turns the panel's tessellated profile from tens of
thousands of points into hundreds. Drawing the copper instead would have run to hundreds of
kilobytes and answered nothing about where a bit goes.

**Left out on purpose.** The drawings are not clickable and do not pan or zoom: a companion page is
read at a machine, printed as often as scrolled, and a figure that needed a pointer would be worse
in exactly the place it is used. Leader lines are drawn only where a label had to move out of its
first choice — a line from every number to its own hole is a thicket.

---

## Story 4 — The commands that would rebuild the export

**Problem.** From the workshop: *"can we add a new section at the bottom for the cli command used to
create the export... or maybe, a list of cli commands that would be the fastest way to replicate the
outputs using the cli. This would give those power users a big head start on setting up their own
pipelines."*

**The second framing is the right one.** A record of the command that was run only exists when a
command was run, and most exports come from the window, where there was none. Deriving the commands
that *would* reproduce this export works either way, and is more useful in the case that matters:
somebody who set the job up by clicking, likes the result, and now wants it repeatable.

**What.** A section at the foot of the companion page: the sequence of CLI invocations that rebuilds
what was just written, with a line of prose before each saying what it produces.

**How.** Off the finished plan, never off the settings — the same rule the rest of the export
follows. A block written from the settings could name flags the export ignored, which is worse than
no block at all, because somebody would run it. Expect a short script rather than one line: a
double-sided job with stock, a probe and an alignment is several invocations in an order that
matters, and the order is half the value.

**Every path quoted and relative.** Board folders have spaces in them — `Arduino Mega 2560` — and an
absolute path from the machine that exported is wrong on the machine that runs it. `--set` values
need the same care, because layer names have spaces too.

**It has to be honest about what it cannot express.** Anything the window can do that the CLI cannot
gets named in the block as a comment rather than quietly omitted. This is the kind of feature that
finds missing CLI flags, and those are worth fixing rather than papering over — so expect the story
to grow a flag or two and say which in its closure.

**Done when** an export of the test board writes a block that, pasted into a shell in a fresh folder
holding the Gerbers, produces the same programs byte for byte — checked for a single-sided job, for a
double-sided one with stock and alignment, and for a board whose name has a space in it.

### Closed — 2026-10-03

**The block is one pasteable thing.** A `#` comment of prose above each command, the commands between
them, and nothing beside it: what somebody copies is the whole script including its warnings. The
comments are hard-wrapped by the generator at ninety characters rather than left to the browser,
because a comment the page wrapped pastes as one line and would run its own tail as a command.

**Derived from the finished plan, never from the settings.** `CliScript.For` takes the plan and reads
the stock off the rectangle that was resolved, the tabs off the layer that cut the outline, the
isolation width off the layers that were isolated. A refused stock is in the settings and not in the
block.

**The stock is written as four borders**, whichever way it was asked for, because a stated size
centres the board inside it and the command line has no way to say otherwise: borders carry the size
and the placement in the same four numbers and resolve back to the same rectangle through the same
function the CLI uses. The one exception is pre-cut stock that only gets its alignment holes drilled,
which the CLI honours only on a stated size — so that case says `--stock-size`, and says in a comment
when the board is not centred on it.

**A project needs nothing but itself.** `millburn-cli export "Board.millburn" -o "out" --write
--svg-marks stock`, and that is all: the project carries the thickness, the job, the stock and every
layer's output, depth, cutter and tabs. Naming one of them would have been worse than noise — naming
a single job flag makes the CLI build the whole job from flags, and the flags cannot place a board
off-centre on its stock. A folder of Gerbers is the other extreme: every layer named with its own
`--set`, because a layer left unnamed becomes whatever the *running* machine's Settings › Import says
that role becomes.

**It grew four flags.** `export` had no `--isolation-width`, `--tabs`, `--tab-width` or
`--tab-height`; all four already existed on `mill`, and they are spelled here as `mill` spells them.
Without them a block could not say how wide the copper was cleared or how big the tabs are — the two
settings most often changed from their defaults, and the two whose absence would read as agreement.

**And `export --commands`, which prints the block and writes nothing.** The same text the page
carries, from the same renderer, so the terminal and the folder cannot drift apart. It is also the
only way to read the script for an aligned export — an aligned export writes the moved programs and
no page — and it is how every check below was done.

**What it cannot carry is named, not omitted.** A per-layer depth, break-through, pass count, cutter,
mirror or per-tab size; a board off-centre on a stated stock; copper layers isolated to different
widths; and Settings › Machine and the tool library, which shape every line of every program — the
bit each layer is cut with comes out of that library — and belong to the machine that runs the
block. "Byte for byte" is a claim about one
machine, and the block says so above every command.

**Checked by running it, five ways**, each block piped to a file, pasted into a shell in a fresh
folder holding the Gerbers, and the two folders compared file by file. A single-sided export of the
test board with `--only gcode` (23 files); a double-sided one with stock, alignment holes, milled
holes and a drill alignment, where the block is two commands and the second writes the `.aligned`
programs (30 files); a board whose folder *and* every layer name has a space in it (25 files); a
laser-only job with two inverted layers and the placing layers set to the outline alone; and the
author's own saved project (16 files). Identical in every case, under bash and again under
PowerShell.

**`/describe-test` on `CliScriptTests` found three tests weaker than they read.** The round-trip of
the stock's four borders placed the board on whole millimetres, so it would have passed a generator
that printed the borders to the nearest tenth — it places the board at 20.123456, 8.07654 mm now,
which is what makes the six decimals the formatting actually uses load-bearing. The check that every
flag is one `export` documents was plain substring containment, so a dropped `--align` would have
passed on the strength of `--align-outline` still being listed; it matches the whole flag now, and
says so when the help's own headings move rather than throwing out of range. And the project case
asserted six flags absent without asserting the one that must still be there, which is the half of
that claim somebody would get wrong: the export now has a layer going to the laser, so `--svg-marks`
has to appear.

**The project was what found the bug.** Routed slots are written as an outline operation on a drill
layer, so taking the first outline item out of the plan picked the test board's plated slots rather
than its Edge_Cuts and read the tab count off a layer nobody had touched. A project with three tabs
rebuilt with four: a different count, different positions, one extra retract. `IsBoardOutline`
already existed for exactly this and warns about it in its own doc comment. Nothing on the page
looked wrong and no test failed — it took rebuilding a real project and diffing the programs, which
is the whole argument for the Done-when being what it is.

### What the code review found — 2026-10-03, medium

Eight, and only one of them was in this story's own code. The other seven are story 1's tabs and
story 3's figures: the review diffs the whole branch from the sprint's start, so a story merged a
day ago is still in front of it.

**The one here was the worst of the eight.** A project source used to be left to carry its own
settings, which made the command one line and made it wrong. The window exports what is on screen,
and nothing stops that being an unsaved change — so a board whose tab count had just been raised
from four to six wrote a page saying `export "Board.millburn"`, a command that rebuilds the *saved*
four-tab job. That is exactly what this class's own doc says it exists to prevent, and the one thing
worse than no block is a wrong one somebody runs. The flags are the authority now, whatever the
source: a project command carries the thickness, the stock, the job, the tabs and every layer's
output, and the project is only the input. What no flag reaches is named twice — once as the layer's
own setting, once as a line saying the file is read as it was last saved. The project still rebuilds
byte for byte, now from a long command instead of a short lie.

**Three tab flags grew a guard.** `--tabs`, `--tab-width` and `--tab-height` go on every outline
layer, so a board with two of them set differently would have come back with both cut to one of the
two. They are left off when the layers disagree, with a note, exactly as the isolation width already
was.

#### Story 1's tabs, found by the same pass

**An empty box was read as zero, and zero is not a tab.** Clearing the Width box in Tabs and
pressing Enter — Save is the default button — wrote a tab width of 0. Nothing clamped it: zero is
not equal to the derived width, so it was taken for the operator's own number, and the outline cut
the board fully free while its own header said "4 tabs, 0.0 mm wide". An empty box means "as the
board gives it" now, and `TabPlan.For` ignores a non-positive size as a last line, because this is
geometry that cuts a real board.

**A width change re-places the tabs, so the per-tab rows stop being about them.** The width is what
tells the planner how many tabs an edge can hold, so widening it past what a short edge can take
drops that edge and moves every tab. An edit made against the old list then landed on whichever tab
now sorted third — a thin web nobody chose — or was dropped for being out of range. The rows now
follow the board-wide pair and close until the window is reopened on the new layout, with a line
saying why. It reverses: put the width back and the hand edits come back with it.

**A per-tab edit reached every outermost profile.** The edits were handed to each one and applied by
index inside it, so "tab 1, 4.25 mm" — chosen from a list of one board's tabs — also widened tab 1
of a second board in the same Edge_Cuts that nobody had looked at. They go to the one profile the
window lists now, which is the first that places any.

**The derived width came from the wrong profile.** It was the first profile that *could* have had
tabs rather than the one that got them, so a board whose first profile was too small reported one
profile's number beside another's tabs. The window seeds its board-wide box from that number and
each row from its own tab's width, so every row read as hand-set and pressing Save with no changes
wrote an explicit edit for every tab — the one thing `Apply` exists to avoid.

**The tabs window's coordinates were the board's, not the machine's.** It shifted by the board's
corner alone, while the file is written in the frame: on stock, work zero is the *stock's* corner, so
the window was out by the border, and a mirrored outline layer has its x flipped about the frame's
centreline in the file and was not here. It goes through the planner's own `Placed` now — the same
three steps the emitter takes, less the drill alignment, which is a correction measured later and
not part of where a tab is.

**And "were asked for" counted the wrong profiles**: the multiplier was the profiles that received a
tab, so one too small to hold any was left out of it. A job of two profiles where one held four and
the other none reported "4 were asked for: the straight edges hold 4", a sentence that says nothing
happened.

#### Story 3's figures

**A number beside a mark on the edge was drawn outside the frame.** The margin is a millimetre and a
label can be pushed `font * 2.1` from its mark — about 5.8 mm on a 100 mm board — so a hole on the
board's edge had its number outside the `viewBox`, where a browser clips the root SVG. The mark is
drawn and the number is simply missing, on a drawing whose whole job is to say which hole is which;
and holes do sit on the edge, because a castellation is one. Candidates inside the frame are
preferred now and the chosen spot is pulled in as a backstop.

**Six of the eight have a regression test that fails without its fix**, each checked by putting the
fault back and watching it go red. The two that do not are in the window: nothing in this suite
drives Avalonia, so both were read rather than run.

**`/describe-test` on the three test files the fixes touched found four more**, two of them in
sentences this sprint wrote. `AStepDownDeeperThanTheWholeCutStillCutsTheTabs` ended with
`Assert.Equal(0.5, 0.8 - overTabs)` after the line above it had already pinned `overTabs` to 0.3 —
a restatement that cannot fail once the first has passed — and neither line said the board is cut
*through*, so a program of one shallow groove would have satisfied both; it now requires a pass past
the board's thickness. `TheTabbedPassesAreAllDeeperThanTheOneThatCrossesThem` had the mirror image of
a hole a previous `/describe-test` pass closed: with no closed pass at all the crossing depth comes
back as zero, every real depth beats it, and both assertions pass while nothing ever crosses a tab.
The new figure test parsed the font size without checking the match, so a figure declaring none would
have thrown rather than failed, and it estimated each number's width from an anchor it never
asserted — it checks `text-anchor="middle"` now, because the whole measurement rests on it. And
`ThePictureIsAFewKilobytes` would have passed an empty string.

**Two comments were wrong.** The new two-profile fixture is a 5 x 5 mm square and its own doc said
6 x 6; and the note test's comment gave two different numbers for the same thing in one sentence —
"Nineteen is what the old arithmetic would have said — twenty, for the one profile that got any".
Twenty was right.

1,295 tests and 28 goldens green, and all five rebuild checks still come out byte for byte.

---

## Story 5 — A test cut that finds where the copper stops

**Problem.** From the product owner, 2026-09-27: *"A new test for copper thickness. This test will
cut a series of ladders... The first rung is very shallow and each rung gets very small increment
deeper. This test will help users determine the optimal cut depth for their piece. For this
particular test, I think we should increase the granularity of the probe job."*

**The geometry exists; the resolution does not.** `TestCutKind.Depth` already cuts this shape, and
its defaults step **0.05 mm** on purpose, because that series exists to fit a line through
width-against-depth for a V-bit and a narrow spread sits inside a caliper's own error. One of those
steps is larger than the whole thickness of one-ounce foil, about **0.035 mm**: a single rung crosses
from *not through the copper* to *well into the substrate*, and no rung sits where the answer is.

**What.** `Copper`, a kind beside `Depth` and `Feed` in the dialog and the CLI, starting at 0.010 mm
and stepping 0.010 mm over twelve rungs, with a guide page that says how to read it.

**Three design questions were put to the product owner and answered on 2026-09-27**, so this starts
from decided ground: a new kind rather than a retune of `Depth` (the two are read differently and
want opposite defaults); one ladder shallow to deep, with the repeat-the-first-rung check the coupon
already puts at its far end; and the test picks its own probe spacing, finer than the coupon rule
gives, with the guide saying what it chose and why.

**Measured already**, with the existing generator driven at copper resolution — `testcut depth
--lines 12 --from 0.01 --step 0.01 --rungs 0` gives twelve rungs from 0.010 to 0.120 mm on a
**19.0 x 47.5 mm** coupon, about 214 seconds of cutting. The shape is right and it is cheap.

**The probe grid decides whether any of it means anything.** The coupon rule —
`Math.Max(4, Math.Min(StockWidthMm, StockHeightMm) / 3)` — gives 6.3 mm and a 3 x 8 grid on a coupon
that size, so the map interpolates over 8.5 mm between columns while clamped copper-clad is
**0.1–0.2 mm** out of flat. A few hundredths is several rungs of a 0.010 mm step: that does not shift
the reading, it invents it. **And that expression is written out twice**, in `Program.cs` and
`MainWindow.axaml.cs`, so the rule has no home. It gets one.

**A risk to name before it is built.** The stepover comes from the *predicted* width of the
shallowest line, and the premise of this test is that the tool model is not trusted. A blunter tip
than the library believes leaves ribs of copper between passes, which reads as *the copper is
thicker* when it means *the stepover was too wide for this depth*. The guide has to separate those or
the test answers the wrong question convincingly.

**Done when** `Copper` is a kind in the dialog and the CLI with those defaults, the coupon is one
ladder with the repeat at its far end, its probing is finer than the board rule gives and says so on
the page, the spacing rule lives in one place, the guide tells the operator what the two transitions
mean and how to tell ribbing from thickness, and a test pins the emitted depths to their rungs.

### Closed — 2026-10-03

**`TestCutKind.Copper`, beside `Depth` and `Feed`.** Twelve rungs from 0.010 mm stepping 0.010,
twenty overlapping passes each, no width ladder, the repeat at the far end: a 19.0 x 47.5 mm coupon
and 214 seconds of cutting, which is the figure the entry measured — reproduced by the defaults
rather than by seven flags. `TestCutOptions.For(kind, tool)` is where the kinds disagree about their
own numbers, because a copper test that starts and steps where the depth series does *is* the depth
series.

**Nothing on the coupon asks for a caliper.** That turned out to be four separate sentences, each of
which had to be found: the per-line comment telling the operator to subtract 1.501 mm, the predicted
band in the line header, the repeat line's *both should measure the same*, and the CLI's own
`measure` row. A copper rung carries its depth, its pass count and `copper all gone? into the
fibreglass?` — and the guide page is a tick list with a row per rung and two columns to mark, rather
than a column of predicted widths. Found by reading the emitted program and the page rather than by
any test failing.

**The probing rule has one home now.** `TestCut.ProbeSpacingMmFor` replaces the expression that was
written out in `Program.cs` and again in `MainWindow.axaml.cs`. A coupon still gets a third of its
narrow side floored at 4 mm; a copper coupon gets 3 mm, or the coupon rule where that is already
finer. Measured: **96 touches against 21**, six and a half minutes of probing against a minute and a
half, for three and a half minutes of cutting. `CouponSpacingMmFor` is public beside it so the page
can say what the test asked for *and* what it would otherwise have had, without writing the rule out
a third time.

**And a coupon's probe program stopped lying about where work zero is.** It said *"the board's
lower-left corner, the same as every other file in this export"* on a piece of scrap with no board
and no export. `OnCoupon` says the scrap's corner and tells the operator not to re-zero between
probing and cutting, which is the thing that would quietly undo the whole exercise.

**The risk the entry named is a section of the page.** The stepover comes from the width the library
*predicts* at the shallowest rung, so a blunter tip than it believes leaves rows of copper between
the passes — which looks exactly like copper that has not been cut through. Regular, evenly spaced
ridges fading over several rungs are the stepover; patchy copper that goes away over one or two is
the thickness. If the ridges are there, the page says to run the depth and width test first, because
this one will give a confident answer anyway.

**The dialog needed more care than the kind did.** Eight `SelectedIndex == 0` comparisons meant *is
this the depth test*, and `!= 0` meant *the feed test* — which with three kinds is no longer the same
question. They go through one `Kind` property now. Switching the test reseeds the depth-ladder rows,
because leaving 0.050 mm in the step box of a copper test is the depth series wearing its name. And
the rows are relabelled: the program, the page and the summary all say rungs, so the dialog does too.

**Checked by running it.** `--testcut [depth|feed|copper]` is a new headless flag — the dialog needs
no board, so it renders on its own — and all three kinds were photographed to check which rows each
shows. The emitted Z values are the twelve rungs and the repeat; `--probe` writes a 96-touch routine.

**`/describe-test` found four things, one of them a comment claiming more than its assertion.** The
probing test's own doc said the millimetre figure was pinned; it compared the answer with the
constant it comes from, which cannot fail. Three millimetres is a literal in it now. The page test
checked that a `<td>` existed for every rung's depth — but the repeat shares rung 1's depth, so a
page printing one row per *distinct* depth would have passed with a row missing; the rows are counted
now, and the blank cells exactly rather than halved, where 26 and 27 both used to pass. The repeat was
evidenced only as a thirteenth plunge, so the plan's own flag is asserted too. And two absent column
headings were matched case-sensitively, which "Measured band" would have walked straight past.

1,305 tests and 28 goldens green.

### What the code review found — 2026-10-04, medium

Five, and the two that matter most are a crash the new kind made reachable and a check added by an
earlier review that could not fail.

**`testcut copper --lines 0` came out as a stack trace.** The CLI's own help offers it — *"either
half can be left out: --rungs 0 or --lines 0"* — and a copper coupon has no width ladder to fall back
on, so the report has no lines at all and the guide's facts strip takes `Min` over nothing. The same
crash needed two flags before this story and one after it. The CLI refuses an empty test now, with
the reason and *"Nothing was written"*, and the page guards its own facts so no caller can crash
instead. The refusal is kind-aware: a copper test has no ladder to be told it is missing.

**`--step` was silently ignored on a copper coupon.** The CLI read `kind == TestCutKind.Depth` where
it meant *this test steps a depth* — one of the exact sites `StepsDepth` was introduced to catch, and
missed. `--step 0.02` was accepted, dropped, and the rungs came out a hundredth apart anyway, while
the generator's own warning told the operator to step 0.010. A number that shapes the cut is honoured
or refused, never dropped.

**And the seam check an earlier `/code-review` added did nothing at all.** It compared
`SameWay(segments[^1], segments[0], Straight)` with `SameWay(segments[0], segments[^1],
StraightOverall)` — but `SameWay` is a normalised dot product, so it is symmetric in its arguments:
the second line asks about the same two segments as the first with a looser threshold, which cannot
fail once the first has passed. The whole-edge bound was absent at the seam for as long as it had
appeared to be there. The merged edge runs from the last run's *first* segment through the seam to
the first run's *last*, so those are the two it compares now, exactly as the in-run loop does.
`ATabIsNotCentredOnASeamTheProfileCurvesThrough` is a shallow arc with the path's start vertex in the
middle of it: with the fault it puts a tab on a 41.9 mm "straight" edge bending 2.40° end to end, and
the three real boards in the corpus are unchanged either way.

And two smaller: the dialog's new copper branch would have thrown inside a keystroke handler on a
cleared Rungs box, the same fault as the first through a different door; and the block generator
reached `TabEdits.Length` without the `IsDefaultOrEmpty` guard its two neighbours use.

**That last one turned out to be a comment telling a story that is not true.** The guard's stated
reason — that `"TabEdits": null` in a project file deserialises to a default `ImmutableArray` — was
checked by running it: `System.Text.Json` throws `JsonException` while the file is being read, which
is a refusal in the right place, and an absent key gives the `= []` initialiser. So the guard defends
against a value the type permits and no caller produces. All three sites have it now, and none of
them claims a reproduction any more.

1,306 tests and 28 goldens green.

---

## Story 6 — Say where the project came from

**Problem.** From the product owner: *"We should display the gerber folder somewhere so the user
knows where the project is from."* `MillBurnProject.OriginFolder` exists and is used — for the folder
a file dialog opens at, and for *"The source folder '…' is not there any more."* — and is never shown
while a project is open. The title shows the folder's last segment alone.

The author's own recent-projects list holds three entries reading `Millburn_Test_Board`, from three
different folders. **The list can tell them apart because it shows the path underneath; once one is
open, nothing can** — and the operator is a click away from exporting programs built from a board
they did not mean to open. That is this application's whole subject arriving from the least
interesting possible direction.

**What.** The Project info panel shows the folder the project was imported from, elided in the
middle, with the full path available in a tooltip, and says whether this is a saved `.millburn` file
or a folder opened directly — because the two behave differently on a refresh.

**Why here.** It is the smallest story in the sprint and the one with the worst consequence per line
of code. It sits after the three large ones deliberately: it is the recovery point if those run long.

**Not the status bar.** That row is contested, and sprint 3's story 1 showed what happens when
something is added to it without regard for what it pushes out. The Project info panel already
carries what the board *is* and has the Import folder button beside it, so where it came from belongs
in the same place.

**Done when** an open project shows the folder it came from, a long path is readable without resizing
the window, the full path is available somewhere in full, two projects whose folders share a name can
be told apart at a glance, and there is a `--shot` of it.

### Closed — 2026-10-05

**At the head of Project info**, above the board's size, where the Import folder button already is:
`Gerbers`, and the folder's full path. Absolute, in this platform's separators, wrapping when it is
long, with the same path in a tooltip. Nothing about the `.millburn` file — that was offered and
declined.

**It was built elided first, and the product owner sent it back.** This story's own text says *"the
folder the project was imported from, elided in the middle, with the full path available in a
tooltip"*, and the roadmap entry it came from argues at length about telling two folders of the same
name apart. So the first version kept the root and the last two folders and took the middle out:
`E:\…\A Rather Long Folder Name\Millburn_Test_Board`. The reply was *"what I would like to know is
where the source folder is (where the gerber files are)"* — and a path with its middle removed
answers *which of the two is this* rather than *where is it*. Those are different questions and the
plan had the elaboration, not the ask.

**Worth keeping as a lesson rather than only as a fix.** The quoted request is the requirement, and
everything written under it — here and in the roadmap — is one reading of it, written by whoever
picked the entry up. This is the first story in the sprint to be sent back, and it was sent back for
following the reading rather than the sentence. The sentence was the shorter of the two.

**Labelled, because a bare path was read as the project file's own location.** The caption says which
folder it is. And the path is normalised: a board opened from the command line as
`tests/boards/Millburn_Test_Board` showed exactly that, forward slashes in a window of backslashes
and relative to a working directory the operator never sees.

**Four tests, and the one that matters is a negative.** `ADeepPathIsNotShortened` requires every
segment of an eight-segment path to survive and no ellipsis of either spelling to appear — which is
the thing that was wrong, asserted so it cannot come back.

1,310 tests and 28 goldens green.

### What the code review found — 2026-10-05, medium

Six, and none of them in this story. The panel is four lines of XAML and one property; what the
review spent itself on was stories 1, 4 and 5, which is where the code is.

**Changing the outline cutter re-plans the tabs and kept the hand-set sizes.** The third door onto
the same hazard: the tab count clears them, the tabs window clears them when the board-wide width
changes, and the cutter — which nobody had thought of — did neither. The profile is offset by the
cutter's radius *before* the tabs are placed, so a wider bit lengthens every straight edge, moves
the derived width, and changes which edges can hold how many. An edit names a tab by its place in
that order. It clears them now, on an outline layer, for the reason the other two do.

**The command block was silent about the files it does not write.** Dry runs, a probing routine and
a levelled copy are built by whoever runs the export rather than by the planner, so they are not in
the plan and nothing in the generator can see they were asked for — and `export --dry-run --probe
--write` put twenty of them in a folder whose page said nothing about either flag. That is this
class's own standard turned on itself: *"a block that silently leaves a setting out is worse than no
block, because somebody runs it and gets a different job with no sign that it differs."* It names all
three now, in one sentence that is always true, rather than threading three flags through a planner
that has nothing to do with them.

**A per-tab edit of nothing was not a tab either.** `For` re-checks the board-wide sizes for being
positive — *"this is geometry that cuts a real board, so it does not take anyone's word for it"* —
and `Edited` applied the per-tab ones unconditionally, so a hand-edited `"WidthNm": 0` in a project
file put two cut runs end to end and cut the profile clean through while the header still counted
that tab. The dialog cannot produce it; the project file is the route that does not go through the
dialog.

**And three about words.** The copper coupon's header said *"a hundredth at a time"* whatever
`--step` was given, so a coupon cut at 0.020 mm told the operator 0.010 — in the file that is read at
the machine, where the note that would have contradicted it does not appear. Its repeat line still
said *"if they do not **measure** the same"* on a coupon this sprint went out of its way to stop
calling measurable: the per-line comment had been changed and the header had not. And two outline
layers, one sized by hand and one taking the board's, fell through both the flag and the note, so a
rebuild would have derived a width for the layer that had one with nothing said.

**The tabs window discarded edits made since it opened.** `Replaced` restored the rows from a
snapshot taken in the constructor, so a size set by hand and then lost to a width change did not come
back when the width was typed back — which its own doc said it would. The snapshot is taken at the
moment the rows stop being about the tabs below them, which is the last moment they are still the
operator's.

**Four of the six have a regression test that fails without its fix**, each checked by putting the
fault back. The two that do not are the window and the layer row; nothing in this suite drives
Avalonia. The mixed tab-size case has no test either, and for a different reason: it needs two
board-outline programs, which needs two Outline-role layers both going to G-code, and no board in
the corpus has them. The fix is defence, and this sentence is the record of that.

1,315 tests and 28 goldens green, and the rebuild block still round-trips byte for byte.

---

## Story 7 — A tool library you can find a bit in

**Problem.** From the workshop, three requests: **filter by tool type, sorting, and copy a tool.**
All three are one symptom — the list was designed for the six tools it shipped with, and a library
grows. It is insertion-ordered, which is not an order: the 0.5 mm end mill bought last Tuesday sits
between a V-bit and a drill, and the only way to find anything is to read every row.

**A better row may be worth more than all three.** Each entry shows a name and nothing else, and a
name is whatever somebody typed — `1mm Corn Endmill` and `1.2 mm end mill` in one list with no common
shape. A row showing the kind as a small mark, the name, and the diameter or tip in a column of its
own makes the list scannable without sorting or filtering it. **Build the row first and see what is
left to want**; that judgement is part of the story, and its closure says what it decided.

**Then, in the order they earn their place.** Copy, which has the most value and the least design —
tools come in families, and entering the second from scratch is retyping the first. Sorting by name,
kind or diameter, with the tip width standing in for a V-bit's diameter so the two sort together
rather than a V-bit landing at zero. Filtering by kind as three visible buttons rather than a
dropdown, because a filter you cannot see is how somebody concludes their tools have vanished.

**Three rules that are not negotiable**, each of which is a way this story could ship a fault:

- **Copy must not copy the id.** `Tool.Id` is what a project stores, and two tools sharing one are
  the same tool to everything downstream: a job naming the copy would cut with the original. New
  `Guid`, name suffixed, selected with the name box focused.
- **Sort the view, never the file.** `tools.json` keeps its order — it is diffable, and it is what a
  person reads when something has gone wrong. Nothing about the library depends on its order, so the
  sort need not even be saved.
- **New and Copy defeat the filter.** A tool created while a filter would hide it must appear and be
  selected anyway, and the honest way is to clear the filter rather than make an exception to it.

**Done when** a library of thirty tools can be sorted by diameter and filtered to end mills, a tool
can be copied and renamed without retyping its feeds, the copy has its own id, and `tools.json` is
byte-identical after a session that only looked at it.

### Closed — 2026-10-05

**The row first, as the story asked, and it was worth more than expected.** A kind mark, the name,
and the size in a column of its own — a V-bit's tip, an end mill's diameter, nothing for the shipped
drill whose size comes from the drill file rather than from the library. On this machine's own nine
tools that alone makes the list scannable: the kinds group by eye without being filtered, and
`1mm Corn Endmill` sits beside `1.2 mm end mill` with their sizes lined up where their names never
will be.

**The judgement the story asked for.** *Sorting by size* earns its place at nine tools, let alone
thirty — the sizes as the library holds them run 0.13, 0.13, 0.80, 1.00, 2.00, 1.00, 1.00, 1.20,
0.50, and finding "the 1.2" means reading every row. *Copy* was never about the row and is still the
one with the most value for the least design. *Filtering* earns the least of the three: the kind
marks already do its job by eye on a short library, and it comes into its own at the thirty the entry
is written for. All three are built; what the row changed is the ranking, not the list.

**Sorted by the size a tool is known by.** `ToolView.SizeOf` is the tip for a V-bit and the diameter
for anything else, because a V-bit's `DiameterNm` is its shank — 3.175 mm on every one of them — and
an order taken from that field gathers the V-bits in a block at a number nobody chose them for.
`ThirtyToolsSortByTheSizeTheyAreKnownBy` asserts the V-bits come out *spread through* the order
rather than bunched, which is the shape of that fault rather than a remembered position.

**The rule is in `MillBurn.Core`, not in the window.** `ToolView.Arrange` takes the library, a kind
and a sort and returns a new list; the window is the four controls over it. That is what lets a
library of thirty be arranged in a test, and it is why `ArrangingDoesNotTouchTheLibrary` can state
the "sort the view, never the file" rule as a property rather than as a comment.

**The three rules hold, and each has its own check.** The copy gets a new `Guid` and a suffixed name,
and `ACopyCarriesEverythingButTheIdentity` asserts it equals the original once the id and the name are
put back — so a field added to `Tool` later is covered without anyone remembering to extend the test.
`Save` is called from Commit, Delete and Copy, all three behind click handlers, and from nowhere else
in the application. (An earlier draft offered this machine's `tools.json` timestamp as evidence; it is
not evidence, because the operator added a drill to it the same afternoon.) New and Copy clear the filter rather
than making an exception to it.

**Two dialogs, asked for after the first look at it.** From the workshop: *"How about a confirm
dialog when deleting a tool please. And when copying a tool, a small dialog for the user to enter the
new name."*

Delete is the one button in that window that cannot be undone — there is no history, the file is
written immediately, and the only way back is to retype the tool. The question names the bit rather
than saying "this tool", because the list and the form are both still on screen behind it and a
question that does not name its subject is one somebody answers from memory. **And it says what the
loss costs**: a layer that named the deleted tool falls back to a default the next time its project
is opened — a different bit, chosen silently — so when the open project cuts with it, the dialog says
so. The window is told which tools the open board is using for exactly that sentence.

Copy asks first. The button used to make the copy and leave the name box selected, which works if you
notice and leaves a "1.0 mm end mill (copy)" in the library if you do not. The suffixed name is the
dialog's *proposal* now, selected so the first keystroke replaces it, and Cancel makes no copy at
all. `NameWindow` sits beside `ConfirmWindow` rather than inside it: that one answers a question with
a button and this one answers it with a string, and folding them together would give every
confirmation an unused text box.

**A defect found while reading the form.** `Read()` rebuilds the tool from the boxes, and there are no
boxes for `Flutes` or `FluteLengthNm` — so a tool whose flute count had been set by hand in
`tools.json` lost it the moment it was saved here, silently, and `Flutes` is what the chip load is
divided by. Nothing in the app can set them yet, so nothing has been lost; the window no longer
unsets them. Absorbed into this branch rather than filed, as a defect found in the work is.

**`--tools` grew two flags** — `--tools-only` and `--tools-sort` — so the filtered and sorted list
can be photographed rather than only the buttons that would produce it.

**`/describe-test` found a comment of mine that was arithmetically wrong, and two assertions weaker
than they read.** The thirty-tool fixture's doc said its sizes repeat; seven and thirty are coprime,
so the walk is a permutation and every size is distinct — which turns out to matter, because the sort
is now asserted *strictly* increasing. Merely non-decreasing is also what a `SizeOf` returning a
constant would produce, and a stable sort would then leave the insertion order and pass the "V-bits
are spread, not bunched" check beside it; the two holes lined up. The composition test claimed in its
own doc that filtering-then-sorting equals sorting-then-filtering and then made a single call, so it
compares the two now; and it counted the end mills without checking *which* eighteen came back, which
a filter that swapped one for another would have walked straight past.

1,329 tests and 28 goldens green.

### What the code review found — 2026-10-05, medium

Six, all of them in this story's own window, and the first is the one worth the pass on its own.

**The list stopped scrolling, in exactly the long-library case the story is for.** Putting the two
new controls above the list made the sidebar a vertical `StackPanel`, which measures its children
with infinite height — so the `ListBox` reported the height of every row it had and its own scroller
never engaged. The review reasoned it from Avalonia's measure contract and said plainly that a shot
of this machine's ten tools would not show it. Against a thirty-tool library it is worse than
reasoned: the list runs straight off the bottom of the window, there is no scrollbar, and **sixteen
tools cannot be reached at all**. The feature built for long libraries, broken by long libraries. The
sidebar is a `Grid` now — two `Auto` rows and a `*` — and a screenshot of the same thirty scrolled to
the end shows tool 29 and a scrollbar.

**Saving a kind change made the tool vanish.** Filter to End mills, change a tool's Kind to V-bit,
press Save: the file was written correctly and then the tool was no longer in the filtered list, so
nothing could be selected, and the empty selection reset the form to a blank new tool. Three wrong
signals from one correct write. New and Copy already cleared the filter for this reason; Commit was
the hole in "what is on screen always matches what the buttons say".

**Changing the sort discarded whatever was typed.** Reassigning the list's source clears the
selection and the selection reloads the form, so a feed rate typed and not yet saved was gone the
moment the operator sorted the list to find the next bit. Before this story `Refresh` only ran after
a save, where that was harmless. It is guarded now, so a rebuild of the list does not reload the
form.

**And three smaller.** After a delete the form loaded the library's first tool rather than the
list's, so with a filter on it could be editing something no row pointed at while Delete stayed live
against it — a second click would have deleted a tool nobody selected, with the new confirmation the
only thing in the way. Copy took the stored tool rather than the form, so edits typed before pressing
it went into neither the copy nor anywhere else. And the private `Show(Tool?)` overloaded
`Window.Show`: it bound correctly only because C# drops base-class candidates once a derived one
applies, and it read at the call site as showing the window twice.

**One claim in this story's own closure was wrong**, and the review did not find it — checking the
library's timestamp did. The closure offered *"`tools.json` was last written on 26 September"* as
evidence that looking at the window does not write it. The file was written at 18:48 that afternoon,
because the operator added a 0.5 mm drill to it. The argument is unchanged and the evidence had to
be: `Save` is reachable from three click handlers and from nothing else in the application.

**`--tools-from <file>`** loads a library from a path for a screenshot, which is how the thirty-tool
case was seen. Borrowing the operator's own file to make one is not a thing to do while they are
using the app.

1,329 tests and 28 goldens green.

---

## Story 8 — Rulers, and the pointer's own numbers

**Problem.** Asked for in the workshop twice, a week apart. A scale down the left edge and along the
top of the viewport, so the size of what is on screen can be read rather than guessed at
([6.1](../Documentation/06-Roadmap-and-Risks.md#61-rulers--enhancement--fixed)); and *"Put a small
rectangular display in the top right corner of the UI and have it display the coordinates. Also, if
possible turn the mouse into a crosshair"*
([6.48](../Documentation/06-Roadmap-and-Risks.md#648-where-the-pointer-is-in-the-boards-own-numbers--enhancement--fixed--asked-for-in-the-workshop)).

**It has a reader now, which it did not have when it was written.** The findings view prints a
coordinate for every place the copper stays joined — *"44.05, 30.31 mm"*, from the board's lower-left
corner, which is where work zero is. A ruler reading from the same corner is what lets somebody find
it.

**And 6.48 was asked for the day it would have saved an afternoon.** 6.26 was reported as *"a
misplaced hole"* with no position, because there was no way to read one off the screen; finding it
again took a screenshot measured against the outline in a script.

**What.** A ruler along the top and left of both viewports, a continuous readout of the pointer's
position in the board's own numbers with the origin named beside it, and a crosshair cursor over the
canvas.

**Decided from mockups on 2026-09-30.** Five were rendered over a real Mega at true scale, in
`WorkingFolder/mockups/rulers/`, and the product owner chose **B**: a translucent strip floating over
the viewport rather than a gutter insetting it. The board is the thing being looked at, and a gutter
takes a strip out of it on two sides.

**How.** It belongs in `MillBurn.Viewer` beside the grid, because a ruler is a function of the
transform and the viewport rectangle and nothing else — so `BoardView` and `ToolpathView` both call
it, and the headless PNG renderers get it for free, which is what makes it checkable in a `--shot`
like everything else here.

**Reuse the grid's step ladder**, and notice that there are currently two of it —
`BoardRenderer.GridSteps` and `ToolpathRenderer.GridSteps`, the same nine numbers declared twice. A
ruler choosing its own spacing would disagree with the grid it sits over at some zoom, and a
disagreement is worse than no ruler; a ruler agreeing with two ladders that have drifted apart is
worse still. One ladder, in one place, used by the grid and the ruler in both viewports.

**Zero is the board's corner**, because that is where the machine's zero is, and Y counts upward
rather than down the screen. A ruler measuring from the window's edge would be a second coordinate
system on screen, and it would eventually be believed.

**Also in it, cheaply:** the measuring drag — click-drag with a modifier for dx, dy and the diagonal
between two points. A tape measure, not a drawing tool, and not saved. **Toggleable and remembered**,
beside the existing theme and colour preferences.

**Left out on purpose:** a ruler in inches, and printable dimensioned output. The app is millimetres
throughout, and mixing units in one view is how a wrong number gets read confidently.

**Done when** both viewports carry a ruler that agrees with the grid at every zoom on the ladder, the
pointer's position reads continuously with its origin named, the cursor is a crosshair over the
canvas and an arrow everywhere else, the measuring drag gives dx, dy and the diagonal, the setting
survives a restart, and a `--shot` at whole-board and at bridge zoom shows both.

### Closed — 2026-10-06

**One ladder, and it had already drifted into two.** `GridLadder.Steps` holds the nine spacings once;
`BoardRenderer` and `ToolpathRenderer` each took their own copy before, one picking the first past
twelve screen pixels and the other the first past sixty, so the two viewports drew grids of different
densities at the same zoom and nothing said so. The ruler ticks against the number the grid was drawn
with, which is what makes the agreement structural rather than a thing to keep checking.

**The ladder behaved as the story predicted.** Whole-board on the Mega reads 0 to 100 in tens; at
90 px/mm it reads 39.5, 40, 40.5 — the 0.5 mm majors the story wrote down in advance, out of the
ladder rather than out of a second rule.

**It was drawn under the board, and only a screenshot would ever have said so.** Under the board
reads perfectly at whole-board zoom: the strips sit in the margin and nothing reaches them. At bridge
zoom copper fills the viewport and the ruler disappeared completely — at exactly the zoom it is for.
The mockup chosen on 2026-09-30 was B, floating over the board, and a thing that floats over the
board has to be drawn after it. Both renderers now draw it last.

**The frame, not the board.** Zero is the lower-left corner of the rectangle the emitted programs are
referenced to, which is the stock's on a job built on stock and the board's otherwise — the same
`Frame` the exporter shifts by and the preview draws over. So the line under the readout says *from
the board's corner* or *from the stock's corner* according to what the open project actually has,
rather than naming one and being wrong on the other. `--change blank` was added to photograph the
second case: on `Millburn_Test_Board` with stock, the ruler runs 0 to 90 across the 88.63 mm blank
and the readout says *from the stock's corner*, which is the corner the checks panel calls work zero
three lines below it.

**And photographing it found a defect that is not this story's — [6.57](../Documentation/06-Roadmap-and-Risks.md#657-ticking-build-on-stock-does-not-move-the-viewport-onto-the-stock--defect--open--found-while-building-61-2026-10-06).**
With no preview built, ticking *Build on stock* does not rebuild the scene at all: `OnOutputChanged`
returns early when nothing is drawn, so the viewport keeps the board's frame while the panel beside
it already describes the stock. Press Preview and both correct themselves. The readout code is right
— it reads whatever frame the scene was built to — and the frame is stale. Written up rather than
fixed here, because the fix is in the preview-interruption guard and that is not this story.

**It is a defect the ruler created the conditions to see.** Before this story nothing on screen said
which corner the view was referenced to, so a stale frame showed only as a board that did not move.
Now it is a number, and a wrong number is read and believed.

**The readout clears when the pointer leaves the canvas.** Left at its last value it is a number that
looks live and is not, which is the unnamed-origin fault wearing a different coat.

**The measuring drag reports both sides as well as the hypotenuse.** Shift-drag, because the plain
drag is pan and pan is what the hand does without thinking. dx and dy are what somebody moving a
machine types; reading them off a diagonal is arithmetic. Backwards keeps the sign on each side and
not on the distance.

**`--shot` had no way to reach a zoom or a pointer, so three flags were added.** `--zoom` centres a
board point at a given pixels-per-millimetre, `--pointer` places the readout, `--measure` runs a drag
between two points, and `--change blank` builds the job on stock — the live readings cannot be reached headlessly any other way, and the
story's own Done-when asks for a shot at bridge zoom.

**`--zoom` was wrong on its first use, and the shot is what said so.** Asked for the bridge at
y 30.31, it centred y 293: world y counts *against* board y from the frame's rectangle, which is the
renderer's flip and the one sign here easy to get backwards. A board-height out and twice. Fixed, and
`BoardYCountsUpTheScreen` states the two directions separately rather than as a round trip — a
mapping that negated y in both directions would round-trip perfectly and still draw it upside down.

**`--no-ruler` exists because every other shot proves nothing about the binding.** The setting and the
control's property both default to on, so a broken binding would produce shots identical to a working
one. That flag is the only one that changes if the toggle does not reach the viewport. It goes through
`RememberRuler`, which keeps the value for the session without writing the file: a shot is taken
against whatever settings file is on the machine and must not leave the operator's rulers switched
off. The real file's checksum is unchanged across every run taken for this story.

**The guide lines were invisible over copper.** The two sides of the measuring triangle were white at
alpha 0x55, which reads on bare substrate and disappears on a trace — and a board is mostly trace. The
first shot that showed them was over the one part of the Mega that is not copper. 0xAA now.

**Remembered, and an upgrade does not take it away.** `View ▸ Show rulers`, a checked menu item bound
two-way to the preference so the tick is the stored value rather than a copy of it.
`TheRulerIsOnUnlessItWasTurnedOff` covers both halves: a settings file with no key for it reads back
on, and one written off reads back off.

**That test failed first, for the right reason.** The fixture was written in camelCase and the reader
is case-sensitive, so the file did not parse at all — which returns defaults, which made the
"defaults to on" assertion pass while proving nothing. The Theme assertion beside it is what caught
that, and is why it is there.

1,337 tests and 28 goldens green. Shots at whole-board, at 90 px/mm with a measuring drag, with the
rulers off, and both stock cases are in `WorkingFolder/V0.5.0/Rulers`.

### What the code review found — 2026-10-06, medium

Eight findings, seven fixed and one already written up as 6.57.

**The one that was already known, and the half of it that was not.** The readout's origin and the
ruler's zero go stale when the frame changes without a preview — that is 6.57, found while
photographing the stock case and recorded before the review ran. What the review added is that
`OriginNameProperty` was missing from `AffectsRender`, so even once the frame is rebuilt the string
changing on its own would not repaint. In the list now.

**`--no-ruler` could have turned the operator's rulers off for good, and nearly proved it.** It went
through `RememberSettings`, whose own doc comment says the quiet part: *"It still survives a restart,
because closing the window saves the placement, and that goes through `SaveSettings` and writes the
whole object — this value with it."* It did not happen, only because `SavePlacement` returns early
when the window was given a `--shot` or a `--size` — a guard in another file, written for another
reason, that this was leaning on without saying so.

**And the hazard was not hypothetical: a run taken for this very story wrote that file.** The
checksum was unchanged across the first few shots, which is what the closing note claimed; it changed
later, over the `--change blank --preview` run, and `"ShowRuler": true` is in the author's settings
now. True is the default and no harm was done — but had the flag been a `Settings` value at that
moment it would have been written as false. It is a `bool?` field the settings record never sees now,
and a run combining `--no-ruler` with a preview leaves the file byte-identical.

**A hover re-rendered the whole board.** `PointerAt` was in `AffectsRender`, and
`BoardDrawOperation.Equals` always returns false, so every mouse sample re-executed the draw — every
layer path and the grid — including with the rulers switched off, where nothing on screen could
change. Sweeping a dense board cost nothing before this feature and costs nothing again: the two
handlers invalidate for themselves, and only when there is something to redraw.

**"Letting go ends it" was only true of letting go.** `_measureFrom` was cleared in
`OnPointerReleased` and nowhere else, so alt-tabbing mid-measure — or anything else that cancels a
capture — left the dashed line and its dx/dy tracking the pointer on every later hover, forever.
`OnPointerCaptureLost` ends it too.

**A dash effect leaked once per frame of a drag.** `SKPaint.Dispose` does not dispose an attached
path effect, which is why `BoardRenderer` disposes its own by hand; the measuring triangle's guides
built a fresh one every frame. One static now.

**The grid step was computed twice in each renderer** — once for the grid and once for the ruler.
They agreed, which is exactly the state the two copies of the ladder were in before they drifted. It
is passed in now, so a changed threshold cannot reach only one of them.

**`--zoom` failed silently and did not clamp.** It is called during `OnOpened`, before any guaranteed
layout pass, so the early return is reachable — and a shot that quietly came out at the whole-board
fit would read as *the ladder gives 5 mm majors at bridge zoom*, a false claim in a picture. It
returns a bool and the shot line says so. And `--zoom 100000` now lands at the wheel's own 8000
px/mm, reading 44 to 44.1 in tenths, rather than a state no gesture could produce or undo.

**The "mm" legend sat on top of the first left-hand number.** A label's baseline is three pixels
above its tick, so a tick just under the top strip put its digits in the corner the legend occupies —
and the legend, drawn last, showed the number through itself. Ten pixels of clearance.

1,337 tests and 28 goldens still green, and the bridge, clamp and rulers-off shots were retaken after
the fixes.

---

## Story 9 — A finding takes you to the copper it is about

**Problem.** From the product owner, the day after the findings view shipped: *"The Findings box is
great, especially being non-modal. Having the rulers in the viewer so the user can find and zoom into
the problem areas based on the findings x and y mm values would be awesome."* Reading the coordinate
is one thing; getting there is another, and today the only route is panning and zooming by eye to a
tenth of a millimetre.

**What.** Click a place in the findings, and the viewport goes to it and marks it.

**It has to zoom, not only centre**, and the mockups settled that. A bridge is a fraction of a
millimetre across, so at whole-board zoom it is smaller than a pixel and a view that merely centres
leaves the operator looking at the same board with a marker they cannot see —
`D-crosshair-and-finding.png` makes that plain, and `E-zoomed-to-finding.png` is the same finding at
a zoom where the copper is legible.

**The window stays non-modal.** That is what lets the list sit open beside the board while the view
moves, and it is the property the request named first.

**How the marker clears**, designed up front rather than discovered:

- **Picking another place moves it.** One marker at a time — a view that accumulates rings becomes a
  second findings list, drawn worse.
- **Escape clears it**, with the viewport focused.
- **A new check, preview or export clears it**, because the finding it came from is gone. Sprint 3's
  story 5 already applies that rule to the panel's highlight, and for the same reason: *"a mark that
  outlives the thing it explains is worse than none, because it looks like a property of the
  check."*
- **Panning and zooming do not clear it.** Exploring around the place is the entire point.
- **Closing the findings window does not clear it**, which is why the marker carries its own short
  label — the coordinate and the nets. A bare ring left on the board after the list is gone is a mark
  nobody can interpret.

**Not in it.** Selection semantics. This is a marker: nothing acts on it, nothing is measured from
it, and calling it a selection would invite both. Measuring belongs to story 8's tape measure.

**Done when** clicking a place in the findings view moves the viewport to it at a zoom where the
copper is legible, the marker names what it is, it clears by each of the routes above, and there is a
`--shot` of the before and the after.

### Closed — 2026-10-06

**Fourteen millimetres across, taken off the mockup.** `E-zoomed-to-finding.png` reads 37.0 to 51.0,
and that is the span `Marker.SpanMm` holds. Clicking *44.05, 30.31 mm* on the Mega puts the ruler at
37 to 51 with a pointer on the top strip at 44 and another on the left at 30 — which is what makes
the picture prove the view arrived rather than merely claim it. That is story 8 earning its keep one
story later, exactly as the roadmap predicted it would.

**The span is measured across the shorter side**, not the width. Across the width it would be a
promise about one axis that a short window quietly breaks, and the bridge somebody was sent to would
be off the top of a view claiming to show it.

**The coordinate is the button.** Not a *go to* control beside the row: the verb belongs on the noun,
and the noun is already the only part of that line anybody reads twice. A `Button` with a transparent
background rather than a handler on the `TextBlock`, so it is focusable and reachable by keyboard for
nothing, and the list looks exactly as it did.

**The mark is drawn twice, dark underneath.** It has one colour and the board has two extremes — bare
substrate and bright copper — and a single stroke that reads on one vanishes into the other. The
first shot put a pink ring on orange copper that could barely be found. **This is the third time this
sprint that the same fault has appeared**: story 8's measuring guides at alpha 0x55, then its label
over copper, now this. A halo is cheaper than choosing a colour per background and cannot be wrong.

**The label carries both lines in one box.** It was split at first — coordinate boxed, nets loose
underneath in the mark's own colour — which is unreadable over most of a board; and the box was sized
from the nets line while containing only the coordinate, so a long net name gave a box three times
the width of its contents. Both lines go in, and the box is outlined in the mark's colour so it reads
as belonging to the ring.

**It has to carry its own label because closing the findings window does not clear it.** That rule,
written before any of this was built, is the whole reason the label exists; a bare ring left after
the list is shut is a mark nobody can interpret.

**Clearing, as designed.** Picking another place moves it. Escape clears it with the viewport focused
— and the view stays where it is, because somebody who cleared the ring is looking at that copper on
purpose and moving them away would undo what they asked for. A new check, preview or export clears
it, which is sprint 3 story 5's rule about the checks panel's highlight applied to the same kind of
mark. Panning and zooming do not, and nor does closing the list.

**`--goto <n>` presses the nth place**, because a screenshot cannot click, and the shot is then of
the board rather than of the list — the list is already photographable with `--findings` alone, and
it stays open behind, which is the non-modal property the request named first. `--clear-marker` shows
what Escape does.

**`/describe-test` earned its place again, on three counts.** The span test round-tripped the scale
through the constant, so both sides moved together and fourteen millimetres — a decision read off a
mockup — was guarded by nothing; 14 is written out now. The degenerate-viewport test had three cases
of which only one exercised the guard, and said so only after being told. And the test named *the way
the findings view does* compared against a literal that looked like the findings view's output
without ever touching its formatter; it now puts the same nanometres through both routes. A fourth
test asserted that a record stores its parameters, and is now the one claim worth making there: that
the two adjacent strings cannot be transposed.

1,343 tests and 28 goldens green. The list, the view after a pick, and the view after clearing are in
`WorkingFolder/V0.5.0/Findings`.

### What the code review found — 2026-10-07, medium

Five findings, all fixed, and the first is the most serious thing this sprint has turned up.

**The findings list printed its coordinates in the wrong frame on a job built on stock —
[6.58](../Documentation/06-Roadmap-and-Risks.md#658-findings-were-reported-from-the-boards-corner-on-a-job-built-on-stock--defect--fixed--found-by-code-review-2026-10-07).**
`BoardFindings` shifted every located gap by `board.Bounds`, while the exporter references its
programs to the job's *frame* — the blank's rectangle when there is one. The checks panel says so in
words three lines from that window: *"its lower-left corner is work zero for every other file in
this export."* On the Mega's default 10 mm border every number in the list was 10 mm low in both
axes, so an operator reading *"44.05, 30.31 mm"* and driving there would arrive 14 mm away,
diagonally, on a board whose traces are 0.15 mm apart.

**It had been on screen since the findings view shipped, and looked like a plausible number.**
Printed, nothing contradicts it. This story is the first thing to consume those coordinates
geometrically rather than print them, and that is what put the error somewhere it could be seen —
the ring ten millimetres from the bridge it was labelled with, at a zoom where ten millimetres is
most of the viewport.

**Two other defects conspired to hide it.** The first attempt to photograph it came out looking
right, because 6.57 means ticking *Build on stock* does not move the viewport onto the stock — so
both frames were still the board's and they agreed. Adding `--preview`, which does rebuild, is what
separated them. Fixed by passing `Frame` into `BoardFindings`: the same bridge now reads 44.05, 30.31
with no stock and 54.05, 40.31 with a 10 mm border, and the ring lands on the same copper either way.

**And the fix had a trap of its own, which the suite caught.** The new parameter was written
`Bounds frame = default`, meaning "none given". `default(Bounds)` is `(0, 0, 0, 0)` and
`Bounds.IsEmpty` tests `MinX > MaxX`, so it reads as a real frame whose corner is the origin — and
every existing caller silently got a shift of nothing. `AGapIsReportedInTheFrameTheMachineWorksIn`
failed immediately, because it asserts a located gap lands inside the board rather than matching a
remembered number. That test was written in sprint 3 against a different fault and has now caught a
second one; the parameter is `Bounds?` and `NoFrameMeansTheBoardsOwn` states the distinction.

**The mark outlived the board it explains.** Check, preview and export cleared it; Refresh source and
opening another project did not, so re-plotting in KiCad and pressing Refresh left the ring where it
was, with the old coordinate and the old net names, over copper from a different board. It now
clears on the scene's *extent* changing, which is the honest predicate — that is the rectangle the
coordinates are measured from. Panning, zooming and hiding a layer leave it alone, as the rule asks;
re-importing, opening another board and turning stock on do not. One place instead of four doors.

**`--goto` failed silently, which it had already been told not to do.** The first board tried for the
stock shot has no findings at all, so the flag quietly did nothing and produced a whole-board
screenshot — the exact "false claim in a picture" `--zoom` had been given a message for one story
earlier. It now says when the list is empty, when the index is past the end, and when there is no
layout to go to. `--goto 0` and negatives silently selected the first row, because `Skip(-1)` is
`Skip(0)`.

**The label could still fall off the left edge.** The flip tested the right edge and then used the
left-hand position unchecked, so a long net name on a marker in the left half of a narrow viewport
put most of the box outside the window — the very outcome the flip exists to prevent, reached by the
flip itself. It is clamped after the flip now, and clamped to below the top strip rather than to the
viewport, which is not the same line: a marker near the top of the board laid its label over the
scale its own pointer points at.

**And a doc comment had two `<summary>` elements**, of which only the first is documentation.

### What `/describe-test` found on the way — 2026-10-07

Running it on `BoardFindingsTests`, because this story's fix added two tests to that file, turned up
a third defect that is not this story's and is the worst of the three —
[6.59](../Documentation/06-Roadmap-and-Risks.md#659-the-findings-cancellation-test-stopped-the-run-before-it-started--defect--fixed--found-by-describe-test-2026-10-07).

`ARunStoppedPartWayThroughSaysSo` cancelled after `first.Index` lookups — the position of the first
isolation layer, which on the Mega is **zero** — so the very first settings lookup turned the token
and the abandoned run reported no layers at all. Its two assertions passed on that: it was
cancelled, and nought is fewer than two. A test named *a run stopped part way through says so* was
proving that a run stopped before it started says so.

**That is the `LatestRun` fault, in a second file.** It is the one CLAUDE.md names as the reason this
skill is mandatory, and it was found the same way. `PlanningCancellationFixture` warns about exactly
this pattern three files away — *"A test that cancels after two and calls that 'two layers in' has
actually stopped the run before it started planning anything"* — and the findings test did it
anyway. `after: 1` and an `Assert.NotEmpty` now; the abandoned run reports one layer of two, so it
has demonstrably finished one and then been stopped.

**It also found a weakness in a test written an hour earlier.** `NoFrameMeansTheBoardsOwn`
distinguishes "no frame" from `default(Bounds)` only because the Mega's artwork does not start at
the origin — on a board drawn from zero the two shifts coincide and the assertion would fail for a
reason nobody could read off it. The premise is asserted first now, with a message that says what it
is.

**Two things it reported that were left alone**, both pre-existing and neither this story's: the
same-net test pools declared net names across all layers while its comment says "that layer", and
the `Nameless` bucket is printed and never asserted anywhere in the file.

1,345 tests and 28 goldens green. The stock case is in `WorkingFolder/V0.5.0/Findings` as
`gone-to-it-on-stock.png` and `list-on-stock.png`.

### From the bench — 2026-10-07

*"The findings dialog is not closed when opening a recent project or upon close project."*
[6.60](../Documentation/06-Roadmap-and-Risks.md#660-the-findings-window-stays-open-over-the-next-board--defect--fixed--reported-from-the-bench-2026-10-07),
fixed on this branch rather than deferred: it is the same window this story just taught to do
something, and the fault it describes is the one this story spent the afternoon on — a view of a
board that is no longer there — one level up.

**The property that makes the window useful is what left it behind.** It is the only non-modal
window the application opens from a real handler, and non-modal is what the original request named
first. But nothing held a reference to it, so nothing could shut it.

**Closed from `Adopt`**, the one place a board is actually replaced — import, open, and close, which
is `NewProject`, which is `Adopt` with an empty project. A layer edit, a preview and an export do
not go through it, so they do not close it, which is right. One event instead of five call sites.

**A list, not a reference**, because F7 opens another window each time it is pressed. Holding only
the latest would have left every earlier one behind, which is the reported fault still happening and
harder to notice.

**Both doors were run, because the report named both.** `--close-project` and `--open-project` run
after every other flag, and the capture now prints `findings windows open: N` — a shot of the main
window cannot show a separate top level sitting behind it. Findings open and nothing else done
prints 1; either door prints 0.

**One note on the run:** opening a project puts it at the top of the recent list, so the test-board
project moved up in the author's own settings. That is what opening a project does, not something
this did to the file.

### What the second code review found — 2026-10-07, medium

Four findings, all fixed. **Two of them are regressions from the previous review's own fixes**,
which is worth recording as plainly as the fixes were.

**The pointer readout froze when the pointer left the canvas —
[6.61](../Documentation/06-Roadmap-and-Risks.md#661-the-pointer-readout-froze-when-the-pointer-left-the-canvas--defect--fixed--found-by-code-review-2026-10-07).**
`OnPointerExited` cleared `PointerAt` and nothing redrew, so the box stayed showing the last
position — the exact *"number that looks live and is not"* its own doc says it prevents. It was
correct when written: `PointerAt` was in `AffectsRender` and clearing it repainted for free. The
previous review found that this re-rendered the whole board on every mouse sample, taking the
property out of that list was right, and it left this method setting a value nothing would draw.

**The rulers could not be turned back on after `--no-ruler` —
[6.62](../Documentation/06-Roadmap-and-Risks.md#662-the-rulers-could-not-be-turned-back-on-after---no-ruler--defect--fixed--found-by-code-review-2026-10-07).**
The session-only override was added by the previous review to stop a screenshot run writing the
preference to the operator's file. Correct — and it added a second reader of the preference without
telling the writer, so the setter still compared against the stored value, saw no change, and the
menu's tick snapped back.

**Both are the same shape**: a fix that was right in itself, landing next to code that depended on
the thing it changed. Neither would have been caught by the suite, because neither is reachable
from a test that cannot open a window.

**`--only stock --commands` said the export writes nothing —
[6.63](../Documentation/06-Roadmap-and-Risks.md#663---only-stock---commands-said-the-export-writes-nothing--defect--fixed--found-by-code-review-2026-10-07).**
`BlankOnlyPlan` never filled `Commands`, so the flag printed a refusal about an export that does
write a file. A refusal that states something untrue about what it refused sends somebody looking
for a reason that is not there. The emitted command was run: one file, the stock program.

*An aside worth keeping:* the review named the flag `--stock-only`, which does not exist — it is
`--only stock`. Run as written it printed the commands for the **whole** export and exited 0, which
looked at first like the finding being wrong and was actually a different thing: the CLI accepted a
flag it does not know and carried on. The finding was right once the real spelling was used.

**The tabs window named the board's corner on a stock job —
[6.64](../Documentation/06-Roadmap-and-Risks.md#664-the-tabs-window-named-the-boards-corner-on-a-job-built-on-stock--defect--fixed--found-by-code-review-2026-10-07).**
The same defect as 6.58, in the next window along, found by the next review. Its own class doc
already stated the correct rule and the heading did not follow it. It takes `OriginName` now. On the
test board the first tab reads 79.13, 30.34 with 10 mm of stock and 69.13, 20.34 without — the
border, exactly — and the heading changes with them.

**Three windows in this sprint have printed a coordinate while naming the wrong origin**, and each
was found only when something else made the two disagree visibly. That is now written at the foot of
6.64 as a rule: anything printing a position takes its origin's name from the frame it was shifted
by, rather than stating one.

1,345 tests and 28 goldens green. Both tabs headings are in `WorkingFolder/V0.5.0/Findings`.

---

## Deliberately out

**[6.54](../Documentation/06-Roadmap-and-Risks.md#654-the-checks-panel-cannot-show-a-cancelled-run--enhancement--open--found-by-code-review-2026-09-29) — the checks panel cannot show a cancelled run.**
The flag, the status line and the banner all exist and none can be reached, because the only
cancellation today is supersession by a later press, where returning early is correct. It becomes
worth having the moment an operator has a way to stop a run, and it should be proved by a test on
that day rather than assumed to have worked all along. Nothing in this sprint adds that button.

**[6.55](../Documentation/06-Roadmap-and-Risks.md#655-the-findings-view-costs-about-twice-what-it-needs-to-and-cannot-be-interrupted--enhancement--open--found-by-code-review-at-xhigh-2026-09-30) — the findings view costs about twice what it needs to.**
**First reserve**, and the one item here most likely to be pulled in: story 9 works in that window,
and a story that makes the findings list clickable while leaving that window slower than it needs to
be will feel like half a job. Held out rather than folded in because the two are independent — the
marker does not need the view to be faster — and because folding a performance fix into a feature
story is how neither gets measured.

**[6.45](../Documentation/06-Roadmap-and-Risks.md#645-block-apertures-and-the-aperture-transforms--defect--open--no-fixture-exists-yet) — block apertures.**
Still no fixture. It cannot start.

**[6.47](../Documentation/06-Roadmap-and-Risks.md#647-isolation-cuts-inside-a-hole-that-is-about-to-be-drilled--defect--open--found-in-the-workshop-low-priority-a-later-sprint) — isolation inside a hole.**
Built in sprint 3 and withdrawn by the product owner, because the fix made two nets' separation
depend on the drill. The patch is kept in `WorkingFolder/held/`; it should not be re-attempted in
that shape.

**[6.50](../Documentation/06-Roadmap-and-Risks.md#650-nothing-can-test-the-view-model--enhancement--open--three-times-in-one-sprint) — nothing can test the view model.**
It cost sprint 3 three times and it will cost this one at least twice: stories 6, 8 and 9 are all in
`MillBurn.App`, which the test projects cannot reach, so all three close on a `--shot` and a run
rather than on a test. That is the argument for doing it and not the argument for doing it *here* —
it is a project-structure change, and putting it in a sprint this size would be the tenth item.
**It is the strongest candidate to be a sprint of its own**, in the way 6.44 was.

**The features.** V30 the paste stencil, 6.18 the 3D-printed stencil, 6.2 climb or conventional, 6.7
the coachmarks. None was asked for recently and none is on the held list.
