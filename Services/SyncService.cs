using BmsLightBridge.Models;
using BmsLightBridge.Services.Icp;

namespace BmsLightBridge.Services
{
    public class SyncService : IDisposable
    {
        private readonly BmsSharedMemoryReader _bmsReader;
        public  readonly ArduinoService        ArduinoOutput;
        private readonly WinWingService        _winWingService;
        private readonly IcpService            _icpService;
        public  readonly AxisBindingService    AxisBindings;

        public IcpService IcpOutput => _icpService;

        public event EventHandler<bool>?                     BmsConnectionChanged;
        public event EventHandler<bool>?                     SyncStateChanged;
        public event EventHandler<Dictionary<string, bool>>? LightStatesUpdated;
        public event EventHandler<bool>?                     IcpConnectionChanged;

        /// <remarks>Set internally; not currently consumed outside SyncService.
        /// Kept public in case external status checks are added later.</remarks>
        public bool IsBmsConnected { get; private set; }
        public bool IsSyncing      { get; private set; }

        public Dictionary<string, bool> CurrentLightStates { get; } = new();

        private AppConfiguration? _activeConfig;

        public SyncService()
        {
            _bmsReader      = new BmsSharedMemoryReader();
            ArduinoOutput   = new ArduinoService();
            _winWingService = new WinWingService();
            _icpService     = new IcpService();
            AxisBindings    = new AxisBindingService();

            _icpService.ConnectionChanged += OnIcpConnectionChanged;
            _bmsReader.ConnectionChanged  += OnBmsConnectionChanged;
            _bmsReader.LightsChanged      += OnLightsChanged;

            // Route axis brightness changes directly to the WinWing service.
            AxisBindings.BrightnessChanged += (pid, lightIndex, brightness) =>
                _winWingService.SetBrightness(pid, lightIndex, brightness);

            AxisBindings.Start();

            // Start slow polling so the connection indicator works before the user presses Start.
            _bmsReader.Start(500);
        }

        // ── Start / Stop ──────────────────────────────────────────────────

        public void Start(AppConfiguration config)
        {
            if (IsSyncing) return;

            // WinWing: fast USB open, no blocking wait.
            var winWingPids = config.Mappings
                .Where(m => m.IsEnabled && m.TargetDevice == DeviceType.WinWing)
                .Select(m => m.WinWingProductId)
                .Distinct();

            foreach (var pid in winWingPids)
                _winWingService.Connect(pid);

            // ICP DED LCD
            if (config.IcpDisplay.IcpDedEnabled)
                _icpService.Connect();

            _activeConfig = config;
            IsSyncing     = true;

            _bmsReader.ChangeInterval(config.PollingIntervalMs);
            SyncStateChanged?.Invoke(this, true);

            _winWingService.ProcessBrightnessChannels(config.BrightnessChannels);
            _bmsReader.ForceUpdate();

            // Launch Helios Control Center if configured and not already running.
            TryLaunchHelios(config.HeliosLaunch);

            // Arduino: connect each board in parallel (Leonardo needs ~2000 ms DTR reset).
            var arduinoGroups = BuildArduinoGroups(config);
            if (arduinoGroups.Count > 0)
            {
                System.Threading.Tasks.Task.Run(() =>
                    System.Threading.Tasks.Parallel.ForEach(arduinoGroups, group =>
                        ArduinoOutput.Connect(group.ComPort, group.BaudRate, group.ResetDelayMs, group.DtrEnable, group.Mappings)));
            }
        }

        /// <summary>
        /// Builds the per-COM-port Arduino connection groups from a configuration.
        /// Extracted to avoid duplication between Start() and the test-all flow in the UI.
        /// </summary>
        public static List<(string ComPort, int BaudRate, int ResetDelayMs, bool DtrEnable, IEnumerable<SignalMapping> Mappings)>
            BuildArduinoGroups(AppConfiguration config)
            => config.Mappings
                .Where(m => m.IsEnabled && m.TargetDevice == DeviceType.Arduino)
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

        public void Stop(AppConfiguration config)
        {
            if (!IsSyncing) return;

            _bmsReader.ChangeInterval(500);

            ArduinoOutput.AllOff(config.Mappings);
            _winWingService.AllOff(config.Mappings);
            ArduinoOutput.Disconnect();

            _icpService.Disconnect();

            IsSyncing     = false;
            _activeConfig = null;

            TryShutdownHelios(config.HeliosLaunch);

            SyncStateChanged?.Invoke(this, false);
        }

        public WinWingService WinWingOutput => _winWingService;

        // ── Event handlers (background timer thread) ──────────────────────

        private void OnIcpConnectionChanged(object? sender, bool connected)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                IcpConnectionChanged?.Invoke(this, connected));
        }

        private void OnBmsConnectionChanged(object? sender, bool connected)
        {
            IsBmsConnected = connected;
            _icpService.SetBmsConnected(connected);
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                BmsConnectionChanged?.Invoke(this, connected));
        }

        private void OnLightsChanged(object? sender, LightsChangedEventArgs e)
        {
            foreach (var light in BmsLights.All)
            {
                uint bits = light.BitField switch
                {
                    1 => e.LightBits,
                    2 => e.LightBits2,
                    3 => e.LightBits3,
                    _ => 0
                };
                CurrentLightStates[light.Name] = (bits & light.BitMask) != 0;
            }

            if (IsSyncing && _activeConfig != null)
            {
                ArduinoOutput.ProcessMappings(_activeConfig.Mappings, CurrentLightStates);
                _winWingService.ProcessMappings(_activeConfig.Mappings, CurrentLightStates);
            }

            // Take a snapshot before crossing to the UI thread: the background timer
            // continues writing CurrentLightStates on the next tick, while InvokeAsync
            // delivers the event asynchronously — without a snapshot both threads would
            // read/write the same dictionary concurrently.
            var snapshot = new Dictionary<string, bool>(CurrentLightStates);
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                LightStatesUpdated?.Invoke(this, snapshot));
        }

        // ── Helios launch ─────────────────────────────────────────────────

        /// <summary>
        /// Starts Helios Control Center with the configured profile, minimised, but only if it is
        /// not already running. Silently ignores errors so that a misconfigured Helios path never
        /// prevents BmsLightBridge sync from starting.
        /// </summary>
        private static void TryLaunchHelios(HeliosLaunchConfig cfg)
        {
            if (!cfg.Enabled
                || string.IsNullOrWhiteSpace(cfg.ControlCenterPath)
                || string.IsNullOrWhiteSpace(cfg.ProfilePath))
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
                    Arguments       = $"\"{cfg.ProfilePath}\"",
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
            ArduinoOutput.ProcessMappings(config.Mappings, testStates);
            _winWingService.ProcessMappings(config.Mappings, testStates);
        }

        // ── IDisposable ───────────────────────────────────────────────────

        public void Dispose()
        {
            _bmsReader.Dispose();
            ArduinoOutput.Dispose();
            _winWingService.Dispose();
            _icpService.Dispose();
            AxisBindings.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
