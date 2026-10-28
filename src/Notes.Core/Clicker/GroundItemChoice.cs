using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Clicker;

/// <summary>
/// Что на земле подходит — общее у условий «Рядом лежит предмет», «Рядом можно копать» и действий кликера: названия
/// (у предметов — и группы, как у лута), пусто — любое. Предметы и ресурсы порознь: предмет подбирают, ресурс копают.
/// </summary>
public static class GroundItemChoice
{
    public const int MaxRadius = 200;

    /// <summary>Ближайший предмет (или монеты) не дальше <paramref name="radius"/> от персонажа (null — на любом расстоянии); нет — null.</summary>
    public static GroundItem? NearestItem(WorldState world, IReadOnlyCollection<string> items, int? radius = null)
        => Nearest(world.GroundItems.Where(i => i.Kind != GroundItemKind.Resource), items, radius);

    /// <summary>
    /// Ближайшее, что копается: ресурс или «нересурс» (квестовый, ящик), — без проверки инструмента, уровня, квеста: что выбрано,
    /// то и подходит. Не дальше <paramref name="radius"/> (null — на любом расстоянии); нет — null.
    /// </summary>
    public static GroundItem? NearestResource(WorldState world, IReadOnlyCollection<string> names, int? radius = null)
        => Nearest(world.GroundItems.Where(i => i.Kind == GroundItemKind.Resource), names, radius);

    private static GroundItem? Nearest(IEnumerable<GroundItem> things, IReadOnlyCollection<string> names, int? radius)
        => things
            .Where(i => radius is null || i.Offset.Direct <= radius)
            .Where(i => names.Count == 0 || MobNameFilter.Contains(names, i.Name) || MobNameFilter.Contains(names, i.Group))
            .OrderBy(i => i.Offset.Direct)
            .FirstOrDefault();

    /// <summary>Радиус — в пределы: от 1 м до <see cref="MaxRadius"/>.</summary>
    public static int CleanRadius(int radius) => Math.Min(MaxRadius, Math.Max(1, radius));
}
