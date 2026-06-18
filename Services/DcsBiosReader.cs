using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BmsLightBridge.Models;

namespace BmsLightBridge.Services
{
    /// <summary>
    /// Reads cockpit state from DCS via the DCS-BIOS UDP export stream.
    ///
    /// DCS-BIOS broadcasts the full cockpit state as a stream of 16-bit-word writes
    /// into a virtual address space (max 0x10000 bytes / 0x8000 words), sent over
    /// UDP multicast 239.255.50.10:5010 on the loopback interface. Each update
    /// starts with the 4-byte sync marker 0x55 0x55 0x55 0x55, followed by any
    /// number of (address, count, data...) frames — address and count are 16-bit
    /// little-endian, data is `count` bytes that get written starting at `address`.
    ///
    /// This reader maintains a local mirror of that address space and, on each
    /// UDP packet, re-evaluates the configured DcsF16Lights table against it to
    /// produce the generic LightStates dictionary used by SyncService.
    /// </summary>
    public class DcsBiosReader : ISimulatorReader
    {
        private const string MulticastAddress = "239.255.50.10";
        private const int    Port             = 5010;
        private const int    BufferSize       = 0x10000; // full 16-bit address space, in bytes

        // DED Display (New) category, F-16C_50.json — 24-char strings, 5 lines.
        // Text lines: DED_L1..DED_L5, contiguous 24-byte blocks starting at 17902.
        // Format lines (i=inverse, b=big): DED_L1_FORMAT..DED_L5_FORMAT, contiguous from 18022.
        private const int DED_LINE_BASE   = 17902;
        private const int DED_FORMAT_BASE = 18022;
        private const int DED_LINE_LEN    = 24;
        private const int DED_LINE_COUNT  = 5;

        public event EventHandler<CockpitStateChangedEventArgs>? StateChanged;
        public event EventHandler<bool>?                          ConnectionChanged;

        public bool IsConnected { get; private set; }
        public bool IsRunning   { get; private set; }

        private UdpClient?              _udpClient;
        private CancellationTokenSource? _cts;
        private Task?                    _receiveTask;

        // Local mirror of the DCS-BIOS export address space.
        private readonly byte[] _state = new byte[BufferSize];
        private readonly object _stateLock = new();

        private Dictionary<string, bool> _lastLightStates = new();
        private string[]? _lastDedLines;
        private string[]? _lastDedFormat;

        // Connection is based on the DCS process running — matches BmsSharedMemoryReader's
        // approach, so AutoSync/Helios behave the same for both simulators: "connected"
        // means "the simulator is running", not "cockpit data is flowing right now".
        // DCS-BIOS only exports cockpit data while a unit is loaded (not in the main menu,
        // briefing screen, or paused with Escape) — light/DED updates simply pause during
        // those moments without dropping the connection or stopping sync.
        private const int PROCESS_CHECK_INTERVAL_MS = 2000;
        private static readonly string[] DcsProcessNames = { "DCS", "DCS_server" };

        private DateTime _lastProcessCheck = DateTime.MinValue;
        private System.Timers.Timer? _processCheckTimer;

