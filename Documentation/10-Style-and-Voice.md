# 10 — Style and voice

Rules that were already being followed, written down so they survive a new contributor, a new
session, or a year away from the code. Where a rule has a reason, the reason is given: a rule whose
reason is lost gets applied where it does not belong.

---

## 1. Comments say why

The code says what it does. A comment repeating that is noise that has to be maintained.

```csharp
// The flat lap takes the slope out of the floor the last ramp left. Under a through cut there
// may be no floor to flatten: when the last ramp starts below the underside of the board,
// every point along it is already through, and another lap is time spent cutting spoilboard.
```

Worth a comment: a decision with an alternative, a number that came from somewhere, a bug that this
shape prevents, an assumption that could stop being true. **A defect that was found and fixed is
worth recording where it was**, because the next person to tidy that code will otherwise reinvent
it.

A type or member's `<summary>` says what it is *for*, not what its name already says. Long summaries
are welcome where the subject is subtle; the SVG writer and the optimizer both earn theirs.

## 2. Names are words

`BreakThroughNm`, `ShortLastLap`, `PlacingLayers` — not `btNm`, `slp`, `pl`. Abbreviations save
typing once and cost reading forever. The exceptions are the ones the domain already uses and
everybody says aloud: `Gerber`, `SVG`, `G-code`, `DRC`, `rpm`.

Booleans read as claims: `Mirrored`, `Levellable`, `WritesProgram`. Methods that answer a question
are named as the question's subject: `FlatLap(...)`, not `CheckFlatLap(...)`.

## 3. Units are in the name, and nanometres are the currency

Geometry is `long` nanometres end to end, because floating-point millimetres accumulate error over a
panel and because Clipper2 wants integers. Anything holding a length says its unit:
`BoardThicknessNm`, `SpanMm`, `FeedMmPerMin`.

Millimetres exist for people: they appear at the edges — settings the operator types, text the
operator reads — and are converted immediately. `Nm.FromMillimetres` and `Nm.ToMillimetreString` are
the doors.

## 4. Refuse rather than guess

Anything that writes a file, or reports a number a person will act on, says "I cannot" rather than
producing something plausible. A dry run that cannot be made safe hands back the original and says
why; an alignment whose two holes disagree with the geometry refuses the fit; a version the update
check cannot read is reported as unreadable rather than as "up to date".

The test for this: **what does a wrong answer here cost?** If it costs copper, time, or trust,
refusing is the feature.

## 5. Emitted text names its source

Every derived number in an emitted file says where it came from — the cutter's stepdown, the layer's
break-through, a setting and where to find it:

> 1.20 mm deep, 0.30 mm of it the layer's break-through: laps of 0.40 + 0.40 + 0.40 mm — the 0.8 mm
> end mill's 0.50 mm stepdown, a short last lap spread evenly (Settings > Milling).

This is why the app adds a sentence rather than a second control when a setting confuses somebody:
a value already decided somewhere gets its source named, not a duplicate picker.

Emitted text stays neutral about what happens away from the machine. The app's job ends at the file:
name the step, never the chemistry.

## 6. UI text says what will happen

Buttons are verbs with objects — *Write files…*, *Start from the program*, *Check for updates* —
never *OK* where something more specific fits. A confirmation's buttons say what they do (*Save*,
*Discard*), so that reading fast does not mean reconstructing the question from the answers.

Explanations sit under the control they explain, in the smaller, quieter style, and say the
consequence rather than the mechanism: *"High enough to see daylight under it from across the
workshop, which is the point."*

## 7. Tests are sentences, and they say what they guard

A test name is a claim: `TheOutlineCutsTabsToTheDepthItPromises`,
`AnUnreadableVersionIsUnknown`, `EveryHoleIsApproachedThenPlunged`. The `<summary>` says what would
break in the real world if the test failed — which is what tells a future reader whether to fix the
test or the code.

Prefer a test that reads the emitted artefact over one that inspects an intermediate object: the
file is what the machine runs. Where a number came from a real machine, put the number in the test
and cite where it was measured.

Property and golden tests carry their own rule: a golden file that changes is a decision, not a
chore. Read the diff.

## 8. Prose, in documents and in the app

British spelling: colour, behaviour, optimise, centre. .NET's own APIs keep their spelling, so
`SerializeToUtf8Bytes` stays as it is and a property beside it does not.

Sentences over bullet fragments where the thought has a shape. Numbers with units. No exclamation
marks. No "simply", "just", "obviously" — if it were obvious the sentence would not be needed.

The documents in this folder explain **why the code is shaped as it is**, for whoever maintains it.
`Help/` explains **how to use the app**, for whoever runs a machine. Neither borrows the other's
voice, and material in the wrong one makes both worse.

## 9. Shape of the code

- **Small types with names.** A record with four well-named properties beats a tuple.
- **`required` and `init` over constructors** for option bags, so a caller reads as a description.
- **Guard at the boundary:** `ArgumentNullException.ThrowIfNull` on public entry points; inside a
  module, trust the module.
- **Catch what you can name.** `catch (Exception ex) when (ex is IOException or
  UnauthorizedAccessException)`, never a bare `catch`.
- **Analysers are not advisory.** Warnings are errors, and the fix is the code rather than a
  suppression, unless the suppression carries a comment saying why.
- **No `#region`.** If a file needs one, it needs splitting, or it needs the `// ---- section` rule
  the larger files already use.

## 10. Commits

A commit message's first line says what changed, in the voice of the change rather than of the
author: *"The notices ship as a page, not as markdown"*. The body says why it was worth doing, what
was measured if anything was, and what was deliberately left alone.

Commits that land on a release branch are squashed to one per story; the release branch merges into
`main` with a merge commit, because a release is a real event.
