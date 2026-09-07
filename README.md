# PCB_MillBurn

A modern, Windows-only Gerber-to-G-code converter for **CNC mills**, **laser engravers**, and
**mixed mill+laser workflows** — a rethink of `pcb2gcode` with a real travel optimizer, first-class
laser output, a live toolpath viewer, and a job model that handles a board moving between machines.

**PCB_MillBurn converts files. It does not drive machines.** No serial port, no jogging, no
streaming — UGS, Candle, LightBurn and LinuxCNC already do that well.

> **Status: Phase 0.** Scaffolding and the UI-framework spike are done. No Gerber is parsed yet.

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

## Running

```
dotnet run --project src/MillBurn.App            # the app
dotnet run --project src/MillBurn.App -- --bench # viewport benchmark (offscreen, CPU raster)
dotnet run --project src/MillBurn.App -- --probe # render-configuration sweep
dotnet run --project src/MillBurn.App -- --fpstest  # measures the real GPU render loop
```

## Layout

```
src/
  MillBurn.Core       units, transforms, project model
  MillBurn.Gerber     Gerber X2/X3 + Excellon parsing
  MillBurn.Geometry   Clipper2 + NetTopologySuite: offsets, booleans, voronoi, pocketing
  MillBurn.Cam        isolation, drilling, outline, laser operation generators
  MillBurn.Optimize   travel optimizer and the motion time model
  MillBurn.Gcode      emitter, parser, processor chain, backplot
  MillBurn.Post       machine profiles and post-processors
  MillBurn.Align      fiducial fits, transforms, height-map import
  MillBurn.Export     SVG / DXF / PDF / PNG
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

## Reference checkouts

`pcb2gcode/` and `Universal-G-Code-Sender/` sit alongside this repo (git-ignored) as design
references. **Both are GPL-3.0.** Read them for approach; do not copy code. See
[01 §9](Documentation/01-Architecture.md#9-licensing-strategy).

## Licence

Not yet chosen — see [06 §4](Documentation/06-Roadmap-and-Risks.md#4-open-questions). Decide
before the first public commit.
