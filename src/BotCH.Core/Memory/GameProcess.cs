using System;
using System.ComponentModel;
using System.Diagnostics;
using BotCH.Core.Calls;
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
public sealed class GameProcess : IMemory, IRemoteRunner, IDisposable
{
    private readonly SafeProcessHandle _handle;
    private readonly Process _process;

    public int Pid { get; }
    public string ProcessName { get; }
    public GameProcessRights Rights { get; }

    /// <summary>Адрес, по которому загружен exe клиента (ElementClient.exe). От него считаются статические смещения.</summary>
    public uint MainModuleBase { get; }

    public int MainModuleSize { get; }

    /// <summary>Полный путь к exe клиента — рядом лежат файлы игры (configs.pck).</summary>
    public string MainModulePath { get; }

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
        MainModulePath = module.FileName;
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

        return NativeMethods.ReadProcessMemory(_handle, ToPointer(address), buffer, new IntPtr(count), out var read)
            && read.ToInt64() == count;
    }

    public bool TryWrite(uint address, byte[] data)
    {
        if (!Rights.HasFlag(GameProcessRights.Write) && !Rights.HasFlag(GameProcessRights.Execute))
            throw new InvalidOperationException("Процесс открыт только для чтения");

        return NativeMethods.WriteProcessMemory(_handle, ToPointer(address), data, new IntPtr(data.Length), out var written)
            && written.ToInt64() == data.Length;
    }

    private const int RemotePageSize = 0x1000;
    private const int RemoteDataOffset = 0x100;
    private static readonly TimeSpan RemoteTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Выполняет заглушку потоком в игре (как старый GameCall): страница под код и данные, запись, защита «только
    /// чтение и выполнение», CreateRemoteThread, ожидание до 5 с, освобождение. Код самой игры не меняется.
    /// Нужны права <see cref="GameProcessRights.Execute"/>.
    /// </summary>
    public RemoteRunResult Run(byte[]? data, Func<uint, byte[]> buildStub)
    {
        if (!Rights.HasFlag(GameProcessRights.Execute))
            throw new InvalidOperationException("Процесс открыт без права вызывать функции игры");
        if (data is { Length: > RemotePageSize - RemoteDataOffset })
            return new RemoteRunResult(RemoteRunStatus.Failed, "слишком много данных для вызова");

        var page = NativeMethods.VirtualAllocEx(_handle, IntPtr.Zero, new IntPtr(RemotePageSize),
            NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
        if (page == IntPtr.Zero)
            return Failed("VirtualAllocEx");

        var free = true;
        try
        {
            var pageAddress = unchecked((uint)page.ToInt32());
            var dataAddress = pageAddress + RemoteDataOffset;
            if (data is not null && !TryWriteRaw(dataAddress, data))
                return Failed("запись данных");

            var stub = buildStub(dataAddress);
            if (stub.Length > RemoteDataOffset)
                return new RemoteRunResult(RemoteRunStatus.Failed, "заглушка не помещается перед данными");
            if (!TryWriteRaw(pageAddress, stub)
                || !NativeMethods.VirtualProtectEx(_handle, page, new IntPtr(RemotePageSize), NativeMethods.PageExecuteRead, out _))
                return Failed("запись заглушки");

            using var thread = NativeMethods.CreateRemoteThread(_handle, IntPtr.Zero, IntPtr.Zero, page, IntPtr.Zero, 0, IntPtr.Zero);
            if (thread.IsInvalid)
                return Failed("CreateRemoteThread");

            if (NativeMethods.WaitForSingleObject(thread, (uint)RemoteTimeout.TotalMilliseconds) != NativeMethods.WaitObject0)
            {
                // Поток ещё работает — память не освобождаем, иначе игра упадёт
                free = false;
                return new RemoteRunResult(RemoteRunStatus.Timeout, $"поток в игре не закончился за {RemoteTimeout.TotalSeconds:0} с");
            }

            return new RemoteRunResult(RemoteRunStatus.Done);
        }
        finally
        {
            if (free)
                NativeMethods.VirtualFreeEx(_handle, page, IntPtr.Zero, NativeMethods.MemRelease);
        }
    }

    private bool TryWriteRaw(uint address, byte[] data)
        => NativeMethods.WriteProcessMemory(_handle, ToPointer(address), data, new IntPtr(data.Length), out var written)
           && written.ToInt64() == data.Length;

    private static RemoteRunResult Failed(string step)
        => new(RemoteRunStatus.Failed, $"{step}: ошибка Windows {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");

    // В 32-битном процессе IntPtr(long) для адреса ≥ 0x80000000 бросает OverflowException — берём те же 32 бита как int
    private static IntPtr ToPointer(uint address) => new(unchecked((int)address));

    public void Dispose()
    {
        _handle.Dispose();
        _process.Dispose();
    }
}
