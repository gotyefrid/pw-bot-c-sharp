using System;
using System.Linq;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

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
}
