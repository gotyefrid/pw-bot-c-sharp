using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.Calls;
using Notes.Core.Logging;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.Session;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Probe;

/// <summary>
/// Ремонт, как его делает бот в «Бить мобов»: тот же <see cref="RepairBehavior"/> с настройками персонажа из файла бота —
/// долететь до NPC, сесть, подойти, заговорить, «Починить всё», закрыть окно. ВЫЗЫВАЕТ ФУНКЦИИ ИГРЫ — запускает владелец.
/// Печатает лог бота, прочность и ответы сервера в чате.
/// </summary>
internal static class RepairCommand
{
    public const string Usage =
        "  repair --settings <characters\\сервер\\Ник.json> [--back]  ремонт, как у бота: долететь до NPC из настроек, заговорить, «Починить всё»;\n"
        + "                        --back — потом лететь обратно в центр фарма";

    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);

    public static int Run(ServerProfile profile, string[] args)
    {
        var at = Array.IndexOf(args, "--settings");
        if (at < 0 || at + 1 >= args.Length || !File.Exists(args[at + 1]))
        {
            Console.WriteLine("❌ нужно: repair --settings <путь к characters\\сервер\\Ник.json бота>");
            return 1;
        }

        var settings = new SettingsStore(args[at + 1]).Load(out var problem);
        if (problem is not null)
        {
            Console.WriteLine($"❌ {problem}");
            return 1;
        }

        var rest = args.Where((a, i) => i != at && i != at + 1).ToArray();
        using var game = Program.OpenClient(rest, GameProcessRights.Execute);
        var data = profile.Data;
        var roots = GameRoots.Find(game, game.MainModuleBase, data);
        var reader = new WorldReader(game, roots, data);
        using var calls = GameCalls.Open(game, roots, data, CallTransport.Window, out var callsProblem);
        if (calls is null)
        {
            Console.WriteLine($"❌ вызовы не подключились: {callsProblem}");
            return 3;
        }

        var log = new Logger { MinLevel = LogLevel.Debug }.AddSink(new ConsoleSink());
        var runner = new ActionRunner(new GameControl(calls.Caller), log.For("действия"));

        var world = reader.Read();
        var repair = settings.Repair;
        Console.WriteLine($"Ремонт: {(repair.Enabled ? "включён" : "ВЫКЛЮЧЕН")}, {(repair.FromBag ? "из сумки" : "у NPC")}, "
                          + $"чинить ниже {repair.BelowPercent} %, NPC: {repair.Npc?.Name ?? "не выбран"}, высота {repair.FlightHeight * 10:0} м");
        PrintGear(world);
        var chatSeen = world.Chat.Count == 0 ? 0 : world.Chat.Max(m => m.Number);

        var brain = BotModes.RepairOnly(runner, data.Skills, settings, log.For("мозг"), back: rest.Contains("--back"));
        var started = false;
        var done = false;
        brain.StatusChanged += status =>
        {
            Console.WriteLine($"            · {status}");
            if (status.StartsWith("ремонт", StringComparison.Ordinal))
                started = true;
            else if (started)
                done = true;
        };

        var watch = Stopwatch.StartNew();
        while (!done && watch.Elapsed < Limit)
        {
            world = reader.Read();
            brain.Tick(world);
            foreach (var m in world.Chat.Where(m => m.Number > chatSeen && m.Channel != 1).OrderBy(m => m.Number))
                Console.WriteLine($"            чат, канал {m.Channel}: {m.Plain}");
            if (world.Chat.Count > 0)
                chatSeen = Math.Max(chatSeen, world.Chat.Max(m => m.Number));
            if (!started && watch.Elapsed > TimeSpan.FromSeconds(3))
            {
                Console.WriteLine("❌ ремонт не начался (выключен, нечего чинить или NPC не выбран — см. выше)");
                return 4;
            }
            Thread.Sleep(Period);
        }

        Thread.Sleep(1000);
        PrintGear(reader.Read());
        Console.WriteLine(done ? "Готово" : "❌ не закончил за 5 мин");
        return done ? 0 : 4;
    }

    private static void PrintGear(WorldState w)
    {
        var worn = RepairBehavior.WornPercent(w);
        var gear = w.Equipment.Where(i => i.NeedsRepair)
            .Select(i => $"{i.Name} {i.Durability / 100.0:0.##}/{i.MaxDurability / 100.0:0.##}");
        Console.WriteLine($"Прочность: самая поношенная {worn:0.#} %; поношено: {string.Join(", ", gear.DefaultIfEmpty("ничего"))}");
    }

    private sealed class ConsoleSink : ILogSink
    {
        public void Write(LogEntry entry) => Console.WriteLine(entry);
    }
}
