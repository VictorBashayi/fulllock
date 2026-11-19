# FullLock

**Steer F1 games on PC by tilting your phone or a DualShock 4.** FullLock turns your phone's gyroscope (or a PS4 pad's) into a smooth analog steering wheel, and shows a live race-engineer dashboard on the phone.

> FullLock is an independent hobby project. Not affiliated with Formula One, Formula One Licensing B.V., Codemasters or EA.

## What it does

- **Phone mode:** hold your Android phone like a wheel and tilt to steer, with on-screen throttle, brake, gears, DRS and ERS. The PC sees an ordinary Xbox 360 controller.
- **DualShock 4 mode:** hold a PS4 pad flat and bank it left or right to steer, with the triggers as pedals and rumble passed through from the game.
- **Race engineer:** in phone mode, the phone shows position, gaps, tyres, fuel and ERS from the game's telemetry, and calls out important events by voice and vibration.
- **Personal bests:** FullLock saves your best valid lap per track, per session type, dry and wet (full wet tyres), in `%APPDATA%\FullLock\pbs.json`. The phone's timing view shows your Time Trial dry and wet bests for the current track. Needs F1 2021 telemetry and the matching phone app.

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
3. First time only: pair the phone (see [Pairing](#pairing)).
4. Hold the phone level like a wheel and tap **Set Center (Calibrate)**.
5. Start the game and drive.

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

## Pairing

Pairing stops other people on your Wi-Fi from driving your virtual pad. You do it once per phone.

1. On first run the PC window shows a QR code and `Pair code: XXXX-XXXX-XX`.
2. On the phone, the pairing dialog opens by itself when the PC is found. Tap **Scan QR** (the camera permission is asked only then), or type the code. Case and dashes don't matter, and I, L and O are read as 1, 1 and 0.
3. After pairing, the QR hides. Press **P** in the PC window to show the code again, for example to pair another phone. Any number of phones can know the code, but only one drives at a time.

To get a new code, open a Command Prompt and run:

```
"C:\Program Files\FullLock\FullLock.exe" --new-code
```

With the portable zip, run `FullLock.exe --new-code` from the unzipped folder instead. The new code takes effect the next time FullLock starts (a running FullLock keeps the old code), so close and reopen FullLock. Every phone then shows "PC's pair code changed — re-pair".

"Rejected packets" in the PC window counts up briefly after a PC restart or a code change while the phone reconnects or re-pairs. Otherwise it means something else on the network is sending to FullLock.

**Limitation:** traffic is authenticated (nobody else on the Wi-Fi can drive the pad or pretend to be your PC) but not encrypted, so someone on your Wi-Fi could read the telemetry and inputs.

**Updating from the old "F1 Controller" app:** uninstall it first. FullLock has a different app id and won't replace it.

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
- A PC with both Ethernet and Wi-Fi on the same network may take a couple of seconds to connect. If it never connects, disable one of the two adapters.

**"Code doesn't match this PC — check the code on the PC screen".**
Check the code on the PC window. This message also appears when the PC didn't answer at all (PC off, wrong network, firewall), so make sure FullLock is running and both devices are on the same Wi-Fi. In the app, Settings → Pairing has **Forget PC** and **Re-pair**.

**Using `--port` other than 5555.**
Setup's firewall rule only allows UDP 5555 and 20777 from the local subnet. Add your own firewall rule for the custom port.

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
