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

**Brought up to date:** 2026-10-11, at 0.6.0, after sprint 5 — every row the sprint touched, not a
fresh audit of the rest. M6 and V43 done, V11 superseded, V32, M16 and M17 set aside with the reason in
each, and V37, V39 and V42 carrying what the sprint changed in them. A duplicated ID found on the
way (two rows called V40) is now refused by `MatrixSummaryTests`.

**Last audited:** 2026-10-10, after sprint 4 and the two fixes that followed it. **Three audits'
worth of work had landed without this document moving**, which is the longest it has ever been
left, and the rows said so plainly: G20, G21 and G23 all read *Not started* for features that
shipped in sprint 4, and sprint 3 — which was one item, 6.44, from end to end — had never been
recorded at all.

**What moved.** G20 (the numbered picture), G21 (the tool library) and G23 (the CLI commands) to
Done. Nine rows added for work that had no row: G24 tabs, M32 the copper test cut, and V37–V42 for
the findings view, unnamed copper, the rulers, the pointer readout, finding-to-copper and the
project's origin. Nothing moved the other way.

**Two errors predating sprint 4**, found by counting rather than by reading: the summary claimed
114 rows against 115, and 7 Partials against 8 — V11, V13, V16 and V34 are all Partial and only
three were counted. They have one cause, and it is the next paragraph.

**One row had been invisible to the test that counts them.** `MatrixSummaryTests` recomputes this
summary and fails the build when it drifts, and its row pattern wants a bare `**Status**` cell.
V34's read `**Partial** — backlash and squareness built`, with the qualifier outside the bold and
inside the cell, so the row matched nothing and was dropped from the arithmetic entirely — which is
why the summary said 114 rows and 7 Partials while the document held 115 and 8. The test's own
notes warn about this exact shape: *"a Parked row vanished from the arithmetic entirely"*. V34 now
carries its qualifier in the Evidence column like every other row, and a sweep for the pattern finds
no others.

**And two rows described a tree that no longer exists.** A2 said *"13 projects; `MillBurn.Mcp`
declared, not built"* — there is no such project and no solution entry for one, so the parenthetical
was describing an intention rather than the tree, which is the exact failure this document opens by
naming. C7's reason for being unmeasured was *"depends on A4/A5, which do not exist"*; both landed
in sprint 1 and the row was never revisited, so the only thing standing between that criterion and
a number is somebody measuring it.

**The roadmap-phases table lost its 6.x half.** It listed 6.1 to 6.24 by hand, duplicating 06, and
every error in this document that was not a missing row was in that block: 6.19 and 6.24 both read
*Not started* while V31 and V36 in this same file recorded them Done with evidence. The roadmap now
runs to 6.68, so maintaining that list by hand is a standing invitation to the rot it already
produced. It points at [11](11-Backlog.md) instead, which `BacklogTests` regenerates and fails the
build over.

The rest of this note is the 2026-09-20 audit it replaces: V34 moved to Partial as the first two
checks landed, V35 added for the About window, and the not-started rows were read through to pick
the five that sprint is made of (06, *The next sprint*).

**Last audited before that:** 2026-09-18, after 6.15 was requested. The previous audit fixed counts that had drifted rather than
rows — twelve rows added or changed after v0.1.0 while the summary still described a document of 92.
This one moves what 6.14 built: V26, V27 and V29 done, and with them M20 and the first halves of M16
and M17, the fiducial block of document 04 that had not been touched at all. V29 arrived from the
workshop while 6.14 was being written, and is the row that turned "what the alignment moves" from a
rule into a choice.

The 2026-09-19 pass adds M31, the stock's holes marked in every SVG for registering a burn, and
records 6.16 as parked.

The 2026-09-18 pass adds one row and builds nothing: G23, the companion page listing the commands
that would rebuild the export, asked for from the workshop as a head start for anyone moving from
the window to a scripted pipeline, and M30, a re-measured stock keeping the alignment holes it was
cut with — found by asking what the pre-cut correction does to them, which is discard them.

