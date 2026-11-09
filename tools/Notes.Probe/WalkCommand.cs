using System;
using System.Diagnostics;
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
        + "            --no-movemap — без карты проходимости клиента, только камни и постройки";

    // Дошёл до точки отрезка, м
    private const float Reached = 0.8f;

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
        var numbers = words.Select(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : (float?)null)
            .Where(f => f is not null).Select(f => f!.Value).ToList();
        if (numbers.Count < 2 || numbers[0] > 2000 || numbers[1] > 2000)
        {
            Console.WriteLine(Usage);
            return 1;
        }

        using var game = Program.OpenClient(args, go ? GameProcessRights.Execute : GameProcessRights.Read);
        var data = profile.Data;
        var roots = GameRoots.Find(game, game.MainModuleBase, data);
        var reader = new WorldReader(game, roots, data, _ => null);
        var world = reader.Read();
        var me = world.Host.Position;

        // Карта игры: X = (x + 4000) / 10, Y = (y + 5500) / 10
        var goal = new PathPoint(numbers[0] * 10 - 4000, numbers[1] * 10 - 5500);
        var from = new PathPoint(me.X, me.Y);
        Console.WriteLine($"Перс: карта {(me.X + 4000) / 10:0.0} {(me.Y + 5500) / 10:0.0}, мир {from}; цель: мир {goal}, {from.DistanceTo(goal):0.0} м");

        if (MoveMapCommand.Load(profile, game, GuidePoints.MainWorld, me, [from, goal], PathFinder.Margin, !noMoveMap) is not { } scene)
            return 2;

        var finder = new PathFinder(scene.Area);
        var timer = Stopwatch.StartNew();
        var path = finder.Find(from, goal);
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

        for (var replan = 0; ; replan++)
        {
            var result = Follow(reader, calls, path);
            if (result is null)
            {
                var end = reader.Read().Host.Position;
                Console.WriteLine($"✅ дошёл: {new PathPoint(end.X, end.Y)}, до цели {new PathPoint(end.X, end.Y).DistanceTo(goal):0.0} м");
                return 0;
            }

            Console.WriteLine($"⚠️ {result}");
            if (replan >= MaxReplans)
            {
                Console.WriteLine($"❌ не дошёл: застревал {MaxReplans + 1} раза");
                return 5;
            }

            var now = reader.Read().Host.Position;
            path = finder.Find(new PathPoint(now.X, now.Y), goal);
            Console.Write("Пересчёт: ");
            Print(path, TimeSpan.Zero);
            if (!path.Found)
                return 3;
        }
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
                          + (took > TimeSpan.Zero ? $", {took.TotalMilliseconds:0} мс" : ""));
        foreach (var p in path.Points)
            Console.WriteLine($"   {p}  карта {(p.X + 4000) / 10:0.0} {(p.Y + 5500) / 10:0.0}");
    }

    // Пройти точки пути по порядку; null — дошёл, иначе — где и почему застрял
    private static string? Follow(WorldReader reader, GameCalls calls, PathResult path)
    {
        for (var i = 1; i < path.Points.Count; i++)
        {
            var target = path.Points[i];
            var start = reader.Read().Host.Position;
            var sent = calls.Caller.MoveTo(new Position(target.X, start.Height, target.Y), smart: false);
            if (!sent.Ok)
                return $"команда «бежать» не прошла: {sent.Details}";
            Console.WriteLine($"→ точка {i}/{path.Points.Count - 1} {target}");

            var last = new PathPoint(start.X, start.Y);
            var lastMoved = DateTime.UtcNow;
            while (true)
            {
                Thread.Sleep(150);
                var p = reader.Read().Host.Position;
                var here = new PathPoint(p.X, p.Y);
                if (here.DistanceTo(target) <= Reached)
                    break;
                if (here.DistanceTo(last) >= StuckMove)
                {
                    last = here;
                    lastMoved = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - lastMoved > StuckTime)
                {
                    return $"застрял в {here} по пути к точке {i} {target} (до неё {here.DistanceTo(target):0.0} м)";
                }
            }
        }

        return null;
    }
}
