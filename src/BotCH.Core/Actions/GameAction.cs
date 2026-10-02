using System;
using System.Linq;
using BotCH.Core.Calls;
using BotCH.Core.World;

namespace BotCH.Core.Actions;

public enum VerdictKind
{
    Pending,
    Confirmed,
    Rejected,
    /// <summary>Действие больше не нужно (цель умерла раньше) — не ошибка.</summary>
    Cancelled,
}

public readonly record struct Verdict(VerdictKind Kind, string Details = "")
{
    public static readonly Verdict Pending = new(VerdictKind.Pending);
    public static Verdict Confirmed(string details = "") => new(VerdictKind.Confirmed, details);
    public static Verdict Rejected(string details) => new(VerdictKind.Rejected, details);
    public static Verdict Cancelled(string details) => new(VerdictKind.Cancelled, details);
}

/// <summary>Что действие занимает у персонажа, пока ждёт подтверждения.</summary>
public enum ActionResource
{
    /// <summary>Ничего: банка, корм, приказ пету, выбор цели — идут параллельно с чем угодно.</summary>
    None,
    /// <summary>
    /// Тело: бег, скилл, атака, подбор с подходом — у клиента это «работы» персонажа, новая заменяет прежнюю. Два таких разом
    /// перебивают друг друга (так 2026-10-02 упал клиент 1.4.6), поэтому одновременно — только одно.
    /// </summary>
    Body,
}

/// <summary>
/// Действие бота: как отправить и как по снимкам понять, что оно сработало.
/// Пока не подтверждено (или не вышел срок), такое же действие (<see cref="Key"/>) повторно не отправляется.
/// </summary>
public abstract class GameAction
{
    /// <summary>Для лога: «скилл 299 → Сидящий волк».</summary>
    public abstract string Name { get; }

    /// <summary>Одинаковый ключ — «то же самое действие». По умолчанию — <see cref="Name"/>.</summary>
    public virtual string Key => Name;

    /// <summary>Что занимает, пока ждёт подтверждения: тело — одновременно только одно такое действие.</summary>
    public virtual ActionResource Resource => ActionResource.None;

    /// <summary>Сколько ждать подтверждения.</summary>
    public abstract TimeSpan Timeout { get; }

    /// <summary>Чем считать истечение срока: обычно «не дождались», у банок и корма — «отказ» (стопка не изменилась).</summary>
    public virtual ActionStatus TimeoutStatus => ActionStatus.Timeout;

    /// <summary>Можно ли отправлять при таком снимке; null — можно, иначе причина.</summary>
    public virtual string? Precondition(WorldState now) => null;

    public abstract CallResult Send(IGameActions actions, WorldState now);

    /// <summary>Сравнивает снимок в момент отправки и текущий.</summary>
    public abstract Verdict Check(WorldState start, WorldState now);

    public override string ToString() => Name;
}

public sealed class SelectTargetAction(NpcInfo npc) : GameAction
{
    public NpcInfo Npc { get; } = npc;
    public override string Name => $"выбрать цель {Npc.Name} 0x{Npc.Wid:X8}";
    public override string Key => "цель";
    public override TimeSpan Timeout => TimeSpan.FromSeconds(2);
    public override CallResult Send(IGameActions actions, WorldState now) => actions.SelectTarget(Npc.Wid);

    public override Verdict Check(WorldState start, WorldState now)
        => now.Host.TargetWid == Npc.Wid ? Verdict.Confirmed() : Verdict.Pending;
}

public sealed class UnselectAction : GameAction
{
    public override string Name => "снять цель";
    public override string Key => "цель";
    public override TimeSpan Timeout => TimeSpan.FromSeconds(2);
    public override CallResult Send(IGameActions actions, WorldState now) => actions.Unselect();
    public override Verdict Check(WorldState start, WorldState now) => now.Host.TargetWid == 0 ? Verdict.Confirmed() : Verdict.Pending;
}

/// <summary>Обычная атака текущей цели. Подтверждение: моб переключился на перса, у него убыло HP или он умер.</summary>
public sealed class NormalAttackAction : GameAction
{
    public override string Name => "обычная атака";
    public override TimeSpan Timeout => TimeSpan.FromSeconds(6);
    public override ActionResource Resource => ActionResource.Body;

    public override string? Precondition(WorldState now) => now.Target is null ? "нет цели" : null;

    public override CallResult Send(IGameActions actions, WorldState now) => actions.NormalAttack();

    public override Verdict Check(WorldState start, WorldState now)
    {
        var before = start.Target;
        if (before is null || now.Host.TargetWid != before.Wid)
            return Verdict.Rejected("цель сменилась");

        var mob = now.Npcs.FirstOrDefault(n => n.Wid == before.Wid);
        if (mob is null || mob.IsDead)
            return Verdict.Confirmed("цель умерла");
        if (mob.TargetWid == now.Host.Wid)
            return Verdict.Confirmed("моб бьёт перса");
        return mob.Hp < before.Hp ? Verdict.Confirmed($"HP цели {before.Hp} → {mob.Hp}") : Verdict.Pending;
    }
}

