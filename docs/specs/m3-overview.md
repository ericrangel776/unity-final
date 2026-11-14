# Spec: M3 overview - Map Card loop & relics

## Goal
A full ~10-minute run: starter deck -> 5 rooms chosen from Map Cards -> floor boss (placeholder until M4).
Clearing rooms pays currency, a 1-of-3 card draft and (elites) a relic. Death or a boss kill ends the run
with a restart. Each M3 step gets its own short spec like M2 did. This file holds the shared decisions.

## Decisions (from interview, 2026-11-14)
- **Map Card families = Red / Green / Blue (design doc §3.2) + a new Purple Elite.**
- **Floor = a hand of Map Cards at each door** (no pre-built node map). You pick 1 of 3 offered cards
  for rooms 1-5. Room 6 is always the boss.
- **Enemies = tuned TrainingDummy variants.** Real mob AI and the real boss are M4.
- **Relics: the 3 named in the design doc + 3 proposed below** (need your approval).

## Proposals - APPROVE / EDIT before step 1
### Map Cards (names are original)
| Family | Card | Room | Effect |
|---|---|---|---|
| Red | **Vanguard** | Combat | 3 dummies with Strike-heavy decks. Draft rolls one rarity tier higher. |
| Red | **Hexfire** | Combat | 2 dummies whose cards get +1 value and an element. Draft rolls one tier higher. |
| Green | **Kindled Ground** | Combat | Your cards of a random element get +2 this room (`TemporaryRoomMod`). Enemies move 20% faster. |
| Green | **Bounty Wind** | Combat | Rewards pay 1.5x currency. Enemies have +25% HP. |
| Blue | **Wayfarer's Stall** | Shop | Buy cards, or pay to purge a card (min deck size 6). Stock scales with Quality. |
| Blue | **Ember Forge** | Forge | Upgrade a card (`PermanentRunUpgradeMod`) **or** make it Premium. Amount scales with Quality. |
| Blue | **Still Waters** | Rest | Heal **or** restore all burned cards plus the next room's first Overburn free. Heal scales with Quality. |
| Purple | **Elite Trial** | Elite | 1 large dummy (2.5x HP, 1.5x posture) + **one random trial** (below). Drops a relic choice (1 of 2). |

### Intensity number (added 2026-11-14)
Every Red, Green and Purple Map Card shows an **Intensity 1-9** in its corner, like a battle card value.
Low = muted room, high = frantic room. The value is clamped to `[1, 9]` with `CardValue.Clamp`.
Everything scales off one value `I`:

| What scales | Formula (tuning) | I=1 -> I=5 -> I=9 |
|---|---|---|
| Enemy count | card base + `(I - 1) / 3` | +0 -> +1 -> +2 |
| Enemy HP | x `0.8 + 0.1 * I` | 0.9x -> 1.3x -> 1.7x |
| Enemy tempo (move speed, shorter telegraph/recover) | x `0.9 + 0.05 * I` | 0.95x -> 1.15x -> 1.35x |
| Enemy card values | + `(I - 1) / 4`, clamped to [1, 9] | +0 -> +1 -> +2 |
| Green buff / drawback size | buff +1 / +2 / +3 at I 1-3 / 4-6 / 7-9; drawback scales with the tempo row | |
| Purple trial strength | Veiled reveal 0.40 -> 0.15 s; Counterpuncher 1 / 2 / 3 zeros; Hexer at I >= 7 also costs 2 HP per break | |

- **Offer rolls:** a band centered on room depth (`center = 1 + 1.3 * room`, so room 1 is about 2 and room 5
  is about 7, spread ±2). The 3 offered cards take one low, one mid and one high roll, so every door has a
  calm choice and a frantic one.
- **Boss (room 6) is fixed at Intensity 7.**
- **Run summary** shows the average Intensity of the rooms you chose, as a "how hard did you play it" stat.

### Why go hard: reward tiers + Momentum (added 2026-11-14)
Rewards climb faster than the danger, so playing it safe works but falls behind. Each combat Map Card shows
its tier name and reward pips, so the trade is visible at the door.

| Tier | Intensity | Currency | Draft | Extra |
|---|---|---|---|---|
| **Muted** | 1-3 | 0.6x | 1 of **2** | - |
| **Steady** | 4-6 | 1.0x | 1 of 3 | - |
| **Fierce** | 7-8 | 1.5x | 1 of 3, one rarity tier higher | the drafted card comes **pre-upgraded +1** |
| **Frantic** | 9 | 2.0x | 1 of 3, one tier higher, **take 2** | 1-in-3 chance of a relic choice |

Red's "+1 rarity tier" stacks on top (capped at Rare-heavy weights). Purple uses the same tiers, plus its relic.

**Momentum (run-wide streak, 0-3 stacks, shown on the HUD strip):**
- Clearing a room at Intensity >= 6 adds 1 stack. Clearing a room at Intensity <= 3 resets it to 0.
  Rooms at 4-5 and Blue rooms leave it unchanged.
- Each stack gives +10% currency, and **+1 Quality on the next Blue card offered** per stack.
- So a hard streak makes the next shop, forge or rest noticeably better, and an easy room costs you the streak.

