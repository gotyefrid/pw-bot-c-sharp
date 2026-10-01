using BotCH.Entity;
using System.Collections.Generic;

namespace BotCH.MemoryHelpers
{
    public class MobReader : Reader
    {
        public static uint GetMobStruct(uint wid)
        {
            for (uint i = 0; i <= 768; i++)
            {
                uint value = ReadUint32(ReadGameAddress() + Offset.Get.MOB_OFFSET_1);
                value = ReadUint32(value + Offset.Get.MOB_OFFSET_2);
                value = ReadUint32(value + Offset.Get.MOB_STRUCT_OFFSET);
                string offset = (i * 4).ToString("X");
                value = ReadUint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                if (value != 0)
                {
                    uint mobStruct = ReadUint32(value + 0x4);
                    value = ReadUint32(mobStruct + Offset.Get.MOB_WID_OFFSET);

                    if (value == wid)
                    {
                        return mobStruct;
                    }
                }
            }

            return 0;
        }

        public static string GetMobArrayHexNumber(uint wid)
        {
            for (uint i = 0; i <= 768; i++)
            {
                uint value = ReadUint32(ReadGameAddress() + Offset.Get.MOB_OFFSET_1);
                value = ReadUint32(value + Offset.Get.MOB_OFFSET_2);
                value = ReadUint32(value + Offset.Get.MOB_STRUCT_OFFSET);
                string offset = (i * 4).ToString("X");
                value = ReadUint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                if (value != 0)
                {
                    uint mobStruct = ReadUint32(value + 0x4);
                    value = ReadUint32(mobStruct + Offset.Get.MOB_WID_OFFSET);

                    if (value == wid)
                    {
                        return offset;
                    }
                }
            }

            return "0";
        }

        public static uint GetMobStruct(uint wid, Dictionary<uint, string> mobList = null)
        {
            foreach (KeyValuePair<uint, string> mob in mobList)
            {
                uint value = ReadUint32(ReadGameAddress() + Offset.Get.MOB_OFFSET_1);
                value = ReadUint32(value + Offset.Get.MOB_OFFSET_2);
                value = ReadUint32(value + Offset.Get.MOB_STRUCT_OFFSET);
                string offset = mob.Value;
                value = ReadUint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                if (value != 0)
                {
                    uint mobStruct = ReadUint32(value + 0x4);
                    value = ReadUint32(mobStruct + Offset.Get.MOB_WID_OFFSET);

                    if (value == wid)
                    {
                        return mobStruct;
                    }
                }
            }

            return 0;
        }

        public static float GetMobDistance(uint wid, Dictionary<uint, string> mobList = null)
        {
            uint value;

            if (mobList != null && mobList.ContainsKey(wid))
            {
                value = GetMobStruct(wid, mobList);
            }
            else
            {
                value = GetMobStruct(wid);
            }

            return ReadFloat(value + Offset.Get.MOB_DIST_OFFSET);
        }

        public static string GetMobName(uint wid, Dictionary<uint, string> mobList = null)
        {
            try
            {
                uint value;

                if (mobList != null && mobList.ContainsKey(wid))
                {
                    value = GetMobStruct(wid, mobList);
                }
                else
                {
                    value = GetMobStruct(wid);
                }

                uint buff = ReadUint32(value + Offset.Get.MOB_NAME);
                string name = ReadString(buff + 0x0);
                return name;
            }
            catch
            {
                return "";
            }
        }


