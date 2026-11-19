# M4 overview - Boss AI & juice

Status: approved decisions from the 2026-11-16 interview. Master plan for milestone 4 (same pattern as
`m3-overview.md`): each large step (3, 4, 7) gets its own sub-spec via `/spec` when it starts. Numbers here are
the source of truth for M4 tuning.

## Goal
Turn the placeholder boss (a scaled `TrainingDummyEnemy` with the Warden deck) into a fight with real counterplay.
The boss plays by player rules: a 3-card hand, a draw queue, telegraphed Sleights, values clamped to [1, 9]. It
has a punishable reload, and parrying enough of its cards earns a 4 s Exhausted stagger. Add the juice that makes it
readable and satisfying: boss telegraph/state FX, a boss HUD, a player draw-order strip and procedural SFX.
Target: a decent player beats the boss in **90-150 s**.

## Decisions (user, 2026-11-16)
- **Mill = stagger, not a win.** 0-deleted boss cards come back on its reload. HP is the only win condition.
- **Reload window vs Exhausted (delete threshold).** Every boss cycle end opens a **1.5 s reload window**
  (no flinch armor, takes **1.5x posture damage**, doesn't attack). If **>= 4 cards were 0-deleted this cycle**,
  it is a **4 s Exhausted** stagger instead (stunned, takes **2x HP damage and 2x posture damage**).
- **Boss Sleights are pattern-based for now:** every **3rd attack** it stocks its hand instead of swinging. The user
  wants this to "feel more natural at some point", so the decision sits behind an `IBossAttackPolicy` seam.
- **Boss 9s: fixed deck + spacing.** The boss ignores the Intensity card-value bonus. The deck has **three 9s of 15**,
  and its shuffle never puts two 9s next to each other in play order (including across a reload).
- **Answering a boss Sleight (all four):**
  1. **0 after activation:** a global clash. A player 0 played within **0.5 s after the Sleight activates** breaks it at
     any range and 0-deletes all 3 of its cards.
  2. **Higher player Sleight:** a player Sleight that activates during the boss wind-up or within that 0.5 s window
     clashes globally. A higher sum breaks the boss Sleight; a lower sum gets the player's Sleight broken.
  3. **Dash / i-frames** always works (no clash).
  4. **Equal sums -> Big Deadlock ("winner takes all").** Slow-mo timing ring with a **1.5 s window**. Player wins:
     their Sleight lands with **both sums combined**, the boss goes straight to Exhausted, and an arena-wide
     shockwave plays. Boss wins: a heavy hit that is **non-lethal** (the player keeps >= 1 HP), plus knockback.
     Tie: both Sleights shatter with an arena flash.
- **Boss HUD (boss only):** top bar with name, HP, posture, the 3 hand cards face-up, a 15-pip deck counter
  (left / spent / deleted / burned / stocked) and a `RELOADING` / `EXHAUSTED` label. The queue order is **not** shown.
- **Player draw-order strip:** always shows the full draw order (compact, consumed left to right), redrawn on every
  draw / reload / shuffle.
- **Juice in scope:** boss telegraph FX, boss reload/Exhausted FX, and an SFX pass with **procedural synth** clips
  (no audio assets, no new packages).
- **Done when:** tests + reviewed screenshots + a manual boss playtest checklist pass.

## Tuning changes after the bot measurements (user, 2026-11-17)
`BossFightMeasurement` (an `[Explicit]` PlayMode fixture) plays 6 seeded runs to the boss and lets `BossFightBot` (scripted
"decent player": 0.15 s reactions, imperfect timing, 0-parries, telegraph answers, all-in during punish windows) fight it
in real time. Results land in `TestResults/bossfight.txt`.

| Setup | Bot result | Fight time | Notes |
| --- | --- | --- | --- |
| 630 HP, no guard | 4 wins / 6 | 16-23 s | Card spam (~30 dmg/s) killed the boss before its first reload: 0 reload windows, 0 Exhausted. |
| 630 HP, guard 35% | 4 / 6 | 27-32 s | Every win came in the first Exhausted. |
| 1710 HP, guard | 0 / 6 | 27-59 s | The bot (100 HP) lasts ~40 s at full boss damage. |
| 1710 HP, guard, boss deals 50% | 1 / 6 | 41-71 s | Losses end with the boss at 551-956 HP. |
| 900 HP, guard, boss deals 50% | 5 / 6 | 33-48 s | Every fight had one Exhausted; the loss ended with the boss at 146. |
| 630 HP, guard, boss deals 50% | 6 / 6 | 31-36 s | Every win came in the first Exhausted: below this, fight length is set by how fast the boss reaches Exhausted, not by HP. |
| **150 HP (1.5x player), guard, boss deals 50% (current)** | 6 / 6 | 13-22 s | The boss dies before its first cycle ends: 0 reload windows, 0 Exhausted (the counterplay never shows for the bot). Bot ends at 68-100 HP. |

Decisions from these results:
- **Guard:** the boss takes 35% HP damage outside its punish windows, 100% in the reload window, 200% when Exhausted
  (`BossRules.GuardDamageMultiplier`). HUD label `GUARDED`. The Big Deadlock's combined hit ignores the guard.
- **Boss damage dealt x0.5** (`BossRules.DamageDealtMultiplier`, stamped on its hitboxes via
  `CombatActionController.OutgoingDamageScale` / `AttackPayload.DamageScale`). Card values and clashes are unchanged.
- **Boss HP = 1.5x the player's max HP on floor 1** (150 against 100; `BossRules.MaxHp(floor, playerMaxHp)`,
  `RunState.Floor`). History: 1900% (1710) from bot data -> 1000% (900) -> 700% (630), each "too long" in the user's
  playtests. User (2026-11-18): give the boss "a similar amount as the player but a bit more so it feels like a challenge
  but not a pushover"; the first-floor boss mustn't make players rage-quit; difficulty ramps on later floors. Roguelike
  comparison (Slay the Spire, Hades, Enter the Gungeon, Dead Cells): the first boss is a short skill check that teaches
  its mechanic; the ramp comes from later floors/acts and opt-in difficulty (Ascension, Heat). `HpRatioByFloor` holds one
  row per floor, so later floors add rows. The boss no longer uses the room HP percent.
  **Confirmed by user playtest (2026-11-18): the length feels good for floor 1.** Future (out of M4 scope): on later
  floors, scale the boss's variance (deck, AI, HP) alongside the player's progression, not HP alone.
