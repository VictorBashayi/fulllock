# Engineer Radio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The PC receiver turns F1 2021 telemetry into short race-engineer calls; the phone shows, speaks and vibrates them.

**Architecture:** `TelemetryReceiver` builds a `RaceSnapshot` per Lap Data packet. `EngineerService` runs `EngineerRules` (stateful, deterministic, no clock but session time) and `RadioDispatcher` (spacing, cooldowns, priority), and hands each chosen call to `RadioSender`, which writes a radio packet (marker `0xFFFFFFFC`) to the phone 3× ~30 ms apart. The phone decodes it, drops duplicates by call ID, filters by chattiness, and plays it through a banner, `TextToSpeech` and the vibrator.

**Tech Stack:** C# / .NET 8 + xUnit (receiver), Kotlin / Jetpack Compose + JUnit 4 (phone).

**Spec:** `docs/superpowers/specs/2026-10-27-engineer-radio-design.md`

## Global Constraints

- F1 2021 only (`packetFormat == 2021`); other formats produce no snapshots and no calls.
- Radio packet: `[marker:u32 = 0xFFFFFFFC][callId:u16][priority:u8][category:u8][textLen:u8][UTF-8 text]`, little-endian, text ≤ 120 bytes (truncate on a UTF-8 character boundary).
- Priorities: 0 chatter, 1 info, 2 important, 3 critical. Categories: 0 tyres, 1 fuel, 2 gaps, 3 lap, 4 flags, 5 weather, 6 damage, 7 other.
- Chattiness: 0 Off (none), 1 Quiet (≥2), 2 Balanced (≥1, default), 3 Talkative (≥0).
- Dispatcher: 4 s minimum spacing (critical bypasses), 60 s default per-key cooldown, queued non-critical calls older than 15 s are dropped. All times are game session time.
- Silent for the first 3 s of a session; session restart (session time goes backwards or session UID changes) clears all engineer state.
- No "in DRS range" call, no every-lap gap report, no "wait for a straight" delay.
- All thresholds live in `EngineerThresholds`; no magic numbers in rules.
- Prefs keys: `engineer_level` (int), `engineer_voice` (bool), `engineer_vibrate` (bool).
- Vibration: info one 40 ms tick; important two 40 ms ticks 80 ms apart; critical one 300 ms buzz; chatter none.
- TTS: English, speech rate 1.15; critical uses `QUEUE_FLUSH`, others `QUEUE_ADD`.
- Banner shows max(4 s, until speech ends); new call replaces it.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Pit stop / new tyres**: wear drops back near 0 → the 50 %/70 % wear calls must re-arm (test in Task 4).
2. **Repair at pit stop**: damage values drop → baseline follows them down so the next 10-point rise is called again (test in Task 6).
3. **Position change mid-trend**: overtaking or being passed changes which car "ahead"/"behind" means → gap trend history must reset, no false "closing" call (test in Task 5).
4. **Safety car**: tyres cool under SC → no "tyres are cold" call while `SafetyCar != 0` (test in Task 4).
5. **Non-ASCII / long text**: a call longer than 120 UTF-8 bytes must truncate without splitting a character (test in Task 1).

---

## File Structure

Receiver (`windows-receiver/`):
- Create `F1Controller/Engineer/RadioCall.cs` — `RadioCall`, `RadioPriority`, `RadioCategory`.
- Create `F1Controller/Engineer/RaceSnapshot.cs` — `RaceSnapshot`, `Damage`, `GameEvent`.
- Create `F1Controller/Engineer/EngineerThresholds.cs` — all tunable numbers.
- Create `F1Controller/Engineer/EngineerRules.cs` — rules (split into `EngineerRules.*.cs` partials by call group).
- Create `F1Controller/Engineer/RadioDispatcher.cs`.
- Create `F1Controller/Engineer/EngineerService.cs` — reset/warm-up, glues rules + dispatcher.
- Create `F1Controller/Validation/F1Packets2021.cs` — static parsers for Session / Event / Damage / FIA flag.
- Create `F1Controller/Transport/RadioSender.cs` — 3× send.
- Modify `F1Controller/Transport/PacketParser.cs` — `WriteRadioCall`.
- Modify `F1Controller/Validation/TelemetryReceiver.cs` — use parsers, raise `OnSnapshot`.
- Modify `F1Controller/Program.cs` — wiring, `--radio-demo`.
- Create `F1Controller.Tests/` (xUnit) and add to `F1Controller.sln`.

Phone (`android/app/`):
- Create `src/main/java/com/f1controller/network/RadioPacket.kt` — `RadioCall`, `decodeRadioCall`.
- Modify `src/main/java/com/f1controller/network/UdpTransport.kt` — radio flow, dedupe, 256-byte buffer.
- Create `src/main/java/com/f1controller/engineer/EngineerSettings.kt` — state + `shouldPlay`.
- Create `src/main/java/com/f1controller/engineer/EngineerRadio.kt` — TTS, vibration, current call.
- Create `src/main/java/com/f1controller/ui/EngineerBanner.kt`.
- Modify `ui/ControllerScreen.kt`, `ui/SettingsScreen.kt`, `MainActivity.kt`, `build.gradle.kts`.
- Create `src/test/java/com/f1controller/network/RadioPacketTest.kt`, `src/test/java/com/f1controller/engineer/EngineerSettingsTest.kt`.

