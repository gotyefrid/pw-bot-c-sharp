using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Notes.Core.Actions;
using Notes.Core.Calls;
using Notes.Core.Clients;
using Notes.Core.Logging;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.Session;
using Notes.Core.World;

namespace Notes.Probe;

/// <summary>
/// Полуавто-тесты действий в игре. ВЫЗЫВАЮТ ФУНКЦИИ ИГРЫ (поток в процессе клиента) — запускает владелец.
/// Каждый тест: снимок → одно действие → снимки раз в 200 мс → ✅/❌ и время до подтверждения.
/// </summary>
internal static class ActCommands
{
    public const string Usage = """
          act target            выбрать ближайшего живого моба
          act untarget          снять цель
          act npc-hello [имя]   начать разговор с NPC в цели или ближайшим NPC, в имени которого есть «имя» (откроется его окно)
          act npc-bank          открыть банк у банкира, с которым начат разговор (после npc-hello)
          act quest-return НОМЕР  сдать задание NPC, с которым начат разговор (после npc-hello); номер — из quests
          act bank              открыть банк, как кнопкой в сумке (смотреть в игре)
          act bag-split ЯЧ ПУСТАЯ_ЯЧ ЧИСЛО   отделить часть стопки в сумке в пустую ячейку
          act bank-put ЯЧ_СУМКИ ЯЧ_БАНКА [ЧИСЛО]   положить стопку из сумки в пустую ячейку банка или доложить (ЧИСЛО штук,
                                по умолчанию сколько влезет) на стопку того же предмета (банк открыт)
          act bank-take ЯЧ_БАНКА ЯЧ_СУМКИ  взять стопку из банка в пустую ячейку сумки (банк открыт)
          act attack            обычная атака текущей цели
          act skill [id]        скилл пакетом по текущей цели (по умолчанию атакующий)
          act skill-approach [id]  скилл «как кнопкой»: подойти и скастовать
          act macro [номер]     запустить макрос персонажа по текущей цели (по умолчанию 1) — как кнопкой на панели
          act skill-self [id]   скилл пакетом без цели (по умолчанию воскрешение пета, без проверки «пет мёртв»)
          act potion-hp / potion-mp   выпить самую слабую подходящую банку
          act pet-food          покормить пета (если сыт — отказ, это нормально)
          act pet-summon [клетка]  призвать пета (по умолчанию 1)
          act pet-heal          вылечить призванного пета
          act pet-revive [клетка]  воскресить мёртвого пета
          act pet-attack        приказать пету атаковать цель
          act pickup            подобрать ближайший предмет «как мышкой»
          act cancel            прервать каст или копание (как Esc)
          act gather            собрать ближайший ресурс (до 50 м) «как мышкой»; для руды нужна кирка в сумке
          act move <dx> <dy>    отойти на dx, dy метров от текущего места
          act move-mob          дойти до ближайшего живого моба (его точка — точно на земле)
          act npc-go [имя]      подойти к NPC и заговорить, как вторым кликом по нему (ведёт игра)
          act move-npc [имя]    дойти до NPC в цели или до ближайшего NPC, в имени которого есть «имя» (Банкир)
          act move-to <x> <y>   дойти до точки в координатах карты игры (как в углу экрана), не дальше 1000 м
          act fly               взлететь (на земле) или сесть (в воздухе) — как кнопкой «Полёт»
          act fly-to <dx> <dy> <dh>  в воздухе: лететь на dx, dy метров в сторону и dh вверх (минус — вниз), не дальше 100 м
        """;

    public static int Act(ServerProfile profile, string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine(Usage);
            return 1;
        }

        var name = args[0];
        var rest = args.Skip(1).ToArray();
        using var game = Program.OpenClient(rest, GameProcessRights.Execute);
        var data = profile.Data;
        var roots = GameRoots.Find(game, game.MainModuleBase, data);
        var reader = new WorldReader(game, roots, data);

        // Вызовы — в главном потоке игры через её окно; --thread — по-старому, отдельным потоком
        var transport = rest.Contains("--thread") ? CallTransport.Thread : CallTransport.Window;
        using var calls = GameCalls.Open(game, roots, data, transport, out var callsProblem);
        if (calls is null)
        {
            Console.WriteLine($"❌ вызовы не подключились: {callsProblem}");
            return 3;
        }

