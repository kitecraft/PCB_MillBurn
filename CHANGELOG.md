# Changelog

What changed in each release, and where it was proven. The full notes live on the
[releases page](https://github.com/kitecraft/PCB_MillBurn/releases); this file is the copy that
travels with the repository, so a clone or a mirror still has the history.

**Versions, pre-1.0:** a sprint moves the middle digit, an urgent fix shipped mid-sprint moves the
last one, and a change to a CLI workflow or to a saved file's shape is marked **Breaking** here and
in the first line of the release notes. See [CONTRIBUTING](CONTRIBUTING.md#versions).

---

## Unreleased — `release/0.2.0`

[Sprint 1 — Speed and accuracy](Sprints/Sprint-01-Speed-and-Accuracy.md). No features: the pipeline
off the UI thread and cancellable, memoised stages, the optimizer's two open items, electrical DRC
against the X2 netlist, the job file and block apertures, and the outline's wasted vertical moves.

So far: the working agreement itself — sprints, a release branch, squashed story branches, review
before every merge, a written style, `AGENTS.md` for any assistant, and five statements in the
architecture document that had stopped being true.

And the scaffolding a public repository needs: CI on release branches, a pinned SDK, a pull-request
template, this file, and an `.editorconfig` whose every enforced rule was checked by turning it on
and building — the first draft's naming and layout sections turned out to be decoration.

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