Run receiver tests with: `dotnet test windows-receiver/F1Controller.sln` (from repo root).
Run phone tests with: `./gradlew :app:testDebugUnitTest` (from `android/`).

---

### Task 1: Test project, radio types, radio packet writer

**Files:**
- Create: `windows-receiver/F1Controller.Tests/F1Controller.Tests.csproj` (net8.0, xunit 2.9.x, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk; ProjectReference to `../F1Controller/F1Controller.csproj`)
- Modify: `windows-receiver/F1Controller.sln` (`dotnet sln add`)
- Create: `windows-receiver/F1Controller/Engineer/RadioCall.cs`
- Modify: `windows-receiver/F1Controller/Transport/PacketParser.cs` (class `ControlPacket`)
- Test: `windows-receiver/F1Controller.Tests/RadioPacketTests.cs`

**Interfaces:**
- Produces (namespace `F1Controller.Engineer`):
  - `public enum RadioPriority : byte { Chatter = 0, Info = 1, Important = 2, Critical = 3 }`
  - `public enum RadioCategory : byte { Tyres = 0, Fuel = 1, Gaps = 2, Lap = 3, Flags = 4, Weather = 5, Damage = 6, Other = 7 }`
  - `public sealed record RadioCall(string Key, string Text, RadioPriority Priority, RadioCategory Category)`
- Produces (in `ControlPacket`): `public const uint RadioMarker = 0xFFFFFFFC; public const int RadioMaxText = 120; public const int RadioHeaderSize = 9;`
  `public static int WriteRadioCall(Span<byte> dest, ushort callId, RadioCall call)` → returns total bytes written. `dest` must be ≥ `RadioHeaderSize + RadioMaxText`.

- [ ] **Step 1: Create the test project and add it to the solution**

Run: `dotnet new xunit -o windows-receiver/F1Controller.Tests -f net8.0 && dotnet sln windows-receiver/F1Controller.sln add windows-receiver/F1Controller.Tests && dotnet add windows-receiver/F1Controller.Tests reference windows-receiver/F1Controller/F1Controller.csproj`
Delete the generated `UnitTest1.cs`.

- [ ] **Step 2: Write the failing tests**

```csharp
public class RadioPacketTests
{
    [Fact]
    public void WritesHeaderAndText()
    {
        var buf = new byte[ControlPacket.RadioHeaderSize + ControlPacket.RadioMaxText];
        int n = ControlPacket.WriteRadioCall(buf, 0x1234,
            new RadioCall("k", "Box", RadioPriority.Important, RadioCategory.Tyres));
        Assert.Equal(12, n);
        Assert.Equal(new byte[] { 0xFC, 0xFF, 0xFF, 0xFF, 0x34, 0x12, 2, 0, 3, (byte)'B', (byte)'o', (byte)'x' },
            buf[..n]);
    }

    [Fact]
    public void TruncatesLongTextOnCharacterBoundary()
    {
        var buf = new byte[ControlPacket.RadioHeaderSize + ControlPacket.RadioMaxText];
        string text = new string('a', 119) + "é";  // 'é' is 2 bytes: 121 total
        int n = ControlPacket.WriteRadioCall(buf, 1, new RadioCall("k", text, RadioPriority.Info, RadioCategory.Other));
        Assert.Equal(119, buf[8]);
        Assert.Equal(ControlPacket.RadioHeaderSize + 119, n);
    }
}
```

- [ ] **Step 3: Run tests, expect compile failure** (`RadioCall` / `WriteRadioCall` not defined)

Run: `dotnet test windows-receiver/F1Controller.sln`

- [ ] **Step 4: Implement `RadioCall.cs` and `WriteRadioCall`**

Truncation: encode with `Encoding.UTF8`; if > 120 bytes, step back from byte 120 while the byte is a continuation byte (`(b & 0xC0) == 0x80`). Document the layout in a comment above `WriteRadioCall`, matching the existing `WriteRaceState` style.

- [ ] **Step 5: Run tests, expect PASS**

- [ ] **Step 6: Commit** — `git add windows-receiver && git commit -m "Add radio call packet and receiver test project"`

---

### Task 2: F1 2021 parsers for Session, Event, Damage and FIA flag

**Files:**
- Create: `windows-receiver/F1Controller/Validation/F1Packets2021.cs`
- Test: `windows-receiver/F1Controller.Tests/F1Packets2021Tests.cs`

