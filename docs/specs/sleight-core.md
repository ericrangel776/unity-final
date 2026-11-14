# Spec: Sleight core rules (M2 step 1)

## Goal
Add the pure-C# rules for stocking cards into a 3-slot Sleight buffer and firing the combined
Sleight: value = sum, slot 1 = shape, slot 2 = element, slot 3 = damage multiplier
`1 + v*0.1`, and the slot-1 card is burned with Floor-of-3 protection. Stocking has risk: a Card
Break cracks a partial stock, and an idle stock expires. Everything lives in `DeckArpg.Core`, covered by
EditMode tests, so the Gameplay/UI steps can build on it.

## Decisions (from interview, 2026-11-13)
- **Stocking a 0 = Mirror.** A stocked zero-breaker copies the highest-value *numbered* card in
  the same Sleight (its value, shape and element) when the Sleight is composed. This is flag
  logic, not integer math. If there's nothing numbered to copy (all three are 0s), each 0 counts
  as `CardValue.Min` (1) and keeps its own shape/element.
- **Interrupt penalty = Crack catalyst.** Losing a clash (any Card Break against you, including
  being zero-broken) while 1-2 cards are stocked: the slot-1 card is burned and the rest go to
  discard. Floor of 3 applies: if it would burn at `CyclingCardCount <= 3`, nothing burns and all
  of them go to discard.
- **Interrupt trigger = Card Break only.** Body hits don't touch the stock. (Gameplay wiring
  calls `InterruptStock()` from the Card Break callback in step 4; Core only exposes it.)
- **Timeout = yes.** `SleightRules.StockTimeoutSeconds = 5f`, measured from the *most recently
  stocked* card (assumption: each new stock resets the timer). When it expires, all stocked
  cards go to discard with **no burn**.

## Out of scope
- Input actions, `PlayerCombatInput`/`CombatActionController` wiring, spawning Sleight hitboxes,
  multi-hit resolution (step 4).
- HUD for the stock slots / timer (step 5).
- Active Reload timing bar (step 2), Overburn Reload (step 3).
- Element status effects (burn tiles, chill, chain arcs, +50% posture) and the same-element
  finisher shockwave *effect*. Core only reports `AllSameElement`.
- What a 0 breaking an enemy *Sleight* deletes (needs enemy Sleights; M4).
- Returning stocked cards on room end (no room system yet; `Initialize` already resets all piles).

## Design

### New: `Assets/_Project/Scripts/Core/SleightRules.cs`
```csharp
public readonly struct SleightResult
{
    public readonly int Value;            // sum of the composed values, >= 3
    public readonly CardShape Shape;      // slot 1 (after mirroring)
    public readonly Element Element;      // slot 2 (after mirroring)
    public readonly int FinisherValue;    // slot 3 composed value, 1..9
    public readonly bool AllSameElement;  // all three composed elements equal
    public float DamageMultiplier => 1f + FinisherValue * SleightRules.MultiplierPerPoint;
}

public static class SleightRules
{
    public const int StockSize = 3;
    public const int BurnFloor = 3;                 // burn only if CyclingCardCount > BurnFloor
    public const float MultiplierPerPoint = 0.1f;
    public const float StockTimeoutSeconds = 5f;

    // Throws on null / count != 3 / null entries.
    public static SleightResult Compose(IReadOnlyList<RuntimeCardInstance> stocked);
    // Value each slot contributes after mirroring (exposed for the HUD preview + tests).
    public static int ComposedValue(IReadOnlyList<RuntimeCardInstance> stocked, int slot);
    public static bool CanBurn(int cyclingCardCount) => cyclingCardCount > BurnFloor;
}
```
Mirror source = the numbered card with the highest `GetEffectiveValue()`. Ties go to the lowest slot index.

