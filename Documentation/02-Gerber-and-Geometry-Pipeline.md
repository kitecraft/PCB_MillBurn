# 02 — Gerber Parsing & the Geometry Pipeline

## 1. The key insight: keep the semantics

pcb2gcode hands the Gerber to **libgerbv**, which renders it, and then vectorises the result into
Boost.Geometry multipolygons. By the time the CAM code sees the board, it knows only *"here is a
blob of copper"*. It does not know:

- which blob is a **pad** and which is a **trace**,
- which pad belongs to **which net**,
- which pad is a **via** vs. an **SMD pad** vs. a **through-hole component pad**,
- the **reference designator and pin number** of a pad.

All of that is present in modern Gerber. **Gerber X2** (and X3) carry standard attributes that
KiCad, Altium, and Eagle all emit by default:

| Attribute | Meaning | What it unlocks for us |
|---|---|---|
| `%TA.AperFunction,SMDPad,CuDef*%` | This aperture draws SMD pads | Select exactly the pads for the laser mask-opening pass |
| `%TA.AperFunction,ViaPad*%` | Via pads | Optionally *don't* open mask over vias |
| `%TO.N,GND*%` | Net name of the following objects | Electrical DRC, per-net isolation width |
| `%TO.C,U1*%` / `%TO.P,U1,3*%` | Component ref-des and pin | "Open mask for U1 only", human-readable issue messages |
| `%TF.FileFunction,Copper,L1,Top*%` | Layer role | **Auto-detect which file is which** — no more manually assigning six files |

**We write our own parser** so we keep all of it. This is not a heroic effort — the Gerber format
is a small, well-specified state machine, the spec is a free PDF from Ucamco, and the hard part
(polygon booleans) is Clipper2's job, not ours.

This single decision is what makes Use Case 1 tractable. "Burn away the mask over the pads" is
otherwise a guessing game of morphological heuristics; with X2 attributes it is a database query.

### Fallback for files without X2

Older/exported Gerbers may carry no attributes. Provide a **pad inference** fallback:

1. Any flash (`D03`) is a pad. This alone catches most cases — traces are drawn (`D01`), pads are
   flashed.
2. Copper regions that contain a drill hit from the Excellon file are through-hole pads.
3. Manual override in the UI: rubber-band select geometry → "treat as pad".

Report clearly in the UI which mode is active: *"X2 attributes found — pad selection is exact"*
vs. *"No X2 attributes — pads inferred from flashes, please verify"*.

## 2. Parser design

```
MillBurn.Gerber
├── Lexing/            Command tokeniser (X2 attribute-aware, handles both %-blocks and G-codes)
├── State/             GraphicsState: current aperture, interpolation mode, quadrant mode,
│                      polarity (LPD/LPC), step&repeat, transformations (LM/LR/LS/LP)
├── Apertures/         Standard (C,R,O,P) + AM macro interpreter (all 21 primitives, with
│                      full expression evaluation and $-variable substitution)
├── Model/             GerberImage: ordered list of GraphicObjects, each with
│                      { Geometry, Polarity, ApertureId, Attributes, SourceLine }
└── Excellon/          Drill file parser: tools, plated/non-plated, routed slots (G85/G00-G01),
                       header/units auto-detection, Altium/KiCad/Eagle dialect quirks
```

Implementation notes that matter:

- **Aperture macros.** pcb2gcode/gerbv handle these; a naive parser does not. They are how
  thermal reliefs, rotated rectangles, and odd pads are expressed. Implement the full primitive
  set (1 circle, 4 outline, 5 polygon, 6 moiré, 7 thermal, 20 vector line, 21 centre line, 22
  lower-left line) and the arithmetic expression evaluator. Skipping this fails on real boards.
- **Polarity is order-dependent.** Dark/clear (`%LPD*%`/`%LPC*%`) objects must be composited in
  file order — a clear object only erases what came before it. Model it as a sequential
  accumulate with Clipper2 `Union`/`Difference`, not a bulk union of darks minus a bulk union of
  clears. Getting this wrong silently deletes copper.
