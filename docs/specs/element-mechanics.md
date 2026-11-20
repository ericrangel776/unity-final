# Element mechanics (fun pass, item 2)

Branch: `feat/element-mechanics` (from `main`). Source: `docs/notes/CURRENT.md`, Next steps item 2, and design doc 6.1.

## Goal
Today `Element` changes only colours. `CombatantStatus.TakeHit` ignores it, and `SleightResult.AllSameElement` is
computed but never used. This pass makes elements real. **Element decides how a card is delivered** (Fire is a travelling
fireball, Ice a short frost blast, Lightning a mid-range arc that chains, and the new Wind element a gust cone). Each
element also applies a status on hit. Same-element Sleights fire a finisher shockwave. The goal is for draft choices and
Sleight order to change how a fight plays, not just the colour of the numbers.

## Out of scope
- Elements beyond Wind (Poison, Holy, Shadow and so on). They get a follow-up spec once these feel right.
- Fire burning-ground tiles and Ice crystal shrapnel (design doc 6.1). They go on the later list.
- Freeze, or any status stacking (burn and chill refresh; they never stack).
- Wind cards in enemy decks or the starter deck. Enemy and boss decks don't change.
- The finisher shockwave for enemy or boss Sleights (see Decisions: player-only).
- Heal cards (`CardData.isHeal`). That is item 3, HP economy.
- New SFX or imported assets. Everything is procedural or greybox, like the hit-juice pass.
- Balance changes outside the new element constants (card values, HP, room intensity).

## Decisions (from the interview, 2026-11-19)
| Topic | Decision |
|---|---|
| Delivery | **Element decides delivery.** Physical keeps the card's `CardShape` (Strike/Throw/Arcana). Fire = Projectile (fireball), Ice = FrostBlast (short, in front), Lightning = ChainArc (fast, mid-range, chains), Wind = Gust (forward cone). |
| Sleights | Slot 1's element (plus shape, if Physical) decides the **delivery**, and slot 2's element decides the **status**. Slot 3 is still the multiplier. Mirrored 0s resolve as today. |
| Elements | The existing 4 plus **Wind**. |
| Symmetry | Statuses apply to everyone, including Hexfire Elite cards and the player. A Lightning chain jumps only between combatants on the victim's team. |
| Single plays | Single cards apply their status too. Sleights use the longer durations, and only Sleights get the finisher shockwave. |
| Tuning | "Medium" package (constants below). |
| Boss/Elites | Every status lasts **50%** as long on the boss and Elites. Wind knockback is x0.5 on the **boss** only. |
| Player chill | Move speed -30% only. Dash, i-frames and the Active Reload bar are untouched. |
| Wind content | Draft pool: Gust 4 (Common), Gust 6 (Uncommon), Tempest 8 (Rare). Wind joins the KindledGround element roll. |
| Readability | Afflicted bodies pulse in the element colour. One small coloured pip per status floats above the head. Chains draw a brief line. The player's statuses also show in the run HUD strip. |
| Done when | EditMode tests for every rule, PlayMode tests for every delivery and status, a screenshot, and **a playtest sign-off by the user**. |

### Assumptions made while writing (flag in the PR if the user disagrees)
- **Finisher shockwave is player-only.** Boss and Warden decks are all Physical, so a symmetric shockwave would fire on
  every boss Sleight and undo the signed-off M4 boss tuning.
- **Physical's +50% posture applies to breaks.** Plain body hits deal no posture today (`CombatantStatus.React`), and this
  spec doesn't add posture-on-hit. So Physical = a Physical attack that wins a clash deals x1.5 break posture. This is
  symmetric, so boss breaks on the player get stronger too. Step 11 re-measures the boss fight to check it.
- Existing `CardData` assets keep their ids. Only the `shape` and display names in `StarterContentBuilder` change where a
  name now lies about its delivery (see Content).
- `Element.Wind` is appended **last** (value 4), so the serialized ints of existing assets don't shift.

