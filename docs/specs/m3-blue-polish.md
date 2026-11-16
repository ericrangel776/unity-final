# Spec: M3 Blue-room polish (Shop / Forge / Rest)

Status: approved 2026-11-16 (select + confirm for deck changes; Esc never leaves; unused-room leave asks once).

## Why
Playtest (2026-11-16): the non-combat rooms aren't intuitive. All four problems were confirmed:
unclear sub-modes, no feedback on actions, cluttered layout, inconsistent flow/controls.
Seen in the screenshots: the hint line overlaps the hand HUD's "Deck/Discard/Burned" text, purge mode has no
prompt and still says "1-5: buy", unaffordable cards look nearly the same as affordable ones, and Esc leaves the
room from the top level (easy to leave a Forge unused by accident).

## Goal
Every Blue room follows the same pattern, so learning one teaches all three:
**choose an option -> (if it needs a card) choose a card and see the result -> confirm -> see what happened.**

## Changes

### 1. Layout (all run screens)
- **The combat HUD is hidden while any run screen is open** (hand, stock, reload bar, prompts) and comes back
  when it closes. This fixes the overlap and the clutter. Owned by `RunScreens`, like the input block.
- Fixed regions on every Blue screen: header (room + Quality), status line (currency / HP / deck), **prompt line**
  (what to do right now), content, a feedback line, then the buttons and a key hint that matches the current mode.

### 2. Clear modes
- The prompt line always says what to do: "Buy a card, or remove one from your deck" / "Choose a card to remove
  (40 currency)" / "Choose a card to upgrade (+2)" / "Choose a card to make Premium".
- The key hint changes with the mode (no "1-5: buy" while purging).
- The deck list shows cards as cards: value, name, shape and element, Premium/upgrade marks. Cards that can't be
  chosen are greyed out with a short reason (e.g. "already Premium", "0 cards can't be upgraded").

### 3. Choose, preview, confirm (deck-changing actions)
- Clicking a deck card **selects** it (highlight) and shows a preview: "Strike 4 -> Strike 6", "Remove Ember 3
  (-40 currency, deck 10 -> 9)", "Frost 4 -> Frost 6, burns when played".
- A **Confirm** button (or `Enter`) applies it. Clicking another card changes the selection. Purge and Premium
  are permanent, so they never apply on a single click.
- Buying from the stock stays one step (click or `1`-`5`), because what you buy is already on the tile.

### 4. Feedback
- A feedback line after every action: "Bought Inferno 8 (-80)", "Removed Strike 2 (-40)", "Strike 4 is now
  Strike 6", "Healed 30 HP", "+1 Momentum". A failed action says why: "Need 30 more currency".
- Unaffordable stock: dimmed tile, price in red, footer "Need N more". Sold tiles say SOLD (as now).
- Spent options show DONE and the prompt says what's left ("Forge spent. Leave when ready").

### 5. Controls (same in all three rooms)
| Key | Top level | Choosing a card |
|---|---|---|
| `1`-`5` / click | pick an option / buy | (mouse: select a card) |
| `Enter` / Confirm | Leave (see below) | apply the selected card |
| `Esc` / Back | nothing | back to the top level |
- **Leave only from the Leave button or `Enter` at the top level.** `Esc` never leaves a room.
- Leaving a room you haven't used (no buy/purge, no forge, no rest choice) asks once: "Leave without using the
  Forge? Press Leave again." So a stray key can't waste the room.

## Out of scope
New shop rules or prices, the M4 deck-queue strip (it will reuse this card-style deck list), art/animation.

## Tests
- EditMode: preview/feedback text helpers in `RunUiText` (upgrade, Premium, purge, price shortfall).
- PlayMode: combat HUD hidden while a screen is open and back after; purge/forge need select + confirm (one click
  changes nothing); Esc never leaves; unused-room leave asks once; feedback line after buy/purge/upgrade/heal;
  unaffordable tile shows the shortfall. Update `FullRunSmokeTests` for the new confirm step.
- Screenshot pass with `HudScreenshotCapture` for every mode.
