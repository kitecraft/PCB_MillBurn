# Test boards

Real KiCad exports, committed as project content and used by `MillBurn.Tests` and
`MillBurn.GoldenTests`.

Two provenances, and the difference matters:

- The `GridStripConnector*`, `PogoTest1*` and `Millburn_Test_Board` boards are **contributed by the repository owner**
  under the project's [MIT licence](../../LICENSE).
- `Millburn_Test_Board_Protel` is **the test board again**, exported from the same KiCad project
  with Protel extensions (`.gtl`, `.gbl`, `.gts`, `.gm1`) and X2 turned off in the dialog — the two
  screenshots beside the files record exactly which settings those were. Turning X2 off does not
  remove the attributes: KiCad writes them as `G04 #@! TF.FileFunction,...` comments instead, and
  the parser reads that form, so **the layers here are still named by their attributes and not by
  their filenames**. Do not reach for this board to test filename matching; `TopBotNames` is the one
  for that. What it covers that nothing else does is the comment form itself, the Protel extensions,
  and a single merged `.drl` that carries no attributes at all — so its role comes from its
  extension alone, and its holes are all read as plated because the file never says otherwise. The
  geometry is identical to `Millburn_Test_Board`, which makes the two comparable on purpose.

- `TopBotNames` is **PogoTest1 again**, under the same licence: the same geometry with its files
  renamed to the `Top` / `Bot` / `Dimension` / `Ln1_Cu` convention that Altium, Eagle and Olimex
  use, and with the `TF.FileFunction` attribute stripped out. Both halves of that are deliberate.
  The renaming is what it tests; the stripping is what makes it a test at all, because a file that
  still declares its own function is never asked what its name means. Only that one attribute went:
  the others record when the file was made and by what, and nothing reads them to decide a role.
  `Ln1_Cu.gbr` holds the top copper's geometry — an inner layer is never exported, so what is in it
  does not matter, only what it is called. `Top_Mask.gbr` and `Bot_Mask.gbr` are byte-identical, and
  that is not a mistake: PogoTest1's two mask files differ only in the attribute that was removed,
  because its mask openings are the same on both sides.
- `PogoTest1-Inch` is **PogoTest1's two drill files and nothing else**, converted arithmetically:
  every coordinate and diameter divided by 25.4, `METRIC` replaced by `M72`, and nothing else
  touched. It is the corpus's only inch-mode Excellon, which is how 6.29 survived — an inch file
  states its units with `M72` and no other line, that line was not read, and every drill in the
  board was reported at a fiftieth of its size. Not a board: there is nothing to realise and no
  Gerbers beside it, and the test that uses it parses the two files directly and compares the
  millimetres against PogoTest1's own. Being a conversion, it cannot catch the parser being wrong
  about inches in a way the conversion was also wrong about — only a unit not being read at all,
  which is the failure that happened.