## Design

### Core (EditMode-testable)

#### `Assets/_Project/Scripts/Core/CardEnums.cs` (change)
```csharp
public enum Element { Physical, Fire, Ice, Lightning, Wind }   // Wind appended = 4

/// <summary>How an attack travels. Derived from element (and shape for Physical); never authored.</summary>
public enum Delivery { Strike, Projectile, Burst, FrostBlast, ChainArc, Gust }
```

#### `Assets/_Project/Scripts/Core/ElementRules.cs` (new)
Pure rules and every tuning constant. There are no scene dependencies.
```csharp
public static class ElementRules
{
    // Burn (Fire)
    public const float BurnFraction = 0.4f;          // total burn = ceil(hitDamage * 0.4), at least 1
    public const float BurnSeconds = 3f;             // single card
    public const float SleightBurnSeconds = 4f;
    public const float BurnTickSeconds = 0.5f;
    // Chill (Ice)
    public const float ChillSlow = 0.3f;             // x0.7 move speed; enemies also x0.7 wind-up/recover tick rate
    public const float ChillSeconds = 2.5f;
    public const float SleightChillSeconds = 4f;
    // Chain (Lightning)
    public const int ChainTargets = 3;
    public const float ChainHopRadius = 4f;          // each hop measured from the previous link
    public const float ChainDamageFraction = 0.5f;   // ceil, at least 1
    // Physical
    public const float PhysicalBreakPostureMultiplier = 1.5f;
    // Wind
    public const float WindKnockbackMultiplier = 2.5f;
    public const float WindSlamPostureFraction = 0.3f;   // of the victim's MaxPosture, once per gust hit
    public const float WindSlamWindowSeconds = 0.5f;     // a wall/body bump this soon after the hit counts as a slam
    public const float WindSlamMinSpeed = 2f;            // m/s of knockback still left at the bump
    // Resistances
    public const float EliteBossDurationScale = 0.5f;
    public const float BossWindKnockbackScale = 0.5f;
    // Same-element finisher shockwave (player Sleights only)
    public const float FinisherRadius = 3.5f;
    public const float FinisherDamageFraction = 0.3f;    // of the Sleight's total (unsplit) damage, ceil
    public const float FinisherDelaySeconds = 0.35f;     // scaled time, after the Sleight fires
    public const float PhysicalFinisherPostureFraction = 0.2f;

    public static Delivery DeliveryFor(Element element, CardShape shape);  // Physical: Strike/Throw/Arcana -> Strike/Projectile/Burst
    public static int BurnTotal(int hitDamage);
    public static int ChainDamage(int hitDamage);
    public static float StatusSeconds(Element element, bool isSleight, float durationScale); // 0 for Physical/Wind/Lightning
    public static float KnockbackScale(Element element);                     // 2.5 for Wind, else 1
    public static int FinisherDamage(int sleightTotalDamage);
    /// <summary>Picks up to ChainTargets links: nearest unvisited candidate within ChainHopRadius of the previous link.</summary>
    public static List<int> PickChain(Vector3 origin, IReadOnlyList<Vector3> candidates);
}
```
`PickChain` uses `UnityEngine.Vector3` (Core already references UnityEngine for `ScriptableObject`). It measures
distance on the XZ plane and returns candidate indices in hop order. Ties go to the lower index.

#### `Assets/_Project/Scripts/Core/ElementStatusState.cs` (new, plain C#)
Holds one combatant's statuses and is advanced manually, so it can be tested without a scene.
```csharp
public sealed class ElementStatusState
{
    public float DurationScale { get; set; } = 1f;       // 0.5 on boss/Elites
    public bool IsBurning { get; }   public float BurnRemaining { get; }
    public bool IsChilled { get; }   public float ChillRemaining { get; }
    public float SpeedMultiplier { get; }                // 1 - ChillSlow while chilled, else 1
    public void ApplyBurn(int totalDamage, bool isSleight); // refresh: duration resets; pending damage = max(pending, new)
    public void ApplyChill(bool isSleight);                 // refresh: remaining = max(remaining, new duration)
    public int Tick(float dt);                              // advances timers, returns burn damage due this tick (0 if none)
    public void Clear();
}
```
Burn spreads its pending damage evenly across the remaining ticks, rounding up and never dealing more than is pending. A
burn with fewer ticks than damage deals more per tick. A burn always deals at least 1 per tick until it is empty.

