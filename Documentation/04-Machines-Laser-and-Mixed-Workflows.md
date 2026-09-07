# 04 — Machines, Laser Output & Mixed Workflows

pcb2gcode has no concept of a laser (`grep -i laser src/` finds two mentions, both in the help
text for "run this custom G-code before each trace, e.g. to turn on a fan"). It also has no
concept of a job that spans two machines. Both are core requirements here, and the second one —
mixed use — is where all the difficulty lives.

## 1. Two machines, two output formats

**The mill takes G-code. The laser takes SVG.** That split runs through the whole design, and it
is deliberate.

| | Mill | Laser |
|---|---|---|
| Output | G-code (`.nc`) | **SVG** (and DXF) at exact mm scale |
| Consumed by | GRBL / grblHAL / LinuxCNC, via the operator's sender | LightBurn, LaserGRBL, RDWorks, xTool Creative Space |
| What we decide | Every coordinate, feed, depth, and travel move | The geometry, its layer assignment, and the scale |
| What the target decides | Nothing | Power, speed, passes, fill interval, overscan, dithering |

The reason is not laziness. Laser control software is *good at being laser control software* —
LightBurn's fill engine already does bidirectional scanning with overscan, scan-offset
compensation, and power-versus-speed correction, tuned per machine, against a material library
the user has already calibrated. Emitting our own laser G-code would mean reimplementing all of
that, worse, and it would restrict the tool to GRBL when a large share of laser hardware (Ruida,
Trocen, most galvo controllers) does not speak G-code at all.

What laser software is *bad* at is knowing what a PCB is. It cannot tell a via pad from an SMD
pad, it cannot invert copper against a board outline, and it has no idea that the etchant will
undercut the resist by 20 µm. **That is our half of the job, and it is entirely geometry.** We
hand over a correctly compensated, correctly layered vector drawing and get out of the way.

The consequence for the codebase is worth stating plainly: `MillBurn.Gcode` and `MillBurn.Post`
are **mill-only**. Laser output is `MillBurn.Export`. There is no laser dialect, no `M4` power
scaling, no raster scanline generator, no laser postamble, no air-assist handling. That is a
large volume of fiddly, machine-specific, hard-to-test code that we now simply never write — and
correspondingly, the SVG writer is not a side feature. It is half the product.

### 1.1 Mill profile

```csharp
sealed record MillProfile(
    string Name,
    Dialect Dialect,                // Grbl | GrblHal | FluidNc | LinuxCnc | Mach3
    Kinematics Kin,                 // work volume, max feed per axis, acceleration per axis,
                                    //   junction deviation - feeds the time model
    Spindle Spindle,                // RPM range, spin-up time
    ImmutableArray<Tool> Tools,     // available tools, changer or manual
    ProbeConfig Probe,              // G38.2 support, probe plate thickness, tool-length sensor
    GcodeTemplates Templates);      // preamble / postamble / toolchange (Scriban)
```

#### Post-processor

Template-driven rather than hard-coded, because every controller has quirks:

- **GRBL 1.1**: no canned cycles (emulate G81/G83), no G68 rotation (bake transforms into
  coordinates), no subroutines.
- **grblHAL / FluidNC**: mostly GRBL plus more; different `$` settings, more axes.
- **LinuxCNC / Mach3**: real canned cycles, G68 available, subroutines available.

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

### 1.2 Laser profile

Much smaller, because it describes the *beam* and the *hand-off*, not a controller:

```csharp
sealed record LaserProfile(
    string Name,
    long SpotSizeNm,                // measured with the kerf comb (2.4); drives kerf compensation
    long BedWidthNm, long BedHeightNm,
    SvgFlavour Flavour,             // LightBurn | Inkscape | Generic
    LayerPalette Palette,           // geometry role -> stroke colour, so the target auto-assigns
    bool MirrorForBottomSide);
```

There is no feed, no power, and no pass count here, and there should not be. Those belong to the
user's material library in their laser software, where they are already calibrated for their
machine and their mask, and where they can be changed without regenerating anything.

## 2. Laser output — what goes in the SVG

### 2.1 What to burn

For the mask workflows, the geometry to ablate is derived from the board model, not from a
generic image. This is the step that needs Gerber semantics, and therefore the step no laser
program can do for you:

| Pass | Region to remove mask from | Source |
|---|---|---|
| Etch resist | **Non-copper** area (so acid eats it) — i.e. board outline minus copper | Copper layer, inverted, clipped to outline |
| Solder-mask openings | **Pads only** | Gerber X2 `.AperFunction = SMDPad / ComponentPad`, optionally excluding `ViaPad` |
| Silkscreen | Silk layer geometry (§2.5 — the simplest operation here) | Silk Gerber |
| Registration marks | Alignment targets | Generated (§4) |

Being able to say "pads only, excluding vias, excluding U3" precisely — because we kept the X2
attributes — is what makes Use Case 1 practical instead of fiddly.

