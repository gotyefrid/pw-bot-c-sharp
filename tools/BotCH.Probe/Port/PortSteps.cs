using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.World;

namespace BotCH.Probe.Port;

/// <summary>
/// Шаги мастера <see cref="PortCommand"/>: каждый проверяет свои поля профиля на живом клиенте, а неверные ищет —
/// по значению, которое называет владелец, по связям с уже проверенным (WID моба, названия скиллов) или записью
/// «до/после», пока владелец делает действие. Из нескольких мест берётся ближайшее к старому смещению: поля между
/// версиями клиента обычно сдвигаются блоками на десятки байт.
/// </summary>
internal static class PortSteps
{
    public static readonly IReadOnlyList<PortStep> All =
    [
        new("functions", "Функции: сигнатуры и запрещённые", false, Functions),
        new("host", "Персонаж: база и ник", false, Host),
        new("stats", "Уровень, HP, MP", false, Stats),
        new("location", "Координаты персонажа", false, Location),
        new("world", "Мобы и предметы рядом", false, World),
        new("bag", "Сумка", false, Bag),
        new("skills", "Скиллы", false, Skills),
        new("target", "Цель персонажа", true, Target),
        new("pet", "Пет: призыв и отзыв", true, Pet),
        new("cast", "Каст скилла", true, Cast),
        new("gather", "Копание ресурса", true, Gather),
        new("petfood", "Перезарядка корма пета", true, PetFood),
        new("fly", "Полёт", true, Fly),
    ];

    // ───────────────────────── функции ─────────────────────────

    private static PortResult Functions(PortContext c)
    {
        var resolver = new FunctionResolver(c.Game, c.Module);
        var results = new List<PortResult>();
        foreach (var location in resolver.ResolveAll(c.Data))
        {
            if (location.Status == FunctionStatus.Relocated)
                c.Set($"functions.{location.Name}.rva", location.Address - c.Module, "сигнатура нашлась в другом месте");
            else if (!location.IsUsable)
                results.Add(PortResult.Fail($"{location.Name}: {location.Details}"));
        }

        // Чего в профиле нет — поискать по сигнатурам других профилей (тот же код в другой сборке)
        var catalog = new ProfileCatalog();
        var missing = typeof(GameFunctions).GetFields().Select(f => (string)f.GetValue(null)!)
            .Where(n => !c.Data.Functions.TryGetValue(n, out var f) || f.Rva == 0).ToList();
        foreach (var name in missing)
        {
            var found = catalog.Ids.Select(id => catalog.Load(id).Data.Functions.TryGetValue(name, out var f) ? (id, f.Signature) : (id, null))
                .Where(x => x.Signature is not null)
                .Select(x => (x.id, Hits: resolver.FindEverywhere(x.Signature!)))
                .FirstOrDefault(x => x.Hits.Count == 1);
            results.Add(found.Hits is { Count: 1 }
                ? PortResult.Likely($"{name}: нет в профиле, сигнатура {found.id} — rva 0x{found.Hits[0] - c.Module:X} (вписать руками)")
                : PortResult.Fail($"{name}: нет в профиле, по сигнатурам других серверов не найдено"));
        }

        results.Add(Forbidden(c, "logout", 0x01, "выход из игры"));
        results.Add(Forbidden(c, "releasePet", 0x66, "отпустить пета"));
        return Merge(results, "все функции профиля на месте");
    }

    /// <summary>
    /// Запрещённая функция — по коду отправки пакета: «mov eax, N … mov word [esi|edi], ax» (так у всех известных клиентов
    /// ветки Comeback). Начало — ближайший выше адрес, кратный 16, перед которым заполнитель int3.
    /// </summary>
    private static PortResult Forbidden(PortContext c, string name, byte packet, string what)
    {
        var starts = new List<uint>();
        foreach (var section in ModuleSections.Read(c.Game, c.Module).Where(s => s.IsCode && s.Size > 0))
        {
            var code = ModuleSections.ReadRegion(c.Game, section.Address, section.Size);
            for (var i = 0; i + 30 < code.Length; i++)
            {
                if (code[i] != 0xB8 || code[i + 1] != packet || code[i + 2] != 0 || code[i + 3] != 0 || code[i + 4] != 0)
                    continue;
                var store = Enumerable.Range(i + 5, 24).Any(j => code[j] == 0x66 && code[j + 1] == 0x89 && code[j + 2] is 0x06 or 0x07);
                if (!store)
                    continue;
                for (var j = i & ~0xF; j > i - 0x200 && j > 1; j -= 0x10)
                {
                    if (code[j - 1] == 0xCC && code[j - 2] == 0xCC)
                    {
                        starts.Add(section.Address + (uint)j - c.Module);
                        break;
                    }
                }
            }
        }

        starts = starts.Distinct().ToList();
        c.Data.ForbiddenFunctions.TryGetValue(name, out var current);
        if (c.Data.Functions.Values.Any(f => f.Rva != 0 && f.Rva == current))
            return PortResult.Fail($"{name}: адрес запрещённой функции есть среди вызываемых — исправить профиль!");
        if (starts.Count == 1 && starts[0] == current)
            return PortResult.Ok($"{name} ({what}) 0x{current:X}");
        if (starts.Count == 1)
        {
            c.Set($"forbiddenFunctions.{name}", starts[0], $"c2s 0x{packet:X2} — {what}");
            return PortResult.Ok("");
        }

        return PortResult.Fail($"{name} ({what}): код отправки 0x{packet:X2} найден в {starts.Count} местах"
                               + (starts.Count > 0 ? ": " + string.Join(", ", starts.Take(6).Select(s => $"0x{s:X}")) : "") + " — искать руками");
    }

