using System;
using BotCH.Core.Memory;

namespace BotCH.Core.Calls;

/// <summary>
/// Запасной способ вызова (как старый GameCall): заглушка выполняется своим потоком в игре — страница под код и данные,
/// CreateRemoteThread, ожидание до 5 с, освобождение. Код самой игры не меняется. Основной способ — <see cref="WindowCallRunner"/>
/// (в главном потоке игры); потоком же он ставит и снимает свой обработчик окна.
/// </summary>
public sealed class ThreadCallRunner(GameProcess game) : IRemoteRunner
{
    // Стек нашего потока. По умолчанию Windows резервирует как у exe игры (1 МБ); когда адресное пространство
    // игры (2 ГБ) забито и раздроблено, 1 МБ подряд не находится — CreateRemoteThread падает с ошибкой 8.
    // Функциям отправки пакетов 256 КБ хватает с большим запасом.
    private const int StackSize = 0x40000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly RemotePages _pages = new(game);

    public RemoteRunResult Run(byte[]? data, Func<uint, byte[]> buildStub) => Run(data, buildStub, out _);

    /// <summary>То же, плюс что заглушка вернула в eax (код выхода потока); 0, если не выполнена.</summary>
    public RemoteRunResult Run(byte[]? data, Func<uint, byte[]> buildStub, out uint returned)
    {
        returned = 0;
        if (_pages.Prepare(data, buildStub, out var page) is { } failed)
            return failed;

        var free = true;
        try
        {
            using var thread = NativeMethods.CreateRemoteThread(game.Handle, IntPtr.Zero, new IntPtr(StackSize), GameProcess.ToPointer(page),
                IntPtr.Zero, NativeMethods.StackSizeParamIsAReservation, IntPtr.Zero);
            if (thread.IsInvalid)
                return RemotePages.Failed("CreateRemoteThread");

            if (NativeMethods.WaitForSingleObject(thread, (uint)Timeout.TotalMilliseconds) != NativeMethods.WaitObject0)
            {
                // Поток ещё работает — память не освобождаем, иначе игра упадёт
                free = false;
                return new RemoteRunResult(RemoteRunStatus.Timeout, $"поток в игре не закончился за {Timeout.TotalSeconds:0} с");
            }

            if (!NativeMethods.GetExitCodeThread(thread, out returned))
                returned = 0;
            return new RemoteRunResult(RemoteRunStatus.Done);
        }
        finally
        {
            if (free)
                _pages.Free(page);
        }
    }
}
