# Spec: M2 HUD (M2 step 5)

## Goal
Make the M2 systems readable at a glance:
- the Active Reload bar at its rolled spot around the player (rolled speed and sweet zone visible)
- the Sleight stock with a live preview and its timeout
- Overburn prompt and cost
- grade popups
- speed lines while the Perfect boost lasts
- a burned-card count

Greybox uGUI built in code, like the existing views.

## Decisions (from interview, 2026-11-13)
- **Bar render = screen overlay that follows the player.** Each frame the player's position (+1 m) is
  projected to the screen, then offset toward `ActiveReload.Anchor`. Constant size, clamped on-screen.
- **Bar style = horizontal + marker.** A 260 px bar with a Good zone and a narrower Perfect zone at the rolled
  `Target01`. A marker sweeps by `Progress01`. Jammed: red fill drains, zones dimmed, marker hidden.
- **Stock = slots + preview.** 3 small slots above the hand. Mirrored 0s show `0→N`. Preview line:
  `SLEIGHT 15+ · Throw · Fire · 3rd card = finisher x(1 + v/10)`. The `+` marks a partial total. A full
  stock fires instantly, so a 3-card total (and the final multiplier) never sits on screen. A thin
  timeout bar drains over `StockTimeoutSeconds`.
- **Feedback:**
  - Grade popup (PERFECT / GOOD / JAMMED, Deadlock colors).
  - Overburn prompt with its cost, and an ember tint on the bar during an Overburn.
  - Burned counter in the pile line.
  - **Speed lines** (not a text tag) while the boost lasts.

## Assumptions (mine, flag if wrong)
- **Partial preview rule** (Core, testable): with 1-3 stocked cards, a 0 mirrors the highest numbered card
  stocked *so far*, and is 1 if none. Shape = slot 1, element = slot 2 (if present), multiplier only with 3.
  With 3 cards it equals `Compose`.
- **Anchor offsets** (canvas units at 1920x1080, from the projected point): Above (0,150), UpperRight (200,110),
  Right (250,0), Below (0,-120), Left (-250,0), UpperLeft (-200,110). Clamped 40 px inside the screen.
- **Prompt texts:**
  - `[R] RELOAD` while a normal reload is possible and the bar is idle.
  - `[Shift+R] OVERBURN  -10 HP  (restores N)` while an Overburn is possible.
  - Shown greyed with `needs 11+ HP` when it's otherwise possible but unaffordable.
- **Popup timing:**
  - Shows for 0.6 s (unscaled) at the bar's last position.
  - JAMMED shows the moment the bar jams. PERFECT/GOOD show on completion. Running out with no press shows nothing.
- **Speed lines:**
  - 12 thin streaks along the left/right screen edges, scrolling toward the edges.
  - Opacity = 0.6 × remaining boost / `BoostSeconds`.
  - Hidden when there's no boost.
- Help text gains `Shift+1/2/3 stock · Shift+R overburn`.
- **Separate components** (`ReloadBarView`, `StockView`, `SpeedLinesView`), each auto-finding the player like
  `HandHudView` and offering `Bind(...)` for tests. Pile/prompt lines stay in `HandHudView`.

## Out of scope
- Real art/fonts/animation curves, SFX, controller-glyph swapping (the text shows keyboard keys).
- Enemy reload/stock displays (M4).

## Design

### Core: `SleightRules`
- `int PreviewSlotValue(IReadOnlyList<RuntimeCardInstance> stocked, int slot)`
- `int PreviewTotal(IReadOnlyList<RuntimeCardInstance> stocked)`
- Valid for 1-3 non-null cards. Throws on null/empty/>3/null entries/bad slot.

### UI (`Assets/_Project/Scripts/UI/`)
- `ReloadBarView`:
  - Bind: `Bind(DeckRuntimeManager deck, Transform player, Camera cam = null)`.
  - Test hooks: `IsBarVisible`, `MarkerProgress`, `ZoneCenter01`, `GoodZoneWidth01`, `IsJammedStyle`,
    `IsOverburnTint`, `GradeText`, `BarScreenOffset`.
- `StockView`:
  - Bind: `Bind(DeckRuntimeManager deck)`.
  - Test hooks: `SlotText(i)`, `PreviewText`, `TimeoutFill01`, `IsVisible`.
  - Visible only while something is stocked.
- `SpeedLinesView`:
  - Bind: `Bind(DeckRuntimeManager deck)`.
  - Test hooks: `Alpha`, `IsActive`.
- `HandHudView`: pile line adds `Burned N`. Prompt line handles RELOAD / OVERBURN (it gets
  `CombatantStatus` for affordability, which it already has). New hook: `PromptText`.
- `GreyboxArenaBuilder`: adds the three views to the HUD object. Then the arena is regenerated
  (`-executeMethod DeckArpg.Editor.GreyboxArenaBuilder.Build`).

## Edge cases
| Case | Behavior |
| --- | --- |
| No camera | bar placed at screen center + anchor offset |
| Player behind camera | bar hidden |
| Anchor would push the bar off-screen | clamped inside |
| Bar idle | hidden; a popup may linger 0.6 s |
| Stock emptied (fire/interrupt/timeout) | stock row hidden |
| Stock of only 0s | preview `SLEIGHT 1+` (2 cards) / `3` (3 cards) |
| Boost refreshed | speed lines back to full opacity |
| Jammed | popup says JAMMED, bar title says RECHARGING (no duplicate text; found in the screenshot review) |
| New bar starts while a popup lingers | popup cleared (found in the screenshot review) |
| Player died | bar hidden (cancelled), stock row hidden after respawn |

## Steps
1. Core preview functions + EditMode tests.
2. `StockView` + PlayMode tests.
3. `ReloadBarView` + PlayMode tests (zones follow the rolled target, marker follows progress, jam style, Overburn tint, popups, anchor offset direction).
4. `SpeedLinesView` + PlayMode test.
5. `HandHudView` prompt/pile changes + test.
6. Builder + regenerate arena. Update the content test so it expects the new views.
7. **Visual check:** an `[Explicit]` PlayMode capture test loads the arena, sets the states (stock with a 0, running bar,
   jam, Overburn prompt, boost), renders the main camera with the HUD canvases temporarily switched to
   Screen Space - Camera, and writes PNGs to `TestResults/screens/`. Run without `-nographics`, then inspect.
8. Full EditMode + PlayMode runs.

## Verification
- EditMode/PlayMode batchmode commands (CLAUDE.md, wrapped in `timeout 900`). Exit code 0, 0 failures, no `error CS`.
- Screenshot run: `-testPlatform PlayMode -testFilter DeckArpg.Tests.PlayMode.HudScreenshotCapture` without `-nographics`.
  Look at each PNG (bar at its anchor with zones and marker, stock row and preview, prompt text, speed lines).
