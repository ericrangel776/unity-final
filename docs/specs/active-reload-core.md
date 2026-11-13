# Spec: Active Reload core rules (M2 step 2)

## Goal
Replace the instant reshuffle with an Active Reload. Pressing Reload (when the draw queue is empty)
starts a 1.2 s timing bar. A well-timed second press reloads instantly, and a Perfect press also
grants a short move-speed boost. A mistimed press jams the reload, which then takes longer than
not pressing at all. This step adds the pure-C# state machine in `DeckArpg.Core` with EditMode tests.
Gameplay/UI wiring is step 4/5.

## Decisions (from interview, 2026-11-13)
- **Miss jams.** No second press: the reload finishes when the bar ends (1.2 s). A press outside
  the Good zone jams it: the bar stops and the reload finishes at `JamTotalSeconds` (2.0 s after
  the bar started), so mistiming costs more than not trying.
- **Perfect + Good tiers.** Perfect = instant reload + boost. Good = instant reload, no boost.
  This is the same grade vocabulary (and tolerances) as the Deadlock ring.
- **Boost = move speed.** x1.3 for 2 s. Core exposes `SpeedMultiplier`, and `PlayerMotor` applies it in step 4.
- **During the bar:** move, dash and poke only. Hand plays, Parry and stocking are locked
  (`LocksCardPlays`). Core exposes the flag, and `PlayerCombatInput` enforces it in step 4.

## Assumptions (mine, flag if wrong)
- The sweet spot is the bar's **center**, `TargetTime = 0.6 s` (the design doc says "center zone").
  Tolerances: Perfect ±0.06 s, Good ±0.15 s.
- The jam end is fixed at 2.0 s from the start whenever you mistime (early or late), so the
  penalty is predictable.
- The boost **refreshes** to 2 s on another Perfect. It doesn't stack.
- The bar runs on **scaled** time (the gameplay caller passes `Time.deltaTime`), so hit-stop pauses it.
  The boost also runs on scaled time.
- `ActiveReload` owns the call to `DeckCycle.Reload()` on completion, so Core can be tested end to end.

## Out of scope
- Input/HUD/motor wiring, the overhead bar visual (steps 4-5).
- Overburn Reload (step 3).
- Partial 1-card reloads on 0-parries (design doc table row; this is the Shattered Hourglass relic, M3).
- Enemy/boss reloads (M4).

## Design

### New: `Assets/_Project/Scripts/Core/ActiveReload.cs`
```csharp
public enum ReloadGrade { None, Miss, Good, Perfect }   // None = let the bar run out
public enum ReloadState { Idle, Running, Jammed }

public static class ActiveReloadRules
{
    public const float BarSeconds = 1.2f;
    public const float TargetTime = 0.6f;
    public const float PerfectTolerance = 0.06f;
    public const float GoodTolerance = 0.15f;
    public const float JamTotalSeconds = 2.0f;
    public const float BoostSpeedMultiplier = 1.3f;
    public const float BoostSeconds = 2f;

    // Grade for a press `offset` s after the bar started; outside [0, BarSeconds] or NaN -> Miss.
    public static ReloadGrade Grade(float offset);
}

public sealed class ActiveReload
{
    public ActiveReload(DeckCycle deck);              // throws on null
    public ReloadState State { get; }
    public float Elapsed { get; }                     // s since Begin (0 when Idle)
    public float Progress01 { get; }                  // Running: Elapsed/BarSeconds; Jammed: Elapsed/JamTotalSeconds; Idle: 0
    public bool LocksCardPlays => State != ReloadState.Idle;
    public float BoostRemaining { get; }
    public float SpeedMultiplier { get; }             // BoostSpeedMultiplier while BoostRemaining > 0, else 1
    public event Action<ReloadGrade> Completed;       // after deck.Reload(); grade = how it finished

    public bool Begin();                              // Idle && deck.CanReload -> Running
    public ReloadGrade Press();                       // Running only, else None (ignored)
    public void Tick(float deltaSeconds);             // advances bar + boost; ignores <= 0
    public void Cancel();                             // back to Idle, no reload (e.g. death/respawn); boost kept
}
```

**Flow**
- `Begin()`: from Idle with `deck.CanReload`, the bar starts at `Elapsed = 0` in Running. Otherwise it returns false and nothing changes.
- `Press()` while Running: grade from `Elapsed`.
  - Perfect: complete now, start the boost (`BoostRemaining = BoostSeconds`).
  - Good: complete now.
  - Miss: switch to Jammed (`Elapsed` keeps counting).
- `Tick(dt)`: the boost counts down (in every state, floored at 0).
  - Running: `Elapsed += dt`. At `>= BarSeconds`, complete with `None`.
  - Jammed: at `>= JamTotalSeconds`, complete with `Miss`.
- Complete: `deck.Reload()`, then back to Idle and `Elapsed = 0`, then raise `Completed(grade)`.

## Edge cases
| Case | Behavior |
| --- | --- |
| `Begin` while queue not empty / nothing to reshuffle | false, stays Idle |
| `Begin` while Running/Jammed | false, the bar doesn't restart |
| `Press` while Idle or Jammed | `None`, no state change (no mashing out of a jam) |
| Press at exactly 0.6 ± 0.06 | Perfect (inclusive bounds) |
| Press at 0.6 ± (0.06, 0.15] | Good |
| Press before 0.45 or after 0.75 | Miss, jam |
| Large `dt` that overshoots the end | completes once, `Elapsed` resets |
| `Tick` with dt <= 0 | ignored |
| Perfect while a boost is active | refreshed to 2 s, not stacked |
| `Cancel` mid-bar | Idle, no reload, no `Completed` event |
| Deck emptied of reloadables by completion time | `deck.Reload()` returns false. Still completes (Idle + event), since cards can only be added to discard during the bar |

## Steps
1. `ActiveReloadRules.Grade` + constants, with tests (bounds, NaN, out-of-window).
2. `ActiveReload` Begin/Tick/no-press completion against a real `DeckCycle`, with tests.
3. Press: Perfect/Good/Miss→jam, boost countdown/refresh, Cancel, `Completed` grades, with tests.
4. Run the full EditMode + PlayMode suites.

## Verification
- EditMode: `timeout 900 "/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform EditMode -testResults "$(pwd -W)/TestResults/editmode.xml" -logFile "$(pwd -W)/TestResults/editmode.log"`.
  The exit code must be 0, there must be 0 failures, and the log must have no `error CS` lines.
- PlayMode: same with `-testPlatform PlayMode` / `playmode.*`. It must still pass (no Gameplay changes).