#### `Assets/_Project/Scripts/Core/AttackPayload.cs` (change)
- Add `public readonly Delivery Delivery;`. Set it to `ElementRules.DeliveryFor(element, shape)` in the main constructor.
  A Sleight passes slot 1's delivery explicitly (below), because its `Element` is slot 2's.
- Add `public readonly bool IsFinisherSource;` (= `SleightResult.AllSameElement`; Sleights only).
- `Poke` stays Physical Strike.

#### `Assets/_Project/Scripts/Core/SleightRules.cs` (change)
- `SleightResult` gains `Delivery Delivery`: `DeliveryFor(first.Data.Element, first.Data.Shape)`, using the resolved
  (mirrored) card. `Shape` stays for HUD text and the existing callers.
- Add a `HitCount(Delivery)` overload: Strike 3, Projectile 2 (boomerang), and 1 for everything else. Keep
  `HitCount(CardShape)`, forwarding it as Physical, so the boss call sites keep working. Then switch
  `PlayerCombatInput:258`, `BossEnemy:238` and `BossSleightClash:121` to the delivery overload.
- `AttackPayload.FromSleight` passes `sleight.Delivery` and `sleight.AllSameElement`.

#### `Assets/_Project/Scripts/Core/CombatMath.cs` (change)
- `KnockbackSpeed(payload)` multiplies by `ElementRules.KnockbackScale(payload.Element)`.

#### `Assets/_Project/Scripts/Core/MapOfferGenerator.cs` (change)
- `Elements` gains `Element.Wind`, so KindledGround can roll Wind. Check that the existing seeded tests that pin a
  specific roll still hold. If one shifts because the array grew, update its expected value and say so in the commit.

### Gameplay

#### `Assets/_Project/Scripts/Gameplay/CombatActionController.cs` (change)
Switch on `payload.Delivery` instead of `payload.Shape` in `SpawnScaled` and `SpawnSleight`. New serialized fields:
| Delivery | Single card | Sleight |
|---|---|---|
| Strike | unchanged box | unchanged 3-hit lunge combo |
| Projectile | unchanged sphere, speed 12 (the fireball) | unchanged boomerang |
| Burst | unchanged Arcana sphere around self | unchanged growing blast |
| FrostBlast | sphere r=`frostRadius` 1.4, centred `frostReach` 1.0 u in front, `frostDuration` 0.25 s | grows from 0.5 to `frostRadius * 2.1` (~3 u) around self over 0.3 s |
| ChainArc | sphere r=0.3, speed `arcSpeed` 28, lifetime `arcLifetime` 0.25 s (~7 u). **Despawns on its first body hit** | same, scale 1.5. The chain does the crowd work |
| Gust | box `gustSize` (2.4 w x 1.2 h x 2.5 d), centred `gustReach` 1.6 u in front, 0.2 s; hits every body inside | box x1.5 |

Every delivery is still an `AttackHitbox` carrying the immutable payload, so mid-air and body clashes work unchanged.
`AttackHitbox` gets a `DespawnOnBodyHit` flag for ChainArc.

#### `Assets/_Project/Scripts/Gameplay/CombatantStatus.cs` (change)
- `HitInfo` gains `bool IsEffect`. Effect damage (burn ticks, chain jumps, finisher shockwave) sets it.
- New `public bool TakeEffectDamage(int amount, Element element, AttackPayload source)`. It applies damage with no
  flinch, knockback, counter or clash. It respects `IsInvulnerable`, `IsDead`, same team and `DamageTakenMultiplier`. It
  raises `Damaged`, `AnyHitTaken` and `AnyHitLanded` (`IsEffect = true`), and `Died` if lethal. It sets `LastHitElement`.
