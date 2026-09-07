# 04 — Machines, Laser Output & Mixed Workflows

pcb2gcode has no concept of a laser (`grep -i laser src/` finds two mentions, both in the help
text for "run this custom G-code before each trace, e.g. to turn on a fan"). It also has no
concept of a job that spans two machines. Both are core requirements here, and the second one —
mixed use — is where all the difficulty lives.

## 1. Machine profiles

```csharp
sealed record MachineProfile(
    string Name,
    MachineKind Kind,               // Mill | Laser  (a mill with a laser mount is two profiles)
    Dialect Dialect,                // Grbl | GrblHal | FluidNc | LinuxCnc | Mach3 | Marlin | Iso
    Kinematics Kin,                 // work volume, max feed per axis, acceleration per axis,
                                    //   junction deviation — feeds the time model
    SpindleOrLaser Head,            // RPM range / power range, spin-up time, M3-vs-M4, S scale
    ImmutableArray<Tool> Toolchange,// available tools, changer or manual
    ProbeConfig Probe,              // G38.2 support, probe plate thickness, tool-length sensor
    GcodeTemplates Templates);      // preamble / postamble / toolchange / per-line (Scriban)
```

Two profiles ship preconfigured for the target setup — *"CNC router (GRBL)"* and *"Diode laser
(GRBL laser mode)"* — and both are exportable/importable JSON so profiles can be shared.

### Post-processor

Template-driven rather than hard-coded, because every controller has quirks:

- **GRBL 1.1**: no canned cycles (emulate G81/G83), no G68 rotation (bake transforms into
  coordinates), laser mode `$32=1`, `M4` dynamic power, `S` scaled to `$30`.
- **grblHAL / FluidNC**: mostly GRBL plus more; different `$` settings, more axes.
- **LinuxCNC / Mach3**: real canned cycles, G68 available, subroutines available.
- **Marlin (laser)**: `M3 I`/`M5`, `M106` fan-as-laser on some builds.

The **G-code processor chain** — an idea taken from UGS's `gcode/processors` package (design
only; see [Licensing](01-Architecture.md#9-licensing-strategy)) — is the right shape here. Each
processor is a small, testable, order-dependent transform over the command stream:

```
ArcExpander? → HeightMapLeveler → CoordinateTransform (fiducial alignment) → Mirror →
DecimalTrimmer → ModalCompressor → LineLengthLimiter → CommentStripper
```

Making alignment and levelling *processors* rather than special cases in the emitter means they
compose, they can be applied to externally supplied G-code too, and each is a 100-line class with
its own unit tests.

## 2. Laser output

### 2.1 What to burn

For the mask workflows, the geometry to ablate is derived from the board model, not from a
generic image:

| Pass | Region to remove mask from | Source |
|---|---|---|
| Etch resist | **Non-copper** area (so acid eats it) — i.e. board outline minus copper | Copper layer, inverted, clipped to outline |
| Solder-mask openings | **Pads only** | Gerber X2 `.AperFunction = SMDPad / ComponentPad`, optionally excluding `ViaPad` |
| Silkscreen burn | Silk layer geometry | Silk Gerber |
| Fiducials | Fiducial marks | Generated (§4) |

Being able to say "pads only, excluding vias, excluding U3" precisely — because we kept the X2
attributes — is the feature that makes Use Case 1 practical instead of fiddly.

### 2.2 Compensations

Two compensations that no free tool applies and that determine whether the board works:

- **Kerf compensation.** The beam has a finite spot (typically 0.06–0.15 mm on a diode laser).
  Removing mask along a boundary widens the removed region by one spot radius on each side.
  Offset the removal geometry **inward by the spot radius** so the resulting feature lands on
  nominal. Calibrate the spot size with a generated test pattern (§2.5).
- **Etch bias.** Ferric chloride / persulphate undercut the resist; a 0.2 mm trace comes out
  0.16 mm. Apply a per-process bias (default 0.02 mm per edge, calibrated) that **shrinks the
  removal region** so the finished copper lands on nominal. Expose it per-recipe because it
  depends on etchant, temperature, and agitation.

Both are simple Clipper2 offsets — the value is in *knowing they are needed* and in giving the
user a calibration routine rather than a number to guess.

### 2.3 Raster fills

For clearing large mask areas:

- **Scanline generation** at a configurable line interval (default = 0.8 × spot size for overlap).
- **`M4` dynamic power mode** on GRBL: power scales with actual feed, so corners and
  acceleration ramps don't over-burn. This is the single most important laser G-code detail and
  it is why hand-rolled generators produce dark edges.
- **Overscan**: extend each scanline past the burn region by `v²/(2a)` (the acceleration
  distance) with the laser off, so the head is at constant velocity throughout the burn. Compute
  it from the machine profile rather than making the user guess.
- **Bidirectional scanning** with a **scan-offset (backlash) compensation** term, calibrated by a
  generated test pattern. Without it, bidirectional rasters show a comb edge.
- **Angle control**: raster at 0°/90°/45°, chosen to minimise the number of scanline
  start/stops for the given geometry (long thin regions should be scanned along their length).
- No dithering — mask removal is binary, so solid on/off with a single power. (Dithering is for
  photo engraving and would be actively harmful here.)

### 2.4 Vector passes

- Outline the region boundary **first**, at a lower power/higher pass count, to get a crisp edge,
  then raster-fill the interior. Same principle as "cut the outline, then clear the pocket".
- Multi-pass with per-pass power/feed.
- Air assist `M8`/`M9`, laser-safe start (`S0` before the first move), and a postamble that
  guarantees the beam is off.

### 2.5 Calibration generators

Ship G-code generators for:

- **Spot-size / kerf test** — a comb of lines at descending widths; measure which ones close up.
- **Power/feed matrix** — a grid of squares at varying S and F; pick the cell that cleanly clears
  mask without damaging copper.
- **Scan-offset test** — bidirectional bars at several offsets; pick the aligned one.
- **Focus ramp** — a diagonal line at varying Z to find focus.

These take five minutes to write and save every user an hour of trial-and-error. They also feed
their results straight back into the machine profile, so the numbers get *used* rather than
written on a sticky note.

### 2.6 LightBurn / external-laser escape hatch

Many laser users have controllers that don't take G-code (Ruida, and anything driven by
LightBurn). Rather than chase every controller, make **"export to LightBurn"** a first-class
output: an SVG or DXF at exact mm scale, with geometry split into layers named `C00`, `C01`, …
so LightBurn auto-assigns them to its cut layers, with fills and outlines separated, and with the
fiducials on their own layer. See [05 § SVG](05-Viewer-and-Export.md).

