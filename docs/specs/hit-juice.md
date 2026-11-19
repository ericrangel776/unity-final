# Hit juice (fun pass, item 1)

Branch: `feat/hit-juice` (from `main`). Source: `docs/notes/CURRENT.md` -> Next steps 1 and 6.

## Goal
Right now only clashes feel good. Hit-stop, shake and sparks fire only on `ClashSystem.ClashOccurred`. A plain damaging
hit gets only a white `HitFlash` and a SFX, and a dead mob stands still for 2 s and then vanishes. This pass gives every
damaging body hit weight: a short freeze, element-coloured sparks, floating damage numbers, a camera kick when the player
is hurt, and a death pop for mobs. It reuses the existing systems (`HitStop` owns `Time.timeScale`, Cinemachine impulse,
the unscaled-time particle setup).

## Out of scope
- Element *mechanics* (burn, chill, chain, posture) and new elements. That is item 2. Here, element is only a colour.
- New SFX. The existing `SfxId.Hit` stays as the only hit sound.
- Imported art or audio assets. Everything stays procedural or built at runtime, like `ClashImpactFx`.
- Changes to clash juice (`ClashImpactFx`, `CameraPunch`, `ClashFeedbackView`) or to clash hit-stop durations.
- Player death FX (Defeat already has its own screen) and boss-specific death cinematics.
- Hit-stop on pokes. Pokes get sparks and a number, but no freeze.
- Tuning balance numbers (damage, HP).

## Decisions (from the interview, 2026-11-18)
| Topic | Decision |
|---|---|
| Hit-stop | Applies both ways. The player's card hits on enemies freeze 0.03-0.05 s, scaled by value; Sleights use the 0.05 cap. Enemy hits on the player freeze a flat 0.04 s and add a camera impulse. Pokes never freeze. |
| Damage numbers | Shown on every damaging hit, pokes included. On enemies the number takes the attack's element colour; on the player it is red. Size grows with damage. A counter shows the boosted damage, and the existing `COUNTER!` popup is kept. The number rises with a sideways drift and fades over ~0.7 s. Labels come from a pool. |
| Death pop | A particle burst in the killing hit's element colour, a small impulse, and the body squashing to nothing over ~0.15 s. The root is destroyed on the existing timer. The burst is larger for Elites and larger still for the boss. |
| Done when | PlayMode tests for each trigger, `HudScreenshotCapture` shots of the numbers, and **a playtest sign-off by the user** on the feel. |

## Design

### Core (EditMode-testable): `Assets/_Project/Scripts/Core/HitJuiceRules.cs` (new)
```csharp
namespace DeckArpg.Core
{
    public static class HitJuiceRules
    {
        public const float MinCardHitStop = 0.03f;   // value 1
        public const float MaxCardHitStop = 0.05f;   // value 9 and every Sleight
        public const float PlayerHurtHitStop = 0.04f;
        public const float DamageNumberMinScale = 1f;
        public const float DamageNumberMaxScale = 2f;
        public const int DamageNumberFullScaleDamage = 60;

        /// Freeze for a damaging body hit. 0 for pokes, zero-breakers and damage <= 0.
        /// victimTeam == Player -> PlayerHurtHitStop. Sleight -> MaxCardHitStop.
        /// Otherwise lerp(Min, Max, (Value - 1) / 8), with Value clamped to [1, 9].
        public static float BodyHitStopSeconds(in AttackPayload payload, Team victimTeam, int damage);

        /// Label scale: 1 + clamp01(damage / 60), so 1..2. Returns 1 for damage <= 0.
        public static float DamageNumberScale(int damage);
    }
}
```
Every value stays under the 0.2 s input buffer, as `CombatMath.HitStopSeconds` already does.

### Hit event: `Assets/_Project/Scripts/Gameplay/CombatantStatus.cs` (change)
The existing events don't carry enough data. `AnyHitTaken` has no payload or position, and `HitTaken` is not raised on a
lethal hit. Add:
```csharp
public readonly struct HitInfo
{
    public readonly CombatantStatus Victim;
    public readonly AttackPayload Payload;
    public readonly int Damage;        // final damage after multipliers/counter/non-lethal cap
    public readonly bool WasCounter;
    public readonly bool Killed;
    public readonly Vector3 Position;  // victim.position + Vector3.up * 1f
}
/// Any combatant took a damaging body hit (damage > 0), lethal hits included. Raised after HP changes, BEFORE Died.
public static event Action<HitInfo> AnyHitLanded;
/// Element of the last damaging hit taken (for the death pop colour). Physical until hit.
public Element LastHitElement { get; private set; }
/// Raised by Revive(), so FX can restore visuals on M1-arena respawn.
public event Action Revived;
```
In `TakeHit`, when `damage > 0`, set `LastHitElement = payload.Element` and raise `AnyHitLanded` right after the existing
`AnyHitTaken` call and before the `Hp == 0` / `Died` branch. Leave `AnyHitTaken`, `HitTaken` and `Damaged` unchanged.
`SpendHp` raises nothing, because it isn't a hit. `Revive()` raises `Revived`.