        Console.WriteLine(transport == CallTransport.Thread ? "Вызовы: отдельным потоком (--thread)" : "Вызовы: в главном потоке игры (через окно)");
        var log = new Logger { MinLevel = LogLevel.Debug }.AddSink(new ConsoleSink()).For("act");
        var runner = new ActionRunner(new GameControl(calls.Caller), log);

        var world = reader.Read();
        var action = Build(name, rest, world, data, reader, out var problem);
        if (action is null)
        {
            Console.WriteLine($"❌ {problem}");
            return 2;
        }

        Console.WriteLine($"Действие: {action.Name}");
        var submit = runner.Submit(new ActionOwner("probe"), action, world);
        if (!submit.Sent)
        {
            Console.WriteLine($"❌ не отправлено: {submit.Outcome?.Details}");
            return 3;
        }

        var watch = Stopwatch.StartNew();
        var seen = Seen(action, world);
        Console.WriteLine($"  {0.0,5:0.0} с  {seen}");
        while (watch.Elapsed < action.Timeout + TimeSpan.FromSeconds(1))
        {
            Thread.Sleep(200);
            var now = reader.Read();
            if (Seen(action, now) is var next && next != seen)
                Console.WriteLine($"  {watch.Elapsed.TotalSeconds,5:0.0} с  {seen = next}");

            var outcome = runner.Update(now).FirstOrDefault();
            if (outcome is null)
                continue;

            var ok = outcome.Status == ActionStatus.Confirmed;
            Console.WriteLine($"{(ok ? "✅" : "❌")} {outcome}");
            return ok ? 0 : 4;
        }

