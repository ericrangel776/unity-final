# Spec: Enemy variants (Caster, Rusher, second wave)

## Goal
Mob rooms all play the same today: melee dummies walk in and swing. This spec adds two variants. The **Caster** keeps
its distance and fires projectile cards, which can be clashed mid-air. The **Rusher** is fast and fragile and plays low
cards. Mob rooms at Intensity 4 and up also get a small **second wave**. Variants are placed by room theme, so the Map
Card choice also picks the kind of fight.

## Out of scope
- Elite and Boss rooms: no variants and no waves. `BossEnemy`, `CounterpuncherTrial` and the `TurnHook` paths are unchanged.
- New art, models or animation. Variants are greybox only: a base tint and a body scale.
- Changing clash rules. Winning a mid-air clash against a Caster's projectile stuns and shoves the Caster through the
  existing `ClashSystem.Break`/`OnClashLost` path, even at range. This is intended.
- Teleport/blink movement, pathfinding, leading moving targets, wave 3+, and waves that overlap wave 1.
- Retuning the boss or the bot harness numbers.

## Design

### Room composition (user decisions)
| Room | Wave 1 (n = `RoomIntensity.EnemyCount`, unchanged) | Wave 2 |
|---|---|---|
| Hexfire | `ceil(n/2)` Casters, the rest Brutes (Brutes keep the Hexfire deck) | Mid (I 4-6): 1, High (I 7-9): 2, using the same rule |
| Vanguard | `floor(n/2)` Rushers, the rest Brutes | same |
| KindledGround / BountyWind | 1 variant (Caster or Rusher, seeded coin flip), the rest Brutes | same rule with the wave's count |
| EliteTrial / Boss | 1 Brute (unchanged) | none |

A second wave adds **+25% currency** to the room's reward (`RewardRules.Currency`). Wave 2 spawns **1.0 s** after the
last wave-1 enemy dies, and a ground ring shows at each spawn point during that delay. The room clears when wave 2 dies.

### Variant stats (percent of a Brute in the same room; room tempo/HP/value scaling still apply on top)
| | Move | HP | Wind-up time | Recover time | Posture | Deck |
|---|---|---|---|---|---|---|
| Brute | 100 | 100 | 100 | 100 | 100 | room deck (unchanged) |
| Rusher | 180 | 50 | 75 | 50 | 60 | Jab 1/2/2/3/3/4 (Strike/Physical) |
| Caster | 90 | 70 | 100 | 100 | 80 | Bolt 3, Shard 4, Bolt 5, Shard 6 (Bolt = Fire Throw, Shard = Physical Throw; both deliver `Projectile`) |

The Caster deck holds only projectile deliveries. Lightning (ChainArc, about 7 u reach) and Ice (FrostBlast, short
range) are left out because the Caster fights from 4-9 u. Room value bonuses (`EnemyCardValueBonus`) still apply to
both variant decks.

### Caster movement (replaces the Approach rule for Casters only)
Constants: `PreferredRange 7`, `RetreatRange 4`, `MaxCastRange 9`, `RetreatSpeedFactor 0.8`, `CorneredSeconds 1.5`.
- If the distance is more than 7, it advances.
- If the distance is less than 4, it backs away at 0.8x speed and adds to a `closeTimer`. The timer resets to 0 whenever
  the distance is 4 or more.
- Otherwise it holds its position and keeps facing the player.
- It may begin a telegraph when its timer is up **and** (`4 <= d <= 9` **or** `closeTimer >= 1.5`). The second case is
  a cornered Caster shooting point-blank.
- Telegraph/Recover/Swing are unchanged. The swing aims at the player's position at that moment, as Brutes do.

### Files

**Create `Assets/_Project/Scripts/Core/EnemyVariantRules.cs`** (namespace `DeckArpg.Core`, pure, EditMode-tested):
```csharp
public enum EnemyVariant { Brute, Caster, Rusher }

public readonly struct VariantStats {
    public readonly int MovePercent, HpPercent, TelegraphPercent, RecoverPercent, PosturePercent;
}

public enum CasterMove { Advance, Hold, Retreat }

public static class EnemyVariantRules {
    public const float CasterPreferredRange = 7f, CasterRetreatRange = 4f, CasterMaxCastRange = 9f,
                       CasterRetreatSpeedFactor = 0.8f, CasterCorneredSeconds = 1.5f;
    public const int WaveCurrencyPercent = 125;
    public const float WaveSpawnDelaySeconds = 1.0f;

    public static VariantStats Stats(EnemyVariant v);
    /// Wave composition for a combat room. Order: Brutes first, then variants. Elite/Boss -> all Brute.
    /// Throws for non-combat rooms or count < 1. `rng` is used only by Green rooms.
    public static IReadOnlyList<EnemyVariant> Composition(in MapCard room, int count, System.Random rng);
    /// 0 for Low band, Elite and Boss; 1 for Mid; 2 for High.
    public static int WaveTwoCount(in MapCard room);
    public static CasterMove CasterMovement(float distance);
    public static bool CasterMayCast(float distance, float closeSeconds);
}
```

