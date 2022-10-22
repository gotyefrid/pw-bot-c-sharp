using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace BotCH
{
    class Reader
    {
        private const uint BASEADDR_OFFSET = 0x9B3EEC;
        private const uint GAMEADDR_OFFSET = 0x1C;

        private const uint PERS_STRUCT_OFFSET = 0x20;
        private const uint HP_OFFSET = 0x46C;
        private const uint MP_OFFSET = 0x470;
        private const uint MAX_HP_OFFSET = 0x4A4;
        private const uint TARGETID_OFFSET = 0xAF0;
        private const uint CURRENT_PETID_OFFSET = 0x38;

        private const uint PET_STRUCT_OFFSET = 0xE60;
        private const uint PET_HP_OFFSET = 0x1C;
        private const uint PET_FEED_STATUS_OFFSET = 0x8;

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
        private static VAMemory currentProcess;

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
                currentProcess = new VAMemory(Reader.processName);
                uint value = currentProcess.ReadUInt32((IntPtr)address);

                return value;
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
                currentProcess = new VAMemory(Reader.processName);
                float value = currentProcess.ReadFloat((IntPtr)address);

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
                value = Reader.Read_uint32(value + Reader.HP_OFFSET);
            }

            return Convert.ToInt32(value);
        }

        public static int GetPersMP()
        {
            uint value = Reader.GetPersStruct();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + Reader.MP_OFFSET);
            }

            return Convert.ToInt32(value);
        }

        public static bool IsPetInvited()
        {
            uint value = Reader.GetPetStructWithoutCage();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + Reader.CURRENT_PETID_OFFSET);
            }

            if (value != 0)
            {
                return true;
            }

            return false;
        }

        public static String GetTargetId()
        {
            uint value = Reader.GetPersStruct();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + Reader.TARGETID_OFFSET);
            }

            return value.ToString();
        }

        public static int GetPersMaxHP()
        {
            uint value = Reader.GetPersStruct();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + Reader.MAX_HP_OFFSET);
            }

            return Convert.ToInt32(value);
        }

    }
}