- **Arcs.** Keep them as arcs in the model (centre, radius, start/end angle, direction) as long as
  possible. Only flatten at the last moment, with a **sagitta-based tolerance** (default 2 µm), so
  we can re-fit G2/G3 on output. pcb2gcode flattens early and can never recover arcs — which is a
  major reason its G-code files are megabytes of `G01`.
- **Step & repeat** (`%SR%`) and **block apertures** (`%AB%`): expand to real geometry.
- **Coordinate format** (`%FSLAX36Y36*%`), units (`%MOMM*%`), and omitted leading zeros: get the
  format spec right or everything is off by 10×. Validate and warn loudly on ambiguity.

### Precision

Internal representation is **integer nanometres** (`long`). Clipper2 works in Int64 and is exact
on integers; nanometres give ±9.2×10⁹ mm of range, far more than needed, and 1 nm is 4 orders of
magnitude below anything a hobby machine can resolve. Floating-point coordinates are converted at
the boundary only. This eliminates a whole class of robustness bugs that pcb2gcode fights with
`merge_near_points.cpp` and epsilon comparisons.

## 3. Layer auto-detection

From `%TF.FileFunction%` when present, else by filename heuristics covering KiCad
(`-F_Cu.gbr`, `-Edge_Cuts.gbr`, `.drl`), Altium (`.GTL`, `.GBL`, `.GKO`, `.TXT`), Eagle
(`.cmp`, `.sol`, `.dim`), and the generic `.gbr`/`.ger` + RS-274X sniffing.

Drop a folder on the window → correctly assigned layers, board outline detected, preview drawn.
That interaction alone is a bigger usability win than any single algorithm in this document.

## 4. Geometry operations

`MillBurn.Geometry` wraps Clipper2 and NTS behind a small domain API so the CAM layer never
touches library types directly.

| Operation | Implementation |
|---|---|
| Union / difference / intersection | Clipper2 `Clipper64` |
| Offset (grow/shrink) | Clipper2 `ClipperOffset`, `JoinType.Round`, `EndType.Polygon`, arc tolerance from the sagitta budget |
| Voronoi / medial-axis isolation | NTS `VoronoiDiagramBuilder` over densified copper boundary points, then trim to the inter-net corridor |
| Spatial queries | NTS `STRtree` |
| Polyline simplification | Douglas–Peucker at 5 µm (configurable), applied *after* offsetting, *before* arc fitting |
| Arc fitting | Least-squares circle fit over sliding windows with a max-deviation test; emit G2/G3 when the fit is within tolerance and spans > 15° |
| Pocketing | Contour-parallel offset chains + optional trochoidal for large open areas |
| Minkowski (tool sweep) | Clipper2 `Minkowski.Sum` — used by the material simulation |

## 5. Isolation milling

The core operation. Given copper geometry and a tool:

1. **Effective tool diameter.** For a V-bit, `d_eff = tip_width + 2·depth·tan(angle/2)`. Expose
   this as a live-computed readout and provide the inverse solver: *"I need 0.20 mm isolation
   clearance — what depth?"* Hobby users get this wrong constantly; make it impossible to get
   wrong.
2. **Pass 1** = copper offset outward by `d_eff/2 + safety`. Passes 2..N at increasing offsets,
   stopping at `max_isolation_width` or when the pass no longer removes anything.
3. **Minimum-clearance DRC.** Before generating anything, compute the *gap width field* between
   distinct nets. Anywhere the gap is narrower than `d_eff`, the tool will destroy a trace.
   Currently pcb2gcode's handling of this is a hard-to-read warning; ours is:
   - highlighted **red** in the viewport with a zoom-to link,
   - listed in the Issues panel with net names and coordinates,
   - with a one-click **"use a narrower tool here"** that emits a second, finer tool operation
     confined to just the tight regions (rest machining) instead of failing the whole board.
