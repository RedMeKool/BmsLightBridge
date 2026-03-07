namespace BmsLightBridge.Models
{
    public static class BmsSharedMemoryNames
    {
        public const string FlightData = "FalconSharedMemoryArea";
    }

    public class CockpitLight
    {
        public string Name     { get; set; } = "";
        public string Category { get; set; } = "";
        public int    BitField { get; set; } = 1;
        public uint   BitMask  { get; set; }

        public CockpitLight(string name, string category, int bitField, uint bitMask)
        {
            Name     = name;
            Category = category;
            BitField = bitField;
            BitMask  = bitMask;
        }
    }

    public static class BmsLights
    {
        public static List<CockpitLight> All { get; } = new()
        {
            // === LIGHTBITS (BitField = 1) ===
            new("Master Caution",        "Eyebrow",       1, 0x00000001),
            new("TF",                    "Eyebrow",       1, 0x00000002),
            new("OXY LOW",               "Eyebrow",       1, 0x00000004),
            new("EQUIP HOT",             "Eyebrow",       1, 0x00000008),
            new("ENG FIRE",              "Eyebrow",       1, 0x00000020),
            new("CONFIG",                "Caution Panel", 1, 0x00000040),
            new("HYD OIL PRESS",         "Eyebrow",       1, 0x00000080),
            new("FLCS ABCD",             "Test Panel",    1, 0x00000100),
            new("FLCS",                  "Eyebrow",       1, 0x00000200),
            new("CAN",                   "Eyebrow",       1, 0x00000400),
            new("T/L CFG",               "Eyebrow",       1, 0x00000800),
            new("AOA Above",             "AOA Indexer",   1, 0x00001000),
            new("AOA On",                "AOA Indexer",   1, 0x00002000),
            new("AOA Below",             "AOA Indexer",   1, 0x00004000),
            new("Refuel RDY",            "Refuel",        1, 0x00008000),
            new("Refuel AR",             "Refuel",        1, 0x00010000),
            new("Refuel DSC",            "Refuel",        1, 0x00020000),
            new("FLT CONTROL SYS",       "Caution Panel", 1, 0x00040000),
            new("LE FLAPS",              "Caution Panel", 1, 0x00080000),
            new("ENGINE FAULT",          "Caution Panel", 1, 0x00100000),
            new("OVERHEAT",              "Caution Panel", 1, 0x00200000),
            new("FUEL LOW",              "Caution Panel", 1, 0x00400000),
            new("AVIONICS",              "Caution Panel", 1, 0x00800000),
            new("RADAR ALT",             "Caution Panel", 1, 0x01000000),
            new("IFF",                   "Caution Panel", 1, 0x02000000),
            new("ECM",                   "Caution Panel", 1, 0x04000000),
            new("HOOK",                  "Caution Panel", 1, 0x08000000),
            new("NWS FAIL",              "Caution Panel", 1, 0x10000000),
            new("CABIN PRESS",           "Caution Panel", 1, 0x20000000),
            new("TFR STBY",              "MISC Panel",    1, 0x80000000),

            // === LIGHTBITS2 (BitField = 2) ===
            new("TWP HandOff",           "Threat Warning",2, 0x00000001),
            new("TWP Launch",            "Threat Warning",2, 0x00000002),
            new("TWP PriMode",           "Threat Warning",2, 0x00000004),
            new("TWP Naval",             "Threat Warning",2, 0x00000008),
            new("TWP Unk",               "Threat Warning",2, 0x00000010),
            new("TWP TgtSep",            "Threat Warning",2, 0x00000020),
            new("EWS Go",                "EWS",           2, 0x00000040),
            new("EWS NoGo",              "EWS",           2, 0x00000080),
            new("EWS Degr",              "EWS",           2, 0x00000100),
            new("EWS Rdy",               "EWS",           2, 0x00000200),
            new("Chaff Lo",              "EWS",           2, 0x00000400),
            new("Flare Lo",              "EWS",           2, 0x00000800),
            new("AUX Search",            "Threat Warning",2, 0x00001000),
            new("AUX Act",               "Threat Warning",2, 0x00002000),
            new("AUX Low",               "Threat Warning",2, 0x00004000),
            new("AUX Pwr",               "Threat Warning",2, 0x00008000),
            new("ECM Power",             "ECM",           2, 0x00010000),
            new("ECM Fail",              "ECM",           2, 0x00020000),
            new("Fwd Fuel Low",          "Fuel",          2, 0x00040000),
            new("Aft Fuel Low",          "Fuel",          2, 0x00080000),
            new("EPU On",                "EPU Panel",     2, 0x00100000),
            new("JFS Run",               "Engine",        2, 0x00200000),
            new("SEC",                   "Caution Panel", 2, 0x00400000),
            new("OXY LOW (caution)",     "Caution Panel", 2, 0x00800000),
            new("PROBE HEAT",            "Caution Panel", 2, 0x01000000),
            new("SEAT ARM",              "Caution Panel", 2, 0x02000000),
            new("BUC",                   "Caution Panel", 2, 0x04000000),
            new("FUEL OIL HOT",          "Caution Panel", 2, 0x08000000),
            new("ANTI SKID",             "Caution Panel", 2, 0x10000000),
            new("TFR ENGAGED",           "MISC Panel",    2, 0x20000000),
            new("GEAR HANDLE",           "Landing Gear",  2, 0x40000000),
            new("ENGINE",                "Eyebrow",       2, 0x80000000),

            // === LIGHTBITS3 (BitField = 3) ===
            new("FLCS PMG",              "Electronic",    3, 0x00000001),
            new("Main Gen",              "Electronic",    3, 0x00000002),
            new("Stby Gen",              "Electronic",    3, 0x00000004),
            new("EPU Gen",               "Electronic",    3, 0x00000008),
            new("EPU PMG",               "Electronic",    3, 0x00000010),
            new("To FLCS",               "Electronic",    3, 0x00000020),
            new("FLCS RLY",              "Electronic",    3, 0x00000040),
            new("Bat Fail",              "Electronic",    3, 0x00000080),
            new("Hydrazine",             "EPU Panel",     3, 0x00000100),
            new("Air",                   "EPU Panel",     3, 0x00000200),
            new("Elec Fault",            "Caution Panel", 3, 0x00000400),
            new("LEF Fault",             "Caution Panel", 3, 0x00000800),
            new("On Ground",             "Status",        3, 0x00001000),
            new("FLT CONTROL RUN",       "FLT Control",   3, 0x00002000),
            new("FLT CONTROL FAIL",      "FLT Control",   3, 0x00004000),
            new("DBU Warn",              "Eyebrow",       3, 0x00008000),
            new("Nose Gear Down",        "Landing Gear",  3, 0x00010000),
            new("Left Gear Down",        "Landing Gear",  3, 0x00020000),
            new("Right Gear Down",       "Landing Gear",  3, 0x00040000),
            new("Park Brake On",         "Status",        3, 0x00100000),
            new("Power Off",             "Status",        3, 0x00200000),
            new("CADC",                  "Caution Panel", 3, 0x00400000),
            new("Speed Brake",           "Status",        3, 0x00800000),
            new("SysTest",               "Threat Warning",3, 0x01000000),
            new("MC Announced",          "Status",        3, 0x02000000),
            new("MLGWOW",                "Landing Gear",  3, 0x04000000),
            new("NLGWOW",                "Landing Gear",  3, 0x08000000),
            new("ATF Not Engaged",       "Status",        3, 0x10000000),
            new("Inlet Icing",           "Caution Panel", 3, 0x20000000),
        };

        public static IEnumerable<string> Categories => All.Select(l => l.Category).Distinct();
    }
}
