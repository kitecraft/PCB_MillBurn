<div align="center">
  <img src="art/millburn-mark.png" alt="" width="120">
  <h1>PCB_MillBurn</h1>
  <p>
    <b>Turn your Gerber files into a circuit board.</b><br>
    G-code for the mill. SVG for the laser. One app, one board, no guesswork.
  </p>
  <p>
    <a href="LICENSE"><img src="https://img.shields.io/badge/licence-MIT-blue.svg" alt="Licence: MIT"></a>
    <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10-512BD4.svg" alt=".NET 10"></a>
    <a href="#build-it-yourself"><img src="https://img.shields.io/badge/runs%20on-Windows%20%7C%20Linux-informational.svg" alt="Runs on Windows and Linux"></a>
    <a href="THIRD-PARTY-NOTICES.md"><img src="https://img.shields.io/badge/dependencies-permissive%20only-success.svg" alt="Permissively licensed dependencies only"></a>
  </p>
</div>

<!--
  Once this is pushed to GitHub, add the build badge beside the others — replace OWNER/REPO:
  <a href="../../actions/workflows/build.yml"><img src="https://github.com/OWNER/REPO/actions/workflows/build.yml/badge.svg" alt="build"></a>
-->

<img src="art/screenshots/hero.png" alt="PCB_MillBurn showing a 66-up panel with isolation toolpaths drawn over the copper">

<sub>A 66-up panel — 11,414 objects, 198 copper islands — with the emitted isolation program drawn back over the copper it was made from.</sub>

---

## What it does

Drop a folder of Gerbers on the window. The board appears. Tell each layer what it should become.
Press Export.

- **Mill it.** Isolation routing, drilling, soldermask relief, cut-out with tabs. Real G-code.
- **Burn it.** Laser-ready SVG at true 1:1 millimetres, for LightBurn or Inkscape.
- **Or both.** Etch the traces with the laser, drill and cut the outline on the mill. Same board,
  same origin, same export.

Every file is one layer. Every file shares the board's lower-left corner as work zero. Nothing is
written until you have seen a list of exactly what is about to be written.

**It writes files. It does not drive machines.** No serial port, no jogging, no streaming — gSender,
UGS, Candle, LightBurn and LinuxCNC already do that well.

---

## 🔍 It is also just a really good Gerber viewer

**Use it for nothing else and it still earns its place on your machine.**

Point it at any Gerber export folder and look at your board — fast, correct, and offline.

- **Reads what your EDA tool actually writes.** Gerber RS-274X and X2, aperture macros, negative
  polarity, step-and-repeat, arcs kept as arcs. Excellon drill files including the zero-suppressed
  dialects, G85 slots and routed slots.
- **Knows what each file is** from its X2 `.FileFunction` — not from its filename — and *says so*
  when it had to guess.
- **Fast.** The viewport holds **98 fps** through a full fit → 40× → fit zoom sweep on 500,247
  segments. A board renders in 0.04 ms.
- **Every layer independently toggleable**, with colours you pick and it remembers.
- **Opens anybody's G-code too.** `File ▸ Open G-code…`, or drag a `.nc` onto the window. It parses
  the text, so a program from any CAM tool draws the same way.
- **Light and dark**, and it means it.

<img src="art/screenshots/viewer-light.png" alt="The board viewer in light theme showing copper, silkscreen, holes and outline">

---

## See it