    // ───────────────────────── персонаж ─────────────────────────

    private static PortResult Host(PortContext c)
    {
        var name = HostName(c, c.Data.Host.Struct, c.Data.Host.NamePointer, c.GameObject());
        if (name is not null)
        {
            var ok = c.Confirm($"Ник персонажа «{name}»?");
            if (ok == true)
                return PortResult.Ok($"ник «{name}», перс 0x{c.HostAddress():X8}");
            if (ok is null)
                return PortResult.Likely($"ник «{name}» (не подтверждён)");
        }

        var nick = c.Ask("Ник персонажа, как в игре");
        if (string.IsNullOrEmpty(nick))
            return PortResult.Fail(c.HostAddress() == 0 ? "персонаж не читается (база/game/struct)" : "ник не читается (host.namePointer)");

        // 1) game верный: перебрать указатели в нём и поля ника в каждом
        var game = c.GameObject();
        if (game != 0)
        {
            for (var structOffset = 0u; structOffset < 0x100; structOffset += 4)
            {
                if (!c.Game.TryReadUInt32(game + structOffset, out var host) || !c.Game.TryReadUInt32(host, out var vtable) || !c.InModule(vtable)
                    || !MemoryBlock.TryRead(c.Game, host, PortContext.HostSize, out var block))
                    continue;
                for (var nameOffset = 0u; nameOffset < PortContext.HostSize; nameOffset += 4)
                {
                    if (block.UInt32(nameOffset) is var p && p > 0x10000 && c.Text(p) == nick)
                    {
                        SetIfChanged(c, "host.struct", c.Data.Host.Struct, structOffset, "указатель на перса в game");
                        SetIfChanged(c, "host.namePointer", c.Data.Host.NamePointer, nameOffset, $"ник «{nick}»");
                        return PortResult.Ok("");
                    }
                }
            }
        }

        // 2) базовый указатель: [модуль + X] → game → перс → ник, с теми же game/struct/namePointer
        foreach (var section in ModuleSections.Read(c.Game, c.Module).Where(s => !s.IsCode && s.Size > 0))
        {
            var data = ModuleSections.ReadRegion(c.Game, section.Address, section.Size);
            for (var i = 0; i + 4 <= data.Length; i += 4)
            {
                var b = BitConverter.ToUInt32(data, i);
                if (b < 0x10000 || !c.Game.TryReadUInt32(b + c.Data.Base.Game, out var g) || g < 0x10000)
                    continue;
                if (HostName(c, c.Data.Host.Struct, c.Data.Host.NamePointer, g) == nick)
                {
                    c.Set("base.basePointer", section.Address + (uint)i - c.Module, $"ник «{nick}» через game +0x{c.Data.Base.Game:X}");
                    return PortResult.Ok("");
                }
            }
        }

        return PortResult.Fail($"ник «{nick}» не найден ни в game, ни через другой базовый указатель — нужно сопоставление кода");
    }

    private static string? HostName(PortContext c, uint structOffset, uint nameOffset, uint game)
        => game != 0 && c.Game.TryReadUInt32(game + structOffset, out var host) && host != 0
           && c.Game.TryReadUInt32(host + nameOffset, out var p) && p > 0x10000
            ? c.Text(p)
            : null;

    private static PortResult Stats(PortContext c)
    {
        var h = c.Data.Host;
        var fields = new (string Path, uint Offset)[]
        {
            ("host.level", h.Level), ("host.hp", h.Hp), ("host.maxHp", h.MaxHp), ("host.mp", h.Mp), ("host.maxMp", h.MaxMp),
        };
        var block = c.HostBlock();
        var now = fields.Select(f => f.Offset == 0 ? 0 : BitConverter.ToInt32(block, (int)f.Offset)).ToArray();
        var wid = BitConverter.ToUInt32(block, (int)h.Wid);
        Console.WriteLine($"  По профилю: ур. {now[0]}, HP {now[1]}/{now[2]}, MP {now[3]}/{now[4]}, WID 0x{wid:X8}");

        var answer = c.Ask("Из игры через пробел: уровень HP максHP MP максMP (Enter — всё совпадает)");
        if (answer is null)
        {
            var plausible = now[0] is >= 1 and <= 150 && now[1] > 0 && now[1] <= now[2] && now[3] >= 0 && now[3] <= now[4] && wid != 0;
            return plausible ? PortResult.Likely("значения правдоподобны") : PortResult.Fail("значения неправдоподобны — запустить с владельцем");
        }

        if (answer.Length == 0)
            return PortResult.Ok($"ур. {now[0]}, HP {now[1]}/{now[2]}, MP {now[3]}/{now[4]}");

        var expected = Numbers(answer);
        if (expected.Count != 5)
            return PortResult.Fail("нужно 5 чисел");

        var results = new List<PortResult>();
        var used = new HashSet<uint>(fields.Where((f, i) => now[i] == expected[i]).Select(f => f.Offset));
        for (var i = 0; i < fields.Length; i++)
        {
            if (now[i] == expected[i])
                continue;
            var hits = Offsets(block, (int)expected[i]).Where(o => !used.Contains(o)).ToList();
            results.Add(Choose(c, fields[i].Path, fields[i].Offset, hits, $"значение {expected[i]}"));
            used.Add(Field(c, fields[i].Path));
        }

        return Merge(results, "все совпали");
    }

