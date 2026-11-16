---
paths:
  - "Assets/_Project/Tests/PlayMode/**"
---

# PlayMode test gotchas (loaded only when working on PlayMode tests)

- In PlayMode tests, `WaitForFixedUpdate` / `Time.time` loops never advance while `timeScale == 0`;
  wait on frames + `Time.realtimeSinceStartup` with a timeout, and reset `Time.timeScale = 1` in TearDown.
- A scene a test loads stays loaded for later fixtures, and its live objects (dummy, player, `HitStop`)
  react to their hitboxes/clashes and cause flaky failures. `ArenaSmokeTests` unloads `M1_Arena` in a
  `[UnityTearDown]`; any new test that loads a scene must do the same.
- `yield return null` in a PlayMode test resumes after `Update` but before `LateUpdate`, so a view that
  redraws in `LateUpdate` still shows last frame's value. To compare a view to the state it draws, freeze
  `Time.timeScale = 0` first (frames still advance) and yield twice.
