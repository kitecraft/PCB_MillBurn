# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this
repository.

**Read [AGENTS.md](AGENTS.md) first.** It holds the briefing every assistant needs — environment,
commands, layout, testing conventions, the rules that shape the code, the process, and the traps.
This file holds only what is specific to Claude Code, so that there is one place to keep current
rather than two that drift.

## Before merging anything

**Run `/code-review` at medium effort before every merge into a release branch or into `main`**,
with an eye on anything that could affect performance: the UI's responsiveness, the geometry
pipeline, the optimizer, or the size and shape of the emitted G-code and SVG. Fix what it finds,
or say why it was left.

**A deeper pass is the product owner's call, not an automatic one.** `/code-review` at `xhigh` is
worth running when the work is large enough to deserve it — the end of a sprint, most likely — and
they will ask for it. Do not escalate on your own because a medium pass found something; medium
finding something is the ordinary case, not a signal.

## After writing or changing a test

**Run `/describe-test`.** A subagent with none of this conversation's context reads the test back in
plain language — its steps, its inputs with their literal values, its assertions, and what would
have to break for it to fail. Check that account against the test *and* against the production code.
It exists to catch a test and a bug that agree with each other, which is the failure mode a test
written beside its code is most prone to.

Skills live in `.claude/skills/`.

## Working here

- **Ask before committing, merging, pushing or publishing.** The author's own mill and laser run
  what this produces.
- **Verify by running**, not by a green suite: `--shot` renders the real window headlessly, and the
  CLI prints the travel and line-count numbers to quote for any performance claim. AGENTS.md lists
  the flags.
- **The workshop is the source of requirements.** When the author reports something from the bench,
  their words go into the roadmap with the measurement that backs them, and the fix cites it.