- The `Arduino_*` boards come from [KiCad-Arduino-Boards](https://github.com/sabogalc/KiCad-Arduino-Boards)
  by Camilo Sabogal, released under the **WTFPL**, which places no condition on redistribution at
  all. They are third-party work, they are attributed here and in
  [THIRD-PARTY-NOTICES](../../THIRD-PARTY-NOTICES.md), and they carry no copyleft into this
  repository — which is the test any board has to pass to be committed (see the section below).

**Why real boards are committed at all.** Hand-written fixtures prove the parser handles the
*specification*; these prove it handles what an EDA tool actually *writes*, which is a different
question and the one that decides whether a board is cut correctly. It is not a theoretical
distinction — both parser bugs found so far came out of real files and neither would have been
caught by a spec-derived fixture:

- the closing `*` of an extended command was left attached to the body, so every aperture
  parameter parsed as `0.5*` and whole layers silently came out empty;
- KiCad writes a bare `D10` with no `*` terminator, which merges with the following line into
  `D10X0Y0D03`, so deferring aperture selection to the end of a block let the `D03` overwrite it.

Both produced a clean-looking *empty* result rather than an error. That is the failure mode real
files catch and synthetic ones do not.

## The boards

| Board | What it exercises |
|---|---|
| `GridStripConnector/` | A small panelised board with tabs. `RoundRect` aperture macro with `$1+$1` expressions, four outline arcs kept as arcs, six SMD pads across three named nets, X2 attributes throughout. |
| `PogoTest1/` | Two copper layers with pours (regions), PTH (16 holes, 2 tools) and NPTH (2 holes) with plating and tool functions, front and back silk, soldermask. The PTH count and the `ComponentPad` flash count on `B_Cu` cross-check each other. |
| `PogoTest1-AllLayers/` | The same design at a **later revision**, exported with every layer KiCad offers: 18 plated holes against 16, and two more copper objects. The only board here with **paste** layers (empty, which is its own case — an empty layer must produce no file rather than an empty one) or a **user** layer, which has to come through as an unrecognised role rather than being guessed at. Kept alongside the shorter export rather than replacing it: together they are a real before-and-after of one design, which is what the refresh and fingerprint machinery exists to tell apart. |
| `Arduino_Uno/` | An Arduino Uno R3 recreated in KiCad 8. **The board that found the slot bug**: seven plated slots written as strokes rather than flashes, which the drill reader discarded while the viewer drew them perfectly. Also the first board here with **drill files written as Gerber** rather than Excellon, **five drill sizes in one program** — four tool changes, where the rest of the corpus tops out at two sizes and one change, **five outline profiles** with interior mounting holes cut before the frame around them, and **spaces in every filename**, which nothing else here tests. 6,674 objects. |
| `Arduino_Mega_2560/` | The same design language at twice the size: 10,007 objects, 63 % copper coverage, **six drill sizes and five tool changes**, seven outline profiles, seven slots. The densest routing in the corpus and the one that most exercises the isolation gap check — it reports one unreachable gap on the bottom and eight on the top, which are real features of the design rather than a defect. The stress case; `Arduino_Uno` covers the same ground faster. |
| `Millburn_Test_Board/` | **The project's own feature-coverage board**, designed to exercise what users run into and to become the tutorial and screenshot board. Plated and non-plated holes from 0.3 to 3.5 mm — six of them in matched plated/non-plated pairs above any drill in the default library, so they are milled — three 1 mm plated slots, and edge cuts. **The board that found the routing page reading one feature per cutter**: six holes spiralled out by one end mill, and a page that said "1 hole". The only board here where a routing program mixes slots and milled holes with a tool change between them. Its KiCad sources are in [`design/Millburn_Test_Board/`](../../design/Millburn_Test_Board/). |
| `GridStripConnector_Panelized/` | **66 boards** (6 x 11) and a frame in one 165 x 107 mm panel: 11,414 objects, 198 copper islands. The board that found the outline bug — taking only the largest ring cut the frame and left every board attached, which nothing smaller reproduces, because on a single board the largest ring is the right answer. Note that its `Edge_Cuts` is a shared lattice with tab gaps, not 66 separate outlines, so it realises to 51 ring regions: a count of profiles is not a count of boards. It is also the optimizer's main benchmark. |

The `GridStripConnector*` and `PogoTest1*` boards are KiCad 10 exports; the `Arduino_*` boards are
KiCad 8. All carry `%TF%` attribute blocks. The optional pcb2gcode corpus
(see `MillBurn.Tests/GerberCorpus.cs`) is KiCad 9 and older, and uses the `G04 #@!` comment
encoding instead — between them the parser is exercised on both forms.

## Can the pcb2gcode test data live here?

Short answer: it could, and it should not.

pcb2gcode is GPL-3.0, and its test data is part of that work as distributed. Copying those files in
would mean this repository distributes GPL-3.0 material. That does not relicense anything of ours —
data files are not linked against and nothing here is derived from them — but it does make the
repository mixed-licence, and the whole reason [01 §9](../../Documentation/01-Architecture.md#9-licensing-strategy)
picked MIT with a clean-room rule was to keep the licensing answer a single sentence. "MIT, except
these directories, which are GPL-3.0 and carry their own notice" is a worse answer, and it is the
kind of thing that gets simplified back to "MIT" by whoever copies a file out of here next.

(Whether a Gerber file attracts much copyright at all is a real question — it is close to purely
functional data — but that is a judgement for a lawyer, and "probably fine" is a poor foundation
for a licence claim.)

**What we do instead**, in the order the coverage actually matters:

1. **Hand-written fixtures** for the specification: every aperture template, macro primitive,
   polarity sequence and coordinate format, written as inline strings in the tests. These prove the
   parser implements the standard.
2. **Real exports, committed here**, contributed by whoever owns the design. These prove it handles
   what EDA tools actually write, which is a different question and where both parser bugs so far
   have come from.
3. **`MILLBURN_GERBER_CORPUS`** points the optional corpus tests at a pcb2gcode checkout if the
   developer happens to have one. Nothing is copied, the tests skip cleanly without it, and it
   costs the repository nothing.

The gap that leaves is breadth across *other* tools — Altium, Eagle, older KiCad. That is filled by
adding boards to this directory, not by importing someone else's suite: a board here is one whose
provenance we know and whose licence is ours to state.

## Adding a board

Copy it in as its own directory and reference it by name through `RealBoards`. Two things to check
first, because a commit is forever:

1. **It is yours to publish, or its licence lets you.** Anything under client NDA or covering
   someone else's design does not belong here. Third-party boards are fine when the licence is
   permissive and the attribution is recorded — the `Arduino_*` boards are WTFPL, which is why
   they could come in and why pcb2gcode's GPL-3.0 fixtures could not. Anything else goes in a
   scratch folder beside the repository instead: `WorkingFolder/` is git-ignored for exactly this
   purpose, and `MILLBURN_BOARDS` points the fixtures elsewhere if you want to run against
   something private.
2. **It earns its place.** A board that exercises nothing the others already cover is
   maintenance with no return. Say in the table above what it adds.
