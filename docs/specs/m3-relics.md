# Spec: Relics (M3 step 5)

## Goal
Relics are earned and their six effects work in fights. The choice screen is step 6, so here it is an API
(`RunDirector.ChooseRelic(i)`) that tests and, later, the UI call. Effects are in `m3-overview.md`; the details
below were settled with the user on 2026-11-14.

## Earning relics
- A cleared room offers a relic choice when `CombatReward.OffersRelic`: always after an Elite, 1-in-3 after a
  Frantic (I 9) room, never after the boss (unchanged rules from step 1).
- The choice is **2 distinct relics you don't own**, rolled from the run RNG (`RelicRules.RollOffer`). If only
  1 is left, it's offered alone. **If you own all 6, you get `RelicFallbackCurrency` (30) instead**, paid with the
  room's currency.
- `CombatReward.RelicOptions`, `TryPickRelic(i, run)` (one pick), `IsRelicClaimed`. `RunDirector.ChooseRelic(i)`
  in the Reward state. A relic choice left open when the reward closes is forfeited. The step 6 screen will
  require a pick.

## Effects
| Relic | Effect | Details |
|---|---|---|
| **Gilded Quill** | Drafts offer +1 card; skipping pays +10 more | `RunState.CompleteRoom` passes `extraOptions` and the extra skip bonus to `RewardRoller`. |
| **Cinder Lantern** | First Overburn each room costs no HP | `ActiveReload.FreeOverburnsPerRoom = 1`. The free one doesn't count toward the escalation, so the next costs 10%. |
| **Tempered Coil** | A Perfect Active Reload restores 1 burned card | A random burned card goes to the **bottom of the Draw Queue** (into an empty hand slot if the queue is empty). |
| **Ashen Sigil** | Each Sleight burn: +10% swing speed for the room (stacks) and heals 3 HP | Only burns caused by firing a Sleight (not Premium plays, cracked stocks or Hexer). "Swing speed" = the player's 0.25 s action lock is divided by `1 + 0.1 x stacks`. Resets each room. |
| **Shattered Hourglass** | Your 0-break returns 1 spent card to the Draw Queue and deals 15 posture damage | When your 0 breaks an enemy card: a random discard card goes to the bottom of the Draw Queue (same placement as Tempered Coil), and the enemy takes 15 posture (can posture-break). An empty discard means only the posture damage. |
| **Iron Will Idol** | First Sleight after each Active Reload can't be broken by an enemy 0 | Any completed reload (any grade, Overburn too) arms it. The next Sleight fired consumes it. The payload carries `ZeroProof`, and a 0 vs a zero-proof attack = **the attack wins**. Not armed at room start. Doesn't stack. A Counterpuncher answering it loses its 0. |

## New / changed code
- Core
  - `RelicRules`: constants, `RollOffer(owned, rng)`, `ActionSpeedMultiplier(stacks)`.
  - `RewardRoller.RollCombatReward(..., extraSkipBonus, ownedRelics)` and `CombatReward` relic options/skip bonus.
  - `RunState.CompleteRoom` applies Gilded Quill.
  - `DeckCycle`: `CardBurned(card, BurnCause)` event, `ReturnRandomSpentToDrawQueue()`, `RestoreRandomBurnedToDrawQueue()`.
  - `ActiveReload.FreeOverburnsPerRoom`.
  - `AttackPayload.ZeroProof` + `ClashResolver` rule.
- Gameplay
  - New `RelicEffects` on the player, configured by `RunDirector` each room (relic set + per-room reset).
  - `CombatActionController.ClashWon(loser, byZero)`.
  - `CombatantStatus.AddPostureDamage`.
  - `PlayerCombatInput.ActionSpeedMultiplier` and `NextSleightZeroProof`.
  - `RunDirector.ChooseRelic`.

## Tests
- EditMode: offer rolling (2 distinct unowned, 1 left, none left -> currency, seeded), `TryPickRelic` (once,
  adds relic, bad index), Gilded Quill draft size and skip bonus, `CardBurned` causes, both return-to-queue methods,
  free Overburn (cost 0, then 10%, resets per room), zero-proof clash table, Ashen multiplier.
- PlayMode (`RelicTests`, loads `M3_Run`): Elite clear offers 2 relics and `ChooseRelic` adds one; each fight
  effect through the player (Ashen speed + heal, Coil restore, Hourglass return + posture, Iron Will vs an enemy 0,
  Lantern free Overburn).