### Quality number on Blue cards (added 2026-11-14)
Blue cards show a **Quality 1-9** (different icon/colour from Intensity so the two aren't confused). It rolls in
the same depth band as Intensity, **plus the current Momentum stacks**, clamped to [1, 9].

| Card | Q 1-3 | Q 4-6 | Q 7-9 |
|---|---|---|---|
| **Wayfarer's Stall** | 3 cards, base rarity | 4 cards, one tier higher | 5 cards, one tier higher, purge half price |
| **Ember Forge** | +1 to one card, or Premium | +2 to one card, or Premium | +3 to one card **and** one other card Premium |
| **Still Waters** | heal 20% | heal 30% | heal 45% (the burned-cards option is the same at every Quality) |

Prices stay flat (the shop doesn't charge more at high Quality). Good shops are the payoff for a hard streak.

**Purple trials** (each tests something different):
1. **Veiled** - its telegraphs show `?` until 0.25 s before the swing. Tests reading and reaction instead of planning.
2. **Counterpuncher** - its deck holds two 0-cards. When you play a card inside its range it answers with a 0
   (always second, so it breaks you). Forces baiting, Sleights or waiting it out instead of spamming.
3. **Hexer** - every Card Break you suffer burns that card for the room. Floor of 3 still applies.
   Pressures deck thinning and Overburn use.

### Offer rules (seeded)
- Each door offers 3 cards with no duplicate family, and at least one is Red or Green (combat).
- Room 1 offers only Red/Green. Purple can show up from room 2. If no Purple has been offered by room 4,
  room 4's offer includes one, so a relic is always reachable.
- At most one Blue per offer.

### Rewards & economy (tuning, will change in playtest)
- Combat clear: 15 currency (Purple: 30). 1-of-3 draft from the pool. Skip pays +10 currency.
- Card rarity Common/Uncommon/Rare, base weights 70/25/5 (one tier higher: 40/45/15).
- Shop: Common 30 / Uncommon 50 / Rare 80, purge 40 (+20 per earlier purge).
- HP carries between rooms. Death -> run summary -> new run.
- The design doc's "replacement Map Card" reward is dropped: a new offer is dealt at every door anyway.

### Relics (the 3 named in the design doc + 3 new, all original names)
1. **Shattered Hourglass** (doc) - a 0-break returns 1 spent card to the Draw Queue and deals 15 posture damage.
2. **Ashen Sigil** (doc) - each Sleight burn gives +10% attack speed for the room (stacks) and heals 3 HP.
3. **Iron Will Idol** (doc) - your first Sleight after each Active Reload can't be broken by an enemy 0.
4. **Tempered Coil** *(new)* - a Perfect Active Reload also restores 1 burned card. Rewards the reload skill.
5. **Cinder Lantern** *(new)* - the first Overburn Reload each room costs no HP. Supports the thin-deck build.
6. **Gilded Quill** *(new)* - card drafts offer 4 cards, and skipping pays +10 more currency. Economy / deck control.

## Architecture
- **Core (pure C#, EditMode-tested):** `RunState` (deck as `RuntimeCardInstance`s, currency, relics, room index,
  HP), `MapCardDef` + `MapOfferGenerator`, `RewardRoller` (rarity weights, draft), `ShopRules`, `ForgeRules`,
  `RelicId` + rule helpers. `CardData` gains a `rarity` field (still read-only). Premium conversion is
  stored on the instance (`RuntimeCardInstance.IsPremiumOverride`), never on the asset.
- **Gameplay:** `RunDirector` in a new `M3_Run` scene. One arena is reused: on room enter it clears enemies,
  applies the room modifier, spawns the enemies, and moves the player to spawn. **No scene reloads between
  rooms** (avoids lost state and the scene-unload test issues). The dummy gets a no-respawn mode, a tuning
  profile and trial hooks. Relic effects subscribe to existing events (break, burn, reload graded, Overburn).
- **UI:** Map Card choice at the exit door, reward/draft screen, Shop/Forge/Rest panels, currency + relic HUD
  strip, run-end screen. All built through `UiFactory`, like the M2 HUD.

## Steps (each = spec -> implement -> tests -> commit)
1. Core run model: `RunState`, Map Card defs + Intensity/Quality scaling (`RoomIntensity`), reward tiers,
   Momentum, offer generator, reward roller, shop/forge/rest rules, `CardData.rarity`.
2. Card pool content: ~15 draftable cards with rarities (builder + assets), elite/boss decks.
3. Room loop gameplay: `RunDirector`, `M3_Run` scene, enemy spawning/clear detection, Green modifiers, dummy tuning.
4. Purple trials (Veiled, Counterpuncher, Hexer).
5. Relics: `RelicData` + 6 effects.
6. UI: door choice, draft, shop/forge/rest, HUD strip, run end. Screenshot review with `HudScreenshotCapture`.
7. Full-run PlayMode smoke test + manual 10-minute playtest pass.

## Out of scope (M4)
Real boss AI and its 15-card deck, new enemy types, element status effects, juice polish for the new screens.
