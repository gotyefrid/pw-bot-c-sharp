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
/// Слот действия: в игре одновременно ждёт подтверждения только одно действие слота (<see cref="ActionKey"/>) — одна смена
/// цели, одно действие с петом, один скилл с этим ID. Слот объявляет сам класс действия.
/// </summary>
public enum ActionSlot
{
    /// <summary>Выбрать или снять цель.</summary>
    Target,
    /// <summary>Обычная атака.</summary>
    Attack,
    /// <summary>Скилл; в ключе — его ID.</summary>
    Skill,
    /// <summary>Банка или корм; в ключе — ячейка сумки.</summary>
    Item,
    /// <summary>Подбор с земли; в ключе — ID предмета.</summary>
    Pickup,
    /// <summary>Сбор ресурса; в ключе — ID ресурса.</summary>
    Gather,
    /// <summary>Бег или полёт в точку.</summary>
    Movement,
    /// <summary>Взлёт или посадка.</summary>
    Flight,
    /// <summary>Призвать, отозвать или воскресить пета.</summary>
    Pet,
    /// <summary>Приказ пету.</summary>
    PetOrder,
    /// <summary>Прервать каст или копание.</summary>
    Cancel,
}

/// <summary>«То же самое действие»: слот и, где нужно, что в нём (ID скилла, ячейка, предмет); у остальных 0.</summary>
public readonly record struct ActionKey(ActionSlot Slot, uint Id = 0)
{
    public override string ToString() => Id == 0 ? Slot.ToString() : $"{Slot} {Id}";
}

/// <summary>Насколько действие важно, когда тело занято: более важное прерывает менее важное (решает <see cref="ActionRunner"/>).</summary>
public enum ActionPriority
{
    /// <summary>Фон: копание ресурсов — уступает всему.</summary>
    Background,
    /// <summary>Обычное: бой, бег, лут.</summary>
    Normal,
    /// <summary>Срочное: лечение и воскрешение пета — прерывает всё, даже чужой каст (как Esc).</summary>
    Urgent,
}

/// <summary>
/// Действие бота: как отправить (<see cref="Send"/>) и как по снимкам понять, что оно сработало (<see cref="Check"/>).
/// Пока не подтверждено (или не вышел срок), такое же действие (<see cref="Key"/>) повторно не отправляется.
/// Подтверждение — по условию над снимком; если проверять нечего (например, нажатие клавиши), <see cref="Check"/> сразу
/// отвечает <see cref="Verdict.Confirmed"/> — исполнитель закроет действие на ближайшем снимке (≤ 250 мс).
/// </summary>
public abstract class GameAction
{
    /// <summary>Для лога: «скилл 299 → Сидящий волк».</summary>
    public abstract string Name { get; }

    /// <summary>Слот: в игре одновременно ждёт только одно действие слота с тем же <see cref="SlotId"/>.</summary>
    public abstract ActionSlot Slot { get; }

    /// <summary>Что именно в слоте: ID скилла, ячейка, предмет на земле; у остальных 0.</summary>
    public virtual uint SlotId => 0;

    /// <summary>Одинаковый ключ — «то же самое действие».</summary>
    public ActionKey Key => new(Slot, SlotId);

    /// <summary>Кто отправил — ему придёт итог. Ставит исполнитель при отправке.</summary>
    public IActionOwner? Owner { get; internal set; }

    /// <summary>
    /// В игре уже идёт то же самое: в слоте ждёт <paramref name="pending"/> (пусть и чужое), и повторная отправка навредила
    /// бы — кнопка «Полёт» переключает, второе нажатие посреди взлёта его отменит. Тогда новое не вытесняет ждущее и не
    /// отправляется: «уже ждёт».
    /// </summary>
    public virtual bool SameInGame(GameAction pending) => false;

    /// <summary>Что занимает, пока ждёт подтверждения: тело — одновременно только одно такое действие.</summary>
    public virtual ActionResource Resource => ActionResource.None;

    /// <summary>Важность за тело. По умолчанию обычное; задаётся при создании (<c>{ Priority = ActionPriority.Urgent }</c>).</summary>
    public virtual ActionPriority Priority { get; init; } = ActionPriority.Normal;

    /// <summary>Какой скилл кастует это действие (0 — не скилл): пока кастуется он же, действие не сбивает само себя.</summary>
    public virtual int CastsSkill => 0;

    /// <summary>Сколько ждать подтверждения.</summary>
    public abstract TimeSpan Timeout { get; }

