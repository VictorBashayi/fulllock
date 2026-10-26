# Engineer radio (dashboard step 2) — design

Date: 2026-10-27. Status: approved in conversation, awaiting spec review.

## Goal

While driving F1 2021, the phone tells the driver what a race engineer would:
short calls about tyres, fuel, gaps, lap validity, flags, weather and damage.
Calls come from fixed rules (no AI). Step 3 (a conversational Gemini engineer)
will reuse the same delivery path, so nothing here should assume the text was
produced by rules.

Success: in a real race the calls are timely, correct, and not annoying at the
default chattiness; the driver hears/feels them without looking at the phone.

## Decisions (from the user)

- All four call groups: tyres & fuel, battle & gaps, lap & pace, flags/weather/damage.
- Delivery: banner + voice + vibration, each switchable.
- Chattiness is adjustable: Off / Quiet / Balanced (default) / Talkative.
- Rules run on the PC receiver (approach A); the phone only filters and delivers.
- Voice comes from the phone (Android text-to-speech).
- No "wait for a straight" delay: calls play as soon as the dispatcher sends them.
- F1 2021 only.

## Architecture

```
F1 2021 UDP ──► TelemetryReceiver ──► RaceSnapshot ──► EngineerRules ──► RadioDispatcher ──► radio packet ──► phone
                (parsing)              (record)          (pure)            (pacing, dedupe)                    UdpTransport ─► EngineerRadio ─► banner / TTS / vibration
```

### Receiver (C#, `windows-receiver/F1Controller/`)

1. **Parsing** (`Validation/TelemetryReceiver.cs`), in addition to what is read today:
   - Event packet (ID 3): `PENA` (penalty type, time), `FTLP` (fastest lap), `DRSE`/`DRSD`, `CHQF`.
   - Session packet (ID 1): current weather, weather forecast samples (time offset + weather),
     safety car status (0 none, 1 full, 2 virtual, 3 formation).
   - Car Status (ID 7): player's `vehicleFiaFlags` (-1 unknown, 0 none, 1 green, 2 blue, 3 yellow, 4 red).
   - Car Damage (ID 10): front-left/front-right/rear wing, floor, diffuser, sidepod, gearbox, engine damage (%).
   Field offsets are taken from the F1 2021 UDP specification and noted next to the code, as the
   existing parsing does.
