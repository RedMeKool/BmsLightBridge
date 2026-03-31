using System.Runtime.InteropServices;
using SharpDX.DirectInput;
using BmsLightBridge.Models;

namespace BmsLightBridge.Services
{
    /// <summary>
    /// Gebruikt de al geopende joystick verbindingen van AxisBindingService.
    /// Zo werkt het device altijd, ook als het nog niet "geactiveerd" is.
    /// </summary>
    public class AxisToKeyService : IDisposable
    {
        private readonly AxisBindingService _axisBindingService;
        private bool _disposed;

        private List<AxisToKeyBinding>             _activeBindings = new();
        private readonly object                    _bindLock       = new();
        private readonly Dictionary<Guid, int>     _lastRaw        = new();
        private readonly Dictionary<Guid, DateTime> _lastFireTime   = new();

        private System.Threading.Timer? _pollTimer;

        public AxisToKeyService(AxisBindingService axisBindingService)
        {
            _axisBindingService = axisBindingService;
        }

        public void Start()
        {
            _pollTimer = new System.Threading.Timer(
                _ => { try { Poll(); } catch { } },
                null,
                System.Threading.Timeout.Infinite,
                System.Threading.Timeout.Infinite);
        }

        public void UpdateBindings(IEnumerable<AxisToKeyBinding> bindings)
        {
            var active = bindings
                .Where(b => b.IsEnabled
                         && !string.IsNullOrEmpty(b.DeviceInstanceGuid)
                         && (b.KeyUp != 0 || b.KeyDown != 0))
                .ToList();

            lock (_bindLock) _activeBindings = active;

            // Zorg dat elk device open is via AxisBindingService
            foreach (var b in active)
                _axisBindingService.EnsureDeviceOpen(b.DeviceInstanceGuid);

            if (active.Count > 0)
                _pollTimer?.Change(16, 16);
            else
                _pollTimer?.Change(System.Threading.Timeout.Infinite,
                                   System.Threading.Timeout.Infinite);
        }

        public void DetectAxis(string deviceGuid, int timeoutMs,
            Action<JoystickAxis> onDetected, Action onTimeout)
        {
            _axisBindingService.EnsureDeviceOpen(deviceGuid);
            System.Threading.Tasks.Task.Run(() =>
            {
                int[]? baseline = null;
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                var axes = Enum.GetValues<JoystickAxis>().Take(8).ToArray();

                while (DateTime.UtcNow < deadline)
                {
                    var current = axes.Select(a =>
                        _axisBindingService.GetAxisValue(deviceGuid, a) ?? 32767).ToArray();

                    if (baseline == null) { baseline = current; }
                    else
                    {
                        for (int i = 0; i < current.Length; i++)
                            if (Math.Abs(current[i] - baseline[i]) > 3000)
                            { onDetected(axes[i]); return; }
                    }
                    System.Threading.Thread.Sleep(16);
                }
                onTimeout();
            });
        }

