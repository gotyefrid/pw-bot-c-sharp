using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Что и где копает <see cref="GatherBehavior"/>. Фарм мобов — по своему списку ресурсов в радиусе фарма (<see cref="FarmAreaScope"/>);
/// обход — по списку обхода у текущей точки (это говорит сам <see cref="RouteBehavior"/>).
/// </summary>
public interface IGatherScope
{
    /// <summary>Копать ли вообще.</summary>
    bool Enabled(BrainContext c);

    /// <summary>Ресурс в нужном месте.</summary>
    bool InArea(BrainContext c, Position p);

    /// <summary>Обычный ресурс с таким названием копать.</summary>
    bool Wanted(BrainContext c, string name);

    /// <summary>«Нересурс» (квестовый, особый) назван явно — копать без условий.</summary>
    bool Listed(BrainContext c, string name);

    /// <summary>Для лога: где копаем (« у точки 2/4»); "" — ничего не добавлять.</summary>
    string Where(BrainContext c);

    /// <summary>Опасный моб, в зоне которого ресурс (к такому не летим); null — такого нет или опасных не проверяем.</summary>
    NpcInfo? Guard(BrainContext c, Position p);
}

/// <summary>Фарм мобов: ресурсы из списка лута в радиусе фарма; опасных мобов не проверяем.</summary>
public sealed class FarmAreaScope : IGatherScope
{
    public static readonly FarmAreaScope Instance = new();

    private FarmAreaScope()
    {
    }

    public bool Enabled(BrainContext c) => c.Settings.Loot.PickResources;
    public bool InArea(BrainContext c, Position p) => c.InFarmArea(p);
    public bool Wanted(BrainContext c, string name) => LootFilter.AllowsGather(c.Settings.Loot, name);
    public bool Listed(BrainContext c, string name) => LootFilter.ListsForGather(c.Settings.Loot, name);
    public string Where(BrainContext c) => "";
    public NpcInfo? Guard(BrainContext c, Position p) => null;
}