- **Natural boss AI** (`NaturalBossPolicy`, used in runs; the pattern policy stays the component default for tests):
  base 15% Sleight chance, +35% if the player has no 0 ready, +30% if the player is reloading or stunned, +4% per hand-sum
  point above 15 (max +30%), +10% per attack beyond 3 since the last Sleight, capped at 90%; never twice in a row; needs a
  full hand. Seeded from the boss deck seed.
- **Open:** the bot isn't a human. It deals damage faster than a person likely would (longer human fights) and defends
  worse (shorter ones), so fight length and difficulty are decided in the manual playtest below.

## Assumptions (not asked; change in a sub-spec if wrong)
- The boss has **no 0-card** of its own (it never parries). Its "player rules" are hand, queue, Sleights,
  Floor of 3, burns and clamping.
- A boss Sleight follows the player's burn rule: slot 1 burns at fire (Floor of 3 applies). The exception is a
  0-break, which turns that burn into a delete (see `DeckCycle.DeleteSleight`).
- During a Sleight wind-up the boss moves its 3 hand cards into its stock (visible on the HUD) and refills its hand.
  It fires at the end of the wind-up. A boss Sleight wind-up is **1.5 s** (normal telegraph: 0.7 s / tempo).
- Single player cards hitting the boss during a *Sleight* wind-up use the normal telegraph-clash rule against the sum,
  so they always lose (break). Only a player Sleight can clash a wind-up.
- `0`s still never clash telegraphs (2026-11-14 decision). Against a boss Sleight a 0 works only *after* activation.
- Boss HP/posture start from today's boss numbers and are tuned in step 8 to hit 90-150 s (see the tuning section above).

## Out of scope
- A "natural" boss decision AI (hand-value or phase-based). Only the `IBossAttackPolicy` seam and the pattern policy.
- Permanent mill / mill-as-win, a boss 0-card, multiple bosses or boss phases.
- Showing the boss's queue order. Boss-style HUD or deck visibility for mobs or Elites.
- Floating damage numbers: approved later, see `docs/specs/hit-juice.md`.
- Imported audio assets, music, an audio mixer/settings menu.
- New art/models: the boss stays greybox (scaled capsule plus a tint).
- Changing Elite trials, relics, rewards or the Map Card loop (except `RoomIntensity` for the boss bonus).

