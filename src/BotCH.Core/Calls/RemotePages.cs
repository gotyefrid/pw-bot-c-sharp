using System;
using System.Runtime.InteropServices;
using BotCH.Core.Memory;

namespace BotCH.Core.Calls;

/// <summary>
/// Страницы в памяти игры под наши вызовы (VirtualAllocEx / VirtualProtectEx / VirtualFreeEx): одноразовая «заглушка + данные»
/// и постоянная — для обработчика окна. Код самой игры не меняется. Нужен дескриптор с правом <see cref="GameProcessRights.Execute"/>.
/// </summary>
public sealed class RemotePages
{
    private const int PageSize = 0x1000;
    private const int DataOffset = 0x100;

    private readonly GameProcess _game;

    public RemotePages(GameProcess game)
    {
        if (!game.Rights.HasFlag(GameProcessRights.Execute))
            throw new InvalidOperationException("Процесс открыт без права вызывать функции игры");
        _game = game;
    }

    /// <summary>
    /// Страница под вызов: данные со смещения 0x100, заглушка в начале, защита «чтение и выполнение».
    /// null — готово, адрес в <paramref name="page"/> (освободить <see cref="Free"/>, когда выполнится); иначе — отказ.
    /// </summary>
    public RemoteRunResult? Prepare(byte[]? data, Func<uint, byte[]> buildStub, out uint page)
    {
        page = 0;
        if (data is { Length: > PageSize - DataOffset })
            return new RemoteRunResult(RemoteRunStatus.Failed, "слишком много данных для вызова");

        var memory = NativeMethods.VirtualAllocEx(_game.Handle, IntPtr.Zero, new IntPtr(PageSize),
            NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
        if (memory == IntPtr.Zero)
            return Failed("VirtualAllocEx");

        var pageAddress = unchecked((uint)memory.ToInt32());
        var dataAddress = pageAddress + DataOffset;
        RemoteRunResult? problem = null;
        if (data is not null && !_game.TryWrite(dataAddress, data))
            problem = Failed("запись данных");

        if (problem is null)
        {
            var stub = buildStub(dataAddress);
            if (stub.Length > DataOffset)
                problem = new RemoteRunResult(RemoteRunStatus.Failed, "заглушка не помещается перед данными");
            else if (!_game.TryWrite(pageAddress, stub)
                     || !NativeMethods.VirtualProtectEx(_game.Handle, memory, new IntPtr(PageSize), NativeMethods.PageExecuteRead, out _))
                problem = Failed("запись заглушки");
        }

        if (problem is not null)
        {
            Free(pageAddress);
            return problem;
        }

        page = pageAddress;
        return null;
    }

    public void Free(uint page) => NativeMethods.VirtualFreeEx(_game.Handle, GameProcess.ToPointer(page), IntPtr.Zero, NativeMethods.MemRelease);

    /// <summary>
    /// Постоянная страница «код + свои данные» (чтение, запись, выполнение) — для обработчика окна: живёт до закрытия игры,
    /// не освобождается (на её коде может стоять чей-то стек). null — не вышло.
    /// </summary>
    public uint? AllocateResident(byte[] content)
    {
        if (content.Length > PageSize)
            return null;

        var memory = NativeMethods.VirtualAllocEx(_game.Handle, IntPtr.Zero, new IntPtr(PageSize),
            NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageExecuteReadWrite);
        if (memory == IntPtr.Zero)
            return null;

        var address = unchecked((uint)memory.ToInt32());
        if (_game.TryWrite(address, content))
            return address;

        Free(address);
        return null;
    }

    /// <summary>Отказ шага с ошибкой Windows (сразу после неудачного вызова — пока ошибку не перезаписали).</summary>
    internal static RemoteRunResult Failed(string step)
    {
        var error = Marshal.GetLastWin32Error();
        // 8 — ERROR_NOT_ENOUGH_MEMORY: у игры кончилось адресное пространство, лечится только перезапуском клиента
        return new(RemoteRunStatus.Failed, error == 8
            ? $"{step}: игре не хватает памяти (ошибка Windows 8) — перезапустите клиент"
            : $"{step}: ошибка Windows {error}");
    }
}
