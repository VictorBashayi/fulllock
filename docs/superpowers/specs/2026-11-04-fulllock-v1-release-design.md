# FullLock v1.0 — Public Release Design

Date: 2026-11-04
Status: Draft for review

## Intent

**What the user said:**
- Package the F1 controller (Windows receiver + Android app) for a **public release** that strangers download.
- Users will almost certainly struggle with setup, so driver setup must be as automatic as possible.
- HidHide should be configured automatically, only when double pad readings would occur.
- Include the Android APK in v1.0.
- Run a full security audit before release.
- Rename to **FullLock** (avoid "F1" trademark in the product name).

**Success criteria:**
- A stranger with a DS4 or an Android phone can go from GitHub Release page to driving in F1 without reading source code or opening HidHide/ViGEm tools by hand.
- The game sees exactly one controller in pad mode, on any USB port, without manual steps.
- After FullLock exits (cleanly or by crash), the user's DS4 is visible to other games again.
- No secrets or personal data in the public repo or its history; the security audit has no open High/Critical findings at release.

**Out of scope for v1.0:** a GUI (planned as a separate project after v1.0), Play Store publishing, R8/minification, auto-update inside the app.

## Decisions

| Topic | Decision |
|---|---|
| Audience | Public, via GitHub Releases |
| UI | Existing console app, polished; GUI is a later, separate project |
| Name | **FullLock** — tagline "Gyro steering for F1® games — phone or DualShock 4." |
| Repo | Rename `VictorBashayi/f1-phone-controller` → `VictorBashayi/fulllock`, make public after audit |
| Windows exe | `FullLock.exe` (assembly name only; C# namespaces stay `F1Controller`) |
| Android package ID | `io.github.victorbashayi.fulllock` (changed before any public install) |
| Version | Shared across Windows + Android; v1.0.0 |
| License | MIT |
| Code signing (Windows) | Unsigned for v1.0, README explains SmartScreen; apply to SignPath Foundation in parallel |
| Android signing | New release keystore; user backs up `.jks` + password in two places; CI gets it via GitHub secrets |
| Drivers | Installer installs ViGEmBus if missing (required) and offers HidHide (optional checkbox, pad mode) |
| HidHide config | Automatic at runtime; hide only while FullLock runs, restore on exit / next launch after crash |

## 1. Windows package

**Build:** `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` → one `FullLock.exe` (~70 MB, no .NET install needed). `AssemblyName` changes to `FullLock`; `Version` set from the release tag.

**Installer (Inno Setup):**
- Installs to `Program Files\FullLock`. Per-machine install (admin), needed anyway for drivers.
- Start Menu shortcuts: *FullLock (Phone)* → `FullLock.exe`, *FullLock (DS4 pad)* → `FullLock.exe --ds4`. Optional desktop shortcuts.
- ViGEmBus: bundles the official last release installer (v1.22.0, ViGEmBus is archived upstream but still the standard); runs it silently only if the driver service is absent. License permitting redistribution to be confirmed during implementation; if not redistributable, the installer downloads it from the official GitHub release with a hash check.
- HidHide: same approach, behind an unchecked-by-default checkbox labelled for DS4 pad users. Reboot prompt passed through if the driver installer requests one.
- Windows Firewall: adds an inbound rule for `FullLock.exe`, UDP, **Private profile only** (phone mode needs UDP 5555 inbound; telemetry 20777 is localhost from the game). Removed on uninstall.
- Uninstaller: removes app, shortcuts, firewall rule; removes FullLock's own HidHide entries (whitelist + any device it hid); leaves drivers installed (other apps may use them) and asks whether to delete user settings.

**Portable zip:** same `FullLock.exe` + README; no drivers, no firewall rule (README explains; app prints guidance).

**File locations (Program Files isn't writable):**
- Settings: `%APPDATA%\FullLock\pad_settings.json`. On first run, if a `pad_settings.json` exists next to the exe (the dev setup, lock 45°), migrate it.
- Logs (engineer, transport, correlation): `%LOCALAPPDATA%\FullLock\logs\`. Currently written to the working directory, which fails under Program Files.
- HidHide restore state: `%LOCALAPPDATA%\FullLock\hidhide-state.json`.

## 2. Runtime checks and HidHide auto-configuration

**Startup checks (both modes):**
- ViGEmBus missing → clear message ("FullLock needs the ViGEmBus driver to create a virtual Xbox controller"), the official download link, exit code ≠ 0. Today this surfaces as an exception.
- Pad mode, no DS4 found → existing reconnect loop, plus a one-line hint (USB cable, close DS4Windows/Steam Input).

**HidHide auto-config (pad mode only, after the virtual pad is up):**
1. Locate `HidHideCLI.exe` via the HidHide install location (registry / Program Files). Not installed → print "Games may see your DS4 twice. Install HidHide: <link>" and continue.
2. Query: cloak state, hidden device list, whitelisted apps.
3. Determine the DS4's current HID device instance path(s) (from the HidSharp device FullLock already opened — the path changes per USB port).
4. If FullLock.exe isn't whitelisted, or the DS4 path isn't hidden, or cloak is off: write the restore state file *first* (what FullLock is about to add/change), then apply via CLI (`--app-reg`, `--dev-hide`, `--cloak-on`). Print one line: "Hid your real DS4 from games so F1 sees only one controller."
5. Order matters: FullLock must be whitelisted before the device is hidden, or it would hide the pad from itself.

**Restore:**
- On exit (normal, Ctrl+C, console close via `SetConsoleCtrlHandler`): undo only what FullLock added — unhide the devices it hid, turn cloak off only if FullLock turned it on. The whitelist entry stays (harmless, avoids churn).
- On next launch, if a restore state file exists (crash, power loss), restore first, then proceed.
- Never touches devices/settings the user configured themselves.

**HidHideCLI wrapper** is a small interface (`IHidHide`) so the decision logic is unit-tested with a fake; only the thin CLI adapter talks to the real tool. Requires verifying whether CLI calls need elevation on a stock install (on the dev PC they don't).

## 3. Android release

- `applicationId` → `io.github.victorbashayi.fulllock`; app label → "FullLock"; `versionName` 1.0.0, `versionCode` 1 (Gradle reads both from one place shared with Windows).
- `release` signingConfig reads keystore path/passwords from environment variables / `keystore.properties` (gitignored). No secrets in Gradle files.
- R8 stays off for v1.0.
- Manifest review in the audit (e.g. `allowBackup`, legacy storage permissions, exported activities).
- Output `FullLock-1.0.0.apk` attached to the GitHub Release; README covers sideloading.
- Keystore generation is a one-time step done together with the user; `.gitignore` gains `*.jks`, `*.keystore`, `keystore.properties`.

## 4. CI / release pipeline (GitHub Actions)

- On pull request / push to main: build + run receiver tests (149) and Android unit tests.
- On tag `v*`: tests → `dotnet publish` → Inno Setup compile → portable zip → signed APK (keystore from secrets) → SHA-256 checksums file → **draft** GitHub Release with all assets. The user reviews and publishes.
- Secrets: `ANDROID_KEYSTORE_B64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`.
- Actions pinned to commit SHAs (supply-chain hygiene).

## 5. Public readiness and release gate

Order:
1. Rename (exe, package ID, app label, README, repo).
2. Implement sections 1–4.
3. README for strangers: what it is, requirements, 3-step setup per mode, SmartScreen + sideloading notes, troubleshooting (double pad, no pad found, firewall, phone can't find PC). LICENSE (MIT). Trademark disclaimer ("Not affiliated with Formula One…").
4. **Full security audit** (security-audit skill) — scope: Windows receiver (UDP input parsing from the network, discovery beacon, telemetry parsing, HidHide/CLI invocation, file paths), Android app (network, permissions, manifest), installer (firewall scope, privilege), CI (secrets, action pinning), git history (secrets, personal data such as MAC addresses/IPs). Fix High/Critical before release; Medium/Low triaged with the user.
5. Make repo public → tag `v1.0.0` → review draft release → publish.

## Testing

- Unit: HidHide decision logic (fake `IHidHide`): already-configured → no-op; new USB port path → hides only the new path; not whitelisted → whitelist before hide; restore undoes only FullLock's changes; crash-state file restored on startup. Settings path + migration. Version wiring.
- Existing 149 receiver tests + Android unit tests stay green.
- Manual, on the dev PC: fresh install from the installer on a clean user profile; pad mode with HidHide entries removed (auto-config kicks in, F1 sees one pad); switch USB port; kill the process (restore on next launch); uninstall (HidHide entries gone, DS4 visible). Phone mode via installed APK through the firewall rule.
- Ideally one test on a second Windows machine / VM without the drivers (installer path).

## Risks

- **ViGEmBus is archived upstream.** Still works on Windows 11; if a future Windows update breaks it, FullLock breaks too. Accepted for v1.0; noted in README.
- **Driver redistribution licenses** — to verify; fallback is download-at-install with hash check.
- **HidHideCLI elevation** on stock installs — if it requires admin, the app requests elevation only for that step (or the installer pre-registers the whitelist and FullLock only toggles device hiding).
- **Unsigned exe** → SmartScreen friction and possible AV false positives on a self-contained single-file exe.
