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
/// Всё, на чём бот принимает решения, прочитанное одним проходом. Неизменяемый: читается целиком, потом передаётся мозгу.
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
    /// полна — только если такой же предмет уже лежит неполной стопкой.
    /// </summary>
    public bool FitsInBag(GroundItem item)
        => item.Kind == GroundItemKind.Money || !BagFull
           || Inventory.Any(i => i.Tid == item.Tid && i.MaxCount > 0 && i.Count < i.MaxCount);
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
    /// <summary>Полоска копания; null — поля не найдены для этого сервера.</summary>
    public GatherProgress? Gather { get; init; }

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
    float Distance,
    string Name,
    /// <summary>Игра знает HP только у выбранной цели; у остальных 0.</summary>
    int Hp)
{
    /// <summary>Уровень; 0 — неизвестен (поле не найдено для сервера).</summary>
    public int Level { get; init; }

    public const int StateAttacking = 2;
    public const int StateCasting = 3;
    public const int StateDead = 4;

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

public sealed record GroundItem(uint Address, uint Id, uint Tid, GroundItemKind Kind, Position Position, float Distance, string Name);

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
    public int HpPercent => (int)Math.Round(HpRatio * 100);

    // По доле, а не по процентам: 0.3 % HP округлится до 0, но пет жив
    public bool IsAlive => HpRatio > 0;

    /// <summary>Как в старом боте: сытость больше 0 — пора кормить.</summary>
    public bool IsHungry => Hunger > 0;
}
