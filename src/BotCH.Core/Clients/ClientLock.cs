using System;
using System.Threading;

namespace BotCH.Core.Clients;

/// <summary>
/// Метка «этот клиент занят окном BotCH»: именованный объект Windows с PID клиента. Живёт, пока окно подключено
/// (и само исчезает, если окно бота закрылось или упало) — другое окно BotCH при запуске выберет свободный клиент.
/// </summary>
public sealed class ClientLock : IDisposable
{
    private readonly Mutex _mark;

    private ClientLock(Mutex mark) => _mark = mark;

    private static string NameFor(int pid) => $@"Local\BotCH-client-{pid}";

    /// <summary>Занять клиент; null — его уже занимает другое окно BotCH.</summary>
    public static ClientLock? TryTake(int pid)
    {
        var mark = new Mutex(initiallyOwned: false, NameFor(pid), out var created);
        if (created)
            return new ClientLock(mark);

        mark.Dispose();
        return null;
    }

    public static bool IsTaken(int pid)
    {
        if (!Mutex.TryOpenExisting(NameFor(pid), out var mark))
            return false;

        mark.Dispose();
        return true;
    }

    public void Dispose() => _mark.Dispose();
}
