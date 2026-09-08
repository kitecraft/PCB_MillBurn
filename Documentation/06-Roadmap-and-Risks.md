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

### Phase 2 — Mill toolpaths + G-code + backplot

- Clipper2 offsets; isolation passes; V-bit effective-diameter model; minimum-clearance DRC.
- Drilling with grouping and peck.
- Outline with tabs, containment tree, ramped entry.
- Naive ordering (nearest-neighbour) — deliberately *not* the good optimizer yet.
- G-code emitter + processor chain + GRBL post.
- G-code parser and **backplot viewer** with travel-move layer and stats.

**Done when:** a board goes Gerber → G-code → viewer, and the numbers in the stats panel match
what the machine actually does. This is the first genuinely useful build.

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
5. **Single board or panels?** Panelisation is cheap to add early if the geometry layer knows
   about it from the start, and expensive to retrofit.
6. **Which sender(s) do you use?** It determines which probe-log formats to import first and which
   G-code dialect quirks to prioritise. (The app itself never talks to a machine —
   [01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-writes-files-it-does-not-drive-machines).)
