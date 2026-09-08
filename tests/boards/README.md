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

Both are KiCad 10 exports carrying `%TF%` attribute blocks. The optional pcb2gcode corpus
(see `MillBurn.Tests/GerberCorpus.cs`) is KiCad 9 and older, and uses the `G04 #@!` comment
encoding instead — between them the parser is exercised on both forms.

## Adding a board

Copy it in as its own directory and reference it by name through `RealBoards`. Two things to check
first, because a commit is forever:

1. **It is yours to publish.** Anything under client NDA or covering someone else's design does not
   belong here. Keep that in a scratch folder beside the repository instead — `MyGerbers/` and
   `MyGerbers2/` are git-ignored for exactly this purpose, and `MILLBURN_BOARDS` points the
   fixtures elsewhere if you want to run against something private.
2. **It earns its place.** A board that exercises nothing the existing two already cover is
   maintenance with no return. Say in the table above what it adds.