### 2.2 Compensations — the part that has to be right

Two compensations that no laser program applies, because none of them knows it is looking at a
PCB. Both are Clipper2 offsets applied to the geometry **before** it reaches the SVG, so what the
operator imports is already correct and they never have to think about it again.

- **Kerf compensation.** The beam has a finite spot (typically 0.06–0.15 mm on a diode laser).
  Removing mask along a boundary widens the removed region by one spot radius on each side.
  Offset the removal geometry **inward by the spot radius** so the resulting feature lands on
  nominal. Calibrate the spot size with a generated test pattern (§2.4).
- **Etch bias.** Ferric chloride and persulphate undercut the resist; a 0.2 mm trace comes out
  0.16 mm. Apply a per-process bias (default 0.02 mm per edge, calibrated) that **shrinks the
  removal region** so the finished copper lands on nominal. Expose it per-recipe, because it
  depends on etchant, temperature, and agitation.

LightBurn does have a kerf-offset field, but it applies one global number per layer with no
knowledge of which side is material — and it cannot express etch bias at all. Doing it here, as
signed polygon offsets against real board geometry, is both more correct and invisible to the
user. **Show the applied compensation in the export dialog** (*"outlines shrunk 0.055 mm =
0.045 kerf + 0.010 etch bias"*) so the number is auditable rather than magic.

### 2.3 Fill versus line, and why the SVG must say which

A laser program decides *how* to burn a shape from its layer settings — Line, Fill, or Offset
Fill — not from the SVG's own `fill` attribute. So the SVG's job is to put each piece of geometry
on the layer that has the right mode. In LightBurn that assignment happens **by stroke colour**:
an imported object whose stroke matches a palette entry lands on the corresponding cut layer.

That gives a clean contract:

| Geometry | Layer | Mode the user sets, once |
|---|---|---|
| Mask-removal regions (etch resist, pad openings) | `C01` | **Fill** |
| Region boundaries, when a crisp edge is wanted | `C02` | Line — low power, runs first |
| Silkscreen centrelines | `C03` | Line |
| Registration marks | `C00` | Line, or assigned as a "tool" layer |
| Board outline, reference only | `C08` | **Output off** — never burn it; it is there to see |

Ship this as a **LightBurn layer preset** next to the SVG, so the settings are applied once and
then reused for every board. The palette lives in the `LaserProfile` rather than in code, because
the RGB values have to match the target's palette exactly; verify them against the installed
version rather than trusting a table in a document.

Two rules the SVG writer must obey or fills silently misbehave:

- **Closed subpaths.** Every fill region ends with `Z`. An open path fills unpredictably.
- **Holes are subpaths of the same `<path>`**, with `fill-rule="evenodd"` — not separate shapes.
  A pad's inner clearance has to be a hole in the same path, or it burns solid.

### 2.4 Calibration generators

Generated as SVG, so they travel the same path as a real job:

- **Spot-size / kerf comb** — parallel lines at descending gaps; the narrowest gap that stays open
  gives the kerf, which feeds straight back into `LaserProfile.SpotSizeNm`.
- **Registration repeatability test** — burn the marks, re-home, burn them again; the doubling
  shows the repeatability the machine actually has, which bounds everything in §4.

Power/feed matrices and focus ramps are **deliberately not generated**. LightBurn's built-in
material test does them better and writes the result straight into the user's material library.
Point at that rather than duplicating it.

### 2.5 Silkscreen marking — the easiest win on the board

Worth calling out separately, because it is far simpler than every other operation here and
delivers something people actually want: **legible component labels on a home-made board.**

Parsing a real KiCad silk layer shows why. `PogoTest1-F_Silkscreen.gbr` is **217 stroked draws,
two apertures (0.10 mm and 0.15 mm), and zero flashes.** It is pure vector line art. Compare that
with copper, which needs region compositing, polarity ordering, offsetting and DRC.

**The key coincidence: silk stroke width ≈ laser spot size.** Silk is drawn with a 0.10–0.15 mm
round aperture; a diode laser spot is 0.06–0.15 mm. So for the common case the correct output is
simply *the stroke centrelines* — the beam is already the right width. No fill, no offsetting, no
kerf compensation. The Gerber's own draw segments are the geometry, and the SVG is a direct
transcription of them.

That makes silkscreen the one operation that needs **no geometry realisation at all**: no aperture
rendering, no polygon compositing, nothing between the parser and the SVG writer. It is
consequently the first thing this project can usefully ship.

Rules the generator applies:

| Condition | Output |
|---|---|
| Stroke width within ~1.5× the spot | Centrelines as open paths, on the Line layer, at 1× |
| Stroke wider than that | Outline the stroked shape and put it on the Fill layer |
| Bottom silk | Mirror through the setup frame, same as any bottom-side layer |

Two ways to use it, and they want different power settings:

- **Direct marking.** Burn the bare FR4 or the cured solder mask. The substrate darkens and the
  legend is permanently legible with no chemistry at all. This is a standalone job — it needs no
  masking, no etching, and no registration beyond the usual marks.
- **Mask-and-etch**, exactly like the copper workflows, when the legend should be etched rather
  than burned.

Practical limits to surface in the UI rather than let people discover:

- **Minimum legible character height is about 0.8 mm** on a diode laser. KiCad's default silk text
  is 1.0 mm, so most boards are fine — but flag anything below the threshold before it burns into
  an unreadable smudge.
- **Marking does not work on bare copper.** Copper reflects, and conducts the heat away. Silk
  marking belongs after masking, or on the substrate side.
- **Order it before the cutout**, while the board is still held by the stock.
- Marking settings are *far* lower power than mask ablation. Say so in the runbook, and point at
  the laser software's material test rather than shipping a competing one.

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

The mill and the laser need different answers, because only one of them takes our
coordinates. Both start from the same place: don't solve the problem twice.

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

### 4.2 Fiducials + measurement (mill)

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
§1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-writes-files-it-does-not-drive-machines)). We never touch
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

