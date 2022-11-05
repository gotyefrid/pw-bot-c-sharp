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
                uint value = Read_uint32(ReadGameAddress() + Offset.MOB_OFFSET_1);
                value = Read_uint32(value + Offset.MOB_OFFSET_2);
                value = Read_uint32(value + Offset.MOB_STRUCT_OFFSET);
                string offset = (i * 4).ToString("X");
                value = Read_uint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                if (value != 0)
                {
                    uint mobStruct = Read_uint32(value + 0x4);
                    value = Read_uint32(mobStruct + Offset.MOB_WID_OFFSET);

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
                uint value = Read_uint32(ReadGameAddress() + Offset.MOB_OFFSET_1);
                value = Read_uint32(value + Offset.MOB_OFFSET_2);
                value = Read_uint32(value + Offset.MOB_STRUCT_OFFSET);
                string offset = mob.Value;
                value = Read_uint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                if (value != 0)
                {
                    uint mobStruct = Read_uint32(value + 0x4);
                    value = Read_uint32(mobStruct + Offset.MOB_WID_OFFSET);

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

            return Read_float(value + Offset.MOB_DIST_OFFSET);
        }
        public static uint IsExistMobAttackingMe()
        {
            for (uint i = 0; i <= 900; i++)
            {
                try
                {
                    uint value = Read_uint32(ReadGameAddress() + Offset.MOB_OFFSET_1);
                    value = Read_uint32(value + Offset.MOB_OFFSET_2);
                    value = Read_uint32(value + Offset.MOB_STRUCT_OFFSET);
                    string offset = (i * 4).ToString("X");
                    value = Read_uint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                    if (value != 0)
                    {
                        uint mobStruct = Read_uint32(value + 0x4);
                        value = Read_uint32(mobStruct + Offset.MOB_TARGET_OFFSET);

                        if ((value == PersReader.GetMyPersWID() || value == PersReader.GetCurrentPetId()))
                        {
                            uint mobWid = Read_uint32(mobStruct + Offset.MOB_WID_OFFSET);
                            uint mobType = GetMobType(mobWid);
                            uint mobAction = Read_uint32(mobStruct + Offset.MOB_ACTION_OFFSET);

                            if (mobType == TargetMobEntity.TYPE_MOB && mobAction != TargetMobEntity.ACTION_DIES)
                            {
                                return Read_uint32(mobStruct + Offset.MOB_WID_OFFSET); ;
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
            foreach (KeyValuePair<uint, string> mob in mobs)
            {
                uint value = Read_uint32(ReadGameAddress() + Offset.MOB_OFFSET_1);
                value = Read_uint32(value + Offset.MOB_OFFSET_2);
                value = Read_uint32(value + Offset.MOB_STRUCT_OFFSET);
                string offset = mob.Value;
                value = Read_uint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                if (value != 0)
                {
                    uint mobStruct = Read_uint32(value + 0x4);
                    value = Read_uint32(mobStruct + Offset.MOB_TARGET_OFFSET);

                    if ((value == PersReader.GetMyPersWID() || value == PersReader.GetCurrentPetId()))
                    {
                        uint mobWid = Read_uint32(mobStruct + Offset.MOB_WID_OFFSET);
                        uint mobType = GetMobType(mobWid);
                        uint mobAction = Read_uint32(mobStruct + Offset.MOB_ACTION_OFFSET);

                        if (mobType == TargetMobEntity.TYPE_MOB && mobAction != TargetMobEntity.ACTION_DIES)
                        {
                            return Read_uint32(mobStruct + Offset.MOB_WID_OFFSET); ;
                        }
                    }
                }
            }

            return 0;
        }

        public static Dictionary<uint, string> GetActualListMobsOffsetsInArray()
        {
            Dictionary<uint, string> array = new Dictionary<uint, string>();

            for (uint i = 0; i <= 900; i++)
            {
                uint value = Read_uint32(ReadGameAddress() + Offset.MOB_OFFSET_1);
                value = Read_uint32(value + Offset.MOB_OFFSET_2);
                value = Read_uint32(value + Offset.MOB_STRUCT_OFFSET);
                string offset = (i * 4).ToString("X");
                value = Read_uint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                if (value != 0)
                {
                    uint mobStruct = Read_uint32(value + 0x4);

                    uint mobWid = Read_uint32(mobStruct + Offset.MOB_WID_OFFSET);
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
            mobType = Read_uint32(mobType + Offset.MOB_TYPE_OFFSET);

            return mobType;
        }

        public static uint GetMobAction(uint wid)
        {
            uint mobAction = GetMobStruct(wid);
            mobAction = Read_uint32(mobAction + Offset.MOB_ACTION_OFFSET);

            return mobAction;
        }
    }
}
