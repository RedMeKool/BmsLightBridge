using SharpDX.DirectInput;
using BmsLightBridge.Models;

namespace BmsLightBridge.Services
{
    /// <summary>
    /// Represents one discovered joystick device (for the UI combo-box).
    /// </summary>
    public class JoystickDeviceInfo
    {
        public string InstanceGuid { get; init; } = "";
        public string Name         { get; init; } = "";
    }

    /// <summary>
    /// Polls joystick axes and buttons via SharpDX.DirectInput and fires <see cref="BrightnessChanged"/>
    /// whenever a bound brightness channel value changes.
    ///
    /// Supports two binding modes per channel:
    ///   - Axis binding: maps a joystick axis linearly to 0–255.
    ///   - Button binding: two buttons step brightness up/down by a configurable percentage.
    ///
    /// Thread safety: Start/Stop/UpdateBindings are called from the UI thread.
    /// The poll loop runs on a background thread; <see cref="BrightnessChanged"/> is
    /// raised from that background thread — callers must marshal to the UI thread if needed.
    /// </summary>
    public class AxisBindingService : IDisposable
    {
        // ── State ─────────────────────────────────────────────────────────

        private DirectInput?  _directInput;
        private bool          _dinputAvailable;
        private bool          _disposed;

        // Active bindings snapshots — replaced atomically via lock
        private List<(WinWingBrightnessChannel channel, AxisBinding binding)>   _activeAxisBindings   = new();
        private List<(WinWingBrightnessChannel channel, ButtonBinding binding)> _activeButtonBindings = new();
        private readonly object _bindLock = new();

        // Open joystick handles: instanceGuid → Joystick
        private readonly Dictionary<string, Joystick> _openDevices = new();
        private readonly Dictionary<(string guid, int axis), int> _axisCache = new();
        private readonly HashSet<string> _pinnedGuids = new();   // guids kept open by EnsureDeviceOpen (AxisToKey)
        private readonly object _devLock = new();

        private System.Threading.Timer? _pollTimer;

        // Last sent value per (productId, lightIndex) — avoids flooding HID with identical packets
        private readonly Dictionary<(int pid, int idx), byte> _lastSent = new();

        // Button state tracking: previous pressed state and last press timestamp per (guid, btn)
        private readonly Dictionary<(string guid, int btn), bool>     _lastButtonState = new();
        private readonly Dictionary<(string guid, int btn), DateTime>  _lastPressTime   = new();

        // Velocity step thresholds and curve
        // Interval < FastMs  → MaxStep (25%), > SlowMs → MinStep (2%)
        // Curve: quadratic falloff so moderate speed feels natural
        private const double VelocityFastMs = 200.0;
        private const double VelocitySlowMs = 1000.0;
        private const double VelocityMinStep = 2.0  * 255.0 / 100.0;   // ~5 units
        private const double VelocityMaxStep = 25.0 * 255.0 / 100.0;   // ~64 units

        /// <summary>
        /// Raised from the poll thread when a brightness channel value changes.
        /// Arguments: (productId, lightIndex, brightness 0-255).
        /// </summary>
        public event Action<int, int, byte>? BrightnessChanged;

        // ── Constructor ───────────────────────────────────────────────────

