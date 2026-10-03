using BotCH.Core.Calls;
using BotCH.Core.World;

namespace BotCH.Core.Actions;

/// <summary>
/// Всё, что бот умеет делать в игре. Реализации: прямые вызовы функций клиента (<see cref="DirectCallActions"/>),
/// позже — клавиши. Методы только отправляют команду; сработала ли она, решает <see cref="ActionRunner"/> по снимкам.
/// </summary>
public interface IGameActions
{
    /// <summary>Как выполняются действия: «вызовы» / «клавиши».</summary>
    string Mode { get; }

    /// <summary>Умеет ли «идти в точку». Нет — мозг не ходит сам (подбор «как мышкой» всё равно подводит к предмету).</summary>
    bool CanMove { get; }

    CallResult SelectTarget(uint wid);
    CallResult Unselect();
    CallResult NormalAttack();

    /// <summary>Скилл пакетом, без подхода. target 0 — без цели.</summary>
    CallResult CastSkill(int skillId, uint targetWid);

    /// <summary>Скилл как нажатием кнопки: клиент сам подходит на дальность. target 0 — текущая цель.</summary>
    CallResult ApplySkill(HostState host, int skillId, uint targetWid);

    CallResult PetAttack(uint targetWid);

    /// <summary>Отменить текущее действие (каст, копание) — как Esc.</summary>
    CallResult CancelAction();

    /// <summary>Подобрать пакетом (только в радиусе ~10 м).</summary>
    CallResult Pickup(GroundItem item);

    /// <summary>Подобрать как кликом мыши: клиент подходит сам.</summary>
    CallResult PickupObject(HostState host, GroundItem item);

    CallResult UseItem(InventoryItem item);
    CallResult SummonPet(int cage);
    CallResult MoveTo(HostState host, Position point);

    // ── Режим «сбор ресурсов» (часть 8, не сделано) ──

    /// <summary>Собрать ресурс (трава, руда) как кликом мыши: PickupObject с gather — клиент подходит и копает сам.</summary>
    CallResult Gather(HostState host, GroundItem resource);

    /// <summary>Лететь в точку. Не исследовано (SetDestination тип 1 «3D»?).</summary>
    CallResult FlyTo(HostState host, Position point);
}

/// <summary>Действия прямыми вызовами функций клиента — работают и при неактивном окне игры.</summary>
public sealed class DirectCallActions(GameCaller caller) : IGameActions
{
    public string Mode => "вызовы";

    public bool CanMove => caller.CanMoveTo;

    public CallResult SelectTarget(uint wid) => caller.SelectTarget(wid);
    public CallResult Unselect() => caller.Unselect();
    public CallResult NormalAttack() => caller.NormalAttack();
    public CallResult CastSkill(int skillId, uint targetWid) => caller.CastSkill(skillId, targetWid);
    public CallResult ApplySkill(HostState host, int skillId, uint targetWid) => caller.ApplySkill(host.Address, skillId, targetWid);
    public CallResult PetAttack(uint targetWid) => caller.PetAttack(targetWid);
    public CallResult CancelAction() => caller.CancelAction();
    public CallResult Pickup(GroundItem item) => caller.Pickup(item.Id, item.Tid);
    public CallResult PickupObject(HostState host, GroundItem item) => caller.PickupObject(host.Address, item.Id);
    public CallResult UseItem(InventoryItem item) => caller.UseItem(item.Slot, item.Tid);
    public CallResult SummonPet(int cage) => caller.SummonPet(cage);
    public CallResult MoveTo(HostState host, Position point) => caller.MoveTo(host.Address, point.X, point.Height, point.Y);

    public CallResult Gather(HostState host, GroundItem resource) => caller.PickupObject(host.Address, resource.Id, gather: true);

    public CallResult FlyTo(HostState host, Position point)
        => CallResult.Refused("полёт ещё не исследован (часть 8)");
}
