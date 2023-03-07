using System.Collections.Generic;

namespace BotCH.MemoryHelpers.Offsets
{
    /* Comeback PW 1.4.6 
     https://comeback.pw/
    */
    public class Comeback146X : OffsetTemplate
    {
        public override uint BASEADDR_OFFSET
        {
            get =>
                uint.Parse(BotForm.IniManager.ReadINI("settings", "baseAddress", "860380"), System.Globalization.NumberStyles.HexNumber);
        }

        public override uint GAMEADDR_OFFSET { get => 0x1C; }
        public override uint UNZREEFE_OFFSET { get => 0x4E8; }
        public override uint SET_TARGET_FUNC_OFFSET { get => 0x2AC99C; }
        public override byte[] ORIG_BYTES_FUNC_OFFSET => new byte[] { 0x8B, 0x75, 0x08, 0x57, 0x8B, 0x40, 0x1C };
        public override uint PERS_STRUCT_OFFSET { get => 0x28; }
        public override uint PERS_NAME { get => 0x6F8; }
        public override uint PERS_FLAG_SKILL_OFFSET { get => 0xB8; }
        public override uint PERS_WID_OFFSET { get => 0x4BC; }
        public override uint PERS_HP_OFFSET { get => 0x4D0; }
        public override uint PERS_MP_OFFSET { get => 0x4D4; }
        public override uint PERS_MAX_HP_OFFSET { get => 0x534; }
        public override uint PERS_TARGETID_OFFSET { get => 0x5B8; }
        public override uint PERS_COOLDOWN_FEED_PET_OFFSET { get => 0xBF4; }
        public override uint PERS_COOLDOWN_POT_HP_OFFSET { get => 0xBBC; }
        public override uint PERS_LOC_X { get => 0x3C; }
        public override uint PERS_LOC_Z { get => 0x40; }
        public override uint PERS_LOC_Y { get => 0x44; }

        public override uint PET_STRUCT_OFFSET { get => 0x143C; }
        public override Dictionary<int, uint> PET_CAGES_ARRAY
        {
            get => new Dictionary<int, uint>
            {
                    { 1, 0x10 },
                    { 2, 0x14 },
                    { 3, 0x18 },
                    { 4, 0x1C },
                    { 5, 0x20 },
            };
        }
        public override uint PET_CURRENT_PETID_OFFSET { get => 0x64; }
        public override uint PET_HP_OFFSET { get => 0x1C; }
        public override uint PET_FEED_STATUS_OFFSET { get => 0x8; }

        public override uint MOB_OFFSET_1 { get => 0x14; }
        public override uint MOB_OFFSET_2 { get => 0x20; }
        public override uint MOB_NAME { get => 0x26C; }
        public override uint MOB_STRUCT_OFFSET { get => 0x20; }
        public override uint MOB_TYPE_OFFSET { get => 0xB4; }
        public override uint MOB_ACTION_OFFSET { get => 0x2D4; }
        public override uint MOB_WID_OFFSET { get => 0x114; }
        public override uint MOB_HP_OFFSET { get => 0x128; }
        public override uint MOB_DIST_OFFSET { get => 0x290; }
        public override uint MOB_TARGET_OFFSET { get => 0x210; }
    }
}
