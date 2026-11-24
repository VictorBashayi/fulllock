# Phone Settings Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the phone's single scrolling Settings page with a seven-page two-pane Settings in the app's current look, with live previews, Swap sides, an About page and a first-launch walkthrough. The bottom bar is removed and the whole app moves onto the current palette.

**Architecture:** `Palette`/`Condensed` move to `ui/theme` and drive a dark Material scheme for the whole app. A new `ui/settings` package holds the shell (`SettingsScreen`: page rail + content pane), a small kit of row and card composables, and one file per page. Testable decisions (start page, walkthrough and auto-pairing gating, pedal side order) are pure functions in `ui/settings/SettingsLogic.kt` with JUnit tests. Settings still saves through `MainActivity.loadSettings`/`saveSettings`.

**Tech Stack:** Kotlin, Jetpack Compose (BOM 2024.02.00), Material 3, navigation-compose 2.7.6, JUnit 4.

**Spec:** `docs/superpowers/specs/2026-11-24-phone-settings-design.md`

## Global Constraints

- Landscape only; touch targets ≥ 48 dp.
- Colours come only from `Palette`: BgCenter `#10232B`, BgEdge `#090F14`, Panel `#111B24`, PanelBorder `#243441`, Text `#E6EDF2`, TextDim `#8A99A6`, Cyan `#22D3EE` (anything active), Red `#F26B6B`, Green `#3DDC84`, Yellow `#F2C94C`, Blue `#5B7BF0`. Type: `Condensed`.
- The driving screen must look exactly as it does today, except that Swap sides can mirror the pedals.
- Existing prefs keys and defaults stay unchanged. New keys: `swap_pedals` (Boolean, false) and `walkthrough_seen` (Boolean, false).
- Row copy is verbatim from the spec. One TextDim line per row, no taglines.
- Run the tests with `./gradlew :app:testDebugUnitTest` from `android/`. Build with `./gradlew :app:assembleDebug`.
- Commits are authored by the user with **no Co-Authored-By trailer** (user rule). Work stays on branch `feature/phone-settings`.

## Review Focus

- **Upgrade with a saved pairing key:** the walkthrough must not appear and the pairing sheet behaves as before. Test: `walkthroughOnUpgradeIsSkipped` (Task 6).
- **Fresh install:** the pairing sheet must not open on top of the walkthrough, and it opens once the walkthrough closes. Test: `autoPairWaitsForWalkthrough` (Task 6).
- **Swap sides changed while a pedal reads non-zero:** both values reset to 0 on the swap, so no input stays stuck. The check is a device step in Task 7: hold throttle in slider mode, go to Settings, toggle, come back, and the PC must read 0. The controller already zeroes inputs on dispose; the swap must do the same.
- **Leaving Settings to the IMU Logger and pressing Back** returns to Advanced, not Connection. Test: `startPageKeepsLastPage` (Task 2), plus the device check in Task 7.
- **Pedals preview touches** must never move `transport.brake`/`throttle`. Test: the preview takes no transport parameter (enforced by its signature, Task 4), plus the device check.

---

### Task 1: Palette theme for the whole app

**Files:**
- Create: `android/app/src/main/java/com/f1controller/ui/theme/Palette.kt`
- Modify: `android/app/src/main/java/com/f1controller/ui/theme/Theme.kt`
- Modify: `android/app/src/main/java/com/f1controller/ui/ControllerScreen.kt:71-87` (delete `Palette` and `Condensed` and import them from `ui.theme`)
- Test: `android/app/src/test/java/com/f1controller/ui/theme/ThemeTest.kt`

**Interfaces:**
- Produces: `object Palette` (same members, now public, in `com.f1controller.ui.theme`), `val Condensed: FontFamily`, `fun fullLockColorScheme(): ColorScheme`, and `F1ControllerTheme(content)` (same name; now uses `fullLockColorScheme()` and paints the system bars `Palette.BgEdge`).

