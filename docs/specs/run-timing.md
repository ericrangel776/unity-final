# Spec: Room/run timing (fun-pass item 5)

Prereqs: the user playtests enemy variants, then `feat/enemy-variants` and `feat/debug-menu` are merged to `main`.
Branch `feat/run-timing` from `main`. Decisions come from the user interview on 2026-11-21.


## Goal
Measure how long each room and each full run takes when a scripted "decent player" plays them, so pacing changes can
be compared run to run. This extends the boss-only bot harness to mob rooms and to whole seeded runs, and writes a
readable per-room and per-run report.

## Out of scope
- Logging human playtests to a file (the F1 `DebugRunTimer` stays on-screen only).
- Timing menus, door walks or reward screens (they resolve instantly in the harness).
- A pre-variants baseline or a worktree back-port.
- Retuning anything based on the numbers (a separate decision with the user).
- Changing the boss tactics or the output format of `BossFightMeasurement`.

## Design
**Core (pure, EditMode-testable):** `Assets/_Project/Scripts/Core/RunTimingLog.cs` (`DeckArpg.Core`).
```csharp
public enum RoomOutcome { Cleared, Loss, Timeout, Utility }
public readonly struct RoomTiming {
    public RoomTiming(int roomNumber, MapCard card, string mix, int waves, float seconds,
                      int hpBefore, int hpAfter, int maxHp, RoomOutcome outcome);
    // + read-only fields of the same names
    public int HpLost => Math.Max(0, HpBefore - HpAfter);
}
public enum RunResult { Victory, Loss, Incomplete }
public sealed class RunTimingLog {
    public RunTimingLog(int seed);
    public int Seed { get; }
    public IReadOnlyList<RoomTiming> Rooms { get; }
    public RunResult Result { get; private set; }      // Incomplete until Finish
    public void Add(in RoomTiming room);
    public void Finish(RunResult result);
    public float CombatSeconds { get; }                // sum of Seconds over non-Utility rooms
    public int HpLost { get; }                         // sum of HpLost
    public string FormatRun();                         // header line + one line per room
    public static string FormatSummary(IReadOnlyList<RunTimingLog> runs);
        // win rate; mean/median run combat time; per MapCardKind: count, mean, median, max seconds, mean HP lost;
        // TIMEOUT count. Empty input -> "no runs".
}
```
Formatting uses `CultureInfo.InvariantCulture` and one decimal, for example
`room 2  Vanguard I4  mix B2 R1  waves 1  31.4s  HP 88->71/100  Cleared`.
The `mix` string is built by the caller from `RunDirector.CurrentMix` (B/C/R counts for Brute/Caster/Rusher).

**Bot:** generalize `Assets/_Project/Tests/PlayMode/BossFightBot.cs`. Keep the class name, so the boss measurement and
its docs stay valid. This is the smallest change.
- Keep `Init(PlayerCombatInput player, BossEnemy target)` with the same behaviour.
- Add `Init(PlayerCombatInput player, RunDirector director)` for **run mode**. Each frame it picks a target:
  `director.Boss` if one is alive (then the existing boss branch runs unchanged), else the nearest live
  `TrainingDummyEnemy` in `director.Enemies`.
- Split the target into `TrainingDummyEnemy body` (telegraph, `Swung`, `Status`, transform) and an optional
  `BossEnemy boss`. The boss-only steps (Sleight wind-up, punish windows, Deadlock) run only when `boss != null`.
  Steps 4 (telegraph) and 5 (neutral chip/reload) use `body`.
- Swing subscriptions: keep a `HashSet<TrainingDummyEnemy>`. Subscribe `Swung` on each enemy the first time it is
  seen (wave 2 spawns later), and unsubscribe all of them in `OnDestroy`. A swing schedules a parry only if that enemy
  is within `reach + 1` of the player.
- Reach uses the target's `localScale.x`, as today. Casters keep their distance, so the existing "close in" movement
  handles them. No new kiting logic.
- With no live target (between waves, or a cleared room), stand still and reload if the hand is empty.

**Measurement:** new `Assets/_Project/Tests/PlayMode/RunTimingMeasurement.cs`, `[Explicit]`. It is a copy of the
`BossFightMeasurement` structure: the same scene load and teardown, and `Timeout(3600000)`.
- `Seeds = { 1, 2, 3, 4, 5, 6 }`. The door RNG is `new System.Random(seed * 7919 + 17)`, and it picks a uniform index
  of `director.Run.CurrentOffer`.
- Loop over `director.State` the same way `BossFightMeasurement` does:
  - `ChoosingDoor`: `ChooseDoor(rng)`.
  - `Fighting`: time the room with `Time.realtimeSinceStartup`, unscaled, which is what the player feels (and what
    `DebugRunTimer` uses). The clock starts on entering the room and stops when the state leaves `Fighting`, so the
    wave-2 delay is included. Cap: 180 s for mob rooms and 300 s for the boss. On the cap, log TIMEOUT and call
    `DebugDefeatAllEnemies()`.
  - `Reward`: relic 0, then draft 0, then `FinishReward()`, the same as `BossFightMeasurement`.
  - `Utility`: on a Rest, take the heal through the `RestVisit` API that `RestView` key 1 calls. Then call
    `LeaveUtility()`. Log the room as `Utility` with 0 s.
  - `Defeat`: log the room as Loss and finish with `RunResult.Loss`. `Victory`: finish with `RunResult.Victory`.
