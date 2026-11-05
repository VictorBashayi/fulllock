# FullLock pairing code + audit fixes — design

Date: 2026-11-05 · Status: draft for review · Parent: `2026-11-04-fulllock-v1-release-design.md` (Task 10)

## 1. Goal

The November audit (`docs/security/2026-11-audit.md`) found that any device on the LAN can:
drive the virtual pad (#1), redirect the PC's replies (#2), redirect the phone with a fake
beacon (#3), and feed the phone fake radio/dashboard data (#4). The user chose **option B**:
close all of these in v1.0 with a pairing code, plus the quick fixes #5, #6 and hardening.

**Success criteria**
- Only a phone that knows the pair code can move the virtual pad or receive telemetry/radio.
- The phone only connects to a PC that proves it knows the code; beacons are hints only.
- Setup cost for a stranger: one extra step, once (scan a QR or type 10 characters).
- No noticeable input latency; no new crypto libraries (platform crypto only).
- Pairing logic is UI-agnostic so the post-v1.0 GUI reuses it unchanged.

**Non-goals:** confidentiality (traffic stays readable; it is not secret), protection against
someone with access to the PC user account or an unlocked phone, compatibility with the old
"F1 Controller" app (different package; must be uninstalled anyway).

## 2. Pair code and key

- **Code:** 10 random characters from Crockford base32 (`0-9 A-Z` minus `I L O U`), ~50 bits,
  from `RandomNumberGenerator`. Displayed `XXXX-XXXX-XX`.
- **Normalization (both apps):** uppercase, drop spaces/dashes, map `I/L→1`, `O→0`; reject
  anything else or a length ≠ 10.
- **Key:** `key = PBKDF2-HMAC-SHA256(password = normalized code (ASCII), salt = "FullLock-pair-v1",
  iterations = 200_000, length = 32 bytes)`. Derived once at pairing / app start, never per packet.
- **PC storage:** `%APPDATA%\FullLock\pairing.json` = `{ "version": 1, "code": "<DPAPI CurrentUser,
  base64>" }`. The PC keeps the code (it must show it again). Created on first run.
  Unreadable/corrupt file → treated as absent → new code (phones must re-pair; logged on dashboard).
- **Phone storage:** only the 32-byte key, imported into the Android Keystore as an
  `HmacSHA256` key (`KeyProtection` with `PURPOSE_SIGN|PURPOSE_VERIFY`), alias `fulllock_pair`.
  Plus the last authenticated server IP in normal prefs. The code itself is not stored.

## 3. Wire protocol (v2)

### 3.1 Authenticated trailer

Every datagram on the control port (5555), both directions, becomes:

```
payload (unchanged v1 layout) ‖ ctr:u32 LE ‖ tag:8 bytes
tag = HMAC-SHA256(key, dir ‖ ctx ‖ payload ‖ ctr)[0..8]
dir = 0x01 phone→PC, 0x02 PC→phone
ctx = pcSession(8) ‖ phoneNonce(8) for session packets; empty for ping (3.2)
```

Tags are compared in constant time (`CryptographicOperations.FixedTimeEquals` /
`MessageDigest.isEqual`). Any datagram that is too short or fails the tag is **dropped silently**
(no reply, no state change, counted in a `RejectedPackets` stat on the dashboard).

### 3.2 Handshake (reuses ping/pong)

- **Ping** (phone→PC, ctx empty): `[0xFFFFFFFF][seq:u32][ts_us:u64][level:u8][phoneNonce:8]` + trailer.
  `phoneNonce` is random, chosen when the phone (re)connects and kept for the connection.
- **Pong** (PC→phone, session ctx): `[0xFFFFFFFF][seq:u32][ts_us:u64][pcSession:8]` + trailer,
  where ctx = pcSession ‖ the ping's phoneNonce. `pcSession` is random per FullLock run.
  The PC replies to the ping's source endpoint; a pong reveals nothing secret.
- The phone accepts a pong only if the tag verifies with its own current `phoneNonce` and
  `seq` matches an outstanding ping. On first valid pong it learns `pcSession` → **connected**.

### 3.3 Session rules

- All other packets (control, game state, race state, radio) use the session ctx.
- `ctr` must strictly increase per direction within a session; anything ≤ the last accepted
  `ctr` is dropped (reordered UDP packets are stale input anyway). The v1 "|delta| ≥ 1000 resets"
  rule is removed. The phone's send counter starts at 1 for each new phoneNonce; the PC uses one
  send counter per run (starting at 1, never reset), shared by pongs and data, so it stays
  strictly increasing for every phone. The PC tracks the last accepted phone `ctr` per active
  phoneNonce; a takeover by a new nonce starts that tracking fresh.
- **PC active phone:** the reply target (`_phoneEndPoint`) and the active `phoneNonce` change
  **only on a valid session control packet** — not on pings, not on invalid data (fixes #2).
  A new phone (or a reconnecting phone with a new nonce) takes over once it sends one valid
  control packet. One active phone at a time; any number of phones may know the code.
- **Phone:** drops everything from any source that does not verify with the current session
  (fixes #4). The socket is also `connect()`ed to the authenticated server address.

### 3.4 Discovery

The beacon stays unauthenticated (broadcast; nothing to prove yet) but becomes a **hint**:
layout `[0xFFFFFFFB]["FLRX"][controlPort:u16][version:u8 = 2]` (11 bytes). The phone
treats a beacon as a candidate address, pings it, and switches/persists the server only after a
valid pong from it (fixes #3). A forged beacon can at most make the phone send a ping somewhere.

## 4. User flow

### PC console
- No phone has sent a valid control packet this run → dashboard shows a QR panel (QR encodes
  `fulllock://pair?code=XXXXXXXXXX`, normalized code) with `Pair code: XXXX-XXXX-XX` under it and
  "Scan or type this in the FullLock app".
- After the first valid control packet → normal dashboard with status `Paired ✓ (P = show code)`.
  `P` toggles the QR panel (for pairing another phone).
- `FullLock --new-code` → generates and saves a new code, prints it, exits. All phones must re-pair.
- Pad mode (DS4 → virtual pad) never shows pairing UI.
- QR rendering: QRCoder (MIT) produces the module matrix; drawn with half-block characters
  (`▀ ▄ █`), black-on-white with a 2-module quiet zone. If the window is too small for the QR,
  only the text code is shown.

### Phone
- No stored key and a beacon arrives → a **"PC found — pair"** sheet opens automatically, with
  **Scan QR** and a code field (normalizes as typed, auto-formats `XXXX-XXXX-XX`).
- Scan QR → embedded ZXing scanner (`zxing-android-embedded`, Apache-2.0). Camera permission is
  requested only on tap; if denied, the sheet stays on the typed field. Accepts only
  `fulllock://pair?code=…` QR contents.
- Submit → derive key (~0.5 s, spinner) → ping → valid pong within 2 s → store key → "Paired ✓".
  No valid pong while beacons from that PC keep arriving → **"Code doesn't match this PC — check
  the code on the PC screen"**; nothing stored.
- Paired phone whose pings stop getting valid pongs while the PC's beacon is present for > 3 s →
  status **"PC's pair code changed — re-pair"** with a button to reopen the sheet.
- Settings → "Pairing": **Re-pair**, **Forget PC** (deletes Keystore key + server IP).
- Manual IP entry stays, but still requires a valid pong.
- User-visible states only: Looking for PC… · PC found — pair · Code doesn't match ·
  Pair code changed — re-pair · Paired ✓.

## 5. Components

### Windows (`windows-receiver/F1Controller`)
| Unit | Purpose |
|---|---|
| `Security/PairingCode.cs` | Generate / normalize / format codes. Pure. |
| `Security/PacketAuth.cs` | `DeriveKey(code)`, `Sign(...)`, `Verify(...)` (trailer build/check). Pure. |
| `Security/PairingStore.cs` | Load/save `pairing.json` via DPAPI; `Code`, `NewCode()`. UI-agnostic. |
| `Security/Session.cs` | pcSession, active phoneNonce + endpoint, per-direction counters; `TryAccept(datagram, from)`, `Seal(payload)`. Raises `Paired` on first valid control packet. |
| `AppPaths.PairingFile` | New path property. |
| `Transport/UdpReceiver.cs` | Verify via `Session` before parsing; v2 ping/pong; reply target from `Session`. |
| `Transport/RadioSender.cs`, state senders | Seal payloads via `Session`. |
| `Transport/DiscoveryBeacon.cs` / `PacketParser.cs` | New beacon layout (11 bytes, "FLRX", v2). |
| `UI/QrPanel.cs` | QRCoder matrix → half-block lines. |
| `UI/ConsoleDashboard.cs` | QR panel, `P` key, paired/rejected status. |
| `Program.cs` | `--new-code`. |

### Android (`android/app/.../com/f1controller`)
| Unit | Purpose |
|---|---|
| `network/PairingCode.kt` | Mirror of C# normalize/format + QR URI parse. Pure. |
| `network/PacketAuth.kt` | Mirror of C# sign/verify (`javax.crypto.Mac`), key derivation (`PBKDF2WithHmacSHA256`). |
| `network/PairingStore.kt` | Keystore import/delete of the HMAC key; `isPaired`; server IP persistence. |
| `network/UdpTransport.kt` | phoneNonce, v2 ping, pong verification, seal all sends, drop unverified, `connect()` socket. |
| `network/DiscoveryListener.kt` | Beacon → candidate only; switch/persist after authenticated pong. |
| `ui/PairingSheet.kt` | Scan + typed entry, states above. |
| `ui/SettingsScreen.kt` | Pairing row (Re-pair / Forget PC). |

### Dependencies / notices
QRCoder (MIT, NuGet) and zxing-android-embedded + ZXing core (Apache-2.0) added to
`installer/THIRD-PARTY-NOTICES.txt` and an Android notices entry. Android manifest gains
`CAMERA` (runtime, optional: `uses-feature android:required="false"`).

## 6. Bundled audit fixes and hardening

- **#5:** `RaceTracker` lap-distance bin: reject non-finite values and `bin < 0 || bin >= length`;
  `TelemetryReceiver.RunAsync` wraps per-datagram handling in try/catch (log + continue).
- **#6:** installer pins install dir under `{autopf}` (`DisableDirPage=yes`), so the elevated
  uninstaller never runs from a user-writable folder.
- **Firewall rule:** UDP inbound limited to local ports 5555 and 20777 and `remoteip=localsubnet`
  (still program-scoped, Private profile).
- `.gitignore`: `*.pem *.key *.p12 *.pfx .env`.
- Drop `<AllowUnsafeBlocks>` from `F1Controller.csproj` if the build passes without it
  (otherwise keep and note why).
- Redact the DS4 MAC address from the plan doc.
- README: "Pairing" section + troubleshooting ("Code doesn't match", `--new-code`), and the
  known-limitation that traffic is authenticated, not encrypted.

## 7. Testing

- **Shared test vectors** (`docs/protocol/pairing-test-vectors.md`, consumed by both suites):
  fixed code → expected normalized code → expected key (hex) → fixed ctx/payload/ctr/dir →
  expected tag. Proves C# and Kotlin agree without a phone.
- **C# unit:** code generation alphabet/length, normalization (dashes, case, I/L/O), tag
  rejection on any single flipped bit, short packets, replayed/old `ctr` dropped, reply target
  unchanged by invalid or ping-only traffic, takeover only via a valid control packet,
  `PairingStore` round-trip + corrupt file → new code, `--new-code`.
  #5: NaN/±∞/negative/huge lapDistance does not throw; loop survives a throwing handler.
- **C# integration:** loopback UDP fake phone: ping → pong → control moves the pad state;
  forged and replayed packets do not.
- **Kotlin unit:** same vectors, normalization, QR URI parse, pong acceptance rules, ctr rules.
- **Manual (needs phone, before tagging):** pair via QR, pair via typing, wrong code message,
  `--new-code` → "Pair code changed", drive a lap through the installed build + firewall rule
  (also closes the pending phone-mode test), uninstall old "F1 Controller" first.
- After implementation: re-run the security-audit skill on the diff to confirm #1–#6 closed.

## 8. Later (not v1.0)

GUI app (separate project right after v1.0) built on `PairingStore` / `Session` unchanged.
