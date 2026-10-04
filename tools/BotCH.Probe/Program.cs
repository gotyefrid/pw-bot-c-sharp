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
                case "mem":
                    return WithClient(rest, (_, game) => Memory(game));
                case "memmap":
                    return WithClient(rest, (_, game) => MemoryMap(game, rest));
                case "heaps":
                    return WithClient(rest, (_, game) => Heaps(game, rest));
                case "memwatch":
                    return WithClient(rest, (_, game) => MemoryWatch(game));
                case "rename":
                    return Rename(rest);
                case "dump":
                    return WithClient(rest, (profile, game) => WorldCommands.Dump(profile, game, rest));
                case "mobfields":
                    return WithClient(rest, (profile, game) => ScanCommands.MobFields(profile, game, rest));
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
        Console.WriteLine("  mem       сколько свободной памяти (адресов) осталось у игры: всего и самый большой кусок");
        Console.WriteLine("  memmap    [файл.tsv] карта памяти игры: кто сколько занял (картинки, файлы, куча); файл — для сравнения «до/после»");
        Console.WriteLine("  memwatch  то же раз в секунду: сколько сейчас и сколько изменилось с запуска, Ctrl+C — выход");
        Console.WriteLine("  rename    переименовать окна всех клиентов в «Ник PID» и проверить заголовки (WinAPI, память только читается)");
        Console.WriteLine("  dump      [файл.dump] сохранить прочитанную снимком память в файл — фикстура для тестов без игры");
        Console.WriteLine("  mobfields [мин макс]  поля мобов, одинаковые у одного вида и разные у разных (уровень?)");
        Console.WriteLine("  scanint   ЧИСЛО... найти в структуре перса поля с этим значением (поиск «до/после»)");
        Console.WriteLine("  scanstr   [mob|npc|item|0xАДРЕС] найти в структуре указатели на строки (поиск поля «название»)");
        Console.WriteLine("  selftest  проверить, что снимок разумный (HP ≤ MaxHP, типы мобов…), и замерить скорость");
        Console.WriteLine();
        Console.WriteLine("ДЕЙСТВИЯ В ИГРЕ (вызывают функции клиента — запускает владелец):");
        Console.WriteLine(ActCommands.Usage);
        Console.WriteLine("  cdfind [pet-food|potion-hp|potion-mp]  использовать предмет и 15 с искать поле его перезарядки («до/после»)");
    }

    private static int Memory(GameProcess game)
    {
        if (game.QueryFreeMemory() is not FreeMemory free)
        {
            Console.Error.WriteLine("❌ Windows не ответила (VirtualQueryEx)");
            return 2;
        }

        Console.WriteLine($"Свободно у игры: {free.TotalMb} МБ, самый большой кусок подряд: {free.Largest / 1024} КБ");
        Console.WriteLine($"Бот остановится при < {GameMemoryGuard.StopTotal >> 20} МБ или куске < {GameMemoryGuard.StopLargest >> 10} КБ, "
                          + $"предупредит при < {GameMemoryGuard.WarnTotal >> 20} МБ");
        return 0;
    }

    private static int MemoryMap(GameProcess game, string[] args)
    {
        var regions = game.Regions().ToList();
        var files = new System.Collections.Generic.Dictionary<uint, string>();
        foreach (var r in regions.Where(r => r.State != MemoryRegion.Free && r.Type != MemoryRegion.Private))
        {
            if (!files.ContainsKey(r.AllocationBase))
                files[r.AllocationBase] = game.MappedFileName(r.Start) ?? "";
        }

        string Kind(MemoryRegion r) => r.State == MemoryRegion.Free ? "free"
            : r.Type == MemoryRegion.Image ? "image" : r.Type == MemoryRegion.Mapped ? "mapped" : "private";

        var path = args.FirstOrDefault(a => a.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase));
        if (path is not null)
        {
            using var w = new System.IO.StreamWriter(path, false, new System.Text.UTF8Encoding(false));
            w.WriteLine("start\tallocBase\tsize\tkind\tstate\tprotect\tfile\thead");
            var head = new byte[0x40];
            foreach (var r in regions)
            {
                var state = r.State == MemoryRegion.Commit ? "commit" : r.State == MemoryRegion.Reserve ? "reserve" : "free";
                // Начало выделения: заголовок кучи Windows (сигнатура 0xFFEEFFEE на +8) или чужой распределитель
                var start = r.Start == r.AllocationBase && r.State == MemoryRegion.Commit && r.Type == MemoryRegion.Private
                            && game.TryRead(r.Start, head, head.Length)
                    ? BitConverter.ToString(head).Replace("-", "")
                    : "";
                w.WriteLine($"{r.Start:X8}\t{r.AllocationBase:X8}\t{r.Size}\t{Kind(r)}\t{state}\t{r.Protect:X}\t"
                            + (files.TryGetValue(r.AllocationBase, out var f) ? f : "") + "\t" + start);
            }
        }

        Console.WriteLine($"Участков: {regions.Count}");
        Console.WriteLine("вид        занято (commit)   отгорожено (reserve)");
        foreach (var g in regions.Where(r => r.State != MemoryRegion.Free).GroupBy(Kind).OrderBy(g => g.Key))
            Console.WriteLine($"{g.Key,-8} {g.Where(r => r.State == MemoryRegion.Commit).Sum(r => r.Size) >> 20,10} МБ "
                              + $"{g.Where(r => r.State == MemoryRegion.Reserve).Sum(r => r.Size) >> 20,16} МБ");
        Console.WriteLine($"свободно {regions.Where(r => r.State == MemoryRegion.Free).Sum(r => r.Size) >> 20,10} МБ");
        if (path is not null)
            Console.WriteLine($"Карта сохранена: {System.IO.Path.GetFullPath(path)}");
        return 0;
    }

    // Кучи Windows игры: список из PEB (x86: +0x18 ProcessHeap, +0x88 NumberOfHeaps, +0x90 ProcessHeaps) плюс адреса
    // из аргументов (кучи видны в memmap: сигнатура сегмента 0xFFEEFFEE). У _HEAP x86: +0x40 Flags, +0x60 EEFFEEFF
    private static int Heaps(GameProcess game, string[] args)
    {
        if (game.PebAddress() is not uint peb)
        {
            Console.Error.WriteLine("❌ PEB не прочитать");
            return 2;
        }

        var processHeap = game.ReadUInt32(peb + 0x18);
        var count = game.ReadUInt32(peb + 0x88);
        var max = game.ReadUInt32(peb + 0x8C);
        var list = game.ReadUInt32(peb + 0x90);
        Console.WriteLine($"PEB {peb:X8}, куча процесса {processHeap:X8}, куч {count} (места на {max}), список {list:X8}");
        var heaps = new System.Collections.Generic.List<uint>();
        for (var i = 0u; i < Math.Min(Math.Max(count, 8u), 64u); i++)
            heaps.Add(game.ReadUInt32(list + i * 4));
        heaps.AddRange(args.Where(a => a.StartsWith("0x")).Select(a => Convert.ToUInt32(a.Substring(2), 16)));

        foreach (var heap in heaps.Where(h => h != 0).Distinct())
        {
            var raw = new byte[0x100];
            if (!game.TryRead(heap, raw, raw.Length) || BitConverter.ToUInt32(raw, 0x60) != 0xEEFFEEFF)
            {
                Console.WriteLine($"{heap:X8}: не куча (нет EEFFEEFF на +0x60)");
                continue;
            }

            // Дальше Flags раскладку _HEAP не проверяли — не печатаем, чтобы не вводить в заблуждение
            var flags = BitConverter.ToUInt32(raw, 0x40);
            Console.WriteLine($"{heap:X8}{(heap == processHeap ? " (процесса)" : "")}: Flags {flags:X}"
                              + $"{((flags & 1) != 0 ? " NO_SERIALIZE" : "")}{((flags & 2) != 0 ? " GROWABLE" : "")}{((flags & 0x1000) != 0 ? " (HeapCreate)" : "")}");
        }

        return 0;
    }

    private static int MemoryWatch(GameProcess game)
    {
        Console.Title = $"Память игры {game.Pid}";
        using var process = Process.GetProcessById(game.Pid);
        var started = DateTime.Now;
        FreeMemory? first = null;
        long firstUsed = 0;

        Console.WriteLine($"Клиент {game.Pid}. Бот остановится при свободных < {GameMemoryGuard.StopTotal >> 20} МБ. Ctrl+C — выход");
        Console.WriteLine("время      свободно      изм.     кусок подряд   занято игрой   изм.    темп       хватит");
        while (!game.HasExited)
        {
            if (game.QueryFreeMemory() is FreeMemory free)
            {
                process.Refresh();
                var used = process.PrivateMemorySize64 >> 20;
                if (first is null)
                {
                    first = free;
                    firstUsed = used;
                }

                var change = free.TotalMb - first.Value.TotalMb;
                var minutes = (DateTime.Now - started).TotalMinutes;
                // Темп — с запуска консоли; первые полминуты он ещё ничего не значит
                var rate = minutes >= 0.5 ? change / minutes : double.NaN;
                var left = rate < -0.1 ? TimeSpan.FromMinutes((free.TotalMb - (GameMemoryGuard.StopTotal >> 20)) / -rate) : (TimeSpan?)null;

                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {free.TotalMb,6} МБ  {change,+6:+0;-0;0} МБ  {free.Largest >> 20,8} МБ  "
                                  + $"{used,9} МБ  {used - firstUsed,+6:+0;-0;0} МБ  "
                                  + (double.IsNaN(rate) ? "    …     " : $"{rate,5:+0.0;-0.0;0} МБ/мин")
                                  + (left is { } l ? $"  ≈ {(int)l.TotalHours} ч {l.Minutes:00} мин" : ""));
            }

            System.Threading.Thread.Sleep(1000);
        }

        Console.WriteLine("Клиент игры закрыт");
        return 0;
    }

    private static int Attach(string[] args)
    {
        var profile = LoadProfile(args);
        using var game = OpenClient(args);
        var data = profile.Data;
        Console.WriteLine($"Сервер:   {profile.Name}");
        Console.WriteLine($"Процесс:  {game.ProcessName} PID {game.Pid}, модуль 0x{game.MainModuleBase:X8} ({game.MainModuleSize / 1024} КБ)");

        var gameAddress = new GameRoots(game, game.MainModuleBase, data).Game();
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

    private static int WithClient(string[] args, Func<ServerProfile, GameProcess, int> command)
    {
        var profile = LoadProfile(args);
        using var game = OpenClient(args);
        return command(profile, game);
    }

    private static ServerProfile LoadProfile(string[] args)
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
