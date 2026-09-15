# 08 — Requirements Matrix

Every requirement stated in documents 01–07, traced to what exists in the tree.

**Status is derived from the code, not from the roadmap's own labels.** That distinction is the
whole point of this document: a phase can be marked done while carrying an open item, a section can
describe machinery in the present tense that was never built, and neither shows up until somebody
reads the documents against `src/`. The first audit that did so found eleven such statements.

Keep it updated as work lands. A row that moves to **Done** should name the evidence — a type, a
test, a measurement — so that "done" means something a reader can check.

| Status | Means |
|---|---|
| **Done** | In the tree and exercised by a test |
| **Partial** | Some of it exists; the row says which part |
| **Not started** | No code |
| **Superseded** | Replaced by something better, or deliberately dropped. The reason is the useful half |

**Last audited:** 2026-09-12, at commit `7985840`; the eleven corrections it found landed in
`7235387`.

| Section | Rows | Done | Partial | Not started | Superseded |
|---|---|---|---|---|---|
| 01 Architecture | 15 | 8 | 0 | 7 | 0 |
| 02 Gerber & geometry | 22 | 15 | 0 | 6 | 1 |
| 03 Optimization | 10 | 7 | 1 | 1 | 1 |
| 04 Machines & laser | 29 | 9 | 2 | 18 | 0 |
| 05 Viewer & export | 16 | 6 | 3 | 6 | 1 |
| **Requirements** | **92** | **45** | **6** | **38** | **3** |

Plus 9 acceptance criteria (4 met, 1 met so far, 2 never measured, 2 superseded) and 11 physical
capabilities (5 proven, 2 wanting a re-run, 4 never cut).

**The shape of that table is the finding.** Documents 01–03 are largely built; document 04 is 17
not-started rows out of 27, nearly all of them the alignment and Job-model half of Phase 5. That
one block is most of what is left.

---

## 01 — Architecture

| ID | Requirement | Source | Status | Evidence |
|---|---|---|---|---|
| A1 | Writes files, never drives a machine — no serial, no jogging, no streaming | §1.1 | **Done** | no I/O port code anywhere in `src/` |
| A2 | `src/` + `tests/` layout, strictly downward dependency graph | §2 | **Done** | 13 projects; `MillBurn.Mcp` declared, not built |
| A3 | Permissive dependencies only; no copyleft in the graph | §3, §9 | **Done** | `THIRD-PARTY-NOTICES.md` |
| A4 | Incremental pipeline: memoised stages keyed by structural hash | §4 | **Not started** | no `PipelineCache` type exists |
| A5 | Cancellable: one `CancellationTokenSource` per edit, UI never blocks | §4 | **Not started** | no `CancellationToken` in `src/` |
| A6 | Debounce/coalesce window on slider drags | §4 | **Not started** | — |
| A7 | Progressive reveal: paths drawn before the optimizer finishes | §4 | **Not started** | — |
| A8 | Determinism: no unseeded RNG, no hash-order iteration | §4 | **Done** | `DeterminismTests`, 8 cases |
| A9 | Project model: `.millburn` file, refresh from source | §5 | **Done** | `Project.cs`, `ProjectRefresh.cs` |
| A10 | Pipeline work on the thread pool via `Task.Run` | §7 | **Not started** | 0 `Task.Run` in `MillBurn.App` |
| A11 | Electrical DRC: connected components vs the X2 netlist | §8 | **Not started** | — |
| A12 | Material simulation as preview and test oracle | §8 | **Not started** | also listed in Phase 6 |
| A13 | G-code round trip: emit → parse → backplot → compare | §8 | **Done** | `BackplotTests`, `MirroredBackplotTests` |
| A14 | Golden files over the corpus, deterministic | §8 | **Done** | 13 golden tests, 5 snapshots |
| A15 | MIT licence, notices shipped in the binary | §9 | **Done** | `LICENSE`, Help ▸ About |

## 02 — Gerber & geometry pipeline

