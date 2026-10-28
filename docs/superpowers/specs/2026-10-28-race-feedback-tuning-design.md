# Race feedback tuning: lap report, tyre heat, pit stops, horizon steering — design

Date: 2026-10-28. Status: approved in conversation, awaiting spec review.

## Goal

Fix what the 2026-10-28 race (Montreal, engineer_log_20261028_193207.txt) showed:

1. Sector calls came almost every lap (27 of 51 calls).
2. Front tyres were called "overheating" nearly every lap: the surface temperature spikes under
   every heavy braking zone, and a second tyre crossing the limit later got its own call.
3. No gap calls in the race: gaps work (the Timing view showed them), but the only gap calls are
   catching/closing trends, which never qualified.
4. Steering sensitivity changes with how far the phone is tilted up or down, and the driver's
   hands keep moving during a race.

And make the engineer understand pit stops, so in/out laps and new tyres don't confuse it.

Success: in a race at the default chattiness, the driver gets a lap report at the chosen rhythm,
hears overheating only when a tyre really overheats, hears nothing misleading around pit stops,
and steering feels the same at any screen tilt.

## Decisions (from the user)

- Sector calls are replaced by a lap report. Its rhythm follows the chattiness level:
  Quiet every 3 laps, Balanced every 2, Talkative every lap, Off never.
- Sectors are compared with the driver's best lap's sectors.
- The report includes the gaps to the cars ahead and behind.
- Pit stops must not distort the data (in/out laps, new tyres, gaps).
- Steering: new pitch-independent mode as the default (agreed to the proposal).

## A. Lap report

**When.** At each lap rollover (the lap number goes up), for the lap just completed, number `L`.
A report is due when `L % period == 0`, with period 3 / 2 / 1 for Quiet / Balanced / Talkative
and no reports when Off. Laps before lap 1 is completed produce nothing.

**Knowing the level.** Chattiness lives on the phone. The phone appends the level as a 17th byte
to its ping (`[0xFFFFFFFF][seq:u32][timestamp_us:u64][level:u8]`). The receiver still echoes only
the first 16 bytes, so RTT measurement is unchanged, and an old phone app (16-byte pings) is still
understood: the receiver then assumes Balanced. `UdpReceiver` exposes the latest level;
`EngineerService` reads it through a `Func<int>` passed in by `Program`.

**Content**, built by the rules from the snapshot just before the rollover (S1, S2, validity) and
the one at the rollover (last lap time):

- `S3 = lastLapMs − S1 − S2` of the completed lap.
- Lap time to tenths: "Lap 12, 1:20.9."
- Sectors vs the best lap's sectors, each as "sector N up 0.2" (faster), "down 0.1" (slower) or
  "matched" (|difference| < 0.05 s), e.g. "Sector 1 up 0.2, sector 2 down 0.1, sector 3 matched."
  Left out while there is no best lap yet.
- Gaps, race sessions only: "Gap ahead 1.2, behind 0.8." Each side is left out when there's no
  car there (leading / last) or no gap is known.
- Deleted lap: "Lap 12 deleted." plus the gaps, no time or sectors.
- In-lap / out-lap (see D): "In lap." / "Out lap." plus the gaps.
- If the lap was a new personal best, the report starts with "Personal best." and is compared
  with the previous best; the separate `pb` call is dropped for that lap (a `pb` call still comes
  on laps without a report).

**Best lap's sectors** are kept by the rules: the S1/S2/S3 of the fastest valid lap that is not an
in- or out-lap. Reset with the rest of the rule memory on a new session.

