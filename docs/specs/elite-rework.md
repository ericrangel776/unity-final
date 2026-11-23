# Elite rework: readable and punishable

Status: implemented on `feat/elite-rework`. This changes `docs/specs/m3-elite-trials.md`, whose trial rules (Veiled,
Counterpuncher, Hexer) are unchanged.

## Problem
Elites were the wall of the run. A pacing bot with god mode on lost about **95 HP per Elite room**, against 100 max HP.
The next-worst room kind (BountyWind) costs about 55. The user's playtest complaints:
- **Can't win clashes.** The Elite deck `{5,6,7,7,8,9}` + the Intensity bonus outvalues the starter deck (2-7) even at I2.
- **Hits are too heavy.** 21-27 per hit, and a counter-hit can do up to 41.
- **Break-stun combos.** An Elite is always alone, so crowd stun scaling never helps. Physical breaks do x1.5 posture,
  so two breaks posture-break the player into a 2 s stun, followed by a counter-hit.

Goal: the skill is reading and punishing the Elite, not tanking it. Target: about **45-55 HP per Elite room**.

## Rules (`Core/EliteRules.cs`)
| Lever | Rule | Value |
|---|---|---|
| Winnable clashes | Elite card value offset, added to the room bonus (clamped to [1,9]) | -2 |
| Lighter hits | HP damage the Elite's attacks deal (`OutgoingDamageScale`, as the boss uses) | x0.8 |
| No stun-lock | Player's break stun in an Elite room | x0.7 |
| No stun-lock | Player's posture damage taken in an Elite room | x0.67 |
| Readable tell | A heavy card (effective value >= 6, not a 0) winds up longer | +0.15 s |
| Punish window | After a heavy swing: **Overextended**, recover is longer | +0.6 s |
| Punish window | While Overextended: HP and posture damage taken (orange tint) | x1.5 |

Elite deck by Intensity after the offset: I1-4 `{3,4,5,5,6,7}`, I5-8 `{4,5,6,6,7,8}`, I9 `{5,6,7,7,8,9}`. At I2, 2 of 6
cards are heavy; at I9, 5 of 6. Harder Elites hit harder but commit more.

Unchanged: HP 250%, posture 150%, size, the trials, rewards.

## Wiring
- `RoomIntensity.EnemyCardValueBonus` adds the offset for Elite rooms.
- `RunDirector` sets the Elite's `OutgoingDamageScale` and adds an `EliteOpening` component.
- `RunDirector` also sets the player's break-stun and posture-taken scales on every room entry: the Elite values in an
  Elite room, 1 everywhere else.
- `EliteOpening` listens to `TelegraphStarted` and `Swung`. It extends the telegraph or the next recover through hooks
  on `TrainingDummyEnemy`, and owns the Overextended window: the multipliers and the tint. The window ends on its
  timer, at the next telegraph, or on death.
- Debug: Enemies tab -> "Elite: force Overextended".

## Verification
- EditMode tests: `EliteRulesTests` and the new `EnemyCardValueBonus` cases.
- PlayMode tests: `EliteOpeningTests`.
- Pacing: `RunTimingMeasurement` before and after (god mode, 6 seeds). Judge by the Elite mean damage against the
  45-55 target, while the other room kinds stay unchanged.
- Final numbers come from the user's playtest.