### Hit-stop: `Assets/_Project/Scripts/Gameplay/HitStop.cs` (change)
In `OnEnable`/`OnDisable`, also subscribe to `CombatantStatus.AnyHitLanded`. The handler calls
`Freeze(HitJuiceRules.BodyHitStopSeconds(info.Payload, info.Victim.Team, info.Damage))`. No other change: `HitStopTimer`
already extends overlapping freezes instead of stacking them, so the body hit from a breaking swing during a 0.12 s clash
freeze adds nothing, and each hit of a multi-hit Sleight only extends the freeze.

### World FX: `Assets/_Project/Scripts/Gameplay/HitImpactFx.cs` (new)
`[RequireComponent(typeof(CinemachineImpulseSource))]`. It lives on its own child `HitFx` under the `ClashFx` object so it
never shares a `GetComponent<CinemachineImpulseSource>()` with `ClashImpactFx`. It reuses
`ClashImpactFx.CreateParticleMaterial()`, `ConfigureAdditive` and the same unscaled-time particle settings. The small
particle-system builder can be copied or made `internal static`; don't refactor `ClashImpactFx` beyond that.
- `AnyHitLanded` -> emit sparks at `info.Position` coloured `Opaque(ElementColors.For(info.Payload))`, 6 for a poke and 10
  for a card or Sleight. If `info.Victim.Team == Team.Player`, call `impulse.GenerateImpulseAtPositionWithVelocity(pos,
  Random.onUnitSphere * PlayerHurtShake)` with `PlayerHurtShake = 0.3f`.
- Public read-only state for tests: `int LastSparkCount`, `Color LastSparkColor`, `int PlayerHurtImpulses` (a counter).
- Serialized `Material particleMaterial`, set by the scene builder like `ClashImpactFx`.

### Death pop: `Assets/_Project/Scripts/Gameplay/DeathPop.cs` (new)
`[RequireComponent(typeof(CombatantStatus))]`. It goes on the enemy prefab only; the player's death is out of scope.
- `public float Scale { get; set; } = 1f`. `RunDirector` sets it at spawn: Elite 1.6, Boss 2.5, otherwise 1.
- On `status.Died`, burst `Mathf.RoundToInt(24 * Scale)` particles coloured `ElementColors.For(status.LastHitElement)` at
  body height, generate an impulse of `0.25f * Scale`, then squash the visual children (each direct child that has a
  `Renderer`, never the root, which carries the `CharacterController`) from their cached `localScale` to zero over
  `squashSeconds = 0.15f` on unscaled time. Ease in, and stretch Y briefly before collapsing if it's cheap. Afterwards, set
  `renderer.enabled = false`. `TrainingDummyEnemy` still destroys the root on its timer, so `RunDirector` room-clear logic
  doesn't change.
- The particle system and impulse source are its own: a child `DeathPopFx` with
  `CinemachineImpulseSource`. Detach the particle GameObject on death (`SetParent(null)`, then `Destroy(go, 1.5f)`) so the
  burst survives the root being destroyed. Rebuild it on `Revived`.
- On `status.Revived`: restore the cached scales and re-enable the renderers (the M1 arena dummy respawns).
- Read-only state for tests: `bool IsPopped`, `int LastBurstCount`, `Color LastBurstColor`.

`RunDirector.SpawnEnemies` (around `RunDirector.cs:216`) already computes `scale` from the room kind. Add
`var pop = enemy.GetComponent<DeathPop>(); if (pop != null) pop.Scale = <Elite 1.6 / Boss 2.5 / else 1>;`. Use a plain
null check, not `?.`, because Unity objects don't support null propagation.

### Damage numbers: `Assets/_Project/Scripts/UI/DamageNumberView.cs` (new)
- Its own overlay canvas via `UiFactory.CreateOverlayCanvas(transform, "DamageNumberCanvas", sortingOrder: 9)`, below
  `ClashFeedbackView`'s 10 so `COUNTER!` and `CARD BREAK!` draw on top.