4. **Voronoi mode.** Instead of a fixed offset, cut along the medial axis between nets —
   maximises copper retained and removes the "did I clear enough?" question. Ours differs from
   pcb2gcode's in two ways: it is **capped** (never wander more than `max_isolation_width` from
   the copper) and it is **blended** with the fixed-offset passes rather than being an
   either/or mode.
5. **Copper pour removal** ("mill everything"): pocket the non-copper region. Multi-tool: bulk
   with a 2 mm end mill, detail with the V-bit, with proper **rest machining** so the small tool
   only visits what the big tool could not reach. pcb2gcode has no rest machining; on a
   ground-plane board this is the difference between 12 minutes and 90 minutes.
6. **Electrical verification.** After generating the isolation paths, subtract the swept tool
   from the copper and compute connected components. Compare against the X2 net attributes:
   - two different nets now connected → **short**, error;
   - one net split into two islands → **open**, error;
   - report both with net names before the user cuts anything.

   This is the single highest-value check in the product and no free tool does it.

## 6. Drilling & routing

- Group by tool; TSP-order within each tool group (see [03](03-Toolpath-Optimization.md)).
- **Peck drilling** (G83 or emulated for GRBL, which lacks canned cycles) with configurable peck
  depth and retract.
- **Spot drilling** pass with a centre drill for accuracy on FR4.
- **Hole size compensation**: nominal Ø minus bit Ø, with a per-material calibration offset.
- **Mill-drill** for holes larger than the largest available bit: helical interpolation with
  proper lead-in, not pcb2gcode's plunge-and-circle. Also handles **routed slots** from the
  Excellon G85 records, which pcb2gcode handles poorly.
  *Scheduled as [Phase 5.5](06-Roadmap-and-Risks.md#phase-55).
  Slots are read and drawn today and deliberately left out of the drilling program, which says so:
  a slot needs an end mill, not a drill, and choosing one means consulting the tool library, which
  nothing in this path has ever done.*
- Tool changes: `M6` + configurable pause/probe sequence, with an optional **tool-length probe**
  step so Z stays correct across changes.

## 7. Board outline

The operation the user specifically called out as badly ordered. Geometry side:

- Build a **containment tree** of the outline contours (outer boundary, internal cutouts, slots).
- Offset outward by tool radius for the outer boundary, inward for cutouts. Getting the side
  wrong is a classic failure; derive it from the containment tree, never from a user flag.
- **Tabs/bridges**: place automatically at even spacing with a minimum count per contour, avoid
  placing them across a cutout corner, and let the user drag them in the viewport. Tab height and
  width per-material.
- **Lead-in/lead-out**: tangential arc lead-in so there is no dwell mark at the entry point.
- **Ramped/helical entry** rather than vertical plunge — 1.6 mm FR4 with a 2 mm end mill plunging
  straight down is how you break bits.
- **Multi-pass depth**: contour-major by default (finish one contour's full depth before moving
  on) so the tool never travels between contours more than once per contour. pcb2gcode's
  depth-major ordering is a large part of the excess travel the user is seeing.

## 8. What the geometry stage outputs

```csharp
sealed record ToolpathSegment(
    PathGeometry  Geometry,     // polyline or arc chain, in board coordinates, nm
    bool          IsClosed,     // closed loops have free choice of start vertex
    ToolId        Tool,
    double        DepthZ,       // or LaserPower for laser ops
    PathRole      Role,         // Isolation, Pocket, Drill, Outline, Fiducial, MaskOpen
    NetId?        Net,          // for DRC messages and per-net rules
    PrecedenceKey Precedence);  // constraints the optimizer must respect
```

A flat list of these, plus the constraint set, is exactly what the optimizer needs — and nothing
else. Keeping this interface narrow is what lets the optimizer be genuinely good.
