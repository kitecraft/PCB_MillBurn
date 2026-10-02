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
| [6.1](../Documentation/06-Roadmap-and-Risks.md#61-rulers--enhancement--open) + [6.48](../Documentation/06-Roadmap-and-Risks.md#648-where-the-pointer-is-in-the-boards-own-numbers--enhancement--open--asked-for-in-the-workshop) | Rulers, and the pointer's own numbers | The workshop, twice: a scale down the edges, and *"display the coordinates"* |
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

---

## Story 8 — Rulers, and the pointer's own numbers

**Problem.** Asked for in the workshop twice, a week apart. A scale down the left edge and along the
top of the viewport, so the size of what is on screen can be read rather than guessed at
([6.1](../Documentation/06-Roadmap-and-Risks.md#61-rulers--enhancement--open)); and *"Put a small
rectangular display in the top right corner of the UI and have it display the coordinates. Also, if
possible turn the mouse into a crosshair"*
([6.48](../Documentation/06-Roadmap-and-Risks.md#648-where-the-pointer-is-in-the-boards-own-numbers--enhancement--open--asked-for-in-the-workshop)).

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