**Interfaces:**
- Produces (namespace `F1Controller.Validation`, `public static class F1Packets2021`). All take the packet body (span after the 24-byte header):
  - `public readonly record struct SessionInfo(byte Weather, byte TotalLaps, byte SessionType, byte SafetyCar, int? RainInMinutes)`
  - `public static SessionInfo? ParseSession(ReadOnlySpan<byte> body)`
  - `public static Engineer.GameEvent? ParseEvent(ReadOnlySpan<byte> body, byte playerIdx)`
  - `public static Engineer.Damage ParseDamage(ReadOnlySpan<byte> carEntry)` (one 39-byte car entry)
  - `public static sbyte FiaFlag(ReadOnlySpan<byte> carStatusEntry)` → byte @28 as `sbyte`
- Consumes: `Damage` and `GameEvent` from Task 3's `RaceSnapshot.cs`. **Create `RaceSnapshot.cs` in this task** with these two types (Task 3 adds `RaceSnapshot` itself):
  - `public readonly record struct Damage(byte FrontWing, byte RearWing, byte Floor, byte Diffuser, byte Sidepod)` (FrontWing = max of FL/FR wing)
  - `public enum GameEventKind { DrsEnabled, Penalty }`
  - `public readonly record struct GameEvent(GameEventKind Kind, byte PenaltyType = 0, byte Seconds = 0)`

Offsets (F1 2021 spec, write them as comments in the file):
- Session: weather @0, totalLaps @3, sessionType @6, numMarshalZones @18, marshalZones[21×5] @19, safetyCarStatus @124, numWeatherForecastSamples @126, samples[56×8] @127, each `{sessionType u8, timeOffset u8 (min), weather u8, …}`. `RainInMinutes` = smallest `timeOffset` among samples whose sessionType equals the current one and weather ≥ 3, else null.
- Event: code[4] @0 ASCII. `"DRSE"` → `DrsEnabled`. `"PENA"`: penaltyType @4, vehicleIdx @6, time @8; return `Penalty` only when vehicleIdx == playerIdx and penaltyType ∈ {0 drive-through, 1 stop-go, 4 time penalty, 6 disqualified}. Anything else → null.
- Damage entry: FL wing @24, FR wing @25, rear wing @26, floor @27, diffuser @28, sidepod @29.

- [ ] **Step 1: Write the failing tests** (build buffers with `new byte[N]` and set the bytes the test needs)

```csharp
[Fact] public void SessionReadsWeatherLapsTypeSafetyCar()   // body[0]=2, body[3]=28, body[6]=10, body[124]=1 → (2, 28, 10, 1, null)
[Fact] public void SessionRainInMinutesIsEarliestRainySample() // 3 samples type 10: (5, weather 1), (10, weather 3), (15, weather 4) → 10
[Fact] public void SessionIgnoresForecastForOtherSessionTypes()  // sample type 5 weather 3 → null
[Fact] public void SessionTooShortReturnsNull()                 // 100-byte body → null
[Fact] public void EventDrsEnabled()                            // "DRSE" → GameEvent(DrsEnabled)
[Fact] public void EventPenaltyForPlayerOnly()                  // PENA type 4, vehicle 3, time 5: playerIdx 3 → Penalty(4,5); playerIdx 2 → null
[Fact] public void EventWarningIsIgnored()                      // PENA type 5 → null
[Fact] public void DamageTakesWorstFrontWing()                  // @24=10, @25=30, @26=5 → FrontWing 30, RearWing 5
```

- [ ] **Step 2: Run, expect FAIL** — `dotnet test windows-receiver/F1Controller.sln`
- [ ] **Step 3: Implement `F1Packets2021`**
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** — `"Parse F1 2021 session, event and damage details"`

---

### Task 3: RaceSnapshot from TelemetryReceiver

**Files:**
- Modify: `windows-receiver/F1Controller/Engineer/RaceSnapshot.cs`
- Modify: `windows-receiver/F1Controller/Validation/TelemetryReceiver.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record RaceSnapshot(
      float SessionTime,
      ulong SessionUid,
      RaceState Race,            // existing record: Timing, TotalLaps, tyres, fuel
      bool IsRace,               // sessionType 10 or 11
      byte Weather,              // 0 clear, 1 light cloud, 2 overcast, 3 light rain, 4 heavy rain, 5 storm
      int? RainInMinutes,
      byte SafetyCar,            // 0 none, 1 full, 2 virtual, 3 formation
      sbyte FiaFlag,             // -1 unknown, 0 none, 1 green, 2 blue, 3 yellow, 4 red
      Damage Damage,
      IReadOnlyList<GameEvent> Events);  // events received since the previous snapshot
  ```
  `TelemetryReceiver.OnSnapshot` : `event Action<RaceSnapshot>?` raised after every 2021 Lap Data packet is processed (not throttled like `OnRaceStateChanged`).
- Consumes: `F1Packets2021` (Task 2).

- [ ] **Step 1: Store the new values in `TelemetryReceiver`**
  - Session branch: `F1Packets2021.ParseSession(body)`; keep `_totalLaps` from it plus `_session` (`SessionInfo?`).
  - Car Status branch (2021 only): `_fiaFlag = F1Packets2021.FiaFlag(car)`.
  - Damage branch: `_damage = F1Packets2021.ParseDamage(entry)`.
  - New `PacketIdEvent = 3` branch (2021 only): non-null result appended to `_pendingEvents` (`List<GameEvent>`).
  - Read sessionUID (u64 @6) in the Lap Data branch.
