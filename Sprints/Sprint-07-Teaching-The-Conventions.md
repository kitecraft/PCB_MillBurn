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

## Size, and what gets dropped first

One entry, four stories, and the first is the risk: nothing in the app draws over the window today,
and the entry is frank that which layer behaves inside a `ScrollViewer` *"is the question that will
decide the implementation"*. Story 1 therefore opens with a short spike — the cut-out drawn three
ways against the status bar and against a layer row scrolled half out of view — and keeps the one
that behaves.

**What never drops: story 1, the first-load tour, and the first-save tip.** The first is the
machinery. The first-load tour is the one every new user meets, the moment the window first has a
board in it. The first-save tip is the sentence from the workshop that started the entry, and a
sprint that did not answer it would have missed its own point.

**The test-board offer (story 3) goes first** if something has to go: the tours already teach on the
operator's own board as each moment arrives, and story 3 is the one with the packaging question in
it. **Then the later tours**, last one first: the tool library, the preview, the findings — the
zero, tab and export tours and the two Help entries stay, because each guards a mistake that costs a
board.

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

## Story 1 — The overlay: a pill, an arrow and one sentence, in steps

**Problem.** Nothing in the app can draw over the window and point at one control. The entry's
research section lists what is unknown: which layer to draw on (`OverlayLayer`, an `AdornerLayer`,
or a top-level panel in the window's own grid), what happens when the target scrolls, resizes or
disappears, and how to dismiss on Esc and on any click without trapping focus or swallowing the
keystroke.

**What.** The machinery, with one tour on it to prove it:
- The window dimmed, a cut-out round one control, and a callout with one sentence beside it.
- **Tours of several steps**, driven by next and back — story 2's tours are sequences, not single
  tips — and a step that can **act before it points**: expand a layer row, open the toolpath-moves
  section, so the callout has the thing it explains in view.
- **Every tour ends with "Don't show tips again"**, the owner's own requirement, beside the plain
  close. It turns off every tour still to come, not only this one; **Reset tips** in Settings turns
  them back on.
- Dismissed by Esc or any click outside the callout, focus never trapped, the dismissing keystroke
  not swallowed.
- It follows a target that moves, and dismisses itself when the target goes — *"points at empty
  space is worse than not pointing"*.
- No animation that reduced motion does not turn off.
- **A "seen" flag per tour**, and one "no more tips" switch, in `AppSettings`, written through
  `SettingsKeeper` (one method per setting, as sprint 6 left it), and a **Reset tips** button in
  Settings that clears both.
- **Never in an automated run**, or every screenshot in the repository grows an overlay.
- **`--coachmark <key>[:<step>]`** forces a tour open, at a step, for a screenshot. (The entry
  proposed `--tip`, which the CLI already uses for a V-bit's tip width.)

**Done when** a tour can be forced open at any step headlessly and photographed against both a fixed
target (the status bar) and one in a scrolled list; next and back move through it; Esc and a click
each close it and nothing else happens; "Don't show tips again" stops every later tour and **Reset
tips** brings them back; and a shot run with no flag shows no overlay.

---

## Story 2 — The tours, at the moments they are needed

**Problem.** Each place below works by a convention the app relies on and never states. The owner set
the list on 2026-10-18 — *"Each of these should end with an option to disable further coaching."*

**What.** Eight tours, each firing once, *after* the moment that triggers it — the overlay is never in
the way of the thing it explains — and each ending with **Don't show tips again**. The first five are
the owner's list; the last three were added on 2026-10-18 after a discussion of where new users lose
a board.

1. **The first time a board is loaded** — the owner's sequence, with zero added as its last step:
   - **A. Project info**: what the board is, build on stock, refresh source — and **the board
     thickness**, which drives cut depth and tab height, and whose *"not saved in this project yet"*
     note exists because a new board was once cut to the previous board's depth.
   - **B. Layers**, with two rows opened as examples:
     - **B1. A G-code layer**, expanded: its operation, its bit, its depth and passes — what the
       mill will do with this layer.
     - **B2. An SVG layer**, expanded: what the laser gets, and that the laser's own software
       decides power and speed.
   - **C. The checks area**: what a check, an advice and a refusal are, and that a refusal is a
     file deliberately not written.
   - **D. The status bar's right-hand side**: the board's layers, objects and vertices, how long it
     took to load, and the frame time.
   - **E. Where zero is**: the rulers and the pointer readout, which say what the machine will call
     zero — the board's lower-left corner, or the stock's on a job built on stock. A wrong work zero
     is the costliest mistake a new user makes. The step closes by naming **Help ▸ Keyboard and
     mouse**.
2. **The first save** → the status bar's message area: *this is where the app tells you things* —
   the sentence that started 6.7, kept as the entry wrote it.
3. **The first time Findings is opened**: each finding is clickable and takes the viewport to the
   place; what the kinds of finding mean.
4. **The first preview**: the tour opens the **Toolpath moves** section and explains it — each kind
   of move, its colour and its toggle — and that the picture is read back out of the emitted G-code,
   not drawn from the toolpath that made it: the app's most load-bearing idea, with nothing on
   screen saying it.
5. **The first time the tool library is opened**: what a bit's numbers are for, and how the library
   chooses a bit for a layer.
6. **The first time "Build on stock" is ticked**: zero has just moved to the stock's corner, and the
   readout now says so — a short tour, because the moment it matters is the moment it changes.
7. **The first time tab handles are on screen** (a preview of an outline cut with tabs): drag a
   circle to move a tab along the cut path, click it for its own width, right-click to remove it,
   click the dashed path to add one, and what the letters are. Sprint 6's tab work is invisible
   without this — the only clue today is the cursor changing over a handle.
8. **The first export**: the review list, read before anything is written; what lands in the
   folder — each program, the companion page beside the drilling files, and the project page with
   the commands that would rebuild the export; and the dry run, for watching a job in the air
   first.

**And two entries in the Help menu:**
- **Help ▸ Tours** replays any tour on demand. **Reset tips** brings them all back at once; this
  brings back the one somebody wants — and it is how the bench pass (story 4) runs a tour without
  resetting a profile each time.
- **Help ▸ Keyboard and mouse** lists every shortcut in one place, and the viewport's gestures that
  appear in no menu at all: scroll to zoom, drag or middle-drag to pan, Shift-drag to measure,
  Escape to stop a check or drop a dragged tab, and the tab gestures. The menus show each shortcut
  beside its own item (F1, F5, F6, F7, Ctrl+S …) and nowhere lists them together.

The wording is the owner's to settle at the bench (story 4); the first draft follows
10-Style-and-Voice. Of the entry's own moments, the first export is tour 8, and the first refusal is
covered by the checks step of the first-load tour — left for story 4 to ask for on its own if the
bench wants it.

**Done when** each tour fires exactly once on a fresh profile, after its moment; never a second time
unless **Reset tips** is pressed or it is chosen from **Help ▸ Tours**; never on its own after
**Don't show tips again**; every step of every tour can be forced open by key and photographed
headlessly; and **Help ▸ Keyboard and mouse** lists every gesture the viewport answers to.

---

## Story 3 — The first run, on a real board

**Problem.** The entry's trap: *"on a first run there is no board open"*. Story 2's first-load tour
waits for a board, which is right — but somebody opening the app for the first time, with no Gerbers
to hand, meets an empty window and no tour at all. The entry's answer: offer to open the test board
itself — `Millburn_Test_Board`, built in the workshop as a tutorial as much as a fixture.

**What.** On the first run, with no board open, an offer to load the test board — which then starts
story 2's first-load tour on something real.
- **The test board ships with the app** (about 0.5 MB of Gerbers) beside the Help pages, the way
  `Help/` already does, and opening it never touches the operator's own work.
- **A tour run on the test board counts as seen**, so loading the operator's own first board does
  not run it again — and *Don't show tips again* in the offer itself turns everything off.
- **`--coachmark first-run`** forces the offer open for a screenshot.

**Done when** a fresh profile with no board is offered the test board; accepting it loads the board
and starts the first-load tour; the operator's own first board afterwards does not repeat the tour;
and the offer can be photographed headlessly.

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

**Tours beyond these eight.** Two more were discussed and left for the bench pass to ask for: the
first board with a bottom-side layer (mirroring, and flipping the stock), and the first export with
an unset machine profile — which may be better as a check than a tip, being a fact about this
machine rather than a convention. Beyond those, *"a coachmark on everything is a tutorial
nobody finishes"* — new moments are added later, one flag each, which is why the flags are per tour.

**A help system.** `Help/` and F1 already exist; this sprint teaches conventions in place, it does
not move documentation into the window.
