using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using Notes.Core.GameFiles;
using Notes.Core.Memory;
using Notes.Core.Navigation;
using Notes.Core.Profiles;
using Notes.Core.Session;
using Notes.Core.World;

namespace Notes.Probe;

/// <summary>
/// Путь в обход камней, построек и заборов (файлы клиента) до точки карты игры: считает и рисует; с --go ведёт перса по
/// отрезкам командами «бежать в точку по прямой» и пересчитывает путь, если он застрял.
/// </summary>
internal static class WalkCommand
{
    public const string Usage =
        "  walk X Y [файл.png] [--go] [--no-movemap]  путь в обход препятствий до точки карты игры (как на карте: 493 869): считает и рисует;\n"
        + "            --go — вести перса по отрезкам («по прямой»), застрял — пересчитать (вызывает функции клиента — запускает владелец);\n"
        + "            --no-movemap — без карты проходимости клиента, только камни и постройки;\n"
        + "  walk target [дальность м] [--skill N] [--go]  то же к текущей цели перса: встать не дальше дальности (15 м), откуда\n"
        + "            до цели ничего не загораживает; --skill — затем кастовать скилл N по цели";

    // Дошёл до точки пути, м. Раньше (ближе Early) — к следующей, только если до неё уже видно с запасом: иначе перс
    // срезает поворот и цепляет угол (так было 09.11 на 1.3.6 у угла дома)
    private const float Reached = 0.3f;
    private const float Early = 0.8f;

    // Застрял: за это время сдвинулся меньше чем на StuckMove
    private static readonly TimeSpan StuckTime = TimeSpan.FromSeconds(1.5);
    private const float StuckMove = 0.3f;
    private const int MaxReplans = 3;

