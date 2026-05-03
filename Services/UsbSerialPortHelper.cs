using Microsoft.Win32;
using System.Management;

namespace BmsLightBridge.Services
{
    public static class UsbSerialPortHelper
    {
        public class UsbSerialInfo
        {
            public string Vid          { get; init; } = "";
            public string Pid          { get; init; } = "";
            public string SerialNumber { get; init; } = "";
            public string FriendlyName { get; init; } = "";
            public string ComPort      { get; init; } = "";
        }

        private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "USB Serial Device",
            "Serieel USB-apparaat",
            "USB-Seriellgerät",
            "Périphérique série USB",
            "Dispositivo serie USB",
        };

        public static Dictionary<string, UsbSerialInfo> GetAllUsbSerialPorts()
        {
            var result = new Dictionary<string, UsbSerialInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var enumKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum");
                if (enumKey == null) return result;

                foreach (var busName in enumKey.GetSubKeyNames())
                {
                    using var busKey = enumKey.OpenSubKey(busName);
                    if (busKey == null) continue;

                    foreach (var deviceId in busKey.GetSubKeyNames())
                    {
                        using var deviceKey = busKey.OpenSubKey(deviceId);
                        if (deviceKey == null) continue;

                        string vid = "", pid = "";
                        if (busName.Equals("USB", StringComparison.OrdinalIgnoreCase))
                            ParseVidPid(deviceId, out vid, out pid);

                        foreach (var instanceId in deviceKey.GetSubKeyNames())
                        {
                            using var instanceKey = deviceKey.OpenSubKey(instanceId);
                            if (instanceKey == null) continue;

                            using var devParams = instanceKey.OpenSubKey("Device Parameters");
                            if (devParams == null) continue;

                            string? comPort = devParams.GetValue("PortName") as string;
                            if (string.IsNullOrEmpty(comPort)) continue;

                            string friendlyName = StripComPort(
                                instanceKey.GetValue("FriendlyName") as string ?? "");

                            if (string.IsNullOrEmpty(friendlyName) || GenericNames.Contains(friendlyName))
                            {
                                string? desc = instanceKey.GetValue("DeviceDesc") as string;
                                if (!string.IsNullOrEmpty(desc))
                                {
                                    int semi = desc.LastIndexOf(';');
                                    if (semi >= 0) desc = desc[(semi + 1)..].Trim();
                                    if (!string.IsNullOrEmpty(desc) && !GenericNames.Contains(desc))
                                        friendlyName = desc;
                                }
                            }

                            result[comPort] = new UsbSerialInfo
                            {
                                Vid          = vid,
                                Pid          = pid,
                                SerialNumber = instanceId,
                                FriendlyName = friendlyName,
                                ComPort      = comPort,
                            };
                        }
                    }
                }
            }
            catch { }