| ID | Requirement | Source | Status | Evidence |
|---|---|---|---|---|
| G1 | Keep the semantics: X2 attributes carried through, not rasterised away | §1 | **Done** | both `%TF%` and KiCad 9 `G04 #@!` encodings |
| G2 | RS-274X + X2: apertures, full macro evaluator, polarity, arcs, step & repeat | §2 | **Done** | all 21 macro primitives; 24 corpus files, zero errors |
| G3 | Block apertures `%AB%`, aperture transforms `%LM/LR/LS%` | §2 | **Not started** | reported as errors, never silently ignored |
| G4 | Excellon: zero-suppressed dialects, G85 slots, routed slots, plating | §2 | **Done** | `ExcellonParserTests`, `GerberDrillTests` |
| G5 | Job file (`.gbrjob`) read for board-level metadata | title "X2/X3" | **Not started** | present in every KiCad export, unread |
| G6 | Layer auto-detection from `.FileFunction`, filename only as a labelled fallback | §3 | **Done** | `LayerRoles`; `AllLayerRolesTests` |
| G7 | Geometry ops: offset, boolean, area, inversion, canonical form | §4 | **Done** | `Polygons.cs`; canonical-form golden test |
| G8 | Voronoi-based isolation as an alternative to offsets | 01 §2 | **Not started** | was named in the layout table only; now removed from it |
| G9 | Isolation milling, multi-pass, width-driven rather than lap-driven | §5 | **Done** | `IsolationWidthTests` |
| G10 | Pocketing / area clearing | §5 | **Done** | `PocketOperation.cs`, used for mask relief |
| G11 | Drilling and routing, pecking, per-bit programs | §6 | **Done** | `DrillGuide` + companion HTML |
| G12 | Slots routed rather than refused | §6 | **Done** | `SlotOperation`; 3 of the Uno's 7, the rest refused by name |
| G13 | Board outline: every closed profile, inner pieces before the frame | §7 | **Done** | `PanelOrderTests`, `OutlineSideTests` |
| G14 | Tolerance-driven flattening, never a fixed segment count | §4 | **Done** | `Tessellate`, 1 µm sagitta |
| G15 | Panelisation / array generation | 06 §4 Q5 | **Superseded** | reasoned decision; recorded in the user FAQ |
| G16 | A cutter chosen from the library rather than synthesised | 06 §5.5.1 | **Done** | `ToolChooser`; widest that fits and reaches |
| G17 | Mill-drill: a hole too big for any drill, spiralled out | 06 §5.5.3 | **Done** | `SlotOperation.Holes`; off unless the project asks |
| G18 | Job-level options, carried in the project | 06 §5.5.3 | **Done** | `JobOptions`; Project info, and `--mill-holes` on the CLI |
| G19 | A companion page for the routing program | workshop | **Done** | `RoutingGuide`; names the cutter, and what will not be cut |
| G20 | A numbered picture of the holes and slots on each companion page | 06 §6.5 | **Not started** | inline SVG; refusals hatched, which is the point |
| G21 | Tool library: filter by kind, sort, copy a tool | 06 §6.6 | **Not started** | a scannable row may be worth more than all three |
| G22 | Coachmarks and a first-run walkthrough | 06 §6.7 | **Not started** | needs research; the walkthrough wants the test board to point at |

## 03 — Toolpath optimization

| ID | Requirement | Source | Status | Evidence |
|---|---|---|---|---|
| O1 | Generalized TSP with entry sets — both ends of every candidate considered | §2 | **Done** | `RouteOptimizer`; reversal of open runs |
| O2 | Trapezoidal cost model: acceleration, junction deviation, separate Z rate | §3 | **Done** | `MotionPlanner`, `MachineProfile` |
| O3 | Precedence constraints: inner pieces before the frame, depth stacks | §5 | **Done** | `Group` / `Stack` on `ToolpathPass` |
| O4 | Three-position budget control, same result every time | §6 | **Done** | budget is moves examined, not milliseconds |
| O5 | Do not lift when the link stays out of keep-out geometry | §7.1 | **Done** | `PassLinker`; shipped as 6.3, 13/17 links on a real board |
| O6 | Eulerian path merging across the containment tree | §7.2 | **Not started** | the last open Phase 3 item |
| O7 | Douglas–Peucker + G2/G3 arc fitting under a hard tolerance | §7.3 | **Done** | 301,097 → 15,191 lines on a panel, 2 µm bound |
| O8 | Benchmark against pcb2gcode on the corpus as a CI gate | §8 | **Superseded** | gate compares against MillBurn's own NN baseline |
| O9 | Zero precedence violations, asserted in the golden tests | §8 | **Done** | `PanelOrderTests` |
| O10 | Local search never makes travel worse than the baseline | §8 | **Partial** | cycling on open runs bounded, not fixed |