- New `public float KnockbackTakenMultiplier { get; set; } = 1f;`. `Push` multiplies by it. The boss sets it to 0.5 only
  for Wind hits: `React` uses `payload.Element == Wind ? KnockbackTakenMultiplier : 1`.
- New `public event Action<AttackPayload> KnockedBackBy;`, raised from `React` when a non-poke push happens. The Wind
  slam uses it.
- New `public static IReadOnlyList<CombatantStatus> All` registry (OnEnable add, OnDisable remove) for chain targeting.
- `Revive()` and death clear statuses through `ElementStatusEffects` (below).

#### `Assets/_Project/Scripts/Gameplay/ElementStatusEffects.cs` (new, on every combatant)
`[RequireComponent(typeof(CombatantStatus))]` MonoBehaviour that owns an `ElementStatusState`.
- `Update`: `int burn = state.Tick(Time.deltaTime)`. If above 0, call `status.TakeEffectDamage(burn, Fire, ...)`. Burn
  stops when the combatant dies, and the room clear destroys the object anyway.
- `public float SpeedMultiplier => state.SpeedMultiplier;`, plus `ApplyBurn`/`ApplyChill`/`Clear`, and a
  `DurationScale` passthrough.
- **Wind slam:** on `KnockedBackBy` with a Wind payload, open a `WindSlamWindowSeconds` window. Callers report bumps
  through `NotifyKnockbackBlocked()`. If the window is open and `status.KnockbackVelocity.magnitude >= WindSlamMinSpeed`,
  call `status.AddPostureDamage(WindSlamPostureFraction * status.MaxPosture)` once and close the window.
- Events `StatusChanged` (for the views). It clears on `Died` and `Revived`.

#### `Assets/_Project/Scripts/Gameplay/ElementSystem.cs` (new, one per scene)
Subscribes to `CombatantStatus.AnyHitLanded`. For each **non-effect** hit whose victim isn't dead:
- **Fire:** `victimFx.ApplyBurn(ElementRules.BurnTotal(hit.Damage), hit.Payload.IsSleight)`.
- **Ice:** `victimFx.ApplyChill(hit.Payload.IsSleight)`.
- **Lightning:** `PickChain` from the victim over `CombatantStatus.All` (alive, same team as the victim, not the victim).
  Each link gets `TakeEffectDamage(ChainDamage(hit.Damage), Lightning, payload)`. Raise
  `ChainFired(Vector3[] points)` for the line FX. Effect hits never chain again.
- **Physical / Wind:** nothing here. Physical lives in the clash (below) and Wind in knockback plus the slam.

It also runs the **finisher shockwave**. `CombatActionController.SpawnSleight` raises `SleightSpawned(payload)`. If
`payload.IsFinisherSource && payload.Team == Team.Player`, the shockwave waits `FinisherDelaySeconds` (scaled time, so
hit-stop pauses it). It then hits every living enemy within `FinisherRadius` of the caster for
`FinisherDamage(CombatMath total Sleight damage)` as effect damage, and applies the element's Sleight-strength status
(Lightning: each target chains; Wind: a Wind push from the caster; Physical: posture `0.2 * MaxPosture`). It raises
`FinisherFired(position, element)` for FX.

#### Clash and knockback hooks (change)
- `ClashSystem.cs:120` and `CombatActionController.cs:183`: when the **winning** payload's element is Physical, scale
  the loser's break posture by `PhysicalBreakPostureMultiplier`. Add an optional `postureScale = 1f` parameter to
  `CombatantStatus.ApplyBreak` rather than a new method.
- `TrainingDummyEnemy.cs`: movement uses `moveSpeed * fx.SpeedMultiplier`. The telegraph and recover timers decrement by
  `Time.deltaTime * fx.SpeedMultiplier`. If `body.Move(KnockbackVelocity * dt)` returns `CollisionFlags.Sides`, call
  `fx.NotifyKnockbackBlocked()`. `TelegraphProgress` stays correct because it reads `timer`.
