using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BotCH.Core.GameFiles;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.World;

namespace BotCH.Probe;

/// <summary>Снимок мира: печать, слежение, самопроверка. Только чтение.</summary>
internal static class WorldCommands
{
    public static int Snapshot(IServerProfile profile, GameProcess game)
    {
        var reader = CreateReader(profile, game, out var names);
        Console.WriteLine(names.Count > 0 ? $"Названия скиллов: {names.Count} из {Path.GetFileName(names.Source)}" : "Названия скиллов не прочитаны");
        Console.Write(Describe(reader.Read(), full: true));
        return 0;
    }

    /// <summary>Снимок раз в <paramref name="periodMs"/> мс до Ctrl+C; показывает только перса, цель и ближайших.</summary>
    public static int Watch(IServerProfile profile, GameProcess game, int periodMs = 300)
    {
        var reader = CreateReader(profile, game, out _);
        var stop = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop = true;
        };

        while (!stop && !game.HasExited)
        {
            string text;
            try
            {
                text = Describe(reader.Read(), full: false);
            }
            catch (WorldNotReadyException e)
            {
                text = "⏳ " + e.Message + Environment.NewLine;
            }

            Console.Clear();
            Console.Write(text);
            Console.WriteLine("Ctrl+C — выход");
            Thread.Sleep(periodMs);
        }

