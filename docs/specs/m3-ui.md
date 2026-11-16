# Spec: Run UI (M3 step 6)

## Goal
Play a whole run without the RunDirector Inspector menu: walk to an exit door and pick a Map Card, take the
reward (relic, then draft), use Shop/Forge/Rest, read currency, Momentum and relics on a HUD strip, and see a
run-end summary with a restart. All screens are built in code through `UiFactory`, like the M2 HUD.

## Decisions (user, 2026-11-16)
- **Input: mouse + number keys.** Every option is a clickable uGUI button, and keys `1`-`4` pick options.
  `Esc` backs out, skips or leaves (see each screen). `Enter` = the screen's main button (Continue / Restart).
- **Physical exit door.** In `ChoosingDoor`, an exit door stands at the far end of the arena. Walking into it
  opens the Map Card choice. `Esc` closes the choice and you can walk around again.
- **No pause.** Screens only open when no enemies are alive, so `Time.timeScale` is never touched. Instead,
  **player input is blocked while any screen is open** (no move, dash, cards, reload). Keys `1`/`2`/`3` are also
  the combat play slots, so the block is required, not just nice to have.

## Assumptions (edit if wrong)
- **One door, not three.** The door opens a 3-card overlay. Three doors (one per card) would push card info into
  world-space labels, which are harder to read and to screenshot.
- The door is at `(0, 0, 8)`, 4 m past `enemyCenter` (behind where enemies spawn). It shows a glowing frame only while
  `State == ChoosingDoor`, and it's also how the first room is chosen at run start.
- Purple cards show their trial name (Veiled / Counterpuncher / Hexer) at the door, so the choice is informed.
- Relic and card names are text only (no icons yet). Art is M4 juice.

## Screens
| Screen | Opens when | Content | Input |
|---|---|---|---|
| **HUD strip** (always on, top edge) | run started | Room `n/6`, currency, Momentum pips (0-3), relic short names, Ashen stacks this room | - |
| **Door choice** | player enters the exit-door trigger in `ChoosingDoor` | 1 card per `Run.CurrentOffer`: family colour, name, Intensity (red/green/purple) **or** Quality (blue, different badge), and for combat: tier name (`Muted`..`Frantic`) + reward pips. Shows the trial (Purple) and the buff element (Kindled Ground). | `1`-`3`/click = `ChooseDoor(i)`; `Esc` = close |
| **Reward** | `RewardReady` | Currency gained. **Relic pick first if `RelicOptions.Count > 0`** (Continue is disabled until one is taken), or a "+30 currency" line if `RelicFallbackPaid`. Then the draft: card name, value, rarity, `+N` upgrade, picks left (`PicksAllowed - PicksMade`). | `1`-`4`/click = pick; `Esc` = skip (shows `+SkipBonus`); `Enter` = `FinishReward()` |
| **Shop** | `Utility` + `CurrentShop` | Stock with price and a sold state; a Purge tab listing the deck with the current purge cost (disabled at min deck size). | click/keys = buy; `Esc`/Leave = `LeaveUtility()` |
| **Forge** | `Utility` + `CurrentForge` | Deck list; `Upgrade +N` and `Make Premium` modes, which grey out per `CanStillUpgrade`/`CanStillMakePremium` and `ForgeRules.CanUpgrade/CanMakePremium`. | click = apply; `Esc`/Leave |
| **Rest** | `Utility` + `CurrentRest` | `Heal X HP` (`HealAmount`) or `Meditate (+1 Momentum)`, one choice. | `1`/`2`; `Esc`/Leave |
| **Run end** | `RunEnded` | Victory/Defeat, rooms cleared, **average Intensity**, currency, relics, deck size. | `Enter`/Restart = `RestartRun()` |

## New / changed code
- **Gameplay**
  - `PlayerCombatInput.InputBlocked` (bool): when true, `Update` clears the buffer and skips movement and combat.
    **One owner:** only `RunScreens` sets it (the timeScale lesson: no two writers).
  - `ExitDoor`: a horizontal distance check (no physics trigger, so it's deterministic) that raises
    `PlayerEntered` once per entry. Standing inside when it opens doesn't count. Visible only while open.
  - `RunDirector`: builds the door at runtime (`ExitDoor.Create(doorPosition)`, default `(0, 0, 8)`), opens it
    only in `ChoosingDoor` (in `SetState`), and adds an `ExitDoorEntered` event. The existing debug ⋮ menu stays.
  - `GreyboxArenaBuilder` (Editor): the `M3_Run` scene gets a `RunUI` object (`RunScreens` + `RunHudStripView`,
    wired to the director). `RunScreens` creates an `EventSystem` with `InputSystemUIInputModule` at runtime if
    the scene has none.
- **UI** (`DeckArpg.UI`)
  - `RunScreens`: shows one `RunScreen` at a time over a dim, owns `InputBlocked`, and routes the
    number keys (`Keyboard.current`) to the open screen. The block turns on at `Show` but turns off only in
    `LateUpdate`, so a `1` that closes a screen can't also play card slot 1 that frame.
  - `RunHudStripView`, `DoorChoiceView`, `RewardView`, `ShopView`, `ForgeView`, `RestView`, `RunEndView`, and a
    shared `DeckListView` (Shop purge + Forge).
  - `RunUiText`: pure string/colour helpers (tier name, pips, family colour, relic short name), unit-testable.
- No Core changes are expected.

## Sub-steps (one commit each)
1. Input lock + `ExitDoor` + `RunDirector` door wiring.
2. `RunScreens` shell + HUD strip + scene builder (`RunUI`).
3. Door choice screen.
4. Reward screen (relic-first + draft + skip).
5. Shop / Forge / Rest panels + `DeckListView`.
6. Run-end screen + restart.
7. Screenshot pass with `HudScreenshotCapture` (one PNG per screen), fixes, CURRENT.md update.

## Tests
- EditMode: `RunUiText` (tier names for I 1/4/7/9, pip counts, Blue cards show Quality instead of a tier).
- PlayMode (`RunScreensTests`, loads `M3_Run`):
  - Entering the door trigger opens the door choice and sets `InputBlocked`. `Esc`/close clears it.
  - Selecting option `i` calls `ChooseDoor(i)`, closes the screen and unblocks input.
  - A Purple clear forces the relic: Continue is disabled until `ChooseRelic`, then the draft pick adds the card
    to `Run.Deck`. Skip pays the skip bonus.
  - Shop buy spends currency and marks the item sold. Forge upgrade/premium respects the Quality rules. Rest heal
    raises HP.
  - Player death shows the run end with average Intensity, and Restart starts a new run in `ChoosingDoor`.
  - The HUD strip shows currency/Momentum after a clear.
- Screens are driven through public view methods (`Select(i)`, `Close()`), not simulated key presses. One test
  covers the key routing by calling `RunScreens.HandleKey(n)`.

## Out of scope
Animations/juice for the screens, card art, gamepad navigation, a tooltip for relic effects (M4).
