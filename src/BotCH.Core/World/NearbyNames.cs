using System.Collections.Generic;
using System.Linq;

namespace BotCH.Core.World;

/// <summary>Название (и уровни мобов этого вида) — для выбора в списки мобов и лута.</summary>
public sealed record NameCount(string Name, int Count, float Nearest, int MinLevel = 0, int MaxLevel = 0)
{
    /// <summary>«Сидящий волк (10)», «Волк (10–12)» — в скобках уровень; предметы — просто название.</summary>
    public override string ToString()
        => MaxLevel <= 0 ? Name : MinLevel == MaxLevel ? $"{Name} ({MaxLevel})" : $"{Name} ({MinLevel}–{MaxLevel})";
}

public static class NearbyNames
{
    /// <summary>Живые мобы вокруг по названиям: сначала самые частые, при равенстве — ближайшие.</summary>
    public static IReadOnlyList<NameCount> Mobs(WorldState world)
        => world.Mobs
            .Where(m => !m.IsDead && m.Name.Length > 0)
            .GroupBy(m => m.Name.Trim())
            .Select(g => new NameCount(g.Key, g.Count(), g.Min(m => m.Offset.Horizontal),
                g.Where(m => m.Level > 0).Select(m => m.Level).DefaultIfEmpty().Min(),
                g.Select(m => m.Level).Max()))
            .OrderByDescending(n => n.Count)
            .ThenBy(n => n.Nearest)
            .ToList();

    /// <summary>Предметы на земле по названиям — для списка лута (монеты — своей галкой, ресурсы — в <see cref="Resources"/>).</summary>
    public static IReadOnlyList<NameCount> GroundItems(WorldState world)
        => Group(world.GroundItems.Where(IsLootItem).Select(i => (i.Name, i.Offset.Direct)));

    /// <summary>Предмет, который выбирают в список лута: не ресурс и не монеты, с названием.</summary>
    public static bool IsLootItem(GroundItem item)
        => item.Kind is not (GroundItemKind.Resource or GroundItemKind.Money) && item.Name.Length > 0;

    /// <summary>Ресурсы на земле по названиям (и «нересурсы»: их копают, если назвать явно) — для списков обхода и фарма.</summary>
    public static IReadOnlyList<NameCount> Resources(WorldState world)
        => Group(world.GroundItems.Where(i => i.Kind == GroundItemKind.Resource && i.Name.Length > 0).Select(i => (i.Name, i.Offset.Direct)));

    private static IReadOnlyList<NameCount> Group(IEnumerable<(string Name, float Distance)> things)
        => things
            .GroupBy(t => t.Name.Trim())
            .Select(g => new NameCount(g.Key, g.Count(), g.Min(t => t.Distance)))
            .OrderByDescending(n => n.Count)
            .ThenBy(n => n.Nearest)
            .ToList();
}
