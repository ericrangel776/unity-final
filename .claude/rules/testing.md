---
paths:
  - "tests/**"
  - "**/test_*.py"
  - "**/*_test.py"
  - "**/*.test.{js,ts,jsx,tsx}"
  - "**/*.spec.{js,ts,jsx,tsx}"
  - "**/*Tests.cs"
---

# Testing rules (loaded only when touching test files)

- Write the failing test first when fixing a bug; confirm it fails, then fix.
- One behavior per test. Name tests after the behavior: `test_returns_empty_list_when_no_matches`.
- Cover the edge cases: empty input, None/null, invalid input, the error path.
- Never weaken, skip, or delete an assertion to make a test pass. If a test looks
  wrong, stop and explain why to the user.
- No network or real API calls in unit tests; mock them at the boundary.
- Tests must be deterministic: no sleeps, fixed seeds, no dependence on test order.