    public static int Run(ServerProfile profile, string[] args)
    {
        var go = args.Contains("--go");
        var noMoveMap = args.Contains("--no-movemap");
        var words = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || args[i - 1] != "--server")).ToList();
        var file = words.FirstOrDefault(a => a.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ?? "walk.png";
        var toTarget = words.FirstOrDefault() == "target";
        var skillAt = Array.IndexOf(args, "--skill");
        var skill = skillAt >= 0 && skillAt + 1 < args.Length && int.TryParse(args[skillAt + 1], out var sk) ? sk : (int?)null;
        var numbers = words.Skip(toTarget ? 1 : 0)
            .Where(a => skill is null || a != skill.Value.ToString(CultureInfo.InvariantCulture))
            .Select(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : (float?)null)
            .Where(f => f is not null).Select(f => f!.Value).ToList();
        if (!toTarget && (numbers.Count < 2 || numbers[0] > 2000 || numbers[1] > 2000))
        {
            Console.WriteLine(Usage);
            return 1;
        }

        // К цели: дальность — первое число до 100 (больше — PID клиента)
        var range = toTarget ? numbers.FirstOrDefault(n => n is > 0 and <= 100) : 0;
        if (toTarget && range <= 0)
            range = DefaultRange;

        using var game = Program.OpenClient(args, go ? GameProcessRights.Execute : GameProcessRights.Read);
        var data = profile.Data;
        var roots = GameRoots.Find(game, game.MainModuleBase, data);
        var reader = new WorldReader(game, roots, data, _ => null);
        var world = reader.Read();
        var me = world.Host.Position;

        NpcInfo? target = null;
        if (toTarget)
        {
            target = world.Npcs.FirstOrDefault(n => n.Wid == world.Host.TargetWid);
            if (target is null)
            {
                Console.WriteLine("❌ у перса нет цели (или она не в списке мобов)");
                return 2;
            }

            Console.WriteLine($"Цель: {target.Name}, {target.Offset}; подойти не дальше {range:0} м");
        }

        // Карта игры: X = (x + 4000) / 10, Y = (y + 5500) / 10
        var goal = target is not null
            ? new PathPoint(target.Position.X, target.Position.Y)
            : new PathPoint(numbers[0] * 10 - 4000, numbers[1] * 10 - 5500);
        var from = new PathPoint(me.X, me.Y);
        Console.WriteLine($"Перс: карта {(me.X + 4000) / 10:0.0} {(me.Y + 5500) / 10:0.0}, мир {from}; цель: мир {goal}, {from.DistanceTo(goal):0.0} м");

        if (MoveMapCommand.Load(profile, game, GuidePoints.MainWorld, me, [from, goal], PathFinder.Margin, !noMoveMap) is not { } scene)
            return 2;

        var timer = Stopwatch.StartNew();
        var path = PathFinder.FindWithMargin(scene.Comfort, scene.Area, from, goal);
        Print(path, timer.Elapsed);
        var center = new PathPoint((from.X + goal.X) / 2, (from.Y + goal.Y) / 2);
        var radius = (int)Math.Ceiling(Math.Max(Math.Abs(from.X - goal.X), Math.Abs(from.Y - goal.Y)) / 2 + 15);
        MoveMapCommand.Draw(scene, world, center, radius, path.Points, file);
        if (!path.Found || !go)
            return path.Found ? 0 : 3;

        using var calls = GameCalls.Open(game, roots, data, CallTransport.Window, out var callsProblem);
        if (calls is null)
        {
            Console.WriteLine($"❌ вызовы не подключились: {callsProblem}");
            return 4;
        }

        // След перса и все пути (первый и пересчёты) — на картинку «после»
        var trail = new List<PathPoint> { from };
        // К цели — хватит, когда до неё не дальше дальности и прямую ничего не загораживает (по тем же препятствиям)
        var sight = new PathFinder(scene.Area);
        Func<PathPoint, bool> close = here => target is not null && here.DistanceTo(goal) <= range && sight.LineFree(here, goal);
        var planned = new List<PathPoint>(path.Points);
        var after = Path.ChangeExtension(file, null) + "-после.png";
        for (var replan = 0; ; replan++)
        {
            var result = Follow(reader, calls, path, trail, new PathFinder(scene.Comfort), close);
            if (result is null)
            {
                var end = reader.Read().Host.Position;
                Console.WriteLine($"✅ дошёл: {new PathPoint(end.X, end.Y)}, до цели {new PathPoint(end.X, end.Y).DistanceTo(goal):0.0} м");
                if (target is not null)
                    StopAndCast(reader, calls, target, skill);
                MoveMapCommand.Draw(scene, reader.Read(), center, radius, planned, after, trail);
                return 0;
            }

            Console.WriteLine($"⚠️ {result}");
            if (replan >= MaxReplans)
            {
                Console.WriteLine($"❌ не дошёл: застревал {MaxReplans + 1} раза");
                MoveMapCommand.Draw(scene, reader.Read(), center, radius, planned, after, trail);
                return 5;
            }

            var now = reader.Read().Host.Position;
            path = PathFinder.FindWithMargin(scene.Comfort, scene.Area, new PathPoint(now.X, now.Y), goal);
            Console.Write("Пересчёт: ");
            Print(path, TimeSpan.Zero);
            if (!path.Found)
                return 3;
            planned.AddRange(path.Points);
        }
    }

    // Подойти к цели — до этой дальности, м (дальность друида 18 м, бот в заданиях бьёт с 15)
    private const float DefaultRange = 15f;

    // Встать (команда «бежать» на своё место) и, если задан, кастовать скилл по цели; каст пошёл — перс кастует
    private static void StopAndCast(WorldReader reader, GameCalls calls, NpcInfo target, int? skill)
    {
        var here = reader.Read().Host.Position;
        calls.Caller.MoveTo(here, smart: false);
        if (skill is null)
            return;

        Thread.Sleep(200);
        var sent = calls.Caller.CastSkill(skill.Value, target.Wid);
        Console.WriteLine(sent.Ok ? $"Скилл {skill} по цели {target.Name} отправлен" : $"❌ скилл не отправлен: {sent.Details}");
        for (var i = 0; i < 10 && sent.Ok; i++)
        {
            Thread.Sleep(150);
            var w = reader.Read();
            if (w.Host.IsCasting)
            {
                Console.WriteLine($"✅ каст пошёл, до цели {w.Npcs.FirstOrDefault(n => n.Wid == target.Wid)?.Offset.ToString() ?? "?"}");
                return;
            }
        }

        if (sent.Ok)
            Console.WriteLine("⚠️ за 1,5 с перс так и не начал кастовать — смотрите в игре (далеко? не видно?)");
    }

    private static void Print(PathResult path, TimeSpan took)
    {
        if (!path.Found)
        {
            Console.WriteLine($"❌ пути нет: {path.Problem} (клеток просмотрено: {path.Explored})");
            return;
        }

        Console.WriteLine($"Путь: {path.Points.Count - 1} отрезков, {path.Length:0.0} м"
                          + (path.Explored > 0 ? $", обход — клеток просмотрено {path.Explored}" : ", по прямой")
                          + (path.Tight ? $", впритирку {WalkArea.Radius} м: с запасом {WalkArea.ComfortRadius} м не пролезть" : $", запас от стен {WalkArea.ComfortRadius} м")
                          + (took > TimeSpan.Zero ? $", {took.TotalMilliseconds:0} мс" : ""));
        foreach (var p in path.Points)
            Console.WriteLine($"   {p}  карта {(p.X + 4000) / 10:0.0} {(p.Y + 5500) / 10:0.0}");
    }

    // Пройти точки пути по порядку; null — дошёл, иначе — где и почему застрял
    private static string? Follow(WorldReader reader, GameCalls calls, PathResult path, List<PathPoint> trail, PathFinder comfort,
        Func<PathPoint, bool> close)
    {
        for (var i = 1; i < path.Points.Count; i++)
        {
            var target = path.Points[i];
            PathPoint? next = i + 1 < path.Points.Count ? path.Points[i + 1] : null;
            var start = reader.Read().Host.Position;
            var sent = calls.Caller.MoveTo(new Position(target.X, start.Height, target.Y), smart: false);
            if (!sent.Ok)
                return $"команда «бежать» не прошла: {sent.Details}";
            Console.WriteLine($"→ точка {i}/{path.Points.Count - 1} {target}");

            var last = new PathPoint(start.X, start.Y);
            var lastMoved = DateTime.UtcNow;
            var resent = false;
            while (true)
            {
                Thread.Sleep(150);
                var p = reader.Read().Host.Position;
                var here = new PathPoint(p.X, p.Y);
                trail.Add(here);
                if (close(here))
                    return null;
                var left = here.DistanceTo(target);
                if (left <= Reached || left <= Early && (next is null || comfort.LineFree(here, next.Value)))
                    break;
                if (here.DistanceTo(last) >= StuckMove)
                {
                    last = here;
                    lastMoved = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - lastMoved > StuckTime)
                {
                    // Стоит: игра могла пропустить команду, пока доделывала прошлую ходьбу (на 1.3.6 так бывает сразу после
                    // точки пути, перс при этом ещё чуть доезжает) — повторить один раз; стоит и после — застрял
                    if (!resent)
                    {
                        resent = true;
                        Console.WriteLine($"   стоит — команда ещё раз");
                        var again = calls.Caller.MoveTo(new Position(target.X, p.Height, target.Y), smart: false);
                        if (!again.Ok)
                            return $"команда «бежать» не прошла: {again.Details}";
                        lastMoved = DateTime.UtcNow;
                        continue;
                    }

                    return $"застрял в {here} по пути к точке {i} {target} (до неё {here.DistanceTo(target):0.0} м)";
                }
            }
        }

        return null;
    }
}
