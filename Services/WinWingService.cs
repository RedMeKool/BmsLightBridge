using System.Runtime.InteropServices;
using BmsLightBridge.Models;
using BmsLightBridge.ViewModels;

namespace BmsLightBridge.Services
{
    /// <summary>
    /// Controls WinWing device LEDs via hidapi.dll.
    /// Protocol reverse-engineered via USBPcap capture of SimAppPro.
    /// </summary>
    public class WinWingService : IDisposable
    {
        public const int WinWingVendorId = 0x4098;

        // ── hidapi P/Invoke ───────────────────────────────────────────────

        [DllImport("hidapi.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hid_init();

        [DllImport("hidapi.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hid_exit();

        [DllImport("hidapi.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr hid_open(ushort vendor_id, ushort product_id, IntPtr serial_number);

        [DllImport("hidapi.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void hid_close(IntPtr device);

        [DllImport("hidapi.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int hid_write(IntPtr device, byte[] data, UIntPtr length);

        // ── Known devices ─────────────────────────────────────────────────

        private static readonly Dictionary<ushort, string> KnownDevices = new()
        {
            { 0xBE68, "Orion Throttle Base II + F16 Grip" },
            { 0xBEDE, "CarrierAce UFC + HUD"              },
            { 0xBEE0, "CarrierAce MFD C"                  },
            { 0xBEE1, "CarrierAce MFD L"                  },
            { 0xBEE2, "CarrierAce MFD R"                  },
            { 0xBF05, "CarrierAce PTO 2"                  },
            { 0xBF06, "ViperAce ICP"                      },
        };

        // ── State ─────────────────────────────────────────────────────────

        private readonly Dictionary<ushort, IntPtr> _handles = new();
        private readonly object _lock = new();
        private bool _hidInitialized;
        private bool _disposed;

        // WinWing requires a periodic heartbeat to keep LEDs lit.
        private readonly System.Threading.Timer _heartbeatTimer;

        private static readonly byte[] HeartbeatPacket =
            { 0x02, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        // ── Constructor ───────────────────────────────────────────────────

        public WinWingService()
        {
            try
            {
                _hidInitialized = hid_init() == 0;
            }
            catch { /* hidapi.dll not available */ }

            _heartbeatTimer = new System.Threading.Timer(
                _ => SendHeartbeatToAll(),
                null,
                System.Threading.Timeout.Infinite,
                System.Threading.Timeout.Infinite);
        }

        // ── Enumeration ───────────────────────────────────────────────────

        public static List<WinWingDevice> EnumerateDevices()
        {
            var result    = new List<WinWingDevice>();
            var addedPids = new HashSet<ushort>();

            try
            {
                var hidDevices = HidSharp.DeviceList.Local
                    .GetHidDevices(WinWingVendorId)
                    .ToList();

                foreach (var d in hidDevices)
                {
                    ushort pid = (ushort)d.ProductID;
                    if (!KnownDevices.TryGetValue(pid, out var name)) continue;
                    if (addedPids.Contains(pid)) continue;

                    var groupName = WinWingDeviceGroups.GetGroupName(pid);
                    if (groupName != null)
                    {
                        var groupPids = WinWingDeviceGroups.Groups[groupName];
                        if (addedPids.Contains(groupPids[0])) continue;

                        result.Add(new WinWingDevice
                        {
                            ProductId = groupPids[0],
                            Name      = groupName,
                        });

                        foreach (var gPid in WinWingDeviceGroups.Groups[groupName])
                            addedPids.Add(gPid);
                    }
                    else
                    {
                        result.Add(new WinWingDevice { ProductId = pid, Name = name });
                        addedPids.Add(pid);
                    }
                }
            }
            catch { /* device enumeration not available */ }

            return result;
        }

        // ── Connection management ─────────────────────────────────────────

        /// <summary>Opens a hidapi handle for the given product ID. Safe to call multiple times.</summary>
        public bool Connect(int productId)
        {
            ushort pid = (ushort)productId;
            lock (_lock)
            {
                if (_handles.TryGetValue(pid, out IntPtr existing) && existing != IntPtr.Zero)
                    return true;

                IntPtr handle = hid_open((ushort)WinWingVendorId, pid, IntPtr.Zero);
                if (handle == IntPtr.Zero) return false;

                _handles[pid] = handle;

                HidWrite(handle, HeartbeatPacket);
                _heartbeatTimer.Change(1000, 1000);
                return true;
            }
        }

        public void Disconnect(int productId)
        {
            ushort pid = (ushort)productId;
            lock (_lock)
            {
                if (_handles.TryGetValue(pid, out IntPtr handle) && handle != IntPtr.Zero)
                {
                    try { hid_close(handle); } catch { }
                    _handles.Remove(pid);
                }

                if (_handles.Count == 0)
                    _heartbeatTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }
        }

        public void DisconnectAll()
        {
            _heartbeatTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            lock (_lock)
            {
                foreach (var handle in _handles.Values)
                    try { hid_close(handle); } catch { }
                _handles.Clear();
            }
        }

        // ── Light control ─────────────────────────────────────────────────

        /// <summary>
        /// Sets a single LED on/off. Uses SetBrightness for channels that require it.
        /// </summary>
        public bool SetLight(int productId, int lightIndex, bool on)
        {
            ushort pid = (ushort)productId;

            if (WinWingLightEntry.IsBrightnessChannel(pid, lightIndex))
                return SetBrightness(productId, lightIndex, on ? (byte)255 : (byte)0);

            // Fast path: handle already open.
            lock (_lock)
            {
                if (_handles.TryGetValue(pid, out IntPtr handle) && handle != IntPtr.Zero)
                {
                    int result = HidWrite(handle, BuildLightPacket(pid, lightIndex, on));
                    if (result > 0) return true;
                    _handles.Remove(pid);
                    return false;
                }
            }

            // Handle not open — connect (idempotent), then send once.
            // Connect() acquires _lock internally, so we release above first to avoid re-entrancy.
            if (!Connect(productId)) return false;
            lock (_lock)
            {
                if (!_handles.TryGetValue(pid, out IntPtr handle) || handle == IntPtr.Zero)
                    return false;
                int result = HidWrite(handle, BuildLightPacket(pid, lightIndex, on));
                if (result <= 0) { _handles.Remove(pid); return false; }
                return true;
            }
        }

        /// <summary>Sets a brightness channel (0–255) on a WinWing device.</summary>
        public bool SetBrightness(int productId, int lightIndex, byte brightness)
        {
            ushort pid = (ushort)productId;

            lock (_lock)
            {
                if (_handles.TryGetValue(pid, out IntPtr handle) && handle != IntPtr.Zero)
                {
                    int result = HidWrite(handle, BuildLightPacketForChannel(pid, lightIndex, brightness));
                    if (result > 0) return true;
                    _handles.Remove(pid);
                    return false;
                }
            }

            // Handle was not open — try to connect, then send once.
            if (!Connect(productId)) return false;
            lock (_lock)
            {
                if (!_handles.TryGetValue(pid, out IntPtr handle) || handle == IntPtr.Zero)
                    return false;
                int result = HidWrite(handle, BuildLightPacketForChannel(pid, lightIndex, brightness));
                if (result <= 0) { _handles.Remove(pid); return false; }
                return true;
            }
        }

        /// <summary>Applies all brightness channel settings.</summary>
        public void ProcessBrightnessChannels(IEnumerable<WinWingBrightnessChannel> channels)
        {
            foreach (var ch in channels)
                SetBrightness(ch.ProductId, ch.LightIndex, (byte)ch.FixedBrightness);
        }

        public void ProcessMappings(
            IEnumerable<SignalMapping> mappings,
            Dictionary<string, bool> lightStates)
        {
            foreach (var mapping in mappings.Where(m => m.IsEnabled && m.TargetDevice == DeviceType.WinWing))
            {
                bool on = lightStates.TryGetValue(mapping.BmsSignalName, out bool val) && val;
                SetLight(mapping.WinWingProductId, mapping.WinWingLightIndex, on);
            }
        }

        public void AllOff(IEnumerable<SignalMapping> mappings)
        {
            foreach (var mapping in mappings.Where(m => m.IsEnabled && m.TargetDevice == DeviceType.WinWing))
                SetLight(mapping.WinWingProductId, mapping.WinWingLightIndex, false);
        }

        // ── Packet builders ───────────────────────────────────────────────
        // Protocol reverse-engineered from SimAppPro USB captures.
        // All packets: 02 [b2] [b3] 00 00 03 49 [index] [value] 00 00 00 00 00
        // b2/b3 are device-family specific; some devices route channels differently.

        /// <summary>Protocol bytes (b2, b3) differ per device family.</summary>
        private static (byte b2, byte b3) GetProtocolBytes(ushort pid) => pid switch
        {
            0xBE68 => (0x60, 0xBE),   // Orion Throttle Base II + F16 Grip
            0xBF06 => (0x06, 0xBF),   // ViperAce ICP
            0xBEDE => (0xD0, 0xBE),   // CarrierAce UFC + HUD
            0xBEE2 => (0x01, 0x00),   // CarrierAce MFD R
            0xBEE0 => (0x01, 0x00),   // CarrierAce MFD C
            0xBEE1 => (0x01, 0x00),   // CarrierAce MFD L
            _      => (0x05, 0xBF),   // PTO2 and others
        };

        /// <summary>
        /// Some devices route brightness channels to different internal interfaces.
        /// UFC+HUD: index 2 (HUD Backlight) uses 0x0E/0xBE and packet index 1.
        /// </summary>
        private static (byte b2, byte b3, int packetIndex) GetChannelProtocol(ushort pid, int lightIndex)
        {
            if (pid == 0xBEDE)
            {
                return lightIndex switch
                {
                    0 => (0xD0, 0xBE, 0),  // UFC Backlight
                    1 => (0xD0, 0xBE, 1),  // LCD Backlight
                    2 => (0x0E, 0xBE, 1),  // HUD Backlight — separate interface
                    _ => (0xD0, 0xBE, lightIndex)
                };
            }
            var (b2, b3) = GetProtocolBytes(pid);
            return (b2, b3, lightIndex);
        }

        private static byte[] BuildLightPacket(ushort pid, int lightIndex, bool on)
        {
            var (b2, b3) = GetProtocolBytes(pid);
            return new byte[]
            {
                0x02, b2, b3, 0x00, 0x00, 0x03, 0x49,
                (byte)lightIndex,
                on ? (byte)0x01 : (byte)0x00,
                0x00, 0x00, 0x00, 0x00, 0x00
            };
        }

        private static byte[] BuildLightPacketForChannel(ushort pid, int lightIndex, byte brightness)
        {
            var (b2, b3, packetIndex) = GetChannelProtocol(pid, lightIndex);
            return new byte[]
            {
                0x02, b2, b3, 0x00, 0x00, 0x03, 0x49,
                (byte)packetIndex,
                brightness,
                0x00, 0x00, 0x00, 0x00, 0x00
            };
        }

        // ── Heartbeat ─────────────────────────────────────────────────────

        private void SendHeartbeatToAll()
        {
            List<(ushort pid, IntPtr handle)> snapshot;
            lock (_lock)
            {
                snapshot = _handles
                    .Where(kv => kv.Value != IntPtr.Zero)
                    .Select(kv => (kv.Key, kv.Value))
                    .ToList();
            }

            foreach (var (pid, handle) in snapshot)
            {
                if (HidWrite(handle, HeartbeatPacket) <= 0)
                    lock (_lock) { _handles.Remove(pid); }
            }
        }

        // ── Low-level write ───────────────────────────────────────────────

        private static int HidWrite(IntPtr handle, byte[] data)
            => hid_write(handle, data, (UIntPtr)data.Length);

        // ── IDisposable ───────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _heartbeatTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _heartbeatTimer.Dispose();

            DisconnectAll();

            if (_hidInitialized)
                try { hid_exit(); } catch { }

            GC.SuppressFinalize(this);
        }
    }
}
