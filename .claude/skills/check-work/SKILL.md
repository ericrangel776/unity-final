---
name: check-work
description: Prove the current change actually works with evidence (tests, running the app, comparing against main) before reporting it as done. Use before saying a feature or fix is complete.
---

# Check the work

"It looks right" is not evidence. Produce proof that the change does what was
asked, the way a careful engineer would before asking for review.

## Steps
1. Restate what the change is supposed to do in one or two sentences, and list
   the 3-6 behaviors that must be true (include at least one edge case and one
   error case).
2. Run the automated checks from CLAUDE.md (tests, lint, type check, build).
3. Exercise the real behavior, not just unit tests:
   - CLI / script: run it with realistic input, including a bad input.
   - API: call the endpoint (e.g. `curl`) and show the response.
   - UI: open it in the browser and check the actual page.
   - Library code: a short throwaway script calling the public function.
4. If it is a bug fix, show the behavior before (on `main` or via `git stash`)
   and after.
5. For anything non-trivial, delegate an independent check to the `verifier`
   subagent with the list of behaviors from step 1.
6. Report as a checklist: each behavior -> PASS/FAIL -> the evidence (command and
   the key line of output). If anything fails or could not be checked, say so
   plainly; do not round up to "done".