### 4.3 Registering on the laser — placement, not coordinates

The mill flow above is *numbers in, G-code out*: the operator reads a DRO and we bake a transform.
**On the laser none of that applies**, because we do not emit the motion. The operator imports an
SVG and positions it, so laser registration is a *placement* problem and the transform is applied
by their software, not by us. What we owe them is a drawing that makes placement easy and a
mistake obvious.

**Every SVG exported from one Job shares one origin and one page size.** This is the single most
important rule in the whole laser path, and it is easy to violate by accident: if the etch-resist
export is cropped to the copper extents and the pad export is cropped to the pad extents, the two
will not overlay when imported, and the second burn will be offset by the difference. Fix the page
to the **stock outline plus a documented margin**, for every layer, every time — including the
calibration files. Assert it in the golden tests.

Three ways to register, in order of preference:

- **A jig cut on the laser itself.** The direct analogue of the dowel plate (§4.1), and better than
  it, because a jig cut by the laser is in the laser's own coordinate frame by construction — every
  error in the machine cancels out. Cut an L-shaped corner stop or a nest pocket from scrap at a
  known SVG position, leave it clamped to the bed, and drop the board into it. Placement then costs
  nothing per job and repeats to the machine's own accuracy. The app generates the jig SVG.
- **Two-point registration in the laser software** (LightBurn calls this *Print and Cut*): the
  design carries two marks, the operator jogs the head to each physical mark in turn and captures
  it, and the software solves the offset, rotation and scale. This handles a board dropped on the
  bed at any angle, which is exactly the Use Case 1 situation after the board comes back from the
  etchant. We support it by always emitting **two marks on their own layer, as far apart as the
  stock allows**, and by listing their nominal mm coordinates in the export report and the runbook
  so the operator has the numbers to hand. Confirm the capture workflow against the installed
  version; the geometry we emit is the same either way.
- **A camera**, on machines whose software supports one. Entirely their alignment path — we just
  need to have put marks in the drawing.

**Marks that survive.** The same survivability table in §4.2 applies, with one addition specific
to this path: a mark burned into the *mask* is destroyed by the etch, so for Use Case 1 the
registration marks must be **copper islands** the resist protects — the etch then creates them in
relief and they are still there for step 4.

**Make a wrong flip impossible to miss.** Bottom-side work mirrors the geometry, and the marks
mirror with it. Two symmetric marks cannot distinguish a mirrored placement from a correct one, so
place a **third, deliberately asymmetric mark** — a different shape, off the line of the other two.
A board registered mirrored then looks visibly wrong on the bed before anything burns, which is
worth more than any amount of arithmetic. The same asymmetry defeats a 180° rotation.

### 4.4 Verification before committing

Never let the first confirmation that alignment worked be a ruined board. Since we can't watch the
machine, we hand the operator artefacts that let *them* check:

- **A generated mill dry-run file** — the toolpath outline and fiducial positions traced at safe
  Z, as a separate `.nc` they run first and watch. This is the single most valuable safety feature
  in the app and it costs almost nothing to generate. The laser equivalent is already built into
  every laser program (frame / preview the job at low power), so we do not duplicate it — but the
  runbook step says to do it.
- **Backplot in the aligned frame**, with the previous operation's result ghosted underneath, so
  the registration is visibly correct on screen before anything moves.
- **A printed 1:1 overlay** from the [documentation SVG profile](05-Viewer-and-Export.md#33-export-profiles):
  print the fiducial positions at exact scale, lay it on the board, confirm they land. Crude,
  free, and catches gross errors like a mirrored transform instantly.
- **Refuse to export** when the fit residual exceeds the threshold, unless explicitly overridden.

### 4.5 Alignment records are persisted

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
