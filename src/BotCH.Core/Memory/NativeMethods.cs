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
        QueryInformation = 0x0400,
        QueryLimitedInformation = 0x1000,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeProcessHandle OpenProcess(ProcessAccess access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, [Out] byte[] buffer, IntPtr size, out IntPtr bytesRead);

    public const uint MemCommit = 0x1000;
    public const uint MemReserve = 0x2000;
    public const uint MemRelease = 0x8000;
    public const uint PageReadWrite = 0x04;
    public const uint PageExecuteRead = 0x20;
    public const uint WaitObject0 = 0;
    public const uint MemFree = 0x10000;
    /// <summary>dwStackSize у CreateRemoteThread — сколько адресов зарезервировать под стек, а не сколько выделить сразу.</summary>
    public const uint StackSizeParamIsAReservation = 0x10000;

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualQueryEx(SafeProcessHandle process, IntPtr address, out MemoryBasicInformation info, IntPtr length);

    [DllImport("kernel32.dll", EntryPoint = "K32GetMappedFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetMappedFileName(SafeProcessHandle process, IntPtr address, System.Text.StringBuilder name, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(SafeProcessHandle process, IntPtr address, IntPtr size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(SafeProcessHandle process, IntPtr address, IntPtr size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualProtectEx(SafeProcessHandle process, IntPtr address, IntPtr size, uint newProtect, out uint oldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeThreadHandle CreateRemoteThread(SafeProcessHandle process, IntPtr attributes, IntPtr stackSize, IntPtr start, IntPtr parameter, uint flags, IntPtr threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(SafeThreadHandle handle, uint milliseconds);

    /// <summary>Дескриптор потока: закрывается сам (CloseHandle).</summary>
    public sealed class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeThreadHandle()
            : base(true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(SafeProcessHandle process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr bytesWritten);
}
