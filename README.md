# PCB_MillBurn

A modern Gerber-to-G-code converter for **CNC mills**, **laser engravers**, and **mixed mill+laser
workflows** — a rethink of `pcb2gcode` with a real travel optimizer, first-class laser output, a
live toolpath viewer, and a job model that handles a board moving between machines.

Runs on **Windows and Linux**. Every project targets plain `net10.0`, there is no Windows-only API
in the codebase, and the GUI publishes for `linux-x64` cleanly — Avalonia supplies the X11 backend
and SkiaSharp the native rendering library.

**PCB_MillBurn converts files. It does not drive machines.** No serial port, no jogging, no
streaming — UGS, Candle, LightBurn and LinuxCNC already do that well. **The mill takes G-code;
the laser takes SVG** — see [04 §1](Documentation/04-Machines-Laser-and-Mixed-Workflows.md#1-two-machines-two-output-formats).

> **Status: Phase 1 complete.** Drop a Gerber export folder on the window and the board appears:
> parsers, geometry realisation (apertures, macros, strokes, regions, polarity), layer detection
> and the viewer are all in, clean on real boards and the full external corpus. Silkscreen exports
> as laser-ready SVG. Toolpaths and G-code are Phase 2 and not built yet.

## Documentation

Design docs live in [`Documentation/`](Documentation/README.md). Start there — the plan is
considerably more interesting than the code so far.

| Doc | Subject |
|---|---|
| [01](Documentation/01-Architecture.md) | Solution layout, incremental pipeline, scope boundary, licensing |
| [02](Documentation/02-Gerber-and-Geometry-Pipeline.md) | Gerber X2/X3 parsing, Clipper2/NTS geometry, isolation, DRC |
| [03](Documentation/03-Toolpath-Optimization.md) | Why pcb2gcode's travel is bad, and the replacement |
| [04](Documentation/04-Machines-Laser-and-Mixed-Workflows.md) | Laser output, the Job model, board re-alignment |
| [05](Documentation/05-Viewer-and-Export.md) | The G-code viewer, SVG/DXF export |
| [06](Documentation/06-Roadmap-and-Risks.md) | Phases, acceptance metrics, risks |
| [07](Documentation/07-UI-Framework-Decision.md) | Avalonia vs. WPF vs. MAUI |

## Building

Requires the **.NET 10 SDK**. Nothing else — no vcpkg, no CMake, no MSYS2.

```
dotnet build PCB_MillBurn.slnx
dotnet test  PCB_MillBurn.slnx
```

For Linux, self-contained so nothing has to be installed on the target:

```
dotnet publish src/MillBurn.App -c Release -r linux-x64 --self-contained -o out/linux
dotnet publish src/MillBurn.Cli -c Release -r linux-x64 --self-contained -o out/linux
```

On a fresh Debian or Ubuntu the app may need `sudo apt install -y libfontconfig1`; everything else
it needs ships in the publish output.

## Running

```
dotnet run --project src/MillBurn.App  -- <gerber-folder>            # open a board in the window
dotnet run --project src/MillBurn.App  -- <project.millburn>         # open a saved project
dotnet run --project src/MillBurn.Cli -- project save <folder> -o p.millburn
dotnet run --project src/MillBurn.Cli -- project refresh p.millburn  # what changed since the export
dotnet run --project src/MillBurn.Cli -- tools list                  # the saved tool library
dotnet run --project src/MillBurn.Cli -- mill <folder> --isolation-tool "30°" --outline-tool "1.0 mm"
dotnet run --project src/MillBurn.Cli -- board <gerber-folder>      # detect layers, realise, report
dotnet run --project src/MillBurn.Cli -- board <folder> --png b.png  # render the board headlessly
dotnet run --project src/MillBurn.Cli -- inspect <gerber-folder>     # parse report
dotnet run --project src/MillBurn.Cli -- render <layer.gbr> --svg out.svg
dotnet run --project src/MillBurn.Cli -- svg <silkscreen.gbr>       # laser-ready SVG

dotnet run --project src/MillBurn.App            # the app (drag a folder onto it)
dotnet run --project src/MillBurn.App -- --bench # viewport benchmark (offscreen, CPU raster)
dotnet run --project src/MillBurn.App -- --probe # render-configuration sweep
dotnet run --project src/MillBurn.App -- --fpstest  # measures the real GPU render loop
```

## Layout

```
src/
  MillBurn.Core       units, transforms, project model
  MillBurn.Gerber     Gerber X2/X3 + Excellon parsing
  MillBurn.Geometry   Clipper2 + NetTopologySuite: tessellation, booleans, offsets, pocketing
  MillBurn.Cam        isolation, drilling, outline, mask and silkscreen generators
  MillBurn.Optimize   travel optimizer and the motion time model
  MillBurn.Gcode      mill only: emitter, parser, processor chain, backplot
  MillBurn.Post       mill only: machine profiles and post-processors
  MillBurn.Align      fiducial fits, transforms, height-map import
  MillBurn.Export     SVG / DXF / PDF / PNG - the whole laser output path
  MillBurn.Viewer     toolpath scene, level of detail, spatial culling, Skia renderer
  MillBurn.Pipeline   the cached, cancellable stage graph tying it together
  MillBurn.App        Avalonia shell (UI only)
  MillBurn.Cli        headless batch driver
tests/
  MillBurn.Tests      unit and property tests
  MillBurn.GoldenTests  golden-file and determinism regression
  corpus/             Gerber test boards
```

Every algorithm lives in a UI-free `net10.0` library; only `MillBurn.App` references a UI
framework. That is deliberate — see [07 §4](Documentation/07-UI-Framework-Decision.md).

## Working material beside the repo (all git-ignored)

| Path | What it is | Note |
|---|---|---|
| `pcb2gcode/` | The original C++ tool | **GPL-3.0.** Read for approach; do not copy code. See [01 §9](Documentation/01-Architecture.md#9-licensing-strategy). |
| `Universal-G-Code-Sender/` | UGS, whose visualizer informed ours | **GPL-3.0.** Same rule. |
| `MyGerbers/`, `MyGerbers2/` | Scratch Gerber exports | A drop zone for boards that are *not* project content. Boards that earn a place in the suite go in `tests/boards/`. |

None of these is committed, linked, or shipped.

Test fixtures come in two kinds, both committed and both run everywhere: hand-written cases from
the Ucamco specification, inline in the test files, and two real KiCad 10 board exports in
[`tests/boards/`](tests/boards/README.md). The first prove the parser handles the specification;
the second prove it handles what an EDA tool actually writes, which is where the bugs have been.
Set `MILLBURN_BOARDS` to run the suite against a private board without committing it.

## Licence

**[MIT](LICENSE).** Use it for anything, including commercially; keep the copyright notice.

Every dependency is permissively licensed as well — MIT, BSD, BSL-1.0, Apache-2.0 — so there is no
copyleft anywhere in the graph and a build can be shipped by anyone in any product. The full list,
with the licence of each, is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

That is only possible because the two GPL-3.0 reference checkouts above are read and never copied.
It is a live constraint, not a formality.
