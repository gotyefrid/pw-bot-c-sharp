using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Clients;

/// <summary>Запущенный клиент игры.</summary>
public sealed record GameClient(int Pid, string? Nick, IntPtr Window, string? ExePath)
{
    /// <summary>Как показать в списке выбора.</summary>
    public string Display => Nick is null ? $"PID {Pid} — персонаж не в мире" : $"{Nick} (PID {Pid})";

    /// <summary>Заголовок окна игры: «Ник PID», без ника — просто PID (как в старом боте).</summary>
    public string WindowTitle => WindowTitles.For(Nick, Pid);
}

public static class WindowTitles
{
    public static string For(string? nick, int pid) => $"{nick} {pid}".Trim();
}

/// <summary>Процесс клиента игры, как его видит система: PID, главное окно, путь exe (null — не достать: права).</summary>
public sealed record ClientProcess(int Pid, IntPtr Window, string? ExePath);

/// <summary>Откуда берутся процессы и ники. Подменяется в тестах.</summary>
public interface IClientSource
{
    /// <summary>Все запущенные клиенты игры.</summary>
    IReadOnlyList<ClientProcess> FindProcesses();

    /// <summary>Ник персонажа в клиенте или null (экран выбора, нет доступа).</summary>
    string? ReadNick(int pid);
}

public static class ClientList
{
    /// <summary>Клиенты по возрастанию PID — порядок не прыгает при обновлении списка.</summary>
    public static List<GameClient> Build(IClientSource source)
        => source.FindProcesses()
            .OrderBy(p => p.Pid)
            .Select(p => new GameClient(p.Pid, source.ReadNick(p.Pid), p.Window, p.ExePath))
            .ToList();

    /// <summary>
    /// Кого выбрать после обновления списка: того же по PID, если он ещё жив — обновление списка и переименование окон
    /// не подменяют подключённый клиент. Иначе (запуск бота, клиент закрылся) — из свободных (не занятых другим окном бота):
    /// клиент последнего персонажа, потом первый, у кого читается ник (ник читается, только если профиль сервера подходит
    /// к клиенту), потом первый свободный.
    /// </summary>
    public static GameClient? KeepSelection(IReadOnlyList<GameClient> clients, int? selectedPid, string? lastNick = null, Func<int, bool>? isTaken = null)
    {
        if (clients.FirstOrDefault(c => c.Pid == selectedPid) is { } same)
            return same;

        var free = clients.Where(c => isTaken?.Invoke(c.Pid) != true).ToList();
        return free.FirstOrDefault(c => !string.IsNullOrEmpty(lastNick) && c.Nick == lastNick)
            ?? free.FirstOrDefault(c => c.Nick is not null)
            ?? free.FirstOrDefault()
            ?? clients.FirstOrDefault();
    }
}

/// <summary>Настоящие процессы и память. Ник читается отдельным дескриптором только на чтение.</summary>
public sealed class SystemClientSource(ProfileData profile) : IClientSource
{
    public IReadOnlyList<ClientProcess> FindProcesses() => Running();

    /// <summary>
    /// Клиенты, запущенные прямо сейчас, — свежий опрос системы (ники не читаются, профиль не нужен).
    /// Клиент — процесс с окном игры, имя exe не важно: у Comeback есть ElementClient.exe и ElementClient_test.exe,
    /// игроки запускают любой; у PW Classic — client.exe (старый elementclient.exe бот не поддерживает).
    /// </summary>
    public static IReadOnlyList<ClientProcess> Running()
        => NativeWindows.FindClientProcesses()
            .Select(pid => new ClientProcess(pid, NativeWindows.FindMainWindow(pid), MainModulePath(pid)))
            .ToList();

    /// <summary>Путь exe процесса; null — процесс закрылся или пути не достать (права).</summary>
    private static string? MainModulePath(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch (Exception e) when (e is ArgumentException or Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    public string? ReadNick(int pid)
    {
        try
        {
            using var game = GameProcess.Open(pid);
            var name = new WorldReader(game, GameRoots.Find(game, game.MainModuleBase, profile), profile).TryReadHostName();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }
}