| Section | Rows | Done | Done — used on metal | Not started | Parked | Partial | Superseded |
|---|---|---|---|---|---|---|---|
| 01 Architecture | 15 | 13 | 0 | 2 | 0 | 0 | 0 |
| 02 Gerber & geometry pipeline | 24 | 18 | 0 | 3 | 1 | 1 | 1 |
| 03 Toolpath optimization | 10 | 8 | 0 | 0 | 1 | 0 | 1 |
| 04 Machines, laser & mixed workflows | 32 | 12 | 1 | 15 | 0 | 3 | 1 |
| 05 Viewer & export | 43 | 26 | 0 | 11 | 0 | 3 | 3 |
| **Requirements** | **124** | **77** | **1** | **31** | **2** | **7** | **6** |

**06 §2 Cross-cutting acceptance criteria** — 9 rows: 4 met, 1 met so far, 2 not measured, 2 superseded.

**07 Physically verified on a machine** — 17 rows: 1 partial, 13 proven, 3 untested.

**The shape of that table is the finding, and it has not changed.** Documents 01–03 are largely
built; document 04 is **15 not-started rows out of 32**, nearly all of them the alignment and
Job-model half of Phase 5. That one block is still most of what is left, and 6.14 was the first cut
into it: the stock's waste holes, a two-point rigid fit and a correction kept with the project are
M16, M17 and M20 in their smallest useful form. What those rows still want is fiducials in copper,
a fit from three or more points, and a residual threshold that refuses an export rather than
reporting it.

**Document 05 has grown fastest and is now the largest section** — 42 rows against document 04's
32, having been the smaller of the two at the first audit. All of that growth came from the
workshop: every row from V17 on came from a board being cut rather than from a document, and the
six added in this audit are no exception.

**Of the 31 rows still not started, 15 are in that one Phase 5 block.** The other 16 are spread
across the laser's second flavour, the MCP server, solder paste and the mill posts — four separate
pieces of new capability, none of which the workshop has asked for.

---

## 01 — Architecture

| ID | Requirement | Source | Status | Evidence |
|---|---|---|---|---|
| A1 | Writes files, never drives a machine — no serial, no jogging, no streaming | §1.1 | **Done** | no I/O port code anywhere in `src/` |
| A2 | `src/` + `tests/` layout, strictly downward dependency graph | §2 | **Done** | 13 under `src/`, 2 under `tests/`; `MillBurn.Post` is wired and empty. `MillBurn.Mcp` is **not** declared — it was named here as "declared, not built" and no such project or solution entry exists |
| A3 | Permissive dependencies only; no copyleft in the graph | §3, §9 | **Done** | `THIRD-PARTY-NOTICES.md` |
| A4 | Incremental pipeline: memoised stages keyed by structural hash | §4 | **Done** | `RealisedLayers.Get` keyed on bytes, role and options; stamps `Fingerprint`. Sprint 1 story 2 |
| A5 | Cancellable: one `CancellationTokenSource` per edit, UI never blocks | §4 | **Done** | `LatestRun`, `PreviewBuild`; one source per edit by design, not sprinkled. Sprint 1 story 1 |
| A6 | Debounce/coalesce window on slider drags | §4 | **Done** | Already satisfied, by the invalidation guard in `OnOutputChanged` rather than by a debounce: a drag rebuilds at most once whatever its length. Nothing plans on a drag at all. Traced, not built — sprint 2 story 6 |
| A7 | Progressive reveal: paths drawn before the optimizer finishes | §4 | **Not started** | — |
| A8 | Determinism: no unseeded RNG, no hash-order iteration | §4 | **Done** | `DeterminismTests`, 8 cases |
| A9 | Project model: `.millburn` file, refresh from source | §5 | **Done** | `Project.cs`, `ProjectRefresh.cs` |
| A10 | Pipeline work on the thread pool via `Task.Run` | §7 | **Done** | one `Task.Run` at the pipeline boundary in `MainViewModel`, which is the whole of the design. Sprint 1 story 1 |
| A11 | Electrical DRC: connected components vs the X2 netlist | §8 | **Done** | `ElectricalCheck` names the nets a cut cannot separate; confirmed at the bench against the board's own KiCad project. Sprint 1 story 4 |
| A12 | Material simulation as preview and test oracle | §8 | **Not started** | also listed in Phase 6 |
| A13 | G-code round trip: emit → parse → backplot → compare | §8 | **Done** | `BackplotTests`, `MirroredBackplotTests` |
| A14 | Golden files over the corpus, deterministic | §8 | **Done** | 13 golden tests, 5 snapshots |
| A15 | MIT licence, notices shipped in the binary | §9 | **Done** | `LICENSE`, Help ▸ About |