- `PlayerCombatInput.cs:133`: `motor.MoveSpeedMultiplier = deck.Reload.SpeedMultiplier * fx.SpeedMultiplier`.
  `PlayerMotor` reports side collisions during knockback the same way.
- `RunDirector.cs` (spawn, ~line 216): for EliteTrial and Boss rooms set `fx.DurationScale = EliteBossDurationScale`.
  For the boss, also set `status.KnockbackTakenMultiplier = BossWindKnockbackScale`.

#### `Assets/_Project/Scripts/Gameplay/ElementStatusFx.cs` (new)
Greybox readability on every combatant: renderer tint pulse (fire orange / ice blue), one pip sphere per active status
above the head, at the height `HitFeedbackHeight + 0.6`, plus a chain `LineRenderer` (0.12 s, unscaled time) driven by
`ElementSystem.ChainFired`. The finisher ring is an expanding greybox disc in the element colour on `FinisherFired`. It
must not fight `HitFlash`: tint through a `MaterialPropertyBlock` colour that `HitFlash` overrides while flashing.

#### `Assets/_Project/Scripts/Gameplay/ElementColors.cs` (change)
Add Wind: pale green `(0.6, 1, 0.75, 0.5)`.

### UI
- `UI/RunHudStripView.cs`: add BURN and CHILL chips with seconds left, shown only while the player has them.
- `UI/RunUiText.cs:138,168`, `UI/HandHudView.cs:137`, `UI/StockView.cs:111`: show the **delivery** in place of
  `Shape` (e.g. "Fireball · Fire", "Chain Arc · Lightning", "Gust · Wind"). Add `RunUiText.DeliveryName(Delivery)`.
- `UI/DamageNumberView.cs`: effect hits show at x0.75 size, still element-coloured.
- `Gameplay/HitImpactFx.cs` and `Core/HitJuiceRules.BodyHitStopSeconds`: effect hits give **no** hit-stop, no shake and
  only a 3-spark puff. Add `bool isEffect` to `BodyHitStopSeconds` (or check it in `HitStop`); burn ticks must never
  freeze the game.

### Content: `Assets/_Project/Scripts/Editor/StarterContentBuilder.cs` (change)
Author `shape` to match each element's delivery so `RunContent.CheckDraftPool`'s every-shape check still passes:
Fire -> Throw, Ice -> Arcana, Lightning -> Throw, Wind -> Strike. Changes:
- `frost_*`, `hex_frost_4`: already Arcana. `spark_*`, `hex_spark_5`, `hex_arc_7`: already Throw.
- `volt_burst_4` (Lightning Arcana) -> Throw, display name "Volt Arc 4". `thunderhead_9` -> Throw (name kept).
- `cinder_nova_6` (Fire Arcana) -> Throw, display name "Cinder Bolt 6". `inferno_8`, `hex_flare_6` -> Throw (names kept).
- `rime_lance_6` (Ice Throw) -> Arcana, display name "Rime Burst 6".
- New: `gust_4` (Wind, Common), `gust_6` (Wind, Uncommon), `tempest_8` (Wind, Rare), all Strike shape. The draft pool goes
  from 17 to 20 cards.
- `RunContent.CheckDraftPool` loops over `Enum.GetValues(typeof(Element))`, so it requires a Wind card automatically.
- Rebuild the content and scenes with the builder's `-executeMethod` entry points (same as hit-juice step 8), and commit
  the regenerated assets.

### Scene wiring: `Assets/_Project/Scripts/Editor/GreyboxArenaBuilder.cs` (change)
Add `ElementSystem` to both scenes. Add `ElementStatusEffects` and `ElementStatusFx` to the player and the dummy prefab
(and, through it, to the boss and Elites). Rebuild `M1_Arena` and `M3_Run`.

