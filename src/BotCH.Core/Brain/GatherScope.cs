using System;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Что и где копает <see cref="GatherBehavior"/>. Фарм мобов — по списку лута в радиусе фарма (<see cref="FarmArea"/>);
/// обход — по списку обхода у текущей точки (<see cref="RouteBehavior.Scope"/>).
/// </summary>
/// <param name="enabled">Копать ли вообще.</param>
/// <param name="inArea">Ресурс в нужном месте.</param>
/// <param name="wanted">Обычный ресурс с таким названием копать.</param>
/// <param name="listed">«Нересурс» (квестовый, особый) назван явно — копать без условий.</param>
/// <param name="where">Для лога: где копаем («у точки 2/4 …»); null — ничего не добавлять.</param>
/// <param name="guard">Опасный моб, в зоне которого ресурс (к такому не летим); null — опасных не проверяем.</param>
public sealed class GatherScope(
    Func<BrainContext, bool> enabled,
    Func<BrainContext, Position, bool> inArea,
    Func<BrainContext, string, bool> wanted,
    Func<BrainContext, string, bool> listed,
    Func<BrainContext, string>? where = null,
    Func<BrainContext, Position, NpcInfo?>? guard = null)
{
    public static readonly GatherScope FarmArea = new(
        c => c.Settings.Loot is { Enabled: true, PickResources: true },
        (c, p) => c.InFarmArea(p),
        (c, name) => LootFilter.AllowsGather(c.Settings.Loot, name),
        (c, name) => LootFilter.ListsForGather(c.Settings.Loot, name));

    public bool Enabled(BrainContext c) => enabled(c);
    public bool InArea(BrainContext c, Position p) => inArea(c, p);
    public bool Wanted(BrainContext c, string name) => wanted(c, name);
    public bool Listed(BrainContext c, string name) => listed(c, name);
    public string Where(BrainContext c) => where?.Invoke(c) ?? "";
    public NpcInfo? Guard(BrainContext c, Position p) => guard?.Invoke(c, p);
}
