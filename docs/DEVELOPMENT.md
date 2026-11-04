# FullLock — developer guide

How FullLock is built: architecture, wire format, design phases and build instructions. End-user docs are in the [README](../README.md).

## Architecture

```
┌─────────────────────────────────────┐      UDP (18B packets)      ┌──────────────────────────────────────┐
│            ANDROID PHONE            │ ──────────────────────────► │           WINDOWS PC                 │
│                                     │      Wi-Fi / USB-ADB       │                                      │
│  ┌───────────┐    ┌──────────────┐  │                            │  ┌─────────────┐   ┌──────────────┐  │
│  │ IMU (Gyro │───►│  Madgwick    │  │                            │  │ UDP         │──►│ ViGEmBus     │  │
│  │ + Accel)  │    │  AHRS Filter │  │                            │  │ Receiver    │   │ Xbox 360 Pad │  │
│  │ ≥200 Hz   │    │  β tunable   │  │                            │  │ SO_TIMESTAMP│   │ 250 Hz report│  │
│  └───────────┘    └──────┬───────┘  │                            │  └─────────────┘   └──────┬───────┘  │
│                          │          │                            │                           │          │
│  ┌───────────┐    ┌──────▼───────┐  │                            │                    ┌──────▼───────┐  │
│  │ Touch     │───►│ Packet       │  │                            │                    │ XInput API   │  │
│  │ Controls  │    │ Builder      │──╋────────────────────────────╋───────────────────►│ (Game reads) │  │
│  │ (pedals,  │    │ 18 bytes     │  │                            │                    │              │  │
│  │  buttons) │    └──────────────┘  │                            │                    └──────────────┘  │
│  └───────────┘                      │                            │                                      │
│                                     │                            │  ┌─────────────┐                     │
│  ┌───────────┐                      │     Telemetry (port 20777) │  │ F1 Telemetry│                     │
│  │ Latency   │◄─────────────────────╋────────────────────────────╋──│ Receiver    │                     │
│  │ Display   │     ping/pong RTT    │                            │  │ (validation)│                     │
│  └───────────┘                      │                            │  └─────────────┘                     │
└─────────────────────────────────────┘                            └──────────────────────────────────────┘
```

## Latency Budget

Target: **≤ 12 ms** end-to-end on Wi-Fi, **≤ 8 ms** on USB.

| Stage                        | Budget (Wi-Fi) | Budget (USB) | Notes                                      |
|------------------------------|----------------|--------------|---------------------------------------------|
| IMU sample → filter output   | 2 ms           | 2 ms         | Sensor @ 200 Hz = 5 ms period; filter < 1 ms |
| Packet build + send          | 0.5 ms         | 0.5 ms       | 18 bytes, pre-allocated buffer               |
| Network transit              | 3–5 ms         | 0.2 ms       | Wi-Fi w/ LOW_LATENCY lock; USB via ADB fwd   |
| Receive + deserialize        | 0.5 ms         | 0.5 ms       | Non-blocking recv, tight loop                 |
| ViGEm report submission      | 1–2 ms         | 1–2 ms       | Kernel IRP round-trip                         |
| XInput poll by game          | 0–4 ms         | 0–4 ms       | Game-dependent; 60 Hz = 16.6 ms worst case    |
| **Total (p50)**              | **~8 ms**      | **~4 ms**    |                                               |
| **Total (p99)**              | **≤ 12 ms**    | **≤ 8 ms**   |                                               |

## Binary Packet Format

All fields little-endian. Fixed 18 bytes, no framing overhead.

```
Offset  Size  Type    Field         Range / Notes
──────  ────  ──────  ────────────  ─────────────────────────────────
0       4     u32     seq           Monotonic sequence number
4       8     u64     timestamp_us  Phone monotonic clock (System.nanoTime / 1000)
12      2     i16     steer         -32768 … +32767 (left … right)
14      1     u8      throttle      0 … 255
15      1     u8      brake         0 … 255
16      2     u16     buttons       Bitfield (see below)
──────  ────
Total: 18 bytes
```

### Button Bitfield (u16)

Uses the XInput `XUSB_GAMEPAD.wButtons` layout directly, so neither side translates.

| Mask   | Xbox Button        | Phone button |
|--------|--------------------|--------------|
| 0x0001 | D-pad Up           | —            |
| 0x0002 | D-pad Down         | —            |
| 0x0004 | D-pad Left         | —            |
| 0x0008 | D-pad Right        | —            |
| 0x0010 | Start              | MENU         |
| 0x0020 | Back               | MFD          |
| 0x0040 | Left Stick Click   | —            |
| 0x0080 | Right Stick Click  | —            |
| 0x0100 | Left Bumper        | GEAR ↓       |
| 0x0200 | Right Bumper       | GEAR ↑       |
| 0x1000 | A                  | A            |
| 0x2000 | B                  | B            |
| 0x4000 | X                  | DRS          |
| 0x8000 | Y                  | ERS          |

### Ping / Pong (16 bytes)

`[0xFFFFFFFF:u32][seq:u32][timestamp_us:u64]` — phone sends 10 Hz, receiver echoes it
back unchanged; the phone computes RTT from the echoed timestamp.

## Phases