This is a cheap feature that instantly widens the addressable hardware to "all of it".

## 3. The Job model — how mixed workflows are expressed

The central abstraction, and the answer to *"mixed use is the most difficult to achieve"*:

```
Job
 └── Step[]                      ordered, each is either:
      ├── Operation              machine work: has a Setup, a MachineProfile, toolpaths, G-code
      └── ManualStep             human work: "etch for 12 min", "spray mask, cure 30 min"

Setup = (MachineProfile, Fixture, CoordinateFrame)
```

A **Setup** is "the board is mounted in this machine, in this fixture, in this frame". Every
Operation belongs to exactly one Setup. **A change of Setup is a re-registration event** — and
that is precisely where alignment error creeps in. Making it an explicit first-class object means
the app can *see* every place alignment is needed and handle it automatically instead of leaving
it to the user to notice.

### Use Case 1 — laser-etch-resist, then mill

| # | Step | Setup | Notes |
|---|---|---|---|
| 1 | Laser: burn **fiducials** | A (laser) | Auto-inserted. Marks must survive etching → see §4.2 |
| 2 | Laser: burn mask over **non-copper** | A | Kerf + etch-bias compensated |
| 3 | *Manual: etch, rinse, strip, re-mask* | — | Checklist with timer |
| 4 | Laser: **re-align to fiducials** | A′ | Board was removed; A′ is a new frame |
| 5 | Laser: burn mask over **pads only** | A′ | From X2 pad attributes |
| 6 | *Manual: move board to the mill* | — | |
| 7 | Mill: **align to fiducials/fixture** | B (mill) | The hard one |
| 8 | Mill: drill | B | |
| 9 | Mill: cut outline with tabs | B | Last, always |

### Use Case 2 — mill traces, mask in place, mill outline, laser pads

| # | Step | Setup | Notes |
|---|---|---|---|
| 1 | Mill: **fiducials** (spot-drill or engraved cross) | B | Auto-inserted; these are what the laser will find later |
| 2 | Mill: isolation routing | B | |
| 3 | Mill: drill | B | |
| 4 | *Manual: spray mask, cure* | — | **Board never leaves the machine** |
| 5 | Mill: cut outline with tabs | B | **Same Setup — no re-registration needed.** The app says so explicitly and preserves the WCS. |
| 6 | *Manual: move to laser* | — | |
| 7 | Laser: **align to fiducials** | A | |
| 8 | Laser: burn mask over pads | A | |