- [ ] **Step 2: Raise `OnSnapshot` in the Lap Data branch** with the current values; pass a copy of `_pendingEvents` then clear it. Build the `RaceState` exactly as the existing `OnRaceStateChanged` call does (extract a private `CurrentRaceState()` helper to share it).
- [ ] **Step 3: Build** — `dotnet build windows-receiver/F1Controller.sln` → `Build succeeded`, 0 errors; then `dotnet test` still passes.
- [ ] **Step 4: Commit** — `"Build race snapshots for the engineer"`

---

### Task 4: EngineerThresholds and tyre & fuel rules

**Files:**
- Create: `windows-receiver/F1Controller/Engineer/EngineerThresholds.cs`
- Create: `windows-receiver/F1Controller/Engineer/EngineerRules.cs` (core + `Evaluate`)
- Create: `windows-receiver/F1Controller/Engineer/EngineerRules.TyresFuel.cs`
- Test: `windows-receiver/F1Controller.Tests/EngineerRulesTyresFuelTests.cs`, `windows-receiver/F1Controller.Tests/Snapshots.cs` (test builder)

**Interfaces:**
- Produces:
  - `public sealed class EngineerThresholds` with init properties and these defaults:
    `TyreHotC = 110, TyreHotSeconds = 5f, TyreColdC = 70, WearWarnPct = 50, WearBoxPct = 70, WearRearmPct = 10, FuelTightLaps = -0.3f, FuelCriticalLaps = -1.0f, CatchMinLaps = 2, CatchMaxGapMs = 3000, ClosingMaxGapMs = 2000, DamageStepPct = 10, RainForecastMinutes = 10, MinSpacingSeconds = 4f, DefaultCooldownSeconds = 60f, GapCooldownSeconds = 240f, SectorCooldownSeconds = 20f, BlueFlagCooldownSeconds = 30f, ColdCooldownSeconds = 120f, RainForecastCooldownSeconds = 600f, StaleSeconds = 15f, WarmupSeconds = 3f`
    plus `public float CooldownFor(string key)` mapping key prefixes `gap-` → Gap, `sector-` → Sector, `flag-blue` → BlueFlag, `tyre-cold` → Cold, `rain-forecast` → RainForecast; state-change keys (`sc-*`, `flag-red`, `flag-yellow`, `damage-*`, `penalty`, `drs`, `pb`, `last-lap`, `lap-invalid`) → 0; anything else → Default.
  - `public sealed partial class EngineerRules { public EngineerRules(EngineerThresholds t); public IReadOnlyList<RadioCall> Evaluate(RaceSnapshot? previous, RaceSnapshot current); public void Reset(); }`
    Rules keep their memory in private fields; `Reset()` clears all of it. `Evaluate` calls one private method per group (`TyresFuel`, `GapsLap`, `FlagsWeatherDamage`) and concatenates.
- Consumes: `RaceSnapshot` (Task 3), `RadioCall` (Task 1).

Calls in this task (category `Tyres` for `tyre-*`, `Fuel` for `fuel-*`):
| Key | Text | Pri | When |
|---|---|---|---|
| `tyre-hot-FL` (FR/RL/RR) | "Front left is overheating, manage it." (Front right / Rear left / Rear right) | Important | that tyre's surface temp > `TyreHotC` continuously for `TyreHotSeconds` of session time; fires once per hot spell |
| `tyre-cold` | "Tyres are cold, push to get heat in." | Important | average < `TyreColdC`, `CurrentLap ≥ 2`, `SafetyCar == 0` |
| `tyre-wear-50` | "Tyre wear at 50 percent." | Info | worst wear crosses `WearWarnPct` (once until re-armed) |
| `tyre-wear-70` | "{Fronts\|Rears} at 70 percent, box soon." (axle of the worst tyre) | Important | worst wear crosses `WearBoxPct` (once until re-armed) |
| `fuel-tight` | "Fuel's tight, start lifting and coasting." | Important | `IsRace` and `FuelLaps` crosses below `FuelTightLaps`; re-arm above 0 |
| `fuel-critical` | "We won't make it on fuel, lift and coast now." | Critical | `IsRace` and `FuelLaps` crosses below `FuelCriticalLaps`; re-arm above 0 |
Wear re-arms when worst wear < `WearRearmPct` (new tyres).

- [ ] **Step 1: Write the test builder** `Snapshots.Make(float t = 100, byte lap = 5, byte[]? temps = null, byte[]? wear = null, float fuel = 1f, bool isRace = true, byte sc = 0, ...)` returning a `RaceSnapshot` with sensible defaults (temps 90, wear 10, no gaps, no damage, no events). Later tasks add optional parameters to it.
- [ ] **Step 2: Write the failing tests**

