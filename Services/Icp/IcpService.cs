using System.Timers;
using BmsLightBridge.Models;

namespace BmsLightBridge.Services.Icp
{
    /// <summary>
    /// Renders BMS DED data to the WinWing ViperAce ICP LCD at up to ~10 Hz.
    ///
    /// DED bytes are supplied by SyncService (via BmsReader) — IcpService does not
    /// open its own shared memory handle. A device timer (2 s) handles USB connect/disconnect.
    /// </summary>
    public class IcpService : IDisposable
    {
        // Shared memory — FalconSharedMemoryArea (Area1 / FlightData struct).
        // Offsets verified against BMS FlightData.h.
        // Raw bytes are supplied by BmsReader via SyncService — IcpService has no MMF handle of its own.
        private const int    OFFSET_DED_LINES  = 236;  // char DEDLines[5][26]
        private const int    OFFSET_DED_INVERT = 366;  // char Invert[5][26]
        private const int    DED_LINE_LEN      = 26;
        private const int    DED_LINE_COUNT    = 5;

        private const int DEVICE_POLL_MS = 2000;

        private IcpHidDevice?             _device;
        private readonly object           _lock       = new();
        private bool                      _isEnabled;
        private bool                      _isBmsConnected;
        private string[]                  _lastDed = Array.Empty<string>();
        private string[]                  _lastInv = Array.Empty<string>();

        /// <summary>Pre-allocated blank DED frame — reused by TryClearDisplay to avoid repeated allocations.</summary>
        private static readonly string[] BlankDedLines =
            Enumerable.Repeat(new string(' ', 24), DED_LINE_COUNT).ToArray();

        private readonly System.Timers.Timer _deviceTimer;

        public bool IsConnected { get; private set; }

        /// <summary>Fired on a thread-pool thread when USB connect/disconnect is detected.</summary>
        public event EventHandler<bool>? ConnectionChanged;

        public IcpService()
        {
            _deviceTimer          = new System.Timers.Timer(DEVICE_POLL_MS) { AutoReset = true };
            _deviceTimer.Elapsed += OnDeviceTimerElapsed;
            _deviceTimer.Start();
        }

        // ── Timer callbacks ───────────────────────────────────────────────

        private void OnDeviceTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            lock (_lock)
            {
                if (_device == null)
                    TryOpenDevice();
                // Liveness is detected via write failures in SendFrame/TryClearDisplay;
                // no need to re-enumerate HID devices every 2 s just to check presence.
            }
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>Enable DED output. Called when the user enables the checkbox or starts sync.</summary>
        public void Connect()
        {
            lock (_lock)
            {
                _isEnabled = true;
                TryOpenDevice();
                if (_device != null)
                    TryClearDisplay();
            }
        }