            EnrichWithWmiNames(result);
            return result;
        }

        /// <summary>
        /// For ports that still have a generic name, query WMI Win32_PnPEntity for the
        /// USB product string from the device descriptor. This catches boards that use
        /// the generic usbser.inf driver but report a meaningful name via USB.
        /// </summary>
        private static void EnrichWithWmiNames(Dictionary<string, UsbSerialInfo> ports)
        {
            var needsName = ports
                .Where(kv => string.IsNullOrEmpty(kv.Value.FriendlyName)
                          || GenericNames.Contains(kv.Value.FriendlyName))
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (needsName.Count == 0) return;

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%)'");

                foreach (ManagementObject obj in searcher.Get())
                {
                    string? name  = obj["Name"]?.ToString();
                    string? pnpId = obj["PNPDeviceID"]?.ToString();
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pnpId)) continue;

                    string? comPort = ExtractComPort(name);
                    if (comPort == null || !needsName.Contains(comPort)) continue;

                    string cleanName = StripComPort(name);
                    if (string.IsNullOrEmpty(cleanName) || GenericNames.Contains(cleanName)) continue;

                    var existing = ports[comPort];
                    ports[comPort] = new UsbSerialInfo
                    {
                        Vid          = existing.Vid,
                        Pid          = existing.Pid,
                        SerialNumber = existing.SerialNumber,
                        FriendlyName = cleanName,
                        ComPort      = existing.ComPort,
                    };
                }
            }
            catch { }
        }

        /// <summary>
        /// Overlays DirectInput joystick names onto COM port entries whose VID and PID
        /// match a GameControl device. This provides the name shown in Windows Game Controllers
        /// for boards that expose both a serial port and a HID joystick interface
        /// (e.g. Teensy, custom F-16 panels with composite USB firmware).
        ///
        /// ProductGuid layout (SharpDX): bytes 0-1 = VID, bytes 2-3 = PID (little-endian).
        /// </summary>
        public static void EnrichWithDirectInputNames(
            Dictionary<string, UsbSerialInfo> ports,
            SharpDX.DirectInput.DirectInput directInput)
        {
            try
            {
                var byVidPid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var devices = directInput.GetDevices(
                    SharpDX.DirectInput.DeviceClass.GameControl,
                    SharpDX.DirectInput.DeviceEnumerationFlags.AttachedOnly);

                foreach (var d in devices)
                {
                    byte[] pg    = d.ProductGuid.ToByteArray();
                    string diVid = BitConverter.ToUInt16(pg, 0).ToString("X4");
                    string diPid = BitConverter.ToUInt16(pg, 2).ToString("X4");
                    string name  = d.InstanceName.TrimEnd('\0');
                    if (!string.IsNullOrEmpty(name))
                        byVidPid[$"{diVid}:{diPid}"] = name;
                }

                foreach (var comPort in ports.Keys.ToList())
                {
                    var info = ports[comPort];
                    if (string.IsNullOrEmpty(info.Vid) || string.IsNullOrEmpty(info.Pid)) continue;

                    if (byVidPid.TryGetValue($"{info.Vid}:{info.Pid}", out string? joystickName))
                        ports[comPort] = Rename(info, joystickName);
                }
            }
            catch { }
        }

        private static UsbSerialInfo Rename(UsbSerialInfo info, string newName) =>
            new UsbSerialInfo
            {
                Vid          = info.Vid,
                Pid          = info.Pid,
                SerialNumber = info.SerialNumber,
                FriendlyName = newName,
                ComPort      = info.ComPort,
            };

        public static UsbSerialInfo? GetInfoForPort(string comPort)
        {
            var all = GetAllUsbSerialPorts();
            return all.TryGetValue(comPort, out var info) ? info : null;
        }

        public static string? FindComPortByIdentifier(string vid, string pid, string serialNumber)
        {
            if (string.IsNullOrEmpty(vid) || string.IsNullOrEmpty(pid) || string.IsNullOrEmpty(serialNumber))
                return null;

            var all = GetAllUsbSerialPorts();
            foreach (var (port, info) in all)
            {
                if (string.Equals(info.Vid,          vid,          StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(info.Pid,          pid,          StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(info.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase))
                    return port;
            }
            return null;
        }

        private static string? ExtractComPort(string name)
        {
            int start = name.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return null;
            int end = name.IndexOf(')', start);
            if (end < 0) return null;
            return name[(start + 1)..end].Trim();
        }

        private static void ParseVidPid(string deviceId, out string vid, out string pid)
        {
            vid = "";
            pid = "";
            foreach (var part in deviceId.Split(new[] { '\\', '&' }))
            {
                if (part.StartsWith("VID_", StringComparison.OrdinalIgnoreCase))
                    vid = part[4..];
                else if (part.StartsWith("PID_", StringComparison.OrdinalIgnoreCase))
                    pid = part[4..];
            }
        }

        private static string StripComPort(string name)
        {
            int idx = name.LastIndexOf(" (COM", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? name[..idx].Trim() : name.Trim();
        }
    }
}
