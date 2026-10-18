# Sprint 7 — Teaching the conventions

**Drafted** 2026-10-18, **not yet opened.** **Release branch:** `release/0.8.0`, to be cut from
`main` once v0.7.0 is tagged. Written on `release/0.7.0` at the product owner's request, so the plan
ships with the release before it — *"so it's in the current release (for anyone who wants to look
at the sprint work)."*

**The product owner's direction, in the owner's words:** *"For the next sprint, I'd like to work on
6.7. I think that is a big story that will require much testing on my part. And will be very
helpfull to new users. Let's make that the next sprint."*

**The theme is the first hour.** Every sprint so far has been about what the app does with a board.
This one is about the person in front of it who has not used it before. The app makes deliberate
choices that are right and unusual — it never interrupts to say something worked; it reads its own
G-code back to draw the preview; it refuses rather than guesses and says so in a panel, not a
dialog — and each of them is invisible until somebody is told. 6.7 came from the workshop as
exactly that: *"I really like how MillBurn uses the status messages in the footer of the window.
However, it's not a common thing … it can be a bit hard to get used to at first."*

---

## How this sprint was picked

**The defect list first**, as the standing rule requires:

```
grep -n 'defect · open' Documentation/06-Roadmap-and-Risks.md
```

**Two came back, and neither is in** — on the same grounds as every sprint since 2.

