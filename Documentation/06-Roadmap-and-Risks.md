# 06 — Roadmap, Acceptance Criteria & Risks

## 1. Phasing

Each phase ends with something demonstrable. No phase is longer than it needs to be to produce a
thing you can look at.

### Phase 0 — Foundation *(small)*

- `git init` — this workspace is not a repository yet, and it needs to be before anything else.
- Restructure into `src/` + `tests/` per [01 §2](01-Architecture.md#2-solution-layout); create the
  class libraries as empty shells with the dependency graph wired. **This step is identical under
  every UI framework**, so do it before settling the shell question.
- Add Clipper2, NetTopologySuite, CommunityToolkit.Mvvm, SkiaSharp, xUnit.
- **UI shell spike (2 days)** per [07 §6](07-UI-Framework-Decision.md#6-what-id-actually-do):
  Avalonia + Dock.Avalonia + a Skia viewport panning 500k segments + AvaloniaEdit + the IceLight
  token palette ported to `ThemeDictionaries`. Keep it if it feels good; fall back to WPF if not.
- Retire or trim the existing MAUI project accordingly (if kept: drop Android/iOS/MacCatalyst
  targets, leaving `net10.0-windows10.0.19041.0`).
- CI: build + test on push.

**Done when:** empty solution builds, tests run, and a window opens with a Skia canvas that pans
and zooms a synthetic 500k-segment path at 60 fps. That last number is the acceptance test for
the framework choice, not a nice-to-have.

**Result: done.** 14 projects, 11 tests, zero warnings with `TreatWarningsAsErrors`. Avalonia
kept. The viewport sustains **98 fps** through the real render loop across a full fit -> 40x -> fit
zoom sweep on 500,247 segments (`--fpstest`); offscreen CPU raster measures mean 5.5 ms / p95
20 ms (`--bench`). Getting there needed level-of-detail plus spatial culling, and the first
attempt at culling was 400x *slower* than none because of a defect in the per-tile bounds — see
[05 section 2.2](05-Viewer-and-Export.md#22-rendering-skiasharp).

### Phase 1 — Read and draw a board

- Gerber RS-274X + X2 parser: apertures, macros, polarity, arcs, step&repeat.
- Excellon parser.
- Layer auto-detection; drag-and-drop a folder.
- SkiaSharp Gerber viewer with layer toggles, pan/zoom.

**Done when:** drop a KiCad output folder on the window and see the board, correctly, including a
board with aperture macros and negative polarity. **Met**: both real boards and all 24 corpus
files load, realise and render, macros and negative polarity included.

**Done.** Parsers, geometry realisation, layer detection and the viewer are all in.

- `MillBurn.Geometry`: `Tessellate` (tolerance-driven arc and circle flattening, never a fixed
  segment count) and `Polygons` (booleans, area, bounds, inversion, and a canonical form so a
  golden hash means the geometry changed rather than that Clipper swept the edges differently).
- `MillBurn.Cam`: `ApertureShapes` realises every standard template and **every specified macro
  primitive** — circle, vector line, centre line, lower-left line, outline, polygon, thermal,
  moire — with per-primitive exposure compositing and rotation about the macro origin.
  `GerberRealiser` does flashes, strokes, regions and polarity; `PolygonArtwork` bridges the
  result to the SVG writer.
- `MillBurn.Cli render` reports area, ring count, vertex count and timing, and `--svg` writes the
  realised geometry out to look at.
- `MillBurn.Pipeline`: `LayerRoles` identifies each file from its X2 `.FileFunction`, falling back
  to filename patterns and *saying so* when it does; `BoardLoader` loads a whole export folder,
  drill files included, realising holes and slots into area like everything else.
- `MillBurn.Viewer`: `BoardScene`, `BoardRenderer`, `BoardSceneBuilder` and `BoardPalette` — the
  board as filled area, with paint order, default visibility and a substrate derived from the
  outline.
- `MillBurn.App`: drag-and-drop a folder (or any file in one), a layer panel with per-layer
  toggles, colour swatches and counts, a "check this" panel for anything doubtful, and live frame
  cost. `MillBurn.Cli board --png` and the app's own `--shot` render the same stack headlessly, so
  the viewer is checkable from a terminal.

**The board scene has no level of detail and no spatial tiling, and that was measured rather than
assumed.** Those exist in `ToolpathScene` because a toolpath runs to hundreds of thousands of
segments; a board does not. PogoTest1 is 10,045 vertices and renders in **0.04 ms**. Phase 0's
lesson was that guessing here produced a 48x48 grid that made things 140x slower, so the rule is
now to build the simple thing and let the number decide. The tiling machinery is one class away if
a panelised board ever needs it.

Two decisions worth remembering:

- **Roles come from `.FileFunction` first, filenames only as a fallback, and a guess is labelled.**
  Filename conventions are a guess dressed up as a rule — `.gbl` means different things to
  different tools, and renaming a file to something tidy silently changes what the app believes it
  is. Getting the role wrong routes the wrong geometry to the wrong operation.
- **The board is drawn on its own substrate**, filled from the outline. Layer colours model
  physical reality — copper is copper, silk is white — so they cannot follow the UI theme, and
  white silk on a light theme background is invisible.

Two decisions worth remembering:

- **Circles built as apertures contain the true circle; arcs flattened from a file are inscribed
  in it.** The first is the safe direction for copper — isolation offsets outward, so copper
  modelled slightly large keeps the cutter clear of real copper. The second has no choice: a
  flattened arc must pass through the endpoints the file stored, or a region fails to close.
- **A negative file is reported, never inverted.** Inverting needs the board outline, which lives
  in a different file, and the drawn area is what CAM wants from a mask layer anyway — the
  openings are exactly what gets lasered. `Polygons.Invert` does the job when a real frame exists.

Verified on both boards and all 24 corpus files, with nothing skipped. The strongest checks are
the ones with an independent answer: `PogoTest1-Edge_Cuts` realises to 5.7664 mm2 against a
perimeter-times-pen-width prediction of 5.767, and `GridStripConnector-F_Cu` resolves to exactly
three copper islands, matching the three named nets the parser found.

- Gerber RS-274X + X2: lexer, modal state machine, standard apertures, full aperture-macro
  expression evaluator, region compositing, arcs kept as arcs, step and repeat, and X2 attributes
  in **both** encodings (`%TF%` blocks and KiCad 9's `G04 #@!` comments).
- Excellon: KiCad decimal plus the leading/trailing zero-suppressed dialects, G85 slots, routed
  slots, plating and tool functions.
- `MillBurn.Cli inspect` prints the parse report from the risk table below.
- Verified against 24 external corpus files and two real KiCad 10 boards: **zero errors**.
  The NanoV3.3 board resolves 34 apertures with SMDPad/ComponentPad/ViaPad/Conductor classified.
- Two bugs worth remembering, both of which produced a *clean-looking* empty result:
  the closing `*` of an extended command was left on the body, so every aperture parameter parsed
  as `0.5*`; and files write a bare `D10` with no `*` terminator, which merges with the next line,
  so deferring aperture selection to the end of a block let a `D03` overwrite it.
- Not yet: block apertures (`%AB%`), aperture transforms (`%LM/LR/LS%`). Both are reported as
  errors rather than silently ignored; neither appears in the corpus or in KiCad output.

### Phase 1.5 — Projects — **done**

Not on the original plan; it arrived from using the app. The container had to exist before Phase 2
added the first settings worth saving, because dirty-tracking and the replace guards touch every
mutation and are far cheaper to add to a small app than a large one.

- `.millburn` zip container: `project.json` plus embedded copies of every source file.
- Dirty tracking, New / Open / Save / Save As, and guards on close, on drop, and on import.
- **Refresh from source**: re-import after a KiCad edit and keep the settings.
- `MillBurn.Cli project save | info | refresh [--apply]`, so the whole thing is scriptable.

Four decisions worth remembering:

- **A project embeds its sources, and hashes them twice.** Referencing a folder by path means a
  re-export silently changes the geometry under the saved toolpaths — no error, just a wrong
  board. Embedding also makes the migration to "self-contained" unnecessary later, which would
  otherwise have meant supporting both forever.
- **The two hashes answer different questions, and only one is worth showing.** An EDA tool stamps
  a creation date into every file, so re-exporting an unedited board changes *every byte of every
  layer*. A content hash therefore reports nine changed layers on every export, and a warning that
  is always wrong is a warning nobody reads. The content hash detects that a file was touched; the
  **geometry fingerprint** — `Polygons.Fingerprint`, promoted out of the determinism tests where it
  was already doing exactly this job — says whether the board actually changed.
- **Selections are stored by identity, never by position or index** (`SelectionRef`). "The 47th
  flash" changes if anything is added earlier in the file; coordinates detach when a component
  moves 0.1 mm. Net names and component references are the designer's own identifiers and X2 puts
  them in the Gerber, so a selection made against them still resolves after an edit. That is the
  third distinct payoff from keeping the X2 attributes.
- **View state is persisted but never dirties the document.** If peeking under a layer prompts a
  save, people learn to dismiss the prompt without reading it, which is worse than never prompting.
  For the same reason the drop guard stays silent when nothing has been configured: the
  frictionless path is the feature.

Refresh is reviewed before it is applied — per file, with what changed in numbers — because a CAM
tool that swaps geometry underneath you without saying so is how a board gets scrapped. Applying
marks the project dirty, so closing without saving reverts it; that is the whole undo story for
now, and it is honest. **Not yet built:** the before/after ghost overlay in the viewport.

### Phase 2 — Mill toolpaths + G-code + backplot — **done**

- Clipper2 offsets; isolation passes; V-bit effective-diameter model; minimum-clearance DRC.
- Drilling with grouping and peck.
- Outline with tabs, containment tree, ramped entry.
- Naive ordering (nearest-neighbour) — deliberately *not* the good optimizer yet.
- G-code emitter + processor chain + GRBL post.
- G-code parser and **backplot viewer** with travel-move layer and stats.

**Done when:** a board goes Gerber → G-code → viewer, and the numbers in the stats panel match
what the machine actually does. This is the first genuinely useful build.

**Done.** Gerber → G-code → viewer works end to end: `MillBurn.Cli mill <folder> --png`, or the
**Mill** button in the app. Isolation, drilling and outline-with-tabs, ordered nearest-neighbour,
emitted as GRBL-safe G-code, then **parsed back and drawn over the board**.

- `Tool` carries the **V-bit effective-diameter model**, `width = tip + 2·depth·tan(included/2)`.
  A 30° bit with a 0.1 mm tip at 0.05 mm deep cuts **0.127 mm**, and 0.01 mm of depth error moves
  that by 5.4 µm. The width is reported, never typed in — it is a consequence of the depth, not a
  setting. The included angle is the *full* angle; both conventions are in circulation and reading
  it as a half angle doubles the answer.
- `IsolationOperation` offsets the copper union by half a cut width per pass. Offsetting the union
  rather than each island is what makes the hard case right for free: where two traces are closer
  than the tool is wide, the offsets merge and no path is produced — which is the truth.
  `UnreachableGaps` counts exactly those, because a gap the tool cannot enter leaves the copper
  connected *and draws nothing*, so the picture looks perfect while the board is shorted.
- `OutlineOperation` cuts **outside** the profile, steps down in depth, and leaves tabs spaced by
  arc length rather than by vertex — offset contours bunch their vertices at corners, so spacing by
  index would put every tab on one corner.
- `DrillOperation` groups by size, largest first, and pecks. **No canned cycles by default**: GRBL
  does not implement G81/G83 and ignores what it cannot parse, so a canned drill file travels the
  pattern without ever going down — a board with no holes and no error.
- `NearestNeighbour` is deliberately the naive baseline for Phase 3 to beat. It does consider both
  ends of a candidate, which pcb2gcode's greedy pass does not; reproducing that bug to flatter the
  successor would be dishonest.
- `JobBuilder` fixes the order — isolate, drill, cut out — and references the job to the board's
  own lower-left corner. Gerber coordinates come from wherever the board sat on the EDA canvas
  (PogoTest1 lands at X150 Y−90), so emitting them raw would need work zero set at a point the
  operator cannot see or measure.

Two things caught by looking at the output rather than the tests: ordering restarted at the origin
for every operation instead of chaining, which both ordered badly and reported a travel figure
dominated by one long move in (total rapid 402 mm → 232 mm once fixed); and the peck loop was
described in a comment but never actually emitted, so drills plunged full depth in one go.

**Tools are a saved library, chosen per operation.** Traces and edge cuts want genuinely different
cutters and that is physics, not preference: a V-bit's width follows its depth, which is what makes
a 0.13 mm isolation cut reachable at all and an end mill that narrow unaffordable — while the same
V-bit taken to 1.9 mm for the outline would be millimetres wide at the surface. So `ToolLibrary`
persists to `%AppData%/PCB_MillBurn/tools.json`, `ToolSelection` picks one per operation, and the
app has an editor that shows what a tool will *do* as its numbers are typed, because the cut width
is a consequence rather than a setting.

Two decisions carried over from elsewhere in the design:

- **A project embeds the tool it was cut with**, exactly as it embeds its Gerbers. Pointing at the
  library by name would mean that adjusting a tip width to suit a newly bought bit silently changed
  the toolpaths of every project that ever used it. Stable ids let the app still say "this project
  used 0.10 mm; your library now says 0.12".
- **The cone ends at the shank.** A real engraving bit is a cone ground onto a straight shank, so
  past that depth the cut stops widening — and `DepthForWidth` refuses a width beyond it rather
  than returning a depth the bit cannot physically reach.

The library also makes validation possible, and each of these produces a program that looks
entirely reasonable: isolating with an end mill (one width everywhere, so it cannot separate
anything closer than itself), cutting the outline with a V-bit, going deeper than the cone, or
taking the whole board thickness in one pass.

**The backplot draws the parsed file, not the toolpaths that made it.** Those two agree right up
until the emitter has a bug, and only one of them is what the machine will run. `GcodeParser` is a
proper modal-state interpreter — motion mode, units, distance mode, feed and every unmentioned axis
are all inherited — and `GcodeBackplot` classifies each move into cut, plunge, retract, travel,
long travel, and **gouge: a rapid below Z0**, which is the tool crossing the board at cutting depth
and is never legitimate. Checking that here rather than in the emitter is the point.

Round trip verified on a real board: 907.54 mm emitted, 907.57 mm read back — 0.003%, which is
three-decimal coordinate rounding over 2,993 points and nothing else.

**Time is reported as a bracket, not a number.** Feed-only is a true lower bound; stopping at every
corner is a true upper bound. Junction handling decides where a real machine lands between them,
and modelling it is Phase 3's job — so both ends are shown and neither is dressed up as the answer.
Most of a PCB job is segments far too short to reach the programmed feed, which is exactly why
distance ÷ feed is such a poor estimate.

Three more found by looking rather than testing. The backplot was drawn in **blue over a blue
ground pour** and was invisible — the palette now uses only hues the board does not (yellow cuts,
magenta long rapids). Then it was drawn 150 mm off screen, because the job is referenced to the
board's corner while the board is still in source coordinates; `Job.OriginShift` now records the
translation so a viewer can undo it. And the run-joining compared an offset point against a raw
one, so every move started its own run: 2,978 instead of 15, drawing correctly the whole time.

**Output is per layer, one file each, and the panel is organised around that.** A job crosses tools
and often machines, so a single program holding isolation, three drill sizes and the outline assumes
one operator babysitting one long run. Split per layer, a broken bit costs the drilling rather than
the board — and it is the only shape that works at all when the silkscreen goes to the laser while
the outline goes to the mill. `LayerOutputSettings` carries what one layer becomes, `LayerOperations`
says which pairings are meaningful (a laser cannot drill, so that choice is not offered), and
`ExportPlanner` turns a board plus those settings into the set of files to write — **without writing
any of them**. Planning and writing stay separate because the check is the point: which layer became
which file, what tool it assumes, how deep it goes, and whether anything about the combination is
wrong. Every SVG in one export shares a page, so the layers overlay when imported; cropping each to
its own extents is the mistake that puts the second burn out by the difference between two crops.

The left panel follows the same shape: **Project info**, **Layers**, **Export**, as collapsible
drawers, with Export pinned to the bottom so a nine-layer board cannot push it off-screen. Each
layer row carries its own output dropdown, its tool, and only the numbers its operation actually
has — a depth for isolation, a break-through for anything that goes all the way through, tabs for
the outline. **Visibility and export are deliberately separate**: you constantly want the soldermask
on screen to check a pad against while cutting only the copper. The checkbox is the eye; the
dropdown is the machine.

Two settings moved out of the project and into `AppSettings`, at `%AppData%/PCB_MillBurn/settings.json`:
**layer colours** and **board thickness**. Colours because which ones read well is a fact about the
operator's eyes and monitor rather than about the board — "I can't see that layer" is a complete
blocker, not a preference — so they must not travel inside a project or change when one is opened.
Thickness because it is one physical fact about the material in the machine; what varies per layer
is how far past the back to break, which belongs to the operation.

Two things the pictures caught that no test would have. The Fluent button repaints its own
background on hover, so a colour swatch bound to `Background` stopped showing the layer's colour at
exactly the moment someone moved to click it — the colour now sits on an inner `Border`. And
`Avalonia.Controls.ColorPicker` ships its theme in its own package: without the `StyleInclude` the
control resolves no template and renders as an empty rectangle. A blank dialog, never an error.

`MenuItem.InputGesture` is display-only, so the menu advertised Ctrl+S and did nothing when it was
pressed. The keys are now dispatched for real, on the bubbling route so a control that wants a
keystroke still gets it first.

**Bottom-side programs are mirrored, and say which way to flip the stock.** Found by asking what a
user would actually get if they set `B_Cu` to G-code: nothing anywhere mirrored it, so the answer
was a program that cuts the bottom side backwards. That is the worst shape a bug can have here — it
looks completely correct in the viewer and in the backplot, and it is only discoverable by trying
to fit a part to the finished board. `ExportPlanner` now reflects bottom-side toolpaths about the
board's own vertical centreline, which keeps the geometry in the same bounding box and so leaves
work zero at the board's lower-left corner in *both* setups — the corner the operator can still see
after the stock is turned over. Arc sweeps flip with the points, because a reflection reverses
handedness and a G2 that should have become a G3 takes the long way round the circle, straight
through the middle of the board. The flip is stated in the file header, in the export summary and
in CHECK, because it is the one step that cannot be recovered from once cutting has started.

**The mirror is a per-layer checkbox, not a rule, and it applies to SVG too.** Deciding it purely
from the role turned out to be a guess wearing a fact's clothes: burning a mask onto a transparency
that will be laid face-down wants the opposite handedness from engraving the same layer directly,
and a single-sided board laid out on the bottom copper wants neither. `LayerOutputSettings.Mirrored`
is therefore `bool?` — null takes the side's default — so a caller that knows nothing about
mirroring (the CLI, a test, an older project) still gets the physically correct answer instead of
silently cutting the bottom side backwards. Asking the question also exposed that only G-code was
being mirrored at all; a bottom silkscreen exported as SVG came out reversed, and getting one output
right while the other was wrong would have been worse than getting both wrong. Departing from the
default warns in *both* directions, because both overrides are legitimate and neither is visible in
the file that results.

This is checked against the same geometry planned as a top-side layer. That is the only comparison
that isolates the flip: everything else about the two programs is identical, so any difference
beyond the reflection is the transform being wrong. Comparing against a polygon centroid was tried
first and was too loose to prove anything — offset contours do not share a vertex distribution with
the copper they came from.

**Every drawn layer can be recoloured, including the ones no file produces.** Reported as a bug:
the cutting moves, travel moves and long rapids could not be changed on either platform. The cause
was the colour store's shape rather than the UI — it was keyed by `LayerRole`, and a backplot layer
has no role, so the swatch was simply disabled for it. Scene layers are now keyed by their own id,
the same stable strings the view state already uses to remember which layers are hidden, and the
substrate's one-off setting migrates into that map on load rather than being dropped. This matters
more than it sounds: the backplot palette picks hues the board does not use precisely so a program
is never invisible against the copper it was made from, and which hues those are depends on the
layers showing and the monitor in front of the operator — which is the one thing the palette cannot
know.

**The window comes back where it was left, and a screenshot run never writes one.** Two traps, both
avoided deliberately: the saved size is the client size that gets set on restore rather than the
frame size, because storing a frame and restoring it as a client area adds the border thickness back
every launch — measured here as 16 x 39 px, which is how a window grows a little each time it opens.
And a placement is only honoured while enough of its title bar still lands on a connected screen,
because a window restored onto an unplugged monitor is invisible, undraggable, and fixable only by
editing a settings file the user does not know exists. Maximised is stored separately from the size,
so un-maximising gives back a normal window rather than a screen-sized one.

**The app opened with unsaved changes it had invented itself.** Restoring the saved board thickness
in the view-model constructor went through the observable property, which fired the changed handler,
which called `Touch()` — so the first click of any session asked whether to save an empty project.
It only appeared when the saved thickness differed from the built-in default, which is why it
survived every screenshot. Fixed at the root, and again at the guard: `NeedsSaving` is
`IsDirty && Sources.Length > 0`, because a project holding no sources holds nothing however dirty it
believes itself to be. A prompt that appears when there is nothing to lose is not an extra
safeguard — it is what teaches people to dismiss the prompt without reading it.

**A project did not remember what its layers become, and that is the document.** It embedded the
Gerbers, embedded the tools it was cut with, kept the hidden layers — and the per-layer output
settings were session state, so opening a saved project handed back the defaults. Nothing about the
file showed it; the job simply came out differently the second time.

`ProjectSettings.LayerOutputs` holds them now, keyed by file name — the only identifier that
survives a refresh, since roles repeat and indices move when a file is added. The view model writes
straight through to the project rather than keeping its own copy, because two answers to "what does
this layer become" means the one that gets saved is whichever was updated last. Untouched layers are
not recorded at all, so their defaults can still improve without silently overriding a choice nobody
made.

Fixing the storage exposed the consumer: the CLI's `export` built its own role defaults even when
handed a project, so a configured, saved job still exported as if it had never been configured. It
reads the project's own settings first now. `project save` gained the same `--set` as `export`, so a
configured project is scriptable rather than only clickable — which is also what makes the round
trip checkable without a person in the loop.

### Phase 3 — The optimizer — **done**

- GTSP model with entry-configuration sets, closed-loop free start.
- Trapezoidal time cost model.
- Greedy + 2-opt + Or-opt + configuration flip, with candidate lists and don't-look bits.
- Precedence DAG.
- Eulerian path merging; travel-at-depth when safe.
- Douglas–Peucker simplification + G2/G3 arc fitting.

**The cost model came first, because everything else is only as good as what it minimises.**
`MotionPlanner` is GRBL's own algorithm: a junction speed per corner from the machine's deviation
setting, then a backward and a forward pass to make every junction reachable, then a trapezoid per
segment. That matters because time is not distance — below `v²/a` a move never reaches full speed
and costs `2·sqrt(d/a)`, so quartering a rapid's length only halves its time, and an optimizer
scored on distance makes choices a time-scored one would not. Checked against closed forms rather
than against itself: a 100 mm rapid at 40 mm/s and 200 mm/s² is 2.7 s, the two formulae agree at
the 8 mm crossover, and splitting a straight move into four changes nothing.

**The ordering is a GTSP over entry configurations.** Both ends of every open run are candidates
from the start; a closed contour is entered at whichever vertex is nearest, which is a closed-form
answer rather than a search because a loop's entry and exit coincide; and local search is Or-opt as
well as 2-opt, because relocating one stranded contour is the move that fixes the reported symptom
and reversing a run cannot do it. Groups are a hard partition, so precedence cannot be traded away
for a shorter route.

Measured against the nearest-neighbour baseline that shipped — which already considers both ends,
so it is not a straw man:

| Board | Operation | Before | After | |
|---|---|---|---|---|
| GridStripConnector | isolation | 50.7 mm | 14.0 mm | −72% |
| PogoTest1 | isolation | 95.4 mm | 78.7 mm | −18% |
| PogoTest1 | drilling | 114.0 mm | 100.3 mm | −12% |
| 50-up panel | isolation | 1274.8 mm | 952.4 mm | −25% |
| 50-up panel | outline | 1168.6 mm | 962.3 mm | −18% |

The panel's 198 contours order in 63 ms and its 1,152 outline passes in 250 ms, inside the 500 ms
Balanced budget.

Three things found by measuring rather than by testing. The route started at the coordinate origin
rather than at the board, so PogoTest1 carried a 175 mm lead-in from wherever it sat on the EDA
canvas — which both skewed the first choice and swamped the reported saving. The emitter parks back
at work zero, and that last hop was 35 mm of a 79 mm total: nearly half the rapid in the file, and
invisible to an optimizer that stops at the last cut, so the tour is closed now. And the model's
travel disagreed with the file's until both were fixed — they now agree exactly, 952.4 mm against
952.4 mm on the panel, which is the check that says the number in the UI is the number the machine
will do.

**A 66-up panel was cutting the frame and leaving every board attached.** `BuildOutline`
took only the largest ring, which is right for one board and silently wrong for everything else —
it would drop an interior slot the same way. Every positive ring is now a profile, and each is
offset on its own: offsetting them as one polygon set makes Clipper fill the whole thing, so the
frame's own rectangle covers every board inside it and they vanish. Cutting went from 2,862 mm to
19,586 mm on that panel, and single boards are untouched. The worst shape a bug can have — the file
runs, the frame comes free, and the boards are still in it.

**Precedence is a chain per contour, not a global ordering — and the difference is most of the
rapid.** Reading Documentation/03 §5 as "all contours finish one depth before any starts the next"
made the panel outline three times worse, because it crosses the whole panel once per depth step.
The constraint it actually states is *on the same contour*. So a `Stack` marks the passes that must
run consecutively and in order, and the optimizer orders stacks freely within a containment tier.

Two further orderings inside a stack were wrong before they were right, and both were found by
measuring the exported file rather than by reasoning: taking one *open* run to full depth before
moving on costs its whole length in travel each time, because an open run ends at the far end of
itself — so the tabbed passes go depth by depth around the profile, where the only cost is the tab
gap, and the last run's end is already beside the first run's start. Single-board cutout travel came
back to 46 mm, byte-identical to what shipped; the panel's is 1,922 mm and safe, against 793 mm for
an ordering free to cut a place to full depth before it had been cut shallow.

**Paste can be milled as well as burned.** Asked for from experience rather than from the plan:
people mill the cured soldermask off the pads, using the paste apertures as the areas to clear.
`PocketOperation` does contour-parallel clearing — offset the boundary in by half a cut width, then
step inwards until nothing is left — with each opening as its own stack so a pad is finished before
the tool moves. Openings smaller than the tool are counted and reported rather than approximated,
because a pad the tool cannot enter keeps its mask and the picture would look perfect.

The depth default had to become per-operation for this. A single 0.05 mm suits isolation and goes
clean through 20-40 µm of cured mask into the copper underneath, so `LayerOutputSettings.DepthNm` is
nullable in the same way `Mirrored` is: unset takes the default for whatever the layer becomes, and
a depth the operator typed is never replaced. The export warns, with the chosen depth quoted, that
the whole cut is shallower than an ordinary board's flatness — which is exactly the trouble that was
reported with it, and the strongest argument yet for Phase 5's height mapping.

**Tabs go on the outermost profile only.** Reported from a 66-up panel: every one of the
boards was getting four tabs of our own, on top of the tabs the designer had already drawn between
them, leaving a panel that had to be cut apart by hand. A tab holds a piece to the material around
it, so the only boundary that needs one is between the job and the stock — which is exactly the
`nesting == 0` test the containment tree already computes. Panel cutout travel fell from 1,922 mm to
772 mm as a side effect, because the inner profiles became closed contours again instead of
four tabbed runs each.

**Drill files written as Gerber X2 now drill.** KiCad's *Generate Drill Files* offers X2 instead of
Excellon; picking it produced a Gerber full of circles that realised beautifully and drilled nothing,
and the board would have come off the machine solid with no warning. `GerberDrills` reads the holes
from the `D03` flashes, taking each diameter from the aperture's own parameter rather than measuring
it back off the polygon it was drawn as.

That export also writes a **drill map**, and it was being read as 688 plated holes: the file declares
`Drillmap`, the filename says `PTH`, and the filename was winning because an unrecognised function
fell through to the guess. A file that says what it is and says it is not part of the board is
*known*, not unknown — so declared documentation functions now stop the fallback instead of
triggering it.

**A project read X2 drill files differently from a folder, and lost the holes.** `LoadSources` —
the path a saved project takes, because its files live in the container rather than on disk — chose
its parser from the layer's *role*. An X2 drill file has a drill role, so it went to the Excellon
parser, which found no holes in it and warned about a units declaration the file plainly had. The
drill layers simply were not there, in a project that opened perfectly well from a folder. The
parser is chosen by the file now: every Gerber declares a format specification and no Excellon file
does.

**Drawings are named rather than shrugged at.** A drill map was showing as "Unknown", drawn over the
board in orange, which makes an identified thing look like a failure to identify — the file says
exactly what it is. `DrillMap` and `Documentation` are roles now: labelled, off by default, and not
offered for export, because a drawing is for reading. Two drill maps also stopped triggering the
duplicate-role warning; KiCad writes one per drill file, and warning about the normal case teaches
people to skim past the warning that matters.

**Soldermask joins paste as a millable layer**, since its openings are by definition everywhere the
mask is not meant to be — a superset of the paste apertures, because vias and test points have mask
openings and no paste.

**SVG gained an inverted form**, asked for from the etching workflow: paint the board, burn away the
resist everywhere the acid should reach, which is the complement of the copper. That needs the board
edge in the drawing, because the complement of a shape is unbounded until something bounds it — the
`Edge_Cuts` outline when there is one, the extents when there is not. Inverting is ignored for
G-code rather than producing a program that mills the whole board away.

**The operation tag came out of exported filenames.** `PogoTest1-F_Cu.iso.nc` is now
`PogoTest1-F_Cu.nc`: a layer produces one output, so there was nothing for the tag to disambiguate,
and the extension already says which machine wants the file.

**Simplification and arc fitting: the panel went from 301,097 lines to 15,191.** Documentation/03
§7.3 rates this above the travel ordering and it is right to. A controller decelerates into every
block it cannot see past, so an isolation contour delivered as tens of thousands of one-micron
segments never reaches its programmed feed at all — the estimate's own pessimistic bound fell from
1h 18m to 1h 4m on the panel's copper, which is the machine no longer stopping at corners that were
never really there.

| | segments | after | arcs |
|---|---|---|---|
| Panel isolation | 75,768 | 5,610 | 2,640 |
| Panel outline | 222,988 | 7,240 | 3,814 |

Three things had to be right for that to be safe rather than merely small.

The **deviation bound has to hold end to end**. The two stages compose: an arc within its tolerance
of a point that was itself within tolerance of the original sits at up to the sum of the two from
where the cutter is actually needed. The first version budgeted the full tolerance twice and drifted
2.21 µm against a stated 2 µm — measured, not reasoned about. Each stage gets half now, and the
worst observed deviation on a real board is 1.48 µm.

The tolerance itself is **2 µm rather than the 5 µm the sketch suggested**, because the number that
matters is not how accurate the shape looks but how much of the *clearance* it spends: an isolation
cut is offset half a tool width from the copper and every micron of simplification is a micron of
that margin given away.

And **simplification runs before the mirror**, not after. It preserves every path's endpoints, so
ordering is unaffected by going second — but arc fitting is greedy against a hard tolerance, so a
run that just fits in one orientation just misses in the other, and the two sides of a board stopped
being exact reflections for no reason anyone could see. The exact-mirror test caught it.

The arc fitter also had a bug worth remembering: it *stopped searching* when a candidate run swept
too little to be worth an arc, rather than extending further. That meant an arc was only ever found
where the first few points already spanned enough angle — on a finely tessellated circle, never. A
180-sided circle fitted nothing at all; it now fits one arc. Fixing it tripled the reduction on real
boards, from 77% to 93%.

**Eulerian path merging was measured and not built.** Documentation/03 §7.2 wants it because
pcb2gcode's isolation produces open segments that can be chained. Ours does not: every isolation and
engrave path is a closed contour out of a Clipper offset, and two distinct closed contours never
share a vertex — if they touched, the offset would have merged them into one. Counted on both
PogoTest1 and the 66-up panel: **zero endpoints shared by more than one pass**, in isolation and
in engraving alike. There is nothing to merge, and machinery that provably does nothing is worse
than none.

**Travel-at-depth is deferred, on the numbers.** On the panel, 67 of 198 transitions are under 2 mm
— the plausible candidates. Each avoided lift saves a retract and a plunge, about 0.4 s, so the
whole opportunity is roughly 27 seconds of a one-hour job: under 1%. Against that, deciding it needs
a visibility test whose failure mode is the cutter crossing a trace at depth, and there is still no
dry-run generator to catch that before it happens. It is the right feature in the wrong order;
revisit it once Phase 5 can verify a program without cutting.
- Benchmark harness vs. pcb2gcode; before/after comparison view.

**Done when:** the acceptance metrics in [03 §8](03-Toolpath-Optimization.md#8-acceptance-criteria)
are met on the corpus and enforced in CI.

### Phase 4 — Laser output (SVG) — **in progress**

Laser output is **SVG, not G-code** ([04 §1](04-Machines-Laser-and-Mixed-Workflows.md#1-two-machines-two-output-formats)),
which deletes most of what this phase used to contain: no laser dialect, no scanline fill, no
overscan, no power model. What is left is geometry and packaging.

- Structured SVG writer: real mm units, named layers, CSS classes, `evenodd` holes, deterministic
  output. **Shared with the mill-side documentation exports**, so it is written once.
- Silkscreen → SVG: centrelines straight from the parsed strokes. Needs no geometry realisation
  at all, so it lands first.
- Pad selection from X2 attributes; copper inversion against the outline; mask-open regions.
- LightBurn palette mapping and a shipped layer preset; DXF as a second flavour.
- One page origin and size shared by every export in a Job, asserted in the golden tests.
- Calibration generators: registration repeatability.

**Done when:** an exported mask job imports into LightBurn on the right layers at 1:1 scale, and
the burned result is dimensionally true to the design. **Met for scale and geometry** — see "First
physical result" — on a 66-up panel, measured true to within 0.1 mm.

**Progress — pulled forward, because the SVG decision made it cheap.** The neutral artwork model
(`MillBurn.Core.Artwork`), the SVG writer and the silkscreen operation are built and tested:

- `Artwork` / `ArtLayer` / `ArtShape` / `ArtSegment` is the CAM-to-exporter hand-off, in
  nanometres, arcs kept as arcs, with roles rather than colours so the palette lives in the
  profile.
- `SvgWriter` emits real millimetres, per-element palette colours for LightBurn or a stylesheet
  for Inkscape, `evenodd` holes, closed subpaths, embedded notes, and byte-identical output across
  runs.
- `SvgPage` makes the shared-page rule structural rather than a convention.
- `SilkscreenOperation` buckets strokes by whether they fit the beam, merges same-width strokes
  into one element, and *reports* what it could not realise (macro flashes, clear polarity) rather
  than dropping it.
- `MillBurn.Cli svg` exports a layer and prints the page, the layers and the notes.
- Verified on both real boards: `PogoTest1-F_Silkscreen` is 217 paths in two elements, 26.8 x 33.7
  mm page, every coordinate inside the viewBox, and the legend reads the right way up.

Copper inversion has since landed too — an inverted SVG is everything inside the board edge
*except* the layer, which is what burning a resist mask off a painted board wants.

Still to come in this phase: pad selection from X2 attributes, the LightBurn layer preset, DXF,
and the registration repeatability generator.

### First physical result

**The SVG path has marked a real board.** `GridStripConnector_Panelized` — the 66-up panel, 165 x
107 mm, 11,414 objects, 198 copper islands — exported as front-copper SVG and **laser-engraved onto
copper-clad**. Reported as "as best as I can tell, it's perfect". The acid etch was not done, so
what is proven is the pattern on the board, not a finished circuit.

That is the right boundary anyway: everything up to the mark on the copper is this project's, and
the chemistry after it is not. The chain now verified is Gerber parse, geometry realisation, the
neutral artwork model, the SVG writer, an import into laser software, and the burn. All of it was
previously checked only by rendering.

It also happens to be the hardest artwork in the corpus rather than the easiest, which is a better
first result than a small board would have been.

**Measured.** Traces, pads and outer dimensions all match nominal to within the ~0.1 mm the
measurement itself is good for.

**Scale is settled.** 0.1 mm across a 165 mm panel bounds any scale error at **0.061%** — and a
scale error is the silent failure of an SVG hand-off, the one that looks entirely correct until a
part will not fit. The `width="165mm" viewBox=...` contract in [05 §3](05-Viewer-and-Export.md)
holds on real hardware, which is the single most valuable thing this measurement could have told us.

**And it settled the scope question underneath.** Kerf and etch-bias compensation are **dropped
from this project** — see [04 §2.2](04-Machines-Laser-and-Mixed-Workflows.md). Not deferred, not
blocked on a measurement: out.

The measurement is what made the answer obvious. A caliper good to 0.1 mm cannot resolve kerf on a
0.25 mm trace to better than ±40% of the trace's own width, so this result bounds kerf below the
threshold where it matters rather than measuring it — and output that is already dimensionally right
does not want a correction applied to it. Underneath that, kerf is a function of power, speed,
focus, lens and material, all of which live in the laser software beside a calibrated material
library. A number held here would go stale the moment any of them changed, with nothing to say so,
and two tools each applying an offset is a doubly compensated board that looks wrong in neither.

What stays ours is what the measurement actually validated: an SVG true to the design, in real
millimetres, at 1:1. That is a claim this project can verify and keep verifying. Kerf is a claim
about somebody else's beam.

**The mill half remains physically unverified**, and is now the larger unknown of the two.

### Phase 5 — Jobs, setups, alignment — **started**

**The dry-run generator is done.** `DryRun.Rewrite` turns an emitted program into one that traces
the same path 5 mm in the air with the spindle never started: `export --dry-run` writes
`Board-F_Cu.dryrun.nc` beside `Board-F_Cu.nc`, and the Export window offers the same as a
checkbox that is remembered.

It rewrites **the emitted file**, not the toolpath — the same reasoning as the backplot
([05 §2.1](05-Viewer-and-Export.md#21-two-data-sources-one-renderer)). Those two agree right up until the emitter has a bug, and only
one of them is what the machine will run; a dry run generated from the toolpath would faithfully
prove the safety of a program nobody is about to run.

Three decisions are worth recording, because each is the difference between a safety feature and a
plausible-looking one.

*The rise is in the preamble, not trusted to the file.* Every program this app emits does lift
before it travels — but a dry run is exactly the thing you point at a file you are unsure of, and
"safe as long as the file was already sensible" is not a guarantee worth making. So the output
opens with `M5`, an explicit `G21 G90`, and `G0 Z<height>`.

*The claim is checked by re-parsing the result.* Not "we substituted every Z", which is a statement
about the rewrite, but "nothing that moves in X or Y does so below the height", measured through
`GcodeParser` on the text that will be written. The first version asserted the weaker "no Z below
the height" and immediately caught itself out: the parser starts at Z0, so a program can satisfy it
and still drag the tool sideways before it has risen. The stronger property is the one that matters
and it is the one the tests assert.

*Incremental mode is refused rather than guessed at.* Under `G91` a Z word is a change, not a
position, so substituting an absolute height sends the tool somewhere nobody asked for. Nothing we
emit does that; other people's files might. **A dry run that is wrong is worse than none, because
it is the thing people trust before committing a board** — so it hands back the original untouched
and says why. (`G91.1` is arc-centre mode and is not mistaken for it.)

Feeds are kept by default, so the dry run takes as long as the real one. Half the value of watching
a job in the air is finding out it is a ninety-minute job.

This also unblocks the travel-at-depth optimisation deferred in Phase 3, whose failure mode was the
cutter crossing a trace at depth with no way to see it coming.

**Height mapping is done, and it is the one users will feel.** Isolation cuts 0.05 mm deep; clamped
FR4 is 0.1-0.2 mm out of flat. That single comparison is the most common reason PCB milling
disappoints people, and nothing about the toolpath can fix it. `ProbeRoutine` emits the grid,
`ProbeLog` reads the sender's log back, `HeightMap` interpolates, and `Leveller` bends the emitted
program to the result. In the app it is three menu items and a checkbox; from the CLI,
`probe`, `export --level <log>`, and a standalone `level <any.nc> --map <log>`.

Four decisions carry the weight.

*Thin-plate spline, not bilinear on a grid.* Bilinear leaves a crease along every grid line, and a
crease in the depth of a cut is a visible line in the copper. The spline reproduces a plane exactly
— checked, because a tilted board is the common case and any drift there would be invented
curvature.

*Outside the probed area it holds the edge value.* A spline has no opinion about ground it was not
shown, and its linear term will carry a tilt off into space: extrapolating a board bowed 0.1 mm a
few centimetres past the last touch produces a correction of **millimetres**, which drives the
cutter through the board rather than into it. Queries are pulled back onto the convex hull of the
measurements, and a job that runs more than 3 mm outside is refused outright.

*The fit degrades rather than failing.* Three points not in a line support a surface; collinear ones
support a tilt; one supports an offset. Probing a single row down a long thin board is a reasonable
thing to do and should give a tilt, not an exception — and claiming a surface the measurements
cannot justify is how autolevelling produces a board worse than the unlevelled one.

*The map is not saved into the project.* It describes the piece of stock currently clamped to the
table, and it stops being true the moment that piece is unclamped. Persisting it would invite
someone to reuse it next week on a different board.

Two bugs are worth recording because both were found by running the thing rather than by building
it. The probing routine emitted **raw Gerber coordinates** while its own header promised work zero
at the board's corner — a surface measured in one frame and a toolpath cut in another, with both
files looking perfectly reasonable on their own. Every test had used a board whose bounds started at
the origin, where the bug is invisible; there is now one that does not. And `SmoothingMm` was
cosmetic: 0.05 on the diagonal of a system whose off-diagonal entries run to 2,400 does nothing at
all. It is now a unitless dial scaled by the kernel's own magnitude, so the same number means the
same relaxation on a 20 mm board and a 200 mm one.

The parser gained two fixes on the way. `G38.2` was read as an unhandled `G38` and its descent
recorded as whatever motion mode was already current — so a probing routine backplotted as a series
of rapids diving to -2 mm. Worse, **`G91.1` was rounded to `G91`**: a file that merely stated how it
expresses arc centres was read as switching the whole program to incremental distance mode. G words
now carry their fraction instead of being rounded to the nearest integer.

Still to come in this phase:

- Job / Step / Setup / Fixture model; automatic insertion of fiducial and alignment steps.
- Fiducial generation (drilled holes, copper crosses, engraved marks) with survivability rules
  per workflow.
- Kabsch/affine fit from typed-in measurements, residual reporting, transform baked in as a
  processor. **No serial code** — see [01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-writes-files-it-does-not-drive-machines).
- Paste-friendly alignment entry (accepts `X12.345 Y67.890`, TSV, or a pasted GRBL status line).
- Reusing one height map across the operations of a Setup, and transforming it when the Setup
  changes. Also the grid formats Candle and bCNC write, which are matrices with their extents in
  a header rather than a coordinate per point — not guessed at without a real file to check
  against, since a parser written from memory of a format is worse than none.
- **Corner-stop fixture generator** — the recommended default
  ([04 §4.1.1](04-Machines-Laser-and-Mixed-Workflows.md#411-the-corner-stop--the-recommended-default)).
  3-2-1 pad placement sized from the stock, relieved inside corners, stop height derived from
  stock thickness, engraved with its own datum, and a mirrored twin for double-sided work. The
  datum needs no calibration step at all: we emit the program that cuts it, so we know where it
  is. Refuse to plan a job whose outline would run into a stop.
- **"Square the stock" operation** — mills the two datum edges true as the job's first cut. This
  is what takes the corner stop from +/-0.2 mm to +/-0.05 mm, and it is one pass.
- Dowel-plate generator + one-time machine calibration (the tightest fixture, 20-50 um).
- Nest-pocket generator with asymmetric keying, for boards already cut out.
- Job Runbook with persistence and PDF export.

**Done when:** Use Case 1 and Use Case 2 both run end-to-end with ≤ 50 µm registration measured
on a test coupon.

### Settings, and a constant that should never have been one

`Edit ▸ Settings…` now holds the numbers that describe the machine rather than the board: safe and
approach height, rapid rate, coordinate decimals, canned cycles, the dry-run height and feed
handling, the probing grid, and the levelling parameters. Persisted in `AppSettings`, honoured by
both the app and the CLI so a job exported either way comes out identical.

**The safe height should not have been a constant.** Every travel move in every program crosses the
board at exactly that height, it defaulted to 2 mm, and until now the only way to raise it was to
recompile — so anybody with a clamp taller than 2 mm had no way to avoid striking it at rapid. That
it sat unnoticed among a dozen other hard-coded defaults is the argument for the dialog: a constant
nobody can see is a constant nobody questions.

Checked rather than clamped, and Save is refused on a contradiction — an approach height above the
safe height has nothing to descend through, and a dry run held below the job's own travel proves
less than the job does. Silently correcting either would leave the operator believing the machine is
set up one way while it is set up another, which is the failure this whole app is arranged against.
Everything else is a note: canned cycles on, a safe height under 2 mm, a probing run over twenty
minutes.

### A bug that shipped: every hole drilled with the first bit

Found while sizing the drilling companion above, which is the only reason it was found at all.

`ExportPlanner.BuildDrilling` built one toolpath per hole size — correctly, each with its own tool —
and then folded them together with `paths.Aggregate((a, b) => a with { Drills = [..a.Drills,
..b.Drills] })`. A `Toolpath` carries one `Tool`, so the fold kept the first and discarded the rest.
The comment above it read *"the sizes inside it become tool changes rather than more files"*, which
is what it was supposed to do and never did.

PogoTest1 has sixteen plated holes in two sizes. Every one of them was drilled at 1.70 mm; the eight
that should have been 1.00 mm came out nearly twice the diameter they were asked for.

**Nothing showed it.** The hole count was right. The positions were right. The summary said "16
holes in 2 sizes" because it counts the Excellon's tools, not the program's. The backplot draws
plunges, not diameters. Five hundred tests passed. The only symptom was on the board.

The fix carries a list of toolpaths through the whole pipeline — simplify, mirror, order, emit —
rather than one, which is what the emitter always expected: it already knew how to write a manual
tool change, and had simply never been given two tools. Each toolpath is routed from where the last
one finished, because a tool change lifts to safe Z and stops without moving in X or Y, and only the
last returns to work zero.

That costs a little travel — PogoTest1's plated holes went from 100 mm to 118 mm — and that increase
is correct. The old number was the distance to visit sixteen holes in one tour, which is only
available if you are willing to drill them all the same size.

`DrillSizeTests` now checks the three things that were wrong: a stop for every size after the first,
each diameter named with its own hole count, and no hole lost in the split. Plus that the biggest
bit goes first, which was right all along and is worth pinning.

### A second silent omission: slots

Found the same way as the first one, by running a full export on a board that had just arrived --
two Arduino designs, Uno and Mega, from a KiCad recreation project. It took about ten minutes.

Seven plated **slots** on each board were not in the drilling program, and nothing said so.

A slot is an oval or routed hole, and KiCad writes it into a drill file as a **stroke**: the drill
dragged from one point to the other with a round aperture the width of the hole. `GerberDrills.From`
read `FlashObject`s only and hard-coded `Slots = []`, so a stroke was discarded before the drill
model existed. The layer was still realised through the ordinary Gerber path, which is why the
picture was completely correct -- the slots were drawn, counted in the object total, and included in
the layer's area -- while the program that gets run left them out.

That is the same shape as the mirrored backplot: **the display was right, so there was nothing to
notice.** The first symptom available to anybody is a connector that will not fit a finished board.

The compounding detail is that this was not Gerber-specific. `ExcellonFile.Slots` has existed since
the Excellon parser was written, `BoardLoader` realises slots into area, and `ProjectFile` persists
them -- but a search for `.Slots` across `MillBurn.Cam` and `ExportPlanner` returns nothing at all.
**No slot from any drill file, in any format, has ever been machined.** Only the Gerber path also
threw the data away on the floor.

Two changes, and deliberately not three:

*The file is read whole.* Strokes with a circular aperture become `DrillSlot`s, sharing the tool
table with the holes, and the drill extent grows to cover them. An arc-shaped slot is left out
rather than straightened, because recording it as the chord between its ends would put a straight
cut where a curved one belongs -- a worse answer than admitting the feature is not handled.

*The program says what it is not making.* The summary gains "7 slots -- not in this program" and the
warnings gain a sentence saying they are drawn but not made, and that the parts needing them will
not fit. **Routing them is not built**, and pretending otherwise would be the actual danger; what
was unacceptable was the silence.

It also corrected a smaller lie found on the way. A slot's width is a tool but it is not a hole
size, and the summary counted `Tools.Count` -- so the Mega reported "258 holes in 7 sizes" while
drilling six.

**Routing slots properly is a real feature and it is not a drilling feature.** That is the part
worth writing down, because the file it appears in makes it look like one. A drill cannot make a
slot at all -- a twist drill cuts on its point and will not move sideways -- so a slot needs an end
mill, and that pulls the whole thing out of the drilling operation:

- **A different tool class.** The drilling program's tool changes are all drills, sized by hole. A
  slot needs an end mill no larger than the slot's width, and the library has to be searched for one
  rather than told.
- **A different motion.** Plunge to depth and traverse, or ramp along the slot in passes. Neither is
  a peck cycle, and both want the isolation feed rather than the plunge feed.
- **So probably a different program.** Putting a 0.6 mm end mill in the middle of a drill file means
  a tool change the drilling companion page cannot describe honestly, and an operator swapping
  between drills and cutters in one run. A `Board-PTH.slots.nc` beside the drill file, with its own
  entry in the export list, is the likelier shape.
- **And a refusal.** When the widest usable end mill is still wider than the narrowest slot, there
  is no correct program to write, and the answer is to say so rather than to cut an oversized slot.

None of that is built. What is built is the export saying, in the summary and in a warning, that
these features exist in the file and are not being made.

### The layer panel, rearranged — and the bug that fell out of it

All of this came from using the app rather than from the plan, and it is recorded because the last
step of it found a defect that five hundred tests had not.

**Two toggles per layer, deliberately unalike.** A checkbox draws the artwork; a path glyph draws
the toolpath that layer becomes. They had to be separated because *the artwork hides the cuts* — a
trace is a filled shape and its isolation path runs around the outside of it, so with the copper
drawn, the line you are trying to look at is a hair against a solid colour. The two controls are
given different shapes on purpose: two identical checkboxes side by side is a coin toss every time.
The first mockup drew exactly those two checkboxes, which is the argument for drawing mockups.

**The kinds of move stopped pretending to be layers.** Cutting, travel, long rapids, rapid-at-depth
and the substrate were rows in the layer list. No file makes one, none can be exported, and every
setting a layer row offers is meaningless for them. They are chips under the list now — a filter
across the whole drawing, which is what they always were. This needed the backplot to be built **per
source program** rather than merged, so that *which layer* and *what kind of move* could be asked
separately. Merged, the viewer could only ever show every program's cuts at once, which is the
opposite of the reason anyone opens a backplot.

**Grouped by part of the board, not by paint order.** Top side, inner layers, bottom side, holes and
outline. Paint order is back-to-front — right for the renderer, wrong for a person: it opens with
the bottom silkscreen, sets the two soldermasks four rows apart with nothing but their labels to
tell them apart, and buries the side you are working on in the middle. `LayerRoleInfo.PanelGroup`
and `PanelOrder` sit beside `DrawOrder` in Core, so the two orders are visibly different answers to
different questions and both are testable without a UI.

**The collapsed row says what it becomes.** A pill on the right — `G-code`, `SVG`, or nothing — and
a count on each group heading, so a fully shut drawer still answers *which parts of this board are
being cut*. Before, that took opening every row in turn.

**And the two tedious gestures got names.** Right-click a layer for *Show only this toolpath* and
*Export only this layer*. The second snapshots what every layer was set to and *Restore exports*
puts that back — the point of the gesture is that it is undoable, and re-setting six dropdowns by
hand afterwards is the tedium it exists to remove.

**Preview and Export moved to the header, and the export filter was deleted.** They had lived at the
bottom of the left panel, under a heading, behind a "Both / SVG only / G-code only" dropdown. Once
every row says what it produces, a second control that can disagree with six rows at once is one
answer too many. `PlanExport` still takes a filter, because Preview genuinely only ever wants the
G-code; an export takes none.

**Then the bug.** With one layer's artwork and that layer's toolpath showing and nothing else — an
arrangement the panel could not previously produce — the bottom copper's isolation paths were
visibly sitting on the mirror image of the traces they isolate. A bottom-side program is emitted
mirrored, because the stock is turned over before it is cut, and the backplot placed it on the board
by translation alone. Correct file, wrong frame, since the backplot existed. The reasoning and the
fix are in [05 §2.1.1](05-Viewer-and-Export.md#211-where-a-program-is-drawn-and-the-bug-that-hid-there),
including why its first regression test passed while the bug was still there.

Two headless flags exist now because of it: `--isolate <layer>` and `--overlay <layer>`. That
arrangement is unreachable by loading a file, and it is the one in which this class of defect is
visible at all.

### Requested from the workshop — all five built

Five ideas that arrived from using the app at the machine, recorded here so they keep their
reasoning. All five are done.

**An app icon.** Done. `art/millburn-logo.png` is the source; `art/make-icons.py` cuts the mark out
of it — the wordmark is dropped, because at 16 px "PCB_MillBurn" set across a taskbar tile is a grey
smear — and writes `Assets/millburn.png` for the window icon and `Assets/millburn.ico` for the
executable. Regenerate by running the script; do not hand-edit the outputs.

**A drilling companion, as HTML beside the program.** Done. `MyBoard-PTH.drilling.html` lands next
to `MyBoard-PTH.nc`: the bits in the order they go in, hole counts, per-section times, the line each
section starts at, and the setup notes that matter — sacrificial board, biggest bit first, and
re-zero Z after every change. A checkbox on the drill layer, on by default. One self-contained file
with no links out, because it opens on a workshop machine that has never been online.

Derived from **the emitted program**, like the backplot, the dry run and the leveller: it splits the
file at its `M0` stops and measures each section, so the times are the file's own rather than an
estimate of what was intended.

One bug worth remembering, because the shape of it recurs. The emitter writes a toolpath's label
*before* the tool-change sequence that precedes it, so splitting at `M0` leaves every label attached
to the section before the one it names. The first version read each section's label out of that
section and confidently listed the second bit as "the bit already in the spindle". Labels are now
collected across the whole file in order and zipped with the sections. **A guide that names the
wrong bit is worse than no guide**, because somebody will follow it.

The same page shape would suit the outline and isolation later, but drilling is where it earns its
keep: it is the only operation with tool changes in it.

**How far to take tool configuration.** Fusion's tool dialog is the reference the question came
with, and most of it is there to feed two things we do not have and do not plan: a materials
database, and a collision simulator that needs holder geometry and gauge length. Copying fields for
their own sake makes the dialog longer without making any output better.

The test that separates the useful from the decorative is *does it let the app tell the operator
something they did not already know?* By that test:

| Field | Worth it | Because |
|---|---|---|
| Flutes | **Yes** | With feed and RPM this gives chipload, `feed / (rpm × flutes)`. On a 0.4 mm end mill that number is the difference between a bit that lasts one board and twenty — under about 5 µm it rubs and work-hardens instead of cutting, and well over it snaps. We already hold feed and RPM, so this is one field for a real warning. |
| Plunge feed per revolution | **Yes** | Derived the same way, and it is what breaks small drills. A 0.3 mm drill at 100 mm/min and 12,000 rpm is 8 µm a revolution, which is fine; the same feed at 3,000 rpm is 33 µm, which is not. |
| Surface speed | Derived, show it | `π × D × rpm`, free to compute. Not a warning on its own, but it is the number people compare against a table. |
| Flute length | **Yes** | It is the honest source for `MaxDepthNm`, which we already have and currently ask the user to type. A 0.8 mm bit with 3 mm of flute cannot cut a 1.6 mm board plus break-through if it is only stuck 2 mm out of the collet. |
| Length below holder | Later | Answers "will the collet nut hit a clamp", which needs fixture geometry we do not have until the fixture generators land. |
| Ramp angle, ramp feedrate | With ramping | Belongs to the tool, but only once entry ramping is implemented. |
| Lead-in / lead-out feedrate | No | Our isolation and engraving paths are closed contours entered by plunging in place. There is no lead to give a feedrate to. |
| Transition feedrate | With travel-at-depth | The deferred Phase 3 optimisation is the only thing that would move between cuts without lifting. |
| Shaft diameter, overall length, shoulder length | No | Geometry for drawing the tool and for collision checks. We draw toolpaths, not tools. |
| Material, holder, gauge length, tool assembly | No | Fusion needs these for its speeds-and-feeds calculator and its machine simulation. We have neither, and a field nobody reads is a field that goes stale and then lies. |

So the answer to "how far can we go" is: **four more fields, not thirty** — flutes, flute length,
and the two derived numbers shown next to them — and the payoff is not the fields but the warnings
they make possible. A tool dialog that says "this is 2 µm a tooth, you are rubbing not cutting" is
worth more than one with every dimension of the bit and no opinion about any of them.

**Built.** `Tool.Flutes` and `Tool.FluteLengthNm` are stored; `ChipLoadNm`, `SurfaceSpeedMPerMin`,
`PlungePerRevNm` and `UsableDepthNm` are derived and `[JsonIgnore]`d, along with `WidthPerDepth`
which was being written into `tools.json` as though it were a decision rather than a consequence.
`ToolAdvice` turns them into warnings, shown in `tools list` and against the operation at export.

Running it against the shipped library immediately caught the advice being wrong: it flagged a
sensible 1.0 mm drill as "rubbing" on its *lateral* feed, which a drill never uses because drilling
only plunges. Drills are judged on the plunge alone now. Advice that cries wolf gets ignored, and
then it is worse than none.

**Custom pre- and post-G-code.** Done. `ProgramFraming` holds a start and an end block; the machine
default lives in settings, a project can override either half, and `Edit ▸ Start and end G-code…`
edits it with the checks running as you type.

The interesting decision turned out not to be the validation but **the placement**. The start block
goes in *before* `G21 G90 G94` / `G17`, so whatever state it leaves the machine in, the program puts
it back before cutting. That makes the worst class of mistake — a header that quietly switches to
inches or to incremental, after which every coordinate in the file means something else and nothing
about the file looks wrong — **impossible rather than merely warned about**. The end block goes in
after the spindle stops and before `M30`, because a controller stops reading there.

That left only two things worth refusing, both of which produce a file that looks fine and is not: a
comment that does not close or has a bracket inside it (GRBL truncates it and feeds the rest to the
parser; LinuxCNC rejects the file), and an `M30`/`M2` that ends the program where it stands. Save is
disabled on those. Everything else is advice and can be saved anyway — `G92` shifting every
coordinate after it, moving in X or Y before the program has lifted, or setting a mode the program
is about to set again. It is somebody's own G-code for their own machine, and refusing things merely
because we would not have written them is how a feature like this stops being useful.

Null and empty are kept distinct throughout: null is "use the machine's", empty is "this project
deliberately has none". Collapsing them would make a project that was told to add nothing start
adding something when the machine default changed.

**Open an existing program.** Done. `File ▸ Open G-code…`, a dropped `.nc`, or one on the command
line. It draws any G-code, not only ours, because the viewer has always parsed emitted text rather
than the toolpaths behind it — so this needed no new geometry at all, only a scene with no layers
in it and an extent taken from the program.

Three things had to change, and all three came from looking at the result rather than from writing
it.

*The empty-state panel and the drop hint were bound to "no board" and drew on top of a program.*
They are about having nothing to look at, which a program is not.

*A program's extent is its own outermost move,* where a board's is its outline with the copper
inset — so fitted raw it sat hard against the window edge with the stroke width half over the side.
It gets a few per cent of air now. Worth recording that the first reading of this was wrong: it
looked unfitted, and measuring the rendered pixels showed the fit was correct and merely tight.

*A dry run is all travel by construction,* and travel is hidden by default so it does not clutter
the copper. Opened on its own, the first dry run drew two dashed lines and looked broken. A
standalone program now turns every backplot layer on: over nothing, there is nothing to declutter.

### Phase 6 — Polish and reach

- Material-removal simulation as a first-class view and test oracle.
- Rest machining / multi-tool bulk clearing.
- Trochoidal pocketing.
- Additional mill posts: grblHAL, FluidNC, LinuxCNC, Mach3.
- Solder-paste stencil generation.
- Machine-profile sharing.

### Phase 7 — User documentation — **started**

Plain HTML in `Help/`, shipped with the app, opened in the user's browser from a Help menu. No
static-site generator and no CDN: it has to work with no network, next to a machine, in a workshop.

**Started early, because the research was already done.** `Help/index.html` and `Help/faq.html`
ship beside the executable (plain files, not embedded resources — a resource we would have to
unpack to a temp file first is strictly worse at being opened in a browser) and are reachable from
Help ▸ Contents, Help ▸ Questions and answers, and F1. The FAQ covers what the exported files are,
where work zero is, why a V-bit's depth *is* its width, break-through, tabs, unreachable gaps, the
SVG page-sharing rule, and — at length — double-sided registration.

The double-sided section is the one worth having written down, because the answer is a *recipe*
rather than a feature: drill every hole in the first setup while the stock is still located, into a
scrap plate underneath, then pin through both. Nothing is ever measured, so nothing can be measured
wrong, and it reaches 20–50 µm with two dowel pins. It also produces a rule the user has to follow
in their own design, which is exactly the kind of thing that has to be documented rather than
inferred: the two registration holes must be **mirror-symmetric about the board's vertical
centreline** — that is the axis the app mirrors about, so the board only drops back onto the same
pins if they straddle it — and both **off** the horizontal centreline, so the board cannot also go
back rotated 180°, which looks identical and is not.

Anything not built is labelled as such on the page rather than described as if it worked — the
`not built yet` marker is a class in the stylesheet, not a habit. The pages have since grown
sections on dry runs, probing and levelling, the drilling companion, custom start and end G-code,
the standalone G-code viewer, the machine settings, and the layer drawer, all of which now exist.

**`Help/` is not `Documentation/`.** This directory is design documentation — why the code is
shaped the way it is, written for whoever maintains it. User help is a different audience, a
different lifecycle, and mixing the two makes both worse.

**Generate every reference section; hand-write only the prose.** A stale number in a CAM manual is
worse than no manual: someone reads "default depth 0.05 mm" long after the default moved, and cuts
a board to it. So the settings reference comes from the settings types, the CLI reference from the
CLI's own help, and the shortcut list from the key bindings — none of them retyped. Workflows,
troubleshooting and the conceptual pages are prose and stay stable.

A test walks the HTML and fails on a broken internal link or a missing image. Cheap, and it is the
only thing that reliably catches documentation rot.

Contents, in the order someone needs them: getting started; the two mixed workflows; milling with a
V-bit and why the depth-to-width relationship matters; laser export and which SVG flavour suits
which program (including single-layer mode, and why Creality Falcon needs it); alignment and the
corner-stop jig; projects and refresh-from-source; troubleshooting ("my board is 25.4x too big",
"my layers were not detected", "my laser software made 200 layers"); then the generated reference.

Help also carries **About**, with the version, the MIT licence and the third-party notices.
Clipper2, SkiaSharp, NetTopologySuite and Scriban all require their notices to be preserved in a
binary distribution, so this is the thing that actually satisfies that rather than a file in the
repository nobody ships.

Writing it late is safe **because the knowledge is being captured as it is learned** — the
timestamp trap, the Falcon layer explosion, the V-bit effective-diameter model and the fixture
error budget are all already written down here. The user manual is a rewrite of these documents for
a different reader, not new research.

## 2. Cross-cutting acceptance criteria

| Metric | Target |
|---|---|
| Preview latency (parameter change → redrawn) | < 200 ms on a 100×80 mm 2-layer board |
| Optimizer, Balanced mode, 5000 paths | < 500 ms |
| Rapid travel vs. pcb2gcode, outline ops | ≥ 40% reduction |
| Estimated cut time vs. pcb2gcode, overall | ≥ 20% reduction |
| G-code line count vs. pcb2gcode | ≥ 5× reduction |
| Viewer frame rate, 500k segments | >= 60 fps pan/zoom — **met: 98 fps measured, Phase 0** |
| Cross-machine registration | ≤ 50 µm |
| Time-estimate accuracy vs. wall clock | within 10% |
| Output determinism | byte-identical across runs |

## 3. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **Gerber parser edge cases.** Real-world Gerbers from old tools break parsers in creative ways. | High | Build against a wide corpus early (KiCad demos, tracespace test suite, Altium/Eagle exports). Fail loudly and specifically, never silently produce wrong geometry. Ship a "parse report" panel showing what was understood. |
| **Aperture macros are more work than they look.** | Medium | Budget real time in Phase 1. All 21 primitives plus expression evaluation. Do not defer — boards that use them are common and the failure mode is silent wrong copper. |
| **UI framework fit.** A dense CAM tool needs docking, a real code editor, and a fast custom canvas. | Medium | Decided in [07](07-UI-Framework-Decision.md): Avalonia recommended, WPF as fallback. Mitigated structurally — all logic lives in UI-free libraries, so the shell is one replaceable project. The Phase 0 spike settles it in two days rather than two months. |
| **Registration accuracy may not reach 50 µm**, since measurement is done by hand in the operator's sender. | Medium | Push hard on the **fixture** path, which needs no per-job measurement at all. Where fiducials are used, layer the fallbacks: probe (numeric) → microscope crosshair (cheap) → naked eye (worst). Always report the fit residual so the operator knows what they actually got rather than assuming, and refuse to export above the threshold without an override. |
| **Fiducials destroyed by an intervening process.** | High | This is the classic mixed-workflow failure. Encode survivability rules per workflow ([04 §4.2](04-Machines-Laser-and-Mixed-Workflows.md#42-fiducials--measurement)) and validate them when the Job is built: refuse to plan a job whose fiducials cannot survive to their next use. |
| **Optimizer is slow enough to break the live pipeline.** | Medium | Hard time budget with three modes; candidate lists cap the work; the optimizer is always interruptible and always returns its best-so-far. |
| **GPL contamination** from reading pcb2gcode / UGS. | High | Clean-room discipline ([01 §9](01-Architecture.md#9-licensing-strategy)). Reference the *design*, write the code. Do not paste. Keep a note in any file whose design was informed by a GPL source, describing what was learned rather than copied. |
| **Scope.** This document describes a lot of software. | High | The phase boundaries are real. Phase 2 alone is already useful; Phase 3 alone already beats pcb2gcode at the thing that prompted this project. Ship those before touching Phase 6. |
| **Post-processor dialect variability.** Every firmware fork accepts slightly different G-code. | Low | Template-driven posts plus a corpus of known-good output per dialect. Far smaller risk than it would be if we drove the machines — we only have to emit text a sender will accept, not maintain a live protocol. |

## 4. Open questions

1. ~~**License for PCB_MillBurn itself?**~~ **Answered: MIT** — attribution only, no restrictions
   on who ships it. Every dependency is permissive too, so there is no copyleft in the graph. See
   [01 §9](01-Architecture.md#9-licensing-strategy) and `THIRD-PARTY-NOTICES.md`.
2. **UI shell: Avalonia, WPF, or stay on MAUI?** Recommendation and reasoning in
   [07](07-UI-Framework-Decision.md); settled by the Phase 0 spike.
3. ~~**Which laser controller(s) do you actually have?**~~ **Answered:** it does not matter, because
   laser output is SVG. Still worth knowing *which laser software* — LightBurn's palette mapping is
   the one target-specific thing in the export, and the shipped layer preset should match it.
4. **Is there a touch probe on the mill?** It changes the default alignment recommendation from
   microscope-crosshair to probe, and it makes the generated probe routines worth building early.
5. ~~**Single board or panels?**~~ **Settled: we do not panelise.** A panel made in the EDA tool
   is cut correctly and always has been — every closed profile in `Edge_Cuts`, inner pieces before
   the frame around them. Building an array *here* is dropped, not deferred.

   Panelising properly means understanding the design rather than the geometry: which copies share
   a net, whether a mouse-bite crosses a track, what the clearances are. By the time a board reaches
   this app it is a set of Gerbers — shapes on layers, with the design gone. A CAM-side array could
   copy shapes faithfully and could not tell you it had perforated a trace, and a tool that
   panelises badly is worse than one that does not offer to. KiKit already does this properly for
   KiCad, against the netlist and the design rules, and the other EDA tools have their own.
   The geometry layer already makes the array half cheap — copies are a transform over geometry
   that is already realised — and mouse-bites are close to the outline tabs that exist. What is
   genuinely not free is per-instance identity in the UI, height mapping (across 200 mm the stock's
   flatness stops being ignorable), and the fact that isolation milling a panel multiplies both the
   run time and the cost of one broken bit by N. Recorded for the user in `Help/faq.html` so the
   trade is visible before someone asks for it.
6. **Which sender(s) do you use?** It determines which probe-log formats to import first and which
   G-code dialect quirks to prioritise. (The app itself never talks to a machine —
   [01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-writes-files-it-does-not-drive-machines).)
