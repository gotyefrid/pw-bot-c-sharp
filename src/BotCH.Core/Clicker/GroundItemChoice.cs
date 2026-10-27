using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Clicker;

/// <summary>
/// Какой предмет на земле подходит — общее у условия «Рядом лежит предмет» и действия «Поднять предмет»: названия или группы,
/// как у лута (пусто — любой предмет или монеты), не дальше радиуса от персонажа. Ресурсы не в счёт: их копают, а не подбирают.
/// </summary>
public static class GroundItemChoice
{
    public const int MaxRadius = 200;

    /// <summary>Ближайший подходящий; нет — null.</summary>
    public static GroundItem? Nearest(WorldState world, IReadOnlyCollection<string> items, int radius)
        => world.GroundItems
            .Where(i => i.Kind != GroundItemKind.Resource && i.Offset.Direct <= radius)
            .Where(i => items.Count == 0 || MobNameFilter.Contains(items, i.Name) || MobNameFilter.Contains(items, i.Group))
            .OrderBy(i => i.Offset.Direct)
            .FirstOrDefault();

    /// <summary>Радиус — в пределы: от 1 м до <see cref="MaxRadius"/>.</summary>
    public static int CleanRadius(int radius) => Math.Min(MaxRadius, Math.Max(1, radius));
}
