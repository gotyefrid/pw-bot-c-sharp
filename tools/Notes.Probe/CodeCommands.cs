using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Notes.Core.Memory;
using Notes.Core.Profiles;

namespace Notes.Probe;

/// <summary>
/// Карта кода, который сервер прислал клиенту для исполнения. Только чтение — ни одной записи в игру.
/// <c>code</c> — разовый снимок в консоль; <c>code watch</c> — раз в 2 с, только новое; <c>code save</c> — ещё и в файл.
/// </summary>
internal static class CodeCommands
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(2);

    public static string Usage =>
        "  code       [watch] [файл.log]  карта пришедшего с сервера кода: ключ, адрес, строки внутри; watch — следить за новыми";

    public static int Run(ServerProfile profile, GameProcess game, string[] args)
    {
        var spec = profile.Data.ServerCode;
        if (spec is null || spec.MapHead == 0)
        {
            Console.WriteLine($"В профиле «{profile.Id}» карта серверного кода не задана (serverCode.mapHead пуст) — "
                              + "эта сборка ещё не проверена. Разбор механизма — tz-refactoring/23, п.14.");
            return 3;
        }

        var watch = args.Any(a => a.Equals("watch", StringComparison.OrdinalIgnoreCase));
        var path = args.FirstOrDefault(a => a.EndsWith(".log", StringComparison.OrdinalIgnoreCase));
        var codeBytes = spec.CodeBytes > 0 ? (int)spec.CodeBytes : ServerCodeMap.DefaultCodeBytes;
        var mapHead = game.MainModuleBase + spec.MapHead;

        Console.WriteLine($"Сервер: {profile.Name}, клиент PID {game.Pid}, модуль 0x{game.MainModuleBase:X8}");
        Console.WriteLine($"Карта: голова 0x{mapHead:X8} (модуль + 0x{spec.MapHead:X}), "
                          + $"свои ключи от 0x{spec.OwnKeyMin:X}, читаем по {codeBytes} байт кода");

        StreamWriter? file = null;
        try
        {
            if (path is not null)
            {
                file = new StreamWriter(path, append: true, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                Console.WriteLine($"Пишу в {Path.GetFullPath(path)}");
            }

            var known = new HashSet<uint>();
            // Порядок обхода дерева не задан, поэтому «новое» — по ключу, а не по позиции
            while (true)
            {
                var entries = ServerCodeMap.Read(game, mapHead, spec.OwnKeyMin, codeBytes);
                var fresh = entries.Where(e => known.Add(e.Key)).ToList();

                if (!watch)
                {
                    Show(entries, file);
                    return 0;
                }

                if (fresh.Count > 0)
                {
                    Console.WriteLine($"=== {DateTime.Now:HH:mm:ss}: новых записей {fresh.Count}");
                    Show(fresh, file, onlyNew: true);
                }

                if (game.HasExited)
                {
                    Console.WriteLine("Клиент игры закрыт");
                    return 0;
                }

                System.Threading.Thread.Sleep(Period);
            }
        }
        catch (MemoryAccessException e)
        {
            Console.Error.WriteLine($"❌ {e.Message}: похоже, serverCode.mapHead в профиле не от этой сборки");
            return 2;
        }
        finally
        {
            file?.Dispose();
        }
    }

    private static void Show(IReadOnlyList<ServerCodeEntry> entries, StreamWriter? file, bool onlyNew = false)
    {
        var server = entries.Where(e => !e.Own).ToList();
        Console.WriteLine($"Записей всего {entries.Count}: с сервера {server.Count}, свои (клиента) {entries.Count - server.Count}");
        if (server.Count == 0)
            Console.WriteLine("Сервер в этой сессии код не присылал.");

        foreach (var e in onlyNew ? server : entries)
        {
            var mark = e.Own ? "свой" : "СЕРВЕР";
            Console.WriteLine($"  {mark,-6} ключ {e.Key,6} код 0x{e.Code:X8} флаги 0x{e.Flags:X2} "
                              + $"{(e.CodeBytes == 0 ? "(страница освобождена)" : $"({e.CodeBytes} байт)")}");
            if (e.Strings.Count > 0)
                Console.WriteLine($"         строки: {string.Join(" | ", e.Strings)}");
            if (e.Hex.Length > 0)
                Console.WriteLine($"         код: {e.Hex}");

            file?.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{(e.Own ? "свой" : "сервер")}\t"
                            + $"{e.Key.ToString(CultureInfo.InvariantCulture)}\t0x{e.Code:X8}\t0x{e.Flags:X2}\t"
                            + string.Join(" | ", e.Strings) + "\t" + e.Hex);
        }
    }
}
