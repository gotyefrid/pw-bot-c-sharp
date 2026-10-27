using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Clicker;

/// <summary>
/// Кого взять в цель — общее у действия «Взять цель» и условия «Рядом можно взять цель»: по названиям, пусто — кто угодно из
/// тех, кого можно взять (<see cref="WorldState.Targetable"/>). Расстояние — по прямой, как до предметов (<see cref="GroundItemChoice"/>),
/// радиус — в тех же пределах.
/// </summary>
public static class TargetChoice
{
    /// <summary>Ближайший не дальше <paramref name="radius"/> от персонажа (null — на любом расстоянии); нет — null.</summary>
    public static NpcInfo? Nearest(WorldState world, IReadOnlyCollection<string> names, int? radius = null)
        => world.Targetable
            .Where(n => radius is null || n.Offset.Direct <= radius)
            .Where(n => names.Count == 0 || MobNameFilter.Contains(names, n.Name))
            .OrderBy(n => n.Offset.Direct)
            .FirstOrDefault();
}
