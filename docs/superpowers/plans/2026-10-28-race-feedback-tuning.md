# Race Feedback Tuning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace sector calls with a level-paced lap report, judge tyre overheating on inner temperature, make the engineer pit-stop aware, and add pitch-independent "horizon" steering.

**Architecture:** The receiver's race tracker gains pit data and inner tyre temps; the engineer rules (partial class `EngineerRules`) gain a lap-report file and per-lap state; the phone sends its chattiness level as a 17th ping byte; the phone's `SteeringProcessor` gets a new angle function and default axis.

**Tech Stack:** C# / .NET 8 + xUnit (receiver), Kotlin + JUnit (phone).

**Spec:** `docs/superpowers/specs/2026-10-28-race-feedback-tuning-design.md`

## Global Constraints

- F1 2021 only (Lap Data entry 43 bytes; `pitStatus` byte 26, `numPitStops` byte 27; Car Telemetry inner tyre temps at entry offset 34, 4 bytes, same wheel order as surface at 30).
- Level codes: 0 Off, 1 Quiet, 2 Balanced, 3 Talkative. Report period: Quiet 3, Balanced 2, Talkative 1, Off none.
- Sector "matched" when |difference| < 0.05 s; differences and gaps spoken to one decimal (`0.0`, invariant culture).
- Overheating: inner > 105 °C for 5 s; mute 3 laps; re-call when ≥ called temp + 5 °C.
- Ping: 17 bytes `[0xFFFFFFFF][seq:u32][timestamp_us:u64][level:u8]`; receiver echoes the first 16; 16-byte pings mean Balanced.
- Run receiver tests with `--artifacts-path` to a scratch folder (the running receiver locks `bin/`).
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Level byte missing (old app) or out of range (e.g. 7) → treat as Balanced (2); never throw. → Task 2 test `PingLevelDefaultsAndClamps`.
2. Lap number going down or jumping by 2 (flashback across the line, dropped packets) → no report, no best-lap update. → Task 3 test `NoReportUnlessLapAdvancesByOne`.
3. S1 or S2 is 0 at the rollover (sector times missing) → report the lap time but no sector comparison, and don't store it as best. → Task 3 test `MissingSectorTimesSkipComparison`.
4. In-lap rollover happens while in the pit lane → the "In lap." report must still be sent (pit-lane suppression would otherwise drop it). → Task 5 test `InLapReportSurvivesPitLane`.
5. Phone lying flat or gravity not yet estimated (all zero) → horizon angle is 0, not NaN. → Task 6 test `horizonIsZeroWhenFlatOrUnknown`.

---

### Task 1: Pit data and inner tyre temps in the receiver's race state

**Files:**
- Modify: `windows-receiver/F1Controller/Validation/RaceTracker.cs` (Process loop, final `info with`, `RaceInfo`)
- Modify: `windows-receiver/F1Controller/Validation/TelemetryReceiver.cs` (parse inner temps, `RaceState`, `CurrentRaceState`)
- Modify: `windows-receiver/F1Controller.Tests/Snapshots.cs`
- Create: `windows-receiver/F1Controller.Tests/RaceTrackerTests.cs`

**Interfaces:**
- Produces: `RaceInfo` gains trailing optional params `byte PitStatus = 0, byte NumPitStops = 0, bool CarAheadInPit = false, bool CarBehindInPit = false`.
- Produces: `RaceState` gains `byte[] TyreInnerTemp` right after `TyreTemp` (required).
- Produces: `Snapshots.Make` gains `byte[]? inner = null` (default all 90), `byte pitStatus = 0`, `byte pitStops = 0`, `bool aheadInPit = false`, `bool behindInPit = false`, `uint lastLapMs = 0`, `ushort s1Ms = 0`, `ushort s2Ms = 0`.

