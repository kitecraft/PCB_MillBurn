# PCB_MillBurn — Design Documentation

A modern, UI-driven replacement for `pcb2gcode`, targeting **CNC mills**, **laser engravers**, and
**mixed mill+laser workflows**. Desktop: Windows and Linux.

> **Scope: PCB_MillBurn writes files. It does not drive machines.** No serial port, no jogging,
> no streaming. UGS, Candle, LightBurn and LinuxCNC already do that job well. Where a workflow
> needs a measurement from the machine (fiducial alignment, height mapping), we *generate* the
> routine as G-code and *import* the numbers the operator's sender produced. See
> [01 §1.1](01-Architecture.md#11-scope-boundary--pcb_millburn-writes-files-it-does-not-drive-machines).
>
> **Two outputs, one per machine: G-code for the mill, SVG for the laser.** Laser software already
> owns power, speed, passes and fill strategy against a calibrated material library, and much laser
> hardware does not take G-code at all. What it cannot do is read a Gerber — so our half is the
> geometry: pad selection, copper inversion, layer assignment, and an SVG true to the design at
> 1:1. Kerf and etch-bias compensation stay with the laser software, which owns the beam and
> material settings they depend on — see [04 §2.2](04-Machines-Laser-and-Mixed-Workflows.md).
> See [04 §1](04-Machines-Laser-and-Mixed-Workflows.md#1-two-machines-two-output-formats).

## Documents

| # | Document | Contents |
|---|---|---|
| 01 | [Architecture](01-Architecture.md) | Solution layout, project graph, the scope boundary, incremental pipeline, licensing |
| 02 | [Gerber & Geometry Pipeline](02-Gerber-and-Geometry-Pipeline.md) | Gerber X2 + Excellon parsing, Clipper2 geometry, isolation, pocketing, drilling, the board outline |
| 03 | [Toolpath Optimization](03-Toolpath-Optimization.md) | Why pcb2gcode's travel is bad, the GTSP model, cost model, constraints, arc fitting |
| 04 | [Machines, Laser & Mixed Workflows](04-Machines-Laser-and-Mixed-Workflows.md) | **Mill takes G-code, laser takes SVG**; machine profiles, why kerf compensation is *not* ours, the Job/Operation/Setup model, **board re-alignment** |
| 05 | [Viewer & Export](05-Viewer-and-Export.md) | Real-time backplot viewer (UGS-informed), and the **SVG writer that is the entire laser output path** |
| 06 | [Roadmap & Risks](06-Roadmap-and-Risks.md) | Phased milestones, acceptance metrics, open questions |
| 07 | [UI Framework Decision](07-UI-Framework-Decision.md) | MAUI vs. Avalonia vs. WPF for this app, and why it's a cheap decision |
| 08 | [Requirements Matrix](08-Requirements-Matrix.md) | Every requirement in 01-07 traced to what exists in the tree, with the queue-order analysis |

## Reference checkouts in this workspace

| Path | What it is | License | How we use it |
|---|---|---|---|
| `WorkingFolder/pcb2gcode/` | The original C++ tool | GPL-3.0 | Behavioural reference. **Do not port code.** Never actually run — the optimizer's gate measures against our own nearest-neighbour baseline instead, for the reasons in [03 §8](03-Toolpath-Optimization.md#8-acceptance-criteria). |
| `WorkingFolder/Universal-G-Code-Sender/` | UGS (Java/NetBeans/JOGL) | GPL-3.0 | Design reference for the backplot viewer and the G-code processor chain. **Do not port code.** Also the sender this project's output is actually run through. |

`WorkingFolder/` is scratch space and is git-ignored in its entirety; both checkouts are the
developer's own and are not part of this repository.

Both are GPL-3.0. Reading them to understand *approach* is fine; copying code makes PCB_MillBurn
GPL-3.0 too — and PCB_MillBurn is **MIT**, so that rule is load-bearing rather than decorative. Everything we build should be clean-room from published specs
(Ucamco Gerber spec, Excellon, GRBL/LinuxCNC G-code docs) plus permissively licensed libraries
(Clipper2 = BSL-1.0, NetTopologySuite = BSD-3, SkiaSharp = MIT). See
[Architecture § Licensing](01-Architecture.md#9-licensing-strategy).

## The one-paragraph summary

pcb2gcode does the hard geometry well but treats G-code as a dumb text dump: no laser support,
travel ordering that ignores half the available freedom, debug-grade SVG, no preview, and no
concept of a *job* that spans two machines. PCB_MillBurn keeps the good geometry ideas, rebuilds
them on Clipper2 + NetTopologySuite in .NET, and adds the three things that actually matter for
the target workflows: **a real travel optimizer**, **laser output as production-grade SVG, true to
the design at 1:1**, and **a Job model that automatically plants registration marks
and re-registers the work when the board moves between the laser and the mill**. A live SkiaSharp
backplot viewer makes all of it verifiable before a single chip is cut. It stays a converter
throughout — files in, files out, and someone else's software runs them.