```csharp
[Fact] public void HotTyreNeedsFiveSeconds()          // FL 112 at t=100 → none; t=104 → none; t=105.1 → one "tyre-hot-FL"
[Fact] public void HotTyreFiresOncePerSpell()          // stays 112 until t=120 → still one; drops to 100 at t=121, back to 112 t=122..127.2 → second call
[Fact] public void ColdTyresIgnoredOnLapOneAndUnderSafetyCar() // avg 60: lap 1 → none; lap 3 sc=1 → none; lap 3 sc=0 → "tyre-cold"
[Fact] public void WearCallsOncePerThreshold()         // worst wear 49→51 → "tyre-wear-50"; 51→52 → none; 69→71 → "tyre-wear-70", text "Rears at 70 percent, box soon." when worst is RL
[Fact] public void WearRearmsAfterNewTyres()           // 71 → 5 → 51 → "tyre-wear-50" again   (Review Focus 1)
[Fact] public void FuelCallsOnCrossingOnlyInRace()     // 0.0 → -0.4 → "fuel-tight"; -0.4 → -0.5 → none; -0.9 → -1.1 → "fuel-critical" Critical; isRace=false → none
```

- [ ] **Step 3: Run, expect FAIL**
- [ ] **Step 4: Implement thresholds, `EngineerRules` core and `TyresFuel`**
- [ ] **Step 5: Run, expect PASS**
- [ ] **Step 6: Commit** — `"Engineer rules: tyres and fuel"`

---

### Task 5: Gap and lap rules

**Files:**
- Create: `windows-receiver/F1Controller/Engineer/EngineerRules.GapsLap.cs`
- Test: `windows-receiver/F1Controller.Tests/EngineerRulesGapsLapTests.cs`

**Interfaces:**
- Consumes: `EngineerRules`, `EngineerThresholds` (Task 4); `RaceInfo` fields `Position, CurrentLap, Sector, LapInvalid, LastLapMs, BestLapMs, S1Status, S2Status, GapAheadMs, GapBehindMs`.

Calls (category `Gaps` for `gap-*`, `Lap` for the rest):
| Key | Text | Pri | When |
|---|---|---|---|
| `gap-ahead` | "You're catching the car ahead, {0.3} a lap. Gap {1.8}." | Info | `IsRace`; on each lap change record `GapAheadMs`; the last `CatchMinLaps`+1 samples strictly decreasing and latest < `CatchMaxGapMs`. Rate = average drop per lap, one decimal |
| `gap-behind` | "Car behind is closing, {0.4} a lap." | Info | same on `GapBehindMs` with `ClosingMaxGapMs` |
| `lap-invalid` | "Lap deleted, track limits." | Important | `LapInvalid` false → true within the same lap |
| `pb` | "Personal best, {1:31.204}." | Info | `BestLapMs` changes to a lower non-zero value and previous `BestLapMs != 0` |
| `sector-1` / `sector-2` | "Purple sector." / "Green sector." | Chatter | `S1Status` (or `S2Status`) changes to `OverallBest` / `PersonalBest` |
| `last-lap` | "Last lap, bring it home." | Info | `IsRace`, `TotalLaps > 0`, `CurrentLap` changes to `TotalLaps` |
Gap histories (ahead and behind) are cleared when `Position` changes or a gap becomes null. Time format `m:ss.fff`.

- [ ] **Step 1: Extend `Snapshots.Make`** with `position`, `gapAhead`, `gapBehind`, `lapInvalid`, `bestLapMs`, `s1`, `s2`, `totalLaps`.
- [ ] **Step 2: Write the failing tests**

```csharp
[Fact] public void CatchingNeedsTwoLapsOfShrinkingGap()   // laps 5,6,7 gaps 2400,2100,1800 → on lap 7 "You're catching the car ahead, 0.3 a lap. Gap 1.8."; with 2400,2100 only → none
[Fact] public void CatchingIgnoredWhenGapTooBig()         // 4000,3700,3400 → none
[Fact] public void PositionChangeResetsGapHistory()       // 2400,2100 then position 4→3 with 1800 → none   (Review Focus 3)
[Fact] public void ClosingBehind()                        // behind 1900,1500,1100 → "Car behind is closing, 0.4 a lap."
[Fact] public void LapInvalidOncePerLap()                 // false→true → call; true→true → none
[Fact] public void PersonalBestSkipsFirstLap()            // 0→91500 → none; 91500→91204 → "Personal best, 1:31.204."
[Fact] public void PurpleAndGreenSectors()                // S1 None→OverallBest → "Purple sector." Chatter; S2 → PersonalBest → "Green sector."
[Fact] public void LastLapInRaceOnly()                    // totalLaps 28: lap 27→28 → "last-lap"; isRace=false → none
```

- [ ] **Step 3: Run, expect FAIL**
- [ ] **Step 4: Implement `GapsLap`**
- [ ] **Step 5: Run, expect PASS**
- [ ] **Step 6: Commit** — `"Engineer rules: gaps and lap"`

---

### Task 6: Flags, weather, damage and event rules

**Files:**
- Create: `windows-receiver/F1Controller/Engineer/EngineerRules.FlagsWeatherDamage.cs`
- Test: `windows-receiver/F1Controller.Tests/EngineerRulesFlagsTests.cs`