### Docs
- `docs/design/game-design-doc.md` 6.1: add a short "Implemented (vertical slice)" note. It should say that element
  decides delivery, that Wind was added, that ground tiles and shrapnel are deferred, and that the finisher is
  player-only. Point to this spec.
- `CLAUDE.md` design summary: change "Slot 1 = shape" to "Slot 1 = delivery (element-derived; Physical keeps shape)",
  and add Wind to the slot 2 list.

## Edge cases
| Case | Behaviour |
|---|---|
| 0-card / zero-breaker hit | 0 damage, so no `AnyHitLanded` and no status. Mirrored 0s in a Sleight resolve element through the mirror source, as today. |
| Poke | Physical with no break, so no element effect. |
| Effect damage (burn/chain/finisher) | Never applies a new status, never chains, never hit-stops, never counters or flinches, never clashes. |
| Burn reapplied while burning | Duration resets to the new value, and pending damage = max(old pending, new total). It never stacks. |
| Chill reapplied | Remaining = max(remaining, new duration). The slow stays 30% (no stacking, no freeze). |
| Lethal hit | No status is applied to a dead victim. Lightning still chains *from* the dead victim's position. |
| Burn kills | `TakeEffectDamage` raises `Died`. The death pop uses Fire through `LastHitElement`. The room clear works as usual. |
| Chain with no other enemies within 4 u | No jumps and no line. A chain never jumps to the attacker's team. |
| Chain candidates already dead or destroyed mid-chain | They are skipped. `All` only holds enabled components, and dead ones are filtered. |
| Multi-hit Strike Sleight with a Lightning slot 2 | Each of the 3 hits chains on its own (the "multi-hit resolves per hit" safeguard), and each uses that hit's split damage. |
| Boss / Elite | Burn and chill durations are halved (burn total damage halves with them). Chain damage and Physical posture are unchanged. Wind knockback is x0.5 on the boss only. |
| Boss Sleight with all-Physical cards | No finisher (player-only). Its Physical break posture bonus does apply to the player. |
| Chilled enemy mid-telegraph | The wind-up takes longer (timer ticks at x0.7). The parry window (`BodyHitDelay`) is unscaled, as with tempo. The telegraph UI keeps reading `TelegraphProgress`. |
| Chill + room tempo (`ApplyTempo`) | They multiply: tempo changes the base fields once, and chill scales the per-frame rate. |
| Player chilled + Active Reload speed boost | The multipliers multiply. Dash speed and i-frames are unaffected. |
| Wind pushes a body into a wall twice within the window | Posture is added once per gust hit. |
| Wind slam on a body with `maxPosture` 0 | `AddPostureDamage` already handles it. No posture break loop. |
| Gust or FrostBlast hitting several bodies | One hit per body per hitbox (existing `alreadyHit` set). |
| ChainArc meets an enemy hitbox mid-air | It clashes like any projectile. If it loses, it is cancelled and never chains. |
| Hit-stop during a burn or finisher delay | Both use scaled time, so they pause with the freeze. |
| Room clear / scene unload with burns active | The components are destroyed with their objects. `ElementSystem` unsubscribes in `OnDisable`. |
| Revive (M1 dummy) | Statuses and pips are cleared. |
| Old card assets missing `Element.Wind` | Unaffected. Wind is appended as value 4. |

## Steps
Run EditMode after each Core step and PlayMode after each Gameplay/UI step (commands below). Commit after each green
step with a focused message.

1. **Branch.** `git checkout -b feat/element-mechanics`.
2. **Enums + ElementRules.** Add `Element.Wind`, `Delivery`, `ElementRules` and `Tests/EditMode/ElementRulesTests.cs`.
   Cases: `DeliveryFor` for all 5 elements x 3 shapes. `BurnTotal(30) == 12`, `BurnTotal(1) == 1`, `ChainDamage(7) == 4`.
   `StatusSeconds` single/Sleight/scaled 0.5. `KnockbackScale(Wind) == 2.5`. `FinisherDamage`. `PickChain`: nearest-first
   hops, the 4 u limit measured from the previous link, max 3, ties to the lower index, empty input.
