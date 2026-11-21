# Spec: In-game debug menu (F1)

## Context
Testing a change today means seed-hunting (`RunTestHelpers.EnterRoom` loops seeds 1-399), Inspector context menus on
`RunDirector`, or the `DeckArpg/Play Boss Fight` editor shortcut. There is no way to jump straight into a "Hexfire I7"
room, spawn a Caster, become unkillable, grant a relic or slow time while playing. The user wants one in-game menu that
covers what we've built (rooms, Map Cards, waves/variants, elements, heals, relics, reload, juice) and the planned
work (room/run timing, floor progression, VFX polish).

User decisions: **in-game overlay** toggled by a key; compiled in **Editor + Development builds only**; first version
includes **Run & rooms, Player cheats, Enemies & combat, Time & juice**.

Branch: `feat/debug-menu` from `main`. (`feat/enemy-variants` is unmerged. Branch from it instead if it hasn't merged
by then, because the menu spawns variants.) Write the spec to `docs/specs/debug-menu.md` first, as for every feature.

## Design

### Shape
- **Toggle:** F1 (free: no F-keys are bound anywhere). Backquote is a second toggle key.
- **Overlay:** its own Screen Space - Overlay canvas at sorting order 100, so it sits above `RunScreens` (30) and the HUD
  strip. It is a left-side panel (about 460 px wide) with a tab row of **Run | Player | Enemies | Time | Info** and a
  button column per tab. It's built with the existing `UiFactory` (`CreateOverlayCanvas`/`CreateImage`/`CreateText`)
  and `RunWidgets.TextButton`, using the same legacy `Text`, look and dark panel colours as the run screens.
- **Input while open:** the gameplay input is blocked (so a click doesn't Poke and `1-3` don't play cards). The game
  keeps running unless you press Pause. A "Pin" toggle keeps the panel visible while input is unblocked, so you can
  watch the Info readouts while fighting.
- **Bootstrap without a scene rebuild:** `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` creates one
  `DontDestroyOnLoad` "DebugMenu" object, inside `#if UNITY_EDITOR || DEVELOPMENT_BUILD`. It finds `RunDirector`, the
  player and other targets lazily every time it opens or refreshes, so it works in `M3_Run` and also in `M1_Arena`
  (where the Run tab shows "no RunDirector in this scene" and the other tabs still work through `CombatantStatus.All`).
- **Extensible:** every button is a `DebugCommand` (label, tab, `Func<bool> available`, `Action run`) in one registry
  list. A future feature adds one line, and unavailable commands grey out instead of throwing.

### Tabs and commands (each built on an existing API where one exists)
**Run** (needs `RunDirector`)
- Restart run with a random seed or the same seed. Show the seed and copy it to the clipboard (`GUIUtility.systemCopyBuffer`).
- **Room picker:** Kind ◀▶ (all 9 kinds), Intensity −/+ (1-9), Trial ◀▶ (shown only for EliteTrial), Buff element ◀▶
  (shown only for KindledGround), then **Enter now**. It finishes whatever room you're in first (fight → debug clear +
  finish reward, Blue → leave, run over → restart), then enters the room you picked. It's disabled on room 6 unless the
  pick is the Boss (room 7+ has no offer table).
- Clear room (`DebugDefeatAllEnemies`, which already kills wave 2), Skip to boss (`DebugSkipToBoss`), Skip wave delay,
  Finish reward / Leave room.

**Player**
- God mode toggle: HP never drops; hits, clashes, stuns and FX still play.
- Full heal / set HP 1 / set HP 50%. These go through a director helper that also writes `Run.SetHp`, so the change
  survives the room carry-over.
- +10 max HP (`Run.IncreaseMaxHp`), +100 currency (`Run.AddCurrency`), +1 Momentum (`Momentum.AddStack`).
- Give relic ◀▶ over the `RelicId` values, using the same sequence as `ChooseRelic`: `Run.AddRelic` → `PlayerRelics.SetRelics` →
  `SetMaxHpKeepHp`.