### Phase 0 — IMU Characterization (`phase0-imu-characterization/`)

Android app that logs raw gyroscope + accelerometer data at the sensor's maximum
rate to CSV. Records `SensorEvent.timestamp` (nanoseconds, monotonic) per sample.

**Acceptance criteria:**
- CSV output with columns: `timestamp_ns, gyro_x, gyro_y, gyro_z, accel_x, accel_y, accel_z`
- Achieves the sensor's actual maximum rate (verify via timestamp deltas)
- 60s stationary recording + 60s controlled rotation recording
- Data usable for Allan variance / noise density analysis

### Phase 1 — Transport (`phase1-transport/`)

UDP sender (Android) and receiver (C# .NET 8) using the 18-byte binary packet format.
Includes ping/pong RTT measurement and rolling latency display.

**Acceptance criteria:**
- Packet loss < 0.1% on local Wi-Fi
- Round-trip latency p99 ≤ 6 ms (network only, no processing)
- `WifiLock` with `WIFI_MODE_FULL_LOW_LATENCY` active during transmission
- Receiver logs timestamps for offline jitter analysis
- No TCP anywhere in the control path

### Phase 2 — Sensor Fusion (`phase2-sensor-fusion/`)

Madgwick AHRS filter extracting phone orientation from gyro + accelerometer.
Maps orientation to the signed 16-bit steering value in the packet.

**Acceptance criteria:**
- Filter beta parameter configurable at runtime (slider)
- Steering axis selectable (yaw or roll)
- Lock-to-lock angle configurable (default ±90°)
- "Set center" calibration button captures current orientation as zero
- Steering jitter at rest < 0.5° equivalent (< ~182 raw counts)
- Processing latency < 1 ms per sample

### Phase 3 — ViGEmBus Virtual Controller (`phase3-vigem-controller/`)

Windows receiver creates and submits XInput reports via `Nefarius.ViGEm.Client`
at 250 Hz.

**Acceptance criteria:**
- Virtual Xbox 360 controller visible in `joy.cpl`
- Report submission rate sustained at 250 Hz
- Steering → `sThumbLX`, throttle → `bRightTrigger`, brake → `bLeftTrigger`
- All button bits correctly mapped to `XUSB_BUTTON` flags
- F1 24 detects and accepts input from the virtual controller

### Phase 4 — Closed-Loop Validation (`phase4-validation/`)

Telemetry receiver reads Codemasters F1 UDP telemetry (port 20777) and correlates
`m_steer` with the phone's sent value.

**Acceptance criteria:**
- p99 end-to-end latency ≤ 12 ms on Wi-Fi
- Steering jitter at rest < 0.5°
- Sent vs. received steering angle R² > 0.99 during controlled sweep
- Produces latency histogram, correlation plot, jitter plot

## Prerequisites

| Tool                | Version   | Purpose                                    |
|--------------------|-----------|--------------------------------------------|
| VS Code            | any       | Editor (see `.vscode/` for tasks + extensions) |
| Android SDK        | platform 35 | Android build (Gradle auto-installs missing parts) |
| JDK                | 17 or 21  | Gradle 8.9 can't run on JDK 23+ (Android Studio's JBR 25 won't work) |
| .NET SDK           | 8.0+      | Windows receiver                           |
| ViGEmBus Driver    | 1.22+     | Virtual Xbox controller kernel driver      |
| ADB                | Latest    | Install the app over USB                   |

Point Gradle at the JDK in `~/.gradle/gradle.properties` (machine-specific, not in the repo):

```properties
org.gradle.java.home=C:/path/to/jdk-21
```

### ViGEmBus Installation

1. Download from https://github.com/nefarius/ViGEmBus/releases
2. Run the installer (requires admin / reboot)
3. Verify: Device Manager → System devices → "Virtual Gamepad Emulation Bus"

## Building

From VS Code: **Terminal → Run Task…** and pick one of the tasks in `.vscode/tasks.json`
(Android build/install/launch/logcat, receiver build/run). Or from a terminal:

```bash
# Android app (phone connected with USB debugging on)
cd android
./gradlew.bat assembleDebug        # build APK → app/build/outputs/apk/debug/
./gradlew.bat installDebug         # build + install to phone

# Windows receiver (all phases 1, 3, 4)
cd windows-receiver
dotnet build
dotnet run --project F1Controller -- --port 5555   # add --ds4 for DualShock 4 mode, --help for all flags
```

## Releases

- `installer/build.ps1` builds `installer/out/FullLock-Setup-<version>.exe` and the portable zip (needs Inno Setup 6; drivers are downloaded and SHA-256-checked from `installer/drivers.json`).
- Android release signing reads `FULLLOCK_KEYSTORE`, `FULLLOCK_KEYSTORE_PASSWORD`, `FULLLOCK_KEY_ALIAS`, `FULLLOCK_KEY_PASSWORD` from the environment or `android/keystore.properties` (see `keystore.properties.example`). Without them `assembleRelease` produces an unsigned APK.
- The version lives in `VERSION` only. Pushing tag `v<VERSION>` runs `.github/workflows/release.yml`, which creates a **draft** GitHub release with the installer, zip, APK and `SHA256SUMS.txt`.