    /// <summary>Чем считать истечение срока: обычно «не дождались», у банок и корма — «отказ» (стопка не изменилась).</summary>
    public virtual ActionStatus TimeoutStatus => ActionStatus.Timeout;

    /// <summary>Можно ли отправлять при таком снимке; null — можно, иначе причина.</summary>
    public virtual string? Precondition(WorldState now) => null;

    /// <summary>Отправить в игру: <paramref name="control"/> — чем (пока вызовы функций игры).</summary>
    public abstract CallResult Send(GameControl control, WorldState now);

    /// <summary>Сравнивает снимок в момент отправки и текущий.</summary>
    public abstract Verdict Check(WorldState start, WorldState now);

    public override string ToString() => Name;
}

public sealed class SelectTargetAction(NpcInfo npc) : GameAction
{
    public NpcInfo Npc { get; } = npc;
    public override string Name => $"выбрать цель {Npc.Name} 0x{Npc.Wid:X8}";
    public override ActionSlot Slot => ActionSlot.Target;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(2);
    public override CallResult Send(GameControl control, WorldState now) => control.Calls.SelectTarget(Npc.Wid);

    public override Verdict Check(WorldState start, WorldState now)
        => now.Host.TargetWid == Npc.Wid ? Verdict.Confirmed() : Verdict.Pending;
}

public sealed class UnselectAction : GameAction
{
    public override string Name => "снять цель";
    public override ActionSlot Slot => ActionSlot.Target;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(2);
    public override CallResult Send(GameControl control, WorldState now) => control.Calls.Unselect();
    public override Verdict Check(WorldState start, WorldState now) => now.Host.TargetWid == 0 ? Verdict.Confirmed() : Verdict.Pending;
}

/// <summary>
/// Прервать каст или копание — как Esc (нужно срочно лечить пета). Тело не занимает: оно его освобождает.
/// Подтверждение — персонаж больше не кастует и не копает.
/// </summary>
public sealed class CancelAction(string what) : GameAction
{
    /// <summary>Что прерываем: «каст», «копание».</summary>
    public string What { get; } = what;
    public override string Name => $"прервать {What}";
    public override ActionSlot Slot => ActionSlot.Cancel;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(2);
    public override CallResult Send(GameControl control, WorldState now) => control.Calls.CancelAction();

    public override Verdict Check(WorldState start, WorldState now)
        => !now.Host.IsCasting && now.Host.Gather is not { Active: true } ? Verdict.Confirmed() : Verdict.Pending;
}

/// <summary>Обычная атака текущей цели. Подтверждение: моб переключился на перса, у него убыло HP или он умер.</summary>
public sealed class NormalAttackAction : GameAction
{
    public override string Name => "обычная атака";
    public override ActionSlot Slot => ActionSlot.Attack;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(6);
    public override ActionResource Resource => ActionResource.Body;

    public override string? Precondition(WorldState now) => now.Target is null ? "нет цели" : null;

    public override CallResult Send(GameControl control, WorldState now) => control.Calls.NormalAttack();

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
    private readonly TimeSpan? _timeout;

    /// <param name="skill">ID скилла.</param>
    /// <param name="targetWid">Цель; 0 — текущая цель перса (approach) или без цели (пакет).</param>
    /// <param name="approach">Как кнопкой: клиент сам подходит на дальность. Иначе — пакетом.</param>
    /// <param name="timeout">Сколько ждать подтверждения; по умолчанию 5 с (как кнопкой) или 8 с (пакетом).</param>
    public SkillAction(int skill, uint targetWid, bool approach, string? title = null, TimeSpan? timeout = null)
    {
        _timeout = timeout;
        Skill = skill;
        TargetWid = targetWid;
        _approach = approach;
        Title = title ?? $"скилл {skill}";
    }

    public int Skill { get; }
    public uint TargetWid { get; }
    public string Title { get; }

    public override string Name => TargetWid == 0 ? Title : $"{Title} → 0x{TargetWid:X8}";
    public override ActionSlot Slot => ActionSlot.Skill;
    public override uint SlotId => (uint)Skill;
    public override ActionResource Resource => ActionResource.Body;
    public override int CastsSkill => Skill;
    // Как кнопкой: не дождались за 5 с — бот просто нажмёт ещё раз (клиент продолжит подход), долго ждать незачем
    public override TimeSpan Timeout => _timeout ?? TimeSpan.FromSeconds(_approach ? 5 : 8);

