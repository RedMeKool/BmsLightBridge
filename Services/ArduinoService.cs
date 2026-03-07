using System.IO.Ports;
using System.Text;
using BmsLightBridge.Models;

namespace BmsLightBridge.Services
{
    // =========================================================================
    // ARDUINO SERVICE — F4ToSerial compatible protocol
    // =========================================================================
    // Wire format (same as F4TS):
    //   [0xC0] {"setup_LightBit":{"pins":[2,5,13]}}\r
    //   [0xC0] {"set_LightBit":{"mode":[1,0,1]}}\r
    //
    // - 0xC0 = SLIP frame marker
    // - \r   = frame terminator
    // - pins = Arduino pin numbers, order defines index
    // - mode = 0/1 values in the same order as pins
    //
    // Multiple boards are supported simultaneously via per-port state.
    // =========================================================================

    public class ArduinoService : IDisposable
    {
        // ── Per-port state ────────────────────────────────────────────────

        private sealed class PortState : IDisposable
        {
            public SerialPort Port    { get; }
            public string     ComPort { get; }
            public int[]      Pins       { get; set; } = Array.Empty<int>();
            public bool[]     LastMode   { get; set; } = Array.Empty<bool>();
            /// <summary>
            /// Scratch buffer for the current poll cycle — same length as LastMode.
            /// Reused to avoid a new bool[] allocation on every ProcessPort call.
            /// </summary>
            public bool[]     ModeBuffer { get; set; } = Array.Empty<bool>();
            /// <summary>
            /// Cached, ordered list of active mappings for this port.
            /// Rebuilt by BuildPinTable; reused every poll cycle to avoid repeated LINQ filtering.
            /// </summary>
            public List<SignalMapping> ActiveMappings { get; set; } = new();

            public PortState(SerialPort port, string comPort)
            {
                Port    = port;
                ComPort = comPort;
            }

            public void Dispose()
            {
                try { Port.Close(); Port.Dispose(); } catch { }
            }
        }

        // ── Fields ────────────────────────────────────────────────────────

        private readonly Dictionary<string, PortState> _ports = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        // ── Static helpers ────────────────────────────────────────────────

        public static string[] GetAvailableComPorts()
            => SerialPort.GetPortNames().OrderBy(p => p).ToArray();

        /// <summary>True if at least one port is open.</summary>
        public bool IsConnected
        {
            get { lock (_lock) return _ports.Values.Any(p => p.Port.IsOpen); }
        }

        /// <summary>True if the specified COM port is open.</summary>
        public bool IsPortConnected(string comPort)
        {
            lock (_lock)
                return _ports.TryGetValue(comPort, out var s) && s.Port.IsOpen;
        }

        // ── Connection ────────────────────────────────────────────────────

