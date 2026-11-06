using System;
using System.Diagnostics;
using System.Threading;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Clients;

/// <summary>Чем кончилось ожидание входа персонажа.</summary>
public enum LoginState
{
    /// <summary>Ник прочитался — персонаж в мире.</summary>
    InWorld,

    /// <summary>Время вышло: окно есть, но персонаж так и не появился (вход или выбор персонажа).</summary>
    Timeout,

    /// <summary>Процесс клиента закрылся.</summary>
    Exited,
}

/// <param name="WindowFound">Окно игры успело появиться (значит, клиент не упал до окна).</param>
/// <param name="Problem">Последняя ошибка чтения памяти; null — ошибок не было.</param>
public sealed record LoginWait(LoginState State, string? Nick, bool WindowFound, TimeSpan Elapsed, string? Problem);

/// <summary>
/// Ждёт, пока запущенный клиент окажется в мире: читает ник из памяти, как список клиентов. Пока персонаж на входе
/// или на выборе, ник пустой — это не ошибка, а повод подождать. Память только читается.
/// </summary>
public static class ClientLogin
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(10);

    public static LoginWait WaitForWorld(int pid, ProfileData profile, TimeSpan timeout, Action<string>? progress = null)
    {
        var started = DateTime.UtcNow;
        var windowFound = false;
        var lastProgress = started;
        var problem = (string?)null;
        GameProcess? game = null;
        GameRoots? roots = null;
        try
        {
            while (DateTime.UtcNow - started < timeout)
            {
                if (!Alive(pid))
                    return new LoginWait(LoginState.Exited, null, windowFound, DateTime.UtcNow - started, "клиент закрылся");

                if (!windowFound && NativeWindows.FindMainWindow(pid, includeHidden: true) != IntPtr.Zero)
                {
                    windowFound = true;
                    progress?.Invoke("окно клиента найдено");
                }

                try
                {
                    game ??= GameProcess.Open(pid);
                    roots ??= GameRoots.Find(game, game.MainModuleBase, profile);
                    var nick = new WorldReader(game, roots, profile).TryReadHostName();
                    if (!string.IsNullOrEmpty(nick))
                        return new LoginWait(LoginState.InWorld, nick, windowFound, DateTime.UtcNow - started, null);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // Клиент ещё грузится: база или персонаж не появились. Закрываем дескриптор и пробуем заново
                    problem = e.Message;
                    game?.Dispose();
                    game = null;
                    roots = null;
                }

                var now = DateTime.UtcNow;
                if (now - lastProgress >= ProgressEvery)
                {
                    lastProgress = now;
                    progress?.Invoke($"жду персонажа: {(int)(now - started).TotalSeconds} с");
                }

                Thread.Sleep(Poll);
            }

            return new LoginWait(LoginState.Timeout, null, windowFound, DateTime.UtcNow - started, problem);
        }
        finally
        {
            game?.Dispose();
        }
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
