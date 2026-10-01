using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.World;

namespace BotCH.Core.Clients;

/// <summary>Запущенный клиент игры.</summary>
public sealed record GameClient(int Pid, string? Nick, IntPtr Window)
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

/// <summary>Откуда берутся процессы и ники. Подменяется в тестах.</summary>
public interface IClientSource
{
    /// <summary>PID и главное окно всех процессов с таким именем.</summary>
    IReadOnlyList<(int Pid, IntPtr Window)> FindProcesses(string processName);

    /// <summary>Ник персонажа в клиенте или null (экран выбора, нет доступа).</summary>
    string? ReadNick(int pid);
}

public static class ClientList
{
    /// <summary>Клиенты по возрастанию PID — порядок не прыгает при обновлении списка.</summary>
    public static List<GameClient> Build(IClientSource source, string processName)
        => source.FindProcesses(processName)
            .OrderBy(p => p.Pid)
            .Select(p => new GameClient(p.Pid, source.ReadNick(p.Pid), p.Window))
            .ToList();

    /// <summary>
    /// Кого выбрать после обновления списка: того же по PID, если он ещё жив, иначе первого.
    /// Так обновление списка и переименование окон не подменяют подключённый клиент.
    /// </summary>
    public static GameClient? KeepSelection(IReadOnlyList<GameClient> clients, int? selectedPid)
        => clients.FirstOrDefault(c => c.Pid == selectedPid) ?? clients.FirstOrDefault();
}

/// <summary>Настоящие процессы и память. Ник читается отдельным дескриптором только на чтение.</summary>
public sealed class SystemClientSource(ProfileData profile) : IClientSource
{
    public IReadOnlyList<(int Pid, IntPtr Window)> FindProcesses(string processName)
    {
        var result = new List<(int, IntPtr)>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
                result.Add((process.Id, NativeWindows.FindMainWindow(process.Id)));
        }

        return result;
    }

    public string? ReadNick(int pid)
    {
        try
        {
            using var game = GameProcess.Open(pid);
            var name = new WorldReader(game, game.MainModuleBase, profile).TryReadHostName();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }
}
