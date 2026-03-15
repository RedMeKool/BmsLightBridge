# BmsLightBridge

![BMS Light Bridge screenshot](Docs/screenshot.png)

A C# WPF application that synchronises [Falcon BMS](https://www.benchmarksims.org/) cockpit lighting, display data, and hardware state directly to physical controllers — without relying on middleware like SimAppPro.

## Features

- **Cockpit light signal mapping** — Maps individual BMS cockpit lamp signals (e.g. Master Caution, Gear Down, Hook, LE Flaps, TWP warnings) directly to physical lights on WinWing controllers and Arduino boards. Each signal can be independently assigned to any supported output device and pin/light index.
- **Brightness synchronisation** — Maps Falcon BMS cockpit lighting variables to WinWing device brightness channels in real time, including axis binding (e.g. a physical dimmer knob)
- **DED LCD synchronisation** — Renders the Data Entry Display (DED) directly onto the WinWing ViperAce ICP dot-matrix LCD via HID protocol
- **Arduino lighting control** — Controls lights on Arduino boards via the F4TS protocol
- **Helios profile launcher** — Automatically starts a Helios profile alongside Falcon BMS
- **Direct HID communication** — No middleware required; communicates directly with hardware using raw HID packets
- **Modular architecture** — Service-based design makes it easy to extend with additional displays and hardware

## Supported Hardware

| Hardware | PID | Capabilities |
|---|---|---|
| WinWing ViperAce ICP | `0xBF06` | DED LCD sync, brightness, light mapping |
| WinWing CarrierAce PTO 2 | `0xBF05` | Light mapping, brightness |
| WinWing Orion Throttle Base II + F16 Grip | `0xBE68` | Light mapping, brightness |
| WinWing CarrierAce UFC + HUD | `0xBEDE` | Brightness (UFC, LCD & HUD backlight) |
| WinWing CarrierAce MFD C | `0xBEE0` | Brightness |
| WinWing CarrierAce MFD L | `0xBEE1` | Brightness |
| WinWing CarrierAce MFD R | `0xBEE2` | Brightness |
| Arduino (via F4TS) | — | Light mapping |

> **Note:** All devices above have been actively tested. Any WinWing device that exposes HID light channels can potentially be added.

## Requirements

- [Falcon BMS](https://www.benchmarksims.org/) (tested with BMS 4.38.1)
- Windows 10/11 (64-bit)
- .NET 8.0 or later
- WinWing ViperAce ICP (VID `0x4098` / PID `0xBF06`) for DED sync

## Building

1. Clone the repository:
   ```bash
   git clone https://github.com/RedMeKool/BmsLightBridge.git
   cd BmsLightBridge
   ```

2. Open `BmsLightBridge.sln` in Visual Studio 2022 or later.

3. Restore NuGet packages (done automatically on build):
   - `System.Drawing.Common`

4. Build the solution (`Ctrl+Shift+B`).

## Usage

1. Start Falcon BMS and load into a mission.
2. Launch BmsLightBridge.
3. In the **Brightness** tab: configure your WinWing device brightness mappings.
4. In the **Displays** tab: enable DED synchronisation and verify that your ICP is detected.
5. The application reads cockpit state from `FalconSharedMemoryArea2` in real time.

## Project Structure

```
BmsLightBridge/
├── Services/
│   ├── Icp/
│   │   ├── IcpService.cs          # Main ICP service orchestrator
│   │   ├── IcpHidDevice.cs        # Direct HID communication
│   │   ├── DedCommand.cs          # DED command types
│   │   └── DedFont.cs             # Glyph rendering (8×13px bitmap font)
│   ├── WinWingService.cs          # WinWing HID communication
│   ├── ArduinoService.cs          # Arduino F4TS communication
│   ├── BmsReader.cs               # Falcon BMS shared memory reader
│   ├── AxisBindingService.cs      # Joystick axis binding
│   └── SyncService.cs             # Main sync loop
├── Models/
│   ├── Configuration.cs           # App configuration model
│   └── BmsSharedMemory.cs         # Shared memory definitions
├── Resources/
│   ├── DedFont.bmp                # Embedded font bitmap (normal)
│   └── DedFontInverted.bmp        # Embedded font bitmap (inverted)
├── ViewModels/
│   └── MainViewModel.cs           # WPF ViewModel
└── Views/
    └── MainWindow.xaml            # Main UI (Brightness + Displays tabs)
```

## Technical Notes

- DED data is read from `FalconSharedMemoryArea2`
- ICP communicates via 64-byte HID packets using two command types: `CMD_WRITE_DISPLAY_MEM` and `CMD_REFRESH_DISPLAY`
- Font glyphs are 8×13 pixels, rendered from embedded BMP bitmaps
- Protocol analysis was based on the open-source [DedSharp](https://github.com/broosa/DedSharp) project

## Roadmap

- [ ] UFD (Up-Front Display) support
- [ ] Additional WinWing device support
- [ ] Extended Displays tab with more cockpit displays

## License

[MIT](LICENSE) — feel free to use, modify, and distribute.