## 02 — Gerber & geometry pipeline

| ID | Requirement | Source | Status | Evidence |
|---|---|---|---|---|
| G1 | Keep the semantics: X2 attributes carried through, not rasterised away | §1 | **Done** | both `%TF%` and KiCad 9 `G04 #@!` encodings; carried *correctly* only since 06 §6.41 — a trace was filed under the next net |
| G2 | RS-274X + X2: apertures, full macro evaluator, polarity, arcs, step & repeat | §2 | **Done** | all 21 macro primitives; 24 corpus files, zero errors |
| G3 | Block apertures `%AB%`, aperture transforms `%LM/LR/LS%` | §2 | **Not started** | reported as errors, never silently ignored; split out of sprint 1 story 5 into 06 §6.45 — no fixture exists yet |
| G4 | Excellon: zero-suppressed dialects, G85 slots, routed slots, plating | §2 | **Done** | `ExcellonParserTests`, `GerberDrillTests` |
| G5 | Job file (`.gbrjob`) read for board-level metadata | title "X2/X3" | **Parked** | surveyed 2026-09-23: its roles duplicate `%TF.FileFunction`, its thickness is the design nominal not the stock — 06 §6.46 |
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
| G16 | A cutter chosen from the library rather than synthesised | 06 §5.5.1 | **Done** | `ToolChooser`; widest that fits and reaches. Was Done on the export path only — `mill` synthesised from its own 30°/0.1 mm defaults and ignored an edited library bit until 2026-09-28 (06 §6.42); all three callers now share `LayerOperations.DefaultToolFor` |
| G17 | Mill-drill: a hole too big for any drill, spiralled out | 06 §5.5.3 | **Done** | `SlotOperation.Holes`; off unless the project asks |
| G18 | Job-level options, carried in the project | 06 §5.5.3 | **Done** | `JobOptions`; Project info, and `--mill-holes` on the CLI |
| G19 | A companion page for the routing program | workshop | **Done** | `RoutingGuide`; names the cutter, and what will not be cut |
| G20 | A numbered picture of the holes and slots on each companion page | 06 §6.5 | **Done** | `GuideFigure`; inline SVG, every feature numbered in the order the program visits it, refusals hatched. `GuideFigureTests`. Sprint 4 story 3 |
| G21 | Tool library: filter by kind, sort, copy a tool | 06 §6.6 | **Done** | `ToolView.Arrange`, `ToolSort`, `ToolView.Copy`; a kind mark, name and size per row, with a confirm on delete and a name prompt on copy. `ToolViewTests`. Sprint 4 story 7 — and the scannable row did turn out to be worth more than the three, which the closure records |
| G22 | Coachmarks and a first-run walkthrough | 06 §6.7 | **Not started** | needs research; the walkthrough wants the test board to point at |
| G23 | The project page lists the CLI commands that would rebuild the export | 06 §6.15 | **Done** | `CliScript.For(plan, context)`, derived from the emitted plan rather than the settings, so it cannot name a flag the export ignored; `--commands` prints it alone. `CliScriptTests`. Sprint 4 story 4 |
| G24 | Tabs: placed where a hand would put them, sized from the board, and movable one at a time | 06 §6.4 | **Partial** | `TabPlan` centres one on each straight edge the profile has room for and sizes both numbers from the board; the tabs window lists every position and lets each be overridden. `TabPlacementTests`, `OutlineTabTests`. Sprint 4 story 1. **Dragging a tab on the board is not built** |

## 03 — Toolpath optimization

