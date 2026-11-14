# Spec: M2 gameplay wiring (M2 step 4)

## Goal
Make the M2 Core rules playable. Stock cards with a held modifier and fire Sleights whose slot-1
shape gives each its own attack (Strike lunge combo, Throw boomerang, Arcana expanding blast).
Replace the instant reshuffle with the randomized Active Reload (timing press, card lock, speed
boost). Start Overburn Reloads. A Card Break cracks a partial stock. HUD/visuals for stock and
the bar are step 5.

## Decisions (from interview, 2026-11-13)
- **Stock input = hold modifier.** New `Stock` action (Left Shift / gamepad Left Shoulder). Holding
  Stock + slot key stocks that card. Stock + Parry stocks the 0. A slot key alone still plays.
- **Overburn = Stock + Reload.** Plain Reload starts a normal reload. Reload while the bar runs is the
  timing press.
- **Sleights are distinct per shape** (below).
- **Locked presses are dropped.** While the reload bar runs (`LocksCardPlays`), slot/Parry/stock presses
  aren't buffered, and `Execute` also refuses them. Move, dash and poke still work.

## Assumptions (mine, flag if wrong)
- **Strike Sleight:** a 3-hit combo. Strike boxes (1.3x size) spawn 0.12 s apart while the player lunges
  forward (10 m/s for 0.3 s, no i-frames). The lunge stops on stun/death/dash.
- **Throw Sleight:** a piercing boomerang (1.5x radius). It flies out for 0.45 s, then homes back to the owner
  and despawns on arrival (safety lifetime 2 s). Each pass can hit each body once.