    private static PortResult Location(PortContext c)
    {
        var block = c.HostBlock();
        var at = (int)c.Data.Host.Location;
        float x = BitConverter.ToSingle(block, at), height = BitConverter.ToSingle(block, at + 4), y = BitConverter.ToSingle(block, at + 8);
        var answer = c.Ask($"Координаты из угла экрана «X Y». По памяти: {MapX(x):0} {MapY(y):0}, высота {height / 10:0} (Enter — так и есть)");
        if (answer is null)
            return Math.Abs(x) < 10000 && Math.Abs(y) < 10000 && Math.Abs(height) < 3000
                ? PortResult.Likely($"по карте {MapX(x):0} {MapY(y):0} — правдоподобно")
                : PortResult.Fail($"координаты неправдоподобны: {x} {height} {y}");
        if (answer.Length == 0)
            return PortResult.Ok($"по карте {MapX(x):0} {MapY(y):0}");

        var xy = Numbers(answer);
        if (xy.Count < 2)
            return PortResult.Fail("нужно 2 числа");
        var hits = Enumerable.Range(0, (block.Length - 12) / 4).Select(i => (uint)i * 4)
            .Where(o => Math.Abs(MapX(BitConverter.ToSingle(block, (int)o)) - xy[0]) <= 1.5 && Math.Abs(MapY(BitConverter.ToSingle(block, (int)o + 8)) - xy[1]) <= 1.5)
            .ToList();
        return Choose(c, "host.location", c.Data.Host.Location, hits, $"карта {xy[0]} {xy[1]}");
    }

    // Карта игры: X = (x + 4000) / 10, Y = (y + 5500) / 10 (как в ActCommands move-to)
    private static double MapX(float x) => (x + 4000) / 10.0;
    private static double MapY(float y) => (y + 5500) / 10.0;

    // ───────────────────────── мир ─────────────────────────

    private static PortResult World(PortContext c)
    {
        var results = new List<PortResult>();
        // Списки — те же хэш-таблицы, сдвигаются только «мир» в game и менеджеры в мире
        var first = TryRead(c);
        if (first is not { Npcs.Count: > 0 } || first.Npcs.Any(n => n.Offset.Horizontal > NearbyMeters))
        {
            var lists = c.Data.World;
            // Мобы, NPC и петы — тип 6/7/9; так список мобов не путается с соседним списком игроков
            var npcs = ListCandidates(c, lists.Npcs, c.Data.Npc.Location, o => c.Game.TryReadUInt32(o + c.Data.Npc.Type, out var type) && type is 6 or 7 or 9);
            if (npcs.Count == 0)
                return PortResult.Fail("список мобов не найден: в game нет мира с менеджером (count/slotArray как в профиле) и объектами рядом");
            var best = npcs.OrderBy(x => Math.Abs((long)x.World - lists.World) + Math.Abs((long)x.Manager - lists.Npcs.Manager)).First();
            Console.WriteLine($"  кандидаты мир/мобы: {string.Join(", ", npcs.Select(x => $"+0x{x.World:X}/+0x{x.Manager:X} ({x.Count})"))}");
            SetIfChanged(c, "world.world", lists.World, best.World, $"мир в game; мобов {best.Count}");
            SetIfChanged(c, "world.npcs.manager", lists.Npcs.Manager, best.Manager, $"менеджер мобов в мире: все {best.Count} рядом с персом");
        }

        if (TryRead(c) is { } before && before.GroundItems.Any(i => i.Offset.Direct > NearbyMeters))
        {
            var lists = c.Data.World;
            var itemLists = ListCandidates(c, lists.GroundItems, c.Data.GroundItem.Location, _ => true).Where(x => x.World == lists.World && x.Manager != lists.Npcs.Manager).ToList();
            Console.WriteLine($"  кандидаты предметов: {string.Join(", ", itemLists.Select(x => $"+0x{x.Manager:X} ({x.Count})"))}");
            if (itemLists.Count > 0)
            {
                var item = itemLists.OrderBy(x => Math.Abs((long)x.Manager - lists.GroundItems.Manager)).First();
                SetIfChanged(c, "world.groundItems.manager", lists.GroundItems.Manager, item.Manager, $"менеджер предметов: все {item.Count} рядом с персом");
            }
            else
            {
                results.Add(PortResult.Fail("список предметов на земле не найден (рядом с персом)"));
            }
        }

        var w = c.Reader().Read();
        if (w.Npcs.Count == 0)
            return PortResult.Fail($"мобов не видно (в игре {w.NpcCountInGame?.ToString() ?? "?"}) — встаньте у мобов; иначе world.npcs неверно");

        // Названия: смещение, где у всех объектов указатель на текст
        if (w.Npcs.Any(n => n.Name.Length == 0))
            results.Add(Choose(c, "npc.namePointer", c.Data.Npc.NamePointer, Common(c, w.Npcs.Select(n => n.Address), 0x400, (b, o) => c.Text(b.UInt32(o)) is not null), "название у всех мобов"));
        if (w.GroundItems.Count > 0 && w.GroundItems.Any(i => i.Name.Length == 0))
            results.Add(Choose(c, "groundItem.namePointer", c.Data.GroundItem.NamePointer, Common(c, w.GroundItems.Select(i => i.Address), 0x400, (b, o) => c.Text(b.UInt32(o)) is not null), "название у всех предметов"));
        w = c.Reader().Read();

        // Запись справочника: указатель, по которому +0x0 — id вида; тот же id лежит и в самом мобе
        var essence = Common(c, w.Npcs.Select(n => n.Address), 0x400, (b, o) => c.Game.TryReadUInt32(b.UInt32(o), out var id) && id != 0 && IdInside(b, id))
            .Where(o => w.Npcs.Select(n => c.Game.ReadUInt32(c.Game.ReadUInt32(n.Address + o))).Distinct().Count() > 1 || w.Npcs.Count == 1).ToList();
        results.Add(Choose(c, "npc.essence", c.Data.Npc.Essence, essence, "запись справочника: её id есть в самом мобе"));
        w = c.Reader().Read();

        // Расстояние в памяти игры = расчёт по координатам: так проверяются и координаты, и само поле
        var far = w.Npcs.Where(n => n.Offset.Horizontal > 1).ToList();
        if (far.Count >= 2 && c.Data.Npc.Distance != 0 && far.Any(n => Math.Abs(c.Game.ReadFloat(n.Address + c.Data.Npc.Distance) - n.Offset.Horizontal) > 0.5))
            results.Add(Choose(c, "npc.distance", c.Data.Npc.Distance,
                Common(c, far.Select(n => n.Address), 0x400, (b, o) => Math.Abs(b.Float(o) - Find(far, b.Address).Offset.Horizontal) <= 0.5), "= расстояние по координатам"));
        var items = w.GroundItems.Where(i => i.Offset.Direct > 1).ToList();
        if (items.Count >= 2 && c.Data.GroundItem.Distance != 0 && items.Any(i => Math.Abs(c.Game.ReadFloat(i.Address + c.Data.GroundItem.Distance) - i.Offset.Direct) > 0.5))
            results.Add(Choose(c, "groundItem.distance", c.Data.GroundItem.Distance,
                Common(c, items.Select(i => i.Address), 0x400, (b, o) => Math.Abs(b.Float(o) - items.First(i => i.Address == b.Address).Offset.Direct) <= 0.5), "= расстояние по координатам"));

        w = c.Reader().Read();
        if (w.NpcCountInGame is { } count && count != w.Npcs.Count)
            results.Add(PortResult.Fail($"мобов прочитано {w.Npcs.Count}, в игре {count}"));
        var badAggro = w.Mobs.Where(n => n.Aggressive is null || n.AggroRadius is < 0 or > 100).Select(n => $"{n.Name} {n.AggroRadius}").ToList();
        if (badAggro.Count > 0)
            results.Add(PortResult.Fail("агр/радиус агра у мобов неправдоподобны (monsterEssence): " + string.Join(", ", badAggro.Take(3))));
        var badKind = w.Npcs.Where(n => n.Kind is not (NpcKind.Mob or NpcKind.Npc or NpcKind.Pet)).Select(n => $"{n.Name}={(int)n.Kind}").ToList();
        if (badKind.Count > 0)
            results.Add(PortResult.Fail("тип моба не 6/7/9: " + string.Join(", ", badKind.Take(5))));

        Console.WriteLine("  Рядом (по памяти):");
        foreach (var n in w.Npcs.OrderBy(n => n.Offset.Horizontal).Take(6))
            Console.WriteLine($"    {n.Offset.Horizontal,5:0} м  ур.{n.Level,3}  {n.Name}  HP {n.Hp}{(n.Aggressive == true ? $"  агр {n.AggroRadius} м" : "")}");
        foreach (var i in w.GroundItems.OrderBy(i => i.Offset.Direct).Take(4))
            Console.WriteLine($"    {i.Offset.Direct,5:0} м  {i.Name}{(i.Mine is { } m ? $" (ресурс, инструмент {m.Tool})" : "")}");

        var same = c.Confirm("Названия и уровни мобов, предметы — как на экране?");
        results.Add(same switch
        {
            true => PortResult.Ok($"мобов {w.Npcs.Count}, предметов {w.GroundItems.Count}"),
            false => PortResult.Fail("владелец: не как на экране — проверить npc.level/namePointer (Probe mobfields, scanstr)"),
            null => PortResult.Likely($"мобов {w.Npcs.Count}, предметов {w.GroundItems.Count}, названия читаются"),
        });
        return Merge(results, "");
    }

