using System;
using System.Threading;

namespace BotCH.Core.Clients;

/// <summary>
/// Метка «этот клиент занят окном бота»: именованный объект Windows, имя — отпечаток PID клиента (<see cref="MutexNames"/>).
/// Живёт, пока окно подключено (и само исчезает, если окно бота закрылось или упало) — другое окно бота при запуске выберет
/// свободный клиент.
/// </summary>
public sealed class ClientLock : IDisposable
{
    private readonly Mutex _mark;

    private ClientLock(Mutex mark) => _mark = mark;

    private static string NameFor(int pid) => MutexNames.For("client", pid.ToString());

    /// <summary>Занять клиент; null — его уже занимает другое окно бота.</summary>
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