| ID | Requirement | Source | Status | Evidence |
|---|---|---|---|---|
| O1 | Generalized TSP with entry sets — both ends of every candidate considered | §2 | **Done** | `RouteOptimizer`; reversal of open runs |
| O2 | Trapezoidal cost model: acceleration, junction deviation, separate Z rate | §3 | **Done** | `MotionPlanner`, `MachineProfile` |
| O3 | Precedence constraints: inner pieces before the frame, depth stacks | §5 | **Done** | `Group` / `Stack` on `ToolpathPass` |
| O4 | Three-position budget control, same result every time | §6 | **Done** | budget is moves examined, not milliseconds |
| O5 | Do not lift when the link stays out of keep-out geometry | §7.1 | **Done** | `PassLinker`; shipped as 6.3, 13/17 links on a real board |
| O6 | Eulerian path merging across the containment tree | §7.2 | **Parked** | measured and not needed on any committed board; reopenable on a Gerber set that shows a gain — 03 §O6 |
| O7 | Douglas–Peucker + G2/G3 arc fitting under a hard tolerance | §7.3 | **Done** | 301,097 → 15,191 lines on a panel, 2 µm bound |
| O8 | Benchmark against pcb2gcode on the corpus as a CI gate | §8 | **Superseded** | gate compares against MillBurn's own NN baseline |
| O9 | Zero precedence violations, asserted in the golden tests | §8 | **Done** | `PanelOrderTests` |
| O10 | Local search never makes travel worse than the baseline | §8 | **Done** | a search that could apply 321,413 improvements and finish worse now settles in fifteen. Sprint 1 story 3 |

## 04 — Machines, laser & mixed workflows

