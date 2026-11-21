# Heal rework (HP economy)

Item 3 of the gameplay-loop fun pass (`docs/notes/CURRENT.md`). Decisions from the 2026-11-19 interview.

## Goal
Make HP a resource the player manages instead of something that only goes down. There are three changes:
- **Heal cards.** The unused `CardData.isHeal` flag becomes a real mechanic. A heal played from the hand is a risky
  0.5 s channel. A heal inside a Sleight heals as the Sleight fires.
- **Paid healing.** The Shop sells a heal.
- **Max-HP upgrades.** The player can raise max HP at Rest, in the Shop and with a new relic. Upgrades don't make the
  boss tougher.

## Out of scope
- Enemy or boss heal cards: decks stay as they are.
- Consumable items or an inventory (no potion slot).
- Elite rewards that raise max HP (not chosen).
- New art or animation. Feedback uses the existing greybox FX: an `FxShapes` ring, `DamageNumberView` and a `SfxSynth`
  tone.
- Rebalancing existing cards, rooms or the boss beyond the boss HP base change below.
- Floor progression.

## Rules (all constants in Core)
| Rule | Value |
|---|---|
| Heal per point of card value | `x3`, same as damage: Mend 3 heals 9 |
| Hand-heal channel | 0.5 s on scaled time, so hit-stop pauses it |
| Move speed while channelling | x0.5 |
| Channel interrupted by | a direct damaging hit, a Card Break, any flinch or stun, a dash, or death. Effect damage (burn ticks, a Wind slam's damage) does **not** interrupt on its own |
| Interrupted heal card | already in the discard, because `TryPlay` discards on play: no heal, card spent |
| Sleight heal | on the fire frame, `x3` of each heal card's effective value. The Sleight still attacks normally |
| Heal card in a Sleight | counts like any card: adds its value, can sit in any slot, burns in slot 1, follows Floor of 3, and Overburn restores it |
| Heal card element/shape | `Element.Physical`, `CardShape.Strike` |
| Shop heal | 40 currency: heal 25% of max HP (35% at Quality 7-9), rounded up. Once per visit |
| Shop Vital Draught | 60 currency: +10 max HP (also heals 10). Once per visit |
| Rest | pick **one** of three: Heal 20/30/45% (today), **Fortify** +8/+10/+12 max HP (also heals that much), or Meditate (today) |
| Relic Heartwood Seed | on pickup: +15 max HP and heal 15 |
| Boss HP | `BossRules.MaxHp(floor, run.StartingMaxHp)`: the run's **starting** max HP, so 150 stays 150 |

**Cards.** The starter deck gains `mend_3` (Mend 3, Common), so it now has 11 cards. The draft and shop pool gains
`mend_3` (Common), `mend_5` (Uncommon) and `mend_7` (Rare).

**Safeguard change.** CLAUDE.md says "heals fire on activation frame". This spec deliberately changes that: hand heals
channel and can be interrupted, and Sleight heals still fire on the fire frame. Step 9 updates CLAUDE.md and the design
doc to match.

## Design

### Core (`Assets/_Project/Scripts/Core`)
- **New `HealRules.cs`** (static):
  - Constants: `HealPerValue = 3`, `ChannelSeconds = 0.5f`, `ChannelMoveMultiplier = 0.5f`.
  - `public static int Amount(int value)`: returns `value * HealPerValue`, with `value <= 0` returning 0.
  - `public static int SleightHeal(IReadOnlyList<RuntimeCardInstance> stocked)`: the sum of `Amount(card.GetEffectiveValue())`
    over the heal cards in the stock.
- **`SleightResult`**: add a `public readonly int HealAmount` field, filled in `SleightRules.Compose`, and add a matching
  constructor parameter that defaults to 0, so existing callers compile.
- **`CardData`**:
  - Keep `isHeal`.
  - `Create(..., heal: true, zeroBreaker: true)` stores `isHeal = false`. A 0-card is never a heal.
  - In `OnValidate`, clear `isHeal` when `isZeroBreaker` is set.
- **`RunState`**:
  - `public int StartingMaxHp { get; }`, set in the constructor.
  - `MaxHp { get; private set; }`.
  - `public void IncreaseMaxHp(int amount)`: throws on `< 0`, raises `MaxHp`, then `Heal(amount)`.
  - `AddRelic(RelicId.HeartwoodSeed)` applies `IncreaseMaxHp(RelicRules.HeartwoodSeedMaxHp)` once, when it is newly added.
- **`RunState.RelicId`**: append `HeartwoodSeed` **last**, so the serialized ints don't shift.
- **`RelicRules`**: `public const int HeartwoodSeedMaxHp = 15;`. `RollOffer` already iterates every `RelicId`, so the
  new relic enters the offer pool automatically.
- **`BlueRooms.cs`**:
  - `ShopRules`:
    - `HealCost = 40`
    - `HealPercent(int quality)`: 25, or 35 at the High band
    - `HealAmount(int maxHp, int quality)`: uses `RewardRules.PercentCeil`
    - `VitalDraughtCost = 60`
    - `VitalDraughtMaxHp = 10`
  - `ShopVisit`:
    - `bool HealUsed`, `bool DraughtUsed`
    - `int HealAmount(RunState run)`
    - `bool TryHeal(RunState run)`: false if already used, at full HP, or unaffordable. Otherwise spend, heal, mark used.
    - `bool TryBuyDraught(RunState run)`: false if already used or unaffordable.
  - `RestRules`: `FortifyMaxHp(int quality)` returns `RoomIntensity.BandStep(quality, 8, 10, 12)`.
  - `RestVisit`:
    - `int FortifyAmount`
    - `bool TryFortify(RunState run)`: marks `IsDone`, like `TryHeal`.
- **`BossRules.MaxHp`**: the signature is unchanged. The caller passes `Run.StartingMaxHp`.

### Gameplay (`Assets/_Project/Scripts/Gameplay`)
- **`CombatantStatus`**:
  - `public int Heal(int amount)`:
    - Ignored when dead or when `amount <= 0`.
    - Clamps HP to max and returns the amount actually healed.
    - When it heals more than 0, raises `public event Action<int> Healed` and the static
      `public static event Action<CombatantStatus, int> AnyHealed`, for damage numbers and SFX.
  - `public void SetMaxHpKeepHp(int maxHp, int hp)`: run sync that does not refill HP.
- **New `HealChannel.cs`** (on the player, a `[RequireComponent]` of `PlayerCombatInput`, or added in `Awake` if missing):
  - Members:
    - `bool IsChannelling`
    - `float Progress01`
    - `int PendingAmount`
    - `bool Begin(RuntimeCardInstance card)`
    - `void Cancel()`
    - `event Action<int> Completed`
    - `event Action Interrupted`
  - While channelling:
    - It sets `PlayerMotor.MoveSpeedMultiplier`. Restore the saved previous value; don't hard-set 1, because the
      Perfect-reload boost also uses this multiplier.
    - On completion it calls `status.Heal(PendingAmount)`.
  - It cancels on:
    - `status.HitTaken`, unless the hit is an effect. Check `HitInfo.IsEffect` through `AnyHitLanded`, or pass the flag
      some other way; pick whichever the existing events allow cleanly.
    - `status.Broken`, `status.Flinched` and `status.Died`.
    - `PlayerMotor.Dashed`.
    - `OnDisable`.
- **`PlayerCombatInput.Execute`**:
  - For `PlaySlot1-3`, if the hand card `IsHeal`:
    - Reject (return false, card stays in hand) when `status.Hp >= status.MaxHp` or `healChannel.IsChannelling`.
    - Otherwise `TryPlay`, then `healChannel.Begin(card)`. No hitbox.
  - While `healChannel.IsChannelling`, every card, stock, Parry, Poke, Reload and Overburn command returns false. Dash
    still works and cancels the channel.
  - `HandleStock` on `Fired`: if `sleight.HealAmount > 0`, call `status.Heal(sleight.HealAmount)` before spawning the
    Sleight.
- **`DeadlockController`**: no change. A heal card chosen in a deadlock acts as its value and doesn't heal. The
  existing code already does this, because it only reads the value.
- **New `HealChannelFx.cs`**: a green ring at the player's feet, filled with `FxShapes.SetArc(…, Progress01)`, using a
  `FxShapes.CreateLine` line. It is hidden when not channelling.
- **`ElementColors`**: `public static readonly Color Heal` (green) and `CardFace(CardData card)`, which returns the heal
  green for heal cards and otherwise `CardFace(card.Element)`.
- **`RunDirector`**:
  - Boss HP: `BossRules.MaxHp(Run.Floor, Run.StartingMaxHp)` (line ~248).
  - After `LeaveUtility` and `ChooseRelic`, and at room start, call `playerStatus.SetMaxHpKeepHp(Run.MaxHp, Run.Hp)`.
    This replaces the bare `SetHp(Run.Hp)` in `LeaveUtility`.
- **`AudioService`/`SfxSynth`**: one short rising "heal" tone on `AnyHealed` for the player.

### UI (`Assets/_Project/Scripts/UI`)
- **Hand and draw queue**: `HandHudView` and `DrawQueueStripView` use `ElementColors.CardFace(card.Data)`, so heal
  cards show green. The hand value text shows `+3` for a heal card instead of `3`.
- **`RunUiText.CardDetails`**: heal cards read `"Heal · restores {Amount} HP (0.5 s channel)"`. Add relic
  name, description and short name entries for Heartwood Seed (`"Heartwood Seed"`, `"+15 max HP"`, `"Seed"`).
- **`DamageNumberView`**: subscribe to `CombatantStatus.AnyHealed` and show green `+N` numbers on the same pool.
- **`ShopView`**:
  - Two more buttons, Heal and Vital Draught, alongside Purge.
  - `UtilityView.ExtraButton` becomes `protected virtual IReadOnlyList<Button> ExtraButtons`, and `LayoutButtons`
    centres them. Labels state the cost, or why the button is disabled ("full HP", "need N more", "bought").
  - Update the `TopHint`.
- **`RestView`**: `TileCount = 3`: Heal, Fortify, Meditate. Fortify's badge is `+{n}` and its body reads `Max HP +n (and heal n)`.
- **`HandHudView` status line**: unchanged. It already shows `HP x/max`.

### Editor (`Assets/_Project/Scripts/Editor/StarterContentBuilder.cs`)
- `GetOrCreate(..., bool heal = false)` passes `heal` through to `CardData.Create`. `SyncAuthoredFields` must not clear
  `isHeal` on existing assets.
- `BuildPlayerStarterDeck` adds `mend_3`. `BuildDraftPool` adds `mend_3` (C), `mend_5` (U) and `mend_7` (R). Update the
  "10 cards" doc comment.

## Edge cases
| Case | Behavior |
|---|---|
| Heal card played at full HP | Rejected: the card stays in hand and nothing is spent |
| Heal card in a Sleight at full HP | The Sleight fires normally and heals 0. No damage number, because `Heal` returns 0 and raises no event |
| Damaged mid-channel | Channel cancelled, no heal, card stays in the discard, `Interrupted` fires and the ring hides |
| Burn tick mid-channel | Channel continues (effect damage) |
| Lightning jolt stun mid-channel | Cancelled (stun) |
| Dash mid-channel | Cancelled |
| Player dies mid-channel | Cancelled. `HandleDied` also calls `healChannel.Cancel()` |
| Room ends or door entered mid-channel | Cancelled by `ResetForNewRoom`/`OnDisable`. No heal |
| Hit-stop during channel | The channel pauses (scaled time) |
| Second heal pressed while channelling | Rejected |
| Shop heal at full HP / unaffordable / used | Button disabled with the reason. `TryHeal` returns false |
| Vital Draught or Fortify with HP below max | Max rises by n and HP rises by n, clamped |
| Heartwood Seed offered when all relics are owned | Unchanged: fallback currency |
| Heartwood Seed added twice | `AddRelic` returns false the second time, so there is no second +15 |
| Max HP raised | Overburn cost (10% of max) grows with it. This is intended and documented in the relic and shop text |
| Boss HP after upgrades | Uses `StartingMaxHp`, so it stays 150 on floor 1 |
| Zero-breaker authored with `isHeal` | `isHeal` is forced to false |
| Heal card in a deadlock | Uses its value, no heal |

## Steps
Each step ends green on the EditMode command, and on PlayMode too where noted.

1. **Core rules.**
   - Add `HealRules`, `SleightResult.HealAmount` (filled in `Compose`), the `CardData` zero/heal guard, `RunState`
     (`StartingMaxHp`, `IncreaseMaxHp`, Heartwood Seed), `RelicId.HeartwoodSeed` and `RelicRules.HeartwoodSeedMaxHp`.
   - EditMode tests: `HealRulesTests` (amounts, zero/negative), Sleight heal sum (0, 1 and 3 heal cards),
     `IncreaseMaxHp` (raises, heals, clamps, rejects negative), and Heartwood Seed applied once.
2. **Blue-room rules.**
   - Add the `ShopRules`/`ShopVisit` heal and draught, and `RestRules`/`RestVisit` Fortify.
   - EditMode tests: costs, percents by band, the once-per-visit limit, the full-HP rejection, the unaffordable case,
     and Rest's one-of-three.
3. **Boss HP base.**
   - `RunDirector` passes `Run.StartingMaxHp`.
   - Test: after `IncreaseMaxHp(15)`, the boss spawns with 150 HP on floor 1. Extend the existing boss-HP PlayMode
     test. PlayMode must be green.
4. **`CombatantStatus.Heal`/`SetMaxHpKeepHp` and run sync.**
   - Add both methods and wire the `RunDirector` sync points.
   - PlayMode tests:
     - Heal clamps, ignores the dead, and fires events only when it heals more than 0.
     - A Rest Fortify shows on the player's status after leaving.
     - Heartwood Seed raises the player's max HP.
5. **Heal channel.**
   - Add `HealChannel` and the `PlayerCombatInput` changes, for both hand heals and Sleight heals.
   - PlayMode tests, using a runtime-built player and the existing test helpers:
     - It completes after 0.5 s and heals 3x the value.
     - Direct hit, Card Break, stun, dash and death each cancel with no heal.
     - A burn tick doesn't cancel.
     - Other commands are rejected while channelling.
     - It is rejected at full HP.
     - A Sleight with a heal card heals on fire and still spawns its attack.
     - Move speed is halved and then restored to the previous multiplier.
6. **Content.**
   - Builder changes for the starter `mend_3` and the pool `mend_3/5/7`.
   - Rebuild the content and both scenes with the builder's `-executeMethod` entry points (as in the
     element-mechanics spec), then commit the regenerated assets.
   - Extend `RunContentTests`: the starter deck has 11 cards with exactly one heal, the pool has 3 heal cards, no
     zero-breaker is a heal, and the mend cards are Physical/Strike.
