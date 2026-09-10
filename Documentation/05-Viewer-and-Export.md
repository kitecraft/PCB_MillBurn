# 05 — Viewer & Export (SVG, DXF, PNG, PDF)

## 1. Viewer: what we can learn from UGS

`WorkingFolder/Universal-G-Code-Sender/` is checked out in this workspace and its visualizer is the most mature
open-source G-code viewer in the .NET-adjacent world. It cannot be used directly — it is
**Java + NetBeans Platform + JOGL, GPL-3.0** — but its *design* is worth copying wholesale.

### The parts worth studying

| UGS file | Idea to re-derive |
|---|---|
| `ugs-core/.../visualizer/LineSegment.java` | The backplot primitive: `{ first, second, lineNumber, isArc, isFastTraverse, isZMovement, feedRate, spindleSpeed }`. Note `spindleSpeed` — that is exactly the field that gives us **laser power colouring for free**, because on a laser `S` *is* power. |
| `ugs-core/.../visualizer/GcodeViewParse.java` | Streaming parse → segment list with running min/max extremes, `toObjReduxWithArcTolerance()` — arc flattening driven by a *tolerance*, not a fixed segment count. Correct choice. |
| `ugs-core/.../gcode/GcodeParser.java` + `GcodeState.java` | A proper modal-state G-code interpreter. Modal groups, current plane, absolute/incremental, units. This is fiddly and easy to get subtly wrong — worth reading before writing ours. |
| `ugs-core/.../gcode/processors/*` | `ArcExpander`, `MeshLeveler`, `RotateProcessor`, `TranslateProcessor`, `MirrorProcessor`, `SimplifyProcessor`, `BacklashCompensator`, `RunFromProcessor`, `CommandProcessorList`. The **chain-of-processors architecture** is exactly right and we adopt it (see [04 §1](04-Machines-Laser-and-Mixed-Workflows.md#1-machine-profiles)). Note that `RotateProcessor`+`TranslateProcessor` are precisely the "bake the fiducial transform into coordinates" approach argued for in [04 §4.2](04-Machines-Laser-and-Mixed-Workflows.md#42-fiducials--measurement). `MeshLeveler` is autolevelling as a processor. |
| `ugs-platform/ugs-platform-visualizer/.../renderables/` | The **renderable-layer model**: `GcodeModel`, `Grid`, `MachineBoundries`, `Tool`, `SizeDisplay`, `OrientationCube`, `Selection`, `MouseOver` — each an independently toggleable overlay. Copy this decomposition exactly; it is why UGS's viewer is extensible. |
| `.../GcodeLineColorizer.java` | Colour-by-attribute (rapid / feed / plunge / arc / spindle speed) as a pluggable strategy. |
| `.../RendererInputHandler.java` + `MouseProjectionUtils.java` | Screen→world unprojection for hover/click picking. |
| `.../JogToHereAction`, `SetWorkingCoordinatesHereAction` | **Not applicable** — we don't drive machines ([01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-writes-files-it-does-not-drive-machines)). Noted only to mark the boundary: everything in UGS that talks to a controller is out of scope for us. The offline half of its visualizer is what we want. |

**Licensing: read, do not port.** See [Architecture § 9](01-Architecture.md#9-licensing-strategy).

## 2. Our viewer

### 2.1 Two data sources, one renderer

1. **In-memory toolpath** — instant, fully annotated (tool, net, operation, DRC status), used
   during live editing.
2. **Parsed final G-code** — the actual emitted text, run through our own parser and backplotter.

Default to **rendering the parsed G-code**. This makes the viewer a genuine verification of the
output rather than a second rendering of the same intent — it catches post-processor bugs, unit
errors, modal-state mistakes, and transform errors, which are exactly the bugs that destroy
boards. Cross-reference back to the rich in-memory model via line-number mapping so we keep the
annotations. Provide a "show source toolpath" ghost overlay to diff the two; any divergence is a
bug and should be flagged.

This also means the viewer works on **imported G-code** from any source, which is a useful
product in its own right.

**Built.** `File ▸ Open G-code…`, a dropped `.nc`, or one on the command line. It needed no new
geometry — a scene with no layers in it and an extent taken from the program rather than from a
board outline was the whole of it, which is the dividend of having always drawn the emitted text.
It is also the only way to look at the files that are not the job: the `.dryrun.nc`, the
`.levelled.nc` and the `.probe.nc`.

### 2.2 Rendering: SkiaSharp

A custom Avalonia `Control` drawing through `ICustomDrawOperation` + `ISkiaSharpApiLeaseFeature`,
which hands us Avalonia's own GPU-backed `SKCanvas` — no intermediate bitmap, no per-frame copy.
Top-down 2D as the primary view, with an optional isometric 2.5D projection. Full 3D is not worth
it for PCB work — the Z range is 2 mm.

The scene model, level of detail, culling and Skia drawing live in **`MillBurn.Viewer`**, a plain
`net10.0` library with no UI dependency. Only the ~60-line draw operation and the input-handling
control are Avalonia-specific, which keeps the shell replaceable and lets the culling logic be
unit-tested.

Performance approach for large files (a dense isolation job on a panelised board runs to
hundreds of thousands of segments). **Built and measured in Phase 0** — `MillBurn.Viewer`:

- Batch segments into a small number of `SKPath` objects **grouped by style** (rapid / cut /
  plunge / per-tool / per-power-bucket), so we issue tens of draw calls, not hundreds of
  thousands.
- **Level of detail**: four Douglas–Peucker tiers, tolerance doubling each step. Pick the coarsest
  tier whose simplification error stays under half a screen pixel, so LOD is never visible. On the
  test scene this takes a fit-zoom frame from 500k segments to 94k.
- **Spatial culling**: bucket each tier into a coarse grid with one prebuilt path per (tile,
  style), and draw only tiles intersecting the viewport. Essential, because zooming *in* is the
  worst case — the finest tier gets selected while almost nothing is on screen.
- Target 60 fps pan/zoom on a 500k-segment file. **Measured: 98 fps** through the real render loop
  across a fit → 40× → fit sweep.

Three findings from actually measuring, all of which contradict a reasonable guess:

1. **Culling correctness dominates everything else.** A defect in the per-tile bounds accumulator
   (it treated a single-point rect as empty and reset, leaving tiles claiming territory back to
   the origin) made a 40× zoom frame cost **3.2 seconds**. The picture still looked perfectly
   correct. Fixing it alone took that frame to 2.5 ms. Culling bugs are invisible by inspection,
   which is why `MillBurn.Tests` asserts them and why frame cost is on screen in the status bar.
2. **A fine tile grid is worse than none.** At 48×48, a zoomed-in frame issued ~940 draw calls and
   measured 833 ms, against 5.9 ms with no tiling at all. Skia already rejects off-screen contours
   by bounds very cheaply; per-draw-call overhead is the thing to economise. 8×8 on the finest
   tier is the right order of magnitude.
3. **Paint settings barely matter once culling is right.** Antialiasing and geometric stroking
   together cost about 4%. Early numbers suggesting hairline strokes were 7× faster were an
   artefact of the bounds bug. The renderer keeps AA and real stroke widths, with a downgrade path
   retained only for files far past the design target.

`--probe` (a render-configuration sweep), `--bench` (offscreen CPU raster) and `--fpstest` (the
real GPU render loop) are all in the app so these numbers stay checkable rather than remembered.

If real 3D is ever needed, the escape hatches are Silk.NET into a WinUI `SwapChainPanel`, or the
MIT-licensed `gcode-preview` (three.js) in a WebView2. Neither is needed.

### 2.3 Layers (all independently toggleable)

Adopting UGS's renderable decomposition:

| Layer | Content |
|---|---|
| Gerber underlay | Copper / silk / mask at low opacity, so isolation can be visually verified |
| Toolpaths | Coloured by tool, by operation, by Z depth, or **by laser power** — switchable |
| Travel moves | Dashed, dimmed, toggleable. **This is the layer that makes the optimizer's work visible.** |
| Long-rapid highlight | Any rapid longer than *N* mm drawn in warning colour — instantly exposes the "edge cuts all over the place" problem, in the original *and* in ours |
| DRC violations | Red, with click-to-zoom into the Issues panel |
| Material simulation | The swept-tool result: "what the copper will look like after this runs" |
| Height map | Colour-mapped surface from an imported probe log |
| Fiducials & fixture | Marks, dowel pins, plate outline, current alignment transform |
| Machine bounds & grid | Work envelope, origin, axis indicator |
| Tool position | Animated during simulation |

### 2.4 Simulation and scrubbing

- Timeline scrubber over **estimated time**, not line number, using the trapezoidal profile from
  [03 §3](03-Toolpath-Optimization.md#3-the-cost-model). The simulation runs at true speed (and
  at 2×/10×/100×), so the progress bar and the ETA are honest.
- Play / pause / step-by-line / step-by-operation.
- Progressive reveal: everything before the cursor drawn solid, everything after ghosted.
- Live stats readout: cut distance, rapid distance, lift count, per-tool time breakdown,
  estimated total.

### 2.5 Bidirectional selection

- Hover a segment → tooltip with G-code line, tool, feed, power, net name, source operation.
- Click a segment → the G-code text pane scrolls and highlights; the Operations tree selects the
  producing operation; the Inspector shows the parameters that generated it.
- Click a G-code line → the geometry highlights and the view pans to it.
- Rubber-band select → statistics for the selection; "exclude this region" for quick fixes.

### 2.6 Before/after comparison

A dedicated mode that renders the **same job with the optimizer off vs. on**, side by side, with
the travel numbers. This is both a QA tool and the most convincing demo of the product's central
claim.

## 3. SVG export — the laser half of the product

This is not a side feature. **The laser takes SVG, not G-code**
([04 §1](04-Machines-Laser-and-Mixed-Workflows.md#1-two-machines-two-output-formats)), so the SVG
writer carries every laser operation the app performs. It has to be as correct as the G-code
emitter, and it is held to the same standard: exact units, deterministic bytes, golden tests.

### 3.1 What is wrong with pcb2gcode's SVG

From `WorkingFolder/pcb2gcode/src/svg_writer.cpp`:

```cpp
const unsigned int r = ConsistentRand::rand() % 256;
const unsigned int g = ConsistentRand::rand() % 256;
const unsigned int b = ConsistentRand::rand() % 256;
```

Every polygon gets a **random colour**. There are no groups, no ids, no classes, no layer
structure, no metadata, and the dimensions are computed in pixels at a hard-coded DPI. It is a
debug dump — genuinely useful for eyeballing whether the geometry is sane, and useless for
anything else. It cannot be imported into a laser workflow, printed to scale, or diffed
meaningfully.

Note the specific irony: random colour is the *worst possible* choice for a laser target, because
colour is exactly how LightBurn decides which cut layer an imported object belongs to.

### 3.2 The replacement

**Real units.** `width="100mm" height="80mm" viewBox="0 0 100 80"` so it opens at exactly 1:1 in
Inkscape, LightBurn, Illustrator, and a browser. Never DPI-dependent pixels. A scale error here is
a scrapped board, so the golden tests assert the header, and the CLI prints the page size on
export.

**One page per Job.** Every SVG exported from the same Job shares an origin and a page size, taken
from the stock outline plus a documented margin — never cropped to each layer's own extents.
Otherwise two exports of the same board will not overlay when imported, and the second burn is
offset by the difference. This is asserted in the golden tests, not just intended.

**Structured layers**, with both `id` and `inkscape:label` so Inkscape shows sensible names:

```xml
<g id="copper-top"      inkscape:label="Copper Top"       class="copper"/>
<g id="isolation-p1"    inkscape:label="Isolation pass 1" class="tool-vbit"/>
<g id="mask-open"       inkscape:label="Mask openings"    class="burn-fill"/>
<g id="silkscreen"      inkscape:label="Silkscreen"       class="burn-line"/>
<g id="drills"          inkscape:label="Drills"           class="drill"/>
<g id="outline"         inkscape:label="Board outline"    class="outline"/>
<g id="registration"    inkscape:label="Registration"     class="fiducial"/>
```

**Styling: CSS for humans, explicit attributes for machines.** A `<style>` block keyed by class
makes the drawing re-themeable in one place and is right for the Inkscape and documentation
profiles. But **the LightBurn profile must write `stroke="#RRGGBB"` on every element**, because
layer assignment there is by stroke colour and importers vary in how much CSS they resolve.
Relying on a stylesheet to carry the layer identity is the kind of thing that works in the browser
preview and silently collapses everything onto one layer in the tool that matters. The writer
therefore takes a flag: emit style by class, or bake it per element.

**Closed subpaths, and `fill-rule="nonzero"` with explicit winding.** Anything intended to be
filled must close with `Z`, and holes must be subpaths of the same `<path>` as their outer contour.
The rule is **non-zero, with holes wound opposite to islands** — not even-odd. Even-odd makes a
hole a hole regardless of direction, which is why it is the tempting choice, but it also treats
*any* overlap as a hole: two shapes that touch punch a void where they cross. Non-zero gets holes
right and overlaps right, and it is what makes it safe to merge a whole board into one path.
See [04 §2.3](04-Machines-Laser-and-Mixed-Workflows.md#23-fill-versus-line-and-why-the-svg-must-say-which).

**Single-layer mode, for importers that make a layer per object.** Some laser software creates one
of its own cut layers for every imported object. On a board that means one layer per pad — hundreds
of them, each needing power and speed set by hand — which makes the file unusable. Creality Falcon
does this. `SvgExportOptions.SingleLayer` therefore emits one group with **no Inkscape layer
markup**, one colour, one filled path and one stroked path per width.

It is the exact opposite of what LightBurn wants, where colour *is* the layer assignment and
separate layers are the point, so the two are alternatives rather than defaults. The export report
prints the element and group count either way, because that is the number which decides what the
laser program will do and there is no way to tell by looking at the picture.

**Metadata.** Embed the project name, the source Gerber, the generation timestamp, and the
PCB_MillBurn version in `<metadata>` / `<desc>`, so a burn can be traced back to what made it. No
compensation figures: the geometry is the design's own, and any kerf offset is applied downstream by
the laser software ([04 §2.2](04-Machines-Laser-and-Mixed-Workflows.md)). What the file should be
able to tell somebody six months later is *which design and which version*, not what we did to it,
because we do nothing to it.

**Deterministic and diffable.** Stable ordering, fixed decimal precision, no randomness, invariant
formatting. Two runs of the same project produce byte-identical SVG — which is what makes it a
golden-test artefact rather than something a human has to eyeball.

### 3.3 Export profiles

| Profile | Purpose | Characteristics |
|---|---|---|
| **Silkscreen** | Laser marking a legend | Stroke centrelines only, mm units — the beam is already the right width ([04 §2.5](04-Machines-Laser-and-Mixed-Workflows.md#25-silkscreen-marking--the-easiest-win-on-the-board)). Needs no geometry realisation, so it ships first. |
| **LightBurn** | The main laser output | Per-element stroke colours matching the target palette, so objects land on the intended cut layers; fill geometry and line geometry separated; closed subpaths with `evenodd` holes; mm units; no styling the importer will discard. Ships with a layer preset. |
| **Inkscape / general** | Inspection, editing, documentation | Named layers, CSS classes, mm units |
| **Documentation** | Printed reference / assembly aid | Dimensioned, with registration callouts, a scale bar, a title block, tool/parameter table |
| **Interactive HTML** | Sharing and review | SVG plus a small JS layer-toggle panel, hover tooltips with net/tool/feed, and G-code line cross-links |

### 3.4 Other exports

- **DXF** (R12 polyline flavour — the most universally readable) as the second laser flavour, for
  software that imports DXF more reliably than SVG.
- **PNG** at a specified DPI, via SkiaSharp — for docs and forum posts.
- **PDF** for the documentation profile, so the runbook prints properly.
- **Excellon / Gerber round-trip** of the *modified* board (e.g. after panelisation), so the
  project can be sent to a fab as an alternative to cutting it.

## 4. The Job Runbook

A generated, printable, on-screen **step-by-step checklist** for the whole job, since these
workflows span days and two machines:

- Each step: which machine, which file, expected duration, the tool to fit, the alignment
  procedure, and a rendered picture of what that step produces.
- Manual steps get timers (etch time) and their own checkboxes.
- Job state persists — close the app mid-job, reopen tomorrow, it knows you are on step 4.
- Export to PDF so it can sit next to the machine on paper.

For a process with eight steps across two machines and an acid bath in the middle, the runbook is
not a nice-to-have; it is the difference between a repeatable process and a remembered one.