| ID | Requirement | Source | Status | Evidence |
|---|---|---|---|---|
| M1 | Two outputs: G-code for the mill, SVG for the laser | §1 | **Done** | both paths ship |
| M2 | Mill profile: rates, acceleration, junction deviation, Z traverse | §1.1 | **Done** | read from a pasted `$$` dump |
| M3 | Laser profile: spot size, page, palette | §1.2 | **Partial** | `SvgProfile` has flavour + spot; no material library |
| M4 | Pad selection from X2 attributes for pad-only burns | §2.1 | **Not started** | attributes parsed; not used for selection |
| M5 | Kerf and etch-bias compensation stay with the laser software | §2.2 | **Done** | decision, documented and held |
| M6 | SVG says fill versus line per layer | §2.3 | **Done** | sprint 5 story 1: every colour comes from the role through `SvgProfile` — drawing C01, board outline C08, stock and holes C00, silk centrelines C03 — single-layer mode included, and each file's description names its layers and modes. `SvgWriterTests`, `ProjectPageTests`. Rendered, not yet imported into LightBurn on the bench |
| M7 | Calibration generators: registration repeatability coupon | §2.4 | **Not started** | mill-side test cuts exist; laser-side does not |
| M8 | Silkscreen marking straight from parsed strokes | §2.5 | **Done** | `SilkscreenOperation`; verified at 1:1 |
| M9 | Job / Step / Setup / Fixture model | §3 | **Not started** | `JobBuilder` builds one job, not a workflow |
| M10 | Use Case 1 (etch-resist then mill) end to end | §3 | **Not started** | Phase 5 "done when" — unmet |
| M11 | Use Case 2 (mill, mask, outline, laser pads) end to end | §3 | **Not started** | Phase 5 "done when" — unmet |
| M12 | Corner-stop fixture generator — the recommended default | §4.1 | **Superseded** | The generator was declined 2026-09-27 and the requirement met by `Help/guides/corner-stops.html` instead: the datum it would have stored is a work offset the controller already owns, and the geometry is a pocket with three islands anyone can draw once. Reachable from the contents and the Help menu, which `HelpPagesTests` enforces |
| M28 | The blank: grown from the board, or stated outright and cut or declared | 06 §5.6.3 | **Done** | `Blanks`, `BlankOperation`; work zero, page and mirror axis all follow it |
| M29 | Laser verification: burn into the blank's border and measure | 06 §5.6.5 | **Not started** | the mill defines its datum; the laser only trusts one |
| M30 | A re-measured (pre-cut) stock keeps the alignment holes it was cut with | 06 §6.16 | **Not started** | parked 2026-09-19: built on branch `005_StockKeepsItsHoles`, too hard to follow in the window |
| M31 | The stock's alignment holes on a layer of their own in every SVG, for registering a burn | 06 §6.17 | **Done — used on metal** | `ExportPlanner.ReferenceLayers`, `SvgReference`; board outline grey on C08 and stock and holes black on C00 since sprint 5 (M6), red and blue before; Settings › Laser › Placing layers: outline and stock, outline only, none; `ProjectPageTests` |
| M13 | "Square the stock" operation | §4.1 | **Not started** | also the bridge from a declared blank to a known one |
| M14 | Dowel-plate generator and one-time machine calibration | §4.1 | **Not started** | the method is documented in the user FAQ |
| M15 | Nest-pocket generator with asymmetric keying | §4.1 | **Not started** | — |
| M16 | Fiducial generation with per-workflow survivability rules | §4.2 | **Partial** | the stock's two waste holes (06 §6.14) are the milled case, cut and measured; fiducials in copper, with the rules about what survives each workflow, are not. **Set aside in sprint 5** with M17: the two waste holes already survive the workshop's workflow, and nothing has needed a third mark yet |
| M17 | Kabsch/affine fit from typed measurements, residual reported | §4.2 | **Partial** | `RigidFit.Solve`: the two-point rigid case, with the separation error as its residual. Three or more points, and the SVD, are not built. **Set aside in sprint 5** with M16 |
| M18 | Laser registration by placement, not coordinates | §4.3 | **Not started** | — |
| M19 | Refuse to export above the residual threshold without an override | §4.4 | **Not started** | — |
| M20 | Alignment records persisted with the project | §4.5 | **Done** | `ProjectSettings.Alignment` — shift, turn, pivot and the date found; `ProjectTests.TheDrillAlignmentIsSavedWithTheProject` |
| M21 | Height mapping: generate the probe routine, import the sender's log | §5 | **Done** | `ProbeRoutine`, `ProbeLog`, `Leveller` |
| M22 | Refuse a log that is not a probe log, and say why | §5 | **Done** | `ProbeLogRejectionTests`, five refusal shapes |
| M23 | Fit degrades rather than failing: 3 points a surface, 2 a tilt, 1 an offset | §5 | **Done** | `HeightMapTests` |
| M24 | A map is never saved into the project | §5.2 | **Done** | deliberate; tested |
| M25 | Candle and bCNC grid-matrix log formats | Phase 5 | **Not started** | deliberately waiting on a real file |
| M26 | Reuse one height map across a Setup's operations | Phase 5 | **Not started** | — |
| M27 | Additional mill posts: grblHAL, FluidNC, LinuxCNC, Mach3 | Phase 6 | **Not started** | `MillBurn.Post` is an empty project with Scriban wired |
| M32 | A test cut that finds where the copper stops on this piece of laminate | 06 §6.52 | **Done** | `TestCutKind.Copper`, `TestCutOptions.For`; twelve lines from 0.01 mm in 0.01 mm steps, spaced for the bit, with the step named in the header. `CopperTestCutTests`. Sprint 4 story 5. **Not yet cut on metal** |

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
| V11 | LightBurn palette mapping and a shipped layer preset | §3.3 | **Superseded** | palette mapping done (M6). The preset was built and checked on the bench in sprint 5, and declined: a LightBurn project with no shapes loads no layers, one with the geometry works, and keeping a second output format for software the workshop does not use is not worth it — 06 §6.69, with the format as LightBurn wrote it. The project page's *Laser layers* table says what each colour is, for any software that imports by colour |
| V12 | DXF (R12 polyline) as the second laser flavour | §3.4 | **Not started** | `MillBurn.Export` holds SVG only |
| V13 | PNG at a specified DPI | §3.4 | **Partial** | `--png` renders; DPI is not a parameter |
| V14 | PDF for the documentation profile | §3.4 | **Not started** | — |
| V15 | Gerber/Excellon round-trip of the modified board | §3.4 | **Superseded** | its purpose was panelisation, which was dropped |
| V16 | Job Runbook: printable checklist, persisted state, PDF export | §4 | **Partial** | `ProjectPage` covers the order and the caveats; no state, no PDF |
| V17 | A stroked area layer is drawn closed | 06 §6.8 | **Done** | `ClosedRings` split from `Outlined`; `ClosedRingTests` |
| V18 | Open recent, off the File menu | 06 §6.9 | **Done** | `MainWindow.OnRecentOpened`; `AppSettingsTests.TheSameProjectUnderTwoPathsIsListedOnce`, `ForgettingARecentProjectLeavesTheRestInOrder` |
| V19 | Drill hits drawn as an X, with their own toolpath toggle | 06 §6.10 | **Superseded** | KiCad's Gerber drill maps already mark every hole by size; the program preview still draws no mark where a drilling program plunges |
| V20 | A layer needing several bits is one file per bit, and its page lists them in order | 06 §6.11 | **Done** | `AssembleEach`; an `M0` stop leaves GRBL on hold, where it will not jog or probe |
| V21 | Choose one file per bit or one file with custom tool-change G-code | 06 §6.11 | **Not started** | for tool-change macros and automatic changers |
| V22 | Drill alignment: hover test over a real hole, then aligned drilling, routing and outline files | 06 §6.12 | **Done** | `AlignmentTest`, `DrillAlignment`; Job › Drill alignment, `align`, `export --align [--align-outline]` |
| V23 | Routed holes and slots: one continuous ramp per feature, no lift between laps, no floor lap under a through cut | 06 §6.13 | **Done** | `PassLinker` continuations; `SlotRoutingTests.OnTheTestBoardEachHoleAndSlotIsEnteredOnce`, `AThroughCutWhoseLastRampIsBelowTheBoardHasNoFloorLap` |
| V24 | Routing settings: depth per lap, short last lap, break-through, finishing lap, ramp feed | 06 §6.13 | **Done** | `Laps`, `MachineSettings.ShortLastLap`/`FinishingLapOnThroughCuts`, `SlotOperation.LapNotes`; stepdown, feed and break-through named from where they live; `RoutingSettingsTests` |
| V25 | Board thickness saved with the project, and read by the CLI | 06 §6.13 | **Done** | `ProjectSettings.BoardThicknessMm`; `ProjectTests.TheBoardThicknessIsSavedWithTheProject`; CLI `ThicknessFor` |
| V26 | Alignment holes in the stock's bottom and left waste borders, cut with the stock | 06 §6.14 | **Done** | `Blanks.AlignmentHolesFor`, `BlankOperation.Holes`; `BlankTests.TheAlignmentHolesSitInTheWasteAsFarApartAsTheStockAllows`, `TheStockProgramCutsTheHolesBeforeItsEdges` |
| V27 | Drill alignment from two holes: translation and rotation, separation checked, saved with the project | 06 §6.14 | **Done** | `RigidFit.Solve`, `DrillAlignment.Apply`, `ProjectSettings.Alignment`; `RigidFitTests`, `AlignmentTests.ATurnMovesEveryHoleAlongWithIt`, `ProjectTests.TheDrillAlignmentIsSavedWithTheProject` |
| V28 | Build-on-stock options in a dialog of their own | 06 §6.14 | **Not started** | to examine; *Project info* is crowded |
| V29 | Align from the stock's waste holes, from either side of the board, and choose which programs move | 06 §6.14 | **Done** | `ExportPlanner.Movable`, `DrillAlignment.Moved`, the dialog's waste/flip ticks; `AlignmentTests.OnlyTheProgramsNamedAreMoved`, `TheStockIsNeverMovedEvenWhenNamed`; CLI `align --waste-holes --flipped`, `export --align-moves` |
| V30 | A paste stencil to 3D-print: STL from a paste layer, apertures shrunk to the right volume, thinner steps only where release needs them, optional locating lip | 06 §6.18 | **Not started** | *Job › Paste stencil…*; target volume from Phase 9's rule |
| V31 | A dry run that is the real program raised: every move kept, spindle off, Z offset by a rise (default 3 mm) | 06 §6.19 | **Done** | move-for-move identical to the real program with every Z lifted; refuses on depth, on a program that commands no Z at all, on `G92`, `G10`, `G38`, `G43`/`G49` and their decimal variants, and on a machine-referenced Z (`G53`, `G28`, `G30`); the flat run is a choice in Settings and `--dry-run-height`. Sprint 2 story 8 |
| V32 | The stock's datum corner readable at a glance on a square piece | 06 §6.20 | **Not started** | a 3 mm chamfer is too subtle on 78 mm; the two waste holes are near-symmetric under 180°. **Set aside by the product owner in sprint 5**: one chamfer cannot catch a flip on a near-square piece at any size, and a second needs a corner the alignment holes already use — 06 §6.20 |
| V33 | Every hole approached from one side, and a backlash check to measure the slack | 06 §6.21 | **Not started** | the workshop's waste holes came out 0.24 mm closer than programmed (09 §1): about 0.17 mm of X backlash, or 0.13° of skew — 6.22's check tells them apart |
| V34 | Machine checks: backlash (six plunges, three rows), axis scale, squareness, effective cutter diameter, return to zero, tram | 06 §6.22 | **Partial** | backlash and squareness built: `MachineCheck`, `MachineCheckGuide`, *Job › Machine checks…*, `machine-check` CLI, `MachineCheckTests`, `Help/guides/machine-checks.html`. Axis scale, cutter diameter, return to zero and tram not started |
| V35 | About window: version, build date, and a manual check for a newer release | 06 §6.23 | **Done** | `ReleaseCheck` (parse + compare, no network), `AboutWindow`, `TravelDemo`; `ReleaseCheckTests` |
| V36 | The outline drops straight to the next lap, and hops tabs at the tab's own height | 06 §6.24 | **Done** | 45.4 mm of vertical motion down to 27.4 on the test board; about eight minutes off a 66-up panel. Sprint 1 story 6 |
| V37 | The checks become something you can read: a non-modal findings view naming every place the copper stays joined | 06 §6.44 | **Done** | `BoardFindings`, `FindingsWindow`, *Job › Findings* (F7); a coordinate and the nets for each place, capped and counted. `BoardFindingsTests`. Sprint 3 — **the whole of it**. Sprint 5 story 6 (06 §6.55): the layer is grown once for the check and the search, about half the cost on the Mega, and a second F7 abandons the run in progress. `NetGapTests`, `BoardFindingsTests.ASecondPressAbandonsTheRunInsteadOfWaitingItOut` |
| V38 | Copper with no net name is called unnamed wherever it is reported | 06 §6.53 | **Done** | `ElectricalCheck.NamedSides`; a region sharing one named net is no longer reported as safe. `ElectricalCheckTests`. Sprint 4 story 2 |
| V39 | A scale along the top and left of both viewports, agreeing with the grid at every zoom | 06 §6.1 | **Done** | `GridLadder`, `RulerFrame`, `Ruler.Draw`, called by `BoardRenderer` and `ToolpathRenderer` from one ladder; toggled by *View › Show rulers* and remembered. `RulerTests`, `AppSettingsTests.TheRulerIsOnUnlessItWasTurnedOff`. Sprint 4 story 8. Readable on the light theme since 0.6.0, where its marks had been white on white (06 §6.70); `RulerTests.TheMarksStandOutFromEitherThemesViewport` |
| V40 | The pointer's position, continuously, in the board's own numbers with its origin named | 06 §6.48 | **Done** | `RulerReadout`, `BoardView.PointerAt`/`OriginName`; reads from the stock's corner on a job built on stock and the board's otherwise, cleared when the pointer leaves. Shift-drag measures: `RulerMeasure` gives dx, dy and the diagonal. Sprint 4 story 8 |
| V41 | A finding takes you to the copper it is about | 06 §6.56 | **Done** | `Marker`, `BoardView.GoTo`; every coordinate in the findings list is a button, the view goes to it at 14 mm across and rings it, and the mark clears by each of the five routes the entry names. `MarkerTests`. Sprint 4 story 9 |
| V42 | The window says which folder the Gerbers came from | 06 §6.32 | **Done** | `ProjectOrigin.Full`, at the head of *Project info*; the full path, normalised, labelled `Gerbers`. `ProjectOriginTests`. Sprint 4 story 6. One line since 0.6.0, the middle folders elided and the whole path on hover — the product owner's call, reversing the wrapping 6.32 shipped with |
| V43 | A layer's role can be set by hand on its row, and a guessed one says so | 06 §6.31 | **Done** | `MillBurnProject.SetRole`, `ProjectSource.RoleGuessed`, *Layer is* on the row, `StaleBecause.RoleChanged`; `--set-role`. `LayerRoleByHandTests`, `StalenessTests`. Sprint 5 story 7 |

