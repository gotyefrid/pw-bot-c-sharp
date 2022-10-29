using System;
using System.Diagnostics;

namespace BotCH.MemoryHelpers
{
    public class Reader
    {
        public static BotForm form;
        public static string processName;
        public static int currentPID = 0;
        public static bool statusConnection;
        public static Process process;
        public static VAMemory VAMemoryClass;

        public static string SetPID(int number)
        {
            if (number != 0)
            {
                string name;

                try
                {
                    name = SetProccesName(number);
                }
                catch
                {
                    statusConnection = false;

                    return "Process not found";
                }

                currentPID = number;
                statusConnection = true;

                VAMemoryClass = new VAMemory(processName);

                return "Connected to " + name + " PID = " + currentPID;
            }
            else
            {
                SetProccesName(0);
                Process[] localAll = Process.GetProcesses();

                foreach (var oneProcess in localAll)
                {
                    if (oneProcess.ProcessName.ToLower() == processName.ToLower())
                    {
                        currentPID = oneProcess.Id;
                        process = oneProcess;
                        statusConnection = true;

                        VAMemoryClass = new VAMemory(processName);

                        return "Connected to " + processName + " PID = " + Reader.currentPID;

                    }
                }

                statusConnection = false;

                return "Can't find Elementclient process";
            }
        }

        public static string SetProccesName(int pid)
        {
            if (pid == 0)
            {
                processName = "elementclient";

                return "elementclient";
            }
            else
            {
                processName = Process.GetProcessById(pid).ProcessName;

                return processName;
            }
        }

        public static uint Read_uint32(uint address)
        {
            try
            {
                return VAMemoryClass.ReadUInt32((IntPtr)address);
            }
            catch (OverflowException)
            {
                return 0;
            }
        }

        public static float Read_float(uint address)
        {
            return VAMemoryClass.ReadFloat((IntPtr)address);
        }

        public static uint ReadGameAddress()
        {
            if (statusConnection)
            {
                uint gameAdrress = Read_uint32(Offset.GetBaseAddress());
                gameAdrress = Read_uint32(gameAdrress + Offset.GAMEADDR_OFFSET);

                return gameAdrress;
            }
            else
            {
                return 0;
            }
        }
    }
}