    private static bool IdInside(MemoryBlock b, uint id)
    {
        for (var o = 0x100u; o < 0x140; o += 4)
        {
            if (b.UInt32(o) == id)
                return true;
        }

        return false;
    }

    private static NpcInfo Find(IEnumerable<NpcInfo> npcs, uint address) => npcs.First(n => n.Address == address);

    private static WorldState? TryRead(PortContext c)
    {
        try
        {
            return c.Reader().Read();
        }
        catch (Exception e) when (e is WorldNotReadyException or MemoryAccessException)
        {
            Console.WriteLine($"  снимок не читается: {e.Message}");
            return null;
        }
    }

    // Объекты списков — в радиусе видимости; дальше — значит, читаем не то
    private const float NearbyMeters = 400;

    /// <summary>
    /// Хэш-таблицы объектов: [[game + мир] + менеджер] с тем же расположением счётчика и массива слотов, что в профиле.
    /// Подходит, если обход слотов дал ровно столько объектов, сколько в счётчике, и все они рядом с персонажем.
    /// </summary>
    private static List<(uint World, uint Manager, int Count)> ListCandidates(PortContext c, WorldListOffsets list, uint location, Func<uint, bool> fits)
    {
        var result = new List<(uint, uint, int)>();
        var game = c.GameObject();
        var w = c.Data.World;
        var hostBlock = c.HostBlock();
        var at = (int)c.Data.Host.Location;
        var host = new Position(BitConverter.ToSingle(hostBlock, at), BitConverter.ToSingle(hostBlock, at + 4), BitConverter.ToSingle(hostBlock, at + 8));
        for (var worldOffset = 0u; game != 0 && worldOffset < 0x80; worldOffset += 4)
        {
            if (!c.Game.TryReadUInt32(game + worldOffset, out var world) || world < 0x10000)
                continue;
            for (var managerOffset = 0u; managerOffset < 0x80; managerOffset += 4)
            {
                if (!c.Game.TryReadUInt32(world + managerOffset, out var manager) || manager < 0x10000
                    || !c.Game.TryReadUInt32(manager + list.Count, out var count) || count is < 1 or > 5000
                    || !c.Game.TryReadUInt32(manager + list.SlotArray, out var slots)
                    || !MemoryBlock.TryRead(c.Game, slots, w.SlotCount * 4, out var array))
                    continue;

                var objects = new List<uint>();
                for (var i = 0u; i < w.SlotCount && objects.Count <= count; i++)
                {
                    for (var node = array.UInt32(i * 4); node != 0 && objects.Count <= count; )
                    {
                        if (!c.Game.TryReadUInt32(node + w.ObjectInSlot, out var obj) || !c.Game.TryReadUInt32(node, out var next))
                            break;
                        objects.Add(obj);
                        node = next;
                    }
                }

                if (objects.Count != count || !objects.All(fits))
                    continue;
                var near = objects.All(o => MemoryBlock.TryRead(c.Game, o + location, 12, out var p)
                                            && new Position(p.Float(0), p.Float(4), p.Float(8)) is var pos && pos.IsFinite
                                            && pos.DistanceTo(host) < NearbyMeters);
                if (near)
                    result.Add((worldOffset, managerOffset, (int)count));
            }
        }

        return result;
    }

