using BotCH.Entity;
using System;
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

            if (mobList != null)
            {
                value = GetMobStruct(wid, mobList);
            }
            else
            {
                value = GetMobStruct(wid);
            }

            return ReadFloat(value + Offset.Get.MOB_DIST_OFFSET);
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