3. **ElementStatusState.** Add the class and `ElementStatusStateTests.cs`: burn total delivered exactly over its
   duration, refresh vs. max-pending, chill refresh and `SpeedMultiplier`, `DurationScale` 0.5 halves both, `Clear`, and
   `Tick` with a huge `dt` not over-dealing.
4. **Payload, Sleight, knockback.** Add `Delivery` and `IsFinisherSource` to `AttackPayload`, add `SleightResult.Delivery`
   and `HitCount(Delivery)`, and apply Wind knockback in `CombatMath`. Extend `AttackPayloadTests`, `SleightRulesTests`
   (slot-1 delivery with a slot-2 element, a mirrored 0 in slot 1, `AllSameElement` -> `IsFinisherSource`) and
   `CombatMathTests`. Add Wind to `MapOfferGenerator.Elements` and fix any pinned-seed test.
5. **Deliveries.** Switch `CombatActionController` to `Delivery` and add FrostBlast, ChainArc (`DespawnOnBodyHit`) and
   Gust. Update the 3 `HitCount` call sites. Add PlayMode `ElementDeliveryTests.cs` with one test per delivery: hitbox
   type, size and position, that ChainArc despawns after its first hit and never reaches 8 u, that Gust hits 2 bodies at
   once, and that the Sleight variants match the table. Existing Strike/Throw/Arcana tests must still pass unchanged.
6. **Effect damage + registry.** Add `TakeEffectDamage`, `HitInfo.IsEffect`, `CombatantStatus.All`,
   `KnockbackTakenMultiplier`, `KnockedBackBy` and `ApplyBreak(..., postureScale)`. Make `HitStop`/`HitImpactFx` skip
   effects and shrink effect numbers in `DamageNumberView`. PlayMode tests: effect damage fires `AnyHitLanded` with
   `IsEffect`, gives no hit-stop (timeScale stays 1), no flinch, no knockback, can kill, and respects i-frames.
7. **Statuses.** Add `ElementStatusEffects` and `ElementSystem`, and wire chill into `TrainingDummyEnemy` and
   `PlayerCombatInput`. PlayMode `ElementStatusTests.cs`: a Fire card hit burns for `BurnTotal` over 3 s (realtime wait);
   Ice slows enemy move speed and stretches the telegraph by 1/0.7; a chilled player moves at x0.7; Lightning chains to 3
   of 4 clustered enemies and not to a 5 u straggler or to the player; a Physical win deals x1.5 break posture; a Wind hit
   pushes 2.5x as far and a wall bump adds 30% posture once; boss and Elite durations are halved; boss Wind push x0.5;
   statuses also land on the player from enemy Fire/Ice cards.
8. **Finisher shockwave.** Raise `SleightSpawned`, then add the finisher in `ElementSystem`. Tests: an all-Fire player
   Sleight damages a 3 u enemy for `FinisherDamage` after 0.35 s and burns it; a 4 u enemy is untouched; a mixed Sleight
   gives no shockwave; an enemy all-Physical Sleight gives no shockwave.
9. **Readability FX + UI.** Add `ElementStatusFx`, Wind in `ElementColors`, the HUD chips, and delivery names in
   `RunUiText`/`HandHudView`/`StockView`. Tests: pips appear and clear with statuses, a chain raises `ChainFired` with 2+
   points, the HUD chip shows while the player burns, and the `RunUiTextTests` strings show delivery names.
