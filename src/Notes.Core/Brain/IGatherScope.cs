using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain;

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

    /// <summary>Подлетать к ресурсу сверху: сначала на своей высоте точно над ним, потом копать (игра опустит вертикально).</summary>
    bool FromAbove(BrainContext c);

    /// <summary>
    /// На какой высоте лететь над ресурсом при подлёте сверху: ниже неё (выкопали предыдущий) — сначала вертикально вверх на неё.
    /// null — на своей высоте.
    /// </summary>
    float? FlightHeight(BrainContext c);

    /// <summary>Опасный моб, через зону которого проходит путь от <paramref name="from"/> до <paramref name="to"/>; null — путь чист.</summary>
    NpcInfo? GuardOnPath(BrainContext c, Position from, Position to);
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
    public bool FromAbove(BrainContext c) => false;
    public float? FlightHeight(BrainContext c) => null;
    public NpcInfo? GuardOnPath(BrainContext c, Position from, Position to) => null;
}
