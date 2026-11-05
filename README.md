# FullLock

**Steer F1 games on PC by tilting your phone or a DualShock 4.** FullLock turns your phone's gyroscope (or a PS4 pad's) into a smooth analog steering wheel, and shows a live race-engineer dashboard on the phone.

> FullLock is an independent hobby project. Not affiliated with Formula One, Formula One Licensing B.V., Codemasters or EA.

## What it does

- **Phone mode:** hold your Android phone like a wheel and tilt to steer, with on-screen throttle, brake, gears, DRS and ERS. The PC sees an ordinary Xbox 360 controller.
- **DualShock 4 mode:** hold a PS4 pad flat and bank it left or right to steer, with the triggers as pedals and rumble passed through from the game.
- **Race engineer:** in phone mode, the phone shows position, gaps, tyres, fuel and ERS from the game's telemetry, and calls out important events by voice and vibration.

## Requirements

- Windows 10 or 11 (64-bit).
- One of:
  - an Android 8.0+ phone on the **same Wi-Fi** as the PC, or
  - a DualShock 4 connected **by USB**.
- An F1 game that accepts an Xbox controller. The engineer dashboard also needs the game's UDP telemetry (tested with F1 2021).

## Install

### Windows

1. Download `FullLock-Setup-<version>.exe` from the [latest release](https://github.com/VictorBashayi/fulllock/releases/latest).
2. Run it. FullLock isn't code-signed yet, so Windows SmartScreen may warn you. Click **More info → Run anyway**.
3. Setup installs the **ViGEmBus** driver, which provides the virtual Xbox controller. If you'll use a DualShock 4, also tick **Install HidHide**.
4. Restart if setup asks you to.

You can check the download against `SHA256SUMS.txt` on the release page. A portable zip is there too; it needs [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases/latest) installed separately.

### Android (phone mode only)

1. On your phone, download `FullLock-<version>.apk` from the [latest release](https://github.com/VictorBashayi/fulllock/releases/latest).
2. Open it, and allow your browser to install apps when Android asks.

## Quick start

### Phone

1. On the PC, open **FullLock (Phone)** from the Start menu.
2. Open the FullLock app on the phone and tap **Connect**. It finds the PC on your network automatically. You can also type the PC's IP under Settings.
3. Hold the phone level like a wheel and tap **Set Center (Calibrate)**.
4. Start the game and drive.

### DualShock 4

1. Close DS4Windows, Steam Input and any other pad mappers.
2. Plug the pad in by USB and open **FullLock (DS4 pad)** from the Start menu.
3. Hold the pad **flat and still for 1 second** while it calibrates. Then bank it left or right to steer.
4. While FullLock is running, these keys tune the steering, and your settings are saved:

   | Keys | Setting |
   |---|---|
   | `[` `]` | steering lock: how far you bank for full lock |
   | `-` `=` | centre deadzone |
   | `,` `.` | response curve |

## Recommended in-game settings

In the game's controller calibration, set **steering deadzone to 0**, **steering linearity to 0**, and **steering saturation** to taste. FullLock already applies its own deadzone and curve.

For the engineer dashboard, turn on **UDP Telemetry** in the game's telemetry settings. Leave the port at **20777** and set the UDP IP to the PC that runs FullLock: `127.0.0.1` if the game and FullLock are on the same PC.

## Troubleshooting

**The game sees two controllers, or steering fights itself (DualShock 4).**
The game is reading the real pad as well as FullLock's virtual one. Install HidHide by re-running setup and ticking **Install HidHide**, or from [HidHide releases](https://github.com/nefarius/HidHide/releases/latest). FullLock then hides the real pad from games while it runs, and un-hides it when you close FullLock.

**The phone can't find the PC.**
- Make sure both are on the same Wi-Fi network.
- Set that network to **Private** in Windows (Settings → Network & internet → your network). Setup only allows FullLock through the firewall on Private networks, so it never listens on public Wi-Fi.
- Some routers block devices from talking to each other ("AP isolation" or "guest network"). If yours does, use the main network.

**"ViGEmBus is not installed".**
Run setup again, or install [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases/latest), then restart the PC.

**The steering jumps around or doesn't respond (DualShock 4).**
Another pad tool, such as DS4Windows, Steam Input or JoyShockMapper, is also using the pad. Close it and restart FullLock.

## Privacy

FullLock doesn't collect or send any data. The phone and the PC talk directly over your local network, and nothing goes to the internet.

Settings are stored in `%APPDATA%\FullLock` and logs in `%LOCALAPPDATA%\FullLock\logs`. The uninstaller offers to delete both.

## Building from source

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## License

[MIT](LICENSE). Third-party components are listed in [installer/THIRD-PARTY-NOTICES.txt](installer/THIRD-PARTY-NOTICES.txt). The Android app also bundles ZXing (Apache-2.0); see `android/app/src/main/assets/THIRD-PARTY-NOTICES.txt`.