- [ ] **Step 1: Write the failing test** `ThemeTest.schemeUsesPalette`: `fullLockColorScheme()` has `primary == Palette.Cyan`, `onPrimary == Palette.BgEdge`, `background == Palette.BgEdge`, `surface == Palette.Panel`, `outline == Palette.PanelBorder`, `onSurface == Palette.Text`, `onSurfaceVariant == Palette.TextDim`, `error == Palette.Red`.
- [ ] **Step 2: Run the tests.** Expected: FAIL, unresolved `fullLockColorScheme`.
- [ ] **Step 3: Implement.** Move `Palette`/`Condensed` and build `darkColorScheme(...)` with the values above. Use `surfaceVariant = Palette.BgCenter` and `secondary = Palette.Green`. Delete the racing-red constants.
- [ ] **Step 4: Run the tests and build.** Expected: PASS, BUILD SUCCESSFUL.
- [ ] **Step 5: Device check.** Install with `./gradlew :app:installDebug`, open the driving screen and capture it with `adb exec-out screencap -p`. It must match today's screenshot, apart from live values.
- [ ] **Step 6: Commit** `feat(phone): app theme from the controller palette`

### Task 2: Settings shell, kit and navigation

**Files:**
- Create: `android/app/src/main/java/com/f1controller/ui/settings/SettingsLogic.kt`
- Create: `android/app/src/main/java/com/f1controller/ui/settings/SettingsKit.kt`
- Create: `android/app/src/main/java/com/f1controller/ui/settings/SettingsScreen.kt` (replaces `ui/SettingsScreen.kt`, which is deleted once its controls have moved in Tasks 3–6; until then any page not yet built shows the old content, so that every commit builds)
- Modify: `android/app/src/main/java/com/f1controller/MainActivity.kt:393-507` (remove `NavTab`, the bottom bar and the Logger tab; keep routes `controller`, `settings` and `logger`)
- Test: `android/app/src/test/java/com/f1controller/ui/settings/SettingsLogicTest.kt`

**Interfaces:**
- Produces:
  - `enum class SettingsPage(val label: String, val icon: ImageVector) { CONNECTION, STEERING, PEDALS, ENGINEER, LAYOUT, ADVANCED, ABOUT }` with labels "Connection", "Steering", "Pedals", "Engineer", "Layout", "Advanced", "About".
  - `fun startPage(paired: Boolean, last: SettingsPage?): SettingsPage`
  - Kit: `SettingsGroup(content: @Composable ColumnScope.() -> Unit)`, `SettingsRow(label: String, line: String, control: @Composable () -> Unit = {}, below: (@Composable () -> Unit)? = null)`, `PreviewCard(content: @Composable ColumnScope.() -> Unit)`, `ValuePill(text: String)`, `PrimaryPill(text: String, onClick: () -> Unit)`, `OutlinePill(text: String, onClick: () -> Unit)`, `PaletteSlider(value: Float, range: ClosedFloatingPointRange<Float>, onChange: (Float) -> Unit)`, `PaletteSwitch(checked: Boolean, onChange: (Boolean) -> Unit)`, `PaletteChips(options: List<String>, selected: Int, onSelect: (Int) -> Unit)`.
  - `SettingsScreen(page: SettingsPage, onPage: (SettingsPage) -> Unit, pageContent: @Composable (SettingsPage) -> Unit)`: the rail plus the content pane. Each page draws its own preview and group.
- Consumes: `Palette`, `Condensed` (Task 1).

- [ ] **Step 1: Write failing tests** in `SettingsLogicTest`:
  - `startPageUnpairedIsConnection`: `startPage(false, SettingsPage.ENGINEER) == CONNECTION`
  - `startPageKeepsLastPage`: `startPage(true, ADVANCED) == ADVANCED`
  - `startPageDefaultPaired`: `startPage(true, null) == STEERING`
- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Implement `startPage`, the kit and the shell** to the spec's Shell section:
  - Rail about 190 dp, with "Settings" in 20 sp Condensed bold.
  - The selected item is a Panel pill with a 1 dp PanelBorder outline and a cyan icon chip (Cyan at 15% alpha behind a Cyan icon).
  - Groups and cards: Panel fill, 1 dp PanelBorder, 18 dp radius. Rows are at least 48 dp, with PanelBorder dividers.
  - The ground is the same radial BgCenter→BgEdge gradient the controller uses.