2. **`RaceSnapshot`**: an immutable record with everything the rules read: session time, lap,
   total laps, sector, lap invalid flag, last/best lap times and sector statuses, gaps ahead/behind,
   DRS allowed, tyre surface temps and wear, fuel margin (laps), weather now and forecast,
   safety car status, FIA flag, damage values, plus the events received since the last snapshot.
   Produced whenever Lap Data is processed (~the game's send rate).
3. **`EngineerRules`** (pure): `Evaluate(previous, current, memory) → IReadOnlyList<RadioCall>`.
   `RadioCall` = text, priority (0–3), category, key (e.g. `tyre-hot-FL`). `memory` holds per-rule
   state (timers such as "hot for 5 s", last gap values per lap, thresholds already announced).
   No I/O, no clock other than snapshot session time, so it is fully unit-testable.
   All thresholds live in one `EngineerThresholds` class.
4. **`RadioDispatcher`**: takes calls from the rules and decides what to send.
   - Minimum spacing between sent calls: 4 s (critical calls ignore spacing).
   - Per-key cooldown (default 60 s; per-rule overrides) so the same call doesn't repeat.
   - If several calls are waiting, highest priority first; a waiting call older than 15 s is dropped
     (stale news), except critical.
   - Sends each call 3 times ~30 ms apart with the same call ID (UDP loss protection).
   - Uses session time for cooldowns so pausing the game doesn't burn them.

### Radio packet (receiver → phone)

Little-endian, sent to the phone endpoint like the game/race state packets:

```
[marker:u32 = 0xFFFFFFFC][callId:u16][priority:u8][category:u8][textLen:u8][text: UTF-8, textLen bytes, ≤ 120]
```

Categories: 0 tyres, 1 fuel, 2 gaps, 3 lap, 4 flags, 5 weather, 6 damage, 7 other.
Layout documented in `PacketParser.cs` (`WriteRadioCall`) and in `UdpTransport.kt`'s header comment.
The phone's receive buffer grows from 64 to 256 bytes.

### Phone (Kotlin, `android/app/src/main/java/com/f1controller/`)

1. **`UdpTransport`**: decodes the radio packet, drops duplicates by `callId` (remember the last ~16 IDs),
   exposes `radio: SharedFlow<RadioCall>`.
2. **`EngineerRadio`** (new): filters by chattiness, then speaks and vibrates.
   | Chattiness | Plays priorities |
   |---|---|
   | Off | none |
   | Quiet | 2–3 |
   | Balanced (default) | 1–3 |
   | Talkative | 0–3 |
   - Voice: Android `TextToSpeech`, English, speech rate ~1.15. A critical call interrupts current
     speech (`QUEUE_FLUSH`); others queue (`QUEUE_ADD`).
   - Vibration: info = one 40 ms tick, important = two ticks, critical = one 300 ms buzz; chatter none.
   - Exposes `current: StateFlow<RadioCall?>` for the banner.
3. **Banner** (`ui/`): in the centre column between BACK/START and the wheel graphic, covering the
   info strip while shown. 1–2 lines, "ENGINEER" label, left colour bar by priority
   (cyan info / amber important / red critical). Shown 4 s or until speech ends, whichever is longer,
   then fades. A new call replaces it. Not interactive.
4. **Settings → Engineer**: chattiness, voice on/off, vibration on/off, "Test call" button.
   Saved in the existing prefs (`engineer_level`, `engineer_voice`, `engineer_vibrate`).

## Rules (initial set)

Wording is indicative; final strings live with the rules. Thresholds in `EngineerThresholds`.

### Tyres & fuel
| Call | Fires when | Pri |
|---|---|---|
| "Front left is overheating, manage it." | a tyre's surface temp > 110 °C for 5 s | 2 |
| "Tyres are cold, push to get heat in." | average surface temp < 70 °C, after lap 1 | 2 |
| "Tyre wear at 50 percent." | worst tyre crosses 50 % | 1 |
| "Rears at 70 percent, box soon." | worst tyre crosses 70 % | 2 |
| "Fuel's tight, start lifting and coasting." | fuel margin < −0.3 laps | 2 |
| "We won't make it on fuel, lift and coast now." | fuel margin < −1.0 lap | 3 |

### Battle & gaps
| Call | Fires when | Pri |
|---|---|---|
| "Gap ahead 1.4, behind 2.1." | each new lap (omit a side that is unknown) | 1 |
| "Car behind is closing, 0.4 a lap." | gap behind shrank on each of the last 2 laps and is < 2 s | 1 |
| "DRS enabled." | `DRSE` event (start of race and after a safety car, not every lap) | 1 |

No "in DRS range" call: the DRS button on the phone already shows when DRS is available,
and a per-lap call would repeat whenever the driver follows a car closely.

### Lap & pace
| Call | Fires when | Pri |
|---|---|---|
| "Lap deleted, track limits." | current lap becomes invalid | 2 |
| "Personal best, 1:31.204." | a valid lap beats the personal best | 1 |
| "Purple sector." / "Green sector." | sector status becomes overall / personal best | 0 |
| "Last lap, bring it home." | final lap of a race starts | 1 |

### Flags, weather, damage
| Call | Fires when | Pri |
|---|---|---|
| "Safety car deployed." / "Virtual safety car." | safety car status → 1 / 2 | 3 |
| "Safety car is in, get ready." | safety car status 1 or 2 → 0 | 3 |
| "Red flag." | FIA flag → red | 3 |
| "Yellow flag, careful." | FIA flag → yellow | 2 |
| "Blue flags, let them by." | FIA flag → blue | 2 |
| "Rain expected in 5 minutes." | forecast shows rain within 10 min while it's dry now | 2 |
| "It's raining." | current weather becomes light rain or worse | 2 |
| "Front wing damage, 30 percent." | any wing/floor/diffuser/sidepod value rises ≥ 10 points | 3 |
| "Five second penalty." | `PENA` event for the player | 2 |

## Edge cases

- **New session / restart** (session UID change or session time going backwards): clear rule memory
  and dispatcher state. Rules stay silent for the first 3 s of a session so the first frame doesn't
  trigger a burst.
- **Paused / menus**: session time doesn't advance and Lap Data stops, so no snapshots, no calls.
- **TTS missing or failed to initialise**: banner and vibration still work; Settings shows
  "Voice unavailable".
- **Old app, new receiver**: unknown marker is already ignored by the phone.
- **Other games / formats**: 2021-gated like today; no calls.

## Testing

- **New xUnit project** `windows-receiver/F1Controller.Tests` (added to the solution):
  - each rule: fires on its condition, doesn't fire below threshold, respects its cooldown;
  - dispatcher: spacing, priority ordering, critical bypass, stale drop, dedupe;
  - `WriteRadioCall`: byte layout (marker, lengths, UTF-8, truncation at 120 bytes).
- **Phone**: the radio packet decoder is a small function with a JVM unit test using the same
  byte example as the C# test.
- **End to end**: a scratch script sends fake F1 2021 packets that trigger overheating, safety car,
  DRS enabled and a penalty; check banner, voice and vibration on the phone with the real receiver.
- **In game**: one race on Balanced, then tune thresholds.

## Out of scope

Step 3 (Gemini conversation, voice questions to the engineer), games other than F1 2021,
pit strategy/pit window calls, speaking through the PC.
