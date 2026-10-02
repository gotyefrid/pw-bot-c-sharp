using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using BotCH.Core.Actions;
using BotCH.Core.Calls;
using BotCH.Core.Logging;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.World;

namespace BotCH.Probe;

/// <summary>
/// Полуавто-тесты действий в игре. ВЫЗЫВАЮТ ФУНКЦИИ ИГРЫ (поток в процессе клиента) — запускает владелец.
/// Каждый тест: снимок → одно действие → снимки раз в 200 мс → ✅/❌ и время до подтверждения.
/// </summary>
internal static class ActCommands
{
    public const string Usage = """
          act target            выбрать ближайшего живого моба
          act untarget          снять цель
          act attack            обычная атака текущей цели
          act skill [id]        скилл пакетом по текущей цели (по умолчанию атакующий)
          act skill-approach [id]  скилл «как кнопкой»: подойти и скастовать
          act potion-hp / potion-mp   выпить самую слабую подходящую банку
          act pet-food          покормить пета (если сыт — отказ, это нормально)
          act pet-summon [клетка]  призвать пета (по умолчанию 1)
          act pet-heal          вылечить призванного пета
          act pet-revive [клетка]  воскресить мёртвого пета
          act pet-attack        приказать пету атаковать цель
          act pickup            подобрать ближайший предмет в 10 м пакетом
          act pickup-approach   подобрать ближайший предмет «как мышкой»
          act move <dx> <dy>    отойти на dx, dy метров от текущего места
          act move-mob          дойти до ближайшего живого моба (его точка — точно на земле)
        """;

    public static int Act(IServerProfile profile, string[] args)
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
        var reader = new WorldReader(game, game.MainModuleBase, data);
        var caller = new GameCaller(game, game, game.MainModuleBase, data);
        var log = new Logger { MinLevel = LogLevel.Debug }.AddSink(new ConsoleSink()).For("act");
        var runner = new ActionRunner(new DirectCallActions(caller), log);

        var world = reader.Read();
        var action = Build(name, rest, world, data, out var problem);
        if (action is null)
        {
            Console.WriteLine($"❌ {problem}");
            return 2;
        }

        Console.WriteLine($"Действие: {action.Name}");
        var submit = runner.Submit(action, world);
        if (!submit.Sent)
        {
            Console.WriteLine($"❌ не отправлено: {submit.Outcome?.Details}");
            return 3;
        }

        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < action.Timeout + TimeSpan.FromSeconds(1))
        {
            Thread.Sleep(200);
            var outcome = runner.Update(reader.Read()).FirstOrDefault();
            if (outcome is null)
                continue;

            var ok = outcome.Status == ActionStatus.Confirmed;
            Console.WriteLine($"{(ok ? "✅" : "❌")} {outcome}");
            return ok ? 0 : 4;
        }

        Console.WriteLine("❌ нет ответа");
        return 4;
    }

    private static GameAction? Build(string name, string[] args, WorldState w, ProfileData data, out string problem)
    {
        problem = "";
        var number = args.Select(a => int.TryParse(a, out var n) ? n : (int?)null).FirstOrDefault(n => n is not null);
        var skills = data.Skills;
        var nearestItem = w.GroundItems.Where(i => i.Kind != GroundItemKind.Resource).OrderBy(i => i.Distance).FirstOrDefault();

        switch (name)
        {
            case "target":
                var mob = w.Mobs.Where(m => !m.IsDead).OrderBy(m => m.Distance).FirstOrDefault();
                problem = "рядом нет живых мобов";
                return mob is null ? null : new SelectTargetAction(mob);

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
                problem = "рядом (до 10 м) нет предметов";
                return nearestItem is { Distance: <= 10 } ? new PickupAction(nearestItem, approach: false) : null;

            case "pickup-approach":
                problem = "на земле нет предметов";
                return nearestItem is null ? null : new PickupAction(nearestItem, approach: true);

            case "move":
                var numbers = args.Select(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : (float?)null)
                    .Where(f => f is not null).Select(f => f!.Value).ToList();
                if (numbers.Count < 2 || Math.Abs(numbers[0]) > 50 || Math.Abs(numbers[1]) > 50)
                {
                    problem = "нужно: act move <dx> <dy>, не дальше 50 м";
                    return null;
                }

                var p = w.Host.Position;
                return new MoveAction(new Position(p.X + numbers[0], p.Height, p.Y + numbers[1]));

            case "move-mob":
                var near = w.Mobs.Where(m => !m.IsDead).OrderBy(m => m.Distance).FirstOrDefault();
                problem = near is null ? "рядом нет живых мобов" : $"{near.Name} дальше 50 м";
                return near is { Distance: <= 50 } ? new MoveAction(near.Position) : null;

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
