using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BotCH.MemoryHelpers
{
    /*
     * Прямой вызов функций клиента, которые отправляют команды на сервер (c2s_SendCmd*).
     * Код игры не меняется: в память процесса пишется маленькая заглушка,
     * которая вызывает функцию игры, и запускается отдельным потоком через CreateRemoteThread.
     * Работает при неактивном окне игры.
     */
    public class GameCall
    {
        public static bool IsSupported
        {
            get
            {
                var offsets = Offset.Get;
                return offsets != null && offsets.C2S_SELECT_TARGET_FUNC != 0 && offsets.C2S_NORMAL_ATTACK_FUNC != 0;
            }
        }

        public static bool Enabled
        {
            get { return IsSupported && BotForm.IniManager.ReadINI("settings", "directCalls", "1") == "1"; }
        }

        public static bool SelectTarget(uint wid)
        {
            return Call(Offset.Get.C2S_SELECT_TARGET_FUNC, Offset.Get.C2S_SELECT_TARGET_SIG, wid);
        }

        public static bool CanPickup
        {
            get { return Enabled && Offset.Get.C2S_PICKUP_FUNC != 0 && Offset.Get.GROUND_ITEMS_OFFSET != 0; }
        }

        public static bool CanPetAttack
        {
            get { return Enabled && Offset.Get.C2S_PET_CTRL_FUNC != 0; }
        }

        // id и tid предмета на земле (ItemReader). Сервер поднимает только в радиусе ~10 м, сам персонаж не подходит
        public static bool Pickup(uint id, uint tid)
        {
            return Call(Offset.Get.C2S_PICKUP_FUNC, Offset.Get.C2S_PICKUP_SIG, id, tid);
        }

        public static bool CanSummonPet
        {
            get { return Enabled && Offset.Get.C2S_SUMMON_PET_FUNC != 0; }
        }

        // Призвать пета из клетки cage (1..10, как в настройке бота)
        public static bool SummonPet(int cage)
        {
            return Call(Offset.Get.C2S_SUMMON_PET_FUNC, Offset.Get.C2S_SUMMON_PET_SIG, (uint)(cage - 1));
        }

        public static bool CanUseItem
        {
            get { return Enabled && Offset.Get.C2S_USE_ITEM_FUNC != 0 && Offset.Get.INVENTORY_OFFSET != 0; }
        }

        // Использовать 1 предмет из ячейки slot основной сумки
        public static bool UseItem(uint slot, uint tid)
        {
            return Call(Offset.Get.C2S_USE_ITEM_FUNC, Offset.Get.C2S_USE_ITEM_SIG, 0, slot, tid, 1);
        }

        // Снять цель (аналог Esc)
        public static bool Unselect()
        {
            if (!Enabled || Offset.Get.C2S_UNSELECT_FUNC == 0)
            {
                return false;
            }

            return Call(Offset.Get.C2S_UNSELECT_FUNC, Offset.Get.C2S_UNSELECT_SIG);
        }

        // pvpMask = 0: обычная атака без PvP
        public static bool NormalAttack()
        {
            return Call(Offset.Get.C2S_NORMAL_ATTACK_FUNC, Offset.Get.C2S_NORMAL_ATTACK_SIG, 0);
        }

        // Приказ пету атаковать цель (как Alt+1): команда управления петом, приказ 1, данные — 1 байт pvpMask (0 для мобов)
        public static bool PetAttack(uint targetWid)
        {
            return CallWithData(Offset.Get.C2S_PET_CTRL_FUNC, Offset.Get.C2S_PET_CTRL_SIG, new byte[] { 0 },
                targetWid, PET_CMD_ATTACK, DataPtr, 1);
        }

        const uint PET_CMD_ATTACK = 1;

        // Аргумент-заглушка: при вызове заменяется адресом данных, скопированных в память игры
        private const uint DataPtr = 0xDA7A0000;
        private const int DataOffset = 0x100;

        private static bool Call(uint funcOffset, byte[] signature, params uint[] args)
        {
            return CallWithData(funcOffset, signature, null, args);
        }

        private static bool CallWithData(uint funcOffset, byte[] signature, byte[] data, params uint[] args)
        {
            uint func = (uint)Reader.process.MainModule.BaseAddress.ToInt32() + funcOffset;

            IntPtr h = OpenProcess(PROCESS_ACCESS, false, Reader.process.Id);
            if (h == IntPtr.Zero)
            {
                Logger.setLog("GameCall: OpenProcess failed, error " + Marshal.GetLastWin32Error());
                return false;
            }

            IntPtr mem = IntPtr.Zero;

            try
            {
                // Проверяем, что по адресу та функция, которую ждём (другая версия клиента — не вызываем)
                byte[] actual = new byte[signature.Length];
                if (!ReadProcessMemory(h, (IntPtr)func, actual, actual.Length, out _) || !BytesEqual(actual, signature))
                {
                    Logger.setLog("GameCall: unexpected bytes at 0x" + func.ToString("X") + ", call skipped");
                    return false;
                }

                mem = VirtualAllocEx(h, IntPtr.Zero, (IntPtr)0x1000, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (mem == IntPtr.Zero)
                {
                    Logger.setLog("GameCall: VirtualAllocEx failed, error " + Marshal.GetLastWin32Error());
                    return false;
                }

                // Данные (если есть) кладём в ту же страницу, после кода заглушки
                if (data != null)
                {
                    uint dataAddr = (uint)mem.ToInt64() + DataOffset;
                    args = Array.ConvertAll(args, a => a == DataPtr ? dataAddr : a);

                    if (!WriteProcessMemory(h, (IntPtr)dataAddr, data, data.Length, out _))
                    {
                        Logger.setLog("GameCall: writing data failed, error " + Marshal.GetLastWin32Error());
                        return false;
                    }
                }

                byte[] stub = BuildCdeclStub(func, args);

                if (!WriteProcessMemory(h, mem, stub, stub.Length, out _)
                    || !VirtualProtectEx(h, mem, (IntPtr)0x1000, PAGE_EXECUTE_READ, out _))
                {
                    Logger.setLog("GameCall: writing stub failed, error " + Marshal.GetLastWin32Error());
                    return false;
                }

                IntPtr thread = CreateRemoteThread(h, IntPtr.Zero, IntPtr.Zero, mem, IntPtr.Zero, 0, IntPtr.Zero);
                if (thread == IntPtr.Zero)
                {
                    Logger.setLog("GameCall: CreateRemoteThread failed, error " + Marshal.GetLastWin32Error());
                    return false;
                }

                uint wait = WaitForSingleObject(thread, 5000);
                CloseHandle(thread);

                if (wait != 0)
                {
                    // Поток ещё работает — память не освобождаем, иначе игра упадёт
                    mem = IntPtr.Zero;
                    Logger.setLog("GameCall: remote thread timeout");
                    return false;
                }

                return true;
            }
            finally
            {
                if (mem != IntPtr.Zero)
                {
                    VirtualFreeEx(h, mem, IntPtr.Zero, MEM_RELEASE);
                }

                CloseHandle(h);
            }
        }

        // push argN ... push arg1; mov eax, func; call eax; add esp, 4*N; xor eax, eax; ret 4
        private static byte[] BuildCdeclStub(uint func, uint[] args)
        {
            var code = new List<byte>();

            for (int i = args.Length - 1; i >= 0; i--)
            {
                code.Add(0x68);
                code.AddRange(BitConverter.GetBytes(args[i]));
            }

            code.Add(0xB8);
            code.AddRange(BitConverter.GetBytes(func));
            code.AddRange(new byte[] { 0xFF, 0xD0 });

            if (args.Length > 0)
            {
                code.AddRange(new byte[] { 0x83, 0xC4, (byte)(4 * args.Length) });
            }

            code.AddRange(new byte[] { 0x31, 0xC0, 0xC2, 0x04, 0x00 });

            return code.ToArray();
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        const uint PROCESS_ACCESS = 0x0002 | 0x0008 | 0x0010 | 0x0020 | 0x0400; // CREATE_THREAD | VM_OPERATION | VM_READ | VM_WRITE | QUERY_INFORMATION
        const uint MEM_COMMIT = 0x1000;
        const uint MEM_RESERVE = 0x2000;
        const uint MEM_RELEASE = 0x8000;
        const uint PAGE_READWRITE = 0x04;
        const uint PAGE_EXECUTE_READ = 0x20;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, IntPtr dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);
    }
}
