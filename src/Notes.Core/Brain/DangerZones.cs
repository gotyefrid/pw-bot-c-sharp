using System;
using System.Linq;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Опасные мобы обхода (<see cref="RouteSettings"/>): агрессивный моб от порога уровня или из списка (боссы). Пассивные не
/// опасны — сами не нападают. Зона — цилиндр вокруг моба: радиус — агро + запас, по высоте — столько же вверх и вниз. Как игра
/// считает агро по высоте (от координат моба или от тела), не знаем — цилиндр накрывает оба случая.
/// </summary>
public static class DangerZones
{
    public static bool IsDangerous(NpcInfo mob, RouteSettings route)
        => mob is { Kind: NpcKind.Mob, IsDead: false, Aggressive: true, AggroRadius: > 0 }
           && ((route.DangerLevel > 0 && mob.Level >= route.DangerLevel) || MobNameFilter.Contains(route.DangerMobs, mob.Name));

    /// <summary>
    /// От этого моба, если напал, уходим и с ним не дерёмся: опасный — или любой живой моб, когда включено «Не драться»
    /// (<see cref="RouteSettings.AvoidFights"/>). Зоны у ресурсов это не меняет — там только опасные.
    /// </summary>
    public static bool Flee(NpcInfo mob, RouteSettings route)
        => route.AvoidFights ? mob is { Kind: NpcKind.Mob, IsDead: false } : IsDangerous(mob, route);

    /// <summary>Точка внутри зоны моба (с запасом <paramref name="margin"/>, м).</summary>
    public static bool Inside(NpcInfo mob, Position point, int margin)
    {
        var reach = mob.AggroRadius + margin;
        return mob.Position.HorizontalDistanceTo(point) < reach && Math.Abs(point.Height - mob.Position.Height) <= reach;
    }

    /// <summary>Ближайший опасный моб, в зоне которого точка; null — точка безопасна.</summary>
    public static NpcInfo? Guard(WorldState world, Position point, RouteSettings route)
        => world.Mobs
            .Where(m => IsDangerous(m, route) && Inside(m, point, route.DangerMargin))
            .OrderBy(m => m.Position.HorizontalDistanceTo(point))
            .FirstOrDefault();

    // Шаг проверки пути, м: зона — не меньше запаса 1 м, проскочить её между шагами нельзя
    private const float PathStep = 1f;

    /// <summary>Первый опасный моб, через зону которого проходит отрезок от <paramref name="from"/> до <paramref name="to"/>; null — путь чист.</summary>
    public static NpcInfo? GuardOnPath(WorldState world, Position from, Position to, RouteSettings route)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to) / PathStep));
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            var point = new Position(from.X + (to.X - from.X) * t, from.Height + (to.Height - from.Height) * t, from.Y + (to.Y - from.Y) * t);
            if (Guard(world, point, route) is { } guard)
                return guard;
        }

        return null;
    }
}
