---
name: describe-test
description: Read a test back in plain language using a context-free subagent, then check that description against the test and against what the code under test actually does. Use when a test was just written or changed, when reviewing tests before a merge, or when asked to verify that a test really tests what its name claims.
---

# Describe a test, then check the description

A test can pass while guarding nothing. The usual causes are an assertion on a value the test itself
computed, a name that promises more than the body delivers, and — when the test was written
alongside the code by the same author, human or model — a shared misunderstanding that makes both
sides agree about the wrong thing.

This skill separates those. A subagent that has never seen the conversation reads the test cold and
says what it does. Then you check that description against the test and against the production code.
Where the two accounts differ, something is wrong: the test, the code, or the name.

## How to run it

**1. Identify the target.** One test, or one test class, named by the user or obvious from what was
just written. If several changed, do them one at a time — a mixed report is hard to check.

**2. Spawn a context-free subagent.** Use the Agent tool with `subagent_type: "general-purpose"`.
Never `fork`: inheriting this conversation is exactly what the skill exists to avoid. Give it the
file path and nothing else about intent — no summary of what the test is supposed to prove.

Prompt it with:

> Read `<path>` — only this file, plus any file it needs for a type definition. Do not read the
> production code it tests, do not read git history, and do not search for related tests.
>
> Report, in plain language, for each test in the file:
> 1. **What it does** — the steps, in order, as if narrating someone operating the software.
> 2. **The inputs** — each one, and whether it is hard-coded, computed in the test, randomised,
>    loaded from a fixture file, or a real measurement. Quote the literal values.
> 3. **The assertions** — what is actually checked, in full. Say plainly when an assertion checks a
>    value the test computed itself rather than one the system produced.
> 4. **What would have to break in the software for this to fail.** If nothing obvious would, say
>    so.
>
> No praise, no suggestions, no judgement of quality. Describe only what is there.

**3. Read the report against the test.** Line by line. You are looking for:

- **A name that over-promises.** The test is called `TheOutlineCutsTabsToTheDepthItPromises` and the
  report says it checks a pass count. The name is a claim; make the body meet it or change the name.
- **A self-fulfilling assertion.** The report says an expected value was computed by the same
  formula the code under test uses. That test cannot fail for the reason it exists.
- **Inputs that do not reach the assertion.** Elaborate setup, then a check that would pass with
  half of it missing.
- **A missing case the description makes obvious** — one axis tested and not the other, the happy
  path and no refusal.

**4. Check it against the production code.** Read the code under test now. Does it do what the
report says the test observes? A description that matches the test but not the code has found a
shared misunderstanding, which is the expensive kind.

**5. Report back briefly.** What the test really guards, anything the check turned up, and the fix
if there is one. If everything holds, say that plainly and stop — this skill is a check, not an
excuse to rewrite.

## What this is not

Not a coverage tool, not a style review, and not a rewrite. If the test is sound, say so in two
sentences. The value is in the cases where the plain-language account and the test disagree, and
those are rare enough to be worth reading properly when they appear.
