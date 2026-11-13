# Real-Time Deckbuilder ARPG: Game Design & Technical Architecture Document

**Project Scope:** Unity Game Development Class Final (Vertical Slice)  
**Core Inspiration:** [*Kingdom Hearts: Chain of Memories*](https://www.khwiki.com/Kingdom_Hearts_Chain_of_Memories) (GBA, 2004)  
**Roguelike & Action Benchmarks:** [*Slay the Spire*](https://store.steampowered.com/app/646570/Slay_the_Spire/), [*One Step From Eden*](https://store.steampowered.com/app/960690/One_Step_From_Eden/), [*Hades*](https://store.steampowered.com/app/1145360/Hades/), and [*Wizard of Legend*](https://store.steampowered.com/app/445980/Wizard_of_Legend/)

## 1. Executive Summary: Evolving *Chain of Memories* into a Roguelike

[*Kingdom Hearts: Chain of Memories*](https://kingdomhearts.fandom.com/wiki/Kingdom_Hearts:_Chain_of_Memories), developed by Jupiter and Square Enix for the Game Boy Advance, merged real-time 2D isometric action combat with numerical card-clash mechanics and room-synthesis exploration ([Battle Card Wiki](https://www.khwiki.com/Battle_Card)). While its core systems were ahead of their time, the original campaign suffered from menu-sorting fatigue, repetitive hallway grinding, and late-game combo spam ([TrueGaming Analysis](https://www.reddit.com/r/truegaming/comments/1lzphf5/kingdom_hearts_chain_of_memories_zero_cards_kind/)).

Converting this foundation into a run-based roguelike solves the original game's pacing flaws while preserving its tactical tension:
* **Drafting Replaces Menu Micromanagement:** Instead of manually sorting 30 to 99 cards in pause menus using **Card Points (CP)** ([Fandom Wiki](https://kingdomhearts.fandom.com/wiki/Kingdom_Hearts:_Chain_of_Memories)), players start each run with a lean 10–12 card starter deck and draft 1 of 3 cards after clearing rooms ([Cardanoir *Slay the Spire* Analysis](https://cardanoir.com/slay-the-spire-deckbuilder-reinvented-card-games/)).
* **Passives Replace Active Buff Toggles:** Clunky black-bordered **Enemy Cards** ([Enemy Card Wiki](https://www.khwiki.com/Enemy_Card)) become permanent, stackable **Relics** dropped by Elites and Bosses ([SummerEngine Design Guide](https://www.summerengine.com/blog/make-a-deckbuilder-like-slay-the-spire)).
* **Intentional Deck Thinning Replaces Card Hoarding:** The penalty of permanently burning the first card stocked in a 3-card **Sleight** ([Sleight Wiki](https://www.khwiki.com/Sleight)) becomes an intentional mid-combat deck-thinning ("Exhaust") engine ([Cardanoir *Slay the Spire* Analysis](https://cardanoir.com/slay-the-spire-deckbuilder-reinvented-card-games/)).

## 2. Core Pillars & Reference Analysis

### 2.1 Reference Game Deconstruction

| Reference Title | Core Mechanic Borrowed | Why It Works | Application in Our Unity Project |
| :--- | :--- | :--- | :--- |
| [***Kingdom Hearts: Chain of Memories***](https://www.khwiki.com/Kingdom_Hearts_Chain_of_Memories) | `0–9` **Card Breaks**, 3-card **Sleights**, and **Map Card** room synthesis ([Fandom Wiki](https://kingdomhearts.fandom.com/wiki/Kingdom_Hearts:_Chain_of_Memories)) | Turns every attack and parry into a simultaneous mathematical duel and resource trade-off | Core clash resolution engine, `0`-card reactive parries, and room-modifier card drafting |
| [***Slay the Spire***](https://store.steampowered.com/app/646570/Slay_the_Spire/) | 1-of-3 card rewards, deck thinning/exhaust synergies, and passive **Relics** ([Cardanoir Analysis](https://cardanoir.com/slay-the-spire-deckbuilder-reinvented-card-games/)) | Keeps decks small and consistent while allowing passive rules to create emergent builds | Post-room reward loop, Forge/Purge rest nodes, and **Enemy Card Relics** |
| [***One Step From Eden***](https://store.steampowered.com/app/960690/One_Step_From_Eden/) | Real-time spell queueing and minimal-reading HUD iconography ([Presskit Overview](https://onestepfromeden.com/presskit/)) | Prevents cognitive overload by letting players fire and cycle cards without reading text mid-dodge | 3-slot visible hand mapped to dedicated buttons with color-coded icons and overhead card values |
| [***Hades***](https://store.steampowered.com/app/1145360/Hades/) | Responsive baseline movement/dash decoupled from cooldowns, modified by slot **Boons** ([TrueGaming Analysis](https://www.reddit.com/r/truegaming/comments/qj9gwg/the_combat_in_hades_is_perfect_and_i_think_more/)) | Ensures combat feels fluid even when resources are recharging | Always-available i-frame dash and weak `Value 1` poke so the player is never helpless while reloading |
| [***Wizard of Legend***](https://store.steampowered.com/app/445980/Wizard_of_Legend/) | Fast elemental spell-chaining (*Arcana*) and rushdown combo windows ([The Lock of Time Review](https://thelockoftime.world/blog/games-like-hades)) | Rewards aggressive elemental sequencing and punishes boss stagger windows | Tag-based 3-card **Sleight** synthesis (*Delivery + Element + Finisher*) |

### 2.2 Adapting *Chain of Memories* Mechanics for a Roguelike Loop

| *Chain of Memories* System | Original GBA Implementation | Flaw in Original Design | Modern Roguelike Adaptation |
| :--- | :--- | :--- | :--- |
| **Battle Deck & CP** | Manually curated 30–99 card list constrained by **CP** ([Fandom Wiki](https://kingdomhearts.fandom.com/wiki/Kingdom_Hearts:_Chain_of_Memories)) | Players pre-stacked fixed 3-card Sleight loops in menus and mashed buttons blindly ([TV Tropes GameBreaker](https://tvtropes.org/pmwiki/pmwiki.php/GameBreaker/KingdomHearts)) | Start with a 10–12 card starter deck; shuffle draw order on reload so players build synergies around card ratios rather than fixed deck order ([*Slay the Spire*](https://store.steampowered.com/app/646570/Slay_the_Spire/)) |
| **Card Break (`0–9`)** | Higher number breaks lower number; `0` breaks any card/Sleight if played second, but loses if played first ([Fandom Wiki](https://kingdomhearts.fandom.com/wiki/Kingdom_Hearts:_Chain_of_Memories)) | Late-game bosses spammed `0` cards, rendering slow single-card combos obsolete ([TrueGaming Analysis](https://www.reddit.com/r/truegaming/comments/1lzphf5/kingdom_hearts_chain_of_memories_zero_cards_kind/)) | Keep `0` as a skill-timed parry card; add **Exact Match Clashes** (clashing equal numbers triggers a brief bullet-time counter window) ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/GameBreaker/KingdomHearts)) |
| **Sleights (Stocking 3)** | Stocking 3 cards sums their values and triggers a named technique, burning the 1st card for the fight ([Sleight Wiki](https://www.khwiki.com/Sleight)) | Coding 50+ rigid recipe combinations led to players only spamming *Sonic Blade* or *Lethal Frame* ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories)) | Use **Tag-Based Modular Sleights** (Card 1 sets shape, Card 2 sets element/status, Card 3 sets finisher multiplier) and reward burning weak starter cards ([*Slay the Spire*](https://cardanoir.com/slay-the-spire-deckbuilder-reinvented-card-games/)) |
| **Reload Counter** | Holding reload charges a meter that escalates from `1` to `2` to `3` seconds per shuffle ([Fandom Wiki](https://kingdomhearts.fandom.com/wiki/Kingdom_Hearts:_Chain_of_Memories)) | Standing still for 3 seconds breaks action pacing and forces players to hoard Item cards ([Note.com Guide](https://note.com/mouru555/n/n14f34ef338e7?hl=en)) | Implement an **Active Reload Timing Bar** (hitting the sweet spot reloads instantly) plus partial 1-card reloads on successful `0`-card parries ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/GameBreaker/KingdomHearts)) |
| **Enemy Cards** | Separate deck of black-bordered boss/mob cards activated manually for temporary buffs ([Enemy Card Wiki](https://www.khwiki.com/Enemy_Card)) | Clunky secondary menu activation mid-combat interrupted action flow ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories)) | Convert into permanent **Passive Relics** dropped by Elites and Bosses that alter run rules automatically ([SummerEngine Design Guide](https://www.summerengine.com/blog/make-a-deckbuilder-like-slay-the-spire)) |
| **Map Cards** | Red, Green, and Blue cards dropped by foes and required to open numbered doors ([Map Card Wiki](https://www.khwiki.com/Map_Card)) | Mandatory door requirements forced tedious hallway grinding when RNG failed ([Metacritic Reviews](https://www.metacritic.com/game/kingdom-hearts-rechain-of-memories/user-reviews/)) | Doors never softlock; players draft from a 3-card hand of **Room Modifiers** between encounters to customize risk and reward ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories)) |

## 3. Core Gameplay Loop (Micro & Macro Structure)

### 3.1 Micro Combat Loop (Second-to-Second Arena Play)

1. **Read & Position:** The player moves freely and uses an off-card i-frame Dash ([*Hades*](https://store.steampowered.com/app/1145360/Hades/)). Enemies telegraph attacks with an overhead card icon displaying their card value (`1–9` or `Sleight 15+`) ([Note.com Guide](https://note.com/mouru555/n/n14f34ef338e7?hl=en)).
2. **Play or Stock from 3-Card Hand:** Rather than scrolling through 30 cards one by one ([Reddit GBA Tips](https://www.reddit.com/r/KingdomHearts/comments/1scs7x6/give_me_your_best_tips_and_tricks_with_chain_of/)), the HUD displays a 3-card active hand plus a reserved `0`-Parry slot:
   * **Single Play:** Fires the selected card immediately and draws the next card from the deck queue ([*One Step From Eden*](https://store.steampowered.com/app/960690/One_Step_From_Eden/)).
   * **Stock for Sleight:** Moves the card into the 3-slot Sleight buffer. Once 3 cards are stocked, the combined Sleight fires automatically with a total value equal to `Card1 + Card2 + Card3`, and `Card1` is moved to the `BurnedPile` for the remainder of the room ([Fandom Wiki](https://kingdomhearts.fandom.com/wiki/Kingdom_Hearts:_Chain_of_Memories)).
3. **Resolve Clashes:**
   * **Higher Value Wins:** Playing a `7` into an enemy's `5` triggers a **Card Break**, interrupting the enemy's animation, dealing posture damage, and stunning them ([Fandom Wiki](https://kingdomhearts.fandom.com/wiki/Kingdom_Hearts:_Chain_of_Memories)).
   * **Zero-Card Parry:** Playing a `0` *after* an enemy activates any card or Sleight shatters their attack and permanently deletes the broken card from the enemy's current deck cycle ([TV Tropes GameBreaker](https://tvtropes.org/pmwiki/pmwiki.php/GameBreaker/KingdomHearts)).
   * **Equal Value Neutral Clash:** Playing an identical value (`6` vs. `6`) cancels both attacks with zero damage and triggers a brief clash spark ([Reddit CoM Guide](https://www.reddit.com/r/KingdomHearts/comments/6s8qhr/khcom_confused_with_rechain_of_memories_card/)).
4. **Active Reload:** When the draw queue empties, pressing Reload triggers a 1.2-second timing bar over the player's head. Hitting the center zone completes the shuffle immediately and grants a brief speed boost; missing requires waiting the full recharge duration ([Fandom Wiki](https://kingdomhearts.fandom.com/wiki/Kingdom_Hearts:_Chain_of_Memories)).

### 3.2 Macro Floor Progression (Room-to-Room Loop)

1. **Map Card Room Synthesis:** At each exit door, the player chooses 1 of 3 **Map Cards** in their hand to generate the next room ([Map Card Wiki](https://www.khwiki.com/Map_Card)):
   * **Red Map Cards (Martial / Sorcerous / Encounter):** Modify enemy archetypes and elite spawn rates in exchange for higher rarity card rewards ([Map Card Wiki](https://www.khwiki.com/Map_Card)).
   * **Green Map Cards (Calm Bounty / Alchemy / Martial Waking):** Grant ambient room modifiers (e.g., *+2 to all Fire cards, but enemies move 20% faster*) or guaranteed upgrade chests ([Reddit GBA Tips](https://www.reddit.com/r/KingdomHearts/comments/1scs7x6/give_me_your_best_tips_and_tricks_with_chain_of/)).
   * **Blue Map Cards (Moogle Shop / Tranquil Darkness / Rest Forge):** Non-combat utility rooms where players spend currency to buy packs, upgrade a card's number (`+2 Value`), convert a card to **Premium** (costs 0 CP, auto-burns on use), or purge a weak starter card ([Fandom Wiki](https://kingdomhearts.fandom.com/wiki/Kingdom_Hearts:_Chain_of_Memories); [Cardanoir Analysis](https://cardanoir.com/slay-the-spire-deckbuilder-reinvented-card-games/)).
2. **Post-Combat Spoils:** Clearing a combat room awards currency, a 1-of-3 **Battle Card** draft (with a "Skip for Bonus Currency" option so decks stay slim), and a replacement **Map Card** ([Cardanoir Analysis](https://cardanoir.com/slay-the-spire-deckbuilder-reinvented-card-games/)).

## 4. Gameplay Edge Cases & Design Safeguards

The original [*Chain of Memories*](https://www.khwiki.com/Kingdom_Hearts_Chain_of_Memories) and its PS2 remake exposed five critical design edge cases that must be solved upfront in our combat rules:

| Edge Case | How It Breaks Gameplay in *Chain of Memories* | Mandatory Design Safeguard for Our Game |
| :--- | :--- | :--- |
| **1. The Zero-Card Deck Depletion Softlock** | Burning the 1st card of every Sleight (plus Item and Premium cards) can reduce a small deck to 0–2 cards before a boss dies, leaving the player unable to attack and forced to flee against the arena wall ([Note.com Guide](https://note.com/mouru555/n/n14f34ef338e7?hl=en); [TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories)) | **Minimum Deck Floor of 3 + Base Poke:** Mirroring *Re:CoM*'s last-card protection ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories)), if `ReloadableDeck.Count <= 3`, Sleights are locked (or no longer burn the 1st card). Additionally, provide a permanent off-deck `Value 1` weak slash and an **Overburn Reload** (spend 10% HP to restore burned cards) |
| **2. The `1 - 1 = 0` Modifier Math Bug** | The Shadow Enemy Card (`+1` to all values) accidentally turns precious `0` parry cards into useless `1`s ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories)). Conversely, reducing an enemy's `1` card by `-1` would turn it into an unblockable `0` parry card unless clamped ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories)) | **Decouple `0` from Integer Arithmetic:** Store `isZeroBreaker` as a boolean flag (or dedicated `CardRank.Zero` enum) rather than the integer `0`. Clamp all numerical buffs and debuffs strictly within `Mathf.Clamp(val + mod, 1, 9)` |
| **3. Multi-Enemy Targeting vs. Global Card Breaks** | *Chain of Memories* secretly limits mob fights so only 1 Heartless attacks at a time, and Card Breaks happen globally without physical contact ([Note.com Guide](https://note.com/mouru555/n/n14f34ef338e7?hl=en); [Sleights Wiki](https://kingdomhearts.fandom.com/wiki/Sleights)). In a fast ARPG where 4 foes attack at once, a global card slot breaks down | **Two-Tier Clash System:** Use **Local Hitbox Clashes** for standard mobs (overlapping attack colliders compare their carried card values locally) and reserve the **Global HUD Clash** for telegraphed Boss/Elite Sleights |
| **4. Multi-Hit Sleight & Heal Interrupts** | Long Sleights (*Sonic Blade*, *Ars Arcanum*) and *Cure* spells can be broken on startup by a boss's `0` card, deleting the burned card and negating the entire heal or combo ([TV Tropes GameBreaker](https://tvtropes.org/pmwiki/pmwiki.php/GameBreaker/KingdomHearts); [Reddit Final Boss Thread](https://www.reddit.com/r/KingdomHearts/comments/1u6znij/i_cant_beat_the_final_boss_in_chain_of_memories/)) | **Instant Cast Heals & Per-Hit Sleight Resolution:** Trigger healing/item effects on the activation frame (or grant unbreakable *Bracer* frames via Relics ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories))), and resolve multi-hit Sleights per hit so a late Card Break only cancels remaining hits |
| **5. Asymmetric Boss Cheating** | Original bosses have arbitrary Sleight rules and dash-teleport invulnerably while reloading so players cannot punish their reload window ([TV Tropes My Rules Are Not Your Rules](https://tvtropes.org/pmwiki/pmwiki.php/Main/MyRulesAreNotYourRules)) | **Honest Boss Deck Counter (Mill Win Condition):** Give bosses a visible 15-card counter. Since breaking a boss card with a `0` deletes it until they reload ([TV Tropes GameBreaker](https://tvtropes.org/pmwiki/pmwiki.php/GameBreaker/KingdomHearts)), parrying their deck down to 0 forces a punishable 4-second exhaustion stagger |

## 5. Unity Technical Architecture & Engineering Pitfalls

### 5.1 Core C# Data & State Architecture

To prevent asset corruption and state desynchronization in Unity, separate static card definitions from runtime combat instances:

1. **`CardData` (`ScriptableObject` — Read-Only Template):**
   * Fields: `string cardId`, `string displayName`, `bool isZeroBreaker`, `int baseValue (1-9)`, `ElementTag element` (`Physical`, `Fire`, `Ice`, `Lightning`, `Heal`), `SleightShape deliveryShape` (`Slash`, `Lunge`, `Projectile`, `Nova`), `bool isPremium`.
   * **Critical Rule:** Never mutate `CardData` fields during playmode, or changes will persist on disk in the Unity Editor.
2. **`RuntimeCardInstance` (Pure C# Class — Per-Run Wrapper):**
   * Wraps a reference to `CardData` and tracks `int permanentRunUpgradeMod`, `int temporaryRoomMod`, `bool isPremiumInstance`, `bool isBurnedThisFight`, and `float activationTimestamp`.
   * Exposes `GetEffectiveValue()` which returns `0` if `CardData.isZeroBreaker == true`, or `Mathf.Clamp(CardData.baseValue + permanentRunUpgradeMod + temporaryRoomMod, 1, 9)`.
3. **`DeckRuntimeManager` (`MonoBehaviour`):**
   * Maintains four lists: `List<RuntimeCardInstance> MasterRunDeck`, `Queue<RuntimeCardInstance> DrawQueue`, `RuntimeCardInstance[] ActiveHand (Size 3)`, `List<RuntimeCardInstance> DiscardPile`, and `List<RuntimeCardInstance> BurnedPile`.
   * Enforces the **Floor of 3 Rule** before moving the first card of a stocked Sleight to `BurnedPile`.
4. **`AttackPayload` & `CombatClashResolver` (`MonoBehaviour`):**
   * Every spawned melee hitbox or projectile prefab receives an `AttackPayload` struct containing `OwnerEntity`, `EffectiveValue`, `IsZeroBreaker`, `IsSleight`, and `ActivationTimestamp` (`Time.time`).
   * When an attack collider intersects an opposing attack collider (or when a Boss Global Sleight is challenged), `CombatClashResolver` compares the two `AttackPayload` structs deterministically.

### 5.2 Unity Engineering Edge Cases & Fixes

| Technical Pitfall | Root Cause in Unity | Implementation Fix |
| :--- | :--- | :--- |
| **Orphaned Hitboxes & Stuck VFX on Card Break** | Forcing `animator.CrossFade("Stagger", 0.05f)` mid-attack skips the Animation Event on frame 20 that was supposed to disable the weapon's `BoxCollider` or stop a particle emitter | Manage all attacks through a `CombatActionController` state machine. Register every active hitbox and spawned VFX in a list, and invoke `CancelActiveAction()` inside the Card Break callback to disable all colliders immediately |
| **Simultaneous-Frame `0`-Card Clashes** | A `0` card wins if played *second* and loses if played *first* ([Reddit CoM Guide](https://www.reddit.com/r/KingdomHearts/comments/6s8qhr/khcom_confused_with_rechain_of_memories_card/)). If both player and boss activate on the same `Update()` tick, component execution order decides the winner arbitrarily | Record an integer `activationTick` (or `Time.time`) on every `AttackPayload`. A `0` card breaks the opposing card only if `zeroCard.activationTick > targetCard.activationTick`; if equal, resolve as a neutral bounce |
| **Projectile vs. Melee Value Overwrite** | If the player casts a slow `Fire (Value 4)` projectile and then swings a `Keyblade (Value 8)` before the fireball hits, reading a single `Player.CurrentCardValue` variable gives the fireball the sword's value | Bind the `AttackPayload` snapshot to the projectile instance at spawn time so each hitbox carries its own immutable card value independent of the player's next card |
| **Dropped Inputs During Card-Break Hit-Stop** | Setting `Time.timeScale = 0f` for a 0.15-second dramatic "CARD BREAK!" freeze causes `WaitForSeconds` coroutines to stall and drops button presses if input isn't buffered | Run hit-stop freezes using `WaitForSecondsRealtime(0.15f)` and feed controller inputs into a 0.2-second ring buffer (`Queue<BufferedInput>`) that processes on the first frame `Time.timeScale` returns to `1f` |

## 6. Modular Content Design for the Vertical Slice

Instead of animating 50 custom Sleights like the original GBA game ([Sleight Wiki](https://www.khwiki.com/Sleight)), use a **3-Slot Combinatorial Rule System** so a small pool of assets creates deep build variety:

### 6.1 Tag-Based Sleight Synthesis Rules

* **Slot 1 (The Burned Catalyst — Defines Attack Shape):**
  * *Strike Card:* Dash-lunge melee combo (inspired by *Sonic Blade* ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories))).
  * *Thrust/Throw Card:* Piercing boomerang projectile (inspired by *Strike Raid* ([Sleights Wiki](https://kingdomhearts.fandom.com/wiki/Sleights))).
  * *Arcana/Magic Card:* Expanding radial AoE blast around the player (inspired by *Mega Flare* ([Reddit GBA Tips](https://www.reddit.com/r/KingdomHearts/comments/1scs7x6/give_me_your_best_tips_and_tricks_with_chain_of/))).
* **Slot 2 (The Modifier — Defines Element & Status):**
  * *Fire:* Leaves burning ground tiles and applies DoT.
  * *Ice:* Applies Chill/Freeze slow and spawns crystal shrapnel ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories)).
  * *Lightning:* Chains a secondary arc to up to 3 nearby enemies.
  * *Physical:* Adds +50% posture/poise damage on hit.
* **Slot 3 (The Finisher — Defines Scaling):**
  * Scales final damage multiplier by `1.0f + (Slot3.EffectiveValue * 0.1f)` and triggers a finisher shockwave if all 3 cards share the same element tag.

### 6.2 Three Playable Roguelike Build Archetypes

1. **The "Zero-Breaker" Deck-Mill Archetype:**
   * *Core Loop:* Stockpile `0` cards and defensive relics; intentionally break the boss's highest Sleights to delete their cards and force repeated 4-second Exhaustion Staggers ([TV Tropes GameBreaker](https://tvtropes.org/pmwiki/pmwiki.php/GameBreaker/KingdomHearts)).
   * *Key Relic (Inspired by Pirate/Hook Enemy Card):* **Shattered Hourglass** — Breaking an enemy card with a `0` automatically reloads 1 spent card into your Draw Queue and deals 15 posture damage ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories)).
2. **The "First-Slot Burn" Thin-Deck Engine:**
   * *Core Loop:* Convert weak `1–3` starter cards into **Premium Cards** ([Fandom Wiki](https://kingdomhearts.fandom.com/wiki/Kingdom_Hearts:_Chain_of_Memories)) and feed them into Slot 1 of Sleights early in the fight until the deck is thinned down to an unstoppable 4-card loop of `8`s and `9`s ([Cardanoir Analysis](https://cardanoir.com/slay-the-spire-deckbuilder-reinvented-card-games/)).
   * *Key Relic (Inspired by Riku/Creeper Plant Enemy Cards):* **Ashen Sigil** — Whenever a card is burned via a Sleight, gain +10% attack speed for the rest of the room and heal 3 HP ([Reddit Final Boss Thread](https://www.reddit.com/r/KingdomHearts/comments/1u6znij/i_cant_beat_the_final_boss_in_chain_of_memories/)).
3. **The "Bracer" Elemental Sleight Brawler:**
   * *Core Loop:* Draft multi-element magic cards and high-CP attacks to fire non-stop 20+ value Sleights without worrying about enemy counter-breaks ([Note.com Guide](https://note.com/mouru555/n/n14f34ef338e7?hl=en)).
   * *Key Relic (Inspired by Jafar's *Attack Bracer*):* **Iron Will Idol** — Your first Sleight after every Active Reload cannot be broken by enemy `0` cards ([TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KingdomHeartsChainOfMemories)).

## 7. Recommended Vertical Slice Milestone Plan

| Milestone Phase | Core Deliverables in Unity | Verification & Playtest Goal |
| :--- | :--- | :--- |
| **Phase 1: Core Clash Prototype** | Player movement/dash, `CardData` & `RuntimeCardInstance`, 3-card hand HUD, and `CombatClashResolver` (`0–9` breaks + interrupt cleanup) | Verify that `0` cards break `9`s only when played second, `1 - 1` modifiers clamp to `1`, and canceled animations never leave stuck hitboxes |
| **Phase 2: Sleights & Active Reload** | 3-slot Sleight buffer, Slot-1 card burning with Floor-of-3 protection, and overhead Active Reload timing bar | Confirm players can intentionally thin their deck mid-fight without ever softlocking at 0 cards |
| **Phase 3: Map Card Loop & Relics** | 6-room branching floor generator, 4 **Map Card** types (*Combat*, *Elite*, *Forge*, *Shop*), 1-of-3 card reward UI, and 6 **Enemy Card Relics** | Playtest a full 10-minute run from starter deck through room drafting to the floor boss |
| **Phase 4: Boss AI & Juice Polish** | 1 Boss with a visible 15-card deck counter, telegraphed Boss Sleights, hit-stop freeze frames, and overhead card break VFX/SFX | Ensure the boss can be defeated either via standard HP depletion or by milling its 15-card deck with `0`-card parries |