        /// <summary>
        /// Disable DED output and blank the display.
        /// Note: _isBmsConnected is intentionally NOT reset here — it is managed
        /// exclusively by SetBmsConnected() so the state stays consistent when
        /// the user re-enables the toggle while BMS is still running.
        /// </summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                _isEnabled = false;
                _lastDed   = Array.Empty<string>();
                _lastInv   = Array.Empty<string>();
                TryClearDisplay();
            }
        }

        /// <summary>Called by SyncService when BMS connection state changes.</summary>
        public void SetBmsConnected(bool connected)
        {
            lock (_lock)
            {
                _isBmsConnected = connected;
                if (!connected)
                {
                    _lastDed = Array.Empty<string>();
                    _lastInv = Array.Empty<string>();
                    if (_isEnabled && _device != null)
                        TryClearDisplay();
                }
            }
        }
        // ── Device handling ───────────────────────────────────────────────

        private void TryOpenDevice()
        {
            if (_device != null) return;
            try
            {
                _device     = new IcpHidDevice();
                IsConnected = true;
                ConnectionChanged?.Invoke(this, true);
                if (_isEnabled) TryClearDisplay();
            }
            catch
            {
                _device     = null;
                IsConnected = false;
            }
        }

        private void CloseDevice()
        {
            _device?.Dispose();
            _device     = null;
            IsConnected = false;
            ConnectionChanged?.Invoke(this, false);
        }

        // ── DED read / send ───────────────────────────────────────────────

        /// <summary>
        /// Called by SyncService with the raw shared memory buffer from BmsReader.
        /// Replaces the former self-managed MMF poll — IcpService no longer opens its own MMF handle.
        /// </summary>
        public void ProcessDedBuffer(byte[] raw)
        {
            lock (_lock)
            {
                if (!_isEnabled || !_isBmsConnected || _device == null) return;

                try
                {
                    var ded = ReadLines(raw, OFFSET_DED_LINES);
                    var inv = ReadInvertLines(raw, OFFSET_DED_INVERT);

                    if (DedChanged(ded, inv))
                    {
                        _lastDed = ded;
                        _lastInv = inv;
                        SendFrame(ded, inv);
                    }
                }
                catch { }
            }
        }

        private void SendFrame(string[] ded, string[] inv)
        {
            if (_device == null) return;
            try
            {
                var frameData = DedFont.Render(ded, inv);
                _device.WriteDedCommands(new[]
                {
                    new DedCommand { CommandType = DedCommand.CMD_WRITE_DISPLAY_MEM,
                        TimeStamp = 0xFFFF, DataBuffer = frameData },
                    new DedCommand { CommandType = DedCommand.CMD_REFRESH_DISPLAY,
                        TimeStamp = 0xFFFF, DataBuffer = DedCommand.RefreshPayload }
                });
            }
            catch
            {
                // Write failure means the device was physically disconnected.
                // Close the handle so the device timer can reopen it on the next tick.
                CloseDevice();
            }
        }

        private void TryClearDisplay()
        {
            try { SendFrame(BlankDedLines, BlankDedLines); }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static string[] ReadLines(byte[] raw, int offset)
        {
            var lines = new string[DED_LINE_COUNT];
            for (int i = 0; i < DED_LINE_COUNT; i++)
            {
                int start = offset + i * DED_LINE_LEN;
                int len   = 0;
                while (len < DED_LINE_LEN && start + len < raw.Length && raw[start + len] != 0)
                    len++;

                var chars = new char[len];
                for (int j = 0; j < len; j++)
                {
                    byte b = raw[start + j];
                    // BMS uses 0x01/0x02 as DED marker glyphs; all others must be printable ASCII.
                    chars[j] = (b == 0x01 || b == 0x02 || (b >= 0x20 && b <= 0x7E)) ? (char)b : ' ';
                }
                lines[i] = new string(chars).PadRight(24);
            }
            return lines;
        }

        /// <summary>
        /// Reads the BMS Invert[] array from shared memory.
        /// Unlike DEDLines, the Invert array is NOT null-terminated — it is a fixed-size
        /// block where every byte is either a space (0x20, no inversion) or a non-space
        /// marker byte (inversion active). We must read all DED_LINE_LEN bytes and treat
        /// null bytes as spaces, otherwise invert markers past the first null are missed.
        /// </summary>
        private static string[] ReadInvertLines(byte[] raw, int offset)
        {
            var lines = new string[DED_LINE_COUNT];
            for (int i = 0; i < DED_LINE_COUNT; i++)
            {
                int start = offset + i * DED_LINE_LEN;
                var chars = new char[DED_LINE_LEN];
                for (int j = 0; j < DED_LINE_LEN; j++)
                {
                    byte b = (start + j < raw.Length) ? raw[start + j] : (byte)0;
                    // Any non-space, non-null byte signals inversion for this column.
                    chars[j] = (b != 0x00 && b != 0x20) ? (char)b : ' ';
                }
                lines[i] = new string(chars);
            }
            return lines;
        }

        private bool DedChanged(string[] ded, string[] inv)
        {
            if (_lastDed.Length == 0) return true;
            for (int i = 0; i < DED_LINE_COUNT; i++)
            {
                if (ded[i] != _lastDed[i]) return true;
                if (inv[i] != _lastInv[i]) return true;
            }
            return false;
        }

        // ── IDisposable ───────────────────────────────────────────────────

        public void Dispose()
        {
            _deviceTimer.Stop(); _deviceTimer.Dispose();
            lock (_lock) { TryClearDisplay(); CloseDevice(); }
            GC.SuppressFinalize(this);
        }
    }
}
