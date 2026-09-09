# Test boards

Real KiCad 10 exports, committed as project content and used by `MillBurn.Tests` and
`MillBurn.GoldenTests`. They are contributed by the repository owner under the project's
[MIT licence](../../LICENSE).

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
| `GridStripConnector_Panelized/` | **66 boards** (6 x 11) and a frame in one 165 x 107 mm panel: 11,414 objects, 198 copper islands. The board that found the outline bug — taking only the largest ring cut the frame and left every board attached, which nothing smaller reproduces, because on a single board the largest ring is the right answer. Note that its `Edge_Cuts` is a shared lattice with tab gaps, not 66 separate outlines, so it realises to 51 ring regions: a count of profiles is not a count of boards. It is also the optimizer's main benchmark. |

All are KiCad 10 exports carrying `%TF%` attribute blocks. The optional pcb2gcode corpus
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

1. **It is yours to publish.** Anything under client NDA or covering someone else's design does not
   belong here. Keep that in a scratch folder beside the repository instead — `MyGerbers/` and
   `MyGerbers2/` are git-ignored for exactly this purpose, and `MILLBURN_BOARDS` points the
   fixtures elsewhere if you want to run against something private.
2. **It earns its place.** A board that exercises nothing the existing two already cover is
   maintenance with no return. Say in the table above what it adds.