        public void Start(int intervalMs = 500)
        {
            if (IsRunning) return;
            IsRunning = true;

            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                _udpClient.JoinMulticastGroup(IPAddress.Parse(MulticastAddress));
            }
            catch (Exception ex)
            {
                // Socket setup failed (port in use, multicast join refused, etc.) — leave
                // not-running so the caller can retry, but log the reason so this isn't silent.
                App.LogException("DcsBiosReader.Start", ex);
                _udpClient?.Dispose();
                _udpClient = null;
                IsRunning  = false;
                return;
            }

            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token));

            // Connection reflects whether DCS.exe is running, checked periodically —
            // mirrors BmsSharedMemoryReader's process-based detection.
            _processCheckTimer = new System.Timers.Timer(1000) { AutoReset = true };
            _processCheckTimer.Elapsed += (_, _) => CheckDcsProcess();
            _processCheckTimer.Start();
            CheckDcsProcess();
        }

        public void Stop()
        {
            if (!IsRunning) return;

            _processCheckTimer?.Stop();
            _processCheckTimer?.Dispose();
            _processCheckTimer = null;

            _cts?.Cancel();
            try { _receiveTask?.Wait(1000); } catch { /* ignore */ }
            _cts?.Dispose();
            _cts = null;
            _receiveTask = null;

            try { _udpClient?.DropMulticastGroup(IPAddress.Parse(MulticastAddress)); } catch { /* ignore */ }
            _udpClient?.Dispose();
            _udpClient = null;

            IsRunning = false;

            if (IsConnected)
            {
                IsConnected = false;
                ConnectionChanged?.Invoke(this, false);
            }
        }

        /// <summary>ChangeInterval is a no-op for DCS-BIOS — data arrives push-based via UDP.</summary>
        public void ChangeInterval(int intervalMs) { }

        public void ForceUpdate()
        {
            if (!IsConnected) return;

            Dictionary<string, bool> states;
            string[] dedLines, dedFormat;
            lock (_stateLock)
            {
                states    = BuildLightStates(_state);
                dedLines  = ReadDedLines(_state, DED_LINE_BASE);
                dedFormat = ReadDedLines(_state, DED_FORMAT_BASE);
            }
            _lastLightStates = states;
            _lastDedLines    = dedLines;
            _lastDedFormat   = dedFormat;

            StateChanged?.Invoke(this, new CockpitStateChangedEventArgs
            {
                LightStates = states,
                HasChanged  = true,
                RawBuffer   = Array.Empty<byte>(),
                DedLines    = dedLines,
                DedFormat   = dedFormat,
            });
        }

        /// <summary>
        /// Checks whether DCS.exe is running and raises ConnectionChanged on transitions.
        /// Throttled to PROCESS_CHECK_INTERVAL_MS to avoid hammering process enumeration.
        /// </summary>
        private void CheckDcsProcess()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastProcessCheck).TotalMilliseconds < PROCESS_CHECK_INTERVAL_MS)
                return;
            _lastProcessCheck = now;

            bool running = IsDcsProcessRunning();
            if (running == IsConnected) return;

            IsConnected = running;
            ConnectionChanged?.Invoke(this, running);
        }

        private static bool IsDcsProcessRunning()
        {
            foreach (var name in DcsProcessNames)
            {
                var procs = Process.GetProcessesByName(name);
                bool found = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                if (found) return true;
            }
            return false;
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            var client = _udpClient!;
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await client.ReceiveAsync(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch
                {
                    // Transient socket error — keep listening.
                    continue;
                }

                bool changed = ApplyFrame(result.Buffer);
                if (!changed) continue;

                Dictionary<string, bool> states;
                string[] dedLines, dedFormat;
                lock (_stateLock)
                {
                    states    = BuildLightStates(_state);
                    dedLines  = ReadDedLines(_state, DED_LINE_BASE);
                    dedFormat = ReadDedLines(_state, DED_FORMAT_BASE);
                }

                bool lightsDifferent = !LightStatesEqual(states, _lastLightStates);
                _lastLightStates = states;

                bool dedDifferent = !DedLinesEqual(dedLines, _lastDedLines) || !DedLinesEqual(dedFormat, _lastDedFormat);
                _lastDedLines  = dedLines;
                _lastDedFormat = dedFormat;

                StateChanged?.Invoke(this, new CockpitStateChangedEventArgs
                {
                    LightStates = states,
                    HasChanged  = lightsDifferent,
                    RawBuffer   = Array.Empty<byte>(),
                    DedLines    = dedDifferent ? dedLines  : null,
                    DedFormat   = dedDifferent ? dedFormat : null,
                });
            }
        }

        /// <summary>
        /// Parses one UDP datagram of the DCS-BIOS export protocol and applies its
        /// (address, count, data) writes to the local state mirror.
        /// Returns true if at least one byte in the buffer was written.
        /// </summary>
        private bool ApplyFrame(byte[] buffer)
        {
            bool wroteAny = false;
            int i = 0;

            while (i + 4 <= buffer.Length)
            {
                // Sync marker: 0x55 0x55 0x55 0x55 — skip, it just marks the start of an update.
                if (buffer[i] == 0x55 && buffer[i + 1] == 0x55 && buffer[i + 2] == 0x55 && buffer[i + 3] == 0x55)
                {
                    i += 4;
                    continue;
                }

                if (i + 4 > buffer.Length) break;

                int address = buffer[i] | (buffer[i + 1] << 8);
                int count   = buffer[i + 2] | (buffer[i + 3] << 8);
                i += 4;

                if (count == 0) continue;
                if (i + count > buffer.Length) break; // malformed/truncated frame — stop processing this packet

                lock (_stateLock)
                {
                    for (int b = 0; b < count; b++)
                    {
                        int destAddr = address + b;
                        if (destAddr >= 0 && destAddr < _state.Length)
                            _state[destAddr] = buffer[i + b];
                    }
                }
                wroteAny = true;
                i += count;
            }

            return wroteAny;
        }

        /// <summary>
        /// Evaluates the DcsF16Lights table against the current state mirror,
        /// producing the same Dictionary&lt;string,bool&gt; shape as BmsSharedMemoryReader.
        /// </summary>
        private static Dictionary<string, bool> BuildLightStates(byte[] state)
        {
            var result = new Dictionary<string, bool>(DcsF16Lights.All.Count);
            foreach (var light in DcsF16Lights.All)
            {
                if (light.Address + 1 >= state.Length)
                {
                    result[light.Name] = false;
                    continue;
                }

                // DCS-BIOS export words are little-endian 16-bit.
                int word = state[light.Address] | (state[light.Address + 1] << 8);
                int value = (word & light.Mask) >> light.ShiftBy;
                result[light.Name] = value != 0;
            }
            return result;
        }

        /// <summary>
        /// Reads the 5 DED text lines (or 5 format lines) as 24-char ASCII strings,
        /// each block starting `DED_LINE_LEN` bytes after the previous one.
        /// </summary>
        private static string[] ReadDedLines(byte[] state, int baseAddress)
        {
            var lines = new string[DED_LINE_COUNT];
            for (int i = 0; i < DED_LINE_COUNT; i++)
            {
                int start = baseAddress + i * DED_LINE_LEN;
                var chars = new char[DED_LINE_LEN];
                for (int j = 0; j < DED_LINE_LEN; j++)
                {
                    int addr = start + j;
                    byte b = (addr >= 0 && addr < state.Length) ? state[addr] : (byte)0;
                    // Keep printable ASCII and the BMS-style DED marker glyphs (0x01/0x02);
                    // treat anything else (including null padding) as a space.
                    chars[j] = (b == 0x01 || b == 0x02 || (b >= 0x20 && b <= 0x7E)) ? (char)b : ' ';
                }
                lines[i] = new string(chars);
            }
            return lines;
        }

        private static bool DedLinesEqual(string[]? a, string[]? b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>True if both dictionaries contain exactly the same key/value pairs.</summary>
        private static bool LightStatesEqual(Dictionary<string, bool> a, Dictionary<string, bool> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var bv) || bv != kvp.Value)
                    return false;
            }
            return true;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
