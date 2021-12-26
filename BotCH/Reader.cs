using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Memory;

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
                    name = SetProccesName(number);
                } catch
                {
                    statusConnection = false;

                    return "Process not found";
                }
                
                currentPID = number;
                statusConnection = true;

                return "Connected to " + name + " PID = " + currentPID;
            }
            else
            {
                SetProccesName(0);
                Process[] localAll = Process.GetProcesses();

                foreach (var s in localAll)
                {
                    if (s.ProcessName.ToLower() == processName.ToLower())
                    {
                        currentPID = s.Id;
                        statusConnection = true;

                        return "Connected to " + processName + " PID = " + currentPID;

                    }
                }
                statusConnection = false;

                return "Can't find Elementclient process";
            }
        }

        public static string SetProccesName(int pid)
        {
            if(pid == 0)
            {
                processName = "elementclient";
                return "elementclient";
            } 
            else
            {
                try
                {
                    processName = Process.GetProcessById(pid).ProcessName;
                    return processName;
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
                currentProcess = new VAMemory(processName);
                uint value = currentProcess.ReadUInt32((IntPtr)address);

                return value;
            } catch (Exception ex)
            {
                MessageBox.Show("Connect to client first.");

                throw ex;
            }
        }

        public static uint ReadGameAddress()
        {
            if (statusConnection)
            {
                uint gameAdrress = Read_uint32(0xC7662C);
                gameAdrress = Read_uint32(gameAdrress + 0x1C);

                return gameAdrress;
            } else
            {
                MessageBox.Show("Connect to client first.");
                return 0;
            }
            

            
        }

        public static int GetPersHP()
        {

            uint value = ReadGameAddress();

            if (value != 0)
            {
                value = Read_uint32(value + 0x2C);
                value = Read_uint32(value + 0x4A8);
            }

            return Convert.ToInt32(value);
        }

        

    }
}