    public override string? Precondition(WorldState now)
        => now.Skill(Skill) switch
        {
            null => $"скилл {Skill} не изучен",
            { IsReady: false } s => $"{Title}: перезарядка {s.CooldownLeftMs / 1000.0:0.0} с",
            _ => null,
        };

    public override CallResult Send(GameControl control, WorldState now)
        => _approach ? control.Calls.ApplySkill(now.Host, Skill, TargetWid) : control.Calls.CastSkill(Skill, TargetWid);

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
/// <param name="potion">Банка: от чего пьём (HP или MP) — выпитая действует какое-то время, такую же пока не пить.</param>
public sealed class UseItemAction(InventoryItem item, ItemUse use, PotionKind? potion = null) : GameAction
{
    public InventoryItem Item { get; } = item;
    public ItemUse Use { get; } = use;

    /// <summary>Банка: HP или MP; null — не банка (или неважно).</summary>
    public PotionKind? Potion { get; } = potion;

    public override string Name => $"{(Use == ItemUse.Potion ? "банка" : "корм")} tid {Item.Tid} (ячейка {Item.Slot}, ×{Item.Count})";
    public override ActionSlot Slot => ActionSlot.Item;
    public override uint SlotId => (uint)Item.Slot;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(3);
    public override ActionStatus TimeoutStatus => ActionStatus.Rejected;

    public override string? Precondition(WorldState now)
        => now.Inventory.Any(i => i.Slot == Item.Slot && i.Tid == Item.Tid) ? null : "предмета уже нет в сумке";

    public override CallResult Send(GameControl control, WorldState now) => control.Calls.UseItem(Item);

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
    public override ActionSlot Slot => ActionSlot.Pickup;
    public override uint SlotId => Item.Id;
    // Пакетом персонаж не двигается; «как мышкой» — бежит к предмету
    public override ActionResource Resource => approach ? ActionResource.Body : ActionResource.None;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(approach ? 10 : 3);

    public override string? Precondition(WorldState now)
        => Item.Kind == GroundItemKind.Resource ? "ресурс не подбирается, а собирается"
            : !approach && Item.Distance > 10 ? "дальше 10 м — сервер не поднимет без подхода"
            : null;

    public override CallResult Send(GameControl control, WorldState now)
        => approach ? control.Calls.PickupObject(now.Host, Item) : control.Calls.Pickup(Item);

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

/// <summary>
/// Собрать ресурс «как мышкой»: клиент подводит персонажа, копает (полоска) и кладёт добычу в сумку.
/// Подтверждение — в сумке прибавилось или ресурс пропал с земли. Нужен инструмент (для руды — кирка): без него откажет игра.
/// Если сервер показывает полоску копания (<see cref="HostState.Gather"/>): полоска кончилась раньше времени — сбили;
/// стоим на месте, а копать не начали — игра не даёт (нет инструмента).
/// </summary>
public sealed class GatherAction(GroundItem resource) : GameAction
{
    public GroundItem Item { get; } = resource;

    public override string Name => $"собрать {Item.Name} ({Item.Distance:0.0} м)";
    public override ActionSlot Slot => ActionSlot.Gather;
    public override uint SlotId => Item.Id;
    public override ActionResource Resource => ActionResource.Body;
    public override ActionPriority Priority { get; init; } = ActionPriority.Background;
    // Подойти + копать несколько секунд
    public override TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public override string? Precondition(WorldState now)
        => Item.Kind != GroundItemKind.Resource ? "это не ресурс — его подбирают" : null;

    public override CallResult Send(GameControl control, WorldState now) => control.Calls.Gather(now.Host, Item);

    private static readonly TimeSpan StandPatience = TimeSpan.FromSeconds(4);
    private bool _started;

    /// <summary>Копание началось и оборвалось раньше конца полоски (ударили, сдвинули).</summary>
    public bool KnockedDown { get; private set; }
    private Position? _lastPosition;
    private DateTime? _standingSince;

