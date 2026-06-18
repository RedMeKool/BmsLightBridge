using BmsLightBridge.Models;
using BmsLightBridge.Services.Icp;

namespace BmsLightBridge.Services
{
    public class SyncService : IDisposable
    {
        private ISimulatorReader        _activeReader;
        private readonly ArduinoService        _arduinoOutput;
        private readonly WinWingService        _winWingService;
        private readonly IcpService            _icpService;
        private readonly AxisBindingService    _axisBindings;

        private SimulatorType _activeSimulator = SimulatorType.BMS;

        public ArduinoService     ArduinoOutput => _arduinoOutput;
        public AxisBindingService AxisBindings  => _axisBindings;
        public IcpService         IcpOutput     => _icpService;

        public event EventHandler<bool>?                     SimulatorConnectionChanged;
        public event EventHandler<bool>?                     SyncStateChanged;
        public event EventHandler<Dictionary<string, bool>>? LightStatesUpdated;
        public event EventHandler<bool>?                     IcpConnectionChanged;

        public bool IsSyncing { get; private set; }

        private readonly Dictionary<string, bool> _currentLightStates = new();

        private AppConfiguration? _activeConfig;

        public SyncService()
        {
            _arduinoOutput  = new ArduinoService();
            _winWingService = new WinWingService();
            _icpService     = new IcpService();
            _axisBindings   = new AxisBindingService();

            _icpService.ConnectionChanged += OnIcpConnectionChanged;

            _axisBindings.BrightnessChanged += (pid, lightIndex, brightness) =>
                _winWingService.SetBrightness(pid, lightIndex, brightness);

            _axisBindings.Start();

            // Default to BMS on startup; SetSimulator(config.Simulator) is called once
            // the configuration has been loaded, and will swap readers if needed.
            _activeReader = CreateReader(SimulatorType.BMS);
            AttachReader(_activeReader);
            _activeReader.Start(500);
        }

        /// <summary>
        /// Switches the active cockpit-data reader (BMS shared memory or DCS-BIOS).
        /// Safe to call at any time, including while sync is running — the previous
        /// reader is stopped/disposed and the new one started in its place.
        /// If the simulator is unchanged, this is a no-op.
        /// </summary>
        public void SetSimulator(SimulatorType simulator)
        {
            if (simulator == _activeSimulator) return;

            int interval = IsSyncing ? (_activeConfig?.PollingIntervalMs ?? 500) : 500;

            DetachReader(_activeReader);
            _activeReader.Dispose();

            _activeSimulator = simulator;
            _activeReader    = CreateReader(simulator);
            AttachReader(_activeReader);
            _activeReader.Start(interval);

            // Reset cached state — the new reader starts disconnected.
            _currentLightStates.Clear();
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                SimulatorConnectionChanged?.Invoke(this, false));
        }

        private static ISimulatorReader CreateReader(SimulatorType simulator) => simulator switch
        {
            SimulatorType.DCS => new DcsBiosReader(),
            _                  => new BmsSharedMemoryReader(),
        };

        private void AttachReader(ISimulatorReader reader)
        {
            reader.ConnectionChanged += OnSimulatorConnectionChanged;
            reader.StateChanged      += OnStateChanged;
        }

        private void DetachReader(ISimulatorReader reader)
        {
            reader.ConnectionChanged -= OnSimulatorConnectionChanged;
            reader.StateChanged      -= OnStateChanged;
        }

        // ── Start / Stop ──────────────────────────────────────────────────

        public void Start(AppConfiguration config)
        {
            if (IsSyncing) return;

            // Make sure the reader matches the configured simulator before we start syncing.
            SetSimulator(config.Simulator);

            // WinWing: fast USB open, no blocking wait.
            var winWingPids = config.Mappings
                .Where(m => m.IsEnabled && m.TargetDevice == DeviceType.WinWing && m.Simulator == config.Simulator)
                .Select(m => m.WinWingProductId)
                .Distinct();

            foreach (var pid in winWingPids)
                _winWingService.Connect(pid);

            // ICP DED LCD
            if (config.IcpDisplay.IcpDedEnabled)
                _icpService.Connect();

            _activeConfig = config;
            IsSyncing     = true;

            _activeReader.ChangeInterval(config.PollingIntervalMs);
            SyncStateChanged?.Invoke(this, true);

            _winWingService.ProcessBrightnessChannels(config.BrightnessChannels);
            _activeReader.ForceUpdate();

            // Launch Helios Control Center if configured and not already running.
            TryLaunchHelios(config.HeliosLaunch, config.HeliosLaunch.GetProfilePath(config.Simulator));

            // Arduino: connect each board in parallel (Leonardo needs ~2000 ms DTR reset).
            var arduinoGroups = BuildArduinoGroups(config);
            if (arduinoGroups.Count > 0)
            {
                System.Threading.Tasks.Task.Run(() =>
                    System.Threading.Tasks.Parallel.ForEach(arduinoGroups, group =>
                        _arduinoOutput.Connect(group.ComPort, group.BaudRate, group.ResetDelayMs, group.DtrEnable, group.Mappings)));
            }
        }

