using System.Collections.Generic;
using System.Linq;

namespace BotCH.Core.World;

/// <summary>Название и сколько таких рядом — для выбора в списки мобов и лута.</summary>
public sealed record NameCount(string Name, int Count, float Nearest)
{
    public override string ToString() => Count > 1 ? $"{Name} ×{Count}" : Name;
}

public static class NearbyNames
{
    /// <summary>Живые мобы вокруг по названиям: сначала самые частые, при равенстве — ближайшие.</summary>
    public static IReadOnlyList<NameCount> Mobs(WorldState world)
        => Group(world.Mobs.Where(m => !m.IsDead && m.Name.Length > 0).Select(m => (m.Name, m.Distance)));

    /// <summary>Предметы и монеты на земле по названиям (ресурсы не подбираются — их нет).</summary>
    public static IReadOnlyList<NameCount> GroundItems(WorldState world)
        => Group(world.GroundItems.Where(i => i.Kind != GroundItemKind.Resource && i.Name.Length > 0).Select(i => (i.Name, i.Distance)));

    private static IReadOnlyList<NameCount> Group(IEnumerable<(string Name, float Distance)> things)
        => things
            .GroupBy(t => t.Name.Trim())
            .Select(g => new NameCount(g.Key, g.Count(), g.Min(t => t.Distance)))
            .OrderByDescending(n => n.Count)
            .ThenBy(n => n.Nearest)
            .ToList();
}
