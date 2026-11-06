using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
          login --character Ник [--accounts <charactersccounts.json>] [--exe <exe>] [--pid PID] [--retry] [--check]
                                                вход по этапам, как «Персонажи»: клиент без пароля в аргументах, бот
                                                сам вводит логин и пароль и выбирает ник. --pid — клиент уже открыт на
                                                экране логина; --retry — повторы через 1, 3, 5 мин
        """;

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

        if (options.Character is not null)
            return Steps(options);

        ClientLaunch launch;
        try
        {
            launch = Build(options, profile);
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
            var result = ClientLogin.EnterWorld(process.Id, profile.Data, TimeSpan.FromSeconds(options.Wait),
                options.Enter ? 2 : 0, Note);
            switch (result.State)
            {
                case LoginState.InWorld:
                    Console.WriteLine($"✅ Вошёл: {result.Nick} (PID {process.Id}, {result.Elapsed.TotalSeconds:0} с).");
                    return 0;
                case LoginState.Exited:
                    Console.Error.WriteLine($"❌ Клиент закрылся, не дойдя до мира ({result.Elapsed.TotalSeconds:0} с).");
                    return 3;
            }

            if (!result.WindowFound)
            {
                Console.Error.WriteLine($"❌ За {options.Wait} с окно клиента не появилось.");
                return 3;
            }

            Console.Error.WriteLine($"❌ Персонаж не появился за {result.Elapsed.TotalSeconds:0} с. "
                                    + "Клиент стоит на входе или на выборе персонажа — посмотрите окно игры.");
            if (result.Problem is { } problem)
                Console.Error.WriteLine($"   Последняя ошибка чтения: {problem}");
            return 3;
        }
    }

    private static void Note(string message) => Console.WriteLine($"  {message}");

    // Вход по этапам персонажем из «Персонажей»: логин и пароль бот вводит сам, пароль в консоль не печатается
    private static int Steps(Options options)
    {
        var path = options.Accounts ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "characters", CharacterBook.FileName);
        var accounts = new CharacterBook(path).Load(out var problem);
        if (problem is not null)
            Console.Error.WriteLine($"⚠️ {problem}");
        var account = accounts.FirstOrDefault(a => a.Nick == options.Character);
        if (account is null)
        {
            Console.Error.WriteLine($"❌ В {path} нет персонажа {options.Character} (есть: {string.Join(", ", accounts.Select(a => a.Nick))})");
            return 2;
        }

        var profile = ProfileCatalog.Default().Load(account.Server);
        if (!LoginScreen.Supported(profile.Data))
        {
            Console.Error.WriteLine($"❌ В профиле {profile.Id} нет окон клиента (loginScreen) — вход по этапам там ещё не проверен");
            return 2;
        }

        ClientLaunch launch;
        string password;
        try
        {
            launch = account.Launch(profile.Data.LaunchArguments, options.Exe, credentials: false);
            password = account.PlainPassword();
        }
        catch (InvalidOperationException e)
        {
            Console.Error.WriteLine($"❌ {e.Message}{(account.ExePath.Length == 0 && options.Exe is null ? " (в Probe — ключ --exe)" : "")}");
            return 2;
        }

        Console.WriteLine($"Персонаж: {account.Nick}, сервер {profile.Name}, логин есть, пароль есть (в консоль не печатаю)");
        Console.WriteLine($"exe:      {launch.ExePath} {string.Join(" ", launch.Arguments)}");
        if (options.Check)
            return 0;

        var same = RunningSame(launch.ExePath);
        if (options.Pid is null && same.Count > 0 && !options.Force)
        {
            Console.Error.WriteLine($"❌ Клиент этого exe уже запущен (PID {string.Join(", ", same)}): --pid, чтобы войти в него, "
                                    + "или --force, чтобы открыть ещё один.");
            return 3;
        }

        using var cancel = new System.Threading.CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        var started = DateTime.Now;
        var runner = new LoginRunner(() =>
            {
                using var process = launch.Start();
                Console.WriteLine($"  клиент запущен: PID {process.Id}");
                return process.Id;
            }, pid => new ClientLoginAttempt(pid, profile.Data), ClientLoginAttempt.Close)
            { Delays = options.Retry ? LoginRunner.DefaultDelays : [] };
        var run = runner.Run(account.Login.Trim(), password, account.Nick, text => Console.WriteLine($"  {(DateTime.Now - started).TotalSeconds,5:0.0} с  {text}"),
            cancel.Token, options.Pid);
        var mark = run.Result.Ok ? "✅" : "❌";
        Console.WriteLine($"{mark} {run.Result.Outcome}: {run.Result.Text} (PID {run.Pid?.ToString() ?? "—"}, попыток {run.Attempts}, "
                          + $"{(DateTime.Now - started).TotalSeconds:0} с)");
        return run.Result.Ok ? 0 : 3;
    }

    /// <summary>Либо файл запуска, либо логин/пароль/роль и exe — вместе нельзя: непонятно, что главнее.</summary>
    private static ClientLaunch Build(Options options, ServerProfile profile)
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
        // Постоянные аргументы — из профиля сервера, как в bat его клиентов
        if (profile.Data.LaunchArguments.Count == 0)
            throw new ArgumentException($"В профиле {profile.Id} нет launchArguments — запуск без файла не настроен.");
        return ClientLaunch.WithCredentials(exe, directory, profile.Data.LaunchArguments,
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
                case "--character":
                    options.Character = Next(args, ref index, "--character");
                    break;
                case "--accounts":
                    options.Accounts = Next(args, ref index, "--accounts");
                    break;
                case "--pid":
                    options.Pid = int.TryParse(Next(args, ref index, "--pid"), out var pid) ? pid : throw new ArgumentException("У --pid не число.");
                    break;
                case "--retry":
                    options.Retry = true;
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
        public string? Character { get; set; }
        public string? Accounts { get; set; }
        public int? Pid { get; set; }
        public bool Retry { get; set; }
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