        public AxisBindingService()
        {
            try
            {
                _directInput     = new DirectInput();
                _dinputAvailable = true;
            }
            catch
            {
                _dinputAvailable = false;
            }
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// The underlying DirectInput instance, exposed so other services can reuse it
        /// for device enumeration without creating a second instance.
        /// Null when DirectInput is unavailable on this system.
        /// </summary>
        public SharpDX.DirectInput.DirectInput? DirectInputInstance => _directInput;

        /// <summary>Returns all currently attached joystick / gamepad devices.</summary>
        public List<JoystickDeviceInfo> EnumerateJoysticks()
        {
            var result = new List<JoystickDeviceInfo>();
            if (!_dinputAvailable || _directInput == null) return result;

            try
            {
                var devices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
                foreach (var d in devices)
                    result.Add(new JoystickDeviceInfo
                    {
                        InstanceGuid = d.InstanceGuid.ToString(),
                        Name         = d.InstanceName.TrimEnd('\0')
                    });
            }
            catch { /* DI not available */ }

            return result;
        }

        /// <summary>
        /// Replaces the active binding set and starts/stops the poll timer as needed.
        /// Call whenever bindings change (config load, user edit, device refresh).
        /// </summary>
        public void UpdateBindings(IEnumerable<WinWingBrightnessChannel> allChannels)
        {
            var channels = allChannels.ToList();

            var axisBindings = channels
                .Where(c => c.AxisBinding != null &&
                            !string.IsNullOrEmpty(c.AxisBinding.DeviceInstanceGuid))
                .Select(c => (c, c.AxisBinding!))
                .ToList();

            var buttonBindings = channels
                .Where(c => c.ButtonBinding != null &&
                            !string.IsNullOrEmpty(c.ButtonBinding.DeviceInstanceGuid))
                .Select(c => (c, c.ButtonBinding!))
                .ToList();

            lock (_bindLock)
            {
                _activeAxisBindings   = axisBindings;
                _activeButtonBindings = buttonBindings;
            }

            var neededGuids = axisBindings  .Select(b => b.Item2.DeviceInstanceGuid)
                .Concat(buttonBindings.Select(b => b.Item2.DeviceInstanceGuid))
                .ToHashSet();
            EnsureDevicesOpen(neededGuids);

            bool hasBindings = axisBindings.Count > 0 || buttonBindings.Count > 0;
            if (hasBindings)
                _pollTimer?.Change(50, 50);
            else
                _pollTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }

        /// <summary>Starts the background poll timer (call once after construction).</summary>
        public void Start()
        {
            _pollTimer = new System.Threading.Timer(
                _ => Poll(),
                null,
                System.Threading.Timeout.Infinite,
                System.Threading.Timeout.Infinite);
        }

        // ── Internal polling ──────────────────────────────────────────────

        private void Poll()
        {
            List<(WinWingBrightnessChannel ch, AxisBinding ab)>   axisSnapshot;
            List<(WinWingBrightnessChannel ch, ButtonBinding bb)> btnSnapshot;
            lock (_bindLock)
            {
                axisSnapshot = new List<(WinWingBrightnessChannel, AxisBinding)>(_activeAxisBindings);
                btnSnapshot  = new List<(WinWingBrightnessChannel, ButtonBinding)>(_activeButtonBindings);
            }

            if (axisSnapshot.Count == 0 && btnSnapshot.Count == 0) return;

            // Take a single snapshot of open device handles for the whole poll cycle,
            // then poll each device exactly once and cache the state.
            // This avoids calling joystick.Poll() repeatedly when multiple bindings share a device.
            Dictionary<string, Joystick> devSnapshot;
            lock (_devLock)
                devSnapshot = new Dictionary<string, Joystick>(_openDevices);

            var stateCache = new Dictionary<string, JoystickState?>();

            JoystickState? GetDeviceState(string guid, Joystick joystick)
            {
                if (stateCache.TryGetValue(guid, out var cached)) return cached;
                try
                {
                    joystick.Poll();
                    var s = joystick.GetCurrentState();
                    stateCache[guid] = s;
                    return s;
                }
                catch
                {
                    RemoveDevice(guid, joystick, devSnapshot);
                    stateCache[guid] = null;
                    return null;
                }
            }

            // ── Axis bindings ─────────────────────────────────────────────
            foreach (var (ch, ab) in axisSnapshot)
            {
                if (!devSnapshot.TryGetValue(ab.DeviceInstanceGuid, out var joystick)) continue;
                var state = GetDeviceState(ab.DeviceInstanceGuid, joystick);
                if (state == null) continue;

                int raw = GetAxisValue(state, ab.Axis);

                // Als joystick nog niet actief is (ESP32 geeft 32767), gebruik laatste bekende waarde
                if (raw == 32767 && ab.LastRawValue >= 0)
                    raw = ab.LastRawValue;
                else if (raw != 32767)
                    ab.LastRawValue = raw; // Store for next poll

                // Cache all axes for this device so AxisToKeyService can read them
                lock (_devLock)
                {
                    _axisCache[(ab.DeviceInstanceGuid, 0)] = state.X;
                    _axisCache[(ab.DeviceInstanceGuid, 1)] = state.Y;
                    _axisCache[(ab.DeviceInstanceGuid, 2)] = state.Z;
                    _axisCache[(ab.DeviceInstanceGuid, 3)] = state.RotationX;
                    _axisCache[(ab.DeviceInstanceGuid, 4)] = state.RotationY;
                    _axisCache[(ab.DeviceInstanceGuid, 5)] = state.RotationZ;
                    _axisCache[(ab.DeviceInstanceGuid, 6)] = state.Sliders.Length > 0 ? state.Sliders[0] : 0;
                    _axisCache[(ab.DeviceInstanceGuid, 7)] = state.Sliders.Length > 1 ? state.Sliders[1] : 0;
                }
                int mapped = Math.Clamp(raw, 0, 65535) * 255 / 65535;
                if (ab.Invert) mapped = 255 - mapped;
                FireIfChanged(ch, (byte)mapped);
            }

            // ── Button bindings ───────────────────────────────────────────
            foreach (var (ch, bb) in btnSnapshot)
            {
                if (!devSnapshot.TryGetValue(bb.DeviceInstanceGuid, out var joystick)) continue;
                var state = GetDeviceState(bb.DeviceInstanceGuid, joystick);
                if (state == null) continue;

                bool[] buttons = state.Buttons;
                bool upNow   = bb.ButtonUp   < buttons.Length && buttons[bb.ButtonUp];
                bool downNow = bb.ButtonDown < buttons.Length && buttons[bb.ButtonDown];

                var keyUp   = (bb.DeviceInstanceGuid, bb.ButtonUp);
                var keyDown = (bb.DeviceInstanceGuid, bb.ButtonDown);
                bool upPrev   = _lastButtonState.GetValueOrDefault(keyUp,   false);
                bool downPrev = _lastButtonState.GetValueOrDefault(keyDown, false);

                _lastButtonState[keyUp]   = upNow;
                _lastButtonState[keyDown] = downNow;

                // Fire on leading edge (press) only — not while held.
                bool stepUp   = upNow   && !upPrev;
                bool stepDown = downNow && !downPrev;

                if (!stepUp && !stepDown) continue;

                // Velocity: measure time since last press on whichever button fired.
                var activeKey = stepUp ? keyUp : keyDown;
                var now       = DateTime.UtcNow;
                double intervalMs = _lastPressTime.TryGetValue(activeKey, out var lastPress)
                    ? (now - lastPress).TotalMilliseconds
                    : VelocitySlowMs;   // first press → treat as slow
                _lastPressTime[activeKey] = now;

                // Quadratic falloff: t=0 → fast (MaxStep), t=1 → slow (MinStep)
                // t grows quadratically so moderate speed already gives a noticeably smaller step
                double t    = Math.Clamp((intervalMs - VelocityFastMs) / (VelocitySlowMs - VelocityFastMs), 0.0, 1.0);
                int step    = (int)Math.Round(VelocityMinStep + (1.0 - t * t) * (VelocityMaxStep - VelocityMinStep));

                int current = _lastSent.TryGetValue((ch.ProductId, ch.LightIndex), out byte prev)
                              ? prev : ch.FixedBrightness;

                int next = stepUp
                    ? Math.Min(current + step, 255)
                    : Math.Max(current - step, 0);

                FireIfChanged(ch, (byte)next);
            }
        }

        private void FireIfChanged(WinWingBrightnessChannel ch, byte brightness)
        {
            var key = (ch.ProductId, ch.LightIndex);
            if (_lastSent.TryGetValue(key, out byte prev) && prev == brightness) return;
            _lastSent[key]         = brightness;
            ch.FixedBrightness     = brightness;   // keep model in sync for button mode
            BrightnessChanged?.Invoke(ch.ProductId, ch.LightIndex, brightness);
        }

        private void RemoveDevice(string guid, Joystick joystick,
            Dictionary<string, Joystick> devSnapshot)
        {
            lock (_devLock)
            {
                _openDevices.Remove(guid);
                try { joystick.Dispose(); } catch { }
            }
            devSnapshot.Remove(guid);
        }

        private static int GetAxisValue(JoystickState state, JoystickAxis axis) => axis switch
        {
            JoystickAxis.X         => state.X,
            JoystickAxis.Y         => state.Y,
            JoystickAxis.Z         => state.Z,
            JoystickAxis.RotationX => state.RotationX,
            JoystickAxis.RotationY => state.RotationY,
            JoystickAxis.RotationZ => state.RotationZ,
            JoystickAxis.Slider0   => state.Sliders.Length > 0 ? state.Sliders[0] : 0,
            JoystickAxis.Slider1   => state.Sliders.Length > 1 ? state.Sliders[1] : 0,
            _                      => 0
        };

        // ── Axis / button detection ───────────────────────────────────────

        // How many poll ticks we observe before accepting a button as "stable off" in the baseline.
        private const int DetectBaselineSamples = 8;
        private const int DetectPollMs          = 40;

        /// <summary>
        /// Listens for the next clean button press on the given device and calls
        /// <paramref name="onDetected"/> with the detected button index (0-based),
        /// or <paramref name="onTimeout"/> after <paramref name="timeoutMs"/> ms.
        ///
        /// "Clean" means: the button was never seen as pressed during the baseline
        /// sampling window (so permanently-on axes/detents/HAT directions are ignored),
        /// and then transitions from off → on.
        ///
        /// Runs on a background thread; callers must marshal to the UI thread.
        /// </summary>
        public void DetectButton(string deviceInstanceGuid, int timeoutMs,
            Action<int> onDetected, Action onTimeout)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                Joystick? joystick = null;
                bool ownDevice     = false;

                lock (_devLock)
                    _openDevices.TryGetValue(deviceInstanceGuid, out joystick);

                if (joystick == null && _dinputAvailable && _directInput != null)
                {
                    try
                    {
                        var guid = new Guid(deviceInstanceGuid);
                        joystick = new Joystick(_directInput, guid);
                        joystick.SetCooperativeLevel(
                            IntPtr.Zero,
                            CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                        joystick.Acquire();
                        ownDevice = true;
                    }
                    catch { onTimeout(); return; }
                }

                if (joystick == null) { onTimeout(); return; }

                try
                {
                    // ── Phase 1: build a stable baseline ─────────────────────────
                    // Poll several times. Any button that is TRUE in ANY sample is
                    // excluded — this catches permanently-on buttons, HAT positions,
                    // and anything that flickers at rest.
                    joystick.Poll();
                    int buttonCount = joystick.GetCurrentState().Buttons.Length;
                    var everHigh = new bool[buttonCount];

                    for (int s = 0; s < DetectBaselineSamples; s++)
                    {
                        System.Threading.Thread.Sleep(DetectPollMs);
                        joystick.Poll();
                        bool[] sample = joystick.GetCurrentState().Buttons;
                        for (int i = 0; i < buttonCount; i++)
                            if (sample[i]) everHigh[i] = true;
                    }

                    // ── Phase 2: watch for a fresh leading-edge press ─────────────
                    bool[] lastSeen = new bool[buttonCount]; // all false (known stable-off)

                    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                    while (DateTime.UtcNow < deadline)
                    {
                        System.Threading.Thread.Sleep(DetectPollMs);
                        joystick.Poll();
                        bool[] current = joystick.GetCurrentState().Buttons;

                        for (int i = 0; i < buttonCount; i++)
                        {
                            if (everHigh[i]) continue;              // excluded — unstable at rest
                            if (!lastSeen[i] && current[i])         // leading edge
                            {
                                onDetected(i);
                                return;
                            }
                            lastSeen[i] = current[i];
                        }
                    }
                    onTimeout();
                }
                catch { onTimeout(); }
                finally
                {
                    if (ownDevice)
                        try { joystick.Dispose(); } catch { }
                }
            });
        }

