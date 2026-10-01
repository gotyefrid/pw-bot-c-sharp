using System;
using System.Diagnostics;
using System.Linq;
using BotCH.Core.Clients;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;

namespace BotCH.Probe;

/// <summary>
/// Проверки на живом клиенте. Все команды здесь — только чтение памяти игры: их можно запускать без риска.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var rest = args.Skip(1).ToArray();
        try
        {
            switch (args[0])
            {
                case "attach":
                    return Attach(rest);
                case "sigcheck":
                    return SigCheck(rest);
                case "sigmake":
                    return SigMake(rest);
                case "snapshot":
                    return WithClient(rest, WorldCommands.Snapshot);
                case "watch":
                    return WithClient(rest, (profile, game) => WorldCommands.Watch(profile, game));
                case "selftest":
                    return WithClient(rest, WorldCommands.SelfTest);
                case "cdfind":
                    return CooldownFinder.Find(LoadProfile(rest), rest);
                case "act":
                    return ActCommands.Act(LoadProfile(rest), rest);
                case "rename":
                    return Rename(rest);
                case "dump":
                    return WithClient(rest, (profile, game) => WorldCommands.Dump(profile, game, rest));
                case "scanint":
                    return WithClient(rest, (profile, game) => ScanCommands.ScanInt(profile, game, rest));
                case "scanstr":
                    return WithClient(rest, (profile, game) => ScanCommands.ScanStrings(profile, game, rest));
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"❌ {e.Message}");
            return 2;
        }

        Console.Error.WriteLine($"Неизвестная команда: {args[0]}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("BotCH.Probe <команда> [PID] [--server <id>]");
        Console.WriteLine("  attach    подключиться к клиенту и показать базовые адреса и ник");
        Console.WriteLine("  sigcheck  проверить все функции профиля: на месте ли, не переехали ли");
        Console.WriteLine("  sigmake   сделать длинные уникальные сигнатуры функций профиля (для обновления JSON)");
        Console.WriteLine("  snapshot  прочитать снимок мира: перс, мобы, лут, сумка, скиллы, пет");
        Console.WriteLine("  watch     снимок раз в 300 мс (для проверок «до/после»), Ctrl+C — выход");
        Console.WriteLine("  rename    переименовать окна всех клиентов в «Ник PID» и проверить заголовки (WinAPI, память только читается)");
        Console.WriteLine("  dump      [файл.dump] сохранить прочитанную снимком память в файл — фикстура для тестов без игры");
        Console.WriteLine("  scanint   ЧИСЛО... найти в структуре перса поля с этим значением (поиск «до/после»)");
        Console.WriteLine("  scanstr   [mob|npc|item|0xАДРЕС] найти в структуре указатели на строки (поиск поля «название»)");
        Console.WriteLine("  selftest  проверить, что снимок разумный (HP ≤ MaxHP, типы мобов…), и замерить скорость");
        Console.WriteLine();
        Console.WriteLine("ДЕЙСТВИЯ В ИГРЕ (вызывают функции клиента — запускает владелец):");
        Console.WriteLine(ActCommands.Usage);
        Console.WriteLine("  cdfind [pet-food|potion-hp|potion-mp]  использовать предмет и 15 с искать поле его перезарядки («до/после»)");
    }

    private static int Attach(string[] args)
    {
        var profile = LoadProfile(args);
        using var game = OpenClient(args);
        var data = profile.Data;
        Console.WriteLine($"Сервер:   {profile.Name}");
        Console.WriteLine($"Процесс:  {game.ProcessName} PID {game.Pid}, модуль 0x{game.MainModuleBase:X8} ({game.MainModuleSize / 1024} КБ)");

        var gameAddress = game.ReadPointerChain(game.MainModuleBase + data.Base.BasePointer, data.Base.Game);
        Console.WriteLine($"game      = 0x{gameAddress:X8}");

        var pers = game.ReadUInt32(gameAddress + data.Host.Struct);
        Console.WriteLine($"персонаж  = 0x{pers:X8}, vtable 0x{game.ReadUInt32(pers):X8}");
        Console.WriteLine($"ник       = {game.ReadUnicodeString(game.ReadUInt32(pers + data.Host.NamePointer))}");
        return 0;
    }

    private static int SigCheck(string[] args)
    {
        var profile = LoadProfile(args);
        using var game = OpenClient(args);
        var resolver = new FunctionResolver(game, game.MainModuleBase);

        Console.WriteLine($"{profile.Name}, PID {game.Pid}");
        var failed = 0;
        foreach (var location in resolver.ResolveAll(profile.Data))
        {
            var mark = location.Status switch
            {
                FunctionStatus.Found => "✅",
                FunctionStatus.Relocated => "⚠️",
                _ => "❌",
            };
            if (!location.IsUsable)
                failed++;

            var signature = profile.Data.Functions[location.Name].Signature;
            var unique = signature is null ? "" : $", в коде совпадений: {Count(resolver.FindEverywhere(signature).Count)}";
            Console.WriteLine($"{mark} {location.Name,-24} 0x{location.Address:X8}  {location.Details}{unique}");
        }

        Console.WriteLine(failed == 0 ? "Все функции на месте." : $"Не найдено/неоднозначно: {failed}");
        return failed == 0 ? 0 : 3;
    }

    // Меньше — сигнатура хрупкая: легко совпадёт с чужим кодом в обновлённом клиенте
    private const int MinFixedBytes = 10;

    private static string Count(int n) => n >= 16 ? "16+" : n.ToString();

    /// <summary>
    /// Для каждой функции профиля (по адресу из профиля) подбирает самую короткую сигнатуру от 12 байт,
    /// которая в коде клиента встречается ровно один раз.
    /// </summary>
    private static int SigMake(string[] args)
    {
        var profile = LoadProfile(args);
        using var game = OpenClient(args);
        var resolver = new FunctionResolver(game, game.MainModuleBase);

        foreach (var entry in profile.Data.Functions)
        {
            var (name, function) = (entry.Key, entry.Value);
            var address = game.MainModuleBase + function.Rva;
            var code = game.ReadBytes(address, 96);

            if (function.Signature is { } old && !old.Matches(code))
            {
                Console.WriteLine($"❌ {name}: по адресу 0x{address:X8} старая сигнатура не совпала — пропускаю");
                continue;
            }

            Signature? unique = null;
            for (var length = 12; length <= 64 && unique is null; length += 4)
            {
                var candidate = SignatureBuilder.FromCode(code, length, game.MainModuleBase, game.MainModuleSize);
                var hits = resolver.FindEverywhere(candidate);
                if (candidate.FixedCount >= MinFixedBytes && hits.Count == 1 && hits[0] == address)
                    unique = candidate;
            }

            Console.WriteLine(unique is null
                ? $"❌ {name}: уникальной сигнатуры до 64 байт не нашлось"
                : $"\"{name}\": \"{unique}\"");
        }

        return 0;
    }

    private static int Rename(string[] args)
    {
        var profile = LoadProfile(args);
        var clients = ClientList.Build(new SystemClientSource(profile.Data), profile.Data.ClientProcessName);
        if (clients.Count == 0)
        {
            Console.WriteLine("❌ Клиенты не найдены");
            return 1;
        }

        var failed = 0;
        foreach (var client in clients)
        {
            var before = NativeWindows.GetTitle(client.Window);
            NativeWindows.SetTitle(client.Window, client.WindowTitle);
            var after = NativeWindows.GetTitle(client.Window);
            var ok = after == client.WindowTitle;
            failed += ok ? 0 : 1;
            Console.WriteLine($"{(ok ? "✅" : "❌")} PID {client.Pid}: «{before}» → «{after}»");
        }

        return failed == 0 ? 0 : 5;
    }

    private static int WithClient(string[] args, Func<IServerProfile, GameProcess, int> command)
    {
        var profile = LoadProfile(args);
        using var game = OpenClient(args);
        return command(profile, game);
    }

    private static IServerProfile LoadProfile(string[] args)
    {
        var index = Array.IndexOf(args, "--server");
        var id = index >= 0 && index + 1 < args.Length ? args[index + 1] : "pwclassic136";
        return ProfileCatalog.Default().Load(id);
    }

    internal static GameProcess OpenClient(string[] args, GameProcessRights rights = GameProcessRights.Read)
    {
        // Число среди аргументов — PID, только если это запущенный клиент (у scanint числа — искомые значения)
        var clients = Process.GetProcessesByName("elementclient").Select(p => p.Id).ToList();
        var pidArg = args.TakeWhile(a => a != "--server").Select(a => int.TryParse(a, out var n) ? n : -1).FirstOrDefault(clients.Contains);
        var pid = pidArg > 0
            ? pidArg
            : clients.Count > 0 ? clients[0] : throw new InvalidOperationException("Клиент elementclient не найден");

        return GameProcess.Open(pid, rights);
    }
}
