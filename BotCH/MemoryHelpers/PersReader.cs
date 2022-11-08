using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotCH.MemoryHelpers
{
    public class PersReader : Reader
    {
        public static uint GetPersStruct()
        {
            uint value = ReadGameAddress();

            if (value != 0)
            {
                return Read_uint32(value + Offset.Get.PERS_STRUCT_OFFSET);
            }

            string error = "Не найден GameAddress";
            Logger.setLog(error);

            throw new Exception(error);
        }

        public static bool GetFlagUseSkill()
        {
            uint path = GetPersStruct();

            return VAMemoryClass.ReadByte((IntPtr)(path + Offset.Get.PERS_FLAG_SKILL_OFFSET)) == 1;
        }

        public static uint GetPetStructWithoutCage()
        {
            uint value = GetPersStruct();

            if (value != 0)
            {
                return Read_uint32(value + Offset.Get.PET_STRUCT_OFFSET);
            }

            string error = "Ошибка в GetPersStruct";
            Logger.setLog(error);

            throw new Exception(error);
        }

        public static uint GetPetStruct()
        {
            int cageNumber = Convert.ToInt32(form.cageSelect.Text);
            uint value = GetPersStruct();

            if (value != 0)
            {
                value = Read_uint32(value + Offset.Get.PET_STRUCT_OFFSET);
                return Read_uint32(value + Offset.Get.PET_CAGES_ARRAY[cageNumber]);
            }

            string error = "Ошибка в GetPetStruct";
            Logger.setLog(error);

            throw new Exception(error);
        }

        public static int GetPetHpPercent()
        {
            uint value = GetPetStruct();

            if (value != 0)
            {
                float hp = Read_float(value + Offset.Get.PET_HP_OFFSET);
                hp = hp * 100;

                return Convert.ToInt32(hp);
            }
            else
            {
                return 0;
            }
        }

        public static int GetPetFeedStatus()
        {
            uint value = GetPetStruct();

            if (value != 0)
            {
                value = Read_uint32(value + Offset.Get.PET_FEED_STATUS_OFFSET);
            }

            return Convert.ToInt32(value);
        }

        public static int GetPersHP()
        {
            uint value = GetPersStruct();

            if (value != 0)
            {
                value = Read_uint32(value + Offset.Get.PERS_HP_OFFSET);
            }

            return Convert.ToInt32(value);
        }

        public static int GetPersMP()
        {
            uint value = GetPersStruct();

            if (value != 0)
            {
                value = Read_uint32(value + Offset.Get.PERS_MP_OFFSET);
            }

            return Convert.ToInt32(value);
        }

        public static uint GetCurrentPetId()
        {
            uint value = GetPetStructWithoutCage();

            if (value != 0)
            {
                return Read_uint32(value + Offset.Get.PET_CURRENT_PETID_OFFSET);
            }

            return 0;
        }

        public static bool IsPetInvited()
        {
            uint value = GetPetStructWithoutCage();

            if (value != 0)
            {
                value = Read_uint32(value + Offset.Get.PET_CURRENT_PETID_OFFSET);
            }

            if (value != 0)
            {
                return true;
            }

            return false;
        }

        public static uint GetTargetId()
        {
            uint value = GetPersStruct();

            if (value != 0)
            {
                value = Read_uint32(value + Offset.Get.PERS_TARGETID_OFFSET);
            }

            return value;
        }

        public static uint GetTargetIdAddres()
        {
            uint value = GetPersStruct();

            if (value != 0)
            {
                return value + Offset.Get.PERS_TARGETID_OFFSET;
            }

            return 0;
        }

        public static int GetPersMaxHP()
        {
            uint value = GetPersStruct();

            if (value != 0)
            {
                value = Read_uint32(value + Offset.Get.PERS_MAX_HP_OFFSET);
            }

            return Convert.ToInt32(value);
        }

        public static bool IsFeedPetAvailable()
        {
            uint value = GetPersStruct();
            value = Read_uint32(value + Offset.Get.PERS_COOLDOWN_FEED_PET_OFFSET);

            return value == 0;
        }

        public static bool IsPotHPAvailable()
        {
            uint value = GetPersStruct();
            value = Read_uint32(value + Offset.Get.PERS_COOLDOWN_POT_HP_OFFSET);

            return value == 0;
        }

        public static uint GetMyPersWID()
        {
            uint value = GetPersStruct();

            return Read_uint32(value + Offset.Get.PERS_WID_OFFSET);
        }

        public static bool IsExistTarget()
        {
            return GetTargetId() != 0;
        }
    }
}
