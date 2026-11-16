# Spec: M3 step 7 - full-run smoke test + playtest

## Goal
Prove a whole run works the way a player drives it: walk into the exit door, pick from the screens with the
number keys / Esc / Enter, and reach Victory or Defeat, then restart. Then a manual 10-minute playtest.

## Decisions (user, 2026-11-16)
- **Combat rooms are cleared with `RunDirector.DebugDefeatAllEnemies`.** Real fights are covered by the clash,
  trial and relic tests; fake-keyboard combat would be slow and flaky in batchmode.
- **Build Settings = `M3_Run`, `M1_Arena`.** A build opens straight into the run. `SampleScene` (template) leaves
  the build list; the file stays. Both scene builders write this order, so a rebuild can't undo it.

## Smoke test (`Tests/PlayMode/FullRunSmokeTests.cs`)
Everything except the combat clear goes through `RunScreens.HandleKey/HandleBack/HandleConfirm` (the same calls
the keyboard routing makes) and walking the player onto the door.

1. **Victory run** (fixed seed). Door policy: an Elite if offered, else a Blue room not yet visited, else card 1.
   At every step:
   - Door: walking in opens `DoorChoiceView` and blocks input; `1`-`3` enters the room and closes it.
   - Combat: no screen open, input free, enemies spawned. After the clear, `RewardView` opens and blocks input.
     If a relic is offered, `Enter` does nothing until `1` takes it. Then `1` drafts a card, `Esc` closes the
     draft, `Enter` continues.
   - Blue: its screen opens; Rest takes `1` (heal), Shop tries `1` (buy), Forge just leaves. `Enter` leaves.
   - After every screen closes, input is released by the end of the frame.
   - Room 6 shows a single boss door. Clearing it opens `RunEndView` "VICTORY" with 6/6 rooms cleared; `Esc` does
     nothing; `Enter` starts a fresh run at room 1 (starter deck, 0 currency, no relics, full HP).
   - The run must have visited at least one Elite (relic taken) and one Blue room, else the seed is wrong.
2. **Defeat run.** Enter room 1, take a lethal hit: `RunEndView` "DEFEAT"; `Enter` restarts at room 1.

## Manual playtest checklist (user)
Open `M3_Run`, press Play, play a full run with the keyboard and mouse:
- Screen dim strength and text over the hand HUD look right in the real overlay (screenshots used camera space).
- No card fires when a number key closes a screen.
- A run takes roughly 10 minutes. Note any room that feels too long, too easy or unreadable.

## Out of scope
Balance changes from the playtest (logged as notes for M4), a title/menu scene.