**Change `Core/RunContent.cs`:**
- Add `casterDeck` and `rusherDeck` lists, the `CasterDeck`/`RusherDeck` properties, and two new **required** params
  in `Create(...)` after `boss`.
- Add `EnemyDeckFor(MapCardKind kind, EnemyVariant variant)`, which returns `casterDeck` or `rusherDeck` for those
  variants and the existing per-kind deck for Brute. Keep the 1-arg overload as `=> EnemyDeckFor(kind, EnemyVariant.Brute)`.
- In `Validate`, check both new decks (no nulls, at least 4 cards, no 0-cards). Add them to `CheckUniqueIds` and to the
  enemy set that the draft pool must not overlap.

**Change `Core/RewardRules.cs`:** `Currency` multiplies by `WaveCurrencyPercent` (rounded half up, before the
Momentum bonus) when `EnemyVariantRules.WaveTwoCount(room) > 0`. Update the doc comment.

**Change `Editor/StarterContentBuilder.cs`:** add `BuildCasterDeck()` (`caster_bolt_3`, `caster_shard_4`,
`caster_bolt_5`, `caster_shard_6`) and `BuildRusherDeck()` (`rusher_jab_1_1`, `rusher_jab_2_2`, `rusher_jab_3_2`,
`rusher_jab_4_3`, `rusher_jab_5_3`, `rusher_jab_6_4`, following the Elite-deck id pattern). Pass both to `RunContent.Create`.

**Change `Gameplay/TrainingDummyEnemy.cs`** (still `sealed`; the boss and trials keep working):
- Add `public EnemyVariant Variant { get; private set; }` (default Brute).
- Add `public void ApplyVariant(EnemyVariant v)`. It scales `moveSpeed`, `telegraphSeconds` and `recoverSeconds` by
  `Stats(v)`. Call it once, right after spawning. It is multiplicative, so the order with `ApplyTempo` doesn't matter.
  It throws if called twice or if a `TurnHook` is set (the boss is always a Brute).
- In the `State.Approach` case, branch to a new `private void CasterApproach(Vector3 toTarget, float speed)` when
  `Variant == Caster`. It uses `CasterMovement`/`CasterMayCast` and tracks `closeTimer`. Retreat moves with
  `body.SimpleMove(-dir * moveSpeed * CasterRetreatSpeedFactor * speed)`. Chill (`speed`) slows it like other movement.
  The Brute/Rusher branch is the existing code, unchanged.
- Reset `closeTimer` in `Respawn` and `HandleDied`.

**Change `Gameplay/BodyTint.cs`:** add `public void SetBaseColor(Color c)`. It replaces each renderer's captured base
colour. With no layer active, the body shows the base colour through the property block instead of releasing it.
`Release()` and `OnDisable` keep it correct: a body with a base override re-applies it, and only a body without one
clears to null. Flash/Status/State layers still win over the base colour, and a lerp "from own colour" starts from
the base colour.

**Create `Gameplay/EnemyVariantLook.cs`** (`static class`): `Apply(TrainingDummyEnemy e, EnemyVariant v)` sets the base
tint and a non-uniform `localScale`:
- Caster: purple `(0.55, 0.30, 0.85)`, scale `(0.85, 1.2, 0.85)`.
- Rusher: amber `(0.95, 0.70, 0.20)`, scale `(0.8, 0.7, 0.8)`.
- Brute: unchanged.

Before finalizing, check both colours against the Fire/Burn and Lightning element colours so a variant doesn't read
as a status, and adjust if they clash. Set `DeathPop.Scale` to the variant's average scale.

**Change `Gameplay/RunDirector.cs`:**
- Split `SpawnEnemies` into `SpawnEnemies(room)` and `SpawnWave(in MapCard room, IReadOnlyList<EnemyVariant> mix)`.
  - `SpawnEnemies(room)` builds the composition and calls `SpawnWave`, then sets `wavesPending` (0 or 1) and
    `waveTwoCount`.
  - `SpawnWave` holds the existing per-enemy loop. Each enemy gets `EnemyDeckFor(kind, variant)`, its HP scaled by
    `HpPercent`, its posture by `PosturePercent`, then `ApplyVariant` and `EnemyVariantLook.Apply`.
  - Name format: `"{Kind} {Variant} Enemy {i+1}"`, keeping "Enemy" in the name. Casters spawn 2 u further from the
    player than their arc point (`+z`), clamped inside the arena.
  - Composition RNG: `new System.Random(NextSeed())`, drawn once per combat room before spawning.