        Console.WriteLine("❌ нет ответа");
        return 4;
    }

    // Что видно по ходу действия — печатается при каждой перемене: каст; у воскрешения ещё скилл и пет в клетке
    private static string Seen(GameAction action, WorldState w)
    {
        var host = w.Host;
        var cast = $"каст {(host.IsCasting ? "да" : "нет")}{(host.CastingSkillId is int id and not 0 ? $" ({id})" : "")}";
        if (action is not RevivePetAction revive)
            return cast;

        var skill = w.Skill(revive.CastsSkill) switch
        {
            null => "не изучен",
            { IsReady: true } => "готов",
            var s => $"перезарядка из {s.CooldownFullMs / 1000.0:0.#} с",
        };
        var pet = w.Pet?.InCage(revive.Cage) is { } p ? $"HP {p.HpRatio * 100:0.#} %" : "нет";
        return $"{cast}; скилл {skill}; пет в клетке {revive.Cage}: {pet}{(w.Pet is { IsSummoned: true } ? ", призван" : "")}";
    }

    // Номера ячеек: первые числа до 1000 (PID клиента, если указан, больше)
    private static GameAction? BuildBank(bool put, string[] args, WorldState w, WorldReader reader, out string problem)
    {
        problem = "";
        var n = args.Select(a => int.TryParse(a, out var v) && v is >= 0 and < 1000 ? v : (int?)null).Where(v => v is not null).Select(v => v!.Value).ToList();
        if (n.Count < 2)
        {
            problem = put ? "нужно: act bank-put ЯЧЕЙКА_СУМКИ ЯЧЕЙКА_БАНКА" : "нужно: act bank-take ЯЧЕЙКА_БАНКА ЯЧЕЙКА_СУМКИ";
            return null;
        }

        var bank = reader.ReadBank(out var bankSlots);
        if (bank is null)
        {
            problem = "в профиле нет банка";
            return null;
        }

        var bankSlot = put ? n[1] : n[0];
        if (bankSlot >= bankSlots)
        {
            problem = $"в банке только {bankSlots} ячеек (банк открыт? act bank)";
            return null;
        }

        if (put)
        {
            var item = w.Inventory.FirstOrDefault(i => i.Slot == n[0]);
            problem = $"в ячейке сумки {n[0]} ничего нет";
            if (item is null)
                return null;

            // В занятую ячейку — только на стопку того же предмета (докладывание); чужой предмет — был бы обмен
            var target = bank.FirstOrDefault(i => i.Slot == n[1]);
            problem = $"в ячейке банка {n[1]} другой предмет — в неё нельзя положить (это был бы обмен)";
            if (target is not null && target.Tid != item.Tid)
                return null;

            if (target is null)
            {
                Console.WriteLine($"Положить: сумка {n[0]} (tid {item.Tid} ×{item.Count}) → банк {n[1]}");
                return new BankPutAction(item, n[1]);
            }

            // Докладывание: сколько влезет в стопку банка (предел стопки — у предмета)
            var room = target.MaxCount - target.Count;
            problem = $"стопка в ячейке банка {n[1]} полная (×{target.Count} из {target.MaxCount})";
            if (room <= 0)
                return null;

            // Третье число — сколько доложить (по умолчанию — сколько влезет)
            var count = Math.Min(n.Count > 2 && n[2] > 0 ? n[2] : item.Count, Math.Min(item.Count, room));
            Console.WriteLine($"Доложить: сумка {n[0]} (tid {item.Tid} ×{item.Count}) → {count} шт. на стопку банка {n[1]} "
                              + $"×{target.Count} из {target.MaxCount}");
            return new BankMergeAction(item, n[1], count);
        }

        var stored = bank.FirstOrDefault(i => i.Slot == n[0]);
        problem = $"в ячейке банка {n[0]} ничего нет (банк открыт? act bank)";
        if (stored is null)
            return null;

        Console.WriteLine($"Взять: банк {n[0]} (tid {stored.Tid} ×{stored.Count}) → сумка {n[1]}");
        return new BankTakeAction(stored.Tid, n[0], n[1]);
    }

    /// <summary>За сколько метров до NPC остановиться: рядом, но не вплотную.</summary>
    private const float NpcStop = 2.5f;

    /// <summary>NPC для действия: ближайший, в имени которого есть слово из аргументов, а без слова — тот, что в цели.</summary>
    private static NpcInfo? FindNpc(string[] args, WorldState w, out string problem)
    {
        // Слово — аргумент, который не PID, не ключ и не значение ключа (--server comeback146)
        var word = args.Where((a, i) => !a.StartsWith("--") && !uint.TryParse(a, out _) && (i == 0 || !args[i - 1].StartsWith("--")))
            .FirstOrDefault();
        if (word is null)
        {
            problem = "возьмите NPC в цель или напишите часть его имени";
            return w.Target is { Kind: NpcKind.Npc } target ? target : null;
        }

        problem = $"рядом нет NPC «{word}»";
        return w.Npcs.Where(n => n.Kind == NpcKind.Npc && n.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(n => n.Offset.Horizontal).FirstOrDefault();
    }

    private static GameAction? Build(string name, string[] args, WorldState w, ProfileData data, WorldReader reader, out string problem)
    {
        problem = "";
        var number = args.Select(a => int.TryParse(a, out var n) ? n : (int?)null).FirstOrDefault(n => n is not null);
        var skills = data.Skills;
        var nearestItem = w.GroundItems.Where(i => i.Kind != GroundItemKind.Resource).OrderBy(i => i.Offset.Direct).FirstOrDefault();

        switch (name)
        {
            case "target":
                var mob = w.Mobs.Where(m => !m.IsDead).OrderBy(m => m.Offset.Horizontal).FirstOrDefault();
                problem = "рядом нет живых мобов";
                return mob is null ? null : new SelectTargetAction(mob);

            case "bag-split":
                var sp = args.Select(a => int.TryParse(a, out var v) && v is >= 0 and < 100000 && a.Length < 5 ? v : (int?)null).Where(v => v is not null).Select(v => v!.Value).ToList();
                if (sp.Count > 3)
                    sp = sp.Skip(sp.Count - 3).ToList();
                problem = "нужно: act bag-split ЯЧЕЙКА ПУСТАЯ_ЯЧЕЙКА ЧИСЛО";
                var src = sp.Count < 3 ? null : w.Inventory.FirstOrDefault(i => i.Slot == sp[0]);
                if (src is null)
                    return null;

                return new SplitStackAction(src, sp[1], sp[2]);

            case "bank-put":
            case "bank-take":
                return BuildBank(name == "bank-put", args, w, reader, out problem);

            case "npc-hello":
                var talk = FindNpc(args, w, out problem);
                return talk is null ? null : new NpcHelloAction(talk.Wid);

            case "npc-bank":
                return new NpcOpenBankAction();

            case "quest-return":
                problem = "нужно: act quest-return НОМЕР_ЗАДАНИЯ (номер — из Probe quests)";
                var task = args.Select(a => int.TryParse(a, out var v) && v > 0 && v < 65536 ? v : 0).FirstOrDefault(v => v != 0);
                return task == 0 ? null : new NpcReturnTaskAction(task);

            case "bank":
                return new OpenBankAction();

            case "untarget":
                return new UnselectAction();

            case "attack":
                return new NormalAttackAction();

            case "skill":
                if (w.Host.TargetWid == 0)
                {
                    problem = "сначала выберите цель (act target)";
                    return null;
                }

                return new SkillAction(number ?? skills.DefaultAttack, w.Host.TargetWid, approach: false);

            case "macro":
                if (w.Host.TargetWid == 0)
                {
                    problem = "сначала выберите цель (act target)";
                    return null;
                }

                // Итог не ждём, как и бот: смотреть в игре, пошёл ли макрос
                Console.WriteLine($"Макросы: {(w.Macros.Count == 0 ? "нет" : string.Join(", ", w.Macros.Select(m => $"{m.Number} (иконка {m.Icon})")))}");
                return new MacroAction(number ?? 1);

            case "skill-self":
                return new SkillAction(number ?? skills.RevivePet, 0, approach: false);

            case "skill-approach":
                return new SkillAction(number ?? skills.DefaultAttack, 0, approach: true);

            case "potion-hp":
            case "potion-mp":
                var kind = name == "potion-hp" ? PotionKind.Hp : PotionKind.Mp;
                var potion = new PotionPolicy().Choose(w, kind, out problem);
                return potion is null ? null : new UseItemAction(potion, ItemUse.Potion);

            case "pet-food":
                var food = w.Inventory.Where(i => i.IsPetFood).OrderBy(i => i.FoodLoyalty).FirstOrDefault();
                problem = w.Pet is not { IsSummoned: true } ? "пет не призван" : "нет корма в сумке";
                return food is null || w.Pet is not { IsSummoned: true } ? null : new UseItemAction(food, ItemUse.PetFood);

            case "pet-summon":
                return new SummonPetAction(number ?? 1);

            case "pet-heal":
                problem = "пет не призван";
                return w.Pet is { IsSummoned: true } pet ? new SkillAction(skills.HealPet, pet.ActiveWid, approach: true, "лечение пета") : null;

            case "pet-revive":
                return new RevivePetAction(number ?? 1, skills.RevivePet);

            case "pet-attack":
                problem = "нет цели";
                return w.Host.TargetWid == 0 ? null : new PetAttackAction(w.Host.TargetWid);

            case "pickup":
                problem = "на земле нет предметов";
                return nearestItem is null ? null : new PickupAction(nearestItem);

            case "cancel":
                // Что было до: без этого ✓ ничего не доказывает (нечего прерывать — тоже ✓)
                var g = w.Host.Gather;
                Console.WriteLine("До: " + (g is { Active: true } ? $"копает, {g.ElapsedMs / 1000.0:0.0} из {g.TotalMs / 1000.0:0} с"
                    : w.Host.IsCasting ? "кастует" : "не копает и не кастует — прерывать нечего"));
                return new CancelAction("каст или копание");

            case "gather":
                var resource = w.GroundItems.Where(i => i.Kind == GroundItemKind.Resource).OrderBy(i => i.Offset.Direct).FirstOrDefault();
                Console.WriteLine($"Сумка: {string.Join(", ", w.Inventory.Select(i => $"{i.Tid}×{i.Count}"))}");
                problem = resource is null ? "рядом нет ресурсов" : $"{resource.Name} дальше 50 м";
                return resource is { Offset.Direct: <= 50 } ? new GatherAction(resource) : null;

            case "move":
                var numbers = args.Select(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : (float?)null)
                    .Where(f => f is not null).Select(f => f!.Value).ToList();
                if (numbers.Count < 2 || Math.Abs(numbers[0]) > 50 || Math.Abs(numbers[1]) > 50)
                {
                    problem = "нужно: act move <dx> <dy>, не дальше 50 м";
                    return null;
                }

                var p = w.Host.Position;
                return new MoveAction(new Position(p.X + numbers[0], p.Height, p.Y + numbers[1]), smart: true);

            case "move-to":
                var coords = args.Select(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : (float?)null)
                    .Where(f => f is not null).Select(f => f!.Value).ToList();
                if (coords.Count < 2)
                {
                    problem = "нужно: act move-to <x> <y> — координаты карты игры";
                    return null;
                }

                // Карта игры: X = (x + 4000) / 10, Y = (y + 5500) / 10
                var target = new Position(coords[0] * 10 - 4000, w.Host.Position.Height, coords[1] * 10 - 5500);
                Console.WriteLine($"Сейчас: карта {(w.Host.Position.X + 4000) / 10:0.0} {(w.Host.Position.Y + 5500) / 10:0.0}");
                problem = $"точка в {w.Host.Position.HorizontalDistanceTo(target):0} м — дальше 1000 м";
                return w.Host.Position.HorizontalDistanceTo(target) <= 1000 ? new MoveAction(target, smart: true) : null;

            case "fly":
                Console.WriteLine($"Сейчас: {(w.Host.Flying switch { true => "в воздухе", false => "на земле", _ => "неизвестно где" })}, высота {w.Host.Position.Height:0.0}");
                return new FlyAction(up: w.Host.Flying != true);

            case "fly-to":
                var shift = args.Select(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : (float?)null)
                    .Where(f => f is not null).Select(f => f!.Value).Take(3).ToList(); // дальше — PID
                if (shift.Count < 3 || shift.Any(f => Math.Abs(f) > 100))
                {
                    problem = "нужно: act fly-to <dx> <dy> <dh>, каждое не больше 100 м";
                    return null;
                }

                var from = w.Host.Position;
                Console.WriteLine($"Сейчас высота {from.Height:0.0}, лечу на {from.Height + shift[2]:0.0}");
                return new MoveAction(new Position(from.X + shift[0], from.Height + shift[2], from.Y + shift[1]), fly: true);

            case "npc-go":
                var go = FindNpc(args, w, out problem);
                if (go is not null)
                    Console.WriteLine($"Подойти к {go.Name}: {go.Offset.Horizontal:0.0} м");
                return go is null ? null : new TalkToAction(go.Wid);

            case "move-npc":
                var npc = FindNpc(args, w, out problem);
                if (npc is null)
                    return null;

                // По прямой (автопуть до NPC в городе петляет — под мост и т. п.) в точку рядом с NPC: на линии от перса к нему,
                // не доходя NpcStop метров, — не упираться в него. Высота — NPC (земля у него)
                var hostAt = w.Host.Position;
                var dx = npc.Position.X - hostAt.X;
                var dy = npc.Position.Y - hostAt.Y;
                var length = (float)Math.Sqrt(dx * dx + dy * dy);
                var keep = length <= NpcStop ? 0f : (length - NpcStop) / length;
                var stopAt = new Position(hostAt.X + dx * keep, npc.Position.Height, hostAt.Y + dy * keep);
                Console.WriteLine($"Идти к {npc.Name}: до него {length:0.0} м, точка {stopAt} — в {npc.Position.DistanceTo(stopAt):0.0} м от него");
                return new MoveAction(stopAt, tolerance: 1f);

            case "move-mob":
                var near = w.Mobs.Where(m => !m.IsDead).OrderBy(m => m.Offset.Horizontal).FirstOrDefault();
                problem = near is null ? "рядом нет живых мобов" : $"{near.Name} дальше 50 м";
                return near is { Offset.Horizontal: <= 50 } ? new MoveAction(near.Position, smart: true) : null;

            default:
                problem = $"неизвестное действие «{name}»\n{Usage}";
                return null;
        }
    }

    private sealed class ConsoleSink : ILogSink
    {
        public void Write(LogEntry entry)
        {
            if (entry.Level == LogLevel.Debug)
                Console.WriteLine($"  {entry.Message}");
        }
    }
}