    private static PortResult Bag(PortContext c)
    {
        var w = c.Reader().Read();
        if (w.Inventory.Count == 0)
        {
            var block = c.HostBlock();
            var hits = Pointers(block).Where(o => LooksLikeBag(c, BitConverter.ToUInt32(block, (int)o))).ToList();
            var found = Choose(c, "host.inventory", c.Data.Host.Inventory, hits, "сумка: ячейки с предметами");
            if (found.Status == PortStatus.Failed)
                return found;
            w = c.Reader().Read();
        }

        // Описания банок и корма: указатель в предмете, по которому +0x0 — тот же tid
        var inv = c.Data.Inventory;
        var cells = BagItems(c);
        var potions = cells.Where(x => x.Category == inv.CategoryPotion).ToList();
        if (w.Inventory.Any(i => i.Potion is { } p && (p.Hp < 0 || p.Mp < 0 || p.RequiredLevel is < 0 or > 150)) && potions.Count > 0)
            Choose(c, "inventory.itemEssence", inv.ItemEssence, Common(c, potions.Select(x => x.Address), 0x100, (b, o) => c.Game.TryReadUInt32(b.UInt32(o), out var id) && id == Tid(potions, b.Address)), "описание банки: +0x0 = её tid");
        var food = cells.Where(x => x.Category == inv.CategoryPetFood).ToList();
        if (w.Inventory.Any(i => i.FoodLoyalty is < 0 or > 1000) && food.Count > 0)
            Choose(c, "inventory.foodEssence", inv.FoodEssence, Common(c, food.Select(x => x.Address), 0x100, (b, o) => c.Game.TryReadUInt32(b.UInt32(o), out var id) && id == Tid(food, b.Address)), "описание корма: +0x0 = его tid");
        w = c.Reader().Read();

        Console.WriteLine($"  Сумка: занято {w.Inventory.Count} из {w.InventorySlots} (ячейки с 0)");
        foreach (var i in w.Inventory.Take(40))
        {
            var what = i.Potion is { } p ? $"банка HP {p.Hp} MP {p.Mp}, ур. {p.RequiredLevel}"
                : i.IsPetFood ? $"корм, верность {i.FoodLoyalty}"
                : c.Data.GatherTools.Contains(i.Tid) ? "кирка" : $"категория {i.Category}";
            Console.WriteLine($"    {i.Slot,3}: tid {i.Tid,6} ×{i.Count,-4} {what}");
        }

        return c.Confirm("Сумка как в игре (сколько чего, банки, корм)?") switch
        {
            true => PortResult.Ok($"предметов {w.Inventory.Count}"),
            false => PortResult.Fail("владелец: сумка не такая — проверить inventory.*"),
            null => PortResult.Likely($"предметов {w.Inventory.Count}, не подтверждено"),
        };
    }

    private static uint Tid(IEnumerable<(uint Address, uint Tid, uint Category)> items, uint address) => items.First(x => x.Address == address).Tid;

    /// <summary>Предметы сумки: адрес, tid, категория — по смещениям профиля.</summary>
    private static List<(uint Address, uint Tid, uint Category)> BagItems(PortContext c)
    {
        var inv = c.Data.Inventory;
        var result = new List<(uint, uint, uint)>();
        if (!c.Game.TryReadUInt32(c.HostAddress() + c.Data.Host.Inventory, out var bag) || !c.Game.TryReadUInt32(bag + inv.Size, out var size)
            || size > 300 || !c.Game.TryReadUInt32(bag + inv.Items, out var array))
            return result;
        for (var slot = 0u; slot < size; slot++)
        {
            if (c.Game.TryReadUInt32(array + slot * 4, out var item) && item != 0
                && c.Game.TryReadUInt32(item + inv.ItemTid, out var tid) && c.Game.TryReadUInt32(item + inv.ItemCategory, out var category))
                result.Add((item, tid, category));
        }

        return result;
    }