Note step 5: because the board never leaves the mill, there is *no alignment step*. The app
should recognise same-Setup continuity and tell the user "do not touch the work zero" rather
than making them wonder. Half of good workflow software is telling people what *not* to do.

### Other workflows the same model covers for free

- Mill isolation + mill outline only (classic pcb2gcode).
- Laser-only (mask, etch, and outline scored + snapped by hand).
- Double-sided: top ops, flip, bottom ops — a flip is just a Setup change with a mirror in its
  frame.
- Panelised boards: an array transform on the Setup frame.
- Solder-paste stencil cutting on the laser from the paste Gerber.

## 4. Board re-alignment — the hard problem

**Goal: ≤ 50 µm registration between machines.** That is what a 0.2 mm trace needs.

Three mechanisms, in order of preference.

### 4.1 Primary: a physical registration fixture

The best alignment problem is one you don't have to solve twice.

- Design a **reusable registration plate**: MDF or acrylic with two (or three) dowel pins at a
  known spacing, plus a flat reference edge. The app **generates the G-code to make it** on the
  mill.
- The plate is calibrated **once per machine**: the operator locates the two pins in their sender
  and types the coordinates in; we store the `machine ↔ plate` transform in the MachineProfile.
  Do it once, ever — this is the payoff, and it's why the fixture is the recommended path even
  though we can't drive the machine.
- The stock is drilled with matching pin holes in the **first operation of the job**, before it is
  ever removed. From then on, dropping the board on the plate reproduces its position to the
  fit of the pins — typically 20–50 µm with 3 mm dowels in reamed holes.
- For a board already cut out, generate a **nest pocket** matching the outline (plus a clearance
  fit), with an **asymmetric corner key** so it physically cannot be inserted rotated 180°.

This converts a per-job alignment problem into a one-time calibration. It should be the
recommended path and the app should walk the user through building the plate on first run.

### 4.2 Fiducials + measurement

Needed when there is no fixture, when verifying a fixture, or for double-sided work.

**Fiducial design matters more than the maths.** The mark must survive every process step in
between:

| Workflow | Fiducial that survives |
|---|---|
| Laser mask → acid etch | A **copper island** (a filled donut or a cross) that the resist protects, so etching *creates* it in relief. A burned-away-mask mark would be destroyed by the etch. |
| Any workflow with a mill step | **Drilled holes** (2–3, Ø 1.5–3 mm, outside the outline but in the stock). Probeable, scope-visible, and survives everything. |
| Mill-first | An **engraved cross** (V-bit, 0.3 mm deep) — easy to centre a crosshair on, and probeable if cut as a small conical dimple. |

Default: **two drilled holes + one copper cross**, all in the waste frame outside the board
outline. Automatically placed as far apart as the stock allows (long baseline = low angular
error), never collinear when three are used.

**Measurement happens in the operator's sender, not in PCB_MillBurn** ([01
§1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-does-not-drive-machines)). We never touch
the serial port. The workflow is *numbers in, G-code out*:

1. PCB_MillBurn tells the operator the **nominal** fiducial coordinates and shows a picture of
   where they are.
2. The operator uses **whatever sender they already run** — UGS, Candle, LightBurn, LinuxCNC — to
   jog to each mark and reads the DRO.
3. They type the measured X/Y into PCB_MillBurn's alignment dialog (a small grid: nominal vs.
   measured, per fiducial).
4. We fit the transform, report the residual, and emit re-registered G-code.

Measurement techniques the operator can use, in increasing accuracy — all of them theirs, not ours:

| Method | Accuracy | Notes |
|---|---|---|
| Jog by eye to the mark | ~50–100 µm | Free. Adequate for 0.4 mm+ trace boards. |
| USB microscope with a crosshair | ~20–30 µm | A $20 scope taped to the spindle. Best effort-to-accuracy ratio by far; recommend it in the docs. |
| Touch probe into the registration holes | ~10–25 µm | **We generate the probe routine as G-code**: 4 `G38.2` touches around each bore, with the sender logging positions. The operator pastes the four numbers back, or imports the sender's probe log; we do the least-squares circle fit and derive the centre. Fully numeric, no eyeballing. |
| Edge finder / 3D-taster | ~10 µm | Same flow — read numbers, type them in. |

Because we only consume coordinates, this works with **every controller and every sender that has
ever existed**, and there is no firmware-compatibility surface to maintain. A camera-based
auto-detect would require us to own the hardware loop, so it stays out of scope; the microscope
crosshair gets the same accuracy for $20 and no code.