10. **Content + scenes.** Update `StarterContentBuilder` and `GreyboxArenaBuilder`, close the Editor, run the content
    and both scene rebuilds through `-executeMethod`, and commit the regenerated assets. Extend `RunContentTests`
    (pool = 20, has Wind, every element's cards author the matching shape) and `RunSceneContentTests`/`ArenaContentTests`
    (`ElementSystem` is in both scenes, and player and dummy have `ElementStatusEffects` + `ElementStatusFx`).
11. **Boss re-measure.** Run `BossFightMeasurement` (`[Explicit]`) before (on `main`) and after. Record both in
    `docs/notes/CURRENT.md`. If the player's win rate or fight length moves a lot because of Physical break posture on
    the player, report it to the user. Don't retune without asking.
12. **Screenshot.** Extend `HudScreenshotCapture` with a shot of a burning, chilled cluster mid-chain, with pips and the
    line visible and the HUD chip on the player. Check it in `TestResults/screens/`.
13. **Docs, full suite, playtest.** Make the design doc and `CLAUDE.md` edits, run EditMode and PlayMode in full, then
    hand off to the user for a playtest (M3 run: a mob room with each element, Hexfire Elite, the boss). Tune only the
    `ElementRules` constants or serialized fields they ask for. Record the sign-off in `docs/notes/CURRENT.md`, then
    open the PR (merge commit, no attribution lines).

## Verification
Close the Unity Editor first (`tasklist //FI "IMAGENAME eq Unity.exe"`). Wrap each run in `timeout 1200`.
- EditMode:
  `timeout 1200 "/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform EditMode -testResults "$(pwd -W)/TestResults/editmode.xml" -logFile "$(pwd -W)/TestResults/editmode.log"`
- PlayMode: the same with `-testPlatform PlayMode` and `playmode.xml`/`playmode.log`.
- Pass = exit code 0. The `<test-run result="Passed" ...>` counts must be at least the baseline (EditMode 734, PlayMode
  319 passed / 10 skipped) plus the new tests, with 0 failed. `grep "error CS" TestResults/*.log` must print nothing.
- Boss measurement:
  `timeout 1200 ".../Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform PlayMode -testFilter DeckArpg.Tests.PlayMode.BossFightMeasurement -testResults "$(pwd -W)/TestResults/boss.xml" -logFile "$(pwd -W)/TestResults/boss.log"`.
  Compare against the `main` run.
- Screenshots:
  `timeout 1200 ".../Unity.exe" -batchmode -projectPath "$(pwd -W)" -runTests -testPlatform PlayMode -testFilter DeckArpg.Tests.PlayMode.HudScreenshotCapture -testResults "$(pwd -W)/TestResults/screens.xml" -logFile "$(pwd -W)/TestResults/screens.log"`,
  then view the PNGs in `TestResults/screens/`.
- Feel: the user's playtest sign-off (required).

## Addendum: Wind + Lightning tuning pass (2026-11-19, after the first playtest)

Playtest feedback: "make the wind effect much stronger and make the lightning effect more impressive". Gameplay first;
visuals stay greybox (animation is the last big step).

| Item | Before | After |
|---|---|---|
| Wind knockback multiplier | x2.5 | **x3** |
| Wind push decay | 0.15 s (shared) | **0.4 s** (`WindKnockbackDecaySeconds`): Gust 6 carries ~4 u, Tempest 8 ~4.7 u, boss half |
| Wind slam | +30% max posture | **+50% max posture and effect damage = ceil(gust hit x 0.5)** |
| Gust that loses/ties a telegraph clash | cancelled, no effect | **still shoves (no damage, no cancel)**, so the enemy's swing tends to whiff. Clash rules unchanged |
| Chain targets / hop radius / damage | 3 / 4 u / 50% | **5 / 6 u / 60%** |
| Chain jolt | none | **0.4 s stun on each surviving link** (x duration scale: 0.2 s on boss/Elites). No flinch event, so it pauses a telegraph rather than cancelling it |
| Chain feedback | 8 cm straight line, 0.12 s | **22 cm jagged bolt re-jittered each frame, 0.3 s, thinning out; 0.07 s hit-stop; 14-spark burst per link** |

The user picked "shove even on a loss" over letting Wind bypass telegraph clashes, so "higher value breaks lower" holds.