- [ ] **Step 1: Write the failing test** `RaceTrackerTests.ReadsPitStatusAndNeighboursInPit`: build a Lap Data buffer (24-byte header + 22 × 43 bytes), car 0 = player at position 2 (`[24]=2`, `[25]=5` lap, `[26]=1` pitting, `[27]=1` stops, `[36]=2` active), car 1 at position 1 with `[26]=2`, car 2 at position 3 with `[26]=0`, all other cars `[36]=0`. After `Process(buf, 24, 0, 100f)`: `Info.PitStatus == 1`, `Info.NumPitStops == 1`, `Info.CarAheadInPit == true`, `Info.CarBehindInPit == false`.
- [ ] **Step 2: Run** `dotnet test F1Controller.Tests --artifacts-path <scratch>/rxtest` → FAIL (compile: no `PitStatus`).
- [ ] **Step 3: Implement.** In the player branch set `PitStatus = e[26], NumPitStops = e[27]`; keep a `_pitStatus[idx]` array next to `_positions`; in the final `info with` set `CarAheadInPit`/`CarBehindInPit` from the cars found by `FindByPosition(Position ∓ 1)` (false when none). In `TelemetryReceiver` add `_tyreInnerTemp[w] = car[34 + WheelOrder[w]]` and pass a clone into `RaceState`. Update `Snapshots.Make` (new params feed `RaceInfo`/`RaceState`; `S1: s1Ms, S2: s2Ms, LastLapMs: lastLapMs`).
- [ ] **Step 4: Run** the full receiver suite → all pass (89 + 1).
- [ ] **Step 5: Commit** `Receiver: pit status, pit stops, neighbours in pit, inner tyre temps`.

### Task 2: Chattiness level travels in the ping

**Files:**
- Modify: `windows-receiver/F1Controller/Transport/PacketParser.cs`, `windows-receiver/F1Controller/Transport/UdpReceiver.cs`
- Create: `windows-receiver/F1Controller.Tests/PingLevelTests.cs`
- Create: `android/app/src/main/java/com/f1controller/network/Ping.kt`
- Modify: `android/app/src/main/java/com/f1controller/network/UdpTransport.kt` (`pingLoop`), `android/app/src/main/java/com/f1controller/MainActivity.kt`
- Create: `android/app/src/test/java/com/f1controller/network/PingTest.kt`