        return 0;
    }

    /// <summary>Читает снимок и проверяет, что значения разумные. Плюс замер скорости.</summary>
    public static int SelfTest(IServerProfile profile, GameProcess game)
    {
        var reader = CreateReader(profile, game, out var names);
        var world = reader.Read();
        var checks = new List<(bool Ok, string Text)>();
        void Check(bool ok, string text) => checks.Add((ok, text));

        var h = world.Host;
        Check(h.Name.Length > 0, $"ник не пустой: «{h.Name}»");
        Check(h.Level is >= 1 and <= 150, $"уровень 1..150: {h.Level}");
        Check(h.MaxHp > 0 && h.Hp >= 0 && h.Hp <= h.MaxHp, $"0 ≤ HP ≤ MaxHP: {h.Hp}/{h.MaxHp}");
        Check(h.Mp >= 0 && (h.MaxMp is null || h.Mp <= h.MaxMp), $"MP ≥ 0{(h.MaxMp is null ? " (MaxMP не найдено)" : " и ≤ MaxMP")}: {h.Mp}/{h.MaxMp?.ToString() ?? "?"}");
        Check(h.Wid != 0, $"WID перса: 0x{h.Wid:X8}");
        Check(h.Position.IsFinite, $"координаты перса: {h.Position}");

        Check(world.NpcCountInGame < 0 || world.Npcs.Count == world.NpcCountInGame, $"мобов прочитано {world.Npcs.Count}, в игре {world.NpcCountInGame}");
        var badKind = world.Npcs.Where(n => n.Kind is not (NpcKind.Mob or NpcKind.Npc or NpcKind.Pet)).ToList();
        Check(badKind.Count == 0, $"у всех мобов тип 6/7/9{Bad(badKind.Select(n => $"{n.Name}={(int)n.Kind}"))}");
        var badPos = world.Npcs.Where(n => !n.Position.IsFinite || n.Distance < 0 || n.Distance > 1000).ToList();
        Check(badPos.Count == 0, $"координаты и дистанция мобов разумные{Bad(badPos.Select(n => n.Name))}");
        var noName = world.Npcs.Count(n => n.Name.Length == 0);
        Check(noName == 0, $"у всех мобов есть название (без названия: {noName})");
        var distanceMismatch = world.Npcs.Where(n => Math.Abs(n.Position.DistanceTo(h.Position) - n.Distance) > 2).ToList();
        Check(distanceMismatch.Count == 0, $"дистанция моба совпадает с расчётом по координатам (±2 м){Bad(distanceMismatch.Select(n => $"{n.Name} {n.Distance:0.0}≠{n.Position.DistanceTo(h.Position):0.0}"))}");

        Check(world.GroundItemCountInGame < 0 || world.GroundItems.Count == world.GroundItemCountInGame, $"предметов на земле прочитано {world.GroundItems.Count}, в игре {world.GroundItemCountInGame}");
        var badItems = world.GroundItems.Where(i => i.Kind is not (GroundItemKind.Item or GroundItemKind.Resource or GroundItemKind.Money) || !i.Position.IsFinite).ToList();
        Check(badItems.Count == 0, $"у предметов на земле вид 1/2/3 и нормальные координаты{Bad(badItems.Select(i => $"{i.Name}={(int)i.Kind}"))}");

        Check(world.Inventory.Count > 0, $"в сумке есть предметы: {world.Inventory.Count}");
        Check(world.Inventory.All(i => i.Count > 0), "у всех предметов в сумке количество > 0");
        var badPotions = world.Inventory.Where(i => i.Potion is { } p && p.Hp <= 0 && p.Mp <= 0).ToList();
        Check(badPotions.Count == 0, $"у банок HP или MP > 0 (банок: {world.Inventory.Count(i => i.Potion is not null)})");

        Check(world.Skills.Count > 0, $"скиллов изучено: {world.Skills.Count}");
        Check(names.Count > 0, $"названия скиллов прочитаны из файла игры: {names.Count}");
        var unnamed = world.Skills.Where(s => s.Name is null).Select(s => s.Id.ToString()).ToList();
        Check(unnamed.Count == 0, $"у всех скиллов есть название{Bad(unnamed)}");

        if (world.Pet is { } pet)
        {
            Check(pet.Cages.All(c => c.HpRatio is >= 0 and <= 1), "HP петов в пределах 0..100 %");
            Check(!pet.IsSummoned || world.Npcs.Any(n => n.Wid == pet.ActiveWid && n.Kind == NpcKind.Pet),
                $"призванный пет есть в списке мобов с типом 9: {(pet.IsSummoned ? $"0x{pet.ActiveWid:X8}" : "не призван")}");
        }
        else
        {
            Console.WriteLine("ℹ️ Петов нет — проверки пета пропущены (это нормально для не-друида)");
        }

        var times = Enumerable.Range(0, 20).Select(_ =>
        {
            var watch = Stopwatch.StartNew();
            reader.Read();
            return watch.Elapsed.TotalMilliseconds;
        }).OrderBy(t => t).ToList();
        var median = times[times.Count / 2];
        Check(median <= 50, $"скорость чтения снимка ≤ 50 мс: медиана {median:0.0} мс, макс {times.Last():0.0} мс");

        foreach (var (ok, text) in checks)
            Console.WriteLine($"{(ok ? "✅" : "❌")} {text}");

        var failed = checks.Count(c => !c.Ok);
        Console.WriteLine(failed == 0 ? $"Все проверки пройдены ({checks.Count})." : $"Не пройдено: {failed} из {checks.Count}");
        return failed == 0 ? 0 : 4;
    }

    private static string Bad(IEnumerable<string> items)
    {
        var list = items.Take(5).ToList();
        return list.Count == 0 ? "" : " — " + string.Join(", ", list);
    }

    private static WorldReader CreateReader(IServerProfile profile, GameProcess game, out SkillNames names)
    {
        names = SkillNames.LoadFromGameDirectory(Path.GetDirectoryName(game.MainModulePath)!, out var problem);
        if (problem is not null)
            Console.WriteLine("⚠️ " + problem);

        var loaded = names;
        return new WorldReader(game, game.MainModuleBase, profile.Data, loaded.Get);
    }

    private static string Describe(WorldState w, bool full)
    {
        var s = new StringBuilder();
        var h = w.Host;
        s.AppendLine($"Снимок {w.Time:HH:mm:ss.fff}, прочитан за {w.ReadDuration.TotalMilliseconds:0.0} мс");
        s.AppendLine($"Перс   {h.Name}, ур. {h.Level}, WID 0x{h.Wid:X8}");
        s.AppendLine($"       HP {h.Hp}/{h.MaxHp} ({h.HpPercent} %)  MP {h.Mp}/{h.MaxMp?.ToString() ?? "?"}  {h.Position}");
        s.AppendLine($"       каст: {(h.IsCasting ? "да" : "нет")}, банка HP: {(h.HpPotionReady ? "готова" : "перезарядка")}");

        var target = w.Target;
        s.AppendLine(h.TargetWid == 0 ? "Цель   нет" : target is null
            ? $"Цель   0x{h.TargetWid:X8} (не моб — игрок/NPC вне списка?)"
            : $"Цель   {target.Name} 0x{target.Wid:X8}, HP {target.Hp}, {target.Distance:0.0} м, состояние {State(target)}");

        s.AppendLine(w.Pet is { } pet
            ? $"Пет    {(pet.IsSummoned ? $"призван из клетки {pet.ActiveCage}, WID 0x{pet.ActiveWid:X8}" : "не призван")}; " +
              string.Join("; ", pet.Cages.Select(c => $"клетка {c.Cage}: HP {c.HpPercent} %, сытость {c.Hunger}{(c.IsAlive ? "" : " (мёртв)")}"))
            : "Пет    нет");

        var npcs = w.Npcs.OrderBy(n => n.Distance).Take(full ? 10 : 5).ToList();
        s.AppendLine($"Мобы   {w.Npcs.Count} (в игре {w.NpcCountInGame}), ближайшие:");
        foreach (var n in npcs)
        {
            var aggro = n.TargetWid == 0 ? "" : n.TargetWid == h.Wid ? "  ⚔ бьёт перса" : w.Pet?.ActiveWid == n.TargetWid ? "  ⚔ бьёт пета" : $"  → 0x{n.TargetWid:X8}";
            s.AppendLine($"       {n.Distance,6:0.0} м  {Kind(n.Kind),-4} {n.Name,-24} 0x{n.Wid:X8}  {State(n)}{aggro}");
        }

        if (full)
        {
            var names = w.Mobs.GroupBy(n => n.Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} ×{g.Count()}");
            s.AppendLine($"       мобы по названиям: {string.Join(", ", names)}");
        }

        s.AppendLine($"Лут    {w.GroundItems.Count} (в игре {w.GroundItemCountInGame})");
        foreach (var i in w.GroundItems.OrderBy(i => i.Distance).Take(full ? 10 : 3))
            s.AppendLine($"       {i.Distance,6:0.0} м  {ItemKind(i.Kind),-7} {i.Name} (id 0x{i.Id:X8}, tid {i.Tid})");

        if (!full)
            return s.ToString();

        s.AppendLine($"Сумка  {w.Inventory.Count} предметов");
        foreach (var i in w.Inventory.Where(i => i.Potion is not null))
        {
            var p = i.Potion!;
            s.AppendLine($"       банка  ячейка {i.Slot,2}: tid {i.Tid} ×{i.Count}, ур. {p.RequiredLevel}, HP {p.Hp}/{p.HpSeconds} с, MP {p.Mp}/{p.MpSeconds} с");
        }

        foreach (var i in w.Inventory.Where(i => i.IsPetFood))
            s.AppendLine($"       корм   ячейка {i.Slot,2}: tid {i.Tid} ×{i.Count}, верность {i.FoodLoyalty}");

        s.AppendLine($"       прочее: {string.Join(", ", w.Inventory.Where(i => i.Potion is null && !i.IsPetFood).Select(i => $"{i.Tid}×{i.Count}"))}");

        s.AppendLine($"Скиллы {w.Skills.Count}");
        foreach (var k in w.Skills)
            s.AppendLine($"       {k.Id,5} ур. {k.Level,2}  {k.Name ?? "(нет названия)",-28} {(k.IsReady ? "готов" : $"перезарядка {k.CooldownLeftMs / 1000.0:0.0}/{k.CooldownFullMs / 1000.0:0.0} с")}");

        return s.ToString();
    }

    private static string State(NpcInfo n) => n.State switch
    {
        1 => "стоит",
        2 => "бьёт",
        3 => "кастует",
        4 => "мёртв",
        5 => "идёт",
        var x => $"состояние {x}",
    };

    private static string Kind(NpcKind kind) => kind switch
    {
        NpcKind.Mob => "моб",
        NpcKind.Npc => "NPC",
        NpcKind.Pet => "пет",
        _ => $"?{(int)kind}",
    };

    private static string ItemKind(GroundItemKind kind) => kind switch
    {
        GroundItemKind.Item => "предмет",
        GroundItemKind.Resource => "ресурс",
        GroundItemKind.Money => "монеты",
        _ => $"?{(int)kind}",
    };
}
