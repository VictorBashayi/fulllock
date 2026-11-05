# FullLock Pairing + Audit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close audit findings #1–#6 for v1.0: a pair code (typed or QR) authenticates every UDP datagram between phone and PC; plus the quick fixes and hardening.

**Architecture:** Platform crypto only. A 10-char code → PBKDF2 key; every control-port datagram gets a `ctr:u32 ‖ tag:8` trailer (HMAC-SHA256). Ping/pong carry nonces to form a session; the PC's `Session` gatekeeps all receives and seals all sends; the phone verifies pongs before trusting any server. Pairing logic lives in UI-agnostic classes (`Security/*`, `network/Pairing*`) for the future GUI.

**Tech Stack:** .NET 8 (C#, xUnit), QRCoder 1.6.0, System.Security.Cryptography.ProtectedData 8.0.0; Android Kotlin/Compose, minSdk 26, JUnit 4, zxing-android-embedded 4.3.0, Android Keystore; Inno Setup.

**Spec:** `docs/superpowers/specs/2026-11-05-fulllock-pairing-design.md` (b6924be). Parent plan: `docs/superpowers/plans/2026-11-04-fulllock-v1-release.md` — this plan is its Task 10 Step 2; afterwards resume there (remove TEMP trigger, Task 11).

## Global Constraints

- Code alphabet `0123456789ABCDEFGHJKMNPQRSTVWXYZ`, length 10, display `XXXX-XXXX-XX`; normalize: uppercase, drop spaces/dashes, `I`/`L`→`1`, `O`→`0`, else invalid.
- Key = PBKDF2-HMAC-SHA256(ASCII normalized code, salt ASCII `FullLock-pair-v1`, 200_000 iterations, 32 bytes).
- Trailer = `ctr:u32 LE ‖ tag[0..8]`, tag = HMAC-SHA256(key, `dir ‖ ctx ‖ payload ‖ ctr`), `dir` 0x01 phone→PC, 0x02 PC→phone; ctx = `pcSession(8) ‖ phoneNonce(8)`, empty for pings.
- Ping payload `[FFFFFFFF][seq:u32][ts_us:u64][level:u8][phoneNonce:8]` (25 B); pong payload `[FFFFFFFF][seq:u32][ts_us:u64][pcSession:8]` (24 B), sealed with ctx = pcSession ‖ ping's nonce.
- Beacon `[FBFFFFFF]["FLRX"][controlPort:u16 LE][0x02]` (11 B), unauthenticated hint.
- Bad tag / short / stale `ctr` → drop silently, increment `RejectedPackets`. Constant-time tag compare.
- QR text: `fulllock://pair?code=<normalized>`.
- Phone-visible states exactly: "Looking for PC…", "PC found — pair", "Code doesn't match this PC — check the code on the PC screen", "PC's pair code changed — re-pair", "Paired ✓".
- Little-endian everywhere. No crypto libraries beyond the platform.
- Tests: `dotnet test windows-receiver/F1Controller.sln` (all pass); `cd android; .\gradlew.bat testDebugUnitTest` (all pass).

## Review Focus

1. **Phone restart / reconnect mid-race** — new nonce must take over within one valid control packet; no stuck "old session" that ignores the phone. Test in Task 4 (`NewNonceTakesOverAfterValidControl`).
2. **PC restarted while phone stays connected** — new pcSession; the phone must accept the new pong and reset its PC `ctr` tracking, not treat it as stale. Test in Task 7 (`PongWithNewPcSessionResetsCounter`).
3. **Old "F1 Controller" app / v1 packets still running on the LAN** — 18-byte unauthenticated packets must be rejected without crashing or moving the pad. Test in Task 4 (`V1ControlPacketIsRejected`).
4. **QR shown in a small console window** — must fall back to text code, not wrap into garbage or throw. Test in Task 6 (`TooNarrowReturnsNoQr`).
5. **Telemetry/radio sent before any phone paired** — `SendToPhone` must no-op, not throw or send unauthenticated data. Test in Task 5 (`SendToPhoneBeforePairingSendsNothing`).

## Test vectors (computed independently with Python `hashlib`/`hmac`)

Write these to `docs/protocol/pairing-test-vectors.md` in Task 2; both suites assert them.

| Name | Value (hex unless noted) |
|---|---|
| code | `K7QD9MXP2A` (input variants: `k7qd-9mxp-2a`, ` K7QD 9MXP 2A `) |
| key | `60a20da146d5592c8a6ad61091a0551ecf63753c657085b111d631a5dbdc99d9` |
| ctx | `0102030405060708` ‖ `1112131415161718` (pcSession ‖ phoneNonce) |
| V1 control payload (seq 1, steer 0x4000, throttle 0x80, brake 0, buttons 0x1000) | `010000000000000000000000004080000010` |
| V1 tag (dir 01, ctx, ctr 7) | `f363ac24a73c7d68` |
| V2 ping payload (seq 1, ts 0, level 2, nonce 1112…18) | `ffffffff010000000000000000000000021112131415161718` |
| V2 tag (dir 01, ctx empty, ctr 1) | `5826a84a01af4962` |
| V3 game-state payload | `feffffff033200000000000000000000` |
| V3 tag (dir 02, ctx, ctr 5) | `1338ee307bedb447` |

---

### Task 1: Audit quick fixes and hardening (#5, #6, firewall, repo hygiene)

**Files:**
- Modify: `windows-receiver/F1Controller/Validation/RaceTracker.cs:~209` (bin calc), `windows-receiver/F1Controller/Validation/TelemetryReceiver.cs:~150` (Handle call)
- Modify: `installer/FullLock.iss` (Setup + firewall line 52), `.gitignore`, `windows-receiver/F1Controller/F1Controller.csproj`, `docs/superpowers/plans/2026-11-04-fulllock-v1-release.md:273`
- Add (commit): `docs/security/2026-11-audit.md` (already present, untracked)
- Test: `windows-receiver/F1Controller.Tests/RaceTrackerTests.cs`, new `TelemetryReceiverTests.cs`

**Interfaces:** Produces `TelemetryReceiver.HandleSafely(byte[] buffer)` (internal-visible via public; per-datagram try/catch around `Handle`, logs once per distinct message to `Console.Error`/existing log, returns `bool` handled).

- [ ] **Step 1: Failing tests.** In `RaceTrackerTests`: `LapDistanceNonFiniteOrNegativeDoesNotThrow` — `[Theory]` over `float.NaN, float.PositiveInfinity, float.NegativeInfinity, -5f, 1e30f`; feed a player Lap Data packet with that `lapDistance` through the same helper the existing tests use; assert no exception and delta is null. In `TelemetryReceiverTests`: `HandleSafelySurvivesThrowingSubscriber` — subscriber to `OnSnapshot`/`OnStateChanged` throws; `HandleSafely(validPacket)` returns false and a following `HandleSafely` call still raises the event.
- [ ] **Step 2:** `dotnet test windows-receiver/F1Controller.sln` → the +∞/1e30 cases FAIL (IndexOutOfRange), HandleSafely doesn't compile.
- [ ] **Step 3: Implement.** RaceTracker: guard `float.IsFinite(lapDist) && lapDist >= 0` before computing `bin`, and require `bin >= 0 && bin < MaxBins - 1`. TelemetryReceiver: add `HandleSafely`, call it from `RunAsync` instead of `Handle`.
- [ ] **Step 4: Installer.** `FullLock.iss` `[Setup]`: `DisableDirPage=yes` (DefaultDirName stays `{autopf}\FullLock`; also add `UsePreviousAppDir=no` so an old custom dir is not reused). Firewall add rule: append `localport=5555,20777 remoteip=localsubnet` to the netsh parameters. Note in README (Task 10) that `--port` other than 5555 needs a manual firewall rule.
- [ ] **Step 5: Hygiene.** `.gitignore` add `*.pem`, `*.key`, `*.p12`, `*.pfx`, `.env`. Remove `<AllowUnsafeBlocks>` (no `unsafe`/`fixed` in the codebase) — keep removed only if build + tests pass. Replace the MAC `<DS4-MAC>` in the parent plan line 273 with `<DS4 MAC>`.
- [ ] **Step 6:** `dotnet test windows-receiver/F1Controller.sln` → all pass. Build installer with the existing `installer/build.ps1` → succeeds.
- [ ] **Step 7: Commit** `git add -A windows-receiver installer .gitignore docs/security docs/superpowers/plans/2026-11-04-fulllock-v1-release.md` → `fix: audit #5 #6, firewall scope, repo hygiene`.

### Task 2: C# PairingCode + PacketAuth + test vectors

**Files:**
- Create: `windows-receiver/F1Controller/Security/PairingCode.cs`, `windows-receiver/F1Controller/Security/PacketAuth.cs`, `docs/protocol/pairing-test-vectors.md`
- Test: `windows-receiver/F1Controller.Tests/PairingCodeTests.cs`, `PacketAuthTests.cs`

**Interfaces — Produces:**
```csharp
namespace F1Controller.Security;
public static class PairingCode {
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int Length = 10;
    public static string Generate();                     // RandomNumberGenerator.GetItems
    public static string? Normalize(string? input);      // null if invalid
    public static string Format(string normalized);      // "K7QD-9MXP-2A"
    public static string ToQrText(string normalized);    // "fulllock://pair?code=K7QD9MXP2A"
}
public static class PacketAuth {
    public const int TagSize = 8, CtrSize = 4, TrailerSize = 12, ContextSize = 16;
    public const byte DirPhoneToPc = 0x01, DirPcToPhone = 0x02;
    public static byte[] DeriveKey(string normalizedCode);
    // writes payload ‖ ctr ‖ tag into dest, returns payload.Length + TrailerSize
    public static int Seal(byte[] key, byte dir, ReadOnlySpan<byte> ctx, ReadOnlySpan<byte> payload, uint ctr, Span<byte> dest);
    // false if too short or tag mismatch; on success payloadLength = datagram.Length - TrailerSize
    public static bool TryOpen(byte[] key, byte dir, ReadOnlySpan<byte> ctx, ReadOnlySpan<byte> datagram, out int payloadLength, out uint ctr);
}
```

- [ ] **Step 1: Write vectors file** with the table from this plan's "Test vectors" section, plus one line: "Generated with Python hashlib.pbkdf2_hmac / hmac; C# and Kotlin suites must match."
- [ ] **Step 2: Failing tests.** `PairingCodeTests`: `GenerateUsesAlphabetAndLength` (1000 codes, every char in Alphabet, length 10, not all equal); `NormalizeAcceptsVariants` (`"k7qd-9mxp-2a"`, `" K7QD 9MXP 2A "` → `"K7QD9MXP2A"`); `NormalizeMapsLookalikes` (`"K7QD9MXPIO"` → `"K7QD9MXP10"`, `"l"`→`"1"`); `NormalizeRejects` (`null`, `""`, 9 chars, 11 chars, `"K7QD9MXP2U"`, `"K7QD9MXP2!"` → null); `FormatGroups` → `"K7QD-9MXP-2A"`; `QrText` → `"fulllock://pair?code=K7QD9MXP2A"`. `PacketAuthTests`: `DeriveKeyMatchesVector`; `SealMatchesVector1/2/3` (dest = payload ‖ ctr LE ‖ tag hex from table); `TryOpenRoundTrips`; `TryOpenRejectsAnySingleBitFlip` (flip each bit of sealed V1 → false); `TryOpenRejectsWrongDirAndCtx`; `TryOpenRejectsShorterThanTrailer` (0..11 bytes).
- [ ] **Step 3:** run `dotnet test windows-receiver/F1Controller.sln --filter "PairingCode|PacketAuth"` → FAIL (types missing).
- [ ] **Step 4: Implement** both classes. `Rfc2898DeriveBytes.Pbkdf2(..., HashAlgorithmName.SHA256, 32)`; `IncrementalHash.CreateHMAC` (no per-packet allocations beyond the hash object is fine); compare with `CryptographicOperations.FixedTimeEquals`.
- [ ] **Step 5:** same filter → PASS; full suite → PASS.
- [ ] **Step 6: Commit** `feat(security): pair code and packet authentication`.

### Task 3: C# PairingStore

**Files:**
- Create: `windows-receiver/F1Controller/Security/PairingStore.cs`
- Modify: `windows-receiver/F1Controller/AppPaths.cs` (add `PairingFile => Path.Combine(roamingDir, "pairing.json")`), `F1Controller.csproj` (`<PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />`)
- Test: `windows-receiver/F1Controller.Tests/PairingStoreTests.cs`, `AppPathsTests.cs`

**Interfaces — Consumes:** `PairingCode`, `PacketAuth.DeriveKey`. **Produces:**
```csharp
public sealed class PairingStore {
    public string Code { get; }            // normalized
    public byte[] Key { get; }
    public bool WasCreated { get; }        // true if file was absent/corrupt and a new code was made
    public bool WasReset { get; }          // true only if a file existed but was unreadable/corrupt
    public static PairingStore LoadOrCreate(string path);   // DPAPI CurrentUser
    public static PairingStore LoadOrCreate(string path, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect);
    public static PairingStore CreateNew(string path, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect); // --new-code
    public static PairingStore CreateNew(string path);
}
```
File JSON: `{ "version": 1, "code": "<base64(protect(ascii code))>" }`. Create parent dir.

- [ ] **Step 1: Failing tests** (temp dir, identity protect/unprotect): `CreatesFileOnFirstRun` (WasCreated true, WasReset false, file exists, Code valid); `LoadsSameCodeSecondTime` (WasCreated false, same Code, same Key); `CorruptFileMakesNewCode` (write `"{not json"` → WasCreated true, WasReset true, new valid code, file rewritten); `UnprotectFailureMakesNewCode` (unprotect throws `CryptographicException` → WasCreated true); `CreateNewReplacesCode` (differs from previous; reload returns new one); `FileDoesNotContainPlainCode` with a XOR protect → file text doesn't contain Code. `AppPathsTests`: `PairingFileInRoaming`.
- [ ] **Step 2:** run filter `PairingStore|AppPaths` → FAIL.
- [ ] **Step 3: Implement.** DPAPI overloads use `ProtectedData.Protect/Unprotect(data, entropy: "FullLock-pair-v1"u8.ToArray(), DataProtectionScope.CurrentUser)`. Any read/parse/unprotect/normalize failure → create new.
- [ ] **Step 4:** filter → PASS; full suite → PASS.
- [ ] **Step 5: Commit** `feat(security): DPAPI-protected pairing store`.

### Task 4: C# Session (gatekeeper)

**Files:**
- Create: `windows-receiver/F1Controller/Security/Session.cs`
- Test: `windows-receiver/F1Controller.Tests/SessionTests.cs`

**Interfaces — Consumes:** `PacketAuth`, `ControlPacket.PingMarker/PingSize/Size`. **Produces:**
```csharp
public enum AcceptResult { Rejected, Ping, Control, ControlNewPhone }
public sealed class Session {
    public const int NonceSize = 8, PingPayloadSize = 25, PongPayloadSize = 24;
    public Session(byte[] key);                                  // random pcSession
    public Session(byte[] key, byte[] pcSession);                // tests
    public ReadOnlyMemory<byte> PcSession { get; }
    public IPEndPoint? ActiveEndPoint { get; }
    public bool HasPaired { get; }                               // a valid control packet seen this run
    public long RejectedPackets { get; }
    public event Action? Paired;                                 // first time HasPaired becomes true
    // On success payload = datagram[..payloadLength]
    public AcceptResult Accept(ReadOnlySpan<byte> datagram, IPEndPoint from, out int payloadLength);
    public byte[] BuildPong(ReadOnlySpan<byte> pingPayload);     // sealed pong for that ping's nonce, next PC ctr
    public byte[]? SealForActive(ReadOnlySpan<byte> payload);    // null if no active phone
}
```
Rules (spec §3.3): datagram starting with `FFFFFFFF` → verify as ping (ctx empty, length == PingPayloadSize + Trailer); valid → remember nonce in a pending list (max 4, most recent wins) → `Ping` (no ctr check, no active change). Otherwise try ctx = pcSession ‖ active nonce, then each pending nonce; valid and ctr > last for that nonce → if nonce == active: `Control`; else promote to active (fresh ctr tracking), set ActiveEndPoint=from → `ControlNewPhone`. ActiveEndPoint also updates to `from` on each valid Control (phone roaming). Everything else → Rejected, RejectedPackets++. One PC send counter per run (starts at 1) shared by `BuildPong`/`SealForActive`. Thread-safe (`lock`), since sealing runs on telemetry/radio threads.

- [ ] **Step 1: Failing tests** (helper `Phone` in test file that seals with `PacketAuth` like the app would): `ValidPingReturnsPingAndDoesNotActivate` (ActiveEndPoint null, HasPaired false); `PongVerifiesWithPhoneNonce` (TryOpen dir 02 ctx = pcSession‖nonce succeeds; payload[16..24] == PcSession; marker and seq/ts echoed); `ControlAfterPingActivatesAndRaisesPairedOnce`; `ControlWithoutPingIsRejected`; `ReplayedControlIsRejected` (same ctr twice → second Rejected); `OlderCtrIsRejected`; `V1ControlPacketIsRejected` (raw 18 bytes, and 18 + 12 garbage); `WrongKeyRejected`; `InvalidPacketDoesNotChangeActiveEndPoint` (attacker endpoint sends garbage + forged-tag packets → ActiveEndPoint unchanged — audit #2); `PingAloneDoesNotStealActive` (valid replayed ping from attacker endpoint → ActiveEndPoint unchanged); `NewNonceTakesOverAfterValidControl` (phone B pings + 1 control → ControlNewPhone, ActiveEndPoint = B; then A's next control with old nonce → Rejected unless A pings again); `SealForActiveNullBeforePairing`; `SealForActiveCountersIncrease` (pong ctr 1, then sealed data ctr 2, 3 — verifiable by phone helper); `RejectedPacketsCounts`.
- [ ] **Step 2:** filter `Session` → FAIL.
- [ ] **Step 3: Implement** `Session`.
- [ ] **Step 4:** filter → PASS; full suite → PASS.
- [ ] **Step 5: Commit** `feat(security): session gatekeeper for paired phones`.

### Task 5: Wire Session into UdpReceiver; beacon v2

**Files:**
- Modify: `windows-receiver/F1Controller/Transport/UdpReceiver.cs`, `Transport/PacketParser.cs` (`WriteDiscovery`, `DiscoverySize = 11`), `Transport/DiscoveryBeacon.cs` if it hardcodes size
- Test: `windows-receiver/F1Controller.Tests/DiscoveryBeaconTests.cs` (update), new `UdpReceiverLoopbackTests.cs`

**Interfaces — Consumes:** `Session` (Task 4). **Produces:** `UdpReceiver(int port, bool logTransport, Session session)`; `long RejectedPackets => session.RejectedPackets`; `SendToPhone(byte[] data)` now seals via `session.SealForActive` (unchanged signature, so `Program.cs`/`RadioSender` callers stay). `Session Session { get; }`.

- [ ] **Step 1: Failing tests.** `DiscoveryBeaconTests`: expected bytes `FB FF FF FF 'F' 'L' 'R' 'X' B3 15 02`. `UdpReceiverLoopbackTests` (real UDP on 127.0.0.1, port 0 / a free port; add an `int BoundPort` property if needed for tests): `PairedPhoneDrivesPad` (fake phone pings → receives a pong that verifies → sends sealed control with steer 0x4000 → `OnPacketReceived` fires with Steer 0x4000); `ForgedAndReplayedPacketsDoNotFire` (unsealed V1 packet, wrong-key packet, replay of the valid control → no event, RejectedPackets ≥ 3); `SendToPhoneBeforePairingSendsNothing` (fake phone socket receives nothing within 200 ms; no exception); `SendToPhoneAfterPairingIsSealed` (phone receives datagram that `TryOpen`s with dir 02 and session ctx).
- [ ] **Step 2:** run filter `DiscoveryBeacon|UdpReceiverLoopback` → FAIL.
- [ ] **Step 3: Implement.** Receive loop: `session.Accept` first; `Ping` → `EngineerLevel = ControlPacket.ReadPingLevel(payload)`, send `session.BuildPong(payload)` to `result.RemoteEndPoint`; `Control`/`ControlNewPhone` → `ControlPacket.Parse(payload)`; on `ControlNewPhone` reset `_lastSeq` (new phone counter). Remove the `|delta| ≥ 1000` reset branch (`delta <= 0` cannot happen any more; keep loss counting for `delta > 0`). `RemoteAddress` and the send target come from `session.ActiveEndPoint`; delete `_phoneEndPoint` writes from raw receives. Update `Program.cs`: `var pairing = PairingStore.LoadOrCreate(AppPaths.Default.PairingFile); var session = new Session(pairing.Key);` passed to `UdpReceiver` (Task 6 adds the UI). Beacon: write "FLRX" + version byte 2.
- [ ] **Step 4:** filter → PASS; full suite → PASS.
- [ ] **Step 5: Commit** `feat(transport): authenticated control port and beacon v2`.

### Task 6: PC pairing UI — QR panel, P key, `--new-code`

**Files:**
- Create: `windows-receiver/F1Controller/UI/QrPanel.cs`
- Modify: `UI/ConsoleDashboard.cs`, `Program.cs`, `F1Controller.csproj` (`<PackageReference Include="QRCoder" Version="1.6.0" />`), `installer/THIRD-PARTY-NOTICES.txt` (QRCoder MIT)
- Test: `windows-receiver/F1Controller.Tests/QrPanelTests.cs`, `ConsoleDashboardTests.cs` (new, for key/visibility logic only)

**Interfaces — Consumes:** `PairingStore`, `Session.HasPaired/RejectedPackets`, `PairingCode.Format/ToQrText`. **Produces:**
```csharp
public static class QrPanel {
    // Half-block lines (▀ ▄ █ and space), 2-module quiet zone, dark modules = '█' half on light background,
    // or null if the QR (incl. quiet zone) is wider than maxWidth or taller than maxHeight lines.
    public static IReadOnlyList<string>? Render(string text, int maxWidth, int maxHeight);
}
// ConsoleDashboard
public ConsoleDashboard(UdpReceiver receiver, XInputController? xinput, CorrelationLogger? correlation, RadioSender radio, PairingStore pairing);
public bool ShowPairing { get; }       // !receiver.Session.HasPaired || toggled on
public void HandleKey(char key);       // 'p' / 'P' toggles
```

- [ ] **Step 1: Failing tests.** `QrPanelTests`: `RendersSquareWithQuietZone` (`Render(ToQrText("K7QD9MXP2A"), 80, 40)` → all lines same length; first line is all spaces-equivalent quiet zone; line count == ceil((modules+4)/2)); `MatrixMatchesQRCoder` (rebuild the boolean grid from the half-block chars and compare to `QRCodeGenerator.CreateQrCode(text, ECCLevel.M).ModuleMatrix` with quiet zone); `TooNarrowReturnsNoQr` (`maxWidth` 20 → null); `TooShortReturnsNoQr`. `ConsoleDashboardTests`: `PairingShownUntilPaired`, `PKeyTogglesAfterPaired`, `OtherKeysIgnored` (construct with a `UdpReceiver` + `Session` not started; drive `HasPaired` via a sealed control through `Session.Accept`).
- [ ] **Step 2:** filter `QrPanel|ConsoleDashboard` → FAIL.
- [ ] **Step 3: Implement.** QRCoder ECC level M, `drawQuietZones: false` and add 2 modules yourself. Dashboard: when `ShowPairing`, draw the QR (fit to `Console.WindowWidth/Height` minus the status rows) with `Pair code: XXXX-XXXX-XX` and `Scan or type this in the FullLock app` beneath; when the QR doesn't fit, only the two text lines. When paired: status line `Paired ✓ (P = show code)` and `Rejected packets: N` when N > 0. If `pairing.WasReset`, show one notice line `Pair code was reset — re-pair your phone`. Program: parse `--new-code` → `PairingStore.CreateNew(AppPaths.Default.PairingFile)`, print `New pair code: XXXX-XXXX-XX — re-pair every phone.`, exit 0; add to `PrintUsage`. Startup header prints `Pair code:  XXXX-XXXX-XX`. Dashboard loop polls `Console.KeyAvailable` → `dashboard.HandleKey` (same try pattern as `PadMode.cs:147`). Pad mode unchanged.
- [ ] **Step 4:** filter → PASS; full suite → PASS. Manual: `dotnet run --project windows-receiver/F1Controller -- --no-vigem` shows the QR; scan it with any phone camera app → text `fulllock://pair?code=…` matches the printed code; resize window narrow → text-only fallback.
- [ ] **Step 5: Commit** `feat(ui): pairing QR panel, P key, --new-code`.

### Task 7: Kotlin PairingCode, PacketAuth, beacon v2

**Files:**
- Create: `android/app/src/main/java/com/f1controller/network/PairingCode.kt`, `network/PacketAuth.kt`
- Modify: `network/DiscoveryListener.kt` (`decodeDiscovery`), `network/Ping.kt` (v2 ping writer)
- Test: `android/app/src/test/java/com/f1controller/network/PairingCodeTest.kt`, `PacketAuthTest.kt`, update `DiscoveryPacketTest.kt`, `PingTest.kt`

**Interfaces — Produces:**
```kotlin
object PairingCode {
    const val ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; const val LENGTH = 10
    fun normalize(input: String?): String?
    fun format(normalized: String): String                 // also used for as-you-type formatting of partial input
    fun fromQrText(text: String): String?                  // only "fulllock://pair?code=…" (case-insensitive scheme/host), normalized
}
class PacketAuth(key: java.security.Key) {                 // ThreadLocal<Mac>("HmacSHA256")
    companion object {
        const val TAG_SIZE = 8; const val CTR_SIZE = 4; const val TRAILER_SIZE = 12
        const val DIR_PHONE_TO_PC: Byte = 1; const val DIR_PC_TO_PHONE: Byte = 2
        fun deriveKey(normalizedCode: String): ByteArray   // PBKDF2WithHmacSHA256
        fun keyFromRaw(raw: ByteArray): java.security.Key  // SecretKeySpec(raw, "HmacSHA256")
    }
    /** Writes payload[0 until len] ‖ ctr ‖ tag into out; returns len + TRAILER_SIZE. */
    fun seal(dir: Byte, ctx: ByteArray, payload: ByteArray, len: Int, ctr: Int, out: ByteArray): Int
    /** Null if too short or tag mismatch. */
    fun open(dir: Byte, ctx: ByteArray, data: ByteArray, len: Int): Opened?
    data class Opened(val payloadLen: Int, val ctr: Long)  // ctr as unsigned
}
fun writePing(buf: ByteBuffer, seq: Int, timestampUs: Long, level: Int, nonce: ByteArray)  // PING_PAYLOAD_SIZE = 25
fun decodeDiscovery(bytes: ByteArray, length: Int): Int?  // requires 11 bytes, "FLRX", version 2
```

- [ ] **Step 1: Failing tests.** Same cases and vectors as Task 2 (`deriveKeyMatchesVector`, `sealMatchesVector1/2/3`, `openRoundTrips`, `openRejectsAnySingleBitFlip`, `openRejectsShort`, normalization/format cases, `fromQrTextAcceptsOnlyFulllockUri` — `"https://x/?code=K7QD9MXP2A"` → null, `"fulllock://pair?code=k7qd-9mxp-2a"` → `"K7QD9MXP2A"`). `PingTest`: v2 ping with level 2, seq 1, ts 0, nonce 11..18 → bytes == vector V2 payload. `DiscoveryPacketTest`: v2 bytes → 5555; old 10-byte "F1RX" → null; version 1 → null.
- [ ] **Step 2:** `cd android; .\gradlew.bat testDebugUnitTest` → FAIL (compile).
- [ ] **Step 3: Implement.** `SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256")` with `PBEKeySpec(code.toCharArray(), salt, 200_000, 256)`; constant-time compare with `MessageDigest.isEqual`.
- [ ] **Step 4:** tests → PASS.
- [ ] **Step 5: Commit** `feat(android): pair code and packet authentication`.

### Task 8: Kotlin authenticated transport, pairing state, Keystore store

**Files:**
- Create: `network/PairingStore.kt`, `network/PairingStatus.kt`
- Modify: `network/UdpTransport.kt`, `MainActivity.kt` (discovery switch + server-IP persistence)
- Test: `android/app/src/test/java/com/f1controller/network/PairingStatusTest.kt`, new `PongVerifierTest.kt`

**Interfaces — Consumes:** Task 7. **Produces:**
```kotlin
class PairingStore(context: Context) {                       // AndroidKeyStore alias "fulllock_pair"
    val isPaired: Boolean
    fun key(): java.security.Key?
    fun save(rawKey: ByteArray)                              // KeyStore.SecretKeyEntry + KeyProtection(PURPOSE_SIGN or PURPOSE_VERIFY)
    fun forget()                                             // delete entry + remove "server_ip" pref
}
/** Pure: decides whether a datagram is a valid pong for our nonce and tracks the PC ctr. */
class PongVerifier(private val auth: PacketAuth, private val phoneNonce: ByteArray) {
    var pcSession: ByteArray? ; private set
    /** Returns the pong payload length if it's a valid pong (marker FFFFFFFF, seq outstanding), else null. */
    fun acceptPong(data: ByteArray, len: Int, isOutstanding: (seq: Int) -> Boolean): Int?
    /** For non-pong datagrams: valid under current session and ctr > last. */
    fun acceptData(data: ByteArray, len: Int): Int?
}
enum class PairingStatus { LOOKING, NEEDS_PAIRING, CODE_MISMATCH, CODE_CHANGED, PAIRED }
fun pairingStatus(hasKey: Boolean, pairingAttempt: Boolean, beaconAgeMs: Long?, validPongAgeMs: Long?, everConnected: Boolean): PairingStatus
// UdpTransport additions
@Volatile var auth: PacketAuth?                              // null → transport refuses to start (state.error = null, isActive false)
val lastValidPongNs: Long
```
`pairingStatus` rules: !hasKey → beacon seen (age ≤ 3000) ? NEEDS_PAIRING : LOOKING; validPongAge ≤ 2000 → PAIRED; pairingAttempt and beacon ≤ 3000 → CODE_MISMATCH; everConnected and beacon ≤ 3000 and (validPongAge == null or > 3000) → CODE_CHANGED; else LOOKING.
UdpTransport (spec §3.2–3.3): new random 8-byte `phoneNonce` per `connect()`; send counter starts at 1 per connect, shared by pings and control; ping sealed with ctx empty; control packets are sent only once `pcSession` is known (pings only before that), sealed with ctx `pcSession ‖ nonce`; socket `connect(addr, port)` after resolve; receive loop: marker FFFFFFFF → `acceptPong`, else `acceptData`, null → drop; `isConnected` driven only by valid pongs. A valid pong with a different pcSession replaces the session and resets PC ctr tracking.
MainActivity: beacon → `shouldSwitchServer` unchanged (candidate only, in memory); persist `server_ip` to prefs only while `transport.state.value.isConnected`. On start, `transport.auth = pairingStore.key()?.let(::PacketAuth)`.

- [ ] **Step 1: Failing tests.** `PongVerifierTest` (build pongs with a C#-equivalent helper using `PacketAuth` and `DIR_PC_TO_PHONE`): `acceptsValidPongAndLearnsPcSession`; `rejectsPongForOtherNonce`; `rejectsPongNotOutstanding`; `rejectsDataBeforePcSessionKnown`; `acceptsSealedGameStateAfterPong` (vector V3 shape, ctx = pcSession‖nonce); `rejectsReplayedData` (same ctr twice); `PongWithNewPcSessionResetsCounter` (session A data ctr 50, then pong with pcSession B ctr 1 → accepted, then B data ctr 2 accepted); `rejectsUnsealedV1Datagrams` (raw 16-byte game state, raw pong 16 bytes). `PairingStatusTest`: one test per rule above, plus `pongWinsOverBeacon`.
- [ ] **Step 2:** tests → FAIL (compile).
- [ ] **Step 3: Implement** `PongVerifier`, `pairingStatus`, `PairingStore`, transport + MainActivity changes.
- [ ] **Step 4:** tests → PASS; `.\gradlew.bat assembleDebug` → BUILD SUCCESSFUL.
- [ ] **Step 5: Commit** `feat(android): authenticated transport and pairing state`.

### Task 9: Kotlin pairing UI (sheet, ZXing scan, settings)

**Files:**
- Create: `android/app/src/main/java/com/f1controller/ui/PairingSheet.kt`, `android/app/src/main/assets/THIRD-PARTY-NOTICES.txt` (ZXing + zxing-android-embedded, Apache-2.0)
- Modify: `android/app/build.gradle.kts` (`implementation("com.journeyapps:zxing-android-embedded:4.3.0")`), `AndroidManifest.xml` (`<uses-feature android:name="android.hardware.camera" android:required="false" />`; the CAMERA permission comes from the library manifest), `ui/SettingsScreen.kt`, `ui/ControllerScreen.kt` (status text), `MainActivity.kt`, `README.md` third-party list if present
- Test: `android/app/src/test/java/com/f1controller/network/PairingCodeTest.kt` (add as-you-type case)

**Interfaces — Consumes:** `PairingCode`, `PacketAuth`, `PairingStore`, `pairingStatus`, `UdpTransport.auth/lastValidPongNs`. **Produces:** `suspend fun MainActivity.tryPair(normalizedCode: String): Boolean` — derive key on `Dispatchers.Default`; set `transport.auth = PacketAuth(keyFromRaw(raw))`, reconnect; wait ≤ 2000 ms for a valid pong; on success `pairingStore.save(raw)` and switch `auth` to the Keystore key; on failure restore previous `auth` and reconnect. `@Composable fun PairingSheet(status: PairingStatus, onSubmit: (String) -> Unit, onScan: () -> Unit, busy: Boolean, error: String?)`.

- [ ] **Step 1: Failing test.** `formatPartialInput`: `format` of partial normalized `"K7QD9"` → `"K7QD-9"` (if Task 7 `format` only accepted full codes, extend it).
- [ ] **Step 2:** tests → FAIL; then implement `format` partial support → PASS.
- [ ] **Step 3: Implement UI.** Sheet opens automatically when status = NEEDS_PAIRING (once per app start unless reopened). Field: keyboard caps + no autocorrect, shows `format(normalize-as-you-go)`; Submit enabled at 10 valid chars; spinner while `tryPair`. **Scan QR** → `rememberLauncherForActivityResult(ScanContract())` with `ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE).setBeepEnabled(false).setPrompt("Scan the code on the FullLock PC window")`; result → `PairingCode.fromQrText` → null shows "That QR isn't a FullLock pair code" else `tryPair`. Permission denial is handled by the library screen; the user returns to the sheet with the typed field. Status strings exactly as Global Constraints. Settings: "Pairing" row with **Re-pair** (opens sheet) and **Forget PC** (`pairingStore.forget()`, `transport.auth = null`, disconnect). Controller screen shows CODE_CHANGED status with a "Re-pair" button.
- [ ] **Step 4:** `.\gradlew.bat testDebugUnitTest assembleDebug` → all pass, BUILD SUCCESSFUL.
- [ ] **Step 5: Commit** `feat(android): pairing sheet with QR scan and settings`.

### Task 10: Docs, release build, manual phone test, re-audit

**Files:**
- Modify: `README.md` (Pairing section: QR or type the code once; `P` shows it again; `--new-code` re-pairs all phones; troubleshooting for "Code doesn't match" / "PC's pair code changed"; `--port` ≠ 5555 needs a firewall rule; known limitation: traffic is authenticated, not encrypted), `docs/DEVELOPMENT.md` (protocol v2 summary + link to `docs/protocol/pairing-test-vectors.md`), `installer/THIRD-PARTY-NOTICES.txt` (confirm QRCoder + ProtectedData entries)
- Ledger: `.superpowers/sdd/2026-11-04-fulllock-v1-release/progress.md`

- [ ] **Step 1:** Write the docs. Commit `docs: pairing guide and protocol v2`.
- [ ] **Step 2:** Build signed APK (`assembleRelease`, as in parent Task 7) and installer (`installer/build.ps1`). Push branch; CI (PR #1) green.
- [ ] **Step 3: Manual (user, with phone)** — uninstall old "F1 Controller"; install release APK and installer build. Check: QR pairing; Forget PC → typed pairing; wrong code → "Code doesn't match…"; `FullLock --new-code` while phone connected → "PC's pair code changed — re-pair" → re-pair; drive a lap in F1 2021 through the installed build (firewall rule) with dashboard + radio working; `P` toggles QR. Record results in the ledger. (This also closes the pending phone-mode test.)
- [ ] **Step 4:** Re-run the `security-audit` skill scoped to the diff since `eec93fe` and findings #1–#6; expect each closed or downgraded to documented limitation. Update `docs/security/2026-11-audit.md` with a "Resolution" section; commit `docs(security): audit resolution`.
- [ ] **Step 5:** Ledger line `Task 10: complete`; resume parent plan (remove TEMP trigger in `release.yml`, Task 11).
