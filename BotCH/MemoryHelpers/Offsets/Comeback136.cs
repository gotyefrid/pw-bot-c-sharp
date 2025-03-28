using System.Collections.Generic;

namespace BotCH.MemoryHelpers.Offsets
{
    /* Comeback PW 1.3.6 
     https://comeback.pw/
    */
    public class Comeback136 : OffsetTemplate
    {
        public override uint BASEADDR_OFFSET { get => GetOffsetFromIni("baseAddress", "0x8E5D48"); }
        public override uint GAMEADDR_OFFSET { get => GetOffsetFromIni("gameAddr", "0x1C"); }
        public override uint UNZREEFE_OFFSET { get => GetOffsetFromIni("unfrz", "0x4E8"); }
        public override uint SET_TARGET_FUNC_OFFSET { get; }
        public override byte[] ORIG_BYTES_FUNC_OFFSET => new byte[] { };
        public override uint PERS_STRUCT_OFFSET { get => GetOffsetFromIni("persStruct", "0x28"); }
        public override uint PERS_NAME { get => GetOffsetFromIni("persName", "0x6F8"); }
        public override uint PERS_FLAG_SKILL_OFFSET { get => GetOffsetFromIni("flagSkill", "0xB8"); }
        public override uint PERS_WID_OFFSET { get => GetOffsetFromIni("persWid", "0x4BC"); }
        public override uint PERS_HP_OFFSET { get => GetOffsetFromIni("persHp", "0x4D0"); }
        public override uint PERS_MP_OFFSET { get => GetOffsetFromIni("persMp", "0x4D4"); }
        public override uint PERS_MAX_HP_OFFSET { get => GetOffsetFromIni("persMaxHp", "0x534"); }
        public override uint PERS_TARGETID_OFFSET { get => GetOffsetFromIni("persTargetId", "0x5B8"); }
        public override uint PERS_COOLDOWN_FEED_PET_OFFSET { get => GetOffsetFromIni("persCdFeedPet", "0xBFC"); }
        public override uint PERS_COOLDOWN_POT_HP_OFFSET { get => GetOffsetFromIni("persCdPotHp", "0xBC4"); }
        public override uint PERS_LOC_X { get => 0x3C; }
        public override uint PERS_LOC_Z { get => 0x40; }
        public override uint PERS_LOC_Y { get => 0x44; }

        public override uint PET_STRUCT_OFFSET { get => GetOffsetFromIni("petStruct", "0x1444"); }
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
        public override uint PET_CURRENT_PETID_OFFSET { get => GetOffsetFromIni("petCurrentPetId", "0x64"); }
        public override uint PET_HP_OFFSET { get => GetOffsetFromIni("petHp", "0x1C"); }
        public override uint PET_FEED_STATUS_OFFSET { get => GetOffsetFromIni("petFeedStatus", "0x8"); }

        public override uint MOB_OFFSET_1 { get => GetOffsetFromIni("mobOffset1", "0x14"); }
        public override uint MOB_OFFSET_2 { get => GetOffsetFromIni("mobOffset2", "0x20"); }
        public override uint MOB_NAME { get => 0x268; }
        public override uint MOB_STRUCT_OFFSET { get => GetOffsetFromIni("mobStruct", "0x20"); }
        public override uint MOB_TYPE_OFFSET { get => GetOffsetFromIni("mobType", "0xB4"); }
        public override uint MOB_ACTION_OFFSET { get => GetOffsetFromIni("mobAction", "0x2D4"); }
        public override uint MOB_WID_OFFSET { get => GetOffsetFromIni("mobWid", "0x114"); }
        public override uint MOB_HP_OFFSET { get => 0x128; }
        public override uint MOB_DIST_OFFSET { get => GetOffsetFromIni("mobDistance", "0x294"); }
        public override uint MOB_TARGET_OFFSET { get => GetOffsetFromIni("mobTarget", "0x210"); }
    }
}