## 04 — Machines, laser & mixed workflows

| ID | Requirement | Source | Status | Evidence |
|---|---|---|---|---|
| M1 | Two outputs: G-code for the mill, SVG for the laser | §1 | **Done** | both paths ship |
| M2 | Mill profile: rates, acceleration, junction deviation, Z traverse | §1.1 | **Done** | read from a pasted `$$` dump |
| M3 | Laser profile: spot size, page, palette | §1.2 | **Partial** | `SvgProfile` has flavour + spot; no material library |
| M4 | Pad selection from X2 attributes for pad-only burns | §2.1 | **Not started** | attributes parsed; not used for selection |
| M5 | Kerf and etch-bias compensation stay with the laser software | §2.2 | **Done** | decision, documented and held |
| M6 | SVG says fill versus line per layer | §2.3 | **Partial** | single-layer mode and inversion exist |
| M7 | Calibration generators: registration repeatability coupon | §2.4 | **Not started** | mill-side test cuts exist; laser-side does not |
| M8 | Silkscreen marking straight from parsed strokes | §2.5 | **Done** | `SilkscreenOperation`; verified at 1:1 |
| M9 | Job / Step / Setup / Fixture model | §3 | **Not started** | `JobBuilder` builds one job, not a workflow |
| M10 | Use Case 1 (etch-resist then mill) end to end | §3 | **Not started** | Phase 5 "done when" — unmet |
| M11 | Use Case 2 (mill, mask, outline, laser pads) end to end | §3 | **Not started** | Phase 5 "done when" — unmet |
| M12 | Corner-stop fixture generator — the recommended default | §4.1 | **Not started** | — |
| M28 | The blank: grown from the board, or stated outright and cut or declared | 06 §5.6.3 | **Done** | `Blanks`, `BlankOperation`; work zero, page and mirror axis all follow it |
| M29 | Laser verification: burn into the blank's border and measure | 06 §5.6.5 | **Not started** | the mill defines its datum; the laser only trusts one |
| M13 | "Square the stock" operation | §4.1 | **Not started** | also the bridge from a declared blank to a known one |
| M14 | Dowel-plate generator and one-time machine calibration | §4.1 | **Not started** | the method is documented in the user FAQ |
| M15 | Nest-pocket generator with asymmetric keying | §4.1 | **Not started** | — |
| M16 | Fiducial generation with per-workflow survivability rules | §4.2 | **Not started** | `ToolpathKind.Fiducial` exists as an enum value |
| M17 | Kabsch/affine fit from typed measurements, residual reported | §4.2 | **Not started** | `MillBurn.Align` holds maps and levelling only |
| M18 | Laser registration by placement, not coordinates | §4.3 | **Not started** | — |
| M19 | Refuse to export above the residual threshold without an override | §4.4 | **Not started** | — |
| M20 | Alignment records persisted with the project | §4.5 | **Not started** | — |
| M21 | Height mapping: generate the probe routine, import the sender's log | §5 | **Done** | `ProbeRoutine`, `ProbeLog`, `Leveller` |
| M22 | Refuse a log that is not a probe log, and say why | §5 | **Done** | `ProbeLogRejectionTests`, five refusal shapes |
| M23 | Fit degrades rather than failing: 3 points a surface, 2 a tilt, 1 an offset | §5 | **Done** | `HeightMapTests` |
| M24 | A map is never saved into the project | §5.2 | **Done** | deliberate; tested |
| M25 | Candle and bCNC grid-matrix log formats | Phase 5 | **Not started** | deliberately waiting on a real file |
| M26 | Reuse one height map across a Setup's operations | Phase 5 | **Not started** | — |
| M27 | Additional mill posts: grblHAL, FluidNC, LinuxCNC, Mach3 | Phase 6 | **Not started** | `MillBurn.Post` is an empty project with Scriban wired |

