using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotCH.MemoryHelpers.Offsets
{
    public abstract class OffsetTemplate
    {
        public abstract uint BASEADDR_OFFSET { get; }
        public abstract uint GAMEADDR_OFFSET { get; }
        public abstract uint SET_TARGET_FUNC_OFFSET { get; }
        public abstract byte[] ORIG_BYTES_FUNC_OFFSET { get; }
        public abstract uint UNZREEFE_OFFSET { get; }
        public abstract uint PERS_STRUCT_OFFSET { get; }
        public abstract uint PERS_NAME { get; }
        public abstract uint PERS_FLAG_SKILL_OFFSET { get; }
        public abstract uint PERS_WID_OFFSET { get; }
        public abstract uint PERS_HP_OFFSET { get; }
        public abstract uint PERS_MP_OFFSET { get; }
        public abstract uint PERS_MAX_HP_OFFSET { get; }
        public abstract uint PERS_TARGETID_OFFSET { get; }
        public abstract uint PERS_COOLDOWN_FEED_PET_OFFSET { get; }
        public abstract uint PERS_COOLDOWN_POT_HP_OFFSET { get; }
        public abstract uint PERS_LOC_X { get; }
        public abstract uint PERS_LOC_Z { get; }
        public abstract uint PERS_LOC_Y { get; }

        public abstract uint PET_STRUCT_OFFSET { get; }
        public abstract Dictionary<int, uint> PET_CAGES_ARRAY { get; }
        public abstract uint PET_CURRENT_PETID_OFFSET { get; }
        public abstract uint PET_HP_OFFSET { get; }
        public abstract uint PET_FEED_STATUS_OFFSET { get; }

        public abstract uint MOB_OFFSET_1 { get; }
        public abstract uint MOB_OFFSET_2 { get; }
        public abstract uint MOB_NAME { get; }
        public abstract uint MOB_STRUCT_OFFSET { get; }
        public abstract uint MOB_TYPE_OFFSET { get; }
        public abstract uint MOB_ACTION_OFFSET { get; }
        public abstract uint MOB_WID_OFFSET { get; }
        public abstract uint MOB_HP_OFFSET { get; }
        public abstract uint MOB_DIST_OFFSET { get; }
        public abstract uint MOB_TARGET_OFFSET { get; }

        public uint GetOffsetFromIni(string name, string defaultValue)
        {
            string value = BotForm.IniManager.ReadINI("offsets", name, defaultValue);

            // Уберем префикс "0x", если он присутствует
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(2);
            }

            return uint.Parse(value, System.Globalization.NumberStyles.HexNumber);
        }

        public uint GetTargetFuncAddress()
        {
            uint ba = (uint)Reader.process.MainModule.BaseAddress;

            return ba + this.SET_TARGET_FUNC_OFFSET;
        }
    }
}
