# 07 — UI Framework Decision

## Recommendation: **Avalonia UI** (WPF a close, safe second; MAUI is workable but costs you)

## 1. Why I flagged MAUI

Not because MAUI is bad — IceLight V2 shows it handles a real, complex app fine. It's that
PCB_MillBurn is a *different shape of app*. IceLight is a device-control app: pages, popups,
forms, lists, sliders. PCB_MillBurn is a **CAM workstation**: one enormous custom-drawn viewport
surrounded by dense tool panes, driven by precise pointer input, with a synced code editor.

The specific gaps, all concrete:

| Need | MAUI today | Cost if you stay |
|---|---|---|
| **Docking / floating tool panes** (viewport + operations tree + inspector + G-code + issues, rearrangeable, second monitor) | Nothing. No docking library exists. | Build it yourself, or accept a fixed 3-pane grid forever. |
| **G-code text editor** with syntax highlighting, line highlighting, folding, 500k-line virtualization, and click-to-select sync with the viewport | `Editor` is a plain multiline text box. No syntax highlighting, no line addressing, poor virtualization. | Build a code editor. This is weeks of work and it is the backbone of [05 §2.5](05-Viewer-and-Export.md#25-bidirectional-selection). |
| **High-performance custom canvas** at 60 fps on 500k segments | `SKCanvasView` renders Skia to a bitmap, then MAUI composites that bitmap through its own layer. You are drawing with Skia *through* a non-Skia UI stack. | Extra copy per frame; GPU path is awkward. Workable, not ideal. |
| **Precise pointer input** — wheel zoom about the cursor, middle-drag pan, pointer capture, modifier keys, right-click context menus on canvas hit-tests | Gesture recognizers are touch-first and coarse. Wheel/modifier support on Windows is thin. | Platform handler code per interaction. |
| **Virtualized DataGrid** (tool tables, operations, issues, alignment records) | No first-class DataGrid. `CollectionView` is not the same thing. | Hand-roll or accept worse tables. |
| **Menu bar, multi-window, rich commands** | `MenuBarItem` is basic; multi-window on Windows is fiddly. | Friction, not a blocker. |
| **Styling depth** — restyling controls for a dense tool aesthetic | No `ControlTemplate` in the WPF sense; no property triggers. | You already feel this in IceLight's `AppThemeBinding` verbosity. |

None of these stop you shipping. All of them mean writing infrastructure instead of writing CAM.

## 2. Why Avalonia

**It is Skia all the way down.** Avalonia's own renderer *is* SkiaSharp. Your viewport isn't a
Skia bitmap composited into a foreign UI stack — it's a control drawing into the same Skia canvas
the rest of the UI uses, and `ICustomDrawOperation` + `ISkiaSharpApiLease` hands you the raw
`SKCanvas` with GPU backing. For an app whose central feature is a custom-drawn 500k-segment
viewport, this is the single strongest argument, and it applies to no other option.

**AvaloniaEdit** — a maintained port of AvalonEdit — gives you the G-code pane for free: syntax
highlighting, folding, line transformers (highlight line 4,281 because the user clicked that
segment), column selection, and virtualization over huge files. This is exactly the control the
bidirectional-selection design needs, and it does not exist in MAUI.

**Dock.Avalonia** gives you real docking: draggable, floatable, tabbed tool windows, layouts that
persist. CAM users rearrange their workspace; give them the ability.

**The XAML is a step up, and it's familiar.** Avalonia XAML is WPF-shaped: `ControlTemplate`,
`Style` selectors, pseudo-classes (`:pointerover`, `:disabled`), `MultiBinding`, and **compiled
bindings** (`x:CompileBindings="True"`) that are both faster and checked at compile time — no
more silent binding typos. Coming from MAUI XAML this is a lateral move in syntax and an upgrade
in capability.

**Your theme system gets simpler, not harder.** Avalonia has `ThemeVariant` +
`ResourceDictionary.ThemeDictionaries`, so the IceLight semantic-token model ports directly and
the use sites collapse:

```xml
<!-- MAUI, at every single use site -->
<Label TextColor="{AppThemeBinding Light={StaticResource TextPrimaryLight},
                                   Dark={StaticResource TextPrimaryDark}}" />

<!-- Avalonia: variant resolved by the framework -->
<TextBlock Foreground="{DynamicResource TextPrimary}" />
```

You define `TextPrimary` once per variant in `ThemeDictionaries` and never mention Light/Dark
again. Every token in `Theme_Quick_Reference.md` carries over unchanged; the boilerplate does not.

**Everything else you rely on still works.** CommunityToolkit.Mvvm, DI, SkiaSharp, Clipper2,
NetTopologySuite — all plain .NET, all unaffected. Value converters port almost verbatim.

**MIT, actively developed, and the de facto choice for new .NET desktop tooling.** Free bonus:
Linux support, which is not nothing when a chunk of the CNC world runs LinuxCNC.

### Costs, honestly

- Smaller control ecosystem than WPF's 20 years of accumulation.
- Fewer Stack Overflow answers; you read the docs and the source more often.
- Some MAUI-specific things (`Preferences`, `FilePicker`, `Popup`) need small replacements —
  Avalonia has equivalents (`StorageProvider`, window-based dialogs) but they aren't the same API.
- Pin a specific Avalonia version and verify the `net10.0` target on day one rather than
  discovering it in month two.

## 3. The alternatives, briefly

**WPF** — the conservative choice, and a genuinely good one. AvalonDock + AvalonEdit + WPF-UI for
modern theming, the deepest control ecosystem in .NET, and total stability. Against it: SkiaSharp
comes in via `SKElement`/`SKGLElement` (bitmap blit or a GL host window), which is the same
indirection problem MAUI has; it is in maintenance mode; and its XAML, while powerful, carries a
lot of 2006 in it. Pick this if "boring and certain" outranks everything else.

**WinUI 3 / Windows App SDK** — I'd skip it. It's the layer MAUI already sits on for Windows, so
you inherit its quirks without gaining much; there's no mature docking library; and the ecosystem
is thinner than both WPF and Avalonia. The modern look is nice, and that's about it.

**Blazor Hybrid / WebView2** — tempting only because it'd let you drop in the MIT-licensed
`gcode-preview` (three.js) viewer. But you'd be shipping a browser to render a desktop tool,
interop for a 500k-segment viewport is awkward, and you'd be fighting two ecosystems. Not worth it
when SkiaSharp gets you there natively.

**Stay on MAUI** — a legitimate choice. You know it, the app will work, and the architecture in
[01](01-Architecture.md) means the decision is reversible. Choose this if you want to be building
CAM features next week rather than relearning a UI stack. Just go in knowing you will be writing
a docking system and a code editor at some point.

## 4. Why this decision is cheap either way

This is the point that matters more than the choice itself: **the architecture in
[01 §2](01-Architecture.md#2-solution-layout) puts every algorithm in UI-free `net10.0` class
libraries.** Gerber parsing, geometry, CAM, the optimizer, G-code, posts, export — none of them
reference a UI type. Only `MillBurn.App` does.

So:

- Switching frameworks later means rewriting one project, not the product.
- You can even **defer the decision**: build Phases 0–2 as `MillBurn.Cli` plus the class
  libraries, with SVG/PNG output as your "UI", and pick the shell once you have something worth
  looking at.
- Or hedge: start the shell in Avalonia; if it fights you in the first two weeks, the MAUI
  project is still sitting there and the libraries don't care.

Keep this discipline no matter what you choose. It is what makes the framework question a
two-week question instead of a two-year one.

## 5. Ideas worth carrying over from IceLight V2

Regardless of framework:

- **The semantic token system.** `TextPrimary` / `Surface` / `StatusError` / `InteractivePrimary`
  rather than raw colours, with documented contrast ratios. Port it wholesale. Extend it with
  CAM-specific tokens: `PathIsolation`, `PathDrill`, `PathOutline`, `PathTravel`, `PathRapidLong`,
  `DrcViolation`, `SimulationDone`, `SimulationPending` — so the viewport, the SVG export, and the
  legend all read from one palette and stay consistent.
- **The documented-contrast discipline.** A CAM viewport lives or dies on whether you can
  distinguish a travel move from a cut at a glance, in both themes.
- **Pre-styled controls with no per-use colour.** Same principle, better tooling in Avalonia/WPF
  via `ControlTemplate`.
- **The `CoreUI/` organisation** — feature-foldered views with co-located view models — maps
  directly onto a docking layout's tool panes.
- **Converters** (`InvertedBoolConverter`, `AllTrueMultiConverter`, `RatioToPercentConverter`)
  port essentially unchanged.

## 6. What I'd actually do

1. Spike Avalonia for **two days**: a window with Dock.Avalonia, one SkiaSharp viewport panning
   and zooming 500k line segments, AvaloniaEdit in a pane, and the IceLight token palette ported
   to `ThemeDictionaries`.
2. If the spike feels good — and it should — make Avalonia the shell and delete the MAUI project.
3. If it fights you, fall back to WPF (not MAUI) for the docking + editor ecosystem.
4. Either way, **start Phase 0's library restructuring now**, because it's identical under all
   three options.