- Attach the bot once per run with `Init(player, director)`. **The player is in god mode** (changed on 2026-11-21, see
  the addendum). A room's `HP a->b` is the damage the bot took, counted down from the room's starting HP.
- Write `TestResults/runtiming.txt`. It holds every finished run's `FormatRun()` followed by `FormatSummary(all)`,
  and it is rewritten after each seed, so a crash keeps the partial data. Also `Debug.Log` each run with the
  `[RunTiming]` prefix.

**Smoke test (regular suite):** new `Assets/_Project/Tests/PlayMode/MeasurementBotTests.cs`.
`Bot_ClearsAForcedLowIntensityRoom`: load `M3_Run`, call `RestartRun(1)`, then
`DebugEnterRoom(new MapCard(MapCardKind.Vanguard, 1))`. Attach the bot in run mode with `GodMode = true`, so the test
is deterministic about survival. Wait on real time with a 90 s cap, and assert that the state leaves `Fighting` into
`Reward`. Unload the scene in `[UnityTearDown]`, and reset `Time.timeScale = 1` (see `unity-playmode-tests.md`).

**Debug menu:** nothing to add. The new code is test harness code, and the Info-tab timers already exist.

## Edge cases
- Wave 2 spawns while the bot is mid-room: it is picked up as a new nearest target, and its `Swung` is subscribed
  when first seen.
- The target dies mid-telegraph: the next frame retargets, and a scheduled parry still fires if a `0` is ready (harmless).
- No enemies alive but the room hasn't ended (wave delay): the bot idles and reloads, and the clock keeps running.
- The player dies: the room is logged as Loss, the run ends, and the next seed starts with a fresh scene load. (This can't happen under god mode; the code path stays.)
- A room hits the cap: logged as TIMEOUT, then debug-cleared, and the run continues. The reward is taken as normal.
- The boss room uses the existing boss logic. `BossFightMeasurement` still compiles and runs unchanged.
- Shop or Forge: left without using it (0 s, `Utility`). A Rest takes the heal.
- `FormatSummary` with an empty list, or with a kind that was never visited: prints "no runs" or skips the kind,
  with no divide-by-zero.
- A `MapCardKind` with an odd number of samples: the median takes the middle value. With an even count it averages
  the two middle values.

## Steps
1. `RunTimingLog` + `RoomTiming` in Core, with EditMode tests in `Tests/EditMode/RunTimingLogTests.cs` (add, totals,
   HP lost clamp, Utility excluded from combat time, Finish, FormatRun line, FormatSummary mean/median/odd-even/empty,
   invariant culture). Verify: EditMode suite.
2. Refactor `BossFightBot` into the body/boss split with no behaviour change. Verify: the full PlayMode suite is
   green, and `BossFightMeasurement` still compiles (no `error CS` in the log).
3. Add run mode (target selection, swing subscriptions, idle). Add `MeasurementBotTests`. Verify: the PlayMode suite,
   including the new test.
4. Add `RunTimingMeasurement` (`[Explicit]`). Verify: run it alone and check that `TestResults/runtiming.txt` has 6
   runs and a summary.
5. Paste the summary into `docs/notes/CURRENT.md` as the pacing baseline. Note any TIMEOUTs or suspicious rooms for
   the user, and do not retune.

## Verification
Close the Editor first. Wrap every run in `timeout 1200`.
- EditMode: `timeout 1200 "/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform EditMode -testResults "$(pwd -W)/TestResults/editmode.xml" -logFile "$(pwd -W)/TestResults/editmode.log"`.
  Expect exit 0 and `<test-run result="Passed"`.
- PlayMode: the same command with `-testPlatform PlayMode` and `playmode.*` file names. Expect 0 failed.
  `MeasurementBotTests` passes, and the Explicit count goes up by 1.
- Measurement: the PlayMode command plus `-testFilter DeckArpg.Tests.PlayMode.RunTimingMeasurement` (use a long
  timeout, about 3600). Then read `TestResults/runtiming.txt`.
- `grep "error CS" TestResults/*.log` shows nothing.

## Addendum (2026-11-21): god-mode timing
The first measurement, without god mode, lost all 6 runs, mostly to Elites (I2-I9 kill the bot 1v1: about 25 damage per
hit, 0 parries, clashes W0 L4-5). Two attempts at a stronger bot failed. Attempt 2 (parry retry, answering the most
urgent nearby telegraph, an equal-card answer, and chipping only in the swing recovery) made rooms 2-4x longer and still
won nothing, so it was dropped. The user chose god-mode timing: a pacing baseline doesn't need a bot that wins.
What was kept:
- The bot fix for a heal-only hand at full HP (the bot pokes or reloads instead of stalling).
- Per-room bot diagnostics (`BossFightBot.StatsLine()`: hits, damage by bot decision, clash W/L/N), written to the
  report's "Bot detail per room" section.
- The Elite trial in the room label.

