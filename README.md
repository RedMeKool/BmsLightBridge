# BmsLightBridge

![BMS Light Bridge screenshot](Docs/screenshot.png)

A C# WPF application that synchronises cockpit lighting, display data, and hardware state from **Falcon BMS** and **DCS World** directly to physical controllers — without relying on middleware like SimAppPro.

## Features

### Dual Simulator Support
- **Falcon BMS** — reads cockpit state directly from `FalconSharedMemoryArea` (LightBits, LightBits2, LightBits3)
- **DCS World (F-16C Viper)** — reads cockpit state from the [DCS-BIOS](https://github.com/DCS-Skunkworks/dcs-bios) UDP export stream; requires DCS-BIOS installed in your DCS scripts folder
- Switch between simulators with a single toggle in the toolbar; the choice is remembered between sessions
- BMS and DCS each have their own independent set of mappings — the same physical output can be mapped to different signals per simulator
- **Auto Sync** works identically for both simulators: sync starts automatically when the simulator is detected as running, and stops when it closes

### Signal Mapping
- Maps any cockpit lamp signal to a physical output on a WinWing controller or Arduino board
- **130 DCS-BIOS signals** available for the F-16C Viper, covering all exported LED states across: eyebrow warnings, AOA indexer, caution panel, landing gear, EWS/RWR panel, ECM panel, electrical/EPU system
- **Full BMS LightBits signal set** available for Falcon BMS
- Each signal can be mapped to multiple outputs simultaneously
- Duplicate-output detection is scoped per simulator: a pin mapped in BMS does not conflict with the same pin in DCS
- Signals are grouped by category and searchable by name
- **Test Signal** — fires a single mapping without starting full sync
- **Test All Mappings** — turns all mapped lights on at once for a complete hardware check (scoped to the active simulator)
- **Arduino Diagnostic** — sends setup and on/off frames to one pin and logs the result
- **Remove All** — removes all mappings for the active simulator only; the other simulator's mappings are untouched

### DED LCD Synchronisation
- Renders the Data Entry Display (DED) onto the WinWing ViperAce ICP dot-matrix LCD in real time
- Works for both **Falcon BMS** (raw shared memory) and **DCS World** (DCS-BIOS DED_L1–DED_L5 string export), with automatic inverse-video rendering
- No SimAppPro required; communicates directly via HID
- Can be toggled independently from the main sync

### COM Port & Device Identification
- COM port dropdown shows the device name next to the port number (e.g. `COM5 — F16 Misc G3`)
- Names resolved in order: DirectInput joystick name → Windows registry FriendlyName → WMI device descriptor
- **Automatic COM port recovery** — if a driver update or USB re-enumeration changes a board's COM port number, BmsLightBridge detects the board by its USB hardware identifier (VID, PID, serial number) and updates all affected mappings silently on startup

### Brightness Control
- Per-channel brightness sliders for every supported WinWing device
- Three binding modes per channel: **Manual** (fixed), **Axis** (joystick axis → 0–255), **Buttons** (step up/down with velocity-sensitive step size)
- Live brightness preview; settings survive disconnect/reconnect

### Axis to Key
- Maps a joystick axis to keyboard key presses using DIK scancodes via `keybd_event` — compatible with BMS key bindings including F13–F22
- Configurable sensitivity, repeat delay, dead zone, invert, and modifier keys

### Helios Integration
- Automatically launches Helios Control Center with a selected profile when sync starts
- **Separate profile paths for BMS and DCS** — the correct profile is loaded automatically based on the active simulator
- Optionally closes Helios when sync stops

### General
- **Auto Sync** — starts/stops sync automatically when the simulator connects/disconnects
- **Auto Start on Launch** — starts sync immediately when BmsLightBridge opens
- **Start Minimised** — launches to the system tray area
- **Config import / export** — save and load full configurations as JSON files
- Configurable BMS polling interval (default 50 ms)
- Per-Arduino board settings: baud rate, reset delay, DTR enable (supports Leonardo and ESP32)
- Configuration saved atomically to prevent corruption on crash

## Supported Hardware

| Hardware | PID | Capabilities |
|---|---|---|
| WinWing ViperAce ICP | `0xBF06` | DED LCD sync (BMS + DCS), brightness, light mapping |
| WinWing CarrierAce PTO 2 | `0xBF05` | Light mapping, brightness |
| WinWing Orion Throttle Base II + F16 Grip | `0xBE68` | Light mapping, brightness |
| WinWing CarrierAce UFC + HUD | `0xBEDE` | Brightness (UFC, LCD & HUD backlight) |
| WinWing CarrierAce MFD C | `0xBEE0` | Brightness |
| WinWing CarrierAce MFD L | `0xBEE1` | Brightness |
| WinWing CarrierAce MFD R | `0xBEE2` | Brightness |
| Arduino / ESP32 (via F4TS) | — | Light mapping |

> All devices above have been actively tested. Any WinWing device that exposes HID light channels can potentially be added.

## Requirements

- Windows 10/11 (64-bit)
- .NET 8.0 runtime (included when published as self-contained)
- **For BMS**: [Falcon BMS](https://www.benchmarksims.org/) 4.38.x or later
- **For DCS**: [DCS World](https://www.digitalcombatsimulator.com/) with [DCS-BIOS](https://github.com/DCS-Skunkworks/dcs-bios) installed
- WinWing ViperAce ICP (`0xBF06`) for DED sync

### DCS-BIOS Setup

1. Download DCS-BIOS from [github.com/DCS-Skunkworks/dcs-bios](https://github.com/DCS-Skunkworks/dcs-bios/releases)
2. Extract the `DCS-BIOS` folder to `Saved Games\DCS\Scripts\`
3. Ensure `Saved Games\DCS\Scripts\Export.lua` loads DCS-BIOS (see the DCS-BIOS README for the exact line to add)
4. Start DCS — BmsLightBridge will detect it automatically and receive cockpit data once you enter a unit

## Building

1. Clone the repository:
   ```bash
   git clone https://github.com/RedMeKool/BmsLightBridge.git
   cd BmsLightBridge
   ```

2. Open in Visual Studio 2022 or later, or build from the command line:
   ```bash
   dotnet publish BmsLightBridge.csproj -c Release -r win-x64 --self-contained true \
     -p:PublishSingleFile=true \
     -p:IncludeNativeLibrariesForSelfExtract=true
   ```

3. The self-contained executable is placed in `bin/Release/net8.0-windows/win-x64/publish/`.

## Usage

1. Launch BmsLightBridge.
2. **Select your simulator** using the BMS/DCS toggle in the toolbar. Start the simulator — the connection indicator turns green automatically.
3. **Mapping tab** — select a signal, choose a device and output, click **Add Mapping**. Mappings are stored per simulator, so you can build a full BMS set and a full DCS set independently.
4. **Brightness tab** — set brightness for each WinWing channel.
5. **Axis to Key tab** — add axis-to-key bindings for cockpit rotaries.
6. **Displays tab** — enable DED LCD synchronisation (works for both BMS and DCS).
7. **Helios tab** — configure a separate profile path for BMS and DCS if you use Helios.
8. Press **Start Sync** (or enable **Auto Sync** to do this automatically).

> Close BmsLightBridge before uploading a new sketch to an Arduino, or the COM port will be held open.

## Arduino Setup

BmsLightBridge uses the [F4ToSerial (F4TS)](https://github.com/jdahlblom/AirframeSimulatorsHardware) wire protocol. Upload a compatible sketch to your Arduino, then configure the COM port and pin assignments in the Mapping tab.

Supported boards:
- **Arduino Leonardo** — Reset Delay `2000 ms`, DTR Enable `on`
- **Arduino Uno** — Reset Delay `500 ms`, DTR Enable `on`
- **ESP32** — Reset Delay `0 ms`, DTR Enable `off`

Usable digital pins: D2–D13 and A0–A5 (referenced as 14–19). Avoid D0 and D1 (serial RX/TX).

> If a driver update changes your Arduino's COM port number, BmsLightBridge will find it automatically on next startup using the board's USB serial number.

## Project Structure

```
BmsLightBridge/
├── Services/
│   ├── Icp/
│   │   ├── IcpService.cs          # DED LCD orchestrator (BMS + DCS)
│   │   ├── IcpHidDevice.cs        # Direct HID communication
│   │   ├── DedCommand.cs          # HID command types
│   │   └── DedFont.cs             # Glyph rendering (8×13px bitmap font)
│   ├── ISimulatorReader.cs        # Common interface for simulator data sources
│   ├── BmsReader.cs               # Falcon BMS shared memory reader
│   ├── DcsBiosReader.cs           # DCS-BIOS UDP export stream reader
│   ├── WinWingService.cs          # WinWing HID light and brightness control
│   ├── ArduinoService.cs          # Arduino F4TS serial protocol
│   ├── AxisBindingService.cs      # Joystick axis/button polling and brightness binding
│   ├── AxisToKeyService.cs        # Axis-to-key injection
│   ├── SyncService.cs             # Main sync orchestrator
│   └── UsbSerialPortHelper.cs     # USB device name resolution (registry, WMI, DirectInput)
├── Models/
│   ├── Configuration.cs           # Full app configuration and persistence
│   ├── BmsSharedMemory.cs         # BMS shared memory layout and signal definitions
│   ├── DcsBiosLights.cs           # DCS-BIOS F-16C signal definitions (130 LED signals)
│   └── AxisToKeyBinding.cs        # Axis-to-key binding model
├── ViewModels/
│   ├── MainViewModel.cs           # Main application ViewModel
│   ├── AxisToKeyTabViewModel.cs   # Axis to Key tab ViewModel
│   ├── AxisToKeyBindingViewModel.cs # Per-binding ViewModel
│   ├── WinWingLightEntry.cs       # WinWing device light/brightness definitions
│   └── BaseViewModel.cs           # INotifyPropertyChanged base + RelayCommand
├── Resources/
│   ├── DedFont.bmp                # Embedded font bitmap (normal)
│   └── DedFontInverted.bmp        # Embedded font bitmap (inverted)
└── Views/
    ├── MainWindow.xaml            # Full application UI
    └── MainWindow.xaml.cs         # Code-behind (window state, import/export)
```

## Technical Notes

**Falcon BMS**
- Cockpit state is read from `FalconSharedMemoryArea` (LightBits, LightBits2, LightBits3) at a configurable polling interval
- The BMS process is verified before opening shared memory to prevent oscillation when BMS closes
- DED data is read from `FalconSharedMemoryArea2`; ICP communicates via 64-byte HID packets

**DCS World**
- DCS-BIOS broadcasts cockpit state as a binary stream of 16-bit word writes over UDP multicast `239.255.50.10:5010`
- BmsLightBridge maintains a full 64 KB mirror of the DCS-BIOS address space and evaluates the F-16C signal table against it on every update
- Connection status is based on `DCS.exe` process presence (same approach as BMS), so AutoSync and Helios launch work consistently regardless of whether the pilot is in the cockpit, the menu, or the briefing screen
- DED text is received as pre-formatted 24-character strings (`DED_L1`–`DED_L5`) with corresponding format markers (`i` = inverse, `b` = big text) from the DCS-BIOS "DED Display (New)" category

**General**
- WinWing devices are controlled via 14-byte raw HID output reports; a 1-second heartbeat keeps LEDs lit
- Key injection uses `keybd_event` with `KEYEVENTF_SCANCODE` and DIK scancodes — the only method that works reliably inside Falcon BMS; F13–F22 map to DIK scancodes `0x64`–`0x6D`
- Font glyphs are 8×13 pixels rendered from embedded BMP bitmaps; protocol analysis based on [DedSharp](https://github.com/broosa/DedSharp)
- COM port device names are resolved via DirectInput `InstanceName`, Windows registry `FriendlyName`, and WMI `Win32_PnPEntity`; USB VID/PID/serial number are persisted per board for automatic port recovery

## Roadmap

- [ ] DCS World support for additional aircraft modules beyond the F-16C Viper
- [ ] UFD (Up-Front Display) support
- [ ] WinWing Combat Ready Panel support (pending PID identification)
- [ ] MPO/OVRD lamp (BMS does not export this bit; DirectInput button monitoring under consideration)

## License

[MIT](LICENSE) — feel free to use, modify, and distribute.
