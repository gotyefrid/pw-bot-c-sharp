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
/// Тестовый автовход: открыть клиент по файлу запуска (bat или ярлык) или по данным прямо из команды, дождаться
/// персонажа и, если клиент застрял на выборе, нажать Enter. Пароль в консоль не печатается. Живой запуск — по команде
/// владельца.
/// </summary>
internal static class LoginCommand
{
    public const string Usage = """
          login <файл.bat|.lnk> [ключи]       открыть клиент по данным файла запуска и войти персонажем
          login --login X --password Y --role Z --exe <exe> [ключи]
                                                то же без файла: логин, пароль и роль прямо в команде
          ключи: --server id, --wait с, --no-enter, --force, --check (только показать, не запускать)
        """;

    private static readonly TimeSpan EnterWait = TimeSpan.FromSeconds(20);

    public static int Run(ServerProfile profile, string[] args)
    {
        Options options;
        try
        {
            options = Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine($"❌ {e.Message}");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        ClientLaunch launch;
        try
        {
            launch = Build(options);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"❌ {e.Message}");
            return 2;
        }

        Console.WriteLine($"Файл:    {(options.File is null ? "данные команды" : launch.SourcePath)}");
        Console.WriteLine($"exe:     {launch.ExePath}");
        Console.WriteLine($"папка:   {launch.WorkingDirectory}");
        Console.WriteLine($"логин:   {launch.Login ?? "—"}");
        Console.WriteLine($"роль:    {launch.Role ?? "—"}");
        Console.WriteLine($"пароль:  {(launch.HasPassword ? "есть (в консоль не печатаю)" : "нет")}");
        Console.WriteLine($"сервер:  {profile.Name} ({profile.Id})");
        if (options.File is null)
            Console.WriteLine("⚠️ Пароль передан в командной строке: он виден в списке процессов, пока идёт запуск. "
                              + "Для постоянного автовхода лучше файл запуска.");

        if (!File.Exists(launch.ExePath))
        {
            Console.Error.WriteLine($"❌ Нет exe клиента: {launch.ExePath}");
            return 2;
        }

        if (options.Check)
        {
            Console.WriteLine("Проверка: клиент не запускается.");
            return 0;
        }

        var same = RunningSame(launch.ExePath);
        if (same.Count > 0 && !options.Force)
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
            var result = ClientLogin.WaitForWorld(process.Id, profile.Data, TimeSpan.FromSeconds(options.Wait), Note);
            if (result.State == LoginState.InWorld)
                return Success(result, process.Id, started.Elapsed);

            if (result.State == LoginState.Exited)
            {
                Console.Error.WriteLine($"❌ Клиент закрылся, не дойдя до мира ({started.Elapsed.TotalSeconds:0} с).");
                return 3;
            }

            if (!result.WindowFound)
            {
                Console.Error.WriteLine($"❌ За {options.Wait} с окно клиента не появилось.");
                return 3;
            }

            if (options.Enter)
            {
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    Console.WriteLine($"⚠️ Персонаж не появился за {options.Wait} с — похоже, клиент стоит на выборе. "
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

    /// <summary>Либо файл запуска, либо логин/пароль/роль и exe — вместе нельзя: непонятно, что главнее.</summary>
    private static ClientLaunch Build(Options options)
    {
        if (options.File is { } file)
        {
            if (options.Login is not null || options.Password is not null || options.Role is not null
                || options.Exe is not null || options.Directory is not null)
                throw new ArgumentException("Укажите либо файл запуска, либо --login/--password/--role/--exe.");
            return LaunchFile.Read(file);
        }

        if (options.Login is null || options.Password is null)
            throw new ArgumentException("Без файла нужны --login и --password.");
        if (options.Exe is null)
            throw new ArgumentException("Без файла нужен --exe — путь к ElementClient.exe.");

        var exe = Path.GetFullPath(options.Exe);
        var directory = options.Directory is null ? Path.GetDirectoryName(exe)! : Path.GetFullPath(options.Directory);
        // Те же постоянные аргументы, что в bat этих клиентов: через сторожа, игра cpw, с консолью
        return ClientLaunch.WithCredentials(exe, directory, ["startbypatcher", "game:cpw", "console:1"],
            options.Login, options.Password, options.Role);
    }

    private static Options Parse(string[] args)
    {
        var options = new Options();
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--server":
                    // Значение разбирает Program.LoadProfile; здесь только пропускаем, чтобы не принять за файл
                    if (++index >= args.Length)
                        throw new ArgumentException("У --server нет значения.");
                    break;
                case "--wait":
                    if (++index >= args.Length || !int.TryParse(args[index], out var wait))
                        throw new ArgumentException("У --wait нет числа секунд.");
                    options.Wait = Math.Min(600, Math.Max(10, wait));
                    break;
                case "--login":
                    options.Login = Next(args, ref index, "--login");
                    break;
                case "--password":
                    options.Password = Next(args, ref index, "--password");
                    break;
                case "--role":
                    options.Role = Next(args, ref index, "--role");
                    break;
                case "--exe":
                    options.Exe = Next(args, ref index, "--exe");
                    break;
                case "--dir":
                    options.Directory = Next(args, ref index, "--dir");
                    break;
                case "--no-enter":
                    options.Enter = false;
                    break;
                case "--force":
                    options.Force = true;
                    break;
                case "--check":
                    options.Check = true;
                    break;
                default:
                    if (args[index].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Неизвестный ключ: {args[index]}");
                    if (options.File is not null)
                        throw new ArgumentException("Укажите один файл запуска.");
                    options.File = args[index];
                    break;
            }
        }

        return options;
    }

    private static string Next(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"У {name} нет значения.");
        return args[++index];
    }

    private sealed class Options
    {
        public string? File { get; set; }
        public string? Login { get; set; }
        public string? Password { get; set; }
        public string? Role { get; set; }
        public string? Exe { get; set; }
        public string? Directory { get; set; }
        public int Wait { get; set; } = 90;
        public bool Enter { get; set; } = true;
        public bool Force { get; set; }
        public bool Check { get; set; }
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