## Design

### Core (`Assets/_Project/Scripts/Core`, `DeckArpg.Core`)
- **`BossRules.cs` (new)**: constants plus pure decisions.
  ```csharp
  public static class BossRules {
      public const float ReloadWindowSeconds = 1.5f, ExhaustedSeconds = 4f;
      public const int   ExhaustDeleteThreshold = 4;
      public const float ReloadPostureMultiplier = 1.5f, ExhaustedDamageMultiplier = 2f;
      public const float SleightWindupSeconds = 1.5f, GlobalZeroWindowSeconds = 0.5f;
      public const float BigDeadlockWindowSeconds = 1.5f;
      public static BossCycleEnd CycleEnd(int deletedThisCycle);   // ReloadWindow | Exhausted
      public static int NonLethalDamage(int damage, int currentHp); // min(damage, currentHp - 1), >= 0
  }
  public enum BossCycleEnd { ReloadWindow, Exhausted }
  ```
- **`BossAttackPolicy.cs` (new)**: the seam for later "natural" AI.
  ```csharp
  public enum BossAction { Swing, Sleight }
  public readonly struct BossTurnContext { public readonly int AttacksSoFar; public readonly int HandCount;
      public readonly float HpFraction; /* ctor */ }
  public interface IBossAttackPolicy { BossAction Next(in BossTurnContext ctx); }
  public sealed class PatternBossPolicy : IBossAttackPolicy { public PatternBossPolicy(int every = 3); ... }
  ```
  Pattern: attack #3, #6, ... is a Sleight when `HandCount == 3`; otherwise it swings.