Calls:
| Key | Text | Pri | Cat | When |
|---|---|---|---|---|
| `sc-deployed` | "Safety car deployed." | Critical | Flags | `SafetyCar` → 1 |
| `sc-virtual` | "Virtual safety car." | Critical | Flags | `SafetyCar` → 2 |
| `sc-in` | "Safety car is in, get ready." | Critical | Flags | `SafetyCar` 1 or 2 → 0 |
| `flag-red` | "Red flag." | Critical | Flags | `FiaFlag` → 4 |
| `flag-yellow` | "Yellow flag, careful." | Important | Flags | `FiaFlag` → 3 |
| `flag-blue` | "Blue flags, let them by." | Important | Flags | `FiaFlag` → 2 |
| `rain-forecast` | "Rain expected in {N} minutes." | Important | Weather | `Weather < 3` and `RainInMinutes ≤ RainForecastMinutes` |
| `rain-now` | "It's raining." | Important | Weather | `Weather` crosses from < 3 to ≥ 3 |
| `damage-front-wing` (`-rear-wing`, `-floor`, `-diffuser`, `-sidepod`) | "Front wing damage, {30} percent." (Rear wing / Floor / Diffuser / Sidepod) | Critical | Damage | value ≥ baseline + `DamageStepPct`; then baseline = value. If value < baseline, baseline = value |
| `drs` | "DRS enabled." | Info | Other | `GameEvent DrsEnabled` |
| `penalty` | type 4: "{N} second penalty." · 1: "Stop-go penalty, {N} seconds." · 0: "Drive through penalty." · 6: "We've been disqualified." | Important | Other | `GameEvent Penalty` |
Transitions compare against `previous`; with `previous == null` nothing fires.

- [ ] **Step 1: Extend `Snapshots.Make`** with `weather`, `rainIn`, `fiaFlag`, `damage`, `events`.
- [ ] **Step 2: Write the failing tests**

```csharp
[Fact] public void SafetyCarTransitions()       // 0→1 "Safety car deployed." Critical; 1→0 "Safety car is in, get ready."; 0→2 "Virtual safety car."
[Fact] public void FlagChanges()                // 0→3 yellow Important; 0→4 red Critical; 3→3 none
[Fact] public void RainForecastAndRain()         // weather 1 rainIn 8 → "Rain expected in 8 minutes."; rainIn 20 → none; weather 2→3 → "It's raining."
[Fact] public void DamageStepsOfTen()            // front wing 0→5 none; 5→12 "Front wing damage, 12 percent."; 12→18 none; 18→22 "…22 percent."
[Fact] public void DamageBaselineFollowsRepair() // 30 → 0 (repair) → 10 → call again   (Review Focus 2)
[Fact] public void EventsBecomeCalls()           // DrsEnabled → "DRS enabled."; Penalty(4,5) → "5 second penalty."; Penalty(1,10) → "Stop-go penalty, 10 seconds."
```

- [ ] **Step 3: Run, expect FAIL**
- [ ] **Step 4: Implement `FlagsWeatherDamage`**
- [ ] **Step 5: Run, expect PASS**
- [ ] **Step 6: Commit** — `"Engineer rules: flags, weather, damage, events"`

---

### Task 7: RadioDispatcher

**Files:**
- Create: `windows-receiver/F1Controller/Engineer/RadioDispatcher.cs`
- Test: `windows-receiver/F1Controller.Tests/RadioDispatcherTests.cs`

**Interfaces:**
- Produces: `public sealed class RadioDispatcher { public RadioDispatcher(EngineerThresholds t); public void Enqueue(IEnumerable<RadioCall> calls, float now); public RadioCall? Next(float now); public void Reset(); }`
  - `Enqueue`: skip a call whose key was sent less than `t.CooldownFor(key)` ago, or whose key is already queued. Remember enqueue time.
  - `Next`: drop queued non-critical calls older than `StaleSeconds`; pick highest priority, then oldest; return it only if it's critical or `now - lastSent ≥ MinSpacingSeconds`; record sent time per key and `lastSent`. Otherwise null.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void SpacesCallsFourSeconds()        // enqueue A,B (Info) at 10 → Next(10)=A, Next(12)=null, Next(14)=B
