using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace BotCH
{
    class Reader
    {
        public static string processName;
        public static int currentPID;
        public static bool statusConnection;
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

                foreach (var s in localAll)
                {
                    if (s.ProcessName.ToLower() == Reader.processName.ToLower())
                    {
                        Reader.currentPID = s.Id;
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
                MessageBox.Show("Connect to client first.");

                throw ex;
            }
        }

        public static uint ReadGameAddress()
        {
            if (Reader.statusConnection)
            {
                uint gameAdrress = Reader.Read_uint32(0x9B3EEC);
                gameAdrress = Reader.Read_uint32(gameAdrress + 0x1C);

                return gameAdrress;
            }
            else
            {
                MessageBox.Show("Connect to client first.");
                return 0;
            }



        }

        public static uint GetPersStruct()
        {
            uint value = Reader.ReadGameAddress();

            if (value != 0)
            {
                return Reader.Read_uint32(value + 0x20);
            }

            throw new Exception("Не найден GameAddress");
        }

        public static int GetPersHP()
        {
            uint value = Reader.GetPersStruct();

            if (value != 0)
            {
                value = Reader.Read_uint32(value + 0x46C);
            }

            return Convert.ToInt32(value);
        }



    }
}
