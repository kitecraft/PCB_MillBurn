# 01 — Architecture

## 1. Constraints and posture

- **Desktop only.** No Android/iOS/MacCatalyst targets — they are dead weight that slows every
  build and forces lowest-common-denominator API choices.

  Desktop, though, means **Windows and Linux** rather than Windows alone, and that came free rather
  than by design. Every project targets plain `net10.0`, there is no Windows-only API anywhere in
  the codebase, and the GUI publishes for `linux-x64` with zero warnings — Avalonia brings its X11
  backend and SkiaSharp its native `libSkiaSharp.so` automatically.

  Three earlier decisions, none about portability, are why. Keeping every algorithm in UI-free
  libraries was for *shell swappability*. Choosing Avalonia over WPF or WinUI was for the SkiaSharp
  viewport, and WPF would have been a hard stop. And **not driving machines** (§1.1) removes serial
  ports, driver installs and USB permissions — which is the platform-specific misery that makes
  most CNC software Windows-shaped in the first place.

  **Verified on WSL2 with WSLg**, not merely built: the window renders, the theme toggle works, the
  file and folder pickers work — that was the part most likely to break, since Avalonia goes through
  xdg-desktop-portal on Linux — and G-code saved from the Linux build loaded into gSender.

  macOS is likely and unverified; nothing is known to be in the way.
- **UI shell: Avalonia UI**, chosen over MAUI and WPF — see
  [07 — UI Framework Decision](07-UI-Framework-Decision.md) for the full argument. Short version:
  Avalonia *is* SkiaSharp underneath (so the 500k-segment viewport is native, not a composited
  bitmap), `AvaloniaEdit` gives the synced G-code editor for free, and `Dock.Avalonia` gives real
  docking. WPF is the conservative fallback; MAUI is workable but means hand-building a docking
  system and a code editor.
- **The shell choice is deliberately cheap.** Every algorithm lives in plain `net10.0` class
  libraries with zero UI dependency, so swapping shells is one project, not a rewrite. Hold this
  line absolutely — it is what keeps the framework question small.
- **The CLI is a first-class product**, not an afterthought. It is how we run regression tests,
  how CI works, and how power users batch things.

### 1.1 Scope boundary — PCB_MillBurn writes files, it does not drive machines

**This app converts Gerber to G-code for the mill and to SVG for the laser. It never opens a
serial port.** No jogging, no probing, no streaming, no DRO, no `$$` settings. UGS, Candle, LightBurn, bCNC, and LinuxCNC already do that
job well; competing with them would double the surface area and add all the risk (a bug in a file
generator wastes a board; a bug in a sender crashes a spindle into a fixture).

This is a real constraint, not a simplification, and it shapes two designs in particular:

