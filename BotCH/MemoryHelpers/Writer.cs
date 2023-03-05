using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BotCH.MemoryHelpers
{
    class Writer : Reader
    {
        public static void RestoreDefaultsInjections()
        {
            ChangeGetTargetAssembly(0);
        }

        public static bool ChangeGetTargetAssembly(uint value)
        {
            try
            {
                if (value == 0)
                {
                    throw new ArgumentException();
                }

                byte[] bytes = BitConverter.GetBytes(value);
                byte[] result = new byte[7];

                result[0] = 0xBE;

                if (bytes.Length < 4)
                {
                    Array.Copy(bytes, 0, result, 1, bytes.Length);
                    for (int i = bytes.Length + 1; i < 7; i++)
                    {
                        result[i] = 0x90;
                    }
                }
                else
                {
                    Array.Copy(bytes, 0, result, 1, 4);
                }

                ChangeAssemblyCommand(Offset.Get.SET_TARGET_FUNC_OFFSET, result);

                return true;
            }
            catch (Exception ex)
            {
                ChangeAssemblyCommand(Offset.Get.SET_TARGET_FUNC_OFFSET, Offset.Get.ORIG_BYTES_FUNC_OFFSET);
                return false;
            }
        }

        public static void ChangeAssemblyCommand(uint address, byte[] asmBytes)
        {
            // Получаем дескриптор процесса
            IntPtr processHandle = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, Reader.process.Id);
            if (processHandle == IntPtr.Zero)
            {
                throw new Exception($"Failed to open process {Reader.process.Id}. Error code: {Marshal.GetLastWin32Error()}");
            }

            // Получаем указатель на указанный адрес памяти в процессе
            IntPtr remoteAddress = new IntPtr(address);

            // Изменяем права доступа к памяти на чтение и запись
            if (!VirtualProtectEx(processHandle, remoteAddress, 7, PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                throw new Exception($"Failed to change memory protection. Error code: {Marshal.GetLastWin32Error()}");
            }

            // Копируем команды ассемблера в память процесса
            if (!WriteProcessMemory(processHandle, remoteAddress, asmBytes, asmBytes.Length, out _))
            {
                throw new Exception($"Failed to write process memory. Error code: {Marshal.GetLastWin32Error()}");
            }

            // Возвращаем права доступа к памяти в исходное состояние
            if (!VirtualProtectEx(processHandle, remoteAddress, 7, oldProtect, out _))
            {
                throw new Exception($"Failed to restore memory protection. Error code: {Marshal.GetLastWin32Error()}");
            }

            // Закрываем дескриптор процесса
            CloseHandle(processHandle);
        }

        // Это объявление метода OpenProcess, который используется для получения дескриптора процесса
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        // Это объявления констант, которые используются для работы с процессами и памятью процессов
        const uint PROCESS_VM_OPERATION = 0x0008;
        const uint PROCESS_VM_READ = 0x0010;
        const uint PROCESS_VM_WRITE = 0x0020;
        const uint PAGE_EXECUTE_READWRITE = 0x40;

        // Это объявление метода VirtualProtectEx, который используется для изменения прав доступа к памяти процесса:
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, uint flNewProtect, out uint lpflOldProtect);

        // Это объявление метода WriteProcessMemory, который используется для записи данных в память процесса
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesWritten);

        // Это объявление метода CloseHandle, который используется для закрытия дескриптора процесса
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);




        public static async void UnFreezeeWindow()
        {
            await Task.Run(() =>
            {
                while (form.checkBoxUnfrezze.Checked)
                {
                    uint baseAdrress = ReadUint32(Offset.Get.GetBaseAddress());
                    memory.WriteMemory((baseAdrress + Offset.Get.UNZREEFE_OFFSET).ToString("X"), "int", "1");
                    Thread.Sleep(1000);
                }
            });
        }



    }
}