- **`CardSpacing.cs` (new)**: `public static void SpreadApart(List<RuntimeCardInstance> order,
  Func<RuntimeCardInstance, bool> spaced, bool previousWasSpaced, System.Random rng)` reorders a shuffled list so
  no two `spaced` cards are adjacent (and the first isn't one if `previousWasSpaced`). Deterministic with the rng.
  When it's impossible (too many), it spaces as many as it can.
- **`DeckCycle.cs` (change)**:
  - `public IEnumerable<RuntimeCardInstance> DrawOrder` gives a read-only peek at the queue (next card first),
    for the strip.
  - `public Func<RuntimeCardInstance, bool> SpacingRule { get; set; }`. When set, `ShuffleInto` calls
    `CardSpacing.SpreadApart` after Fisher-Yates (tracks the last played card for the reload boundary).
  - `public int DeleteSleight(IReadOnlyList<RuntimeCardInstance> cards)` moves each Sleight card from discard
    **or the burned pile** into the deleted-this-cycle list and returns the count. Used by the global 0-break.
- **`RoomIntensity.EnemyCardValueBonus` (change)**: returns `0` for `MapCardKind.Boss`.

### Content (`Scripts/Editor/StarterContentBuilder.cs`)
- `BuildBossDeck()` retuned to `{4,4,5,5,6,6,6,7,7,7,8,8,9,9,9}` (three 9s). Rebuild `RunContent.asset` with
  `DeckArpg/Build Run Content`. `RunContent.Validate()` keeps the 15-card check.

### Gameplay (`Scripts/Gameplay`, `DeckArpg.Gameplay`)
- **`TrainingDummyEnemy.cs` (minimal hook)**: `public IEnemyTurnHook TurnHook { get; set; }`. In `Update`,
  `if (TurnHook != null && TurnHook.IsBusy) return;` (after the knockback/stun lines). In `BeginTelegraph`, the
  hook may take the turn (`TurnHook.TryTakeTurn()` -> true = handled), and the out-of-cards branch calls
  `TurnHook.OnOutOfCards()` instead of reloading instantly. Mobs and Elites have no hook, so their behaviour is
  unchanged. Also add `public event Action<RuntimeCardInstance> Swung` (for SFX and the boss counter).
- **`IEnemyTurnHook.cs` (new)**: `bool IsBusy { get; } bool TryTakeTurn(); void OnOutOfCards();`
- **`BossEnemy.cs` (new)**: a MonoBehaviour next to `TrainingDummyEnemy`, implementing `IEnemyTurnHook`.
  - States: `Idle` (dummy drives) / `SleightWindup` / `ReloadWindow` / `Exhausted`.
  - `public BossState State`, `public float StateRemaining`, `public int AttackCount`,
    `public SleightResult? PendingSleight`, `public IBossAttackPolicy Policy`.
  - Events: `SleightWindupStarted(int sum)`, `SleightFired(AttackHitbox)`, `CycleEnded(BossCycleEnd)`,
    `ExhaustedStarted`, `RecoveredFromStagger`.
  - `public void ForceExhausted()` is for the Big Deadlock win. It doesn't reload; at the end of the stagger,
    the boss reloads only if its queue is empty.
  - Sets `deck.Cycle.SpacingRule = c => c.GetEffectiveValue() == 9`.
  - Tests get `internal` force methods (`ForceSleight()`, `ForceCycleEnd()`), the same pattern as `ForceTelegraph`.
- **`CombatantStatus.cs` (change)**: `public float DamageTakenMultiplier { get; set; } = 1f;` and
  `public float PostureTakenMultiplier { get; set; } = 1f;`, applied in `TakeHit`/`AddPostureDamage`.
  `BossEnemy` sets and resets them.
- **`BossSleightClash.cs` (new)**: the global HUD clash. Watches the player's `CombatActionController.HitboxSpawned`.
  During a boss wind-up, a player Sleight resolves against the anticipated boss Sleight via `ClashSystem`. During
  the 0.5 s post-activation window, a player 0 or Sleight resolves against the live boss Sleight at any range.
  On a 0-break it calls `DeckCycle.DeleteSleight`. On equal sums it starts the Big Deadlock.
- **`DeadlockController.cs` / `DeadlockRules.cs` (change)**: a Big variant:
  `internal bool TryBeginBig(AttackHitbox player, AttackHitbox boss, Vector3 pos)` with
  `DeadlockRules.BigWindowSeconds = 1.5f` (the target time scales with it), and
  `public bool IsBig { get; }` on `DeadlockResult`. Resolution follows "winner takes all" above.
  Slow-mo goes through the existing single `HitStop`/time-scale owner (see the Lessons learned in CLAUDE.md).
- **`RunDirector.cs` (change)**: for `MapCardKind.Boss`, add `BossEnemy` to the spawned enemy and expose
  `public BossEnemy Boss { get; }` + `public event Action<BossEnemy> BossSpawned`.
- **`AudioService.cs` (new)**: one per scene. It builds `AudioClip`s from `SfxSynth` samples at startup, uses a
  small `AudioSource` pool, and adds ±5% pitch variance. `public void Play(SfxId id, Vector3? at = null)` and
  `public event Action<SfxId> Played` (for tests). Subscribes to `ClashSystem.ClashOccurred`,
  `DeadlockController.Resolved`, the reload result, dash, `CombatantStatus.HitTaken`, and the `BossEnemy` and
  `RunDirector.RunEnded` events. It never throws without an audio device (batchmode): it logs once and still raises
  `Played`.
- **`SfxSynth.cs` (Core, new)**: pure `float[] Generate(SfxId id, int sampleRate)` (noise bursts, pitched blips,
  sweeps, with envelopes). `public enum SfxId { ClashNeutral, CardBreak, ZeroBreak, Swing, Hit, Dash,
  ReloadPerfect, ReloadMiss, BossSleightWindup, BossSleightFire, BossReload, BossExhausted, BigDeadlock, Victory }`.
- **`BossTelegraphFx.cs` (new)**: a ground warning ring (radius = attack range) and a large overhead card during
  boss wind-ups. Sleight wind-ups use a distinct color plus a sum label. Unscaled time, like `ClashImpactFx`.
- **`BossStateFx.cs` (new)**: a tint, orbiting card quads and a countdown ring for the reload window/Exhausted,
  plus a burst when it ends.

### UI (`Scripts/UI`, `DeckArpg.UI`)
- **`DrawQueueStripView.cs` (new)**: a compact chip row under the hand HUD. Each chip shows the value plus the
  element color, with a Premium mark. The next card is leftmost. It redraws on `DeckCycle.Changed`. Chips shrink to
  a minimum width, then the strip shows `+N`. An empty queue shows `Reload`. Canvas sorting stays below 30 (enforced
  by an existing test).
- **`BossHudView.cs` (new)**: the top bar from the decisions. `Bind(BossEnemy)` happens on
  `RunDirector.BossSpawned`, and it's hidden when there's no boss. It's hidden behind run screens
  (`RunScreens.CombatHudHidden`).
- **`RunUiText.cs` (change)**: pure helpers for the counter/label text (EditMode-tested).
- **`Editor/GreyboxArenaBuilder.cs` (change)**: adds the new views and `AudioService` to `M3_Run` (and the strip to
  `M1_Arena`). Rebuild with `-executeMethod DeckArpg.Editor.GreyboxArenaBuilder.BuildRunScene -quit`.

### Data flow (boss turn)
`TrainingDummyEnemy.Update` -> ready to attack -> `BossEnemy.TryTakeTurn()` -> `Policy.Next(ctx)`:
- **Swing:** returns false, so the dummy telegraphs and swings as today (spacing keeps 9s apart).
- **Sleight:** stocks 3 hand cards -> `SleightWindup` (1.5 s, `BossTelegraphFx` + HUD) -> `CombatActionController.SpawnSleight`
  -> `BossSleightClash` opens the 0.5 s global window -> the hit lands or is answered.

Out of cards -> `OnOutOfCards()` -> `BossRules.CycleEnd(DeletedThisCycleCount)` -> `ReloadWindow` (1.5 s) or
`Exhausted` (4 s) -> `Cycle.Reload()` -> the dummy resumes in Recover.

## Edge cases
| Case | Behaviour |
| --- | --- |
| Sleight turn but fewer than 3 cards in hand (end of cycle) | The policy returns Swing. With 0 cards it's the cycle end. |
| Boss deck down to the Floor of 3 from Sleight burns | Sleights stop burning slot 1 (player rule). The boss keeps playing. |
| Boss is broken during a Sleight wind-up (higher player Sleight) | `InterruptStock` (slot-1 burns unless at the Floor), stun, back to Recover. |
| Player 0 during a boss Sleight wind-up | No effect (0s never clash telegraphs). |
| Player 0 more than 0.5 s after activation | No global clash. Local hitbox rules apply per hit (multi-hit resolves per hit). |
| Two player answers inside the window | The first one resolves. The boss Sleight is gone, and the second plays normally. |
| 0-break of a boss Sleight whose slot 1 already burned | `DeleteSleight` pulls it out of the burned pile: all 3 count as deleted. |
| Deletes reach the threshold mid-cycle | Nothing happens until the cycle ends. Then it's Exhausted. |
| Big Deadlock won by the player while the boss queue still has cards | Exhausted with no reload. After it, the boss continues its current cycle. |
| Big Deadlock lost at low HP | Damage = `BossRules.NonLethalDamage`. The player survives with >= 1 HP. |
| Boss dies during Exhausted, a wind-up or a Big Deadlock | Instant victory. The deadlock aborts, time scale and multipliers are restored, and FX stop. |
| Player dies during the boss reload window | Normal defeat. Boss state is irrelevant. |
| 9 spacing across a reload | The first card after a reload isn't a 9 if the last card played was a 9. |
| Spacing impossible (too many 9s after a forge/mod) | Best effort, no exception (only reachable in tests). |
| A room mod would raise boss cards | The boss bonus is 0. Values are still clamped to [1, 9]. |
| No audio device / batchmode | `AudioService` logs once, never throws, and still raises `Played`. |
| Empty / huge player queue | The strip shows `Reload` / shrinks then shows `+N`. |
| A run screen opens during the boss fight | The boss HUD and strip hide with the rest of the combat HUD. |
| Overlapping freezes (hit-stop during the Big Deadlock slow-mo) | One time-scale owner. The saved scale is never 0 (CLAUDE.md lesson). |

## Steps
0. **Branch.** Merge the `chore/playmode-test-rules` PR (user), then
   `git checkout main && git pull && git checkout -b feat/m4-boss-juice`. Commit this spec first.
1. **Core boss rules + deck** (EditMode): `BossRules`, `IBossAttackPolicy`/`PatternBossPolicy`, `CardSpacing`,
   `DeckCycle.DrawOrder`/`SpacingRule`/`DeleteSleight`, boss bonus 0, deck retune + `Build Run Content`.
   Tests: thresholds, the pattern every 3rd attack, no adjacent 9s over 1000 seeds (incl. the reload boundary),
   `DeleteSleight` from the burned pile, `NonLethalDamage`.
2. **Player draw-order strip** (independent quick win): `DrawQueueStripView` + scene builder.
   PlayMode: chips match `DrawOrder` after a play, a reload and a shuffle. Screenshot.
3. **Boss body** (sub-spec `m4-boss-ai.md`): `IEnemyTurnHook`, `BossEnemy` (Sleight wind-up/fire, reload window,
   Exhausted), `CombatantStatus` multipliers, the `RunDirector` boss wiring. PlayMode: the state sequence, 4 deletes ->
   Exhausted, 3 -> reload window, multipliers set and reset, mobs unchanged.
4. **Global boss-Sleight clash** (sub-spec `m4-boss-clash.md`): `BossSleightClash`, Big Deadlock.
   PlayMode: all four answers, the 0.5 s window edges, a non-lethal loss, a win -> Exhausted, a tie -> both shatter.
5. **Boss HUD**: `BossHudView` + `RunUiText` helpers. EditMode text tests, a PlayMode bind/hide test, a screenshot.
6. **Boss FX**: `BossTelegraphFx`, `BossStateFx`. PlayMode: FX objects appear/disappear with states, nothing is
   left behind after death. Screenshot.
7. **SFX pass** (sub-spec `m4-sfx.md`): `SfxSynth` (EditMode: non-empty, no NaN, peak <= 1, deterministic),
   `AudioService` (PlayMode: the right `SfxId` per event via `Played`; no throw in batchmode).
8. **Tune + verify**: boss HP/posture tuned for 90-150 s. Extend `FullRunSmokeTests` so the boss room spawns a
   `BossEnemy`. Full test runs, a screenshot review, the user playtest checklist below. Update `docs/notes/CURRENT.md`.

Each step: run both test platforms, then commit (`feat: ...` / `test: ...`). Keep commits small.

## Verification
Close the Editor first. From the repo root (Git Bash):
```bash
U="/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe"
timeout 1200 "$U" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform EditMode \
  -testResults "$(pwd -W)/TestResults/editmode.xml" -logFile "$(pwd -W)/TestResults/editmode.log"; echo "exit $?"
timeout 1200 "$U" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform PlayMode \
  -testResults "$(pwd -W)/TestResults/playmode.xml" -logFile "$(pwd -W)/TestResults/playmode.log"; echo "exit $?"
grep -m1 "<test-run" TestResults/editmode.xml TestResults/playmode.xml
grep -c "error CS" TestResults/*.log   # expect 0
```
Pass = exit 0 on both, with the `<test-run>` lines showing `failed="0"`. Baseline before M4: EditMode 597, PlayMode
227 passed / 2 skipped.

Screenshots (with graphics, run alone):
```bash
timeout 1200 "$U" -batchmode -projectPath "$(pwd -W)" -runTests -testPlatform PlayMode \
  -testFilter DeckArpg.Tests.PlayMode.HudScreenshotCapture -testResults "$(pwd -W)/TestResults/screens.xml"
```
Add boss-HUD, boss-Exhausted and queue-strip shots, then review them in `TestResults/screens/`.

**Manual boss playtest checklist (user, `M3_Run`, any seed):**
- [ ] The draw-order strip matches the cards I actually draw, before and after a reload.
- [ ] Boss 9s show up in most cycles and never twice in a row.
- [ ] I see and punish a boss reload window (1.5 s, more posture damage).
- [ ] I reach Exhausted by 0-parrying 4+ boss cards in one cycle.
- [ ] I answer a boss Sleight with a 0 after activation, with a higher Sleight, and with a dash.
- [ ] (If it comes up) a Sleight tie triggers the Big Deadlock, and it feels destructive.
- [ ] The boss telegraph ring, Sleight sum and the RELOADING/EXHAUSTED states are readable without the HUD.
- [ ] Every clash/break/reload/boss event has a sound, with none missing or doubled.
- [ ] The boss fight takes 90-150 s.