## 06 §2 — Cross-cutting acceptance criteria

| ID | Metric | Target | Status | Measured |
|---|---|---|---|---|
| C1 | Viewer frame rate, 500k segments | ≥ 60 fps | **Met** | 98 fps |
| C2 | Output determinism | byte-identical across runs | **Met** | 8 determinism tests |
| C3 | G-code line count | ≥ 5× reduction | **Met** | ~20× on a panel |
| C4 | Rapid travel, outline operations | below the NN baseline | **Met** | gated per board |
| C5 | Estimated cut time vs pcb2gcode | ≥ 20% reduction | **Superseded** | pcb2gcode has never been run |
| C6 | Optimizer, Balanced, 5000 paths | < 500 ms | **Superseded** | budget is steps; ms *was* the non-determinism bug |
| C7 | Preview latency, parameter change to redraw | < 200 ms | **Not measured** | the blocker named here — *"depends on A4/A5, which do not exist"* — has gone: both landed in sprint 1 and this row was never revisited. Nothing now stands between this and a number |
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
| P5 | Outline cut-out with tabs | Millburn test board after v0.1.0: 3 tabs, "outline is great" | **Proven** | — |
| P6 | Arcs surviving levelling | confirmed in a second controller's viewer | **Proven** | — |
| P7 | Links cut at depth between laps | Millburn test board top copper after v0.1.0: 4 passes, lifts only where they belonged; 28:45 against an estimate of 16m 17s – 45m 45s | **Proven** | — |
| P8 | Drilling with bit changes | Millburn test board: one file per bit, three bits, holes landing in their pads | **Proven** | — |
| P9 | Double-sided and its mirror | Millburn test board, both sides made on the laser: the mirrored bottom lined up with the top, a small offset put down to the setup | **Partial** | milled bottom-side isolation, flipped on the mill |
| P10 | Soldermask relief | — | **Untested** | needs cured mask on the stock; low priority |
| P11 | Panel channel, one pass down the middle | — | **Untested** | GridStripConnector_Panelized with a 0.8 mm end mill |
| P12 | Stock cut to size, its corner work zero for every later setup | Millburn test board, 10 mm border | **Proven** | — |
| P13 | Routed slots and milled holes, one continuous descent each | Millburn test board after v0.1.0: 1 mm slots and 2.2–3.5 mm holes, 0.8 mm end mill; plated file 1:12 against 0m 53s – 1m 31s, non-plated 0:47 against 0m 37s – 0m 58s | **Proven** | — |
| P14 | Drill alignment: hover test, then aligned files | Millburn test board: a couple of tests, then every hole in the middle of its pad (photographed in the guide) | **Proven** | — |
| P16 | Two-hole alignment with rotation, cut on metal | Millburn test board, 2026-09-18: drills and edge cuts from a two-hole correction, "as good as I can expect" | **Proven** | — |
| P15 | Alignment from the stock's waste holes, measured from either side | — | **Untested** | the numbers are right on the test board; nothing cut from them yet |
| P17 | Tabs left the thickness the program promises | Millburn test board, 2026-09-19: outline cut with the extra pass at the tab top, tabs left partial rather than full thickness | **Proven** | — |

---

## Queue order

The phases as [06](06-Roadmap-and-Risks.md) declares them, against where the work actually went.

| Phase | Declared | Actual |
|---|---|---|
| 0 Foundation | — | **Done** |
| 1 Read and draw a board | — | **Done** |
| 1.5 Projects | — | **Done** |
| 2 Mill toolpaths, G-code, backplot | — | **Done** |
| 3 The optimizer | — | **Done** — O6 parked, measured and not needed on any committed board |
| 4 Laser output (SVG) | in progress | **Parked** — M4, M7, V11, V12 outstanding |
| 5 Jobs, setups, alignment | started | **Partial** — levelling, the stock's waste holes, the two-point fit and the saved record are in (M16, M17, M20); the Job/Step/Setup model (M9) and most of §3 are not |
| 5.5 Drilling, finished | scheduled | **Done — jumped the queue** |
| 5.6 The blank ("stock" in the app) | scheduled | **Built** — 5.6.5 laser verification open |
| 6.x The numbered roadmap items | — | **See [11](11-Backlog.md)** — generated from 06, so it cannot drift |
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
