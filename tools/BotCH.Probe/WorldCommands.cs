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
    public static int Snapshot(ServerProfile profile, GameProcess game)
    {
        var reader = CreateReader(profile, game, out var names);
        Console.WriteLine(names.Count > 0 ? $"Названия скиллов: {names.Count} из {Path.GetFileName(names.Source)}" : "Названия скиллов не прочитаны");
        var icons = LoadIcons(profile, game);
        Console.Write(Describe(reader.Read(), full: true, icons));
        return 0;
    }

    /// <summary>Снимок раз в <paramref name="periodMs"/> мс до Ctrl+C; показывает только перса, цель и ближайших.</summary>
    public static int Watch(ServerProfile profile, GameProcess game, int periodMs = 300)
    {
        var reader = CreateReader(profile, game, out _);
        // Как видит бот: застрявшая после отагра цель снята, «идёт к нам» проставлено
        var tracker = new NpcTracker();
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
                text = Describe(tracker.Track(reader.Read()), full: false);
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

    /// <summary>
    /// Запись для поиска полей по ходу игры («в бою» и т. п.): раз в 250 мс — что видит снимок (перс; мобы и петы: состояние,
    /// цель, место) и сырые байты их структур. Разбирается вне Probe. Формат (little-endian): «BREC», int версия 1, uint WID
    /// перса; дальше снимки: long мс с начала, uint адрес перса, uint цель перса, byte каст, int длина + байты перса,
    /// int число мобов; у каждого: uint адрес, uint WID, int вид, int состояние, uint цель, float x, h, y, float расстояние,
    /// string название (BinaryWriter), int длина + байты. Не прочиталось — длина 0.
    /// </summary>
    public static int Record(ServerProfile profile, GameProcess game, string[] args)
    {
        const int hostSize = 0x2000, npcSize = 0x800;
        var path = args.FirstOrDefault(a => a.EndsWith(".rec", StringComparison.OrdinalIgnoreCase)) ?? $"record-{profile.Id}.rec";
        var seconds = args.Select(a => int.TryParse(a, out var n) && n != game.Pid ? n : 0).FirstOrDefault(n => n is > 0 and <= 3600);
        if (seconds == 0)
            seconds = 180;

        var reader = CreateReader(profile, game, out _);
        var stop = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop = true;
        };

        byte[] Block(uint address, int size)
        {
            var data = new byte[size];
            return game.TryRead(address, data, size) ? data : [];
        }

        using var file = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8);
        file.Write(Encoding.ASCII.GetBytes("BREC"));
        file.Write(1);
        file.Write(reader.Read().Host.Wid);
        Console.WriteLine($"Запись {seconds} с в {Path.GetFullPath(path)}, Ctrl+C — раньше");

        var watch = Stopwatch.StartNew();
        var count = 0;
        while (!stop && !game.HasExited && watch.Elapsed.TotalSeconds < seconds)
        {
            var started = watch.ElapsedMilliseconds;
            WorldState w;
            try
            {
                w = reader.Read();
            }
            catch (WorldNotReadyException e)
            {
                Console.WriteLine($"{started / 1000,4} с  ⏳ {e.Message}");
                Thread.Sleep(250);
                continue;
            }

            file.Write(started);
            file.Write(w.Host.Address);
            file.Write(w.Host.TargetWid);
            file.Write(w.Host.IsCasting);
            var host = Block(w.Host.Address, hostSize);
            file.Write(host.Length);
            file.Write(host);
            file.Write(w.Npcs.Count);
            foreach (var n in w.Npcs)
            {
                file.Write(n.Address);
                file.Write(n.Wid);
                file.Write((int)n.Kind);
                file.Write((int)n.State);
                file.Write(n.TargetWid);
                file.Write(n.Position.X);
                file.Write(n.Position.Height);
                file.Write(n.Position.Y);
                file.Write(n.Offset.Horizontal);
                file.Write(n.Name);
                var data = Block(n.Address, npcSize);
                file.Write(data.Length);
                file.Write(data);
            }

            file.Flush();
            // Раз в секунду — кто дерётся, чтобы по выводу видеть, что запись идёт и бой в ней есть
            if (count++ % 4 == 0)
            {
                var busy = w.Npcs.Where(n => n.State is NpcState.Attacking or NpcState.Casting)
                    .Select(n => $"{n.Name} {n.State.Text()}{(n.TargetWid != 0 ? $" →0x{n.TargetWid:X8}" : "")}");
                Console.WriteLine($"{started / 1000,4} с  цель перса 0x{w.Host.TargetWid:X8}{(w.Host.IsCasting ? ", каст" : "")}  {string.Join("; ", busy)}");
            }

            Thread.Sleep((int)Math.Max(0, 250 - (watch.ElapsedMilliseconds - started)));
        }

        Console.WriteLine($"Готово: {count} снимков, {new FileInfo(path).Length / 1024} КБ");
        return 0;
    }

    /// <summary>
    /// Кто кого бьёт и на сколько: каждый новый удар из менеджера атак игры (снимок раз в 100 мс), в конце — итог по парам
    /// «кто → кого». Файл .tsv — те же удары таблицей, для разбора вне Probe.
    /// </summary>
    public static int Attacks(ServerProfile profile, GameProcess game, string[] args)
    {
        if (profile.Data.Attacks.Manager == 0)
        {
            Console.Error.WriteLine($"❌ {profile.Name}: в профиле нет менеджера атак (attacks)");
            return 1;
        }

        var path = args.FirstOrDefault(a => a.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase));
        var seconds = args.Select(a => int.TryParse(a, out var n) && n != game.Pid ? n : 0).FirstOrDefault(n => n is > 0 and <= 3600);
        if (seconds == 0)
            seconds = 60;

        var reader = CreateReader(profile, game, out var skills);
        var tracker = new AttackTracker();
        var stop = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop = true;
        };

        using var file = path is null
            ? null
            : new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
        file?.WriteLine("мс\tкто\tWID кто\tкого\tWID кого\tурон\tскилл\tфлаги");
        Console.WriteLine($"Удары {seconds} с{(path is null ? "" : " → " + Path.GetFullPath(path))}, Ctrl+C — раньше");

        var names = new Dictionary<uint, string>();
        var totals = new Dictionary<(uint From, uint To), (int Hits, long Damage, int Min, int Max)>();
        var watch = Stopwatch.StartNew();
        while (!stop && !game.HasExited && watch.Elapsed.TotalSeconds < seconds)
        {
            var started = watch.ElapsedMilliseconds;
            WorldState w;
            try
            {
                w = reader.Read();
            }
            catch (WorldNotReadyException e)
            {
                Console.WriteLine($"{started / 1000,4} с  ⏳ {e.Message}");
                Thread.Sleep(250);
                continue;
            }

            foreach (var attack in tracker.Update(w))
            {
                var from = names[attack.AttackerWid] = Who(w, attack.AttackerWid);
                var skill = attack.Skill == 0 ? "" : $", {skills.Get(attack.Skill) ?? "скилл"} ({attack.Skill})";
                foreach (var hit in attack.Hits)
                {
                    var to = names[hit.TargetWid] = Who(w, hit.TargetWid);
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {from} → {to}: {hit.Damage}{skill}{(hit.Flags != 0 ? $", флаги 0x{hit.Flags:X}" : "")}");
                    file?.WriteLine($"{started}\t{from}\t0x{attack.AttackerWid:X8}\t{to}\t0x{hit.TargetWid:X8}\t{hit.Damage}\t{attack.Skill}\t0x{hit.Flags:X}");
                    if (hit.Damage <= 0)
                        continue;

                    var key = (attack.AttackerWid, hit.TargetWid);
                    (int Hits, long Damage, int Min, int Max) t = totals.TryGetValue(key, out var old) ? old : (0, 0L, int.MaxValue, 0);
                    totals[key] = (t.Hits + 1, t.Damage + hit.Damage, Math.Min(t.Min, hit.Damage), Math.Max(t.Max, hit.Damage));
                }
            }

            file?.Flush();
            Thread.Sleep((int)Math.Max(0, 100 - (watch.ElapsedMilliseconds - started)));
        }

        Console.WriteLine();
        Console.WriteLine(totals.Count == 0 ? "Ударов с уроном не было" : $"Итог за {watch.Elapsed.TotalSeconds:0} с (урон больше 0):");
        foreach (var t in totals.OrderByDescending(t => t.Value.Damage))
            Console.WriteLine($"  {names[t.Key.From]} → {names[t.Key.To]}: {t.Value.Damage} за {t.Value.Hits} уд. ({t.Value.Min}…{t.Value.Max})");
        return 0;
    }

    // Вы, ваш пет, моб или NPC из снимка (с WID — их бывает несколько одинаковых), игрок (WID без старшего бита) или просто WID
    private static string Who(WorldState w, uint wid)
        => wid == w.Host.Wid ? "вы"
            : w.Npcs.FirstOrDefault(n => n.Wid == wid) is { } n
                ? (w.Pet?.ActiveWid == wid ? $"пет «{n.Name}»" : $"{n.Name} ур. {n.Level} ({wid & 0xFFFF:X4})")
                : (wid & 0x8000_0000) == 0 ? $"игрок 0x{wid:X8}" : $"0x{wid:X8}";

    /// <summary>Читает снимок и проверяет, что значения разумные. Плюс замер скорости.</summary>
    public static int SelfTest(ServerProfile profile, GameProcess game)
    {
        var reader = CreateReader(profile, game, out var names);
        var icons = LoadIcons(profile, game);
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

        Check(world.NpcCountInGame is null || world.Npcs.Count == world.NpcCountInGame, $"мобов прочитано {world.Npcs.Count}, в игре {world.NpcCountInGame}");
        var badKind = world.Npcs.Where(n => n.Kind is not (NpcKind.Mob or NpcKind.Npc or NpcKind.Pet)).ToList();
        Check(badKind.Count == 0, $"у всех мобов тип 6/7/9{Bad(badKind.Select(n => $"{n.Name}={(int)n.Kind}"))}");
        var badPos = world.Npcs.Where(n => !n.Position.IsFinite || n.Offset.Direct > 1000).ToList();
        Check(badPos.Count == 0, $"координаты мобов разумные (ближе 1000 м){Bad(badPos.Select(n => n.Name))}");
        var noName = world.Npcs.Count(n => n.Name.Length == 0);
        Check(noName == 0, $"у всех мобов есть название (без названия: {noName})");
        // Бот расстояния считает из координат; число в памяти игры — независимая проверка, что смещения позиций найдены верно
        var data = profile.Data;
        if (data.Npc.Distance != 0)
        {
            var off = world.Npcs.Where(n => Math.Abs(game.ReadFloat(n.Address + data.Npc.Distance) - n.Offset.Horizontal) > 0.5f).ToList();
            Check(off.Count == 0, $"расстояние до моба по координатам = в памяти (по горизонтали, ±0,5 м)"
                                  + Bad(off.Select(n => $"{n.Name} {n.Offset.Horizontal:0.0}≠{game.ReadFloat(n.Address + data.Npc.Distance):0.0}")));
        }

        Check(world.GroundItemCountInGame is null || world.GroundItems.Count == world.GroundItemCountInGame, $"предметов на земле прочитано {world.GroundItems.Count}, в игре {world.GroundItemCountInGame}");
        var badItems = world.GroundItems.Where(i => i.Kind is not (GroundItemKind.Item or GroundItemKind.Resource or GroundItemKind.Money) || !i.Position.IsFinite).ToList();
        Check(badItems.Count == 0, $"у предметов на земле вид 1/2/3 и нормальные координаты{Bad(badItems.Select(i => $"{i.Name}={(int)i.Kind}"))}");
        if (data.GroundItem.Distance != 0)
        {
            var off = world.GroundItems.Where(i => Math.Abs(game.ReadFloat(i.Address + data.GroundItem.Distance) - i.Offset.Direct) > 0.5f).ToList();
            Check(off.Count == 0, $"расстояние до предмета по координатам = в памяти (в 3D, ±0,5 м)"
                                  + Bad(off.Select(i => $"{i.Name} {i.Offset.Direct:0.0}≠{game.ReadFloat(i.Address + data.GroundItem.Distance):0.0}")));
        }

        Check(world.Inventory.Count > 0, $"в сумке есть предметы: {world.Inventory.Count}");
        Check(world.Inventory.All(i => i.Count > 0), "у всех предметов в сумке количество > 0");
        var badPotions = world.Inventory.Where(i => i.Potion is { } p && p.Hp <= 0 && p.Mp <= 0).ToList();
        Check(badPotions.Count == 0, $"у банок HP или MP > 0 (банок: {world.Inventory.Count(i => i.Potion is not null)})");
        var food = world.Inventory.Where(i => i.IsPetFood).ToList();
        if (food.Count > 0)
            Check(food.All(i => i.Name is not null), $"у корма пета есть название{Bad(food.Where(i => i.Name is null).Select(i => i.Tid.ToString()))}");

        Check(world.Skills.Count > 0, $"скиллов изучено: {world.Skills.Count}");
        Check(names.Count > 0, $"названия скиллов прочитаны из файла игры: {names.Count}");
        var unnamed = world.Skills.Where(s => s.Name is null).Select(s => s.Id.ToString()).ToList();
        Check(unnamed.Count == 0, $"у всех скиллов есть название{Bad(unnamed)}");
        var noIcon = world.Skills.Where(s => icons.Get(s.Id) is null).Select(s => s.Id.ToString()).ToList();
        Check(icons.Count > 0 && noIcon.Count == 0, $"у всех скиллов есть иконка (всего иконок: {icons.Count}){Bad(noIcon)}");

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

    /// <summary>
    /// Снимает дамп: читает снимок через <see cref="RecordingMemory"/> и сохраняет только реально прочитанные байты
    /// (остальное на страницах — нули, лишнего из памяти клиента в файл не попадает). Рядом — текстовый снимок для сверки.
    /// </summary>
    public static int Dump(ServerProfile profile, GameProcess game, string[] args)
    {
        var path = args.FirstOrDefault(a => a.EndsWith(".dump", StringComparison.OrdinalIgnoreCase)) ?? $"world-{profile.Id}.dump";
        var names = SkillNames.LoadFromGameDirectory(Path.GetDirectoryName(game.MainModulePath)!, profile.Data.GameFiles.Pck, out _);
        var recording = new RecordingMemory(game);
        // Базу ищем по живому клиенту: через запись прочитался бы в дамп весь код
        var pointer = GameRoots.Find(game, game.MainModuleBase, profile.Data).Pointer;
        var world = new WorldReader(recording, new GameRoots(recording, pointer, profile.Data), profile.Data, names.Get).Read();

        new MemoryDump(profile.Id, game.MainModuleBase, recording.Image).Save(path);
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), Describe(world, full: true), new UTF8Encoding(true));
        Console.WriteLine($"Дамп: {path} ({recording.Image.PageCount} страниц, {new FileInfo(path).Length / 1024} КБ), снимок: {Path.ChangeExtension(path, ".txt")}");
        return 0;
    }

    private static string Bad(IEnumerable<string> items)
    {
        var list = items.Take(5).ToList();
        return list.Count == 0 ? "" : " — " + string.Join(", ", list);
    }

    // Высота объекта над персонажем, если заметна: «↑30», «↓113»
    private static string Height(Offset offset)
        => Math.Abs(offset.Vertical) < 5 ? "" : $"{(offset.Vertical > 0 ? "↑" : "↓")}{Math.Abs(offset.Vertical):0}";

    private static SkillIcons LoadIcons(ServerProfile profile, GameProcess game)
    {
        var icons = SkillIcons.Load(game, profile.Data, out var problem);
        Console.WriteLine(problem is not null ? "⚠️ Иконки скиллов: " + problem : $"Иконки скиллов: {icons.Count}");
        return icons;
    }

    private static WorldReader CreateReader(ServerProfile profile, GameProcess game, out SkillNames names)
    {
        names = SkillNames.LoadFromGameDirectory(Path.GetDirectoryName(game.MainModulePath)!, profile.Data.GameFiles.Pck, out var problem);
        if (problem is not null)
            Console.WriteLine("⚠️ " + problem);

        var loaded = names;
        return new WorldReader(game, GameRoots.Find(game, game.MainModuleBase, profile.Data), profile.Data, loaded.Get);
    }

    private static string Describe(WorldState w, bool full, SkillIcons? icons = null)
    {
        var s = new StringBuilder();
        var h = w.Host;
        s.AppendLine($"Снимок {w.Time.ToLocalTime():HH:mm:ss.fff}, прочитан за {w.ReadDuration.TotalMilliseconds:0.0} мс");
        s.AppendLine($"Перс   {h.Name}, ур. {h.Level}, WID 0x{h.Wid:X8}");
        s.AppendLine($"       HP {h.Hp}/{h.MaxHp} ({h.HpPercent} %)  MP {h.Mp}/{h.MaxMp?.ToString() ?? "?"}  {h.Position}");
        s.AppendLine($"       каст: {(h.IsCasting ? "да" : "нет")}, корм пета: {(h.PetFoodCooldownMs == 0 ? "можно" : $"перезарядка {h.PetFoodCooldownMs / 1000.0:0} с")}");

        var target = w.Target;
        s.AppendLine(h.TargetWid == 0 ? "Цель   нет" : target is null
            ? $"Цель   0x{h.TargetWid:X8} (не моб — игрок/NPC вне списка?)"
            : $"Цель   {target.Name} 0x{target.Wid:X8}, HP {target.Hp}, {target.Offset}, состояние {State(target)}");

        s.AppendLine(w.Pet is { } pet
            ? $"Пет    {(pet.IsSummoned ? $"призван из клетки {pet.ActiveCage}, WID 0x{pet.ActiveWid:X8}" : "не призван")}; " +
              string.Join("; ", pet.Cages.Select(c => $"клетка {c.Cage}: {(c.Name is { } name ? $"{name} ({PetHabitats.Text(c.Habitat)}), " : "")}HP {c.HpPercent} %{(c.Hp is { } hp ? $" ({hp}/{c.MaxHp})" : "")}, ур. {c.Level}, сытость {c.Hunger}{(c.IsAlive ? "" : " (мёртв)")}"))
            : "Пет    нет");

        var npcs = w.Npcs.OrderBy(n => n.Offset.Horizontal).Take(full ? 10 : 5).ToList();
        s.AppendLine($"Мобы   {w.Npcs.Count} (в игре {w.NpcCountInGame?.ToString() ?? "?"}), ближайшие:");
        foreach (var n in npcs)
        {
            var aggro = n.TargetWid == 0 ? "" : n.TargetWid == h.Wid ? "  ⚔ бьёт перса" : w.Pet?.ActiveWid == n.TargetWid ? "  ⚔ бьёт пета" : $"  → 0x{n.TargetWid:X8}";
            s.AppendLine($"       {n.Offset.Horizontal,6:0.0} м {Height(n.Offset),6}  {Kind(n.Kind),-4} {(n.Level > 0 ? $"ур.{n.Level,3}" : "      ")} {n.Name,-24} 0x{n.Wid:X8}  {State(n)}{(n.Aggressive == true ? $"  агр {n.AggroRadius} м" : "")}{aggro}");
        }

        if (full)
        {
            var names = w.Mobs.GroupBy(n => n.Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} ×{g.Count()}");
            s.AppendLine($"       мобы по названиям: {string.Join(", ", names)}");
        }

        s.AppendLine($"Лут    {w.GroundItems.Count} (в игре {w.GroundItemCountInGame?.ToString() ?? "?"})");
        foreach (var i in w.GroundItems.OrderBy(i => i.Offset.Direct).Take(full ? 10 : 3))
            s.AppendLine($"       {i.Offset.Horizontal,6:0.0} м {Height(i.Offset),6}  {ItemKind(i.Kind),-7} {i.Name}{(i.Group is { } group ? $" [{group}]" : "")} (id 0x{i.Id:X8}, tid {i.Tid})");

        if (!full)
            return s.ToString();

        s.AppendLine($"Сумка  занято {w.Inventory.Count} из {w.InventorySlots} ячеек{(w.BagFull ? " — ПОЛНА" : "")}");
        foreach (var i in w.Inventory.Where(i => i.Potion is not null))
        {
            var p = i.Potion!;
            s.AppendLine($"       банка  ячейка {i.Slot,2}: tid {i.Tid} ×{i.Count}, ур. {p.RequiredLevel}, HP {p.Hp}/{p.HpSeconds} с, MP {p.Mp}/{p.MpSeconds} с");
        }

        foreach (var i in w.Inventory.Where(i => i.IsPetFood))
            s.AppendLine($"       корм   ячейка {i.Slot,2}: {i.Name ?? "?"} (tid {i.Tid}) ×{i.Count}, верность {i.FoodLoyalty}");

        s.AppendLine($"       прочее: {string.Join(", ", w.Inventory.Where(i => i.Potion is null && !i.IsPetFood).Select(i => $"{i.Tid}×{i.Count}"))}");

        s.AppendLine($"Скиллы {w.Skills.Count}");
        foreach (var k in w.Skills)
            s.AppendLine($"       {k.Id,5} ур. {k.Level,2}  {k.Name ?? "(нет названия)",-28} {(k.IsReady ? "готов" : $"перезарядка {k.CooldownLeftMs / 1000.0:0.0}/{k.CooldownFullMs / 1000.0:0.0} с")}"
                         + (icons is null ? "" : icons.Get(k.Id) is null ? "  без иконки" : $"  {icons.FileOf(k.Id)}"));
        s.AppendLine($"Макросы {(w.Macros.Count == 0 ? "нет" : string.Join(", ", w.Macros.Select(m => $"{m.Number} (иконка {m.Icon})")))}");

        return s.ToString();
    }

    private static string State(NpcInfo n) => n.State.Text() ?? $"состояние {(int)n.State}";

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
