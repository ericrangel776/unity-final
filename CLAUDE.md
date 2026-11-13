# Unity Deck-Builder ARPG

A Unity game that mixes action RPG gameplay with deck-building elements.
Class final project, scoped as a **vertical slice**. Full design: `docs/design/game-design-doc.md`
(read the relevant section before implementing a system).

## Design summary (source of truth: the design doc)
Real-time roguelike ARPG with a card-clash combat core (mechanics inspired by KH: Chain of
Memories; run structure like Slay the Spire). Original assets/names only - no IP from references.

- **Hand:** 3-card active hand + reserved `0`-Parry slot. Single play fires a card and draws next.
- **Sleights:** stock 3 cards -> auto-fire, value = sum. Slot 1 = shape (Strike/Throw/Arcana),
  Slot 2 = element/status (Fire/Ice/Lightning/Physical), Slot 3 = multiplier `1 + v*0.1`.
  Slot-1 card is burned for the room.
- **Clashes:** higher value breaks lower (stun + posture dmg); equal = neutral cancel;
  `0` breaks anything only if played *second* (`activationTick` strictly greater; tie = bounce),
  and deletes the enemy card from its current cycle.
- **Active Reload:** 1.2 s timing bar; sweet spot = instant reload + speed boost.
- **Safeguards (must hold):** Floor of 3 (deck <= 3 -> Sleights don't burn); always-available
  `Value 1` poke + i-frame dash; Overburn Reload (10% HP restores burned cards);
  `0` is a flag (`isZeroBreaker`), never integer math - clamp all values to `[1, 9]`;
  heals fire on activation frame; multi-hit Sleights resolve per hit;
  local hitbox clashes for mobs, global HUD clash only for Boss/Elite Sleights;
  boss has a visible 15-card deck - milling it to 0 causes a 4 s stagger.
- **Macro loop:** choose 1 of 3 Map Cards per door (Red = combat mods, Green = room buffs,
  Blue = shop/forge/rest). Rewards: currency + 1-of-3 card draft (skippable) + Map Card.
  Elites/Bosses drop passive Relics.

## Technical rules (from design doc section 5)
- `CardData` is a read-only `ScriptableObject` - **never mutate it at runtime**.
- Per-run state lives in `RuntimeCardInstance` (plain C#); `GetEffectiveValue()` does the clamp.
- `DeckRuntimeManager` owns DrawQueue / ActiveHand[3] / DiscardPile / BurnedPile.
- Each hitbox/projectile carries an immutable `AttackPayload` snapshot taken at spawn.
- `CombatActionController` registers hitboxes/VFX; `CancelActiveAction()` on Card Break.
- Hit-stop uses `WaitForSecondsRealtime`; inputs go through a 0.2 s buffer.

## Milestones
1. Core clash prototype (movement/dash, card data, hand HUD, clash resolver)
2. Sleights + Active Reload (burning, Floor of 3)
3. Map Card loop + relics (6-room floor, 4 room types, reward UI, 6 relics)
4. Boss AI + juice (15-card boss deck, telegraphs, hit-stop, VFX/SFX)

## Commands
Unity **6000.3.24f1** (6.3 LTS), URP 3D, Input System only (legacy input disabled).
Close the Editor before batchmode runs (a project can only be open once).
- Open: Unity Hub -> Add -> select the repo root. First import takes several minutes (shaders).
- EditMode tests:
  `"/c/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe" -batchmode -nographics -projectPath "$(pwd -W)" -runTests -testPlatform EditMode -testResults "$(pwd -W)/TestResults/editmode.xml" -logFile "$(pwd -W)/TestResults/editmode.log"`
- PlayMode tests: same command with `-testPlatform PlayMode` (and `playmode.*` file names).
- Result: exit code 0 = all passed; summary in the `<test-run ...>` line of the XML.
- Lint: none yet; compile errors show as `error CS` lines in the log.

## Architecture
- `Assets/_Project/` - all our code/content (keeps it apart from template/third-party assets).
  - `Scripts/Core` (`DeckArpg.Core`) - data + rules, no scene deps: `CardData`, `RuntimeCardInstance`,
    `AttackPayload`, clash/Sleight math. References nothing of ours.
  - `Scripts/Gameplay` (`DeckArpg.Gameplay`) - MonoBehaviours: player, `DeckRuntimeManager`,
    `CombatActionController`, enemies, boss, rooms. Refs Core, Input System, Cinemachine.
  - `Scripts/UI` (`DeckArpg.UI`) - hand HUD, Active Reload bar, reward/map-card screens. Refs Core, Gameplay, uGUI/TMP.
  - `Scripts/Editor` (`DeckArpg.Editor`) - editor-only tools (card authoring, debug windows).
  - `Tests/EditMode`, `Tests/PlayMode` - NUnit test assemblies (`DeckArpg.Tests.*`).
  - Add content folders (`Prefabs/`, `ScriptableObjects/Cards/`, `Art/`, `Audio/`, `Scenes/`) when the
    first asset goes in - git does not track empty folders and Unity churns their `.meta` files.
- Dependency direction: Core <- Gameplay <- UI. Never reference upward. Namespace = assembly name.
- `Assets/Settings/` - URP pipeline/renderer assets (PC + Mobile); `Assets/Scenes/SampleScene` - template scene.
- `Assets/InputSystem_Actions.inputactions` - project-wide input actions (`Player`, `UI` maps).
- Commit `Assets/`, `Packages/`, `ProjectSettings/` and every `.meta`; `Library/`, `Logs/`, `UserSettings/` are ignored.
- `docs/specs/` - feature specs written with /spec
- `docs/notes/CURRENT.md` - running notes for the task in progress (/handoff)

## Definition of done
1. The change does what was asked, including edge cases (empty input, errors).
2. Tests added or updated for changed behavior.
3. The test and lint commands listed under Commands pass - show the output.
4. No unrelated changes in the diff.

## Compaction
When compacting, always keep: the current goal, the list of files modified, the
test command and its latest result, and any failed approaches to avoid.

## Lessons learned
- `Time.timeScale` is global: anything that freezes it must share one owner/saved scale across
  instances, or a second freezer saves `0` as "previous" and locks the game. Restore it in `OnDisable`.
- In PlayMode tests, `WaitForFixedUpdate` / `Time.time` loops never advance while `timeScale == 0`;
  wait on frames + `Time.realtimeSinceStartup` with a timeout, and reset `Time.timeScale = 1` in TearDown.
- Arena tests leave `M1_Arena` loaded for later tests, so its scene components (e.g. `HitStop`)
  still react to events in other fixtures. Wrap batchmode test runs in `timeout` so a hang fails fast.
