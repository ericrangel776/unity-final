---
name: spec
description: Interview the user about a feature and write an implementation spec to docs/specs/ before any code is written. Use for features bigger than a one-line change.
argument-hint: "<feature idea in a sentence>"
disable-model-invocation: true
---

# Write a spec

Feature idea: $ARGUMENTS

Goal: surface the decisions hidden in a vague idea BEFORE coding, then write a
spec that a fresh session can implement in one pass. Do not write code here.

## Steps
1. Explore just enough of the codebase to understand where this feature fits
   (use the `researcher` subagent if it needs a broad search).
2. Interview me with the AskUserQuestion tool: 2-4 questions per round, at most
   3 rounds. Ask only about real decisions: behavior in edge cases, data shapes,
   UX, what is out of scope, how we will know it works. Offer a recommended
   option for each. Skip anything you can decide from the code.
3. Write `docs/specs/<short-kebab-name>.md` with:
   - **Goal** - what and why, 2-3 sentences.
   - **Out of scope** - explicit list.
   - **Design** - files to create/change, functions/interfaces with signatures,
     data flow. Name real paths.
   - **Edge cases** - and the intended behavior for each.
   - **Steps** - ordered, each small enough to verify on its own.
   - **Verification** - exact commands/tests that prove it works end to end.
4. Reply with the spec path and: "Review it, then run /clear and say:
   `Implement docs/specs/<name>.md. Follow the steps in order and verify each one.`"