        public bool Connect(string comPort, int baudRate, int resetDelayMs, bool dtrEnable, IEnumerable<SignalMapping> mappings)
        {
            lock (_lock)
            {
                // Already open — just refresh the pin table.
                if (_ports.TryGetValue(comPort, out var existing) && existing.Port.IsOpen)
                {
                    BuildPinTable(existing, mappings);
                    SendSetup(existing);
                    return true;
                }

                existing?.Dispose();
                _ports.Remove(comPort);

                try
                {
                    var serial = new SerialPort(comPort, baudRate)
                    {
                        DataBits     = 8,
                        Parity       = Parity.None,
                        StopBits     = StopBits.One,
                        WriteTimeout = 2000,
                        Encoding     = Encoding.UTF8,
                        DtrEnable    = dtrEnable,
                        RtsEnable    = false
                    };

                    serial.Open();

                    // Wait for the board to finish resetting/booting.
                    // Leonardo: ~2000 ms (hard resets on DTR).
                    // ESP32 with auto-reset circuit: ~500 ms.
                    // ESP32 without reset circuit: 0 ms (firmware already running).
                    if (resetDelayMs > 0)
                        System.Threading.Thread.Sleep(resetDelayMs);

                    var state = new PortState(serial, comPort);
                    BuildPinTable(state, mappings);
                    SendSetup(state);

                    _ports[comPort] = state;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Disconnects all open ports.</summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                foreach (var state in _ports.Values)
                    state.Dispose();
                _ports.Clear();
            }
        }

        /// <summary>Disconnects a single port.</summary>
        public void Disconnect(string comPort)
        {
            lock (_lock)
            {
                if (_ports.TryGetValue(comPort, out var state))
                {
                    state.Dispose();
                    _ports.Remove(comPort);
                }
            }
        }

        // ── Build internal pin table ──────────────────────────────────────

        private static void BuildPinTable(PortState state, IEnumerable<SignalMapping> mappings)
        {
            var active = mappings
                .Where(m => m.IsEnabled
                         && m.TargetDevice == DeviceType.Arduino
                         && string.Equals(m.ArduinoComPort, state.ComPort, StringComparison.OrdinalIgnoreCase))
                .ToList();

            state.ActiveMappings = active;
            state.Pins           = active.Select(m => m.ArduinoPin).ToArray();
            state.LastMode       = new bool[state.Pins.Length];
            state.ModeBuffer     = new bool[state.Pins.Length];
        }

        // ── F4TS wire-format senders ──────────────────────────────────────

        private static void SendSetup(PortState state)
        {
            if (state.Pins.Length == 0) return;
            string frame = "{\"setup_LightBit\":{\"pins\":[" + string.Join(",", state.Pins) + "]}}";
            WriteFrame(state, frame);
        }

        private static void SendMode(PortState state, bool[] modes)
        {
            string modeJson = string.Join(",", modes.Select(m => m ? "1" : "0"));
            WriteFrame(state, "{\"set_LightBit\":{\"mode\":[" + modeJson + "]}}");
        }

        // ── High-level: process all mappings ──────────────────────────────

        public void ProcessMappings(IEnumerable<SignalMapping> mappings, Dictionary<string, bool> lightStates)
        {
            lock (_lock)
            {
                foreach (var state in _ports.Values)
                    ProcessPort(state, mappings, lightStates);
            }
        }

        private static void ProcessPort(PortState state, IEnumerable<SignalMapping> mappings,
            Dictionary<string, bool> lightStates)
        {
            if (!state.Port.IsOpen || state.Pins.Length == 0) return;

            // Use the cached mapping list. Rebuild only if the count has changed
            // (e.g., user added/removed a mapping while sync is active).
            var active = state.ActiveMappings;
            if (active.Count != state.Pins.Length)
            {
                BuildPinTable(state, mappings);
                SendSetup(state);
                active = state.ActiveMappings;
            }

            // Fill the reusable scratch buffer — no heap allocation.
            bool[] modes   = state.ModeBuffer;
            bool   changed = false;

            for (int i = 0; i < active.Count && i < modes.Length; i++)
            {
                modes[i] = lightStates.TryGetValue(active[i].BmsSignalName, out bool isOn) && isOn;
                if (modes[i] != state.LastMode[i]) changed = true;
            }

            if (!changed) return;

            // Copy current values into LastMode and send.
            Array.Copy(modes, state.LastMode, modes.Length);
            SendMode(state, modes);
        }

        /// <summary>Turns all lights off on all open ports.</summary>
        public void AllOff(IEnumerable<SignalMapping> mappings)
        {
            lock (_lock)
            {
                foreach (var state in _ports.Values)
                {
                    if (!state.Port.IsOpen || state.Pins.Length == 0) continue;
                    // Reuse LastMode: clear it in-place and send.
                    Array.Clear(state.LastMode, 0, state.LastMode.Length);
                    SendMode(state, state.LastMode);
                }
            }
        }

        // ── Diagnostic (temporary port, does not affect live ports) ───────

        public string RunDiagnostic(string comPort, int baudRate, int resetDelayMs, bool dtrEnable,
            string signalName, int pin, IEnumerable<SignalMapping> allMappings)
        {
            var log = new StringBuilder();

            try
            {
                log.AppendLine($"Opening {comPort} @ {baudRate} baud (DTR={dtrEnable}, resetDelay={resetDelayMs} ms)...");

                var portMappings = allMappings
                    .Where(m => m.IsEnabled
                             && m.TargetDevice == DeviceType.Arduino
                             && string.Equals(m.ArduinoComPort, comPort, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                int[] allPins  = portMappings.Select(m => m.ArduinoPin).ToArray();
                int   pinIndex = Array.IndexOf(allPins, pin);

                if (pinIndex < 0)
                {
                    log.AppendLine($"ERROR: Pin {pin} not found in mappings for {comPort}.");
                    log.AppendLine($"Available pins: {string.Join(", ", allPins)}");
                    return log.ToString();
                }

                log.AppendLine($"Pin {pin} is index {pinIndex} in pin table: [{string.Join(",", allPins)}]");

                SerialPort p;
                bool ownedByUs = false;

                lock (_lock)
                {
                    if (_ports.TryGetValue(comPort, out var existing) && existing.Port.IsOpen)
                    {
                        log.AppendLine("Reusing existing connection.");
                        p = existing.Port;
                    }
                    else
                    {
                        var serial = new SerialPort(comPort, baudRate)
                        {
                            DataBits     = 8,
                            Parity       = Parity.None,
                            StopBits     = StopBits.One,
                            WriteTimeout = 2000,
                            Encoding     = Encoding.UTF8,
                            DtrEnable    = dtrEnable,
                            RtsEnable    = false
                        };
                        serial.Open();
                        ownedByUs = true;
                        p = serial;

                        System.Threading.Thread.Sleep(200);
                        if (resetDelayMs > 0)
                        {
                            log.AppendLine($"Waiting {resetDelayMs} ms for board to boot...");
                            System.Threading.Thread.Sleep(resetDelayMs);
                        }
                    }
                }

                string fullSetup = "{\"setup_LightBit\":{\"pins\":[" + string.Join(",", allPins) + "]}}";
                log.AppendLine("Sending SETUP: " + fullSetup);
                p.Write(new byte[] { 0xC0 }, 0, 1);
                p.Write(fullSetup + "\r");
                System.Threading.Thread.Sleep(300);

                string allOff = "{\"set_LightBit\":{\"mode\":[" + string.Join(",", Enumerable.Repeat("0", allPins.Length)) + "]}}";
                p.Write(new byte[] { 0xC0 }, 0, 1);
                p.Write(allOff + "\r");
                System.Threading.Thread.Sleep(100);

                var modeArr = Enumerable.Repeat("0", allPins.Length).ToArray();
                modeArr[pinIndex] = "1";
                string onFrame = "{\"set_LightBit\":{\"mode\":[" + string.Join(",", modeArr) + "]}}";

                log.AppendLine($"Sending ON (index {pinIndex} = pin {pin}): " + onFrame);
                p.Write(new byte[] { 0xC0 }, 0, 1);
                p.Write(onFrame + "\r");
                log.AppendLine("Light should be ON for 2 seconds...");
                System.Threading.Thread.Sleep(2000);

                log.AppendLine("Sending all OFF.");
                p.Write(new byte[] { 0xC0 }, 0, 1);
                p.Write(allOff + "\r");
                log.AppendLine("Done.");

                if (ownedByUs)
                {
                    p.Close();
                    p.Dispose();
                }
                else
                {
                    lock (_lock)
                    {
                        if (_ports.TryGetValue(comPort, out var state))
                            SendSetup(state);
                    }
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("ERROR: " + ex.Message);
            }

            return log.ToString();
        }

        // ── Low-level write ───────────────────────────────────────────────

        private static void WriteFrame(PortState state, string frame)
        {
            try
            {
                state.Port.Write(new byte[] { 0xC0 }, 0, 1);
                state.Port.Write(frame + "\r");
            }
            catch { /* write errors are non-fatal; next poll will retry */ }
        }

        // ── IDisposable ───────────────────────────────────────────────────

        public void Dispose()
        {
            Disconnect();
            GC.SuppressFinalize(this);
        }
    }
}