<table>
<tr>
<td width="50%" valign="top">
<img src="art/screenshots/toolpath.png" alt="One layer's copper with its isolation toolpath drawn over it">
<p><b>Check the cut before you make it.</b> One layer's copper, one layer's toolpath, everything
else off. The path drawn is parsed back out of the emitted G-code — not from the toolpath that made
it — so what you are looking at is the file the machine will run.</p>
</td>
<td width="50%" valign="top">
<img src="art/screenshots/export.png" alt="The export review window listing every file with its facts and warnings">
<p><b>Nothing is written by surprise.</b> Every file, what it will do, how long it takes, how much
it simplified, and anything worth checking — before a single byte lands on disk.</p>
</td>
</tr>
<tr>
<td width="50%" valign="top">
<img src="art/screenshots/drill-guide.png" alt="The drilling companion page listing bits in order with hole counts">
<p><b>Drilling gets a page of its own.</b> The bits in the order they go in, hole counts, when the
run stops to change them, and what you have to re-zero. Written beside the program, as HTML.</p>
</td>
<td width="50%" valign="top">
<img src="art/screenshots/settings.png" alt="The settings window showing machine, dry run and probing values">
<p><b>The machine's numbers are yours.</b> Safe height, approach, rapid rate, decimals, canned
cycles, dry-run height, probing grid, levelling. Checked for contradictions, and refused rather
than silently clamped.</p>
</td>
</tr>
</table>

<img src="art/screenshots/svg-output.png" alt="The exported SVG rendered in a browser: 66 boards in LightBurn green">

<sub>The actual exported SVG, opened in a browser. Real millimetres, named layers, LightBurn's
palette. This artwork has been laser-engraved onto copper-clad and measured true to within 0.1 mm.</sub>

---

## Quick start

