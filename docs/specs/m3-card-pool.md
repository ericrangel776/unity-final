# Spec: M3 card pool content (M3 step 2)

## Goal
The content the run needs, as assets: a draftable card pool with rarities, enemy decks per room type, and one
`RunContent` asset that holds all of it, so the room loop (step 3) has a single reference.

## Design
- **`RunContent`** (Core, read-only `ScriptableObject`, like `CardData`): StarterDeck, DraftPool, MobDeck,
  HexfireDeck, EliteDeck, BossDeck, EnemyZero. `Validate()` returns a list of problems (empty = valid), so both
  the generated asset and in-memory fixtures can be checked in EditMode.
- **Validation rules:**
  - StarterDeck has at least 6 cards (the purge minimum). Every list is free of nulls.
  - DraftPool has at least 15 distinct cards, every rarity, every shape, every element, and at least one 0-card.
  - Card ids are unique across all of `RunContent`. Draft ids may not reuse enemy cards.
  - MobDeck, HexfireDeck and EliteDeck have at least 4 cards each (above the Floor of 3) and no 0-cards. Enemy zeros
    come only from the Counterpuncher trial, which adds EnemyZero copies.
  - BossDeck has exactly 15 cards (the boss's visible deck from the design doc). The M3 placeholder uses it as a
    plain deck.
  - EnemyZero is a 0-card.
- **Builder:** new `DeckArpg/Build Run Content` menu in `StarterContentBuilder`. Card assets stay create-if-missing
  (hand-tuned values survive). The `RunContent` asset's lists are rewritten on every build, because it's a manifest.

## Draft pool (17 cards, original names)
| Rarity | Cards |
|---|---|
| Common (7) | Cleave 4, Cleave 6 (Strike/Physical) · Ember 4 (Throw/Fire) · Frost 5 (Arcana/Ice) · Spark 3, Spark 5 (Throw/Lightning) · Volt Burst 4 (Arcana/Lightning) |
| Uncommon (6) | Cleave 8 (Strike/Physical) · Ember 7 (Throw/Fire) · Frost 7 (Arcana/Ice) · Spark 7 (Throw/Lightning) · Cinder Nova 6 (Arcana/Fire) · Rime Lance 6 (Throw/Ice) |
| Rare (4) | Cleave 9 (Strike/Physical) · Thunderhead 9 (Arcana/Lightning) · Inferno 8 (Arcana/Fire) · **Null Ward** (0-card) |

## Enemy decks
- **Mob** (Vanguard, Green): the existing dummy deck, Slam 3/4/5/6/9.
- **Hexfire:** Hex Bolt 3 (Fire), Hex Spark 5 (Lightning), Hex Frost 4 (Ice), Hex Flare 6 (Fire), Hex Arc 7 (Lightning).
- **Elite:** Brute 5, 6, 7, 7, 8, 9.
- **Boss placeholder:** 15 Warden cards, values 3, 4, 4, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 9, 9.
- **EnemyZero:** Counter (0-card).

## Assumptions (mine, flag if wrong)
- Heal cards are left out of the pool, because `isHeal` has no gameplay effect yet.
- The starter deck is unchanged (10 cards, one Parry).

## Out of scope
Spawning enemies with these decks (step 3), card art.