**Interfaces:**
- Produces (C#): `public static int ControlPacket.ReadPingLevel(ReadOnlySpan<byte> ping)` → byte 16 if length ≥ 17 and value 0..3, else 2. `UdpReceiver.EngineerLevel` (`int`, volatile, default 2), updated on every ping.
- Produces (Kotlin): `const val PING_SIZE = 17`; `fun writePing(buf: ByteBuffer, seq: Int, timestampUs: Long, level: Int)` (little-endian buffer, writes marker, seq, timestamp, level clamped 0..3). `UdpTransport.engineerLevel: () -> Int` (default `{ 2 }`); MainActivity sets it to `{ engineerSettings.level }`.

- [ ] **Step 1: Write failing tests.** C# `PingLevelDefaultsAndClamps`: 17-byte ping with level 3 → 3; 16-byte ping → 2; level byte 7 → 2. Kotlin `PingTest.writesSeventeenBytesWithLevel`: `writePing(buf, 5, 1234L, 1)` → bytes 0..3 = `FF FF FF FF`, seq 5 LE at 4, timestamp LE at 8, `buf.array()[16] == 1`, `buf.position() == 17`.
- [ ] **Step 2: Run** both suites (`dotnet test …`, `./gradlew.bat testDebugUnitTest`) → FAIL (missing symbols).
- [ ] **Step 3: Implement.** Receiver: in the ping branch of `RunAsync`, set `EngineerLevel = ControlPacket.ReadPingLevel(data)` before echoing (echo stays `PingSize` bytes). Phone: `pingLoop` allocates `PING_SIZE`, calls `writePing` with `engineerLevel()`; the receive side still accepts pongs ≥ 16 bytes.
- [ ] **Step 4: Run** both suites → pass; `./gradlew.bat assembleDebug` builds.
- [ ] **Step 5: Commit** `Ping carries the engineer chattiness level`.

### Task 3: Lap report replaces sector calls

**Files:**
- Create: `windows-receiver/F1Controller/Engineer/EngineerRules.LapReport.cs`
- Modify: `windows-receiver/F1Controller/Engineer/EngineerRules.cs` (Evaluate signature, Reset), `EngineerRules.GapsLap.cs` (drop `Sector`, pb suppression), `EngineerService.cs`, `EngineerThresholds.cs`, `windows-receiver/F1Controller/Program.cs`
- Create: `windows-receiver/F1Controller.Tests/EngineerRulesLapReportTests.cs`
- Modify: tests that expect `sector-*` calls (delete those cases; `CooldownForMapsKeys` drops `sector-purple-1`, adds `("lap-report", 0f)`)

**Interfaces:**
- Consumes: Task 1 `RaceInfo.PitStatus`, `Snapshots.Make(lastLapMs:, s1Ms:, s2Ms:, pitStatus:)`; Task 2 `UdpReceiver.EngineerLevel`.
- Produces: `EngineerRules.Evaluate(RaceSnapshot? previous, RaceSnapshot current, int level = 2)`.
- Produces: `EngineerService(EngineerThresholds t, Action<RadioCall> send, Action<Exception>? onError = null, Func<int>? level = null, Action<string>? log = null)` (log used by Task 4); `Program` passes `() => receiver.EngineerLevel`.
- Produces (for Tasks 4–5, private to `EngineerRules`): `bool _lapStartedInPit` (current lap began with `PitStatus != 0`), set at each rollover; `static bool IsRollover(RaceSnapshot prev, RaceSnapshot cur)` = `cur.CurrentLap == prev.CurrentLap + 1`.
- Produces: `EngineerThresholds.SectorMatchedMs = 50`; `CooldownFor("lap-report") == 0`.

Report rules (spec A + D): due when `IsRollover`, completed lap `L = prev.CurrentLap ≥ 1`, level period (3/2/1, none at 0) divides `L`. S1/S2 and validity from `prev`, lap time from `cur.LastLapMs`, `S3 = lap − S1 − S2`. Pit lap = any snapshot of lap `L` had `PitStatus != 0`; out-lap if it started so, else in-lap. Best sectors = fastest valid, non-pit lap with S1 and S2 > 0, updated after building the report. PB lap = the existing `pb` condition at this rollover; when a report is due on a PB lap, no `pb` call. Key `lap-report`, Important, category Lap. Gaps (race only) from `cur.GapAheadMs`/`GapBehindMs`.

- [ ] **Step 1: Write failing tests** (`EngineerRulesLapReportTests`, feeding `Evaluate(prev, cur, level)`):
  - `ReportRhythmFollowsLevel`: rollovers completing laps 1..6 at level 1 → reports after laps 3, 6; level 2 → 2, 4, 6; level 3 → every lap; level 0 → none.
  - `ReportComparesSectorsWithBestLap`: best lap 1 = S1 25000, S2 30000, lap 80000 (S3 25000); lap 2 (level 3) S1 24800, S2 30100, lap 79930 (S3 25030) → text `"Lap 2, 1:19.9. Sector 1 up 0.2, sector 2 down 0.1, sector 3 matched. Gap ahead 1.2, behind 0.8."` with `gapAhead: 1200, gapBehind: 800`, priority Important, key `lap-report`.
  - `FirstLapHasNoComparison`: `"Lap 1, 1:20.0."` (plus gaps if set).
  - `DeletedLap`: `prev.LapInvalid` → `"Lap 2 deleted. Gap ahead 1.2."` (behind null → left out); deleted lap never becomes best.
  - `PbLapReportReplacesPbCall`: improving lap at a due rollover → text starts `"Personal best. Lap 2, "`, compared with the previous best, and no `pb` call in that step; on a non-due lap the `pb` call still comes.
  - `InAndOutLaps`: lap with `pitStatus: 1` mid-lap (started at 0) → `"In lap."`; next lap started with `pitStatus: 2` → `"Out lap."`; neither becomes best even if faster.
  - `NoGapsOutsideRace`: `isRace: false` → no "Gap" text.
  - `NoReportUnlessLapAdvancesByOne` (Review Focus 2): lap 5 → 4 and 5 → 7 give no report.
  - `MissingSectorTimesSkipComparison` (Review Focus 3): S1 = 0 → `"Lap 2, 1:20.0."` and not stored as best.
  - `NoSectorCalls`: S1 status changing to OverallBest yields no `sector-*` call.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** `EngineerRules.LapReport.cs` (`private IEnumerable<RadioCall> LapReport(RaceSnapshot? previous, RaceSnapshot current, int level)` called from `Evaluate`), remove `Sector(...)`, suppress `pb` as above, thread `level` through `EngineerService.Process` (`_level?.Invoke() ?? 2`), wire `Program`.
- [ ] **Step 4: Run** full suite → pass.
- [ ] **Step 5: Commit** `Engineer: lap report every 1-3 laps by chattiness, replaces sector calls`.

### Task 4: Inner-temperature overheating, mutes, per-lap tyre log

**Files:**
- Modify: `windows-receiver/F1Controller/Engineer/EngineerRules.TyresFuel.cs`, `EngineerThresholds.cs`, `EngineerService.cs`, `EngineerRules.cs`, `windows-receiver/F1Controller/Program.cs`, `windows-receiver/F1Controller/Engineer/CallLog.cs`
- Modify: `windows-receiver/F1Controller.Tests/EngineerRulesTyresFuelTests.cs`, `EngineerServiceTests.cs`, `CallLogTests.cs`

**Interfaces:**
- Consumes: Task 1 `RaceState.TyreInnerTemp`, `RaceInfo.NumPitStops`; Task 3 `IsRollover`, `_lapStartedInPit`, `EngineerService(..., log:)`.
- Produces: `EngineerThresholds.TyreHotC = 105` (now inner), `TyreHotRepeatLaps = 3`, `TyreHotWorseC = 5`.
- Produces: `IReadOnlyList<string> EngineerRules.TakeLogLines()` (returns and clears lines queued since the last call); `EngineerService` sends each to `log` after `Evaluate`. `CallLog.WriteNote(string text)` writes `"{time}  {text}"`; `Program` passes `callLog.WriteNote`.
- Log line format exactly: `lap {L} tyres  surface {FL}/{FR}/{RL}/{RR}  inner {FL}/{FR}/{RL}/{RR}` (peaks over lap `L`).

- [ ] **Step 1: Update/write tests.** Existing hot-tyre tests switch to `inner:` temps (surface stays 90). New:
  - `BrakingSurfaceSpikeIsNotOverheating`: surface 125 for 10 s with inner 100 → no hot call.
  - `HotTyreMutedForThreeLaps`: FL inner 108 for 5 s on lap 5 → call; hot again laps 6–7 → none; lap 8 → call.
  - `HotterTyreBreaksMute`: called at 108 on lap 5; inner 113 for 5 s on lap 6 → call.
  - `PitStopResetsTyreMemory`: called on lap 5; `pitStops` 0 → 1; hot again lap 6 → call.
  - `NoColdCallOnOutLap`: out-lap (started with `pitStatus: 2`), surface 60 for 15 s → no `tyre-cold`; next lap → call.
  - `LapTyreLogLine`: over lap 4 peaks surface 121/118/104/102, inner 101/99/95/94; at rollover `TakeLogLines()` returns exactly `["lap 4 tyres  surface 121/118/104/102  inner 101/99/95/94"]`, then empty.
  - `ServiceForwardsLogLines` (EngineerServiceTests): `log` receives the line after warm-up.
  - `CallLogTests.WritesNote`: `WriteNote("x")` → `"12:00:00.000  x"`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** `HotTyres` reads `TyreInnerTemp`; per tyre keep `_mutedUntilLap[i]`, `_calledAtC[i]`; a tyre joins the merged call only if not muted or ≥ `_calledAtC[i] + TyreHotWorseC`; firing sets `mutedUntilLap = CurrentLap + TyreHotRepeatLaps` (muted while `CurrentLap < mutedUntilLap`). Track peaks per lap, emit the line at `IsRollover`, reset peaks. On `NumPitStops` increase, clear hot spells, mutes and peaks. `ColdTyres` returns early (and clears `_coldSince`) while `_lapStartedInPit`.
- [ ] **Step 4: Run** full suite → pass.
- [ ] **Step 5: Commit** `Engineer: overheating on inner temps, 3-lap mute, pit resets, per-lap tyre log`.

### Task 5: Pit-lane quiet and gap resets around stops

**Files:**
- Modify: `windows-receiver/F1Controller/Engineer/EngineerService.cs`, `EngineerRules.GapsLap.cs`
- Modify: `windows-receiver/F1Controller.Tests/EngineerServiceTests.cs`, `EngineerRulesGapsLapTests.cs`

**Interfaces:**
- Consumes: Task 1 `PitStatus`, `CarAheadInPit`, `CarBehindInPit`; Task 3 `lap-report` key.

- [ ] **Step 1: Write failing tests.**
  - `OnlyCriticalInPitLane` (service): `pitStatus: 2` with a yellow flag change and a red flag → only `flag-red` sent; the yellow is not sent later after leaving the pit lane.
  - `InLapReportSurvivesPitLane` (Review Focus 4): rollover with `pitStatus: 1` and a report due → `lap-report` sent.
  - `PlayerPitClearsGapHistory`: laps 5, 6 shrinking gap, then a snapshot with `pitStatus: 1`, lap 7 shrinking again → no `gap-ahead`.
  - `CarAheadPittingClearsAheadOnly`: as above but `aheadInPit: true` mid-lap 6 → no `gap-ahead`, `gap-behind` still comes.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** Service: after `Evaluate`, while `PitStatus != 0` keep only calls with priority Critical or key `lap-report`. `Gaps`: clear both histories when `PitStatus != 0`, ahead when `CarAheadInPit`, behind when `CarBehindInPit`.
- [ ] **Step 4: Run** full suite → pass. Build the receiver to scratch (`dotnet build F1Controller -o <scratch>/rxnew`).
- [ ] **Step 5: Commit** `Engineer: quiet in the pit lane, gap trends reset around pit stops`.

### Task 6: Horizon steering

**Files:**
- Modify: `android/app/src/main/java/com/f1controller/input/SteeringProcessor.kt`, `android/app/src/main/java/com/f1controller/MainActivity.kt` (load/save axis)
- Create: `android/app/src/test/java/com/f1controller/input/SteeringAngleTest.kt`

**Interfaces:**
- Produces: `enum class SteeringAxis { HORIZON, TILT, ROLL, YAW }`, default `HORIZON`. Companion functions `fun horizonAngle(gx: Float, gy: Float, gz: Float): Float = atan2(gy, sqrt(gx² + gz²))` and `fun tiltAngle(gx: Float, gy: Float): Float = atan2(gy, gx)` (radians); `rawAngle()` uses them.
- Prefs: key `axis_horizon_migrated` (Boolean). On load, a saved `"TILT"` without the flag becomes `HORIZON`; `saveSettings` always writes the flag `true`.

Test helper: gravity for wheel turn α about the axis toward the driver, screen tilted back φ: `g = (cos φ · cos α, −sin α, sin φ · cos α)`.

- [ ] **Step 1: Write failing tests** (`SteeringAngleTest`):
  - `horizonIgnoresScreenTilt`: α = 20°, φ ∈ {0, 30, 45, 60}° → `horizonAngle` = −20° ± 0.5°.
  - `tiltGrowsWithScreenTilt` (documents the bug): α = 20°, φ = 60° → `|tiltAngle| > 35°`.
  - `horizonMatchesTiltWhenUpright`: φ = 0, α = 30° → both −30° ± 0.1°.
  - `horizonIsZeroWhenFlatOrUnknown` (Review Focus 5): `(0, 0, 1)` → 0; `(0, 0, 0)` → 0, not NaN.
- [ ] **Step 2: Run** `./gradlew.bat testDebugUnitTest` → FAIL.
- [ ] **Step 3: Implement** the enum, functions, default and the prefs migration (Settings' axis list already iterates `entries`, so HORIZON appears first).
- [ ] **Step 4: Run** tests → pass; `./gradlew.bat installDebug` (ask the user to tap Install on the phone).
- [ ] **Step 5: Commit** `Phone: horizon steering, pitch-independent, now the default`.

### Task 7: Real race check

- [ ] **Step 1:** Ask the user to restart the receiver (Receiver: run) and confirm the app is the new build.
- [ ] **Step 2:** User races at Balanced with at least one pit stop, steering with varied screen tilt.
- [ ] **Step 3:** Read the newest `engineer_log_*.txt`: lap reports after even laps only; `In lap.` / `Out lap.` around the stop; `lap N tyres` lines every lap; no tyre/gap/lap calls between pit entry and exit apart from the in-lap report; overheating calls far rarer than the 2026-10-28 log. Note the inner-temp peaks to tune `TyreHotC`.
- [ ] **Step 4:** Ask the user how steering felt at different tilts; record findings and the tuning in the progress memory.