/// <summary>
/// Скилл. Подтверждение — началась перезарядка (у долгих скиллов — только после каста).
/// Скилл по текущей цели: цель сменилась — отказ.
/// </summary>
public sealed class SkillAction : GameAction
{
    private readonly bool _approach;

    /// <param name="skill">ID скилла.</param>
    /// <param name="targetWid">Цель; 0 — текущая цель перса (approach) или без цели (пакет).</param>
    /// <param name="approach">Как кнопкой: клиент сам подходит на дальность. Иначе — пакетом.</param>
    public SkillAction(int skill, uint targetWid, bool approach, string? title = null)
    {
        Skill = skill;
        TargetWid = targetWid;
        _approach = approach;
        Title = title ?? $"скилл {skill}";
    }

    public int Skill { get; }
    public uint TargetWid { get; }
    public string Title { get; }

    public override string Name => TargetWid == 0 ? Title : $"{Title} → 0x{TargetWid:X8}";
    public override string Key => $"скилл {Skill}";
    public override ActionResource Resource => ActionResource.Body;
    // Как кнопкой: не дождались за 5 с — бот просто нажмёт ещё раз (клиент продолжит подход), долго ждать незачем
    public override TimeSpan Timeout => TimeSpan.FromSeconds(_approach ? 5 : 8);

    public override string? Precondition(WorldState now)
        => now.Skill(Skill) switch
        {
            null => $"скилл {Skill} не изучен",
            { IsReady: false } s => $"{Title}: перезарядка {s.CooldownLeftMs / 1000.0:0.0} с",
            _ => null,
        };

    public override CallResult Send(IGameActions actions, WorldState now)
        => _approach ? actions.ApplySkill(now.Host, Skill, TargetWid) : actions.CastSkill(Skill, TargetWid);

    public override Verdict Check(WorldState start, WorldState now)
    {
        if (now.Skill(Skill) is { IsReady: false })
            return Verdict.Confirmed("началась перезарядка");
        // По текущей цели: игрок/бот сменил цель — скилл ушёл бы не туда. Цель умерла (каст после смерти, бот уже снял её) —
        // просто не понадобился
        if (_approach && TargetWid == 0 && now.Host.TargetWid != start.Host.TargetWid)
            return now.Mobs.Any(m => m.Wid == start.Host.TargetWid && !m.IsDead)
                ? Verdict.Rejected("цель сменилась")
                : Verdict.Cancelled("цель умерла, скилл не понадобился");
        return Verdict.Pending;
    }
}

public enum ItemUse
{
    Potion,
    PetFood,
}

/// <summary>Банка или корм. Подтверждение — стопка в ячейке уменьшилась. Не уменьшилась за срок — отказ игры.</summary>
public sealed class UseItemAction(InventoryItem item, ItemUse use) : GameAction
{
    public InventoryItem Item { get; } = item;
    public ItemUse Use { get; } = use;

    public override string Name => $"{(Use == ItemUse.Potion ? "банка" : "корм")} tid {Item.Tid} (ячейка {Item.Slot}, ×{Item.Count})";
    public override string Key => $"предмет {Item.Slot}";
    public override TimeSpan Timeout => TimeSpan.FromSeconds(3);
    public override ActionStatus TimeoutStatus => ActionStatus.Rejected;

    public override string? Precondition(WorldState now)
        => now.Inventory.Any(i => i.Slot == Item.Slot && i.Tid == Item.Tid) ? null : "предмета уже нет в сумке";

    public override CallResult Send(IGameActions actions, WorldState now) => actions.UseItem(Item);

    public override Verdict Check(WorldState start, WorldState now)
    {
        var before = start.Inventory.FirstOrDefault(i => i.Slot == Item.Slot && i.Tid == Item.Tid)?.Count ?? Item.Count;
        var after = now.Inventory.FirstOrDefault(i => i.Slot == Item.Slot && i.Tid == Item.Tid)?.Count ?? 0;
        return after < before ? Verdict.Confirmed($"стопка {before} → {after}") : Verdict.Pending;
    }
}

/// <summary>Подобрать. Подтверждение — предмет пропал с земли.</summary>
public sealed class PickupAction(GroundItem item, bool approach) : GameAction
{
    public GroundItem Item { get; } = item;

    public override string Name => $"подобрать {Item.Name} ({Item.Distance:0.0} м{(approach ? ", с подходом" : "")})";
    public override string Key => $"подбор 0x{Item.Id:X8}";
    // Пакетом персонаж не двигается; «как мышкой» — бежит к предмету
    public override ActionResource Resource => approach ? ActionResource.Body : ActionResource.None;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(approach ? 10 : 3);

    public override string? Precondition(WorldState now)
        => Item.Kind == GroundItemKind.Resource ? "ресурс не подбирается, а собирается"
            : !approach && Item.Distance > 10 ? "дальше 10 м — сервер не поднимет без подхода"
            : null;

    public override CallResult Send(IGameActions actions, WorldState now)
        => approach ? actions.PickupObject(now.Host, Item) : actions.Pickup(Item);