- **Arcana Sleight:** a blast centered on the player whose radius grows from 0.5 m to 2x `arcanaRadius` over 0.35 s.
- **Damage split:** a Sleight's total damage is split across its possible hits per body
  (`SleightRules.HitCount`: Strike 3, Throw 2, Arcana 1). Per-hit damage = `ceil(total / hits)`, so
  landing everything ≈ the Step-1 total. Every hit carries the full Sleight *value* for clashes
  (each hit clashes on its own, which is the design doc's "multi-hit Sleights resolve per hit" safeguard).
- All hits of one Sleight share its activation tick, so a 0 answered after activation wins against any hit.
- A Card Break cancels the remaining hits (pending combo hits too), and the stun ends the lunge.
- Sleight hitboxes have no source card, so a 0-break of a Sleight deletes nothing from the cycle
  (its cards are already in discard/burned).
- **Any** Card Break (including a poke being broken) calls `InterruptStock()`.
- The timing press is handled immediately (not buffered), so the buffer can't skew the grade. Starting a reload
  is buffered like other actions (it waits out recovery/stun); a buffered Reload only ever starts the bar, so a
  double-tap during recovery can't be graded as an instant Miss.
- `DeckRuntimeManager` owns the `ActiveReload` (seeded like the deck). In `Update` (scaled time) it ticks
  the bar and, only while the bar doesn't lock card plays, `AdvanceStockTimer` (a partial stock can't expire
  during a reload/jam it can't act through). `Reinitialize()` (respawn) cancels the bar, clears the speed
  boost and resets the Overburn cost.
  Death cancels the bar immediately.
- Stocking counts as an action (normal recovery), so firing a Sleight takes 3 presses spaced by recovery.
- `PlayerCombatInput` copies `ActiveReload.SpeedMultiplier` to `PlayerMotor.MoveSpeedMultiplier` every frame
  (walk speed only, not dash).
- The training dummy keeps reshuffling instantly (enemy reloads are M4).

## Out of scope
- HUD: stock slots, reload bar at its anchor, Overburn cost preview (step 5).
- Same-element finisher shockwave, element status effects (later).
- Arena regeneration: no new components are added, so the prefab/scene stay valid.

## Design

### Core
- `SleightRules.HitCount(CardShape shape)`: Strike 3, Throw 2, Arcana 1.
- `AttackPayload`: new `int HitCount` (>= 1; always 1 for non-Sleights), an optional trailing ctor param
  `hitCount = 1`, and `FromSleight(..., int hitCount = 1)`.
- `CombatMath.Damage`: Sleight per-hit damage = `ceil(total / HitCount)` (integer math).

### Input: `Assets/InputSystem_Actions.inputactions`
- New `Player/Stock` Button action. Bindings: `<Keyboard>/leftShift` (Keyboard&Mouse), `<Gamepad>/leftShoulder` (Gamepad).
- `ProjectSetupTests` gains a `"Stock"` case.

### `DeckRuntimeManager`
- `public ActiveReload Reload { get; private set; }`, created in `Awake` before the first `Reinitialize`.
- `Update`: `Reload.Tick(Time.deltaTime)`; `Cycle.AdvanceStockTimer(Time.deltaTime)` unless `Reload.LocksCardPlays`.
- `Reinitialize`: also `Reload.Cancel()`, `Reload.ResetBoost()` and `Reload.ResetOverburnCost()`.
- `HandleCardBroken`: delete on a 0-break (as now), then `Cycle.InterruptStock()`.

### `PlayerMotor`
- `float MoveSpeedMultiplier { get; set; }` (default 1, clamped >= 0), applied to walking.
- `bool TryLunge(Vector3 direction)` / `bool IsLunging`: a forced move at `lungeSpeed` for `lungeDuration`.
  It overrides move input, adds no i-frames, and is refused or stopped while stunned/dead/dashing.

### `AttackHitbox`
- `internal void MakeBoomerang(float outSeconds, Transform returnTo)`: after `outSeconds` it clears the
  per-body hit memory (second pass) and steers back toward `returnTo`, despawning within 0.6 m.
- `internal void MakeGrowing(float fromRadius, float toRadius, float seconds)`: sphere radius + visual
  lerp over `seconds` (FixedUpdate).

### `CombatActionController`
- `public void SpawnSleight(in AttackPayload payload, Vector3 aim)`: dispatch on `payload.Shape`.
  - Strike: a coroutine spawns `HitCount` boxes `sleightStrikeInterval` apart.
  - Throw: `Spawn` + `MakeBoomerang`.
  - Arcana: `Spawn` + `MakeGrowing`.
- `CancelActiveAction()` also stops a pending combo coroutine.
- `public bool IsSleightActive`: a combo is pending or a Sleight hitbox is alive (for tests/HUD).
- New serialized tunables (defaults as in Assumptions).

### `PlayerCombatInput`
- `CombatCommand` adds `StockSlot1..3`, `StockParry`, `Overburn` (appended; existing values unchanged).
- Update:
  - A Reload press while the bar is Running is an immediate `Press()`.
  - While Idle it buffers `Reload`, or `Overburn` when Stock is held.
  - Slot/Parry presses buffer as `Stock*` when Stock is held. They're dropped while `LocksCardPlays`.
  - `motor.MoveSpeedMultiplier = deck.Reload.SpeedMultiplier`.
- Execute:
  - Card/stock commands return false while locked.
  - `Stock*` → `TryStock*`. On `Fired`, build `FromSleight(..., HitCount(shape))`, call
    `combat.SpawnSleight`, and `motor.TryLunge(aim)` for Strike.
  - `Reload`: if Running, `Press()` (true unless ignored); else `Begin()`.
  - `Overburn`: `BeginOverburn(status)`.
- `HandleDied`: also `deck.Reload.Cancel()`.

## Edge cases
| Case | Behavior |
| --- | --- |
| Stock held + slot during reload bar | dropped |
| Poke / dash during reload bar | work |
| Reload press while Jammed | ignored (Core) |
| Stock+Reload with nothing burned / unaffordable | refused, no HP spent |
| Card Break mid-combo | remaining hits never spawn, lunge stops (stun) |
| Boomerang owner dies/destroyed | flies on until its safety lifetime ends |
| Card Break with empty stock | no-op |
| Respawn | stock cleared, bar cancelled, speed boost cleared, Overburn cost reset |
| Reload double-tap buffered during recovery | first starts the bar, second is dropped (no jam) |
| Partial stock while the bar runs/jams | stock timeout paused |
| Dummy decks | unaffected (still `Cycle.Reload()` directly) |

## Steps
1. Core `HitCount` + damage split, with EditMode tests.
2. `Stock` input action + ProjectSetupTests case.
3. `DeckRuntimeManager` reload/stock ownership + break interrupt.
4. `PlayerMotor` speed multiplier + lunge.
5. `AttackHitbox` boomerang/growth + `CombatActionController.SpawnSleight`.
6. `PlayerCombatInput` commands/lock/reload press/Overburn.
7. PlayMode tests: each Sleight shape, lock, reload press, Overburn HP, break interrupt, boost, respawn reset.
8. Full EditMode + PlayMode runs.

## Verification
- EditMode and PlayMode batchmode commands from CLAUDE.md (wrapped in `timeout 900`). Exit code 0, 0
  failures, no `error CS`.
- Existing `ArenaSmokeTests` keep passing (the prefab/scene still load and the player still plays/pokes).
