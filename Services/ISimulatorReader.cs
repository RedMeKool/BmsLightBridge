namespace BmsLightBridge.Services
{
    /// <summary>
    /// Generic, simulator-agnostic snapshot of cockpit state.
    /// LightStates is keyed by the same names used in SignalMapping (e.g. "Master Caution").
    /// RawBuffer is only populated by readers that support raw DED rendering (currently BMS);
    /// other readers leave it empty.
    /// </summary>
    public class CockpitStateChangedEventArgs : EventArgs
    {
        /// <summary>Named light/switch states, e.g. "Master Caution" -> true.</summary>
        public Dictionary<string, bool> LightStates { get; init; } = new();

        /// <summary>True when at least one value in LightStates changed since the previous tick.</summary>
        public bool HasChanged { get; init; }

        /// <summary>Raw shared-memory bytes for DED rendering (BMS only). Empty for other simulators.</summary>
        public byte[] RawBuffer { get; init; } = Array.Empty<byte>();

        /// <summary>
        /// DED text lines 1-5, 24 chars each (DCS-BIOS "DED Display (New)" DED_L1..DED_L5).
        /// Null for readers that don't support DED via this path (BMS uses RawBuffer instead).
        /// </summary>
        public string[]? DedLines { get; init; }

        /// <summary>
        /// DED format lines 1-5, 24 chars each, matching DedLines. Each character is
        /// 'i' (inverse), 'b' (big text) or ' ' (normal) — per DCS-BIOS DED_Lx_FORMAT.
        /// Null for readers that don't support DED via this path.
        /// </summary>
        public string[]? DedFormat { get; init; }
    }

    /// <summary>
    /// Common interface for simulator data sources (BMS shared memory, DCS-BIOS, ...).
    /// SyncService talks to readers only through this interface, so the mapping/output
    /// pipeline (Arduino, WinWing, axis bindings) remains identical regardless of simulator.
    /// </summary>
    public interface ISimulatorReader : IDisposable
    {
        event EventHandler<CockpitStateChangedEventArgs>? StateChanged;
        event EventHandler<bool>?                          ConnectionChanged;

        bool IsConnected { get; }
        bool IsRunning   { get; }

        void Start(int intervalMs = 500);
        void Stop();
        void ChangeInterval(int intervalMs);

        /// <summary>Immediately fires StateChanged with the last known values, if connected.</summary>
        void ForceUpdate();
    }
}
