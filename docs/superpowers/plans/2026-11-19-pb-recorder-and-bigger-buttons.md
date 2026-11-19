# PB Recorder + Bigger Face Buttons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Save the best valid lap per track / session type / dry|wet on the PC, show the Time Trial dry and wet PBs on the phone dashboard, and enlarge the D-pad and X/Y/A/B buttons.

**Architecture:** `RaceTracker` raises an event when the player completes a valid lap; `TelemetryReceiver` combines it with the track ID, session type and tyre compound (from the existing 2021 session/status parsing) and feeds a new `PbStore` (JSON in `%APPDATA%\FullLock`). The 54-byte race packet grows to 62 bytes (track ID + TT dry/wet PB); the phone reads the extra fields only when present.

**Tech Stack:** C# / .NET 8 / xUnit (windows-receiver), Kotlin / Jetpack Compose / JUnit (android).

**Spec:** `docs/superpowers/specs/2026-11-19-pb-recorder-and-bigger-buttons-design.md`

## Global Constraints

- F1 2021 only (race dashboard code path already is). Session type Time Trial = **12**; race = 10/11 (existing code). Wet = visual tyre compound **8**; everything else dry.
- Only valid laps (`!LapInvalid`, `lastLap > 0`) are recorded; PBs never get slower.
- Race packet stays HMAC-authenticated (no change to auth). Existing bytes 0-53 keep their meaning; new fields are appended at 54+ so old phones keep working (they accept `n >= 54`).
- Store: `%APPDATA%\FullLock\pbs.json` via `AppPaths`; atomic write; corrupt file -> start empty and keep `pbs.json.bad`.
- Unknown track (`trackId` < 0 or session packet not seen yet) -> record nothing.
- Commits: **no Co-Authored-By trailer** (user rule).

## Review Focus

- Out-laps, TT restart run-ups and lap-number drops can show `lastLap > 0`: only a clean `lapNum == previous + 1` crossing from a flying/on-track lap may record (PBs never get slower, so one bogus time poisons the slot forever).
- A failing PB file write (antivirus/OneDrive lock) must not throw out of `RaceTracker.Process` (that would freeze phone timing and retry at 60 Hz).
- Flashback or restart mid-lap must not double-record or record a stale lap (restart calls `Reset()`; PB store must NOT reset).
- Session packet arrives after lap data, or track changes mid-run: use the track of the latest session packet at completion time; none -> skip.
- Corrupt / empty / missing `pbs.json` must not crash startup or lose the ability to save.
- New phone connected to an old PC (54-byte packet) must still show the dashboard with PB row blank, not drop packets.

---

### Task 1: PbStore (persistence + improve-only rule)

**Files:**
- Create: `windows-receiver/F1Controller/Validation/PbStore.cs`
- Modify: `windows-receiver/F1Controller/AppPaths.cs` (add `public string PbFile => Path.Combine(roamingDir, "pbs.json");`)
- Test: `windows-receiver/F1Controller.Tests/PbStoreTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class PbStore(string path)`
  - `public bool TryRecord(sbyte trackId, byte sessionType, bool wet, uint lapMs)` — true if it was a new PB (stored and saved); false if slower/equal or `lapMs == 0` or `trackId < 0`.
  - `public uint Get(sbyte trackId, byte sessionType, bool wet)` — ms, 0 = none.
  - Loads in constructor (missing file = empty; unparseable = empty and file copied to `<path>.bad`).

