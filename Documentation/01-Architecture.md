# 01 — Architecture

## 1. Constraints and posture

- **Windows only.** No Android/iOS/MacCatalyst targets — they are dead weight that slows every
  build and forces lowest-common-denominator API choices.
- **UI shell: Avalonia UI recommended** over the existing MAUI scaffold — see
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
a Gerber, so our half is geometry: pad selection from X2 attributes, copper inversion, kerf and
etch-bias compensation, layer assignment. See
[04 §1](04-Machines-Laser-and-Mixed-Workflows.md#1-two-machines-two-output-formats). This removes
an entire subsystem — laser dialect, scanline generator, power model — from the project.

## 2. Solution layout

```
PCB_MillBurn.slnx
├── src/
│   ├── MillBurn.Core            net10.0   Units, geometry primitives, transforms, project model
│   ├── MillBurn.Gerber          net10.0   Gerber X2/X3 + Excellon parsers → semantic model
│   ├── MillBurn.Geometry        net10.0   Clipper2 + NTS: offset, boolean, voronoi, pocket, arcs
│   ├── MillBurn.Cam             net10.0   Operation generators (isolation, drill, outline, mask)
│   ├── MillBurn.Optimize        net10.0   Travel optimizer, precedence constraints, time model
│   ├── MillBurn.Gcode           net10.0   Mill only: emitter, parser, processors, backplot
│   ├── MillBurn.Post            net10.0   Mill only: profiles + post-processor templates
│   ├── MillBurn.Export          net10.0   SVG / DXF / PDF / PNG - the whole laser path
│   ├── MillBurn.Align           net10.0   Fiducial fits, transforms, height-map import
│   ├── MillBurn.Viewer          net10.0   Toolpath scene, LOD, spatial culling, Skia renderer
│   ├── MillBurn.Pipeline        net10.0   The cached, cancellable stage graph tying it together
│   ├── MillBurn.App             net10.0-windows   Shell ONLY: MVVM, docking, Skia viewport
│   └── MillBurn.Cli             net10.0   Headless batch driver
└── tests/
    ├── MillBurn.Tests           xUnit unit + property tests
    ├── MillBurn.GoldenTests     Golden-file regression over a Gerber corpus
    └── corpus/                  KiCad demo boards, tracespace test suite, real user boards
```

Dependency direction is strictly downward. `MillBurn.App` references everything; nothing
references `MillBurn.App`.

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

## 4. The incremental pipeline

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
   the [SVG export](05-Viewer-and-Export.md#3-svg-export) all read from the same source and agree.
   See [07 §5](07-UI-Framework-Decision.md#5-ideas-worth-carrying-over-from-icelight-v2).

## 7. Threading

- UI thread: MAUI/WinUI only.
- Pipeline: `Task.Run` on the thread pool, `Parallel.For` inside geometry stages where the work
  is embarrassingly parallel (per-net offsets, per-contour compositing).
- All cross-boundary data is immutable, so no locks in the pipeline.
- No machine I/O anywhere. PCB_MillBurn writes files; a sender (UGS, Candle, LightBurn,
  LinuxCNC) runs them. See §1.1.

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
- Decide the PCB_MillBurn license deliberately. If you want community contributions and no
  restrictions on who ships it, **MIT or Apache-2.0**. If you want derivatives to stay open,
  GPL-3.0 is available anyway. Making the clean-room choice now keeps both doors open; copying
  code closes one permanently.
