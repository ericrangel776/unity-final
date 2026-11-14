# Spec: Purple Elite trials (M3 step 4)

## Goal
A Purple Elite room (`MapCardKind.EliteTrial`) spawns one large dummy plus its rolled trial. Each trial tests
something different (see `m3-overview.md`). Strength scales with the card's Intensity through the existing
`RoomIntensity` helpers. Every trial is applied in `RunDirector.SpawnEnemies` when `room.Trial != None`.

| Trial | I 1-3 | I 4-6 | I 7-9 |
|---|---|---|---|
| Veiled: value revealed this long before the swing | 0.40 -> 0.15 s (`VeiledRevealSeconds`, linear) | | |
| Counterpuncher: 0-cards added to its deck | 1 | 2 | 3 |
| Hexer: HP lost per Card Break you suffer | 0 | 0 | 2 |

## Veiled
- `TrainingDummyEnemy.VeiledRevealSeconds` (0 = off, the default). `IsTelegraphVeiled` is true while a
  telegraph has more than that long left before the swing.
- `EnemyTelegraphView` shows `?` instead of the value while veiled. The wind-up fill still shows, so the
  player always knows *when* the swing comes, just not *what*.
- The reveal time is absolute: room tempo shortens the wind-up but not the reveal. If the wind-up is shorter
  than the reveal, the value shows from the start.

## Counterpuncher
- Its deck = the elite deck + `CounterpuncherZeros(I)` copies of `RunContent.EnemyZero`. 0-cards go to the
  Parry slot / reserve as usual, so the dummy's normal swing AI never plays them. They come back on its reload.
- New `CounterpuncherTrial` component on the elite watches the player's `CombatActionController.HitboxSpawned`.
  When a player attack spawns while the player is within `counterRange` (3.5 m), the elite isn't dead or
  stunned, and enough 0s are ready, it **spends them right away** and fires one 0 on the **next frame**, aimed at
  the player. Next frame means `activationTick` is strictly greater than the player's, so the 0 breaks the attack.
- **What it answers (`CounterRules.ZerosToSpend`):**
  - Deck cards and the Value 1 poke: costs 1 zero. Pokes are the bait.
  - Sleights: only if **2 zeros** are ready, and it spends both. A multi-hit Sleight is answered (or let
    through) once, keyed by its activation tick.
  - So at I 1-3 Sleights always get through. At I 4-9 they get through once you've baited out a zero.
- If the player's attack is gone by the next frame (it already clashed), the spent 0s are wasted. That is
  the reward for baiting.

## Hexer
- `RunDirector` listens to the player's `CardBroken` during a Hexer fight. For each Card Break the player suffers:
  - the broken card is **burned for the room** via new `DeckCycle.BurnFromCycle(card)`, which respects the
    Floor of 3 (`SleightRules.CanBurn`). A card a 0 broke sits in `deletedThisCycle`, and it still counts
    toward the floor because it would come back on reload.
  - `HexerHpPerBreak(I)` HP is paid with `CombatantStatus.SpendHp`, which is **non-lethal** (floors at 1),
    like Overburn.
- A broken poke or Sleight has no single card, so **only the HP cost applies**. A broken Sleight still cracks
  its stock through the normal Card Break rule.
- Burns reset next room like every other burn (run deck rebuild clears `IsBurnedThisFight`), and Overburn
  restores them.

## New / changed code
- Core: `CounterRules` (pure, `ZerosToSpend`), `DeckCycle.BurnFromCycle`, `DeckCycle.ZerosReady`.
- Gameplay: `TrainingDummyEnemy.VeiledRevealSeconds` + `IsTelegraphVeiled`; new `CounterpuncherTrial`;
  `RunDirector` trial setup in `SpawnEnemies` + the Hexer listener.
- UI: `EnemyTelegraphView` shows `?` while veiled.

## Tests
- EditMode: `CounterRules` table; `BurnFromCycle` (from discard, from deleted, Floor of 3 blocks, already burned,
  unknown card); `ZerosReady`.
- PlayMode (`EliteTrialTests`, loads `M3_Run`): each trial's setup (reveal time, zeros in the deck,
  component attached); veiled flag goes false at the reveal; a counterpuncher breaks a played card with a 0;
  a Sleight with only 1 zero gets through; a Hexer break burns the card and costs HP at I >= 7.

## Assumptions (mine, flag if wrong)
- The counter doesn't interrupt the elite's own telegraph. It is a separate, instant action.
- `counterRange` is measured centre to centre on the ground plane, so throws from beyond 3.5 m are safe.
  That's a third way to deal with it, alongside baiting and Sleights.
