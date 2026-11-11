---
name: ship
description: Verify, commit, push, and open a pull request for the current changes.
argument-hint: "[optional note for the PR description]"
disable-model-invocation: true
allowed-tools: Bash(git status *), Bash(git diff *), Bash(git log *), Bash(git branch *), Bash(git switch *), Bash(git checkout -b *), Bash(git add *), Bash(git commit *), Bash(git push *), Bash(gh pr create *)
---

# Ship it

The state below is collected before you start, so you do not need extra calls
to look it up:

- Branch: !`git branch --show-current`
- Status: !`git status --short`
- Diff summary: !`git diff HEAD --stat`
- Recent commits (for message style): !`git log --oneline -8`

Extra note from the user: $ARGUMENTS

## Steps
1. If there are no changes, say so and stop.
2. If the branch is `main` or `master`, create a branch first:
   `git switch -c <type>/<short-name>` (type = feat, fix, docs, refactor, test, chore).
3. Run the project's test and lint commands from CLAUDE.md. If anything fails,
   stop and report; do not ship broken code.
4. Review the diff for things that should not be committed: debug prints,
   commented-out code, secrets, unrelated changes. Ask me before removing anything
   that is not obviously leftover.
5. Stage the relevant files by name (not `git add -A` blindly) and commit with a
   message in the repo's style: `<type>: <what changed>`, plus a body explaining
   why if it is not obvious.
6. `git push -u origin <branch>`.
7. `gh pr create` with a title matching the commit and a body containing:
   **Summary** (bullets), **Why**, **How it was tested** (commands + results),
   and the user's note if given.
8. Reply with the PR URL.
