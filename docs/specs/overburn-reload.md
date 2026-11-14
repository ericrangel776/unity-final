# Spec: Overburn Reload (M2 step 3)

## Goal
A safeguard against deck thinning: an **Overburn Reload** is a way to start an Active Reload by
paying HP. When that reload finishes, every burned card is shuffled back in with the discard. The
price escalates within a room, so it's a comeback tool, not a free loop. Core rules and the deck
operation live in `DeckArpg.Core`. `CombatantStatus` gains a way to pay HP. Input/HUD wiring is
step 4/5.

## Decisions (from interview, 2026-11-13)
- **Cost = 10% of max HP, never lethal.** Refused unless `Hp > cost`, so it can never kill you.
- **Restores all burned cards** with one payment.
- **Reload variant.** Started instead of a normal reload. You pay at `BeginOverburn`, it runs the same
  randomized timing bar / lock / grades / jam as a normal reload, and on completion the burned cards
  are moved to discard and then reshuffled with it. It needs an empty draw queue, and works even
  when only burned cards remain (discard empty, so a normal reload is impossible).
- **Escalating cost per room.** The n-th Overburn in a room costs `n x 10%` of max HP.

## Assumptions (mine, flag if wrong)
- Cost rounding: `ceil(maxHp * n * 10 / 100)` in integer math (100 HP: 10, 20, 30...; 25 HP: 3, 5, 8...).
- No room system exists yet, so the use counter resets via `ActiveReload.ResetOverburnCost()`.
  Step 4 calls it on room start and on respawn.
- `BeginOverburn` is refused when nothing is burned (a normal reload covers that case).
- Paying HP is **not a hit**: no flinch, knockback, counter, i-frame check or `Damaged` event.
- No refund: jam/miss/no-press still restores the cards (you paid). `Cancel` (death/respawn) restores
  nothing and refunds nothing. Revive resets HP anyway.
- Cards burned *during* the bar (e.g. a Card Break cracking an existing stock) are restored too:
  the restore happens at completion.

## Out of scope
- Input binding (e.g. hold Reload), HUD cost preview, VFX (steps 4-5).
- Room lifecycle (M3). Enemy Overburn (M4).

## Design

### New: `Assets/_Project/Scripts/Core/IHpPool.cs`
```csharp
/// HP that a rule can spend. Implemented by Gameplay's CombatantStatus so Core never references Gameplay.
public interface IHpPool
{
    int Hp { get; }
    int MaxHp { get; }
    void SpendHp(int amount);
}
```

### New: `Assets/_Project/Scripts/Core/OverburnRules.cs`
```csharp
public static class OverburnRules
{
    public const int CostPercentPerUse = 10;
    // Cost of the next Overburn after `usesThisRoom` earlier ones: ceil(maxHp * (uses+1) * 10%). >= 1 for maxHp >= 1.
    public static int Cost(int maxHp, int usesThisRoom);     // throws if maxHp < 1 or uses < 0
    public static bool CanAfford(int hp, int cost) => hp > cost;   // never lethal
}
```

### Changed: `Assets/_Project/Scripts/Core/DeckCycle.cs`
- `int RestoreBurned()`: moves every burned card to discard, clears `IsBurnedThisFight`, raises `Changed`
  (only if something moved), and returns the count.

### Changed: `Assets/_Project/Scripts/Core/ActiveReload.cs`
- `bool IsOverburn { get; }`: true while an Overburn bar runs (and during its `Completed` handlers).
- `int OverburnUsesThisRoom { get; }`, `int NextOverburnCost(int maxHp)`.
- `bool CanBeginOverburn(IHpPool payer)`: Idle, `deck.DrawCount == 0`, `deck.BurnedCount > 0`,
  `CanAfford(payer.Hp, cost)`.
- `bool BeginOverburn(IHpPool payer)`: if allowed, `payer.SpendHp(cost)`, uses++, rolls and starts
  the bar like `Begin()`, and sets `IsOverburn`.
- On completion: if `IsOverburn`, `deck.RestoreBurned()` runs before `deck.Reload()`. Then `Completed`
  is raised, then `IsOverburn` is cleared.
- `Cancel()` also clears `IsOverburn` (no restore).
- `void ResetOverburnCost()`: uses = 0.

### Changed: `Assets/_Project/Scripts/Gameplay/CombatantStatus.cs`
- Implements `IHpPool`. `SpendHp(int amount)`: ignored if dead or `amount <= 0`. Otherwise
  `Hp = max(1, Hp - amount)` (safety net: spending never kills).

## Edge cases
| Case | Behavior |
| --- | --- |
| Draw queue not empty | refused, no HP spent |
| Nothing burned | refused (use a normal reload) |
| `Hp == cost` / `Hp < cost` | refused (would be lethal) |
| `Hp == cost + 1` | allowed, leaves 1 HP |
| Only burned cards left (discard empty) | allowed; on completion the burned cards fill the queue |
| Already reloading | refused, no HP spent |
| 2nd / 3rd use in a room | costs 20% / 30% of max |
| `ResetOverburnCost()` | next use costs 10% again |
| Jam / no press | still restores on completion |
| `Cancel` mid-bar | no restore, no refund, `IsOverburn` cleared |
| `payer` null | `ArgumentNullException` |
| `SpendHp` on a dead combatant / non-positive amount | ignored |

## Steps
1. `IHpPool` + `OverburnRules` + `OverburnRulesTests` (rounding, escalation, affordability, argument errors).
2. `DeckCycle.RestoreBurned`, with tests (conservation, flag cleared, empty no-op without an event).
3. `ActiveReload` Overburn: begin/refusals/payment/completion restore/jam/cancel/reset, with tests
   (fake `IHpPool`).
4. `CombatantStatus : IHpPool` + PlayMode test (`SpendHp` reduces HP, no flinch/knockback/`Damaged`,
   never below 1, ignored when dead).
5. Run the full EditMode + PlayMode suites.

## Verification
- EditMode: `timeout 900 "/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform EditMode -testResults "$(pwd -W)/TestResults/editmode.xml" -logFile "$(pwd -W)/TestResults/editmode.log"`.
  The exit code must be 0, there must be 0 failures, and the log must have no `error CS` lines.
- PlayMode: same with `-testPlatform PlayMode` / `playmode.*`. Must pass, including the new `SpendHp` tests.
- An end-to-end Core test: thin a deck to the Floor of 3 with Sleights, Overburn, and the full deck is cycling again.