- [ ] **Step 1: Write failing tests** in `PbStoreTests.cs` (use a temp dir per test): `FirstLapIsPb` (TryRecord true, Get == ms), `SlowerLapIgnored` (false, Get unchanged), `DryAndWetAndSessionTypesAreSeparate` (record same track dry 80000 / wet 95000 / race 82000 -> three independent values), `SurvivesRestart` (new `PbStore(path)` returns saved values), `CorruptFileStartsEmptyAndKeepsBad` (write `"{not json"`, construct, `Get == 0`, `File.Exists(path + ".bad")`, a subsequent TryRecord works), `UnknownTrackOrZeroLapIgnored` (trackId -1, lapMs 0 -> false), `SaveFailureDoesNotThrow` (path whose directory doesn't exist or is a locked file -> `TryRecord` returns true, `Get` returns the value).
- [ ] **Step 2: Run** `dotnet test windows-receiver/F1Controller.Tests --filter PbStoreTests` -> FAIL (type missing).
- [ ] **Step 3: Implement** `PbStore` with `System.Text.Json`, key string `$"{trackId}/{sessionType}/{(wet ? "wet" : "dry")}"` -> `{ ms, date }`; write to `path + ".tmp"` then `File.Move(..., overwrite: true)`; lock around mutations. `TryRecord` must catch `IOException`/`UnauthorizedAccessException` on save: keep the value in memory, return true, log once (never throw).
- [ ] **Step 4: Run** same filter -> PASS.
- [ ] **Step 5: Commit** `feat(pc): PbStore keeps best lap per track/session/condition`.

### Task 2: Lap-completed event + track ID parsing + recording wiring

**Files:**
- Modify: `windows-receiver/F1Controller/Validation/RaceTracker.cs` (near line 188-196)
- Modify: `windows-receiver/F1Controller/Validation/F1Packets2021.cs:10-29`
- Modify: `windows-receiver/F1Controller/Validation/TelemetryReceiver.cs` (constructor, session branch ~244, `CurrentRaceState`, `RaceState` record)
- Test: `windows-receiver/F1Controller.Tests/RaceTrackerTests.cs`, `F1Packets2021Tests.cs`, `TelemetryReceiverTests.cs`

**Interfaces:**
- Produces:
  - `RaceTracker`: `public event Action<uint>? OnPlayerLapCompleted;` raised with `lastLap` for the player only when ALL hold: `lapNum == car.LapNum + 1` (a plain crossing, not a lap-number drop or jump), `lastLap > 0`, `!car.LapInvalid` (the previous packet's flag, assigned after the branch at line ~201), and the player's driverStatus (lap-data entry byte 35: 0 garage, 1 flying, 2 in-lap, 3 out-lap, 4 on track) in the PREVIOUS packet was 1 or 4 (store it on `Car` like `LapInvalid`). Raise it AFTER `car.LapNum = lapNum` and `Info = info` so a throwing subscriber can't freeze timing. Evaluated for every such lap, not only improvements; `_myBestLap` logic is untouched.
  - `F1Packets2021.SessionInfo` gains `sbyte TrackId = -1` (body offset 7, signed) as the LAST positional parameter; update `ParseSession`. Existing test `F1Packets2021Tests.cs:21` compares `new SessionInfo(2,28,10,1,null)` to a parsed body whose `[7]` is 0: change its expected value to pass `TrackId: 0`.
  - `TelemetryReceiver(int port = 20777, PbStore? pbs = null)`; on `OnPlayerLapCompleted(ms)` if `_session is { } s` and `s.TrackId >= 0` call `pbs?.TryRecord(s.TrackId, s.SessionType, _tyreCompound == 8, ms)`.
  - `RaceState` gains `sbyte TrackId = -1, uint TtDryMs = 0, uint TtWetMs = 0` (defaults keep `Snapshots.cs:50` and other positional constructions compiling; -1/0 when unknown/no store); filled in `CurrentRaceState()` from `_session` and `pbs.Get(trackId, 12, false/true)`.

- [ ] **Step 1: Write failing tests:** (a) `RaceTrackerTests`: extend `PlayerLap` with `bool invalid = false` (sets lap-data entry byte 29) and `byte driverStatus = 1` (byte 35). Tests: `LapCompleteRaisesEvent` (lap 1->2 lastLap 0: nothing; 2->3 lastLap 80000: once with 80000; slower valid 82000 also raises); `InvalidLapDoesNotRaise` (the invalid flag must be on the packet BEFORE the lap-number change, not the crossing packet); `OutLapDoesNotRaise` (previous packet driverStatus 3); `LapNumberDropDoesNotRaise` (lap 5 -> 2 with lastLap > 0); `ThrowingSubscriberDoesNotFreezeInfo`. (b) `F1Packets2021Tests.ParseSessionReadsTrackId`: body with `[6]=12`, `[7]=5` -> `TrackId == 5`; and `[7]=0xFF` -> `-1`. (c) `TelemetryReceiverTests.TimeTrialLapIsRecordedDryAndWet`: feed a session packet (type 12, track 5), car status with compound 8 (wet) and a lap completion -> `pbs.Get(5, 12, true)` set and `Get(5, 12, false)` 0; then compound 16 + faster lap -> dry set. (d) same test file: `NoSessionPacketMeansNothingRecorded`.
- [ ] **Step 2: Run** `dotnet test windows-receiver/F1Controller.Tests` filtered to the three classes -> FAIL.
- [ ] **Step 3: Implement** the interfaces above. Only `CarTelemetryPacket()` exists in `TelemetryReceiverTests.cs` (lines 8-14): write small helpers for a session packet (body >= 127 bytes, 24-byte header, sessionType @6, trackId @7), a car-status packet (22 x 47 bytes, visual compound at entry byte 26) and a lap packet (22 x 43, sessionTime f32 at header offset 14).
- [ ] **Step 4: Run full** `dotnet test windows-receiver/F1Controller.Tests` -> all PASS.
- [ ] **Step 5: Commit** `feat(pc): record time-trial PBs from lap completion`.

### Task 3: Race packet v+ (62 bytes) and PC wiring

**Files:**
- Modify: `windows-receiver/F1Controller/Transport/PacketParser.cs:49-90` (doc comment, `RaceStateSize`, `WriteRaceState`)
- Modify: `windows-receiver/F1Controller/Program.cs` (construct `new PbStore(AppPaths.Default.PbFile)` (folder already created at ~line 115), pass to `TelemetryReceiver`; check `raceBuf` at ~183 uses `RaceStateSize`)
- Test: new `windows-receiver/F1Controller.Tests/RacePacketTests.cs` (nothing tests `WriteRaceState` today)

**Interfaces:**
- Consumes: `RaceState.TrackId/TtDryMs/TtWetMs` (Task 2).
- Produces: `ControlPacket.RaceStateSize = 62`; bytes 52 = trackId as signed byte (`unchecked((byte)s.TrackId)`), 53 = 0 (reserved), 54..57 TtDryMs u32 LE, 58..61 TtWetMs u32 LE.

- [ ] **Step 1: Failing test** `RacePacketCarriesTrackAndPbs`: `WriteRaceState` with TrackId 5, TtDryMs 78123, TtWetMs 91234 -> `dest[52]==5`, `ReadUInt32LittleEndian(dest[54..])==78123`, `...[58..]==91234`; TrackId -1 -> `dest[52]==0xFF`.
- [ ] **Step 2: Run** `dotnet test windows-receiver/F1Controller.Tests --filter RacePacket` -> FAIL.
- [ ] **Step 3: Implement** the layout; update the doc comment (54 -> 62 bytes, new fields) and construct the `PbStore` in `Program.cs`.
- [ ] **Step 4: Run full** receiver tests -> PASS; `dotnet build windows-receiver/F1Controller -c Release` succeeds.
- [ ] **Step 5: Commit** `feat(pc): send track id and TT PBs in race packet`.

### Task 4: Phone parsing, PB row, track names

**Files:**
- Modify: `android/app/src/main/java/com/f1controller/network/UdpTransport.kt` (RaceState data class ~line 83, `onRaceState` ~426, `RACE_STATE_SIZE` ~516, doc comment ~38)
- Modify: `android/app/src/main/java/com/f1controller/ui/DashboardViews.kt` (after the BEST/BEHIND row ~line 153)
- Create: `android/app/src/main/java/com/f1controller/network/TrackNames.kt`
- Test: `android/app/src/test/java/com/f1controller/network/RaceStatePbTest.kt`

**Interfaces:**
- Produces:
  - `RaceState` gains `val trackId: Int` (-1 none), `val ttDryMs: Long`, `val ttWetMs: Long`.
  - `object TrackNames { fun short(id: Int): String }` — F1 2021 IDs: 0 Melbourne, 1 Paul Ricard, 2 Shanghai, 3 Sakhir, 4 Catalunya, 5 Monaco, 6 Montreal, 7 Silverstone, 8 Hockenheim, 9 Hungaroring, 10 Spa, 11 Monza, 12 Singapore, 13 Suzuka, 14 Abu Dhabi, 15 Austin, 16 Interlagos, 17 Austria, 18 Sochi, 19 Mexico, 20 Baku, 21 Sakhir Short, 22 Silverstone Short, 23 Austin Short, 24 Suzuka Short, 25 Hanoi, 26 Zandvoort, 27 Imola, 28 Portimao, 29 Jeddah; else `"Track $id"`; negative -> `""`.
  - Parsing is extracted to a testable function: a top-level `fun parseRaceState(bb: ByteBuffer, n: Int): RaceState` in a new file `network/RaceStatePacket.kt` (like `decodeRadioCall` in `RadioPacket.kt`); `onRaceState` just assigns. Read the new fields at absolute indexes 52 / 54 / 58. Packets with `n >= 62` read trackId (byte 52 signed) and the two u32 at 54/58; `54 <= n < 62` gives trackId -1, PBs 0. `RACE_STATE_SIZE` stays 54 (minimum accepted).

- [ ] **Step 1: Failing tests** (`RaceStatePbTest`): build a 62-byte little-endian buffer (marker consumed as the existing code expects — mirror how `RadioPacketTest`/`PongVerifierTest` set up buffers) with trackId 5, dry 78123, wet 91234 -> parsed values match; a 54-byte buffer parses with `trackId == -1`, PBs 0; `TrackNames.short(5) == "Monaco"`, `short(-1) == ""`, `short(99) == "Track 99"`.
- [ ] **Step 2: Run** `cd android && ./gradlew :app:testDebugUnitTest --tests '*RaceStatePbTest*'` -> FAIL.
- [ ] **Step 3: Implement** parsing + `TrackNames`. In `DashboardViews.kt` the timing card is a fixed 240dp tall and ~210dp wide inside its padding, so one long line won't fit: add two compact lines under the BEST/BEHIND row: `PB DRY m:ss.mmm  WET m:ss.mmm` and the track name as a small `Label`. Show `—` for 0; hide both when `trackId < 0`. Add `color: Color = Palette.Text` to `TimingRow` (`DashboardViews.kt:178`) and use Palette.Cyan for the value that equals `race.lastLapMs` while `currentLapMs < 5000`. Check the card still fits 240dp; raise its height if needed.
- [ ] **Step 4: Run** the android unit tests (`:app:testDebugUnitTest`) -> all PASS; `./gradlew :app:assembleDebug` succeeds.
- [ ] **Step 5: Commit** `feat(android): show time-trial PBs and track on dashboard`.

### Task 5: Bigger D-pad and face buttons

**Files:**
- Modify: `android/app/src/main/java/com/f1controller/ui/ControllerScreen.kt` (face-button divisor line 471; main row weights lines 215-280 for the D-pad)

**Interfaces:** none.

**Geometry (checked):** Square D-pad keys at TopCenter/CenterStart touch once `key > S/3`; today's `S/2.9` is already there, so the D-pad can only grow by enlarging its column (`S = min(width, height)`). The face buttons are circles, which only touch at `d = S/(1+sqrt2) ~ S/2.41`, so their divisor can drop from 2.8 to 2.5 (~12% bigger) with no layout change.

- [ ] **Step 1: Face buttons:** change `/ 2.8f` -> `/ 2.5f` (line 471).
- [ ] **Step 2: D-pad:** widen its box `0.19f` -> `0.23f` taking the 0.04 from the two pedals (`0.16f` -> `0.14f` each). Do NOT shrink the centre column (`0.26f`): the timing card is a fixed 240dp and is already ~210-225dp wide there.
- [ ] **Step 3: Verify on device/emulator** (`cd android && ./gradlew :app:installDebug`, landscape, one small and one large phone): both clusters visibly larger, no key overlap, pedals still usable, timing card (with the new PB lines from Task 4) unclipped. Save before/after screenshots under `docs/superpowers/plans/screens/` and name them in the commit body.
- [ ] **Step 4: Commit** `feat(android): enlarge D-pad and face buttons` (tell the user the pedals got a bit narrower).

### Task 6: End-to-end check and docs

**Files:**
- Modify: `README.md` (one short PB section: where `pbs.json` lives, TT-only target display, wet = full wets), the 54-byte comments in `PacketParser.cs:55` and `UdpTransport.kt:38` (no separate protocol doc has them)

- [ ] **Step 1:** Update docs; note old phones keep working with the new PC, new phones need the new PC for the PB row.
- [ ] **Step 2: Manual F1 2021 check:** run a Time Trial lap on dry, then try full wets. Also look at what weather Time Trial offers: if it is always clear, a TT wet PB can only be wets on a dry track (still fine, recorded by tyre per the spec); if `pbs.json` stays empty for wets, tell the user. Also do an out-lap, a restart-lap and a pit-then-cross lap and confirm no PB appears for them: verify `pbs.json` has `track/12/dry` and `.../wet`, session code 12 really is Time Trial (if nothing records, log `SessionType` and fix the constant), phone shows both PBs.
- [ ] **Step 3: Full test runs:** `dotnet test windows-receiver/F1Controller.Tests` and `./gradlew :app:testDebugUnitTest` PASS.
- [ ] **Step 4: Commit** `docs: PB recorder`.
