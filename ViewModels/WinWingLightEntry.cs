using BmsLightBridge.Models;

namespace BmsLightBridge.ViewModels
{
    /// <summary>
    /// A single mappable light on a WinWing device, with its index and human-readable name.
    /// </summary>
    public class WinWingLightEntry
    {
        // ----------------------------------------------------------------
        // Per-device mappable lights (shown in mapping dropdown)
        // Brightness-only channels are NOT listed here.
        // ----------------------------------------------------------------
        private static readonly Dictionary<ushort, Dictionary<int, string>> MappableLights
            = new Dictionary<ushort, Dictionary<int, string>>
        {
            // CarrierAce PTO 2 (0xBF05)
            {
                0xBF05, new Dictionary<int, string>
                {
                    { 1,  "LANDING GEAR HANDLE" },  // brightness channel used as on/off
                    { 4,  "MASTER CAUTION" },
                    { 5,  "JETT" },
                    { 6,  "CTR" },
                    { 7,  "LI" },
                    { 8,  "LO" },
                    { 9,  "RO" },
                    { 10, "RI" },
                    { 11, "FLAPS" },
                    { 12, "NOSE" },
                    { 13, "FULL" },
                    { 14, "RIGHT" },
                    { 15, "LEFT" },
                    { 16, "HALF" },
                    { 17, "HOOK" },
                }
            },

            // Orion Throttle Base II + F16 Grip (0xBE68)
            // Protocol: 02 60 BE 00 00 03 49 [index] [01=on/00=off] 00 00 00 00 00
            // Confirmed via USB capture: index 1 = A/A, index 2 = A/G, index 0 = Backlight (brightness)
            {
                0xBE68, new Dictionary<int, string>
                {
                    { 1, "A/A" },
                    { 2, "A/G" },
                }
            },

            // ViperAce ICP (0xBF06) — brightness channels only, no mappable on/off lights
            { 0xBF06, new Dictionary<int, string>() },

            // CarrierAce UFC + HUD (0xBEDE) — brightness channels only
            { 0xBEDE, new Dictionary<int, string>() },

            // CarrierAce MFD L (0xBEE1) — brightness channel only
            { 0xBEE1, new Dictionary<int, string>() },

            // CarrierAce MFD C (0xBEE0) — brightness channel only
            { 0xBEE0, new Dictionary<int, string>() },

            // CarrierAce MFD R (0xBEE2) — brightness channel only
            { 0xBEE2, new Dictionary<int, string>() },
        };

        // ----------------------------------------------------------------
        // Device groups — delegates to WinWingDeviceGroups in Models
        // ----------------------------------------------------------------
        public static string? GetGroupName(ushort pid)
            => WinWingDeviceGroups.GetGroupName(pid);

        public static IEnumerable<ushort> GetGroupPids(ushort pid)
            => WinWingDeviceGroups.GetGroupPids(pid);

        // ----------------------------------------------------------------
        // Brightness slider channels per device (shown in Brightness tab)
        // ----------------------------------------------------------------
        public static readonly Dictionary<ushort, List<(int Index, string Label)>> BrightnessSliderChannels
            = new Dictionary<ushort, List<(int Index, string Label)>>
        {
            { 0xBF05, new List<(int, string)> { (0, "Backlight"), (2, "SL"), (3, "Flag") } },
            { 0xBE68, new List<(int, string)> { (0, "Backlight") } },
            { 0xBF06, new List<(int, string)> { (0, "Backlight"), (1, "Screen Backlight") } },
            { 0xBEDE, new List<(int, string)> { (0, "Backlight UFC"), (1, "LCD Backlight"), (2, "Backlight HUD") } },
            { 0xBEE1, new List<(int, string)> { (0, "Backlight") } },
            { 0xBEE0, new List<(int, string)> { (0, "Backlight") } },
            { 0xBEE2, new List<(int, string)> { (0, "Backlight") } },
        };

        // ----------------------------------------------------------------
        // Indices that use SetBrightness(255/0) instead of SetLight(true/false)
        // ----------------------------------------------------------------
        private static readonly Dictionary<ushort, HashSet<int>> BrightnessChannelIndices
            = new Dictionary<ushort, HashSet<int>>
        {
            { 0xBF05, new HashSet<int> { 0, 1, 2, 3 } },
            { 0xBE68, new HashSet<int> { 0 } },
            { 0xBF06, new HashSet<int> { 0, 1 } },
            { 0xBEDE, new HashSet<int> { 0, 1, 2 } },
            { 0xBEE1, new HashSet<int> { 0 } },
            { 0xBEE0, new HashSet<int> { 0 } },
            { 0xBEE2, new HashSet<int> { 0 } },
        };

        public int    Index { get; }
        public string Name  { get; }

        public WinWingLightEntry(int index, ushort productId = 0)
        {
            Index = index;
            if (productId != 0
                && MappableLights.TryGetValue(productId, out var names)
                && names.TryGetValue(index, out var name))
                Name = name;
            else
                Name = $"LED {index}";
        }

        public override string ToString() => Name;

        /// <summary>Returns all mappable light entries for a device.</summary>
        public static List<WinWingLightEntry> GetLightsForDevice(ushort productId)
        {
            if (!MappableLights.TryGetValue(productId, out var names))
                return new List<WinWingLightEntry>();

            return names.Keys
                .OrderBy(i => i)
                .Select(i => new WinWingLightEntry(i, productId))
                .ToList();
        }

        /// <summary>Returns true if this index uses SetBrightness instead of SetLight.</summary>
        public static bool IsBrightnessChannel(ushort productId, int lightIndex)
            => BrightnessChannelIndices.TryGetValue(productId, out var set) && set.Contains(lightIndex);
    }
}
