# DS4 pad mode — design

Date: 2026-10-31

## Goal

Drive F1 2021 with a DualShock 4 held **flat** (face up), steering by **banking** it left/right, with the same pitch-proof feel as the phone app. Nose up/down tilt must not change steering. Full lock must reach a true 100% at a user-chosen lean.

Replaces DS4Windows (wheel emulation is pitch-sensitive in the flat pose) and JoyShockMapper (`LEFT_STEER_X` output measured capped at ~50–60%).

## Decisions

- Runs as a mode of the existing receiver: `F1Controller.exe --ds4`.
- Pad only: in pad mode the receiver does not start UDP, discovery, telemetry or the engineer.
- HID access via the **HidSharp** NuGet package.
- Tuning with live keys in the receiver window; values persist.

## Architecture

```
DS4 (Bluetooth or USB)
  └─ Ds4Reader      HidSharp discovery, BT full-report mode (read feature report 0x05),
       │            read loop, reconnect
       ▼
  Ds4Report         raw bytes → buttons, triggers, sticks, gyro, accel, battery
       ▼
  PadSteering       MadgwickFilter → gravity → bank → deadzone → lock scale → curve → i16
       ▼
  ControlPacket     existing struct, XUSB button layout
       ▼
  XInputController  existing virtual Xbox 360 pad, unchanged
```

### Files

New, in `windows-receiver/F1Controller/Pad/`:

| File | Purpose |
|---|---|
| `Ds4Reader.cs` | Find DS4 (Sony VID 0x054C, PIDs 0x05C4 / 0x09CC, plus the USB wireless adapter 0x0BA0), open, enable full BT reports, read loop, raise a parsed report per input report, reconnect every 1 s when absent. |
| `Ds4Report.cs` | Pure parser: USB report 0x01 (data at offset 1) and BT report 0x11 (data at offset 3). Exposes buttons, L2/R2 (0–255), sticks, gyro (raw i16 ×3), accel (raw i16 ×3), battery. |
| `MadgwickFilter.cs` | Direct C# port of `android/.../sensor/MadgwickFilter.kt`. |
| `PadSteering.cs` | Steering maths (below). |
| `PadSettings.cs` | Lock angle, deadzone, curve; load/save `pad_settings.json` next to the exe; defaults on missing/corrupt file. |
| `PadDashboard.cs` | Console screen: connection state, live steer %, bank angle, settings, key help, battery. |

Changed: `Program.cs` (`--ds4` flag, pad-mode branch, usage text), `F1Controller.csproj` (HidSharp).

### Button mapping

| DS4 | Xbox |
|---|---|
| ✕ ○ □ △ | A B X Y |
| L1 / R1 | LB / RB |
| L3 / R3 | left / right stick click |
| Options / Share | Start / Back |
| D-pad | D-pad |
| L2 / R2 (analog) | brake (left trigger) / throttle (right trigger) |
| PS, touchpad | unmapped |

Sticks pass through: left stick Y and both right-stick axes go to the virtual pad as-is. Left stick X is **replaced** by steering (the virtual X axis carries only the lean steering, so a resting stick can't add drift). This needs a pad-mode submit path in `XInputController` that sets all four stick axes, since the phone path centres the Y axes.

## Steering

1. Gyro → rad/s (DS4: 16 LSB per °/s, after subtracting the startup bias). Accel → normalised direction. Feed both to the Madgwick filter at the report rate (timestamp from the report).
2. Gravity in pad frame. Flat: along Y. Bank moves it into X; nose tilt moves it into Z.
3. `bank = atan2(gX, sqrt(gY² + gZ²))` — invariant to nose tilt.
4. Deadzone (default 1°): inside → 0; outside, the deadzone width is subtracted.
5. Scale: `(|bank| − deadzone) / (lock − deadzone)`, clamp to 1 → full lock exactly at `lock` (default 18°).
6. Curve (default 0): `(1 − k)·x + k·x³`, as on the phone.
7. Output `round(x · 32767)` with sign. If left/right come out reversed on hardware, flip the sign of `gX`.

No centre calibration: bank is absolute from gravity, so flat is 0°.

Startup: "Keep the pad still…", average gyro for 1 s as the bias, then run.

## Live keys (receiver window focused)

| Keys | Setting | Step | Range |
|---|---|---|---|
| `[` `]` | lock angle | 1° | 5–60° |
| `-` `=` | deadzone | 0.5° | 0–10°, always < lock |
| `,` `.` | curve | 0.05 | 0–1 |

Every change saves `pad_settings.json`.

## Failure handling

- No pad at startup: "Waiting for DS4…", retry every 1 s.
- Pad disconnects, or no report for 100 ms: submit neutral immediately (centre steering, triggers 0, buttons up), show "Pad lost — reconnecting", resume when back.
- DS4Windows or JoyShockMapper process running: startup warning naming it.
- Settings file missing/corrupt: defaults, rewritten.

## Testing

Unit (xunit, `F1Controller.Tests`):
- `Ds4Report`: USB and BT byte layouts → correct buttons, d-pad, triggers, gyro, accel.
- `PadSteering` (fed a gravity vector directly): flat → 0; bank ±18° → ±32767; **bank 10° with nose tilt 0°, +30°, −30° → identical output**; deadzone and curve match the phone's formulas.
- `PadSettings`: corrupt JSON → defaults; clamps keep deadzone < lock.

Hardware: XInput sampling (lean to each side → ±100%; hold a bank and tilt nose → steady), then `joy.cpl`, then F1.

## Out of scope

Rumble, lightbar, touchpad, PS button, gyro aiming, running phone mode at the same time.
