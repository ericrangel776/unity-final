# Spec: Active Reload core rules (M2 step 2)

## Goal
Replace the instant reshuffle with an Active Reload. Pressing Reload (when the draw queue is empty)
starts a timing bar. A well-timed second press reloads instantly, and a Perfect press also grants
a short move-speed boost. A mistimed press jams the reload, which then takes longer than not
pressing. To keep the player reading it every time, **each reload rolls a new bar speed, sweet-spot
position and on-screen prompt position**. This step adds the pure-C# state machine in
`DeckArpg.Core` with EditMode tests. Gameplay/UI wiring is step 4/5.

## Decisions (from interviews, 2026-11-13)
- **Miss jams.** No second press: the reload finishes when the bar ends. A press outside the
  Good zone jams it, and the reload finishes `JamExtraSeconds` (0.8 s) *after* the bar would have
  ended, so mistiming always costs more than not trying.
- **Perfect + Good tiers.** Perfect = instant reload + boost. Good = instant reload, no boost.
- **Boost = move speed.** x1.3 for 2 s. Core exposes `SpeedMultiplier`, and `PlayerMotor` applies it in step 4.
- **During the bar:** move, dash and poke only. Hand plays, Parry and stocking are locked (`LocksCardPlays`).
- **Variable speed: random per reload.** The bar duration is rolled uniformly in [0.8 s, 1.6 s] at
  `Begin` and stays constant for that bar.
- **Variable sweet spot: random per reload.** The zone center is rolled uniformly in [0.40, 0.85]
  of the bar (always some reaction time; the Good zone never leaves the bar).
- **Zone size = fixed % of the bar.** Perfect ±5%, Good ±12.5% of the bar (the old sizes at 1.2 s).
  A faster bar gives less real time in the zone, so speed really raises the difficulty.
- **Variable prompt position: random per reload.** One of 6 anchors around the player, never the
  same anchor twice in a row. Core picks it, and the HUD (step 5) places the bar there.

## Assumptions (mine, flag if wrong)
- Grading is on bar **progress** (`Elapsed / BarDuration`), with inclusive bounds and a 1e-4 float margin.
- The boost **refreshes** to 2 s on another Perfect. It doesn't stack.
- The bar runs on **scaled** time (the caller passes `Time.deltaTime`), so hit-stop pauses it. So does the boost.
- The RNG is injected (`System.Random`) so tests and seeded runs are deterministic.
- `ActiveReload` owns the call to `DeckCycle.Reload()` on completion.

## Out of scope
- Input/HUD/motor wiring, the bar visual, mapping anchors to screen offsets (steps 4-5).
- Overburn Reload (step 3). Partial 1-card reloads on 0-parries (relic, M3). Enemy reloads (M4).

## Design

### `Assets/_Project/Scripts/Core/ActiveReload.cs`
```csharp
public enum ReloadGrade { None, Miss, Good, Perfect }   // None = let the bar run out
public enum ReloadState { Idle, Running, Jammed }
public enum ReloadPromptAnchor { Above, UpperRight, Right, Below, Left, UpperLeft }

public static class ActiveReloadRules
{
    public const float MinBarSeconds = 0.8f, MaxBarSeconds = 1.6f;
    public const float MinTarget01 = 0.40f, MaxTarget01 = 0.85f;
    public const float PerfectTolerance01 = 0.05f, GoodTolerance01 = 0.125f;
    public const float JamExtraSeconds = 0.8f;
    public const float BoostSpeedMultiplier = 1.3f, BoostSeconds = 2f;

    // Grade for a press at bar progress `progress01` with the zone centered on `target01`.
    // progress outside [0, 1] or NaN -> Miss.
    public static ReloadGrade Grade(float progress01, float target01);
}

public sealed class ActiveReload
{
    public ActiveReload(DeckCycle deck, Random random);   // throws on null
    public ReloadState State { get; }
    public float Elapsed { get; }
    public float BarDuration { get; }        // rolled at Begin
    public float Target01 { get; }           // rolled at Begin
    public ReloadPromptAnchor Anchor { get; }// rolled at Begin, never repeats the previous one
    public float JamTotalSeconds => BarDuration + ActiveReloadRules.JamExtraSeconds;
    public float Progress01 { get; }         // Running: Elapsed/BarDuration; Jammed: Elapsed/JamTotalSeconds; Idle: 0
    public bool LocksCardPlays => State != ReloadState.Idle;
    public float BoostRemaining { get; }
    public float SpeedMultiplier { get; }
    public event Action<ReloadGrade> Completed;

    public bool Begin();          // Idle && deck.CanReload -> roll duration/target/anchor, Running
    public ReloadGrade Press();   // Running only, else None
    public void Tick(float dt);   // advances bar + boost; ignores <= 0 / NaN
    public void Cancel();         // Idle, no reload; boost kept
}
```

## Edge cases
| Case | Behavior |
| --- | --- |
| `Begin` while queue not empty / nothing to reshuffle | false, stays Idle, nothing rolled |
| `Begin` while Running/Jammed | false, no re-roll |
| `Press` while Idle or Jammed | `None`, no change (no mashing out of a jam) |
| Press at `Target01 ± 0.05` of the bar | Perfect (inclusive) |
| Press at `Target01 ± (0.05, 0.125]` | Good |
| Anything else in the bar | Miss, jam until `BarDuration + 0.8 s` |
| Large `dt` overshooting the end | completes once |
| Perfect while boosted | refreshed to 2 s, not stacked |
| `Cancel` mid-bar | Idle, no reload, no event. The next `Begin` still avoids the last anchor |
| `deck.Reload()` fails at completion | still completes (Idle + event) |

## Verification
- EditMode: `timeout 900 "/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform EditMode -testResults "$(pwd -W)/TestResults/editmode.xml" -logFile "$(pwd -W)/TestResults/editmode.log"`.
  The exit code must be 0, there must be 0 failures, and the log must have no `error CS` lines.
- Roll tests: across many seeds, every duration/target stays in range, both ends of the ranges
  are actually reached (spread > 80% of the range), and the anchor never repeats back to back.
- PlayMode: same with `-testPlatform PlayMode`. It must still pass.