[Fact] public void CriticalBypassesSpacing()       // send Info at 10, enqueue Critical at 11 → Next(11) = critical
[Fact] public void HighestPriorityFirst()          // enqueue Info then Important at 10 → Next(10) = Important
[Fact] public void CooldownBlocksRepeat()          // send "tyre-hot-FL" at 10; enqueue it at 40 → never returned; at 71 → returned
[Fact] public void ZeroCooldownKeysRepeat()        // "sc-deployed" sent at 10, enqueued at 11 → returned
[Fact] public void StaleNonCriticalDropped()       // enqueue Info at 10, Next only at 26 → null
[Fact] public void DuplicateQueuedKeyIgnored()     // enqueue same key twice at 10 → Next(10) returns it, Next(20) null
```

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement**
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** — `"Add radio dispatcher"`

---

### Task 8: EngineerService, RadioSender, Program wiring, `--radio-demo`

**Files:**
- Create: `windows-receiver/F1Controller/Engineer/EngineerService.cs`
- Create: `windows-receiver/F1Controller/Transport/RadioSender.cs`
- Modify: `windows-receiver/F1Controller/Program.cs`
- Test: `windows-receiver/F1Controller.Tests/EngineerServiceTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class EngineerService { public EngineerService(EngineerThresholds t, Action<RadioCall> send); public void OnSnapshot(RaceSnapshot s); }`
    Resets rules + dispatcher and starts a new warm-up when `s.SessionUid` differs from the last one or `s.SessionTime` is lower than the last. During the first `WarmupSeconds` after a (re)start, runs `Evaluate` (to prime memory) but discards the calls. Otherwise `Enqueue` then `Next` once per snapshot; a non-null result goes to `send`.
  - `public sealed class RadioSender { public RadioSender(UdpReceiver receiver); public void Send(RadioCall call); }`
    Increments a `ushort` call ID, writes with `ControlPacket.WriteRadioCall` into its own buffer, and sends 3 times 30 ms apart (`Task.Run` with `Task.Delay(30)`; each send copies the exact-length slice, because `UdpReceiver.SendToPhone(byte[])` sends the whole array). Prints `[Engineer] {text}` to the console.
- Consumes: `TelemetryReceiver.OnSnapshot` (Task 3), `UdpReceiver.SendToPhone(byte[])`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void SilentDuringWarmup()       // snapshots at t=0..2.9 with a red flag change at 1.0 → nothing sent; red flag change at t=4 → sent
[Fact] public void SessionRestartResets()     // run to t=100 with wear 51 (tyre-wear-50 sent); snapshot t=5 → warm-up again; after warm-up, 49→51 → sent again
[Fact] public void NewSessionUidResets()      // same as above but time keeps rising and SessionUid changes
```

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement `EngineerService` and `RadioSender`**
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Wire in `Program.cs`** inside the existing `if (telemetry != null && !noTelemetry)` block: `var radio = new RadioSender(receiver); var engineer = new EngineerService(new EngineerThresholds(), radio.Send); telemetry.OnSnapshot += engineer.OnSnapshot;`
- [ ] **Step 6: Add `--radio-demo`**: after startup, once the phone is connected (`receiver.IsReceiving`), send one call of each priority 5 s apart ("Purple sector." Chatter, "DRS enabled." Info, "Front left is overheating, manage it." Important, "Safety car deployed." Critical), then continue normally. Add it to `PrintUsage`.
- [ ] **Step 7: Build and test** — `dotnet test windows-receiver/F1Controller.sln` → all pass; `dotnet build -c Release` → 0 errors.
- [ ] **Step 8: Commit** — `"Wire the engineer into the receiver; add --radio-demo"`

---

### Task 9: Phone — radio packet decoding and transport flow

**Files:**
- Modify: `android/app/build.gradle.kts` — `testImplementation("junit:junit:4.13.2")`
- Create: `android/app/src/main/java/com/f1controller/network/RadioPacket.kt`
- Modify: `android/app/src/main/java/com/f1controller/network/UdpTransport.kt`
- Test: `android/app/src/test/java/com/f1controller/network/RadioPacketTest.kt`

**Interfaces:**
- Produces:
  - `data class RadioCall(val id: Int, val priority: Int, val category: Int, val text: String)`
  - `fun decodeRadioCall(bb: ByteBuffer): RadioCall?` — `bb` is little-endian, positioned just after the marker; returns null if fewer than 5 bytes remain or `textLen` exceeds what remains.
  - In `UdpTransport`: `val radio: SharedFlow<RadioCall>` (`MutableSharedFlow(extraBufferCapacity = 8)`, `tryEmit`), `RADIO_MARKER = 0xFFFFFFFC.toInt()`. Receive buffer 64 → 256 bytes. Drop a call whose `id` is among the last 16 IDs seen (clear the list in `connect()`). Add the layout to the class header comment.

- [ ] **Step 1: Write the failing test** — same bytes as the C# `WritesHeaderAndText` test:

```kotlin
@Test fun decodesCall() {
    val bytes = byteArrayOf(0xFC.toByte(), -1, -1, -1, 0x34, 0x12, 2, 0, 3, 'B'.code.toByte(), 'o'.code.toByte(), 'x'.code.toByte())
    val bb = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).apply { position(4) }
    assertEquals(RadioCall(0x1234, 2, 0, "Box"), decodeRadioCall(bb))
}
@Test fun truncatedPacketIsNull()  // textLen 10 but only 3 bytes → null
```

- [ ] **Step 2: Run, expect FAIL** — `./gradlew :app:testDebugUnitTest`
- [ ] **Step 3: Implement `RadioPacket.kt` and the `UdpTransport` changes** (`RADIO_MARKER ->` branch in `receiveLoop`; minimum length for radio is 9, so the existing `< 16` check must let radio packets through: check the marker first)
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** — `"Phone: decode engineer radio packets"`

