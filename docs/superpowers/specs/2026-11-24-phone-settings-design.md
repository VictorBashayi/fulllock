# Phone Settings redesign — design

Date: 2026-11-24. Status: draft for approval.

## Intent

Settings is usually the first place a new user goes, both to learn what FullLock does and to tailor it. Today it is one long scrolling page. Everyday choices (engineer voice) sit next to engineering knobs (filter beta), and grey helper text sits under almost every control. The redesign makes Settings easy to find your way around and teaches each setting by showing it working. The look stays the app's current look.

## Goals

- Split Settings into pages, using a two-pane layout that fits the landscape lock.
- Every setting row has a short name, one short explanatory line, and its control.
- Each page has a live preview card that shows its settings working.
- New pages: Layout (Swap sides) and About, with a "How it works" walkthrough that is also shown once on first launch.
- Keep developer tools (filter gain, steering axis, live debug, IMU logger, manual IP/port) in one Advanced page, away from new users.

## Non-goals

- No change to the driving screen's look. The user explicitly dropped that redesign. Its only change is Swap sides, which mirrors the pedals.
- No light theme, no colour picker, no dynamic colour.
- No new settings beyond Swap sides. Existing settings keep their ranges, defaults and saved keys.
- The PC app is not touched.
- Status: draft for approval. The user answered the bottom-bar and colour-reach questions on 2026-11-24.

## Anti-goals (a failure even if it works)

- Settings that looks like a different app from the driving screen.
- A preview that is decoration: it must react to the real setting or the real sensor.
- More words than one line per row. No taglines.

## Constraints

- Android, Jetpack Compose, Material 3, landscape only (`screenOrientation="landscape"`).
- Look: the app's current `Palette` in `ui/ControllerScreen.kt` (BgCenter `#10232B` → BgEdge `#090F14` radial ground, Panel `#111B24`, PanelBorder `#243441`, Text `#E6EDF2`, TextDim `#8A99A6`, Cyan `#22D3EE` for anything active, plus Red, Green, Yellow and Blue), and the `Condensed` font family. **Whole app (user decision):** `ui/theme/Theme.kt`'s racing-red Material scheme is replaced by a dark scheme built from `Palette` (primary = Cyan, background/surface = Panel and BgEdge tones, outline = PanelBorder, error = Red). The pairing sheet, Logger, dialogs and Settings then all match. The driving screen already draws from `Palette` directly and must look unchanged. Move `Palette` and `Condensed` somewhere both screens import, such as `ui/theme`.
- Saved settings stay in the existing SharedPreferences keys (`MainActivity.loadSettings` / `saveSettings`), so upgrading keeps every setting.
- Touch targets at least 48 dp.

## Approaches considered

1. **Two-pane Settings inside the existing Settings route (chosen; my call, since you talked outcomes and handed me the how).** One `SettingsScreen` composable holds a left page list and a right content pane. Each page is its own composable file. The pages are plain state with no extra navigation routes, so Back leaves Settings as it does today. This is the smallest change that gives the agreed layout, and it reuses all existing state objects.
2. Restyle the current single scrolling page in place. It's cheaper, but it gives up the page structure and preview cards you approved, so it doesn't meet the goals.
3. One navigation route per Settings page. That's more wiring (routes, back stack) for no visible gain in landscape, where both panes are visible at once.

## Design

### Shell

- **Left pane** (about 190 dp): the "Settings" title, then seven page items: Connection, Steering, Pedals, Engineer, Layout, Advanced, About. Each item has a small round icon chip and a label. The selected item is a Panel-coloured pill with a thin PanelBorder outline and a cyan icon chip. Unselected items use TextDim.
- **Right pane:** a preview card (about 190 dp wide, Panel fill, PanelBorder outline, 18 dp radius) on the left, and the page's group card of rows on the right.
- **Row:** a bold label (about 16 sp), one TextDim line (about 13 sp), and the control on the right. A slider sits under its text, and the value shows as a small mono pill. Rows are separated by thin PanelBorder lines.
- **Controls:** cyan sliders, toggles and the primary button (filled cyan pill, dark text). Secondary buttons are outlined pills. Segmented choices (Slider / Button, engineer level) use filter chips styled in the palette.
- **Bottom navigation bar: removed** (user decision). Settings opens from the gear button on the driving screen, as today, and system Back returns to the controller. The Scaffold `bottomBar` and the `NavTab` tab list go away.
- **IMU Logger** stays a NavHost route with no tab. Advanced opens it, and Back returns to Settings on the Advanced page. So the selected page lives above the NavHost (or in a saved state holder) and survives the trip.
- Settings opens on Connection when unpaired, otherwise on the last page used during the session.