- **Alignment is "numbers in, G-code out."** The operator measures fiducials in *their* sender and
  reads the DRO; they type those coordinates into PCB_MillBurn; we compute the transform and emit
  re-registered G-code. No closed loop needed. See
  [04 §4](04-Machines-Laser-and-Mixed-Workflows.md#4-board-re-alignment--the-hard-problem).
- **Height mapping is import-based.** We *generate* the probing routine as a G-code file; the
  sender runs it and logs results; we *import* the log. UGS, Candle and bCNC all export height
  maps already. See [04 §5](04-Machines-Laser-and-Mixed-Workflows.md#5-height-mapping-autolevelling).

Both are strictly better for us: they work with every controller and sender in existence, with
zero firmware-compatibility surface.

**The same reasoning, applied to lasers, decides the output format: SVG, not G-code.** Laser
software already owns power, speed, passes, fill strategy, overscan and the user's calibrated
material library — and much laser hardware does not take G-code at all. What it cannot do is read
a Gerber, so our half is geometry: pad selection from X2 attributes, copper inversion, layer
assignment — and an SVG true to the design at 1:1. Kerf and etch-bias compensation are
deliberately *not* ours; they depend on beam and chemistry parameters that live in the laser
software. See
[04 §1](04-Machines-Laser-and-Mixed-Workflows.md#1-two-machines-two-output-formats). This removes
an entire subsystem — laser dialect, scanline generator, power model — from the project.

## 2. Solution layout

```
PCB_MillBurn.slnx
├── src/
│   ├── MillBurn.Core            net10.0   Units, geometry primitives, transforms, project model
│   ├── MillBurn.Gerber          net10.0   Gerber X2/X3 + Excellon parsers → semantic model
│   ├── MillBurn.Geometry        net10.0   Clipper2: offset, boolean, area, inversion, tessellation
│   ├── MillBurn.Cam             net10.0   Operation generators (isolation, drill, outline, mask)
│   ├── MillBurn.Optimize        net10.0   Travel optimizer, precedence, simplification, pass linking
│   ├── MillBurn.Gcode           net10.0   Mill only: emitter, parser, backplot, dry run, probing
│   ├── MillBurn.Post            net10.0   Mill only: post-processor templates — EMPTY, Phase 6
│   ├── MillBurn.Export          net10.0   SVG. DXF and PDF are Phase 4/5 and not built
│   ├── MillBurn.Align           net10.0   Height maps + levelling. Fiducial fits are Phase 5
│   ├── MillBurn.Viewer          net10.0   Toolpath scene, LOD, spatial culling, Skia renderer
│   ├── MillBurn.Pipeline        net10.0   The cached, cancellable stage graph tying it together
│   ├── MillBurn.App             net10.0   Shell ONLY: MVVM, docking, Skia viewport
│   ├── MillBurn.Cli             net10.0   Headless batch driver
│   └── MillBurn.Mcp             net10.0   MCP server over the same libraries (Phase 8, not built)
└── tests/
    ├── MillBurn.Tests           xUnit unit + property tests
    ├── MillBurn.GoldenTests     Golden-file and determinism regression
    ├── boards/                  Six real KiCad exports, committed as fixtures
    └── corpus/                  Empty. Where a pcb2gcode test-data checkout is looked for;
                                 GPL-3.0, so never committed, and those tests skip without it
```

Three projects are named here for the shape of the graph rather than for what they hold today.
`MillBurn.Post` has a Scriban reference and no source file; `MillBurn.Export` writes SVG and
nothing else; `MillBurn.Mcp` does not exist on disk at all. Naming them early is deliberate — it
fixes where the code goes when it arrives — but the table said what they *would* contain as though
they contained it, which is the one thing a layout diagram must not do.

Dependency direction is strictly downward. `MillBurn.App` references everything; nothing
references `MillBurn.App`. `MillBurn.Cli` and `MillBurn.Mcp` are peers of it — entry points holding
no logic of their own, so that a job comes out the same however it was asked for.

`MillBurn.App` targets plain `net10.0`, not `net10.0-windows`: Avalonia supplies the X11 backend and
SkiaSharp the native renderer, and the Linux build is published and run. Nothing in the codebase
touches a Windows-only API.

`MillBurn.Align` references `MillBurn.Gcode`, because the height-map *leveller* is a rewrite of an
emitted program and belongs beside the map it applies. The alternative — putting the leveller in
`MillBurn.Gcode` with the other processors — would drag MathNet.Numerics into a project that every
consumer references for the emitter and the parser, to serve one feature most of them never touch.

## 3. Third-party libraries

| Need | Choice | License | Notes |
|---|---|---|---|
| Polygon boolean + offset | **Clipper2** (`Clipper2Lib`, native C#) | BSL-1.0 | The workhorse. Int64 coords; we scale to nanometres. Round/miter joins, `InflatePaths`, `Minkowski`. |
| Higher-level geometry | **NetTopologySuite** | BSD-3 | Voronoi (`VoronoiDiagramBuilder`), `STRtree` spatial index, validity, prepared geometry. Replaces pcb2gcode's GEOS dependency. |
| 2D rendering | **SkiaSharp** | MIT | Gerber preview, toolpath preview, backplot viewer, PNG export. Under Avalonia this is the framework's own renderer — reach it via `ICustomDrawOperation` + `ISkiaSharpApiLease`. |
| Docking layout | **Dock.Avalonia** | MIT | Floatable/tabbed tool panes with persisted layouts. (WPF equivalent: AvalonDock. MAUI: none — you'd build it.) |
| G-code text pane | **AvaloniaEdit** | MIT | Syntax highlighting, line transformers for click-to-highlight, virtualization over huge files. (WPF: AvalonEdit. MAUI: none.) |
| MVVM | **CommunityToolkit.Mvvm** | MIT | Source-generated observable properties and commands. Framework-agnostic — unchanged from IceLight. |
| Reactive/debounce | **System.Threading.Channels** (or `System.Reactive`) | MIT | Pipeline re-run coalescing. |
| Templating (post) | **Scriban** | BSD-2 | User-editable preamble/postamble/line templates. |
| Numerics | **System.Numerics** / **MathNet.Numerics** | MIT | SVD for the Kabsch/affine fiducial fit, thin-plate-spline height interpolation. |
| Tests | **xUnit**, **Verify** | Apache/MIT | Golden-file snapshot testing. |

Explicitly **not** used: Boost.Geometry, GEOS native, libgerbv, Cairo. Those are pcb2gcode's
dependency chain and they are the reason it is painful to build on Windows. Everything above is a
NuGet package that restores on a clean Windows box with no vcpkg, no MSYS2, no CMake.

## 4. The incremental pipeline — **designed, not built**

> **Status.** Nothing in this section exists. There is no `PipelineCache`, no `CancellationToken`
> anywhere in `src/`, and no debounce. `MillBurn.Pipeline` is a set of builders that run start to
> finish on the calling thread, and every export recomputes from the parsed board.
>
> It has not been the constraint anyone expected. A real board plans, emits, backplots and renders
> fast enough that nobody has waited for it, and the two places that *would* hurt — a 66-up panel's
> isolation, and the optimizer at Thorough — are both one-off operations behind a button rather
> than something a slider drags. So this is kept as the design for when a live preview is built,
> and the **< 200 ms** target below is unmeasured because the thing it measures does not run.
>
> The rest of the section is the intended design, not a description of the code.

The single biggest UX difference from pcb2gcode is that **PCB_MillBurn is not a batch converter**.
Change a number, see the result. That requires the pipeline to be incremental and cancellable.

```
  Files ──▶ Parse ──▶ Layer geometry ──▶ Operation geometry ──▶ Ordered toolpath ──▶ G-code ──▶ Backplot
           (Gerber)   (multipolygons)     (offsets/pockets)      (optimizer)        (post)     (viewer)
              │             │                    │                     │               │
              └── cached ───┴──── cached ────────┴──── cached ─────────┴─── cached ────┘
```

Design:

- Every stage is a pure function `TIn -> TOut` over **immutable** inputs.
- Each stage result is memoised in a `PipelineCache` keyed by a **structural hash** of its inputs
  (`XxHash128` over the serialised parameter record + upstream result id). Changing *isolation
  width* invalidates offset → optimize → post → backplot, but not parse or layer geometry.
- Stages run on the thread pool behind a single `CancellationTokenSource` per edit. A new edit
  cancels the in-flight run before starting the next. UI never blocks.
- A **debounce/coalesce** window (~120 ms) on slider drags; the final value always runs.
- Progress and partial results stream to the UI: the viewer draws the isolation paths as soon as
  offsets are done, before the optimizer finishes, and re-draws when ordering lands.
- **Target: < 200 ms** from parameter change to redrawn preview on a typical 100×80 mm two-layer
  board. Anything slower and the "real-time" promise dies. Budget: parse is one-time, offsets
  ~30 ms with Clipper2, optimizer time-boxed (see [03](03-Toolpath-Optimization.md#6-time-boxing)),
  emit + backplot ~20 ms.

### Determinism

Every stage must be deterministic given the same inputs — no unseeded RNG (pcb2gcode literally
has a `consistent_rand.cpp` to paper over this), no hash-order iteration, no parallel reduction
with float addition in nondeterministic order. Determinism is what makes golden-file testing
possible and what makes "did my change help?" answerable.

## 5. Project model

```csharp
sealed record MillBurnProject(
    ProjectMeta          Meta,
    ImmutableArray<InputFile>   Inputs,      // gerbers, drill files, their roles
    BoardModel           Board,              // derived: layers, outline, nets, pads
    ImmutableArray<MachineProfile> Machines, // mills emit G-code, lasers emit SVG
    ImmutableArray<Fixture>        Fixtures, // pin plates, nests
    ImmutableArray<Setup>          Setups,   // a machine + a fixture + a coordinate frame
    ImmutableArray<Operation>      Operations,
    JobPlan              Job);               // ordered, with manual steps interleaved
```

Persisted as a single `.millburn` file: a **zip container** holding `project.json` plus copies of
the source Gerbers, the height maps, and the alignment records. Self-contained — you can hand the
file to someone else or re-open it in two years and reproduce byte-identical G-code. pcb2gcode's
`millproject` INI file references external paths and stores no provenance; that is a real
reproducibility problem for a workflow that spans days and two machines.

Settings/machine profiles live in `%LOCALAPPDATA%\PCB_MillBurn\` as JSON, importable/exportable
so the community can share machine profiles.

## 6. UI shape

Dockable panes around a central Skia viewport. Default layout:

```
┌──────────────────────────────────────────────────────────────────────┐
│ Job bar:  [Setup A · Laser] → [etch] → [Setup A · Laser] → [Setup B · Mill] │
├────────────┬─────────────────────────────────────────┬───────────────┤
│ Operations │                                         │  Inspector    │
│ tree       │        Viewport (SkiaSharp)             │  (params for  │
│            │        · Gerber underlay                │   selected    │
│  ▸ Setup A │        · toolpaths, colour by tool       │   operation)  │
│    Fiducials│       · travel moves, dashed            │               │
│    Mask burn│       · DRC violations, red             │  live re-run  │
│  ▸ Setup B │        · simulated tool position         │  on change    │
│    Drill   │                                         │               │
│    Outline │                                         │               │
├────────────┴─────────────────────────────────────────┴───────────────┤
│ Timeline scrubber ◀▶  |  G-code text (synced) | Stats: cut 1.2 m · rapid 0.4 m · 6:31 │
└──────────────────────────────────────────────────────────────────────┘
```

Key UI principles:

1. **Nothing is modal.** No "generate" button that blocks. Output is always live.
2. **Every number has a unit and a tooltip explaining the physical consequence.** pcb2gcode's
   40+ CLI flags are individually reasonable and collectively unusable.
3. **Selection is bidirectional**: click a toolpath → the G-code line and the parameter that
   produced it highlight; click a G-code line → the geometry highlights.
4. **Problems are surfaced, not hidden.** A dedicated Issues panel: "tool too wide to isolate
   between U1.3 and U1.4 (needed 0.18 mm, have 0.25 mm)" with a click-to-zoom.
5. **One palette, everywhere.** Port IceLight V2's semantic token system
   (`Theme_Quick_Reference.md`) and extend it with CAM tokens — `PathIsolation`, `PathDrill`,
   `PathOutline`, `PathTravel`, `PathRapidLong`, `DrcViolation` — so the viewport, the legend, and
   the [SVG export](05-Viewer-and-Export.md#3-svg-export--the-laser-half-of-the-product) all read from the same source and agree.
   See [07 §5](07-UI-Framework-Decision.md#5-ideas-worth-carrying-over-from-icelight-v2).

## 7. Threading

- UI thread: Avalonia's dispatcher only. (This said "MAUI/WinUI" until the Phase 0 spike settled
  the shell question; [07](07-UI-Framework-Decision.md) is the decision and Avalonia won it.)
- All cross-boundary data is immutable, so no locks in the pipeline.
- No machine I/O anywhere. PCB_MillBurn writes files; a sender (UGS, Candle, LightBurn,
  LinuxCNC) runs them. See §1.1.

**Planned, not built:** `Task.Run` onto the thread pool, and `Parallel.For` inside the geometry
stages where the work is embarrassingly parallel (per-net offsets, per-contour compositing). Every
stage runs to completion on the calling thread today. It has not hurt — the slowest real board
plans in well under a second, and a panel's isolation is the only thing that takes long enough to
notice — so this waits until §4 is built, which is what makes it worth having.

## 8. Testing strategy

| Layer | Approach |
|---|---|
| Gerber parser | Ucamco spec example files + tracespace's test corpus; assert rendered raster matches a reference within N pixels. |
| Geometry | Property tests: offsets never self-intersect; isolation path never enters copper of another net; area invariants. |
| **Electrical DRC** | After isolation, compute connected components of remaining copper; compare to the netlist from Gerber X2 `.N` attributes. Any accidental short or open fails the build. |
| Optimizer | Benchmark suite: total travel distance and estimated time vs. pcb2gcode on the same inputs. Regression gate: never worse than the last release. |
| G-code | Round-trip: emit → parse → backplot → compare geometry to the pre-emit toolpath within tolerance. Catches post-processor bugs. |
| **Material simulation** | Rasterise the swept tool volume and diff against intended copper. Produces a "what the board will look like" image that doubles as a preview and a test oracle. |
| Golden files | Full pipeline over the corpus, snapshot the G-code and SVG. Deterministic output makes diffs meaningful. |

The material simulation and the electrical DRC are the two tests pcb2gcode does not have and
they are the ones that catch the failures that actually ruin a board.

## 9. Licensing strategy

**Both reference checkouts are GPL-3.0.** `pcb2gcode` and `Universal-G-Code-Sender` alike.

- Reading them to learn *what problems exist and roughly how they were solved* is fine and is
  exactly what this document does.
- Copying, translating, or transliterating their code into C# makes PCB_MillBurn a derivative
  work and forces GPL-3.0 on the whole app.
- **Therefore: clean-room.** Implement from the published specs and from the permissive libraries
  listed in §3. Where a UGS or pcb2gcode idea is genuinely the right design (UGS's command
  processor chain, its `LineSegment` backplot model), we re-derive the design and write our own
  code — the *idea* is not copyrightable, the expression is.
### The decision: MIT

**PCB_MillBurn is MIT licensed.** The goal is attribution and nothing else — anyone may use, modify
and ship it, including in a closed commercial product, provided the copyright notice travels with
it.

MIT over Apache-2.0 because the only material difference for a project of this shape is Apache's
explicit patent grant and retaliation clause, which matters when corporate contributors are in the
picture and adds two hundred lines of text that nobody here will read. MIT is short enough to read
in full, which is worth more.

Every dependency is permissive too — MIT, BSD-2, BSD-3, BSL-1.0, Apache-2.0 — verified from the
restored packages rather than assumed, and listed in `THIRD-PARTY-NOTICES.md`. There is no copyleft
anywhere in the graph.

**This is what makes the clean-room rule load-bearing rather than decorative.** A single translated
function from pcb2gcode or UGS would make the whole application GPL-3.0 and the MIT licence a
misstatement. The rule is now permanent, and worth restating in review: if a solution looks like it
was remembered from that source rather than derived from the spec, it does not go in.