- A pool of 24 labels (`UiFactory` text, as `ClashFeedbackView.Popup` does). When the pool is exhausted, recycle the oldest.
- `AnyHitLanded` -> take a label and set its text to `info.Damage.ToString()`. Colour: `PlayerHurtColor = (1, 0.3, 0.25)`
  if the victim is on the Player team, otherwise `Opaque(ElementColors.For(info.Payload))`. Base font 36, multiplied by
  `HitJuiceRules.DamageNumberScale(info.Damage)`. The anchor is `info.Position + up * 0.6`, with a random sideways jitter of
  ±30 px.
- Animation on unscaled time over `showSeconds = 0.7f`: a scale punch from 1.4x down to 1x in the first 0.12 s, a rise of
  70 px with a sideways drift of ±25 px, and alpha `1 - t^2`. The label follows its world anchor, re-projected every
  `LateUpdate`.
- World to screen: `Camera.main.WorldToScreenPoint`, then `ScreenPointToLocalPointInRectangle(canvasRect, screen,
  canvasCam)`, where `canvasCam = canvas.renderMode == ScreenSpaceOverlay ? null : canvas.worldCamera`. This follows the
  CLAUDE.md lesson and matches `ReloadBarView.cs:166`. Without it the screenshot capture misplaces the numbers.
- Test accessors: `int ActiveCount`, `IReadOnlyList<(string text, Color color, float scale)> Active` (or an equivalent
  small struct).

### Scene wiring: `Assets/_Project/Scripts/Editor/GreyboxArenaBuilder.cs` (change)
- `BuildClashFx()`: add a child `HitFx` with `HitImpactFx`, and set `ParticleMaterial = GetOrCreateSparkMaterial()`.
- `BuildDummyPrefab()`: `root.AddComponent<DeathPop>()`. The spark material comes from a serialized field set here.
- `BuildHud()`: `hud.AddComponent<DamageNumberView>()`.
- Rebuild both scenes with `-executeMethod DeckArpg.Editor.GreyboxArenaBuilder.Build` and `...BuildRunScene`, and commit
  the regenerated prefab and scene diffs.

### Docs
- `CLAUDE.md` line 18: change `1.2 s timing bar` to `each reload rolls a 0.8-1.6 s bar, sweet-spot centre at 40-85%`
  (`Core/ActiveReload.cs:36-47`). This is item 6.
- `docs/specs/m4-overview.md:93`: change `Floating damage numbers (not selected).` to
  `Floating damage numbers: approved later, see docs/specs/hit-juice.md.`

## Edge cases
| Case | Behaviour |
|---|---|
| Poke hit | Sparks and a number, no freeze (`BodyHitStopSeconds` returns 0). |
| 0-card / zero-breaker body hit (0 damage) | No `AnyHitLanded`: no number, no sparks, no freeze. The clash juice already covers it. |
| Hit absorbed (i-frames, dead, same team) | `TakeHit` returns early, so nothing fires. |
| Lethal hit | `AnyHitLanded` fires (`Killed = true`) before `Died`, so the number, sparks and freeze all play, then the death pop. |
| Counter hit | The number shows the boosted damage. `COUNTER!` still pops from `ClashFeedbackView`. |
| Breaking swing during a clash freeze | Hit-stop extends to the later end time, never stacks (`HitStopTimer`). |
| Multi-hit Sleight | One number and spark burst per hit. The freeze is extended, not summed. |
| Boss `nonLethal` Big Deadlock hit on the player | The capped damage is shown. A 0-damage hit after the cap shows nothing. |
| Boss `DamageTakenMultiplier` (Exhausted x2) | The number shows the multiplied damage. |
| Many hits at once (a Sleight AoE into a crowd) | The pool recycles its oldest label. There is no allocation per hit. |
| Damage number while the world is frozen | It animates on unscaled time, so it moves during the hit-stop (intended). |
| Enemy destroyed while its number is still showing | The label keeps its last world anchor (it stores a `Vector3`, not the transform). |
| Death pop when the room clears and `RunDirector` destroys all enemies | The detached particle GameObject keeps playing and destroys itself after 1.5 s. |
| M1 arena dummy respawn | `Revived` restores the scales and renderers and rebuilds the burst child. |
| Scene/disable during a freeze | Unchanged: `HitStop.OnDisable` restores the time scale (CLAUDE.md lesson). |
| `Camera.main` is null (some tests) | Views skip placement. FX still emit. |

## Steps
Run the EditMode command after each Core step and PlayMode after each Gameplay/UI step (commands below). Commit after each
green step.