- In `HandleEnemyDied`, when every enemy is dead and `wavesPending > 0`: set `wavesPending = 0`, then start a
  `WaveTwoRoutine`. The routine shows `SpawnRingFx` at the wave-2 spawn points for `WaveSpawnDelaySeconds` (scaled
  time), then aborts if `State != Fighting`, otherwise calls `SpawnWave`. Only when no wave is pending does it call
  `RoomCleared()`.
- `ClearEnemies` stops the routine and destroys any rings.
- New public members: `int WavesPending`, `event Action<int> WaveStarted` (wave index, 1 or 2), and
  `IReadOnlyList<EnemyVariant> CurrentMix`.

**Create `Gameplay/SpawnRingFx.cs`:** a flat ring/disc made with `FxShapes.CreateVisual` (the shared helper), pulsing
alpha, destroyed after its duration. No new assets.

**Change `UI/RunUiText.cs` `MapCardBlurb`:**
- Vanguard: `"{n} enemies, Rushers among them."`
- Hexfire: `"{n} enemies, Casters among them, stronger elemental cards."`
- Any combat room with a wave 2: add a line `"+{k} wave 2, currency +25%."` Keep it to 3 lines at most. Check in a screenshot.

**Docs:** the Gameplay CLAUDE.md architecture line is fine as is. In `docs/notes/CURRENT.md`, mark item 4 when done.

### Data flow
`ChooseDoor` -> `SpawnEnemies(room)` -> `EnemyVariantRules.Composition` -> `SpawnWave` (deck per variant, stats,
look) -> enemies' `Died` -> `HandleEnemyDied` -> (wave pending? ring + delay -> `SpawnWave`) -> `RoomCleared` ->
`Run.CompleteRoom` -> `RewardRules.Currency` (+25% if the room had a wave 2).

## Edge cases
- **The player dies or the run restarts during the 1 s wave delay.** The routine checks `State == Fighting` after the
  wait and does nothing otherwise. `StartRun`/`ChooseDoor` call `ClearEnemies`, which stops the routine and removes rings.
- **The last two wave-1 enemies die on the same frame.** `wavesPending` is cleared before the routine starts, so only
  one wave spawns.
- **A wave-1 corpse is destroyed before wave 2 spawns.** The list already tolerates null entries in `HandleEnemyDied`.
  Wave-2 enemies are appended to `enemies`.
- **A wave of 1 in Vanguard.** `floor(1/2) = 0` Rushers, so it's a Brute. In Hexfire, `ceil(1/2) = 1`, so it's a
  Caster. This is intended ("same rule").
- **A Caster is pinned against a wall.** The retreat makes no progress, `closeTimer` reaches 1.5 s, and it shoots
  point-blank. Its projectile spawns at melee range and can still be clashed or parried.
- **The player stays more than 9 u from a Caster.** It advances; it never shoots from beyond 9 u.
- **A chilled Caster.** Its retreat and advance are slowed by `SpeedMultiplier`, like every other movement.
- **A Caster loses a mid-air clash.** It is stunned and shoved at range (existing `Break`). All its live hitboxes are
  cancelled. Intended.
- **A projectile ties with a player card.** The existing Deadlock/neutral path handles it, unchanged.
- **The Rusher's value-1 Jab.** It is a normal card. `IsPoke` is a payload flag set only by the poke action, not by
  value. A `+1` room bonus clamps within [1, 9]. A 0-parry still breaks it.
- **`ApplyVariant` on the boss or twice.** It throws `InvalidOperationException`. RunDirector never does this.
- **The live-count stun scaling with waves.** `LiveCount` counts only living enemies, so it already handles waves.
- **Composition for count < 1 or a non-combat room.** It throws `ArgumentOutOfRangeException`/`ArgumentException`.
- **Seed stream change.** One extra `NextSeed()` per combat room shifts later seeded rolls. If a PlayMode test pins an
  exact seeded reward, re-derive the expected value and explain it in the commit. Never loosen the assertion.

## Steps (one commit each, on branch `feat/enemy-variants`)
1. **Core rules.** Add `EnemyVariantRules.cs` and EditMode tests `EnemyVariantRulesTests`: composition per room
   kind/count (incl. n=1,2,3,4 and Green with two seeds giving both variants), `WaveTwoCount` per band/kind, stats
   table, `CasterMovement`/`CasterMayCast` at the boundaries 3.99/4/7/7.01/9/9.01 and the cornered timer at 1.49/1.5.
   Run EditMode.
