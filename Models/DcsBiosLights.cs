namespace BmsLightBridge.Models
{
    /// <summary>
    /// A single DCS-BIOS indicator light export: location in the 16-bit export word array,
    /// plus mask/shift to extract the value. All values generated directly from F-16C_50.json.
    /// </summary>
    public class DcsBiosLight
    {
        public string Name     { get; init; } = "";
        public string Category { get; init; } = "";
        public int Address  { get; init; }
        public int Mask     { get; init; }
        public int ShiftBy  { get; init; }

        public DcsBiosLight(string name, string category, int address, int mask, int shiftBy)
        {
            Name     = name;
            Category = category;
            Address  = address;
            Mask     = mask;
            ShiftBy  = shiftBy;
        }

        public CockpitLight ToCockpitLight() => new(Name, Category, bitField: 0, bitMask: 0);
    }

    /// <summary>
    /// Complete F-16C Viper indicator-light table for DCS-BIOS.
    /// Auto-generated from F-16C_50.json — 130 signals covering all LEDs exported by DCS-BIOS.
    /// </summary>
    public static class DcsF16Lights
    {
        public static IReadOnlyList<DcsBiosLight> All { get; } = new List<DcsBiosLight>()
        {

            // Caution Panel
            new("FLCS Fault", "Caution Panel", 17526, 0x1, 0),  // LIGHT_FLCS_FAULT
            new("Engine Fault", "Caution Panel", 17526, 0x2, 1),  // LIGHT_ENGINE_FAULT
            new("Avionics Fault", "Caution Panel", 17526, 0x4, 2),  // LIGHT_AVIONICS_FAULT
            new("Seat Not", "Caution Panel", 17526, 0x8, 3),  // LIGHT_SEAT_NOT

            // Electronic / EPU
            new("Elec Sys", "Electronic / EPU", 17526, 0x10, 4),  // LIGHT_ELEC_SYS

            // Caution Panel
            new("Sec", "Caution Panel", 17526, 0x20, 5),  // LIGHT_SEC
            new("Equip Hot", "Caution Panel", 17526, 0x40, 6),  // LIGHT_EQUIP_HOT
            new("NWS Fail", "Caution Panel", 17526, 0x80, 7),  // LIGHT_NWS_FAIL
            new("Probe Heat", "Caution Panel", 17526, 0x100, 8),  // LIGHT_PROBE_HEAT
            new("Fuel Oil Hot", "Caution Panel", 17526, 0x200, 9),  // LIGHT_FUEL_OIL_HOT
            new("Radar Alt", "Caution Panel", 17526, 0x400, 10),  // LIGHT_RADAR_ALT
            new("Anti Skid", "Caution Panel", 17526, 0x800, 11),  // LIGHT_ANTI_SKID
            new("CADC", "Caution Panel", 17526, 0x1000, 12),  // LIGHT_CADC
            new("Inlet Icing", "Caution Panel", 17526, 0x2000, 13),  // LIGHT_INLET_ICING
            new("Iff", "Caution Panel", 17526, 0x4000, 14),  // LIGHT_IFF
            new("Hook", "Caution Panel", 17526, 0x8000, 15),  // LIGHT_HOOK
            new("Stores Config", "Caution Panel", 17528, 0x1, 0),  // LIGHT_STORES_CONFIG
            new("Overheat", "Caution Panel", 17528, 0x2, 1),  // LIGHT_OVERHEAT
            new("Nuclear", "Caution Panel", 17528, 0x4, 2),  // LIGHT_NUCLEAR
            new("OBOGS", "Caution Panel", 17528, 0x8, 3),  // LIGHT_OBOGS
            new("ATF Not", "Caution Panel", 17528, 0x10, 4),  // LIGHT_ATF_NOT
            new("EEC", "Caution Panel", 17528, 0x20, 5),  // LIGHT_EEC
            new("Caution 1", "Caution Panel", 17528, 0x40, 6),  // LIGHT_CAUTION_1
            new("Cabin Press", "Caution Panel", 17528, 0x80, 7),  // LIGHT_CABIN_PRESS
            new("FWD Fuel Low", "Caution Panel", 17528, 0x100, 8),  // LIGHT_FWD_FUEL_LOW
            new("BUC", "Caution Panel", 17528, 0x200, 9),  // LIGHT_BUC
            new("Caution 2", "Caution Panel", 17528, 0x400, 10),  // LIGHT_CAUTION_2
            new("Caution 3", "Caution Panel", 17528, 0x800, 11),  // LIGHT_CAUTION_3
            new("AFT Fuel Low", "Caution Panel", 17528, 0x1000, 12),  // LIGHT_AFT_FUEL_LOW
            new("Caution 4", "Caution Panel", 17528, 0x2000, 13),  // LIGHT_CAUTION_4
            new("Caution 5", "Caution Panel", 17528, 0x4000, 14),  // LIGHT_CAUTION_5
            new("Caution 6", "Caution Panel", 17528, 0x8000, 15),  // LIGHT_CAUTION_6

            // Eyebrow
            new("Master Caution", "Eyebrow", 17530, 0x1, 0),  // LIGHT_MASTER_CAUTION
            new("Edge", "Eyebrow", 17530, 0x2, 1),  // LIGHT_EDGE
            new("TF Fail", "Eyebrow", 17530, 0x4, 2),  // LIGHT_TF_FAIL
            new("ENG Fire", "Eyebrow", 17530, 0x8, 3),  // LIGHT_ENG_FIRE
            new("Engine", "Eyebrow", 17530, 0x10, 4),  // LIGHT_ENGINE
            new("HYD Oil Press", "Eyebrow", 17530, 0x20, 5),  // LIGHT_HYD_OIL_PRESS
            new("Flcs", "Eyebrow", 17530, 0x40, 6),  // LIGHT_FLCS
            new("DBU On", "Eyebrow", 17530, 0x80, 7),  // LIGHT_DBU_ON
            new("To Ldg Config", "Eyebrow", 17530, 0x100, 8),  // LIGHT_TO_LDG_CONFIG
            new("Canopy", "Eyebrow", 17530, 0x200, 9),  // LIGHT_CANOPY
            new("OXY Low", "Eyebrow", 17530, 0x400, 10),  // LIGHT_OXY_LOW

            // AOA Indexer
            new("AoA Up", "AOA Indexer", 17530, 0x800, 11),  // LIGHT_AOA_UP
            new("AoA Mid", "AOA Indexer", 17530, 0x1000, 12),  // LIGHT_AOA_MID
            new("AoA Dn", "AOA Indexer", 17530, 0x2000, 13),  // LIGHT_AOA_DN

            // Landing Gear
            new("Gear N", "Landing Gear", 17530, 0x4000, 14),  // LIGHT_GEAR_N
            new("Gear L", "Landing Gear", 17530, 0x8000, 15),  // LIGHT_GEAR_L
            new("Gear R", "Landing Gear", 17532, 0x1, 0),  // LIGHT_GEAR_R
            new("Gear Warn", "Landing Gear", 17532, 0x2, 1),  // LIGHT_GEAR_WARN

            // Landing / AR
            new("RDY", "Landing / AR", 17532, 0x4, 2),  // LIGHT_RDY
            new("AR NWS", "Landing / AR", 17532, 0x8, 3),  // LIGHT_AR_NWS
            new("Disc", "Landing / AR", 17532, 0x10, 4),  // LIGHT_DISC

            // Electronic / EPU
            new("JFS Run", "Electronic / EPU", 17532, 0x20, 5),  // LIGHT_JFS_RUN
            new("Hydrazn", "Electronic / EPU", 17532, 0x40, 6),  // LIGHT_HYDRAZN
            new("Air", "Electronic / EPU", 17532, 0x80, 7),  // LIGHT_AIR
            new("Epu", "Electronic / EPU", 17532, 0x100, 8),  // LIGHT_EPU
            new("FLCS PMG", "Electronic / EPU", 17532, 0x200, 9),  // LIGHT_FLCS_PMG
            new("Main Gen", "Electronic / EPU", 17532, 0x400, 10),  // LIGHT_MAIN_GEN
            new("Stby Gen", "Electronic / EPU", 17532, 0x800, 11),  // LIGHT_STBY_GEN
            new("Elec", "Electronic / EPU", 17532, 0x1000, 12),  // LIGHT_ELEC
            new("EPU Gen", "Electronic / EPU", 17532, 0x2000, 13),  // LIGHT_EPU_GEN
            new("EPU PMG", "Electronic / EPU", 17532, 0x4000, 14),  // LIGHT_EPU_PMG
            new("To Flcs", "Electronic / EPU", 17532, 0x8000, 15),  // LIGHT_TO_FLCS
            new("FLCS RLY", "Electronic / EPU", 17534, 0x1, 0),  // LIGHT_FLCS_RLY
            new("ACFT Batt Fail", "Electronic / EPU", 17534, 0x2, 1),  // LIGHT_ACFT_BATT_FAIL
            new("Active", "Electronic / EPU", 17534, 0x4, 2),  // LIGHT_ACTIVE
            new("Stby", "Electronic / EPU", 17534, 0x8, 3),  // LIGHT_STBY
            new("Fl Run", "Electronic / EPU", 17534, 0x10, 4),  // LIGHT_FL_RUN
            new("Fl Fail", "Electronic / EPU", 17534, 0x20, 5),  // LIGHT_FL_FAIL
            new("FLCS Pwr A", "Electronic / EPU", 17534, 0x40, 6),  // LIGHT_FLCS_PWR_A
            new("FLCS Pwr B", "Electronic / EPU", 17534, 0x80, 7),  // LIGHT_FLCS_PWR_B
            new("FLCS Pwr C", "Electronic / EPU", 17534, 0x100, 8),  // LIGHT_FLCS_PWR_C
            new("FLCS Pwr D", "Electronic / EPU", 17534, 0x200, 9),  // LIGHT_FLCS_PWR_D

            // EWS / RWR
            new("RWR Search", "EWS / RWR", 17534, 0x400, 10),  // LIGHT_RWR_SEARCH
            new("RWR Activity", "EWS / RWR", 17534, 0x800, 11),  // LIGHT_RWR_ACTIVITY
            new("RWR Act Power", "EWS / RWR", 17534, 0x1000, 12),  // LIGHT_RWR_ACT_POWER
            new("RWR Alt Low", "EWS / RWR", 17534, 0x2000, 13),  // LIGHT_RWR_ALT_LOW
            new("RWR Alt", "EWS / RWR", 17534, 0x4000, 14),  // LIGHT_RWR_ALT
            new("RWR Power", "EWS / RWR", 17534, 0x8000, 15),  // LIGHT_RWR_POWER
            new("RWR Handoff Up", "EWS / RWR", 17536, 0x1, 0),  // LIGHT_RWR_HANDOFF_UP
            new("RWR Handoff H", "EWS / RWR", 17536, 0x2, 1),  // LIGHT_RWR_HANDOFF_H
            new("RWR MSL Launch", "EWS / RWR", 17536, 0x4, 2),  // LIGHT_RWR_MSL_LAUNCH
            new("RWR Mode Pri", "EWS / RWR", 17536, 0x8, 3),  // LIGHT_RWR_MODE_PRI
            new("RWR Mode Open", "EWS / RWR", 17536, 0x10, 4),  // LIGHT_RWR_MODE_OPEN
            new("RWR Ship Unk", "EWS / RWR", 17536, 0x20, 5),  // LIGHT_RWR_SHIP_UNK
            new("RWR Systest", "EWS / RWR", 17536, 0x40, 6),  // LIGHT_RWR_SYSTEST
            new("RWR TGTsep Up", "EWS / RWR", 17536, 0x80, 7),  // LIGHT_RWR_TGTSEP_UP
            new("RWR TGTsep Dn", "EWS / RWR", 17536, 0x100, 8),  // LIGHT_RWR_TGTSEP_DN
            new("CMDS No Go", "EWS / RWR", 17536, 0x200, 9),  // LIGHT_CMDS_NO_GO
            new("CMDS Go", "EWS / RWR", 17536, 0x400, 10),  // LIGHT_CMDS_GO
            new("CMDS Disp", "EWS / RWR", 17536, 0x800, 11),  // LIGHT_CMDS_DISP
            new("CMDS RDY", "EWS / RWR", 17536, 0x1000, 12),  // LIGHT_CMDS_RDY

            // ECM Panel
            new("ECM 1 S", "ECM Panel", 17536, 0x2000, 13),  // LIGHT_ECM_1_S
            new("ECM 1 A", "ECM Panel", 17536, 0x4000, 14),  // LIGHT_ECM_1_A
            new("ECM 1 F", "ECM Panel", 17536, 0x8000, 15),  // LIGHT_ECM_1_F
            new("ECM 1 T", "ECM Panel", 17546, 0x1, 0),  // LIGHT_ECM_1_T
            new("ECM 2 S", "ECM Panel", 17546, 0x2, 1),  // LIGHT_ECM_2_S
            new("ECM 2 A", "ECM Panel", 17546, 0x4, 2),  // LIGHT_ECM_2_A
            new("ECM 2 F", "ECM Panel", 17546, 0x8, 3),  // LIGHT_ECM_2_F
            new("ECM 2 T", "ECM Panel", 17546, 0x10, 4),  // LIGHT_ECM_2_T
            new("ECM 3 S", "ECM Panel", 17546, 0x20, 5),  // LIGHT_ECM_3_S
            new("ECM 3 A", "ECM Panel", 17546, 0x40, 6),  // LIGHT_ECM_3_A
            new("ECM 3 F", "ECM Panel", 17546, 0x80, 7),  // LIGHT_ECM_3_F
            new("ECM 3 T", "ECM Panel", 17546, 0x100, 8),  // LIGHT_ECM_3_T
            new("ECM 4 S", "ECM Panel", 17546, 0x200, 9),  // LIGHT_ECM_4_S
            new("ECM 4 A", "ECM Panel", 17546, 0x400, 10),  // LIGHT_ECM_4_A
            new("ECM 4 F", "ECM Panel", 17546, 0x800, 11),  // LIGHT_ECM_4_F
            new("ECM 4 T", "ECM Panel", 17546, 0x1000, 12),  // LIGHT_ECM_4_T
            new("ECM 5 S", "ECM Panel", 17546, 0x2000, 13),  // LIGHT_ECM_5_S
            new("ECM 5 A", "ECM Panel", 17546, 0x4000, 14),  // LIGHT_ECM_5_A
            new("ECM 5 F", "ECM Panel", 17546, 0x8000, 15),  // LIGHT_ECM_5_F
            new("ECM 5 T", "ECM Panel", 17548, 0x1, 0),  // LIGHT_ECM_5_T
            new("ECM S", "ECM Panel", 17548, 0x2, 1),  // LIGHT_ECM_S
            new("ECM A", "ECM Panel", 17548, 0x4, 2),  // LIGHT_ECM_A
            new("ECM F", "ECM Panel", 17548, 0x8, 3),  // LIGHT_ECM_F
            new("ECM T", "ECM Panel", 17548, 0x10, 4),  // LIGHT_ECM_T
            new("ECM FRM S", "ECM Panel", 17548, 0x20, 5),  // LIGHT_ECM_FRM_S
            new("ECM FRM A", "ECM Panel", 17548, 0x40, 6),  // LIGHT_ECM_FRM_A
            new("ECM FRM F", "ECM Panel", 17548, 0x80, 7),  // LIGHT_ECM_FRM_F
            new("ECM FRM T", "ECM Panel", 17548, 0x100, 8),  // LIGHT_ECM_FRM_T
            new("ECM SPL S", "ECM Panel", 17548, 0x200, 9),  // LIGHT_ECM_SPL_S
            new("ECM SPL A", "ECM Panel", 17548, 0x400, 10),  // LIGHT_ECM_SPL_A
            new("ECM SPL F", "ECM Panel", 17548, 0x800, 11),  // LIGHT_ECM_SPL_F
            new("ECM SPL T", "ECM Panel", 17548, 0x1000, 12),  // LIGHT_ECM_SPL_T

            // Navigation
            new("Marker Beacon", "Navigation", 17548, 0x2000, 13),  // LIGHT_MARKER_BEACON

            // Oxygen System
            new("Flow Indicator Light", "Oxygen System", 17548, 0x4000, 14),  // FLOW_INDICATOR_LIGHT

            // ECM Panel
            new("Ecm", "ECM Panel", 17732, 0x4000, 14),  // LIGHT_ECM

            // EWS / RWR
            new("RWR Ship U", "EWS / RWR", 17762, 0x8000, 15),  // LIGHT_RWR_SHIP_U
            new("RWR Systest On", "EWS / RWR", 17792, 0x100, 8),  // LIGHT_RWR_SYSTEST_ON
        };

        public static IReadOnlyList<string> Categories { get; } =
            All.Select(l => l.Category).Distinct().ToList();
    }
}