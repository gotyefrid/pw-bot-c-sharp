using BotCH.Core.Calls;
using BotCH.Core.Profiles;
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

    /// <summary>Умеет ли бегать с автопутём (в обход препятствий). Нет — <see cref="MoveTo"/> бежит по прямой.</summary>
    bool CanMoveSmart { get; }

    /// <summary>Умеет ли прерывать каст и копание (<see cref="CancelAction"/>). Нет — пет ждёт, пока персонаж освободится.</summary>
    bool CanCancel { get; }

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

    /// <summary>Бежать в точку: по прямой или <paramref name="smart"/> — с автопутём (если умеет, иначе по прямой).</summary>
    CallResult MoveTo(HostState host, Position point, bool smart);

    /// <summary>Собрать ресурс (трава, руда) как кликом мыши: PickupObject с gather — клиент подходит и копает сам.</summary>
    CallResult Gather(HostState host, GroundItem resource);

    /// <summary>Кнопка «Полёт»: на земле — взлететь, в воздухе — сесть.</summary>
    CallResult ToggleFly(HostState host);

    /// <summary>Лететь в точку вместе с её высотой (только в воздухе).</summary>
    CallResult FlyTo(HostState host, Position point);
}

/// <summary>Действия прямыми вызовами функций клиента — работают и при неактивном окне игры.</summary>
public sealed class DirectCallActions(GameCaller caller) : IGameActions
{
    public string Mode => "вызовы";

    public bool CanMove => caller.CanMoveTo;

    public bool CanMoveSmart => caller.CanMoveSmart;

    public bool CanCancel => caller.Can(GameFunctions.CancelAction);

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
    public CallResult MoveTo(HostState host, Position point, bool smart)
        => caller.MoveTo(host.Address, point.X, point.Height, point.Y, smart);

    public CallResult Gather(HostState host, GroundItem resource) => caller.PickupObject(host.Address, resource.Id, gather: true);

    public CallResult ToggleFly(HostState host) => caller.ToggleFly(host.Address);

    public CallResult FlyTo(HostState host, Position point) => caller.FlyTo(host.Address, point.X, point.Height, point.Y);
}
