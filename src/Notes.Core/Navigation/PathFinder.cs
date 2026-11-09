using System;
using System.Collections.Generic;

namespace Notes.Core.Navigation;

/// <summary>Точка пути по земле (x, y мира).</summary>
public readonly record struct PathPoint(float X, float Y)
{
    public float DistanceTo(PathPoint other) => (float)Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));

    public override string ToString() => $"({X:0.0}; {Y:0.0})";
}

/// <summary>Путь: точки по порядку (первая — откуда идти, последняя — цель) или причина, почему пути нет.</summary>
public sealed record PathResult(IReadOnlyList<PathPoint> Points, string? Problem, int Explored)
{
    public bool Found => Problem is null;

    /// <summary>Длина по отрезкам, м.</summary>
    public float Length
    {
        get
        {
            var sum = 0f;
            for (var i = 1; i < Points.Count; i++)
                sum += Points[i - 1].DistanceTo(Points[i]);
            return sum;
        }
    }
}

/// <summary>
/// Путь в обход препятствий: прямой отрезок, если он свободен; иначе поиск A* по сетке <see cref="Step"/> м (8 направлений,
/// углы препятствий не срезаются) и сглаживание — из пути оставляются самые дальние точки, до которых видно по прямой.
/// Персонаж прижат к препятствию (его клетка занята) — сначала ближайшая свободная клетка рядом; цель внутри препятствия —
/// ближайшая к ней свободная.
/// </summary>
public sealed class PathFinder(IWalkArea area)
{
    public const float Step = 0.5f;

    /// <summary>Поиск ограничен прямоугольником вокруг начала и цели с таким запасом, м.</summary>
    public const float Margin = 40f;

    /// <summary>Дальше — не ищем: столько клеток не обойти за разумное время.</summary>
    public const float MaxDistance = 400f;

    // Насколько далеко от занятой точки искать свободную (персонаж в углу, цель в камне), м
    private const float Nudge = 3f;

    // Шаг проверки прямой, м
    private const float LineStep = 0.25f;

    public PathResult Find(PathPoint from, PathPoint to)
    {
        if (from.DistanceTo(to) > MaxDistance)
            return new PathResult([], $"цель в {from.DistanceTo(to):0} м — дальше {MaxDistance:0} м", 0);

        var start = area.IsFree(from.X, from.Y) ? from : NearestFree(from);
        if (start is null)
            return new PathResult([], $"вокруг перса в {Nudge:0} м всё занято", 0);
        var goal = area.IsFree(to.X, to.Y) ? to : NearestFree(to);
        if (goal is null)
            return new PathResult([], $"цель внутри препятствия: свободного места в {Nudge:0} м от неё нет", 0);

        var head = start.Value == from ? new List<PathPoint> { from } : [from, start.Value];
        if (LineFree(start.Value, goal.Value))
            return new PathResult([.. head, goal.Value], null, 0);

        var grid = AStar(start.Value, goal.Value, out var explored);
        if (grid is null)
            return new PathResult([], "обхода нет: цель отрезана препятствиями", explored);

        var smooth = Smooth(grid);
        smooth[0] = start.Value;
        smooth[smooth.Count - 1] = goal.Value;
        head.RemoveAt(head.Count - 1);
        head.AddRange(smooth);
        return new PathResult(head, null, explored);
    }

    /// <summary>Свободна ли прямая между точками для круга персонажа.</summary>
    public bool LineFree(PathPoint a, PathPoint b)
    {
        var length = a.DistanceTo(b);
        var steps = Math.Max(1, (int)Math.Ceiling(length / LineStep));
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            if (!area.IsFree(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t))
                return false;
        }