        // ── Device management ─────────────────────────────────────────────

        /// <summary>
        /// Listens for the first axis that moves beyond <paramref name="thresholdDelta"/> from its
        /// resting position and calls <paramref name="onDetected"/> with the detected
        /// <see cref="JoystickAxis"/> value, or <paramref name="onTimeout"/> after
        /// <paramref name="timeoutMs"/> ms.
        /// Runs on a background thread; callers must marshal to the UI thread.
        /// </summary>
        public void DetectAxis(string deviceInstanceGuid, int timeoutMs,
            Action<JoystickAxis> onDetected, Action onTimeout,
            int thresholdDelta = 3000)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                Joystick? joystick = null;
                bool ownDevice     = false;

                lock (_devLock)
                    _openDevices.TryGetValue(deviceInstanceGuid, out joystick);

                if (joystick == null && _dinputAvailable && _directInput != null)
                {
                    try
                    {
                        var guid = new Guid(deviceInstanceGuid);
                        joystick = new Joystick(_directInput, guid);
                        joystick.SetCooperativeLevel(
                            IntPtr.Zero,
                            CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                        joystick.Acquire();
                        ownDevice = true;
                    }
                    catch { onTimeout(); return; }
                }

                if (joystick == null) { onTimeout(); return; }

                try
                {
                    // Capture resting position for each axis.
                    joystick.Poll();
                    var baseline = joystick.GetCurrentState();
                    var rest = new Dictionary<JoystickAxis, int>
                    {
                        [JoystickAxis.X]         = baseline.X,
                        [JoystickAxis.Y]         = baseline.Y,
                        [JoystickAxis.Z]         = baseline.Z,
                        [JoystickAxis.RotationX] = baseline.RotationX,
                        [JoystickAxis.RotationY] = baseline.RotationY,
                        [JoystickAxis.RotationZ] = baseline.RotationZ,
                        [JoystickAxis.Slider0]   = baseline.Sliders.Length > 0 ? baseline.Sliders[0] : 0,
                        [JoystickAxis.Slider1]   = baseline.Sliders.Length > 1 ? baseline.Sliders[1] : 0,
                    };

                    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                    while (DateTime.UtcNow < deadline)
                    {
                        System.Threading.Thread.Sleep(DetectPollMs);
                        joystick.Poll();
                        var state = joystick.GetCurrentState();

                        foreach (var axis in rest.Keys)
                        {
                            int current = GetAxisValue(state, axis);
                            if (Math.Abs(current - rest[axis]) >= thresholdDelta)
                            {
                                onDetected(axis);
                                return;
                            }
                        }
                    }
                    onTimeout();
                }
                catch { onTimeout(); }
                finally
                {
                    if (ownDevice)
                        try { joystick.Dispose(); } catch { }
                }
            });
        }


        private void EnsureDevicesOpen(HashSet<string> neededGuids)
        {
            if (!_dinputAvailable || _directInput == null) return;

            List<string> toOpen;
            lock (_devLock)
            {
                // Close devices no longer needed by brightness bindings and not pinned by AxisToKey
                var toClose = _openDevices.Keys.Where(g => !neededGuids.Contains(g) && !_pinnedGuids.Contains(g)).ToList();
                foreach (var g in toClose)
                {
                    try { _openDevices[g].Dispose(); } catch { }
                    _openDevices.Remove(g);
                }

                // Collect devices that need to be opened (wake-up loop runs outside the lock)
                toOpen = neededGuids.Where(g => !_openDevices.ContainsKey(g)).ToList();
            }

            // Open new devices outside the lock so the 200ms wake-up doesn't block the poll thread
            foreach (var guidStr in toOpen)
            {
                try
                {
                    var guid = new Guid(guidStr);

                    // Pre-check: only try to open the device when it is actually attached.
                    // This prevents SharpDX from throwing a SharpDXException (DIERR_DEVICENOTREG /
                    // DIERR_NOTFOUND) every poll cycle when a configured joystick is disconnected.
                    bool attached = _directInput!
                        .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
                        .Any(d => d.InstanceGuid == guid);

                    if (!attached) continue;

                    var joystick = new Joystick(_directInput, guid);

                    joystick.SetCooperativeLevel(
                        IntPtr.Zero,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    joystick.Acquire();

                    // Wake-up polls outside the lock — this takes ~200ms per new device
                    for (int w = 0; w < 10; w++)
                    {
                        try { joystick.Poll(); joystick.GetCurrentState(); }
                        catch { }
                        System.Threading.Thread.Sleep(20);
                    }

                    lock (_devLock) _openDevices[guidStr] = joystick;
                }
                catch { /* device not available */ }
            }
        }

        // ── IDisposable ───────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _pollTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _pollTimer?.Dispose();

            lock (_devLock)
            {
                foreach (var js in _openDevices.Values)
                    try { js.Dispose(); } catch { }
                _openDevices.Clear();
            }

            _directInput?.Dispose();
            _directInput = null;

            GC.SuppressFinalize(this);
        }

        // ── Publieke hulpmethodes voor AxisToKeyService ───────────────────

        public int? GetAxisValue(string deviceGuid, JoystickAxis axis)
        {
            // Use cached value from the poll loop — always up to date
            lock (_devLock)
            {
                if (_axisCache.TryGetValue((deviceGuid, (int)axis), out int cached))
                    return cached;
            }

            // No cached value yet — try reading directly from the open device
            Joystick? js;
            lock (_devLock)
                if (!_openDevices.TryGetValue(deviceGuid, out js)) return null;
            try
            {
                js.Poll();
                var state = js.GetCurrentState();
                int val   = GetAxisValue(state, axis);
                lock (_devLock) _axisCache[(deviceGuid, (int)axis)] = val;
                return val;
            }
            catch { return null; }
        }

        public void EnsureDeviceOpen(string deviceGuid)
        {
            lock (_devLock)
            {
                _pinnedGuids.Add(deviceGuid);
                if (_openDevices.ContainsKey(deviceGuid)) return;
            }

            if (!_dinputAvailable || _directInput == null) return;
            try
            {
                var guid = new Guid(deviceGuid);

                // Pre-check: avoid SharpDXException when the device is not attached
                bool attached = _directInput
                    .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
                    .Any(d => d.InstanceGuid == guid);

                if (!attached) return;

                var js = new Joystick(_directInput, guid);
                js.SetCooperativeLevel(IntPtr.Zero,
                    CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                js.Acquire();
                lock (_devLock) _openDevices[deviceGuid] = js;
            }
            catch { }
        }
    }
}
