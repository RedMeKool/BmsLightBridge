using System.IO;
using Newtonsoft.Json;

namespace BmsLightBridge.Models
{
    public enum DeviceType
    {
        Arduino,
        WinWing
    }

    /// <summary>Mapping between one BMS light signal and one output on a physical device.</summary>
    public class SignalMapping
    {
        public Guid       Id               { get; set; } = Guid.NewGuid();
        public string     BmsSignalName    { get; set; } = "";
        public DeviceType TargetDevice     { get; set; } = DeviceType.Arduino;

        // Arduino
        public string ArduinoComPort { get; set; } = "";
        public int    ArduinoPin     { get; set; } = 13;

        // WinWing
        public string WinWingDeviceName { get; set; } = "";
        public int    WinWingProductId  { get; set; } = 0;
        public int    WinWingLightIndex { get; set; } = 0;
        public string WinWingLightName  { get; set; } = "";

        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// Configuration for one Arduino or ESP32 board.
    /// Arduino Leonardo: DtrEnable=true, ResetDelayMs=2000 (hard resets on DTR).
    /// ESP32: DtrEnable=false, ResetDelayMs=0 (firmware already running).
    /// </summary>
    public class ArduinoDevice
    {
        public string Name         { get; set; } = "Arduino";
        public string ComPort      { get; set; } = "";
        public int    BaudRate     { get; set; } = 115200;
        public int    ResetDelayMs { get; set; } = 2000;
        public bool   DtrEnable    { get; set; } = true;
    }

    /// <summary>Configuration for one WinWing USB HID controller.</summary>
    public class WinWingDevice
    {
        public string Name      { get; set; } = "";
        public int    VendorId  { get; set; } = 0x4098;
        public int    ProductId { get; set; } = 0;
    }

    /// <summary>Controls the fixed brightness of a backlight channel on a WinWing device.</summary>
    public class WinWingBrightnessChannel
    {
        public int ProductId       { get; set; } = 0;
        public int LightIndex      { get; set; } = 0;
        public int FixedBrightness { get; set; } = 128;

        /// <summary>Optional axis binding. Null = no axis binding.</summary>
        public AxisBinding? AxisBinding { get; set; } = null;

        /// <summary>Optional button binding (step up/down). Null = no button binding.</summary>
        public ButtonBinding? ButtonBinding { get; set; } = null;
    }

    /// <summary>
    /// Binds two joystick buttons to a brightness channel.
    /// Step size scales automatically with press velocity: fast presses (under 250 ms apart)
    /// use up to 100% range, slow presses (over 1200 ms apart) use 1%. No user configuration needed.
    /// </summary>
    public class ButtonBinding
    {
        /// <summary>DirectInput device instance GUID.</summary>
        public string DeviceInstanceGuid { get; set; } = "";

        /// <summary>Human-readable device name (display only).</summary>
        public string DeviceName { get; set; } = "";

        /// <summary>Zero-based DirectInput button index for brightness UP.</summary>
        public int ButtonUp { get; set; } = 0;

        /// <summary>Zero-based DirectInput button index for brightness DOWN.</summary>
        public int ButtonDown { get; set; } = 1;
    }

    /// <summary>
    /// Binds a joystick axis to a brightness channel.
    /// The raw axis value (0–65535 for DirectInput) is mapped linearly to 0–255.
    /// </summary>
    public class AxisBinding
    {
        /// <summary>DirectInput device instance GUID (persisted as string).</summary>
        public string DeviceInstanceGuid { get; set; } = "";

        /// <summary>Human-readable device name (for display in UI).</summary>
        public string DeviceName { get; set; } = "";

        /// <summary>Which axis to read: X, Y, Z, RotationX, RotationY, RotationZ, Slider0, Slider1.</summary>
        public JoystickAxis Axis { get; set; } = JoystickAxis.Z;

        /// <summary>Invert the axis value (so max physical = min brightness and vice versa).</summary>
        public bool Invert { get; set; } = false;

        /// <summary>Last known raw value — used at startup when the joystick is not yet active.</summary>
        public int LastRawValue { get; set; } = -1;
    }

    public enum JoystickAxis
    {
        X, Y, Z,
        RotationX, RotationY, RotationZ,
        Slider0, Slider1
    }

    /// <summary>
    /// Defines logical device groups — multiple physical USB HID interfaces
    /// presented as a single controller in the UI.
    /// </summary>
    public static class WinWingDeviceGroups
    {
        /// <summary>Maps a display name to the list of product IDs that form the group.</summary>
        public static readonly Dictionary<string, List<ushort>> Groups = new();

        public static string? GetGroupName(ushort pid)
        {
            foreach (var (name, pids) in Groups)
                if (pids.Contains(pid)) return name;
            return null;
        }

        public static IEnumerable<ushort> GetGroupPids(ushort pid)
        {
            foreach (var (_, pids) in Groups)
                if (pids.Contains(pid)) return pids;
            return new[] { pid };
        }

        /// <summary>
        /// All WinWing product IDs recognised by BmsLightBridge.
        /// Single authoritative source — used by ConfigurationManager to identify
        /// known devices when pruning orphaned config entries.
        /// Must be kept in sync with WinWingService.KnownDevices and
        /// WinWingLightEntry.BrightnessSliderChannels.
        /// </summary>
        public static readonly HashSet<int> KnownProductIds = new()
        {
            0xBE68, // Orion Throttle Base II + F16 Grip
            0xBEDE, // CarrierAce UFC + HUD
            0xBEE0, // CarrierAce MFD C
            0xBEE1, // CarrierAce MFD L
            0xBEE2, // CarrierAce MFD R
            0xBF05, // CarrierAce PTO 2
            0xBF06, // ViperAce ICP
        };
    }

