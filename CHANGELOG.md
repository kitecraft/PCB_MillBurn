# Changelog

What changed in each release, and where it was proven. The full notes live on the
[releases page](https://github.com/kitecraft/PCB_MillBurn/releases); this file is the copy that
travels with the repository, so a clone or a mirror still has the history.

**Versions, pre-1.0:** a sprint moves the middle digit, an urgent fix shipped mid-sprint moves the
last one, and a change to a CLI workflow or to a saved file's shape is marked **Breaking** here and
in the first line of the release notes. See [CONTRIBUTING](CONTRIBUTING.md#versions).

---

## [0.2.0](https://github.com/kitecraft/PCB_MillBurn/releases/tag/v0.2.0) — 2026-09-24

**Seamless usability.** The product owner's title for it, and a fair one: nothing here is a feature.
It is the release where the window stops freezing, where the application stops being surprised by
an export it did not write, and where what it cannot do it says so about.

[Sprint 1 — Speed and accuracy](Sprints/Sprint-01-Speed-and-Accuracy.md). Five stories delivered —
the pipeline off the UI thread and cancellable, memoised stages, the optimizer's two open items,
an electrical check against the X2 netlist, and the outline's wasted vertical moves. A sixth was
closed by measuring it rather than building it: reading the `.gbrjob` file turned out to duplicate
what every Gerber already declares, and to offer the designer's nominal thickness where the mill
needs the stock on the bed.

The working agreement itself — sprints, a release branch, squashed story branches, review
before every merge, a written style, `AGENTS.md` for any assistant, and five statements in the
architecture document that had stopped being true.

And the scaffolding a public repository needs: CI on release branches, a pinned SDK, a pull-request
template, this file, and an `.editorconfig` whose every enforced rule was checked by turning it on
and building — the first draft's naming and layout sections turned out to be decoration.

**The optimizer stops chasing its tail, and the outline stops climbing.** A local search that could
apply 321,413 "improvements" on twenty-five nodes and settle somewhere worse now converges in
fifteen, because a move it could not cost is no longer applied; and a channel cut at several depths
can be entered from either end, which on a 66-up panel takes the cut-out's travel from 1850 mm to
337. The outline no longer retracts to the safe height between laps that start where the last one
ended, nor climbs the whole board to cross a 3 mm tab: 45.4 mm of vertical motion down to 27.4 on the
test board, and about eight minutes off the panel's cut-out.

Four found at the bench and cleared before the sprint's second half: copper sealed inside the board
can no longer be set to G-code, and a project saved when it could opens corrected and says so; an
Excellon file that states its units as `M72` is read as inches, instead of reporting every drill on
the board at a twenty-fifth of its size; Preview stops unticking layers a freshly opened project had
visible; and an export whose file names are not KiCad's is read by its words rather than by
substring.

**The isolation check names what it cannot separate.** Where the tool does not fit, the picture
draws nothing — which looks exactly like a gap that needed no cutting, and the board is the first
place anybody finds out. After planning, the application compares the copper a cut can actually
divide against the netlist the Gerbers already declare, and says *"AREF and AVCC are left connected:
the gap between them is narrower than the 0.154 mm this cut is wide"* instead of counting gaps
nobody can find. A layer that names no nets is reported as unchecked rather than as clean.

**Every net on every board was one trace out of step.** Found by disbelieving that check: it
reported 110 shorts on an Arduino Mega that demonstrably works. The parser batches a trace's strokes
into one object and read its net attributes when the object was *emitted* rather than when it was
*drawn*, so a trace drawn under one net was filed under the next — and `%TD*%` stripped the net from
a trace still open, dropping it from the netlist entirely. Nothing in the emitted G-code changes;
every netlist does. No test failed at any point: the suite was green before and after.

**What a board costs to compute is now written down.** Clipper booleans, point-in-polygon questions
and the vertices handed to them are counted per board and recorded, so a change in the price of a
board is a diff somebody has to account for. It caught two regressions in its own first week,
including one of 209,752 point tests on the Mega.

**And the documents can be read.** A generated [backlog](Documentation/11-Backlog.md) lists what is
open and what kind of thing it is, because the roadmap had grown to sixty thousand words with no way
in; the requirements matrix and that backlog now both count themselves, after the summary was found
claiming seven not-started rows where the table below it listed three.

## [0.1.6](https://github.com/kitecraft/PCB_MillBurn/releases/tag/v0.1.6) — 2026-09-20

**Measure the machine, not just the bit — and an About window worth opening.**

- **Machine checks**: backlash and squareness, built from plunged holes, with a companion page whose
  diagram is drawn from the same hole positions the program was emitted from. *Job ▸ Machine
  checks…*, `machine-check` on the command line, and a guide with photographs of where the caliper
  jaws go.
- **About**: version, build date, a manual **check for updates** that sends nothing and never
  guesses, and the travel optimizer running live on a scatter of pads.
- Third-party notices ship as a help page rather than a `.md` the operating system hands to an
  editor; *Drill alignment ▸ Start from the program*; About takes its owner's theme.
- **On metal:** the author's mill measured at under 0.05 mm of backlash and 0.068° out of square —
  the readings that closed [investigation 09 §1](Documentation/09-Machine-Accuracy-Investigations.md).

## [0.1.5](https://github.com/kitecraft/PCB_MillBurn/releases/tag/v0.1.5) — 2026-09-20

**A board made with it — and a download you can find the app in.**

- **Placing layers in every SVG**: the board outline, and the stock with its alignment holes, as
  layers of their own, so software that imports the drawing rather than the page still lands every
  file in the same place. Chosen under *Settings ▸ Laser*.
- The project page gives each SVG's own import size and placement.
- Fixed: an inverted, mirrored layer on off-centre stock came out 5 mm across.
- **Releases unpack to two programs**, `MillBurn` and `millburn-cli`, instead of three hundred files.
- **On metal:** a double-sided board start to finish — stock, both coppers by laser, etch, drill and
  cut out from the waste holes, then soldermask and legend. *"The mask and silkscreen alignment is
  perfect."*

## [0.1.4](https://github.com/kitecraft/PCB_MillBurn/releases/tag/v0.1.4) — 2026-09-19

**Routing you can choose, settings you can find, SVGs that land where they belong.**

- Routed holes and slots get settings of their own: what a short last lap becomes, and whether a
  through cut still gets a flat lap. The routing file names where every number came from.
- Settings moved onto tabs, and a problem that blocks Save names its tab.
- The project page tells each SVG where it goes for software that imports by content.

## [0.1.3](https://github.com/kitecraft/PCB_MillBurn/releases/tag/v0.1.3) — 2026-09-19

**Holes you can see, stock you can hide.**

- The stock's alignment holes are drilled rather than spiralled, so they are the bit's own width;
  pre-cut stock can have its holes and nothing else.
- Plunged holes are drawn in the backplot; a Stock chip hides the stock's paths.
- *File ▸ Close project*, recent projects on the empty panel, and no refresh list on every open.

## [0.1.2](https://github.com/kitecraft/PCB_MillBurn/releases/tag/v0.1.2) — 2026-09-19

**Alignment you type, tabs that snap, and a layer panel that names things.**

- Drill alignment takes the measured position rather than an offset, and a second hole corrects the
  board's rotation as well as its shift.
- **On metal:** tabs came out full thickness. The outline now cuts one extra pass at the tab top, so
  they are left partial and the board snaps out.

## [0.1.1](https://github.com/kitecraft/PCB_MillBurn/releases/tag/v0.1.1) — 2026-09-17

Fixes and polish from the first round of testing after v0.1.0 — every change to what gets cut was
run on a real machine.

## [0.1.0](https://github.com/kitecraft/PCB_MillBurn/releases/tag/v0.1.0) — 2026-09-15

The first public release. Gerber in; G-code for the mill and SVG for the laser out.
