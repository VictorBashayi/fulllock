# FullLock v1.0 Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship F1 Phone Controller publicly as **FullLock v1.0** — Windows installer + portable zip + signed APK on a GitHub Release — with automatic HidHide setup and a passed security audit.

**Architecture:** Receiver gets a rename, per-user file locations, driver checks, and a `HidHideGuard` (pure decision logic behind an `IHidHide` interface, thin CLI adapter) wired into pad mode. Packaging is an Inno Setup script plus `dotnet publish` single-file, driven by GitHub Actions on `v*` tags; Android gains a release signing config fed from secrets.

**Tech Stack:** .NET 8 (C#, xUnit, HidSharp, Nefarius.ViGEm.Client), Kotlin/Gradle (AGP 8.13), Inno Setup 6, GitHub Actions, HidHideCLI 1.5.

**Spec:** `docs/superpowers/specs/2026-11-04-fulllock-v1-release-design.md`

## Global Constraints

- Product name **FullLock**; tagline "Gyro steering for F1® games — phone or DualShock 4."; disclaimer "Not affiliated with Formula One, Formula One Licensing B.V., Codemasters or EA."
- Exe `FullLock.exe` (`AssemblyName` only). C# namespaces stay `F1Controller.*`. Android `namespace` stays `com.f1controller`; only `applicationId` becomes `io.github.victorbashayi.fulllock`.
- Single version source: repo-root `VERSION` file, content `1.0.0`. Android `versionCode = major*10000 + minor*100 + patch`.
- Settings: `%APPDATA%\FullLock\pad_settings.json`. Logs: `%LOCALAPPDATA%\FullLock\logs\`. HidHide restore state: `%LOCALAPPDATA%\FullLock\hidhide-state.json`.
- HidHide: change only what FullLock adds; whitelist **before** hide; write restore state **before** applying; restore on exit and on next launch; never throw out of HidHide code into the drive loop.
- Firewall rule: inbound UDP for `FullLock.exe`, Private profile only.
- No secrets in the repo: keystore + passwords only via env vars / gitignored `keystore.properties` / GitHub secrets.
- GitHub Actions pinned to full commit SHAs. Releases are created as **drafts**.
- Never `git stash` in this repo (user's own uncommitted edits may appear).
- Running `F1Controller.exe`/`FullLock.exe` locks `bin\`; close it before rebuilding.
- Build/test commands: `dotnet test windows-receiver/F1Controller.sln`; `cd android && ./gradlew.bat assembleDebug testDebugUnitTest`.

## Review Focus

1. **HidHideCLI fails or needs admin on a stock install** → FullLock keeps driving, prints one hint line, no exception reaches the user. (Task 4 test `Guard_CliThrows_LogsAndContinues`.)
2. **DS4 over Bluetooth** has a different HID path shape (`HID\{00001124-...}_VID&0002054c_PID&09cc\...`) → conversion must produce the exact instance path HidHide lists. (Task 4 test `ToInstancePath_Bluetooth`.)
3. **Pad replugged into another USB port mid-session** → the new path gets hidden too, and exit restores every path hidden this session. (Task 4 test `Guard_SecondPath_HidesAndRestoresBoth`.)
4. **Corrupt or empty restore-state file** (power loss mid-write) → warn, delete, continue; never crash at startup. (Task 4 test `Guard_CorruptState_IgnoredAndDeleted`.)
5. **Paths with spaces / non-ASCII user names** in exe path passed to the CLI → argument quoted correctly. (Task 5 test `Cli_BuildArgs_QuotesPathWithSpaces`.)

---

### Task 1: Rename to FullLock + single version source

**Files:**
- Create: `VERSION` (content `1.0.0` + newline)
- Modify: `windows-receiver/F1Controller/F1Controller.csproj` (AssemblyName, Version, Product)
- Modify: `windows-receiver/F1Controller/Program.cs` (console title, banner, usage text: "F1 Phone Controller" → "FullLock"; usage examples `F1Controller.exe` → `FullLock.exe`)
- Modify: `windows-receiver/F1Controller/Pad/PadMode.cs` (banner "F1 Controller — DS4 pad mode" → "FullLock — DS4 pad mode")
- Modify: `android/app/build.gradle.kts` (applicationId, versionCode/versionName from `VERSION`)
- Modify: `android/app/src/main/res/values/strings.xml` (`app_name` → `FullLock`)
- Modify: `.vscode/tasks.json`, `.vscode/launch.json` (exe name if referenced)
- Test: `windows-receiver/F1Controller.Tests/VersionTests.cs`

**Interfaces:**
- Produces: assembly `FullLock`, `typeof(Program).Assembly.GetName().Version == 1.0.0.0`; output `bin/<cfg>/net8.0/FullLock.exe`.

- [ ] **Step 1: Write failing test** `Assembly_Version_MatchesVersionFile`: reads `VERSION` (walk up from `AppContext.BaseDirectory` to the dir containing it), asserts `typeof(F1Controller.Pad.PadSettings).Assembly.GetName().Version!.ToString(3) == fileText.Trim()` and assembly name `== "FullLock"`.
- [ ] **Step 2: Run** `dotnet test windows-receiver/F1Controller.sln --filter VersionTests` → FAIL (name is F1Controller, version 1.0.0.0 vs file missing).
- [ ] **Step 3: csproj**: `<AssemblyName>FullLock</AssemblyName>`, `<Product>FullLock</Product>`, `<Version>$([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)../../VERSION').Trim())</Version>`. Test project references the main project by path — check nothing references `F1Controller.dll` by file name.
- [ ] **Step 4: Gradle**: read `rootProject.file("../VERSION").readText().trim()`, split on `.`, `versionName` = string, `versionCode` = `major*10000+minor*100+patch`; `applicationId = "io.github.victorbashayi.fulllock"`. Leave `namespace`.
- [ ] **Step 5: Strings** in Program.cs, PadMode.cs, strings.xml, .vscode files as listed.
- [ ] **Step 6: Run** both build/test commands → receiver all pass (150), Android build OK. `ls windows-receiver/F1Controller/bin/Debug/net8.0/FullLock.exe` exists.
- [ ] **Step 7: Commit** `Rename to FullLock; single VERSION source (1.0.0)`.

Note for user (report at end of task): the dev HidHide whitelist has the old `F1Controller.exe` path; Task 5 auto-registers `FullLock.exe`, so nothing manual is needed.

---

### Task 2: Per-user file locations + settings migration

**Files:**
- Create: `windows-receiver/F1Controller/AppPaths.cs`
- Modify: `windows-receiver/F1Controller/Pad/PadSettings.cs:19` (remove `DefaultPath`)
- Modify: `windows-receiver/F1Controller/Pad/PadMode.cs` (use `AppPaths`)
- Modify: `windows-receiver/F1Controller/Program.cs:138` (engineer log), `Transport/UdpReceiver.cs:89`, `Validation/CorrelationLogger.cs:34`
- Test: `windows-receiver/F1Controller.Tests/AppPathsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class AppPaths(string roamingDir, string localDir, string exeDir)` with `static AppPaths Default` (roaming = `%APPDATA%\FullLock`, local = `%LOCALAPPDATA%\FullLock`, exeDir = `AppContext.BaseDirectory`)
  - `string PadSettingsFile` (`roamingDir\pad_settings.json`), `string LogsDir` (`localDir\logs`), `string HidHideStateFile` (`localDir\hidhide-state.json`)
  - `string LogFile(string prefix, string extension)` → `LogsDir\{prefix}_{yyyyMMdd_HHmmss}.{extension}`, creates `LogsDir`
  - `void EnsureCreated()` → creates roaming + local dirs
  - `bool MigrateLegacySettings()` → if `exeDir\pad_settings.json` exists and `PadSettingsFile` doesn't, copy it (not move; exe dir may be read-only) and return true

- [ ] **Step 1: Write failing tests** (temp dirs per test):
  - `PadSettingsFile_IsInRoamingDir`, `LogFile_IsInLocalLogsDir_AndCreatesDir` (asserts dir exists, name starts with `engineer_log_`, ends `.txt`)
  - `Migrate_CopiesLegacyFile_WhenNoNewFile` (legacy content `{"LockDeg":45}` → new file has same content, returns true, legacy still exists)
  - `Migrate_DoesNotOverwrite_ExistingNewFile` (returns false, new content unchanged)
  - `Migrate_NoLegacyFile_ReturnsFalse`
- [ ] **Step 2: Run** `--filter AppPathsTests` → FAIL (type missing).
- [ ] **Step 3: Implement** `AppPaths`.
- [ ] **Step 4: Wire**: PadMode does `var paths = AppPaths.Default; paths.EnsureCreated(); if (paths.MigrateLegacySettings()) Console.WriteLine("[Settings] Moved your pad settings to " + paths.PadSettingsFile);` then `PadSettings.Load(paths.PadSettingsFile)` and saves to the same. Phone mode: `EnsureCreated()` at startup; the three log writers take their path from `AppPaths.Default.LogFile("engineer_log","txt")` / `("transport_log","csv")` / `("correlation_log","csv")`. Print the logs dir once in the phone-mode banner.
- [ ] **Step 5: Run** full receiver tests → all pass. Update `PadSettingsTests` if it used `DefaultPath`.
- [ ] **Step 6: Commit** `Settings to %APPDATA%\FullLock, logs to %LOCALAPPDATA%\FullLock\logs`.

---

### Task 3: Driver checks with clear exits

**Files:**
- Create: `windows-receiver/F1Controller/Drivers.cs`
- Modify: `windows-receiver/F1Controller/Pad/PadMode.cs` (ViGEm init block), `windows-receiver/F1Controller/Program.cs` (phone-mode ViGEm init)
- Test: `windows-receiver/F1Controller.Tests/DriversTests.cs`

**Interfaces:**
- Produces:
  - `public static class Drivers` — `bool IsServiceInstalled(string serviceName)` (registry key `HKLM\SYSTEM\CurrentControlSet\Services\<name>` exists), `bool ViGEmBusInstalled => IsServiceInstalled("ViGEmBus")`, `bool HidHideInstalled => IsServiceInstalled("HidHide")`
  - `const string ViGEmBusUrl = "https://github.com/nefarius/ViGEmBus/releases/latest"`, `const string HidHideUrl = "https://github.com/nefarius/HidHide/releases/latest"`
  - `static string ViGEmMissingMessage` — exact copy: `"FullLock needs the ViGEmBus driver to create a virtual Xbox controller.\nInstall it from " + ViGEmBusUrl + " (or re-run the FullLock installer), then start FullLock again."`
  - Exit code `2` = required driver missing.

- [ ] **Step 1: Write failing tests**: `IsServiceInstalled_UnknownService_False` (name `"FullLockNoSuchService_" + Guid`), `IsServiceInstalled_KnownService_True` (`"Tcpip"`, present on every Windows), `ViGEmMissingMessage_ContainsUrl`.
- [ ] **Step 2: Run** `--filter DriversTests` → FAIL.
- [ ] **Step 3: Implement** with `Microsoft.Win32.Registry.LocalMachine.OpenSubKey(...)` (read-only).
- [ ] **Step 4: Wire**: in both modes, before constructing `XInputController` and unless `--no-vigem`: if `!Drivers.ViGEmBusInstalled` → print `ViGEmMissingMessage`, if interactive also open the URL (`Process.Start(new ProcessStartInfo(url){UseShellExecute=true})` in try/catch), wait for a key press when interactive (so a double-clicked window doesn't vanish), return 2. Keep the existing catch around `Connect()` but make it print the same message and return 2 instead of continuing with `xinput = null`.
- [ ] **Step 5: No-pad hint**: in PadMode, if the reader has not connected 3 s after start, print once: `"[DS4] No DualShock 4 found. Plug it in with a USB data cable, and close DS4Windows / Steam Input if they're running."` (keep the reconnect loop as is).
- [ ] **Step 6: Run** full receiver tests → pass. Manual: `FullLock.exe --ds4` with ViGEmBus present still starts; with the pad unplugged the hint appears once.
- [ ] **Step 7: Commit** `Exit with a clear message when ViGEmBus is missing`.

---

### Task 4: HidHide guard (pure logic)

**Files:**
- Create: `windows-receiver/F1Controller/Pad/HidHide/IHidHide.cs`, `HidHidePaths.cs`, `HidHideGuard.cs`, `HidHideRestoreState.cs`
- Test: `windows-receiver/F1Controller.Tests/HidHidePathsTests.cs`, `HidHideGuardTests.cs` (with `FakeHidHide` in the test file)

**Interfaces:**
- Produces:
  - `public interface IHidHide { bool IsCloaked(); IReadOnlyList<string> HiddenDevices(); IReadOnlyList<string> RegisteredApps(); void RegisterApp(string exePath); void UnregisterApp(string exePath); void Hide(string instancePath); void Unhide(string instancePath); void SetCloak(bool on); }` — implementations throw `HidHideException : Exception` on failure.
  - `public static class HidHidePaths { static string ToInstancePath(string hidSharpDevicePath); }`
  - `public sealed record HidHideRestoreState(List<string> HiddenByUs, bool CloakTurnedOnByUs)` + `static HidHideRestoreState? Load(string path)` (null if missing; throws `InvalidDataException` if corrupt), `void Save(string path)` (write to `path.tmp` then `File.Move(..., overwrite:true)`).
  - `public sealed class HidHideGuard(IHidHide hidHide, string exePath, string statePath, Action<string> log)` with `void RestorePending()`, `void EnsureHidden(string instancePath)`, `void Restore()`, `void Cleanup()` (Restore, then `UnregisterApp(exePath)`, delete state). **No public method throws.** Instance paths compared case-insensitively.

- [ ] **Step 1: Write failing path tests** (`HidHidePathsTests`):
  - `ToInstancePath_Usb`: `@"\\?\hid#vid_054c&pid_09cc&mi_03#7&18eac8c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"` → `@"HID\VID_054C&PID_09CC&MI_03\7&18EAC8C&0&0000"`
  - `ToInstancePath_Bluetooth`: `@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&09cc#8&3a65874a&a&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"` → `@"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&09CC\8&3A65874A&A&0000"`
  - Assertions compare with `StringComparer.OrdinalIgnoreCase` (HidHide lists mixed case; Windows instance IDs are case-insensitive).
- [ ] **Step 2: Implement** `ToInstancePath`: strip leading `\\?\`, drop the last `#{...}` segment, replace remaining `#` with `\`, upper-case. Run `--filter HidHidePathsTests` → PASS.
- [ ] **Step 3: Write failing guard tests** (`FakeHidHide` records calls in order into a `List<string>` like `"reg:<exe>"`, `"hide:<path>"`, `"cloak:on"`; can be told to throw):
  - `Guard_AlreadyConfigured_NoChanges` (app registered, path hidden, cloaked → no mutating calls, no state file)
  - `Guard_Fresh_RegistersBeforeHideThenCloaks` (call order exactly `reg`, `hide`, `cloak:on`; state file has `HiddenByUs=[path]`, `CloakTurnedOnByUs=true`; log contains `"Hid your real DS4 from games so F1 sees only one controller."`)
  - `Guard_StateWrittenBeforeApplying` (fake's `Hide` asserts state file already lists the path)
  - `Guard_Restore_UndoesOnlyOwnChanges` (pre-hidden user path `U` + ours `P`, cloak was on before → restore unhides `P` only, no `cloak:off`, does not unregister app, deletes state)
  - `Guard_Restore_TurnsCloakOffIfWeTurnedItOn`
  - `Guard_SecondPath_HidesAndRestoresBoth`
  - `Guard_RestorePending_RestoresCrashState` (state file exists at construction → `RestorePending()` unhides listed paths, deletes file)
  - `Guard_CorruptState_IgnoredAndDeleted` (state file `"{not json"` → log warning, file deleted, no throw)
  - `Guard_CliThrows_LogsAndContinues` (fake throws `HidHideException` on `Hide` → no exception escapes `EnsureHidden`, log contains `"HidHide"`)
  - `Guard_Cleanup_UnregistersApp`
- [ ] **Step 4: Run** `--filter HidHideGuardTests` → FAIL.
- [ ] **Step 5: Implement** `HidHideRestoreState` and `HidHideGuard`. `EnsureHidden` order: query state → compute needed changes → merge into in-memory state + `Save` → `RegisterApp` (if needed) → `Hide` → `SetCloak(true)` (if was off) → log one line. Every public method wraps its body in `catch (Exception ex) { log($"[HidHide] {ex.Message} — games may see your DS4 twice."); }`.
- [ ] **Step 6: Run** full receiver tests → pass.
- [ ] **Step 7: Commit** `HidHide guard: hide the DS4 only while FullLock runs`.

---

### Task 5: HidHide CLI adapter + pad-mode wiring

**Files:**
- Create: `windows-receiver/F1Controller/Pad/HidHide/HidHideCli.cs`
- Modify: `windows-receiver/F1Controller/Pad/Ds4Reader.cs` (connected event), `windows-receiver/F1Controller/Pad/PadMode.cs`, `windows-receiver/F1Controller/Program.cs` (`--hidhide-cleanup` flag + usage line)
- Test: `windows-receiver/F1Controller.Tests/HidHideCliTests.cs`

**Interfaces:**
- Consumes: `IHidHide`, `HidHideGuard`, `HidHidePaths.ToInstancePath`, `AppPaths.HidHideStateFile`, `Drivers.HidHideInstalled`, `Drivers.HidHideUrl`.
- Produces:
  - `public sealed class HidHideCli(string cliPath) : IHidHide`; `static HidHideCli? Locate()` → registry `HKLM\SOFTWARE\Nefarius Software Solutions e.U.\HidHide` value `Path` + `x64\HidHideCLI.exe`, null if key/file missing.
  - `internal static IReadOnlyList<string> ParseList(string stdout, string flag)` — each line `--dev-hide "<p>"` / `--app-reg "<p>"` → `<p>`; other lines ignored.
  - `internal static bool ParseCloak(string stdout)` — `true` iff trimmed output is `--cloak-on`.
  - `internal static string BuildArgs(string flag, string? value)` → `flag` or `flag "value"`; value must not contain `"` (throw `ArgumentException`).
  - Process run: `CreateNoWindow`, `UseShellExecute=false`, redirect stdout/stderr, 5 s timeout (kill + throw `HidHideException` on timeout), non-zero exit → `HidHideException` with stderr.
  - `Ds4Reader`: `public event Action<string>? OnConnected;` raised with `device.DevicePath` right after a successful `TryOpen`.
  - CLI flag `--hidhide-cleanup`: runs `HidHideGuard.Cleanup()` and exits 0 (used by the uninstaller).

- [ ] **Step 1: Write failing tests**: `ParseList_DevHide` (input copied from real output, 3 lines incl. Bluetooth path → 3 paths), `ParseList_AppReg_PathWithSpaces`, `ParseCloak_On`, `ParseCloak_Off`, `Cli_BuildArgs_QuotesPathWithSpaces` (`--app-reg`, `C:\Program Files\FullLock\FullLock.exe` → `--app-reg "C:\Program Files\FullLock\FullLock.exe"`), `Cli_BuildArgs_RejectsQuote`.
- [ ] **Step 2: Run** `--filter HidHideCliTests` → FAIL.
- [ ] **Step 3: Implement** `HidHideCli`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Wire PadMode**: after ViGEm is up — if `HidHideCli.Locate()` is null print `"[HidHide] Not installed — games may see your DS4 twice. Get it: " + Drivers.HidHideUrl` and skip; else `guard = new HidHideGuard(cli, Environment.ProcessPath!, paths.HidHideStateFile, Console.WriteLine)`, `guard.RestorePending()`, `reader.OnConnected += p => guard.EnsureHidden(HidHidePaths.ToInstancePath(p))`. On shutdown (end of `RunAsync`, in `finally`) `guard.Restore()`. Register `PosixSignalRegistration.Create(PosixSignal.SIGHUP, ...)` (console window closed) that cancels the token and calls `guard.Restore()` synchronously before returning — Windows allows ~5 s.
- [ ] **Step 6: Wire `--hidhide-cleanup`** in Program.cs arg parsing.
- [ ] **Step 7: Run** full tests → pass.
- [ ] **Step 8: Manual hardware check with the user** (DS4 on USB, F1 2021). Record results in the task report:
  1. Back up: `HidHideCLI --dev-list > %TEMP%\hidhide-dev-before.txt` and `--app-list`.
  2. `--dev-unhide` the DS4 USB paths. Start `FullLock.exe --ds4` → prints "Hid your real DS4…", `--dev-list` shows the path, F1 sees one pad.
  3. Ctrl+C → path gone from `--dev-list`. Repeat, close the window with X → gone.
  4. Start, kill the process from Task Manager → path still hidden; start again → "restored" then re-hidden; quit → gone.
  5. Different USB port → new path hidden; quit → both gone.
  6. Re-add the user's original entries from the backup if they want permanent hiding back (ask).
- [ ] **Step 9: Commit** `Pad mode: auto-configure HidHide, restore on exit`.

---

### Task 6: Windows installer, portable zip, publish script

**Files:**
- Create: `installer/FullLock.iss`, `installer/build.ps1`, `installer/drivers.json`
- Modify: `.gitignore` (`installer/out/`, `installer/deps/`)

**Interfaces:**
- Consumes: `FullLock.exe --hidhide-cleanup` (Task 5), `VERSION` (Task 1).
- Produces: `installer/build.ps1 [-Version <x.y.z>]` → `installer/out/FullLock-Setup-<v>.exe`, `installer/out/FullLock-<v>-portable.zip`. Used by CI (Task 8).

- [ ] **Step 1: Verify driver licenses.** Read the LICENSE of nefarius/ViGEmBus (expected BSD-3-Clause) and nefarius/HidHide (expected MIT) at the pinned release tags. If either forbids redistribution, switch that driver to download-at-install in Step 3 instead of bundling. Record finding in the task report.
- [ ] **Step 2: `drivers.json`**: for ViGEmBus `v1.22.0` and HidHide (latest 1.5.x release) — download URL of the x64 installer + SHA-256 (compute once from the downloaded file). `build.ps1` downloads to `installer/deps/` and fails if the hash differs.
- [ ] **Step 3: `build.ps1`**: read version (param or `VERSION`); `dotnet test`; `dotnet publish windows-receiver/F1Controller/F1Controller.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o installer/out/publish`; fetch drivers; `iscc /DAppVersion=<v> installer/FullLock.iss` (find `iscc` on PATH or `${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe`); zip `FullLock.exe` + `README.md` + `LICENSE` to the portable zip.
- [ ] **Step 4: `FullLock.iss`** — requirements:
  - `AppName=FullLock`, `AppVersion={#AppVersion}`, `DefaultDirName={autopf}\FullLock`, `PrivilegesRequired=admin`, `ArchitecturesInstallIn64BitMode=x64compatible`, `OutputBaseFilename=FullLock-Setup-{#AppVersion}`, `LicenseFile=..\LICENSE`, fixed `AppId` GUID (generate once).
  - Icons: `{group}\FullLock (Phone)` → `{app}\FullLock.exe`; `{group}\FullLock (DS4 pad)` → `{app}\FullLock.exe` params `--ds4`; desktop icons under an unchecked task.
  - Tasks: `hidhide` "Install HidHide (recommended for DualShock 4 users — stops games seeing the pad twice)" **unchecked by default**.
  - `[Run]`: ViGEmBus installer silently if `not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus')`; HidHide installer if task `hidhide` and service key absent. Flag `waituntilterminated`. Use each installer's documented silent switch (verify: ViGEmBus is WiX/MSI-based, HidHide likewise).
  - Firewall `[Run]`: `netsh advfirewall firewall add rule name="FullLock" dir=in action=allow program="{app}\FullLock.exe" protocol=UDP profile=private`, `runhidden`.
  - `[UninstallRun]`: `{app}\FullLock.exe --hidhide-cleanup` (runhidden, `RunOnceId: "HidHideCleanup"`), then `netsh advfirewall firewall delete rule name="FullLock"`.
  - `[Code]` `CurUninstallStepChanged(usPostUninstall)`: MsgBox "Delete your FullLock settings?" → `DelTree(ExpandConstant('{userappdata}\FullLock'), True, True, True)` and `{localappdata}\FullLock` on Yes.
  - `NeedRestart`: return true if a driver installer exited with 3010.
- [ ] **Step 5: Build locally** `pwsh installer/build.ps1` (or `powershell -File`) → both artifacts exist; exe size ~60–80 MB.
- [ ] **Step 6: Manual install test (with user)**: run setup → Start Menu shortcuts present; `netsh advfirewall firewall show rule name=FullLock` shows UDP/Private; DS4 shortcut drives F1; phone connects through the firewall; uninstall → shortcuts, rule and FullLock HidHide entries gone, drivers still installed.
- [ ] **Step 7: Commit** `Inno Setup installer, portable zip and build script`.

---

### Task 7: Android release signing

**Files:**
- Modify: `android/app/build.gradle.kts` (signingConfigs.release, release buildType, APK file name)
- Modify: `.gitignore` (`*.jks`, `*.keystore`, `keystore.properties`)
- Create: `android/keystore.properties.example` (keys only, no values)

**Interfaces:**
- Produces: `./gradlew.bat assembleRelease` → `android/app/build/outputs/apk/release/FullLock-<version>.apk`, signed when `FULLLOCK_KEYSTORE`, `FULLLOCK_KEYSTORE_PASSWORD`, `FULLLOCK_KEY_ALIAS`, `FULLLOCK_KEY_PASSWORD` env vars (or the same keys in `android/keystore.properties`) are present; unsigned build otherwise (so contributors can still build).

- [ ] **Step 1: gitignore** entries first; `git check-ignore android/release.jks android/keystore.properties` prints both.
- [ ] **Step 2: Gradle**: `signingConfigs.create("release")` reading env vars, falling back to `keystore.properties`; release buildType uses it only if the keystore path is set; `isMinifyEnabled = false` unchanged; rename output via `applicationVariants`/`androidComponents` to `FullLock-<versionName>.apk`.
- [ ] **Step 3: Generate the keystore with the user** (they pick the passwords; never echo them into logs or commits): `keytool -genkeypair -v -keystore %USERPROFILE%\FullLock-release.jks -alias fulllock -keyalg RSA -keysize 4096 -validity 36500`. Store outside the repo. Ask the user to save the `.jks` + passwords in two places (password manager + offline copy) **before** continuing.
- [ ] **Step 4: Build** with env vars set → `apksigner verify --print-certs FullLock-1.0.0.apk` shows the new cert (not "Android Debug"). Install on the user's phone over the existing debug app: it will refuse (different package ID + key) — tell the user to uninstall the old "F1 Controller" app first.
- [ ] **Step 5: Manual**: phone connects to FullLock.exe phone mode and drives.
- [ ] **Step 6: Commit** `Android release signing from env/keystore.properties`.

---

### Task 8: GitHub Actions CI + release

**Files:**
- Create: `.github/workflows/ci.yml`, `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `installer/build.ps1` (Task 6), Gradle release signing env vars (Task 7).
- Produces: secrets used — `ANDROID_KEYSTORE_B64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`.

- [ ] **Step 1: `ci.yml`** on `push: main` and `pull_request`: job `windows` (`windows-latest`, setup-dotnet 8, `dotnet test windows-receiver/F1Controller.sln`); job `android` (`ubuntu-latest`, setup-java 17 temurin, `./gradlew assembleDebug testDebugUnitTest` in `android/` — ensure `gradlew` is executable / committed with +x). `permissions: contents: read`.
- [ ] **Step 2: `release.yml`** on `push: tags: ['v*']`: check tag == `v` + `VERSION` content (fail otherwise); windows job runs `installer/build.ps1` (install Inno Setup via `choco install innosetup -y` if `iscc` missing); android job decodes `ANDROID_KEYSTORE_B64` to `$RUNNER_TEMP/release.jks`, sets `FULLLOCK_*` env vars, `assembleRelease`; final job collects artifacts, writes `SHA256SUMS.txt`, creates a **draft** release with `gh release create $TAG --draft --title "FullLock $TAG" --generate-notes <files>`. `permissions: contents: write` on the release job only.
- [ ] **Step 3: Pin** every `uses:` to a full commit SHA with the tag in a trailing comment (look up current SHAs for actions/checkout, actions/setup-dotnet, actions/setup-java, actions/upload-artifact, actions/download-artifact).
- [ ] **Step 4: Verify CI**: push branch → `gh run watch` → both jobs green.
- [ ] **Step 5: Verify release pipeline without publishing**: `release.yml` also has a `workflow_dispatch` trigger; on that trigger (no tag) it skips the tag check and the release job and uploads the artifacts as workflow artifacts instead. Run it with `gh workflow run release.yml`, download the artifacts, check the APK signature (`apksigner verify --print-certs`) and that the installer runs.
- [ ] **Step 6: Commit** `CI and tag-driven draft releases`.

(Secrets are added by the user in Task 11 or earlier with `gh secret set`, values never pasted into chat.)

---

### Task 9: Public-facing docs

**Files:**
- Modify: `README.md` (rewrite for end users; move developer content to `docs/DEVELOPMENT.md`)
- Create: `LICENSE` (MIT, `Copyright (c) 2026 Victor Bashayi`), `docs/DEVELOPMENT.md`
- Modify: `windows-receiver/README.md` (point to DEVELOPMENT.md)

- [ ] **Step 1: README sections**: title + tagline + disclaimer; what it does (phone mode, DS4 mode, engineer radio/dashboard); requirements (Windows 10/11 x64, F1 game with UDP telemetry on for the dashboard, Android 8+ phone on same Wi-Fi or a DS4 over USB); Install (Windows: installer, SmartScreen "More info → Run anyway" note; Android: download APK, allow install from browser); Quick start ×2 (Phone: open FullLock (Phone), open app, it finds the PC; DS4: plug in USB, hold flat and still 1 s, bank to steer, keys `[` `]` lock, `-` deadzone, `,` `.` curve); In-game settings (Steering linearity/deadzone 0, saturation); Troubleshooting (double pad → install HidHide; phone can't find PC → Private network / firewall rule; ViGEmBus missing; DS4Windows/Steam Input conflicts); Privacy (no telemetry leaves your LAN; logs location); License.
- [ ] **Step 2: Move** build/dev instructions and phase notes links from the old README into `docs/DEVELOPMENT.md`.
- [ ] **Step 3: Verify** every link resolves (`grep -o 'https\?://[^) ]*' README.md` and open each; relative links exist).
- [ ] **Step 4: Commit** `End-user README, MIT license, developer docs`.

---

### Task 10: Full security audit (release gate)

- [ ] **Step 1:** Invoke the `security-audit` skill, full workflow, scope from spec §5 item 4: receiver network input (`Transport/*` UDP parsing, `DiscoveryBeacon`, `Telemetry`/F1 packet parsing, radio packets), HidHide CLI invocation and state file, file paths/logs, installer (`FullLock.iss` privileges, firewall scope, uninstall), Android (manifest: `allowBackup`, legacy storage permissions, exported components; network code), CI (secrets handling, SHA pinning, permissions), git history (`git log -p --all` scan for keys, tokens, IPs, MAC addresses such as `<DS4 MAC>`, personal paths).
- [ ] **Step 2:** Present findings to the user ranked by severity. Fix all High/Critical (each fix: failing test → fix → test passes → commit). Medium/Low: user decides fix vs. document.
- [ ] **Step 3:** If history contains secrets/personal data that matter, agree with the user on rewriting history (`git filter-repo`) **before** going public — destructive, needs explicit approval.
- [ ] **Step 4:** Save the report to `docs/security/2026-11-audit.md` (redacted of anything sensitive) and commit.

---

### Task 11: Go public (each step confirmed with the user)

- [ ] **Step 1:** `gh repo rename fulllock` (in repo dir) → `git remote -v` shows new URL.
- [ ] **Step 2:** User sets secrets: `gh secret set ANDROID_KEYSTORE_B64 < (base64 of jks)` etc. — user runs these with `!` so values never appear in the transcript.
- [ ] **Step 3:** `gh repo edit --visibility public --accept-visibility-change-consequences`.
- [ ] **Step 4:** Ensure VERSION = `1.0.0`, push `main`, `git tag v1.0.0 && git push origin v1.0.0` → `gh run watch` → draft release with installer, zip, APK, SHA256SUMS.
- [ ] **Step 5:** User reviews the draft and publishes. Submit SignPath Foundation application (spec decision) with the public repo URL.
- [ ] **Step 6:** Update memory file `fulllock-release.md` with the release URL and status.