- [ ] **Step 4: Rewire navigation.** In `F1ControllerApp`, hold `var settingsPage by rememberSaveable { mutableStateOf<SettingsPage?>(null) }` above the `NavHost`, so a trip to the Logger keeps the page. The driving screen's gear navigates to `settings`, Advanced's IMU logger row navigates to `logger`, and system Back pops normally. Drop `Scaffold.bottomBar`.
- [ ] **Step 5: Run the tests and build.** Expected: PASS, BUILD SUCCESSFUL.
- [ ] **Step 6: Commit** `feat(phone): two-pane settings shell, no bottom bar`

### Task 3: Connection and Advanced pages

**Files:**
- Create: `ui/settings/ConnectionPage.kt`, `ui/settings/AdvancedPage.kt` (under `android/app/src/main/java/com/f1controller/`)
- Modify: `MainActivity.kt` (pass the page composables into `SettingsScreen`)

**Interfaces:**
- Produces: `ConnectionPage(transport: UdpTransport, discovery: DiscoveryListener, pairingStatus: PairingStatus, onRepair: () -> Unit, onForget: () -> Unit)` and `AdvancedPage(transport: UdpTransport, steeringProcessor: SteeringProcessor, onOpenLogger: () -> Unit)`
- Consumes: the kit (Task 2) and `pairingStatusText` (existing, `ui/PairingSheet.kt`).

- [ ] **Step 1: Build Connection.**
  - Preview: a phone → PC line drawing, the found address, and a ValuePill reading "Connected · N ms" (RTT avg) when connected, "Looking for PC…" while searching, or "Not paired".
  - Rows, using the spec copy:
    - "Paired PC", whose line is `pairingStatusText(status)`, with OutlinePills "Re-pair" and "Forget".
    - "Connect when the app opens" / "Finds your PC on this Wi-Fi by itself", with a switch bound to `transport.autoConnect`.
    - "Connection": PrimaryPill "Connect"/"Disconnect", keeping the existing click logic, including the `transport.auth == null -> onRepair()` branch.
- [ ] **Step 2: Build Advanced.**
  - Preview: the existing debug rows (packets, pongs, RTT avg/p99, send rate, steering angle and value, beta) in TextDim/Text.
  - Rows:
    - "Server IP" and "Server port": text fields with today's behaviour, including the discovery refresh.
    - "Steering axis": chips over `SteeringAxis.entries`. Changing it resets `centerOffsetRad`, as today.
    - "Filter gain" (beta, 0.01–1.0).
    - "IMU logger", with a chevron that calls `onOpenLogger`.
  - Lines: Steering axis "Horizon is the one to use; others are for testing". Filter gain "Higher trusts gravity more: less drift, more shake". IP/port "Filled in when your PC is found".
- [ ] **Step 3: Build.** Expected: BUILD SUCCESSFUL. Run the tests and expect them to PASS.
- [ ] **Step 4: Commit** `feat(phone): connection and advanced settings pages`

### Task 4: Pedals and Layout pages, Swap sides

**Files:**
- Create: `ui/settings/PedalsPage.kt`, `ui/settings/LayoutPage.kt`
- Modify: `ui/PedalSettings.kt` (add `var swapPedals by mutableStateOf(false)`)
- Modify: `MainActivity.kt:276-325` (load/save `swap_pedals`)
- Modify: `ui/ControllerScreen.kt:227-280` (the pedal slots read `pedalSlots(...)`; zero both pedals when `swapPedals` changes)
- Modify: `ui/settings/SettingsLogic.kt`; Test: `SettingsLogicTest.kt`

**Interfaces:**
- Produces:
  - `enum class Pedal { BRAKE, THROTTLE }`
  - `fun pedalSlots(swap: Boolean): Pair<Pedal, Pedal>`, giving the (left, right) slots
  - `fun pedalModeLine(asButton: Boolean): String`
  - `PedalsPage(pedalSettings: PedalSettings)` (no transport parameter: preview touches never reach the PC)
  - `LayoutPage(pedalSettings: PedalSettings)`
- Consumes: `VerticalPedal` (existing public composable in `ControllerScreen.kt`) and the kit.

- [ ] **Step 1: Write failing tests:**
  - `pedalSlotsDefault`: `pedalSlots(false) == BRAKE to THROTTLE`
  - `pedalSlotsSwapped`: `pedalSlots(true) == THROTTLE to BRAKE`
  - `pedalModeLines`: `pedalModeLine(false) == "Slider: drag up for more pressure"` and `pedalModeLine(true) == "Button: press for full, let go for none"`
- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Implement the logic and Swap sides.**
  - The controller draws the left slot as `pedalSlots(swap).first` and the right as `.second`. Each slot keeps its pedal's colour, label and transport field.
  - A `LaunchedEffect(pedalSettings.swapPedals)` zeroes both values and both transport fields.
  - Persist `swap_pedals`.
- [ ] **Step 4: Build the Pedals page.**
  - Preview: two `VerticalPedal`s with local state only.
  - Rows: "Brake" and "Throttle", each with `PaletteChips(listOf("Slider","Button"))` and `pedalModeLine` as its line.
- [ ] **Step 5: Build the Layout page.**
  - Preview: a mini driving-screen silhouette, with the pedals coloured Cyan (throttle) and Red (brake) placed by `pedalSlots`.
  - Row: "Swap sides" / "Throttle on the left, brake on the right", with a switch.
- [ ] **Step 6: Run the tests and build.** Expected: PASS, BUILD SUCCESSFUL.
- [ ] **Step 7: Commit** `feat(phone): pedals and layout pages, swap sides`

### Task 5: Steering and Engineer pages

**Files:**
- Create: `ui/settings/SteeringPage.kt`, `ui/settings/EngineerPage.kt`
- Modify: `engineer/EngineerRadio.kt` (add `val lastCall: StateFlow<RadioCall?>`, set wherever `_current` gets a non-null call and never cleared)
- Delete: `ui/SettingsScreen.kt` (all of its controls now live in pages)

**Interfaces:**
- Produces: `SteeringPage(steeringProcessor: SteeringProcessor)` and `EngineerPage(engineerSettings: EngineerSettings, engineerRadio: EngineerRadio)`
- Consumes: `SteeringProcessor.applyCurve(x, curve)` (existing), `steeringProcessor.currentAngleDeg`, `calibrateCenter()`, `reset()`, and the kit.

- [ ] **Step 1: Build Steering.**
  - Preview: the controller's steering arc (dotted, with a Cyan marker) and its mini wheel, rotated by the live angle sampled at 30 Hz with `produceState`, as in `ControllerScreen`.
  - Under it: the angle in large Condensed type, then the response curve (`applyCurve` over 0..1, Cyan, with a dashed linear reference). The dot at the current `|angle|/lock` sits on the curve, with "N% steering" in mono TextDim.
  - Rows, using the spec copy:
    - Lock angle (30–180, ValuePill "±N°")
    - Centre smoothing (0–1, "N%")
    - Deadzone (0–10, "N.N°")
    - Set centre: PrimaryPill "Set" → `calibrateCenter()`
    - "Reset filter & calibration": OutlinePill "Reset" → `reset()`, with the line "Back to the default filter and centre"
- [ ] **Step 2: Build Engineer.**
  - Preview: the `lastCall` text, or the empty line "No calls yet", plus OutlinePill "Test call", which plays today's radio check.
  - Rows:
    - "How much the engineer talks", with chips over `ENGINEER_LEVEL_NAMES` and the line "Quiet: only things to act on. Talkative: sector colours too".
    - "Voice": a switch whose line is "Spoken calls", or "Text-to-speech isn't ready on this phone" in Red when `voiceAvailable` is false.
    - "Vibration": a switch, with the line "Buzz on important calls".
- [ ] **Step 3: Delete the old `ui/SettingsScreen.kt`** and wire every page into `SettingsScreen`.
- [ ] **Step 4: Run the tests and build.** Expected: PASS, BUILD SUCCESSFUL.
- [ ] **Step 5: Commit** `feat(phone): steering and engineer pages`

### Task 6: About page and first-launch walkthrough

**Files:**
- Create: `ui/settings/AboutPage.kt`, `ui/Walkthrough.kt`
- Modify: `MainActivity.kt:223-228` (auto-pair gating), plus `loadSettings`/`saveSettings` (`walkthrough_seen`)
- Modify: `ui/settings/SettingsLogic.kt`; Test: `SettingsLogicTest.kt`