1. **Branch and docs.** `git checkout -b feat/hit-juice`. Make both doc edits above. Commit with `docs: fix Active Reload
   bar description; note damage numbers approved`.
2. **Core rules.** Add `HitJuiceRules` and `Tests/EditMode/HitJuiceRulesTests.cs`. Cases: poke -> 0, zero-breaker -> 0,
   damage 0 -> 0, player card value 1 -> 0.03, 9 -> 0.05, 5 -> 0.04, Sleight -> 0.05, enemy hits player -> 0.04 (any
   value), and `DamageNumberScale` at 0, 3, 30, 60 and 200 -> 1, 1.05, 1.5, 2, 2. Run EditMode.
3. **Hit event.** Add `HitInfo`, `AnyHitLanded`, `LastHitElement` and `Revived` to `CombatantStatus`. Extend
   `Tests/PlayMode/SpendHpTests.cs`, or add `HitEventTests.cs`, to check that the event fires once per damaging hit with the
   right damage, counter flag and `Killed` flag, and that it fires before `Died`. Check that it doesn't fire for i-frames,
   0 damage or `SpendHp`, and that `Revive` raises `Revived`.
4. **Hit-stop.** Wire `HitStop` to `AnyHitLanded`. Add PlayMode tests to `ClashJuiceTests.cs`, or a new
   `HitJuiceTests.cs`: a value-9 card hit freezes about 0.05 s (`Time.timeScale == 0` and then restored), a poke never
   freezes, an enemy hit on the player freezes, and the scale is restored to 1 afterwards. Follow
   `.claude/rules/unity-playmode-tests.md` (realtime waits, reset `timeScale` in TearDown).
5. **HitImpactFx.** Add the component and a test: a Fire card hit gives `LastSparkColor` equal to the Fire colour and the
   card spark count, and a hit on the player increments `PlayerHurtImpulses`.
6. **DeathPop.** Add the component and the `RunDirector` scale hookup. Tests: a lethal Ice hit sets `IsPopped`, the burst
   colour is Ice, and the renderers are disabled after about 0.2 s realtime. `Revive` restores them. A spawned Elite room
   enemy has `Scale == 1.6`.
7. **DamageNumberView.** Add the view and tests: one number per damaging hit with the right text and colour (element on
   an enemy, red on the player), scale above 1 for big hits, 25 rapid hits leave `ActiveCount <= 24`, no number for 0
   damage, and every label fades out by 0.8 s realtime.
8. **Scene wiring.** Update `GreyboxArenaBuilder`, close the Editor, run both `-executeMethod` rebuilds, and commit the
   regenerated assets. Extend `ArenaContentTests` or `RunSceneContentTests` (EditMode) to assert that the run scene has
   `HitImpactFx` and `DamageNumberView` and that the dummy prefab has `DeathPop`.
9. **Screenshots.** Extend `HudScreenshotCapture` with a shot taken mid-number. Land a few hits, then capture while the
   labels are visible. Run it alone without `-nographics`, and check `TestResults/screens/` to confirm the numbers sit over
   the enemies at a readable size and colour.
10. **Full suite, then playtest.** Run EditMode and PlayMode in full. Then hand off to the user for a playtest (M3 run
    scene: mob room, Elite, boss). Tune only the serialized or const numbers they ask for. Record the sign-off in
    `docs/notes/CURRENT.md`. Open the PR (merge commit, no attribution lines).

## Verification
Close the Unity Editor first (`tasklist //FI "IMAGENAME eq Unity.exe"`). Wrap each run in `timeout 1200`.
- EditMode:
  `timeout 1200 "/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform EditMode -testResults "$(pwd -W)/TestResults/editmode.xml" -logFile "$(pwd -W)/TestResults/editmode.log"`
- PlayMode: the same with `-testPlatform PlayMode` and `playmode.xml`/`playmode.log`.
- Pass = exit code 0. The `<test-run result="Passed" ...>` counts must be at least the baseline (EditMode 716, PlayMode
  287 passed / 9 skipped) plus the new tests, with 0 failed. `grep "error CS" TestResults/*.log` must print nothing.
- Screenshots:
  `timeout 1200 ".../Unity.exe" -batchmode -projectPath "$(pwd -W)" -runTests -testPlatform PlayMode -testFilter DeckArpg.Tests.PlayMode.HudScreenshotCapture -testResults "$(pwd -W)/TestResults/screens.xml" -logFile "$(pwd -W)/TestResults/screens.log"`,
  then view the PNGs in `TestResults/screens/`.
- Feel: the user's playtest sign-off (required, see Decisions).