    /// <summary>Configuration for the ICP DED LCD display synchronisation.</summary>
    public class IcpDisplayConfig
    {
        /// <summary>Whether DED LCD synchronisation is enabled for the WinWing ICP.</summary>
        public bool IcpDedEnabled { get; set; } = false;
    }

    /// <summary>Settings for automatically launching Helios Control Center when sync starts.</summary>
    public class HeliosLaunchConfig
    {
        /// <summary>Whether to launch Helios Control Center when sync starts.</summary>
        public bool   Enabled             { get; set; } = false;

        /// <summary>Whether to close Helios Control Center when sync stops.</summary>
        public bool   AutoShutdown        { get; set; } = false;

        /// <summary>Full path to Control Center.exe.</summary>
        public string ControlCenterPath   { get; set; } = string.Empty;

        /// <summary>Full path to the .hpf profile file to load.</summary>
        public string ProfilePath         { get; set; } = string.Empty;
    }

    /// <summary>Root configuration — serialized to config.json in the application folder.</summary>
    public class AppConfiguration
    {
        public int    ConfigVersion      { get; set; } = 1;
        public int    PollingIntervalMs  { get; set; } = 50;
        public bool   AutoStartOnLaunch  { get; set; } = false;
        public bool   StartMinimized     { get; set; } = false;
        public bool   ShowOnlyMapped     { get; set; } = false;
        public bool   AutoSync           { get; set; } = false;

        public List<ArduinoDevice>          ArduinoDevices     { get; set; } = new();
        public List<WinWingDevice>          WinWingDevices     { get; set; } = new();
        public List<SignalMapping>          Mappings           { get; set; } = new();
        public List<WinWingBrightnessChannel> BrightnessChannels  { get; set; } = new();
        public List<AxisToKeyBinding>          AxisToKeyBindings   { get; set; } = new();

        /// <summary>Settings for ICP DED LCD display output.</summary>
        public IcpDisplayConfig IcpDisplay { get; set; } = new();

        /// <summary>Settings for launching Helios Control Center alongside sync.</summary>
        public HeliosLaunchConfig HeliosLaunch { get; set; } = new();
    }

    public static class ConfigurationManager
    {
        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public static AppConfiguration Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var config = JsonConvert.DeserializeObject<AppConfiguration>(File.ReadAllText(ConfigPath));
                    return config ?? new AppConfiguration();
                }
            }
            catch { /* corrupt config — start fresh */ }

            return new AppConfiguration();
        }

        public static void Save(AppConfiguration config)
        {
            PruneOrphanedEntries(config);

            // Write atomically: serialize to a temp file first, then replace the real config.
            // This ensures config.json is never left in a corrupt/partial state if the process
            // crashes or is killed mid-write.
            string tempPath = ConfigPath + ".tmp";
            File.WriteAllText(tempPath, JsonConvert.SerializeObject(config, Formatting.Indented));

            // File.Replace requires the destination to already exist.
            // On first launch config.json does not yet exist, so fall back to a plain Move.
            if (File.Exists(ConfigPath))
                File.Replace(tempPath, ConfigPath, null);
            else
                File.Move(tempPath, ConfigPath);
        }

        /// <summary>
        /// Loads configuration from an arbitrary file path (used for import).
        /// Returns null if the file cannot be parsed.
        /// </summary>
        public static AppConfiguration? LoadFrom(string path)
        {
            try
            {
                var config = JsonConvert.DeserializeObject<AppConfiguration>(File.ReadAllText(path));
                return config;
            }
            catch { return null; }
        }

        /// <summary>
        /// Exports the current configuration to an arbitrary file path (used for export).
        /// </summary>
        public static void ExportTo(AppConfiguration config, string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        private static void PruneOrphanedEntries(AppConfiguration config)
        {
            // ── Arduino devices ───────────────────────────────────────────
            // Keep a device entry as long as at least one mapping still targets its COM port.
            var usedComPorts = config.Mappings
                .Where(m => m.TargetDevice == DeviceType.Arduino
                         && !string.IsNullOrEmpty(m.ArduinoComPort))
                .Select(m => m.ArduinoComPort)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            config.ArduinoDevices.RemoveAll(d => !usedComPorts.Contains(d.ComPort));

            // ── Brightness channels ───────────────────────────────────────
            // A brightness channel entry is only removed when BOTH conditions are met:
            //   1. Its ProductId is not a recognised WinWing device (unknown/garbage PID).
            //   2. No mapping references that ProductId (so there is no user intent to keep it).
            // Entries for known devices are always kept — the user's brightness settings
            // for a temporarily disconnected controller must not be lost.
            var mappedProductIds = config.Mappings
                .Where(m => m.TargetDevice == DeviceType.WinWing && m.WinWingProductId != 0)
                .Select(m => m.WinWingProductId)
                .ToHashSet();

            config.BrightnessChannels.RemoveAll(ch =>
                !WinWingDeviceGroups.KnownProductIds.Contains(ch.ProductId) &&
                !mappedProductIds.Contains(ch.ProductId));
        }
    }
}
