# Third-party notices

PCB_MillBurn is [MIT licensed](LICENSE). It depends on the packages below, **all of which are
permissively licensed** — there is no copyleft anywhere in the dependency graph, so a build of
PCB_MillBurn can be shipped by anyone, in any product, open or closed.

The licences here were read from the restored packages in the local NuGet cache, not copied from
memory. Re-check them when a version is bumped; a licence change is a real, if rare, event.

## Runtime dependencies

| Package | Licence | Used for |
|---|---|---|
| [Clipper2](https://github.com/AngusJohnson/Clipper2) | BSL-1.0 | Polygon booleans and offsetting on Int64 coordinates |
| [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) | BSD-3-Clause | Voronoi, STRtree spatial index, geometry validity |
| [MathNet.Numerics](https://github.com/mathnet/mathnet-numerics) | MIT | SVD for the Kabsch/affine fit, TPS interpolation |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | MIT | Rasterisation for the viewport and PNG export |
| [System.IO.Hashing](https://github.com/dotnet/runtime) | MIT | XxHash128 for pipeline cache keys |
| [Scriban](https://github.com/scriban/scriban) | BSD-2-Clause | Post-processor templating |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) (+ Desktop, Themes.Fluent, Fonts.Inter, DiagnosticsSupport) | MIT | UI shell |
| [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) | MIT | The synced G-code pane |
| [Dock.Avalonia](https://github.com/wieslawsoltes/Dock) (+ Themes.Fluent, Model.Mvvm, Settings) | MIT | Dockable tool panes with persisted layouts |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT | MVVM source generators |
| Microsoft.Extensions.{DependencyInjection, Logging, Logging.Console} | MIT | Hosting, DI, logging |

## Test-only dependencies

| Package | Licence |
|---|---|
| [xUnit](https://github.com/xunit/xunit) (+ runner.visualstudio) | Apache-2.0 |
| [Verify](https://github.com/VerifyTests/Verify) | MIT |
| Microsoft.NET.Test.Sdk | MIT |
| [coverlet.collector](https://github.com/coverlet-coverage/coverlet) | MIT |

Note that **Clipper2 (BSL-1.0) and SkiaSharp (MIT) require their copyright notices to be preserved
in binary distributions.** Keeping this file next to a shipped build satisfies that. NetTopologySuite
(BSD-3) and Scriban (BSD-2) carry the same requirement.

## Reference checkouts — not dependencies, not redistributed

Two GPL-3.0 projects sit in the workspace as *reading material* and are excluded by `.gitignore`.
Neither is linked, bundled, or shipped, and no code from either has been ported, translated, or
transliterated into this repository.

| Path | What it is | Licence |
|---|---|---|
| `WorkingFolder/pcb2gcode/` | The original C++ tool | GPL-3.0 |
| `WorkingFolder/Universal-G-Code-Sender/` | UGS, the G-code sender whose visualizer informed ours | GPL-3.0 |

## Committed test data

Two of the boards in [`tests/boards/`](tests/boards/README.md) are somebody else's work, committed
here as test fixtures rather than as product code.

| Path | What it is | Licence |
|---|---|---|
| `tests/boards/Arduino_Uno/`, `tests/boards/Arduino_Mega_2560/` | Gerber and drill exports of Arduino Uno R3 and Mega 2560 designs recreated in KiCad by Camilo Sabogal — [KiCad-Arduino-Boards](https://github.com/sabogalc/KiCad-Arduino-Boards) | WTFPL |

The WTFPL places no condition on use or redistribution, so these carry nothing into this repository
and change its licensing answer not at all. They are listed because attribution is the right thing
to do, not because it is required. Every other board in that directory is the repository owner's own
work under this project's MIT licence.

This distinction is the reason PCB_MillBurn can be MIT at all, and it is not a formality: copying
so much as a translated function from either would make the whole application GPL-3.0. Ideas are
not copyrightable and their expression is, so where a design from those projects is genuinely the
right one — UGS's chain-of-processors model, its backplot segment record — it is re-derived and
written fresh. See [Documentation/01 §9](Documentation/01-Architecture.md#9-licensing-strategy).

pcb2gcode's Gerber test corpus is likewise *referenced from that checkout* by the tests, never
copied into this repository; those tests skip cleanly when the checkout is absent.

See [`tests/boards/README.md`](tests/boards/README.md) for what each committed board exercises and
the bar for adding another. Scratch boards live in `WorkingFolder/`, which is git-ignored and is not
project content.