    public override Verdict Check(WorldState start, WorldState now)
    {
        var before = start.Inventory.Sum(i => i.Count);
        var after = now.Inventory.Sum(i => i.Count);
        if (after > before)
            return Verdict.Confirmed($"в сумке +{after - before}");
        if (!now.GroundItems.Any(i => i.Id == Item.Id))
            return Verdict.Confirmed("ресурс пропал с земли");
        if (now.Host.Gather is not { } g)
            return Verdict.Pending;

        if (g.Active)
        {
            _started = true;
            return Verdict.Pending;
        }

        // Полоска кончилась: дошла до конца — ждём добычу, нет — сбили
        if (_started)
        {
            if (g.Finished)
                return Verdict.Pending;
            KnockedDown = true;
            return Verdict.Rejected($"копание сбили: {g.ElapsedMs / 1000.0:0.0} из {g.TotalMs / 1000.0:0} с");
        }

        // Ещё не начали: бежим — ждём; стоим — игра не даёт копать
        if (_lastPosition is not { } last || now.Host.Position.HorizontalDistanceTo(last) > 0.1f)
        {
            _lastPosition = now.Host.Position;
            _standingSince = now.Time;
            return Verdict.Pending;
        }

        return now.Time - _standingSince!.Value >= StandPatience
            ? Verdict.Rejected($"стоим {StandPatience.TotalSeconds:0} с, а копать не начали — нет инструмента?")
            : Verdict.Pending;
    }
}

/// <summary>Идти в точку: по прямой или <paramref name="smart"/> — с автопутём. Подтверждение — дошли ближе <see cref="Tolerance"/>.</summary>
public sealed class MoveAction(Position point, float tolerance = 2f, bool smart = false, bool fly = false) : GameAction
{
    public Position Point { get; } = point;
    public float Tolerance { get; } = tolerance;
    public bool Smart { get; } = smart;

    /// <summary>Лететь в точку вместе с её высотой (только в воздухе).</summary>
    public bool Fly { get; } = fly;

    public override string Name => Fly ? $"лететь в {Point}" : $"идти в {Point}";
    public override ActionSlot Slot => ActionSlot.Movement;
    public override ActionResource Resource => ActionResource.Body;

    // Бег ~5 м/с, с запасом: 5 с + 0.4 с на метр (считается от точки отправки в Check); здесь — только верхний предел
    // (автопуть на 1.4.6 водит и на сотни метров)
    public override TimeSpan Timeout => TimeSpan.FromMinutes(5);

    public override string? Precondition(WorldState now)
        => Fly && now.Host.Flying != true ? "лететь в точку можно только в воздухе" : null;

    public override CallResult Send(GameControl control, WorldState now)
        => Fly ? control.Calls.FlyTo(now.Host, Point) : control.Calls.MoveTo(now.Host, Point, Smart);

    // Перс встал, не дойдя (упёрся, бег сбился) — не ждём конца времени на дорогу
    private static readonly TimeSpan StandPatience = TimeSpan.FromSeconds(2.5);
    private Position? _lastPosition;
    private DateTime _movedAt;

    public override Verdict Check(WorldState start, WorldState now)
    {
        var left = now.Host.Position.DistanceTo(Point);
        if (left <= Tolerance)
            return Verdict.Confirmed($"дошли, {left:0.0} м до точки");
        if (Fly && now.Host.Flying == false)
            return Verdict.Rejected($"оказались на земле, не долетев {left:0.0} м");

        if (_lastPosition is null)
            (_lastPosition, _movedAt) = (start.Host.Position, start.Time);
        // С высотой: в полёте можно подниматься на месте
        if (now.Host.Position.DistanceTo(_lastPosition.Value) > 0.1f)
            (_lastPosition, _movedAt) = (now.Host.Position, now.Time);
        else if (now.Time - _movedAt >= StandPatience)
            return Verdict.Rejected($"стоим {StandPatience.TotalSeconds:0.0} с, не дойдя {left:0.0} м");

        var limit = TimeSpan.FromSeconds(5 + 0.4 * start.Host.Position.DistanceTo(Point));
        return now.Time - start.Time > limit ? Verdict.Rejected($"не дошли за {limit.TotalSeconds:0} с, осталось {left:0.0} м") : Verdict.Pending;
    }
}

/// <summary>
/// Взлететь или сесть кнопкой «Полёт» (она переключает). Отправляем, только если персонаж не там, где нужно — иначе
/// нажатие сделало бы обратное. Подтверждение — персонаж в воздухе (или на земле).
/// </summary>
public sealed class FlyAction(bool up) : GameAction
{
    public bool Up { get; } = up;
    public override string Name => Up ? "взлететь" : "сесть";
    public override ActionSlot Slot => ActionSlot.Flight;
    public override bool SameInGame(GameAction pending) => pending is FlyAction fly && fly.Up == Up;
    public override ActionResource Resource => ActionResource.Body;

    // Взлёт ~1 с; посадка — спуск до земли, с высоты дольше
    public override TimeSpan Timeout => TimeSpan.FromSeconds(Up ? 5 : 30);