**Interfaces:**
- Produces:
  - `fun shouldShowWalkthrough(seen: Boolean, isPaired: Boolean, hasSavedSettings: Boolean): Boolean`
  - `fun shouldAutoOpenPairing(status: PairingStatus, alreadyOpened: Boolean, walkthroughShowing: Boolean): Boolean`
  - `Walkthrough(onPair: () -> Unit, onDone: () -> Unit)`
  - `AboutPage(versionName: String, onHowItWorks: () -> Unit)`
- Consumes: `PairingStore.isPaired` (existing) and the kit.

- [ ] **Step 1: Write failing tests:**
  - `walkthroughFreshInstall`: `shouldShowWalkthrough(false, false, false) == true`
  - `walkthroughOnUpgradeIsSkipped`: `shouldShowWalkthrough(false, true, false) == false` and `shouldShowWalkthrough(false, false, true) == false`
  - `walkthroughSeen`: `shouldShowWalkthrough(true, false, false) == false`
  - `autoPairWaitsForWalkthrough`: `shouldAutoOpenPairing(NEEDS_PAIRING, false, true) == false` and `shouldAutoOpenPairing(NEEDS_PAIRING, false, false) == true`
  - `autoPairOnlyOnce`: `shouldAutoOpenPairing(NEEDS_PAIRING, true, false) == false`
- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Implement the logic and gating.**
  - `hasSavedSettings` is true when the prefs contain any of today's keys (for example `lock_angle` or `auto_connect`).
  - Work out `shouldShowWalkthrough` once in `onCreate`. When it is false and the pref is unset, write `walkthrough_seen = true`, so upgrades never see it.
  - `computePairingStatus` uses `shouldAutoOpenPairing`.
- [ ] **Step 4: Build the walkthrough.**
  - Three full-screen steps on the radial ground. Each has a simple line illustration in Palette colours, a 28 sp Condensed title and one TextDim line:
    - **Pair:** "Scan the code on the FullLock PC window"
    - **Hold:** "Hold the phone like a wheel, then set the centre"
    - **Drive:** "Pedals at the edges, buttons under your thumbs"
  - Controls: "Skip" (OutlinePill), and "Next" or "Done" (PrimaryPill). On the Pair step, Next sets `walkthrough_seen` and opens the pairing sheet when unpaired. Done sets `walkthrough_seen`.
- [ ] **Step 5: Build About.**
  - Preview: `ic_launcher_foreground`, "FullLock" and `versionName` (from `packageManager.getPackageInfo(packageName, 0).versionName`).
  - Rows, each with a chevron:
    - "How it works" / "Pair, hold, drive in three steps" → walkthrough
    - "Your data" / "Inputs stay on your Wi-Fi, signed with your pairing key" (no action)
    - "Source code" / "github.com/VictorBashayi/fulllock" → `ACTION_VIEW`
    - "Licence" / "MIT, open source" (no action)
- [ ] **Step 6: Run the tests and build.** Expected: PASS, BUILD SUCCESSFUL.
- [ ] **Step 7: Commit** `feat(phone): about page and first-launch walkthrough`

### Task 7: Device verification

**Files:** none (evidence goes in the scratchpad).

- [ ] **Step 1:** Install the debug build on the phone (`./gradlew :app:installDebug`). Capture with `adb exec-out screencap -p`:
  - each of the seven pages
  - the driving screen with Swap sides off and on
  - the pairing sheet
  - the Logger, opened from Advanced, then Back, which must land on Advanced
- [ ] **Step 2: Upgrade path.** On the phone, which already has a pairing key, confirm that no walkthrough appears and that all settings kept their values.
- [ ] **Step 3: Fresh path.** Run `adb shell pm clear io.github.victorbashayi.fulllock` on a debug install. Only do this after asking the user, because it wipes their pairing on that install. Confirm the walkthrough shows first and the pairing sheet follows it.
- [ ] **Step 4: Pedals preview.** Hold a preview pedal while connected. The PC must show no brake or throttle input.
- [ ] **Step 4b: Swap release.** Put the throttle in slider mode and drag it up. Open Settings, toggle Swap sides and return. The PC must read 0 on both pedals.
- [ ] **Step 5:** Fix anything found. Commit any fixes as `fix(phone): …`.