2. **Currency.** Add the `RewardRules.Currency` wave bonus and tests (I3 Vanguard unchanged, I4 +25%, Elite unchanged).
   Run EditMode.
3. **Content.** Add the `RunContent` decks, the overload and validation, update `RunContentTests` (the new
   `Create` params, variant deck lookup, and validation errors for the new decks), and the `StarterContentBuilder` decks. Close
   the Editor, run the content rebuild through `-executeMethod` (as in `docs/specs/heal-rework.md`), and commit the
   regenerated assets. Run EditMode.
4. **Enemy behaviour.** Add `TrainingDummyEnemy.ApplyVariant` and the Caster approach. PlayMode tests, building enemies
   at runtime as the existing tests do (`minMoveDistance = 0`): a Caster starting 2 u from the player moves away; a
   Caster at 12 u moves closer and doesn't telegraph; a Caster at 6 u telegraphs and its swing spawns a projectile
   hitbox; a cornered Caster (wall behind) telegraphs within about 1.5 s plus its timer; a Rusher's speed and timings
   equal the Brute's x the stats. `ApplyVariant` throws when a `TurnHook` is set. Run PlayMode.
5. **Mid-air clash.** PlayMode test: a player Strike/Throw hitbox with a higher value meets a Caster projectile mid-air,
   the projectile is cancelled and the Caster is stunned. Also test the reverse (the player's card is lower, so the
   player is broken). Run PlayMode.
6. **Look.** Add `BodyTint.SetBaseColor` (tests: base shows with no layer; a Flash wins over it then returns to base, not
   to the material colour) and `EnemyVariantLook`. Run PlayMode.
7. **RunDirector waves and mix.** Add composition spawning, deck/HP/posture per variant, the wave routine,
   `SpawnRingFx`, and the public members. PlayMode tests in `RunLoopTests`:
   - a Hexfire room at I3 spawns 1 Caster + 1 Brute and no wave;
   - a Vanguard room at I5 spawns 2 Rushers + 2 Brutes; killing all of them does **not** clear the room, and after
     about 1 s it spawns 1 more enemy;
   - killing wave 2 clears the room and pays +25%;
   - dying during the wave delay spawns nothing;
   - `ChooseDoor`/`StartRun` during the delay leaves no rings or extra enemies;
   - Elite/Boss rooms are unchanged.

   Update any existing test that relied on the old enemy name or all-Brute rooms (find them with
   `grep -rn "Enemy 1\|EnemyDeckFor\|MobDeck" Assets/_Project/Tests`). Run EditMode + PlayMode.
8. **UI blurb.** Update `RunUiText.MapCardBlurb` and its EditMode tests. Capture the map-card and in-room screenshots with
   `HudScreenshotCapture` (add a `CaptureVariants` case: a Hexfire I7 room with Casters and Brutes visible) and inspect
   `TestResults/screens/`. Check that the tints and scales read clearly and that the overhead `EnemyTelegraphView` sits
   above the tall Caster.
9. **Full verification and notes.** Run both suites in full, grep the logs for `error CS`, and update `docs/notes/CURRENT.md`.

## Verification
Close the Unity Editor first (`tasklist //FI "IMAGENAME eq Unity.exe"`), then from the repo root in Git Bash:
```bash
timeout 1200 "/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform EditMode -testResults "$(pwd -W)/TestResults/editmode.xml" -logFile "$(pwd -W)/TestResults/editmode.log"
timeout 1200 "/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform PlayMode -testResults "$(pwd -W)/TestResults/playmode.xml" -logFile "$(pwd -W)/TestResults/playmode.log"
grep "<test-run" TestResults/editmode.xml TestResults/playmode.xml
grep -c "error CS" TestResults/*.log   # 0 expected (exit 1 on a zero count is not a failure)
# screenshots (no -nographics):
timeout 1200 "/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -projectPath "$(pwd -W)" -runTests -testPlatform PlayMode -testFilter DeckArpg.Tests.PlayMode.HudScreenshotCapture -testResults "$(pwd -W)/TestResults/screens.xml" -logFile "$(pwd -W)/TestResults/screens.log"
```
Done when: both suites have exit 0 and 0 failures (baseline EditMode 917, PlayMode 416 passed / 12 skipped, plus the new
tests), there are 0 `error CS` lines, the variant screenshots have been inspected, and the user has done a manual
playtest of one Hexfire room and one Vanguard I5+ room. The user's feel decides the tuning; the stats table is the
knob.
