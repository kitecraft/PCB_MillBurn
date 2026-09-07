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
board with aperture macros and negative polarity.

**Progress.** Parsers done; geometry realisation and the viewer are next.

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

### Phase 4 — Laser

- Laser machine profile, `M4` dynamic power, GRBL laser dialect.
- Raster scanline fill with overscan and bidirectional scan-offset compensation.
- Vector outline passes, multi-pass power/feed.
- Kerf and etch-bias compensation.
- Pad selection from X2 attributes; mask-open operation.
- Calibration generators (spot size, power/feed matrix, scan offset, focus).
- LightBurn SVG/DXF export.

**Done when:** a mask-removal job runs on the laser and the etched result is dimensionally within
one etch-bias unit of nominal.

### Phase 5 — Jobs, setups, alignment

- Job / Step / Setup / Fixture model; automatic insertion of fiducial and alignment steps.
- Fiducial generation (drilled holes, copper crosses, engraved marks) with survivability rules
  per workflow.
- Kabsch/affine fit from typed-in measurements, residual reporting, transform baked in as a
  processor. **No serial code** — see [01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-does-not-drive-machines).
- Paste-friendly alignment entry (accepts `X12.345 Y67.890`, TSV, or a pasted GRBL status line).
- Probe-routine *generator* + probe-log *importer* (UGS surface scanner, Candle heightmap, bCNC,
  plain CSV).
- Height mapping with TPS interpolation, reused across a Setup.
- Dry-run verification file generator.
- Registration plate generator + one-time machine calibration.
- Nest-pocket generator with asymmetric keying.
- Job Runbook with persistence and PDF export.

**Done when:** Use Case 1 and Use Case 2 both run end-to-end with ≤ 50 µm registration measured
on a test coupon.

### Phase 6 — Polish and reach

- Material-removal simulation as a first-class view and test oracle.
- Rest machining / multi-tool bulk clearing.
- Trochoidal pocketing.
- Panelisation.
- Additional posts: grblHAL, FluidNC, LinuxCNC, Mach3, Marlin-laser.
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

1. **License for PCB_MillBurn itself?** MIT/Apache-2.0 maximises adoption; GPL-3.0 keeps
   derivatives open. Decide before the first public commit — it is much harder later.
2. **UI shell: Avalonia, WPF, or stay on MAUI?** Recommendation and reasoning in
   [07](07-UI-Framework-Decision.md); settled by the Phase 0 spike.
3. **Which laser controller(s) do you actually have?** GRBL diode laser is assumed throughout. If
   there is a Ruida/LightBurn machine in the mix, the SVG/DXF export path moves from Phase 4 to
   Phase 1 in importance.
4. **Is there a touch probe on the mill?** It changes the default alignment recommendation from
   microscope-crosshair to probe, and it makes the generated probe routines worth building early.
5. **Single board or panels?** Panelisation is cheap to add early if the geometry layer knows
   about it from the start, and expensive to retrofit.
6. **Which sender(s) do you use?** It determines which probe-log formats to import first and which
   G-code dialect quirks to prioritise. (The app itself never talks to a machine —
   [01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-does-not-drive-machines).)
