using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Timers;
using BmsLightBridge.Models;

namespace BmsLightBridge.Services
{
    public class LightsChangedEventArgs : EventArgs
    {
        public uint   LightBits  { get; init; }
        public uint   LightBits2 { get; init; }
        public uint   LightBits3 { get; init; }
        /// <summary>True when at least one LightBits value has changed since the previous tick.</summary>
        public bool   HasLightBitsChanged { get; init; }
        /// <summary>
        /// Raw copy of the shared memory bytes needed for DED rendering (offsets 0–511).
        /// Supplied so IcpService can share this read rather than opening a second MMF handle.
        /// </summary>
        public byte[] RawBuffer  { get; init; } = Array.Empty<byte>();
    }

    public class BmsSharedMemoryReader : IDisposable
    {
        // Byte offsets in FalconSharedMemoryArea — verified against FlightData.h
        // and confirmed via live memory dump (MasterCaution toggle at offset 108).
        public const int OFFSET_LIGHTBITS  = 108;
        public const int OFFSET_LIGHTBITS2 = 124;
        public const int OFFSET_LIGHTBITS3 = 128;

        private const int READ_SIZE = 1024;

        // Process check runs at most once every 2 seconds.
        // This prevents hammering NtQuerySystemInformation during BMS startup,
        // which was the root cause of BMS crashing when BmsLightBridge was already running.
        private const int PROCESS_CHECK_INTERVAL_MS = 2000;

        public event EventHandler<LightsChangedEventArgs>? LightsChanged;
        public event EventHandler<bool>?                   ConnectionChanged;

        public bool IsConnected { get; private set; }
        public bool IsRunning   { get; private set; }

        private System.Timers.Timer?        _pollTimer;
        private MemoryMappedFile?           _mmf;
        // Accessor and read-buffer are reused across polls to avoid per-poll heap allocations.
        private MemoryMappedViewAccessor?   _accessor;
        private readonly byte[]             _readBuffer = new byte[READ_SIZE];
        private uint     _lastBits1, _lastBits2, _lastBits3;
        private DateTime _lastProcessCheck = DateTime.MinValue;
        private readonly object _lock = new();

        private static readonly string[] BmsProcessNames =
            { "Falcon BMS", "Falcon4", "Falcon BMS 4", "FalconBMS" };

        public void Start(int intervalMs = 500)
        {
            if (IsRunning) return;
            IsRunning  = true;
            _pollTimer = new System.Timers.Timer(intervalMs) { AutoReset = true };
            _pollTimer.Elapsed += OnTimerElapsed;
            _pollTimer.Start();
        }

        /// <summary>
        /// Fully stops the poll timer and closes the shared memory mapping.
        /// Note: SyncService intentionally does NOT call Stop() when sync ends — it only
        /// calls ChangeInterval(500) so the connection indicator keeps working.
        /// Stop() is therefore used only from Dispose().
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
            _pollTimer = null;
            CloseMmf();
            IsRunning = false;

            if (IsConnected)
            {
                IsConnected = false;
                ConnectionChanged?.Invoke(this, false);
            }
        }

        /// <summary>
        /// Immediately fires LightsChanged with the last known values.
        /// Call this after starting sync to push current state to devices right away.
        /// </summary>
        public void ForceUpdate()
        {
            if (!IsConnected) return;
            LightsChanged?.Invoke(this, new LightsChangedEventArgs
            {
                LightBits           = _lastBits1,
                LightBits2          = _lastBits2,
                LightBits3          = _lastBits3,
                HasLightBitsChanged = true,
                RawBuffer           = _readBuffer[.._readBuffer.Length],
            });
        }

        public void ChangeInterval(int intervalMs)
        {
            if (_pollTimer != null)
                _pollTimer.Interval = intervalMs;
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            lock (_lock) { TryReadBms(); }
        }

        private void TryReadBms()
        {
            // If not yet connected: require the BMS process to be running before
            // attempting OpenExisting. This prevents oscillation caused by Windows
            // keeping the MMF alive briefly after BMS closes.
            // The process check is throttled to avoid hammering NtQuerySystemInformation.
            if (!IsConnected)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastProcessCheck).TotalMilliseconds < PROCESS_CHECK_INTERVAL_MS)
                    return;
                _lastProcessCheck = now;
                if (!IsBmsProcessRunning())
                    return;
            }

            // If connected: periodically verify BMS is still running.
            if (_mmf != null && IsConnected)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastProcessCheck).TotalMilliseconds >= PROCESS_CHECK_INTERVAL_MS)
                {
                    _lastProcessCheck = now;
                    if (!IsBmsProcessRunning())
                    {
                        CloseMmf();
                        IsConnected = false;
                        ConnectionChanged?.Invoke(this, false);
                        return;
                    }
                }
            }

            try
            {
                // Open MMF and create the accessor once; reuse on subsequent polls
                // to avoid a ViewAccessor allocation (and disposal) on every tick.
                if (_mmf == null)
                {
                    _mmf      = MemoryMappedFile.OpenExisting(BmsSharedMemoryNames.FlightData);
                    _accessor = _mmf.CreateViewAccessor(0, READ_SIZE, MemoryMappedFileAccess.Read);
                }

                // Reuse the pre-allocated _readBuffer — no heap allocation per poll.
                _accessor!.ReadArray(0, _readBuffer, 0, READ_SIZE);

                uint lb1 = MemoryMarshal.Read<uint>(_readBuffer.AsSpan(OFFSET_LIGHTBITS));
                uint lb2 = MemoryMarshal.Read<uint>(_readBuffer.AsSpan(OFFSET_LIGHTBITS2));
                uint lb3 = MemoryMarshal.Read<uint>(_readBuffer.AsSpan(OFFSET_LIGHTBITS3));

                if (!IsConnected)
                {
                    IsConnected       = true;
                    _lastProcessCheck = DateTime.UtcNow;
                    ConnectionChanged?.Invoke(this, true);
                }

                bool bitsChanged = lb1 != _lastBits1 || lb2 != _lastBits2 || lb3 != _lastBits3;
                if (bitsChanged)
                {
                    _lastBits1 = lb1;
                    _lastBits2 = lb2;
                    _lastBits3 = lb3;
                }

                // Always fire so IcpService receives the raw buffer for DED rendering.
                // HasLightBitsChanged lets SyncService skip ProcessMappings when only DED changed.
                LightsChanged?.Invoke(this, new LightsChangedEventArgs
                {
                    LightBits         = lb1,
                    LightBits2        = lb2,
                    LightBits3        = lb3,
                    HasLightBitsChanged = bitsChanged,
                    RawBuffer         = _readBuffer[..READ_SIZE],
                });
            }
            catch (FileNotFoundException)
            {
                // Shared memory not yet created — BMS not running.
                CloseMmf();
                if (IsConnected)
                {
                    IsConnected = false;
                    ConnectionChanged?.Invoke(this, false);
                }
            }
            catch (Exception ex) when (ex is not ObjectDisposedException)
            {
                // Unexpected read failure (e.g. MMF closed by OS) — close and retry next tick.
                CloseMmf();
            }
        }

        private static bool IsBmsProcessRunning()
        {
            foreach (var name in BmsProcessNames)
            {
                var procs = Process.GetProcessesByName(name);
                bool found = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                if (found) return true;
            }
            return false;
        }

        private void CloseMmf()
        {
            _accessor?.Dispose();
            _accessor = null;
            _mmf?.Dispose();
            _mmf = null;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