### Pages

| Page | Preview card | Rows |
|---|---|---|
| Connection | Phone → PC with the PC's address and live ping, e.g. "Connected · 3 ms", "Looking for PC…" or "Not paired". | Paired PC (Re-pair, Forget) · Connect when the app opens (toggle) · Connection (Connect/Disconnect) |
| Steering | The app's own steering arc and mini wheel turning with the phone, the live angle, and the response curve with today's position dot plus the "N% steering" readout. | Lock angle · Centre smoothing · Deadzone · Set centre (button) · Reset filter & calibration (outlined button) |
| Pedals | Two working pedals using the real Slider/Button behaviour. Touching them doesn't send input to the PC. | Brake (Slider / Button) · Throttle (Slider / Button) |
| Engineer | The last radio call text and a "Test call" button that plays the existing radio check. | How much the engineer talks (Off / Quiet / Balanced / Talkative) · Voice (toggle, with a warning line when TTS is unavailable) · Vibration (toggle) |
| Layout | A tiny driving-screen silhouette whose pedals swap sides live. | Swap sides (toggle): throttle on the left, brake on the right |
| Advanced | The live debug numbers (packets, pongs, RTT avg/p99, send rate, steering angle and value, beta). | Server IP · Server port · Steering axis (chips) · Filter gain (beta slider) · IMU logger (opens the existing LoggerScreen) |
| About | App name, mark and phone version (`versionName`). | How it works (opens the walkthrough) · Your data (one line: inputs stay on your Wi-Fi, signed with your pairing key) · Source code (opens the GitHub repo) · Licence (MIT) |

The one-line explanations are the ones from the approved mockups:
- Lock angle: "How far you turn the phone for full lock"
- Centre smoothing: "Calmer on straights, same full lock"
- Deadzone: "Small wobbles near centre are ignored"
- Set centre: "Hold the phone straight, then tap"
- Brake/Throttle: "Slider: drag up for more pressure" or "Button: press for full, let go for none", following the current choice
- Connect when the app opens: "Finds your PC on this Wi-Fi by itself"
- Swap sides: "Throttle on the left, brake on the right"

### Swap sides (new setting)

- Stored as `swap_pedals` (Boolean, default false) in the same prefs, on `PedalSettings` as Compose state.
- When on, the controller screen puts throttle in the left pedal slot and brake in the right. Nothing else moves.

### How it works walkthrough

- Three full-screen steps in the app palette: **Pair** (scan the code on the FullLock PC window), **Hold** (hold the phone like a wheel and set the centre), **Drive** (pedals at the edges, buttons where your thumbs rest). Each step has a simple illustration, a title and one line. The controls are Next and Done, plus Skip.
- It shows once on first launch, tracked by a `walkthrough_seen` pref. It can be opened again from About.
- **Order on a fresh install (my call):** the walkthrough comes first. The automatic pairing sheet waits until the walkthrough closes, and the Pair step leads straight into it.
- **Upgrades (my call):** a user who already has a pairing key or saved settings is marked as having seen it, so an update never shows them the walkthrough.

## Left to the builder

- Icon choice for each page (Material icons already in the project).
- Exact dp/sp values within the ranges above, kept consistent across pages.
- How the preview cards sample live values (reuse the 30 Hz `produceState` pattern from `ControllerScreen`).
- Splitting `SettingsScreen.kt` into per-page files.

- About links to github.com/VictorBashayi/fulllock and reuses `ic_launcher_foreground` as its mark (my calls).
- The Engineer preview keeps the last radio call after the live banner clears (my call).

## Known limits

- About can show only the phone's version. The phone doesn't know the PC app's version today (discovery carries only a protocol number). Showing both would need a protocol change, which is out of scope.