        return true;
    }

    private PathPoint? NearestFree(PathPoint p)
    {
        for (var r = Step; r <= Nudge; r += Step)
        {
            PathPoint? best = null;
            var count = (int)Math.Ceiling(2 * Math.PI * r / Step);
            for (var i = 0; i < count; i++)
            {
                var a = 2 * Math.PI * i / count;
                var c = new PathPoint(p.X + (float)Math.Cos(a) * r, p.Y + (float)Math.Sin(a) * r);
                if (area.IsFree(c.X, c.Y))
                {
                    best = c;
                    break;
                }
            }

            if (best is not null)
                return best;
        }

        return null;
    }

    private static readonly (int Dx, int Dy, float Cost)[] Moves =
    [
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, 1.4142f), (1, -1, 1.4142f), (-1, 1, 1.4142f), (-1, -1, 1.4142f),
    ];

    private List<PathPoint>? AStar(PathPoint start, PathPoint goal, out int explored)
    {
        var minX = Math.Min(start.X, goal.X) - Margin;
        var minY = Math.Min(start.Y, goal.Y) - Margin;
        var width = (int)Math.Ceiling((Math.Abs(start.X - goal.X) + 2 * Margin) / Step) + 1;
        var height = (int)Math.Ceiling((Math.Abs(start.Y - goal.Y) + 2 * Margin) / Step) + 1;

        // Клетка (i, j) — точка minX + i·Step, minY + j·Step; свободна ли — считаем по мере надобности
        var free = new sbyte[width * height];
        var cost = new float[width * height];
        var from = new int[width * height];
        for (var k = 0; k < cost.Length; k++)
            cost[k] = float.MaxValue;

        int Cell(PathPoint p) => Index((int)Math.Round((p.X - minX) / Step), (int)Math.Round((p.Y - minY) / Step));
        int Index(int i, int j) => j * width + i;
        PathPoint Point(int k) => new(minX + k % width * Step, minY + k / width * Step);
        bool Free(int i, int j)
        {
            if (i < 0 || j < 0 || i >= width || j >= height)
                return false;
            var k = Index(i, j);
            if (free[k] == 0)
                free[k] = (sbyte)(area.IsFree(minX + i * Step, minY + j * Step) ? 1 : -1);
            return free[k] > 0;
        }

        var startCell = Cell(start);
        var goalCell = Cell(goal);
        var gi = goalCell % width;
        var gj = goalCell / width;
        float Guess(int k)
        {
            var dx = Math.Abs(k % width - gi);
            var dy = Math.Abs(k / width - gj);
            return Math.Max(dx, dy) + 0.4142f * Math.Min(dx, dy);
        }

        var open = new SortedSet<(float F, int K)>();
        cost[startCell] = 0;
        from[startCell] = -1;
        open.Add((Guess(startCell), startCell));
        free[startCell] = 1;
        free[goalCell] = 1;
        explored = 0;

        while (open.Count > 0)
        {
            var (_, k) = open.Min;
            open.Remove(open.Min);
            explored++;
            if (k == goalCell)
            {
                var path = new List<PathPoint>();
                for (var c = k; c != -1; c = from[c])
                    path.Add(Point(c));
                path.Reverse();
                return path;
            }

            var i = k % width;
            var j = k / width;
            foreach (var (dx, dy, step) in Moves)
            {
                if (!Free(i + dx, j + dy))
                    continue;
                // По диагонали — только если свободны обе соседние клетки: не срезать угол препятствия
                if (dx != 0 && dy != 0 && (!Free(i + dx, j) || !Free(i, j + dy)))
                    continue;

                var n = Index(i + dx, j + dy);
                var next = cost[k] + step;
                if (next >= cost[n])
                    continue;
                if (cost[n] != float.MaxValue)
                    open.Remove((cost[n] + Guess(n), n));
                cost[n] = next;
                from[n] = k;
                open.Add((next + Guess(n), n));
            }
        }

        return null;
    }

    // Из точек сетки — самые дальние, до которых видно по прямой
    private List<PathPoint> Smooth(List<PathPoint> path)
    {
        var result = new List<PathPoint> { path[0] };
        var i = 0;
        while (i < path.Count - 1)
        {
            var j = path.Count - 1;
            while (j > i + 1 && !LineFree(path[i], path[j]))
                j--;
            result.Add(path[j]);
            i = j;
        }

        return result;
    }
}