## 05 — Viewer & export

| ID | Requirement | Source | Status | Evidence |
|---|---|---|---|---|
| V1 | Two data sources, one renderer: toolpath and parsed-back G-code | §2.1 | **Done** | caught the mirrored-backplot bug |
| V2 | SkiaSharp rendering with level of detail and spatial culling | §2.2 | **Done** | 98 fps on 500,247 segments |
| V3 | Independently toggleable layers, including ones no file produces | §2.3 | **Done** | layer panel with counts and swatches |
| V4 | Timeline scrubber over estimated time; play, pause, step | §2.4 | **Not started** | — |
| V5 | Progressive reveal: solid before the cursor, ghosted after | §2.4 | **Not started** | — |
| V6 | Bidirectional selection: hover a segment, click a G-code line | §2.5 | **Not started** | no tooltip or hit-test in either view |
| V7 | Before/after comparison — optimizer off vs on, side by side | §2.6 | **Not started** | the numbers are reported in the summary instead |
| V8 | Live stats readout: distances, lifts, per-tool time | §2.4 | **Done** | status bar and export summary |
| V9 | Structured SVG: real mm, named layers, `evenodd` holes, deterministic | §3.2 | **Done** | `SvgWriter`; byte-identical across runs |
| V10 | One page origin and size shared by every export in a job | §3.2 | **Done** | `SvgPage`; asserted in the golden tests |
| V11 | LightBurn palette mapping and a shipped layer preset | §3.3 | **Partial** | palette mapping done; no preset file ships |
| V12 | DXF (R12 polyline) as the second laser flavour | §3.4 | **Not started** | `MillBurn.Export` holds SVG only |
| V13 | PNG at a specified DPI | §3.4 | **Partial** | `--png` renders; DPI is not a parameter |
| V14 | PDF for the documentation profile | §3.4 | **Not started** | — |
| V15 | Gerber/Excellon round-trip of the modified board | §3.4 | **Superseded** | its purpose was panelisation, which was dropped |
| V16 | Job Runbook: printable checklist, persisted state, PDF export | §4 | **Partial** | `ProjectPage` covers the order and the caveats; no state, no PDF |
| V17 | A stroked area layer is drawn closed | 06 §6.8 | **Done** | `ClosedRings` split from `Outlined`; `ClosedRingTests` |
| V18 | Open recent, off the File menu | 06 §6.9 | **Not started** | `AppSettings.RecentProjects` is already kept; nothing shows it |
| V19 | Drill hits drawn as an X, with their own toolpath toggle | 06 §6.10 | **Not started** | plunges are counted, never drawn; `G81`/`G83` are not interpreted at all |

## 06 §2 — Cross-cutting acceptance criteria

| ID | Metric | Target | Status | Measured |
|---|---|---|---|---|
| C1 | Viewer frame rate, 500k segments | ≥ 60 fps | **Met** | 98 fps |
| C2 | Output determinism | byte-identical across runs | **Met** | 8 determinism tests |
| C3 | G-code line count | ≥ 5× reduction | **Met** | ~20× on a panel |
| C4 | Rapid travel, outline operations | below the NN baseline | **Met** | gated per board |
| C5 | Estimated cut time vs pcb2gcode | ≥ 20% reduction | **Superseded** | pcb2gcode has never been run |
| C6 | Optimizer, Balanced, 5000 paths | < 500 ms | **Superseded** | budget is steps; ms *was* the non-determinism bug |
| C7 | Preview latency, parameter change to redraw | < 200 ms | **Not measured** | depends on A4/A5, which do not exist |
| C8 | Time estimate vs wall clock | the actual falls inside the bracket | **Met so far** | 4 of 4; the bracket is ±3× and wants narrowing |
| C9 | Cross-machine registration | ≤ 50 µm | **Not measured** | needs M9–M20 |

## 07 — Physically verified on a machine

Not a document's requirements — the capabilities a cut board has actually tested. It belongs here
because "done" in every table above means *the code exists and a test passes*, and that is a
weaker claim than this one.

