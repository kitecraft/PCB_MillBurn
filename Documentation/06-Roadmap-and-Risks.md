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

### Phase 2 — Mill toolpaths + G-code + backplot

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

### Phase 3 — The optimizer

- GTSP model with entry-configuration sets, closed-loop free start.
- Trapezoidal time cost model.
- Greedy + 2-opt + Or-opt + configuration flip, with candidate lists and don't-look bits.
- Precedence DAG.
- Eulerian path merging; travel-at-depth when safe.
- Douglas–Peucker simplification + G2/G3 arc fitting.
- Benchmark harness vs. pcb2gcode; before/after comparison view.

**Done when:** the acceptance metrics in [03 §8](03-Toolpath-Optimization.md#8-acceptance-criteria)
are met on the corpus and enforced in CI.

### Phase 4 — Laser output (SVG)

Laser output is **SVG, not G-code** ([04 §1](04-Machines-Laser-and-Mixed-Workflows.md#1-two-machines-two-output-formats)),
which deletes most of what this phase used to contain: no laser dialect, no scanline fill, no
overscan, no power model. What is left is geometry and packaging.

- Structured SVG writer: real mm units, named layers, CSS classes, `evenodd` holes, deterministic
  output. **Shared with the mill-side documentation exports**, so it is written once.
- Silkscreen → SVG: centrelines straight from the parsed strokes. Needs no geometry realisation
  at all, so it lands first.
- Pad selection from X2 attributes; copper inversion against the outline; mask-open regions.
- Kerf and etch-bias compensation as signed Clipper2 offsets, reported numerically on export.
- LightBurn palette mapping and a shipped layer preset; DXF as a second flavour.
- One page origin and size shared by every export in a Job, asserted in the golden tests.
- Calibration generators: kerf comb, registration repeatability.

**Done when:** an exported mask job imports into LightBurn on the right layers at 1:1 scale, and
the etched result is dimensionally within one etch-bias unit of nominal.

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

Still to come in this phase: copper inversion and pad selection, kerf and etch-bias offsets, the
LightBurn layer preset, DXF, and the calibration generators.

### Phase 5 — Jobs, setups, alignment

- Job / Step / Setup / Fixture model; automatic insertion of fiducial and alignment steps.
- Fiducial generation (drilled holes, copper crosses, engraved marks) with survivability rules
  per workflow.
- Kabsch/affine fit from typed-in measurements, residual reporting, transform baked in as a
  processor. **No serial code** — see [01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-writes-files-it-does-not-drive-machines).
- Paste-friendly alignment entry (accepts `X12.345 Y67.890`, TSV, or a pasted GRBL status line).
- Probe-routine *generator* + probe-log *importer* (UGS surface scanner, Candle heightmap, bCNC,
  plain CSV).
- Height mapping with TPS interpolation, reused across a Setup.
- Dry-run verification file generator.
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

### Phase 6 — Polish and reach

- Material-removal simulation as a first-class view and test oracle.
- Rest machining / multi-tool bulk clearing.
- Trochoidal pocketing.
- Panelisation.
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

Anything not built is labelled as such on the page rather than described as if it worked. The
missing dry-run generator is called out on the landing page, because until it exists the first run
of any program is into copper.

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
5. ~~**Single board or panels?**~~ **Asked about, not yet needed.** Panelisation stays in Phase 6.
   The geometry layer already makes the array half cheap — copies are a transform over geometry
   that is already realised — and mouse-bites are close to the outline tabs that exist. What is
   genuinely not free is per-instance identity in the UI, height mapping (across 200 mm the stock's
   flatness stops being ignorable), and the fact that isolation milling a panel multiplies both the
   run time and the cost of one broken bit by N. Recorded for the user in `Help/faq.html` so the
   trade is visible before someone asks for it.
6. **Which sender(s) do you use?** It determines which probe-log formats to import first and which
   G-code dialect quirks to prioritise. (The app itself never talks to a machine —
   [01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-writes-files-it-does-not-drive-machines).)