    private static bool LooksLikeBag(PortContext c, uint bag)
    {
        var inv = c.Data.Inventory;
        if (bag < 0x10000 || !c.Game.TryReadUInt32(bag + inv.Size, out var size) || size is < 8 or > 300
            || !c.Game.TryReadUInt32(bag + inv.Items, out var cells) || !MemoryBlock.TryRead(c.Game, cells, (int)size * 4, out var array))
            return false;
        var good = 0;
        for (var slot = 0u; slot < size; slot++)
        {
            var item = array.UInt32(slot * 4);
            if (item == 0)
                continue;
            if (!MemoryBlock.TryRead(c.Game, item, (int)Math.Max(inv.ItemCount, inv.ItemMaxCount) + 4, out var b))
                return false;
            var (tid, count, max) = (b.UInt32(inv.ItemTid), b.Int32(inv.ItemCount), b.Int32(inv.ItemMaxCount));
            if (tid is 0 or > 200000 || count < 1 || count > max || max > 100000)
                return false;
            good++;
        }

        return good > 0;
    }

    private static PortResult Skills(PortContext c)
    {
        var w = c.Reader().Read();
        if (c.Names.Count == 0)
            return PortResult.Fail("названия скиллов не прочитаны из configs.pck — сверить не с чем");
        if (w.Skills.Count == 0 || w.Skills.Any(s => s.Name is null))
        {
            var block = c.HostBlock();
            var hits = Pointers(block).Where(o => o + 8 <= block.Length && IsSkillArray(c, BitConverter.ToUInt32(block, (int)o), BitConverter.ToInt32(block, (int)o + 4))).ToList();
            var found = Choose(c, "host.skills", c.Data.Host.Skills, hits, "массив скиллов с названиями из игры");
            if (found.Status == PortStatus.Failed)
                return found;
            SetIfChanged(c, "host.skillsCount", c.Data.Host.SkillsCount, c.Data.Host.Skills + 4, "сразу за массивом");
            w = c.Reader().Read();
        }

        Console.WriteLine("  " + string.Join(", ", w.Skills.Select(s => $"{s.Id} {s.Name}")));
        var attack = c.Data.Skills.DefaultAttack;
        return w.Skills.Count > 0 && w.Skills.All(s => s.Name is not null)
            ? PortResult.Ok($"скиллов {w.Skills.Count}{(w.Skills.Any(s => s.Id == attack) ? $", атака {attack} есть" : $", атаки {attack} нет — другой класс?")}")
            : PortResult.Fail("у скиллов нет названий — skill.id неверно?");
    }

    private static bool IsSkillArray(PortContext c, uint array, int count)
    {
        if (array < 0x10000 || count is < 1 or > 300 || !MemoryBlock.TryRead(c.Game, array, count * 4, out var pointers))
            return false;
        for (var i = 0u; i < count; i++)
        {
            if (!c.Game.TryReadUInt32(pointers.UInt32(i * 4) + c.Data.Skill.Id, out var id) || c.Names.Get((int)id) is null)
                return false;
        }

        return true;
    }

    // ───────────────────────── с владельцем ─────────────────────────

    private static PortResult Target(PortContext c)
    {
        if (!c.Do("Выберите мышкой любого моба рядом"))
            return PortResult.Skip("пропущено");
        var w = c.Reader().Read();
        var wids = w.Npcs.Select(n => n.Wid).Where(x => x != 0).ToHashSet();
        var block = c.HostBlock();
        var hits = Offsets(block, o => wids.Contains(BitConverter.ToUInt32(block, (int)o))).ToList();
        var found = Choose(c, "host.targetId", c.Data.Host.TargetId, hits, "WID моба из списка");
        if (found.Status == PortStatus.Failed)
            return PortResult.Fail("в персонаже нет WID ни одного моба — проверить npc.wid");

        var target = c.Reader().Read().Target;
        Console.WriteLine($"  Цель по памяти: {target?.Name ?? "?"}, ур. {target?.Level}, HP {target?.Hp} (HP игра знает только у цели)");
        var targetOk = c.Confirm("Это ваша цель, и HP как в окошке цели?");
        if (!c.Do("Снимите цель (Esc)"))
            return found with { Text = found.Text + "; снятие цели не проверено" };
        var after = c.Reader().Read().Host.TargetWid;
        if (after != 0)
            return PortResult.Fail($"после снятия цели в поле 0x{after:X8}");
        return targetOk == false
            ? PortResult.Fail($"владелец: цель или её HP не такие (npc.hp 0x{c.Data.Npc.Hp:X})")
            : PortResult.Ok($"цель «{target?.Name}», HP {target?.Hp}, снятие → 0");
    }

    private static PortResult Pet(PortContext c)
    {
        var before = c.HostBlock();
        if (!c.Do("Призовите пета (петов нет — «с»)"))
            return PortResult.Skip("пет не проверен");
        var w = c.Reader().Read();
        if (!(w.Pet?.IsSummoned == true && w.Npcs.Any(n => n.Wid == w.Pet.ActiveWid && n.Kind == NpcKind.Pet)))
        {
            var petWids = w.Npcs.Where(n => n.Kind == NpcKind.Pet).Select(n => n.Wid).ToHashSet();
            var m = c.Data.PetManager;
            var block = c.HostBlock();
            var hits = Pointers(block).Where(o =>
            {
                var p = BitConverter.ToUInt32(block, (int)o);
                return p == BitConverter.ToUInt32(before, (int)o)
                       && c.Game.TryReadUInt32(p + m.ActiveCage, out var cage) && cage < (uint)m.CageCount
                       && c.Game.TryReadUInt32(p + m.ActivePetWid, out var wid) && petWids.Contains(wid);
            }).ToList();
            var found = Choose(c, "host.petManager", c.Data.Host.PetManager, hits, "менеджер: клетка и WID призванного пета");
            if (found.Status == PortStatus.Failed)
                return found;
            w = c.Reader().Read();
        }

        foreach (var cage in w.Pet!.Cages)
            Console.WriteLine($"    клетка {cage.Cage}: {cage.Name ?? "?"}, HP {cage.HpPercent} %, сытость {cage.Hunger}");
        var same = c.Confirm($"Призван пет из клетки {w.Pet.ActiveCage} (с 0) — клетки и HP как в игре?");
        if (!c.Do("Отзовите пета"))
            return PortResult.Likely("призыв виден, отзыв не проверен");
        var recalled = c.Reader().Read().Pet;
        if (recalled?.IsSummoned != false)
            return PortResult.Fail("после отзыва пет всё ещё «призван» — petManager.activeCage/activePetWid");
        return same == false ? PortResult.Fail("владелец: клетки не такие — проверить pet.*") : PortResult.Ok("призыв и отзыв видны");
    }