**Make the numeric part painless**, since that's the whole interface: paste-friendly input
(accept `X12.345 Y67.890`, tab-separated, or a pasted GRBL `?` status line and parse it),
per-fiducial residual shown live as the numbers are entered, and a clear "this is worse than your
last alignment" warning.

**The maths.** Given measured points `mᵢ` and their nominal board coordinates `bᵢ`:

- 2 points → **similarity transform** (rotation + translation, optional uniform scale, optional
  mirror). Closed form.
- 3+ points → least-squares **affine** fit (6 DOF) which also captures scale and shear — real
  and worth correcting, because laser optics and thermal expansion in FR4 both introduce it.
- Use **Kabsch/SVD** for the rigid case; it is numerically stable and handles the mirror case
  explicitly via the determinant sign (important for double-sided work, where an accidental
  mirror flip is a board in the bin).
- **Report the residual RMS to the user** — *"fit RMS 18 µm — good"* / *"fit RMS 210 µm — check
  your fiducial picks"*. A number the operator can judge is what makes this trustworthy. Refuse
  to post a job whose residual exceeds a threshold without an explicit override.

**Applying the transform.** Since we don't drive the machine, we cannot set a work offset or issue
a G68 — and GRBL has no G68 anyway. So there is exactly one correct answer, and happily it's the
best one: **bake the transform into the coordinates at post time**, as a processor in the chain
(§1). Every point in the emitted file is already in the aligned frame. This works on every
controller and every sender, needs nothing from the operator beyond loading the file, and means
the backplot shows *literally* what will run. On controllers that do support G68/G10 L2, offer
that as an option for a cleaner file — but never as the default.

The operator's whole job becomes: set work zero wherever they measured from, load our file, run.

### 4.3 Verification before committing

Never let the first confirmation that alignment worked be a ruined board. Since we can't watch the
machine, we hand the operator artefacts that let *them* check:

- **A generated dry-run file** — the toolpath outline and fiducial positions traced at safe Z
  (mill) or at 0.5% "pointer" power (laser), as a separate `.nc` they run first and watch. This is
  the single most valuable safety feature in the app and it costs almost nothing to generate.
- **Backplot in the aligned frame**, with the previous operation's result ghosted underneath, so
  the registration is visibly correct on screen before anything moves.
- **A printed 1:1 overlay** from the [documentation SVG profile](05-Viewer-and-Export.md#33-export-profiles):
  print the fiducial positions at exact scale, lay it on the board, confirm they land. Crude,
  free, and catches gross errors like a mirrored transform instantly.
- **Refuse to export** when the fit residual exceeds the threshold, unless explicitly overridden.

### 4.4 Alignment records are persisted

Every alignment is stored in the project file: measured points, computed transform, residual,
timestamp, operator note. So a job can be resumed the next day, a failed board can be diagnosed
after the fact, and a repeated job can start from last time's numbers.

## 5. Height mapping (autolevelling)

Isolation milling at 0.05 mm depth on a board that is 0.15 mm out of flat does not work. pcb2gcode
has an autoleveller; ours is **generate-and-import**, because we don't drive the machine:

**The flow.** We *emit* the probing routine as a standalone G-code file (a `G38.2` grid over the
board region, with `G10`/report lines the sender will log). The operator runs it in their sender,
which records the results. We *import* that log and apply the correction. UGS's
`ugs-platform-surfacescanner`, Candle's heightmap tool, and bCNC's autolevel all already produce
files in this shape — support their formats plus a plain CSV, and this is a five-minute step for
the user with zero serial code on our side.

Improvements over pcb2gcode:

1. **Probe over the actual board region only**, adaptively densified where the surface is likely
   to change fastest (near clamps and edges), rather than a fixed grid over the bounding box.
   Fewer probe points for the same accuracy, which matters when each one costs wall-clock time.
2. **Thin-plate-spline / biharmonic interpolation** instead of bilinear — smoother Z, no visible
   grid artefacts in the cut.
3. **Reuse across operations in the same Setup**, and **transform the height map** when the Setup
   changes, so probing once covers drill + isolation + outline.
4. **Import a map probed by any tool**, including one made months ago for the same fixture.

Implemented as a processor in the chain, so it also applies to imported third-party G-code — which
makes PCB_MillBurn useful as a standalone levelling utility. Visualise the surface as a
colour-mapped mesh in the viewport; users immediately see that their stock is bowed, which is
educational in itself.