        public static uint IsExistMobAttackingMe()
        {
            for (uint i = 0; i <= 768; i++)
            {
                try
                {
                    uint value = ReadUint32(ReadGameAddress() + Offset.Get.MOB_OFFSET_1);
                    value = ReadUint32(value + Offset.Get.MOB_OFFSET_2);
                    value = ReadUint32(value + Offset.Get.MOB_STRUCT_OFFSET);
                    string offset = (i * 4).ToString("X");
                    value = ReadUint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                    if (value != 0)
                    {
                        uint mobStruct = ReadUint32(value + 0x4);
                        value = ReadUint32(mobStruct + Offset.Get.MOB_TARGET_OFFSET);

                        if ((value == PersReader.GetMyPersWID() || value == PersReader.GetCurrentPetId()))
                        {
                            uint mobWid = ReadUint32(mobStruct + Offset.Get.MOB_WID_OFFSET);
                            uint mobType = GetMobType(mobWid);
                            uint mobAction = ReadUint32(mobStruct + Offset.Get.MOB_ACTION_OFFSET);

                            if (mobType == TargetMobEntity.TYPE_MOB && mobAction != TargetMobEntity.ACTION_DIES)
                            {
                                return ReadUint32(mobStruct + Offset.Get.MOB_WID_OFFSET); ;
                            }
                        }
                    }
                }
                catch
                {
                    Logger.setLog(i.ToString());
                    continue;
                }
            }

            return 0;
        }
        public static uint IsExistMobAttackingMe(Dictionary<uint, string> mobs)
        {
            if (mobs == null)
            {
                return 0;
            }

            foreach (KeyValuePair<uint, string> mob in mobs)
            {
                uint value = ReadUint32(ReadGameAddress() + Offset.Get.MOB_OFFSET_1);
                value = ReadUint32(value + Offset.Get.MOB_OFFSET_2);
                value = ReadUint32(value + Offset.Get.MOB_STRUCT_OFFSET);
                string offset = mob.Value;
                value = ReadUint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                if (value != 0)
                {
                    uint mobStruct = ReadUint32(value + 0x4);
                    value = ReadUint32(mobStruct + Offset.Get.MOB_TARGET_OFFSET);
                    var persWid = PersReader.GetMyPersWID();
                    var persMobWid = PersReader.GetCurrentPetId();

                    if (value == persWid || (persMobWid != 0 && value == persMobWid))
                    {
                        uint mobWid = ReadUint32(mobStruct + Offset.Get.MOB_WID_OFFSET);
                        uint mobType = GetMobType(mobWid);
                        uint mobAction = ReadUint32(mobStruct + Offset.Get.MOB_ACTION_OFFSET);

                        if (mobType == TargetMobEntity.TYPE_MOB && mobAction != TargetMobEntity.ACTION_DIES)
                        {
                            return ReadUint32(mobStruct + Offset.Get.MOB_WID_OFFSET); ;
                        }
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Аналог Tab: ближайший живой моб вокруг.
        /// Если передан белый список WID — выбираются только мобы из него.
        /// HP не проверяется: клиент знает HP только у выбранной цели, у остальных там 0.
        /// Трупы отсекаются по MOB_ACTION_OFFSET == ACTION_DIES: значение 4 ставится в момент смерти и держится, пока труп не исчезнет.
        /// </summary>
        /// <returns>WID моба или 0, если подходящих нет.</returns>
        public static uint FindNearestMob(ICollection<string> allowedWids = null)
        {
            uint array = ReadUint32(ReadGameAddress() + Offset.Get.MOB_OFFSET_1);
            array = ReadUint32(array + Offset.Get.MOB_OFFSET_2);
            array = ReadUint32(array + Offset.Get.MOB_STRUCT_OFFSET);

            uint nearestWid = 0;
            float nearestDist = float.MaxValue;

            for (uint i = 0; i <= 768; i++)
            {
                uint entry = ReadUint32(array + i * 4);

                if (entry == 0)
                {
                    continue;
                }

                uint mobStruct = ReadUint32(entry + 0x4);
                uint mobWid = ReadUint32(mobStruct + Offset.Get.MOB_WID_OFFSET);

                if (ReadUint32(mobStruct + Offset.Get.MOB_TYPE_OFFSET) != TargetMobEntity.TYPE_MOB
                    || ReadUint32(mobStruct + Offset.Get.MOB_ACTION_OFFSET) == TargetMobEntity.ACTION_DIES)
                {
                    continue;
                }

                if (allowedWids != null && !allowedWids.Contains(mobWid.ToString()))
                {
                    continue;
                }

                float dist = ReadFloat(mobStruct + Offset.Get.MOB_DIST_OFFSET);

                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestWid = mobWid;
                }
            }

            return nearestWid;
        }

        /// <summary>
        /// Ближайший живой моб, который агрится на персонажа или его пета (MOB_TARGET_OFFSET).
        /// Смотрит всех существ вокруг, а не сохранённый список. Игроки и NPC не учитываются.
        /// </summary>
        /// <returns>WID моба или 0, если нас никто не бьёт.</returns>
        public static uint FindMobAttackingUs()
        {
            uint persWid = PersReader.GetMyPersWID();
            uint petWid = PersReader.GetCurrentPetId();

            uint array = ReadUint32(ReadGameAddress() + Offset.Get.MOB_OFFSET_1);
            array = ReadUint32(array + Offset.Get.MOB_OFFSET_2);
            array = ReadUint32(array + Offset.Get.MOB_STRUCT_OFFSET);

            uint nearestWid = 0;
            float nearestDist = float.MaxValue;

            for (uint i = 0; i <= 768; i++)
            {
                uint entry = ReadUint32(array + i * 4);

                if (entry == 0)
                {
                    continue;
                }

                uint mobStruct = ReadUint32(entry + 0x4);

                if (ReadUint32(mobStruct + Offset.Get.MOB_TYPE_OFFSET) != TargetMobEntity.TYPE_MOB
                    || ReadUint32(mobStruct + Offset.Get.MOB_ACTION_OFFSET) == TargetMobEntity.ACTION_DIES)
                {
                    continue;
                }

                uint mobTarget = ReadUint32(mobStruct + Offset.Get.MOB_TARGET_OFFSET);

                if (mobTarget == 0 || (mobTarget != persWid && mobTarget != petWid))
                {
                    continue;
                }

                float dist = ReadFloat(mobStruct + Offset.Get.MOB_DIST_OFFSET);

                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestWid = ReadUint32(mobStruct + Offset.Get.MOB_WID_OFFSET);
                }
            }

            return nearestWid;
        }

        public static Dictionary<uint, string> GetActualListMobsOffsetsInArray()
        {
            Dictionary<uint, string> array = new Dictionary<uint, string>();

            for (uint i = 0; i <= 768; i++)
            {
                uint value = ReadUint32(ReadGameAddress() + Offset.Get.MOB_OFFSET_1);
                value = ReadUint32(value + Offset.Get.MOB_OFFSET_2);
                value = ReadUint32(value + Offset.Get.MOB_STRUCT_OFFSET);
                string offset = (i * 4).ToString("X");
                value = ReadUint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                if (value != 0)
                {
                    uint mobStruct = ReadUint32(value + 0x4);

                    uint mobWid = ReadUint32(mobStruct + Offset.Get.MOB_WID_OFFSET);
                    uint mobType = GetMobType(mobWid);

                    if (mobType == TargetMobEntity.TYPE_MOB)
                    {
                        array.Add(mobWid, offset);
                    }
                }
            }

            return array;
        }

        public static bool IsMobAttakingMeNow(uint wid)
        {
            uint value = ReadUint32(ReadGameAddress() + Offset.Get.MOB_OFFSET_1);
            value = ReadUint32(value + Offset.Get.MOB_OFFSET_2);
            value = ReadUint32(value + Offset.Get.MOB_STRUCT_OFFSET);
            string offset = GetMobArrayHexNumber(wid);
            value = ReadUint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

            if (value != 0)
            {
                uint mobStruct = ReadUint32(value + 0x4);
                value = ReadUint32(mobStruct + Offset.Get.MOB_TARGET_OFFSET);
                var persWid = PersReader.GetMyPersWID();
                var persMobWid = PersReader.GetCurrentPetId();

                if (value == persWid || (persMobWid != 0 && value == persMobWid))
                {
                    uint mobWid = ReadUint32(mobStruct + Offset.Get.MOB_WID_OFFSET);
                    uint mobType = GetMobType(mobWid);
                    uint mobAction = ReadUint32(mobStruct + Offset.Get.MOB_ACTION_OFFSET);

                    if (mobType == TargetMobEntity.TYPE_MOB && mobAction != TargetMobEntity.ACTION_DIES)
                    {
                        return true; ;
                    }
                }
            }

            return false;
        }

        public static uint GetMobType(uint wid)
        {
            uint mobType = GetMobStruct(wid);
            mobType = ReadUint32(mobType + Offset.Get.MOB_TYPE_OFFSET);

            return mobType;
        }

        public static uint GetMobAction(uint wid)
        {
            uint mobAction = GetMobStruct(wid);
            mobAction = ReadUint32(mobAction + Offset.Get.MOB_ACTION_OFFSET);

            return mobAction;
        }
    }
}
