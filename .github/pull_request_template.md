<!--
  Pull requests go into the sprint's release branch (release/X.Y.Z), never into main.
  CONTRIBUTING.md has the whole process; this template is the short version.
-->

## What this changes

<!-- One or two sentences. The problem first, then what you did about it. -->

## What you measured

<!--
  Required for anything touching geometry, the optimizer, the emitted G-code or SVG, or how long
  the app takes to do something. A before-and-after number is worth more than a description:

      millburn-cli export <board> --write -o out/     # travel, line counts, time estimates

  "Not applicable" is a fine answer for a documentation or UI-text change — say so rather than
  leaving this empty.
-->

## Checks

- [ ] `dotnet test PCB_MillBurn.slnx -c Release` passes, with no warnings — Release, because
      that is what CI builds, and `#if DEBUG` code is excluded from it
- [ ] New behaviour has tests; a fix has a test that fails without it
- [ ] Ran it, not just the tests — `--shot` for the window, the CLI for emitted files
- [ ] Reviewed (`/code-review` at medium effort; again at `xhigh` if it found anything)
- [ ] Emitted files still name where their numbers came from, and refuse rather than guess
- [ ] `CHANGELOG.md` has an entry under Unreleased, or this change does not warrant one

## Anything a reviewer should look at first

<!-- The part you are least sure about, or the decision that could reasonably have gone the other way. -->