    public override string? Precondition(WorldState now)
        => now.Host.Flying switch
        {
            null => "не знаем, летит ли персонаж (поле не найдено для этого сервера)",
            true when Up => "уже в воздухе",
            false when !Up => "уже на земле",
            _ => null,
        };

    public override CallResult Send(GameControl control, WorldState now) => control.Calls.ToggleFly(now.Host);

    public override Verdict Check(WorldState start, WorldState now)
        => now.Host.Flying == Up ? Verdict.Confirmed() : Verdict.Pending;
}

/// <summary>Призвать пета. Подтверждение — пет из этой клетки призван.</summary>
public sealed class SummonPetAction(int cage) : GameAction
{
    public int Cage { get; } = cage;
    public override string Name => $"призвать пета из клетки {Cage}";
    public override ActionSlot Slot => ActionSlot.Pet;
    public override ActionResource Resource => ActionResource.Body;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(8);

    public override string? Precondition(WorldState now)
        => now.Pet?.InCage(Cage) switch
        {
            null => $"в клетке {Cage} нет пета",
            { IsAlive: false } => "пет мёртв — сначала воскресить",
            _ => null,
        };

    public override CallResult Send(GameControl control, WorldState now) => control.Calls.SummonPet(Cage);

    public override Verdict Check(WorldState start, WorldState now)
        => now.Pet is { IsSummoned: true } pet && pet.ActiveCage == Cage ? Verdict.Confirmed() : Verdict.Pending;
}

/// <summary>Отозвать пета в клетку. Подтверждение — пет больше не призван.</summary>
public sealed class RecallPetAction : GameAction
{
    public override string Name => "отозвать пета";
    public override ActionSlot Slot => ActionSlot.Pet;
    public override bool SameInGame(GameAction pending) => pending is RecallPetAction;
    public override ActionResource Resource => ActionResource.Body;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(5);

    public override string? Precondition(WorldState now) => now.Pet is { IsSummoned: true } ? null : "пет не призван";

    public override CallResult Send(GameControl control, WorldState now) => control.Calls.RecallPet();

    public override Verdict Check(WorldState start, WorldState now)
        => now.Pet is not { IsSummoned: true } ? Verdict.Confirmed() : Verdict.Pending;
}

/// <summary>Воскресить пета скиллом (пакетом, без цели; каст ~12 с). Подтверждение — пет в клетке жив.</summary>
public sealed class RevivePetAction(int cage, int skill) : GameAction
{
    public int Cage { get; } = cage;
    public override string Name => $"воскресить пета (клетка {Cage})";
    public override ActionSlot Slot => ActionSlot.Pet;
    public override ActionResource Resource => ActionResource.Body;
    public override ActionPriority Priority { get; init; } = ActionPriority.Urgent;
    public override int CastsSkill => skill;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public override string? Precondition(WorldState now)
        => now.Pet?.InCage(Cage) is not { IsAlive: false } ? "пет в клетке не мёртв"
            : now.Skill(skill) is not { IsReady: true } ? $"скилл {skill} не готов или не изучен"
            : null;

    public override CallResult Send(GameControl control, WorldState now) => control.Calls.CastSkill(skill, 0);

    public override Verdict Check(WorldState start, WorldState now)
        => now.Pet?.InCage(Cage) is { IsAlive: true } ? Verdict.Confirmed() : Verdict.Pending;
}

/// <summary>Приказ пету атаковать. Подтверждение — у пета в списке мобов цель = этот моб.</summary>
public sealed class PetAttackAction(uint targetWid) : GameAction
{
    public uint TargetWid { get; } = targetWid;
    public override string Name => $"пет атакует 0x{TargetWid:X8}";
    public override ActionSlot Slot => ActionSlot.PetOrder;

    // В игре подтверждение пришло через 3.1 с: пет сначала разворачивается и бежит к цели
    public override TimeSpan Timeout => TimeSpan.FromSeconds(6);

    public override string? Precondition(WorldState now) => now.Pet is { IsSummoned: true } ? null : "пет не призван";

    public override CallResult Send(GameControl control, WorldState now) => control.Calls.PetAttack(TargetWid);

    public override Verdict Check(WorldState start, WorldState now)
    {
        var pet = now.Npcs.FirstOrDefault(n => n.Wid == now.Pet?.ActiveWid);
        return pet?.TargetWid == TargetWid ? Verdict.Confirmed() : Verdict.Pending;
    }
}