### Changed: `Assets/_Project/Scripts/Core/DeckCycle.cs`
- New state: `List<RuntimeCardInstance> stock`, `float stockIdleSeconds`.
- `IReadOnlyList<RuntimeCardInstance> Stock`, `int StockCount`.
- `CyclingCardCount` **includes stocked cards** (they're still in the cycle).
- `StockResult TryStock(int slot, out SleightResult sleight)`: moves the hand card into the stock and refills the slot from the draw queue (same as a play).
- `StockResult TryStockParry(out SleightResult sleight)`: same for the Parry slot (the next reserved 0 takes its place).
- `enum StockResult { Rejected, Stocked, Fired }`. On the 3rd card, `Fired`: compose, send slot 1 to
  `BurnedPile` (sets `IsBurnedThisFight = true`) if `SleightRules.CanBurn(CyclingCardCount)`,
  otherwise to discard. Slots 2-3 go to discard. The stock clears and the idle timer resets.
- `bool InterruptStock()`: crack catalyst (above). Returns false if the stock is empty.
- `bool AdvanceStockTimer(float deltaSeconds)`: adds time while the stock is non-empty. On
  reaching the timeout it discards the stock (no burn) and returns true. It ignores negative deltas.
- `Initialize` clears the stock and timer. `Reload` leaves the stock alone (stocked cards aren't in discard).
- All of these mutating methods raise `Changed`.

### Changed: `Assets/_Project/Scripts/Core/AttackPayload.cs`
- New field `int FinisherValue` (0 for non-Sleights) and an optional trailing constructor param
  `int finisherValue = 0`, clamped to `[0, 9]`. Existing callers are unchanged.
- `float DamageMultiplier => 1f + FinisherValue * SleightRules.MultiplierPerPoint`.
- `static AttackPayload FromSleight(in SleightResult s, int ownerId, Team team, int activationTick)`.

### Changed: `Assets/_Project/Scripts/Core/CombatMath.cs`
- `Damage`: for Sleights, `ceil(Value * DamagePerPoint * (10 + FinisherValue) / 10)` in **integer**
  math (avoids float error such as `ceil(30 * 1.1f) = 34`). Non-Sleights and 0-cards are unchanged.

## Edge cases
| Case | Behavior |
| --- | --- |
| Stock empty slot / invalid index / empty Parry slot | `Rejected`, nothing changes, no event |
| 0 + 9 + 5 | 0 mirrors 9: value 23, finisher 5 |
| 5 + 9 + 0 (0 in slot 3) | 0 mirrors 9: value 23, finisher 9, multiplier 1.9 |
| 0 in slot 1 | takes the mirrored card's shape. The burned card is the 0 itself (you lose that parry for the room) |
| 0 + 0 + 7 | both 0s mirror 7: value 21 |
| 0 + 0 + 0 | each counts 1: value 3, own shapes/elements |
| Mirror ties (two 9s) | copies the lower slot index |
| Floor of 3 at fire: `CyclingCardCount <= 3` | no burn, all 3 go to discard |
| Interrupt with 1 card, deck > 3 | that card is burned |
| Interrupt with 2 cards, deck > 3 | slot 1 burned, slot 2 discarded |
| Interrupt at deck <= 3 | nothing burned, all discarded |
| Interrupt / timer with empty stock | no-op (false) |
| Timeout | discards everything, no burn, timer resets |
| Reload while stock partly full | stock kept. Reload only shuffles discard |
| Stock with empty draw queue | slot stays empty (same as a play) |
| Sleight value > 9 | allowed (payload already keeps Sleight sums uncapped) |

## Steps
1. `SleightRules` + `SleightResult` + `SleightRulesTests` (compose, mirror, ties, all-zero, multiplier, `CanBurn`, argument errors).
2. `AttackPayload.FinisherValue` / `FromSleight` + `CombatMath.Damage` multiplier, with tests (including the float-trap case).
3. `DeckCycle` stock: `TryStock`/`TryStockParry`/fire/burn/Floor of 3, plus `DeckCycleStockTests`.
4. `InterruptStock` + `AdvanceStockTimer`, with tests. Card conservation (no card lost or duplicated) is checked in every test that moves cards.
5. Run the full EditMode suite (and PlayMode, to prove nothing regressed).

## Verification
- EditMode: `timeout 900 "/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform EditMode -testResults "$(pwd -W)/TestResults/editmode.xml" -logFile "$(pwd -W)/TestResults/editmode.log"`.
  The exit code must be 0, the `<test-run>` line must show 0 failures, and the log must have no `error CS` lines.
- PlayMode: same command with `-testPlatform PlayMode` and `playmode.*` files. Must still pass (nothing in Gameplay changes).