- Add card ◀▶ over every `RunContent` card (starter + pool + enemy decks), plus remove the selected card. These use
  `Run.AddCard`/`RemoveCard`, then re-deal (`UseRunDeck` + relics `SetRelics`/`BeginRoom`, as `PreparePlayerDeck` does).
- Instant reload (`Cycle.Reload()`), restore burned cards (`Cycle.RestoreBurned()`), cancel heal channel.
- Apply Burn / Chill to the player, and clear statuses (`ElementStatusEffects.ApplyBurn/ApplyChill/Clear`).

**Enemies & combat**
- Spawn Brute / Caster / Rusher in front of the player. This goes through the director so the enemy gets the room's
  scaling, joins `Enemies` and counts toward the clear. It works only while fighting a non-boss room.
- Kill all, Kill nearest, Damage all 10.
- Freeze AI toggle (enemies stand still and don't attack), Force telegraph on all (`ForceTelegraph`), Stun all
  (`ApplyStun 2 s`), Break all (`ApplyBreak`).
- Burn / Chill all enemies, and a Wind shove (`ApplyKnockback` away from the player).
- Boss: Force Exhausted (`BossEnemy.ForceExhausted`) and mill 5 cards (`Cycle.DeleteFromCurrentCycle` on the boss's
  queue), when a boss is present.
- Show hitboxes toggle.

**Time & juice**
- Pause, and time scale 0.1 / 0.25 / 0.5 / 1 / 2. These go through a new `HitStop` base-scale API so a hit-stop freeze
  restores *your* scale instead of overwriting it (the CLAUDE.md `Time.timeScale` lesson).
- Toggles for hit-stop, camera shake (`CameraPunch` and `CinemachineImpulseSource` enabled flags), damage numbers
  (`DamageNumberView`), the hit/death FX components, and SFX (`AudioService` enabled / `AudioListener.volume`).

**Info** (read-only, refreshed 4x per second; also shown as a mini strip when the panel is pinned)
- FPS, run state, room number/card, seed, Momentum, currency.
- **Room timer and run timer.** Room time runs from `RoomStarted` to the clear, and the last 6 room times are listed.
  This is a manual first step toward planned item 5 (room/run timing).
- Enemy count, live count, `CurrentMix`, waves pending; player HP/posture/statuses; draw/discard/burned counts.

### Code changes (smallest hooks; Core ← Gameplay ← UI respected)
- **Core `RunState`:** `DebugSetOffer(IReadOnlyList<MapCard>)` replaces the current offer while choosing a door (it throws
  otherwise), and `public bool IsBossRoom` if it isn't public yet. Covered by EditMode tests.
- **Gameplay `RunDirector`:** make the class `partial` and add `RunDirector.Debug.cs` with:
  - `DebugEnterRoom(MapCard)`
  - `DebugSpawnEnemy(EnemyVariant, Vector3)`, which calls the private `SpawnWave` path with a single-entry mix at a position.
    This needs a small refactor: an optional position override.
  - `DebugSkipWaveDelay()` (reuses `SpawnPendingWaveNow`)
  - `DebugSetPlayerHp(int)`, `DebugGrantRelic(RelicId)`, `DebugAddCard(CardData)`/`DebugRemoveCard(...)`
  - `LastSeed`, which `StartRun` records (one line).
  - Leave the existing context-menu methods where they are, with no drive-by moves.
- **Gameplay `CombatantStatus`:** `GodMode` flag. In `TakeHit`/`TakeEffectDamage`, damage becomes 0 and death is
  skipped; everything else is unchanged. It's separate from `IsInvulnerable`, which the dash rewrites every frame.
- **Gameplay `TrainingDummyEnemy`:** `static bool DebugFreezeAI`, checked right after the knockback move in `Update`.
- **Gameplay `CombatActionController`:** `static bool? DebugShowHitboxes`, which overrides the serialized `showHitboxes`
  when it is set.
- **Gameplay `HitStop`:** `static float BaseTimeScale` + `SetBaseTimeScale(float)`. When not frozen it sets
  `Time.timeScale`; while frozen it updates `savedTimeScale`. There's also a `static bool Disabled` toggle. It keeps a
  single owner, and `OnDisable` restores the base scale.
- **Gameplay `PlayerCombatInput`:** `DebugInputBlocked` bool, OR'd into the existing `InputBlocked` check.
  `RunScreens` stays the only writer of `InputBlocked`.
- **UI (new):** `UI/Debug/DebugMenu.cs` (bootstrap, toggle, canvas, tabs), `UI/Debug/DebugCommands.cs` (the registry
  and command definitions), `UI/Debug/DebugRoomPicker.cs` (the ◀▶ picker state, pure enough to unit-test), and
  `UI/Debug/DebugRunTimer.cs` (room/run timing from `RoomStarted`/`StateChanged`).
  The whole `UI/Debug` folder is wrapped in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.

### Reuse
`UiFactory`, `RunWidgets.TextButton/Label/Paint`, `RunDirector.DebugDefeatAllEnemies/DebugSkipToBoss/SpawnPendingWaveNow`,
`RunState.AddCard/AddRelic/IncreaseMaxHp/AddCurrency`, `ElementStatusEffects`, `BossEnemy.ForceExhausted`,
`RunTestHelpers.Place` (the CharacterController-safe teleport pattern), and `MapCard` validation (trial ⇔ EliteTrial).

## Steps (one commit each)
1. Spec `docs/specs/debug-menu.md` (this plan, tightened).
2. Core: `RunState.DebugSetOffer` + EditMode tests (works only at a door, replaces the offer, the boss room accepts it).
3. Gameplay hooks: God mode, freeze AI, hitbox override, `HitStop` base scale/disable, debug input block + PlayMode tests:
   - God mode takes a lethal hit and survives with HP unchanged.
   - Base scale 0.5 survives a hit-stop freeze.
   - A frozen enemy doesn't telegraph.
   - The debug block stops card plays.
4. `RunDirector.Debug.cs` + PlayMode tests in `RunLoopTests` style:
   - `DebugEnterRoom(Hexfire 7)` from the first door and from mid-fight, which also replaces seed-hunting in new tests.
   - Spawning a Caster joins `Enemies` and must die before the room clears.
   - The HP helper persists across rooms.
   - Relic and card grants show in `Run` and the player's deck.
5. UI menu: bootstrap, tabs, all commands, Info/timers + PlayMode tests:
   - F1 toggles via `InputSystem` test keyboard, or a direct `Toggle()` call.
   - Every command whose `available()` is true runs without exceptions in a fight, at a door and in a Blue room.
   - Command smoke loop.
6. Screenshot `CaptureDebugMenu` (`[Explicit]`, `HudScreenshotCapture`) of each tab, then inspect it; check that the
   panel doesn't cover the hand HUD.
7. Docs: add a short "Debug menu (F1)" section to `CLAUDE.md` Commands, and a gotcha to `docs/notes/CURRENT.md`: "add new
   test hooks as a `DebugCommand`".

## Verification
- Editor closed; the CLAUDE.md batchmode EditMode + PlayMode commands wrapped in `timeout 1200`. Both must exit 0, with
  0 `error CS`.
- Screenshot run without `-nographics`: `-testFilter DeckArpg.Tests.PlayMode.HudScreenshotCapture.CaptureDebugMenu`, then
  read `TestResults/screens/debug_*.png`.
- **Release build is stripped:** an EditMode test that asserts `DebugMenu` is wrapped in the define, or a non-development
  player build compiled with `-executeMethod`. If neither is practical, I'll say so plainly.
- **Manual (user):** in Play mode, press F1, then:
  - Run tab → Hexfire, 7, Enter now.
  - Enemies tab → Spawn Caster.
  - Time tab → 0.25x.
  - Clash a projectile and check that the slow-mo comes back after the hit-stop.
  - God mode on and take hits.
  - Info tab shows the room timer.
