using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Clients;
using Notes.Core.Profiles;

namespace Notes.Probe;

/// <summary>
/// Тестовый автовход: открыть клиент по файлу запуска (bat или ярлык), дождаться персонажа и, если клиент застрял на
/// выборе, нажать Enter. Пароль читается из файла и в консоль не печатается. Живой запуск — по команде владельца.
/// </summary>
internal static class LoginCommand
{
    public const string Usage = """
          login <файл.bat|.lnk> [--server id] [--wait с] [--no-enter] [--force]
                                    открыть клиент по данным файла запуска и войти персонажем (пароль не печатается)
        """;

    private static readonly TimeSpan EnterWait = TimeSpan.FromSeconds(20);

    public static int Run(ServerProfile profile, string[] args)
    {
        var path = PathArgument(args);
        if (path is null)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var wait = Seconds(args, "--wait", 90);
        var enter = !args.Contains("--no-enter");
        var force = args.Contains("--force");

        ClientLaunch launch;
        try
        {
            launch = LaunchFile.Read(path);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"❌ {e.Message}");
            return 2;
        }

        Console.WriteLine($"Файл:    {launch.SourcePath}");
        Console.WriteLine($"exe:     {launch.ExePath}");
        Console.WriteLine($"папка:   {launch.WorkingDirectory}");
        Console.WriteLine($"логин:   {launch.Login ?? "—"}");
        Console.WriteLine($"роль:    {launch.Role ?? "—"}");
        Console.WriteLine($"пароль:  {(launch.HasPassword ? "есть (в консоль не печатаю)" : "нет")}");
        Console.WriteLine($"сервер:  {profile.Name} ({profile.Id})");

        if (!File.Exists(launch.ExePath))
        {
            Console.Error.WriteLine($"❌ Нет exe клиента: {launch.ExePath}");
            return 2;
        }

        var same = RunningSame(launch.ExePath);
        if (same.Count > 0 && !force)
        {
            Console.Error.WriteLine($"❌ Клиент этого exe уже запущен (PID {string.Join(", ", same)}). "
                                    + "Закройте его или добавьте --force, чтобы открыть второй.");
            return 3;
        }

        if (same.Count > 0)
            Console.WriteLine($"⚠️ Уже запущен клиент этого exe (PID {string.Join(", ", same)}) — открываю ещё один (--force).");

        Process process;
        try
        {
            process = launch.Start();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"❌ Не удалось запустить клиент: {e.Message}");
            return 2;
        }

        using (process)
        {
            Console.WriteLine($"Клиент запущен: PID {process.Id}. Жду окно и персонажа…");
            var started = Stopwatch.StartNew();
            var result = ClientLogin.WaitForWorld(process.Id, profile.Data, TimeSpan.FromSeconds(wait), Note);
            if (result.State == LoginState.InWorld)
                return Success(result, process.Id, started.Elapsed);

            if (result.State == LoginState.Exited)
            {
                Console.Error.WriteLine($"❌ Клиент закрылся, не дойдя до мира ({started.Elapsed.TotalSeconds:0} с).");
                return 3;
            }

            if (!result.WindowFound)
            {
                Console.Error.WriteLine($"❌ За {wait} с окно клиента не появилось.");
                return 3;
            }

            if (enter)
            {
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    Console.WriteLine($"⚠️ Персонаж не появился за {wait} с — похоже, клиент стоит на выборе. "
                                      + $"Жму Enter (попытка {attempt} из 2).");
                    var keys = new WindowKeys(process.Id);
                    var pressed = keys.Press(new KeyStroke(0x0D));
                    Console.WriteLine(pressed.Ok ? "  Enter отправлена." : $"  ❌ {pressed.Details}");
                    if (!pressed.Ok)
                        break;

                    result = ClientLogin.WaitForWorld(process.Id, profile.Data, EnterWait, Note);
                    if (result.State == LoginState.InWorld)
                        return Success(result, process.Id, started.Elapsed);
                    if (result.State == LoginState.Exited)
                    {
                        Console.Error.WriteLine($"❌ Клиент закрылся ({started.Elapsed.TotalSeconds:0} с).");
                        return 3;
                    }
                }
            }

            Console.Error.WriteLine($"❌ Персонаж не появился за {started.Elapsed.TotalSeconds:0} с. "
                                    + "Клиент стоит на входе или на выборе персонажа — посмотрите окно игры.");
            if (result.Problem is { } problem)
                Console.Error.WriteLine($"   Последняя ошибка чтения: {problem}");
            return 3;
        }
    }

    private static int Success(LoginWait result, int pid, TimeSpan elapsed)
    {
        Console.WriteLine($"✅ Вошёл: {result.Nick} (PID {pid}, {elapsed.TotalSeconds:0} с).");
        return 0;
    }

    private static void Note(string message) => Console.WriteLine($"  {message}");

    private static string? PathArgument(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--server" or "--wait")
            {
                i++;
                continue;
            }

            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                return args[i];
        }

        return null;
    }

    private static int Seconds(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value)
            ? Math.Min(600, Math.Max(10, value))
            : fallback;
    }

    /// <summary>PID уже запущенных клиентов ровно с этим exe (другие серверы и папки не трогаем).</summary>
    private static List<int> RunningSame(string exePath)
        => SystemClientSource.Running()
            .Where(client => SamePath(client.ExePath, exePath))
            .Select(client => client.Pid)
            .ToList();

    private static bool SamePath(string? first, string second)
    {
        try
        {
            return first is not null
                   && string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
