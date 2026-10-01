using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BotCH.Core.Memory;

/// <summary>Функции Windows (kernel32), через которые идёт работа с памятью чужого процесса.</summary>
internal static class NativeMethods
{
    [Flags]
    public enum ProcessAccess : uint
    {
        VmOperation = 0x0008,
        VmRead = 0x0010,
        VmWrite = 0x0020,
        CreateThread = 0x0002,
        QueryLimitedInformation = 0x1000,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeProcessHandle OpenProcess(ProcessAccess access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, [Out] byte[] buffer, IntPtr size, out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(SafeProcessHandle process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr bytesWritten);
}