**Priority / key.** Key `lap-report`, priority Important (the level was already applied by
choosing which laps get one), category Lap, cooldown 0 (it's due at most once per lap anyway).

**Removed:** the "Purple sector" / "Green sector" calls (`sector-1`, `sector-2`).
**Kept:** `pb` (as above), `gap-ahead` / `gap-behind` trend calls, `last-lap`, `lap-invalid`.

## B. Tyre overheating

- The receiver also parses the tyres' **inner** temperature from Car Telemetry
  (`tyresInnerTemperature[4]`, the 4 bytes right after the surface temperatures, offset 34 in
  each car's entry) into `RaceState`. The phone
  packet keeps sending surface temperatures; nothing changes on the phone's dashboard.
- A tyre is overheating when its inner temperature is over **105 °C** (`TyreHotC`) for
  **5 s** (`TyreHotSeconds`, unchanged). The merge into one call (fronts / rears / all / multi) stays.
- After a tyre is called it stays muted for **3 laps** (`TyreHotRepeatLaps`), unless its inner
  temperature reaches the temperature it was called at **+5 °C** (`TyreHotWorseC`).
- **Per-lap tyre line in the engineer log**: at each lap rollover the log gets the lap's peak
  surface and inner temperature per tyre, e.g.
  `20:08:13.023  lap 14 tyres  surface 121/118/104/102  inner 101/99/95/94`, so 105 °C can be
  tuned from real data. It's log-only, never sent to the phone. `EngineerService` takes an
  optional `Action<string>` for such lines; `Program` points it at the existing log writer.
- `tyre-cold` keeps using the surface average (unchanged otherwise), but see D for the out-lap.

## C. Horizon steering

**Cause.** `TILT` measures rotation about the screen normal (`atan2(gy, gx)` of gravity in the
device frame). Hands turn the phone like an upright wheel, about the axis pointing at the driver,
while the screen is tilted back by φ. For that motion the measured angle is
`atan(tan α / cos φ)`: sensitivity grows by `1/cos φ` (×1.15 at 30°, ×1.4 at 45°, ×2 at 60°), so
tilting the screen further back makes steering twitchier and tilting it upright makes it slower.

**Fix.** New axis `HORIZON`: the angle of the phone's long edge against the horizon,
`atan2(gy, sqrt(gx² + gz²))` of the gravity estimate. For the wheel motion above it equals the
steering angle at any φ. Sign matches `TILT` (both are `−α` for the same turn), range ±90°.
Centre calibration, deadzone, lock and curve work as before.

- `HORIZON` becomes the default `SteeringAxis`; `TILT`, `ROLL`, `YAW` stay selectable in Settings.
- One-time migration: a saved axis of `TILT` (the old default) becomes `HORIZON`, recorded with a
  pref flag so a later manual choice of `TILT` sticks.
- The angle maths moves into a pure function so it's unit-tested with synthetic gravity vectors:
  the same wheel turn at φ = 0°, 30°, 45°, 60° gives the same angle (within 0.5°), and `TILT`
  shows the `1/cos φ` growth (documents the bug).

## D. Pit stops

**New data** from Lap Data (F1 2021, per car): `pitStatus` (byte 26: 0 none, 1 pitting, 2 in the
pit area) and `numPitStops` (byte 27). `RaceInfo` gains `PitStatus`, `NumPitStops`, and
`CarAheadInPit` / `CarBehindInPit` (the pit status of the cars one place up / down is not 0).

**In-lap / out-lap.** The rules note whether any snapshot during a lap had `PitStatus != 0`. A
completed lap with pit activity is an **out-lap** if it started with `PitStatus != 0`, otherwise an
**in-lap**. Such laps never become the best lap and get the short report from A.

**Gap trends.** The catching/closing history is cleared while the player is in the pit lane, and
the ahead / behind history while that car is in the pit lane (their gap jumps by a pit stop's
length without anyone catching anyone). The existing reset when a different car is ahead/behind
covers the cars that pass during the stop.

**Tyres.** When `NumPitStops` goes up, the hot-tyre memory (spells, mutes) and the per-lap peaks
reset. `tyre-cold` is not called on an out-lap. Wear warnings already re-arm when wear drops.

**In the pit lane** (`PitStatus != 0`): only Critical calls are queued (safety car, red flag,
damage). Non-critical calls produced there are dropped, not delayed.

## Testing

- Receiver (xUnit, `Snapshots.Make` gains pit status, pit stops, car-in-pit and inner temps):
  lap report rhythm per level, content (sectors up/down/matched, no best yet, deleted lap, PB lap,
  gaps left out, practice without gaps), in/out-lap detection and exclusion from best,
  inner-temp overheating with 3-lap mute and the +5 °C exception, reset on pit stop, no cold call
  on out-lap, pit-lane suppression, gap history cleared around stops, ping level parsing
  (16 and 17 bytes), per-lap tyre log line.
- Phone (JUnit): horizon angle invariance and the TILT `1/cos φ` growth; ping carries the level.
- Real check: a race with at least one pit stop; read the engineer log (reports at the right laps,
  tyre lines, no calls in the pit lane) and feel the steering at different screen tilts.

## Out of scope

- Tuning `tyre-cold` to inner temperatures.
- Engineer messages about the pit stop itself ("box this lap", pit window).
- UI bugs listed in the controller UI to-do (pointer over the banner, stray Timing numbers).