        /// <summary>
        /// Builds the per-COM-port Arduino connection groups from a configuration.
        /// Extracted to avoid duplication between Start() and the test-all flow in the UI.
        /// </summary>
        public static List<(string ComPort, int BaudRate, int ResetDelayMs, bool DtrEnable, IEnumerable<SignalMapping> Mappings)>
            BuildArduinoGroups(AppConfiguration config)
            => config.Mappings
                .Where(m => m.IsEnabled && m.TargetDevice == DeviceType.Arduino && m.Simulator == config.Simulator)
                .GroupBy(m => m.ArduinoComPort)
                .Select(g =>
                {
                    var dev = config.ArduinoDevices.FirstOrDefault(d => d.ComPort == g.Key);
                    return (
                        ComPort:      g.Key,
                        BaudRate:     dev?.BaudRate     ?? 115200,
                        ResetDelayMs: dev?.ResetDelayMs ?? 2000,
                        DtrEnable:    dev?.DtrEnable    ?? true,
                        Mappings:     (IEnumerable<SignalMapping>)g.ToList()
                    );
                })
                .ToList();

        /// <summary>Immediately sends a single brightness channel value to the device. Used for live slider preview.</summary>
        public void ApplyBrightnessNow(WinWingBrightnessChannel channel)
        {
            _winWingService.SetBrightness(channel.ProductId, channel.LightIndex, (byte)channel.FixedBrightness);
        }

        /// <summary>
        /// Called from the UI when the ICP DED toggle changes while sync is already active.
        /// Connects or disconnects the ICP device without restarting full sync.
        /// </summary>
        public void ApplyIcpConfig(bool icpEnabled)
        {
            if (icpEnabled)
                _icpService.Connect();
            else
                _icpService.Disconnect();
        }

        public void Stop()
        {
            if (!IsSyncing) return;

            _activeReader.ChangeInterval(500);

            _arduinoOutput.AllOff();
            _winWingService.AllOff(_activeConfig!.Mappings);
            _arduinoOutput.Disconnect();

            _icpService.Disconnect();

            var heliosCfg = _activeConfig!.HeliosLaunch;
            IsSyncing     = false;
            _activeConfig = null;

            TryShutdownHelios(heliosCfg);

            SyncStateChanged?.Invoke(this, false);
        }

        public WinWingService WinWingOutput => _winWingService;

        // ── Event handlers (background timer thread) ──────────────────────

        private void OnIcpConnectionChanged(object? sender, bool connected)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                IcpConnectionChanged?.Invoke(this, connected));
        }

        private void OnSimulatorConnectionChanged(object? sender, bool connected)
        {
            _icpService.SetSimulatorConnected(connected);
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                SimulatorConnectionChanged?.Invoke(this, connected));
        }

        private void OnStateChanged(object? sender, CockpitStateChangedEventArgs e)
        {
            if (e.RawBuffer.Length > 0)
                _icpService.ProcessDedBuffer(e.RawBuffer);

            if (e.DedLines != null && e.DedFormat != null)
                _icpService.ProcessDedLines(e.DedLines, e.DedFormat);

            if (!e.HasChanged) return;

            _currentLightStates.Clear();
            foreach (var kvp in e.LightStates)
                _currentLightStates[kvp.Key] = kvp.Value;

            var cfg = _activeConfig;
            if (IsSyncing && cfg != null)
            {
                var activeMappings = cfg.Mappings.Where(m => m.Simulator == cfg.Simulator).ToList();
                _arduinoOutput.ProcessMappings(activeMappings, _currentLightStates);
                _winWingService.ProcessMappings(activeMappings, _currentLightStates);
            }

            var snapshot = new Dictionary<string, bool>(_currentLightStates);
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                LightStatesUpdated?.Invoke(this, snapshot));
        }

        // ── Helios launch ─────────────────────────────────────────────────

        /// <summary>
        /// Starts Helios Control Center with the configured profile, minimised, but only if it is
        /// not already running. Silently ignores errors so that a misconfigured Helios path never
        /// prevents BmsLightBridge sync from starting.
        /// </summary>
        private static void TryLaunchHelios(HeliosLaunchConfig cfg, string profilePath)
        {
            if (!cfg.Enabled
                || string.IsNullOrWhiteSpace(cfg.ControlCenterPath)
                || string.IsNullOrWhiteSpace(profilePath))
                return;

            try
            {
                // Helios exe is "Control Center.exe" — process image name has no space.
                bool alreadyRunning = System.Diagnostics.Process
                    .GetProcessesByName("Control Center")
                    .Length > 0;

                if (alreadyRunning) return;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = cfg.ControlCenterPath,
                    Arguments       = $"\"{profilePath}\"",
                    UseShellExecute = true,
                    WindowStyle     = System.Diagnostics.ProcessWindowStyle.Minimized
                });
            }
            catch
            {
                // Deliberately swallowed: Helios is optional tooling.
                // The user will notice it didn't open; sync continues regardless.
            }
        }

        /// <summary>
        /// Closes Helios Control Center if AutoShutdown is enabled.
        /// Gracefully closes the main window; falls back to Kill() if needed.
        /// </summary>
        private static void TryShutdownHelios(HeliosLaunchConfig cfg)
        {
            if (!cfg.AutoShutdown) return;

            try
            {
                var procs = System.Diagnostics.Process.GetProcessesByName("Control Center");
                foreach (var p in procs)
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(2000))
                        p.Kill();
                    p.Dispose();
                }
            }
            catch { }
        }

        // ── Test output (called from UI thread) ───────────────────────────

        public void FireTestOutput(AppConfiguration config, Dictionary<string, bool> testStates)
        {
            var activeMappings = config.Mappings.Where(m => m.Simulator == config.Simulator).ToList();
            _arduinoOutput.ProcessMappings(activeMappings, testStates);
            _winWingService.ProcessMappings(activeMappings, testStates);
        }

        // ── IDisposable ───────────────────────────────────────────────────

        public void Dispose()
        {
            _activeReader.Dispose();
            _arduinoOutput.Dispose();
            _winWingService.Dispose();
            _icpService.Dispose();
            _axisBindings.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
