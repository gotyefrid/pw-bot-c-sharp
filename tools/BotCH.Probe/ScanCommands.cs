using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.World;

namespace BotCH.Probe;

/// <summary>Помощники для поиска полей в структурах. Только чтение.</summary>
internal static class ScanCommands
{
    private static readonly Regex Readable = new(@"^[A-Za-zА-Яа-яЁё0-9☆★][A-Za-zА-Яа-яЁё0-9 \-'.,()☆★]{1,}$");

    /// <summary>
    /// Ищет в структуре указатели на строки UTF-16 (прямые [+x] и через ещё один указатель [[+x]]).
    /// Цель: адрес (0x...) или «mob» — объект ближайшего моба (тип 6).
    /// </summary>
    public static int ScanStrings(ServerProfile profile, GameProcess game, string[] args)
    {
        var what = args.FirstOrDefault(a => a.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || a is "mob" or "npc" or "item") ?? "mob";
        var size = 0x800u;

        uint[] objects;
        if (what.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            objects = [uint.Parse(what.Substring(2), NumberStyles.HexNumber)];
        }
        else
        {
            var world = new WorldReader(game, game.MainModuleBase, profile.Data).Read();
            objects = what switch
            {
                "item" => world.GroundItems.OrderBy(i => i.Offset.Direct).Take(3).Select(i => i.Address).ToArray(),
                "npc" => world.Npcs.Where(n => n.Kind == NpcKind.Npc).OrderBy(n => n.Offset.Horizontal).Take(3).Select(n => n.Address).ToArray(),
                _ => world.Mobs.OrderBy(n => n.Offset.Horizontal).Take(3).Select(n => n.Address).ToArray(),
            };
        }

        foreach (var obj in objects)
        {
            Console.WriteLine($"Объект 0x{obj:X8}");
            for (var offset = 0u; offset < size; offset += 4)
            {
                if (!game.TryReadUInt32(obj + offset, out var pointer) || pointer < 0x10000)
                    continue;

                if (TryString(game, pointer) is { } direct)
                    Console.WriteLine($"  +0x{offset:X3} → «{direct}»");
                if (game.TryReadUInt32(pointer, out var inner) && inner >= 0x10000 && TryString(game, inner) is { } indirect)
                    Console.WriteLine($"  +0x{offset:X3} → [] → «{indirect}»");
            }

            // Строка прямо внутри структуры
            for (var offset = 0u; offset < size; offset += 2)
            {
                if (TryString(game, obj + offset) is { Length: >= 3 } inline)
                {
                    Console.WriteLine($"  +0x{offset:X3} (внутри) «{inline}»");
                    offset += (uint)inline.Length * 2;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Ищет в структуре персонажа int32 с заданным значением (поиск поля «до/после»): scanint 490 [ещё значения...].
    /// Повторить после изменения значения в игре — настоящее поле останется в обоих списках.
    /// </summary>
    public static int ScanInt(ServerProfile profile, GameProcess game, string[] args)
    {
        var values = args.Where(a => int.TryParse(a, out _)).Select(int.Parse).ToList();
        var world = new WorldReader(game, game.MainModuleBase, profile.Data).Read();
        var host = world.Host.Address;
        var block = game.ReadBytes(host, 0x2000);
        Console.WriteLine($"Перс 0x{host:X8}: HP {world.Host.Hp}/{world.Host.MaxHp}, MP {world.Host.Mp}");
        foreach (var value in values)
        {
            var hits = Enumerable.Range(0, block.Length / 4).Where(i => BitConverter.ToInt32(block, i * 4) == value).Select(i => $"+0x{i * 4:X3}");
            Console.WriteLine($"{value}: {string.Join(" ", hits)}");
        }

        return 0;
    }

    /// <summary>
    /// Ищет поле-«свойство вида» в структуре мобов (например, уровень): int в [min..max], одинаковый у всех мобов
    /// с одним названием и разный хотя бы у двух видов. Нужны рядом мобы двух и более видов, лучше по нескольку штук.
    /// </summary>
    public static int MobFields(ServerProfile profile, GameProcess game, string[] args)
    {
        var numbers = args.Where(a => int.TryParse(a, out _)).Select(int.Parse).ToList();
        int min = numbers.Count > 0 ? numbers[0] : 1, max = numbers.Count > 1 ? numbers[1] : 150;
        var mobs = new WorldReader(game, game.MainModuleBase, profile.Data).Read().Mobs.Where(m => m.Name.Length > 0).ToList();
        var blocks = mobs.Select(m => (m.Name, Bytes: game.ReadBytes(m.Address, 0x800))).ToList();
        var kinds = mobs.Select(m => m.Name).Distinct().ToList();
        Console.WriteLine($"Мобов {mobs.Count}, видов {kinds.Count}: {string.Join(", ", kinds)}");

        for (var offset = 0; offset < 0x800; offset += 4)
        {
            var values = blocks.Select(b => (b.Name, Value: BitConverter.ToInt32(b.Bytes, offset))).ToList();
            if (values.Any(v => v.Value < min || v.Value > max))
                continue;
            var byKind = values.GroupBy(v => v.Name).ToDictionary(g => g.Key, g => g.Select(v => v.Value).Distinct().ToList());
            if (byKind.Values.Any(v => v.Count > 1) || byKind.Values.Select(v => v[0]).Distinct().Count() < 2)
                continue;

            Console.WriteLine($"  +0x{offset:X3}: " + string.Join(", ", byKind.Select(k => $"{k.Key}={k.Value[0]}")));
        }

        return 0;
    }

    internal static string? TryString(IMemory memory, uint address)
    {
        try
        {
            var text = memory.ReadUnicodeString(address, 32);
            return Readable.IsMatch(text) ? text : null;
        }
        catch (MemoryAccessException)
        {
            return null;
        }
    }
}
