using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BotCH
{
    class Reader
    {
        protected const uint BASEADDR_OFFSET = 0x9B3EEC;
        protected const uint GAMEADDR_OFFSET = 0x1C;
        protected const uint UNZREEFE_ADDR = 0x9B4A04;

        protected const uint PERS_STRUCT_OFFSET = 0x20;
        protected const uint PERS_WID_OFFSET = 0x458;
        protected const uint PERS_HP_OFFSET = 0x46C;
        protected const uint PERS_MP_OFFSET = 0x470;
        protected const uint PERS_MAX_HP_OFFSET = 0x4A4;
        protected const uint PERS_TARGETID_OFFSET = 0xAF0;
        protected const uint PERS_COOLDOWN_FEED_PET_OFFSET = 0x9EC;
        protected const uint PERS_COOLDOWN_POT_HP_OFFSET = 0x9B4;
        protected const uint PERS_LOC_X = 0x3C;
        protected const uint PERS_LOC_Z = 0x40;
        protected const uint PERS_LOC_Y = 0x44;

        protected const uint PET_STRUCT_OFFSET = 0xE60;
        protected const uint PET_CURRENT_PETID_OFFSET = 0x38;
        protected const uint PET_HP_OFFSET = 0x1C;
        protected const uint PET_FEED_STATUS_OFFSET = 0x8;

        protected const uint MOB_OFFSET_1 = 0x8;
        protected const uint MOB_OFFSET_2 = 0x24;
        protected const uint MOB_STRUCT = 0x18;
        protected const uint MOB_WID = 0x11C;
        protected const uint MOB_DIST = 0x270;
        protected const uint MOB_MOVE = 0x2BC;
        protected const uint MOB_TARGET_OFFSET = 0x2D4;

        public static Dictionary<int, uint> cages = new Dictionary<int, uint>
        {
            { 1, 0x10 },
            { 2, 0x14 },
            { 3, 0x18 },
            { 4, 0x1C },
            { 5, 0x20 },
        };

        public static BotForm form;
        public static string processName;
        public static int currentPID = 0;
        public static bool statusConnection;
        public static Process process;
        protected static VAMemory VAM;

        public static string SetPID(int number)
        {
            if (number != 0)
            {
                string name;

                try
                {
                    name = Reader.SetProccesName(number);
                }
                catch
                {
                    Reader.statusConnection = false;

                    return "Process not found";
                }

                Reader.currentPID = number;
                Reader.statusConnection = true;

                Reader.VAM = new VAMemory(Reader.processName);

                return "Connected to " + name + " PID = " + Reader.currentPID;
            }
            else
            {
                Reader.SetProccesName(0);
                Process[] localAll = Process.GetProcesses();

                foreach (var oneProcess in localAll)
                {
                    if (oneProcess.ProcessName.ToLower() == Reader.processName.ToLower())
                    {
                        Reader.currentPID = oneProcess.Id;
                        Reader.process = oneProcess;
                        Reader.statusConnection = true;

                        Reader.VAM = new VAMemory(Reader.processName);

                        return "Connected to " + Reader.processName + " PID = " + Reader.currentPID;

                    }
                }

                Reader.statusConnection = false;

                return "Can't find Elementclient process";
            }
        }

        public static string SetProccesName(int pid)
        {
            if (pid == 0)
            {
                Reader.processName = "elementclient";
                return "elementclient";
            }
            else
            {
                try
                {
                    Reader.processName = Process.GetProcessById(pid).ProcessName;

                    return Reader.processName;
                }
                catch (Exception ex)
                {
                    throw ex;
                }

            }
        }

        public static uint Read_uint32(uint address)
        {
            try
            {
                return Reader.VAM.ReadUInt32((IntPtr)address);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public static float Read_float(uint address)
        {
            try
            {
                float value = Reader.VAM.ReadFloat((IntPtr)address);

                return value;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public static uint ReadGameAddress()
        {
            if (Reader.statusConnection)
            {
                uint gameAdrress = Reader.Read_uint32(Reader.BASEADDR_OFFSET);
                gameAdrress = Reader.Read_uint32(gameAdrress + Reader.GAMEADDR_OFFSET);

                return gameAdrress;
            }
            else
            {
                return 0;
            }
        }
        public static uint GetCurrentMobStruct()
        {
            for (uint i = 0; i <= 768; i++)
            {
                uint value = Reader.Read_uint32(ReadGameAddress() + Reader.MOB_OFFSET_1);
                value = Reader.Read_uint32(value + Reader.MOB_OFFSET_2);
                value = Reader.Read_uint32(value + Reader.MOB_STRUCT);
                string offset = (i * 4).ToString("X");
                value = Reader.Read_uint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                if (value != 0)
                {
                    uint mobStruct = Reader.Read_uint32(value + 0x4);
                    value = Reader.Read_uint32(mobStruct + Reader.MOB_WID);

                    if (value == Convert.ToUInt32(Reader.GetTargetId()))
                    {
                        return mobStruct;
                    }
                }
            }

            return 0;
        }

        public static float GetCurrentMobDistance()
        {
            uint value = Reader.GetCurrentMobStruct();

            return Reader.Read_float(value + Reader.MOB_DIST);

        }

        public static uint GetPersStruct()
        {
            uint value = Reader.ReadGameAddress();

            if (value != 0)
            {
                return Reader.Read_uint32(value + Reader.PERS_STRUCT_OFFSET);
            }

            throw new Exception("Не найден GameAddress");
        }

        public static uint GetPetStruct()
        {
            int cageNumber = Convert.ToInt32(Reader.form.cageSelect.Text);
            uint value = Reader.GetPersStruct();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + Reader.PET_STRUCT_OFFSET);
                return Reader.Read_uint32(value + Reader.cages[cageNumber]);
            }

            throw new Exception("Ошибка в GetPersStruct");
        }
        public static uint GetPetStructWithoutCage()
        {
            uint value = Reader.GetPersStruct();

            if (value != 0)
            {
                return Reader.Read_uint32(value + Reader.PET_STRUCT_OFFSET);
            }

            throw new Exception("Ошибка в GetPersStruct");
        }

        public static int GetPetHpPercent()
        {
            uint value = Reader.GetPetStruct();

            if (value != 0)
            {
                float hp = Reader.Read_float(value + Reader.PET_HP_OFFSET);
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
            uint value = Reader.GetPetStruct();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + Reader.PET_FEED_STATUS_OFFSET);
            }

            return Convert.ToInt32(value);
        }

        public static int GetPersHP()
        {
            uint value = Reader.GetPersStruct();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + Reader.PERS_HP_OFFSET);
            }

            return Convert.ToInt32(value);
        }

        public static int GetPersMP()
        {
            uint value = Reader.GetPersStruct();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + Reader.PERS_MP_OFFSET);
            }

            return Convert.ToInt32(value);
        }

        public static uint GetCurrentPetId()
        {
            uint value = Reader.GetPetStructWithoutCage();

            if (value != 0)
            {
                return Reader.Read_uint32(value + Reader.PET_CURRENT_PETID_OFFSET);
            }

            return 0;
        }

        public static bool IsPetInvited()
        {
            uint value = Reader.GetPetStructWithoutCage();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + Reader.PET_CURRENT_PETID_OFFSET);
            }

            if (value != 0)
            {
                return true;
            }

            return false;
        }

        public static string GetTargetId()
        {
            uint value = Reader.GetPersStruct();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + Reader.PERS_TARGETID_OFFSET);
            }

            return value.ToString();
        }
        public static uint GetTargetIdAddres()
        {
            uint value = Reader.GetPersStruct();

            if (value != 0)
            {
                return value + Reader.PERS_TARGETID_OFFSET;
            }

            return 0;
        }

        public static int GetPersMaxHP()
        {
            uint value = Reader.GetPersStruct();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + Reader.PERS_MAX_HP_OFFSET);
            }

            return Convert.ToInt32(value);
        }

        public static bool IsFeedPetAvailable()
        {
            uint value = Reader.GetPersStruct();
            value = Reader.Read_uint32(value + Reader.PERS_COOLDOWN_FEED_PET_OFFSET);

            return value == 0;
        }
        public static bool IsPotHPAvailable()
        {
            uint value = Reader.GetPersStruct();
            value = Reader.Read_uint32(value + Reader.PERS_COOLDOWN_POT_HP_OFFSET);

            return value == 0;
        }
        public static uint IsExistMobAttackingMe()
        {
            for (uint i = 0; i <= 768; i++)
            {
                uint value = Reader.Read_uint32(ReadGameAddress() + Reader.MOB_OFFSET_1);
                value = Reader.Read_uint32(value + Reader.MOB_OFFSET_2);
                value = Reader.Read_uint32(value + Reader.MOB_STRUCT);
                string offset = (i * 4).ToString("X");
                value = Reader.Read_uint32(value + uint.Parse(offset, System.Globalization.NumberStyles.HexNumber));

                if (value != 0)
                {
                    uint mobStruct = Reader.Read_uint32(value + 0x4);
                    value = Reader.Read_uint32(mobStruct + Reader.MOB_TARGET_OFFSET);

                    if (value == Reader.GetMyPersWID() || value == Reader.GetCurrentPetId() )
                    {
                        return Reader.Read_uint32(mobStruct + Reader.MOB_WID); ;
                    }
                }
            }

            return 0;
        }

        public static uint GetMyPersWID()
        {
            uint value = Reader.GetPersStruct();

            return Reader.Read_uint32(value + Reader.PERS_WID_OFFSET);
        }

        public static bool IsExistTarget()
        {
            return Reader.GetTargetId() != "0";
        }
    }
}