7. **UI.**
   - Heal card face and `+N` value, `CardDetails`, the relic text, the Shop buttons (the `ExtraButtons` refactor), the
     three Rest tiles, and green heal numbers.
   - Tests: EditMode for the `RunUiText` strings. PlayMode for the shop heal and draught buttons (enabled and disabled
     reasons), the Rest Fortify tile, and a heal damage number appearing.
8. **Feedback FX.**
   - `HealChannelFx` ring and the heal SFX.
   - PlayMode test: the ring is visible only while channelling, and its fill tracks `Progress01`.
   - Take a `HudScreenshotCapture` run mid-channel and look at `TestResults/screens/`.
9. **Docs.**
   - CLAUDE.md safeguard line: change "heals fire on activation frame" to "hand heals channel 0.5 s and can be
     interrupted; Sleight heals fire on the fire frame".
   - Add a short note in `docs/design/game-design-doc.md`, and update `docs/notes/CURRENT.md` item 3.

## Verification
- EditMode:
  `timeout 1200 "/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform EditMode -testResults "$(pwd -W)/TestResults/editmode.xml" -logFile "$(pwd -W)/TestResults/editmode.log"`
- PlayMode: the same command with `-testPlatform PlayMode` and `playmode.*` file names.
- Pass means exit code 0 and `result="Passed"` in each XML's `<test-run>` line, with no `error CS` in the logs. The
  baseline on `main` (`895fcfd`) is EditMode 860/860 and PlayMode 376 passed, 11 skipped.
- Screenshot: run `HudScreenshotCapture` alone without `-nographics`
  (`-testFilter DeckArpg.Tests.PlayMode.HudScreenshotCapture`) and check the heal ring and the green card face.
- Manual playtest in `M3_Run` (the user decides feel and numbers):
  1. Play Mend 3 at low HP. It heals 9 after the ring fills.
  2. Get hit mid-channel. There is no heal and the card is in the discard.
  3. Stock a heal card into a Sleight. It heals on fire.
  4. Buy the shop heal and the Vital Draught.
  5. Fortify at Rest.
  6. Pick Heartwood Seed if offered.
  7. Check that the boss still has 150 HP.
