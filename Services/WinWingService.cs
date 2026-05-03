using HidSharp;
using BmsLightBridge.Models;
using BmsLightBridge.ViewModels;

namespace BmsLightBridge.Services
{
    /// <summary>
    /// Controls WinWing device LEDs via HidSharp.
    /// Protocol reverse-engineered via USBPcap capture of SimAppPro.
    /// </summary>
    public class WinWingService : IDisposable
    {
        public const int WinWingVendorId = 0x4098;

        // ── Known devices ─────────────────────────────────────────────────

        /// <summary>Maps WinWing product ID to display name. Single source of truth for all PID lists.</summary>
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

        private readonly Dictionary<ushort, HidStream> _streams = new();
        private readonly object _lock = new();
        private bool _disposed;

        // WinWing requires a periodic heartbeat to keep LEDs lit.
        private readonly System.Threading.Timer _heartbeatTimer;

        // Pre-allocated packet buffers — avoids heap allocation on the hot 100 ms write path.
        // All WinWing LED/brightness packets are 14 bytes.
        private const int PacketSize = 14;
        private readonly byte[] _packetBuf = new byte[PacketSize];

        private static readonly byte[] HeartbeatPacket =
            { 0x02, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        // ── Constructor ───────────────────────────────────────────────────

        public WinWingService()
        {
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
                foreach (var d in DeviceList.Local.GetHidDevices(WinWingVendorId))
                {
                    ushort pid = (ushort)d.ProductID;
                    if (!KnownDevices.ContainsKey(pid)) continue;
                    if (addedPids.Contains(pid)) continue;

                    result.Add(new WinWingDevice { ProductId = pid, Name = KnownDevices[pid] });
                    addedPids.Add(pid);
                }
            }
            catch { /* device enumeration not available */ }

            return result;
        }

        // ── Connection management ─────────────────────────────────────────

        /// <summary>Opens a HidSharp stream for the given product ID. Safe to call multiple times.</summary>
        public bool Connect(int productId)
        {
            ushort pid = (ushort)productId;
            lock (_lock)
            {
                if (_streams.ContainsKey(pid)) return true;

                try
                {
                    var device = DeviceList.Local
                        .GetHidDevices(WinWingVendorId, pid)
                        .FirstOrDefault(d => d.GetMaxOutputReportLength() > 0);

                    if (device == null) return false;
                    if (!device.TryOpen(out HidStream stream)) return false;

                    stream.WriteTimeout = 100;
                    _streams[pid] = stream;

                    HidWrite(stream, HeartbeatPacket);
                    _heartbeatTimer.Change(1000, 1000);
                    return true;
                }
                catch { return false; }
            }
        }

        public void Disconnect(int productId)
        {
            ushort pid = (ushort)productId;
            lock (_lock)
            {
                if (_streams.Remove(pid, out var stream))
                    try { stream.Close(); } catch { }

                if (_streams.Count == 0)
                    _heartbeatTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }
        }

        public void DisconnectAll()
        {
            _heartbeatTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            lock (_lock)
            {
                foreach (var stream in _streams.Values)
                    try { stream.Close(); } catch { }
                _streams.Clear();
            }
        }

        // ── Light control ─────────────────────────────────────────────────

        /// <summary>Sets a single LED on/off. Brightness channels are routed to SetBrightness.</summary>
        public bool SetLight(int productId, int lightIndex, bool on)
        {
            if (WinWingLightEntry.IsBrightnessChannel((ushort)productId, lightIndex))
                return SetBrightness(productId, lightIndex, on ? (byte)255 : (byte)0);

            FillLightPacket((ushort)productId, lightIndex, on);
            return WriteToDevice((ushort)productId, _packetBuf);
        }

        /// <summary>Sets a brightness channel (0–255) on a WinWing device.</summary>
        public bool SetBrightness(int productId, int lightIndex, byte brightness)
        {
            FillBrightnessPacket((ushort)productId, lightIndex, brightness);
            return WriteToDevice((ushort)productId, _packetBuf);
        }

        /// <summary>Applies all brightness channel settings.</summary>
        public void ProcessBrightnessChannels(IEnumerable<WinWingBrightnessChannel> channels)
        {
            foreach (var ch in channels)
                SetBrightness(ch.ProductId, ch.LightIndex, (byte)ch.FixedBrightness);
        }

