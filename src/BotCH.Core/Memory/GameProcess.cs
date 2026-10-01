using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace BotCH.Core.Memory;

/// <summary>Что разрешено делать с процессом игры. Чтение есть всегда.</summary>
[Flags]
public enum GameProcessRights
{
    Read = 0,
    /// <summary>Запись в память (unfreeze, данные для вызовов).</summary>
    Write = 1,
    /// <summary>Вызов функций игры: выделение памяти и создание потока (часть 4).</summary>
    Execute = 2,
}

/// <summary>
/// Открытый процесс клиента игры. Один экземпляр — один клиент.
/// Сам дескриптор процесса держится открытым, пока объект не освобождён (Dispose).
/// </summary>
public sealed class GameProcess : IMemory, IDisposable
{
    private readonly SafeProcessHandle _handle;
    private readonly Process _process;

    public int Pid { get; }
    public string ProcessName { get; }
    public GameProcessRights Rights { get; }

    /// <summary>Адрес, по которому загружен exe клиента (ElementClient.exe). От него считаются статические смещения.</summary>
    public uint MainModuleBase { get; }

    public int MainModuleSize { get; }

    public bool HasExited => _process.HasExited;

    private GameProcess(Process process, SafeProcessHandle handle, GameProcessRights rights)
    {
        _process = process;
        _handle = handle;
        Pid = process.Id;
        ProcessName = process.ProcessName;
        Rights = rights;

        // Бот и клиент оба 32-битные — MainModule доступен без ухищрений
        var module = process.MainModule ?? throw new InvalidOperationException($"У процесса {Pid} нет главного модуля");
        MainModuleBase = (uint)module.BaseAddress.ToInt64();
        MainModuleSize = module.ModuleMemorySize;
    }

    public static GameProcess Open(int pid, GameProcessRights rights = GameProcessRights.Read)
    {
        var process = Process.GetProcessById(pid);

        var access = NativeMethods.ProcessAccess.VmRead | NativeMethods.ProcessAccess.QueryLimitedInformation;
        if (rights.HasFlag(GameProcessRights.Write))
            access |= NativeMethods.ProcessAccess.VmWrite | NativeMethods.ProcessAccess.VmOperation;
        if (rights.HasFlag(GameProcessRights.Execute))
            access |= NativeMethods.ProcessAccess.VmWrite | NativeMethods.ProcessAccess.VmOperation | NativeMethods.ProcessAccess.CreateThread;

        var handle = NativeMethods.OpenProcess(access, false, pid);
        if (handle.IsInvalid)
            throw new Win32Exception($"Не удалось открыть процесс {pid}");

        try
        {
            return new GameProcess(process, handle, rights);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public bool TryRead(uint address, byte[] buffer, int count)
    {
        if (count < 0 || count > buffer.Length)
            return false;
        if (count == 0)
            return true;

        return NativeMethods.ReadProcessMemory(_handle, new IntPtr((long)address), buffer, new IntPtr(count), out var read)
            && read.ToInt64() == count;
    }

    public bool TryWrite(uint address, byte[] data)
    {
        if (!Rights.HasFlag(GameProcessRights.Write) && !Rights.HasFlag(GameProcessRights.Execute))
            throw new InvalidOperationException("Процесс открыт только для чтения");

        return NativeMethods.WriteProcessMemory(_handle, new IntPtr((long)address), data, new IntPtr(data.Length), out var written)
            && written.ToInt64() == data.Length;
    }

    public void Dispose()
    {
        _handle.Dispose();
        _process.Dispose();
    }
}
