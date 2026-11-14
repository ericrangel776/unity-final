# Spec: M3 room loop (M3 step 3)

## Goal
Play a run end to end in a new `M3_Run` scene: choose a Map Card, fight scaled dummies (or visit a Blue room),
collect the reward, and repeat until the boss falls or the player dies. Driven by `RunState` from step 1 and
`RunContent` from step 2. The screens are step 6, so here the flow is an API that tests (and later the UI) call.

## RunDirector (Gameplay)
One per run scene. Owns the `RunState` and moves through these states:

| State | Enters when | Leaves via |
|---|---|---|
| `ChoosingDoor` | run start, reward finished, Blue room left | `ChooseDoor(i)` |
| `Fighting` | combat card chosen | last enemy dies |
| `Reward` | combat room cleared (not boss) | `FinishReward()` (auto-skips an open draft) |
| `Utility` | Blue card chosen | `LeaveUtility()` |
| `Victory` | boss dies | `RestartRun()` |
| `Defeat` | player dies | `RestartRun()` |

- Events: `StateChanged`, `RoomStarted(MapCard)`, `RewardReady(CombatReward)`, `RunEnded(bool victory)`.
- Exposes `Run`, `State`, `CurrentReward`, `CurrentShop` / `CurrentForge` / `CurrentRest` (step 1 visit objects),
  and `Enemies` (live ones).
- **Entering a room:** cancel the player's actions, move the player to the south spawn, sync HP from `RunState`,
  rebuild the player's cycle from the run deck, then spawn enemies (combat) or create the visit (Blue).
- **Player deck = the run deck instances**, so Forge upgrades, Premium and drafted cards carry into fights.
  Burns reset every room (`IsBurnedThisFight` cleared on rebuild).
- **HP carries:** on clear, `RunState.SetHp(player.Hp)`. Rest heals `RunState`, and the player is synced on the next room.
- **Player death** = `Defeat`. The run turns off the player's auto-respawn.
- **`RestartRun()`** builds a fresh `RunState` (new seed) in place: enemies removed, player revived. No scene reload.

## Enemy setup (per combat room)
- Count `RoomIntensity.EnemyCount`. Spawned on an arc in the north half, facing the player.
- Deck `RunContent.EnemyDeckFor(kind)`. Each enemy gets its own seed from `RunState.Rng`.
- **Card values** + `RoomIntensity.EnemyCardValueBonus` as a room mod (clamped like everything else).
- **Max HP** = 60 x `EnemyHpPercent` x size class. Size class: mob 100%, Elite 250%, Boss 400%. Max posture:
  mob 100%, Elite 150%, Boss 200%. Elite and Boss are drawn 1.4x / 1.8x larger. New Core helper:
  `RoomIntensity.EnemyMaxHp(baseHp, room)` and `EnemyPosturePercent(room)`.
- **Tempo** `EnemyTempoPercent / 100`: move speed x tempo, telegraph and recover times / tempo. The parry window
  (`BodyHitDelay`) is not scaled.
- Run enemies don't respawn. A dead enemy is removed after its usual respawn delay.
- **KindledGround:** the player's cards of `BuffElement` get `PlayerElementBuff` as a room mod. Other rooms clear it.
- Elite trials are step 4. In this step an Elite is a big dummy with no trial effect.

## Code changes outside the new files
- `DeckRuntimeManager`: `UseRunDeck(instances)` (persistent instances instead of fresh ones from assets) and
  `SetRoomValueMod(amount, onlyElement)`, applied on every `Reinitialize`.
- `TrainingDummyEnemy`: `RespawnOnDeath` (default true, so M1 is unchanged) and `ApplyTempo(multiplier)`.
- `CombatantStatus`: `SetMaxHp(int)` (refills), `SetHp(int)` (clamped to [1, max] while alive),
  `MaxPostureScale`.
- `PlayerCombatInput`: `RespawnOnDeath` (default true).

## Scene
`DeckArpg/Build M3 Run Scene` (in `GreyboxArenaBuilder`): the same environment, cameras, clash FX and HUD as M1,
reusing the existing prefabs, plus a `RunDirector` wired to `RunContent.asset` and the dummy prefab.
Added to Build Settings **after** `M1_Arena`.

## Assumptions (mine, flag if wrong)
- **No physical exit door yet.** The next offer is dealt as soon as a room ends. Walking to a door to open the
  Map Card choice is presentation, so it belongs with the step 6 UI.
- **M1_Arena stays first in Build Settings** until step 6/7 adds a title or flow screen.
- **Player max HP** comes from the Player prefab (100).
- Until step 6, the run is driven from tests or the Inspector context menu.

## Out of scope
UI screens (step 6), Elite trials (step 4), relic effects (step 5), boss AI (M4).