1. **Get it.** Download a release, or [build it yourself](#build-it-yourself). Self-contained —
   you do not need .NET installed.
2. **Open a board.** Drag your Gerber export folder onto the window, or `File ▸ Import Gerber
   folder…`. Drill files come along with it.
3. **Set the board thickness** in Project info. Everything that cuts through uses it.
4. **Tell each layer what it becomes.** Open a layer row and pick **G-code (mill)**, **SVG
   (laser)**, or **Not exported**. The pill on the collapsed row shows what you chose.
5. **Press Preview** (F5). The programs are drawn back over your board.
6. **Press Export…** (Ctrl+E). Read the list. Choose a folder. Done.

Press **F1** at any point — the help pages ship with the app and work with no network, which is
the condition a workshop is usually in.

---

## How to

### Mill a board

| Layer | Set it to | You get |
|---|---|---|
| Top / bottom copper | G-code | Isolation routing around every trace and pad |
| Plated / non-plated holes | G-code | A drilling program with tool changes, plus its HTML guide |
| Board outline | G-code | Cut-out with tabs, in depth passes |
| Soldermask | G-code | Mask relief — mills the mask off the pads only |

Run order on the machine: **isolate, drill, cut out.** Work zero is the board's lower-left corner,
shared by every file in the export.

Per layer you can set the tool, cut depth, **isolation width**, break-through past the underside,
and tab count. Pick tools from your saved library (`Edit ▸ Tool library…`) — diameter, tip and
included angle for V-bits, feeds, plunge rate, RPM, max depth, stepdown, flutes.

**Isolation width is a width, not a lap count.** You say how wide a moat you want around every
trace — 0.4 mm by default — and the app works out how many passes that takes with your bit at your
depth, and shows the sum: *4 passes of 0.127 mm clears 0.450 mm*. One lap of a 30° V-bit is 0.127 mm,
which separates the nets and is also a gap you cannot see, cannot solder across without bridging, and
can close by handling the board.

### Burn a board

| Layer | Set it to | You get |
|---|---|---|
| Copper | SVG | The traces, ready to burn as a resist |
| Copper, **inverted** | SVG | Everything inside the board edge *except* the traces — burn the resist *off* a painted board |
| Silkscreen | SVG | Centrelines straight from the strokes, bucketed by whether they fit your beam |
| Soldermask | SVG | The openings, which is exactly what gets lasered |

Every SVG in one export shares **one page origin and size**, so layers land on top of each other
in the laser software without a single alignment step. Bottom-side layers are mirrored for you, and
the export says which way to flip the stock.

### Both machines, one board

Etch the copper with the laser, then drill and cut the outline on the mill — set the copper layers
to SVG and the drill and outline layers to G-code, and export once. Same board, same origin.

For **double-sided** work there is a recipe rather than a feature: drill every hole in the first
setup while the stock is still located, into a scrap plate underneath, then pin through both. It
reaches 20–50 µm and nothing is ever measured, so nothing can be measured wrong. It does put one
rule on your layout, so read the help page before you route.

---

## The parts that stop you ruining boards

This is the half that does not show up in a feature list, and it is the half worth having.

**Watch the job in the air first.** Any program can be rewritten to trace the same path 5 mm up
with the spindle never started. It rewrites *the emitted file*, not the toolpath, and it re-parses
its own output to prove that nothing moving sideways does so below the height. Feeds are kept, so
you also find out it is a ninety-minute job.

**Follow the board that is actually on your table.** Isolation cuts 0.05 mm deep; clamped FR4 is
0.1–0.2 mm out of flat. That one comparison is the most common reason PCB milling disappoints
people. So: generate a `G38.2` probing grid, run it, feed your sender's log back in, and every
program is bent to the measured surface — thin-plate spline, clamped to the probed area, degrading
to a tilt or an offset when the measurements cannot justify a surface.

**Refuse rather than guess.** A dry run that is wrong is worse than no dry run, because it is the
thing you trust just before committing a board. So incremental mode is refused, not guessed at. A
levelling job that runs outside the probed area is refused. Settings that contradict each other are
refused rather than quietly clamped. Every one of those is a case where being helpful would mean
being wrong silently.

**Dial the bit in before you trust it.** *Job ▸ Test cuts…* writes a short program that cuts a few
lines on scrap — one per depth, or one per feed — and an HTML page explaining how to read them. Every
number the app computes about a cut comes from your tool library, and the library is a claim about a
physical object; this is how you check it. Both tests cut the first line twice, at opposite ends of
the coupon, so you can tell whether the stock moved before you believe anything else on it.

**Say what changed.** `File ▸ Refresh from source` compares the project against the folder it came
from and shows you what moved before it applies anything.

---

## The CLI

The whole pipeline is scriptable, and it is the same code the window runs — a job exported either
way comes out identical.

The executable is `MillBurn.Cli`; it is called `millburn` below for brevity. From a source tree,
`dotnet run --project src/MillBurn.Cli --` takes its place.

```sh
millburn board   <gerber-folder>              # detect layers, realise geometry, report
millburn board   <folder> --png board.png     # render the board headlessly
millburn inspect <folder>                     # parse report: what was understood, what was not
millburn render  <layer.gbr> --svg out.svg    # realise one layer's geometry
```

**Make the files:**

```sh
millburn export <folder> --write -o out/
millburn export <folder> --set F_Cu.gbr=gcode --set F_Mask.gbr=svg- --write   # svg- inverts
millburn export <folder> --only gcode --write
millburn export <folder> --dry-run --write            # + a .dryrun.nc beside each program
millburn export <folder> --start-gcode preamble.nc --end-gcode shutdown.nc
millburn mill   <folder> --isolation-width 0.4 --isolation-tool "30°" --png cut.png
millburn svg    <silkscreen.gbr> --flavour lightburn --spot 0.1
```

**Probe and level:**

```sh
millburn testcut depth --tool "30°" --from 0.02 --step 0.02 # lines on scrap, plus a page on reading them
millburn testcut feed --depth 0.05 --step 50 --probe        # + a probing routine for the coupon
millburn probe  <folder> --spacing 8 --depth 2 --feed 30   # a G38.2 grid to run and log
millburn export <folder> --level probe.log --write         # bend every program to the surface
millburn level  anyones.nc --map probe.log                 # works on any G-code, not just ours
```

**Projects:**

```sh
millburn project save    <folder> -o board.millburn
millburn project info    board.millburn
millburn project refresh board.millburn --apply   # take changes from the source folder
millburn tools list
```

Run `millburn` with no arguments for the full list.

---

## What it will not do

Deliberate, all of it.

- **Drive your machine.** No serial port, ever. That is a solved problem and not this one.
- **Panelise.** Do it in your EDA tool — [KiKit](https://github.com/yaqwsx/KiKit) for KiCad — and
  this will cut the panel. A panelising tool that does not know your design rules is a worse
  panelising tool.
- **Compensate for kerf or etch bias.** Kerf is a function of power, speed, focus, lens and
  material, all of which live in your laser software beside a calibrated material library. A number
  held here would go stale the moment any of them changed, with nothing to say so — and two tools
  each applying an offset gives you a doubly compensated board that looks wrong in neither.
- **Mill slotted holes** &mdash; yet. A slot needs an end mill, not a drill, so it is a different
  tool and a different motion from everything else in a drilling program. It is scheduled. Until
  then the export **says** which slots it is not making, rather than leaving them out quietly.
- **Guess.** Where the honest answer is "this file does something I cannot safely handle", it says
  so and hands the file back unchanged.

---

## Status

**The laser half is physically verified.** A 66-up panel — the hardest artwork in the test corpus,
not the easiest — was exported as front-copper SVG and laser-engraved onto copper-clad. Traces,
pads and outer dimensions all measure true to within the ~0.1 mm the caliper is good for, which
bounds any scale error across 165 mm at **0.061%**.

**The mill half has started moving.** A generated dry run has been executed on a real CNC and ran
correctly — the whole program traced in the air, spindle off, nothing struck. That exercises the
emitter, the post, the ordering and the dry-run rewrite in one go, and it put the first number on
the time model: **predicted 1:57–2:01, actual 1:50**, about 5 % conservative.

**What has not happened yet is the tool touching copper.** The cut width, the drilling run and the
levelled surface have not been measured on a real board. 658 tests pass with zero warnings under
`TreatWarningsAsErrors`, but until there is a board on the bench, the cutting path is "believed
correct", not "proven".

Done: reading and drawing boards, projects, toolpaths and G-code, the travel optimizer,
simplification and arc fitting (a panel's isolation goes from 301,097 lines to 15,191, within a
2 µm bound), isolation width, dry runs, height mapping, machine settings, custom start/end G-code,
the standalone G-code viewer.

Next: **slots, mill-drill and library-aware tool selection** — an end mill moving sideways at
depth is a slot and is also a hole too big to drill, and neither can pick its own cutter today.
Then **the blank** — the app cuts you a piece of stock and its edges become the datum for every
machine and every step after it, which removes fiducials, dowel pins and the design rule that goes
with them. Then fiducial fitting and fixture generators, pad selection from X2 attributes, DXF
output, and a LightBurn layer preset. Also planned: an **MCP server** over the same libraries, so an assistant can
load a board, render it, and say what a job would cut — read-only unless you launch it otherwise.

The full picture — including the bugs, and what each one taught — is in
[Documentation/06](Documentation/06-Roadmap-and-Risks.md).

---

## Build it yourself

Requires the **.NET 10 SDK**. Nothing else — no vcpkg, no CMake, no MSYS2.

```sh
dotnet build PCB_MillBurn.slnx
dotnet test  PCB_MillBurn.slnx
dotnet run --project src/MillBurn.App -- <gerber-folder>
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, and CI builds Release on every push — so a
warning is a failed build, which is the point.

### Publishing

Self-contained, so nothing has to be installed on the target — not even .NET. The app and the CLI
go to the same directory and share their runtime.

```sh
dotnet publish src/MillBurn.App -c Release -r win-x64 --self-contained -o out/windows
dotnet publish src/MillBurn.Cli -c Release -r win-x64 --self-contained -o out/windows
```

```sh
dotnet publish src/MillBurn.App -c Release -r linux-x64 --self-contained -o out/linux
dotnet publish src/MillBurn.Cli -c Release -r linux-x64 --self-contained -o out/linux
```

Either target can be published from either host; only the runtime identifier changes.

**One asymmetry, and it will bite.** Publishing Linux binaries *from Windows* leaves the two
launchers non-executable: NTFS has no execute bit, so the ELF files come out `rw-r--r--` and simply
will not start. Either `chmod +x MillBurn.App MillBurn.Cli` after copying them across, or package
them from a filesystem that can hold the permission:

```sh
tar --owner=0 --group=0 -czf out/millburn-linux-x64.tar.gz -C out/linux .
```

Publishing on Linux sets the bit itself. On a fresh Debian or Ubuntu the app may want
`sudo apt install -y libfontconfig1`; everything else it needs ships in the output, including
`libSkiaSharp.so` and the `Help/` pages, so the Help menu and F1 work offline.

| Target | Publish output | Archive |
|---|---|---|
| `win-x64` | `out/windows/`, 296 files, 214 MB | `out/millburn-win-x64.zip`, 75 MB |
| `linux-x64` | `out/linux/`, 293 files, 111 MB | `out/millburn-linux-x64.tar.gz`, 46 MB |

---

## How it is put together

```
src/
  MillBurn.Core       units, transforms, project model, settings
  MillBurn.Gerber     Gerber X2/X3 + Excellon parsing
  MillBurn.Geometry   Clipper2 + NetTopologySuite: tessellation, booleans, offsets, pocketing
  MillBurn.Cam        isolation, drilling, outline, mask and silkscreen generators
  MillBurn.Optimize   travel optimizer and the motion time model
  MillBurn.Gcode      mill only: emitter, parser, processor chain, backplot, dry runs
  MillBurn.Post       mill only: machine profiles and post-processors
  MillBurn.Align      height maps (probe-log import, TPS), G-code levelling, fiducial fits
  MillBurn.Export     SVG / DXF / PDF / PNG — the whole laser output path
  MillBurn.Viewer     scene, level of detail, spatial culling, Skia renderer
  MillBurn.Pipeline   the cached, cancellable stage graph tying it together
  MillBurn.App        Avalonia shell (UI only)
  MillBurn.Cli        headless batch driver
tests/
  MillBurn.Tests        unit and property tests
  MillBurn.GoldenTests  golden-file and determinism regression
  boards/               real KiCad exports, committed
  corpus/               Gerber test files from the specification
```

Every algorithm lives in a UI-free `net10.0` library; only `MillBurn.App` references a UI framework.

**Design documentation is in [`Documentation/`](Documentation/README.md)** — why the code is shaped
the way it is, written for whoever maintains it. It is considerably more interesting than a
changelog.

| Doc | Subject |
|---|---|
| [01](Documentation/01-Architecture.md) | Solution layout, incremental pipeline, scope boundary, licensing |
| [02](Documentation/02-Gerber-and-Geometry-Pipeline.md) | Gerber X2/X3 parsing, Clipper2/NTS geometry, isolation, DRC |
| [03](Documentation/03-Toolpath-Optimization.md) | Why pcb2gcode's travel is bad, and the replacement |
| [04](Documentation/04-Machines-Laser-and-Mixed-Workflows.md) | Laser output, the Job model, board re-alignment |
| [05](Documentation/05-Viewer-and-Export.md) | The G-code viewer, SVG/DXF export |
| [06](Documentation/06-Roadmap-and-Risks.md) | Phases, acceptance metrics, risks, and every bug worth remembering |
| [07](Documentation/07-UI-Framework-Decision.md) | Avalonia vs. WPF vs. MAUI |

Test fixtures come in two kinds, both committed and both run everywhere: hand-written cases from
the Ucamco specification, and real KiCad board exports in
[`tests/boards/`](tests/boards/README.md). The first prove the parser handles the specification;
the second prove it handles what an EDA tool actually writes, which is where the bugs have been.
Set `MILLBURN_BOARDS` to run the suite against a private board without committing it.

---

## Thanks

### To pcb2gcode, first and by a distance

**[pcb2gcode](https://github.com/pcb2gcode/pcb2gcode) is the reason this project knows what it is
for.** For the better part of two decades it has been the answer to "how do I mill a PCB at home",
and an enormous number of boards exist because of it — mine among them. It solved the hard, unglamorous
problems first: isolation from real Gerbers, V-bit geometry, break-through, tabs, the whole shape of
the job. Everything here starts from a problem statement that tool worked out and then proved, in
copper, on thousands of benches.

Where this project does something differently, that is a difference of goals and of what is cheap in
2026 — not a criticism of a tool that got there twenty years earlier with far less to build on. The
maintainers gave that away for free, for decades, and asked for nothing. Thank you.

pcb2gcode is GPL-3.0. It was **read for approach and never copied** — not a line of it is in this
repository — because this project is MIT and taking the code would have quietly relicensed everyone
else's work along with it. That constraint is a form of respect, not an evasion: the ideas are
credited here, and the code stays where its authors put it.

### To Universal G-code Sender

**[UGS](https://github.com/winder/Universal-G-Code-Sender)** and its visualizer taught this project
what a G-code viewer owes the person reading it — the decomposition into renderable layers, the
honest time model, the idea that seeing the rapids is what makes a problem obvious. Also GPL-3.0,
also read and never copied, also given away free for years. Thank you.

### To the libraries this is built on

None of this would exist without work other people gave away:

[**Clipper2**](https://github.com/AngusJohnson/Clipper2) (Angus Johnson) — every boolean and every
offset in this project, on exact Int64 coordinates. It is the single most load-bearing dependency
here and it has never once been wrong. ·
[**NetTopologySuite**](https://github.com/NetTopologySuite/NetTopologySuite) — Voronoi, spatial
indexing, validity. ·
[**SkiaSharp**](https://github.com/mono/SkiaSharp) and the Skia team — the viewport draws half a
million segments at 98 fps because of it. ·
[**Avalonia**](https://github.com/AvaloniaUI/Avalonia) — the reason one codebase is a real desktop
app on Windows and Linux with no per-platform branch. Plus
[**AvaloniaEdit**](https://github.com/AvaloniaUI/AvaloniaEdit) and
[**Dock.Avalonia**](https://github.com/wieslawsoltes/Dock). ·
[**MathNet.Numerics**](https://github.com/mathnet/mathnet-numerics) — the SVD behind the height-map
fit. ·
[**CommunityToolkit.Mvvm**](https://github.com/CommunityToolkit/dotnet) ·
[**Scriban**](https://github.com/scriban/scriban) ·
[**xUnit**](https://github.com/xunit/xunit) and [**Verify**](https://github.com/VerifyTests/Verify)
— 617 tests' worth. ·
And **.NET** itself, which is why the build instructions are two lines long.

### And to the people who made the inputs make sense

**[Ucamco](https://www.ucamco.com/en/gerber)**, for publishing the Gerber specification openly and
keeping it readable — X2's `.FileFunction` is why this app knows what your files *are* instead of
guessing from their names. · **[KiCad](https://www.kicad.org/)**, for being free, excellent, and
the source of every real board in the test suite. · **[KiKit](https://github.com/yaqwsx/KiKit)**,
which panelises so much better than this ever would that panelising is deliberately not here. ·
And the **gSender**, **Candle**, **LinuxCNC** and **LightBurn** communities, who make the machines
actually run — this app only writes the files.

---

## Licence

**[MIT](LICENSE).** Use it for anything, including commercially; keep the copyright notice.

Every dependency is permissively licensed as well — MIT, BSD, BSL-1.0, Apache-2.0 — so there is no
copyleft anywhere in the graph and a build can be shipped by anyone, in any product. The full list
is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

The two GPL-3.0 projects credited above were read for approach and never copied — no line of either
is in this repository. That is a live constraint on how this project is written, not a formality:
it is what keeps this licence honest and everyone else's work unencumbered.