| ID | Capability | Evidence | Status | Next |
|---|---|---|---|---|
| P1 | Laser SVG at true 1:1 | 66-up panel, 0.061% over 165 mm | **Proven** | — |
| P2 | Isolation milling with a V-bit | PogoTest1, two jobs | **Proven** | — |
| P3 | Test cuts calibrating tip width and angle | tip 0.127 → 0.11 measured | **Proven** | — |
| P4 | Probe routine and levelled program | 0.011 mm of bow followed | **Proven** | — |
| P5 | Outline cut-out with tabs | 4 tabs, after the split fix | **Re-test** | fix verified in G-code, not yet on metal |
| P6 | Arcs surviving levelling | confirmed in a second controller's viewer | **Proven** | — |
| P7 | Links cut at depth between laps | 13 links, copper intersection zero | **Re-test** | first change that moves sideways at depth |
| P8 | Drilling with tool changes | — | **Untested** | three `M6` stops on MyGerbers2 |
| P9 | Double-sided and its mirror | — | **Untested** | scrap board with four pads |
| P10 | Soldermask relief | — | **Untested** | needs cured mask on the stock |
| P11 | Panel channel, one pass down the middle | — | **Untested** | MyGerbers3 |

---

## Queue order

The phases as [06](06-Roadmap-and-Risks.md) declares them, against where the work actually went.

| Phase | Declared | Actual |
|---|---|---|
| 0 Foundation | — | **Done** |
| 1 Read and draw a board | — | **Done** |
| 1.5 Projects | — | **Done** |
| 2 Mill toolpaths, G-code, backplot | — | **Done** |
| 3 The optimizer | — | **Done**, O6 open |
| 4 Laser output (SVG) | in progress | **Parked** — M4, V11, V12, M7 outstanding |
| 5 Jobs, setups, alignment | started | **Partial** — levelling done, M9–M20 untouched |
| 5.5 Drilling, finished | scheduled | **Done — jumped the queue** |
| 5.6 The blank ("stock" in the app) | scheduled | **Built** — 5.6.5 laser verification open |
| 6.1 Rulers | scheduled | **Not started** |
| 6.2 Climb or conventional | scheduled | **Not started** |
| 6.3 Staying down between passes | scheduled | **Done — jumped the queue** |
| 6.4 Tabs: where, how many, how big | scheduled | **Not started** |
| 6.5 A picture on the companion pages | scheduled | **Not started** |
| 6.6 Tool library: filter, sort, copy | scheduled | **Not started** |
| 6.7 Coachmarks and a first-run walkthrough | scheduled | **Not started** — needs research |
| 6.8 The viewer's gap in every outline ring | workshop | **Done** |
| 6.9 Open recent | workshop | **Not started** — the list is already kept |
| 6.10 Drill hits drawn as an X | workshop | **Not started** |
| 7 User documentation | started | **Partial** — generated reference sections not built |
| 8 MCP server | scheduled | **Not started** |
| 9 Solder paste | scheduled | **Not started** |

**Six items jumped, and every one was pulled in by a board that could not otherwise be cut.** That
is the right reason, and the risk table in [06 §3](06-Roadmap-and-Risks.md#3-risks) already names
its cost — *"Ship those before touching Phase 6"*. Besides 6.3, the five are all inside Phase 5 and
so do not appear as phases of their own: **test cuts**, the **machine profile read from a `$$`
dump**, the **drilling companion page**, **isolation width**, and **custom start/end G-code**.

6.3 is a special case worth recording: it was already specified as
[03 §7.1](03-Toolpath-Optimization.md#7-beyond-ordering), a *Phase 3* item, so building it closed
an old gap rather than pulling new scope forward — and it revealed that Phase 3 had been marked
done while carrying it.

---

## What this argues for

**The front of the queue has drifted.** Phase 4 has four open bullets and Phase 5 has twelve, and
the alignment half of Phase 5 — the Job model, fiducials, fixtures — is the single largest block of
unbuilt specification in these documents. It is also what `C9` and both headline use cases depend
on, so nothing downstream of it can close while it sits there.

**Phase 5.5 before Phase 6.** Drilling with tool changes is the largest untested path on a machine
that is now known to work, the board fixture already exists, and `P8` is the only untested
capability that needs no new code — just a run.