        public void ProcessMappings(IEnumerable<SignalMapping> mappings, Dictionary<string, bool> lightStates)
        {
            foreach (var m in mappings)
            {
                if (!m.IsEnabled || m.TargetDevice != DeviceType.WinWing) continue;
                bool on = lightStates.TryGetValue(m.BmsSignalName, out bool val) && val;
                SetLight(m.WinWingProductId, m.WinWingLightIndex, on);
            }
        }

        public void AllOff(IEnumerable<SignalMapping> mappings)
        {
            foreach (var m in mappings)
            {
                if (m.IsEnabled && m.TargetDevice == DeviceType.WinWing)
                    SetLight(m.WinWingProductId, m.WinWingLightIndex, false);
            }
        }

        // ── Packet builders (write into pre-allocated buffer) ─────────────
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

        private void FillLightPacket(ushort pid, int lightIndex, bool on)
        {
            var (b2, b3) = GetProtocolBytes(pid);
            _packetBuf[0] = 0x02; _packetBuf[1] = b2;  _packetBuf[2] = b3;  _packetBuf[3] = 0x00;
            _packetBuf[4] = 0x00; _packetBuf[5] = 0x03; _packetBuf[6] = 0x49;
            _packetBuf[7] = (byte)lightIndex;
            _packetBuf[8] = on ? (byte)0x01 : (byte)0x00;
            _packetBuf[9] = 0x00; _packetBuf[10] = 0x00; _packetBuf[11] = 0x00;
            _packetBuf[12] = 0x00; _packetBuf[13] = 0x00;
        }

        private void FillBrightnessPacket(ushort pid, int lightIndex, byte brightness)
        {
            var (b2, b3, packetIndex) = GetChannelProtocol(pid, lightIndex);
            _packetBuf[0] = 0x02; _packetBuf[1] = b2;  _packetBuf[2] = b3;  _packetBuf[3] = 0x00;
            _packetBuf[4] = 0x00; _packetBuf[5] = 0x03; _packetBuf[6] = 0x49;
            _packetBuf[7] = (byte)packetIndex;
            _packetBuf[8] = brightness;
            _packetBuf[9] = 0x00; _packetBuf[10] = 0x00; _packetBuf[11] = 0x00;
            _packetBuf[12] = 0x00; _packetBuf[13] = 0x00;
        }

        // ── Heartbeat ─────────────────────────────────────────────────────

        private void SendHeartbeatToAll()
        {
            List<(ushort pid, HidStream stream)> snapshot;
            lock (_lock)
            {
                snapshot = _streams.Select(kv => (kv.Key, kv.Value)).ToList();
            }

            foreach (var (pid, stream) in snapshot)
            {
                if (!HidWrite(stream, HeartbeatPacket))
                    lock (_lock) { _streams.Remove(pid); }
            }
        }

        // ── Low-level write ───────────────────────────────────────────────

        /// <summary>
        /// Connects if needed, then writes data to the device.
        /// All writes on the 100 ms hot path go through here.
        /// </summary>
        private bool WriteToDevice(ushort pid, byte[] data)
        {
            lock (_lock)
            {
                if (_streams.TryGetValue(pid, out var stream))
                {
                    if (HidWrite(stream, data)) return true;
                    _streams.Remove(pid);
                }
            }

            // Not connected (or just dropped) — try to (re)connect, then write once.
            if (!Connect(pid)) return false;
            lock (_lock)
            {
                return _streams.TryGetValue(pid, out var stream) && HidWrite(stream, data);
            }
        }

        /// <summary>
        /// Writes a HID output report via HidSharp.
        /// WinWing devices are written directly without a prepended report-ID byte,
        /// consistent with how IcpHidDevice handles the same vendor's hardware.
        /// Returns true on success.
        /// </summary>
        private static bool HidWrite(HidStream stream, byte[] data)
        {
            try { stream.Write(data); return true; }
            catch { return false; }
        }

        // ── IDisposable ───────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _heartbeatTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _heartbeatTimer.Dispose();

            DisconnectAll();

            GC.SuppressFinalize(this);
        }
    }
}
