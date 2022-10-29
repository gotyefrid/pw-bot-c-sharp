using System.Collections.Generic;

namespace BotCH.MemoryHelpers
{
    public class Offset
    {
        public const uint GAMEADDR_OFFSET = 0x1C;
        public const uint UNZREEFE_OFFSET = 0x4E8;

        public const uint PERS_STRUCT_OFFSET = 0x28;
        public const uint PERS_FLAG_SKILL_OFFSET = 0xB8;
        public const uint PERS_WID_OFFSET = 0x4BC;
        public const uint PERS_HP_OFFSET = 0x4D0;
        public const uint PERS_MP_OFFSET = 0x4D4;
        public const uint PERS_MAX_HP_OFFSET = 0x534;
        public const uint PERS_TARGETID_OFFSET = 0x5B8;
        public const uint PERS_COOLDOWN_FEED_PET_OFFSET = 0xBEC;
        public const uint PERS_COOLDOWN_POT_HP_OFFSET = 0xBB4;
        public const uint PERS_LOC_X = 0x3C;
        public const uint PERS_LOC_Z = 0x40;
        public const uint PERS_LOC_Y = 0x44;

        public const uint PET_STRUCT_OFFSET = 0x1428;
        public static Dictionary<int, uint> PET_CAGES_ARRAY = new Dictionary<int, uint>
        {
            { 1, 0x10 },
            { 2, 0x14 },
            { 3, 0x18 },
            { 4, 0x1C },
            { 5, 0x20 },
        };
        public const uint PET_CURRENT_PETID_OFFSET = 0x64;
        public const uint PET_HP_OFFSET = 0x1C;
        public const uint PET_FEED_STATUS_OFFSET = 0x8;

        public const uint MOB_OFFSET_1 = 0x14;
        public const uint MOB_OFFSET_2 = 0x20;
        public const uint MOB_STRUCT_OFFSET = 0x20;
        public const uint MOB_TYPE_OFFSET = 0xB4;
        public const uint MOB_ACTION_OFFSET = 0x2D4;
        public const uint MOB_WID_OFFSET = 0x114;
        public const uint MOB_HP_OFFSET = 0x128;
        public const uint MOB_DIST_OFFSET = 0x294;
        public const uint MOB_TARGET_OFFSET = 0x210;

        public static uint GetBaseAddress()
        {
            
            INIManager IniManager = new INIManager(BotForm.configName);
            return uint.Parse(IniManager.ReadINI("offset", "baseAddr"), System.Globalization.NumberStyles.HexNumber);
        }
    }
}