    private static PortResult Cast(PortContext c)
    {
        if (!c.Do("Выберите моба", "Будьте готовы скастовать атакующий скилл: после Enter у вас 6 секунд"))
            return PortResult.Skip("каст не проверен");
        var rec = c.Record(c.HostBlock, 6, 50);
        var skillIds = c.Reader().Read().Skills.Select(s => (uint)s.Id).ToHashSet();

        // Байт «кастует»: был 0, один отрезок не-нуля 0,3..5 с, потом снова 0
        var flags = Enumerable.Range(0, PortContext.HostSize).Select(i => (uint)i)
            .Where(o => OneRun(rec, s => s[o] != 0, 300, 5000)).ToList();
        // Указатель на кастуемый скилл: был 0, потом объект с id изученного скилла, потом 0
        var skill = Offsets(rec[0].Data, o => BitConverter.ToUInt32(rec[0].Data, (int)o) == 0)
            .Where(o => OneRun(rec, s => BitConverter.ToUInt32(s, (int)o) != 0, 300, 5000)
                        && rec.Select(s => BitConverter.ToUInt32(s.Data, (int)o)).Where(p => p != 0).Distinct().All(p =>
                            c.Game.TryReadUInt32(p + c.Data.Skill.Id, out var id) && skillIds.Contains(id)))
            .ToList();
        return Merge(
        [
            Choose(c, "host.castFlag", c.Data.Host.CastFlag, flags, "байт 0 → не 0 на время каста → 0"),
            Choose(c, "host.castingSkill", c.Data.Host.CastingSkill, skill, "скилл на время каста"),
        ], "");
    }

    private static PortResult Gather(PortContext c)
    {
        if (!c.Do("Подойдите к ресурсу (для руды — кирка в сумке)", "После Enter сразу начните копать — запись 15 секунд"))
            return PortResult.Skip("копание не проверено");
        var rec = c.Record(c.HostBlock, 15, 100);
        var world = c.Reader().Read();
        var ids = world.GroundItems.SelectMany(i => new[] { i.Id, i.Tid }).ToHashSet();
        uint At(int sample, uint o) => BitConverter.ToUInt32(rec[sample].Data, (int)o);

        // Прошло мс: растёт примерно со скоростью часов, от 0..1 с
        var elapsed = Offsets(rec[0].Data, _ => true).Where(o => Clock(rec, o, rising: true)).ToList();
        var results = new List<PortResult> { Choose(c, "host.gatherElapsed", c.Data.Host.GatherElapsed, elapsed, "мс копания растут как часы") };
        if (c.Data.Host.GatherElapsed != 0)
        {
            var max = rec.Max(s => BitConverter.ToUInt32(s.Data, (int)c.Data.Host.GatherElapsed));
            var total = Offsets(rec[0].Data, o => Math.Abs((long)At(rec.Count - 1, o) - max) <= 300 && At(rec.Count - 1, o) is >= 1000 and <= 120000
                                                   && At(rec.Count - 1, o) == At(rec.Count / 2, o) && o != c.Data.Host.GatherElapsed).ToList();
            results.Add(Choose(c, "host.gatherTotal", c.Data.Host.GatherTotal, total, $"всего мс = конец полоски ({max})"));
        }

        var target = Offsets(rec[0].Data, o => At(0, o) == 0 && rec.Any(s => ids.Contains(BitConverter.ToUInt32(s.Data, (int)o)))).ToList();
        results.Add(Choose(c, "host.gatherTarget", c.Data.Host.GatherTarget, target, "0 → id ресурса"));
        return Merge(results, "");
    }

    private static PortResult PetFood(PortContext c)
    {
        if (!c.Do("Призовите пета, корм — в сумке", "После Enter сразу покормите пета — запись 6 секунд"))
            return PortResult.Skip("корм не проверен");
        var rec = c.Record(c.HostBlock, 6, 100);
        var hits = Offsets(rec[0].Data, o => BitConverter.ToUInt32(rec[0].Data, (int)o) < 1000).Where(o => Clock(rec, o, rising: false)).ToList();
        return Choose(c, "host.petFoodCooldown", c.Data.Host.PetFoodCooldown, hits, "мс перезарядки: скачок вверх и убывает как часы");
    }

    private static PortResult Fly(PortContext c)
    {
        if (!c.Do("Стойте на земле", "После Enter взлетите, через пару секунд сядьте — запись 10 секунд (полёта нет — «с»)"))
            return PortResult.Skip("полёт не проверен: функции полёта (hostFly, moveTypes.fly) не тестировались");
        var rec = c.Record(c.HostBlock, 10, 100);
        var hits = Offsets(rec[0].Data, o => BitConverter.ToUInt32(rec[0].Data, (int)o) == 0
                                             && rec.Any(s => BitConverter.ToUInt32(s.Data, (int)o) == 2)
                                             && rec.All(s => BitConverter.ToUInt32(s.Data, (int)o) is 0 or 1 or 2)).ToList();
        return Choose(c, "host.moveEnv", c.Data.Host.MoveEnv, hits, "0 → 2 (воздух)");
    }