| # | what | why it is not here |
|---|---|---|
| [6.45](../Documentation/06-Roadmap-and-Risks.md#645-block-apertures-and-the-aperture-transforms--defect--open--no-fixture-exists-yet) | Block apertures and the aperture transforms | No fixture exists; the unblocker is a Gerber set, not a line of code |
| [6.47](../Documentation/06-Roadmap-and-Risks.md#647-isolation-cuts-inside-a-hole-that-is-about-to-be-drilled--defect--open--found-in-the-workshop-low-priority-a-later-sprint) | Isolation cuts inside a hole that is about to be drilled | Built in sprint 3 and withdrawn by the product owner; the shape is still wrong |

**Then the request.** The owner named one entry, and it is a sprint on its own:
[6.7](../Documentation/06-Roadmap-and-Risks.md#67-teaching-the-conventions-coachmarks-and-a-first-run-walkthrough--enhancement--open--needs-research)
(matrix G22, *Not started*). Its entry already says what is decided and what needs research, and
the stories below follow its split.

### And the four

| # | story | where it came from |
|---|---|---|
| [6.7](../Documentation/06-Roadmap-and-Risks.md#67-teaching-the-conventions-coachmarks-and-a-first-run-walkthrough--enhancement--open--needs-research) | The overlay: a pill, an arrow and one sentence | The entry's research section — the machinery everything else stands on |
| 6.7 | The four coachmarks | The entry's list: first save, first preview, first refusal, first export |
| 6.7 | The walkthrough, on a real board | The entry's trap: on a first run there is no board open |
| 6.7 | The bench pass | The owner: *"a big story that will require much testing on my part"* |

**The order is the entry's.** The overlay first, because the coachmarks and the walkthrough are both
drawn with it, and the entry's open questions — which Avalonia layer, what happens when the target
scrolls away — are answered there or not at all. The coachmarks before the walkthrough, because the
walkthrough has to know which of them it has covered. The bench pass last, as a story of its own, so
that what the owner finds at the bench has a place to land before the sprint closes rather than
after it.

### Not in this sprint, and why

| # | what | why it is not here |
|---|---|---|
| [6.75](../Documentation/06-Roadmap-and-Risks.md#675-copper-clearing--enhancement--open--asked-for-by-the-product-owner-2026-10-18) + [6.76](../Documentation/06-Roadmap-and-Risks.md#676-multi-tool-rest-machining--enhancement--open--asked-for-by-the-product-owner-2026-10-18) | Copper clearing, and multi-tool rest machining | Asked for the same day, and added to the list of requests rather than to a sprint, in the owner's words: *"This is just being added to the enhancement request list."* Their shape is decided in the entries |
| [6.18](../Documentation/06-Roadmap-and-Risks.md#618-a-paste-stencil-to-3d-print--enhancement--open) + [6.73](../Documentation/06-Roadmap-and-Risks.md#673-placement-data-for-a-pick-and-place--enhancement--open--asked-for-by-the-product-owner-2026-10-16) | The paste stencil, and placement data | Still wish-list, as the owner set them at sprint 6's picking |

---

## Size, and what gets dropped first

One entry, four stories, and the first is the risk: nothing in the app draws over the window today,
and the entry is frank that which layer behaves inside a `ScrollViewer` *"is the question that will
decide the implementation"*. Story 1 therefore opens with a short spike — the cut-out drawn three
ways against the status bar and against a layer row scrolled half out of view — and keeps the one
that behaves.

**What never drops: story 1, and the first-save coachmark.** The first is the machinery; the second
is the sentence from the workshop that started the entry, and a sprint that did not answer it would
have missed its own point.

**The walkthrough goes first** if something has to go. The coachmarks teach the conventions on their
own, one at a time, as each moment arrives; the walkthrough is the larger build and the one with the
packaging question in it (the test board shipping with the app). **Then the coachmarks after the
first**, last one first: export, refusal, preview.

---

## How a story closes

As in sprints 1 to 6: a `Closed` block with the date, a plain verdict against *Done when*, the
manual test in enough detail that somebody else could run a similar one, what was observed including
the sentence from the bench, and what is left open. `/code-review` before every merge, `/describe-test`
on every test file created or edited.

**And two notes for this sprint.** Every piece of this is UI that a screenshot has to be able to
show — the entry's own rule: *"a coachmark that can only be seen by being a new user is the one
piece of UI nobody can review."* So each story closes with the shots, forced open from the command
line. And none of it closes headlessly alone: whether a callout reads as help or as nagging is a
judgement at the bench, which is what story 4 is for.

---

## Story 1 — The overlay: a pill, an arrow and one sentence

**Problem.** Nothing in the app can draw over the window and point at one control. The entry's
research section lists what is unknown: which layer to draw on (`OverlayLayer`, an `AdornerLayer`,
or a top-level panel in the window's own grid), what happens when the target scrolls, resizes or
disappears, and how to dismiss on Esc and on any click without trapping focus or swallowing the
keystroke.

**What.** The machinery, with one tip on it to prove it:
- The window dimmed, a cut-out round one control, and a callout with one sentence beside it.
- Dismissed by Esc or any click, focus never trapped, the dismissing keystroke not swallowed.
- It follows a target that moves, and dismisses itself when the target goes — *"points at empty
  space is worse than not pointing"*.
- No animation that reduced motion does not turn off.
- **A "seen" flag per tip** in `AppSettings`, written through `SettingsKeeper` (one method per
  setting, as sprint 6 left it), and a **Reset tips** button in Settings.
- **Never in an automated run**, or every screenshot in the repository grows an overlay.
- **`--coachmark <key>`** forces one open for a screenshot. (The entry proposed `--tip`, which the
  CLI already uses for a V-bit's tip width.)

**Done when** a tip can be forced open headlessly and photographed against both a fixed target (the
status bar) and one in a scrolled list; Esc and a click each close it and nothing else happens; a
shot run with no flag shows no overlay; and the seen flags are written once and reset by the button.

---

## Story 2 — The four coachmarks

**Problem.** The four moments the entry names, each a convention the app relies on and never states:
- **First save** → the status bar: *this is where the app tells you things.* The one that prompted
  the entry.
- **First preview** → the toolpath is parsed back out of the emitted G-code, not drawn from the
  toolpath that made it — the app's most load-bearing idea, with nothing on screen saying it.
- **First refusal** → the checks panel, because a refusal that is not read did not work.
- **First export** → the review list, which exists to be read before anything is written.

**What.** Each on the machinery from story 1, firing once, *after* the action has happened — the
overlay is never in the way of the thing it explains. The wording is the owner's to settle at the
bench (story 4); the first draft follows 10-Style-and-Voice.

**Done when** each fires exactly once on a fresh profile, never a second time unless **Reset tips**
is pressed, and can be forced open by key.

---

## Story 3 — The walkthrough, on a real board

**Problem.** The entry's trap: *"on a first run there is no board open"*, and a tour of greyed-out
controls teaches nothing. Its answer: have the walkthrough open the test board itself —
`Millburn_Test_Board`, built in the workshop as a tutorial as much as a fixture.

**What.** The same overlay, driven by next and back, offered on the first run.
- **The test board ships with the app** (about 0.5 MB of Gerbers) beside the Help pages, the way
  `Help/` already does, and the tour opens it rather than the operator's own work.
- **It suppresses the coachmarks it has covered**, or the first save after the tour re-explains the
  status bar to somebody who has just been told.
- **`--tour <step>`** opens it at a given step, for a screenshot.

**Done when** a fresh profile is offered the tour; the tour opens the test board and has something
real to point at from its first step; the first save after finishing it does not re-explain the
status bar; and every step can be photographed headlessly.

---

## Story 4 — The bench pass

**Problem.** Whether a callout reads as help or as nagging, whether a sentence lands, whether a tour
step is one too many — none of it is a test result. The owner: *"a big story that will require much
testing on my part."*

**What.** The owner's sessions with a fresh profile, and the changes they ask for: wording,
placement, which moments earn a tip and which do not. Each finding goes into 6.7's entry with the
words it was said in, as every bench finding has.

**Done when** the owner has run the first hour on a fresh profile and says so, and every finding is
either changed or argued in the entry.

---

## Deliberately out

**Tips beyond the four moments.** *"A coachmark on everything is a tutorial nobody finishes"* — new
moments are added later, one flag each, which is why the flags are per tip.

**A help system.** `Help/` and F1 already exist; this sprint teaches conventions in place, it does
not move documentation into the window.
