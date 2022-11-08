using Memory;
using System;
using System.Diagnostics;

namespace BotCH.MemoryHelpers
{
    public class Reader
    {
        public static BotForm form;
        public static bool statusConnection;
        public static Process process;
        public static Mem memory = new Mem();
        private static readonly string DefaultProcessName = "elementclient";

        public static string SetPID(int number)
        {
            if (number != 0)
            {
                try
                {
                    process = Process.GetProcessById(number);
                }
                catch
                {
                    statusConnection = false;

                    return "Process not found";
                }

                statusConnection = true;

                return "Connected to " + process.ProcessName + " PID = " + number;
            }
            else
            {
                Process[] localAll = Process.GetProcesses();

                foreach (Process oneProcess in localAll)
                {
                    if (oneProcess.ProcessName.ToLower() == DefaultProcessName)
                    {
                        process = oneProcess;
                        statusConnection = true;

                        return "Connected to " + process.ProcessName + " PID = " + process.Id;

                    }
                }

                statusConnection = false;

                return "Can't find Elementclient process";
            }
        }

        public static uint ReadUint32(uint address)
        {
            try
            {
                memory.OpenProcess(process.Id);

                return memory.ReadUInt(address.ToString("X"));
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }
        }

        public static float ReadFloat(uint address)
        {
            try
            {
                memory.OpenProcess(process.Id);

                return memory.ReadFloat(address.ToString("X"));
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }
        }

        public static uint ReadGameAddress()
        {
            if (statusConnection)
            {
                uint baseAdrress = ReadUint32(Offset.Get.GetBaseAddress());
                uint gameAdrress = ReadUint32(baseAdrress + Offset.Get.GAMEADDR_OFFSET);

                return gameAdrress;
            }
            else
            {
                return 0;
            }
        }
    }
}