        private void Poll()
        {
            List<AxisToKeyBinding> snapshot;
            lock (_bindLock) snapshot = new(_activeBindings);

            if (snapshot.Count == 0) return;

            var now = DateTime.UtcNow;

            foreach (var binding in snapshot)
            {
                int? rawNullable = _axisBindingService.GetAxisValue(
                    binding.DeviceInstanceGuid, binding.Axis);

                if (rawNullable == null)
                {
                    _axisBindingService.EnsureDeviceOpen(binding.DeviceInstanceGuid);
                    continue;
                }

                int raw = rawNullable.Value;
                if (binding.Invert) raw = 65535 - raw;

                // Eerste keer: sla op als baseline, vuur niets
                if (!_lastRaw.TryGetValue(binding.Id, out int prevRaw))
                {
                    _lastRaw[binding.Id] = raw;
                    continue;
                }

                int delta = raw - prevRaw;
                // Sensitivity 1-10: 1=coarse (large delta needed), 10=sensitive (small delta needed)
                int stepDelta = 6000 - (Math.Clamp(binding.Sensitivity, 1, 10) - 1) * 600; // 6000..600

                if (Math.Abs(delta) < stepDelta) continue;

                // Dead zone: ignore movement near the physical centre of the axis
                int deadZoneUnits = (int)(binding.DeadZone * 65535);
                if (Math.Abs(raw - 32767) < deadZoneUnits) continue;

                // Respect per-binding repeat delay
                double elapsedMs = _lastFireTime.TryGetValue(binding.Id, out var last)
                    ? (now - last).TotalMilliseconds : double.MaxValue;

                if (elapsedMs < binding.RepeatDelayMs) continue;

                int vk = delta > 0 ? binding.KeyUp : binding.KeyDown;
                bool ctrl  = delta > 0 ? binding.KeyUpCtrl  : binding.KeyDownCtrl;
                bool shift = delta > 0 ? binding.KeyUpShift : binding.KeyDownShift;
                bool alt   = delta > 0 ? binding.KeyUpAlt   : binding.KeyDownAlt;
                if (vk == 0) { _lastRaw[binding.Id] = raw; continue; }

                _lastRaw[binding.Id]  = raw;
                _lastFireTime[binding.Id] = now;
                FireKey((ushort)vk, ctrl, shift, alt);
            }
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint KEYEVENTF_KEYUP    = 0x0002;

        // VK naar DIK scancode mapping
        private static readonly Dictionary<int, byte> VkToDik = new()
        {
            // Letters A-Z
            { 0x41, 0x1E }, { 0x42, 0x30 }, { 0x43, 0x2E }, { 0x44, 0x20 },
            { 0x45, 0x12 }, { 0x46, 0x21 }, { 0x47, 0x22 }, { 0x48, 0x23 },
            { 0x49, 0x17 }, { 0x4A, 0x24 }, { 0x4B, 0x25 }, { 0x4C, 0x26 },
            { 0x4D, 0x32 }, { 0x4E, 0x31 }, { 0x4F, 0x18 }, { 0x50, 0x19 },
            { 0x51, 0x10 }, { 0x52, 0x13 }, { 0x53, 0x1F }, { 0x54, 0x14 },
            { 0x55, 0x16 }, { 0x56, 0x2F }, { 0x57, 0x11 }, { 0x58, 0x2D },
            { 0x59, 0x15 }, { 0x5A, 0x2C },
            // Cijfers 0-9
            { 0x30, 0x0B }, { 0x31, 0x02 }, { 0x32, 0x03 }, { 0x33, 0x04 },
            { 0x34, 0x05 }, { 0x35, 0x06 }, { 0x36, 0x07 }, { 0x37, 0x08 },
            { 0x38, 0x09 }, { 0x39, 0x0A },
            // F1-F12
            { 0x70, 0x3B }, { 0x71, 0x3C }, { 0x72, 0x3D }, { 0x73, 0x3E },
            { 0x74, 0x3F }, { 0x75, 0x40 }, { 0x76, 0x41 }, { 0x77, 0x42 },
            { 0x78, 0x43 }, { 0x79, 0x44 }, { 0x7A, 0x57 }, { 0x7B, 0x58 },
            // F13-F22
            { 124, 0x64 }, { 125, 0x65 }, { 126, 0x66 }, { 127, 0x67 },
            { 128, 0x68 }, { 129, 0x69 }, { 130, 0x6A }, { 131, 0x6B },
            { 132, 0x6C }, { 133, 0x6D },
            // Speciale toetsen
            { 0xBF, 0x35 }, // Slash /
            { 0xBE, 0x34 }, // Period .
            { 0xBC, 0x33 }, // Comma ,
            { 0xBD, 0x0C }, // Minus -
            { 0xBB, 0x0D }, // Equals =
            { 0x20, 0x39 }, // Space
            { 0x0D, 0x1C }, // Enter
            { 0x08, 0x0E }, // Backspace
            { 0x09, 0x0F }, // Tab
            { 0x1B, 0x01 }, // Escape
        };

        private const byte DIK_LSHIFT   = 0x2A;
        private const byte DIK_LCONTROL = 0x1D;
        private const byte DIK_LMENU    = 0x38;

        private static void SendDik(byte dik, bool up)
        {
            uint flags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0u);
            keybd_event(0, dik, flags, UIntPtr.Zero);
        }

        private static void FireKey(ushort vk, bool ctrl = false, bool shift = false, bool alt = false)
        {
            if (!VkToDik.TryGetValue(vk, out byte dik)) return;

            if (ctrl)  SendDik(DIK_LCONTROL, false);
            if (shift) SendDik(DIK_LSHIFT,   false);
            if (alt)   SendDik(DIK_LMENU,    false);

            SendDik(dik, false);
            System.Threading.Thread.Sleep(30);
            SendDik(dik, true);

            if (alt)   SendDik(DIK_LMENU,    true);
            if (shift) SendDik(DIK_LSHIFT,   true);
            if (ctrl)  SendDik(DIK_LCONTROL,  true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pollTimer?.Dispose();
        }
    }
}
