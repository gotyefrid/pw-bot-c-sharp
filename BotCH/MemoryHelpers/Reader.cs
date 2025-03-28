using Memory;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BotCH.MemoryHelpers
{
    public class Reader
    {
        [DllImport("user32.dll")]
        static extern bool SetWindowText(IntPtr hWnd, string text);

        public static BotForm form;
        public static bool statusConnection = false;
        public static Process process;
        public static Mem memory = new Mem();
        private static readonly string DefaultProcessName = "elementclient";

        public static void RenameGameWindows()
        {
            Process[] localAll = Process.GetProcesses();

            foreach (Process oneProcess in localAll)
            {
                if (oneProcess.ProcessName.ToLower() == DefaultProcessName)
                {
                    try
                    {
                        process = oneProcess;
                        string persName = PersReader.GetPersName();
                        //string persName = "";
                        SetWindowText(oneProcess.MainWindowHandle, persName + " " + oneProcess.Id.ToString());
                    }
                    catch
                    {
                        SetWindowText(oneProcess.MainWindowHandle, oneProcess.Id.ToString());
                    }
                }                
            }


        }

        public static string SetPID(int number)
        {
            if (number != 0)
            {
                try
                {
                    process = Process.GetProcessById(number);
                }
                catch { }
            }
            else
            {
                Process[] localAll = Process.GetProcesses();

                foreach (Process oneProcess in localAll)
                {
                    if (oneProcess.ProcessName.ToLower() == DefaultProcessName)
                    {
                        process = oneProcess;
                    }
                }
            }

            if (process == null)
            {
                return "Process not found";
            }

            statusConnection = true;

            return "Connected to " + process.ProcessName + " PID = " + process.Id;
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

        public static uint ReadUint32(uint[] addresses)
        {
            try
            {
                memory.OpenProcess(process.Id);
                string code = "";

                foreach (uint adress in addresses)
                {
                    string adressStr = adress.ToString("X");

                    if (code == "")
                    {
                        code = adressStr;
                    }
                    else
                    {
                        code = code + " + " + adressStr;
                    }
                }

                return memory.ReadUInt(code);
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }
        }
        public static string ReadString(uint address)
        {
            try
            {
                memory.OpenProcess(process.Id);

                return memory.ReadString(address.ToString("X"), "", 150, true, Encoding.Unicode);
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }
        }

        public static int ReadByte(uint address)
        {
            try
            {
                memory.OpenProcess(process.Id);

                return memory.Read2Byte(address.ToString("X"));
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
            IntPtr baseA = process.MainModule.BaseAddress;
            uint baseAdrress = ReadUint32((uint)baseA.ToInt32() + Offset.Get.BASEADDR_OFFSET);
            uint gameAdrress = ReadUint32(baseAdrress + Offset.Get.GAMEADDR_OFFSET);

            return gameAdrress;
        }
    }
}
