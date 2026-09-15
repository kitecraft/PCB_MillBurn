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
  The corpus is pcb2gcode's test data, which is GPL-3.0 and therefore **not committed here**
  ([01 §9](01-Architecture.md#9-licensing-strategy)) — so those cases run for a developer with a
  checkout beside the repo and **skip in CI and on a fresh clone**. The six committed boards and
  the hand-written spec fixtures are what runs everywhere.
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

The board pane follows the same shape: **Project info**, **Layers**, **Export**, as collapsible
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

### Phase 3 — The optimizer — **done, with one item open**

- GTSP model with entry-configuration sets, closed-loop free start. **Done.**
- Trapezoidal time cost model. **Done**, and later corrected against a real machine — see
  [the first real board](#the-first-real-board-and-the-estimate-that-was-wrong-by-more-than-double).
- Greedy + 2-opt + Or-opt + configuration flip, with candidate lists and don't-look bits. **Done.**
- Precedence DAG. **Done.**
- Travel-at-depth when safe. **Done**, but late and under another number: it shipped as
  [§6.3](#63-staying-down-between-passes-that-touch--done) after the workshop asked for it, which
  means this phase was marked complete while carrying it.
- **Eulerian path merging — not built.** Adjacent isolation contours that share endpoints should
  become one continuous move. It is the last open item of the phase and it is worth more now than
  when it was written, because §6.3 has just measured what removing a single plunge is worth on a
  slow Z axis.
- Douglas–Peucker simplification + G2/G3 arc fitting. **Done.**

**Marked done anyway**, because the phase's purpose — beating the ordering that prompted this
project, deterministically, under a gate — is met. The open item is listed so that "done" does not
quietly come to mean "everything in the list above".

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

### Phase 4 — Laser output (SVG) — **in progress, and parked**

> **Where it actually stands.** The half that was pulled forward is done and physically proven: the
> SVG writer, the silkscreen operation, the shared page origin, the LightBurn palette mapping and
> the mask-open geometry, measured true on a 66-up panel. Nothing has been added since, through
> four subsequent phases of mill-side work, because every board cut in that time was a mill job.
>
> Outstanding: **pad selection from X2 attributes**, **DXF as a second flavour**, a **shipped
> LightBurn layer preset**, and the **registration-repeatability generator**. None of them is
> blocked; they are simply behind the mill in the queue. Listed here so "in progress" does not read
> as "being worked on".

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

**The mill half has now moved. `PogoTest1-F_Cu.dryrun.nc` ran on the machine and ran correctly.**
The whole program traced in the air, spindle off, nothing struck, nothing out of place. That is the
first thing this project has produced that a mill has actually executed, and it exercises the emitter,
the post, the ordering, the framing and the dry-run rewrite in one go — everything except the part
where the tool touches copper.

It also put a number on the time model, which had never been checked against anything: **predicted
1:57–2:01, actual 1:50.** About 5 % conservative on a two-minute job, with the bracket's low end
7 seconds out. The trapezoidal profile from [03 §3](03-Toolpath-Optimization.md#3-the-cost-model) is
doing real work — a naive distance-over-feedrate estimate would have been far further off on a job
this short, where acceleration is most of it. Being slightly *over* is the right direction to be
wrong in: an estimate that undersells the wait is the one that gets somebody to walk away.

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
  ([04 §4.1.1](04-Machines-Laser-and-Mixed-Workflows.md#411-the-corner-stop--the-recommended-default)),
  and the stop that [Phase 5.6](#phase-56) locates a generated blank against.
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

### The window and the command line cut with different bits

Found while checking why the app reported two unreachable gaps on a board where the CLI reported
ten. Not a defect in the gap check: the two were using **different tools**.

`ExportPlanner.ResolveTool` fell straight through to a hard-coded `Tool.DefaultVBit` whenever a
layer named no tool of its own — which is every layer of a folder the window has never opened.
`LayerRow.DefaultTool` took the first suitable tool from the operator's **own library**. On a real
library whose 30° V-bit had been reground to a 0.005 mm tip, that was a 0.127 mm cut against a
0.032 mm one: four isolation passes against fifteen, an hour of cutting against three, and a
genuinely different set of gaps too narrow to reach. Both files looked entirely correct, and the
README claimed a job exported either way came out identical.

`LayerOperations.DefaultToolFor` is now the single rule, called by both. **The library comes before
the built-in**, because the library is what the operator owns.

One detail decides whether that is an improvement or a different surprise: **the built-in is
preferred when the library holds it**, rather than simply taking the first of the right kind.
Editing a shipped tool keeps its identity, so somebody who reground their 30° bit gets their version
— but somebody who merely *added* a finer V-bit does not silently have every isolation job re-cut
with it, and adding a 0.8 mm end mill does not re-cut every outline with a cutter thinner than the
one that was chosen. First-of-kind is the fallback for a library that no longer holds the shipped
tool at all, and the built-in is the last resort for one that holds nothing of the kind: a plan that
refused to exist because nobody had entered a drill yet would be worse than one that assumes a 1 mm
drill and says so.

The golden snapshots are unchanged, which is the point — `ToolLibrary.Default` holds the built-ins,
so the shipped behaviour is identical and only a customised library moves.

### The probing round trip did not close

Reported from the bench the same day: a probing routine was generated, run, the sender's log saved
and imported. The app accepted the map — *Job ▸ Forget height map* lit up — and the export window's
**Level to the imported height map** stayed greyed out with no usable reason.

**The routine is written in work coordinates and GRBL answers `[PRB:]` in machine coordinates, and
nothing bridged the two.** The board sat at 0–20.89 mm; the log described a region at 41–60 mm. The
leveller refused it for running 57 mm outside the probed area, which was true, correct, and no help
at all. **The app could not read back the output of its own feature.**

Two things kept it hidden for a whole phase.

*The note said the job was done.* `ProbeLog` already emitted "GRBL probe reports are in machine
coordinates; the map will be shifted to put zero at the board's origin corner" — which is true of
**Z**, where `HeightMapOptions.ZeroAt` re-datums the surface, and was never true of X and Y. A
sentence describing the missing half as finished is worse than no sentence.

*Every test fixture was already in the job's frame.* Machine coordinates only differ from work
coordinates when somebody has set a work offset, which every real user has and no hand-written
fixture did.

**The fix reads the frame out of the log itself.** A sender echoes what it streams, so the log
contains the commanded `G0 X… Y…` positions — in work coordinates — alongside the replies. The
offset between the two point sets is the work offset, and applying it puts the map back on the
board. The recovered offset is stated in the import note rather than applied silently.

Two details decide whether that is safe:

*The sets are matched by their lowest corner, not pairwise in order.* A sender streams several
commands ahead of the replies: in the log that found this, the **first** probe's reply arrives
underneath the **fourth** probe's echo. Anything pairing by position gets every point wrong while
looking entirely reasonable.

*Every shifted point must then land on a commanded one, or the shift is refused.* Two point sets
that merely share a lowest corner are not translations of each other, and moving a whole height map
by a number that happens to line up two corners is precisely the quiet, plausible wrongness this
feature exists to prevent. A log with no echoes in it is not guessed at either — it says that
nothing in the file records where work zero was, and what to do about it.

**A second defect fell out of reading the first real export.** With a map imported, the export
cheerfully wrote `Board-B_Cu.levelled.nc` — a file whose own header says *every Z follows a measured
surface* directly above *flip the stock left-to-right*. Those cannot both be true. The map was
measured on the face that is about to go underneath, in coordinates that have since been mirrored,
so the correction would land on the wrong point of the wrong surface. Wrong twice.

It is refused now rather than warned about, because one export produces both sides from one map and
at most one of them can be right. The refusal names the file, says why, and says what to do instead.
`Leveller.WhyNotLevel` holds the reasoning so the app and the CLI refuse identically.

**The first version of that refusal assumed the map was always of the top**, which is the common
case and not a rule. Raised immediately from the workshop: the operator is the one who knows which
face was pointing up, so ask. The export window now carries *the board was top-up / flipped when you
probed it*, and the rule generalises to the thing it always was — **a program and a map either
belong to the same side of the stock or they do not**. `--level-side top|bottom` on the CLI, top by
default.

Asking also made it possible to say what the answer does, which a static note could not. The window
names the files it will level and the files it will not, and updates both lists as the radio moves:
*Levels PogoTest1-F_Cu.nc, PogoTest1-PTH-drl.nc, PogoTest1-NPTH-drl.nc, PogoTest1-Edge_Cuts.nc. Not
PogoTest1-B_Cu.nc — cut on the other side.* Drilling and the outline are top-side programs, which is
not obvious and is exactly the sort of thing an operator should not have to infer.

One bug came with it and was caught by looking: levelling had been counted as one extra file per
program, which stopped being true the moment some programs were refused. The heading now counts the
files it will actually write, and follows the radio as well as the checkbox — a window whose whole
job is to say what lands on disk cannot be approximate about it.

Worth noting how it was found: by reading the files from a real export before running them. The
suite had nothing to say, because every levelling test levels a program nobody flips.

**Verified against one controller, and now saying so.** A log's shape is a property of the firmware
and the sender, not of anything here, and this reader has only ever met GRBL's. `ProbeLog` records
what the firmware called itself — sniffed loosely from a startup banner or a `$I` reply anywhere in
the file, because the object is to be able to say *this came from a grblHAL* rather than to parse a
version string. The probing file's header asks the operator to run `$I` before starting, so the
answer lands in the same log; it is deliberately not *in* the file, because GRBL refuses `$`
commands while a program runs and a diagnostic that can abort a job is a poor trade. There is an
issue template for the logs that do not import, and it says not to tidy them: the `ok` lines and the
echoed commands are where the work offset comes from. See
[04 §5.1](04-Machines-Laser-and-Mixed-Workflows.md#51-verified-against-one-controller).

The same pass stopped counting the sender's own conversation as damage. Every `ok` and every echoed
command was being tallied as a line that could not be read, so a log that parsed perfectly reported
"39 line(s) could not be read" — which is how a warning worth reading gets ignored.

**And the measurement is the point.** The stock was **0.139 mm out of flat** across 19 × 35 mm, on
a job whose isolation cut is 0.05 mm deep. Unlevelled, that board cuts through in one corner and
does not touch the copper in another. It is the case the whole feature was built for, and until this
it could not be applied to it.

### Isolation had no width, only a lap count

Raised from the machine, immediately after that dry run: *we cut only once around the traces, and
with a V-bit that is not a wide cut.*

Correct, and it had been true since Phase 2. Isolation offered **Passes**, an integer, defaulting to
one. One lap of a 30° V-bit at 0.05 mm deep clears **0.127 mm**. That separates the nets, which is
the only thing the app had ever checked, and it is also a gap you cannot see, cannot solder across
without bridging, and can close by handling the board.

**Passes is the wrong question.** It asks about the machine; how wide the gap is asks about the
board, and only the second one has an answer the operator actually holds an opinion about. Worse, to
convert between them you need the effective cut width — which is itself derived from the tool and
the depth, and which this project already refuses to let anybody type for exactly that reason. Asking
for laps means doing that arithmetic in your head, with a number you were deliberately not given.

So the width is typed and the passes are derived, and the sum is printed under the control that
drives it: *4 passes of 0.127 mm clears 0.450 mm*. Change the bit or the depth and the pass count
moves on its own, because it is a consequence and not a setting.

Three details worth keeping:

*Whole laps, so what you get is what you asked for rounded up.* Never down — rounding an isolation
moat down is a short. 0.40 mm asked for gives **0.450 mm** in four passes, and the export reports
that rather than repeating the number that was typed.

*Zero means one lap.* Which is what every project saved before this existed asked for, so opening an
old board and re-exporting it produces the file it produced before. A setting that silently widened
the isolation on a board somebody had already cut once would be the wrong kind of improvement.

*It costs what it costs, and the export says that too.* PogoTest1's front copper goes from 319 mm of
cutting to 1,239 mm — about four times, and measurably less than four, because once a narrow field is
fully cleared its contours stop being produced at all. On the Arduino Mega the top copper goes from
roughly an hour to roughly four. That is a real trade and it belongs in front of the operator before
the job starts, which is where the time bracket already is.

The default for a freshly imported board is **0.4 mm**, in `Edit ▸ Settings ▸ Milling`. A constant
nobody can see is a constant nobody questions, and this one had been 0.127 mm by omission rather than
by choice for the whole life of the project.

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

It is now scheduled: [Phase 5.5](#phase-55), together
with mill-drill and the library-aware tool selection both of them need underneath.

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
*Export only this layer*.

The second was first built as a state change: set every other layer to Not exported, keep a snapshot,
and offer *Restore exports* to undo it. That was wrong twice over. It shipped with a defect — the
change rebuilt the rows and the rebuild cleared the snapshot, so restore had nothing to put back —
and more importantly it was a state machine with edges nobody could see: change a third layer while
one is isolated, and the snapshot describes a board that no longer exists.

Raised from the workshop, and the answer was in the name: *export only this layer* should open the
export window with that one file in it. It narrows the **plan** and touches nothing else, so there
is no snapshot, no staleness, and nothing to restore. The mute, the snapshot, the restore command
and the flag that drove it are all gone.

*Job ▸ Reset layers to defaults* covers the case the restore was reaching for — every layer back to
what a fresh import would have given it, the whole record and not just the output kind. It asks
first, and it leaves board thickness and colours alone: those describe the stock and the operator's
eyes, not the design.

**Preview and Export moved to the header, and the export filter was deleted.** They had lived at the
bottom of the board pane, under a heading, behind a "Both / SVG only / G-code only" dropdown. Once
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

### Test cuts: checking the library against a caliper

Asked for from the bench, and it closes a gap the rest of the app cannot close on its own.

**Every number this app computes about a cut comes from the tool library, and every one of them is a
claim about a physical object.** A V-bit's cut width is derived from a tip diameter somebody typed
in. That number has already been wrong by a factor of twenty-five — in *units* rather than in digits,
from a product listing that quoted inches without saying so — and nothing anywhere on screen showed
it. The arithmetic was faultless throughout.

So `Job ▸ Test cuts…` writes a short program that cuts a few lines on scrap, plus an HTML page
beside it explaining how to read them. Two tests:

- **Depth and width** — one line per depth, getting deeper. The file and the page both carry the
  *predicted* width for each line, so the operator is comparing a measurement against a stated
  claim rather than against a feeling. A constant error across every line is the tip; an error that
  grows with depth is the angle; and the page gives the arithmetic for both.
- **Feed rate** — one line per feed at a fixed depth, centred on the tool's own feed so there is a
  baseline in the middle. Nothing geometric changes; what changes is the edge, which is a thing only
  an eye can judge. The page says to pick the fastest line that still looks clean rather than the
  cleanest one, because cutting slower than necessary costs hours on a dense board and wears the bit
  faster.

**Both cut the first line again at the far end of the coupon.** One line, four seconds, and it is
the difference between a measurement and a guess: two identical cuts at opposite ends of the stock
should measure the same, and when they do not, the stock is tilted or Z moved and every other number
on the coupon is off by an unknown, varying amount. The check is built into the artefact rather than
left as advice.

**It takes no board**, and is enabled without one. What it tests is the library's claim about a bit,
which does not depend on which design is open.

**Height maps: its own probe, never the board's.** Raised immediately, and the answer is the second
half of the question rather than the first. A map describes one piece of stock as it was clamped, and
a coupon is a different piece in a different place — applying the board's map to it would be
confident and wrong everywhere, which is the same refusal
[the levelling side already makes](#the-probing-round-trip-did-not-close) about the flipped side of a
board. So the dialog offers a probing routine *sized to the coupon*, and the guide says plainly not
to use the board's. The existing `millburn level <file> --map <log>` does the rest, because a test
cut is just G-code.

Arcs were considered and left out. A circle tests backlash, axis squareness and whether the
controller honours `G2`/`G3` — all worth testing, none of them a property of the bit. Mixing them in
would put two unknowns in one measurement, and measuring a width across a curve is harder than
across a line for no gain. A circle test belongs in its own generator, aimed at the machine.

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

### Every channel on a panel cut on the wrong side

Reported from the workshop, off the screen, before anything was cut: *"the cut lines are on the
wrong side for these internal edge cuts."* They were.

A panelised KiCad board draws the routed channels between its boards as closed loops in
`Edge_Cuts` — a 1 mm-wide lattice, with the mouse-bite tabs drawn as excursions in the loop. The
exporter had one rule for every profile it found, and the rule was *offset outward by the cutter
radius*, which is right for the boundary between the job and the stock and exactly wrong for a void
inside it. On the 66-up panel, measured in the emitted program: the channel runs from Y97.05 to
Y98.05, and the passes were at **Y96.500 and Y98.600** — a groove through the board on each side of
the channel, and the 1 mm channel itself left standing. Every board a cutter-radius undersize on
that edge, and a panel that never comes apart.

Nothing in the output said so. The program was the right length, the profile count was right, the
tabs were right, and 716 tests passed.

**The rule is that the cutter goes on the waste side: outside a piece, inside a void.** Nesting
finds the candidates — a profile enclosed by another is usually waste — but nesting on its own
cannot decide it, and that is the part worth writing down. A hand-panelised file draws the stock as
one rectangle and each board as another rectangle inside it. Those nest exactly as a channel does,
and cutting them on the inside would take a cutter diameter off every board. The two cases are
geometrically identical and want opposite answers.

What separates them is whether the profile has any of the board inside it. Two attempts at asking
that were wrong in the same place:

- **Is a vertex of any artwork inside the profile?** A ground pour runs right to the board edge, so
  the copper ring beside a channel begins a micron inside it. Nine rings per channel.
- **Is more than 1 % of the profile covered by artwork?** Same cause, one step along: the profile
  is the *outside* of the pen the outline was drawn with, so it overhangs the true edge by half a
  pen width, and the pour sitting in that overhang came to 0.49 mm² against a 0.39 mm² threshold.

Both were measuring the edge, and the edge is where every board's copper ends. The question that
works is the machining one: **would a cut inside this profile destroy any of the board?** A cutter
run inside sweeps a band one diameter wide in from the boundary; whatever lies further in than that
is what such a cut would spare. A 1 mm channel has nothing left at all. A board inside a frame still
has almost all of itself. No threshold in the middle for either case to fall foul of.

With no artwork to test against, nothing is flipped and the cut stays where it has always been.
Silence is not evidence, and guessing between a channel and a board destroys a panel in one
direction or the other.

The panel's outline program went from 8,577 lines to 5,828 and from 20,996 mm of cutting to 19,039.
`OutlineSideTests` pins the side for a channel, a hand-cut frame, an empty window, a pour that runs
to the edge, and the no-evidence case.

### One pass down the middle

The fix above left the cut in the right place and still going round it twice. A 1.1 mm channel
offset inward by a 0.5 mm radius collapses to a ribbon a sixth of a millimetre across, and running
round *that* sends the cutter out along one side and back along the other a hair away. The return
pass is air.

**Halving the loop does not work**, because a channel lattice branches. The boundary of a thin
branching ribbon is a depth-first walk of it — out and back along every arm — so there is no "other
side" to drop. What the cutter wants is the channel's centreline as a *graph*, and then a walk of
that graph, which covers every arm once except for the backtracking a tree cannot avoid. For a
plus-shaped panel cell, 70.8 mm becomes 44.2 mm.

Folding the ribbon onto its own middle turned out to need one idea and three corrections, each
found by rendering the result and looking at it.

**The idea.** Resample the ribbon's boundary at three times its own width, then pair each point with
the point *opposite* it — which is simply the nearest one that is not an immediate neighbour, since
across the ribbon is by construction three times shorter a hop than along it. Two points that pair
with each other produce the same midpoint to the nanometre, so the two sides land exactly on one
another rather than near.

**Order the branches by distance to the far end, not from the near one.** The walk should finish at
the end of the longest path through the tree, so that the longest arm is the one never retraced.
Sorting a node's children by their distance from the *start* is very nearly the same number for each
of them and therefore no ordering at all; it also dropped an arm entirely, which the render showed
at once as a channel with nothing in it.

**Extend every loose end to the end of the ribbon.** A cap is narrower than the resampling step, so
the last pair of facing points sits back from it and 0.43 mm of each arm went uncut — four times per
cell, invisible in a preview and obvious on the stock.

**A junction is one place.** Where arms cross, a disc of the cutter's radius fits diagonally as well
as along, so the ribbon swells into a small diamond and folding it gives two forks a step apart. The
cutter then rounds the corner between two arms instead of passing through the middle, leaving a
0.29 mm² wedge standing exactly where four boards meet. Moving both forks onto the average of the
arms around them — not onto their own midpoint, which is off to one side for the same reason they
are — puts the crossing where it belongs.

**Nothing is trusted.** The centreline is computed and then checked: sweep the cutter along it and
see whether any of the ribbon is left. The ribbon is the void offset inward by the radius, which is
to say exactly the places the cutter was meant to visit, so a gap there is a channel not severed.
Anything left sends the whole profile back to the loop. Notably the check is *not* "does it remove
everything the lap would have" — the lap hugs the boundary and so also takes out the corner of every
junction and the half pen width the profile overhangs by, and measured against that a centreline is
rejected for doing less damage.

Then the passes needed to alternate direction as they go deeper. A closed contour ends where it
began, so the next pass down starts where the last finished; an open one ends at the far end of
itself, and taking it the same way round every time meant 7 m of driving back. Turning round instead
is free, and a slot is cut at full engagement on both sides anyway.

The panel's outline program, across both fixes: **20,996 mm of cutting to 13,222 mm, and 1h 21m to
56m**.

### The optimizer's budget was a wall clock

Exposed by the change above rather than caused by it, and worth more than the line it takes to fix.

`RouteOptimizer.Improve` ran local search until it converged *or 500 ms elapsed*. That held only
while every search converged well inside the budget, which every job in the corpus did — until the
panel's fifty channels became fifty open runs. That search does not settle at all: 360,000 moves
examined and 299,000 of them "improvements", on fifty nodes. So the cut-off landed somewhere
different every run, and the same build on the same machine emitted 1709, 1831 or 1926 mm of travel
from identical input.

The budget is now counted in moves examined — 1,000 per node, against the 1.2 per node a search that
is working actually uses — which restores
[§2](#2-cross-cutting-acceptance-criteria)'s byte-identical-output promise. `TheSameInputGivesTheSameRouteEveryTime`
did not catch this and could not: two runs in one process on one machine agree under a time budget
too. What does not agree is another machine, or the same one under load.

**The local search cycling on open runs is not fixed**, only bounded. Three hundred thousand
accepted improvements on fifty nodes means a move's computed delta disagrees with the cost it
actually produces, somewhere in the two-opt or or-opt neighbourhood when a node's entry and exit
differ. It costs half a second an export and no correctness, and it is written down here rather than
guessed at.

### The viewer decided a levelled cut was travel — and the first diagnosis was wrong

Reported from the workshop as *"something odd with the first cuts"*: on a levelled depth-test
coupon, part of line 1 drew as travel rather than as cutting.

**The wrong answer, taken first.** Line 1 is cut 0.020 mm deep and the scrap is 0.027 mm out of
flat, so after levelling 79 of that line's segments carry a Z at or above **zero** — up to +0.004.
That looks exactly like a cut lifting out of the material, and a check was built to count it and
warn about it, in the file header, the export review and the CLI.

It was a false alarm, and kitecraft said so: *"in reality the cut would actually work correct
because the leveller follows the true bow."* Right, and the source settles it in one line —
`Leveller` emits `Z = nominal + correction`, where the correction **is** the height of the surface
at that point. So the depth below the actual copper is `commanded − correction = nominal`, always,
everywhere, by construction. **Levelling cannot lift a cut out of the material.** Over a high spot
the commanded Z is positive because the copper is there too.

The warning was reverted. A check that fires on a correct file is worse than no check: it teaches
people to skip the panel it appears in.

**The real answer.** `GcodeBackplot.RoleOf` decided `inMaterial = DeepestZNm < 0`. That is not a
fact, it is an assumption — *the top of the stock is at work zero* — and a levelled program breaks
it deliberately. Two consequences, and the second was invisible:

- A correct cut drew as travel, which reads as a cut that never happened.
- The **cutting distance was under-reported with it**: 2497 mm against a true 2565 mm on that
  coupon, so the time estimate was short too.

The assumption breaks for an ordinary program as well, whenever Z is zeroed on the spoilboard
rather than on the stock top — a common enough habit, and one that would have made a whole job
render as travel.

The fix tracks what plunging and retracting *are*: the tool goes down when a feed move takes it
down and comes up when a move lifts it straight up. No datum, no assumption about where the surface
is. `Z < 0` is kept as well, so a program that never plunges explicitly still classifies sensibly.

**What it cost to learn:** a commit written, shipped and reverted inside an hour, because the
symptom was read as the bug. The evidence that would have settled it — that the correction *is* the
surface, so the depth below it is invariant — was one line of code away the whole time, and the
operator got there first from the physics.

### The first real board, and the estimate that was wrong by more than double

The whole chain ran on metal: Gerbers in, test cuts to correct the tool, a probe, a levelled
program, and a board. The loop the project exists for closed — a tip width taken off a product
listing as 0.127 mm was measured on a coupon, entered as **0.11**, and the app then worked out that
three passes of a 0.148 mm cut would clear the 0.400 mm moat asked for. No file needed editing, the
sender complained about nothing, and the SVG had already imported at true 1:1.

Two numbers came back that did not match.

| | Estimated | Actual |
|---|---|---|
| The dry run | 0:30 – 1:02 | **2:21** |
| The levelled isolation | 1:05 – 2:48 | **4:34** |

The dry run is the clean experiment, because it makes no Z moves at all — so its error is
acceleration alone. Recomputing it by hand: 267 moves, 688 mm, and if every move starts and stops,
**0.89 min at the assumed 200 mm/s² against 2.49 min at the machine's real 20**. The measurement was
2.35. The machine is landing on the pessimistic bound computed with its own acceleration.

**Nothing was wrong with the shape of the model.** `MachineProfile` already had acceleration,
junction deviation, a separate Z traverse and a minimum junction speed, each with its GRBL setting
named in the doc comment. Three things were wrong around it:

- **`MachineSettings` exposed only `RapidMmPerMin`.** The other three could not be set at any price,
  so they sat on defaults: 200 mm/s² against a real 20, and a Z traverse of 600 against a real 100.
- **`MotionLimits` — a second, smaller copy of the same idea, which the estimates actually used —
  had no Z rate at all.** Every `G0 Z` retract was costed at the traverse rate. On that job: 79 Z
  moves, 108 mm, **one minute sixteen**, a quarter of the run, entirely invisible. It is deleted;
  `MachineProfile` is the only profile now.
- **Neither real caller passed a profile to `ExportPlanner.Plan`.** Both handed it `machineSettings`
  and left `machine` null, so the optimizer ran on defaults too — and the optimizer's whole premise
  is that short moves cost more than their length, which is an effect that scales with acceleration.
  It now defaults from the settings.

`GrblSettings.Parse` reads a pasted `$$` dump, the same trick as pulling the firmware's name out of
a `$I` reply in a probe log and for the same reason: the answer is already in the operator's
terminal. **Settings ▸ The machine ▸ Read from a `$$` dump…** fills in `$110`, `$112`, `$120` and
`$11`, leaves alone anything the paste did not mention, lists what changed, and says so when the
paste is a different report rather than silently doing nothing. It also points out `$32 = 1` — laser
mode, where GRBL does not stop at corners for the spindle and S words drive a laser.

With the machine's own numbers, both measurements fall inside the bracket; before, neither did.

| | Estimated before | Estimated now | Actual |
|---|---|---|---|
| The dry run | 0:30 – 1:02 | 0:32 – 3:10 | 2:21 |
| The levelled isolation | 1:05 – 2:48 | 1:57 – 8:52 | 4:34 |

**The bracket is now honest and wide.** Its ends are real bounds — never slowing, and stopping dead
at every one of 1,070 segments — and where a machine lands between them is exactly what `$11`
governs. It is stored and used by the optimizer but does not yet close the estimate's bracket, which
is the next thing worth doing to these numbers.

<a id="every-tab-asked-for"></a>

### Four tabs asked for, two tabs cut

The second board's edge cuts were set to four tabs and came off the blank on two, in **opposing
corners**. That last detail is the whole diagnosis.

`SplitForTabs` walked the outline segment by segment and asked, once per segment, whether *that
segment's midpoint* was under a tab. An outline is an offset profile: four long straight edges and
four tessellated corners. The 37 mm edge is a single segment, tested at 18.5 mm, and a tab anywhere
else along it was simply not noticed — the whole edge got cut. Only the corner segments were short
enough to land inside a tab, so the tabs that survived were the ones that happened to fall on
corners, which on an evenly-spaced four is two of them, diagonally opposite.

It is a good example of a bug that a test can be written *around* without touching: every existing
test used tessellated geometry, where segments are short and the midpoint test is nearly right.

The split now happens **within** a segment. For each one, collect the tab boundaries that fall
inside it, sort them, walk the intervals, and keep the ones whose middle is not under a tab —
slicing the segment at the exact boundary rather than at whatever vertex was nearby. The tests that
came with it assert the thing the user actually observed: *n* tabs asked for produce *n* gaps, and
those gaps are spread around the perimeter rather than clustered.

The golden snapshots moved with it: `PogoTest1`'s outline goes from 588 mm of cutting to 557 mm and
from 92 lines to 134 — the two missing gaps, across five passes.

<a id="hexagon-pads"></a>

### Levelling turned the small pads into hexagons

Reported from the workshop, about a finished board: *"Can we make the circles more circular? The
small pads came out as obvious and clear 6 sided polygons."*

They did, and the chain that produced them was short. The Gerber says `%ADD12C,1.700000*%` — a
circle. `Tessellate` flattens it at a 1 µm sagitta, about 65 sides, and the arc fitter puts it back
together into a single `G3`. The emitted `PogoTest1-F_Cu.nc` contains exactly that. Then levelling
ran, and the file that went to the machine had **no arcs in it at all**.

`Leveller.Write` breaks a cutting move into pieces of `SegmentMm` and, for an arc, wrote those
pieces as chords. `SegmentMm` is 1 mm and exists to bound error in **Z**: the correction is applied
at the ends of a move, so a long move rides a straight line across ground the map says is curved. It
knows nothing about curvature in the plane. Handed a 1.7 mm pad's isolation ring — 7.4 mm around —
it produced eight chords:

| Ring | Circumference | Chords at 1 mm | Shape | Worst error |
|---|---|---|---|---|
| 1.7 mm pad, outer pass | 7.40 mm | 8 | octagon | **0.090 mm** |
| 1.7 mm pad, inner pass | 5.81 mm | 6 | hexagon | **0.124 mm** |
| 0.2 mm trace end cap | 1.13 mm | 2 | a line, traced up and back | — |

On a cut 0.148 mm wide. The user could see it without magnification, which is how it was found.

**The reason given for flattening was wrong.** The comment said an arc cannot survive levelling
because its Z now varies along its length in a way no `G2` can express. A `G2` with a Z word is a
helix, and every controller that accepts the arcs the *unlevelled* program already contains accepts
those. So an arc now stays an arc when it is split: same centre, same radius, one piece per segment,
each piece's `I` and `J` measured from its own start.

**It costs nothing.** The real board's levelled program is the same 1,224 lines it was — 104 arcs
split into 488 — and the machine executes the same number of blocks. Reported cutting distance goes
from 932 mm to 939 mm, which is the seven millimetres the chords were cutting short. Worst
start-to-end radius disagreement across all 488 arcs is 1.17 µm, from printing coordinates to three
decimals; GRBL rejects at 5 µm *and* 0.1 %.

**The test that should have caught it asserted the bug was correct**, and checked that the chord
*endpoints* lay on the circle. They did — that is what a chord is. Measuring the middle of each move
is the version that fails on the old code, and there is now a second test on the exact 1.7 mm ring
this was seen on.

It is the same lesson as [the viewer that decided a levelled cut was
travel](#the-viewer-decided-a-levelled-cut-was-travel--and-the-first-diagnosis-was-wrong): the two
worst bugs in this area were both in what happens to a program *after* it is correct, and both were
found by a person looking at a real result rather than by anything in the suite.

<a id="phase-55"></a>

### Phase 5.5 — Drilling, finished — **done**

Three features that belong to Phase 2 and were left behind when it closed. Numbered 5.5 because
that is when they were scheduled, not where their subject lives — the same reason Phase 1.5 sits
where it does.

They are one phase rather than three because two of them are the same machinery and the third is
what both need underneath. An end mill moving laterally at depth, with a proper lead-in, is a slot
and is also a hole too big to drill; choosing the cutter that does it is a question neither can
answer today, because nothing in the drilling path has ever looked at the tool library at all.

#### 5.5.1 Library-aware tool selection — the foundation — **done**

`DrillAndOutlineOperations` calls `Tool.DrillOf(diameter, template)`: it **synthesises** a drill of
exactly the diameter the file asked for. There is no notion of a bit you own or do not own, and no
size a board can ask for that this refuses.

That is fine for drilling and only for drilling — a hole is drilled by a bit its own size, so the
file's diameter *is* the answer, and the drilling companion page tells you which bits to fit. It is
a real assumption though, and worth saying plainly: an Arduino Mega asks for six sizes down to
0.30 mm, and nothing anywhere checks that those exist in your library or your drawer.

Slots and mill-drill cannot work this way. There is no cutter of exactly the right size to make a
2 mm slot; there is a set of end mills you own, of which some are narrow enough. So:

- The drilling path gains a **tool chooser** that reads `ToolLibrary`: given a required maximum
  diameter and a required depth, return the largest end mill that fits, or nothing.
- Largest, not smallest: a wider cutter clears the same slot in fewer passes and deflects less. The
  constraint is the slot's width, so the best tool is the one closest to it from below.
- `MaxDepthNm` and `FluteLengthNm` are already on `Tool` and are currently read by nobody in this
  path. A cutter that cannot reach through the board is not a candidate.
- **Nothing is synthesised.** The moment a program depends on a tool you might not own, inventing
  one is how a file gets written for a machine that cannot run it.

Also worth doing here, because it costs nothing once the library is being consulted: say on the
drilling summary when the board wants a drill size the library has never heard of. Not a refusal —
you may well own it and not have entered it — but the export window is the right place to find out
that the run stops for a 0.30 mm bit.

#### 5.5.2 Routing slots — **done**

An oval or routed hole. Seven per board on both Arduino designs; every KiCad board with a slotted
pad has some. Currently read, drawn, reported and **not made**, which is the state
[the slot section above](#a-second-silent-omission-slots) left them in.

- **Geometry.** `DrillSlot` is already a `From`, a `To` and a tool whose diameter is the slot's
  width. The cut is the centreline offset inward by the cutter's radius — for a cutter narrower
  than the slot, a racetrack around the inside; for a cutter exactly the slot's width, the
  centreline itself, in one pass.
- **Motion.** Ramp along the slot rather than plunging: an end mill plunged vertically at full
  depth into FR4 is how small cutters break. Depth passes at the tool's `StepdownNm`, ramping down
  the length of the slot on each one, which is a lead-in the geometry gives us for free.
- **Feed.** The isolation feed and the lateral feed, not the plunge feed. `ToolAdvice` already
  knows how to complain about the wrong one.
- **Arcs.** An arc-shaped slot is currently dropped at the parser rather than straightened. It can
  come through as an arc once there is something that can cut one.

#### 5.5.3 Mill-drill for holes too big for any bit — **done**

The same machinery pointed at a circle instead of a line. Helical interpolation with a proper
lead-in — not pcb2gcode's plunge-and-circle, which
[02 §6](02-Gerber-and-Geometry-Pipeline.md#6-drilling--routing) has wanted replacing since before any of
this was written.

It has not bitten yet only by luck. Both Arduino boards put their mounting holes on `Edge_Cuts`
rather than in the drill file, so they are cut as outline profiles and the drilling path never sees
a hole it cannot make. A board that puts a 3.2 mm mounting hole in the drill file, on a machine
whose largest drill is 2 mm, has no correct answer today.

#### 5.5.4 What it refuses — **done**

This is the part that decides whether the phase is worth having, and it is the reason all three
belong together: each one introduces a case where **there is no correct program to write**.

- **No cutter narrow enough.** The stock library's smallest end mill is 0.8 mm; four of the seven
  slots on an Arduino Uno are 0.60 mm. Refuse those, name the width, name the widest cutter that
  would work. Cutting a 0.8 mm slot where the board asked for 0.6 mm puts a hole through the
  adjacent pad.
- **No cutter that reaches.** A 0.6 mm end mill with 2 mm of flute cannot go through 1.6 mm of FR4
  plus break-through on a board that thick. Refuse, and say the depth rather than the diameter.
- **A hole too big to drill and too small to mill.** A cutter needs room to spiral: a hole barely
  wider than the only end mill available is not millable, and the arithmetic should say so rather
  than emitting a helix of zero radius.
- **Partial success is still success.** A board with four 0.6 mm slots and three 1.0 mm slots on a
  library holding a 0.8 mm end mill should get a program for the three and a refusal naming the
  four — not an all-or-nothing failure. The export list already shows one item per file and one
  warning per problem; this fits it.

#### Done when — **met**

The acceptance test ran exactly as written. With the shipped library the Arduino Uno exports
`Arduino UNO-PTH-drl.slots.nc` cutting **three of its seven slots** with the 1.0 mm end mill, and
says on both the slot item and the drilling item that the other four are not cut: *"4 slots 0.60 mm
wide are NOT cut: no end mill is narrow enough — the smallest in the library is 0.80 mm (0.8 mm end
mill)."* Add a 0.5 mm end mill and re-export: **seven slots, no refusals**, and the three that were
already being cut are cut by the same 1.0 mm cutter, because the chooser takes the largest that
fits rather than the newest. Both halves are pinned in `SlotRoutingTests`.

What it turned into, beyond the specification:

- **`ToolChooser`** answers three questions and tells their refusals apart, because they want
  different things from the operator: nothing narrow enough (a different cutter), nothing that
  reaches (a longer one), nothing with room to spiral (a drill instead). `MaxDepthNm` and
  `FluteLengthNm` are read for the first time; zero means *unstated*, which is not zero.
- **`ToolpathPass.RampFromNm`** — a pass that descends along its length rather than plunging. The
  emitter interpolates Z by *distance travelled*, not by segment count, because a racetrack's two
  straights and two arcs are not the same length and splitting the drop by count would descend four
  times faster on the short ones. An arc in a ramped pass is a helix, which is what 5.5.3 needs.
- **A flat lap after the ramps.** A ramp leaves the floor sloping by exactly one stepdown over the
  length of a lap, and on a slot that has to clear a connector's leg that is the difference between
  fitting and nearly fitting.
- **An open slot alternates direction**, so each pass ends where the next begins. A closed racetrack
  is left alone: reversing it reverses the cutting hand, which belongs to [6.2] rather than to pass
  numbering.
- **Drill sizes the library has never heard of** are named on the drilling item. Not a refusal — you
  may own the bit and not have entered it — but the export window is where somebody wants to find
  out that the run stops for a 0.30 mm bit. PogoTest1 says it for 2.20 mm and 1.70 mm; the Mega for
  five sizes.

**5.5.3 came almost free, once the rest existed.** A hole *is* a slot whose two ends coincide, so
`SlotOperation.Holes` is the same machinery pointed at a circle: inflating a zero-length line by the
clearance gives a circle, a circle in a ramped pass is a helix, and the arc fitter turns it back
into a single `G3` with a Z word. On PogoTest1 the 2.20 mm holes come out as one block each —
`G3 X4.911 Y13.957 I-0.600 J0.000 Z-1.000`, a full turn of 0.6 mm radius descending a millimetre.

Two things it needed that the specification did not name.

**A project-level option**, requested from the workshop, because the *policy* had no right answer.
When does a hole stop being drilled and start being milled? Off by default, and when on the
threshold is the largest drill in the library — a claim the operator already curates, rather than a
new number to keep true. `JobOptions` is the container, and it is the third place a setting can
live: the machine's numbers describe the machine, a layer's output describes that file, and between
them sits a small set of decisions about *this job*. It travels in the project, so a board reopened
next year cuts the way it cut.

**A different rule for a hole than for a slot.** "Widest that fits" is right for a slot, whose
constraint is its width, and wrong for a hole: a 2.0 mm end mill in a 2.2 mm hole leaves a tenth of
a millimetre of orbit, every flute buried and nothing evacuating — a plunge wearing a disguise. A
cutter may be at most three quarters of the hole, which on the real board is the difference between
picking the 2.0 mm cutter and the 1.0 mm one that actually spirals. Found by reading the emitted
file rather than by any test.

And one bug, found the same way: a layer whose *every* hole is too big to drill has nothing left to
drill and everything left to route, and it was being written off as "nothing to cut" — a sentence
about the drilling program applied to the whole layer. PogoTest1's non-plated file is exactly that
shape, and it now gets its routing program.

#### Original acceptance test

An Arduino Uno exports a slot program that cuts three of its seven slots and says, in the export
window and in the file, exactly which four it will not cut and why. Add a 0.5 mm end mill to the
library and re-export: seven slots, no refusals, no other change.

That board is the acceptance test because it exercises both paths at once with the shipped library,
which is not a coincidence — it is the board that found the omission in the first place.

Two things this phase does **not** do. It does not put an end mill into the middle of the drilling
program: a run that alternates drills and cutters is a tool change the drilling companion page
cannot describe honestly, so slots get their own file (`Board-PTH.slots.nc`) and their own line in
the export list. And it does not guess a cutter you have not entered into the library.

<a id="phase-56"></a>

### Phase 5.6 — The blank — **built; 5.6.5 open**

The app cuts you a piece of stock, and the edges of that piece are the datum for every machine and
every step after it.

**Called "stock" in the app.** Since 2026-09-14 the window, the files and the pages say *Build on
stock*, *Cut stock to size…* and `Board.stock.nc`. "Blank" also means empty, and *Write blank
program…* read as "write an empty program". This document, the code (`BlankOptions`,
`BlankOperation`) and the project file keep the old name.

Numbered beside Phase 5 because it changes what that phase recommends, not because it was scheduled
then. It arrived from the workshop, the day after the first dry run ran on the machine.

#### 5.6.1 What it is

Home-made boards start with somebody cutting a small rectangle out of a larger sheet of copper-clad.
That cut is currently done by hand, is nobody's business but the operator's, and is thrown away as a
step with no value. It is in fact the most valuable cut in the whole job, because **the mill can make
it, and a piece the mill made is a piece whose dimensions the app knows exactly.**

So: the app generates a **blank** — a rectangle a declared distance larger than the board — and emits
the program that cuts it. The board is then built on that blank, and every subsequent operation, on
either machine, is referenced to the blank's corner rather than to the board's. A physical corner
stop at each machine locates the blank, and because the blank is the same known rectangle everywhere,
the two machines never have to agree with each other about anything except how to hold a rectangle
against a corner.

#### 5.6.2 Why this beats what §4 currently plans

[04 §4.1.1](04-Machines-Laser-and-Mixed-Workflows.md#411-the-corner-stop--the-recommended-default)
already generates a corner stop for the mill, and
[§4.3](04-Machines-Laser-and-Mixed-Workflows.md#43-registering-on-the-laser--placement-not-coordinates)
already generates a jig for the laser. What neither has is a **known workpiece**: the stock is
described there as "any stock at least as big as the board", so each machine solves registration on
its own and the two answers have to be reconciled by the operator.

Four things follow from making the stock known instead.

**The datum travels with the work.** Registration stops being a property of two fixtures that must
agree and becomes a property of the object being carried between them.

**It makes §4.3's most important rule enforceable.** That section says every SVG in a job must share
one page fixed to "the stock outline plus a documented margin" — and the app has never known what the
stock outline is. The blank *is* the page: `SvgPage.Frame` becomes the blank's bounds, and the SVG
origin becomes the same corner the mill uses as work zero. Today it is `ForContent(board.Bounds, 2 mm)`,
a page fitted to the artwork with an invented margin.

**The datum is a physical edge, so it survives.** [§4.2](04-Machines-Laser-and-Mixed-Workflows.md#42-fiducials--measurement-mill)
carries a whole table about whether fiducials survive the etchant, the resist strip and the
soldermask. An FR4 edge does not care about any of them. This is the quiet advantage and it may be
the largest one.

**It removes a design rule.** The double-sided recipe in the FAQ reaches 20–50 µm and imposes a
constraint on the operator's own layout: two registration holes, mirror-symmetric about the vertical
centreline and off the horizontal one. The blank asks nothing of the design at all.

Set against that, honestly: this is **one calibration per machine, ever**, not none. Each corner stop
has to be square to its machine's axes once. That is still an enormous improvement on one alignment
per board, but "no calibration" would be the wrong claim, and the laser's stop is never verified by
anything unless [5.6.5](#565-verification-the-border-is-a-test-coupon) is built with it.

#### 5.6.3 The blank itself

**Always a rectangle**, whatever shape the board is. Its size is given one of two ways, and the
second one arrived from the workshop:

**Grown from the board** — four independent offsets from the board's bounding box. Waste matters: an
L-shaped corner stop only needs margin on two edges, so the default should be generous on the datum
edges and tight on the other two.

**Stated outright** — the blank is *this* rectangle, 183 × 122 mm, and the board sits inside it.

> *"My Gerbers2 project is actually one little board panelised into a grid of 11 rows and 6 columns.
> This is deliberate so that the full board fits onto some pre-cut copper-clad boards I have that
> are 183 x 122 mm. So, much better to just enter those dimensions directly."*

That is the ordinary case rather than the exotic one. Hobby copper-clad is **bought pre-cut**, in
sizes the supplier chose, and a board is laid out to suit the stock at least as often as stock is
cut to suit the board. Asking somebody to work out which four offsets turn their 11 × 6 panel into
183 × 122 is arithmetic the app is better at, and arithmetic they would have to redo every time the
panel changed.

**The two modes are the same four numbers read from opposite ends**, which is what keeps one model
underneath: grown from the board, the offsets give the size; stated outright, the size and the
board's placement inside it give the offsets. Everything downstream — the origin shift, the SVG
page, the mirror axis, the keying — works off the offsets either way and never needs to know which
mode produced them.

##### A stated blank may be one the app cuts, or one the operator already owns

This is the distinction that matters, and 5.6 as first written did not have it: it assumed the mill
cuts the blank. With a stated size there are two cases and they are not the same feature.

**Cut to size**, from a bigger sheet. Unchanged from everything above — the mill makes the piece, so
the app knows its dimensions exactly, and that is the premise the whole phase rests on.

**Declared**, because the stock is already that size. No cutting program at all: the app is being
*told* what is on the table so that the datum, the origin, the shared SVG page and the mirror axis
all refer to it. Most of this phase's value arrives here with no cut at all, which is worth saying
plainly — a declared blank costs nothing and still removes fiducials, still fixes the page, still
makes the two machines agree about a rectangle.

> **A declared blank is a claim, not a measurement.** A pre-cut board sold as 183 × 122 is 182.6 ×
> 121.4 with a corner that is nearly square, and the app has no way to know. Everything a *cut*
> blank guarantees, a declared one only asserts. So: invite the measured numbers rather than the
> nominal ones, say on the runbook that the datum edges are trusted rather than made, and treat the
> keying in this section as load-bearing rather than a nicety.
>
> **"Square the stock" is the bridge between the two.** The one-pass operation already scheduled in
> [Phase 5](#phase-5--jobs-setups-alignment--started) mills the two datum edges true. Run it on a
> declared blank and it becomes a known one, at the cost of a millimetre of stock and one pass —
> which is the honest upgrade path for somebody who starts by declaring and later wants the
> tolerance.

##### Placement, and what it refuses

**Where the board sits inside a stated blank is an input, not a guess.** Centred by default, because
that is what somebody laying a panel onto a sheet means; adjustable as an offset from the datum
corner for the case where the hold-down needs room on one side. The rule from
[5.6.4](#564-the-offset-and-the-flip-that-breaks-it) applies the moment anything is mirrored —
**left and right equal by default**, because the flip is about the blank's centreline and asymmetry
survives it only if the arithmetic is right.

**It refuses when the board does not fit.** Board plus the minimum border against the stated size,
per edge, naming the edge and the shortfall: *"the panel is 1.4 mm too wide for a 183 mm blank —
2.9 mm of border either side, and 3.0 mm is the floor for a 1 mm cutter."* This is the check that
pays for the feature on its own, because the alternative is finding out with the stock clamped.

And having been told the stock size, the app can say how much of it the job uses. For anybody
panelising to fit a sheet — which is why this was asked for — that number is the one being
optimised.

##### Every blank, however its size was decided

- **The minimum border is set by hold-down and cutter clearance, not by the jig.** The outline cutter
  needs room to run outside the board, and the blank needs somewhere to be taped or clamped that is
  not the board. A floor of roughly *cutter diameter + 2 mm* with the reason stated, rather than a
  number that looks arbitrary. A 2 mm sliver of FR4 also flexes and can break away while the board is
  being released, which is its own argument.
- **Tabs never on the datum edges.** The blank comes out of the outline operation, which adds tabs;
  a tab stub on the bottom or left edge stops the blank seating, by a few tenths, silently. Tabs go on
  the non-datum edges, or the blank is cut tab-free with tape or vacuum. The runbook says to deburr
  the two datum edges before first use, because a fresh outline cut leaves a burr underneath.
- **Key it.** A chamfer on one corner, or a shallow notch in one edge, cut while the blank is cut.
  On a *declared* blank there is nothing to cut it with, so the key is a mark the operator makes —
  which the app should say, in those words, rather than assuming a chamfer that never happened.
- **Label it.** The engrave operation already exists and the border is waste: put the project name,
  the date and a mark at the datum corner into it. A blank that says which corner is its datum cannot
  be loaded wrongly three weeks later, and this costs one extra toolpath on a cut that is already
  running.

**It is not a layer.** No file produces it, it cannot be exported as itself, and every setting a
layer row offers is meaningless for it — which is exactly the mistake the panel redesign removed. It
is a job property, like board thickness, and it belongs in Project info beside it — in `JobOptions`,
which now exists for precisely this kind of thing. What it *emits*, when it emits anything, is a
generated operation like the probing routine.

#### 5.6.4 The offset, and the flip that breaks it

The per-layer change is small and the mirroring change is not.

**The origin.** `ExportPlanner` already shifts every program by `-board.Bounds.Min` so that work zero
is the board's lower-left corner. With a blank it shifts by `-blank.Min` instead. That is nearly the
whole of it for single-sided work.

**The mirror axis is the part that is currently wrong for this.** Bottom-side geometry is mirrored
about `board.Bounds.MinX + board.Bounds.MaxX` — the *board's* centreline. Physically, the operator
flips the blank and pushes it back into the same corner, so the flip is about the **blank's**
centreline. With a blank in play the existing axis is simply the wrong one, and it is the wrong kind
of wrong: the file looks entirely correct and the board is scrapped.

**Asymmetric left and right borders survive the flip, but only if that axis is right.** The blank's
footprint is unchanged by a flip — it is a rectangle either way — but the design maps `x → W − x` in
blank coordinates. With borders of 20 mm and 2 mm the design lands 18 mm from where naive arithmetic
puts it. Computable, and easy to get backwards. So: **default `left == right` whenever any layer in
the job is mirrored**, warn when they differ, and offer to equalise them. Top and bottom can stay
asymmetric forever, because nothing ever flips about the horizontal axis.

**A symmetric rectangle seats identically whether the flip was right or wrong.** This is a hazard the
blank *introduces*: today a board flipped the wrong way looks obviously wrong, and a blank flipped the
wrong way seats perfectly and cuts a mirror image. §4.3 already wants an asymmetric mark for this
reason; here the keying in 5.6.3 is not a nicety, it is the mitigation, and the export should say
which corner the key must be in for the side being cut.

#### 5.6.5 Verification: the border is a test coupon — **not started**

The mill cuts the blank, so on the mill the datum is *defined* and nothing needs checking — the same
argument §4.1 makes for fixtures. **The laser cuts nothing and verifies nothing.** It trusts that its
corner stop is where the operator thinks it is, and if that is 0.5 mm out then every laser step is
0.5 mm out and no artefact in the process says so.

The border is waste material sitting exactly where the answer is. On the first laser job of a blank,
burn into the border either a short registration mark at a stated coordinate, or a light trace of
where the app believes the blank's edge to be. One measurement with a caliper against the real edge
gives the laser stop's offset, once, for good — and a trace that lands visibly off the edge catches a
gross error before anything that matters is burned.

This is the same reasoning as [§4.4](04-Machines-Laser-and-Mixed-Workflows.md#44-verification-before-committing):
never let the first confirmation that alignment worked be a ruined board.

#### 5.6.6 What it does not do

**It is an X/Y datum only.** After etching and soldermask the surface height has changed; Z is touched
off at every setup regardless, and the isolation and mask-relief cuts still want a height map. The
blank does not help with Z and should not be described as if it does.

**A mill-only single-sided board gains nothing from it.** Nothing leaves the machine, so one setup
does isolation, drilling and cut-out with no registration problem to solve. The blank earns its keep
exactly when the work travels — which is every mixed and every double-sided job, but the app should
say so rather than recommend it universally.

**A declared blank does not make a pre-cut board square.** It fixes the origin, the page and the
mirror axis, which is most of the value; it cannot tell you that the sheet you bought is 182.6 mm
rather than 183, or that its corner is a degree out. Anything that needs the tolerance wants either
a cut blank or "square the stock" run on a declared one.

**It is looser than the pin recipe.** Mill-only work is excellent, because everything is in one
coordinate frame. Mill-to-laser is limited by the stop's alignment, the blank's squareness and seating
repeatability — realistically a tenth or two without 5.6.5, tightening with it. The drill-and-pin
recipe reaches 20–50 µm. Both belong in the app, and the help has to say plainly which is which: the
blank is easier and asks nothing of the design; pins are tighter.

**The corner stop still needs its inside corner relieved**, exactly as §4.1.1 already says. A sharp
blank corner pushed into a radiused inside corner rides up on the fillet and sits several tenths out,
differently every time. The laser's stop needs the same relief and the same reasoning.

#### Done when

A single-sided mixed job runs end to end from one import: the blank is cut on the mill, the board is
etched on the laser, and the drilling and the release cut — run after the board has been off the mill
twice — land on the etched artwork, measured, within a tenth.

Then the same for a double-sided board, with the flip, and with the export refusing to proceed when
the left and right borders differ.

And the case that asked for the stated size: a panel laid out to fit 183 × 122 mm pre-cut stock is
given those two numbers, nothing is cut to make the blank, every program and every SVG references
its corner, and a panel 1.4 mm too wide for it is refused by name before the stock is clamped rather
than discovered after.

### Phase 6 — Polish and reach

- **Rulers down the edges of the viewport.** See 6.1.
- **Climb or conventional, chosen rather than inherited.** See 6.2.
- **Staying down between passes that touch** — done. See 6.3.
- **Tabs: where, how many, how big.** See 6.4.
- **A picture on the companion pages**, with the holes and slots numbered in run order. See 6.5.
- **The tool library, once it has more than a handful in it** — filter, sort, copy. See 6.6.
- **Teaching the conventions** — a coachmark the first time, and a first-run walkthrough. See 6.7.
- **The viewer leaves a gap in every outline ring** — done. See 6.8.
- **Open recent**, off the File menu. See 6.9.
- **Drill hits drawn as an X**, with their own toggle under Toolpath moves. See 6.10.
- **Bit changes: one file per bit** (built), **or one file with custom tool-change G-code**. See 6.11.
- **Drill alignment**: hover a bit over a real hole, find the origin shift by eye, write the drilling and routing files again with it — built. See 6.12.
- Material-removal simulation as a first-class view and test oracle.
- Rest machining / multi-tool bulk clearing.
- Trochoidal pocketing.
- Additional mill posts: grblHAL, FluidNC, LinuxCNC, Mach3.
- Solder-paste stencil generation.
- Machine-profile sharing.

#### 6.1 Rulers — **scheduled, not started**

Requested from the workshop: a scale down the left edge and along the top of the viewport, so the
size of what is on screen can be read rather than guessed at.

The viewport already knows everything this needs — the board draws in millimetres, `ViewTransform`
converts world to screen, and the status bar carries a millimetres-per-pixel figure that almost
nobody looks at. What is missing is the reading being *where the eye already is*, next to the board
rather than in the corner of the window.

**Reuse the grid's step ladder.** `BoardRenderer` already picks a spacing from
`0.1, 0.5, 1, 5, 10, 25, 50, 100, 250` mm — the first whose on-screen size clears 12 px — and the
grid lines the ruler ticks against are drawn from it. A ruler that chose its own spacing would
disagree with the grid it sits over at some zoom level, and a disagreement is worse than no ruler.
Major ticks get a number; the step below gets an unlabelled minor tick.

**Both viewports, one implementation.** `BoardView` and `ToolpathView` are deliberately siblings
rather than a shared base ([05 §2.2](05-Viewer-and-Export.md#22-rendering-skiasharp)), but a ruler
is a function of the transform and the viewport rectangle and nothing else, so it belongs in
`MillBurn.Viewer` beside the grid and is called by both. The headless PNG renderers get it for free,
which also makes it checkable in a screenshot like everything else here.

**Zero is the board's corner, because that is where the machine's zero is.** Every emitted file uses
the board's lower-left corner as work zero, so the ruler must read the same — a ruler measuring from
the window's edge would be a second coordinate system on the screen and would eventually be
believed. Y counts upward, not down the screen.

Worth having with it, cheaply:

- **A cursor readout.** The world position under the pointer, next to the ruler or on it. This is
  the thing people actually reach for when they ask how big something is.
- **A measuring drag.** Click-drag with a modifier to get a dimension between two points, with dx,
  dy and the diagonal. Not a drawing tool and not saved — a tape measure.
- **Toggleable, and remembered**, alongside the existing theme and colour preferences.

Left for later: a ruler in inches (the app is millimetres throughout and mixing units in one view is
how a wrong number gets read confidently), and printable dimensioned output, which is a drawing
feature rather than a viewer one.

#### 6.2 Climb or conventional — **scheduled, not started, low priority**

Asked from the workshop: *"is the default cut direction making a climb cut or a conventional cut?"*
The honest answer is that **nothing chooses**. There is no setting, and no part of the pipeline
considers it. What follows is what the investigation found, recorded so the work starts from
measurement rather than from re-deriving it.

**Today the direction is inherited from the geometry.** A closed contour keeps whatever winding
Clipper gave it: `ToolpathRouter.Materialise` only rotates which vertex a closed loop starts at
(`RotateTo`) and never reverses it — `RouteNode.Flip` applies to open passes alone. Measured on
PogoTest1:

| File | Counter-clockwise | Clockwise |
|---|---|---|
| `PogoTest1-F_Cu.nc` | 25 | 0 |
| `PogoTest1-Edge_Cuts.nc` | 8 | 0 |
| `PogoTest1-B_Cu.nc` | 27 | 23 |

**Most of what is emitted cannot be either.** A V-bit isolation pass and the outline cut-out are
full-width slots: the cutter is engaged on both sides at once, so it is climbing and conventional
simultaneously. The same goes for the first pass of anything. This is worth stating in whatever UI
the setting eventually gets, or it will promise a choice that does not exist for the operation in
front of it.

**Where it is real:** the second and subsequent isolation passes, which widen the moat and meet
fresh copper on one side only, and mask-relief pocketing. There, going counter-clockwise around an
island puts the fresh material to the *right* of travel, and with the `M3` clockwise spindle the
emitter always writes, material on the right is **climb** — see `G41`, tool offset left of the path.
So the app climbs today, by accident rather than by choice.

Three things make this more than flipping a winding.

**The bottom side is mirrored, and mirroring flips the hand.** Setting every contour
counter-clockwise would give the two faces *opposite* physical cut directions. The convention has to
be applied after the mirror, in machine terms, not to the drawing.

**Holes wind opposite to islands, correctly.** An annular pad's inner boundary must run the other
way round from its outer one to keep the cutter on the same side of the copper. Any normalisation
has to preserve that relationship rather than force one winding everywhere — which is the obvious
implementation and the wrong one.

**It takes a degree of freedom away from the travel optimiser.** Open passes — tabbed outline runs,
panel-channel centrelines, test-cut bands — are reversed freely today to save rapid, and the
test-cut bands alternate deliberately so a deeper pass does not drive back to its start first
([one pass down the middle](#one-pass-down-the-middle) measured 7 m of that on a panel). Honouring a
direction on those costs travel, so the setting should apply to closed contours and say plainly that
it does.

**Done when** a per-operation choice exists, the emitted programs measurably run the chosen hand on
both faces of a double-sided board, and an operation for which the question is meaningless says so
rather than offering a switch that changes nothing.

#### 6.3 Staying down between passes that touch — **done**

Requested from the workshop: *"When cutting paths next to each other, the job does a z-lift then
back down. Cutting around a circle with 3 loops: the first loop is cut, then a z-lift and z-lower,
then the second ring, another z-lift/lower, then the third cut. Perhaps we can optimize some of
them out?"*

Worth more than it sounds, because a lift is not free on a machine whose Z traverse is 100 mm/min
against 2000 in XY. One cycle is a 2.04 mm retract, a 1.50 mm rapid back down and a 0.54 mm feed to
depth: **about 2.7 seconds**, and the real board's isolation did 26 of them in a job that ran in
4:35. A quarter of the wall clock, spent moving the tool away from the exact place it was about to
work.

**How many are actually adjacent was measurable rather than a guess.** Sorting the twenty-five link
moves in that program by the distance from where one run ends to where the next begins:

```
0.126 0.126 0.126 0.126 0.126 0.126 0.126 0.126 0.127 0.150 0.154 0.161 0.161
0.629 0.762 0.982 1.302 1.912 2.175 2.338 2.793 5.054 6.903 8.553 10.008
```

The break is unmistakable. Thirteen are a stepover apart — 0.126 mm is *exactly* the isolation
stepover — and the next is five times further. Those thirteen are lap-to-lap on the same island.

**The rule is about material, not distance**, and that distinction is the whole design. A 0.126 mm
hop between concentric laps is safe because the lap that just finished cleared that band. The same
0.126 mm hop between two unrelated runs that happen to end up near each other is a cut trace, and
it would be invisible in the file. A distance threshold cannot tell those apart, so there is not
one.

`PassLinker` asks the only question that decides it: would the tool, dragged from here to there at
depth, cut anything that is not already gone or about to be? It is answered with the same Clipper
offsets that produced the paths. The region a pass clears is its centreline swept by the tool, and
a link is allowed when its own swept ribbon lies inside the previous pass's ribbon plus the next
one's. The next one counts because it is cut immediately afterwards — material the link takes out
of it was leaving anyway.

**`EndType.Joined`, not `EndType.Polygon`.** A closed contour inflated as a polygon becomes a
filled region, which would claim the island *inside* an isolation ring as cleared ground — the
copper the ring exists to protect. That is the one mistake here that cuts a trace in half, so it
has its own test: two halves of a ring, whose straight-line link runs across the middle of the pad,
must be refused.

**Deliberately local** — the previous pass and the next, not the whole accumulated history of the
board. That is the conservative direction (a link over ground cleared five passes ago is refused),
it keeps the cost to two small offsets and a difference per link, and it covers all thirteen.

**What it does not touch.** An outline cut goes through the stock and has nothing cleared beside
it; its lifts are structural. A drill is a plunge, not a contour. A pass at a different depth is a
plunge by definition, however close it starts to where the last one finished.

**Measured, on the board it was reported from.** Thirteen of seventeen candidate links kept down on
the top copper, twenty-one of thirty-one on the bottom. Plunges in the top-copper program go from
26 to 13, and 1.8 mm of rapid becomes 1.8 mm of cutting. The levelled program's estimate goes from
**1:56 – 8:52 to 1:19 – 8:13**: thirty-nine seconds off each end, against a predicted 13 × 2.7 s =
35 s, on a run that measured 4:35.

**The test that matters is the refusal.** `NoLinkOnARealBoardTouchesCopper` takes the copper
straight from the Gerber and the links from the passes that were marked, sweeps each link by the
tool and intersects it with the copper. The two halves share no code, so it is not the linker
agreeing with itself. Nothing registers — not a square micron.

**A coverage gap it exposed.** Every golden snapshot in the corpus used the default isolation
width, which is one lap, and one lap has no lap after it. Concentric passes, their ordering, and
everything that decides whether the tool lifts between them were outside the corpus entirely —
`PassLinker` could have been deleted and all three snapshots would still have matched. There is now
a `-wide-moat` snapshot per single board at the 0.4 mm the workshop actually cuts.

**Still open:** the general version, where the allowed region is everything cleared so far at this
depth rather than the two neighbouring passes. It would pick up links across ground cleared earlier
in the program, and it matters more for mask-relief pocketing than for isolation. The local test is
a strict subset of it, so nothing has to be undone to get there.

#### 6.4 Tabs: where, how many, how big — **scheduled, not started**

Requested from the workshop, after [every tab asked for is a tab that gets cut](#every-tab-asked-for)
put four tabs on a board that had been getting two: *"They are large tabs and may not be in the best
locations."*

Both observations are right, and both follow from the same thing: tabs today are **count plus two
global numbers**. `TabCount` are spaced by equal arc length from an arbitrary seam, every one is
`TabWidthNm` wide and leaves `TabHeightNm` of material. Nothing looks at the board.

What that gets wrong:

- **3 mm is a lot on a 21 mm edge** and not much on a 100 mm one. The default should scale with the
  perimeter, or with how much of the board is hanging off it.
- **Equal arc length lands them anywhere**, including across a corner, next to a mounting hole, or
  on the one edge that has to be straight because a connector sits on it.
- **One height for all of them.** A tab on a long edge holds less than a tab near a corner.

Worth having, roughly in this order:

1. **Placement by edge rather than by arc length** — one per straight edge, centred, which on a
   rectangle is the answer everyone draws by hand.
2. **Width and height per tab**, with sensible defaults derived from the perimeter and thickness.
3. **Manual placement**: click the outline in the viewport to put a tab there, drag to move it.
   This is the one that actually answers "not in the best locations", and it needs the viewport to
   hit-test the profile, which nothing does yet.
4. **Keep-out from features** — no tab within *n* mm of a hole, a pad, or the board's own artwork.

**The physics stays.** Whatever chooses the position, the split happens in `SplitForTabs`, and the
lesson from the bug is that the split must be computed on arc length within a segment and never on
which vertices happen to exist.

##### Found since, and waiting for this

- **The tab height in the program's comment can be false.** A pass counts as "tabbed" once it is
  deeper than `total − TabHeightNm − BreakThroughNm`, and a tabbed pass jumps the tab entirely.
  Nothing ever cuts the tab region down to the tab's top. So when *every* pass is tabbed — a thin
  board, a big stepdown — the tab is left the full board thickness. Measured on the test board: 0.8 mm
  board, 0.5 mm stepdown, 0.1 mm through, so the threshold is 0.3 mm and both passes (0.5 and 0.9 mm)
  jump the tab. The program says `0.50 mm of material left under each`; 0.8 mm is left. It is the
  same rule in the blank (`BlankOperation`) and the board outline (`OutlineOperation`). The fix is a
  pass over each tab at exactly the tab's top, and a comment built from what was emitted.
- **The blank's tabs are on the top and right edges only**, two per edge, by design: the bottom and
  left are the datum and must be cut clean. Workshop, on seeing them: *"We can deal with that when
  we get to the tabs upgrades."* Whatever placement this phase adds has to keep that rule for the
  blank — a tab stub on a datum edge stops the piece seating, invisibly.

**Done when** a rectangular board puts one centred tab on each edge by default, any of them can be
moved and resized individually, the emitted gaps land where the picture says they will, and the
material left under a tab is the height the program says it is.

#### 6.5 A picture on the companion pages — **scheduled, not started**

Requested from the workshop: *"in the companion html for drills and routing, include images of the
layer with the holes/slots numbered in the order of drilling/routing."*

Both pages are currently a table of numbers standing in for a board. They say *six holes with the
1.00 mm bit* and leave the operator to work out which six — which matters at the two moments the
page exists for: deciding a bit is about to go in the wrong place, and finding where a run stopped.

**The strongest reason is the one about routing, not drilling.** A refused slot is invisible: it is
not in the program, and the board renders it exactly as the EDA tool drew it. The page names it in
prose, which is better than nothing and worse than a picture of the board with four slots marked
*not cut*. That single drawing turns a paragraph somebody skims into a shape they recognise.

**Inline SVG, not a raster.** The pages are self-contained by rule — one file, no network, nothing
beside them — so an image has to travel inside the HTML. SVG rather than a data-URI PNG for three
reasons: the numbers stay crisp when someone zooms in on a cluster of vias, it prints properly next
to a machine, and it is a few kilobytes where a legible raster of a 100 mm board is hundreds.
`SvgWriter` already exists and is already shared between the laser path and the documentation
exports, so this is a third caller rather than a third implementation.

**Draw the outline, the features, and nothing else.** Not the copper. A board's copper as SVG runs
to hundreds of kilobytes on the panel and adds nothing here — the outline plus the holes is enough
to locate any of them, and it keeps the page small enough to open on a phone in a workshop.

**The marks come from the program; the backdrop comes from the board.** The same split the rest of
this code makes, and worth stating because the two can disagree: the outline is context, the
numbered marks are what the machine will do, and where they differ the marks are the truth. A
picture drawn from the toolpaths would show the run somebody meant.

**One drawing per bit, not one for the file.** The table already has a row per bit; the picture
belongs beside its row, showing that bit's holes numbered from one. An overview with everything on
it sounds useful and is not: at the machine the question is always "where does *this* bit go".

**Legibility is the part that will decide whether this is any good.** An Arduino Mega drills 258
holes in six sizes. Numbering 44 of them on a 100 mm board is already crowded; numbering 258 is
spaghetti. So the drawing has to degrade honestly rather than produce something unreadable and call
it a feature:

- Up to about 40 marks in a step: numbered, with leader lines where they would otherwise collide.
- Above that: dots, a marked start, and the travel path between them — which answers "where does it
  begin and roughly where does it go" without pretending to answer "which one is number 173".
- Either way, the count is on the page in text, and the text is the authority.

**What to draw for a slot** is a question the drilling page does not have. A slot has a length and a
direction, so the mark is the slot's own shape with the number at one end and an arrow showing which
way the cutter runs — the direction is not cosmetic, because it is what tells somebody watching that
the machine is doing what the page says.

**Refused features in the same drawing**, greyed and hatched, with their width labelled rather than
a number. They are the only thing on the page that cannot be checked against the machine.

**Done when** a board with several bits produces one drawing per bit with its holes numbered in run
order, a routing page shows its slots with direction and its refusals hatched, the Mega's 44-hole
step degrades to dots without becoming unreadable, and every page is still one file that opens with
no network.

#### 6.6 The tool library, once it has more than a handful in it — **scheduled, not started**

Requested from the workshop: **filter by tool type, sorting, and copy a tool.** All three are the
same symptom — the list was designed for the six tools it shipped with, and a library grows.

**The list is insertion-ordered**, which is not an order. A library added to over months puts the
0.5 mm end mill bought last Tuesday between a V-bit and a drill, and the only way to find anything
is to read every row. That is the defect the three requests are circling.

**A better row may be worth more than all three.** Each entry shows a name and nothing else, and a
name is whatever somebody typed — "1mm Corn Endmill" and "1.2 mm end mill" sit in the same list
with no common shape. A row that showed the kind as a small mark, the name, and the diameter or tip
in a column of its own would make the list scannable without sorting or filtering it, and the two
features would then be for the case where it is genuinely long. Build the row first and see what is
left to want.

Then, in the order they earn their place:

**Copy.** The one with the most value and the least design. Tools come in families — a 0.8 and a
1.0 end mill share their feeds, their stepdown philosophy and their notes, and entering the second
from scratch is retyping the first. It is also how a variant for a different material gets made.

> **It must not copy the id.** `Tool.Id` is what a project stores, and two tools sharing one are the
> same tool to everything downstream: a job that named the copy would silently cut with the
> original. New `Guid`, name suffixed with "(copy)", selected with the name box focused so the first
> keystroke renames it.

**Sorting** by name, by kind, or by diameter. Diameter is the one that matters, because it is how
anybody thinks about a drawer of bits, and it wants the tip width standing in for a V-bit's
diameter so the two sort together rather than a V-bit landing at zero.

> **Sort the view, never the file.** `tools.json` keeps its order: it is diffable, it is what a
> person reads when something goes wrong, and rewriting it on every visit to the window turns a
> change of sort into a change of file. Nothing about the library's behaviour depends on its
> order — selection is by id — so there is no reason for the sort to be persistent, or even saved.

**Filter by kind**, which is three buttons rather than a dropdown: a dropdown hides which filter is
on, and a filter you cannot see is how somebody concludes their tools have vanished.

> **New and Copy must defeat the filter.** A tool created while a filter would hide it has to appear
> and be selected anyway, or the button looks broken. The honest behaviour is to clear the filter
> rather than to make an exception to it, so what is on screen always matches what the controls say.

**Done when** a library of thirty tools can be sorted by diameter and filtered to end mills, a tool
can be copied and renamed without retyping its feeds, the copy has its own id, and `tools.json` is
byte-identical after a session that only looked at it.

#### 6.7 Teaching the conventions: coachmarks and a first-run walkthrough — **scheduled, needs research**

Requested from the workshop, and the reasoning behind the request is the useful part:

> *"I really like how MillBurn uses the status messages in the footer of the window. However, it's
> not a common thing. Saving a project does not pop up a dialog (cause that's annoying) and instead
> just puts the message into the status. I like it, but it can be a bit hard to get used to at
> first."*

**That is not a request for a tutorial. It is a request to teach one convention.** The app
deliberately does not interrupt for routine success — a modal that says "saved" is a modal you
learn to dismiss without reading — and the cost of that choice is that a successful save is silent
to somebody who does not yet know where the app talks. The gap is narrow and specific, and the fix
should be too.

So two features, and the smaller one is the one that pays:

##### The coachmark — a pill, an arrow, and one sentence, the first time an action happens

Press Save for the first time; the window dims, a callout appears beside the status bar saying
*this is where the app tells you things*, and it goes away on the next click. The save already
happened — the overlay is never in the way of the thing it is explaining.

Worth having on a handful of moments and no more. A coachmark on everything is a tutorial nobody
finishes:

- **First save** → the status bar. The one that prompted this.
- **First preview** → the toolpath is parsed back out of the emitted G-code, not drawn from the
  toolpath that made it. That is the app's most load-bearing idea and nothing on screen says it.
- **First refusal** → the checks panel, because a refusal that is not read is a refusal that did
  not work.
- **First export** → the review list, which exists to be read before anything is written.

##### The walkthrough — the same overlay, driven by next and back

For the first run. Same machinery, a scripted sequence rather than a trigger.

**The trap that decides whether it is any good: on a first run there is no board open.** Most of
what a tour wants to point at — the layer list, Preview, Export, the viewport — is empty, disabled,
or not there. A tour of greyed-out controls teaches nothing and reads as a broken feature. Two ways
out, and the second is better:

1. Wait until a board is loaded before offering the tour, which means it is not really a first-run
   experience any more.
2. **Have it load the test board itself.** `Millburn_Test_Board` is being built in the workshop as
   exactly this — a board that covers the scenarios a user meets, meant to be a tutorial as much as
   a fixture. A walkthrough that opens it has something real to point at from its first step, and
   the board and the tour are then worth maintaining together rather than separately.

##### What needs researching, because none of it is decided

- **How to draw it in Avalonia.** An overlay that dims the window and cuts a hole around one
  control needs the control's bounds in window coordinates (`TranslatePoint`) and a layer above
  everything — `OverlayLayer`, an `AdornerLayer`, or a top-level `Panel` in the window's own grid.
  Which of those behaves when the target is inside a `ScrollViewer`, or scrolled out of view, is
  the question that will decide the implementation.
- **What happens when the target moves.** The status bar is fixed; a layer row is not. A callout
  anchored to something that scrolls, resizes or disappears has to follow it or dismiss itself, and
  "points at empty space" is worse than not pointing.
- **Escape, focus and keyboard.** It must dismiss on Esc and on any click, must not trap focus, and
  must not swallow the keystroke that dismissed it.
- **Reduced motion**, and no animation that cannot be turned off.

##### What is already decided, because this codebase has rules about it

- **It has to be checkable from a screenshot.** Every dialog here can be rendered headlessly —
  `--settings`, `--tools`, `--test-cuts`, `--paste`. A coachmark that can only be seen by being a
  new user is the one piece of UI nobody can review. It needs `--tip <key>` to force one open, and
  the walkthrough needs a way to open at a given step.
- **It must never fire in an automated run.** Otherwise every screenshot in the repository grows a
  dimmed overlay, and the tools used to check the app start lying about it.
- **The "seen" flags live in `AppSettings`**, one per coachmark rather than a single "new user"
  boolean — a flag per moment is what lets a new moment be added later without re-teaching the old
  ones. With a **Reset tips** button beside them in Settings, which is also how anybody reviews the
  feature after the first day.
- **The walkthrough suppresses the coachmarks it has already covered**, or the first save after
  finishing a tour re-explains the status bar to somebody who has just been told.

**Done when** a fresh profile gets a walkthrough that has a real board to point at, the first save
after that does *not* re-explain the status bar, every coachmark can be forced open from the
command line for a screenshot, no automated run ever draws one, and Esc closes anything this puts
on screen.

#### 6.8 The viewer leaves a gap in every outline ring — **done**

Reported from the workshop against the panelised connector board: the board outline is drawn with
short pieces missing, most visibly at the closed end of each routed channel. The programs are
correct — this is the picture, not the file.

**The cause is already known**, so this is written down mostly so it is not re-diagnosed. One flag,
`BoardLayerStyle.Outlined`, is doing two unrelated jobs:

- In `BoardRenderer` it means **stroke this layer rather than fill it** — right for the outline,
  whose profile would become a white slab if filled.
- In `BoardScene.Build` it means **these rings are open runs, so do not close them** — right for a
  backplot, where closing the path would draw a segment from the end of the program back to its
  start that the machine never makes.

Every stroked board layer gets both meanings. `LayerRole.Outline`, `DrillMap` and `Documentation`
are stroked, but their rings come from Clipper as *closed areas*, so each one is drawn missing
exactly the segment that would have closed it. One gap per ring, always at whatever vertex Clipper
started that ring on — which is why it looks systematic rather than random, and why it lands in the
same place on all thirty-two cells of a panel.

**The fix separated the two meanings** rather than closing everything, because a backplot must
still stay open. `BoardLayerStyle.ClosedRings` now says whether the rings are areas — true by
default, since everything realised from a Gerber is one, and false on all five `BackplotPalette`
styles. `Outlined` means only "stroke, do not fill".

`ClosedRingTests` guards both directions on the panel fixture: one close verb per outline ring, no
close verb on a backplot run, and a third test asserting the two palettes agree on what their rings
are — so adding a style and forgetting which it is fails a test rather than producing a picture
somebody has to notice.

#### 6.9 Open recent — **scheduled, not started, small**

Requested from the workshop: a **Open recent** item in the File menu that opens a submenu on hover,
listing the last few projects.

**The list already exists and is already being kept.** `AppSettings.RecentProjects` holds ten paths,
newest first, de-duplicated case-insensitively, and `MainViewModel.OpenProject` and `SaveProject`
both push to it. Nothing has ever shown it. So this is a menu, not a feature: the work is in the
view, and the model side is done.

What the implementation has to decide:

- **Unsaved work.** Opening from the menu has to go through the same guard as `Ctrl+O`, not around
  it. A one-click path to discarding an unsaved job is the way this feature usually goes wrong.
- **A path that is no longer there.** Boards move, drives unmount. The entry should say so and drop
  itself from the list rather than raising the same failure every time the menu is opened. Whether
  a missing entry is greyed or simply gone is a judgement to make while looking at it.
- **Long paths.** These are full paths to folders several levels deep. The menu wants the file name
  with enough of the directory to tell two `MillBurn.mbproj` files apart, and the full path in a
  tooltip.
- **Keyboard.** Numbered accelerators (`_1`…`_9`) are conventional and free.
- **Empty and single-entry states.** A submenu with nothing in it should be disabled, not empty.
- **How many.** The store keeps ten; the menu does not have to show ten. The request said "up to x",
  which is a setting if anyone ever wants it and a constant until they do.

**Done when** the submenu lists real recent projects, opening one is indistinguishable from opening
it through the dialog including the unsaved-work guard, a moved project reports itself once and
leaves, and the whole thing is checkable from a headless screenshot like every other menu here.

#### 6.10 Drill hits drawn as an X — **scheduled, not started**

Requested from the workshop: show each drilled hole in the viewer as an X, with its own toggle
under *Toolpath moves*.

**Today a drilling program previews as almost nothing.** A drill hit is a plunge and a retract at
one XY, and the backplot draws only moves that travel in plan — plunges are counted and never drawn.
So a drilling preview is the rapids between the holes and nothing at the holes themselves, which is
the part worth checking.

**With canned cycles turned on it is worse.** The program then drills with `G81`/`G83`, which the
G-code reader does not interpret: it reports "those holes are not shown" and moves on. The X has to
come from both forms, or it is missing for exactly the people who turned that setting on.

What the implementation has to decide:

- **What counts as a hit.** Not every plunge — every milling pass starts with one. A hit is a
  plunge below the surface that comes back up at the same XY with no cutting move in between, or
  each XY a `G81`/`G83` names.
- **How big the X is.** Either a fixed size on screen, like the other toolpath lines, so it is
  findable at any zoom; or the bit's own diameter, read from the program's `( Drill 1.00 mm … )`
  section labels, so a wrong bit shows as an X that does not fit its pad. The second is the more
  useful, and needs a fallback for programs without the labels.
- **Its colour**, chosen from the hues the board does not use, like the rest of the backplot — not
  yellow (cuts), magenta (long rapids) or red (rapids at depth).
- **Its toggle comes free.** The *Toolpath moves* chips are built from whatever kinds of move the
  backplot produced, so a new kind gets its chip and follows the layer rows with no extra wiring —
  provided its ids are unique per source layer, which the routing-rapids fix
  (`BackplotSourceTests`) now guarantees.

**Done when** a drilling program previews with an X at every hole, from plain plunges and from
canned cycles alike; the X hides with its own chip and with its layer's row; and a milling program's
plunges do not grow Xs.

#### 6.11 Bit changes: one file per bit, or one file with custom tool-change G-code — **per-bit built; the choice is not**

**Built (2026-09-14): one file per bit.** A drilling or routing layer that needs more than one bit
is written as one file per bit — `Board-PTH-drl.bit1-1.00mm.nc`, `Board-PTH-drl.bit2-0.50mm.nc`, …,
numbered in the suggested order — and a layer with one bit is still the single file it always was.
Each file's header says which bit to fit and which file comes next. The drilling and routing pages
stay one per layer and list the files in the suggested order.

Found at the machine, on the first drilling run with a bit change. The single file stopped between
bits with `M5`/`M0`/`M3` at safe Z, and `M0` puts GRBL in *Hold*. A controller on hold will not jog
or probe, so there was no way to lift the head, fit the next bit and touch off Z without stopping the
program — and the page's "change the bit, then resume in your sender" could not be followed. The
workaround was Stop, change and re-zero in *Idle*, then restart the file from the line after the
`M0`. One file per bit makes that unavoidable stop the plan rather than a recovery.

Requested alongside, for later:

- **Let the operator choose** one file per bit or one file with bit changes in it. A sender with
  tool-change macros, or a machine with an automatic changer, runs a single file perfectly well, and
  for those one file is simpler. The choice belongs with the machine settings — it depends on the
  controller and the sender, not on the board.
- **Custom tool-change G-code.** What the single file does at a change is the part that varies by
  machine: `M6 T<n>` for a changer or a macro-driven sender; a raise to a tool-change height; a probe
  cycle at a fixed touch-off spot. It wants the same treatment as the custom start and end G-code
  (Settings › start/end), with the bit's number, name and diameter available to it.

What that needs from the code: the emitter's stop sequence (today a fixed `M5`/`M0`/`M3`) replaced by
the operator's block, and the pages' single-file wording, which is still there for that case and
still says "resume in your sender" — right for those machines, and wrong for plain GRBL.

**Done when** Settings offers *one file per bit* (the default) and *one file, with this tool-change
G-code*, the second emits the operator's block at every change, and each form's page describes the
run it actually produces.

#### 6.12 Drill alignment — **built**

Requested from the workshop, for holes that have to land in pads already on the board: a small hole
in a small pad leaves a few tenths either side, and a drilling origin slightly out puts holes on the
edge of their pads. The board is assumed square to the machine, so the correction is an origin shift.

**Job › Drill alignment…** opens a dialog that stays open while tests are written, because finding
the offset is a loop. Pick a drilling or routing file and a hole in it; **Write test** writes
`Board.align-test.nc`, overwriting the last one, which with the spindle off moves over that hole with
the offset applied and stops the tip at the hover height (0.1 mm by default, remembered) and stays
there. Adjust X and Y, write again, reload, run. **Write aligned files** writes every drilling and
routing file again, shifted, as `….aligned.nc` beside the originals with their pages under aligned
names — and the board outline too, unless unticked, since the alignment is for a board that already
has copper on it and the outline has to go round that copper — and closes. The CLI does the same:
`align` for the test, `export --align x,y [--align-outline]` for the files.

- The holes are read from **the emitted program**: drill plunge points, and the centre of the ground
  each routed cut covers — which for a helix is the hole's centre and for a racetrack the slot's.
- The shift is added to the work-zero shift, after mirroring, so X and Y mean what they meant at the
  machine. Drilling, routing and (when ticked) the board outline move; copper, stock and SVGs do
  not. The stock is cut as an outline but is told apart by its layer name and never moves.
- The test refuses a hover height at or below the surface.

Not done: remembering the offset in the project, so a later full export stays aligned; and checking
two holes at once to tell a shift from a stock that is not square. The help page says to hover over a
hole at the far side too.

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
the standalone G-code viewer, the machine settings, and the board pane, all of which now exist.

**Guides** sit beside the FAQ in `Help/guides/`: one self-contained HTML file per job, start to
finish, with its pictures in `Help/guides/images/`. Styles are inside each page so a guide can be
printed or passed on alone. Reached from Help ▸ Guides and from the help contents. Real-world
photos are the author's to supply; until then each is a labelled placeholder naming the file it is
waiting for, so a missing picture is visible rather than a broken image. First: **Drill alignment**
(6.12).

**`Help/` is not `Documentation/`.** This directory is design documentation — why the code is
shaped the way it is, written for whoever maintains it. User help is a different audience, a
different lifecycle, and mixing the two makes both worse.

**Generate every reference section; hand-write only the prose.** A stale number in a CAM manual is
worse than no manual: someone reads "default depth 0.05 mm" long after the default moved, and cuts
a board to it. So the settings reference comes from the settings types, the CLI reference from the
CLI's own help, and the shortcut list from the key bindings — none of them retyped. Workflows,
troubleshooting and the conceptual pages are prose and stay stable.

**Generated sections: not built.** Everything in `Help/` is hand-written today, including the
numbers. The risk is stated above and it is real, so this is the next thing owed to Phase 7 rather
than more prose.

`HelpPagesTests` walks the HTML and fails on a broken internal link, a missing anchor, a referenced
file that does not ship, or an FAQ section nothing links to. Cheap, and it is the only thing that
reliably catches documentation rot — the FAQ is one long page reached almost entirely by fragment,
so a renamed section silently sends every "More ›" to the top of it, which looks exactly like the
help failing to answer the question.

It was written after an audit found this paragraph claiming it already existed. The pages were in
fact sound — 47 internal links, all resolving — which is the point: nothing would have told anyone
if they had not been. The test was verified by breaking an anchor on purpose and watching two of
its five cases fail.

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

<a id="phase-8"></a>

### Phase 8 — An MCP server — **scheduled, not started**

A [Model Context Protocol](https://modelcontextprotocol.io) server over the same libraries the app
and the CLI drive, so an assistant can load a board, look at it, and say what a job would do.

It depends on nothing unbuilt and can be taken at any point. It is numbered last because it is a new
*surface*, not a new capability: everything it would expose already exists and is already tested.

#### 8.1 Why this and not just the CLI

The CLI already makes the pipeline scriptable, and a shell tool is a perfectly good thing for an
assistant to call. Two things are genuinely different.

**It can look at the board.** MCP returns images, and this project's own working method — the one
that found the mirrored backplot, the drill-guide label bug and the panel layout problems — is
*render it and look*. A tool that hands back a PNG of the board, or of the emitted programs drawn
over the copper, gives a model the same check a person gets. Parsing `board`'s text output does not.

**The answers are structured.** `plan_export` returning one object per file, each with its summary
lines and its warnings, is a different thing from scraping a console report that exists to be read
by a human and is free to change wording. The export plan is already a model — `ExportItem` has
carried `Summary`, `Warnings` and `Companion` since Phase 2 — and it has never had a consumer that
wanted it as data.

#### 8.2 What it must not become

**The scope boundary is unchanged.** No serial port, no jogging, no streaming. See
[01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-writes-files-it-does-not-drive-machines).
An MCP server makes it easier to ask for a file; it does not make it acceptable to drive a spindle.

**The export review cannot be quietly removed.** The app's central safety property is that *nothing
is written until you have seen a list of exactly what is about to be written* — every file, what it
will do, how long it takes, and anything worth checking. A tool call skips that window. The operator
approving `write_export` in a chat client sees the tool's name and its arguments; they do not see
"one of these seven files is mirrored and needs the stock flipped."

So:

- **Read-only by default.** The server starts with no ability to write anything. `plan_export`
  answers every question an export answers, without producing a file.
- **Writing is opt-in at launch**, `--allow-write <directory>`, and confined to that directory. A
  server launched without it cannot be talked into writing, because the capability is not there to
  be talked into.
- **`write_export` returns the plan it wrote**, file by file, with the warnings repeated. If the
  human reads one thing after the fact, it should be the same list they would have read before.
- **Host approval is not a substitute.** It is a fine second lock and a poor first one: it asks
  about the call, not about the consequence.

**Board files are untrusted input.** Layer labels, file names, net names and X2 attributes come out
of somebody's Gerber export and flow straight into tool results. They are data. Nothing read out of
a board may be treated as an instruction, and the server should not be built in a way that invites
it — no "notes" field that gets concatenated into a prompt, no passing a layer's own text back as
anything but a quoted value.

#### 8.3 The surface

Reading, in rough order of how often it would be called:

| Tool | Returns |
|---|---|
| `describe_board` | Layers with roles, which were guessed, extents, hole count and sizes, copper coverage |
| `inspect_files` | The parse report: what was understood, what was not, and on which line |
| `render_board` | A PNG, through the same `BoardSceneBuilder` and renderer the app uses. Layer selection, theme, size |
| `plan_export` | Every file the export would write: target name, operation, summary lines, warnings, byte count, time bracket. **Writes nothing** |
| `render_toolpaths` | A PNG of the emitted programs drawn back over the board — the backplot, as an image |
| `list_tools` | The saved tool library, with each tool's effective cut width at a given depth |
| `probe_routine` | The G38.2 grid as text, plus the point count and the estimated standing-around time |

Writing, only when `--allow-write` was given:

| Tool | Does |
|---|---|
| `write_export` | Writes the plan to the allowed directory and returns exactly what it wrote |
| `write_probe_routine`, `write_dry_run`, `write_levelled` | The same three files the CLI writes, same code path |

**Nothing is reimplemented.** Every one of these goes through `ExportPlanner`, `BoardLoader` and the
existing renderers. A job exported through the server must be byte-identical to the same job
exported from the app or the CLI, and the golden tests already pin that property — they would simply
gain a third caller.

#### 8.4 Where it lives, and what it costs

`src/MillBurn.Mcp`, a thin driver beside `MillBurn.Cli`. Both are entry points over the same
libraries; nothing references either. That keeps
[01 §2](01-Architecture.md#2-solution-layout)'s rule intact: every algorithm in a UI-free `net10.0`
library, and the drivers hold no logic.

**One dependency question decides the shape.** The official C# MCP SDK is the obvious way to build
this, and its licence has to be checked and recorded in
[THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md) *before* it is added — this repository has no
copyleft anywhere in its graph and that is a claim worth keeping true rather than assuming. If it
turns out not to be permissive, the fallback is not bad: MCP over stdio is JSON-RPC 2.0 with a small
fixed set of methods, and writing it directly costs a day and no dependency at all.

Transport is **stdio** first, because that is what desktop hosts launch. HTTP only if something
actually needs it.

#### Done when

An assistant pointed at a Gerber folder can answer *"what will this cut, and is anything wrong with
it?"* — naming the files, the isolation width, the tool changes, the mirrored layer and the gaps too
narrow to cut — with a render it has actually looked at, and without a single file being written.

Then, with `--allow-write`, the same job comes out byte-for-byte identical to the app's.

<a id="phase-9"></a>

### Phase 9 — Solder paste — **scheduled, not started**

A paste layer becomes a dispensing program: one deposit of the right size on every pad, for a
syringe on the Z axis instead of a cutter.

Numbered after the MCP server because it is genuinely new capability rather than a new surface, and
because it depends on two things that are themselves unbuilt. It is not numbered last because it is
unlikely — the geometry it needs is already parsed, already drawn, and already exported two other
ways.

#### 9.1 Most of this is already here

`LayerRole.TopPaste` and `LayerRole.BottomPaste` are detected today, from X2's
`.FileFunction Paste,Top` and from the `GTP`/`GBP` conventions, and a paste layer already draws in
the viewer and already exports two ways: as SVG (a stencil to cut on the laser) and as
`OperationKind.Pocket` G-code (soldermask relief, since the paste apertures are exactly where the
mask must not be).

What is missing is a third thing it can become. That is one new `OperationKind`, a new operation in
`MillBurn.Cam`, and an emitter in `MillBurn.Gcode` — not a new front half.

#### 9.2 The volume comes free, and this is the whole idea

A paste aperture is not a decoration. It is the hole a stencil would have had, and the deposit a
stencil leaves is its **area times the foil thickness**. So:

> **volume = aperture area x stencil thickness**

Ask the operator one number — the foil they *would* have ordered, 0.10 to 0.15 mm on most hand-built
boards — and every pad on the board has a target volume, computed from geometry Clipper already
gives us exactly.

The part worth dwelling on is that the **proportions** need no calibration at all. The EDA tool
already decided that this 0402 gets a twentieth of what that QFN thermal pad gets, and already
applied whatever aperture reduction the library called for. Those ratios are right by construction.
The only thing a calibration has to supply is the single scale factor between "a cubic millimetre"
and "what this machine does when told to dispense".

Report the total both ways: mm³, and grams via a density the operator enters. Paste density varies
with alloy and metal load and is printed on the jar; it is not a constant to hardcode.

#### 9.3 The scale factor must be measured, so it is refused until it is

Two families of dispenser, and the app has to know which one it is writing for.

| | How it is commanded | mm³ converts via |
|---|---|---|
| **Volumetric** — auger, screw, or a plunger on a stepper | An `E` axis. `M83` relative, then `G1 E<mm> F<mm/min>` beside the move | Syringe bore, or a mm³-per-revolution figure. One constant |
| **Pneumatic** — time and pressure | A valve line (`M62`/`M42`/`M106`, or spindle-on) and `G4 P<seconds>` | **Nothing.** It depends on pressure, needle bore, paste rheology, temperature, and how long the syringe has been open |

The second row is the important one. There is no formula, no table, and no honest default, so
**nothing ships with one** and a paste layer will not export until a measured figure exists. It is
the rule every emitted file here already follows — the leveller refuses an incremental program
rather than assuming what a Z word meant, the drilling program refuses a hole no bit in the library
can make — and paste is a case where the guess is easy to make and expensive to discover.

**A calibration routine, exactly like the test cuts.** `Job > Paste calibration...` writes a program
that lays a row of deposits at stepped commanded amounts onto a scrap, and an HTML page on reading
them: weigh the row on a jeweller's scale, divide by the count and the density, or measure a
deposit's diameter against the table on the page. Enter the answer as mm³ per E-mm or mm³ per
second. The dialog, the report type and the guide builder from
[test cuts](#test-cuts-checking-the-library-against-a-caliper) are the right shape for this and
should be reused rather than re-invented.

#### 9.4 Deposits, not outlines

Paste is *put down*, not *drawn*. The operation is a list of points with a volume each, never a
toolpath around an aperture — a ring of paste round a large thermal pad leaves a void in the middle
and the part floats on it.

- **Small pad** — inscribed circle under one deposit's footprint: a single deposit at the centroid.
- **Elongated pad** — an SOIC or SOT lead: deposits along the medial axis, spaced to merge,
  `ceil(volume / max deposit)` of them. The Voronoi machinery from Phase 3 already computes the
  skeleton this needs.
- **Large area** — a QFN or D-PAK thermal pad: a grid, at the coverage the aperture already implies.
  Stencils window-pane these for the same reason, so matching that convention is both correct and
  familiar.

#### 9.5 The parameters that decide whether it actually works

Dispensing fails in ways milling does not, and every one of them is a named setting rather than a
constant buried in an emitter.

| Parameter | Why it exists |
|---|---|
| **Standoff** | The needle hovers; it does not touch. Roughly half the needle's inner diameter |
| **Dwell before** | Pressure has to build before the deposit starts |
| **Dwell after** | Without it the deposit trails as the needle leaves |
| **Suck-back** | A small negative `E` to break the string. The single biggest cause of bridging |
| **Lift height and lift feed** | Slow off the deposit, then rapid. A fast lift pulls a tail |
| **Travel height** | Just clear of the tallest deposit, not the usual safe Z — this program travels a lot |
| **Purge** | A dab off the board at the start, because the first deposit is never the right size |

Needle bore belongs in the tool library as a new `ToolKind.Needle`, carrying gauge, inner diameter,
and the measured calibration — because that number belongs to *this* needle with *this* paste at
*this* pressure, not to the job or the board. Common gauges run from about 0.84 mm (18G) down to
about 0.25 mm (25G), but they vary by maker, so the library is the authority and nothing is
hardcoded.

#### 9.6 Two dependencies, named honestly

**Levelling stops being optional.** A 0.2 mm standoff and 0.15 mm of bow are the same size. Every
argument in [04 §5](04-Machines-Laser-and-Mixed-Workflows.md#5-height-mapping-autolevelling) applies
harder here than it does to milling, and the machinery already exists — the export review should
treat an unlevelled paste program on a board with a map loaded as something worth saying out loud.

**The board has to be found again.** Paste goes on after etching, drilling and cut-out, so the work
has been off the table and back. That is [Phase 5.6](#phase-56)'s
problem and Phase 5.6's answer; dispensing is one of the clearest reasons to build it.

#### 9.7 What it is not

- **Not pick and place.** It puts paste down. Nothing here places a component.
- **Not reflow.** No profile, no oven, no hotplate.
- **Still files, not machines.** The `E` moves and the valve lines are written into the program;
  the app does not open a port and push them. [01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-writes-files-it-does-not-drive-machines)
  is unchanged by a syringe being in the spindle mount.
- **Not a substitute for a stencil below about 0.5 mm pitch.** Dispensing gets hard exactly where
  stencils get cheap, and the app should say so when it sees apertures that close rather than
  cheerfully emitting a program that will bridge.

#### 9.8 Where it lives

`src/MillBurn.Cam/DispenseOperation.cs` turns apertures into placed deposits;
`src/MillBurn.Gcode/DispenseProgram.cs` turns deposits into a program for one of the two dispenser
families. `OperationKind.Dispense` joins the enum, and a paste layer set to G-code offers *mask
relief* or *dispense paste*. A companion HTML sits beside the program like the drilling guide does:
deposits by size, totals in mm³ and grams, the calibration and needle used, and the fine-pitch
warning if it applies.

#### Done when

A board with an `F_Paste` layer, a needle calibrated on the machine, and a levelled program produce
a deposit on every pad in the right proportion — checked by weighing the board before and after and
comparing against the total the export review predicted.

## 2. Cross-cutting acceptance criteria

| Metric | Target | Where it stands |
|---|---|---|
| Viewer frame rate, 500k segments | ≥ 60 fps pan/zoom | **Met: 98 fps**, Phase 0 |
| Output determinism | byte-identical across runs | **Met**, 8 determinism tests |
| G-code line count | ≥ 5× reduction by simplification | **Met: 20×** on a panel, 2 µm bound |
| Rapid travel, outline ops | below the nearest-neighbour baseline | **Met**, gated per board |
| Time estimate vs. wall clock | the actual falls inside the bracket | **Met so far: 4 of 4.** See below |
| Optimizer, Balanced, 5000 paths | deterministic result | **Met.** The ms target is retired — see below |
| Preview latency (parameter change → redrawn) | < 200 ms | **Not measured**, and cannot be until [01 §4](01-Architecture.md#4-the-incremental-pipeline--designed-not-built) is built |
| Cross-machine registration | ≤ 50 µm | **Not measured.** Needs the Phase 5 alignment work |

Three of these were re-aimed after they turned out to be measuring the wrong thing, and saying so
is more useful than quietly deleting them.

**"Within 10% of the wall clock" was never reachable, and the reason is physics rather than
effort.** What the model can state honestly is a *bracket*: the low end is a machine that never
slows down, the high end is one that stops dead at every segment, and both are real bounds. Where a
particular machine lands between them is what GRBL's `$11` junction deviation governs, and `$11` is
read from the dump but does not yet narrow the bracket. So the criterion is now "the measurement
falls inside the bracket", which four of four runs have done since the machine profile was
corrected — and the next thing worth doing to these numbers is making the bracket narrower, not
making the midpoint more accurate.

**"Optimizer, Balanced, < 500 ms" is retired**, because a wall clock was the defect: the same board
gave 1709 / 1831 / 1926 mm of travel on three runs. The budget is counted in moves examined now
([03 §6](03-Toolpath-Optimization.md#6-time-boxing)), so the criterion is determinism, which a
golden test can actually hold.

**"vs. pcb2gcode" is gone from every row.** pcb2gcode has never been run in this project and there
is no plan to run it; the benchmark that exists compares against our own nearest-neighbour orderer,
which is a harder baseline than pcb2gcode's solver and is reproducible by anyone with the repo. The
reasoning is in [03 §8](03-Toolpath-Optimization.md#8-acceptance-criteria).

## 3. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **Gerber parser edge cases.** Real-world Gerbers from old tools break parsers in creative ways. | High | Build against a wide corpus early (KiCad demos, tracespace test suite, Altium/Eagle exports). Fail loudly and specifically, never silently produce wrong geometry. Ship a "parse report" panel showing what was understood. |
| **Aperture macros are more work than they look.** | Medium | Budget real time in Phase 1. All 21 primitives plus expression evaluation. Do not defer — boards that use them are common and the failure mode is silent wrong copper. |
| **UI framework fit.** A dense CAM tool needs docking, a real code editor, and a fast custom canvas. | Medium | Decided in [07](07-UI-Framework-Decision.md): Avalonia recommended, WPF as fallback. Mitigated structurally — all logic lives in UI-free libraries, so the shell is one replaceable project. The Phase 0 spike settles it in two days rather than two months. |
| **Registration accuracy may not reach 50 µm**, since measurement is done by hand in the operator's sender. | Medium | Push hard on the **fixture** path, which needs no per-job measurement at all. Where fiducials are used, layer the fallbacks: probe (numeric) → microscope crosshair (cheap) → naked eye (worst). Always report the fit residual so the operator knows what they actually got rather than assuming, and refuse to export above the threshold without an override. |
| **Fiducials destroyed by an intervening process.** | High | This is the classic mixed-workflow failure. Encode survivability rules per workflow ([04 §4.2](04-Machines-Laser-and-Mixed-Workflows.md#42-fiducials--measurement-mill)) and validate them when the Job is built: refuse to plan a job whose fiducials cannot survive to their next use. |
| **Optimizer is slow enough to break the live pipeline.** | Medium | Hard time budget with three modes; candidate lists cap the work; the optimizer is always interruptible and always returns its best-so-far. |
| **GPL contamination** from reading pcb2gcode / UGS. | High | Clean-room discipline ([01 §9](01-Architecture.md#9-licensing-strategy)). Reference the *design*, write the code. Do not paste. Keep a note in any file whose design was informed by a GPL source, describing what was learned rather than copied. |
| **Scope.** This document describes a lot of software. | High | The phase boundaries are real. Phase 2 alone is already useful; Phase 3 alone already beats pcb2gcode at the thing that prompted this project. Ship those before touching Phase 6. |
| **Post-processor dialect variability.** Every firmware fork accepts slightly different G-code. | Low | Template-driven posts plus a corpus of known-good output per dialect. Far smaller risk than it would be if we drove the machines — we only have to emit text a sender will accept, not maintain a live protocol. |

## 4. Open questions

1. ~~**License for PCB_MillBurn itself?**~~ **Answered: MIT** — attribution only, no restrictions
   on who ships it. Every dependency is permissive too, so there is no copyleft in the graph. See
   [01 §9](01-Architecture.md#9-licensing-strategy) and `THIRD-PARTY-NOTICES.md`.
2. ~~**UI shell: Avalonia, WPF, or stay on MAUI?**~~ **Answered: Avalonia**, settled by the Phase 0
   spike exactly as [07](07-UI-Framework-Decision.md) proposed — the viewport sustained 98 fps on
   500,247 segments, which was the acceptance test. The app targets plain `net10.0` and the Linux
   build is published and run.
3. ~~**Which laser controller(s) do you actually have?**~~ **Answered:** it does not matter, because
   laser output is SVG. Still worth knowing *which laser software* — LightBurn's palette mapping is
   the one target-specific thing in the export, and the shipped layer preset should match it.
4. ~~**Is there a touch probe on the mill?**~~ **Answered: yes.** Probe routines have been
   generated, run, and their logs imported to level real boards, so the generated-probe path was
   built early and is the default alignment recommendation. It also means the fiducial fallback
   ladder in [04 §4.2](04-Machines-Laser-and-Mixed-Workflows.md#42-fiducials--measurement-mill) can
   start at its numeric rung rather than at a microscope.
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
6. ~~**Which sender(s) do you use?**~~ **Answered: Universal G-code Sender**, on a Monport
   controller running GRBL 1.1f. That is why UGS's console format is the probe-log dialect that
   works today, why `$I` and `$$` are read out of a pasted console rather than from a file, and why
   Candle's and bCNC's grid-matrix formats are still waiting on a real example — a parser written
   from a memory of a format is worse than none. (The app itself never talks to a machine —
   [01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-writes-files-it-does-not-drive-machines).)