    // ───────────────────────── помощники ─────────────────────────

    /// <summary>
    /// Профиль уже прав — подтвердить; одно место — записать; несколько — записать ближайшее к старому и сказать,
    /// что выбор не однозначный.
    /// </summary>
    private static PortResult Choose(PortContext c, string path, uint current, IList<uint> hits, string what)
    {
        if (current != 0 && hits.Contains(current))
            return PortResult.Ok($"{path} 0x{current:X} — {what}{(hits.Count > 1 ? $" (подходит ещё {hits.Count - 1})" : "")}");
        if (hits.Count == 0)
            return PortResult.Fail($"{path}: не найдено ({what})");
        if (hits.Count > 1)
            Console.WriteLine($"  кандидаты {path}: {string.Join(", ", hits.Take(16).Select(h => $"+0x{h:X}"))}{(hits.Count > 16 ? " …" : "")}");
        var best = hits.OrderBy(h => Math.Abs((long)h - current)).First();
        c.Set(path, best, hits.Count == 1 ? what : $"{what}; мест {hits.Count}, взято ближайшее к старому 0x{current:X}");
        return hits.Count == 1 ? PortResult.Ok("") : PortResult.Likely($"{path}: выбор из {hits.Count}");
    }

    private static void SetIfChanged(PortContext c, string path, uint current, uint value, string why)
    {
        if (current != value)
            c.Set(path, value, why);
    }

    private static uint Field(PortContext c, string path)
    {
        var h = c.Data.Host;
        return path switch
        {
            "host.level" => h.Level, "host.hp" => h.Hp, "host.maxHp" => h.MaxHp, "host.mp" => h.Mp, "host.maxMp" => h.MaxMp,
            _ => 0,
        };
    }

    /// <summary>Худший статус из всех; тексты — через «; ».</summary>
    private static PortResult Merge(IList<PortResult> results, string okText)
    {
        if (results.Count == 0)
            return PortResult.Ok(okText);
        var status = results.Max(r => r.Status);
        return new PortResult(status, string.Join("; ", results.Select(r => r.Text).Where(t => t.Length > 0)));
    }

    private static List<double> Numbers(string text)
        => text.Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN)
            .Where(d => !double.IsNaN(d)).ToList();

    private static IEnumerable<uint> Offsets(byte[] block, int value) => Offsets(block, o => BitConverter.ToInt32(block, (int)o) == value);

    private static IEnumerable<uint> Offsets(byte[] block, Func<uint, bool> match)
        => Enumerable.Range(0, block.Length / 4).Select(i => (uint)i * 4).Where(match);

    private static IEnumerable<uint> Pointers(byte[] block) => Offsets(block, o => BitConverter.ToUInt32(block, (int)o) > 0x10000);

    /// <summary>Смещения, где у всех объектов выполняется условие.</summary>
    private static List<uint> Common(PortContext c, IEnumerable<uint> objects, int size, Func<MemoryBlock, uint, bool> match)
    {
        var blocks = objects.Select(a => MemoryBlock.TryRead(c.Game, a, size, out var b) ? b : (MemoryBlock?)null).Where(b => b is not null).Select(b => b!.Value).ToList();
        return Enumerable.Range(0, size / 4).Select(i => (uint)i * 4).Where(o => blocks.Count > 0 && blocks.All(b => match(b, o))).ToList();
    }

    /// <summary>Условие было ложно в начале и в конце записи и истинно ровно одним отрезком длиной minMs..maxMs.</summary>
    private static bool OneRun(List<(double Ms, byte[] Data)> rec, Func<byte[], bool> on, double minMs, double maxMs)
    {
        if (on(rec[0].Data) || on(rec[rec.Count - 1].Data))
            return false;
        int first = -1, last = -1;
        for (var i = 0; i < rec.Count; i++)
        {
            if (!on(rec[i].Data))
                continue;
            if (first < 0)
                first = i;
            else if (last != i - 1)
                return false;
            last = i;
        }

        var ms = first < 0 ? 0 : rec[last].Ms - rec[first].Ms;
        return first >= 0 && ms >= minMs && ms <= maxMs;
    }

    /// <summary>
    /// Поле-«часы» в мс: от первого изменения до последнего меняется монотонно (вверх; или вниз после скачка вверх),
    /// не меньше чем на 1,5 с и со скоростью настоящего времени ±35 %.
    /// </summary>
    private static bool Clock(List<(double Ms, byte[] Data)> rec, uint o, bool rising)
    {
        var v = rec.Select(s => (long)BitConverter.ToUInt32(s.Data, (int)o)).ToList();
        if (v.Any(x => x > 300000))
            return false;
        var changes = Enumerable.Range(1, v.Count - 1).Where(i => v[i] != v[i - 1]).ToList();
        if (changes.Count < 3)
            return false;
        // Убывающие — с вершины скачка, растущие — с последнего значения до хода
        var from = rising ? changes[0] - 1 : changes[0];
        var to = changes[changes.Count - 1];
        if (!rising && v[from] - v[from - 1] < 5000)
            return false;
        for (var i = from + 1; i <= to; i++)
        {
            if (rising ? v[i] < v[i - 1] : v[i] > v[i - 1])
                return false;
        }

        var changed = Math.Abs(v[to] - v[from]);
        var time = rec[to].Ms - rec[from].Ms;
        return changed >= 1500 && time > 0 && changed / time is >= 0.65 and <= 1.35;
    }
}
