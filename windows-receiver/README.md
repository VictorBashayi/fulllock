# F1 Phone Controller — Windows Receiver

C# .NET 8 console application that receives phone controller input over UDP and presents it as a virtual Xbox 360 controller via ViGEmBus.

## Prerequisites

1. **.NET 8 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
2. **ViGEmBus driver** — [Latest release](https://github.com/nefarius/ViGEmBus/releases)
   - Required for virtual Xbox controller. The app runs without it (`--no-vigem`) for transport testing.

## Build & Run

```bash
cd windows-receiver
dotnet restore
dotnet build
dotnet run --project F1Controller
```

## Usage

```
F1Controller.exe                              # Normal: receive + ViGEm
F1Controller.exe --port 5555                  # Custom UDP port
F1Controller.exe --validate                   # + F1 telemetry correlation (port 20777)
F1Controller.exe --log-transport              # Log transport metrics to CSV
F1Controller.exe --no-vigem                   # Transport only, no virtual controller
```

## Packet Format

18-byte binary, little-endian:

| Offset | Size | Field        | Range                |
|--------|------|--------------|----------------------|
| 0      | 4    | seq (u32)    | Packet sequence      |
| 4      | 8    | timestamp_us | Microseconds         |
| 12     | 2    | steer (i16)  | -32768 to 32767      |
| 14     | 1    | throttle     | 0-255                |
| 15     | 1    | brake        | 0-255                |
| 16     | 2    | buttons      | Bitfield (see below) |

### Button Bitfield

| Bit | Button       |
|-----|-------------|
| 0   | A           |
| 1   | B           |
| 2   | X           |
| 3   | Y           |
| 4   | LB          |
| 5   | RB          |
| 6   | Start       |
| 7   | Back        |
| 8   | Left Thumb  |
| 9   | Right Thumb |
| 10  | DPad Up     |
| 11  | DPad Down   |
| 12  | DPad Left   |
| 13  | DPad Right  |

### Ping/Pong (RTT measurement)

16 bytes: `[0xFFFFFFFF][seq:u32][timestamp_us:u64]` — receiver echoes back immediately.

## Validation Mode

With `--validate`, the receiver also listens on UDP 20777 for Codemasters F1 game telemetry. It logs sent-vs-received steering pairs and computes:

- **Pearson R²** — correlation between sent steering and game-observed steering
- **Mean Absolute Error** — average deviation
- Pass criteria: R² > 0.99, MAE < 0.05

Output is saved to `correlation_log_*.csv`.

## Architecture

```
Phone (UDP) ──→ UdpReceiver ──→ PacketParser ──→ XInputController ──→ ViGEmBus ──→ Game
                    │                                                      │
                    │                                    TelemetryReceiver ←┘ (port 20777)
                    │                                           │
                    └── TransportLog.csv            CorrelationLogger
                                                           │
                                                    correlation_log.csv
```
