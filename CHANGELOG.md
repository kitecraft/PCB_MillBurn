# Changelog

What changed in each release, and where it was proven. The full notes live on the
[releases page](https://github.com/kitecraft/PCB_MillBurn/releases); this file is the copy that
travels with the repository, so a clone or a mirror still has the history.

**Versions, pre-1.0:** a sprint moves the middle digit, an urgent fix shipped mid-sprint moves the
last one, and a change to a CLI workflow or to a saved file's shape is marked **Breaking** here and
in the first line of the release notes. See [CONTRIBUTING](CONTRIBUTING.md#versions).

---

## Unreleased — sprint 3, `release/0.4.0`

**Breaking: `millburn-cli mill` now takes its cutters from your tool library.** It synthesised them
from its own defaults — a 30° V-bit with a 0.1 mm tip, and a 1.0 mm end mill — and ignored the
library entirely unless you named a tool. So `mill` and `export` planned different cuts on the same
board: on a library holding the built-in V-bit with its tip corrected to a measured 0.127 mm, one
command cut 0.154 mm and the other 0.127 mm at the same depth, and they disagreed about which nets
the cut would leave connected. `--angle`, `--tip` and `--tool` still synthesise a bit exactly as
before; what changes is what happens when you say nothing. A script relying on the old default gets
your library's isolation bit instead.

**The checks now read as a list rather than a wall of sentences.** Every line begins with the thing
it is about — `Top copper — …`, `Stock — …`, `Export — …` — and a layer's own checks carry that
layer's colour, the one you chose if you have overridden it. A mark in front of each says how much it
matters without your reading to the end of the sentence: a faint dot for advice, an amber `!` for
something refused, a red `▲` for do-not-run-this.

**The section folds away**, leaving the heading, the count, and how many of them want looking at, so
a board you have already read the checks on gives the room back to the layer list. It is not
remembered between sessions — a fold set on one board should not hide the checks on the next one you
open — and a new check does not reopen it, except an error or an export refusal, which are the two
cases where leaving it folded would hide something you are being told to go and look at.

**And the export's status line now points at a line you can see.** *"17 refused — No dry run — see
the checks"* used to send you to a list where nothing said which one it meant; the checks it is
about are now tinted in the panel. A refusal naming seventeen files also stops after three and counts
the rest, instead of filling the whole panel with a list you had to read to the end to learn it was
all of them.

**The findings have a place of their own — *Job ▸ Findings*, F7.** Every copper layer in one window
instead of one report per program, with room for what a warning line could never carry: every net in
a shorted group rather than the first six, and **where each gap actually is**. Not which copper it is
in — the actual coordinate, in the same frame the G-code uses, so you can go to it.

Finding those places means growing each piece of copper on its own and intersecting the results,
which costs about a second and a half for a two-sided board. That is why it lives behind a menu item
rather than running on every preview, and why it can be cancelled while it works.

**And the CHECK list gets out of the way.** A dense board used to add eleven lines of net names to it
and bury the layer that would not parse; it now adds one line per copper layer, saying how many
findings there are and pointing at F7. The companion pages and the CLI still carry the full set,
because a page read away from the app has no window to open.

This section is a placeholder for the sprint's notes and will be folded into the 0.4.0 entry.

---

## [0.3.0](https://github.com/kitecraft/PCB_MillBurn/releases/tag/v0.3.0) — 2026-09-27

**Fix what is broken.** The first sprint defined by the defect list rather than by a theme, which is
only possible because [the list](Documentation/11-Backlog.md) now exists. Six of the seven open
defects, plus the three requirements the product owner asked to come with them.

[Sprint 2 — Fix what is broken](Sprints/Sprint-02-Fix-What-Is-Broken.md). Nine stories: six closed
by a fix, one by measuring it and deciding it was not worth fixing, one that turned out to be
already satisfied, and one met by a guide instead of the feature it asked for.

**The dry run is now the real program, raised — and that is a changed default.** It used to hold
every Z at one height, which proved the extents and the work zero and nothing else: no plunges, no
lifts, and a run time that was not the job's. It is now the real program with every Z lifted by a
rise, spindle off, so every plunge and lift happens where it really happens and the run takes as
long as the cut will. Checked move by move against the real program rather than at its low point —
same moves, same X and Y, same kinds, every Z exactly the rise higher. The flat run is still there
as a choice, in *Settings › Dry run* and as `--dry-run-height`; `--dry-run-rise` asks for the new
one. **An existing settings file has no style in it, so the first dry run after upgrading will be
the raised one.**

It refuses rather than guesses. A cut deeper than the rise clears is refused with the rise that
would do it; so are `G92`, `G10`, `G38`, `G43`/`G49` and their decimal variants, a machine-
referenced Z (`G53`, `G28`, `G30`), and a program that commands no Z at all — each named with its
line number, because a dry run that is wrong is worse than none.

**A comment no longer hides the code after it.** Three places read a G-code line to decide what it
does, and two of them stopped at the first `(` instead of stepping over the comment — so a line
written `( touch off ) G1 Z-1.0 F50`, which is how people write headers, looked completely blank to
them while the machine saw a real cutting move. Every dry-run refusal went silent on such a line,
the rise skipped it, and the clearance check never measured it: the dry run handed back that plunge
verbatim under a header promising every Z three millimetres higher. The start/end G-code check lost
its errors the same way, on the real program — `(all done) M30` passed without a word. All three
readers now share one implementation, the one that was already right.

**Two faults that spoiled copper, both found at the bench and both on real boards.** An isolation
path no longer bows into an arc where the copper is straight: all eight instances on the Arduino
Mega come out straight, in a program the same size as before. And the hole that was not a hole is
gone — at isolation widths from 0.1 mm to 1.0 mm the smallest loop that survives anywhere on that
board is 0.165 mm across, against a 0.124 mm cut. Reproducing the second took a round trip: it needs
the project's own 0.045 mm cut depth, not the 0.050 mm default, because the threshold it crosses is
a pass count.

**A superseded preview actually stops.** Planning takes a cancellation token now and reads it
between layers, between programs and between toolpaths — the innermost is where simplification and
route optimisation spend the seconds, so it is the one that decides how long a cancelled run keeps
going. A plan that completed is still kept.

**A full-range drag of the thickness slider wrote the settings file twenty-eight times.** It now
writes nothing until the window closes. What it was saving was not the project's thickness but the
app-wide default for the *next* board, so twenty-seven of the twenty-eight were superseded
immediately. The cost of holding it in memory is that a session ending in a crash loses the value.

**Measured, then left alone.** An even number of outline passes really does cost about twice the
travel of an odd one — 337, 721, 326, 326 and 710 mm across five step-downs on a 66-up panel,
exactly as reported. Turned into time it is **about twelve seconds** on a job the program estimates
at over forty minutes, so it is recorded with the number and closed rather than fixed.

**A new guide: [corner stops](Help/guides/corner-stops.html).** Cut a locating corner into a
sacrificial plate once, then start every repeat job by pushing the stock into it. Three pads rather
than two rails, the corner relief that decides whether it works at all, how tall the pads may be
before the cutter finds them, and an honest error budget — the stock's sheared edge dominates at
0.1–0.25 mm, and milling the two datum edges first collapses it to 0.05 mm. Six line drawings, no
photographs. The fixture generator it was going to be was declined: the datum it would have stored
is a work offset your controller already owns.

**Under the hood, and the reason the next regression will be visible.** Every toolpath this program
makes is built by an offset, and not one of them was counted — on the Arduino Mega, 8,024 offsets
against 1,493 booleans, five times as many operations as the tally was watching. They are counted
now, along with what the route search costs, and a build error keeps Clipper's boolean, offset,
Minkowski and point-in-polygon entry points inside one file so there is one place to count from. A
change that swapped a boolean for an offset would previously have reported that the work got
cheaper.

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