---

### Task 10: Phone — engineer settings and filtering

**Files:**
- Create: `android/app/src/main/java/com/f1controller/engineer/EngineerSettings.kt`
- Modify: `android/app/src/main/java/com/f1controller/MainActivity.kt` (load/save prefs)
- Test: `android/app/src/test/java/com/f1controller/engineer/EngineerSettingsTest.kt`

**Interfaces:**
- Produces:
  - `class EngineerSettings { var level by mutableIntStateOf(2); var voice by mutableStateOf(true); var vibrate by mutableStateOf(true) }` (pattern of `PedalSettings`)
  - `fun shouldPlay(level: Int, priority: Int): Boolean` — top-level, pure.
  - `val ENGINEER_LEVEL_NAMES = listOf("Off", "Quiet", "Balanced", "Talkative")`

- [ ] **Step 1: Write the failing test**

```kotlin
@Test fun levels() {
    assertFalse(shouldPlay(0, 3))
    assertTrue(shouldPlay(1, 2)); assertFalse(shouldPlay(1, 1))
    assertTrue(shouldPlay(2, 1)); assertFalse(shouldPlay(2, 0))
    assertTrue(shouldPlay(3, 0))
}
```

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement; load/save `engineer_level`, `engineer_voice`, `engineer_vibrate` in `MainActivity.loadSettings/saveSettings`**
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** — `"Phone: engineer settings"`

---

### Task 11: Phone — voice, vibration, banner, settings section

**Files:**
- Create: `android/app/src/main/java/com/f1controller/engineer/EngineerRadio.kt`
- Create: `android/app/src/main/java/com/f1controller/ui/EngineerBanner.kt`
- Modify: `android/app/src/main/java/com/f1controller/ui/ControllerScreen.kt:186`, `ui/SettingsScreen.kt`, `MainActivity.kt`

**Interfaces:**
- Produces:
  - `class EngineerRadio(context: Context, settings: EngineerSettings, scope: CoroutineScope)`:
    `val current: StateFlow<RadioCall?>`, `val voiceAvailable: StateFlow<Boolean>`, `fun play(call: RadioCall)` (applies `shouldPlay`, sets `current`, speaks, vibrates), `fun shutdown()`.
    Clears `current` when both 4 s have passed and the utterance is done (`UtteranceProgressListener.onDone`/`onError`), unless a newer call replaced it. Vibrator via `VibratorManager` on API 31+, else `Vibrator`; `VibrationEffect.createWaveform`.
  - `@Composable fun EngineerBanner(call: RadioCall)` — "ENGINEER" label, 1–2 lines of text (ellipsis beyond), left colour bar: priority ≤ 1 `Palette.Cyan`, 2 `Palette.Yellow`, 3 `Palette.Red`; fade in/out with `AnimatedVisibility`.
- Consumes: `UdpTransport.radio` (Task 9), `EngineerSettings`, `shouldPlay` (Task 10).

- [ ] **Step 1: Implement `EngineerRadio`**; in `MainActivity.onCreate` create it and collect `transport.radio` in `lifecycleScope` → `engineerRadio.play(it)`; call `shutdown()` in `onDestroy`.
- [ ] **Step 2: Implement `EngineerBanner`; in `ControllerScreen` take `radioCall: RadioCall?` and replace line 186 with** `if (radioCall != null) EngineerBanner(radioCall) else race?.let { InfoStrip(it) }`. Pass `engineerRadio.current.collectAsState()` down from `F1ControllerApp`.
- [ ] **Step 3: Add `SectionCard("Engineer")` to `SettingsScreen`** (after "Pedals"): chattiness as 4 segmented buttons using `ENGINEER_LEVEL_NAMES`, Voice switch, Vibration switch, "Voice unavailable" text when `voiceAvailable` is false, and a "Test call" button that calls `engineerRadio.play(RadioCall(-1, 2, 0, "Radio check, how do you hear me?"))`.
- [ ] **Step 4: Build and unit tests** — `./gradlew :app:assembleDebug :app:testDebugUnitTest` → BUILD SUCCESSFUL.
- [ ] **Step 5: Manual check on the phone** — `./gradlew :app:installDebug` (retry once if it fails; known flaky). Settings → Engineer → Test call: banner shows, voice speaks, vibration buzzes twice. Then ask the user to run the receiver with `--radio-demo`: on Balanced the Chatter call is not shown, the other three are; on Talkative all four; Critical interrupts speech; Off shows nothing.
- [ ] **Step 6: Commit** — `"Phone: engineer banner, voice and vibration"`

---

### Task 12: In-game check

- [ ] **Step 1:** User runs one F1 2021 race (Balanced) with the new receiver and app.
- [ ] **Step 2:** Collect feedback: calls that were wrong, missing, too frequent, badly worded. Receiver console prints every sent call (`[Engineer] …`) for reference.
- [ ] **Step 3:** Adjust `EngineerThresholds` / wording; tests updated to match; commit `"Tune engineer thresholds after first race"`.
