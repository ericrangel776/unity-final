# Spec: M3 run model (M3 step 1)

## Goal
All run-level rules as pure, seeded, EditMode-tested Core code: Map Cards and their Intensity/Quality,
the door offer generator, reward tiers and drafts, Shop/Forge/Rest operations, Premium cards, and a
`RunState` that ties them together. No scenes, MonoBehaviours or UI in this step.
Shared design: `m3-overview.md`.

## Decisions (from interview, 2026-11-14)
- **Premium = +2 value, burns on play.** A Premium card's effective value gets +2 (clamped to [1, 9]).
  When it's single-played or used in a Sleight slot, it goes to the burned pile instead of discard.
  Floor of 3 protects it exactly like a Sleight catalyst burn. Premium is per instance
  (`RuntimeCardInstance.IsPremium`, which also reads `CardData.IsPremium`), never written to the asset.
- **Still Waters option B = Meditate: +1 Momentum stack** (max 3). Replaces "restore burned cards",
  because burns already reset every room.
- Momentum stacks are not spent by Blue offers (confirmed).

## Rules
### Map Cards
- `MapFamily` = Red, Green, Blue, Purple. `MapCardKind` = Vanguard, Hexfire (Red), KindledGround,
  BountyWind (Green), Shop, Forge, Rest (Blue), EliteTrial (Purple), Boss.
- `MapCard` (immutable struct): Kind, Family, Number (Intensity for combat, Quality for Blue, always 1-9),
  `EliteTrial` (Veiled/Counterpuncher/Hexer, Purple only), `BuffElement` (KindledGround only).
- `IsCombat` = every kind except Shop/Forge/Rest.

### Intensity scaling (`RoomIntensity`, integer/percent math)
| Value | Rule |
|---|---|
| Base enemies | Vanguard 3, Hexfire 2, Green 2, Elite 1, Boss 1 |
| Extra enemies | `(I - 1) / 3`. Elite and Boss get none (Intensity scales their stats instead). |
| Enemy HP % | `80 + 10 * I`. BountyWind adds +15 / +25 / +35 by band. |
| Enemy tempo % | `90 + 5 * I`. KindledGround adds +10 / +20 / +30 by band. |
| Enemy card value bonus | `(I - 1) / 4`. Hexfire +1 more. Applied as `TemporaryRoomMod`, so it's clamped. |
| Band | 1-3 low, 4-6 mid, 7-9 high |
| KindledGround buff | +1 / +2 / +3 by band to your cards of `BuffElement` |
| Veiled reveal (s) | `0.40 - (I - 1) * 0.03125` (0.40 down to 0.15) |
| Counterpuncher zeros | 1 / 2 / 3 by band |
| Hexer HP per break | 2 at I >= 7, else 0 |

### Reward tiers (`RewardTier`)
Muted 1-3, Steady 4-6, Fierce 7-8, Frantic 9.

| | Muted | Steady | Fierce | Frantic |
|---|---|---|---|---|
| Currency % | 60 | 100 | 150 | 200 |
| Draft options | 2 | 3 | 3 | 3 |
| Picks | 1 | 1 | 1 | 2 |
| Rarity shift | 0 | 0 | +1 | +1 |
| Drafted card upgrade | 0 | 0 | +1 | +1 |
| Relic choice chance | 0 | 0 | 0 | 1 in 3 |

- **Base currency:** combat 15, Elite 30, Boss 50. `currency = base x tier% x BountyWind% (125/150/175 by band)`,
  rounded half up at each step, then `Momentum.ApplyCurrencyBonus`.
- **Rarity shift:** Red +1 and tier shift, total capped at 2. Weights (Common/Uncommon/Rare): shift 0 = 70/25/5,
  shift 1 = 40/45/15, shift 2 = 20/50/30.
- **Draft:** each option rolls a rarity, then a random pool card of that rarity that isn't already offered.
  If that rarity has none left, it falls back to the nearest rarity that does. The offer can be shorter than the
  option count when the pool is that small. `extraOptions` param is reserved for Gilded Quill (step 5).
- **Skip** pays +10 currency (not Momentum-scaled).
- Elite always offers a relic choice (resolved in step 5; here it's just a flag on the reward).

### Door offers (`MapOfferGenerator`, seeded `System.Random`)
- Rooms are numbered 1-6. Offers are dealt for rooms 1-5. Room 6 is always `Boss` at Intensity 7.
- **Families:** room 1 = Red, Green, and one more Red or Green (3 distinct kinds). Purple is excluded from room 1.
  Rooms 2-5 = 3 distinct families out of 4. In room 4, if no Purple has been offered yet, Purple is forced in.
- **Kind** is uniform within the family. Elite trial and KindledGround element are uniform too.
- **Numbers:** `center = 1 + (13 * room) / 10` (rooms 1-5 -> 2, 3, 4, 6, 7). Rolls: low in [c-2, c-1],
  mid = c, high in [c+1, c+3]. All clamped to [1, 9] and shuffled across the 3 cards.
  A Blue card's roll then gets `Momentum.ApplyQualityBonus`.

### Shop / Forge / Rest (by Quality band 1-3 / 4-6 / 7-9)
- **Shop:** stock 3 / 4 / 5 cards with rarity shift 0 / 1 / 1 (no duplicates, same draft roller).
  Prices: Common 30, Uncommon 50, Rare 80. Purge `40 + 20 x earlier purges`, halved (rounded up) at Q 7-9.
  A purge is refused when the deck would drop below 6. Buying or purging is refused if you can't afford it.
- **Forge:** upgrade amount +1 / +2 / +3, **or** make a card Premium. At Q 7-9 you get both: an upgrade on one
  card and Premium on a *different* card. Upgrade is refused on 0-cards and on cards whose base + mods is
  already >= 9. Premium is refused on 0-cards and on cards that are already Premium.
- **Rest:** heal 20 / 30 / 45% of max HP (rounded up, capped at max), **or** Meditate (+1 Momentum).

### `RunState`
- Owns: deck (`List<RuntimeCardInstance>`), Currency, Hp/MaxHp, `Momentum`, relics (`HashSet<RelicId>`,
  ids only, no effects yet), room number, the current `MapCard`, the chosen-card history, the "Purple offered"
  flag and the seeded RNG.
- `DealOffer()` -> 3 `MapCard`s (or the Boss card in room 6). `EnterRoom(card)`. `CompleteRoom()` updates
  Momentum for combat rooms and advances the room number. `IsRunComplete` after the boss.
  `AverageIntensity` over the combat rooms chosen.
- Rejects bad input with exceptions, like `OverburnRules`. Currency can never go negative.

## Assumptions (mine, flag if wrong)
- The starting deck (10 cards from `StarterContentBuilder`), 100 max HP and 0 currency are passed in by the caller.
- `RewardRoller` takes the card pool as a list of `CardData` and has no asset dependencies.
- `CardData` gets a serialized `rarity` field (default Common). Existing assets stay valid.

## Out of scope
Room spawning, UI, relic effects, card pool content (step 2).