    // Стоим рядом, а предмет не исчезает — игра его не отдаёт (чужой лут, не дотянуться); 10 с ждать незачем
    private const float NearDistance = 3f;
    private static readonly TimeSpan NearPatience = TimeSpan.FromSeconds(3);
    private DateTime? _nearSince;

    public override Verdict Check(WorldState start, WorldState now)
    {
        if (!now.GroundItems.Any(i => i.Id == Item.Id))
            return Verdict.Confirmed("предмет исчез с земли");

        if (now.Host.Position.HorizontalDistanceTo(Item.Position) > NearDistance)
        {
            _nearSince = null;
            return Verdict.Pending;
        }

        _nearSince ??= now.Time;
        return now.Time - _nearSince.Value >= NearPatience
            ? Verdict.Rejected($"стоим рядом {NearPatience.TotalSeconds:0} с, а игра не отдаёт")
            : Verdict.Pending;
    }
}

/// <summary>Идти в точку. Подтверждение — дошли ближе <see cref="Tolerance"/>.</summary>
public sealed class MoveAction(Position point, float tolerance = 2f) : GameAction
{
    public Position Point { get; } = point;
    public float Tolerance { get; } = tolerance;

    public override string Name => $"идти в {Point}";
    public override string Key => "движение";
    public override ActionResource Resource => ActionResource.Body;

    // Бег ~5 м/с, с запасом: 5 с + 0.4 с на метр (считается от точки отправки в Check); здесь — только верхний предел
    // (автопуть на 1.4.6 водит и на сотни метров)
    public override TimeSpan Timeout => TimeSpan.FromMinutes(5);

    public override CallResult Send(IGameActions actions, WorldState now) => actions.MoveTo(now.Host, Point);

    public override Verdict Check(WorldState start, WorldState now)
    {
        var left = now.Host.Position.DistanceTo(Point);
        if (left <= Tolerance)
            return Verdict.Confirmed($"дошли, {left:0.0} м до точки");

        var limit = TimeSpan.FromSeconds(5 + 0.4 * start.Host.Position.DistanceTo(Point));
        return now.Time - start.Time > limit ? Verdict.Rejected($"не дошли за {limit.TotalSeconds:0} с, осталось {left:0.0} м") : Verdict.Pending;
    }
}

/// <summary>Призвать пета. Подтверждение — пет из этой клетки призван.</summary>
public sealed class SummonPetAction(int cage) : GameAction
{
    public int Cage { get; } = cage;
    public override string Name => $"призвать пета из клетки {Cage}";
    public override string Key => "пет";
    public override ActionResource Resource => ActionResource.Body;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(8);

    public override string? Precondition(WorldState now)
        => now.Pet?.InCage(Cage) switch
        {
            null => $"в клетке {Cage} нет пета",
            { IsAlive: false } => "пет мёртв — сначала воскресить",
            _ => null,
        };

    public override CallResult Send(IGameActions actions, WorldState now) => actions.SummonPet(Cage);

    public override Verdict Check(WorldState start, WorldState now)
        => now.Pet is { IsSummoned: true } pet && pet.ActiveCage == Cage ? Verdict.Confirmed() : Verdict.Pending;
}

/// <summary>Воскресить пета скиллом (пакетом, без цели; каст ~12 с). Подтверждение — пет в клетке жив.</summary>
public sealed class RevivePetAction(int cage, int skill) : GameAction
{
    public int Cage { get; } = cage;
    public override string Name => $"воскресить пета (клетка {Cage})";
    public override string Key => "пет";
    public override ActionResource Resource => ActionResource.Body;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public override string? Precondition(WorldState now)
        => now.Pet?.InCage(Cage) is not { IsAlive: false } ? "пет в клетке не мёртв"
            : now.Skill(skill) is not { IsReady: true } ? $"скилл {skill} не готов или не изучен"
            : null;

    public override CallResult Send(IGameActions actions, WorldState now) => actions.CastSkill(skill, 0);

    public override Verdict Check(WorldState start, WorldState now)
        => now.Pet?.InCage(Cage) is { IsAlive: true } ? Verdict.Confirmed() : Verdict.Pending;
}

/// <summary>Приказ пету атаковать. Подтверждение — у пета в списке мобов цель = этот моб.</summary>
public sealed class PetAttackAction(uint targetWid) : GameAction
{
    public uint TargetWid { get; } = targetWid;
    public override string Name => $"пет атакует 0x{TargetWid:X8}";
    public override string Key => "приказ пету";

    // В игре подтверждение пришло через 3.1 с: пет сначала разворачивается и бежит к цели
    public override TimeSpan Timeout => TimeSpan.FromSeconds(6);

    public override string? Precondition(WorldState now) => now.Pet is { IsSummoned: true } ? null : "пет не призван";

    public override CallResult Send(IGameActions actions, WorldState now) => actions.PetAttack(TargetWid);

    public override Verdict Check(WorldState start, WorldState now)
    {
        var pet = now.Npcs.FirstOrDefault(n => n.Wid == now.Pet?.ActiveWid);
        return pet?.TargetWid == TargetWid ? Verdict.Confirmed() : Verdict.Pending;
    }
}
