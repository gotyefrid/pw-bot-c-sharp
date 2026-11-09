using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.GameFiles;

namespace Notes.Core.Navigation;

/// <summary>Где персонаж может стоять: точка (x, y) мира свободна для его круга.</summary>
public interface IWalkArea
{
    bool IsFree(float x, float y);
}

/// <summary>
/// Свободно ли место для персонажа по файлам клиента: карта проходимости (<see cref="MoveMap"/>; где её нет — считаем
/// свободным) и тела столкновений моделей сцены (<see cref="SceneObstacles"/>). Персонаж — круг <see cref="Radius"/> на
/// высоте от ног до роста; точка свободна, если свободны центр и восемь точек по краю круга.
/// <para>
/// Рельеф не читаем, поэтому «мешает ли тело» считаем в полосе от <see cref="Below"/> ниже ног до <see cref="Above"/> выше:
/// подножия камней утоплены в землю, а земля под ними бывает ниже ног персонажа.
/// </para>
/// </summary>
public sealed class WalkArea : IWalkArea
{
    public const float Radius = 0.5f;
    public const float Below = 2f;
    public const float Above = 2.5f;

    // Тела по корзинам 8×8 м: проверка точки смотрит только свою корзину
    private const float BucketSize = 8f;

    private static readonly (float X, float Y)[] Rim = Enumerable.Range(0, 8)
        .Select(i => ((float)Math.Cos(i * Math.PI / 4) * Radius, (float)Math.Sin(i * Math.PI / 4) * Radius))
        .ToArray();

    private readonly MoveMap? _map;
    private readonly float _from;
    private readonly float _to;
    private readonly Dictionary<(int, int), List<ConvexBrush>> _buckets = [];

    public WalkArea(MoveMap? map, SceneObstacles obstacles, float feet)
    {
        _map = map;
        _from = feet - Below;
        _to = feet + Above;
        foreach (var brush in obstacles.Brushes.Where(b => b.MaxHeight > _from && b.MinHeight < _to))
        {
            for (var bx = Bucket(brush.MinX - Radius); bx <= Bucket(brush.MaxX + Radius); bx++)
                for (var by = Bucket(brush.MinY - Radius); by <= Bucket(brush.MaxY + Radius); by++)
                {
                    if (!_buckets.TryGetValue((bx, by), out var list))
                        _buckets[(bx, by)] = list = [];
                    list.Add(brush);
                }
        }
    }

    public bool IsFree(float x, float y)
    {
        if (!PointFree(x, y))
            return false;
        foreach (var (dx, dy) in Rim)
            if (!PointFree(x + dx, y + dy))
                return false;
        return true;
    }

    private bool PointFree(float x, float y)
    {
        if (_map?.Walkable(x, y) == false)
            return false;
        if (!_buckets.TryGetValue((Bucket(x), Bucket(y)), out var brushes))
            return true;
        foreach (var brush in brushes)
            if (brush.VerticalSpan(x, y) is { } span && span.High > _from && span.Low < _to)
                return false;
        return true;
    }

    private static int Bucket(float v) => (int)Math.Floor(v / BucketSize);
}
