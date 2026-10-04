using System;
using System.Collections.Generic;
using System.Linq;

namespace BotCH.Core.World;

/// <summary>Координаты в мире: X, высота, Y (как лежат в памяти).</summary>
public readonly record struct Position(float X, float Height, float Y)
{
    public float DistanceTo(Position other)
    {
        var dx = X - other.X;
        var dh = Height - other.Height;
        var dy = Y - other.Y;
        return (float)Math.Sqrt(dx * dx + dh * dh + dy * dy);
    }

    /// <summary>Расстояние по земле (без высоты): мобы на склоне не «выпадают» из радиуса.</summary>
    public float HorizontalDistanceTo(Position other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    public bool IsFinite => !float.IsNaN(X) && !float.IsInfinity(X) && !float.IsNaN(Height) && !float.IsInfinity(Height) && !float.IsNaN(Y) && !float.IsInfinity(Y);

    public override string ToString() => $"({X:0.0}; {Y:0.0}; h {Height:0.0})";
}

/// <summary>
/// Где объект относительно персонажа. Считается из позиций (<see cref="Between"/>), а не берётся из памяти: число в памяти
/// игры у моба — по горизонтали, у предмета на земле — в 3D, а расчёт одинаков для всех и совпадает с памятью до сотых.
/// </summary>
/// <param name="Horizontal">По земле, м.</param>
/// <param name="Vertical">Высота объекта минус высота персонажа, м: плюс — объект выше, минус — ниже.</param>
public readonly record struct Offset(float Horizontal, float Vertical)
{
    /// <summary>С какой разницы по высоте её стоит показывать: «12 м (выше 30 м)».</summary>
    private const float NotableHeight = 5f;

    /// <summary>Кратчайшее, с высотой: √(по земле² + по высоте²).</summary>
    public float Direct => (float)Math.Sqrt(Horizontal * Horizontal + Vertical * Vertical);

    /// <summary>Где <paramref name="target"/> относительно <paramref name="origin"/> (персонажа).</summary>
    public static Offset Between(Position origin, Position target) => new(origin.HorizontalDistanceTo(target), target.Height - origin.Height);

    /// <summary>«12,0 м» — по земле; заметная разница по высоте — «12,0 м (выше 30 м)».</summary>
    public override string ToString()
        => Math.Abs(Vertical) < NotableHeight
            ? $"{Horizontal:0.0} м"
            : $"{Horizontal:0.0} м ({(Vertical > 0 ? "выше" : "ниже")} {Math.Abs(Vertical):0} м)";
}

/// <summary>
/// Всё, на чём бот принимает решения, прочитанное одним проходом. Неизменяемый: читается целиком, потом передаётся мозгу.
/// Расстояния до мобов и предметов (<see cref="NpcInfo.Offset"/>, <see cref="GroundItem.Offset"/>) — от персонажа в этом
/// снимке: привязывает их только сборка снимка (этот конструктор), поэтому они не расходятся с позициями.
/// </summary>
public sealed record WorldState(
    DateTime Time,
    TimeSpan ReadDuration,
    HostState Host,
    IReadOnlyList<NpcInfo> Npcs,
    IReadOnlyList<GroundItem> GroundItems,
    IReadOnlyList<InventoryItem> Inventory,
    IReadOnlyList<SkillInfo> Skills,
    PetState? Pet)
{
    /// <summary>Мобы, NPC и петы — с расстояниями от персонажа в этом снимке.</summary>
    public IReadOnlyList<NpcInfo> Npcs { get; init; } = Npcs.Select(n => n with { Origin = Host.Position }).ToList();

    /// <summary>Предметы и ресурсы на земле — с расстояниями от персонажа в этом снимке.</summary>
    public IReadOnlyList<GroundItem> GroundItems { get; init; } = GroundItems.Select(i => i with { Origin = Host.Position }).ToList();

    /// <summary>Сколько объектов в списках по данным самой игры (для самопроверки: прочитали всё ли).</summary>
    public int NpcCountInGame { get; init; } = -1;
    public int GroundItemCountInGame { get; init; } = -1;

    public NpcInfo? Target => Host.TargetWid == 0 ? null : Npcs.FirstOrDefault(n => n.Wid == Host.TargetWid);

    public IEnumerable<NpcInfo> Mobs => Npcs.Where(n => n.Kind == NpcKind.Mob);

    public SkillInfo? Skill(int id) => Skills.FirstOrDefault(s => s.Id == id);

    /// <summary>Сколько ячеек в сумке (0 — неизвестно).</summary>
    public int InventorySlots { get; init; }

    /// <summary>Все ячейки сумки заняты.</summary>
    public bool BagFull => InventorySlots > 0 && Inventory.Count >= InventorySlots;

    /// <summary>
    /// Влезет ли предмет с земли: монеты — всегда (идут в кошелёк); сумка не полна — да;
    /// полна — только если такой же предмет уже лежит неполной стопкой. Ресурс при полной сумке — если всё, что он может дать
    /// (по справочнику игры), ляжет в начатые стопки целиком, даже самая большая копка.
    /// </summary>
    public bool FitsInBag(GroundItem item)
    {
        if (item.Kind == GroundItemKind.Money || !BagFull)
            return true;
        if (item.Kind != GroundItemKind.Resource)
            return RoomInStacks(item.Tid) > 0;
        return item.Mine is { Yields.Count: > 0 } mine && mine.Yields.All(y => RoomInStacks(y.Key) >= y.Value);
    }

    private int RoomInStacks(uint tid)
        => Inventory.Where(i => i.Tid == tid && i.MaxCount > 0).Sum(i => Math.Max(0, i.MaxCount - i.Count));
}

public sealed record HostState(
    uint Address,
    uint Wid,
    string Name,
    int Level,
    int Hp,
    int MaxHp,
    int Mp,
    /// <summary>null — смещение ещё не найдено для этого сервера.</summary>
    int? MaxMp,
    Position Position,
    uint TargetWid,
    bool IsCasting,
    /// <summary>Сколько мс осталось до конца перезарядки корма пета (0 — можно кормить или поле не найдено).</summary>
    int PetFoodCooldownMs)
{
    /// <summary>Какой скилл кастуется: 0 — никакой; null — поле не найдено для этого сервера (знаем только «кастует»).</summary>
    public int? CastingSkillId { get; init; }

    /// <summary>Полоска копания; null — поля не найдены для этого сервера.</summary>
    public GatherProgress? Gather { get; init; }

    /// <summary>Персонаж в воздухе (на полётнике); null — поле не найдено для этого сервера.</summary>
    public bool? Flying { get; init; }

    /// <summary>Персонаж в воде; null — поле не найдено для этого сервера.</summary>
    public bool? InWater { get; init; }

    public int HpPercent => MaxHp > 0 ? Hp * 100 / MaxHp : 0;
    public bool IsDead => Hp <= 0;
}

/// <summary>Полоска копания ресурса. После конца или срыва <see cref="ElapsedMs"/> и <see cref="TotalMs"/> остаются последними.</summary>
public sealed record GatherProgress(bool Active, int ElapsedMs, int TotalMs)
{
    /// <summary>Полоска дошла до конца (с запасом на кадр).</summary>
    public bool Finished => TotalMs > 0 && ElapsedMs >= TotalMs - 300;
}

public enum NpcKind
{
    Other = 0,
    Mob = 6,
    Npc = 7,
    Pet = 9,
}

public sealed record NpcInfo(
    uint Address,
    uint Wid,
    NpcKind Kind,
    /// <summary>1 стоит, 2 бьёт, 3 кастует, 4 мёртв, 5 идёт.</summary>
    int State,
    /// <summary>Кого бьёт (WID перса или пета); 0 — никого.</summary>
    uint TargetWid,
    Position Position,
    string Name,
    /// <summary>Игра знает HP только у выбранной цели; у остальных 0.</summary>
    int Hp)
{
    /// <summary>Откуда считается <see cref="Offset"/> — где персонаж в этом снимке. Ставит только сборка снимка.</summary>
    internal Position Origin { get; init; }

    /// <summary>Где моб относительно персонажа. Из позиции: <c>with { Position = … }</c> его тоже пересчитает.</summary>
    public Offset Offset => Offset.Between(Origin, Position);

    /// <summary>Уровень; 0 — неизвестен (поле не найдено для сервера).</summary>
    public int Level { get; init; }

    /// <summary>Нападает сам (агрессивный); null — неизвестно (запись моба не найдена для сервера).</summary>
    public bool? Aggressive { get; init; }

    /// <summary>С какого расстояния агрессивный нападает, м; 0 — неизвестно.</summary>
    public int AggroRadius { get; init; }

    /// <summary>
    /// Бросил цель и возвращается на место — неуязвим и не нападает (false и там, где поле не найдено). Цель (<see cref="TargetWid"/>)
    /// при этом может ещё показывать нас.
    /// </summary>
    public bool Returning { get; init; }

    /// <summary>Идёт и стал ближе к персонажу, чем на прошлом снимке.</summary>
    public bool Approaching { get; init; }

    /// <summary>
    /// Действует сейчас: бьёт, кастует или идёт к персонажу. Моб, который просто стоит с нашей целью, — не напал: так бывает,
    /// когда цель застряла (1.3.6 не сбрасывает её после отагра) или он нас не достаёт.
    /// </summary>
    public bool Engaging => State is StateAttacking or StateCasting || Approaching;

    public const int StateAttacking = 2;
    public const int StateCasting = 3;
    public const int StateDead = 4;
    public const int StateMoving = 5;

    public bool IsDead => State == StateDead;
}

public enum GroundItemKind
{
    Unknown = 0,
    Item = 1,
    /// <summary>Ресурс — копается, не подбирается.</summary>
    Resource = 2,
    Money = 3,
}

public sealed record GroundItem(uint Address, uint Id, uint Tid, GroundItemKind Kind, Position Position, string Name)
{
    /// <summary>Откуда считается <see cref="Offset"/> — где персонаж в этом снимке. Ставит только сборка снимка.</summary>
    internal Position Origin { get; init; }

    /// <summary>Где предмет относительно персонажа. Из позиции: <c>with { Position = … }</c> его тоже пересчитает.</summary>
    public Offset Offset => Offset.Between(Origin, Position);

    /// <summary>Для ресурса — его запись в справочнике игры; null — не ресурс или запись не найдена для сервера.</summary>
    public MineInfo? Mine { get; init; }

    /// <summary>
    /// «Нересурс»: копается, но не киркой или только по квесту (трупы, ящики, печати). Бот копает его, только если название
    /// явно в списке, — и тогда без условий. В точки ресурсов не попадает, в окне виден только в выборе «рядом».
    /// </summary>
    public bool Special { get; init; }
}

/// <summary>Ресурс по справочнику игры: чем копать (0 — ничем), нужен ли квест (0 — нет), что даёт: tid → самое большее за копку.</summary>
public sealed record MineInfo(uint Tool, uint Quest, IReadOnlyDictionary<uint, int> Yields)
{
    /// <summary>С какого уровня персонажа копается (0 — любой или неизвестно); раньше игра отвечает «недостаточно высокий уровень».</summary>
    public int LevelRequired { get; init; }
}

public sealed record InventoryItem(int Slot, uint Tid, int Category, int Count, PotionInfo? Potion, int? FoodLoyalty)
{
    /// <summary>Максимум в стопке (0 — неизвестно).</summary>
    public int MaxCount { get; init; }

    public bool IsPetFood => FoodLoyalty is not null;
}

public sealed record PotionInfo(int RequiredLevel, int Hp, int HpSeconds, int Mp, int MpSeconds);

public sealed record SkillInfo(int Id, int Level, int CooldownLeftMs, int CooldownFullMs, uint Flags, string? Name)
{
    public bool IsReady => CooldownLeftMs == 0;
}

/// <summary>
/// Петы персонажа. null в <see cref="WorldState.Pet"/> — петов нет вовсе (не друид / клетки пустые): это нормально.
/// </summary>
public sealed record PetState(int? ActiveCage, uint ActiveWid, IReadOnlyList<PetInCage> Cages)
{
    public bool IsSummoned => ActiveCage is not null && ActiveWid != 0;

    public PetInCage? InCage(int cage) => Cages.FirstOrDefault(p => p.Cage == cage);
}

/// <summary>Пет в клетке (1..10).</summary>
public sealed record PetInCage(int Cage, float HpRatio, int Hunger)
{
    /// <summary>Название из справочника игры; null — не прочитано (поля не найдены для этого сервера).</summary>
    public string? Name { get; init; }

    /// <summary>Где может жить (призываться); null — не знаем для этого сервера.</summary>
    public PetHabitat? Habitat { get; init; }

    public int HpPercent => (int)Math.Round(HpRatio * 100);

    // По доле, а не по процентам: 0.3 % HP округлится до 0, но пет жив
    public bool IsAlive => HpRatio > 0;

    /// <summary>Как в старом боте: сытость больше 0 — пора кормить.</summary>
    public bool IsHungry => Hunger > 0;

    public bool Lives(PetHabitat where) => Habitat is { } h && (h & where) != 0;
}

/// <summary>Где питомец может быть призван: на земле, в воде, в воздухе (бывает несколько сразу).</summary>
[Flags]
public enum PetHabitat
{
    Ground = 1,
    Water = 2,
    Air = 4,
}

public static class PetHabitats
{
    /// <summary>Поле справочника (inhabit_type клиента): 0 земля, 1 вода, 2 воздух, 3 земля+вода, 4 земля+воздух, 5 вода+воздух, 6 везде.</summary>
    public static PetHabitat? FromGame(int inhabit) => inhabit switch
    {
        0 => PetHabitat.Ground,
        1 => PetHabitat.Water,
        2 => PetHabitat.Air,
        3 => PetHabitat.Ground | PetHabitat.Water,
        4 => PetHabitat.Ground | PetHabitat.Air,
        5 => PetHabitat.Water | PetHabitat.Air,
        6 => PetHabitat.Ground | PetHabitat.Water | PetHabitat.Air,
        _ => null,
    };

    public static string Text(PetHabitat? habitat)
        => habitat is not { } h ? "?"
            : string.Join(", ", new[] { (PetHabitat.Ground, "земля"), (PetHabitat.Water, "вода"), (PetHabitat.Air, "воздух") }
                .Where(x => (h & x.Item1) != 0).Select(x => x.Item2));
}
